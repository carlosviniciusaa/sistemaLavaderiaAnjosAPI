[CmdletBinding()]
param([string] $ServiceName = 'LavanderiaAnjos')

. (Join-Path $PSScriptRoot 'Deployment-Common.ps1')
Assert-Administrator
Start-LavanderiaService -ServiceName $ServiceName
if (-not (Wait-LavanderiaHealth)) {
    throw 'O serviço está em execução, mas o health check não ficou saudável.'
}
[pscustomobject]@{ ServiceName = $ServiceName; Status = 'Running'; Health = 'Healthy' }
