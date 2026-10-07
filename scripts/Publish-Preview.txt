#requires -Version 7.2
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\PhantomFudge.App\PhantomFudge.App.csproj'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$name = "phantom-fudge-0.1.0-preview-$Runtime-$stamp"
$directory = Join-Path $root "artifacts\$name"
$archive = "$directory.zip"
if ((Test-Path -LiteralPath $directory) -or (Test-Path -LiteralPath $archive)) {
    throw 'Preview artifact already exists; refusing to overwrite it.'
}
& dotnet publish $project --configuration Release --runtime $Runtime --self-contained true `
    --output $directory -p:DebugType=None -p:DebugSymbols=false --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw 'Preview publication failed. Partial output is not a release candidate.'
}
Copy-Item -LiteralPath (Join-Path $root 'docs\PREVIEW-GUIDE.md') -Destination $directory
Copy-Item -LiteralPath (Join-Path $root 'docs\TRANSCRIPTION.md') -Destination $directory
Copy-Item -LiteralPath (Join-Path $root 'docs\THIRD-PARTY-NOTICES.md') -Destination $directory
Compress-Archive -Path (Join-Path $directory '*') -DestinationPath $archive
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
Write-Output ("Local unqualified preview: {0}" -f $archive)
Write-Output ("SHA256: {0}" -f $hash.Hash)
