---
title: "18. Extending the System"
description: "Surgical extension checklists for the Agency-first no-new-login boundary, explicit local models, common answer state and optional future Azure."
outline: deep
---

# 18. Extending the System

## Why a small feature can require a wide review

An extension is safe when its responsibilities are narrow **and its consequences are traced completely**. Adding a source is not just another selector; it affects byte framing, queue budgets, timestamp validation, stop behavior, display provenance, tests, and privacy wording. Adding a theme is not just changing a background; selection, disabled controls, alpha composition, and capture qualification also matter. The architecture explicitly prefers small testable interfaces over a generic plugin framework. (docs\ARCHITECTURE.md:35-54, component responsibilities; src\PhantomFudge.Core\AudioSource.cs:3-30, source identity; src\PhantomFudge.App\ThemeBrushes.cs:9-31, semantic resources.)

This chapter teaches change analysis; it does **not** add runtime capabilities. The reviewed local TODO28 slice established structured questions and exact Freeze. **The Agency path is now functionally smoke-tested; the installed adapter and local UI checks are complete under TODO39.** The former “Ask never sends” description applies only to SnapshotOnly. Source commit29 and direct Azure30/31 remain separate future work. (src\PhantomFudge.App\MainWindow.Questions.cs:231-256, `QuestionAsk_Click`; TODO\29_QUESTION_COMMIT_LATENCY.todo:13-22, protocol tasks; TODO\30_AZURE_ANSWER_CONNECTION.todo:13-22, prerequisites.)

Missing settings select SnapshotOnly, and the parent restored that mode after stopping the owned login. The owner requests existing installed Agency, with no new login or organization-data access; LocalModel remains separate. The earlier Copilot smoke failed at the customization/tool gate and consumed its allowance because a model call cannot be ruled out. The successful Agency probe had new, separate authorization, not a retry of that allowance. (src\PhantomFudge.Core\Answers\AnswerConfiguration.cs:13-29, `Load`; src\PhantomFudge.Core\Answers\AnswerContracts.cs:14-20, modes.) See [provider boundaries](../../docs/ANSWER-PROVIDERS.md) and [workstream39](../../TODO/39_INSTALLED_CLI_ANSWERS.todo).

The parent-run `tools\AgencyProbe\probe.py --allow-cloud` produced `PHANTOM_OK` after trimming, exit 0, in 69.8812573 seconds. Its [synthetic receipt](../../docs/answer-provider-smoke.json) records one `claude-sonnet-5` model call, zero model tools/assistant tool requests, disabled `github-mcp-server`/`githubiq`, and no observed new login using existing authentication; no app context was sent. This is functional smoke evidence, not a speed/quality benchmark. The adapter implements the exact observed launch/environment/profile; local code/replay/UI checks passed; release qualification remains separate. No actual LocalModel was installed or exercised—offline mocks only. Expiring credentials or later wrappers may require authentication, so the result is not a universal no-popup guarantee.

The key discipline is to write the invariant before the edit. “A selected source cannot borrow another source's queue allowance” is testable. “Support more audio” is not. Existing tests already express this distinction through canonical schemas, bounded per-source resources, independent timing, and explicit rejection of undeclared input. (services\transcription\tests\test_dual_protocol.py:57-92, `StartSchemaTests`; services\transcription\tests\test_dual_protocol.py:150-200, independent tracks; services\transcription\tests\test_dual_protocol.py:250-255, undeclared-source rejection.)

## Architecture: trace both directions across the boundary

For audio changes, follow data downstream from selected device to rendered transcript, then follow failures upstream to cancellation and cleanup. The local capture GUID is now attached to events before dispatcher delivery; a stale callback must not acquire the identity of a later Start. The unchanged display/export buffer and new structured question store are separate consumers, not interchangeable representations. (src\PhantomFudge.App\Audio\LoopbackTranscriber.cs:85-135, `StartCoreAsync`; src\PhantomFudge.App\Audio\TranscriptionSocket.cs:185-228, `TryEnqueueAudio`; services\transcription\server.py:317-333, `Session.recognize`; src\PhantomFudge.App\MainWindow.xaml.cs:739-761, `ReceiveTranscript`; src\PhantomFudge.App\MainWindow.Questions.cs:120-138, `UpdateQuestionTranscript`.)

