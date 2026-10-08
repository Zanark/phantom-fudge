---
title: "12. Performance and Benchmarks"
description: "Read the local speech benchmark as an experiment: scoring, timing boundaries, reproducibility, and limits."
outline: deep
---

# 12. Performance and Benchmarks

## Why a fast decoder is not necessarily a responsive application

Performance starts with a question, not a stopwatch. “How quickly can this engine decode a complete recording?” differs from “When does the presenter see a useful partial?” An engine can process a finished file faster than its duration yet wait too long before showing live text. The repository deliberately separates unpaced file decoding from a small paced-delivery experiment; neither measures the complete capture-to-WPF path. (tools\SpeechBench\bench.py:207-230, `worker.decode`; docs\SPEECH-BENCHMARK.md:88-118, “Speed and live-update limits”.)

This chapter analyzes existing evidence, not a newly executed benchmark. The public synthetic receipt records **32 cases, 164.070 seconds of audio, two serial warm passes, and 292 normalized reference words per pass**. Repeating a recording improves timing observations; it does not create another independent speaker or double the accuracy sample. (tools\SpeechBench\results\synthetic-20261008.json:13-22, experiment metadata; tools\SpeechBench\results\synthetic-20261008.json:65-109, `summary`; tools\SpeechBench\bench.py:354-369, `run`.)

The comparison is also not C# versus Python. SAPI, Vosk, and Whisper are different recognition engines with different models and exposed APIs. C# hosts the installed Windows dictation engine; Python hosts Vosk and faster-whisper. Language-host differences are confounded with engine differences, so the experiment cannot isolate language overhead. (tools\SpeechBench\SapiBridge.cs:113-145, `Recognize`; tools\SpeechBench\bench.py:188-255, `worker`; docs\SPEECH-BENCHMARK.md:10-15, engine-versus-language distinction.)

## Architecture of the experiment

The harness gives each engine the same completed PCM fixtures, runs engines serially, and scores their materialized transcripts with one normalization function. The diagram describes that file-only path, not device capture. (tools\SpeechBench\bench.py:106-152, `make_fixtures`; tools\SpeechBench\bench.py:319-369, `run`.)

```mermaid
---
config:
  theme: base
  themeVariables:
    lineColor: "#8b949e"
    textColor: "#e6edf3"
---
flowchart TB
    accTitle: File-only benchmark architecture
    accDescr: Original sentences become identical PCM fixtures, decoded serially and scored with a shared metric.
    subgraph Fixtures["Synthetic fixtures"]
        Corpus["Eight sentences"] --> Voices["Two voices"]
        Voices --> PCM["Clean and noisy PCM"]
    end
    PCM --> Runs["Serial engine runs"]
    Runs --> Sapi["SAPI"]
    Runs --> Vosk["Vosk wrapper"]
    Runs --> Whisper["Whisper files"]
    Sapi --> Score["Shared WER"]
    Vosk --> Score
    Whisper --> Score
    Score --> Receipt["Public receipt"]
    style Corpus fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Voices fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style PCM fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Runs fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Sapi fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Vosk fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Whisper fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Score fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Receipt fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Fixtures fill:#161b22,stroke:#30363d,color:#e6edf3
    linkStyle default stroke:#8b949e
```

### Components and controlled inputs

| Component | Responsibility and experimental consequence |
| --- | --- |
| `corpus.json` | Eight original sentences cover ordinary speech, technical terms, and numbers; coverage is intentionally small. |
| `SapiBridge.Synthesize` | Writes synthesized PCM to a memory stream and then files, never a playback device. |
| `make_fixtures` | Adds identical endpoint padding and a reproducible noisy condition. |
| `worker` | Loads one model, excludes one warm-up decode, then times full passes. |
| `errors` / `aggregate` | Uses common tokenization and corpus-weighted edit counts. |
| `verify_results` | Checks hashes, row coverage, scores, and timing totals in a local run receipt. |

Sources: (tools\SpeechBench\corpus.json:1-10, corpus entries); (tools\SpeechBench\SapiBridge.cs:53-73, `Synthesize`); (tools\SpeechBench\bench.py:125-150, `make_fixtures`); (tools\SpeechBench\bench.py:257-279, `worker`); (tools\SpeechBench\metrics.py:28-62, `errors`, `aggregate`); (tools\SpeechBench\bench.py:282-316, `verify_results`).

