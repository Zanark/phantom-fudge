# Implementation status - 2026-10-08

**Local development preview, not a qualified/public release.**

The later15:05 Ask-AI request is **research/planning only**. Four AI-answer design documents,
TODOs27-37 and workflows04/05 are added, but there is no implemented question composer,
source-commit control, Azure auth/client, Ask button or streamed-answer pane. No cloud latency
result, model deployment, account lookup or data upload exists from that research task.
The current runnable application/package below is unchanged.

Latest14:01 input extension: Computer, Mic or Both after explicit Start. The owner's request
supersedes the original never-microphone product scope. Separate WASAPI tracks, source-prefixed v2
transport and independent Python/Vosk decoders produce labelled finals and independent partials.
No chat access or speaker-identity inference exists. OS mic indicators/echo remain external limits.
See DUAL-SOURCE-AUDIO.md. Development checks opened no real mic/render endpoint and played no audio.

Current source evidence: Core41tests, native46synthetic groups, backend71tests and4manager-upgrade
guards pass. Real generated dual PCM through Python yielded Computer11/Mic8partials and3finals each,
correct keywords/source separation and tail drain. Actual C#v2->Python yielded Computer13/Mic10partials
and2finals each with expected phrases. These prove protocol/model integration, not physical mic,
device permissions, echo quality or microphone-indicator/capture behavior.

A review-found stop/restart race was fixed: Start/mode/clear stay blocked through cleanup/final
draining, preventing late old-session IDs from discarding new-session text. Synthetic UI regression
also covers modes, both passive device selectors, source labels, themes and prior note safeguards.

Current dual-source bundle:
`artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-145231.zip`,
76,184,066bytes/485files, SHA256
`BC89867277F3E8C8BAA179BE74B7F517A98D6E09CBE35534AFB21E12E127C846`.
Exact packaged UI passes with `recordingStarted:false`; evidence is `.local\packaged-dual-source-ui`.
The default source-launch build is refreshed. No live microphone/output endpoint was opened,
no chat content was read, and no user process/service was stopped for this update.

Latest UI update: transcript-first with Notes collapsed, default non-activating Present and
standard Arrow cursor. DeepSeaFoam is default; sun/moon switches Harbor Daylight and Plain B/W
resets the neutral theme. Canonical palette provenance and the unresolved recipient-cursor report
are in CURSOR-AND-THEMES.md. No universal no-reveal guarantee is claimed.

Core33tests and local synthetic UI regressions cover palettes/contrast, default foreground
preservation, MA_NOACTIVATE3, Arrow policy, primary/collapsed layout, lossless notes, alpha and
scrolling. Passive device selection explicitly avoids ListBox's focus requirement. No audio was
captured or played, no user process was stopped, and no live sharing recording was made.
The app no longer opens an automatic unprotected protection-failure message box.

Current UI bundle: `artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-123245.zip`
(76,167,343 bytes;484 files), SHA256
`75544D20F3C5CB70E9F8DF5E4B99AD4BBFA6E55AB61F52FE33274F80FED8F1EC`.
Exact packaged UI regressions pass, including passive device selection and scrolling. Evidence:
`.local\packaged-transcript-first-ui\result.json`. No new unprotected positive-control capture
or live meeting recording was run while the owner's existing app was active.
The already-running app was left untouched; save/close it before opening this new bundle.

Latest architecture correction (owner03:25): **Docker is not required.** Native C# GUI/audio
already uses Python recognition; normal setup/start/stop now provisions and manages native Python
directly. The app launcher starts/reuses that service, with NotesOnly available and no automatic
audio capture. Docker recipes are retained only for optional final-stage packaging after native release.
Task22 tracks native runtime;23 contains the requested mute-independent recognizer comparison.
Both are complete within their scoped evidence:37service/management tests, normal launcher/recovery/
owned-shutdown and real silent C#->Python ASR pass.32synthetic cases/164.07s compared all3engines:
SAPI45.89%WER/10.139s, Vosk8.22%/41.754s, Whisperbase.en4.79%/25.745s warm decode.
See SPEECH-BENCHMARK.md for normalization, model-load/RTF, TTS limitations and live-latency caveats.
Whisper is a stronger quality candidate, not yet substituted for the current true-streaming Vosk path.

