# Deep research: rapid dictated questions and Azure answers

As of **2026-10-08**. **Research/design only: no model call, Azure provisioning, audio capture,
cloud upload, new SDK installation or application implementation occurred in this task.**
Evidence comes from the existing local code/benchmark and retrieved public primary references.
Recommendations and latency targets are not measured Azure performance.

## Executive recommendation

Use the current **C# WPF/WASAPI + native Python ASR** app, then add a local question composer
and a **direct C# Azure OpenAI v1 Responses streaming adapter**. Send only an explicitly chosen,
immutable question after Ask. Display streamed text in the same protected WPF window.

Keep audio local by default. Add a negotiated source-scoped Mic commit to reduce finalization
delay without stopping Computer transcription. Benchmark `gpt-5.4-mini` against `gpt-5.4-nano`
and `gpt-5-mini` on available, policy-approved deployments. Choose by quality and measured
Ask-to-visible-answer latency, not model name or language prejudice.

For "Explain Service Fabric like I'm five", one model call is enough. No RAG, hosted Agent,
tool loop, database, queue, Kubernetes cluster, APIM or Docker is required by the first feature.
Add enterprise infrastructure only when a real governance requirement justifies it.

## 1. Grounding in the actual application

The app already has:

- Independent Computer/Mic PCM tracks, selected devices and explicit Start/Stop.
- Native Python/Vosk recognition over one authenticated loopback WebSocket.
- V1 compatibility and strict v2 source-labelled events, global final IDs and per-source times.
- A transcript buffer with source partials but flattened final text, not a structured question store.
- Stop that drains/finalizes **all** sources; no source commit or question epoch exists yet.
- A protected transcript-first UI, collapsed notes, passive default, themes and bounded local data.

Relevant files are `src\PhantomFudge.Core\TranscriptBuffer.cs`,
`src\PhantomFudge.App\Audio\TranscriptEvent.cs`, `TranscriptionSocket.cs`,
`WasapiInput.cs`, `SilenceCadence.cs`, and `services\transcription\server.py`/`recognizer.py`.
Future question selection must use structured source/session/segment identity, not parse display
prefixes such as `[Mic]` from exported text.

Existing **synthetic** results: Vosk8.22% WER/41.754s decode versus Whisper base.en4.79%/25.745s
on164.07s of audio. The two paced Vosk partial observations were about0.96s from stream start;
natural finals around3.807s were also from stream start, not end-of-speech lag.
See [SPEECH-BENCHMARK.md](SPEECH-BENCHMARK.md). No cloud ASR/LLM timing can be inferred from them.

The native adapter does not explicitly choose a device buffer duration. Treat a nominal100ms
library/device assumption as something to measure, not a guaranteed hardware delay.
`SilenceCadence` inserts continuity silence after250ms idle and at100ms intervals; that is not
the recognizer's endpoint-silence setting.

## 2. ASR: eliminate avoidable waiting without losing the question

### Current pinned Vosk

The installed0.3.45 binding exposes `AcceptWaveform`, `Result`, `PartialResult`, `FinalResult`,
`Reset`, `SetWords`, `SetPartialWords` and `SetGrammar`. It does **not** expose later
`SetEndpointerMode`/`SetEndpointerDelays` methods. Do not write a latency plan around absent APIs.

The version-matched native contract [A01] distinguishes:

- endpoint detection followed by `Result`;
- revisable `PartialResult`;
- `FinalResult`, which flushes pending audio without waiting for silence;
- `Reset`, which clears state and is not a replacement for retrieving final text first.

Our wrapper already finalizes/replaces a decoder every30 audio seconds while preserving its
sample offset. Reuse that pattern for a Mic-only commit; ordinary `finish()` alone does not
prepare continued recognition. Preserve Computer state and session-global final IDs.

`SetGrammar` is a restricted vocabulary with model constraints, not a soft terminology hint.
A small command grammar is unsuitable for arbitrary technical questions.

### Three honest submit paths

| Path | Latency advantage | Correctness boundary |
| --- | --- | --- |
| Ask the current editable/selected text | No ASR-finalization wait | Sends exactly that visible revision, including any explicitly provisional wording |
| Finish dictation, then Ask | Explicit Mic commit avoids silence wait | Requires ordered PCM/control barrier and an acknowledged final snapshot |
| Wait for natural final | No new protocol boundary | Silence wait varies and a question may span several natural segments |

Do not call global Stop merely to submit a question: it interrupts Computer capture and loses
the continuity the user asked for. Do not silently switch from commit to provisional text on timeout.

For an armed question, record a start boundary; otherwise earlier Mic conversation can enter the
draft. Retrospective selection should use whole known segments or explicitly edited text.
Current events have segment bounds, not enough word alignment to reconstruct arbitrary sample cuts.