```mermaid
---
config:
  theme: base
  themeVariables:
    lineColor: "#8b949e"
    textColor: "#e6edf3"
---
flowchart TB
    accTitle: Existing audio seams, IN PROGRESS Agency-first provider and PLANNED Azure
    accDescr: Typed events feed display and structured questions. ExactText alone enters the requested Agency-first adapter, still under validation, or optional future Azure. SnapshotOnly currently sends nothing; local installation does not prove offline inference.
    Source["Source identity"] --> Capture["WASAPI owner"]
    Capture --> Socket["Bounded socket"]
    Socket --> Service["Service schema"]
    Service --> Decoder["Recognizer"]
    Decoder --> Event["Typed event"]
    Event --> Buffer["Transcript buffer"]
    Buffer --> UI["Protected UI"]
    Event --> Store["Structured store"]
    Store -->|Explicit selection| Draft["Local draft"]
    Draft -->|Manual Ask| Frozen["Local Freeze"]
    Frozen --> UI
    Frozen -. ExactText when configured .-> CLI["IN PROGRESS Agency"]
    CLI -. AnswerEvent .-> UI
    Frozen -. Optional ExactText .-> Azure["PLANNED Azure"]
    Azure -. Future events .-> UI
    Policy["Tests and privacy"] -.-> Socket
    Policy -.-> Source
    Policy -.-> UI
    style Source fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Capture fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Socket fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Service fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Decoder fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Event fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Buffer fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Store fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Draft fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Frozen fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style UI fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style CLI fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Azure fill:#2d333b,stroke:#6d5dfc,color:#e6edf3,stroke-dasharray:5 5
    style Policy fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    linkStyle default stroke:#8b949e
```

This map is intentionally not a claim that every box is a formal interface. For example, Python expects recognizer objects with `accept` and `finish`, returning `Recognition`; C# injects a capture-construction delegate for tests. Understanding the actual seam prevents introducing abstraction machinery that the code does not need. (services\transcription\recognizer.py:14-19, `Recognition`; services\transcription\server.py:317-333, recognizer calls; src\PhantomFudge.App\Audio\LoopbackTranscriber.cs:17-27, capture injection.)

## Checklist 1: a new audio source

**Start with product meaning.** Define what the source captures, how the user chooses it, and what its label does not mean. Current `Computer` and `Mic` labels identify inputs, not speakers; no source addition should silently become diarization or automatic meeting discovery. (src\PhantomFudge.Core\AudioSource.cs:16-30, `AudioSourceNames`; tests\PhantomFudge.Core.Tests\SourceTranscriptTests.cs:81-87, `LabelsDescribeInputNotSpeaker`.)

Then inspect these concrete surfaces:

1. **Identity and selection:** extend the enum and explicit selection model deliberately. `AudioInputSelection` currently has only two device fields; `ValidateSelection` constructs at most two inputs and rejects duplicate device IDs. A new enum value alone is insufficient. (src\PhantomFudge.App\Audio\TranscriptEvent.cs:5-7, selection records; src\PhantomFudge.App\Audio\LoopbackTranscriber.cs:141-155, `ValidateSelection`.)
2. **Native ownership:** specify endpoint direction and exact identity matching, construction failure, disconnect, stop, and disposal. `WasapiInput` distinguishes Render from Capture and releases both endpoint notifications and native capture ownership. Do not substitute a default device after selection fails. (src\PhantomFudge.App\Audio\WasapiInput.cs:20-74, constructor and `MatchesSelection`; src\PhantomFudge.App\Audio\WasapiInput.cs:97-119, `Dispose`.)
3. **Transport capacity:** audit fixed arrays of length three, allowed source counts, byte prefixes, and total pending-byte limits. Those structures encode the present two-source contract. Update resource arithmetic explicitly rather than increasing a limit until tests stop failing. (src\PhantomFudge.App\Audio\TranscriptionSocket.cs:13-18, limits; src\PhantomFudge.App\Audio\TranscriptionSocket.cs:51-56, source-indexed fields; src\PhantomFudge.App\Audio\TranscriptionSocket.cs:95-105, source validation.)
4. **Server symmetry:** align `SOURCES`, canonical source ordering, declared-source validation, per-source rate credits, and queue accounting. The existing worker must still publish every selected source's final before stopped. (services\transcription\server.py:23-25, source map; services\transcription\server.py:94-106, `parse_start`; services\transcription\server.py:255-287, `Session.receive`; services\transcription\server.py:317-327, stop drain.)
5. **UI, tests, privacy, documentation:** update selectors, meters, source visibility, export labels, denied-device behavior, and user-facing capture scope. Existing UI branches explicitly on Computer/Microphone/Both; do not let a new value fall into an accidental “Both” default. (src\PhantomFudge.App\MainWindow.xaml.cs:668-706, `UpdateAudioControls`, `SetCaptureMode`; tests\PhantomFudge.Audio.Tests\DualSourceTests.cs:17-44, synthetic regression catalogue.)
6. **Question provenance:** update `TranscriptValidation.Source`, structured keys and chooser filters as well as display names. The Mic default must not silently select a new source. All sources is a visibility filter, not authorization to include every row; Use selected still copies only explicit keys. Test new-source partial/final identity and local provenance retention separately from export formatting. (src\PhantomFudge.Core\TranscriptSegment.cs:5-14, `TranscriptSegmentKey`; src\PhantomFudge.Core\TranscriptSegment.cs:76-80, source validation; src\PhantomFudge.App\MainWindow.Questions.cs:61-106, filtering/selection.)

