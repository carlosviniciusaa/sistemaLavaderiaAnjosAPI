# Atualização e rollback

## Princípios

- cada versão fica em `releases\<release-id>`;
- `current` é uma junção para a versão ativa;
- releases nunca são sobrescritas;
- o serviço é parado antes da troca;
- falha no health check provoca rollback automático;
- releases antigas não são removidas automaticamente, preservando no mínimo duas versões anteriores;
- `Install-Service.ps1`, `Update-Service.ps1` e `Rollback-Service.ps1` não modificam banco, migrations nem backups;
- somente `Invoke-DatabaseMigration.ps1`, quando chamado explicitamente, executa o SQL informado.

## Atualizar

Gere e copie um novo pacote conforme `implantacao-producao.md`. A release
`20260722-copy-modal-fix` altera somente arquivos estáticos do frontend e não possui
alteração de banco. Não execute migration para aplicar esta correção. Confirme apenas
que a migration preexistente `20260721194510_GerenciamentoAdministradores` continua
registrada em `dbo.__EFMigrationsHistory`; preserve o banco e os backups existentes.

O pacote mantém o script idempotente da migration de administradores para preservar a
capacidade de instalar o sistema completo em um ambiente novo. Ele não deve ser executado
durante esta atualização de clipboard. Em uma instalação nova, use autenticação integrada
autorizada no SQL Server e aplique-o pela mesma conexão em todos os lotes:

```powershell
Set-ExecutionPolicy -Scope Process RemoteSigned
cd <pacote>\operations\scripts
.\Invoke-DatabaseMigration.ps1 `
    -SqlFile ..\sql\LavanderiaAnjos-admin-management-idempotent.sql `
    -ServerInstance '.\SQLEXPRESS' `
    -Database LavanderiaAnjos
```

Se a administração do banco usa login SQL, obtenha a credencial sem gravá-la em arquivo ou
linha de comando e acrescente `-SqlCredential $sqlCredential`:

```powershell
$sqlCredential = Get-Credential -Message 'Credencial administrativa do SQL Server'
```

O executor usa `System.Data.SqlClient`, não depende de SSMS nem de `sqlcmd`, lê UTF-8 e divide
os lotes exatamente por:

```powershell
[regex]::Split($sql, '(?im)^\s*GO\s*(?:--.*)?$')
```

O script mantém `BEGIN TRANSACTION`, `XACT_ABORT`, `TRY/CATCH` e rollback explícito no mesmo
lote. Ele serializa aplicações concorrentes, preserva usuários, hashes, clientes, histórico de
importação e backups. As colunas e instruções dependentes são compiladas por `sp_executesql`.
Uma colisão após `LOWER(LTRIM(RTRIM(Login)))` aborta antes de qualquer alteração de schema.

Em uma instalação nova, execute o mesmo comando uma segunda vez; ele deve concluir sem mudanças. Confirme que a linha
`20260721194510_GerenciamentoAdministradores` existe uma única vez em
`dbo.__EFMigrationsHistory`. Somente então troque os binários.

No servidor, abra PowerShell como administrador e execute exatamente:

Antes da troca do serviço, valide os arquivos estáticos e o wiring incluído no pacote:

```powershell
cd <pacote>\operations\tests
.\Test-PackagedCopyWiring.ps1 -PackagePath <pacote>
```

Depois execute a atualização:

```powershell
Set-ExecutionPolicy -Scope Process RemoteSigned
cd <pacote>\operations\scripts
.\Update-Service.ps1 `
    -PackagePath <pacote> `
    -ReleaseId 20260722-copy-modal-fix `
    -Root D:\LavanderiaAnjos\Sistema `
    -ServiceName LavanderiaAnjos
```

O script valida o pacote, cria uma release, preserva o alvo atual, para o serviço, troca `current`, reinicia e aguarda `/health`. Se a nova versão falhar, restaura automaticamente a anterior e confirma sua saúde.

Depois valide `/health`, o login do administrador existente, a quantidade esperada de clientes e a tela
Administradores. Faça mutações somente em um administrador sintético aprovado;
não edite nem inative clientes reais. Reinicie o serviço e repita `/health` e login.

## Rollback orientado

```powershell
# Release anterior mais recente
D:\LavanderiaAnjos\Sistema\scripts\Rollback-Service.ps1

# Release específica
D:\LavanderiaAnjos\Sistema\scripts\Rollback-Service.ps1 -TargetReleaseId <release-id>
```

O rollback troca somente binários. Se uma versão exigiu migration, revise manualmente a compatibilidade; os scripts nunca revertem schema ou dados.

## Diagnóstico

```powershell
Get-ChildItem D:\LavanderiaAnjos\Sistema\releases -Directory |
    Sort-Object LastWriteTime -Descending
Get-Item D:\LavanderiaAnjos\Sistema\current -Force |
    Select-Object FullName, Target
Get-Content D:\LavanderiaAnjos\Sistema\logs\lavanderia-$(Get-Date -Format yyyyMMdd).log -Tail 200
```

Não apague a release atual, o alvo de rollback ou as duas versões anteriores. Limpeza é uma ação administrativa separada, após backup e período de observação.
