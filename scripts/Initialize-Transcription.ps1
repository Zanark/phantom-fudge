[CmdletBinding()]
param(
    [switch]$DownloadModel
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Root = Split-Path -Parent $PSScriptRoot
$Local = Join-Path $Root '.local'
$Models = Join-Path $Local 'models'
$Manifest = Get-Content -LiteralPath (Join-Path $Root 'services\transcription\model.json') -Raw | ConvertFrom-Json
$Model = Join-Path $Models $Manifest.name
$Archive = Join-Path $Models ($Manifest.name + '.zip')
$Token = Join-Path $Local 'transcription.token'

function Test-Model {
    if (-not (Test-Path -LiteralPath $Model -PathType Container)) { return $false }
    if ((Get-ChildItem -LiteralPath $Model -File -Recurse -Force | Measure-Object).Count -ne @($Manifest.files.PSObject.Properties).Count) {
        return $false
    }
    if (Get-ChildItem -LiteralPath $Model -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
        return $false
    }
    foreach ($entry in $Manifest.files.PSObject.Properties) {
        $file = Join-Path $Model $entry.Name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { return $false }
        if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) { return $false }
    }
    return $true
}

New-Item -ItemType Directory -Force -Path $Models | Out-Null
if (-not (Test-Path -LiteralPath $Token)) {
    $random = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($random) } finally { $generator.Dispose() }
    $secret = [BitConverter]::ToString($random).Replace('-', '').ToLowerInvariant()
    $stream = [IO.File]::Open($Token, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = [Text.Encoding]::ASCII.GetBytes($secret)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally { $stream.Dispose(); $secret = $null; $random = $null; $bytes = $null }
}
if ($env:OS -eq 'Windows_NT') {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $tokenItem = Get-Item -LiteralPath $Token
    # Touch only the DACL: setting audit/owner sections can require elevation.
    if ($PSVersionTable.PSEdition -eq 'Core') {
        $acl = [IO.FileSystemAclExtensions]::GetAccessControl($tokenItem, [Security.AccessControl.AccessControlSections]::Access)
    }
    else { $acl = $tokenItem.GetAccessControl([Security.AccessControl.AccessControlSections]::Access) }
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($existingRule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        $acl.RemoveAccessRuleSpecific($existingRule)
    }
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'Allow')
    $acl.AddAccessRule($rule)
    if ($PSVersionTable.PSEdition -eq 'Core') {
        [IO.FileSystemAclExtensions]::SetAccessControl($tokenItem, $acl)
    }
    else { $tokenItem.SetAccessControl($acl) }
}
$existingToken = [IO.File]::ReadAllText($Token).Trim()
if ($existingToken -notmatch '^[A-Za-z0-9_-]{32,512}$') { throw 'Existing local token is invalid. Remove it explicitly and rerun setup to rotate it.' }
$existingToken = $null

if (-not (Test-Model)) {
    if (-not (Test-Path -LiteralPath $Archive)) {
        if (-not $DownloadModel) {
            throw 'The pinned local model is absent. Rerun Initialize-Transcription.ps1 -DownloadModel to explicitly download the official English Apache-2.0 model (approximately 40 MB).'
        }
        Write-Host 'Downloading the explicitly requested official English Vosk model (Apache-2.0).'
        $partial = $Archive + '.partial'
        try {
            Invoke-WebRequest -Uri $Manifest.url -OutFile $partial -UseBasicParsing -TimeoutSec 300
            if ((Get-Item -LiteralPath $partial).Length -ne $Manifest.archiveBytes -or
                (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Manifest.archiveSha256) {
                throw 'Downloaded model failed the pinned integrity check. Nothing was installed.'
            }
            Move-Item -LiteralPath $partial -Destination $Archive
        }
        finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force } }
    }
    if ((Get-Item -LiteralPath $Archive).Length -ne $Manifest.archiveBytes -or
        (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Manifest.archiveSha256) {
        throw 'Cached model archive failed its pinned integrity check. Remove the archive explicitly before downloading again.'
    }
    # Only a byte-for-byte verified archive is extracted. Never overwrite an existing model.
    if (Test-Path -LiteralPath $Model) {
        throw 'Existing model files failed verification. Remove that model directory explicitly, then rerun setup.'
    }
    Expand-Archive -LiteralPath $Archive -DestinationPath $Models
    if (-not (Test-Model)) { throw 'Extracted model failed verification. The service must not be started.' }
}

$receipt = [ordered]@{
    model = $Manifest.name
    version = $Manifest.version
    license = $Manifest.license
    source = $Manifest.url
    archiveSha256 = $Manifest.archiveSha256
    archiveBytes = $Manifest.archiveBytes
    hashProvenance = $Manifest.hashProvenance
    modelFilesVerified = $true
    verifiedAtUtc = [DateTime]::UtcNow.ToString('o')
}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Models ($Manifest.name + '.receipt.json')) -Encoding UTF8
Write-Host 'Local token and pinned model are ready. The token was not printed. No speech service was started.'
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Warning 'Docker CLI/engine is not installed. Setup did not install Docker or enable virtualization. Docker execution remains unavailable.'
}
