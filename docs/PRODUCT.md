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
| R07 | Live Computer output plus optional Microphone dictation, separately or together | Explicit selected sources, labelled partial/final tracks, stop/device/fault tests |
| R08 | Native local Python recognition, easily containerizable later; no cloud processing | Native setup/start/stop and container-neutral model/protocol interfaces; Docker never required |
| R09 | See/read actual content behind the notes window using adjustable translucency | Background alpha control, solid text, retained drag/resize and capture requalification |
| R10 | Transcription is primary; notes collapsed by default | Default layout and lossless expansion/collapse |
| R11 | DeepSeaFoam/Harbor Daylight toggle plus Plain reset | Canonical semantic palettes, controls and local UI evidence |
| R12 | Remove I-beam/unnecessary focus cues; investigate shared cursor disappearance | Local Arrow/nonactivation plus separately qualified recipient behavior; no universal guarantee |
| R13 | Local question and installed-provider answer implemented; direct Azure remains future | Immutable source-scoped request and corresponding same-window answer |
| R14 | Complete useful answers targeted below five seconds after readiness; cold preparation reported separately | Bounded exact-text evidence, not a universal SLO or broad quality qualification |
| R15 | Planned: use Azure where justified with explicit data/identity boundaries | Approved selected-text inference profile; audio remains local by default |
| R16 | Keep illustrated engineering documentation and a public development journal current | Source-linked chapters, synthetic images, diagrams and generated editions maintained with changes |
| R17 | Use existing Agency in background or an explicit local model, with no org-data tools | Labelled provider, isolated prompt-only launch, zero-tool surface and bounded streamed output |
| R18 | Click several Mic/Computer transcript lines directly to compose a question | Whole-segment toggles, displayed-order raw text, guarded edits and no send until explicit Ask; workstream44 |
| R19 | Optional, default-off question interpretation before answering | Fixed clarification metadata, original draft retained, explicit Yes/correction replies; no automatic request |
| R20 | Clear primary action hierarchy and reduced reading type | Accent Ask followed by red-bordered Clear; answer50%and transcript25%font reductions |
| R21 | Visible active-transcription state | Red indicator follows running state, not source selection/startup |
| R22 | Direct device-card selection without list scrolling | Wrapping source-specific cards, explicit overflow pages and retained endpoint choice |
| R23 | Reversible notepad cover while ongoing work continues | Same-HWND NOTEPAD IS READY/Uncover; no implicit stop/cancel and no invisibility claim |
| R24 | Opt-in previous-answer context immediately left of Ask | Default-off/session-only; explicit Ask snapshots one completed displayed answer, with no accumulated conversation; workstream59 |

The original wish for a window that can "never be detected" is not an implementable
universal contract. Task Manager, desktop-window enumeration, accessibility tools,
screen photographs, hardware capture, and some capture paths remain outside that promise.
The user chose Windows 11 scope after being told about this limitation.

## Proposed v1 experience

One note and one resizable surface with background-only translucency and a protected live-transcript pane.
Installed Agency can use its hosted model service; an explicit local-model option remains distinct.
There is no direct Azure adapter, Markdown preview or rich formatting.
A quiet header/grip holds Edit/Present,
Hide, and Settings controls. Settings and find UI stay inside the same protected surface.
Dark/light colors, font size, wrapping, and always-on-top are small usability options,
not elaborate theme work.

The15:05 request designed a manual AI-answer feature; the17:37 request authorized subsequent implementation.
Local drafts/snapshots now exist; Azure remains unimplemented/unprovisioned. The proposed baseline adds text
inference after explicit Ask while keeping local ASR, no chat reading, no automatic prompts, same-HWND
answers, and Docker-last. Cloud audio is separately optional. See AI-ANSWER-ARCHITECTURE.md.

Question typing deliberately takes keyboard focus through its own protected transition without
stopping transcription or enabling notes editing. Direct transcript clicks now enter that same
transition; they are not passive no-activation gestures. Snapshot-only mode sends nothing;
default configured-provider Ask sends only the exact reviewed question. **Previous answer** can
explicitly add the displayed completed answer, separately from clarification's fixed instruction
and bounded explicit exchanges. It is background data, never a tool permission or automatic
conversation history. Accurate armed-Mic finalization
remains29. See LOCAL-QUESTIONS.md.

October8 11:31 direction: transcript-first replaces notes-first. Start in non-activating Present,
with notes collapsed, fixed Arrow pointer and a deliberate Edit action for keyboard input.
DeepSeaFoam is default, Harbor Daylight is its sun/moon companion, and Plain resets the neutral UI.
The owner's absolute no-reveal goal remains outside what Windows display affinity can guarantee;
the remote cursor disappearance is not silently declared fixed by local cursor tests.

