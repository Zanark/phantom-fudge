#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadDirectory,
    [Parameter(Mandatory)][string]$CompilerPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $output) { throw 'Fixture output already exists; use a fresh directory.' }
$payload = (Resolve-Path $PayloadDirectory).Path
$compiler = (Resolve-Path $CompilerPath).Path
$release = Get-Content (Join-Path $root 'tools\Installer\release.json') -Raw | ConvertFrom-Json
$id = 'PhantomFudge-Fixture-' + [Guid]::NewGuid().ToString('N')
$installed = Join-Path $output 'installed'
$uninstaller = Join-Path $installed 'unins000.exe'
$registry = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\${id}_is1"
New-Item -ItemType Directory -Path $output | Out-Null
$checks = [ordered]@{}

function Invoke-FixtureProcess([string]$File, [string[]]$Arguments, [int]$Expected = 0) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru
    if (!$process.WaitForExit(300000)) {
        $process.Kill($true)
        throw 'Owned installer fixture timed out; only that fixture process tree was stopped.'
    }
    if ($process.ExitCode -ne $Expected) { throw "Fixture expected exit $Expected, received $($process.ExitCode)." }
}

try {
    & $compiler /Q "/DPayloadDir=$payload" "/DAppVersion=$($release.version)" `
        "/DWindowsVersion=$($release.windowsVersion)" "/DInstallerId=$id" /DInstallerTest=1 "/O$output" `
        /FFixtureSetup (Join-Path $root 'tools\Installer\PhantomFudge.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Isolated installer fixture compilation failed.' }
    $setup = Join-Path $output 'FixtureSetup.exe'
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS',"/DIR=`"$installed`"")
    Invoke-FixtureProcess $setup ($arguments + "/LOG=`"$output\install.log`"")
    $checks.installRegistered = (Test-Path $registry) -and (Test-Path $uninstaller)
    $manifest = Get-Content (Join-Path $installed 'payload-files.json') -Raw | ConvertFrom-Json
    $sourceFiles = @(Get-ChildItem -LiteralPath $payload -Force -Recurse -File |
        ForEach-Object { [IO.Path]::GetRelativePath($payload, $_.FullName).Replace('\','/') })
    $listedFiles = @($manifest.files.path) + 'payload-files.json'
    if (Compare-Object ($sourceFiles | Sort-Object) ($listedFiles | Sort-Object)) {
        throw 'Payload inventory does not exactly cover the packaged files.'
    }
    foreach ($entry in $manifest.files) {
        $file = Join-Path $installed $entry.path
        if (!(Test-Path $file -PathType Leaf) -or (Get-Item $file).Length -ne $entry.bytes -or
            (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
            throw "Installed payload mismatch: $($entry.path)"
        }
    }
    $checks.installedFilesMatch = $true
    Invoke-FixtureProcess (Join-Path $installed 'launcher\PhantomFudge.Launcher.exe') @('--validate-install', "`"$installed`"")
    $checks.compactManifestAccepted = $true
    $sentinel = Join-Path $output 'user-note.txt'
    [IO.File]::WriteAllText($sentinel, 'Synthetic saved note outside the managed installation.')
    Invoke-FixtureProcess (Join-Path $installed 'launcher\PhantomFudge.Launcher.exe') @('--maintenance')
    $checks.idleMaintenance = $true
    Invoke-FixtureProcess $setup ($arguments + "/LOG=`"$output\repair.log`"")
    $checks.sameVersionRepair = (Test-Path $sentinel) -and
        ((Get-Content (Join-Path $installed 'app-version.txt') -Raw).Trim() -eq $release.windowsVersion)
    Move-Item (Join-Path $installed 'launcher\PhantomFudge.Launcher.exe') (Join-Path $installed 'launcher\PhantomFudge.Launcher.missing')
    [IO.File]::WriteAllText((Join-Path $installed 'app-version.txt'), 'damaged-version-record')
    Invoke-FixtureProcess $setup ($arguments + "/LOG=`"$output\damaged-repair.log`"")
    $checks.damagedLauncherRepaired = (Test-Path (Join-Path $installed 'launcher\PhantomFudge.Launcher.exe')) -and
        ((Get-Content (Join-Path $installed 'app-version.txt') -Raw).Trim() -eq $release.windowsVersion)
    [IO.File]::WriteAllText((Join-Path $installed 'app-version.txt'), '0.1.0.0')
    Invoke-FixtureProcess $setup ($arguments + "/LOG=`"$output\upgrade.log`"")
    $checks.upgradeVersionRecord = ((Get-Content (Join-Path $installed 'app-version.txt') -Raw).Trim() -eq $release.windowsVersion)
    [IO.File]::WriteAllText((Join-Path $installed 'app-version.txt'), '99.0.0.0')
    Invoke-FixtureProcess $setup ($arguments + "/LOG=`"$output\downgrade.log`"") 7
    $checks.downgradeRefused = ((Get-Content (Join-Path $installed 'app-version.txt') -Raw).Trim() -eq '99.0.0.0')
    [IO.File]::WriteAllText((Join-Path $installed 'app-version.txt'), $release.windowsVersion)
    Invoke-FixtureProcess $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',"/LOG=`"$output\uninstall.log`"")
    $checks.uninstallRemovedApplication = !(Test-Path (Join-Path $installed 'app\PhantomFudge.exe')) -and !(Test-Path $registry)
    $checks.savedFileRetained = (Get-Content $sentinel -Raw) -eq 'Synthetic saved note outside the managed installation.'
    $foreign = Join-Path $output 'unrecognized'
    New-Item -ItemType Directory -Path $foreign | Out-Null
    [IO.File]::WriteAllText((Join-Path $foreign 'keep.txt'), 'Unrelated content must survive.')
    Invoke-FixtureProcess $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS',"/DIR=`"$foreign`"", "/LOG=`"$output\foreign-directory.log`"") 7
    $checks.foreignDirectoryRefused = (Get-Content (Join-Path $foreign 'keep.txt') -Raw) -eq 'Unrelated content must survive.' -and !(Test-Path $registry)
    if ($checks.Values -contains $false) { throw 'An installer lifecycle assertion failed.' }
}
finally {
    if (Test-Path $uninstaller) {
        $launcher = Join-Path $installed 'launcher\PhantomFudge.Launcher.exe'
        if (!(Test-Path $launcher) -and (Test-Path (Join-Path $installed 'launcher\PhantomFudge.Launcher.missing'))) {
            Move-Item (Join-Path $installed 'launcher\PhantomFudge.Launcher.missing') $launcher
        }
        [IO.File]::WriteAllText((Join-Path $installed 'app-version.txt'), $release.windowsVersion)
        Invoke-FixtureProcess $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    }
}
$receipt = [ordered]@{
    passed = $true
    installerId = $id
    checks = $checks
    payloadFiles = $manifest.files.Count
    realAudioCaptured = $false
    modelRequests = 0
    limitation = 'Current Windows machine, isolated registry identity/path; shortcuts omitted; production Setup never auto-launches; simulated older version record, not a clean VM or binary-version compatibility matrix.'
}
$receipt | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $output 'result.json') -Encoding utf8NoBOM
Write-Output ($receipt | ConvertTo-Json -Depth 5 -Compress)
