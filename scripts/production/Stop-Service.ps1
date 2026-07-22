[CmdletBinding()]
param([string] $ServiceName = 'LavanderiaAnjos')

. (Join-Path $PSScriptRoot 'Deployment-Common.ps1')
Assert-Administrator
Stop-LavanderiaService -ServiceName $ServiceName
[pscustomobject]@{ ServiceName = $ServiceName; Status = 'Stopped' }
