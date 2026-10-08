# Local question drafts and exact snapshots

**Local drafts are independent of answer providers.** Snapshot-only mode sends nothing.
An explicitly configured Copilot CLI or local-model provider can receive only the question on Ask.
Direct Azure answers remain future work. The app does not read chats or send meeting audio.
See [answer provider setup](ANSWER-PROVIDERS.md) before enabling requests.

## Use the composer

1. Open **Question** beside the transcription controls. Transcription remains the default view.
2. Choose **Mic segments** (the default), **Computer segments**, or **All sources**. Click only the
   intended rows, then **Use selected**. Multiple rows can form one question.
3. Alternatively, choose **Type / edit** and type the question directly. This deliberately takes
   keyboard focus for the question, without stopping computer/microphone transcription or enabling
   note editing. **Done typing** or Escape returns to passive interaction.
4. Review the draft and its provisional/final, user-edited and revision indicators.
5. **Freeze locally** creates a snapshot without sending. With an explicitly configured provider,
   **Ask provider** sends only that exact question and displays the streamed answer beside it.
   The provider notice distinguishes GitHub cloud inference from a loopback local model.

No repeated permission or recipient-rehearsal confirmation has been added. Start/Stop still controls
audio separately. Question selection, typing and Ask never start audio.

## What can and cannot enter the question

The structured store keeps capture-session identity, source, utterance identity, revisions and
qualified audio-relative final bounds. A local session GUID is stamped onto events before they enter
the dispatcher; a late callback from a previous capture cannot enter the next session.

Display labels are for reading, never parsed to choose content. A Mic selection does not include
Computer text, earlier unselected Mic segments, notes, clipboard history or chats. **All sources**
only broadens the visible chooser; it does not select or submit those rows automatically.

Selected partials can evolve into final text until you edit the draft. Manual edits detach that text
from automatic recognition replacement. A frozen snapshot is never changed by later recognition,
typing, clearing, eviction or a new capture. Another explicit Ask creates another local snapshot;
question marks and natural finals never do so. One provider request may be active at a time;
Cancel it and wait for cleanup before submitting another. Cancel/Hide never automatically retries.

Selecting across capture sessions is rejected rather than silently combining histories. The draft
keeps immutable copies of its selected entries when the bounded chooser evicts them. Clearing the
transcript does not silently erase the separately copied draft.

## Bounds and deliberate actions

- The question limit is8192 UTF-16 code units (`string.Length` in .NET), not model tokens or visual grapheme clusters. Empty/whitespace-only Ask is rejected. Oversized selection,
  typing, paste or automatic revision is rejected atomically; the app does not silently truncate.
- Structured history is bounded to256segments and64000characters, separate from the unchanged
  display/export transcript limit. Source epochs are locally labelled0 for current v2; this is not
  evidence of a negotiated source-commit feature.
- **Copy draft** copies only the currently displayed question to the Windows clipboard, explicitly.
  Clipboard data is outside the window's capture policy. Nothing is copied automatically.
- **Clear question** explicitly discards the local draft and last frozen snapshot, without clearing
  notes or transcription. Closing with question content uses the existing unsaved-data safeguard;
  saving notes/exporting transcripts does not pretend to save a question.
- The app's draft and answer history remain memory-only. The external CLI may retain session data
  in its dedicated profile; do not confuse application memory-only state with provider zero retention.

## Deferred boundaries

**Finish dictation / armed live-Mic spans** need TODO29's negotiated source-specific commit and
start/end boundaries. Current v2 partial text cannot support a sample-exact cut of speech already in
the decoder/device buffer. Use explicit known segments or typed text in this slice. Global Stop has
not been repurposed into a question boundary.

TODO30/31 retain an approved direct Azure profile/adapter as future work. Installed-provider
workstream39 and the shared answer UI32 do not depend on Azure. No model/resource/credential is
guessed or installed automatically. Docker remains optional and last.

## Source map

`StructuredTranscriptStore`, `TranscriptSegment`, `QuestionDraft` and `QuestionSnapshot` live in
`src\PhantomFudge.Core`. `MainWindow.Questions.cs` owns the local composer; the main window preserves
the existing transcript display/export. `MainWindow.QuestionDiagnostics.cs` exercises the UI with
synthetic data only. The native adapter stamps local session identity without changing v1/v2 wire
schemas. The handbook and public JOURNAL describe this implemented slice separately from the
remaining Azure design.
