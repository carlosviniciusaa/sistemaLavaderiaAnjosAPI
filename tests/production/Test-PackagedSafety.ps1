[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackagePath,
    [switch] $SkipFileHashes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PackagePath).TrimEnd('\')
$appRoot = Join-Path $packageRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'LavanderiaAnjos.Web.exe') -PathType Leaf)) {
    throw 'Executável de produção ausente.'
}
$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -Force -File)
$findings = [Collections.Generic.List[string]]::new()
$textCount = 0
$signatureCount = 0
$secretPatterns = @(
    '-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-----',
    '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b',
    '\bgh[pousr]_[A-Za-z0-9]{30,}\b',
    '\bgithub_pat_[A-Za-z0-9_]{40,}\b',
    '\bsk-(?:proj-|svcacct-)[A-Za-z0-9_-]{30,}\b'
)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
    if ($relative -match '(?i)(^|/)(\.git|\.aws|\.ssh|secrets|node_modules|logs|stage|reports|temporary|dados-reais)(/|$)' -or
        $file.Extension -match '(?i)^\.(xlsx?|csv|bak|pfx|p12|pem|key|user|suo|db|sqlite|sql)$' -or
        $file.Name -match '(?i)(^\.env($|\.)|secrets.*\.json$|^appsettings\..+\.json$|^key-.+\.xml$)') {
        $findings.Add("forbidden-file: $relative")
    }
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { $findings.Add("reparse-point: $relative") }
    # Signature checks also cover compiled files; no matched value is ever printed.
    if ($file.Length -gt 0) {
        $bytes = [IO.File]::ReadAllBytes($file.FullName)
        $utf8 = [Text.Encoding]::UTF8.GetString($bytes)
        foreach ($pattern in $secretPatterns) {
            if ([regex]::IsMatch($utf8, $pattern)) { $findings.Add("secret-signature: $relative") }
        }
        $signatureCount++
        if ($file.Extension -match '(?i)^\.(json|js|mjs|cjs|css|html|ps1|md|txt|xml|config|yml|yaml|webmanifest)$') {
            $textCount++
            if ($utf8 -match '(?im)(?:Server|Data Source)\s*=\s*[^;\r\n]+;\s*(?:Database|Initial Catalog)\s*=\s*[^;\r\n]+;') {
                $findings.Add("literal-connection-string: $relative")
            }
            if ($utf8 -match '(?im)(?:Password|Pwd|ClientSecret|ApiKey|AccessToken)\s*[=:]\s*["''][A-Za-z0-9+/=_-]{24,}["'']') {
                $findings.Add("literal-credential: $relative")
            }
        }
    }
}
$settings = Get-Content -LiteralPath (Join-Path $appRoot 'appsettings.json') -Raw | ConvertFrom-Json
foreach ($entry in $settings.ConnectionStrings.PSObject.Properties) {
    if (-not [string]::IsNullOrWhiteSpace([string]$entry.Value)) { $findings.Add('nonempty-connection-string: app/appsettings.json') }
}
if ($settings.PSObject.Properties.Name -contains 'BootstrapAdmin') { $findings.Add('bootstrap-config: app/appsettings.json') }
if ($findings.Count -gt 0) { throw "Conteúdo sensível ou proibido encontrado (somente regras e caminhos): $($findings -join '; ')" }

$verifiedHashes = 0
if (-not $SkipFileHashes) {
    $hashFile = Join-Path $packageRoot 'release-files.sha256'
    if (-not (Test-Path -LiteralPath $hashFile -PathType Leaf)) { throw 'Inventário SHA-256 ausente.' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath $hashFile) {
        if ($line -notmatch '^([A-Fa-f0-9]{64}) \*(.+)$') { throw 'Linha inválida no inventário SHA-256.' }
        $expected = $Matches[1]
        $relative = $Matches[2]
        $target = [IO.Path]::GetFullPath((Join-Path $packageRoot $relative))
        if (-not $target.StartsWith("$packageRoot\", [StringComparison]::OrdinalIgnoreCase)) { throw 'Caminho fora do pacote no inventário.' }
        if (-not $seen.Add($relative)) { throw 'Arquivo duplicado no inventário.' }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw "Hash divergente: $relative" }
        $verifiedHashes++
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
        if ($relative -ne 'release-files.sha256' -and -not $seen.Contains($relative)) { throw "Arquivo sem hash: $relative" }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'release-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.databaseChanges -ne $false -or $null -ne $manifest.requiredMigration -or $manifest.containsSecrets -ne $false) {
        throw 'Manifesto incompatível com uma release sem segredos e sem migration.'
    }
}
[pscustomobject]@{
    status = 'passed'
    filesInspected = $files.Count
    signatureScannedFiles = $signatureCount
    textFilesInspected = $textCount
    verifiedFileHashes = $verifiedHashes
    findings = 0
    emptyConnectionStrings = $true
    bootstrapCredentialsAbsent = $true
    databasePayloadAbsent = $true
    productionAccessed = $false
    scope = 'Packaged files, secret signatures, text credentials and appsettings; synthetic browser fixtures are not production credentials.'
}
