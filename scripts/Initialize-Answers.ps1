#requires -Version 7.2
param(
    [Parameter(Mandatory)][ValidateSet('SnapshotOnly','Agency','Copilot','LocalModel')][string]$Provider,
    [string]$CliPath,
    [string]$AgencyPath,
    [string]$Model,
    [string]$LocalEndpoint,
    [switch]$Connect
)

$ErrorActionPreference = 'Stop'
if ($Connect -and $Provider -ne 'Copilot') { throw '-Connect is only for the Copilot cloud provider.' }
if ($Model -and ($Model.Length -gt 128 -or $Model -notmatch '^[A-Za-z0-9_.:/-]+$')) {
    throw 'Model must be an identifier, not CLI arguments.'
}
if ($Provider -eq 'LocalModel') {
    if (-not $Model) { throw 'LocalModel requires -Model with an installed model identifier.' }
    if ($LocalEndpoint -notmatch '^http://(127\.0\.0\.1|\[::1\])(:[0-9]{1,5})?/v1/?$') {
        throw 'Use an explicit loopback compatibility endpoint such as http://127.0.0.1:11434/v1.'
    }
} elseif ($LocalEndpoint) { throw '-LocalEndpoint is only for LocalModel.' }

$root = Split-Path -Parent $PSScriptRoot
$state = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PhantomFudge'
$profile = Join-Path $state 'copilot-profile'
New-Item -ItemType Directory -Force $state | Out-Null
$resolvedCli = $null
$resolvedAgency = $null
if ($Provider -ne 'SnapshotOnly') {
    $app = Join-Path $root 'PhantomFudge.exe'
    if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
        $build = Join-Path $root '.local\answer-setup-build\'
        & dotnet build (Join-Path $root 'src\PhantomFudge.App\PhantomFudge.App.csproj') -c Release "-p:OutputPath=$build" --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Answer setup build failed.' }
        $app = Join-Path $build 'PhantomFudge.exe'
    }
    $chosenPath = if ($Provider -eq 'Agency') { $AgencyPath } else { $CliPath }
    $choice = if ($chosenPath) { $chosenPath } else { 'auto' }
    $start = [Diagnostics.ProcessStartInfo]::new($app)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add($(if ($Provider -eq 'Agency') { '--prepare-agency' } else { '--prepare-answer-profile' }))
    $start.ArgumentList.Add($choice)
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            throw 'Owned answer setup timed out.'
        }
        if ($process.ExitCode -ne 0) {
            throw 'Executable/profile preparation failed. Use a trusted compatible installed executable; do not adopt a personal CLI profile.'
        }
    } finally { $process.Dispose() }
    $receipt = Get-Content (Join-Path $state 'answer-setup.json') -Raw | ConvertFrom-Json
    if ($Provider -eq 'Agency') { $resolvedAgency = $receipt.agencyPath } else { $resolvedCli = $receipt.cliPath }
    $resolved = if ($Provider -eq 'Agency') { $resolvedAgency } else { $resolvedCli }
    if (-not $resolved -or -not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw 'Profile preparation did not return an installed executable.'
    }
}

if ($Connect) {
    Write-Output 'Explicit Copilot sign-in uses the app profile. It may open a browser; no question is submitted.'
    $login = [Diagnostics.ProcessStartInfo]::new($resolvedCli)
    $login.UseShellExecute = $false
    $login.WorkingDirectory = $profile
    foreach ($name in @($login.Environment.Keys)) {
        if ($name -match '^(COPILOT_|GITHUB_|GH_|OTEL_|NODE_|NPM_|VSCODE_|AGENCY_)' -or $name -in @('BASH_ENV','ENV')) {
            $login.Environment.Remove($name) | Out-Null
        }
    }
    $login.Environment['COPILOT_HOME'] = $profile
    $login.Environment['HOME'] = $profile
    $login.Environment['USERPROFILE'] = $profile
    $login.Environment['COPILOT_AUTO_UPDATE'] = 'false'
    $login.ArgumentList.Add('--no-auto-update')
    $login.ArgumentList.Add('login')
    $process = [Diagnostics.Process]::Start($login)
    try {
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw 'Copilot sign-in did not complete; answer settings were not enabled.' }
    } finally { $process.Dispose() }
}

$configuration = [ordered]@{
    mode = $Provider
    cliPath = $resolvedCli
    model = if ($Model) { $Model } else { $null }
    localEndpoint = if ($Provider -eq 'LocalModel') { $LocalEndpoint } else { $null }
    agencyPath = $resolvedAgency
}
$path = Join-Path $state 'answers.json'
$temporary = Join-Path $state ("answers-{0}.tmp" -f [Guid]::NewGuid().ToString('N'))
try {
    $configuration | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding utf8
    Move-Item -LiteralPath $temporary -Destination $path -Force
} finally {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
}
Write-Output "Configured $Provider. Open Question and use Reload setup. No model request was sent."
if ($Provider -eq 'Copilot' -and -not $Connect) {
    Write-Output 'If this dedicated profile is not signed in, rerun with -Connect before asking a question.'
}
