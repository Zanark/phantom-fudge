#requires -Version 7.2
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourceDirectory = Join-Path $root 'docs\handbook\assets\screenshots'
$destination = Join-Path $root 'docs\assets\readme'
$sourceManifest = Get-Content (Join-Path $sourceDirectory 'manifest.json') -Raw | ConvertFrom-Json
if (-not $sourceManifest.documentationFixture -or $sourceManifest.recordingStarted) {
    throw 'README images require the synthetic non-recording documentation fixture.'
}
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$variants = @(
    @{ source = 'transcript-deepseafoam.png'; file = 'transcript.png'; background = '#000F13' },
    @{ source = 'answer-synthetic.png'; file = 'question.png'; background = '#000F13' },
    @{ source = 'answer-context.png'; file = 'answer-context.png'; background = '#000F13' },
    @{ source = 'audio-setup.png'; file = 'audio-devices.png'; background = '#000F13' },
    @{ source = 'recording-active.png'; file = 'recording.png'; background = '#000F13' },
    @{ source = 'transcript-multiselect.png'; file = 'selection.png'; background = '#000F13' },
    @{ source = 'question-clarification.png'; file = 'clarification.png'; background = '#000F13' },
    @{ source = 'notepad-cover.png'; file = 'cover.png'; background = '#000F13' },
    @{ source = 'theme-DeepSeaFoam.png'; file = 'deepseafoam.png'; background = '#000F13' },
    @{ source = 'theme-HarborDaylight.png'; file = 'harbor-daylight.png'; background = '#F3F2E9' },
    @{ source = 'theme-Plain.png'; file = 'plain.png'; background = '#FAF9F6' }
)
$images = @()
foreach ($variant in $variants) {
    $source = Join-Path $sourceDirectory $variant.source
    $sourceHash = (Get-FileHash $source -Algorithm SHA256).Hash.ToLowerInvariant()
    $receipt = @($sourceManifest.images | Where-Object file -eq $variant.source)
    if ($receipt.Count -ne 1 -or $receipt[0].sha256 -ne $sourceHash) {
        throw "Source image provenance mismatch: $($variant.source)"
    }
    $output = Join-Path $destination $variant.file
    $original = [Drawing.Image]::FromFile($source)
    try {
        $bitmap = [Drawing.Bitmap]::new($original.Width, $original.Height)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([Drawing.ColorTranslator]::FromHtml($variant.background))
                $graphics.DrawImageUnscaled($original, 0, 0)
            } finally { $graphics.Dispose() }
            $bitmap.Save($output, [Drawing.Imaging.ImageFormat]::Png)
            $images += [ordered]@{
                file = "docs/assets/readme/$($variant.file)"
                sha256 = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
                source = "docs/handbook/assets/screenshots/$($variant.source)"
                sourceSha256 = $sourceHash
                background = $variant.background
                width = $bitmap.Width
                height = $bitmap.Height
            }
        } finally { $bitmap.Dispose() }
    } finally { $original.Dispose() }
}
[ordered]@{
    schemaVersion = 1
    kind = 'synthetic-wpf-render-composited-on-palette-background'
    limitation = 'Opaque README derivatives for legibility on GitHub; not desktop or capture-exclusion evidence.'
    images = $images
} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $destination 'manifest.json') -Encoding utf8
Write-Output "Exported $($images.Count) opaque README images from verified synthetic renders."