## Checklist 2: a new recognition backend

Begin at the recognition contract, not the model download. The service requires a `Recognition` object, bounded text, finite ordered times, and final results at stop. Its runtime serializes CPU calls through one executor and marks readiness failed after a decoder failure; a replacement must respect those ownership and timeout semantics. (services\transcription\server.py:109-142, `Runtime.cpu`; services\transcription\server.py:289-333, `Session.publish`, `recognize`.)

Specify the backend's answers to five questions:

- Does `accept` produce useful revisable partials or only completed chunks?
- Does `finish` flush every processed sample, and does it permit continuation?
- Are timestamps cumulative, and how are reset offsets preserved?
- Can independent source decoders share immutable model weights without sharing recognition history?
- Are inference and setup strictly separated so recognition cannot trigger an implicit download?

These questions follow directly from `VoskFactory`'s verified explicit model path and `VoskRecognizer`'s independent decoder, 30-second rotation, and finalization behavior. Synthetic decoder tests check absolute timing and source independence. (services\transcription\recognizer.py:22-52, `verify_model`, `VoskFactory`; services\transcription\recognizer.py:55-96, `VoskRecognizer`; services\transcription\tests\test_recognizer.py:37-75, `RecognizerTests`.)

Do not advertise batch throughput as streaming latency. The existing Whisper result supports an **optional question-only refinement experiment**, not replacement of production Vosk. Refinement must respect the now-implemented edit/snapshot boundary: it cannot overwrite user-edited text or mutate an already-frozen local question. Backend changes still need protocol, UI, privacy, model-provenance and benchmark review even if their wire shape stays unchanged. (docs\SPEECH-BENCHMARK.md:29-35, recommendation; docs\AI-ANSWER-RESEARCH.md:87-96, optional refinement; src\PhantomFudge.Core\QuestionDraft.cs:48-79, edit/refresh; src\PhantomFudge.Core\QuestionSnapshot.cs:5-23, immutability.)

## Checklist 3: a new theme or semantic color role

Theme changes have a narrower code surface but a wider visual evidence surface. `ThemePalette.For` provides semantic colors; `ThemeBrushes.Apply` maps those roles into both application resources and standard WPF control resources. Adding a role requires checking each palette and its consumers, not inserting an isolated literal into one control. (src\PhantomFudge.Core\ThemePalette.cs:5-25, `ThemePalette.For`; src\PhantomFudge.App\ThemeBrushes.cs:9-31, `Apply`.)

Review text, muted text, headings, selection foreground/background, action text, warning/error, input, hover, and border states. Preserve frozen brushes and dynamic-resource bindings. Also decide whether a new theme participates in the two-theme toggle: `NextThemed` currently alternates the companions and exits Plain toward DeepSeaFoam. That behavior is tested and should not change accidentally. (src\PhantomFudge.App\ThemeBrushes.cs:34-40, `Brush`; src\PhantomFudge.App\MainWindow.xaml:15-55, dynamic resources; src\PhantomFudge.Core\ThemePalette.cs:27-28, `NextThemed`; tests\PhantomFudge.Core.Tests\ThemePaletteTests.cs:19-25, toggle test.)

