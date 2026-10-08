# Required local computer/microphone transcription

Added 2026-10-08 by explicit user instruction. This is REQUIRED v1 implementation, not future scope.
The native client and local recognizer are now implemented; see IMPLEMENTATION-STATUS.md for actual
evidence. The owner clarified that Docker should come last, not be required for the solution.
Normal operation is native C# plus native Python, with no meeting audio sent to a cloud service.

## Architecture

```text
Meeting -> selected render endpoint -> WASAPI loopback ---- Computer track --+
Dictated question -> selected capture endpoint -> WASAPI -- Mic track ------+
       -> one authenticated loopback WebSocket (separate v2 source frames)
       -> independent native Python/Vosk recognizers
       -> labelled partial/final text in the SAME capture-excluded WPF window
```

The owner's14:01 request explicitly adds microphone dictation; it supersedes the old never-mic
requirement. Computer, Mic and Both modes require explicit Start. No chat reading is implemented.
The full v2 contract and limits are in [DUAL-SOURCE-AUDIO.md](DUAL-SOURCE-AUDIO.md). V1 below remains
supported for existing computer-only clients, not the new app's combined-input protocol.

The GUI and Windows audio endpoint run on the Windows host. Recognition already runs in Python;
C# is not the current speech-decoding engine. A loopback protocol separates these responsibilities.
The same headless Python service can later be containerized without changing the C# client.
Windows containers do not provide the interactive desktop required by this application.

Initial engine: Vosk's offline streaming API with an explicitly installed English model.
Its partial results make it suitable for a CPU-first prototype without chunk-boundary text
stitching. Accuracy, names/technical vocabulary, latency, and accents require real evaluation;
small-model speed is not a promise of accurate meeting transcripts. Larger/language-specific
models can be provisioned separately. Do not download a model implicitly during a live meeting.

## Frozen v1 wire contract

- Native client endpoint: `ws://127.0.0.1:8765/v1/transcribe` (literal loopback only).
- Request header: `Authorization: Bearer <local random token>`. Never put tokens in URLs/logs.
- Start text frame:
  `{"type":"start","sampleRate":16000,"channels":1,"encoding":"pcm_s16le"}`.
- Server confirms `{"type":"ready","sampleRate":16000,"channels":1,"encoding":"pcm_s16le"}`.
  Optional model metadata is allowed. Capture begins only after this confirmation.
- Audio: binary frames, nonempty/even byte counts, maximum 32000 bytes per frame.
- Partial: `{"type":"partial","text":"..."}` replaces the current unfinished line.
- Final: `{"type":"final","text":"...","segmentId":1,"startSeconds":0.0,"endSeconds":1.2}`.
  IDs increase within one session; timing is recognizer audio-relative, not speaker identity.
- Stop text frame: `{"type":"stop"}`. Server flushes a final result, sends `{"type":"stopped"}`,
  and ends the session. The client drains the final text before graceful shutdown.
- Error: `{"type":"error","code":"stable_code","message":"safe generic text"}` and close.
  No note/audio content, secrets, stack traces or local model paths in remote errors.
- One active session initially; reject competing clients explicitly rather than sharing recognizer state.
- Bound incoming/outgoing queues. On sustained backlog, stop visibly rather than drop speech silently
  or let captions drift minutes behind. Silence can be supplied by the native client when the
  output endpoint emits no packets; it is not microphone sampling.

## Required behavior and boundaries

Explicitly select a playback device. The default can be the current communications/default output;
the computer selector lists active render endpoints, and the mic selector lists active capture endpoints.
Do not silently switch endpoints or drop an input when a headset disconnects.
Computer captures all output on its selected endpoint; process-specific capture is a separate enhancement.
The older NAudio article's claim that Windows has no process-specific API is not used as a current
platform assertion; the endpoint-scoped implementation simply does not implement that facility.

