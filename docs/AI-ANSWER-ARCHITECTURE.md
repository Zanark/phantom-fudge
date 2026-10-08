---
title: "Provider-independent questions, installed Agency answers and future Azure"
description: "Exact snapshots and bounded answer state, with Agency-first integration requiring no new login or organization-data access, plus local-model and future Azure options."
outline: deep
---

# Architecture: choose locally, freeze exactly, select the answer destination

**Status: the Agency path is functionally smoke-tested; the installed adapter and local UI checks are complete under TODO39.**
The completed TODO28 slice supplied structured selection, editing and immutable Freeze, not an
answer service. That boundary remains: freezing text is distinct from invoking a configured provider.
**SnapshotOnly is the unconfigured default, not a promise that configured Ask never sends data.**
(src\PhantomFudge.App\MainWindow.Questions.cs:231-256, `QuestionAsk_Click`.)

The common answer contracts and coordinator are written. Following an organization-authentication
popup, the owner rejected new login and organization-data access, requesting the existing installed
Agency background-prompt path instead. The parent stopped the owned login and reset the app to
**SnapshotOnly**. A separately authorized Agency probe has now succeeded using existing authentication,
without an observed new login flow. Agency uses a remote model service: local installation is not offline inference.
The explicit LocalModel OFFLINE/BYOK alternative remains separate from Agency.
(src\PhantomFudge.Core\Answers\AnswerContracts.cs:3-20, provider contract/settings;
src\PhantomFudge.Core\Answers\AnswerConfiguration.cs:13-29, `Load`.)

Accurate armed live-Mic boundaries/source commit remain TODO29; an approved Azure profile remains
unknown under TODO30. TODO30/31 retain optional direct Azure work rather than blocking CLI answers.
Commit is not required to ask explicitly selected or typed text. The previously authorized
**single Copilot cloud smoke failed at the customization/tool gate**. Its allowance is consumed:
a model call cannot be ruled out, and that Copilot attempt has no successful-answer receipt. Do not retry it or
reinterpret that authorization as an unused test. See [provider setup and boundaries](ANSWER-PROVIDERS.md),
[workstream39](../TODO/39_INSTALLED_CLI_ANSWERS.todo), [local usage](LOCAL-QUESTIONS.md), [research](AI-ANSWER-RESEARCH.md),
[privacy/identity](AI-ANSWER-PRIVACY.md) and [evaluation](AI-ANSWER-EVALUATION.md).

The **new, separately authorized single Agency prompt** was run by the parent with
`tools\AgencyProbe\probe.py --allow-cloud`, not by this documentation update.
The [synthetic receipt](answer-provider-smoke.json) records `PHANTOM_OK` after trimming, exit 0,
69.8812573 seconds elapsed, one model call using observed model `claude-sonnet-5`, zero model tools
and zero assistant tool requests; `github-mcp-server` and `githubiq` were disabled. No app context
was sent. This is functional smoke evidence, **not a speed/quality benchmark or final app acceptance**.
The adapter is replacing its defensive stub with the observed launch/environment/profile; publishing
passed code, recorded-event replay and synthetic UI checks. No actual local model was installed or exercised—only offline
mocks cover that option. Existing authentication may expire: the observed no-login run does not
guarantee that future wrapper versions or credentials can never produce authentication UI.

## 1. The recommended baseline

```mermaid
---
config:
  theme: base
  themeVariables:
    lineColor: "#8b949e"
    textColor: "#e6edf3"
---
flowchart TB
    accTitle: Local snapshots, IN PROGRESS Agency-first path and PLANNED Azure
    accDescr: SnapshotOnly is the no-send state. One separately authorized Agency probe succeeded through its remote model service, but local adapter checks are complete. LocalModel is a separate option with mock-only evidence; no fallback is implied.
    subgraph Local["Local protected desktop"]
        ASR["Local ASR"] --> Store["Structured store"]
        Store -->|Use selected| Draft["Selected draft"]
        Draft -->|Explicit Ask| Snapshot["Local snapshot"]
        Snapshot --> Refs["Local SourceRefs"]
        Snapshot --> Choice{"Provider choice"}
        Choice -->|Default| NoSend["Snapshot only"]
        Choice -->|Configured| Request["ExactText request"]
        Request -. IN PROGRESS .-> CLI["Installed launcher"]
        Answer["Common answer UI"]
    end
    subgraph Targets["Explicit provider destinations"]
        Agency["Agency model service"]
        Loopback["Explicit loopback model"]
    end
    Azure["PLANNED direct Azure"]
    CLI -. Agency choice .-> Agency
    CLI -. LocalModel choice .-> Loopback
    CLI -. AnswerEvent .-> Answer
    Request -. Optional future .-> Azure
    Azure -. Future events .-> Answer
    style ASR fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Store fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Draft fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Snapshot fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Refs fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Choice fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style NoSend fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Request fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style CLI fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Answer fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Agency fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Loopback fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Azure fill:#2d333b,stroke:#6d5dfc,color:#e6edf3,stroke-dasharray:5 5
    style Local fill:#161b22,stroke:#30363d,color:#e6edf3
    style Targets fill:#161b22,stroke:#30363d,color:#e6edf3,stroke-dasharray:5 5
    linkStyle default stroke:#8b949e
```