### Optional local Whisper

Faster-whisper is a valid refinement candidate, not automatically a better streaming engine [A09].
The inspected Whisper-Streaming code commits common prefixes between repeated hypotheses and
trims context [A10]. Shorter intervals repeat more compute; local agreement requires another
hypothesis. Its `finish()` returns outstanding hypotheses rather than freshly decoding every
newly buffered sample. A finalization barrier must therefore verify processing coverage.

Evaluate bounded **question-only** refinement before considering whole-meeting replacement.
Show corrections as a new draft revision; never rewrite a question already sent to Azure.

## 3. Azure Speech and Realtime: alternatives, not prerequisites

Continuous Azure Speech exposes intermediate `Recognizing` and final `Recognized` events.
The documented push-stream PCM format matches our16kHz/16-bit/mono little-endian samples;
strip our source framing before sending raw PCM [A02,A03]. Independent sources still need
independent recognizer state or an explicitly supported multichannel design.

Current, important capability [A02]:

- **Explicit audio boundary commit is preview in Speech SDK1.52+.**
- The guide documents service property `setfeature=forcecommit`, `PushAudioInputStream.Commit()`,
  and matching `SpeechRecognitionResult.CommitToken`.
- `NoMatch` can acknowledge an empty commit; natural segmentation uses token zero.
- Documented formats include PCM/A-law/mu-law/G.711; Microsoft Audio Stack is unsupported for this path.

This fits an `IQuestionCommitter` abstraction, but preview status, SDK packaging, selected region
and actual behavior must be qualified. It is not an already available app feature.

Other verified controls:

- `Speech_SegmentationSilenceTimeoutMs` accepts100-5000ms, with accuracy tradeoffs [A04].
- Stable-partial thresholds trade early display against revision stability [A04].
- Semantic segmentation requires SDK1.41+, only some locales, and is documented **not for
  interactive scenarios**; do not pick it as the manual Ask default [A02].
- Phrase lists provide soft biasing, weight0-2/default1, with guidance not to exceed2000 phrases.
  Use a small owner-configured terminology list, not the whole meeting [A05].
- Reuse recognizer/connection objects and use documented preconnection where appropriate [A06].
- `RecognitionLatencyMs` is a specific SDK final-result metric, not microphone-to-screen or
  Ask-to-answer latency [A04].

Fast transcription is synchronous **file** transcription; batch is asynchronous file processing.
Neither is the normal live-partial path [A07]. Azure OpenAI Realtime supports transcription sessions,
manual commit and VAD without automatic response generation [A08], but direct audio-to-answer is
unnecessary for the requested manual text question workflow and changes the privacy boundary.

Cloud ASR sends raw audio. It must remain separately enabled/approved, not an automatic fallback
when local recognition is slow. Azure text answers can be implemented with all audio staying local.

## 4. Azure model shortlist and lifecycle

These are **evaluation candidates**, not available deployments or proven speed rankings [L03-L06].

| Candidate/version | Status on research date | Recommended evaluation role |
| --- | --- | --- |
| `gpt-5.4-mini` / `2026-03-17` | GA; scheduled retirement2027-09-21 | Primary quality/latency candidate |
| `gpt-5.4-nano` / `2026-03-17` | GA; scheduled retirement2027-09-21 | Short/simple-question challenger |
| `gpt-5-mini` / `2025-08-07` | GA; scheduled retirement2027-02-09 | Availability/quality alternative with shorter lifecycle |

Do not bootstrap a new design blindly from older recommendations: `gpt-4.1-nano` is listed
deprecated with retirement2026-10-14; `gpt-4.1-mini`/`gpt-4o-mini` are deprecated with retirement
2027-04-14 [L05]. An existing approved deployment may still be usable during its lifecycle,
but a new choice needs an explicit migration plan.

Model, version, region, deployment type, API support, subscription entitlement, quota and
actual capacity must intersect. The public matrix lists the shortlist in Global Standard
examples including East US2/Sweden Central; that is not a location recommendation or proof
that this user's subscription can deploy them [L02,L04].

For5.4 candidates start evaluation with documented low reasoning effort; verify exact mini/nano
`none` support before using it. Original GPT-5 supports `minimal`, which must not be copied to
5.1-and-later models [L06]. Keep a per-model parameter capability profile.

## 5. Recommended inference API

Prefer **Azure OpenAI v1 Responses**, streamed and foreground, with `store:false`,
`background:false`, no tools, no conversation ID and no `previous_response_id` [L01,L02,L06].
The `model` field is an Azure deployment name, not necessarily the catalog ID.

