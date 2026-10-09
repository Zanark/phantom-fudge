# Local diagnostics

The application now records bounded local diagnostic events automatically. The previous preview
did not retain a useful end-to-end request trace; its temporary provider directories were deleted
after each request and its generic speech log could be empty. Those old runs cannot be reconstructed
retroactively from new logging.

## Where to look

App logs:

```text
%LOCALAPPDATA%\PhantomFudge\diagnostics\events-*.jsonl
```

Speech-service metadata is separate, under the active service workspace:

```text
.local\diagnostics\transcription.jsonl
```

It records engine/model identity, decode/audio duration, source queues, pending finalizations,
fault/drop counts and lifecycle events. It rotates at1MiB with three backups and never includes
transcript text. `/v1/runtime` reports whether logging is available. The exporter includes only
these four allowlisted speech-log files from its own workspace, not models/tokens/PCM fixtures.
Backend records do not contain the app's capture/request correlation IDs. Correlate UTC times,
source and event stages cautiously; do not invent a shared identity or an exact cross-process join.

Click **Diagnostics** in the app to open that folder and reload diagnostic settings. This is an
explicit Explorer action outside the protected window, not an automatic popup during a meeting.
The app reports logging failures and dropped-record counts instead of pretending the trace is complete.

From the repository or packaged preview:

```powershell
.\scripts\Show-Diagnostics.ps1 -Last 10
.\scripts\Export-Diagnostics.ps1
```

The summary shows request IDs, provider, first text, UI text assignment, terminal time and result.
Synthetic UI-fixture requests are excluded by default; use `-IncludeSynthetic` only when examining
those fixtures. Their millisecond timings are not model-performance evidence.
The export is local, under `.local\diagnostic-exports`; **nothing is uploaded**. Text bodies are
excluded from the export unless explicitly requested with `-IncludeText`.

## Default contents

| Event family | Meaning |
| --- | --- |
| Application/configuration | Build fingerprint, process ID, safe configuration errors |
| Capture | Start/stop/fault, source, local session identity and elapsed time |
| Audio samples/levels | Delivered sample counts, clipping counts and RMS basis points |
| Transcript | Partial/final arrival, source and character count, not recognized words |
| Question/request | Frozen question character count, request ID and associated capture ID |
| Provider | Launch/version/readiness/model submission/first text/cleanup stages |
| UI answer | First received text, first text assigned to a visible control, completion/cancel/error |

`FirstAnswerVisible` currently means text assigned to the visible WPF control, not an independently
measured screen pixel or recipient frame. Provider timing and UI timing are deliberately separate.
Warm-up is not a model response and must not be excluded silently when reporting cold-start latency.

RMS values are recorded as `0..10000` basis points. Clipping counts are samples at or beyond
`±32760` in the delivered PCM16 stream. These are input-signal observations, not recognition-confidence
or proof of microphone quality. No audio waveform is saved by diagnostics.

## Text is an explicit local opt-in

Default logs contain **no transcript, question or answer bodies**, notes, token values, raw
provider stderr or audio. To enable bounded final transcript/question/answer text locally:

```powershell
.\scripts\Set-Diagnostics.ps1 -Mode Text
```

Click **Diagnostics** or restart the app to apply. Text mode writes plaintext conversation content;
do not enable it for sensitive meetings without considering that retention. It does not record
speaker audio, note documents, clipboard history or authentication data. Return to metadata-only:

```powershell
.\scripts\Set-Diagnostics.ps1 -Mode Metadata
```

Clearing a question/transcript in the UI does not erase diagnostic files already written. Remove
only this application's own log files when deliberately clearing retained diagnostics. Never share
a text-enabled export without reviewing it.

## Bounds and failure behavior

The app queues at most1024events without blocking the capture/UI caller. JSONL files rotate at2MiB
and retain at most10files in the app diagnostics folder. Oversized records or a full queue increment
dropped counts. IO/permission/flush failures are observable as safe status codes, not success-shaped
fallbacks. Exception diagnostics record type/method names, not messages containing user text or paths.

The CLI and model service may maintain their own data outside these app logs. App diagnostics are
not a promise of zero external retention, a full OS audit, or proof that a printed shell command
actually executed. The application distinguishes model text from tool events and does not execute
generated commands.

## Reproducing a problem

Record the app build, chosen input mode/device, ASR profile and configured answer model. For a
timing issue, retain the request ID and metadata export. For recognition quality, provide the exact
intended phrase and resulting transcript; synthetic speech can test a regression but cannot prove
how the user's microphone, accent or room behaved. Never claim to have heard audio from a text-only
report.
