[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [Parameter(Mandatory)][string] $PlaywrightModulePath,
    [Parameter(Mandatory)][string] $OutputPath,
    [string] $ChromePath = 'C:\Program Files\Google\Chrome\Application\chrome.exe'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$names = @('LAVANDERIA_PACKAGE_ROOT', 'PLAYWRIGHT_MODULE_PATH', 'LAVANDERIA_BROWSER_OUTPUT', 'CHROME_PATH')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:LAVANDERIA_PACKAGE_ROOT = [IO.Path]::GetFullPath($PackagePath)
    $env:PLAYWRIGHT_MODULE_PATH = [IO.Path]::GetFullPath($PlaywrightModulePath)
    $env:LAVANDERIA_BROWSER_OUTPUT = [IO.Path]::GetFullPath($OutputPath)
    $env:CHROME_PATH = $ChromePath
    & node (Join-Path $PSScriptRoot 'browser\visual-polish.cjs')
    if ($LASTEXITCODE -ne 0) { throw "Validação visual/clipboard falhou: $LASTEXITCODE" }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