Solid arrows describe common local dataflow; dashed paths require provider integration or future Azure.
The draft copies selected records, and `Freeze` captures text and provenance independently.
`SourceRefs` is **local metadata**, not permission to transmit the selected source records.
Every adapter must consume `AnswerRequest`, **not serialize the whole `QuestionSnapshot`**.
Additional bounded context would require its own explicit policy; none is implicitly added now.
(src\PhantomFudge.Core\QuestionDraft.cs:18-45, `ReplaceSelection`;
src\PhantomFudge.Core\QuestionSnapshot.cs:5-23, `QuestionSnapshot`;
docs\AI-ANSWER-PRIVACY.md:6-20, proposed text boundary.)

Python remains the local speech specialist; C# owns question and answer orchestration. The CLI owns
its own authentication/session behavior, while a future direct Azure adapter would own that path.
Do not add a local HTTP hop through Python solely because it already hosts Vosk. Do not put model
network work in the native capture callback or ASR CPU worker. The question handler establishes
the exact snapshot boundary; provider execution belongs to a separate asynchronous coordinator.
(src\PhantomFudge.App\MainWindow.Questions.cs:231-256, `QuestionAsk_Click`.)

| Choice | Execution and evidence boundary |
| --- | --- |
| `SnapshotOnly` | Current reset state and missing-settings default; Freeze remains local and no model is invoked. |
| `Agency` | Requested primary path: one synthetic `agency copilot` probe succeeded with existing authentication and no observed login; adapter/replay/UI checks passed. |
| `Copilot` | Earlier direct-login route, not the owner's selected path; smoke failed and no further login/retry is authorized by that consumed allowance. |
| `LocalModel` | Separate OFFLINE/BYOK option against literal loopback; offline mocks only, no actual local model installed or exercised. |
| Direct Azure | Optional future adapter; not a current `AnswerProviderMode` value or configured resource. |

`AnswerSettings.AgencyPath` identifies the installed Agency executable; see the
[Agency setup entry point](ANSWER-PROVIDERS.md). The enum describes selectable contracts, not verified
machine readiness. `AnswerSettings.Validate`
requires a local model identifier and accepts only HTTP literal `127.0.0.1` or `[::1]` endpoints with
`/v1` or `/v1/`, no credentials/query/fragment. An optional CLI path must be a fully qualified `.exe`
path, not a shell command; that syntax check does not authenticate the executable. A reachable
loopback server can itself proxy elsewhere, so locality also requires control of that server.
(src\PhantomFudge.Core\Answers\AnswerContracts.cs:14-55, `AnswerSettings.Validate`, `ValidateLocalEndpoint`.)

**PLANNED Azure footprint:** an approved Azure OpenAI/Foundry model resource and deployment,
public-client registration/user RBAC, and optional metadata-only monitoring/budget controls.
Prefer reusing approved existing resources. No hosted agent, database, queue, RAG index, AKS,
container runtime or mandatory gateway is needed for ordinary technical Q&A.

Recommended evaluation candidates are 5.4-mini, 5.4-nano and 5-mini with their exact versions/lifecycle
from the research. No model is selected as "fastest" without the actual approved deployment test.
Use a pay-per-token deployment type whose processing geography is explicitly acceptable.
(docs\AI-ANSWER-RESEARCH.md:136-158, model/deployment shortlist.)

