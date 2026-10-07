# Running the local development preview

This is an **unqualified Windows 11 x64 preview**, not a certified private meeting tool.
Rehearse with synthetic text and a separate recipient before using sensitive notes.
The app remains visible to normal OS/process/accessibility inspection.

## Native app

From the repository in PowerShell 7, with the SDK selected by `global.json`:

```powershell
dotnet test .\tests\PhantomFudge.Core.Tests\PhantomFudge.Core.Tests.csproj -c Release
.\scripts\Run-PhantomFudge.ps1
```

The launcher builds the native WPF app and passes a local service token from
`.local\transcription.token` if configured. It never prints that token.
The app can edit notes without the transcription service. Development builds need
the .NET 10 Desktop Runtime; an explicitly published self-contained ZIP includes its runtime.

If a packaged preview is supplied, extract the WHOLE directory and run `PhantomFudge.exe`.
Do not copy just its EXE away from its dependent files. An unsigned preview may be blocked
by Windows or organizational policy; do not disable security controls to bypass that.

## Notes and recovery

- Drag the quiet header and resize at the edges. The text area remains selectable/editable.
- Use New/Open/Save/Save as; only UTF-8 `.txt` up to 1 MiB is supported in this preview.
- No note autosave or crash recovery is enabled. Save explicitly; an unexpected process crash
  can lose unsaved notes and transcripts.
- Ctrl+S/O/N/F provide save/open/new/find. A-/A+ adjust font size; Wrap changes wrapping.
- Ctrl+Alt+Space hides/reveals; Ctrl+Alt+E returns to Edit. A conflicting global shortcut is
  reported; relaunching the app asks the existing instance to reveal and recenter.
- The app deliberately has no ordinary taskbar/Alt-Tab entry. Hide is not Exit.
- Unsaved content is handled by an inline confirmation, not a note-bearing system popup.

Present mode is read-only/non-activating and requires the rehearsal acknowledgment in Audio
setup. Switching modes, hiding, lock/suspend or protection invalidation stops transcription.
Start again deliberately after checking the new configuration. Normal dialogs/export are
disabled during Present. The current preview uses fixed shortcut defaults.

## Local transcription and Docker

The native app captures **the selected playback device**, not a microphone. It includes other
apps and any sidetone already audible on that device. Match it to the meeting application's
headphone/speaker output. No process-specific meeting filter is implied.

Follow `services\transcription\README.md` in the source checkout for model provisioning and the
Docker service. `Initialize-Transcription.ps1` creates local model/token inputs;
`Start-Transcription.ps1` starts the Docker service when an engine is available.
Windows GUI and WASAPI stay on the host; the headless speech recognizer is containerized.

Use the repository launcher so the token is passed automatically, or enter the local token
in the session-only Audio setup field. The endpoint is literal loopback
`ws://127.0.0.1:8765/v1/transcribe`; there is no cloud speech fallback.

Open Audio setup, select the playback device, confirm that you have permission to transcribe,
and click Start. Final lines accumulate in the transcript pane; the italic unfinished line
may change until finalized. Stop flushes remaining recognition and releases the capture device.
The output meter measures playback, not a microphone.

Audio is processed in memory and is not recorded to a file. Transcripts are retained in memory
and written only through Export. Clear requires export of unsaved transcript content; Exit has
an explicit discard choice. Recognition is machine-generated and can be wrong, particularly
names, technical terms, accents and overlapping speakers. No diarization is promised.

If the service/model/token/device is unavailable, the app reports failure instead of opening
a microphone or uploading audio. A full audio queue stops transcription rather than silently
dropping speech. An interrupted connection can lose the unfinished tail; that is reported.

## Synthetic diagnostics

These paths use generated fixture text, not personal notes or a live meeting:

```powershell
dotnet run --project .\spikes\CaptureProof -c Release -- .\.local\capture-proof
.\src\PhantomFudge.App\bin\Release\net10.0-windows\PhantomFudge.exe --ui-smoke .\.local\ui-smoke
.\scripts\Test-NativeLifecycle.ps1
```

The first records cropped GDI capture samples of a synthetic overlay/backdrop. The second
renders its own synthetic WPF visual tree for layout/state inspection. The latter intentionally
does not test capture exclusion and starts no audio. Neither certifies any meeting application.
The lifecycle test starts only its own blank instance and exercises minimized/hidden recovery
and native close. It refuses to touch an already running PhantomFudge process.

## Current qualification boundary

Teams, Zoom, Meet, OBS, alternate GPUs/DPI/headsets, startup-frame completeness, actual meeting
transcription accuracy, and clean-machine Docker behavior require their own recorded outcomes.
Read `docs\QUALIFICATION.md` and the TODO index for the current evidence and remaining gates.

Screen affinity does not encrypt disk files, protect clipboard history, hide a running process,
stop cameras/hardware recorders, or control microphone/system audio sent by a meeting application.
Prefer sharing only a presentation window/tab or an unshared display/device when appropriate.
