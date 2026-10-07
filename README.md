# Phantom Fudge

A plain-text speaker-notes development preview for Windows 11: a movable, resizable
notepad-like surface without a taskbar button or Alt-Tab entry, with Windows capture
exclusion requested for its contents.

**Status: native development preview implemented; full meeting compatibility is not yet qualified.**

Includes a plain-text editor, hide/relaunch recovery, Edit/Present modes, and **required live
transcription of selected playback-device audio**. No microphone endpoint or cloud speech fallback
is used. Docker runs the local speech recognizer; the Windows window and WASAPI capture stay native.

Start with the [preview guide](docs/PREVIEW-GUIDE.md). From PowerShell 7 in this repository:

```powershell
.\scripts\Initialize-Transcription.ps1 -DownloadModel
.\scripts\Start-Transcription.ps1
.\scripts\Run-PhantomFudge.ps1
```

The service-start command requires a separately installed, running Linux-container Docker engine.
Docker support is prepared but **has not been executed on this machine**; the owner chose to
install/start Docker Desktop separately. Without the service, the native app still edits notes.
No audio starts automatically. Model setup downloads a public model, not meeting audio.

Windows supports `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` for supported capture
paths. It does **not** guarantee exclusion from every recorder, hide a running process,
or necessarily remove a window from a sharing picker. Phantom Fudge will still be a real
application and window. Never rely on it as a security boundary.

The approved target is **Windows 11 first**, qualifying **Teams, Zoom, Google Meet, and OBS**
separately. None has been qualified with this application yet. Synthetic local Windows capture
and local output-loopback recognition evidence do not substitute for those product results.

## Start here

| Document | Purpose |
| --- | --- |
| [TODO index](TODO/00_INDEX.todo) | Workstreams, statuses, dependencies, and the next task |
| [Feasibility research](docs/FEASIBILITY.md) | Sources, technical limits, alternatives, and unanswered experiments |
| [Product contract](docs/PRODUCT.md) | Requested behavior, proposed defaults, and explicit non-goals |
| [Architecture](docs/ARCHITECTURE.md) | Proposed components, HWND lifecycle, and failure behavior |
| [Development workflow](docs/DEVELOPMENT-WORKFLOW.md) | Proof-first delivery sequence and completion gates |
| [Qualification matrix](docs/QUALIFICATION.md) | Exact capture scenarios, controls, and evidence requirements |
| [TODO conventions](docs/TODO-CONVENTIONS.md) | The evidence-backed catalogue format |
| [Required transcription](docs/TRANSCRIPTION.md) | Native output capture, local protocol, consent and Docker boundaries |
| [Local speech service](services/transcription/README.md) | Model setup, service operations and exact current evidence |
| [Implementation evidence](docs/IMPLEMENTATION-STATUS.md) | Implemented scope and remaining release gates |

**Implementation:** C# / WPF on .NET 10 LTS, NAudio render-endpoint loopback, and a local
Python/Vosk streaming service. Keep private notes, transcripts, models and local tokens out of Git.

If required capture paths fail, narrow the supported claim or use a dedicated unshared
display/device or presentation-window sharing. Do not substitute an invisibility promise.