## 2. User experience: no accidental whole-meeting prompts

Example:

1. A chat participant asks "Explain Service Fabric like I'm five."
2. The owner reads it aloud. The Mic transcript updates locally, independently of Computer audio.
3. The owner selects the intended Mic rows and presses **Use selected**, or types/corrects the
   question using **Type / edit**. Computer and All sources are deliberate alternative filters.
4. **Freeze locally** in SnapshotOnly creates the snapshot without a request. In the in-progress
   configured path, **Ask provider** freezes the same draft and starts one exact-text answer request.
5. The common answer view identifies the chosen provider and submitted question. It must distinguish
   completed output from cancelled/failed partial output; a local CLI process is not an offline badge.

The app never reads the chat to obtain that question. It does not auto-answer when it hears a
question mark, forward all Mic text, or include unrelated Computer text as hidden context.
All sources broadens the chooser; it does not automatically select those rows. Selection is
key-based, and an empty selection retains the existing draft.
(src\PhantomFudge.App\MainWindow.Questions.cs:61-114, filters and `UseQuestionSelection_Click`;
src\PhantomFudge.App\MainWindow.Questions.cs:231-249, explicit local `QuestionAsk_Click`.)

### Implemented Freeze versus planned Finish dictation

- **Ask as shown:** freeze the current editable/selected draft immediately. A provisional draft
  is visibly labelled as such; the snapshot is taken without waiting for ASR. SnapshotOnly ends
  there; configured provider mode explicitly requests an answer. Recognition never triggers a send.
- **PLANNED Finish dictation:** commit the armed Mic span and update the draft from its acknowledged final
  segments. The owner then presses Ask. A later optional Finish-and-Ask shortcut may compose these
  two explicit actions, with the same source/watermark/cancellation guarantees.

The implemented revision/provisional indicators and Freeze operation establish the manual boundary
without repurposing global Stop. The future commit timeout must retain the draft and show failure,
not silently submit an alternative. Armed start/end spans are not implemented by selecting v2 text.
(src\PhantomFudge.App\MainWindow.Questions.cs:213-256, rendering and Ask;
src\PhantomFudge.Core\QuestionDraft.cs:90-95, `Freeze`.)

### Layout and focus

Transcript stays primary. The implemented inline Question pane lives in the same protected HWND,
with explicit selection, typing, Copy, Clear and a local snapshot display. Expansion rearranges
existing panels; it does not create a second window. The common answer coordinator now adds provider
choice, a plain-text answer, Cancel and explicit Copy within that same HWND, under TODO39 review.
(src\PhantomFudge.App\MainWindow.Questions.cs:33-58, `SetQuestionExpanded`, `ResizeQuestionPanel`;
src\PhantomFudge.App\MainWindow.Questions.cs:231-287, Ask/Copy/Clear handlers.)

Passive row selection avoids unnecessary activation. **Type / edit deliberately takes keyboard
focus without stopping transcription or enabling Notes editing.** `SetQuestionEditing` changes the
question-editing flag, refreshes interaction/protection through hide/reveal and focuses the editor.
It does not call the global Notes Edit/Present transition. Existing global Edit/Present/Hide audio
semantics are unchanged. Preserve independent Notes state and audio-start cancellation when
extending this question-only focus path.
(src\PhantomFudge.App\MainWindow.Questions.cs:87-93, passive selection;
src\PhantomFudge.App\MainWindow.Questions.cs:141-182, question focus.)

No generated speech/TTS initially: it adds latency and can feed back into Computer transcription.
No automatic clipboard copy, link opening, code execution or reply-posting to a meeting/chat.
Copy draft is explicit and copies only draft text; the Windows clipboard is outside window-capture
protection. Clear question discards the current draft and last snapshot, not notes/transcription.
(src\PhantomFudge.App\MainWindow.Questions.cs:259-287, Copy/Clear;
docs\AI-ANSWER-PRIVACY.md:112-129, proposed output safeguards.)

## 3. Local data model

The flattened `TranscriptBuffer` remains the unchanged display/export model. Question selection now
uses a separate structured store: presentation prefixes are never parsed as source identity.
The store is bounded to **256 segments and 64,000 text characters**; the draft is bounded to
**8,192 characters**, measured using .NET string length, not tokens or UTF-8 bytes.
(src\PhantomFudge.Core\TranscriptBuffer.cs:6-19, display storage;
src\PhantomFudge.Core\StructuredTranscriptStore.cs:13-18, capacity defaults;
src\PhantomFudge.Core\QuestionDraft.cs:8-9, bounds;
src\PhantomFudge.App\MainWindow.Questions.cs:96-106, key-based selection.)

