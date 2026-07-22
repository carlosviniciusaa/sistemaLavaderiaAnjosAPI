[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [string] $JavaScriptTestsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedPackage = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackage 'app\wwwroot\app.js') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $resolvedPackage 'app\wwwroot\clipboard.mjs') -PathType Leaf)) {
    throw "Pacote publicado inválido: $resolvedPackage"
}
if ([string]::IsNullOrWhiteSpace($JavaScriptTestsPath)) {
    $JavaScriptTestsPath = Join-Path $PSScriptRoot 'javascript'
}
$tests = @(Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($JavaScriptTestsPath)) -Filter 'clipboard*.test.mjs' -File)
if ($tests.Count -lt 2) { throw 'Testes JavaScript de clipboard/wiring não encontrados.' }

$previousPackageRoot = [Environment]::GetEnvironmentVariable('LAVANDERIA_PACKAGE_ROOT', 'Process')
try {
    $env:LAVANDERIA_PACKAGE_ROOT = $resolvedPackage
    $testPaths = @($tests.FullName)
    & node --test @testPaths
    if ($LASTEXITCODE -ne 0) { throw "Testes do wiring falharam com código $LASTEXITCODE." }
} finally {
    if ($null -eq $previousPackageRoot) { Remove-Item Env:LAVANDERIA_PACKAGE_ROOT -ErrorAction SilentlyContinue }
    else { $env:LAVANDERIA_PACKAGE_ROOT = $previousPackageRoot }
}
