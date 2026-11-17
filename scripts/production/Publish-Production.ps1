[CmdletBinding()]
param(
    [string] $OutputRoot,
    [string] $ReleaseId,
    [string] $ValidationReportPath,
    [string] $PlaywrightModulePath,
    [string] $BrowserOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $repositoryRoot 'src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj'
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'publish\production'
}
if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $gitHash = (& git -C $repositoryRoot rev-parse --short HEAD 2>$null)
    if ([string]::IsNullOrWhiteSpace($gitHash)) {
        $gitHash = 'sem-git'
    }
    $dirtySuffix = if ((& git -C $repositoryRoot status --porcelain 2>$null)) { '-dirty' } else { '' }
    $ReleaseId = "$(Get-Date -Format 'yyyyMMddHHmmss')-$gitHash$dirtySuffix"
}
if ($ReleaseId -notmatch '^[A-Za-z0-9._-]+$') {
    throw 'ReleaseId contém caracteres inválidos.'
}

$packagePath = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $ReleaseId
$zipPath = "$packagePath.zip"
$hashPath = "$zipPath.sha256"
$temporaryZipPath = "$zipPath.partial.zip"
if ((Test-Path -LiteralPath $packagePath) -or
    (Test-Path -LiteralPath $zipPath) -or
    (Test-Path -LiteralPath $hashPath) -or
    (Test-Path -LiteralPath $temporaryZipPath)) {
    throw "A pasta, o ZIP ou o hash da release '$ReleaseId' já existe. Escolha outro ReleaseId."
}
$appPath = Join-Path $packagePath 'app'
$operationsPath = Join-Path $packagePath 'operations'
New-Item -ItemType Directory -Path $appPath | Out-Null
New-Item -ItemType Directory -Path (Join-Path $operationsPath 'scripts') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $operationsPath 'docs') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $operationsPath 'sql') | Out-Null
New-Item -ItemType Directory -Path (Join-Path $operationsPath 'tests\javascript') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $operationsPath 'tests\browser') -Force | Out-Null

$publishArguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $appPath,
    '--no-restore',
    '-m:1',
    '-nr:false',
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false'
)
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & dotnet @publishArguments
    $publishExitCode = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $previousErrorActionPreference
}
if ($publishExitCode -ne 0) {
    throw "dotnet publish falhou com código $publishExitCode."
}

$forbidden = @(Get-ChildItem -LiteralPath $appPath -Recurse -File | Where-Object {
    $_.Extension -match '^\.(xlsx?|csv|bak)$' -or
    $_.FullName -match '(?i)\\(tests?|stage|reports?|logs?)\\' -or
    $_.Name -match '(?i)(secrets|stage).+\.json$'
})
if ($forbidden.Count -gt 0) {
    throw 'A publicação contém artefatos proibidos e não será entregue.'
}
if (-not (Test-Path -LiteralPath (Join-Path $appPath 'LavanderiaAnjos.Web.exe') -PathType Leaf)) {
    throw 'Executável win-x64 não encontrado após a publicação.'
}
if (-not (Test-Path -LiteralPath (Join-Path $appPath 'wwwroot') -PathType Container)) {
    throw 'Arquivos estáticos não encontrados após a publicação.'
}

Copy-Item -Path (Join-Path $PSScriptRoot '*.ps1') -Destination (Join-Path $operationsPath 'scripts')
$documents = @(
    'implantacao-producao.md',
    'operacao-servidor.md',
    'atualizacao-e-rollback.md',
    'testes-copia-mobile.md',
    'identidade-visual.md'
)
foreach ($document in $documents) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "docs\$document") -Destination (Join-Path $operationsPath 'docs')
}
Copy-Item -Path (Join-Path $repositoryRoot 'tests\javascript\clipboard*.test.mjs') `
    -Destination (Join-Path $operationsPath 'tests\javascript')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'tests\javascript\branding.test.mjs') `
    -Destination (Join-Path $operationsPath 'tests\javascript')