| Implemented record | Identity and ownership |
| --- | --- |
| `TranscriptSegmentKey` | Capture GUID, source, local source epoch and stable local utterance ID. |
| `TranscriptSegment` | Immutable revision/text/finality, optional protocol final ID, qualified audio-relative seconds and active-partial state. |
| `QuestionDraft` | Question ID, revision, exact editable text, copied selected records, provisional and user-edited flags. |
| `QuestionSnapshot` | Question ID/revision, `ExactText`, copied local `SourceRefs`, provisional/edit flags and monotonic creation timestamp. |

Sources: (src\PhantomFudge.Core\TranscriptSegment.cs:3-65, identity and immutable record);
(src\PhantomFudge.Core\QuestionDraft.cs:8-16, draft fields);
(src\PhantomFudge.Core\QuestionSnapshot.cs:5-23, snapshot fields). Profile/version, context policy,
sample-exact start/end boundaries and answer-request IDs are **not** current snapshot fields.

A new local capture GUID is established per Start and stamped before dispatcher delivery.
This is a desktop envelope, not new v2 wire metadata. The structured consumer accepts that GUID,
uses local epoch **0** through the current overload defaults, and rejects obsolete sessions/epochs.
The Core freshness guard rejects an obsolete session before changing structured history.
Current epochs do not constitute negotiated source commit.
(src\PhantomFudge.App\MainWindow.Questions.cs:120-128, `UpdateQuestionTranscript`;
src\PhantomFudge.Core\StructuredTranscriptStore.cs:24-40, `BeginSession`, `SetPartial`;
src\PhantomFudge.Core\StructuredTranscriptStore.cs:120-125, `IsCurrent`, `PrepareUpdate`.)

### Actual update and Freeze algorithm

1. Stage a store update. Repeated unchanged partial text preserves its revision; changed text
   replaces the immutable record. A final, including an empty final, settles the same active
   utterance key. Protocol final IDs are deduplicated using a session-global high-water mark.
2. Enforce store capacity before publishing. Evict oldest non-active records other than the
   just-updated record; if the live records/update cannot fit, reject rather than silently drop them.
3. On Use selected, copy deduplicated keys from one capture session and compose nonempty text in
   store order. Validate the whole result before replacing draft state.
4. On recognition refresh, replace only selected unfinished records with newer matching revisions.
   Evicted selections retain their copies. After any explicit edit, including reasserting identical
   text through `EditText`, automatic draft replacement stops.
5. On manual Ask, reject blank text, then copy the current draft into an immutable snapshot.
   Later finalization, selection, typing, Clear or eviction cannot mutate that snapshot object.

Sources: (src\PhantomFudge.Core\StructuredTranscriptStore.cs:36-100, updates/deduplication;
src\PhantomFudge.Core\StructuredTranscriptStore.cs:134-145, `ApplyUpdate`);
(src\PhantomFudge.Core\QuestionDraft.cs:18-79, selection/edit/refresh;
src\PhantomFudge.Core\QuestionDraft.cs:90-124, Freeze/validation/composition);
(src\PhantomFudge.Core\QuestionSnapshot.cs:5-14, copied provenance).

```mermaid
---
config:
  theme: base
  themeVariables:
    lineColor: "#8b949e"
    textColor: "#e6edf3"
---
flowchart TB
    accTitle: Implemented local draft ownership and Freeze
    accDescr: Explicit selection copies records into a draft. Recognition refresh stops after a user edit; manual Ask copies a local snapshot that later changes cannot mutate.
    Select["Use selected"] --> Copy["Validate and copy"]
    Copy --> Draft["Working draft"]
    ASR["Matching ASR revision"] --> Guard{"User edited?"}
    Guard -->|No| Draft
    Guard -->|Yes| Retain["Keep user text"]
    Edit["Explicit edit"] --> Draft
    Draft --> Ask["Manual Ask"]
    Ask --> Validate{"Nonblank and bounded?"}
    Validate -->|Yes| Snapshot["Immutable local copy"]
    Validate -->|No| Reject["Reject without send"]
    style Select fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Copy fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Draft fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style ASR fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Guard fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Retain fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Edit fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Ask fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Validate fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Snapshot fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Reject fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    linkStyle default stroke:#8b949e
```

