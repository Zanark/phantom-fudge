# Local speech-engine benchmark

## Result: engines, not programming-language quality

Measured on **8 October 2026, 03:40 IST**. All three engines actually ran locally,
one at a time. No microphone, loopback capture, playback, audio upload, cloud ASR,
Docker, device change or production replacement was involved. Laptop mute does not
affect these file/stream tests.

The application already uses **C# WPF/NAudio capture plus Python Vosk recognition**.
The language hosting an engine does not determine its recognition quality. This
compares Windows' installed dictation engine with two different native speech
engines exposed through Python; it does **not** isolate C# versus Python overhead.
The C# helper runs under existing Windows PowerShell/.NET Framework, not WPF's
.NET runtime. No extra Windows framework or voice pack was installed.

| Engine and exact configuration | Clean WER | Noise WER | All WER (S/D/I) | Model-instance load | Warm decode median | RTF |
|---|---:|---:|---:|---:|---:|---:|
| C# System.Speech, Microsoft Speech Recognizer 8.0, en-US | 17.12% | 74.66% | 45.89% (99/2/33) | 0.058 s | 10.139 s | 0.062 |
| Python Vosk 0.3.45, small-en-us-0.15, production wrapper | 8.22% | 8.22% | 8.22% (14/0/10) | 0.367 s | 41.754 s | 0.254 |
| Python faster-whisper 1.2.1, base.en, CPU int8, beam 5 | 4.79% | 4.79% | 4.79% (9/0/5) | 0.345 s | 25.745 s | 0.157 |

**Lower WER and RTF are better.** RTF = decoding wall seconds / audio seconds,
not streaming latency. Every pass decoded the same **164.070 seconds, 32 cases,
292 reference words**. WER counts the first pass once; repeats do not inflate the
sample size. Clean/noisy subsets each contain 16 cases and 146 words.
All three engines returned identical transcripts across their two warm passes.

**Recommendation:** retain Vosk as the low-latency streaming default. This run
supports evaluating an **optional local Whisper refinement/final-transcript mode**:
base.en made fewer token errors and decoded these complete short files faster
than Vosk, but this is not evidence that it produces live partials sooner.
SAPI was fastest but substantially worse on this corpus, especially its noise
condition. Do not replace production recognition based on this small synthetic
test or interpret the results as an inherent C# or Python advantage.

## What was measured

- Original eight-sentence public-safe corpus: ordinary meeting speech, technical
  terms, and numbers. Source: [`corpus.json`](../tools/SpeechBench/corpus.json).
- Microsoft David Desktop and Microsoft Zira Desktop, both en-US, rate zero.
  Microsoft Hazel Desktop (en-GB) was also installed but not used. The installed
  en-GB recognizer was enumerated but not benchmarked; en-US was selected explicitly.
- `SpeechSynthesizer.SetOutputToAudioStream(MemoryStream,
  SpeechAudioFormatInfo(16000, Sixteen, Mono))` produces raw PCM, never speaker
  output. Identical 16 kHz, mono, signed 16-bit WAVs go directly to every engine.
- Each utterance has 250 ms leading and 1,000 ms trailing silence. Both conditions
  share the same padding. Moderate, deterministic Gaussian noise is added across
  the entire noisy file, including padding: **15 dB target SNR relative to active
  synthesized speech samples with absolute amplitude >=100**. This is not a
  full-file SNR definition. Seeds, clipping counts, durations and WAV SHA256s are
  retained per case. It is not a recording of office noise.
- No custom vocabulary, prompt, reference-derived grammar, language-model tuning,
  resampling or denoising. SAPI uses general dictation, with adaptation disabled.
  Whisper specifies English, temperature zero, beam five, no VAD and no previous
  text conditioning. Downloaded FP16 weights are loaded with CPU int8 computation.
- Vosk uses the **existing production `VoskRecognizer.accept/finish` implementation**
  against the existing verified model, with 100 ms PCM chunks. It records changed
  partials and final/finish events. No server or factory process is started.

### Accuracy definition

The common normalization is deterministic: Unicode NFKC, lowercase, `%` becomes
`percent`, digit numerals become English words using num2words 0.5.14, apostrophes
are removed and other punctuation separates words. Case and punctuation alone
are not scored. Acronyms, homophones and stop words are not specially corrected.
Colon times split into cardinals; decimal notation becomes `point`.

