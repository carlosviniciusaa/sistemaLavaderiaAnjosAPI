Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Execute este script em uma janela do PowerShell aberta como administrador.'
    }
}

function Get-DeploymentPaths {
    param([Parameter(Mandatory)][string] $Root)

    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    [pscustomobject]@{
        Root = $resolvedRoot
        Current = Join-Path $resolvedRoot 'current'
        Releases = Join-Path $resolvedRoot 'releases'
        Logs = Join-Path $resolvedRoot 'logs'
        Scripts = Join-Path $resolvedRoot 'scripts'
        Docs = Join-Path $resolvedRoot 'docs'
        Temporary = Join-Path $resolvedRoot 'temporary'
    }
}

function Assert-PathUnderRoot {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Root
    )

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    if (-not $fullPath.StartsWith("$fullRoot\", [StringComparison]::OrdinalIgnoreCase)) {
        throw "O caminho '$fullPath' está fora de '$fullRoot'."
    }
    return $fullPath
}

function Assert-ReleaseContent {
    param([Parameter(Mandatory)][string] $ReleasePath)

    $fullPath = [IO.Path]::GetFullPath($ReleasePath)
    if (-not (Test-Path -LiteralPath (Join-Path $fullPath 'LavanderiaAnjos.Web.exe') -PathType Leaf)) {
        throw "Executável da aplicação não encontrado em '$fullPath'."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $fullPath 'wwwroot') -PathType Container)) {
        throw "Arquivos estáticos não encontrados em '$fullPath'."
    }

    $forbidden = @(Get-ChildItem -LiteralPath $fullPath -Recurse -File | Where-Object {
        $_.Extension -match '^\.(xlsx?|csv|bak)$' -or
        $_.Name -match '(?i)(secrets|stage).+\.json$'
    })
    if ($forbidden.Count -gt 0) {
        throw 'A release contém planilha, relatório, backup ou arquivo temporário proibido.'
    }
}

function Get-CurrentReleaseTarget {
    param([Parameter(Mandatory)][pscustomobject] $Paths)

    if (-not (Test-Path -LiteralPath $Paths.Current)) {
        return $null
    }
    $item = Get-Item -LiteralPath $Paths.Current -Force
    if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "'$($Paths.Current)' existe, mas não é uma junção gerenciada pelos scripts."
    }
    return [IO.Path]::GetFullPath([string]$item.Target)
}

function Set-CurrentRelease {
    param(
        [Parameter(Mandatory)][pscustomobject] $Paths,
        [Parameter(Mandatory)][string] $ReleasePath
    )

    $target = Assert-PathUnderRoot -Path $ReleasePath -Root $Paths.Releases
    Assert-ReleaseContent -ReleasePath $target
    $nextPath = "$($Paths.Current).next.$([Guid]::NewGuid().ToString('N'))"
    $oldPath = "$($Paths.Current).old.$([Guid]::NewGuid().ToString('N'))"
    Assert-PathUnderRoot -Path $nextPath -Root $Paths.Root | Out-Null
    Assert-PathUnderRoot -Path $oldPath -Root $Paths.Root | Out-Null

    New-Item -ItemType Junction -Path $nextPath -Target $target | Out-Null
    try {
        if (Test-Path -LiteralPath $Paths.Current) {
            $currentItem = Get-Item -LiteralPath $Paths.Current -Force
            if (-not ($currentItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "'$($Paths.Current)' não é uma junção; a troca foi interrompida."
            }
            Rename-Item -LiteralPath $Paths.Current -NewName ([IO.Path]::GetFileName($oldPath))
        }
        Rename-Item -LiteralPath $nextPath -NewName ([IO.Path]::GetFileName($Paths.Current))
        if (Test-Path -LiteralPath $oldPath) {
            $oldItem = Get-Item -LiteralPath $oldPath -Force
            if (-not ($oldItem.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "'$oldPath' não é uma junção; a limpeza foi interrompida."
            }
            [IO.Directory]::Delete($oldPath)
        }
    }
    catch {
        if (-not (Test-Path -LiteralPath $Paths.Current) -and (Test-Path -LiteralPath $oldPath)) {
            Rename-Item -LiteralPath $oldPath -NewName ([IO.Path]::GetFileName($Paths.Current))
        }
        if (Test-Path -LiteralPath $nextPath) {
            $nextItem = Get-Item -LiteralPath $nextPath -Force
            if ($nextItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                [IO.Directory]::Delete($nextPath)
            }
        }
        throw
    }
}

function Stop-LavanderiaService {
    param(
        [Parameter(Mandatory)][string] $ServiceName,
        [int] $TimeoutSeconds = 30
    )

    $service = Get-Service -Name $ServiceName -ErrorAction Stop
    if ($service.Status -ne [ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $ServiceName -ErrorAction Stop
        $service.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds($TimeoutSeconds))
    }
}

function Start-LavanderiaService {
    param(
        [Parameter(Mandatory)][string] $ServiceName,
        [int] $TimeoutSeconds = 30
    )

    Start-Service -Name $ServiceName -ErrorAction Stop
    (Get-Service -Name $ServiceName).WaitForStatus(
        [ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds($TimeoutSeconds))
}

function Wait-LavanderiaHealth {
    param(
        [string] $Uri = 'http://127.0.0.1:5080/health',
        [int] $TimeoutSeconds = 60
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        try {
            $response = Invoke-RestMethod -Uri $Uri -TimeoutSec 5
            if ($response.status -eq 'Healthy' -and $response.database -eq 'Healthy') {
                return $true
            }
        }
        catch {
            # O serviço pode ainda estar inicializando.
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    return $false
}

function Copy-ReleasePackage {
    param(
        [Parameter(Mandatory)][string] $PackagePath,
        [Parameter(Mandatory)][pscustomobject] $Paths,
        [Parameter(Mandatory)][string] $ReleaseId
    )

    if ($ReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
        throw 'ReleaseId contém caracteres inválidos.'
    }
    $packageRoot = [IO.Path]::GetFullPath($PackagePath)
    $appSource = if (Test-Path -LiteralPath (Join-Path $packageRoot 'app')) {
        Join-Path $packageRoot 'app'
    } else {
        $packageRoot
    }
    Assert-ReleaseContent -ReleasePath $appSource
    $destination = Assert-PathUnderRoot -Path (Join-Path $Paths.Releases $ReleaseId) -Root $Paths.Releases
    if (Test-Path -LiteralPath $destination) {
        throw "A release '$ReleaseId' já existe."
    }

    New-Item -ItemType Directory -Path $destination | Out-Null
    Copy-Item -Path (Join-Path $appSource '*') -Destination $destination -Recurse -Force
    Assert-ReleaseContent -ReleasePath $destination

    $operations = Join-Path $packageRoot 'operations'
    if (Test-Path -LiteralPath (Join-Path $operations 'scripts')) {
        Copy-Item -Path (Join-Path $operations 'scripts\*') -Destination $Paths.Scripts -Recurse -Force
    }
    if (Test-Path -LiteralPath (Join-Path $operations 'docs')) {
        Copy-Item -Path (Join-Path $operations 'docs\*') -Destination $Paths.Docs -Recurse -Force
    }
    return $destination
}