The UI and Core validate before committing question changes: oversized selection, paste, editing
or refresh preserves the previous draft instead of truncating. Blank text may exist while editing,
but cannot be frozen. Closing a capture/epoch marks unfinished partials inactive, not final.
The application's draft/snapshot objects remain memory-only, not a persistent question history.
That statement does **not** cover the external CLI profile or cloud service retention.
(src\PhantomFudge.App\MainWindow.Questions.cs:187-210, edit/paste rejection;
src\PhantomFudge.Core\QuestionDraft.cs:90-124, blank/oversize checks;
src\PhantomFudge.Core\StructuredTranscriptStore.cs:127-129, `ClosePartial`;
src\PhantomFudge.App\MainWindow.Questions.cs:13-20, local state fields.)

### Implemented common answer contract

```csharp
AnswerRequest(Guid RequestId, string ExactText)
AnswerEvent(Guid RequestId, long Sequence, AnswerEventKind Kind, string Text = "")
IAnswerProvider.StreamAsync(AnswerRequest request, CancellationToken cancellationToken)
```

`AnswerRun` retains the snapshot locally but constructs a fresh request GUID and copies only
`snapshot.ExactText` into the provider request. Current events are `TextDelta` and `Completed`;
provider failures use a safe exception rather than pretending error prose is answer text.
The run already enforces a **32,768 UTF-16-code-unit** text cap. An overflowing delta is rejected
whole, leaving prior output available as incomplete. Model-specific token/context limits remain
separate work. (src\PhantomFudge.Core\Answers\AnswerContracts.cs:3-12, request/event interface;
src\PhantomFudge.Core\Answers\AnswerContracts.cs:59-62, `AnswerProviderException`;
src\PhantomFudge.Core\Answers\AnswerRun.cs:10-49, projection and output bound.)

This narrower request type makes accidental provenance disclosure harder; a generic serializer
over the richer local snapshot would undo that protection. Neither character cap establishes a
token budget or retention guarantee.
(src\PhantomFudge.Core\QuestionSnapshot.cs:17-23, local snapshot fields;
docs\AI-ANSWER-PRIVACY.md:83-107, retention and processing boundaries.)

## 4. PLANNED negotiated source-scoped commit, not stop-all

Introduce speech protocol **v3** (or another explicitly negotiated capability), preserving v1/v2.
The current strict v2 parser accepts only Stop and must not reinterpret a new control silently.
(services\transcription\server.py:247-252, `Session.receive`.)

Use the existing source-prefix PCM format if it remains sufficient; add typed controls and
session/epoch metadata rather than an unrelated second socket around the one-active-session rule.

Illustrative control:

```json
{
  "type": "commit_source",
  "requestId": "<unique commit ID>",
  "source": "microphone",
  "sourceEpoch": 7,
  "throughSample": 123456
}
```

The implementation must:

1. Under the same lock as audio enqueueing, freeze the Mic delivered-sample watermark and append
   a commit envelope to the **same ordered sender queue**. No concurrent control WebSocket send.
2. Send preceding PCM first. On the server, enqueue the control with the audio work; verify the
   source/epoch/watermark and drain through **processed** samples, not merely received bytes.
3. Finalize only the selected Mic decoder. Publish its remaining final text—even if empty—then
   acknowledge the source, old/new epoch, exact watermark and resulting final reference.
4. Recreate/reset that decoder safely with cumulative source offset preserved. Reuse the existing
   finalize-and-replace wrapper pattern; do not assume undocumented Reset timestamp semantics.
5. Leave Computer capture/decoder state intact and preserve session-global final IDs.
6. Make commit retries idempotent by capture session + source + requestId; replay the same ack,
   reject changed parameters for the same ID, and bound the deduplication cache.
7. Tag subsequent events with the new source epoch. Cancelled/old acknowledgments cannot rewrite
   a newer question; user edits during finalization remain a distinct revision.

For newly armed dictation, establish a start boundary as well as an end boundary. Prior Mic
conversation belongs in the transcript, not automatically in the question. Current partials
lack word timing, so arbitrary retrospective sample cuts cannot be reconstructed exactly from text.
(services\transcription\recognizer.py:64-67, `VoskRecognizer._new_decoder`.)

