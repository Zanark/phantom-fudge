#requires -Version 7.2
param(
    [ValidateSet('Metadata','Text')][string]$Mode = 'Metadata'
)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PhantomFudge'
New-Item -ItemType Directory -Force $root | Out-Null
$temporary = Join-Path $root ("diagnostics-{0}.tmp" -f [Guid]::NewGuid().ToString('N'))
try {
    @{ includeText = ($Mode -eq 'Text') } | ConvertTo-Json |
        Set-Content -LiteralPath $temporary -Encoding utf8
    Move-Item -LiteralPath $temporary -Destination (Join-Path $root 'diagnostics.json') -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Output "Local diagnostics mode: $Mode. Click Diagnostics in the app or restart to apply."
if ($Mode -eq 'Text') {
    Write-Output 'Text mode retains final transcripts, submitted questions and answers locally in plaintext. No audio is recorded.'
} else {
    Write-Output 'Metadata mode records timings, counts, levels and safe errors; no transcript/question/answer bodies.'
}
