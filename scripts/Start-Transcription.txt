[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Docker CLI is missing. Install/configure a Linux-container Docker engine yourself, then rerun this script. There is no cloud or native fallback.'
}
& docker version --format '{{.Server.Version}}' 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Docker engine is unavailable. Start your Linux-container engine, then retry.' }
& docker compose version --short 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Docker Compose v2 is required. Setup did not install or change Docker.' }
if (-not (Test-Path -LiteralPath (Join-Path $Root '.local\transcription.token') -PathType Leaf)) {
    throw 'The local token is missing. Run scripts\Initialize-Transcription.ps1 -DownloadModel first.'
}
if (-not (Test-Path -LiteralPath (Join-Path $Root '.local\models\vosk-model-small-en-us-0.15\am\final.mdl') -PathType Leaf)) {
    throw 'The local model is missing. Run scripts\Initialize-Transcription.ps1 -DownloadModel first.'
}
& docker compose --project-directory $Root -f (Join-Path $Root 'compose.yaml') up --build --detach --wait --wait-timeout 120
if ($LASTEXITCODE -ne 0) { throw 'Local transcription did not become healthy. Inspect Docker status and local setup; no cloud fallback was attempted.' }
Write-Host 'Local Docker transcription is healthy at ws://127.0.0.1:8765/v1/transcribe. Start capture explicitly in the Windows app.'
