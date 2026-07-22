[CmdletBinding()]
param(
    [string] $TargetReleaseId,
    [string] $Root = 'D:\LavanderiaAnjos\Sistema',
    [string] $ServiceName = 'LavanderiaAnjos'
)

. (Join-Path $PSScriptRoot 'Deployment-Common.ps1')
Assert-Administrator
$paths = Get-DeploymentPaths -Root $Root
$currentTarget = Get-CurrentReleaseTarget -Paths $paths
if ([string]::IsNullOrWhiteSpace($currentTarget)) {
    throw 'A release atual não pôde ser identificada.'
}

if ([string]::IsNullOrWhiteSpace($TargetReleaseId)) {
    $candidates = @(Get-ChildItem -LiteralPath $paths.Releases -Directory |
        Where-Object { $_.FullName -ne $currentTarget } |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($candidates.Count -eq 0) {
        throw 'Nenhuma release anterior está disponível para rollback.'
    }
    $target = $candidates[0].FullName
} else {
    if ($TargetReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
        throw 'TargetReleaseId contém caracteres inválidos.'
    }
    $target = Assert-PathUnderRoot -Path (Join-Path $paths.Releases $TargetReleaseId) -Root $paths.Releases
}
Assert-ReleaseContent -ReleasePath $target

Stop-LavanderiaService -ServiceName $ServiceName
try {
    Set-CurrentRelease -Paths $paths -ReleasePath $target
    Start-LavanderiaService -ServiceName $ServiceName
    if (-not (Wait-LavanderiaHealth)) {
        throw 'O health check da release de rollback falhou.'
    }
}
catch {
    try { Stop-LavanderiaService -ServiceName $ServiceName } catch { }
    Set-CurrentRelease -Paths $paths -ReleasePath $currentTarget
    Start-LavanderiaService -ServiceName $ServiceName
    throw
}

[pscustomobject]@{
    ServiceName = $ServiceName
    Status = (Get-Service -Name $ServiceName).Status.ToString()
    CurrentRelease = [IO.Path]::GetFileName($target.TrimEnd('\'))
    Health = 'Healthy'
}