No speech starts on launch. Show capturing/stopped/fault status and an output meter. Stop releases
WASAPI, drains bounded queued audio/final recognition, and disposes the socket. Hide/lock/suspend/
capture-policy failure stops transcription; no automatic restart without the user's action.

The owner handles permission and recipient rehearsal externally. Per the explicit October 8
request, no permission/preflight checkbox, confirmation dialog or repeat acknowledgment is
shown or required by Start/Present. This does not remove technical protection or device checks.

Keep transcript text in memory, separate from editable notes. No automatic audio files, transcript
autosave, telemetry, third-party HTTP calls, speaker labels, or fabricated confidence. Explicit export
uses the existing safe save path and is disabled during Present mode.

An absent model, invalid token, unavailable service, unsupported format, device loss, disconnect,
full queue, decoder failure, or malformed message must produce an actionable visible state.
Never fall back to an unselected source/device or cloud recognition.

## Native service lifecycle

Initialize-Transcription prepares a local Python virtual environment, pinned dependencies, the
explicit model and local token. Start-Transcription starts/reuses this workspace's native service.
Run-PhantomFudge invokes it automatically unless NotesOnly is requested. No audio starts until
the owner's explicit Start action in the app.

The manager uses a local lifecycle lock and a per-launch random instance ID. Authenticated runtime
metadata must match this workspace's receipt before reuse or stop. Stop-Transcription requests a
graceful idle shutdown; active transcription and unrelated/untracked services are left alone.
The service remains idle after closing the UI until explicitly stopped. Neither a service
startup nor direct recognizer benchmark opens a microphone or playback endpoint.

## Deferred Docker packaging requirements

Run a non-root service, publish only `127.0.0.1:8765`, authenticate the WebSocket, mount a preinstalled
model read-only, keep audio in memory, and expose a health endpoint that reports model readiness.
Keep local tokens/models out of image build context and source control. Model setup can access its
documented public download once; inference must work with internet access disabled.

Pin service dependencies and provide reproducible unit/protocol tests plus a future container recipe.
Do not claim a successful Docker run when the Docker CLI/engine is unavailable. At implementation
start on this machine, Docker was not installed; a native service run is not container evidence.
That missing Docker runtime is not a blocker for native transcription or the native release.

## Engine comparison, independent of output mute

Speech quality depends on the recognizer/model and input, not whether C# or Python calls it.
The requested comparison feeds identical synthetic PCM directly into local recognizers. Laptop
mute does not affect those tests. Raw-file decode speed must be separated from streaming partial/
final latency, and TTS fixture accuracy must not be sold as real meeting accuracy.
Hardware playback-loopback tests remain a separate explicitly opt-in operation; none are run while
the owner sleeps. The benchmark workstream is23.

## Required qualification additions

Synthetic speech fixture -> real recognizer -> WebSocket -> transcript event must work end-to-end.
Separately qualify actual selected playback/microphone hardware only with explicit test authorization;
computer-only mode must not open a mic, and synthetic unattended tests must open neither input.
Test silence/stop tail flushing, repeated Start/Stop, wrong token, browser Origin, invalid PCM,
bounded queues, device removal, service restart, no-model readiness, and no hidden audio persistence.
Review captured transcript partial/final updates with the same C01-C14 HWND/meeting matrix as notes.
Measure partial/final latency, long-session memory, and accuracy on an owner-approved synthetic corpus.

## Primary references

- Windows loopback captures rendering-endpoint output:
  <https://learn.microsoft.com/en-us/windows/win32/coreaudio/loopback-recording>
- NAudio implementation supports shared-mode auto PCM conversion:
  <https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Wasapi/WasapiCapture.cs>
- Vosk offline streaming toolkit and model catalogue:
  <https://alphacephei.com/vosk/> and <https://alphacephei.com/vosk/models>
- Windows container interactive-desktop limitation:
  <https://learn.microsoft.com/virtualization/windowscontainers/quick-start/lift-shift-to-containers>