Levenshtein WER is **sum(S + D + I) / sum(reference words)**, not an average of
per-sentence percentages. Ties prefer substitution, then deletion, then insertion.
Empty references have undefined WER rather than dividing by zero. An empty
hypothesis incurs deletions; WER can exceed 100%.

Normalization is not semantic understanding: Whisper's `9.30` is treated as
decimal words, not silently reinterpreted as `nine thirty`; `9:30` is treated as
`nine thirty`. Thus the reported WER still includes numeric-format differences.
No normalization was tailored separately to an engine after reading its output.
For example, on one clean David technical sentence, Vosk returned `kabir net`
and `reach rise`, SAPI returned `cougar net` and `cash`, while Whisper returned
`Kubernete`. These are synthetic-text outcomes, not real-meeting claims.

The public-safe full case transcripts, normalized tokens, edit counts, timings,
events and fixture hashes are in
[`synthetic-20261008.json`](../tools/SpeechBench/results/synthetic-20261008.json).
Raw local receipts additionally retain exact commands, all dependency versions,
model hashes, installed recognizer/voice IDs and source hashes.

### Speed and live-update limits

Each engine gets a new process, one model instance, one excluded warm-up utterance,
then two serial full-corpus passes. Load is constructor/grammar time for a **cold
instance**, not a claim of flushed disk/OS caches or a cold machine boot.
Python import and checksum verification are recorded separately. SAPI bridge
compilation/PowerShell startup are outside model-load timing. Warm-up times were
0.235 s SAPI, 1.120 s Vosk, and 0.793 s Whisper.

Per-pass decode times: SAPI **10.202 / 10.075 s**, Vosk **41.765 / 41.743 s**,
Whisper **26.185 / 25.304 s**. File reading is outside timing. WAV input binding,
new per-utterance decoding state where needed, decoding, final flush and transcript
materialization are inside it. Whisper's PCM-to-float conversion is precomputed;
no lazy segment generator is left unconsumed. Vosk event collection adds small
wrapper overhead. This is exposed-API throughput, not pure native-kernel timing.
No capture, resampling, C#-Python transport, UI rendering, network, or service startup
latency is included.

In a **separate paced PCM experiment**, the first two clean David cases were
delivered in real time in 100 ms chunks (sleep, not an audio device). First Vosk
partials appeared after **0.976 / 0.960 wall seconds**, both after 0.9 seconds of
audio had been supplied. Natural final events appeared after **3.807 / 3.807 s**,
at 3.8 seconds of supplied audio. Total file durations were 4.495 / 4.745 s.
These are two observations, **not a latency distribution or service SLA**.

Unpaced Vosk events also record audio-fed position and wall time, which must not
be mistaken for live latency. Whisper here receives each complete utterance and
returns completed segments; it is **not token streaming**. A live implementation
would need chunk collection plus inference and a policy for replacing earlier
text. Its file RTF cannot be substituted for time-to-first-partial. No comparable
SAPI/Whisper streaming-latency measurement was made.

### Machine and resource boundaries

Windows 11 Enterprise 10.0.26200; AMD Ryzen 7 PRO 7840U, 8 physical / 16 logical
processors, 25,410,760,704 visible memory bytes; Python 3.13.14. Windows
System.Speech assembly 4.0.0.0 / CLR 4.0.30319.42000; recognizer
`MS-1033-80-DESK`. CTranslate2 4.6.0. No GPU was used.

Engine processes share affinity mask 15 (four logical processors). Native thread
environment limits request two threads; Whisper explicitly uses two CPU threads
and one worker. SAPI/Vosk do not expose an equivalent comprehensive thread-count
control, so affinity is the common CPU-placement cap, not a guarantee of identical
thread scheduling. Engine runs never overlap. This shared laptop was not isolated
from unrelated work or thermal/power effects, and run order was fixed
SAPI -> Vosk -> Whisper, not randomized. No confidence intervals are claimed.

## Reproduce safely

From the repository root, using the existing Windows PowerShell installation:

