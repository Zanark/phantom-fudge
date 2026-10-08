# Proposed architecture: dictate, choose, Ask, stream an answer

**Status: design ready for implementation planning; not implemented.**
The user requested deep research and appended TODOs/workflows, not Azure provisioning or live
inference. [Research](AI-ANSWER-RESEARCH.md), [privacy/identity](AI-ANSWER-PRIVACY.md) and
[evaluation](AI-ANSWER-EVALUATION.md) provide the evidence and qualification boundaries.

## 1. The recommended baseline

```text
Computer PCM ----+                         remains local
                +--> native Python ASR --> typed transcript/source store
Mic PCM --------+                              |
                                               v
                                 explicit selection / question draft
                                               |
                                      owner presses Ask
                                               |
                                   immutable QuestionSnapshot
                                               |
                         C# Azure adapter + silent Entra credential
                                               |
                                Azure OpenAI v1 Responses
                                               |
                              streamed text / terminal events
                                               |
                              protected WPF answer pane
```

Azure receives only the selected question and explicitly enabled bounded context. Python remains
the local speech specialist; C# owns UI, request state, authentication and asynchronous inference.
Do not add a local HTTP hop through Python solely because it already hosts Vosk. Do not put model
network work in the native capture callback, ASR CPU worker, or WPF UI thread.

**Initial Azure footprint:** an approved Azure OpenAI/Foundry model resource and deployment,
public-client registration/user RBAC, and optional metadata-only monitoring/budget controls.
Prefer reusing approved existing resources. No hosted agent, database, queue, RAG index, AKS,
container runtime or mandatory gateway is needed for ordinary technical Q&A.

Recommended evaluation candidates are5.4-mini,5.4-nano and5-mini with their exact versions/lifecycle
from the research. No model is selected as "fastest" without the actual approved deployment test.
Use a pay-per-token deployment type whose processing geography is explicitly acceptable.

## 2. User experience: no accidental whole-meeting prompts

Example:

1. A chat participant asks "Explain Service Fabric like I'm five."
2. The owner reads it aloud. The Mic transcript updates locally, independently of Computer audio.
3. The owner selects the intended Mic segment(s), uses an explicitly armed Question draft,
   or types/corrects the question in the draft field.
4. **Ask** freezes exactly the displayed draft and sends it to the configured Azure deployment.
5. The answer starts appearing as text arrives, followed by an honest completed/partial/error state.

The app never reads the chat to obtain that question. It does not auto-answer when it hears a
question mark, forward all Mic text, or include unrelated Computer text as hidden context.

### Two deliberate ways to submit

- **Ask as shown:** freeze the current editable/selected draft immediately. A provisional draft
  is visibly labelled as such; Ask sends that exact revision without waiting for ASR.
- **Finish dictation:** commit the armed Mic span and update the draft from its acknowledged final
  segments. The owner then presses Ask. A later optional Finish-and-Ask shortcut may compose these
  two explicit actions, with the same source/watermark/cancellation guarantees.

This respects the requested manual-send workflow while avoiding a global Stop. There is no second
permission/rehearsal confirmation. If finalization times out, retain the draft and show the failure;
do not silently send a different/provisional question.

### Layout and focus

Transcript stays primary. An inline Question/Answer panel expands on explicit use, with the current
question, provider/model profile, Cancel, Copy and concise/expand controls. Notes remains optional.
Use the same HWND, themes, alpha and cursor policy; no detached model-response popup.

Ask/Cancel and transcript selection should work without unnecessary activation. Actual text editing
is deliberate keyboard focus. The current global Edit/Present transition stops audio; the new draft
editor must have a separately designed focus transition so correcting a question does not accidentally
stop the Computer stream. Do not quietly change existing mode semantics without regression coverage.

No generated speech/TTS initially: it adds latency and can feed back into Computer transcription.
No automatic clipboard copy, link opening, code execution or reply-posting to a meeting/chat.

## 3. Local data model

The current flattened transcript string is a display/export representation, not sufficient provenance
for question selection. Add a bounded structured store while preserving existing exports and tests.

Planning-level records:

```text
TranscriptSegment:
  captureSessionId, source, sourceEpoch, segmentId, revision,
  startSample/endSample or qualified audio-relative bounds, text, isFinal

QuestionDraft:
  questionId, captureSessionId?, sourceSelection, selectedSegmentRefs,
  questionStartBoundary?, draftRevision, text, isProvisional, userEdited

QuestionSnapshot:
  questionId, snapshotRevision, exactText, sourceRefs, createdMonotonic,
  wasProvisional, profileVersion, contextPolicy

AnswerRequest:
  requestId, questionSnapshot, deploymentProfile, boundedExplicitContext,
  output/reasoning settings, deadline, cancellation

AnswerEvent:
  requestId, eventSequence, kind, textDelta?, terminalReason?, usage?, safeError?
```

Rules:

- Generate a new capture-session identity per Start, not the long-lived Python process instance ID.
- Maintain source epochs and revisions; v2 global segment IDs alone reset between sessions.
- Select source by structured identity, never by parsing `[Mic]`/`[Computer]` in display strings.
- Freeze question text on the UI-owned revision boundary. Late ASR updates create a later draft
  revision, never mutate a submitted snapshot or silently resend it.
- Retain local copies of referenced text when a bounded transcript store evicts old segments.
- Keep question/answer history bounded and memory-only by default. Explicit export remains deliberate.
- Initial proposals:8K-character draft ceiling,32K-character answer display ceiling and a small
  context window. Also enforce model-specific token/output limits; never silently truncate.

