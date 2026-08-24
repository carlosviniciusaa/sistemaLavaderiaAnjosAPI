[CmdletBinding()]
param(
    [string] $ReleaseId = '20260824-brand-icons',
    [string] $ServerInstance = '(localdb)\MSSQLLocalDB',
    [int] $Port = 5098,
    [ValidateSet('127.0.0.1', '0.0.0.0')][string] $BindAddress = '127.0.0.1',
    [ValidateRange(0, 600)][int] $HoldSeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$packagePath = Join-Path $repositoryRoot "publish\production\$ReleaseId"
$executablePath = Join-Path $packagePath 'app\LavanderiaAnjos.Web.exe'
$executorPath = Join-Path $repositoryRoot 'scripts\production\Invoke-DatabaseMigration.ps1'
$database = 'LavanderiaAnjos_AppValidation_' + [Guid]::NewGuid().ToString('N')
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) $database
$process = $null

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Executável publicado não encontrado: $executablePath"
}
if ($database -notmatch '^LavanderiaAnjos_AppValidation_[0-9a-f]{32}$') {
    throw "Nome de banco sintético inesperado: $database"
}

function New-ConnectionString([string] $Database) {
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = $ServerInstance
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Encrypt'] = $false
    $builder['Application Name'] = 'LavanderiaAnjos packaged app validation'
    return $builder.ConnectionString
}

function Invoke-NonQuery([string] $Database, [string] $Sql) {
    $connection = [System.Data.SqlClient.SqlConnection]::new((New-ConnectionString $Database))
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = $Sql
            $command.CommandTimeout = 120
            [void]$command.ExecuteNonQuery()
        } finally {
            $command.Dispose()
        }
    } finally {
        $connection.Dispose()
    }
}

function Wait-Healthy([string] $Uri) {
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200 -and $response.Content -match '"status"\s*:\s*"Healthy"') {
                return $true
            }
        } catch { }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    Invoke-NonQuery 'master' "CREATE DATABASE [$database];"

    & $executorPath -SqlFile (Join-Path $repositoryRoot 'scripts\sql\LavanderiaAnjos-idempotent.sql') -ServerInstance $ServerInstance -Database $database | Out-Null
    & $executorPath -SqlFile (Join-Path $repositoryRoot 'scripts\sql\LavanderiaAnjos-importacao-clientes-idempotente.sql') -ServerInstance $ServerInstance -Database $database | Out-Null
    & $executorPath -SqlFile (Join-Path $repositoryRoot 'scripts\sql\LavanderiaAnjos-admin-management-idempotent.sql') -ServerInstance $ServerInstance -Database $database | Out-Null

    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:ASPNETCORE_URLS = "http://${BindAddress}:$Port"
    $env:ConnectionStrings__LavanderiaAnjos = New-ConnectionString $database
    $env:BootstrapAdmin__Nome = 'Administrador de Validação'
    $env:BootstrapAdmin__Login = 'admin.validacao'
    $env:BootstrapAdmin__Password = 'Senha-Sintetica-123!'
    $env:Logging__File__Path = Join-Path $temporaryRoot 'logs'

    $process = Start-Process -FilePath $executablePath `
        -WorkingDirectory (Split-Path -Parent $executablePath) `
        -RedirectStandardOutput (Join-Path $temporaryRoot 'stdout.log') `
        -RedirectStandardError (Join-Path $temporaryRoot 'stderr.log') `
        -WindowStyle Hidden `
        -PassThru

    $baseUri = "http://127.0.0.1:$Port"
    if (-not (Wait-Healthy "$baseUri/health")) {
        $stderr = Get-Content -LiteralPath (Join-Path $temporaryRoot 'stderr.log') -Raw -ErrorAction SilentlyContinue
        throw "O executável publicado não ficou saudável. $stderr"
    }

    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $loginBody = @{ login = 'admin.validacao'; password = 'Senha-Sintetica-123!' } | ConvertTo-Json
    $login = Invoke-WebRequest -Uri "$baseUri/api/auth/login" -Method Post -ContentType 'application/json' -Body $loginBody -WebSession $session -UseBasicParsing
    $administratorsPage = Invoke-WebRequest -Uri "$baseUri/administradores" -WebSession $session -UseBasicParsing
    $administratorsApi = Invoke-WebRequest -Uri "$baseUri/api/administradores" -WebSession $session -UseBasicParsing
    $administrators = @($administratorsApi.Content | ConvertFrom-Json)

    if ($login.StatusCode -ne 200) { throw "Login retornou HTTP $($login.StatusCode)." }
    if ($administratorsPage.StatusCode -ne 200 -or $administratorsPage.Content -notmatch 'Administradores') {
        throw 'A tela de administradores não foi servida corretamente.'
    }
    if ($administratorsApi.StatusCode -ne 200 -or $administrators.Count -ne 1 -or $administrators[0].login -ne 'admin.validacao') {
        throw 'A API de administradores não retornou o administrador autenticado esperado.'
    }

    if ($HoldSeconds -gt 0) {
        Write-Host "Validação interativa disponível por $HoldSeconds segundos em http://<ip-local>:$Port."
        Start-Sleep -Seconds $HoldSeconds
    }

    [pscustomobject]@{
        Environment = 'Production'
        Executable = $executablePath
        Health = 'PASS'
        Authentication = 'PASS'
        AdministratorsPage = 'PASS'
        AdministratorsApi = 'PASS'
        SyntheticDatabase = $database
    }
} finally {
    foreach ($name in @(
        'ASPNETCORE_ENVIRONMENT', 'ASPNETCORE_URLS', 'ConnectionStrings__LavanderiaAnjos',
        'BootstrapAdmin__Nome', 'BootstrapAdmin__Login', 'BootstrapAdmin__Password', 'Logging__File__Path')) {
        Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
    }

    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }

    if ($database -match '^LavanderiaAnjos_AppValidation_[0-9a-f]{32}$') {
        try {
            Invoke-NonQuery 'master' "IF DB_ID(N'$database') IS NOT NULL BEGIN ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$database]; END;"
        } catch {
            Write-Warning "Não foi possível remover o banco sintético ${database}: $($_.Exception.Message)"
        }
    }

    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
