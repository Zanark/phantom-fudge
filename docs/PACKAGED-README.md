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
downloads verified Vosk and Whisper small.en English models, and creates a local token. No meeting data is uploaded.
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
- The 1240 x 1520 requested window clamps to the screen. Wide views use transcript/Q&A columns,
  with main actions visible and scrolling confined to long text.
- Sun/moon switches DeepSeaFoam/Harbor Daylight; Plain B/W returns to the neutral view.
- Text keeps an arrow cursor. Captured-pointer visibility is still recorder-dependent, not guaranteed.
- Ctrl+Alt+Space: hide/reveal; Ctrl+Alt+E: Edit. Relaunch to recover/recenter.
- Background:10-100% tint, default55%, including question/answer areas. Reading text stays solid
  white in the dark theme and black in light/plain themes.
- Capture status is quiet; inline Details retains limits without a persistent warning wall.
- Choose Computer, Mic, or Both; select each required device. Start is always explicit.
- Computer includes other output audio; Mic lets you dictate chat questions. The app does not read chats.
- Separate labelled tracks/meters are not speaker identification. Use headphones to reduce echo.
- Vosk gives provisional text; local Whisper small.en CPU-int8 produces accurate finals. Missing
  selected models fail explicitly. Synthetic improvement is not actual-microphone qualification.
- Windows microphone indicators may be visible outside this app's window-exclusion setting.
- Permission and recipient checks are your external workflow; the app does not ask again.
- Save notes/export transcripts explicitly. There is no crash recovery or audio-file recording.
- Hiding, global Notes mode/session/power changes stop transcription; start again deliberately.
- Question opens a local composer: explicit segments or typed text, then Ask as shown freezes a snapshot.
- Snapshot-only mode sends nothing. Configure installed Copilot/local-model answers explicitly;
  Copilot's normal models are cloud-hosted. Direct Azure remains future work.
- Agency prepares one empty session without prompting a model. Wait for Ready, then Ask; each
  question is fresh and answer-only. Three prepared test answers met five seconds, not cold startup
  or a universal latency guarantee.
- Copy/Clear question text deliberately. Diagnostic Text mode is an explicit retention opt-in;
  the external CLI/model service can retain its own data separately.
- Automatic local metadata logs omit conversation bodies, audio, notes and tokens. Diagnostics opens
  the folder; `scripts\Export-Diagnostics.ps1` creates a local metadata-only export, never an upload.

See [preview usage](docs/PREVIEW-GUIDE.md) and [transcription design](docs/TRANSCRIPTION.md).
See [cursor and theme details](docs/CURSOR-AND-THEMES.md) for exact limits and palette sources.
See [local question drafts](docs/LOCAL-QUESTIONS.md) for source selection, editing and exact snapshot boundaries.
See [answer provider setup](docs/ANSWER-PROVIDERS.md) for installed CLI, dedicated sign-in and local model options.
See [quality results](docs/QUALITY-UPDATE.md) and [diagnostic controls](docs/DIAGNOSTICS.md).
Save/copy needed content and close an old app with X before updating; Hide does not unload old code.
Development/test commands in those documents require the source checkout, not just this bundle.
The five allowlisted JSON quality receipts under `tools` are synthetic evidence only, not model
weights, personal profiles, captured audio or executable benchmark tooling.
Optional Docker recipes and the engine benchmark are source-checkout tools for later work;
this bundle is the native runtime, not a container distribution or certified public release.