Copy-Item -Path (Join-Path $repositoryRoot 'tests\browser\*') `
    -Destination (Join-Path $operationsPath 'tests\browser')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'tests\production\Test-PackagedCopyWiring.ps1') `
    -Destination (Join-Path $operationsPath 'tests')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'tests\production\Test-PackagedBranding.ps1') `
    -Destination (Join-Path $operationsPath 'tests')
foreach ($test in @('Test-PackagedSafety.ps1', 'Test-PackagedVisual.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "tests\production\$test") -Destination (Join-Path $operationsPath 'tests')
}
Copy-Item -Path (Join-Path $repositoryRoot 'docs\release-*-visual-polish.md') -Destination (Join-Path $operationsPath 'docs')

$validation = if (-not [string]::IsNullOrWhiteSpace($ValidationReportPath)) {
    Get-Content -LiteralPath $ValidationReportPath -Raw | ConvertFrom-Json
} else { $null }
if ($null -ne $validation) {
    if ($validation.status -ne 'passed') { throw 'O relatório pré-empacotamento não está aprovado.' }
    $validation | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $packagePath 'validation-report.json') -Encoding UTF8
}

# Public source fingerprint: paths and hashes only, including pending visual changes.
$sourceInventory = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard) |
    Sort-Object -Unique | ForEach-Object {
        $sourcePath = Join-Path $repositoryRoot $_
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            "$((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash) *$_"
        }
    }
$sourceInventory | Set-Content -LiteralPath (Join-Path $packagePath 'source-files.sha256') -Encoding UTF8
$gitCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$sourceTreeDirty = [bool](& git -C $repositoryRoot status --porcelain)

$manifest = [ordered]@{
    releaseId = $ReleaseId
    createdAtUtc = [DateTime]::UtcNow.ToString('O')
    configuration = 'Release'
    runtimeIdentifier = 'win-x64'
    selfContained = $true
    publishSingleFile = $false
    publishTrimmed = $false
    containsSecrets = $false
    databaseChanges = $false
    change = ($ReleaseId -replace '^\d+-', '')
    sourceCommit = $gitCommit
    sourceTreeDirty = $sourceTreeDirty
    sourceSnapshotSha256 = (Get-FileHash -LiteralPath (Join-Path $packagePath 'source-files.sha256') -Algorithm SHA256).Hash
    filesInventory = 'release-files.sha256'
    securityReport = 'security-scan.json'
    validationReport = if ($null -ne $validation) { 'validation-report.json' } else { $null }
    productionAccessed = $false
    databaseMigrationsExecuted = $false
    backupsModified = $false
    includedTests = @(
        'operations/tests/Test-PackagedBranding.ps1',
        'operations/tests/Test-PackagedCopyWiring.ps1',
        'operations/tests/Test-PackagedSafety.ps1',
        'operations/tests/Test-PackagedVisual.ps1',
        'operations/tests/browser/visual-polish.cjs'
    )
    requiredMigration = $null
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $packagePath 'release-manifest.json') -Encoding UTF8

# Fail before compression if a publish accidentally copied local configuration or data.
$safetyTest = Join-Path $operationsPath 'tests\Test-PackagedSafety.ps1'
& $safetyTest -PackagePath $packagePath -SkipFileHashes |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $packagePath 'security-scan.json') -Encoding UTF8
& (Join-Path $operationsPath 'tests\Test-PackagedBranding.ps1') -PackagePath $packagePath
& (Join-Path $operationsPath 'tests\Test-PackagedCopyWiring.ps1') -PackagePath $packagePath
$browserValidated = $false
if (-not [string]::IsNullOrWhiteSpace($PlaywrightModulePath)) {
    if ([string]::IsNullOrWhiteSpace($BrowserOutputPath)) {
        $BrowserOutputPath = Join-Path $repositoryRoot ".codex-tmp\$ReleaseId-browser"
    }
    & (Join-Path $operationsPath 'tests\Test-PackagedVisual.ps1') -PackagePath $packagePath `
        -PlaywrightModulePath $PlaywrightModulePath -OutputPath $BrowserOutputPath
    $browserValidated = $true
}
if ($null -ne $validation) {
    $validation | Add-Member -NotePropertyName packagedValidation -NotePropertyValue ([ordered]@{
        branding = 'passed'
        clipboardJavaScript = 'passed'
        browser = if ($browserValidated) { 'passed' } else { 'not-run' }
        security = 'passed'
        apiMode = 'isolated fixtures; no database or production connection'
    }) -Force
    $validation | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $packagePath 'validation-report.json') -Encoding UTF8
}
$packageRoot = [IO.Path]::GetFullPath($packagePath).TrimEnd('\')
Get-ChildItem -LiteralPath $packagePath -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash) *$relative"
} | Set-Content -LiteralPath (Join-Path $packagePath 'release-files.sha256') -Encoding UTF8
& $safetyTest -PackagePath $packagePath

$compressed = $false
for ($attempt = 1; $attempt -le 15 -and -not $compressed; $attempt++) {
    try {
        $archiveParent = Split-Path -Parent $packagePath
        $archiveDirectory = Split-Path -Leaf $packagePath
        Push-Location $archiveParent
        try {
            & tar.exe -a -c -f $temporaryZipPath $archiveDirectory
            if ($LASTEXITCODE -ne 0) { throw "tar.exe falhou com código $LASTEXITCODE." }
        } finally {
            Pop-Location
        }
        Move-Item -LiteralPath $temporaryZipPath -Destination $zipPath
        $compressed = $true
    } catch {
        if (Test-Path -LiteralPath $temporaryZipPath) {
            Remove-Item -LiteralPath $temporaryZipPath -Force
        }
        if ($attempt -eq 15) { throw }
        Start-Sleep -Seconds 2
    }
}
$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
"$sha256 *$([IO.Path]::GetFileName($zipPath))" |
    Set-Content -LiteralPath $hashPath -Encoding ASCII

[pscustomobject]@{
    ReleaseId = $ReleaseId
    PackagePath = $packagePath
    ZipPath = $zipPath
    ZipSizeBytes = (Get-Item -LiteralPath $zipPath).Length
    Sha256 = $sha256
    HashPath = $hashPath
}
