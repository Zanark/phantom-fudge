# Phantom Fudge

A local transcription-first development preview for Windows 11: a movable, resizable
surface with a large live transcript and collapsed speaker notes, no ordinary taskbar/Alt-Tab
entry, and Windows capture exclusion requested for its contents.

**Status: native development preview implemented; full meeting compatibility is not yet qualified.**

**Planned next: Ask AI.** Deep research/design is complete for explicitly sending a chosen
dictated question to an Azure model and streaming its answer. This feature is **not implemented**,
and the current app does not upload audio/questions to Azure. Start with
[the proposed architecture](docs/AI-ANSWER-ARCHITECTURE.md) and new TODOs27-37.

Includes a plain-text editor, hide/relaunch recovery, Edit/Present modes, and **live transcription
from Computer audio, Microphone dictation, or Both**. Inputs are selected explicitly and remain
stopped until Start. **C# handles the window and WASAPI capture; Python recognizes separate
source-labelled tracks locally.** No chat reading or cloud speech fallback is used.
Docker is not required. The same headless Python service can be containerized later.

The **Background** slider adjusts translucency from10% to100% (default55%) while keeping note
and transcript text solid. Lower it to see the actual content behind the window.
The sun/moon button switches **DeepSeaFoam / Harbor Daylight**; **Plain B/W** restores the neutral
view. The default transcript view is non-activating with an arrow cursor; use **Edit** for typing.

Start with the [preview guide](docs/PREVIEW-GUIDE.md). From PowerShell 7 in this repository:

```powershell
.\scripts\Initialize-Transcription.ps1 -DownloadModel
.\scripts\Run-PhantomFudge.ps1
```

Setup creates a service-local Python environment and explicitly downloads the model if needed.
The app launcher starts/reuses native Python automatically; audio still requires an explicit Start
inside the app. Use `-NotesOnly` to launch without the service. Stop the idle service with
`.\scripts\Stop-Transcription.ps1`. No Docker installation or cloud account is needed.

Docker recipes are retained as **optional final-stage packaging**, after the native app is complete.
They are not on the transcription or native-release critical path. Model setup downloads public
dependencies/weights; meeting audio is never uploaded.

Windows supports `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` for supported capture
paths. It does **not** guarantee exclusion from every recorder, hide a running process,
or necessarily remove a window from a sharing picker. Phantom Fudge will still be a real
application and window. Never rely on it as a security boundary.
The owner's recipient-side cursor-disappearance report remains unqualified in its actual
sharing mode; local arrow/focus changes cannot guarantee every recorder's cursor behavior.

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
| [Required transcription](docs/TRANSCRIPTION.md) | C# output capture, native Python recognition, local protocol and later packaging |
| [Local speech service](services/transcription/README.md) | Model setup, service operations and exact current evidence |
| [Implementation evidence](docs/IMPLEMENTATION-STATUS.md) | Implemented scope and remaining release gates |
| [Speech-engine comparison](docs/SPEECH-BENCHMARK.md) | Measured offline SAPI/Vosk/Whisper speed and word-error rates |
| [Cursor and themes](docs/CURSOR-AND-THEMES.md) | Transcript-first controls, canonical palette provenance and cursor limitations |
| [Computer + microphone](docs/DUAL-SOURCE-AUDIO.md) | Input modes, separate source tracks, v2 protocol and microphone boundaries |
| [AI answer research](docs/AI-ANSWER-RESEARCH.md) | Current Azure/ASR sources, model lifecycle and design decisions |
| [AI answer architecture](docs/AI-ANSWER-ARCHITECTURE.md) | Explicit draft/Ask, source commit, streaming and cancellation |
| [Azure data/identity boundary](docs/AI-ANSWER-PRIVACY.md) | Selected-text egress, keyless auth and retention limits |
| [Answer latency evaluation](docs/AI-ANSWER-EVALUATION.md) | Proposed budgets and controlled quality/latency experiments |

**Implementation:** C# / WPF on .NET 10 LTS, NAudio render-endpoint loopback, and a local
Python/Vosk streaming service. Keep private notes, transcripts, models and local tokens out of Git.

If required capture paths fail, narrow the supported claim or use a dedicated unshared
display/device or presentation-window sharing. Do not substitute an invisibility promise.

## Engineering book and development journal

The [illustrated Engineering Handbook](docs/handbook/README.md) covers the system from first
principles through native capture, dual-source speech, failure recovery, measured benchmarks and
the proposed Azure answer flow. Read the [offline HTML edition](docs/handbook/Phantom-Fudge-Engineering-Handbook.html)
or [printable PDF](docs/handbook/Phantom-Fudge-Engineering-Handbook.pdf).

The [public JOURNAL](JOURNAL.md) records the journey, including corrected decisions and remaining
unknowns. [Documentation maintenance](docs/DOCUMENTATION-MAINTENANCE.md) is part of every feature:
update the book, diagrams, synthetic UI images and significant journal entries with the implementation.
See [all documentation](docs/README.md) for the shorter operational guides.
