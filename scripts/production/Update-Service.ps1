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
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    throw "O serviço '$ServiceName' não existe. Use Install-Service.ps1."
}
if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $manifestPath = Join-Path ([IO.Path]::GetFullPath($PackagePath)) 'release-manifest.json'
    $ReleaseId = if (Test-Path -LiteralPath $manifestPath) {
        [string](Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json).releaseId
    } else {
        [IO.Path]::GetFileName([IO.Path]::GetFullPath($PackagePath).TrimEnd('\'))
    }
}

$previousTarget = Get-CurrentReleaseTarget -Paths $paths
if ([string]::IsNullOrWhiteSpace($previousTarget)) {
    throw 'A release atual não pôde ser identificada; a atualização foi interrompida.'
}
$releasePath = Copy-ReleasePackage -PackagePath $PackagePath -Paths $paths -ReleaseId $ReleaseId

Stop-LavanderiaService -ServiceName $ServiceName
try {
    Set-CurrentRelease -Paths $paths -ReleasePath $releasePath
    Start-LavanderiaService -ServiceName $ServiceName
    if (-not (Wait-LavanderiaHealth)) {
        throw 'O health check da nova release falhou.'
    }
}
catch {
    try { Stop-LavanderiaService -ServiceName $ServiceName } catch { }
    Set-CurrentRelease -Paths $paths -ReleasePath $previousTarget
    Start-LavanderiaService -ServiceName $ServiceName
    if (-not (Wait-LavanderiaHealth)) {
        throw 'A atualização falhou e a release anterior também não recuperou a saúde. Intervenção manual necessária.'
    }
    throw 'A atualização falhou; rollback automático concluído e release anterior saudável.'
}

[pscustomobject]@{
    ServiceName = $ServiceName
    Status = (Get-Service -Name $ServiceName).Status.ToString()
    PreviousRelease = [IO.Path]::GetFileName($previousTarget.TrimEnd('\'))
    CurrentRelease = $ReleaseId
    Health = 'Healthy'
}
