# Local streaming speech service

The native default uses Vosk for fast provisional text and locally provisioned
Whisper `small.en` (CPU int8) for accurate finals. Aiohttp exposes the frozen
[`docs\TRANSCRIPTION.md`](../../docs/TRANSCRIPTION.md) WebSocket protocol.
The Windows app supplies explicitly selected Computer and/or Microphone PCM tracks.
The Python service itself cannot open devices, read chats, capture a desktop, identify a
meeting process or play sound. V2 recognizes independent source-labelled tracks; v1 remains
compatible with existing computer-only clients. See [DUAL-SOURCE-AUDIO.md](../../docs/DUAL-SOURCE-AUDIO.md).
No cloud recognition, automatic model discovery, audio recording, transcript
storage, remote telemetry, or inference HTTP calls are implemented. Local metadata diagnostics are described below.

## Normal native setup - no Docker

From any working directory, invoke these scripts using their full paths, or from
the repository root:

```powershell
.\scripts\Initialize-Transcription.ps1 -DownloadModel
.\scripts\Start-Transcription.ps1
.\scripts\Run-PhantomFudge.ps1
# After stopping audio in the app:
.\scripts\Stop-Transcription.ps1
```

Setup explicitly downloads the pinned Vosk and Whisper English models only when no verified
model is cached. Whisper `small.en` needs approximately 486 MB on disk; `base.en` is
approximately 148 MB. Without `-DownloadModel`, provisioning is cache-only.
It generates a 256-bit cryptographically random token once, never prints or
rotates an existing token, and restricts its Windows file ACL to the current user.
It verifies every pinned model file, prepares a service-local Python virtual environment,
installs pinned Python dependencies when needed, and saves nonsecret receipts. It does not
start audio capture, install Docker, enable virtualization or install global packages.
Matching `.txt` copies are provided for PowerShell scripts.

`Start-Transcription.ps1` now starts **native Python by default**. The main app launcher
starts/reuses it automatically; `-NotesOnly` avoids service startup. Opening a second app
instance only reveals the existing window without rebuilding/restarting recognition.
The C# app captures/transports selected PCM sources; Python runs the recognizers.
`.local\transcription-config.json` selects `hybrid-whisper`, model, CPU threads and
the general technical vocabulary (enabled by default; `-Vocabulary none` disables
these disclosed individual-word hints). `-WhisperModel base.en` explicitly
selects the lighter model. `-Engine vosk` explicitly selects the legacy recognizer;
there is **no silent fallback** when the selected accurate model cannot load.

The manager serializes startup with a local lock, stores a per-launch instance receipt,
and authenticates `/v1/runtime` before reusing or stopping a process. An unfamiliar service
on the port is not adopted or killed. `/v1/shutdown` requires the matching instance and
refuses active transcription. The idle service persists until explicitly stopped.
The launcher checks authenticated speech-protocol capabilities and a fingerprint of
engine/model/revision/threads/vocabulary/runtime source/dependency pins. It verifies the
new model before requesting an upgrade of an owned idle service. Active, foreign or
unauthenticated instances are never interrupted. Running setup alone changes no process.

## Optional containerization - final stage only

Docker is not a runtime requirement or native-release gate. The owner's03:25 clarification
supersedes the earlier Docker-required startup. Only when the native application is complete,
the same service can optionally be run with `Start-Transcription.ps1 -Docker` and stopped
with `Stop-Transcription.ps1 -Docker`. Stop the native service first; do not run both on8765.

That optional mode requires a Linux-container engine/Compose. Image builds require package
network access; inference does not. The proposed container has an internal-only network,
host publication **127.0.0.1:8765 only**, non-root UID/GID 10001, a read-only root
filesystem/model, no devices/privileges, dropped capabilities, no-new-privileges,
2 CPU / 768 MiB / 64 PID limits, and a 15-second graceful-stop deadline.
The image does not contain the token or model. `.dockerignore` is an allowlist.
The existing optional container recipe explicitly retains legacy Vosk and its small
resource limit; the new native Whisper pipeline is not claimed to fit that container.
The read-only container also has no writable metadata-log volume: this is reported as
diagnostics unavailable, not as successfully persisted logging. Docker remains unexecuted.

