[CmdletBinding(DefaultParameterSetName = 'Integrated')]
param(
    [Parameter(Mandatory)][ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $SqlFile,

    [string] $ServerInstance = '.\SQLEXPRESS',
    [string] $Database = 'LavanderiaAnjos',

    [Parameter(Mandatory, ParameterSetName = 'SqlCredential')]
    [PSCredential] $SqlCredential,

    [int] $CommandTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source'] = $ServerInstance
$builder['Initial Catalog'] = $Database
$builder['Application Name'] = 'LavanderiaAnjos migration executor'
$builder['Connect Timeout'] = 30
$isLocalDb = $ServerInstance -match '^\(localdb\)\\'
$builder['Encrypt'] = -not $isLocalDb
$builder['TrustServerCertificate'] = -not $isLocalDb

if ($PSCmdlet.ParameterSetName -eq 'SqlCredential') {
    $builder['User ID'] = $SqlCredential.UserName
    $builder['Password'] = $SqlCredential.GetNetworkCredential().Password
} else {
    $builder['Integrated Security'] = $true
}

$sql = Get-Content -LiteralPath $SqlFile -Raw -Encoding UTF8
$batches = [regex]::Split($sql, '(?im)^\s*GO\s*(?:--.*)?$') |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
try {
    $connection.Open()
    foreach ($batch in $batches) {
        $command = $connection.CreateCommand()
        try {
            $command.CommandText = $batch
            $command.CommandTimeout = $CommandTimeoutSeconds
            [void]$command.ExecuteNonQuery()
        } finally {
            $command.Dispose()
        }
    }
} finally {
    $builder['Password'] = ''
    $connection.Dispose()
}

Write-Output "Script SQL concluído com sucesso em '$Database' no servidor '$ServerInstance'."
