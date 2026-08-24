[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [string] $JavaScriptTestsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resolvedPackage = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath (Join-Path $resolvedPackage 'app\wwwroot\assets\brand\lavanderia-anjos-color.svg') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $resolvedPackage 'app\wwwroot\site.webmanifest') -PathType Leaf)) {
    throw "Pacote sem os assets oficiais de marca: $resolvedPackage"
}
if ([string]::IsNullOrWhiteSpace($JavaScriptTestsPath)) {
    $JavaScriptTestsPath = Join-Path $PSScriptRoot 'javascript'
}
$testPath = Join-Path ([IO.Path]::GetFullPath($JavaScriptTestsPath)) 'branding.test.mjs'
if (-not (Test-Path -LiteralPath $testPath -PathType Leaf)) {
    throw 'Teste JavaScript de branding não encontrado.'
}

$previousPackageRoot = [Environment]::GetEnvironmentVariable('LAVANDERIA_PACKAGE_ROOT', 'Process')
try {
    $env:LAVANDERIA_PACKAGE_ROOT = $resolvedPackage
    & node --test $testPath
    if ($LASTEXITCODE -ne 0) { throw "Testes de branding falharam com código $LASTEXITCODE." }
} finally {
    if ($null -eq $previousPackageRoot) { Remove-Item Env:LAVANDERIA_PACKAGE_ROOT -ErrorAction SilentlyContinue }
    else { $env:LAVANDERIA_PACKAGE_ROOT = $previousPackageRoot }
}