Health: `http://127.0.0.1:8765/health` returns HTTP 200 with
`{"status":"ready","modelAvailable":true}` only after successful model loading.
An absent/corrupt model yields 503 without paths. A missing/invalid token prevents
startup. A decoder failure makes readiness fail until an explicit service restart.
Do not expose this unencrypted local protocol beyond loopback.

## Configuration and native-development run

| Setting | Meaning |
| --- | --- |
| `PHANTOM_TRANSCRIPTION_TOKEN_FILE` | Takes precedence; `.local\transcription.token` on the host; `/run/secrets/transcription_token` in Docker |
| `PHANTOM_TRANSCRIPTION_TOKEN` | Optional environment-only alternative; never a URL parameter |
| `PHANTOM_TRANSCRIPTION_MODEL_PATH` | Required explicit model directory; no auto-download |
| `PHANTOM_TRANSCRIPTION_HOST` | Defaults to `127.0.0.1`; image sets `0.0.0.0` **inside** the isolated container |
| Native app `PHANTOM_TRANSCRIPTION_URI` | `ws://127.0.0.1:8765/v1/transcribe`; not a server setting |

The provisioned host model is `.local\models\vosk-model-small-en-us-0.15`.
Whisper files are in `.local\models\faster-whisper-small.en` or `faster-whisper-base.en`.
The complete file set, byte counts and SHA256 values are checked before loading.
Runtime loads only explicit local paths with `local_files_only=True`; model downloads
exist only in the explicit setup helper, never in the recognition pipeline.
The app uses the same token through its native-launch configuration. Use
`Authorization: Bearer <token>`; no token is included in any documented command.

**Foreground native alternative** for development; the managed native launcher above is normal:

```powershell
python -m venv .\services\transcription\.venv
.\services\transcription\.venv\Scripts\python.exe -m pip install -r .\services\transcription\requirements.txt
.\scripts\Initialize-Transcription.ps1 -DownloadModel
$env:PHANTOM_TRANSCRIPTION_TOKEN_FILE = (Resolve-Path .\.local\transcription.token).Path
$env:PHANTOM_TRANSCRIPTION_MODEL_PATH = (Resolve-Path .\.local\models\vosk-model-small-en-us-0.15).Path
$env:PYTHONDONTWRITEBYTECODE = '1'
.\services\transcription\.venv\Scripts\python.exe .\services\transcription\server.py
```

Stop that foreground service with Ctrl+C before starting another service on the same port.
Relative environment paths resolve against the server process's working directory,
so absolute paths as above are recommended. Native runs are not subject to Docker's
memory/CPU/network limits. Tokens grant local transcription access; same-user/admin
inspection, OS dumps, host swap, and malicious local processes are not contained.

## Protocol, privacy, and resource behavior

Native lifecycle endpoints use the same local Bearer authentication, reject browser Origin/query/body,
and expose only protocol/instance ID/PID/readiness/activity metadata, never model paths or token.
Additional authenticated metadata includes engine/model/revision/config fingerprint,
diagnostic availability and numeric queue/fault/drop values, never model paths or tokens.
No field was added to the strict v1/v2 speech events or ready/stopped messages.
Only a launcher-managed instance supports graceful idle shutdown. This does not add remote
administration, arbitrary command execution, microphone capture or any third-party API.

An authenticated upgrade reserves the **single** session immediately, including
its 5-second start deadline. Each admitted session gets a new recognizer; immutable
model data is shared, mutable decoders are not. Unsupported audio is never converted
by the server. Only PCM16 little-endian, mono, 16000 Hz is accepted.

