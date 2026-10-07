# Required local meeting-output transcription

Added 2026-10-08 by explicit user instruction. This is REQUIRED v1 implementation, not future scope.
The native client and local recognizer are now implemented; see IMPLEMENTATION-STATUS.md for actual
evidence. Docker recipe execution awaits the owner's separate Docker Desktop installation/start.
The user selected local Docker processing, with no meeting audio sent to a cloud service.

## Architecture

```text
Meeting -> selected Windows playback device
              -> native WASAPI render-endpoint loopback (never microphone)
              -> PCM16 little-endian / mono / 16000 Hz
              -> authenticated WebSocket on literal loopback address
              -> Docker local Vosk streaming recognizer
              -> partial/final text in the SAME capture-excluded WPF window
```

The GUI and Windows audio endpoint must run on the Windows host. Windows containers do not
provide the interactive desktop required by this application. Docker runs the headless speech
service, not the protected HWND or a fake Linux replacement for WASAPI.

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
list only active render endpoints. Do not silently switch endpoints when a headset disconnects.
Capture all output on that selected endpoint; process-specific capture is a separate future enhancement.
The older NAudio article's claim that Windows has no process-specific API is not used as a current
platform assertion; the endpoint-scoped implementation simply does not implement that facility.

No speech starts on launch. Show capturing/stopped/fault status and an output meter. Stop releases
WASAPI, drains bounded queued audio/final recognition, and disposes the socket. Hide/lock/suspend/
capture-policy failure stops transcription; no automatic restart without the user's action.

Keep transcript text in memory, separate from editable notes. No automatic audio files, transcript
autosave, telemetry, third-party HTTP calls, speaker labels, or fabricated confidence. Explicit export
uses the existing safe save path and is disabled during Present mode.

An absent model, invalid token, unavailable service, unsupported format, device loss, disconnect,
full queue, decoder failure, or malformed message must produce an actionable visible state.
Never fall back to a microphone or cloud recognition.

## Docker requirements

Run a non-root service, publish only `127.0.0.1:8765`, authenticate the WebSocket, mount a preinstalled
model read-only, keep audio in memory, and expose a health endpoint that reports model readiness.
Keep local tokens/models out of image build context and source control. Model setup can access its
documented public download once; inference must work with internet access disabled.

Pin service dependencies and provide reproducible unit/protocol tests plus a container recipe.
Do not claim a successful Docker run when the Docker CLI/engine is unavailable. At implementation
start on this machine, Docker was not installed; a native service run is not container evidence.

## Required qualification additions

Synthetic speech fixture -> real recognizer -> WebSocket -> transcript event must work end-to-end.
Separately exercise actual playback-device loopback, proving no microphone endpoint was opened.
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