Fixture construction matters because endpoint behavior can dominate short utterances. Every case receives 250 ms of leading silence and 1,000 ms of trailing silence. Noise uses a deterministic Gaussian generator and a target 15 dB ratio against active synthesized samples whose absolute amplitude is at least 100. Noise also covers padding. This is a precise laboratory transformation, **not recorded office noise** or a full-file SNR definition. (tools\SpeechBench\bench.py:125-150, `make_fixtures`.)

The two voices are Microsoft David Desktop and Microsoft Zira Desktop. Combined with clean/noisy variants, eight sentences become 32 cases, but the conditions remain correlated. Technical vocabulary examples include Kubernetes and authentication; they do not establish accent, overlap, distant-microphone, or real-meeting coverage. (tools\SpeechBench\corpus.json:2-9, sentence categories; docs\SPEECH-BENCHMARK.md:39-52, fixture description; docs\SPEECH-BENCHMARK.md:220-223, coverage limits.)

## The scoring algorithm

### Normalize before aligning

`normalize` applies Unicode NFKC, lowercases, expands `%`, converts numeric strings through English cardinal words, removes apostrophes, and extracts alphabetic tokens. It does not drop stop words or repair one engine's terminology after seeing its output. Consequently, the benchmark ignores capitalization and punctuation but retains many genuine lexical errors. (tools\SpeechBench\metrics.py:8-25, `NORMALIZATION`, `normalize`.)

Normalization is a measurement policy, not semantic interpretation. `9:30` becomes two cardinals, while `9.30` uses decimal “point” wording; these can describe a similar intended time but score differently. Conversely, “don't” and “dont” become identical tokens. The tests make several such choices executable rather than leaving readers to infer them. (tools\SpeechBench\test_metrics.py:7-17, `test_numbers_are_common`, `test_apostrophes`; docs\SPEECH-BENCHMARK.md:74-77, numeric-format limitation.)

### Levenshtein alignment and ties

For reference prefix length `i` and hypothesis prefix length `j`, the dynamic-programming table stores `(total edits, substitutions, deletions, insertions)`. Equal tokens inherit the diagonal cell. Unequal tokens compare substitution, deletion, and insertion candidates, each costing one additional edit. When totals tie, candidate order prefers substitution, then deletion, then insertion. This stabilizes the reported S/D/I breakdown without making the alignment a semantic explanation of the recognizer's mistake. (tools\SpeechBench\metrics.py:28-55, `errors`.)

The table contains `(reference length + 1) × (hypothesis length + 1)` cells; thus this implementation's time and storage grow with their product. That is acceptable for the short fixture sentences, but a future long-recording evaluator should account for the matrix explicitly rather than assuming a streaming metric. This complexity follows directly from the allocation and nested loops. (tools\SpeechBench\metrics.py:31-48, `errors`.)

Corpus WER is:

```text
WER = sum(substitutions + deletions + insertions)
      / sum(normalized reference words)
```

Do not average sentence percentages: one error in a one-word sentence should not outweigh one error in a much longer sentence simply because each sentence gets equal weight. An empty reference produces undefined WER; an empty hypothesis produces deletions. WER can exceed 100%, so `100 − WER` is not a general “accuracy percentage.” (tools\SpeechBench\metrics.py:49-62, `errors`, `aggregate`; tools\SpeechBench\test_metrics.py:24-37, empty-input and weighted-aggregation tests.)

## Reading the measured result

These are historical results on the synthetic corpus, rounded for readability:

| Engine/configuration | WER | S / D / I | Warm decode median | RTF |
| --- | ---: | ---: | ---: | ---: |
| Installed SAPI en-US dictation | 45.89% | 99 / 2 / 33 | 10.139 s | 0.062 |
| Vosk small-en-us-0.15 | 8.22% | 14 / 0 / 10 | 41.754 s | 0.254 |
| faster-whisper base.en, CPU int8, beam 5 | 4.79% | 9 / 0 / 5 | 25.745 s | 0.157 |

