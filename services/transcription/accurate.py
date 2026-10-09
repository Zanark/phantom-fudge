"""Fast Vosk provisional text; disjoint PCM spans for offline Whisper finals."""

from dataclasses import dataclass
import json
import math
from pathlib import Path
import time

from provision_whisper import verify_whisper
from recognizer import Recognition, SAMPLE_RATE, VoskFactory

SPAN_SECONDS = 15
# A single protocol frame may cross the cutoff by at most one second.
MAX_SPAN_BYTES = (SPAN_SECONDS + 1) * SAMPLE_RATE * 2


@dataclass(frozen=True)
class FinalSpan:
    pcm: bytes
    start_sample: int
    end_sample: int
    reason: str

    def __post_init__(self):
        if (not isinstance(self.pcm, bytes)
                or type(self.start_sample) is not int or type(self.end_sample) is not int
                or self.start_sample < 0 or self.end_sample < self.start_sample
                or len(self.pcm) != (self.end_sample - self.start_sample) * 2
                or len(self.pcm) > MAX_SPAN_BYTES):
            raise ValueError("decoder_error")


class HybridRecognizer:
    def __init__(self, provisional_factory):
        self._factory = provisional_factory
        self._decoder = provisional_factory()
        self._pcm = bytearray()
        self._start = 0
        self._finished = False

    def _take(self, reason):
        end = self._start + len(self._pcm) // 2
        result = FinalSpan(bytes(self._pcm), self._start, end, reason)
        self._start = end
        self._pcm.clear()
        return result

    def accept(self, pcm):
        if self._finished or not pcm or len(pcm) % 2 or len(pcm) > 32000:
            raise ValueError("decoder_error")
        if len(self._pcm) + len(pcm) > MAX_SPAN_BYTES:
            raise ValueError("decoder_error")
        self._pcm.extend(pcm)
        result = self._decoder.accept(pcm)
        if not isinstance(result, Recognition):
            raise ValueError("decoder_error")
        rotate = len(self._pcm) >= SPAN_SECONDS * SAMPLE_RATE * 2
        if result.final or rotate:
            span = self._take("rotation" if rotate else "endpoint")
            # No decoder state crosses a committed PCM boundary. No PCM is replayed.
            self._decoder = self._factory()
            return span
        return result

    def finish(self):
        if self._finished:
            raise ValueError("decoder_error")
        self._finished = True
        # Vosk text is provisional only. Whisper consumes exactly the retained tail.
        self._decoder = None
        return self._take("stop")


class WhisperFinalizer:
    def __init__(self, path: Path, name, threads=4, model=None, vocabulary="none"):
        if model is None:
            verify_whisper(path, name)
            from faster_whisper import WhisperModel

            model = WhisperModel(str(path), device="cpu", compute_type="int8",
                                 cpu_threads=threads, num_workers=1, local_files_only=True)
        self.model = model
        self.hotwords = None
        if vocabulary == "technical":
            terms = json.loads(Path(__file__).with_name("technical-vocabulary.json").read_text(encoding="utf-8"))
            self.hotwords = ", ".join(terms["terms"])
        elif vocabulary != "none":
            raise ValueError("configuration")

    def __call__(self, span):
        import numpy as np

        start, end = span.start_sample / SAMPLE_RATE, span.end_sample / SAMPLE_RATE
        if not span.pcm:
            return Recognition("", True, start, end)
        audio = np.frombuffer(span.pcm, dtype="<i2").astype(np.float32) / 32768
        # An exact/near-digital-silence gate prevents model hallucinations without
        # treating a Vosk spelling guess as evidence of whether speech occurred.
        if float(np.max(np.abs(audio))) < 0.001:
            return Recognition("", True, start, end)
        segments, _ = self.model.transcribe(
            audio, language="en", task="transcribe", beam_size=5, temperature=0,
            condition_on_previous_text=False, initial_prompt=None, hotwords=self.hotwords,
            vad_filter=True,
            vad_parameters={"min_silence_duration_ms": 400, "speech_pad_ms": 200},
            word_timestamps=False, no_speech_threshold=0.6, log_prob_threshold=-1.0,
            compression_ratio_threshold=2.4, hallucination_silence_threshold=1.0,
        )
        parts = []
        for segment in segments:
            if (not isinstance(segment.text, str) or not math.isfinite(segment.start)
                    or not math.isfinite(segment.end)):
                raise ValueError("decoder_error")
            parts.append(segment.text.strip())
            if sum(map(len, parts)) + len(parts) > 8192:
                raise ValueError("decoder_error")
        # Times denote the exact owned span, not forced-aligned word boundaries.
        return Recognition(" ".join(part for part in parts if part), True, start, end)


class HybridFactory:
    def __init__(self, vosk_path, whisper_path, model_name, threads=4, vocabulary="none"):
        begin = time.perf_counter()
        self.provisional = VoskFactory(vosk_path)
        self.finalize = WhisperFinalizer(whisper_path, model_name, threads, vocabulary=vocabulary)
        self.load_ms = (time.perf_counter() - begin) * 1000

    def __call__(self):
        return HybridRecognizer(self.provisional)