```powershell
python -m venv .local\speech-bench\.venv
.\.local\speech-bench\.venv\Scripts\python.exe -m pip install -r tools\SpeechBench\requirements.txt
.\.local\speech-bench\.venv\Scripts\python.exe -m unittest discover -s tools\SpeechBench -p "test_*.py" -v
.\.local\speech-bench\.venv\Scripts\python.exe tools\SpeechBench\bench.py fixtures
.\.local\speech-bench\.venv\Scripts\python.exe tools\SpeechBench\bench.py provision
.\.local\speech-bench\.venv\Scripts\python.exe tools\SpeechBench\bench.py run --repeats 2 --paced --output .local\speech-bench\results
.\.local\speech-bench\.venv\Scripts\python.exe tools\SpeechBench\bench.py verify --output .local\speech-bench\results
```

Only dependency installation and **explicit `provision`** need public downloads.
Provision reads no audio. HF telemetry and implicit tokens are disabled. Inference
sets offline mode and loads local paths with `local_files_only=True`; no cloud
ASR endpoint is used. TLS verification is not disabled. This is not a claim of
OS-firewall-enforced isolation or packet-capture verification.

Requirements are isolated and direct dependencies pinned; the observed complete
resolution is also recorded in the result JSON. CTranslate2 4.6.0 requires
`pkg_resources`, so setuptools 75.8.0 is explicitly pinned in this benchmark venv.
Production dependencies and the original Vosk model are untouched. Missing SAPI
recognizers are recorded as unavailable; no voice packs are installed.

The verified run is under `.local\speech-bench\results-20261008-verified`.
An earlier development attempt, retained separately, failed before SAPI scoring
and before Whisper inference; it is **not** the reported comparison.
Fixtures/models/venv/raw results stay in ignored `.local\speech-bench`.

### Public model provenance

- Vosk: `vosk-model-small-en-us-0.15`, Apache-2.0;
  [official catalogue](https://alphacephei.com/vosk/models).
  Existing production manifest verifies every model file before use.
  Its recorded official ZIP SHA256 is
  `30f26242c4eb449f948e42cb302dd7a686cb29a3423a8367f99ff41780942498`.
- Whisper: [Systran/faster-whisper-base.en](https://huggingface.co/Systran/faster-whisper-base.en),
  pinned revision `3d3d5dee26484f91867d81cb899cfcf72b96be6c`, MIT model-card
  license; publisher's CTranslate2 conversion of `openai/whisper-base.en`.
  Five downloaded files total **147,770,833 bytes**; `model.bin` is 145,216,508 bytes.
  Observed model.bin SHA256:
  `2a166925539a16005f14ff328359f9b9adb9dc4fb631bb3b227526862e93e2ef`.
  Full per-file hashes are retained in `.local\speech-bench\model-download.json`
  and the public result.

These are **observed HTTPS-download/file SHA256 values, not publisher-signed
checksums**. Model hashes are checked before inference. SAPI is an installed
Windows component with recorded engine/assembly IDs, not redistributed weights.

## Replay owner-supplied, consented audio

No real meeting or private note was used for this run. Later, explicitly prepare a
private manifest under `.local\speech-bench\owner-corpus.json`:

```json
[
  {
    "id": "consented-001",
    "path": "owner-audio\\consented-001.wav",
    "reference": "An independently checked transcript goes here.",
    "condition": "quiet-room",
    "category": "meeting"
  }
]
```

Paths are resolved relative to the manifest, or may be absolute. Only supplied
files are read: the harness does not discover recordings. Inputs must already be
16 kHz mono signed 16-bit uncompressed PCM WAV; incompatible formats are rejected
rather than silently resampled. Use nonempty references for meaningful WER.

```powershell
.\.local\speech-bench\.venv\Scripts\python.exe tools\SpeechBench\bench.py run --manifest .local\speech-bench\owner-corpus.json --output .local\speech-bench\owner-results --repeats 2
.\.local\speech-bench\.venv\Scripts\python.exe tools\SpeechBench\bench.py verify --output .local\speech-bench\owner-results
```

Outputs are constrained to the ignored benchmark folder. They contain transcripts
and paths and must stay private; nothing automatically exports or publishes them.
Protect reference transcripts from model prompting. Include varied real speakers,
accents, accents outside the synthesis voices, room noise, technical vocabulary,
and genuine overlap before making a production-quality claim. Use representative
utterance lengths and a separate live-transport test for actual latency.

Synthetic TTS cannot establish real-meeting, accent, distant-microphone or overlap
accuracy. Two same-vendor voices are highly correlated and may favor SAPI. Eight
sentences are a smoke benchmark, not a statistically representative accuracy
evaluation, and WER is not a percentage of meetings correctly understood.