- Strict start/stop objects, 1024-byte control limit, nonempty/even audio frames up
  to **32000 bytes inclusive**, no compression negotiation.
- WebSocket framing hard limit 33024 bytes. Slightly oversized audio receives
  `bad_frame`; a larger transport-level message is closed with RFC 6455 code 1009
  before application processing. Such invalid framing cannot receive a JSON reply
  after aiohttp has closed the socket.
- HTTP parser: 32 headers, 1024-byte line/field limits, 4096-byte reader buffer,
  no body decompression, 5-second keepalive. Aiohttp 3.14.3's WebSocket read queue
  applies TCP backpressure at its fixed 512 KiB high watermark, plus a transport
  read; the application queue is independently limited to 32 frames / 64000 bytes.
- Live input token bucket: 32000 bytes/second, 64000-byte burst; 100 frames/second,
  200-frame burst. Fast file uploads are intentionally not accepted.
- Accurate-mode provisional decoding has one bounded queue and dedicated executor
  per selected source, never the event loop. One source's native work cannot block
  the other source's intake/decode progress. Legacy Vosk retains the single executor.
  Accurate finalization has a separate serial executor, so Whisper cannot block the
  PCM intake/Vosk queue. At most two immutable final spans (including the executing
  one) belong to each source. Accurate-mode rotation targets 15 seconds; a crossing
  protocol frame can extend the active source buffer to at most 16 seconds.
  No unbounded per-frame tasks, executor submissions or outgoing event queue are used.
  Fast decoder calls have a 5-second deadline, Whisper calls 20 seconds, sends 2 seconds,
  absent client PCM/control 15 seconds. Stop draining has a 60-second overall deadline;
  an in-flight native call can require up to 20 additional seconds to settle.
  `/v1/runtime.maxStopSeconds` includes cleanup/send allowance (85 seconds).
  Clients should allow at least 90 seconds rather than abandoning accepted finals early.
  A timed-out native call cannot safely be killed in Python, so the process refuses
  subsequent work; Docker's stop deadline is the final termination boundary.
- Eight-hour audio-duration cap. Accurate-mode decoder history is finalized/recreated
  after 15 audio seconds (legacy Vosk retains its 30-second explicit guard);
  absolute audio timing is retained.
  These forced segment boundaries are not speaker or sentence boundaries.
- Every accurate final owns one disjoint `[start_sample,end_sample)` PCM span.
  Endpoint/rotation transfers and clears that buffer exactly once; Stop transfers
  only the residual tail, including an empty tail. Timestamp fields denote owned
  audio spans, not forced-aligned word boundaries. Models can still misrecognize
  words at forced cuts; byte-perfect accounting is not a zero-word-error promise.
  Per-source offsets/reset state stay independent, and final IDs remain global.
  A forced 30-second synthetic probe produced pathological word repetition, and
  a temperature-fallback experiment exceeded the 20-second call deadline. Neither
  policy is the default. The shorter disjoint-span policy was measured separately;
  no phrase-specific text repair or repeated-word deletion was added.
  Newer provisional text on a source is withheld while that source's preceding final
  is pending, preventing an old final from clearing a newer partial in a strict client.
- Whisper uses Silero VAD, a digital-silence gate, deterministic decoding, no previous
  transcript conditioning, and no reference sentences. Optional vocabulary is the
  disclosed list of 29 individual programming terms in `technical-vocabulary.json`;
  it is not a replacement table. VAD/model failures are visible faults, not empty success.
- Backlog/rate errors close visibly; audio is never silently dropped to keep up.
  The service creates no synthetic silence: the native capture client supplies it.
- Stop drains accepted audio, emits a final event (possibly empty), then `stopped`.
  A transport disconnect cannot guarantee tail delivery. Service shutdown sends a
  generic fault and closes sessions; stop capture first when a graceful tail matters.
