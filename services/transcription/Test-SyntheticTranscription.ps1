[CmdletBinding()]
param(
    [switch]$NativeClient
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Python = Join-Path $PSScriptRoot '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $Python)) { throw 'Create the service-local Python venv and install requirements first.' }
Add-Type -AssemblyName System.Speech
$speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
$stream = New-Object IO.MemoryStream
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    16000,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono
)
$oldTokenFile = $env:PHANTOM_TRANSCRIPTION_TOKEN_FILE
try {
    $speech.SelectVoice('Microsoft David Desktop')
    $speech.Rate = -1
    $speech.SetOutputToAudioStream($stream, $format)
    $speech.Speak('One two three four five. This is a local speech recognition test.')
    $speech.SetOutputToNull()
    $env:PHANTOM_TRANSCRIPTION_TOKEN_FILE = Join-Path $Root '.local\transcription.token'
    # PCM remains in process memory and stdin; no playback, WAV, or transcript file.
    [Convert]::ToBase64String($stream.ToArray()) | & $Python (Join-Path $PSScriptRoot 'tests\real_model_check.py')
    if ($LASTEXITCODE -ne 0) { throw 'Real-model synthetic validation failed. No real audio was used.' }
    if ($NativeClient) {
        $project = Join-Path $Root 'tests\PhantomFudge.Audio.Tests\PhantomFudge.Audio.Tests.csproj'
        & dotnet build $project --configuration Release --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'C# audio transport test build failed.' }
        $info = New-Object Diagnostics.ProcessStartInfo
        $info.FileName = 'dotnet'
        $testDll = Join-Path $Root 'tests\PhantomFudge.Audio.Tests\bin\Release\net10.0-windows\PhantomFudge.Audio.Tests.dll'
        $info.Arguments = '"' + $testDll + '" --service ws://127.0.0.1:8765/v1/transcribe --synthetic-pcm - --expect "one two three four five"'
        $info.UseShellExecute = $false
        $info.RedirectStandardInput = $true
        $info.EnvironmentVariables['PHANTOM_AUDIO_TEST_TOKEN'] = (
            Get-Content -LiteralPath (Join-Path $Root '.local\transcription.token') -Raw).Trim()
        $process = [Diagnostics.Process]::Start($info)
        try {
            $stream.Position = 0
            $stream.CopyTo($process.StandardInput.BaseStream)
            $process.StandardInput.Close()
            if (-not $process.WaitForExit(60000)) {
                Stop-Process -Id $process.Id
                throw 'Synthetic C# client test exceeded its deadline.'
            }
            if ($process.ExitCode -ne 0) { throw 'C# client to native Python recognition failed.' }
        }
        finally { $process.Dispose() }
    }
}
finally {
    $env:PHANTOM_TRANSCRIPTION_TOKEN_FILE = $oldTokenFile
    $speech.Dispose()
    $stream.Dispose()
}
