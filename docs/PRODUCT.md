# Product contract

Planning baseline: 2026-10-07. "Approved" below means the user actually supplied or
selected it. "Proposed" is a recommendation to resolve in `TODO\01_PRODUCT_DECISIONS.todo`.

## Approved requirements

| ID | Requirement | Delivery evidence |
| --- | --- | --- |
| R01 | Plain-text, notepad-like speaker/presentation notes | Edit, select, paste, undo, scroll, and read a real note |
| R02 | Drag the surface like a normal window | Dedicated drag grip and edges; text selection still works |
| R03 | No ordinary taskbar/app chrome | Taskbar/Alt-Tab inspection; minimal visible frame |
| R04 | Keep notes out of audience capture | Qualified capture modes and recipient/recording evidence, not a universal guarantee |
| R05 | Windows 11 first | Exact Windows 11 release/edition/architecture listed in qualification records |
| R06 | Qualify Teams, Zoom, Google Meet, and OBS separately | Versioned results for each required product/mode |
| R07 | REQUIRED live transcription of meeting sound heard through the output device, not microphone input | Render-endpoint loopback, partial/final transcript, stop/device/fault tests |
| R08 | REQUIRED local Docker transcription backend; no cloud speech processing | Container definition, authenticated loopback-only transport, offline inference and lifecycle evidence |

The original wish for a window that can "never be detected" is not an implementable
universal contract. Task Manager, desktop-window enumeration, accessibility tools,
screen photographs, hardware capture, and some capture paths remain outside that promise.
The user chose Windows 11 scope after being told about this limitation.

## Proposed v1 experience

One note and one small, opaque, resizable surface, now with a protected live-transcript pane.
No account, cloud service, Markdown preview, text-generation assistant, or rich formatting.
A quiet header/grip holds Edit/Present,
Hide, and Settings controls. Settings and find UI stay inside the same protected surface.
Dark/light colors, font size, wrapping, and always-on-top are small usability options,
not elaborate theme work.

| Decision | Proposed default | Must be resolved before |
| --- | --- | --- |
| D01 | Windows 11; Teams/Zoom desktop, Meet in Chrome/Edge, OBS | Approved; exact build/backend coverage still needs G1/G4 |
| D02 | C# WPF, .NET 10 LTS, opaque HWND and minimal interop | Capture spike |
| D03 | No tray icon by default; configurable hotkeys plus second-launch recovery | Recovery implementation |
| D04 | Local opt-in crash recovery; explicit `.txt` Open/Save/Save As; no cloud | Persistence implementation |
| D05 | Always-on-top Present mode; editable/focusable Edit mode; explicit mode switch | Interaction implementation |
| D06 | Self-contained x64 ZIP first; ARM64 only after separate qualification | Packaging |
| D07 | Single note; proposed 1 MiB import cap; proposed budgets below | Editor/performance implementation |
| D08 | License and release/signing ownership chosen by owner | Publication |

These defaults are not approvals. Record a change to a default, rationale, and affected
tests before implementing it. Do not infer license or publishing rights from the repo name.

## October 8 implementation authorization

The user requested implementation of the proposed Windows-first design and explicitly
selected a fully local Docker speech service. Begin a local experimental implementation
using WPF/.NET 10, no-tray recovery, explicit saves and no automatic note/audio persistence.
Unchosen license/signing/publication and final UX/resource approval remain release gates.
The full four-product capture gate is not yet passed; local preview work must not promote
it to a qualified release. See [TRANSCRIPTION.md](TRANSCRIPTION.md).

Output-device loopback captures ALL sounds played on the selected device, including other
apps and any microphone sidetone already audible there. It never opens a microphone endpoint
and is not falsely labelled meeting-process isolation. Transcription requires explicit Start,
visible local status, an appropriate right to transcribe the meeting, and explicit Stop.
Raw audio is not persisted; transcript export is explicit. The default local model is an
English streaming model; multilingual accuracy and diarization are not assumed.

## User journeys

1. **Prepare:** launch with a generic shell, initialize protection before displaying note
   content, type/paste or open a UTF-8 text file, and arrange the window.
2. **Rehearse:** use a synthetic test note and the exact screen-sharing configuration.
   Inspect another participant's view and, if relevant, a recording. Prefer sharing the
   presentation window/tab instead of the entire desktop where that meets the presentation need.
3. **Present:** explicitly enter read-only, non-activating mode. Read/scroll and drag from
   the grip without stealing slide keyboard focus. No file dialogs, suggestions, or note previews.
4. **Edit deliberately:** invoke the Edit command/hotkey; the window becomes focusable.
   This action is explicit, not triggered by accidental pointer movement.
5. **Hide/recover:** one shortcut hides the surface; relaunching the executable restores
   the existing instance to a visible on-screen position after reinitialization.
6. **Finish:** exit with explicit unsaved-change/save-failure handling. Hidden does not mean
   exited. No auto-start, background service, or scheduled task by default.

## Privacy boundaries

The app requests capture exclusion for its own supported windows. It cannot control an
arbitrary recorder, meeting participant, administrator, desktop camera, or hardware output.
Do not advertise "undetectable", "100% invisible", "all recorders", or "cannot be recorded".
Safe wording is "requests Windows capture exclusion; supported configurations are listed".

Capture affinity does not encrypt files, clear clipboard history, hide accessibility text,
prevent process dumps, or suppress audio. Reading notes aloud or through a screen reader
can reach a shared microphone/system-audio stream. File dialogs, IME candidates, OS clipboard
history, third-party accessibility UI, and screen-sharing pickers may be outside the app's
protected HWND. Their risks must be qualified or excluded explicitly.

Keep the native title generic: no note excerpt, document path, or personal filename.
Diagnostics must use synthetic identifiers rather than note hashes or snippets.
Real desktop captures remain private unless separately sanitized and authorized.

## Proposed measurable budgets

These are initial targets for approval/revision after the spike, not current measurements.
Name the reference CPU/GPU, Windows build, display setup, and dataset in every result.

| Area | Proposed target |
| --- | --- |
| Hide/reveal command latency | p95 at most 250 ms over 50 trials; reveal includes protection setup |
| Editing | No lost/duplicated input or UI stall over 100 ms in a 10,000-character synthetic note |
| Idle resource use | Mean at most 1% total CPU over 5 minutes; no render polling loop |
| Memory | At most 150 MiB private bytes for the baseline single-note workload |
| Soak | 60 minutes; no crash, lost content, stuck focus, or unbounded handle/memory growth |
| Text size | Proposed 1 MiB input ceiling; explicit rejection, never truncation |
| Capture | Zero visible note content in all observed frames of required qualification scenarios |

The last row applies only to the observed configuration and recorded samples. It is not
a proof over all future frames, devices, versions, or capture implementations.

## Deferred / explicitly outside v1

macOS/Linux parity, Windows 10 qualification, cloud sync, multiple notes/windows, rich text,
click-through, transparent/acrylic backgrounds, automatic meeting detection, plugins,
autoscroll/teleprompter controls, auto-update service, and encrypted vaults are not required
to deliver the approved v1. Reconsider them only through `18_FUTURE_SCOPE.todo`.

No process hiding, security-tool evasion, recorder tampering, injection, or custom driver
is part of this design. The simpler app remains honestly discoverable.
