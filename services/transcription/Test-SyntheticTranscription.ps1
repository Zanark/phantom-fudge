[CmdletBinding()]
param()

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
}
finally {
    $env:PHANTOM_TRANSCRIPTION_TOKEN_FILE = $oldTokenFile
    $speech.Dispose()
    $stream.Dispose()
}
