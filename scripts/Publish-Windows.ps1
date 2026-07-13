[CmdletBinding()]
param(
    [ValidateSet('true', 'false')]
    [string] $SelfContained = 'true'
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj'
$output = Join-Path $PSScriptRoot '..\publish\win-x64'

dotnet publish $project -c Release -r win-x64 --self-contained $SelfContained -o $output