The receipt contains the unrounded numbers and the separate pass totals. Its `transcripts_changed_across_repeats` arrays are empty: the repeats did not change the scored text. SAPI's clean/noisy WER is 17.12%/74.66%; Vosk is 8.22%/8.22%; Whisper is 4.79%/4.79%. Equal percentages in two conditions are observations on these fixtures, not noise-invariance guarantees. (tools\SpeechBench\results\synthetic-20261008.json:65-173, `summary`; docs\SPEECH-BENCHMARK.md:17-27, configuration table.)

RTF divides median decoding seconds by audio seconds. A value below one says this completed corpus decoded faster than its playback duration. It says nothing by itself about the time required to collect an utterance, endpoint it, authenticate a service connection, or render a partial. (tools\SpeechBench\bench.py:355-369, `run`; docs\SPEECH-BENCHMARK.md:97-118, timing exclusions.)

### Which clock did the experiment actually observe?

```mermaid
---
config:
  theme: base
  themeVariables:
    lineColor: "#8b949e"
    textColor: "#e6edf3"
---
flowchart TB
    accTitle: Throughput and live latency have different boundaries
    accDescr: Complete-file decoding measures RTF; paced PCM observes stream-start events; the full application has additional stages.
    File["Complete file"] --> Decode["Timed decode"] --> RTF["File RTF"]
    Pace["Paced PCM"] --> Events["Partial and final"] --> Start["From stream start"]
    Device["Device delivery"] -.-> Queue["Transport queue"]
    Queue -.-> ASR["ASR processing"]
    ASR -.-> UI["WPF rendering"]
    style File fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Decode fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style RTF fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Pace fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Events fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Start fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Device fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style Queue fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style ASR fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style UI fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    linkStyle default stroke:#8b949e
```

Dashed edges identify application stages outside the file experiment. In the separate paced Vosk probe, first partials arrived at **0.976 and 0.960 seconds from stream start**, after 0.9 seconds of PCM had been supplied. Both natural finals arrived near **3.807 seconds from stream start**, not 3.807 seconds after speech ended. Without an annotated last spoken sample, those figures cannot be converted into end-of-speech lag. Two observations also cannot establish a p95 latency distribution. (tools\SpeechBench\results\synthetic-20261008.json:9027-9151, `paced_pcm`; docs\AI-ANSWER-EVALUATION.md:9-37, A1 and latency definitions.)

## Timing boundaries, fairness, and provenance

The Python worker reads audio before timing, measures import/verification/load separately, and consumes Whisper's segment generator inside the decode call. PCM-to-float conversion is outside Whisper timing. SAPI times wave-stream binding through recognition completion, excluding initial file reads. These are exposed-API measurements, not isolated native-kernel timings. (tools\SpeechBench\bench.py:184-255, `worker`; tools\SpeechBench\SapiBridge.cs:75-110, `Decode`; tools\SpeechBench\SapiBridge.cs:123-157, `Recognize`.)

“Cold load” means a new model instance, **not flushed OS caches**. The fixed order is SAPI → Vosk → Whisper. The recorded Windows 11 machine uses a Ryzen 7 PRO 7840U; common affinity limits placement to four logical processors, while requested native thread counts do not make all engines schedule identically. Thermal state, unrelated work, and same-vendor voices remain confounders. No confidence interval follows from two serial passes. (tools\SpeechBench\results\synthetic-20261008.json:4-22, environment/order; docs\SPEECH-BENCHMARK.md:90-95, cold-instance definition; docs\SPEECH-BENCHMARK.md:120-133, resource boundaries.)

There is also a living-document provenance gap: the checked-in receipt's recorded `bench.py` SHA-256 does not match the current harness file; the recorded corpus, metric, SAPI bridge, requirements, wrapper script, and metric-test hashes do match at this documentation inspection. Therefore, treat the receipt as **historical evidence**, not proof that today's entire harness was rerun. The stored hash identifies that distinction; this chapter neither reconstructs the older harness nor attributes the difference to an unverified cause. (tools\SpeechBench\results\synthetic-20261008.json:55-64, `source_sha256`; tools\SpeechBench\bench.py:378-387, receipt construction.)

## Worked pseudocode: auditing without decoding audio

The following is explanatory pseudocode, not a benchmark invocation. It audits the public result's internal arithmetic and repeat coverage; unlike `verify_results`, it cannot establish unchanged raw WAVs from the public receipt alone. (tools\SpeechBench\bench.py:282-316, `verify_results`; tools\SpeechBench\results\synthetic-20261008.json:175-189, public case metadata.)

