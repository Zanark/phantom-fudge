# Low-latency transcription-to-answer evaluation plan

**Design/proposed acceptance targets, not Azure benchmark results.**
Existing local ASR measurements are in SPEECH-BENCHMARK.md. No Azure model or Speech endpoint
has been called for this research, and no new live audio has been captured.

This retained October 8 plan is not a current runtime inventory. See [October 9 results](QUALITY-UPDATE.md)
for hybrid ASR and prepared Agency evidence; the proposed Azure and live-voice acceptance gates remain separate.

## Measure the user-visible path

Use one local monotonic clock for client stage events. Keep source-relative sample positions
separate from wall time; never subtract unrelated machines' wall clocks.

| Event | Meaning |
| --- | --- |
| A0 | Annotated speech onset in a controlled fixture |
| A1 | Annotated last spoken sample, not file end/trailing silence |
| A2 | Capture callback delivers the relevant PCM chunk |
| A3 | Local ASR emits partial/final; include source/session/epoch |
| A4 | That revision is actually rendered in the question draft |
| Q0 | User invokes explicit Ask or Finish-and-Ask |
| Q1 | Immutable displayed draft snapshot is frozen |
| Q2 | If required, source-scoped commit acknowledgment reaches UI |
| N0 | Silent token lookup/refresh begins |
| N1 | HTTP request starts on the reused client |
| N2 | First response headers arrive |
| N3 | First nonempty answer-text delta arrives |
| N4 | First answer text is rendered in WPF |
| N5 | First useful sentence is visible, evaluated against the task |
| N6 | Terminal complete/filtered/interrupted/failed/cancelled state is rendered |

Report these separately:

- First-visible-partial latency: A4-A0.
- Natural final lag: final A4-A1, only with a defensible A1 annotation.
- Commit latency: Q2-Q0; include capture-boundary, queue, inference and UI components.
- Ask-to-first-text: N4-Q0; this is the primary interactive metric.
- API TTFT: N3-N1; not the same as user-visible latency.
- First-useful-sentence latency: N5-Q0; a filler "Sure" token does not satisfy it.
- Completion latency, output tokens/rate, cancellation delay, throttle/error frequency.
- Warm/cold auth, connection reuse, deployment/model version, prompt/output lengths and
  cloud/local ASR mode. Cold-start observations must not disappear into warm medians.

## Initial target budgets to calibrate

These are engineering starting points to approve or revise after real regional/model measurements.
They are not provider guarantees, existing app results, or sums of independent percentiles.

| Metric | Proposed warm-path target |
| --- | --- |
| Freeze an already reviewed draft | at most50ms |
| Source-scoped commit to frozen text | p95 at most500ms |
| First visible partial | p95 at most1.5s on agreed question corpus |
| Ask reviewed text -> first rendered answer text | p50 at most1s; p95 at most2.5s |
| Ask -> first useful short sentence | p50 at most2s; p95 at most4s |
| UI stream batching |16-50ms, without token-by-token dispatcher flooding |
| Local cancel acknowledgment | at most100ms; remote compute/billing cancellation not guaranteed |

Keep a finite request deadline and a separate no-progress deadline. Exact values belong in the
selected adapter/configuration spike, not an undocumented retry loop. If a target fails, publish
the measured distribution and bottleneck; do not silently relax it or label a long spinner "real-time".

## Controlled ASR experiment

Reuse the existing synthetic corpus and add question-specific cases. Feed identical PCM directly;
output mute must not affect these tests. No microphone/loopback is opened by a replay harness.

Include:

1. "Explain Azure Service Fabric like I am five."
2. A question containing multiple natural pauses but one intent.
3. Negation and contrast: "Why should I not use that option?"
4. Service/product names, acronyms, numbered steps and numeric thresholds.
5. A Mic question interleaved with unrelated Computer speech.
6. Two identical words from different sources; source labels cannot be used as speaker identity.
7. Empty/very short input, silence, clipped first/last words and repeated commit.
8. Cancellation, source loss, queued stop, session restart and late final/revision.