- Rejects nonempty browser Origin, query parameters, and bad/missing/duplicate auth.
  Token comparison uses `hmac.compare_digest`; auth/model/busy failures are safe
  JSON HTTP 401/503/409 responses **before** the WebSocket upgrade.
- Access logs and content-bearing Python/native decoder diagnostics are suppressed. Production
  stdout contains only static setup/start failure messages: no waveform, transcript,
  token, filesystem path, request URL, or exception trace.

Error codes: `auth`, `origin`, `busy`, `bad_format`, `bad_frame`, `backlog`,
`rate_limit`, `timeout`, `decoder_error`, `model_unavailable`, `session_limit`,
`server_stopping`. The native client should surface actionable status, never silently
restart a session after a fault.

### Metadata-only diagnostics

Native JSONL: `.local\diagnostics\transcription.jsonl`, rotated at 1 MiB with three
backups (at most four files). Each record contains only `schema_version`, `time_utc`,
`event`, `source`, `engine`, `model`, `fault`, `decode_ms`, `audio_ms`, `queue_bytes`,
`queue_frames`, `pending_finals`, `fault_count`, and `drop_count`. There is no audio,
text, token, path or full exception field. Events are allowlisted model-load,
session-start/stop, decode, finalize, fault and disconnect metadata.
I/O failure emits a generic stderr notice and sets authenticated
`diagnosticsAvailable:false`; it is never reported as successful logging.
Counters distinguish explicit overrun/rejection from successful accepted-audio processing.

## Pins, provenance, and license

`requirements.txt` pins all installed Python dependencies. Primary pins are
**aiohttp 3.14.3**, **Vosk 0.3.45**, **faster-whisper 1.2.1**, **CTranslate2 4.6.0**
and **ONNX Runtime 1.30.0**, tested with host **Python 3.13.14 x64**.
`whisper-models.json` pins MIT-licensed Systran converted OpenAI Whisper models:
`small.en` revision `d1d751a5f8271d482d14ca55d9e2deeebbae577f`;
`base.en` revision `3d3d5dee26484f91867d81cb899cfcf72b96be6c`.
Model-bin hashes match public LFS metadata; metadata-file hashes were computed from
the pinned HTTPS revision. These are integrity pins, not publisher signatures.
The image is `python:3.13.14-slim-bookworm` pinned to its verified registry manifest
digest in `Dockerfile`. Image availability is not a successful build or runtime test.
The recipe deliberately targets Linux amd64; ARM64 has not been qualified.

