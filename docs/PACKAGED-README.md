# Phantom Fudge native preview

This local development preview includes the Windows app and native Python speech-service
source/setup scripts. **Docker is not needed.** The app remains discoverable to Windows and
capture exclusion is not a guarantee against every recorder.

## First run

Extract the entire ZIP. Use PowerShell 7.2+ and an installed Python 3.13 interpreter:

```powershell
.\scripts\Initialize-Transcription.ps1 -DownloadModel
.\scripts\Run-PhantomFudge.ps1
```

Setup creates a package-local Python environment, installs pinned dependencies, explicitly
downloads the public English model, and creates a local token. No meeting data is uploaded.
The C# runtime is included; the Python interpreter/model/venv are not baked into the ZIP.

Later launches only need `Run-PhantomFudge.ps1`. It starts or reuses the native Python
recognizer and launches the app. Audio starts only when you click Start in the app.
Use `-NotesOnly` if you do not want speech-service startup. After stopping transcription:

```powershell
.\scripts\Stop-Transcription.ps1
```

The local service only stops if its saved instance identity matches and no transcription
is active. A process owned by another launcher/workspace is not terminated.

## Controls and limits

- Transcript is primary; Notes is collapsed until opened. Startup Present mode is non-activating.
- Sun/moon switches DeepSeaFoam/Harbor Daylight; Plain B/W returns to the neutral view.
- Text keeps an arrow cursor. Captured-pointer visibility is still recorder-dependent, not guaranteed.
- Ctrl+Alt+Space: hide/reveal; Ctrl+Alt+E: Edit. Relaunch to recover/recenter.
- Background:10-100% tint, default55%; note/transcript text remains solid.
- Choose Computer, Mic, or Both; select each required device. Start is always explicit.
- Computer includes other output audio; Mic lets you dictate chat questions. The app does not read chats.
- Separate labelled tracks/meters are not speaker identification. Use headphones to reduce echo.
- Windows microphone indicators may be visible outside this app's window-exclusion setting.
- Permission and recipient checks are your external workflow; the app does not ask again.
- Save notes/export transcripts explicitly. There is no crash recovery or audio-file recording.
- Hiding, global Notes mode/session/power changes stop transcription; start again deliberately.
- Question opens a local composer: explicit segments or typed text, then Ask as shown freezes a snapshot.
- No Azure answers are connected; Ask sends nothing. Type / edit for the question does not stop audio.
- Copy/Clear question text deliberately; questions stay memory-only and are included in unsaved-exit safeguards.

See [preview usage](docs/PREVIEW-GUIDE.md) and [transcription design](docs/TRANSCRIPTION.md).
See [cursor and theme details](docs/CURSOR-AND-THEMES.md) for exact limits and palette sources.
See [local question drafts](docs/LOCAL-QUESTIONS.md) for source selection, editing and exact snapshot boundaries.
Development/test commands in those documents require the source checkout, not just this bundle.
Optional Docker recipes and the engine benchmark are source-checkout tools for later work;
this bundle is the native runtime, not a container distribution or certified public release.