Compare the current local Vosk stream, bounded question-only Whisper refinement, and optionally
an explicitly authorized Azure Speech profile. The earlier complete-file Whisper win is not a
streaming-latency measurement. Keep cloud audio disabled until an approved test profile exists.

Score WER consistently, but also score **question fidelity**: correct product/entity, negation,
numbers, requested audience/detail, and whether earlier unrelated Mic text leaked into the draft.
Reference-derived restricted grammars/prompts must not contaminate the comparison.

For commit, test:

- marker queued behind exactly the intended delivered samples;
- correct processed watermark, not just received bytes;
- no Computer decoder reset/interruption;
- flush-before-reset and cumulative timestamp/global-ID preservation;
- duplicate request acknowledgment without duplicate finals;
- clear distinction between a clicked boundary and audio still buffered by the device.

## Azure text-model comparison

Only after endpoint/deployment/account/budget authorization:

- Use20-30 original public-safe questions with human-checked task rubrics; include ELI5,
  concise technical explanations, ambiguity, uncertainty, follow-up and harmless refusal cases.
- Run the same frozen prompt/system instructions/context/output budget against each available
  candidate, one request at a time initially. Do not benchmark production meetings.
- Measure at least30 warm requests per candidate/configuration as an initial diagnostic, plus
  explicitly separated cold auth/connection observations. Increase samples before making p95 claims.
- Randomize or interleave model order across repeated runs and record workload/time conditions.
- Fix the actual deployment type, region/processing policy, model version, SDK/API and filter settings.
  Model-family names alone do not identify a reproducible serving configuration.
- Consume the full stream and count visible output/finish status. Empty/incomplete/filtered results
  are outcomes, not dropped timing samples.
- Match output usefulness and length before comparing generation speed. A ten-word non-answer is
  not a valid latency win over a correct explanation.
- Record cost from actual usage and the applicable price schedule; approve a benchmark spend cap.
  Do not infer live quota or price from a generic documentation example.

For the Service Fabric example, assess a simple analogy, explanation of distributed app pieces
and recovery/coordination, and avoidance of false equivalence with Kubernetes. An answer needs no
private RAG corpus to address this generic question; factual checking is an offline evaluation step.

## Failure/privacy matrix

| Case | Required behavior |
| --- | --- |
| Blank draft / oversized input | No request; clear inline reason, no silent truncation |
| Computer text arrives while Mic draft active | Transcript updates; draft remains source-scoped |
| ASR revision after Ask | Frozen request unchanged; later text clearly belongs to a new/revised draft |
| Duplicate Ask / rapid A then B | One active request; explicit cancellation/new ID; old deltas never enter B |
| Commit timeout | No silent substitution; retain editable draft and explicit send-as-is option |
|401 / UI-required token | No surprise browser/broker popup during a presentation |
|403 / private-network failure | No switch to public access or another provider |
|429 before output | Bounded policy respecting retry guidance and user deadline; no retry storm |
| Network loss after partial answer | Preserve partial as incomplete; do not silently restart and duplicate/bill twice |
| Content filter/refusal/incomplete finish | Correct terminal state; never present as a complete unfiltered answer |
| Hide/lock/close/protection failure | Cancel answer request and preserve bounded local draft/partial state as specified |
| Model text includes links/code/instructions | Display as data, no automatic browsing/execution/tool calls |
| Diagnostics enabled | Stage timings/counts only; no audio/prompt/answer/token content export |
| New answer pane / streaming text / themes | Same protected HWND; no new unprotected popup or foreground takeover |

## Benchmark receipt

Store synthetic-only results with: run/config ID, timestamps, source revision, hardware/runtime,
model deployment/version/type/region, API/SDK/auth mode, fixed input/output budgets, filtering mode,
all stage deltas, p50/p95/sample counts, error counts, fidelity/quality scores and cost/usage.
Use random correlation IDs, not hashes of real questions. Real owner corpora/results stay private.

Every report must distinguish: measured local ASR, measured Azure inference, proposed targets,
synthetic versus real-speaker coverage, and remaining uncertainty. No "fastest possible" claim
without defining the actual compared configurations and quality requirements.