The background slider changes only the root brush alpha. Do not “simplify” it into whole-window opacity, which would fade text and change the product contract. Existing contrast tests cover **opaque** reading/selection pairs; they do not certify readability over arbitrary content behind a translucent window. Recheck actual UI, focus/hit regions, and applicable capture scenarios without promoting local rendering evidence into meeting compatibility. (src\PhantomFudge.App\MainWindow.xaml.cs:454-477, opacity and `ApplyTheme`; tests\PhantomFudge.Core.Tests\ThemePaletteTests.cs:31-40, opaque contrast test; docs\QUALIFICATION.md:111-137, transition matrix.)

Include the local question pane in that review: selected rows, editable/read-only draft, provisional/user-edited indicators and read-only snapshot are different visual states. Verify that theme switching preserves their text; an immutable snapshot alone does not prove visual correctness, recipient invisibility or every contrast combination. (src\PhantomFudge.App\MainWindow.Questions.cs:173-182, interaction states; src\PhantomFudge.App\MainWindow.Questions.cs:213-226, provenance display; src\PhantomFudge.Core\QuestionSnapshot.cs:5-23, immutable snapshot.)

## Checklist 4: a provider is a new trust boundary

Answer providers must stay separate from local speech transport. Do not loosen `ValidateEndpoint` to accommodate cloud inference or a model server: it deliberately accepts only literal loopback addresses and the exact transcription path. A second loopback API is not the same protocol simply because both run locally. (src\PhantomFudge.App\Audio\TranscriptionSocket.cs:73-89, `ValidateEndpoint`.)

The future Azure checklist—explicit Ask, destination approval, identity, bounds, cancellation, terminal reasons and safe output—still applies. Installed-CLI integration makes those concerns immediate rather than proving them solved. Render text as data, not XAML/HTML, commands or automatically opened links. Offline fake-stream/process checks must precede a real-call receipt. (TODO\31_STREAMING_ANSWER_CLIENT.todo:13-28, provider contract; TODO\34_ANSWER_PRIVACY_RESILIENCE.todo:13-28, failure/privacy gates; docs\AI-ANSWER-PRIVACY.md:112-129, output and logging boundaries.)

The snapshot remains a **local** provenance contract, not a provider DTO. Repeated Freeze can retain the same question ID/revision, so an answer attempt needs its own identity. Keep mode-specific notices: “nothing sent” is correct for SnapshotOnly, not a guarantee for configured Agency, whose model service may be remote. (src\PhantomFudge.Core\QuestionSnapshot.cs:5-23, snapshot data; src\PhantomFudge.Core\QuestionDraft.cs:90-95, `Freeze`; src\PhantomFudge.App\MainWindow.Questions.cs:231-256, snapshot action.)

Review these additional concrete surfaces before extending the new common interface:

1. **Request projection:** `AnswerRun` generates a fresh GUID and passes only `ExactText`; `SourceRefs`, notes, audio and original unedited selections stay outside `AnswerRequest`. Do not widen that type merely to simplify provider serialization. (src\PhantomFudge.Core\Answers\AnswerContracts.cs:3-12, request/events/interface; src\PhantomFudge.Core\Answers\AnswerRun.cs:15-26, projection.)
2. **Settings versus readiness:** Agency mode adds `AgencyPath` for the supported `agency copilot` launcher; local installation is not offline inference or authorization to inspect organization data. LocalModel remains the separate Copilot-engine OFFLINE/BYOK route to an explicit literal-loopback `/v1` server. Destination validation cannot verify model installation or prevent server-side proxying. No hosted-model fallback is permitted. (src\PhantomFudge.Core\Answers\AnswerContracts.cs:22-55, settings validation.)
3. **Executable and profile authority:** `Initialize-Answers.ps1 -Provider Agency` resolves the executable without login; a `.exe` path does not prove authenticity or isolation. Intended controls are profile-only, no-default-mcps, no-config-plugins, no-aec and no model tools. Do not import personal hooks, permissions, plugins, MCPs or resumed sessions, and do not initiate new login/account switching when existing model authentication is unavailable. These are adapter-validation obligations, not an OS sandbox or organization-data grant. (src\PhantomFudge.Core\Answers\AnswerContracts.cs:28-33, syntactic validation; src\PhantomFudge.Core\Answers\AnswerConfiguration.cs:8-29, profile/settings paths.)
4. **Terminal protocol:** current events are TextDelta/Completed with request ID and strictly increasing sequence. Completion carries no text and is only latched; clean EOF plus nonblank text is also required. Test duplicate, late, wrong-request and post-terminal events without turning raw CLI diagnostics into answers. The 32,768-unit cap rejects a whole overflowing delta. (src\PhantomFudge.Core\Answers\AnswerRun.cs:31-62, `Apply`, `Complete`.)
5. **Ownership and retention:** the common UI uses one active request, a 120-second deadline and 40-ms render timer. Cancel/Hide/Clear/protection/close must stop the owned process and await cleanup; no automatic retry or resend is allowed. Retain partials as incomplete, mark explicit copies accordingly, and distinguish Clear from cancellation. The CLI can persist prompts/sessions, so app memory bounds do not establish end-to-end memory-only operation. (src\PhantomFudge.Core\Answers\AnswerRun.cs:64-78, `Cancel`, `Fail`.) Verify integration details in the [provider guide](../../docs/ANSWER-PROVIDERS.md), not by assuming Core state terminates a process.

