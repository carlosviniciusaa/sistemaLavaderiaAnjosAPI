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
`20260824-brand-icons` altera a identidade visual e o ícone do executável, sem alteração
de banco. O pacote não contém SQL de migration e o manifesto define
`requiredMigration = null`. Não execute migration para aplicar esta atualização; preserve
integralmente o banco, o histórico de migrations e os backups existentes.

No servidor, abra PowerShell como administrador e execute exatamente:

Antes da troca do serviço, valide os arquivos de identidade e o wiring incluídos no pacote:

```powershell
cd <pacote>\operations\tests
.\Test-PackagedBranding.ps1 -PackagePath <pacote>
.\Test-PackagedCopyWiring.ps1 -PackagePath <pacote>
```

Depois execute a atualização:

```powershell
Set-ExecutionPolicy -Scope Process RemoteSigned
cd <pacote>\operations\scripts
.\Update-Service.ps1 `
    -PackagePath <pacote> `
    -ReleaseId 20260824-brand-icons `
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
