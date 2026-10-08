[CmdletBinding()]
param(
    [switch]$Docker
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not $Docker) {
    $Python = Join-Path $Root 'services\transcription\.venv\Scripts\python.exe'
    if (-not (Test-Path -LiteralPath $Python -PathType Leaf)) {
        throw 'Local Python runtime is missing. Run Initialize-Transcription.ps1 -DownloadModel first. Docker is not required.'
    }
    & $Python (Join-Path $Root 'services\transcription\manage.py') start
    if ($LASTEXITCODE -ne 0) { throw 'Local Python transcription did not start. No microphone or cloud fallback was used.' }
    return
}

if (-not (Test-Path -LiteralPath (Join-Path $Root 'compose.yaml') -PathType Leaf)) {
    throw 'Optional Docker recipes are provided in the source checkout. Omit -Docker for normal native Python operation.'
}
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw 'Optional Docker mode requires a running Linux-container Docker engine. Omit -Docker to use normal native Python transcription.'
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
Write-Host 'Optional Docker transcription is healthy at ws://127.0.0.1:8765/v1/transcribe. Start capture explicitly in the Windows app.'