Cloud ASR changes audio egress; an enterprise broker changes authentication, routing, hosting, and streaming behavior. Neither is a routine interchangeable implementation detail. Both need separately justified policy and evaluation decisions, and neither makes Docker an ordinary runtime requirement. (TODO\36_OPTIONAL_AZURE_SPEECH.todo:13-25, optional Speech; TODO\37_OPTIONAL_ANSWER_BROKER.todo:13-26, optional broker.)

## Checklist 5: a new state field is a lifecycle change

For every proposed field, write down its owner, creation boundary, mutation authority, reset rule, serialization version, memory bound and logging/export policy. Existing display-buffer `BeginSession` resets final numbering, while `AddFinal` deduplicates globally within that session. The new structured key therefore separates capture identity from protocol final numbering. (src\PhantomFudge.Core\TranscriptBuffer.cs:23-38, `BeginSession`; src\PhantomFudge.Core\TranscriptBuffer.cs:48-72, `AddFinal`; src\PhantomFudge.Core\TranscriptSegment.cs:3-20, `TranscriptSegmentKey`.)

The implemented local fields provide a concrete lifecycle checklist:

| Field or owner | Preserve this invariant when extending it |
| --- | --- |
| Capture GUID / local utterance ID | New capture identity per Start; partial→final retains its local key. Store Clear does not recycle local IDs. |
| Source epoch | Current UI events use local 0; a field's existence is not wire negotiation or commit support. |
| Selected records | Copies survive 256-entry/64,000-character store eviction; reject cross-session selection. |
| Draft revision / user edit | Compose and validate the full 8,192-character candidate before assignment; explicit editing blocks ASR replacement. |
| Frozen snapshot | Copy provenance/text; future draft changes cannot mutate a previously returned snapshot. |
| Answer request/run | New GUID per attempt, exact-text-only request, increasing event sequence and a terminal marker distinct from completed state. |

Sources: (src\PhantomFudge.Core\StructuredTranscriptStore.cs:13-34, capacity/session;
src\PhantomFudge.Core\StructuredTranscriptStore.cs:76-109, final identity/Clear);
(src\PhantomFudge.App\MainWindow.Questions.cs:120-128, epoch-default calls);
(src\PhantomFudge.Core\QuestionDraft.cs:18-79, selection/refresh transaction);
(src\PhantomFudge.Core\QuestionSnapshot.cs:5-23, immutable copy).

The answer fields have their own lifecycle in (src\PhantomFudge.Core\Answers\AnswerRun.cs:15-62, constructor, `Apply`, `Complete`). They must not reset transcription IDs or turn a new draft revision into a hidden resend.

An empty final is not permission to retain the last guessed partial: it settles that selected record to empty. Blank editing remains possible, but blank Ask rejects. Oversized selection, edit or refresh must leave the prior draft intact; do not “help” by truncating its meaning. Preserve the staged-update pattern when adding fields. (src\PhantomFudge.Core\StructuredTranscriptStore.cs:80-86, empty final; src\PhantomFudge.Core\QuestionDraft.cs:90-124, `Freeze`, `Compose`; src\PhantomFudge.App\MainWindow.Questions.cs:187-210, edit/paste rejection.)

Wire evolution is especially surgical. The current service requires exact start objects, accepts only Stop after start, and the C# client checks exact response property sets. Even an apparently optional extra JSON field can break compatibility. Preserve v1/v2 behavior and negotiate a new version or capability rather than silently weakening parsers. (services\transcription\server.py:94-106, `parse_start`; services\transcription\server.py:247-252, `Session.receive`; src\PhantomFudge.App\Audio\TranscriptionSocket.cs:482-506, `RequireTranscriptProperties`, `RequireProperties`.)

