#requires -Version 7.2
param(
    [ValidateRange(1,100)][int]$Last = 10,
    [switch]$IncludeSynthetic
)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PhantomFudge\diagnostics'
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw 'No app diagnostics exist yet. Start the updated app first.'
}
$records = [Collections.Generic.List[object]]::new()
$invalid = 0
foreach ($file in Get-ChildItem -LiteralPath $root -Filter 'events-*.jsonl' -File) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing redirected log files.' }
    $stream = [IO.FileStream]::new($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    $reader = [IO.StreamReader]::new($stream, [Text.UTF8Encoding]::new($false, $true))
    try {
        while ($null -ne ($line = $reader.ReadLine())) {
            try { $record = $line | ConvertFrom-Json -ErrorAction Stop }
            catch [ArgumentException] { $invalid++; continue }
            if ($record -and $record.Event) { $records.Add($record) }
        }
    } finally { $reader.Dispose() }
}
$starts = @($records | Where-Object {
    $_.Event -eq 'AnswerStarted' -and ($IncludeSynthetic -or $_.Code -eq 'user_ask')
} | Sort-Object Utc -Descending | Select-Object -First $Last)
if ($starts.Count -eq 0) {
    Write-Output "No answer requests recorded yet. Logs: $root"
} else {
    foreach ($start in $starts) {
        $request = @($records | Where-Object CorrelationId -eq $start.CorrelationId)
        $first = $request | Where-Object { $_.Event -eq 'FirstAnswerText' -and $_.Code -eq 'app_received' } | Select-Object -First 1
        $visible = $request | Where-Object Event -eq 'FirstAnswerVisible' | Select-Object -First 1
        $lastEvent = $request | Where-Object { $_.Event -in @('AnswerCompleted','AnswerFailed','AnswerCancelled') } |
            Sort-Object Utc | Select-Object -Last 1
        [pscustomobject]@{
            Utc = $start.Utc
            Provider = $start.Provider
            RunKind = $start.Code
            RequestId = $start.CorrelationId
            FirstTextMs = $first.ElapsedMs
            UiTextMs = $visible.ElapsedMs
            TerminalMs = $lastEvent.ElapsedMs
            Result = $lastEvent.Event
            Code = $lastEvent.Code
        }
    }
}
if ($invalid) { Write-Warning "$invalid incomplete/malformed lines were ignored; prompt/answer bodies are never displayed by this summary." }
