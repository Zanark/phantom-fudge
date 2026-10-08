# Local question drafts and exact snapshots

**Implemented locally; Azure answers are not connected.** This is the first question-to-answer
implementation slice (TODO28), not a working cloud-answer client. The app does not read chats and
does not send a meeting, question or audio to a model.

## Use the composer

1. Open **Question** beside the transcription controls. Transcription remains the default view.
2. Choose **Mic segments** (the default), **Computer segments**, or **All sources**. Click only the
   intended rows, then **Use selected**. Multiple rows can form one question.
3. Alternatively, choose **Type / edit** and type the question directly. This deliberately takes
   keyboard focus for the question, without stopping computer/microphone transcription or enabling
   note editing. **Done typing** or Escape returns to passive interaction.
4. Review the draft and its provisional/final, user-edited and revision indicators.
5. **Ask as shown** freezes the exact displayed text in a local immutable snapshot. The banner and
   status explicitly say that Azure is not connected and nothing was sent.

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
question marks and natural finals never do so.

Selecting across capture sessions is rejected rather than silently combining histories. The draft
keeps immutable copies of its selected entries when the bounded chooser evicts them. Clearing the
transcript does not silently erase the separately copied draft.

## Bounds and deliberate actions

- The question limit is8192characters. Empty/whitespace-only Ask is rejected. Oversized selection,
  typing, paste or automatic revision is rejected atomically; the app does not silently truncate.
- Structured history is bounded to256segments and64000characters, separate from the unchanged
  display/export transcript limit. Source epochs are locally labelled0 for current v2; this is not
  evidence of a negotiated source-commit feature.
- **Copy draft** copies only the currently displayed question to the Windows clipboard, explicitly.
  Clipboard data is outside the window's capture policy. Nothing is copied automatically.
- **Clear question** explicitly discards the local draft and last frozen snapshot, without clearing
  notes or transcription. Closing with question content uses the existing unsaved-data safeguard;
  saving notes/exporting transcripts does not pretend to save a question.
- Everything is memory-only. There is no automatic question history, persistence or crash recovery.

## Deferred boundaries

**Finish dictation / armed live-Mic spans** need TODO29's negotiated source-specific commit and
start/end boundaries. Current v2 partial text cannot support a sample-exact cut of speech already in
the decoder/device buffer. Use explicit known segments or typed text in this slice. Global Stop has
not been repurposed into a question boundary.

TODO30 needs an approved Azure profile/identity/data policy before cloud integration. TODO31/32 add
real streamed answers and their protected lifecycle. No model, region, resource, credentials or
cloud permission was guessed during local implementation. Docker remains optional and last.

## Source map

`StructuredTranscriptStore`, `TranscriptSegment`, `QuestionDraft` and `QuestionSnapshot` live in
`src\PhantomFudge.Core`. `MainWindow.Questions.cs` owns the local composer; the main window preserves
the existing transcript display/export. `MainWindow.QuestionDiagnostics.cs` exercises the UI with
synthetic data only. The native adapter stamps local session identity without changing v1/v2 wire
schemas. The handbook and public JOURNAL describe this implemented slice separately from the
remaining Azure design.