A click watermark covers **samples delivered by capture**, not necessarily the last acoustic
sample spoken before that click; some audio can still be in the device buffer. Specify and measure
a bounded last-packet drain/ready indication instead of inventing sample-exact human boundaries.

## 5. Local revision ownership and common answer states

Implemented local draft state consists of revisions and flags, not the enum below.
`Refresh` may advance a draft while an earlier frozen snapshot remains unchanged; `EditText`
prevents further automatic replacement. Provider submission belongs to `AnswerRun`, not to a new
draft enum; source Finalizing remains planned. (src\PhantomFudge.Core\QuestionDraft.cs:48-95, `EditText`, `Refresh`, `Freeze`.)

**PLANNED** question lifecycle with source commit and an authorized provider:

```text
Empty -> Drafting -> Finalizing -> Ready
             |                      |
             +--> SnapshotFrozen <--+
                       |
                    Submitted

Late transcript revision -> new Drafting revision, never mutation of Submitted.
Finalizing -> Failed/Cancelled retains visible draft.
```

**Implemented common answer state**, with installed-provider integration:

```text
Streaming -- Completed event, then clean EOF and nonblank text --> Completed
    |
    +------> Failed / Cancelled     (retained text is incomplete)

New Ask while active: wait for cancellation and cleanup, then explicitly ask again.
```

`Apply` ignores another request's events and events after the run leaves Streaming. For the current
request it requires a strictly increasing positive sequence, not necessarily contiguous numbers.
Completion must contain no text; it latches a terminal marker rather than declaring success.
Further events after that marker fail. `Complete`, called after enumeration ends, requires both
the marker and nonblank accumulated text. EOF alone, a zero exit code alone, or a terminal marker
followed by failure cannot justify a complete answer.
(src\PhantomFudge.Core\Answers\AnswerRun.cs:31-78, `Apply`, `Complete`, `Cancel`, `Fail`.)

**PLANNED direct Azure connection** (CLI setup is separate):

```text
Disabled -> Configured -> SignInRequired -> Ready
                                  ^          |
                                  +----------+  silent refresh cannot satisfy auth
```

These are separate from audio Stopped/Starting/Running/Stopping and capture-affinity setup.
Do not use `IsRunning=false` as proof that a source is fully drained; the existing stop-race
fix remains relevant. Answer cancellation must not corrupt transcription or reset the draft.

One answer request is active at a time. Every UI delta checks request ID and monotonic sequence;
ignore stale/cancelled streams without appending their text to the new question's answer.
No completion is inferred merely from HTTP200 or a closed connection. The common UI
uses a **120-second overall deadline** and **40-ms render timer**, not a per-token dispatcher queue.
These are code choices, not achieved latency or deadline-precision measurements.

Cancel, Hide, Clear, protection invalidation and close must propagate cancellation to the adapter
and its **owned process**, awaiting cleanup before another request. Common run cancellation retains
text as incomplete; Clear deliberately discards it. There is no automatic retry or resend on reveal.
The UI's Copy action is deliberate and labels non-completed text incomplete. Process termination
and CLI isolation remain adapter acceptance requirements under active offline checking, not properties
proved by `AnswerRun` alone. See [provider lifecycle](ANSWER-PROVIDERS.md).
(src\PhantomFudge.Core\Answers\AnswerRun.cs:64-78, terminal cancellation/failure behavior.)

### Agency setup without new login; profile and retention boundaries

`AnswerConfiguration` loads bounded `answers.json` metadata, rejects unknown properties and numeric
enum values, and supplies a dedicated `%LOCALAPPDATA%\PhantomFudge\copilot-profile` location for
the CLI path. That is not permission to import a personal coding profile or start authentication.
For the corrected Agency path, existing model-service authentication must suffice; otherwise fail
visibly without new login or account switching. A genuine compatible executable, not merely a path
ending in `.exe`, must be verified.
(src\PhantomFudge.Core\Answers\AnswerConfiguration.cs:8-29, configuration/profile paths and loading.)

