#requires -Version 7.2
param(
    [switch]$IncludeText
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$source = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PhantomFudge\diagnostics'
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'No local application diagnostics exist yet. Run the updated application first.'
}
$destination = Join-Path $repo ('.local\diagnostic-exports\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
if (Test-Path -LiteralPath $destination) { throw 'This export already exists; retry in a second.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$keys = @('Utc','MonotonicTicks','ProcessId','Event','CorrelationId','RelatedId','ElapsedMs','Count','Code','Provider','ContentKind')
if ($IncludeText) { $keys += 'Text' }
$count = 0
$backendCount = 0
$invalid = 0
foreach ($file in Get-ChildItem -LiteralPath $source -Filter 'events-*.jsonl' -File) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing redirected diagnostic files.' }
    $output = Join-Path $destination $file.Name
    $writer = [IO.StreamWriter]::new($output, $false, [Text.UTF8Encoding]::new($false))
    $stream = [IO.FileStream]::new($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false, $true))
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            try { $record = $line | ConvertFrom-Json -ErrorAction Stop }
            catch [ArgumentException] { $invalid++; continue }
            if ($null -eq $record -or -not ($record.PSObject.Properties.Name -contains 'Event')) {
                $invalid++
                continue
            }
            $safe = [ordered]@{}
            foreach ($key in $keys) {
                if ($record.PSObject.Properties.Name -contains $key) { $safe[$key] = $record.$key }
            }
            $writer.WriteLine(($safe | ConvertTo-Json -Compress -Depth 5))
            $count++
        }
    } finally { $reader.Dispose(); $writer.Dispose() }
}
$backend = Join-Path $repo '.local\diagnostics'
$backendKeys = @('schema_version','time_utc','event','source','engine','model','fault',
    'decode_ms','audio_ms','queue_bytes','queue_frames','pending_finals','fault_count','drop_count')
foreach ($name in @('transcription.jsonl','transcription.jsonl.1','transcription.jsonl.2','transcription.jsonl.3')) {
    $path = Join-Path $backend $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
    if ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Refusing redirected backend logs.'
    }
    $stream = [IO.FileStream]::new($path, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false, $true))
    $writer = [IO.StreamWriter]::new((Join-Path $destination $name), $false, [Text.UTF8Encoding]::new($false))
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            try { $record = $line | ConvertFrom-Json -ErrorAction Stop }
            catch [ArgumentException] { $invalid++; continue }
            if ($null -eq $record -or -not ($record.PSObject.Properties.Name -contains 'event')) {
                $invalid++
                continue
            }
            $safe = [ordered]@{}
            foreach ($key in $backendKeys) {
                if ($record.PSObject.Properties.Name -contains $key) { $safe[$key] = $record.$key }
            }
            $writer.WriteLine(($safe | ConvertTo-Json -Compress))
            $backendCount++
        }
    } finally { $reader.Dispose(); $writer.Dispose() }
}
@{
    exportedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    records = $count
    backendRecords = $backendCount
    skippedMalformedOrIncompleteLines = $invalid
    textIncluded = [bool]$IncludeText
    uploaded = $false
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8
Write-Output "Exported $count app and $backendCount speech records to $destination. Nothing was uploaded."
if ($invalid) { Write-Warning "$invalid malformed or actively-written lines were skipped and recorded in the manifest." }
if ($IncludeText) { Write-Warning 'This export can contain private conversation text. Review it before sharing.' }
