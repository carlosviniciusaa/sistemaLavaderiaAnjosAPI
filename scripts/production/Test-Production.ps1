[CmdletBinding()]
param(
    [string] $ServiceName = 'LavanderiaAnjos',
    [string] $ServerName = 'SISTEMALAV',
    [string] $ServerIp = '192.168.68.119',
    [int] $ExpectedClientCount = 347,
    [PSCredential] $AdminCredential
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-HttpStatus {
    param(
        [Parameter(Mandatory)][string] $Uri,
        [int] $ExpectedStatus = 200,
        [switch] $NoRedirect
    )

    try {
        $request = [Net.HttpWebRequest]::CreateHttp($Uri)
        $request.AllowAutoRedirect = -not $NoRedirect
        $request.Timeout = 10000
        $response = $request.GetResponse()
        try { return [int]$response.StatusCode -eq $ExpectedStatus } finally { $response.Dispose() }
    }
    catch [Net.WebException] {
        if ($_.Exception.Response) {
            $response = $_.Exception.Response
            try { return [int]$response.StatusCode -eq $ExpectedStatus } finally { $response.Dispose() }
        }
        return $false
    }
}

$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction Stop
$listener = @(Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue)
$localHealth = Test-HttpStatus -Uri 'http://127.0.0.1:5080/health'
$loginPage = Test-HttpStatus -Uri 'http://127.0.0.1:5080/login'
$styles = Test-HttpStatus -Uri 'http://127.0.0.1:5080/styles.css'
$script = Test-HttpStatus -Uri 'http://127.0.0.1:5080/app.js'
$unauthorizedApi = Test-HttpStatus -Uri 'http://127.0.0.1:5080/api/clientes' -ExpectedStatus 401 -NoRedirect
$protectedPage = Test-HttpStatus -Uri 'http://127.0.0.1:5080/clientes' -ExpectedStatus 302 -NoRedirect
$nameHealth = Test-HttpStatus -Uri "http://$ServerName`:5080/health"
$ipHealth = Test-HttpStatus -Uri "http://$ServerIp`:5080/health"

$serviceEnvironment = @((Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName").Environment)
$productionEnvironment = $serviceEnvironment -contains 'ASPNETCORE_ENVIRONMENT=Production'
$fixedUrl = $serviceEnvironment -contains 'ASPNETCORE_URLS=http://0.0.0.0:5080'
$externalConnection = @($serviceEnvironment | Where-Object {
    $_ -like 'ConnectionStrings__LavanderiaAnjos=*' -and $_.Length -gt 'ConnectionStrings__LavanderiaAnjos='.Length
}).Count -eq 1

$loginStatus = 'Não executado: credencial não fornecida'
$listedClientCount = $null
if ($AdminCredential) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $networkCredential = $AdminCredential.GetNetworkCredential()
    $body = @{
        login = $networkCredential.UserName
        password = $networkCredential.Password
    } | ConvertTo-Json
    try {
        $loginParameters = @{
            Uri = 'http://127.0.0.1:5080/api/auth/login'
            Method = 'Post'
            ContentType = 'application/json'
            Body = $body
            WebSession = $session
            UseBasicParsing = $true
            TimeoutSec = 10
        }
        $loginResponse = Invoke-WebRequest @loginParameters
        $listParameters = @{
            Uri = 'http://127.0.0.1:5080/api/clientes?pagina=1&tamanhoPagina=1'
            WebSession = $session
            UseBasicParsing = $true
            TimeoutSec = 10
        }
        $listResponse = Invoke-WebRequest @listParameters
        $listedClientCount = [int]($listResponse.Content | ConvertFrom-Json).totalItens
        $loginStatus = if ($loginResponse.StatusCode -eq 200) { 'Sucesso' } else { 'Falha' }
        $logoutParameters = @{
            Uri = 'http://127.0.0.1:5080/api/auth/logout'
            Method = 'Post'
            WebSession = $session
            UseBasicParsing = $true
            TimeoutSec = 10
        }
        Invoke-WebRequest @logoutParameters | Out-Null
    }
    finally {
        $body = $null
        $networkCredential.Password = $null
    }
}

$result = [pscustomobject]@{
    ServiceStatus = $service.State
    StartMode = $service.StartMode
    Account = $service.StartName
    Port5080Listening = $listener.Count -gt 0
    EnvironmentProduction = $productionEnvironment
    UrlBindingCorrect = $fixedUrl
    ExternalConnectionConfigured = $externalConnection
    LocalHealth = $localHealth
    NameHealth = $nameHealth
    IpHealth = $ipHealth
    LoginPage = $loginPage
    StaticFiles = $styles -and $script
    UnauthenticatedApiBlocked = $unauthorizedApi
    ProtectedPageRedirects = $protectedPage
    Login = $loginStatus
    DisplayedClientCount = $listedClientCount
    ClientCountMatches = $null -eq $listedClientCount -or $listedClientCount -eq $ExpectedClientCount
}
$result

if ($service.State -ne 'Running' -or $service.StartMode -ne 'Auto' -or
    -not $localHealth -or -not $loginPage -or -not ($styles -and $script) -or
    -not $unauthorizedApi -or -not $protectedPage -or -not $productionEnvironment -or
    -not $fixedUrl -or -not $externalConnection -or -not $result.ClientCountMatches) {
    throw 'Uma ou mais validações essenciais de produção falharam.'
}