```python
receipt = read_public_synthetic_receipt()
for name, engine in receipt["engines"].items():
    rows = engine["rows"]
    summary = receipt["summary"][name]
    require_unique_pairs(rows, keys=("id", "repeat"))
    require_complete_case_repeat_grid(rows, receipt["cases"],
                                      receipt["repeats"])
    first_pass = [row for row in rows if row["repeat"] == 0]
    expected_errors = sum(row["substitutions"] + row["deletions"]
                          + row["insertions"] for row in first_pass)
    expected_words = sum(row["reference_words"] for row in first_pass)
    assert summary["wer"] == expected_errors / expected_words
    pass_times = [sum(r["decode_seconds"] for r in rows if r["repeat"] == k)
                  for k in range(receipt["repeats"])]
    assert summary["median_decode_seconds"] == median(pass_times)
# No capture, synthesis, download, or inference is necessary for this audit.
```

The production decision remains conservative: retain Vosk's true streaming `accept` path and its bounded decoder rotation; investigate Whisper only as optional local refinement. A complete-file WER/throughput win does not justify replacing the live path without question-fidelity and latency evidence. (services\transcription\recognizer.py:82-96, `VoskRecognizer.accept`, `finish`; docs\SPEECH-BENCHMARK.md:29-35, recommendation.)

### Failure outcomes belong in the experiment

An unavailable engine is not an infinitely slow engine, a zero-error engine, or a reason to install a new component silently. The SAPI bridge records unavailable status when no en-US recognizer exists; subprocess failures likewise produce an unavailable result rather than a fabricated timing row. A comparison report must retain that outcome and explain which configurations actually ran. (tools\SpeechBench\SapiBridge.cs:113-121, `Recognize`; tools\SpeechBench\bench.py:328-353, `run`.)

Inputs also have a contract: the loader rejects duplicate IDs and empty corpora, and the WAV reader requires uncompressed 16 kHz mono signed 16-bit PCM. Silent resampling would move preprocessing work and change the experiment without an explicit decision. Empty reference strings are technically accepted, but their undefined WER makes them inappropriate for ordinary accuracy ranking. These are methodological limits to document, not inconveniences to bypass. (tools\SpeechBench\bench.py:99-103, `pcm_from_wav`; tools\SpeechBench\bench.py:155-176, `load_cases`; tools\SpeechBench\metrics.py:49-54, `errors`.)

## Exercises

1. Hand-align reference “do not restart” with hypothesis “do restart.” Explain why a modest token error can reverse intent, motivating fidelity checks beyond WER. (tools\SpeechBench\metrics.py:28-55, `errors`; docs\AI-ANSWER-EVALUATION.md:81-83, question fidelity.)
2. Calculate WER for one erroneous one-word case and one correct four-word case. Compare corpus weighting with an unweighted mean; the existing test expects 0.2. (tools\SpeechBench\test_metrics.py:34-37, `test_weighted_aggregation_not_mean_of_case_wer`.)
3. Design, but do not run, a representative follow-up: independent speakers, annotated speech end, randomized engine order, and separate warm/cold strata. State which current limitation each addition addresses. (docs\SPEECH-BENCHMARK.md:127-133, run-order limits; docs\SPEECH-BENCHMARK.md:213-223, corpus limits; docs\AI-ANSWER-EVALUATION.md:30-40, clocks.)

## References

- [Existing comparison and methodology](../../docs/SPEECH-BENCHMARK.md).
- [Public synthetic result](../../tools/SpeechBench/results/synthetic-20261008.json).
- [Harness](../../tools/SpeechBench/bench.py), [metric implementation](../../tools/SpeechBench/metrics.py), and [SAPI bridge](../../tools/SpeechBench/SapiBridge.cs).
- [Metric tests](../../tools/SpeechBench/test_metrics.py) and [harness boundary tests](../../tools/SpeechBench/test_harness.py); the latter explicitly reject escaped output paths, implicit resampling, duplicate case IDs, and empty corpora. (tools\SpeechBench\test_harness.py:8-38, `HarnessTests`.)
- [Planned answer evaluation](../../docs/AI-ANSWER-EVALUATION.md), which must not be read as an executed cloud benchmark.
