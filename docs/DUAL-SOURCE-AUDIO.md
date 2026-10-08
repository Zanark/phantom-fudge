# Computer audio plus microphone dictation

Owner request: 2026-10-08 14:01. This explicitly supersedes the earlier microphone-exclusion
requirement. The app can capture Computer, Microphone, or Both after the user's explicit Start.
It does not read chat applications, clipboard/chat history, meeting APIs, or browser DOM.
The user can read a chat question aloud for transcription through the selected microphone.

## Design and boundaries

Use two independent PCM tracks/recognizers, not a mixed waveform. This preserves input labels
and avoids destroying recognition when computer audio and local dictation overlap. Labels are
**Computer** and **Mic**, not verified speaker identities. Acoustic echo can still reach the mic
when speakers are used; headphones help. No acoustic-echo-cancellation guarantee is made.

C# captures selected WASAPI endpoints: render loopback for Computer and a capture endpoint for
Microphone. Native Python/Vosk recognizes each track independently in one authenticated local
session. Docker stays optional and last. No cloud audio or automatic recording is introduced.

Both is the new default selection, but no endpoint is opened until Start. Missing/denied/lost
selected inputs produce an error and stop the whole session; never silently substitute another
device or downgrade to one input. Stop/hide/lock/suspend/protection failure releases both tracks.
Switching input mode/device requires stopping first. Windows may display its own microphone
privacy indicator; window capture exclusion does not suppress OS indicators.

This implementation/test work uses synthetic PCM only. It does not authorize the agent to
record the user's live microphone or meeting while editing the application.

## Version 2 protocol - frozen integration contract

Keep v1 unchanged for existing computer-only clients/tests. The endpoint, authentication,
loopback restriction, one-active-WebSocket policy and native lifecycle management remain unchanged.
Authenticated `/v1/runtime` keeps management `protocolVersion:1` and adds
`"speechProtocolVersions":[1,2]` so the native launcher can recognize a stale v1-only service.
An owned idle stale service may be restarted through its authenticated shutdown endpoint;
active transcription must never be interrupted automatically.

V2 start:

```json
{"type":"start","protocolVersion":2,"sampleRate":16000,"channels":1,"encoding":"pcm_s16le","sources":["computer","microphone"]}
```

Sources is a nonempty unique subset of `computer` and `microphone`, in canonical order.
Mic-only uses `["microphone"]`; v2 computer-only uses `["computer"]`. Unknown/duplicate sources
or invalid format/version are rejected before any native capture begins.

V2 ready echoes the same fields except `"type":"ready"`. No silent downgrade to v1.
Every v2 binary WebSocket message is **one source byte followed by PCM**:

- Byte `1`: Computer; byte `2`: Microphone.
- PCM is16kHz mono signed16-bit little-endian, nonempty, even byte length, at most32000PCM bytes.
- Therefore total wire size is3..32001bytes, always odd. Source must be declared in start.
- Preserve sample bytes exactly; never concatenate tracks into one recognizer or mix samples.

V2 partial/final events include `"source":"computer"` or `"source":"microphone"`.
Final IDs remain globally strictly increasing within this WebSocket session, across sources.
Each source has its own monotonic audio-relative timestamps and partial-text state.
One source's final must not clear the other source's pending text.

```json
{"type":"partial","source":"microphone","text":"a dictated question"}
{"type":"final","source":"microphone","text":"a dictated question","segmentId":1,"startSeconds":0.0,"endSeconds":1.5}
```

`{"type":"stop"}` drains accepted audio, finalizes every declared source in canonical order,
then sends `{"type":"stopped"}`. Even an empty final has its source and global ID.
Safe error envelopes remain v1-shaped. One source failure aborts the session visibly.

Bounds scale only with declared source count (maximum2): pending PCM at most64000bytes per
source /128000total; server frame-queue cap at most32per source. Rate credits and audio-duration
limits are tracked per source, not one combined stream. Keep one bounded worker and shared
immutable model; each track has a separate mutable recognizer. Native per-source idle silence
may keep recognition moving; server does not invent microphone audio.

V1 frames remain unprefixed and v1 result envelopes have no source field. Old clients continue
to behave as computer-only. Source-labelled v2 must not accept a v1 ready/result as success.

## C# integration types

`PhantomFudge.Core.AudioSource` is the shared enum: Computer=1, Microphone=2.
`AudioCaptureMode` is Computer/Microphone/Both. Wire names and display names are centralized.

The native adapter retains its old StartAsync(renderDeviceId, endpoint, token, cancellation)
overload for v1 compatibility. New API in `PhantomFudge.App.Audio`:

```text
record MicrophoneDevice(string Id, string Name)
record AudioInputSelection(string? ComputerDeviceId, string? MicrophoneDeviceId)
LoopbackTranscriber.GetMicrophoneDevices() -> IReadOnlyList<MicrophoneDevice>
LoopbackTranscriber.StartAsync(AudioInputSelection, Uri, string, CancellationToken)
TranscriptEvent(...existing fields..., AudioSource? Source = null)
event Action<AudioSource, float> SourceLevelChanged  // existing LevelChanged retained
```

New selection overload uses v2, even for one input. Existing overload remains v1.
`TranscriptionSocket` adds source-aware ConnectAsync and TryEnqueueAudio overloads; old APIs
remain. Explicit mode selection and source labels do not add permission/rehearsal checkboxes.

## Required evidence

Protocol unit tests: unchanged v1, independent two-source state, mic-only, unknown/duplicate/missing
source, wrong prefix/length/rate, queued stop/finals, global IDs, per-source time bounds and faults.
Native tests: input selection validation before opening hardware, direction-correct endpoint
enumeration, source-preserving frame bytes, cancellation/device loss/backpressure/repeated stop.
Real recognizer integration: two different synthetic sentences interleaved over v2, correct source
labels and expected words, no playback or live input capture. UI tests cover all modes, both device
selectors, source partial/final display, stopping guards, themes and default passive behavior.