Earlier native bundle:
`artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-035959.zip`,
SHA256 `DED2F7231064F3705F5E5F248F751671DF0B741F9E941A3908999F456C2D7426`.
It includes the C# app, Python service source, pinned requirements/model manifest and normal
setup/start/stop/launch scripts. No interpreter, model weights, venv or token is baked in.
An isolated extracted copy successfully created its own venv/token from cached public model data,
started native Python and the packaged app, recovered/minimized/hid/closed through PID-scoped
window enumeration, and stopped the owned service. The validation tree/processes were removed.
This does not claim a clean physical machine or container test.

Latest rendering change: Background now adjusts10-100% (default55%), with solid note/transcript
text, opaque header/setup controls and no click-through. Local layered-WPF proof retains affinity17
and zero background differences in16protected samples. UI alpha26/255, solid text, stable HWND,
caption/resize/editor hit tests and removed acknowledgments pass; task21 tracks remaining remote/
hardware/readability acceptance. Earlier opaque packages below are historical.

Current local ZIP: `artifacts\phantom-fudge-0.1.0-preview-translucent-20261008-032051.zip`.
SHA256: `6BA27F4CF2DD5F64F86F02263594394694AF9A80FB03E5DE3948B536B4BDA1B8`.
Default launch build is refreshed. Exact packaged UI/alpha/native hit-region and layered GDI
probes passed; native minimize/relaunch/hide/close also passed. No audio capture/playback/upload occurred.

Preserved October8 03:04 owner change: permission and recipient rehearsal are handled externally.
Both checkboxes and all associated Start/Present/reset confirmations are removed; explicit
Start/Stop, technical checks and unsaved-content warnings remain. The updated default build
and `artifacts\phantom-fudge-0.1.0-preview-no-prompts-20261008-030741.zip` are local.
ZIP SHA256: `2B0ED8D92BB325EA657E875DA9323605725D6A2B1F7A1E28663D3640E27285BD`.
Updated synthetic UI behavior passed with no audio started. The earlier package below is historical.

## Implemented

- .NET 10.0.112 WPF/Core solution, normal-user Windows 11 x64 application, layered tool-window
  style, pre-show capture affinity, safe generic title, and native composition flush.
- Plain UTF-8 notepad editing, find/wrap/font size, explicit New/Open/Save/Save as, unsaved
  confirmations and in-memory transcript export. No automatic note/recovery/audio persistence.
- Minimal drag/resize chrome, global hide/Edit shortcuts, current-user named-pipe second-launch
  reveal/reset and explicit read-only/non-activating Present mode without acknowledgment gates.
- Selected Computer/Microphone/Both WASAPI inputs using NAudio 2.2.1, opened only after explicit Start.
  Separate source tracks; device loss stops and there is no automatic default-device switching.
- Strict literal-loopback authenticated WebSocket transport, bounded PCM queue, partial/final
  schema checks, silence handling, startup cancellation, final draining and resource cleanup.
- Protected transcript pane with explicit Start/Stop, per-source meters,
  in-memory cap and source-labelled export. Selected output includes other apps and audible sidetone.
- Offline English Vosk service, explicit verified-model/token/native-Python provisioning, health/auth/
  instance-scoped lifecycle and protocol/resource gates. Dockerfile/Compose are optional future packaging.

## Historical initial-preview evidence (current dual-source evidence is above)

