#requires -Version 7.2
param(
    [Parameter(Mandatory = $true)]
    [switch]$AllowPlayback
)

$ErrorActionPreference = 'Stop'
if (-not $AllowPlayback) {
    throw 'This test plays synthetic speech and captures all output on the selected default device. Explicit approval is required.'
}
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\PhantomFudge.Audio.Tests\PhantomFudge.Audio.Tests.csproj'
& dotnet build $project --configuration Release --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw 'Audio test harness build failed.'
}
Add-Type -AssemblyName System.Speech
$speech = [System.Speech.Synthesis.SpeechSynthesizer]::new()
$stream = [System.IO.MemoryStream]::new()
try {
    $speech.SelectVoice('Microsoft David Desktop')
    $speech.Rate = -1
    $format = [System.Speech.AudioFormat.SpeechAudioFormatInfo]::new(
        16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
        [System.Speech.AudioFormat.AudioChannel]::Mono)
    $speech.SetOutputToAudioStream($stream, $format)
    $speech.Speak('One two three four five. This is a local meeting transcription test.')
    $speech.SetOutputToNull()
    $start = [System.Diagnostics.ProcessStartInfo]::new('dotnet')
    $start.ArgumentList.Add((Join-Path $root 'tests\PhantomFudge.Audio.Tests\bin\Release\net10.0-windows\PhantomFudge.Audio.Tests.dll'))
    $start.ArgumentList.Add('--playback')
    $start.ArgumentList.Add('ws://127.0.0.1:8765/v1/transcribe')
    $start.ArgumentList.Add('--expect')
    $start.ArgumentList.Add('one two three four five')
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    $start.Environment['PHANTOM_AUDIO_TEST_TOKEN'] = (
        Get-Content -LiteralPath (Join-Path $root '.local\transcription.token') -Raw).Trim()
    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $stream.Position = 0
        $stream.CopyTo($process.StandardInput.BaseStream)
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(60000)) {
            Stop-Process -Id $process.Id
            throw 'Synthetic playback test exceeded its deadline.'
        }
        if ($process.ExitCode -ne 0) {
            throw 'Synthetic playback-loopback did not meet its expected result.'
        }
    }
    finally {
        $process.Dispose()
    }
}
finally {
    $speech.Dispose()
    $stream.Dispose()
}
