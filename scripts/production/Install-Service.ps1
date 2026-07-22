[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [string] $ReleaseId,
    [string] $Root = 'D:\LavanderiaAnjos\Sistema',
    [string] $ServiceName = 'LavanderiaAnjos'
)

. (Join-Path $PSScriptRoot 'Deployment-Common.ps1')
Assert-Administrator
$paths = Get-DeploymentPaths -Root $Root
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "O serviço '$ServiceName' já existe. Use Update-Service.ps1."
}
if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $manifestPath = Join-Path ([IO.Path]::GetFullPath($PackagePath)) 'release-manifest.json'
    $ReleaseId = if (Test-Path -LiteralPath $manifestPath) {
        [string](Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).releaseId
    } else {
        [IO.Path]::GetFileName([IO.Path]::GetFullPath($PackagePath).TrimEnd('\'))
    }
}

foreach ($directory in @(
    $paths.Root,
    $paths.Releases,
    $paths.Logs,
    $paths.Scripts,
    $paths.Docs,
    $paths.Temporary)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$connectionString = [Environment]::GetEnvironmentVariable(
    'ConnectionStrings__LavanderiaAnjos',
    [EnvironmentVariableTarget]::Machine)
if ([string]::IsNullOrWhiteSpace($connectionString)) {
    throw 'Configure ConnectionStrings__LavanderiaAnjos como variável de ambiente da máquina antes da instalação.'
}
if ($connectionString -match '[\r\n]') {
    throw 'A variável da connection string contém caracteres inválidos.'
}

$releasePath = Copy-ReleasePackage -PackagePath $PackagePath -Paths $paths -ReleaseId $ReleaseId
Set-CurrentRelease -Paths $paths -ReleasePath $releasePath

foreach ($entry in @(
    @{ Path = $paths.Root; Permission = '(OI)(CI)(RX)' },
    @{ Path = $paths.Releases; Permission = '(OI)(CI)(RX)' },
    @{ Path = $paths.Logs; Permission = '(OI)(CI)(M)' },
    @{ Path = $paths.Temporary; Permission = '(OI)(CI)(M)' })) {
    & icacls.exe $entry.Path /grant "*S-1-5-19:$($entry.Permission)" /T /C | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível configurar as permissões mínimas em '$($entry.Path)'."
    }
}

$executable = Join-Path $paths.Current 'LavanderiaAnjos.Web.exe'
$binaryPath = "`"$executable`" --contentRoot `"$($paths.Current)`""
& sc.exe create $ServiceName binPath= $binaryPath start= auto obj= 'NT AUTHORITY\LocalService' DisplayName= 'Sistema Lavanderia Anjos' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Não foi possível criar o serviço '$ServiceName'."
}
& sc.exe description $ServiceName 'Sistema interno de cadastro de clientes da Lavanderia Anjos' | Out-Null
& sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
& sc.exe failureflag $ServiceName 1 | Out-Null

$serviceEnvironment = @(
    'ASPNETCORE_ENVIRONMENT=Production',
    'ASPNETCORE_URLS=http://0.0.0.0:5080',
    "Logging__File__Path=$($paths.Logs)",
    "Importacao__TemporaryPath=$(Join-Path $paths.Temporary 'importacoes')",
    "ConnectionStrings__LavanderiaAnjos=$connectionString"
)
$serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$environmentProperty = @{
    Path = $serviceRegistryPath
    Name = 'Environment'
    PropertyType = 'MultiString'
    Value = $serviceEnvironment
    Force = $true
}
New-ItemProperty @environmentProperty | Out-Null

$firewallName = 'Lavanderia Anjos Web TCP 5080'
$existingFirewallRule = Get-NetFirewallRule -DisplayName $firewallName -ErrorAction SilentlyContinue
if ($existingFirewallRule) {
    $portFilter = $existingFirewallRule | Get-NetFirewallPortFilter
    $addressFilter = $existingFirewallRule | Get-NetFirewallAddressFilter
    $validRule = $existingFirewallRule.Enabled -eq 'True' -and
        $existingFirewallRule.Direction -eq 'Inbound' -and
        $existingFirewallRule.Action -eq 'Allow' -and
        $existingFirewallRule.Profile -match 'Private' -and
        $portFilter.Protocol -eq 'TCP' -and
        $portFilter.LocalPort -eq '5080' -and
        $addressFilter.RemoteAddress -contains '192.168.68.0/24'
    if (-not $validRule) {
        throw "A regra '$firewallName' já existe com configuração diferente; revise-a manualmente."
    }
} else {
    $firewallParameters = @{
        DisplayName = $firewallName
        Direction = 'Inbound'
        Action = 'Allow'
        Protocol = 'TCP'
        LocalPort = 5080
        Profile = 'Private'
        RemoteAddress = '192.168.68.0/24'
    }
    New-NetFirewallRule @firewallParameters | Out-Null
}

Start-LavanderiaService -ServiceName $ServiceName
if (-not (Wait-LavanderiaHealth)) {
    Stop-LavanderiaService -ServiceName $ServiceName
    throw 'O serviço iniciou, mas o health check falhou. O serviço foi parado para diagnóstico.'
}

[pscustomobject]@{
    ServiceName = $ServiceName
    Status = (Get-Service -Name $ServiceName).Status.ToString()
    StartMode = 'Automatic'
    Account = 'NT AUTHORITY\LocalService'
    ReleaseId = $ReleaseId
    Health = 'Healthy'
    Url = 'http://SISTEMALAV:5080'
}
