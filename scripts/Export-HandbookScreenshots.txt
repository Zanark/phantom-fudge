#requires -Version 7.2
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root '.local\handbook-capture-build\'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$capture = Join-Path $root ".local\handbook-capture-$stamp"
$destination = Join-Path $root 'docs\handbook\assets\screenshots'
$project = Join-Path $root 'src\PhantomFudge.App\PhantomFudge.App.csproj'

& dotnet build $project -c Release "-p:OutputPath=$build" --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Documentation fixture build failed.' }
$exe = Join-Path $build 'PhantomFudge.exe'
$process = Start-Process -FilePath $exe -ArgumentList @(
    '--documentation-screenshots', ('"' + $capture + '"')) -PassThru
if (-not $process.WaitForExit(45000)) {
    Stop-Process -Id $process.Id
    throw 'Documentation fixture timed out; only its own process was stopped.'
}
$result = Get-Content -LiteralPath (Join-Path $capture 'result.json') -Raw | ConvertFrom-Json
if ($process.ExitCode -ne 0 -or -not $result.passed -or -not $result.documentationFixture -or $result.recordingStarted) {
    throw 'Documentation fixture failed or attempted recording; no images imported.'
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$names = @(
    'transcript-deepseafoam.png', 'theme-DeepSeaFoam.png', 'theme-HarborDaylight.png',
    'theme-Plain.png', 'audio-setup.png', 'notes-expanded.png',
    'background-10.png', 'background-100.png', 'present.png',
    'question-selection.png', 'question-draft.png', 'question-harbordaylight.png', 'answer-synthetic.png',
    'answer-background-10.png', 'answer-background-100.png', 'transcript-multiselect.png', 'question-clarification.png', 'recording-active.png', 'audio-setup-cards-compact.png', 'notepad-cover.png',
    'keyboard-selection.png', 'keyboard-command.png', 'keyboard-answer-size.png'
)
$images = @()
foreach ($name in $names) {
    $source = Join-Path $capture $name
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing documentation image: $name" }
    Copy-Item -LiteralPath $source -Destination $destination
    $bytes = [IO.File]::ReadAllBytes($source)
    $widthBytes = $bytes[16..19]
    $heightBytes = $bytes[20..23]
    [Array]::Reverse($widthBytes)
    [Array]::Reverse($heightBytes)
    $images += [ordered]@{
        file = $name
        sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
        width = [BitConverter]::ToInt32($widthBytes, 0)
        height = [BitConverter]::ToInt32($heightBytes, 0)
        kind = 'actual-wpf-visual-tree-synthetic-fixture'
    }
}
$sourceFiles = @(
    'src\PhantomFudge.App\App.xaml', 'src\PhantomFudge.App\App.xaml.cs',
    'src\PhantomFudge.App\MainWindow.xaml', 'src\PhantomFudge.App\MainWindow.xaml.cs',
    'src\PhantomFudge.App\MainWindow.Questions.cs', 'src\PhantomFudge.App\MainWindow.QuestionDiagnostics.cs',
    'src\PhantomFudge.App\MainWindow.TranscriptSelection.cs', 'src\PhantomFudge.App\MainWindow.TranscriptSelectionDiagnostics.cs',
    'src\PhantomFudge.App\MainWindow.Clarification.cs', 'src\PhantomFudge.App\MainWindow.ClarificationDiagnostics.cs',
    'src\PhantomFudge.App\MainWindow.RecordingIndicator.cs',
    'src\PhantomFudge.App\MainWindow.AudioDeviceCards.cs', 'src\PhantomFudge.App\MainWindow.AudioDeviceCardsDiagnostics.cs',
    'src\PhantomFudge.App\MainWindow.Cover.cs', 'src\PhantomFudge.App\MainWindow.CoverDiagnostics.cs',
    'src\PhantomFudge.App\MainWindow.Keyboard.cs', 'src\PhantomFudge.App\MainWindow.KeyboardSelection.cs',
    'src\PhantomFudge.App\MainWindow.KeyboardDevices.cs', 'src\PhantomFudge.App\MainWindow.KeyboardDiagnostics.cs',
    'src\PhantomFudge.App\MainWindow.AnswerFont.cs', 'src\PhantomFudge.App\Native\WindowHotkeys.cs',
    'src\PhantomFudge.Core\KeyboardCommands.cs', 'src\PhantomFudge.Core\KeyboardShortcutConfiguration.cs',
    'src\PhantomFudge.App\MainWindow.Answers.cs', 'src\PhantomFudge.App\MainWindow.AnswerDiagnostics.cs',
    'src\PhantomFudge.App\MainWindow.Layout.cs', 'src\PhantomFudge.App\MainWindow.AnswerLifetime.cs',
    'src\PhantomFudge.App\MainWindow.Diagnostics.cs', 'src\PhantomFudge.Core\WorkspaceLayout.cs',
    'src\PhantomFudge.Core\StructuredTranscriptStore.cs', 'src\PhantomFudge.Core\QuestionDraft.cs',
    'src\PhantomFudge.Core\TranscriptSegment.cs', 'src\PhantomFudge.Core\QuestionSnapshot.cs',
    'src\PhantomFudge.App\ThemeBrushes.cs', 'src\PhantomFudge.Core\ThemePalette.cs',
    'src\PhantomFudge.Core\AudioSource.cs', 'src\PhantomFudge.Core\TranscriptBuffer.cs',
    'src\PhantomFudge.App\Native\WindowProtection.cs', 'src\PhantomFudge.App\Native\CursorPolicy.cs'
)
$hashes = [ordered]@{}
foreach ($relative in $sourceFiles) {
    $hashes[$relative.Replace('\', '/')] = (Get-FileHash -LiteralPath (Join-Path $root $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    captureKind = 'WPF RenderTargetBitmap of actual controls using synthetic text and device labels; not a desktop or screen-sharing capture'
    recordingStarted = $false
    documentationFixture = $true
    appAssemblySha256 = (Get-FileHash -LiteralPath (Join-Path $build 'PhantomFudge.dll') -Algorithm SHA256).Hash.ToLowerInvariant()
    sourceFiles = $hashes
    images = $images
    limitations = @(
        'No user notes, transcripts, token values or actual device names are included.',
        'No microphone/output endpoint is opened and no speech service/cloud request is started.',
        'These images establish UI appearance only, not capture exclusion or recorder compatibility.'
    )
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
Write-Output ("Imported {0} synthetic application images with provenance." -f $images.Count)
& (Join-Path $PSScriptRoot 'Export-ReadmeImages.ps1')