The official [Vosk model catalogue](https://alphacephei.com/vosk/models) identifies
**vosk-model-small-en-us-0.15**, English-US version **0.15**, as **Apache-2.0**.
[Official archive](https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip):

```text
Bytes:  41205931
SHA256: 30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498
```

This hash was **observed from the official HTTPS download on 2026-10-08**; it is
not represented as a publisher-signed checksum. `model.json` also pins every
extracted file hash. Setup writes
`.local\models\vosk-model-small-en-us-0.15.receipt.json`, retains the archive for
offline reprovisioning, and never overwrites a damaged existing model automatically.
The model is not redistributed in Git or the image. The model's license does not
choose a license for this repository.

## Validation and current evidence

Historical native-runtime evidence superseded the earlier Docker-required startup: native setup/start/
reuse/status, detached-starter survival, automatic app launch/recovery, and authenticated owned idle
shutdown passed on this Docker-free host. At that stage, 37 service/management tests passed, including wrong-instance,
unmanaged, browser, body/query and active-transcription shutdown rejection.
Real silent synthetic checks pass through Python and the C# WebSocket client; no playback occurred.

The owner's speed/accuracy comparison is [SPEECH-BENCHMARK.md](../../docs/SPEECH-BENCHMARK.md).
That historical complete-file comparison is unchanged. The new
`tools\SpeechBench\quality_regression.py` and `quality_stress.py` exercise the actual
hybrid service with two installed synthetic voices, clean/noisy technical and unrelated
text, nonspeech controls, real-time pacing, dual input, forced rotation and Stop.
Raw fixtures and detailed operational output stay under `.local\speech-quality`;
sanitized results go under `tools\SpeechBench\results`. Synthetic results are not
the user's recording or microphone qualification. Programming language alone is not recognition quality.

The October 9 accurate-profile regression and retained failed experiments are in
[`synthetic-quality-20261009.json`](../../tools/SpeechBench/results/synthetic-quality-20261009.json).
The final configuration uses independent source workers, unchanged 32-frame/64000-byte
source intake limits, a separate bounded Whisper worker, and 15-second rotation.
On 35 identical two-voice synthetic cases (432 reference words), final raw WER
fell from 12.04% to 4.40%. Unhinted base.en and small.en ablations remain visible.
LeetCode compounding is penalized by that raw WER; a separate generic character
metric is also reported, never substituted for the raw word score.
The final profile's end-of-speech-to-final median/p95 were 2.736/3.017 seconds.
This is a quality/latency tradeoff, not a claim that accurate finals beat Vosk's
approximately one-second endpoint latency. Fast Vosk provisional updates remain.
Some homophone and long-speech errors remain; neither real microphone quality nor
100-percent accuracy is claimed. No port-8765 service was started or stopped by this regression.

```powershell
.\services\transcription\.venv\Scripts\python.exe -m unittest discover -s .\services\transcription\tests -v
.\services\transcription\.venv\Scripts\python.exe -m pip check
# With the native or Docker service already healthy:
.\services\transcription\Test-SyntheticTranscription.ps1
# Also exercise the real C# client with the same silent in-memory fixture:
.\services\transcription\Test-SyntheticTranscription.ps1 -NativeClient
# Exercise normal automatic native service/app launch and owned cleanup:
.\scripts\Test-NativeLifecycle.ps1 -UseLauncher
```

The unit suite uses injected recognizers **only in tests**, covering authentication,
Origin, busy admission, format/framing limits, partial/final timing, flushing,
repeat sessions, backlog, deadlines, decoder failures, health, and shutdown.
The production entrypoint always loads real Vosk.

The synthetic integration script uses installed Windows **Microsoft David Desktop**
via `System.Speech`, generates a fixed test phrase directly into memory at the exact
wire format, and sends it over the real WebSocket at live rate. It does not play
sound, fetch meeting audio, open a microphone, or write WAV/PCM/transcript files.
It asserts known synthetic keywords, nonempty partials, ordered segment IDs/times,
tail-before-stopped, then a separate silence session. Test output contains counts,
not transcript text.

Observed native evidence on 2026-10-08:

- Official model integrity checked; initializer reruns use the cached model,
  preserve the token, and work from a different current directory. PowerShell 7
  and Windows PowerShell 5.1 setup both passed without elevation. Script copies
  and PowerShell syntax checks passed.
- Real model + WebSocket: **5.900 seconds synthetic speech, 11 nonempty partials,
  2 finals, all 7 expected keywords; stop-tail ordering passed**.
- A second real-model silence session returned an empty final with segment ID 1.
- The actual C# `TranscriptionSocket` integration mode also passed against this
  native Vosk service: **2 finals, 13 partial events; expected synthetic phrase
  found**. Command: `dotnet run --project
  .\tests\PhantomFudge.Audio.Tests\PhantomFudge.Audio.Tests.csproj --no-restore --
  --service ws://127.0.0.1:8765/v1/transcribe --synthetic-wave <generated-wave>
  --expect <synthetic-phrase>`, with `PHANTOM_AUDIO_TEST_TOKEN` set in process memory
  from the local token file, never printed. For this separate client integration
  only, System.Speech generated a PCM16/mono/16000-Hz WAV under `.local`; the WAV
  was deleted in `finally`. No playback or real audio was used. This proves
  C# transport to the real recognizer, not WASAPI capture or Docker execution.
- A subsequent audio-agent harness run reported a **fully in-memory C# → Vosk
  pass**: 366720 bytes / 11.46 seconds of Microsoft David synthetic PCM,
  **27 partial events, 5 monotonic finals**, expected synthetic phrase matched,
  and stop/finals drained. The owned console harness accepts
  `--service ws://127.0.0.1:8765/v1/transcribe --synthetic-pcm - --expect
  <synthetic-phrase>` with **raw PCM bytes** on stdin and the token in
  `PHANTOM_AUDIO_TEST_TOKEN`. Write through a binary subprocess stdin stream
  (for example `ProcessStartInfo`), not a PowerShell text pipeline or Base64.
  No WAV was written for this later run.
  The requested native validation service was stopped after the audio agent
  confirmed completion.
- **26 unit tests passed** (final run 3.792 seconds); `pip check`: no broken requirements.
- An actual missing-configuration subprocess exited 2 with only a static generic
  message and no stderr/path/trace. The start script explicitly rejected absent Docker.
- An actual server process with an absent model returned HTTP 503 and
  `modelAvailable:false`, with only static generic diagnostics. All temporary native
  validation server processes were stopped after testing; no fallback is left running.
- PyPI metadata advertised aiohttp 3.14.4, but its wheel failed HTTPS retrieval and
  the configured index offered up to 3.14.3; 3.14.3 was successfully installed and
  pinned instead. TLS verification was not disabled. Reassess dependency patches
  before release; these pins are evidence, not a vulnerability-free certification.

**Not executed / remaining qualification:** Docker CLI/engine is absent. No Docker
build, container health, secret mount permission, internal-network host reachability,
network-isolated inference, container resource/stop behavior, or Linux-wheel load
has been proven here. No Docker installation or machine virtualization change was
attempted. Native tests do **not** satisfy that gate.

The synthetic result is not a meeting accuracy/latency benchmark or language/voice
coverage claim. Real playback loopback, device loss, full native GUI flow, protected
transcript capture pixels, long-session memory/CPU/latency, owner-approved corpora,
and user UX acceptance remain separate qualification workstreams.

## V2 dual-source evidence (2026-10-08)

The frozen [dual-source contract](../../docs/DUAL-SOURCE-AUDIO.md) adds computer,
microphone, or both tracks on the same authenticated endpoint. The service still
does not open capture devices. V1 start/ready/audio/result shapes and management
`protocolVersion: 1` remain unchanged; speech v2 explicitly negotiates its sources.
Each source has its own decoder, partial state, timing, rate credits and eight-hour
audio limit. One shared model and one CPU worker serve the session. Queue bounds
scale to 128000 PCM bytes / 64 frames for both sources; source prefixes do not
consume PCM credits. The transport ceiling is 33026 bytes exclusive, allowing the
32001-byte v2 maximum frame plus bounded control headroom.

```powershell
.\services\transcription\.venv\Scripts\python.exe -m unittest discover -s .\services\transcription\tests -v
.\services\transcription\tests\Test-DualSyntheticTranscription.ps1
```

The complete service/management unit suite passed: **69 tests** in 7.217 seconds.
The second command uses an ephemeral in-process loopback server, a synthetic token,
the verified cached model and two different System.Speech sentences generated
directly in memory. It never touches an existing service or plays/captures audio.
Observed real-model result: Computer 6.900 seconds / 11 nonempty partials / 3 finals;
Mic 7.055 seconds / 10 nonempty partials / 3 finals; all seven expected keywords per
source matched without crossing tracks. Shared model/distinct decoders, global IDs,
independent timestamp bounds, canonical stop tails, mic-only silence and unchanged
v1 silence all passed. No audio/transcript files were written. This is synthetic
protocol evidence, not microphone-device, acoustic-echo or real-meeting qualification.