Focus is also state, not decoration. `PassiveWindow` depends on presentation **and** question editing. The separate question path deliberately focuses typing without stopping audio or enabling Notes editing; global Notes Edit/Present/Hide behavior is not redefined. New answer controls must preserve that distinction while adding their own cancellation lifetime. Check protection readiness, file-busy rejection, return to passive interaction and audio-start cancellation independently. (src\PhantomFudge.App\MainWindow.Questions.cs:22-23, `PassiveWindow`; src\PhantomFudge.App\MainWindow.Questions.cs:141-182, focus transition.)

## A repeatable change algorithm

```mermaid
---
config:
  theme: base
  themeVariables:
    lineColor: "#8b949e"
    textColor: "#e6edf3"
---
flowchart TB
    accTitle: Recommended surgical extension workflow
    accDescr: Define an invariant, map contracts, design compatibility, test failures, and qualify the exact authorized candidate.
    Need["Define invariant"] --> Map["Map change surfaces"]
    Map --> Contract["Version contracts"]
    Contract --> Fake["Synthetic failure tests"]
    Fake --> Review["Privacy and UI review"]
    Review --> Gate{"Evidence sufficient?"}
    Gate -->|No| Revise["Revise or block"]
    Revise --> Contract
    Gate -->|Yes| Qualify["Exact candidate checks"]
    Qualify --> Record["Document limits"]
    style Need fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Map fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Contract fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Fake fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Review fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Gate fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Revise fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Qualify fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Record fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    linkStyle default stroke:#8b949e
```

This is a recommended engineering workflow, not an executed release pipeline. Qualification must identify the exact packaged candidate and distinguish source tests, UI observations, recipient capture evidence, and owner approval. A successful build cannot fill an untested sharing-product row. (docs\QUALIFICATION.md:3-7, current matrix; docs\QUALIFICATION.md:142-168, result record; TODO\35_ANSWER_RELEASE_ACCEPTANCE.todo:13-26, candidate acceptance.)

### Worked pseudocode: evolving local epochs into negotiated commit

`SourceEpoch` already exists locally; current v2 ingestion supplies 0. TODO29 requires a producer/consumer agreement for armed start/end boundaries, ordered sample coverage and acknowledged epoch advancement—not merely another field. The following **future commit** matrix extends existing local guards; its transition functions are illustrative, not implemented APIs. (src\PhantomFudge.Core\TranscriptSegment.cs:17-20, key fields; src\PhantomFudge.App\MainWindow.Questions.cs:120-128, v2 ingestion.)

```python
cases = [
    ("same_session_same_epoch", "accept"),
    ("old_session_same_segment_id", "reject_stale"),
    ("old_epoch_after_commit", "reject_stale"),
    ("duplicate_commit_same_parameters", "replay_ack"),
    ("duplicate_commit_changed_watermark", "reject"),
    ("late_final_after_user_edit", "preserve_user_revision"),
    ("selected_record_evicted", "preserve_local_copy"),
    ("freeze_then_late_final", "preserve_exact_snapshot"),
    ("new_field_sent_to_v2_peer", "reject_protocol"),
]
for scenario, expected in cases:
    state, event = synthetic_fixture(scenario)
    before = snapshot_unrelated_source(state)
    actual = proposed_transition(state, event)
    assert actual == expected
    assert unrelated_source(state) == before
    assert no_request_was_sent_without_explicit_ask(state)
```

This matrix connects protocol changes to UI/privacy consequences. Existing local guards do not establish a sample-exact live-Mic span from v2 text. Future commit must finalize only Mic, preserve Computer capture and use processed—not merely received—sample coverage. Tests should use generated data and injected capture, not silently open hardware. (tests\PhantomFudge.Core.Tests\SourceTranscriptTests.cs:5-56, source isolation tests; src\PhantomFudge.App\Audio\LoopbackTranscriber.cs:24-27, synthetic capture seam; TODO\29_QUESTION_COMMIT_LATENCY.todo:13-28, proposed commit requirements.)

## Tradeoffs and the final change-surface review

Before calling a surgical change complete, review six surfaces even when some require no edit:

