[CmdletBinding()]
param(
    [switch]$Docker
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if ($Docker) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root 'compose.yaml') -PathType Leaf)) {
        throw 'Optional Docker recipes are provided in the source checkout. No native service was changed.'
    }
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw 'Docker is unavailable.' }
    & docker compose --project-directory $Root -f (Join-Path $Root 'compose.yaml') down
    if ($LASTEXITCODE -ne 0) { throw 'Optional Docker transcription did not stop cleanly.' }
    return
}
$Python = Join-Path $Root 'services\transcription\.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $Python -PathType Leaf)) {
    throw 'The local Python runtime is missing. No process was stopped.'
}
& $Python (Join-Path $Root 'services\transcription\manage.py') stop
if ($LASTEXITCODE -ne 0) { throw 'Local transcription was not stopped. Stop active capture in the app, then retry.' }