## 4. Negotiated source-scoped commit, not stop-all

Introduce speech protocol **v3** (or another explicitly negotiated capability), preserving v1/v2.
The current strict v2 parser accepts only Stop and must not reinterpret a new control silently.

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

A click watermark covers **samples delivered by capture**, not necessarily the last acoustic
sample spoken before that click; some audio can still be in the device buffer. Specify and measure
a bounded last-packet drain/ready indication instead of inventing sample-exact human boundaries.

## 5. Three independent state machines

Question:

```text
Empty -> Drafting -> Finalizing -> Ready
             |                      |
             +--> SnapshotFrozen <--+
                       |
                    Submitted

Late transcript revision -> new Drafting revision, never mutation of Submitted.
Finalizing -> Failed/Cancelled retains visible draft.
```

Answer:

```text
Idle -> Dispatching -> Streaming -> Completed
           |              |
           +------> Failed/Filtered/Incomplete/Cancelled

New Ask while active: explicit replacement/cancel, new request ID, retain old partial as incomplete.
```

Connection:

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
No completion is inferred merely from HTTP200 or a closed connection.

## 6. Azure request and authentication path

Use a reusable C# provider abstraction, independent of UI and speech:

```text
IAnswerProvider.StreamAsync(AnswerRequest, CancellationToken)
  -> asynchronous AnswerEvent sequence

IAzureCredentialSession
  -> explicit Connect + silent token access + SignInRequired state

IQuestionCommitter
  -> commit/ack independent of local Vosk versus a future approved cloud-ASR adapter
```

Implement the baseline provider with Azure OpenAI **v1 Responses**, `stream:true`,
`store:false`, `background:false`, no tools or server-side conversation. "Foreground" here is
the API execution mode; it does **not** mean bringing the desktop window to the foreground.
Keep Chat Completions as a separately tested fallback adapter, not automatic cross-provider routing.

Use a configured Entra public-client/WAM identity, resource-level inference RBAC and silent token
cache in the Ask path. Never put managed-identity assumptions or distributed app secrets on the
laptop. Interactive sign-in is an explicit setup action outside the presentation; no surprise
browser/broker fallback from an in-flight Ask. Exact token audience follows the pinned adapter.

An Azure-hosted broker with managed identity is optional if organizational policy requires it.
Its extra hop, auth, quotas and streaming buffering must be measured. No mandatory Docker.
See AI-ANSWER-PRIVACY.md for the full identity/data boundary.

## 7. Streaming, latency and failure details

- Start with a concise direct-answer instruction. Evaluate low supported reasoning effort and a
  total output budget that leaves room for hidden reasoning; do not confuse visible words with
  all billed/generated tokens.
- Begin rendering on the first text delta, not stream-open metadata. Coalesce tiny updates into
  bounded16-50ms UI batches; cap buffered bytes and avoid a growing dispatcher queue.
- Support completed, incomplete, refused/filtered, stream-error and cancelled states. Handle
  Azure-specific filter metadata, including raw fields if the pinned SDK lacks typed support.
- Preserve an interrupted partial answer with its original question/profile. Do not silently
  retry after partial output, concatenate repeated generations, or claim a refund on cancellation.
- Bound pre-output retries and deadlines; honor retry guidance, avoid SDK+app retry multiplication.
  Do not fail over to an unapproved model/provider/geography.
- On Hide/lock/close/protection failure, cancel visible-answer streaming by default and retain
  bounded local draft/partial state. Reopening does not resend a question automatically.
- A local CPU backlog should be visible. Do not prioritize Mic commit by skipping/reordering
  earlier Mic samples or dropping Computer audio silently.
- Pre-create model/credential/client objects once enabled. Do not send dummy paid model requests
  merely to pretend the app is warm; document any separately authorized warm-up behavior.

Measure Ask-to-first-rendered-answer and first-useful-sentence time, not just Azure gateway TTFT.
The evaluation document defines targets, clocks, datasets, cold/warm stratification and failure cases.
No end-to-end cloud latency number has been established in this research.

## 8. Delivery sequence

1. Structured local transcript/question draft and immutable Send semantics; fake answer stream only.
2. Mic-only commit protocol and source/epoch/tail tests; preserve continuous Computer capture.
3. Owner-approved Azure connection/deployment profile and keyless auth spike, with no real meeting data.
4. Streamed answer adapter and same-window UI; cancellations, filters, stale events and local privacy tests.
5. Authorized synthetic regional/model/API comparison and quality/latency tuning.
6. Exact-package capture/privacy/accessibility and native release acceptance.
7. Optional cloud ASR, enterprise broker, richer grounding or Docker only when separately justified.

Cloud ASR may improve a measured requirement, but its raw-audio egress and preview commit support
are separate decisions. Local Whisper refinement is also an experiment, not a silent model switch.

## 9. Decisions still needed before cloud execution

Approved tenant/subscription/resource or resource-creation owner; model/deployment alias/version;
permitted processing geography/deployment type; Entra registration/RBAC and network route;
data handling/retention policy; budget/quotas; and synthetic benchmark spend authorization.
Do not invent these from the user's employer, timezone, previous Azure work or local credentials.

These missing execution values do not prevent a complete architecture/backlog design. They do
prevent claiming a deployed integration, a measured fastest model, or a configured privacy posture.