Setup with `Initialize-Answers.ps1 -Provider Agency` resolves the executable without login.
The supported launcher is `agency copilot`; intended restrictions are profile-only,
no-default-mcps, no-config-plugins and no-aec, plus **no model tools**. These are adapter-validation
requirements, not a verified isolation receipt. The prompt path must not read organization data
through WorkIQ, Graph, ADO, mail or chats, nor inherit personal permissions/hooks/plugins/MCPs.
Using existing model authentication is not permission to access those data sources.
The [provider guide](ANSWER-PROVIDERS.md) owns the evolving setup details.

Agency or its engine may retain prompts and sessions; do not assume profile isolation removes
retention. The separately configured local-model CLI can also retain state, including when inference uses a
local model. Deleting the app's draft or hiding the window does not erase that data or cloud records.
“ExactText only” describes application content passed to the provider, not the CLI's system prompts,
service metadata or retention policy. Never describe the complete configured path as memory-only.

## 6. PLANNED Azure request and authentication path

Reuse the now-written `IAnswerProvider`; the Azure credential and source-commit abstractions below
remain planned and independent of UI/speech:

```text
IAnswerProvider.StreamAsync(AnswerRequest, CancellationToken)
  -> asynchronous AnswerEvent sequence

IAzureCredentialSession
  -> explicit Connect + silent token access + SignInRequired state

IQuestionCommitter
  -> commit/ack independent of local Vosk versus a future approved cloud-ASR adapter
```

Implement an optional direct Azure provider with **v1 Responses**, `stream:true`,
`store:false`, `background:false`, no tools or server-side conversation. "Foreground" here is
the API execution mode; it does **not** mean bringing the desktop window to the foreground.
Keep Chat Completions as a separately tested fallback adapter, not automatic cross-provider routing.
(docs\AI-ANSWER-RESEARCH.md:160-201, proposed inference API.)

Use a configured Entra public-client/WAM identity, resource-level inference RBAC and silent token
cache in the Ask path. Never put managed-identity assumptions or distributed app secrets on the
laptop. Interactive sign-in is an explicit setup action outside the presentation; no surprise
browser/broker fallback from an in-flight Ask. Exact token audience follows the pinned adapter.
(docs\AI-ANSWER-PRIVACY.md:22-62, public-client identity and inference authorization.)

An Azure-hosted broker with managed identity is optional if organizational policy requires it.
Its extra hop, auth, quotas and streaming buffering must be measured. No mandatory Docker.
See AI-ANSWER-PRIVACY.md for the full identity/data boundary.

## 7. Additional PLANNED Azure streaming and evaluation details

- Start with a concise direct-answer instruction. Evaluate low supported reasoning effort and a
  total output budget that leaves room for hidden reasoning; do not confuse visible words with
  all billed/generated tokens.
- Begin rendering on the first text delta, not stream-open metadata. Coalesce tiny updates into
  bounded 16–50-ms UI batches; the current common coordinator's 40-ms timer is a starting point,
  not a measured rendering guarantee. Cap provider buffering separately.
- Support completed, incomplete, refused/filtered, stream-error and cancelled states. Handle
  Azure-specific filter metadata, including raw fields if the pinned SDK lacks typed support.
- Preserve an interrupted partial answer with its original question/profile. Do not silently
  retry after partial output, concatenate repeated generations, or claim a refund on cancellation.
- The installed-provider path has no automatic retry. Any future Azure pre-output retry policy
  requires explicit design, finite deadlines and no SDK+app retry multiplication.
  Do not fail over to an unapproved model/provider/geography.
- On Hide/lock/close/protection failure, cancel visible-answer streaming by default and retain
  bounded local draft/partial state. Reopening does not resend a question automatically.
- A local CPU backlog should be visible. Do not prioritize Mic commit by skipping/reordering
  earlier Mic samples or dropping Computer audio silently.
- Pre-create model/credential/client objects once enabled. Do not send dummy paid model requests
  merely to pretend the app is warm; document any separately authorized warm-up behavior.

Measure Ask-to-first-rendered-answer and first-useful-sentence time, not just Azure gateway TTFT.
The evaluation document defines targets, clocks, datasets, cold/warm stratification and failure cases.
No end-to-end application latency benchmark has been established. The Agency probe's recorded
elapsed time is not Ask-to-render timing or an answer-quality result.

## 8. Delivery sequence

1. **Implemented, local slice complete:** structured local transcript, explicit selection/typing and
   immutable local Freeze. Its historical completion did not establish answer-provider readiness.