| Scope | Actual result | Boundary |
| --- | --- | --- |
| Core tests | 25 passed | File/text/transcript policies, including preservation of unfinished recognition |
| Audio client harness | 20 synthetic groups passed | Auth/schema/queues/cancellation/stop/device-notification logic |
| Python service suite | 37 passed | Protocol/fault plus authenticated native-lifecycle/ownership tests |
| Real local speech engine | Synthetic speech produced expected words and partial/final events | Native Python, not Docker; no real meeting quality benchmark |
| C# socket -> Vosk | Real synthetic PCM recognition and final drain passed | Transport/model integration, not hardware capture |
| User-approved actual playback loopback | 11 partials, 3 finals, 37 nonzero-meter updates, expected phrase matched, zero faults, capture stopped | Default render endpoint on this Windows machine; no microphone or audio file |
| Synthetic native UI | Edit/Present state, text preservation, protected styles, three render endpoints, local layout renders | In-process rendering is not capture exclusion |
| Native recovery/close | Blank app minimized/recovered, hidden/recovered, then native-close exited cleanly | Own temporary processes only, no audio |
| Synthetic cropped GDI proof | Positive control 100000 differing pixels;16 protected samples matched underlying baseline exactly | Specific BitBlt samples, not continuous first-frame proof or any meeting backend |

Native environment: Windows 11 build26200 x64, .NET Desktop10.0.12. Local synthetic evidence
is retained in `.local\capture-proof-initial` and `.local\ui-smoke-initial`, not public personal
desktop screenshots. The getter returned affinity17 for this opaque HWND; this does not settle
every platform's documented getter restrictions.

The actual playback test was explicitly approved by the user. It played generated English
speech and routed default-render loopback through the real local recognizer. Raw PCM remained
in memory. The temporary native validation service was stopped afterward.

Read-only review found and fixed confirmation/save reentrancy, invalid endpoint parsing,
unfinished-recognition loss on restart, reentrant native Close, and minimized-window recovery.
Targeted regression coverage and the final packaged UI/GDI probes passed after those fixes.

## Earlier local packaged artifact

- ZIP: `artifacts\phantom-fudge-0.1.0-preview-win-x64-20261008-024518.zip`
- SHA-256: `518B597F1DCCA3C151EB2366420A27AD366E4AB79FDD4B15C8DA8D9353543E47`
- Size: 76,123,503 bytes;469 files; self-contained .NET/Core Desktop runtime10.0.12.
- Packaged native assembly SHA-256:
  `091B321749551F96B45182219917B878954F7FFD7DB273DF6D2095E76E4E6DA3`.
- Exact packaged synthetic UI and16-frame GDI probes passed; evidence lives in
  `.local\packaged-ui-smoke` and `.local\packaged-capture-proof`.
- Archive includes dependency notices; no private context, token, raw audio or debug symbols.
- This is local and unsigned, not uploaded, clean-machine certified, or a public release.

## Required but not completed

- **Deferred Docker execution:** not a native runtime/release dependency. Container build/mount/network/
  offline/resource/shutdown behavior remains unexecuted and is intentionally left until the final stage.
- Teams, Zoom, Meet and OBS qualification, plus full transcript-pixel and popup/IME capture matrix.
- Complete first-frame/failure evidence; alternative GPU/DPI/headset/HDR/ARM64 and long-session soak.
- Owner usability, real-meeting transcription quality/latency, accessibility and presentation-focus approval.
- Configurable hotkeys, opt-in recovery and persisted placement are unfinished target-plan features.
  Current defaults are explicit saves, no crash recovery, fixed shortcuts and recenter-on-relaunch.
- Final license/signing/distribution choices and public release authorization. No commit or push performed.

The owner went offline and required no further questions/uploads or risky system changes.
No additional playback, Docker installation, live meeting capture, or external publication was performed.

Native transcription19/runtime22 remain release gates; Docker20 does not. Preparing the service
is not claiming it ran in a container. The local preview can be developed/tested while native gates remain open; it must
not be advertised as universally uncapturable or as a finished qualified release.

## Documentation edition

The handbook adds source-grounded chapters, diagrams, a public JOURNAL and a local HTML/PDF pipeline.
The documentation-only `--documentation-screenshots` fixture renders actual WPF controls with
synthetic text/device labels, without starting capture. Its images are not desktop screenshots or
capture-exclusion evidence. The current preview ZIP above was not replaced for this documentation task.
Edition provenance/counts live in `handbook\manifest.json`; maintenance is required by workflow06.
