[CmdletBinding()]
param(
    [string]$ModelPath
)

$ErrorActionPreference = 'Stop'
$Service = Split-Path -Parent $PSScriptRoot
$Root = Split-Path -Parent (Split-Path -Parent $Service)
$Python = Join-Path $Service '.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $Python)) { throw 'The existing service-local Python environment is required.' }
if (-not $ModelPath) { $ModelPath = Join-Path $Root '.local\models\vosk-model-small-en-us-0.15' }
if (-not (Test-Path -LiteralPath $ModelPath -PathType Container)) { throw 'The verified cached model is required; this test never downloads one.' }

Add-Type -AssemblyName System.Speech
$speech = New-Object System.Speech.Synthesis.SpeechSynthesizer
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(
    16000,
    [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen,
    [System.Speech.AudioFormat.AudioChannel]::Mono
)
$phrases = [ordered]@{
    computer = 'One two three four five. This is a local speech recognition test.'
    microphone = 'Six seven eight nine ten. Please write down my question about the weather.'
}
$fixtures = [ordered]@{}
$oldNoBytecode = $env:PYTHONDONTWRITEBYTECODE
try {
    $speech.SelectVoice('Microsoft David Desktop')
    $speech.Rate = -1
    foreach ($source in $phrases.Keys) {
        $stream = New-Object IO.MemoryStream
        try {
            $speech.SetOutputToAudioStream($stream, $format)
            $speech.Speak($phrases[$source])
            $speech.SetOutputToNull()
            $silence = New-Object byte[] 32000
            $stream.Write($silence, 0, $silence.Length)
            $fixtures[$source] = [Convert]::ToBase64String($stream.ToArray())
        }
        finally { $stream.Dispose() }
    }
    $env:PYTHONDONTWRITEBYTECODE = '1'
    # No capture, playback, real token, audio file, or persistent service is used.
    $fixtures | ConvertTo-Json -Compress |
        & $Python (Join-Path $PSScriptRoot 'real_dual_model_check.py') $ModelPath
    if ($LASTEXITCODE -ne 0) { throw 'Real-model dual-source synthetic validation failed.' }
}
finally {
    $env:PYTHONDONTWRITEBYTECODE = $oldNoBytecode
    $speech.Dispose()
}
