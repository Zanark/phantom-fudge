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
$docs = Join-Path $directory 'docs'
$scripts = Join-Path $directory 'scripts'
$service = Join-Path $directory 'services\transcription'
New-Item -ItemType Directory -Path $docs,$scripts,$service -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'docs\PACKAGED-README.md') -Destination (Join-Path $directory 'README.md')
foreach ($name in @('PREVIEW-GUIDE.md','TRANSCRIPTION.md','CURSOR-AND-THEMES.md','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root "docs\$name") -Destination $docs
}
foreach ($name in @('Initialize-Transcription','Start-Transcription','Stop-Transcription','Run-PhantomFudge')) {
    $source = Join-Path $root "scripts\$name.ps1"
    Copy-Item -LiteralPath $source -Destination $scripts
    Copy-Item -LiteralPath $source -Destination (Join-Path $scripts "$name.txt")
}
foreach ($name in @('server.py','recognizer.py','manage.py','model.json','requirements.txt')) {
    Copy-Item -LiteralPath (Join-Path $root "services\transcription\$name") -Destination $service
}
Compress-Archive -Path (Join-Path $directory '*') -DestinationPath $archive
$hash = Get-FileHash -LiteralPath $archive -Algorithm SHA256
Write-Output ("Local unqualified preview: {0}" -f $archive)
Write-Output ("SHA256: {0}" -f $hash.Hash)