2. **TODO39, functionally smoke-tested; local implementation complete:** the separately authorized
   Agency probe succeeded using existing authentication, after the earlier direct Copilot smoke failed
   and its login was cancelled. Its exact observed launch/environment/profile still needs final
   adapter, replay and UI checks passed. LocalModel remains mock-tested only. Keep
   SnapshotOnly until deliberate setup and applicable validation permit use.
3. **TODO29, planned and nonblocking for explicit Ask:** Mic-only commit and armed
   start/end/source/epoch/tail tests; preserve continuous Computer capture.
4. **TODO30/31, optional future Azure:** approved connection/profile and keyless identity,
   with no real meeting data. Actual resource and identity settings remain unknown.
   Preserve future Azure filtering and lifecycle qualification rather than equating the generic UI
   with a deployed Azure client. Fake streams are tests, not pretend AI output presented as real.
5. Authorized synthetic regional/model/API comparison and quality/latency tuning.
6. Exact-package capture/privacy/accessibility and native release acceptance.
7. Optional cloud ASR, enterprise broker, richer grounding or Docker only when separately justified.

Cloud ASR may improve a measured requirement, but its raw-audio egress and preview commit support
are separate decisions. Local Whisper refinement is also an experiment, not a silent model switch.

### Review evidence is narrower than acceptance

Core snapshot tests check exact text and monotonic creation time, copied read-only provenance,
and survival across late ASR, edits, Clear, eviction and capture restart. These assertions define
model-level checks, not proof of UI focus, real devices or every environment.
(tests\PhantomFudge.Core.Tests\QuestionSnapshotTests.cs:10-99,
`FreezeCapturesExactDisplayedRevisionAndMonotonicCreationTime`,
`SnapshotSurvivesLateAsrEditsClearsEvictionAndCaptureRestart`,
`SourceRefsAreDeepCopiedReadOnlyAndCarryFullProvenance`.)

**Historical TODO28 handoff, not TODO39 acceptance or rerun evidence:** 137 Core tests (96 new) and 46 native
synthetic groups passed; the new question checks passed. The first whole-UI attempt nevertheless
had an unexplained foreground-check failure. An identical-source repeat passed all checks.
Do not erase that first failure or invent its cause. No real endpoint was opened in that local slice. TODO28 is
locally complete; these results do not establish owner acceptance, real-device qualification,
recipient capture behavior or Azure latency.

## 9. Decisions still needed before direct Azure execution

Approved tenant/subscription/resource or resource-creation owner; model/deployment alias/version;
permitted processing geography/deployment type; Entra registration/RBAC and network route;
data handling/retention policy; budget/quotas; and synthetic benchmark spend authorization.
Do not invent these from the user's employer, timezone, previous Azure work or local credentials.

These missing execution values do not prevent a complete architecture/backlog design. They do
prevent claiming a deployed integration, a measured fastest model, or a configured privacy posture.

## References

- [Local question usage and boundaries](LOCAL-QUESTIONS.md).
- [Installed-provider setup and retention](ANSWER-PROVIDERS.md) and
  [workstream39](../TODO/39_INSTALLED_CLI_ANSWERS.todo).
- [Common answer contracts](../src/PhantomFudge.Core/Answers/AnswerContracts.cs),
  [run state](../src/PhantomFudge.Core/Answers/AnswerRun.cs) and
  [configuration](../src/PhantomFudge.Core/Answers/AnswerConfiguration.cs).
- [Structured transcript store](../src/PhantomFudge.Core/StructuredTranscriptStore.cs) and
  [segment identity](../src/PhantomFudge.Core/TranscriptSegment.cs).
- [Draft ownership](../src/PhantomFudge.Core/QuestionDraft.cs) and
  [immutable local snapshot](../src/PhantomFudge.Core/QuestionSnapshot.cs).
- [Inline question controls](../src/PhantomFudge.App/MainWindow.Questions.cs) and
  [synthetic question diagnostics](../src/PhantomFudge.App/MainWindow.QuestionDiagnostics.cs).
- [Cloud research](AI-ANSWER-RESEARCH.md), [identity/privacy](AI-ANSWER-PRIVACY.md) and
  [proposed evaluation](AI-ANSWER-EVALUATION.md); these do not establish account entitlement or
  newly measured API performance.
