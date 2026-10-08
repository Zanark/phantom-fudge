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

First run `.\scripts\Initialize-Transcription.ps1 -DownloadModel` to prepare the local Python
environment, pinned dependencies and model. The launcher builds WPF, starts/reuses native Python,
and passes the token from `.local\transcription.token` without printing it.
Use `.\scripts\Run-PhantomFudge.ps1 -NotesOnly` for notes without speech service startup.
Development builds need
the .NET 10 Desktop Runtime; an explicitly published self-contained ZIP includes its runtime.

If a packaged preview is supplied, extract the WHOLE directory and run `PhantomFudge.exe`.
Do not copy just its EXE away from its dependent files. An unsigned preview may be blocked
by Windows or organizational policy; do not disable security controls to bypass that.

## Transcript-first view, themes, and notes

- The live transcript is the main view. Notes starts collapsed; click Notes to expand/collapse.
  Collapsing preserves text and unsaved status. Edit / Ctrl+Alt+E opens editable notes deliberately.
- The default Present view does not request foreground activation and keeps the standard arrow
  over text. This reduces local cues, not a guarantee of another app's captured pointer visibility.
- Use the sun/moon button for DeepSeaFoam/Harbor Daylight, or Plain B/W for the neutral view.
  Themes are session-only and preserve opacity, content, HWND and transcription state.
- Drag the quiet header and resize at the edges. The pointer stays an arrow, including text areas;
  normal hit regions still work. Edit restores keyboard focus and text selection.
- Use **Background** to see through the notes/transcript area: default55%, adjustable10-100%.
  Lower values reveal more of the actual window behind;100% restores an opaque background.
  Note/transcript text stays solid, and the slider also works in Present mode. Header and setup
  controls remain opaque for usability. This does not make the window click-through.
  The10% minimum preserves background hit testing; the value lasts for this app session.
- Use New/Open/Save/Save as; only UTF-8 `.txt` up to 1 MiB is supported in this preview.
- No note autosave or crash recovery is enabled. Save explicitly; an unexpected process crash
  can lose unsaved notes and transcripts.
- Ctrl+S/O/N/F provide save/open/new/find. A-/A+ adjust font size; Wrap changes wrapping.
- Ctrl+Alt+Space hides/reveals; Ctrl+Alt+E returns to Edit. A conflicting global shortcut is
  reported; relaunching the app asks the existing instance to reveal and recenter.
- The app deliberately has no ordinary taskbar/Alt-Tab entry. Hide is not Exit.
- Unsaved content is handled by an inline confirmation, not a note-bearing system popup.

Present mode is the startup default, read-only/non-activating and without an acknowledgment.
Permission and recipient rehearsal are handled by the owner outside the app; neither is
reconfirmed with checkboxes, dialogs or repeat prompts. Switching modes, hiding, lock/suspend
or protection invalidation stops transcription.
Start again deliberately after checking the new configuration. Normal dialogs/export are
disabled during Present. The current preview uses fixed shortcut defaults.

## Local transcription: Computer, Mic, or Both

Choose Computer for meeting/system playback, Mic to dictate a question, or Both to transcribe both
independently. Both is selected initially, but capture does not start automatically. Audio setup has
separate playback and microphone selectors. Match the playback device to the meeting's headphones.
Computer audio includes other apps and audible sidetone; no meeting-process isolation is implied.
Headphones help prevent the microphone from picking up speaker audio twice.

Follow `services\transcription\README.md` for provisioning. `Initialize-Transcription.ps1`
creates the local Python environment, model and token. `Start-Transcription.ps1` starts native
Python; the main launcher does this automatically. Docker is not required.
C# handles the GUI/audio transport; Python/Vosk performs the actual recognition.

The idle Python service persists independently of the starter/app. After stopping audio in the
app, use `.\scripts\Stop-Transcription.ps1` to shut down this workspace's service gracefully.
It will not terminate unrelated services or interrupt active transcription.
Docker is optional final-stage packaging, invoked explicitly with `Start-Transcription.ps1 -Docker`;
do not use that path for normal local operation.

Use the repository launcher so the token is passed automatically, or enter the local token
in the session-only Audio setup field. The endpoint is literal loopback
`ws://127.0.0.1:8765/v1/transcribe`; there is no cloud speech fallback.

Open Audio setup, select the required devices, and click Start. No permission confirmation
is requested. Final lines are labelled Computer or Mic; independent unfinished lines can change.
Stop flushes both sources and releases selected devices. Separate meters show Computer/Mic levels.
Stop before changing mode or devices. Windows may display its own microphone privacy indicator;
this app does not suppress it. No chats, clipboard history or meeting APIs are read.
Audio setup works with the mouse in Present; choose Edit to type a token or export a transcript.

Audio is processed in memory and is not recorded to a file. Transcripts are retained in memory
and written only through Export. Clear requires export of unsaved transcript content; Exit has
an explicit discard choice. Recognition is machine-generated and can be wrong, particularly
names, technical terms, accents and overlapping speakers. No diarization is promised.

If a selected service/model/token/device is unavailable, the app reports failure instead of using
an unselected input or uploading audio. A full audio queue stops transcription rather than silently
dropping speech. An interrupted connection can lose the unfinished tail; that is reported.

## Synthetic diagnostics

These paths use generated fixture text, not personal notes or a live meeting:
The capture-proof command intentionally shows an unprotected synthetic positive control;
run it only outside a live presentation. It must not be mistaken for an invisible production window.

```powershell
dotnet run --project .\spikes\CaptureProof -c Release -- .\.local\capture-proof --transparent
.\src\PhantomFudge.App\bin\Release\net10.0-windows\PhantomFudge.exe --ui-smoke .\.local\ui-smoke
.\scripts\Test-NativeLifecycle.ps1
```

The first records cropped GDI capture samples of a synthetic overlay/backdrop. The second
renders its own synthetic WPF visual tree for layout/state inspection. The latter intentionally
does not test capture exclusion and starts no audio. Neither certifies any meeting application.
The lifecycle test starts only its own blank instance and exercises minimized/hidden recovery
and native close. It refuses to touch an already running PhantomFudge process.

## Current qualification boundary

Teams, Zoom, Meet, OBS, alternate GPUs/DPI/headsets, startup-frame completeness and actual meeting
transcription accuracy require their own recorded outcomes. Docker execution is a separate deferred
packaging test, not a native-app or transcription prerequisite.
Read `docs\QUALIFICATION.md` and the TODO index for the current evidence and remaining gates.

Screen affinity does not encrypt disk files, protect clipboard history, hide a running process,
stop cameras/hardware recorders, or control microphone/system audio sent by a meeting application.
Prefer sharing only a presentation window/tab or an unshared display/device when appropriate.

The reported remote cursor disappearance still needs the actual sharing product/mode checked.
Local nonactivation and Arrow policy do not control that recorder's cursor stream. See
[cursor and theme boundaries](CURSOR-AND-THEMES.md). Automatic protection failures no longer
show a captureable branded popup: content remains hidden and a generic local notice is written
to `%LOCALAPPDATA%\PhantomFudge\last-window-error.txt`. File dialogs explicitly opened in Edit
remain outside the main protected HWND.
