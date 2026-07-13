[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\LavanderiaAnjos.Web\LavanderiaAnjos.Web.csproj'

dotnet run --project $project --no-launch-profile -- --check-database
