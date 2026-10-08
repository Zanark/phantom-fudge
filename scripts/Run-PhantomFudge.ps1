#requires -Version 7.2
param(
    [switch]$NoBuild,
    [switch]$NotesOnly,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PhantomFudge.App\PhantomFudge.App.csproj'
$packagedExecutable = Join-Path $root 'PhantomFudge.exe'
$packaged = Test-Path -LiteralPath $packagedExecutable -PathType Leaf
$executable = if ($packaged) { $packagedExecutable } else {
    Join-Path $root 'src\PhantomFudge.App\bin\Release\net10.0-windows\PhantomFudge.exe'
}

$existing = @(Get-Process -Name PhantomFudge -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $executable })
if ($existing.Count -gt 0) {
    $reveal = [System.Diagnostics.ProcessStartInfo]::new($executable)
    $reveal.UseShellExecute = $false
    $reveal.WorkingDirectory = $root
    $recoveryProcess = [System.Diagnostics.Process]::Start($reveal)
    Write-Output 'Requested reveal of the existing notes window; no build or service restart.'
    if ($PassThru) { Write-Output $recoveryProcess }
    return
}

if (-not $NoBuild -and -not $packaged) {
    & dotnet build $project --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Native application build failed.'
    }
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'No native build exists. Run this script without -NoBuild.'
}

if (-not $NotesOnly) {
    & (Join-Path $PSScriptRoot 'Start-Transcription.ps1')
}

$start = [System.Diagnostics.ProcessStartInfo]::new($executable)
$start.UseShellExecute = $false
$start.WorkingDirectory = $root
$tokenFile = Join-Path $root '.local\transcription.token'
if (Test-Path -LiteralPath $tokenFile -PathType Leaf) {
    $token = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
    if ($token.Length -lt 32) {
        throw 'Local service token is invalid. Re-run Initialize-Transcription.ps1.'
    }
    $start.Environment['PHANTOM_TRANSCRIPTION_TOKEN'] = $token
}
$start.Environment['PHANTOM_TRANSCRIPTION_URI'] = 'ws://127.0.0.1:8765/v1/transcribe'
$process = [System.Diagnostics.Process]::Start($start)
Write-Output ("Phantom Fudge process: {0}" -f $process.Id)
if ($PassThru) { Write-Output $process }