| Decision | Proposed default | Must be resolved before |
| --- | --- | --- |
| D01 | Windows 11; Teams/Zoom desktop, Meet in Chrome/Edge, OBS | Approved; exact build/backend coverage still needs G1/G4 |
| D02 | C# WPF, .NET10 LTS, now a layered HWND with background-only alpha and minimal interop | Layered local proof passed; remote capture qualification remains |
| D03 | No tray icon by default; configurable hotkeys plus second-launch recovery | Recovery implementation |
| D04 | Local opt-in crash recovery; explicit `.txt` Open/Save/Save As; no cloud | Persistence implementation |
| D05 | Always-on-top Present mode; editable/focusable Edit mode; explicit mode switch | Interaction implementation |
| D06 | Self-contained x64 ZIP first; ARM64 only after separate qualification | Packaging |
| D07 | Single note; proposed 1 MiB import cap; proposed budgets below | Editor/performance implementation |
| D08 | License and release/signing ownership chosen by owner | Publication |

These defaults are not approvals. Record a change to a default, rationale, and affected
tests before implementing it. Do not infer license or publishing rights from the repo name.

## October 8 implementation authorization

The user requested implementation of the proposed Windows-first design and fully local speech.
The later03:25 clarification supersedes the earlier Docker-selected interpretation: Docker
containerization is only a final-stage portability option, not a runtime dependency. Continue
local implementation
using WPF/.NET 10, no-tray recovery, explicit saves and no automatic note/audio persistence.
Unchosen license/signing/publication and final UX/resource approval remain release gates.
The full four-product capture gate is not yet passed; local preview work must not promote
it to a qualified release. See [TRANSCRIPTION.md](TRANSCRIPTION.md).

Output-device loopback captures ALL sounds played on the selected device, including other
apps and any microphone sidetone already audible there. The14:01 request adds explicit microphone
input for dictated chat questions; Computer/Mic/Both modes supersede the earlier no-microphone scope.
No chat application/clipboard/history is read. Transcription requires explicit Start,
visible local status, an appropriate right to transcribe the meeting, and explicit Stop.
Raw audio is not persisted; transcript export is explicit. The default local model is an
English hybrid: Vosk provisional text and Whisper small.en CPU-int8 finals. Multilingual accuracy
and diarization are not assumed. App diagnostic Text mode is an additional explicit local-retention
opt-in; default app/backend logs contain metadata only.

Both is the default mode selection, not automatic capture. Independent recognizers label Computer
and Mic; they do not identify speakers. Selected-device failure stops the session without switching
inputs. Windows microphone privacy indicators and acoustic speaker echo are outside window-exclusion
assurance; headphones reduce echo. See DUAL-SOURCE-AUDIO.md.

## Runtime confirmations - owner decision, October 8

The owner states that they always have permission to transcribe and always check sharing
with a recipient first. Those steps are handled outside the app. Remove both checkboxes,
all associated Start/Present gates, and any repeat confirmation after hide/restart/display
changes. Do not replace them with dialogs or recurring reminders.

Explicit Start/Stop, technical protection/device/token checks, and unsaved-content warnings
remain. This preference is the owner's statement, not independent evidence that a capture
configuration passed engineering qualification.

## Background translucency - October 8 request

The owner asked to read what is behind the window. The implementation uses a true layered
WPF surface with a55% default background tint, adjustable10-100%; text remains fully opaque.
The slider stays usable in Present mode and does not recreate the HWND or stop transcription.
Header/setup/confirmation controls remain opaque for readability and recovery. There is no
click-through or whole-window fade. Backgrounds that overlap note glyphs can still reduce
readability; the owner can increase the tint or move the window. The setting is session-only.

October 9 actual-use correction requests twice the former window dimensions: 1240 x 1520, clamped
to the monitor work area. Wide layouts place transcript and Q/A side by side; long text scrolls
within its own control, not nested outer main panels. Reading text is white in dark mode and black
in light/plain modes. The answer shares the Background slider. Capture limitations move behind
quiet inline Details without suppressing actionable failures. Local synthetic validation is not
owner acceptance or recorder qualification. See QUALITY-UPDATE.md.

## User journeys

### October10: follow up using one displayed answer

After a completed answer, enable **Previous answer**, immediately left of Ask, then edit the
question and explicitly Ask. Do not Clear when intending to reuse that answer: Clear resets it.
The toggle is off on launch and stays on across successful follow-ups, but each request includes
only the last displayed completed answer—not its question, older answers or nested request context.
Failed/cancelled responses and interpretations cannot become a prior answer.
([`CapturePreviousAnswer`, `SetUsePreviousAnswer`](../src/PhantomFudge.App/MainWindow.AnswerContext.cs).)

