#requires -Version 7.2
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PhantomFudge.App\PhantomFudge.App.csproj'
$executable = Join-Path $root 'src\PhantomFudge.App\bin\Release\net10.0-windows\PhantomFudge.exe'

if (-not $NoBuild) {
    & dotnet build $project --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Native application build failed.'
    }
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw 'No native build exists. Run this script without -NoBuild.'
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
