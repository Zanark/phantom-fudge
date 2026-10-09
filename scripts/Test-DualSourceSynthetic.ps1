[CmdletBinding()]
param(
    [switch]$NativeClient,
    [switch]$Accurate
)

$ErrorActionPreference = 'Stop'
if ($Accurate -and -not $NativeClient) { throw '-Accurate requires -NativeClient.' }
$Root = Split-Path -Parent $PSScriptRoot
$Python = Join-Path $Root 'services\transcription\.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $Python)) { throw 'Prepare the local Python environment first.' }
Add-Type -AssemblyName System.Speech
$speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono)
$fixture = [ordered]@{}
try {
    $speech.SelectVoice('Microsoft David Desktop')
    $speech.Rate = -1
    foreach ($entry in @(
        @{ Source = 'computer'; Text = 'The project meeting starts at nine. Please review the project plan.' },
        @{ Source = 'microphone'; Text = 'I have a question. One two three four five.' }
    )) {
        $stream = New-Object IO.MemoryStream
        try {
            $speech.SetOutputToAudioStream($stream, $format)
            $speech.Speak($entry.Text)
            $speech.SetOutputToNull()
            $fixture[$entry.Source] = [Convert]::ToBase64String($stream.ToArray())
        }
        finally { $stream.Dispose() }
    }
    $probe = Join-Path $Root 'tools\DualSourceProbe\probe.py'
    if ($NativeClient) {
        $project = Join-Path $Root 'tests\PhantomFudge.Audio.Tests\PhantomFudge.Audio.Tests.csproj'
        & dotnet build $project -c Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Native dual-source test client build failed.' }
        $client = Join-Path $Root 'tests\PhantomFudge.Audio.Tests\bin\Release\net10.0-windows\PhantomFudge.Audio.Tests.dll'
        $probeArguments = @($probe, '--native-client', $client)
        if ($Accurate) { $probeArguments += '--accurate' }
        $fixture | ConvertTo-Json -Compress | & $Python @probeArguments
    }
    else {
        $fixture | ConvertTo-Json -Compress | & $Python $probe
    }
    if ($LASTEXITCODE -ne 0) { throw 'Synthetic dual-source recognition failed. No real endpoint was used.' }
}
finally {
    $speech.Dispose()
}