With **Clarify first**, capture that answer before interpretation replaces the display. Keep the
same immutable snapshot through corrections, confirmation and explicit retry of a failed first
interpretation. Busy work and an active clarification lock context changes; provider changes and
Clear reset the option. Setup/details exposes exact submitted prior text and the snapshot label
records its character count. Narrow panes shorten secondary captions and the context button's
width, keeping Previous answer / Ask / Clear visible; Edit/Copy may wrap within their left group.
([Clarification](../src/PhantomFudge.App/MainWindow.Clarification.cs),
[compact captions](../src/PhantomFudge.App/MainWindow.AnswerFont.cs),
[usage](LOCAL-QUESTIONS.md#follow-up-with-the-previous-answer).)

This is **included in Preview 2**, not the immutable Preview 1 assets. [Workstream59](../TODO/59_PREVIOUS_ANSWER_CONTEXT.todo)
records October10 source validation; completed [workstream62](../TODO/62_PUBLIC_PREVIEW_RELEASE.todo) records public Preview 2's bounded package/publication evidence.

### October9 10:07: direct multi-line questions

The owner requested clicking Mic or Computer lines in the main transcript to populate the
question editor, then explicitly expanded the request to **multiple lines**. The implemented
source path uses ordinary clicks to toggle whole recognition segments without Ctrl, combines
only selected raw text in displayed history order and opens the initially expanded Q/A editor.
Wrapped/multiline text remains one segment; timestamp/source decoration is not question content.
(src/PhantomFudge.App/MainWindow.TranscriptSelection.cs:13-20,85-147;
[`InitializeQuestionView`](../src/PhantomFudge.App/MainWindow.Questions.cs).)

Selection closes source/setup/Notes panels and enters independent question editing, without
stopping transcription, creating a snapshot or sending a request. After a manual edit, direct
selection changes retain the draft/highlights with inline status until **Clear question**, rather
than destroying corrections or adding a confirmation popup. The manual **Choose source → Mic /
Computer / All → Use selected** replacement path remains available.
(src/PhantomFudge.App/MainWindow.TranscriptSelection.cs:94-147;
[`UseQuestionSelection_Click`](../src/PhantomFudge.App/MainWindow.Questions.cs).)

Older visible history remains selectable after the smaller chooser evicts its rows; archived
unfinished partials retain identity, and session separators cannot be selected. New finals follow
scrolling only when already at the end. Both selection paths enforce one capture session,
256entries and8192UTF-16 units including newlines atomically, with no truncation. Selected rows
preserve transparent backgrounds, solid white/black text and a thin accent outline.
(src/PhantomFudge.App/MainWindow.TranscriptSelection.cs:22-83,118-147;
[`QuestionDraft`](../src/PhantomFudge.Core/QuestionDraft.cs);
[`TranscriptRowStyle`](../src/PhantomFudge.App/MainWindow.xaml).)

This is a source-only increment via `scripts\Run-PhantomFudge.ps1`, not a newly packaged or qualified
release. The historical045022ZIP does not contain it. [Workstream44](../TODO/44_TRANSCRIPT_CLICK_SELECTION.todo)
retains final evidence/handbook completion separately from implemented behavior.

### Notes and presentation

1. **Prepare:** launch with a generic shell, initialize protection before displaying note
   content, type/paste or open a UTF-8 text file, and arrange the window.
2. **Rehearse:** use a synthetic test note and the exact screen-sharing configuration.
   Inspect another participant's view and, if relevant, a recording. Prefer sharing the
   presentation window/tab instead of the entire desktop where that meets the presentation need.
3. **Present:** explicitly enter read-only, non-activating mode. Read/scroll and drag from
   the grip without stealing slide keyboard focus. No file dialogs, suggestions, or note previews.
4. **Edit deliberately:** invoke the Edit command/hotkey; the window becomes focusable.
   This Notes action is separate from clicking transcript rows or using **Type / edit** for a
   question. No editing transition is triggered merely by pointer movement.
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
Default diagnostics use request/capture identities and metadata, never note hashes or snippets.
Transcript/question/answer bodies require explicit Text mode; notes, audio and tokens remain excluded.
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
click-through, acrylic/blur backgrounds, automatic meeting detection, plugins,
autoscroll/teleprompter controls, auto-update service, and encrypted vaults are not required
to deliver the approved v1. Reconsider them only through `18_FUTURE_SCOPE.todo`.

No process hiding, security-tool evasion, recorder tampering, injection, or custom driver
is part of this design. The simpler app remains honestly discoverable.
