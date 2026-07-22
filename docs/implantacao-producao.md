# Implantação de produção no SISTEMALAV

## Escopo e arquitetura

A aplicação é publicada para Windows `win-x64`, em `Release`, autocontida, sem single-file e sem trimming. O executável ASP.NET Core serve o frontend e as Minimal APIs e acessa o SQL Server local pela configuração externa `ConnectionStrings__LavanderiaAnjos`.

Endereços planejados:

- `http://SISTEMALAV:5080`
- `http://<IP confirmado do SISTEMALAV>:5080`

Antes da primeira instalação, confirme o IP com `Resolve-DnsName SISTEMALAV`. Na auditoria de 21/07/2026, o nome resolveu para `192.168.68.121`, divergindo do endereço documentado `192.168.68.119`. Não instale nem abra firewall usando um IP ambíguo.

Estrutura adotada:

```text
D:\LavanderiaAnjos\Sistema\current       junção para a release ativa
D:\LavanderiaAnjos\Sistema\releases      releases imutáveis
D:\LavanderiaAnjos\Sistema\logs          logs diários, retenção de 30 dias
D:\LavanderiaAnjos\Sistema\scripts       scripts operacionais
D:\LavanderiaAnjos\Sistema\docs          documentação operacional
D:\LavanderiaAnjos\Sistema\temporary     JSON/CSV temporários fora da aplicação
```

Executáveis não são colocados em `D:\LavanderiaAnjos\Backups`.

## Gerar o pacote

Na máquina de desenvolvimento:

```powershell
dotnet restore .\LavanderiaAnjos.sln
dotnet build .\LavanderiaAnjos.sln -c Release --no-restore
dotnet test .\LavanderiaAnjos.sln -c Release --no-build
node --test .\tests\javascript\*.test.mjs
.\tests\sql\Test-AdminManagementMigration.ps1
.\scripts\production\Publish-Production.ps1 -ReleaseId 20260722-admin-management-fix1
```

O processo cria, sem sobrescrever artefatos anteriores, a pasta, o ZIP e o arquivo
`.zip.sha256` em `publish\production`. O pacote contém a aplicação autocontida, scripts
operacionais, documentação e manifesto público. Não contém User Secrets, connection string,
planilhas, relatórios reais, JSON de lotes, backups, testes ou código-fonte.

Releases que exigem alteração aditiva de schema também incluem o SQL idempotente
revisado em `operations\sql`. O script deve ser aplicado pelo procedimento de
`atualizacao-e-rollback.md`, usando `Invoke-DatabaseMigration.ps1`; `Update-Service.ps1`
não altera o banco. O executor mantém uma única conexão, separa `GO` por linha e não requer SSMS.

## Preparar a connection string no servidor

Abra PowerShell como administrador no `SISTEMALAV`. Obtenha o valor pelo canal seguro já usado pela organização e grave-o sem imprimi-lo:

```powershell
$secureValue = Read-Host 'ConnectionStrings__LavanderiaAnjos' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue)
try {
    $plainValue = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    [Environment]::SetEnvironmentVariable(
        'ConnectionStrings__LavanderiaAnjos',
        $plainValue,
        [EnvironmentVariableTarget]::Machine)
} finally {
    $plainValue = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
}
```

Não cole o valor em scripts, README, histórico compartilhado ou arquivos `appsettings`.

## Primeira instalação

Copie a pasta completa do pacote para o servidor por um canal administrativo aprovado. No servidor:

```powershell
Set-ExecutionPolicy -Scope Process RemoteSigned
cd <pacote>\operations\scripts
.\Install-Service.ps1 -PackagePath <pacote>
```

O script valida o pacote, cria uma release e a junção `current`, concede permissões mínimas ao `LOCAL SERVICE`, instala `LavanderiaAnjos` com início automático, configura três reinícios progressivos após falha e cria somente a regra privada TCP 5080 para `192.168.68.0/24`. Se uma regra homônima tiver configuração diferente, a instalação é interrompida. SQL Server e seu firewall não são alterados.

## Validação

```powershell
$admin = Get-Credential -Message 'Administrador da Lavanderia Anjos'
.\Test-Production.ps1 -AdminCredential $admin -ServerIp <IP-confirmado>
.\Stop-Service.ps1
.\Start-Service.ps1
.\Test-Production.ps1 -AdminCredential $admin -ServerIp <IP-confirmado>
```

O teste exibe apenas status e quantidade total; não imprime credenciais ou dados de clientes.

## Migrations e banco

A aplicação não executa `EnsureCreated` nem aplica migrations ao iniciar. Compare `Persistence\Migrations` com `dbo.__EFMigrationsHistory`. Se não houver migration pendente, não altere o banco. Se houver, gere e revise um script idempotente antes de qualquer aplicação.

## Backup pós-implantação

Somente depois de serviço, login e quantidade de clientes estarem validados:

1. acione a rotina de backup já existente, sem alterá-la;
2. gere um novo `.bak` em `D:\LavanderiaAnjos\Backups` usando `WITH CHECKSUM`;
3. execute `RESTORE VERIFYONLY` no novo arquivo;
4. registre nome, data, tamanho e sucesso, sem apagar backups anteriores.

## Restrição de rede

HTTP não fornece TLS: login e cookie trafegam sem criptografia de transporte. Mantenha a aplicação apenas na LAN confiável, perfil `Private`, origem `192.168.68.0/24`, sem encaminhamento no roteador e sem exposição à internet. Planeje HTTPS interno confiável em etapa posterior.