Chat Completions v1 remains a supported alternative; there is no measured evidence here that
Responses has lower TTFT. Compare the same deployment if SDK behavior makes the alternative
worth testing. Do not introduce stateful Agents just to answer one generic question.

Planning-level baseline:

```text
POST <approved Azure endpoint>/openai/v1/responses
model = <approved deployment alias>
input = <immutable explicit question snapshot>
stream = true
store = false
background = false
tools = omitted
previous_response_id / conversation = omitted
instructions = concise answer policy
reasoning/output settings = verified per-model profile
```

Microsoft's current C# examples use the `OpenAI` package, `OpenAI.Responses.ResponsesClient`,
`CreateResponseOptions`, `CreateResponseStreamingAsync`, and
`StreamingResponseOutputTextDeltaUpdate.Delta` [L02]. Documentation also contains older
`OpenAIResponseClient` examples and experimental-warning suppression. Pin and compile-check
the exact SDK; service GA does not mean every client surface is stable.

Use C# for request coordination/Entra/stream rendering; do not route through Python merely
because ASR is there. Python `OpenAI`/`AsyncOpenAI` is a supported alternative adapter if later
requirements justify it [L01,L15]. Reuse clients and connections [L14].

Handle metadata, text deltas, completed/incomplete/refusal/filter/error and cancellation distinctly.
HTTP200 can be followed by a stream error, including throttling. Azure Responses exposes a
`content_filters` extension not necessarily typed by the SDK; verify raw/extra-field handling [L02].

Closing/disposing a stream stops local consumption. It does not establish immediate server
termination or a billing refund. Background/polling mode is not needed; documented background+
streaming issues make it a poor default [L02,L15].

## 6. Where latency is actually won

Microsoft separates first-token, inter-token and total-generation time [L09].
The app must separately measure first **answer text**, first rendered text and first useful
sentence; an early metadata/annotation event is not an answer.

Prioritize:

1. Keep local ASR/models warm after explicit capture Start; no per-question model load.
2. Freeze an already reviewed draft immediately, or explicitly commit just the Mic question.
3. Keep silent auth and reusable HTTP clients ready; no login popup/new client per Ask.
4. Stream foreground output and batch UI updates every16-50ms rather than wait for a full response.
5. Ask for a short direct answer first; let the user request expansion separately.
6. Use a quality-qualified small model and low supported reasoning effort for simple questions.
7. Keep one active answer request and bounded context; prevent self-created queues/retries.

For reasoning models, hidden reasoning consumes the total output budget. A256-token cap intended
for a short visible answer can yield an incomplete response before any visible text [L06].
Start the authorized experiment with a conservative total budget (for example1024 or2048),
a short visible-answer instruction, and measure reasoning/output usage before tightening it.
Those numbers are experimental settings, not recommendations for unlimited reasoning.

Prompt caching on these pre5.6 candidates requires an identical initial1024-token prefix and
model-specific retention rules [L10]. A short one-question prompt will usually miss it.
**Do not pad a short prompt just to trigger caching.** Do not copy newer breakpoint parameters
into models that reject them.

Default filtering buffers/checks output. Asynchronous filtering is GA but can deliver text
before delayed moderation signals, within an approximately1000-character window [L11].
Keep normal filtering initially; zero filtering-buffer delay is not zero total latency.
Changing the filter mode requires correct annotations/redaction and governance, not merely a speed flag.

## 7. Deployment choices without infrastructure inflation

| Option | Proposed disposition |
| --- | --- |
| Global Standard | Initial pay-per-token candidate only if global processing is permitted |
| Data Zone Standard | Consider where an approved zone boundary is needed; exact model/region support must match |
| Standard geography/region option | Evaluate when policy requires it and the model supports it; not inherently fastest |
| Provisioned/PTU | Revisit for measured sustained traffic/tail-latency needs, not an initial personal utility |
| Priority processing | Later A/B option for eligible combinations; inspect actual returned tier |
| Batch/Flex | Delay-tolerant paths, not default interactive Ask |
| Model router | Evaluate against a fixed deployment; can choose smaller models, but adds a changing decision |
| Hosted Agent/RAG/tools | Outside baseline generic Q&A; add only for explicit capabilities |
| Broker/APIM | Optional governance boundary, not automatically a performance optimization |

Priority's fetched supported table includes5.4-mini, not the other two shortlist entries;
requests can be downgraded and token-generation targets are not desktop TTFT guarantees [L08].
Deployment-location wording differs across official pages: use the conservative privacy/geography
contract, not an unsupported single-region promise [L04,L07,P01].

No subscription quota, capacity, price, service tier or cloud performance was inspected.
See [privacy/identity design](AI-ANSWER-PRIVACY.md) for the direct desktop and optional broker paths.

