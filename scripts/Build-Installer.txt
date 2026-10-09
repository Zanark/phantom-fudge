#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$RuntimeCache,
    [string]$Python,
    [switch]$OfflineRuntime,
    [switch]$SkipPortableArchive
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$release = Get-Content (Join-Path $root 'tools\Installer\release.json') -Raw | ConvertFrom-Json
if ($release.version -notmatch '^\d+\.\d+\.\d+-preview\.\d+$' -or
    $release.windowsVersion -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $release.runtime -ne 'win-x64') {
    throw 'Invalid reviewed release configuration.'
}
$compiler = (Resolve-Path -LiteralPath $CompilerPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Release output already exists; use a new directory.' }
if (!$RuntimeCache) { $RuntimeCache = Join-Path $root '.local\installer-runtime-cache' }
if (!$Python) {
    $developmentPython = Join-Path $root 'services\transcription\.venv\Scripts\python.exe'
    $Python = if (Test-Path $developmentPython -PathType Leaf) { $developmentPython } else { 'python' }
}
$payload = Join-Path $output 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null

$bundleArguments = @((Join-Path $root 'tools\Installer\bundle_runtime.py'), '--output', $payload,
    '--cache', $RuntimeCache, '--verify-synthetic-pcm')
if ($OfflineRuntime) { $bundleArguments += '--offline' }
& $Python @bundleArguments
if ($LASTEXITCODE -ne 0) { throw 'Portable runtime bundling failed. Partial output is not a release.' }
foreach ($project in @(
    @{ Path = 'src\PhantomFudge.App\PhantomFudge.App.csproj'; Target = 'app' },
    @{ Path = 'src\PhantomFudge.Launcher\PhantomFudge.Launcher.csproj'; Target = 'launcher' }
)) {
    & dotnet publish (Join-Path $root $project.Path) --configuration Release --runtime $release.runtime `
        --self-contained true --output (Join-Path $payload $project.Target) `
        "-p:Version=$($release.version)" "-p:FileVersion=$($release.windowsVersion)" `
        -p:DebugType=None -p:DebugSymbols=false --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Publication failed for $($project.Target)." }
}

$docs = Join-Path $payload 'docs'
New-Item -ItemType Directory -Path $docs -Force | Out-Null
foreach ($name in @('INSTALLATION.md','KEYBOARD-CONTROLS.md','CURRENT-WORKFLOW.md','THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root "docs\$name") -Destination $docs
}
Copy-Item -LiteralPath (Join-Path $root 'docs\handbook\Phantom-Fudge-Engineering-Handbook.pdf') -Destination $docs
Copy-Item -LiteralPath (Join-Path $root 'tools\Installer\INSTALLATION-NOTES.txt') -Destination $payload
[IO.File]::WriteAllText((Join-Path $payload 'app-installation.id'), 'phantom-fudge.install.v1', [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $payload 'app-version.txt'), $release.windowsVersion, [Text.Encoding]::ASCII)

$entries = @(Get-ChildItem -LiteralPath $payload -Force -Recurse)
if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
    throw 'Linked payload files or directories are not permitted.'
}
foreach ($entry in $entries) {
    $relative = [IO.Path]::GetRelativePath($payload, $entry.FullName).Replace('\','/')
    if ($relative -match '(^|/)(\.local|\.agent-context|\.git|\.venv|__pycache__)(/|$)') {
        throw "Private payload directory is not permitted: $relative"
    }
}
$files = @($entries | Where-Object { !$_.PSIsContainer })
$inventory = foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\','/')
    if ($relative -match '(^|/)(\.local|\.agent-context|\.git|\.venv|__pycache__)(/|$)' -or
        $file.Name -match '^(transcription\.token|answers\.json|keyboard\.json|diagnostics\.json|.*\.log)$' -or
        ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Unexpected private or linked payload file: $relative"
    }
    [ordered]@{ path = $relative; bytes = $file.Length; sha256 = (Get-FileHash $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$commit = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the source revision.' }
$dirty = @(& git -C $root status --porcelain --untracked-files=normal).Count -gt 0
$manifest = [ordered]@{
    schemaVersion = 1
    product = 'Phantom Fudge'
    version = $release.version
    windowsVersion = $release.windowsVersion
    runtime = $release.runtime
    sourceCommit = $commit
    sourceDirty = $dirty
    signed = $false
    minimumWindowsBuild = $release.minimumWindowsBuild
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $payload 'install-manifest.json') -Encoding utf8NoBOM
$manifestFile = Get-Item (Join-Path $payload 'install-manifest.json')
$inventory += [ordered]@{
    path = 'install-manifest.json'
    bytes = $manifestFile.Length
    sha256 = (Get-FileHash $manifestFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
[ordered]@{ schemaVersion = 1; files = @($inventory | Sort-Object path) } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $payload 'payload-files.json') -Encoding utf8NoBOM

& $compiler /Qp "/DPayloadDir=$payload" "/DAppVersion=$($release.version)" `
    "/DWindowsVersion=$($release.windowsVersion)" "/O$output" (Join-Path $root 'tools\Installer\PhantomFudge.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed; no release may be published.' }
$installer = Join-Path $output "PhantomFudge-$($release.version)-win-x64-Setup.exe"
if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Compiler did not produce the expected installer.' }
if (!$SkipPortableArchive) {
    $archive = Join-Path $output "PhantomFudge-$($release.version)-win-x64-Portable.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
}
$assets = @(Get-ChildItem -LiteralPath $output -File | Where-Object Extension -in '.exe','.zip')
$checksums = foreach ($asset in $assets) {
    '{0}  {1}' -f (Get-FileHash $asset.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $asset.Name
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $checksums, [Text.UTF8Encoding]::new($false))
Write-Output "Unsigned Windows 11 x64 preview built: $output"
Write-Output "Source revision: $commit; uncommitted changes: $dirty"
Write-Output 'Synthetic PCM exercised local speech models; no audio endpoint, answer provider or installer was started.'
