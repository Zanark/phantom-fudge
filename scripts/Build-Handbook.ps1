#requires -Version 7.2
param(
    [switch]$Check,
    [switch]$AcceptReviewedSource,
    [switch]$NoPdf
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'tools\Docs\build.mjs'
if (-not (Test-Path -LiteralPath (Join-Path $root 'tools\Docs\node_modules\mermaid'))) {
    throw 'Documentation dependencies are missing. Run npm ci --prefix .\tools\Docs first.'
}
$arguments = @($tool)
if ($Check) { $arguments += '--check' }
if ($AcceptReviewedSource) { $arguments += '--accept-reviewed-source' }
if ($NoPdf) { $arguments += '--no-pdf' }
& node @arguments
if ($LASTEXITCODE -ne 0) { throw 'Handbook build/freshness check failed.' }
