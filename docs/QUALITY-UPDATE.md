# October 9 actual-use correction

The measurements below describe the earlier direct-answer correction. Updated source also adds
[multi-line transcript selection](LOCAL-QUESTIONS.md) and an optional
[Clarify first loop](QUESTION-CLARIFICATION.md). The latter deliberately permits explicit
Yes/correction turns; its full conversation has not been latency-qualified by these older receipts.

This update addresses the reported small, scroll-heavy window, unreadable transparent text,
poor technical dictation and Agency responses that described directory inspection instead of
answering the question.

## Reading space

The requested default is now **1240 x1520**, twice the earlier width and height, clamped to the
current monitor's work area. On the observed1920 x1200 work area that means1240 x1176: controls
are not placed off-screen merely to claim an exact height.

Transcript and Q/A are side by side at wide sizes. Primary controls stay visible without an outer
question-panel scrollbar. Small-window layouts remain usable; an indefinitely long transcript or
answer still needs its own text scrollbar. Notes remain optional and collapsed initially.

Reading text is **white in DeepSeaFoam** and **black in Harbor Daylight/Plain**. The answer,
question and transcript areas share the Background slider; text stays opaque. A synthetic rendered
pixel check covers10% and100% answer backgrounds. This is appearance evidence, not a recording of
the desktop or a capture-exclusion result.

The long yellow compatibility disclaimer is no longer a persistent warning. A quiet requested-state
indicator and deliberate inline **Details** retain the real limitations. Actual failures remain visible.

## Speech recognition

The default local pipeline now uses fast Vosk provisional text plus **Whisper small.en CPU-int8**
final text, with four CPU threads and disclosed general technical vocabulary. It does not replace
the reported wrong sentence using hardcoded string rules.

| Synthetic paced comparison | Vosk | New small.en technical finals |
| --- | ---: | ---: |
| Raw word error rate | 12.04% | 4.40% |
| Character error rate | 6.37% | 0.263% |
| Speech end to final, median | 1.006s | 2.736s |
| Speech end to final, p95 | 1.217s | 3.017s |
| Observed faults/drops | 0 /0 | 0 /0 |

These are two synthesized voices, noise/non-speech controls and long dual-source stress—not the
owner's microphone. Raw WER preserves compound-word differences (`LeetCode` versus `leet code`).
Unhinted small.en had lower raw WER, while technical vocabulary improved character/proper-name
spelling. All ablations and errors are retained in
[the quality receipt](../tools/SpeechBench/results/synthetic-quality-20261009.json).
Residual `to/two` and `some/sum` errors remain; no100% accuracy claim is made.

Models and dependencies have been provisioned on this machine. The authenticated owned service
was confirmed idle and upgraded through its normal manager; it reports ready, hybrid-whisper/
small.en, diagnostics available and no active capture. No real microphone or playback was opened.
Future setup, including a fresh extracted preview, remains:

```powershell
.\scripts\Initialize-Transcription.ps1 -DownloadModel
.\scripts\Run-PhantomFudge.ps1
```

Missing accurate-model files fail explicitly; there is no silent return to Vosk-only mode.
Old active/foreign services are never killed to force an upgrade. Stop releases capture first and
then drains accepted work; the90second cap is a worst-case failure/queued-refinement bound, not
normal finalization latency. The measured normal stop median/p95 were0.480/0.746seconds.

## Direct answers and the five-second target

The generic coding-agent persona was the wrong behavior for a standalone question. The new path
selects a controlled answer-only role, one exact question as data, no tools and no follow-up inputs.
It can state a reasonable assumption and answer rather than wait for another message or inspect
a workspace. Generated commands are never executed by the app.

Agency's default is now the measured **GPT-5.6 Sol Fast / low** profile. A prepared ACP process is
reused, but each question receives a fresh session with no previous question/answer history.
Preparing the transport/session makes **no model prompt**. It is shown separately as preparation;
the app keeps the instance rather than paying a full launcher cold start on every Ask.

The final exact-text suite recorded **three prepared-session complete answers in2.992–4.176seconds**.
It used the owner's intended question and their reported malformed ASR text without rewriting either.
The malformed case explicitly stated its Two Sum assumption and returned a solution. All three
returned C# solutions compiled and passed33ordinary/full-integer-boundary checks, with no follow-up.
Earlier cold/preparation work took roughly20–31seconds and **did not meet** the five-second target.
Slow, clarification, wrong-problem and overflow failures across earlier models remain preserved.

This demonstrates the target on those synthetic cases, not every future question, network condition
or model load. “Complete” is not a first token, filler or clipped code. The final controls use the
exact user-provided strings, but are still typed synthetic inputs, not an audio recording.
See [timings](../tools/AgencyProbe/results/latency-suite-20261009-h-exact-fast.json)
and [code checks](../tools/AgencyProbe/results/latency-suite-20261009-h-quality.json).

## Diagnostics are now part of the app

Always-on local logs record request/capture correlation, engine stages, levels/clipping, durations
and safe errors. Default logs do not contain audio, notes, tokens, transcripts or answer bodies.
Text is an explicit local opt-in. See [diagnostics](DIAGNOSTICS.md).

```powershell
.\scripts\Show-Diagnostics.ps1 -Last 10
.\scripts\Export-Diagnostics.ps1
```

The earlier preview's empty generic speech log and deleted temporary CLI request directories cannot
reconstruct what happened during the reported interaction. New logs are not retroactive evidence.

## Applying the update

Save/copy needed content, then close the old app with **X**, not Hide. Relaunch the updated source
or full extracted preview so the new code is loaded. Agency preparation starts only for an explicitly
configured Agency provider and sends no question. Wait for Ready, then submit the intended draft.

Direct Azure, recipient-side capture/cursor qualification, actual-microphone quality, broad answer
quality and repeatable latency distributions remain separate gates. No new sign-in, organization-data
connector, cloud speech or Docker runtime is required by this update.