## 8. Decision and uncertainty summary

| Decision | Proposed answer | Still needs evidence/approval |
| --- | --- | --- |
| Can this be built? | Yes: explicit text snapshot plus streamed model response | Implementation and deployment qualification |
| Default ASR | Existing local Vosk; source commit added | Commit boundary/tail correctness and paced p50/p95 |
| Better local quality candidate | Question-only Whisper refinement | Real-speaker fidelity and live/chunk latency |
| Azure use | Text inference first; optional Speech later | Approved tenant/resource/region/model/data policy |
| Model | Evaluate5.4-mini/nano,5-mini | Entitlement/lifecycle/quality/latency/cost |
| API | v1 Responses; Chat alternative | Pinned .NET SDK, cancellation and raw Azure stream annotations |
| Infrastructure | Direct desktop call initially | Broker/private networking only if policy demands it |
| Privacy | Explicit question text only by default | No universal retention or screen-invisibility guarantee |
| Speed claims | Instrument and compare end-to-end | No Azure latency result exists yet |

## Primary source register

All retrieved2026-10-08. Sections—not invented repository line numbers—identify document evidence.

### Inference/model sources

- **L01** [Azure OpenAI v1 API lifecycle](https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle) — API evolution, code changes, model support.
- **L02** [Responses API](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses) — supported regions/models, streaming/C# examples, storage/deletion, guardrails.
- **L03** [Models sold by Azure](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure) — GPT-5.4/GPT-5 model capabilities.
- **L04** [Region availability](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure-region-availability) — deployment-type/model/region matrices.
- **L05** [Retirement schedule](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirement-schedule) — exact lifecycle dates; recheck before provisioning.
- **L06** [Reasoning models](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning) — effort, stateless requests, token budgets and API feature support.
- **L07** [Deployment types](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/deployment-types) — Standard/Global/DataZone/Provisioned and processing locality.
- **L08** [Priority processing](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/priority-processing) — eligible models/types, target scope and downgrade behavior.
- **L09** [Latency guidance](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/latency) — TTFT/TBT/TTLT, output length, streaming, concurrency and metrics.
- **L10** [Prompt caching](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/prompt-caching) — prefix minimum, retention and model-specific breakpoints.
- **L11** [Content streaming](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/content-streaming) — default/async filtering and annotations.
- **L12** [Quota and rate limits](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/quota) — TPM/RPM, short-window throttling, retry headers and double-retry risk.
- **L13** [Model router](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-router) — routing behavior and baseline comparison.
- **L14** [Official .NET SDK](https://github.com/openai/openai-dotnet/blob/main/README.md) — clients, streaming, reuse and experimental APIs; moving source, not pinned compatibility.
- **L15** [Official Python stream implementation](https://github.com/openai/openai-python/blob/main/src/openai/_streaming.py) — stream closing/error behavior; moving source.

### ASR sources

- **A01** [Vosk0.3.45 C API](https://github.com/alphacep/vosk-api/blob/v0.3.45/src/vosk_api.h) — endpoint/final/reset/grammar contracts, corroborated with the installed Python binding.
- **A02** [Speech recognition guide](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-recognize-speech) — continuous events, in-memory stream, semantic segmentation and explicit commit preview.
- **A03** [Speech input streams](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-use-audio-input-streams) — raw PCM format.
- **A04** [Speech PropertyId](https://learn.microsoft.com/en-us/dotnet/api/microsoft.cognitiveservices.speech.propertyid?view=azure-dotnet) — segmentation/stability/recognition-latency properties.
- **A05** [Phrase lists](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/improve-accuracy-phrase-list) — weighting and list-size guidance.
- **A06** [Connection control](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-control-connections) — preconnection and lifetime.
- **A07** [Speech-to-text overview](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-to-text) — real-time versus Fast/file versus Batch.
- **A08** [Azure Realtime guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio) — session types, manual commit and VAD without automatic responses.
- **A09** [Faster-whisper integrations](https://github.com/SYSTRAN/faster-whisper#community-integrations) — streaming is a separate integration concern.
- **A10** [Whisper-Streaming implementation](https://github.com/ufal/whisper_streaming/blob/main/whisper_online.py) — local agreement, context trimming and finish semantics.

Privacy/identity/network sources **P01-P10** are centralized in
[AI-ANSWER-PRIVACY.md](AI-ANSWER-PRIVACY.md#primary-references-actually-retrieved).
Learn MCP discovery exposed no tools in this turn; direct official documentation was used instead.
Exact .NET cancellation overloads, model-specific `none` effort, tenant permissions, deployment
availability, privacy eligibility and Azure latency remain integration/approval questions.