| Surface | Question to answer in the change record |
| --- | --- |
| UI | Did focus, protection lifecycle, source visibility, or status meaning change? |
| Protocol | Can an older peer reject, misinterpret, or accept an unsupported message? |
| Tests | Which existing invariant survives, and which new failure ordering is exercised? |
| Privacy | Did captured, transmitted, retained, logged, or exported data expand? |
| Resources | Are per-source limits, total limits, deadlines, and cleanup still bounded? |
| Documentation | Are implemented behavior, proposed behavior, and qualification gaps still distinguishable? |

This checklist synthesizes the existing strict transport, resource controls, and qualification record rather than introducing a new runtime framework. “No change required” is useful only when accompanied by the reason: a palette-only edit may leave framing unchanged, while a provider edit necessarily revisits egress and credential handling. (src\PhantomFudge.App\Audio\TranscriptionSocket.cs:13-18, limits; src\PhantomFudge.App\Audio\TranscriptionSocket.cs:503-506, strict properties; docs\QUALIFICATION.md:142-168, evidence record; docs\AI-ANSWER-PRIVACY.md:112-131, provider safeguards.)

Prefer a smaller supported contract over a permissive parser or automatic fallback that conceals unsupported behavior. Preserve failed and untested outcomes in the handoff: a new backend passing synthetic tests does not qualify a new window surface, a real microphone, or a cloud data policy. (docs\QUALIFICATION.md:24-26, outcome vocabulary; TODO\35_ANSWER_RELEASE_ACCEPTANCE.todo:23-26, acceptance boundaries.)

## Exercises

1. Find every fixed two-source assumption in the checklist. Explain why changing only the enum can compile in one layer while violating another layer's runtime schema. (src\PhantomFudge.App\Audio\TranscriptionSocket.cs:51-56, arrays; services\transcription\server.py:94-106, `parse_start`.)
2. Propose a warning color with acceptable opaque contrast, then explain why translucent-background readability still needs separate observation. (tests\PhantomFudge.Core.Tests\ThemePaletteTests.cs:31-57, contrast calculation; docs\PRODUCT.md:105-112, translucency limits.)
3. Draft a provider change receipt that records capability and privacy differences without including a real question, token, or account secret. (docs\AI-ANSWER-EVALUATION.md:137-146, receipt requirements.)
4. Select a partial, freeze it, edit the draft, then deliver an empty final and evict the original store row. Explain each object's expected text and why `SourceRefs` must not become the Azure request body. (src\PhantomFudge.Core\QuestionDraft.cs:48-95, edit/refresh/Freeze; src\PhantomFudge.Core\QuestionSnapshot.cs:5-23, local immutable provenance.)
5. On paper, apply TextDelta(1), Completed(2), then TextDelta(3) to one run; contrast that with EOF before Completed. Explain why neither stream may be reported complete and why neither failure should trigger provider fallback. (src\PhantomFudge.Core\Answers\AnswerRun.cs:31-62, event/terminal guards.)

## References

- [Architecture responsibilities](../../docs/ARCHITECTURE.md) and [qualification contract](../../docs/QUALIFICATION.md).
- [Source model](../../src/PhantomFudge.Core/AudioSource.cs), [transport](../../src/PhantomFudge.App/Audio/TranscriptionSocket.cs), and [recognizer](../../services/transcription/recognizer.py).
- [Theme roles](../../src/PhantomFudge.Core/ThemePalette.cs) and [brush mapping](../../src/PhantomFudge.App/ThemeBrushes.cs).
- [Local question contract](../../docs/LOCAL-QUESTIONS.md), [structured store](../../src/PhantomFudge.Core/StructuredTranscriptStore.cs), and [question diagnostics](../../src/PhantomFudge.App/MainWindow.QuestionDiagnostics.cs).
- [Installed-provider boundaries](../../docs/ANSWER-PROVIDERS.md), [common interface](../../src/PhantomFudge.Core/Answers/AnswerContracts.cs), [run state](../../src/PhantomFudge.Core/Answers/AnswerRun.cs), and [configuration](../../src/PhantomFudge.Core/Answers/AnswerConfiguration.cs).
- [Source-state tests](../../tests/PhantomFudge.Core.Tests/SourceTranscriptTests.cs) and [dual-protocol tests](../../services/transcription/tests/test_dual_protocol.py).
- [Planned answer architecture](../../docs/AI-ANSWER-ARCHITECTURE.md) and [release acceptance](../../TODO/35_ANSWER_RELEASE_ACCEPTANCE.todo).
