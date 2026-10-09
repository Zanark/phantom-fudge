"""The production recognizer uses only the explicitly provisioned, verified model."""

from dataclasses import dataclass
import hashlib
import json
import math
import os
from pathlib import Path
import threading


SAMPLE_RATE = 16000
MODEL_MANIFEST = Path(__file__).with_name("model.json")
MODEL_LOAD_LOCK = threading.Lock()


@dataclass(frozen=True)
class Recognition:
    text: str
    final: bool = False
    start: float = 0.0
    end: float = 0.0


def verify_model(path: Path) -> None:
    manifest = json.loads(MODEL_MANIFEST.read_text(encoding="utf-8"))
    if not path.is_dir() or path.is_symlink():
        raise ValueError("model_unavailable")
    entries = list(path.rglob("*"))
    if any(entry.is_symlink() for entry in entries):
        raise ValueError("model_unavailable")
    actual_files = {entry.relative_to(path).as_posix() for entry in entries if entry.is_file()}
    if actual_files != set(manifest["files"]):
        raise ValueError("model_unavailable")
    for relative, expected in manifest["files"].items():
        target = path.joinpath(*relative.split("/"))
        if not target.is_file() or any(p.is_symlink() for p in (target, *target.parents)):
            raise ValueError("model_unavailable")
        with target.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != expected:
                raise ValueError("model_unavailable")


class VoskFactory:
    def __init__(self, path: Path):
        verify_model(path)
        import vosk

        vosk.SetLogLevel(-1)
        # Supplying a path is essential: language/name discovery may download a model.
        if os.name == "nt" and not str(path).isascii():
            # Vosk's Windows native file APIs do not accept UTF-8 absolute paths.
            # Startup awaits this load before serving requests; chdir uses Windows wide APIs.
            with MODEL_LOAD_LOCK:
                previous = Path.cwd()
                try:
                    os.chdir(path)
                    self._model = vosk.Model(model_path=".")
                finally:
                    os.chdir(previous)
        else:
            self._model = vosk.Model(model_path=str(path))
        self._recognizer_class = vosk.KaldiRecognizer

    def __call__(self):
        return VoskRecognizer(self._model, self._recognizer_class)


class VoskRecognizer:
    def __init__(self, model, recognizer_class):
        self._model = model
        self._recognizer_class = recognizer_class
        self._samples = 0
        self._offset = 0.0
        self._last_end = 0.0
        self._new_decoder()

    def _new_decoder(self):
        self._decoder = self._recognizer_class(self._model, SAMPLE_RATE)
        self._decoder.SetWords(True)
        self._decoder.SetPartialWords(False)

    def _final(self, payload: str) -> Recognition:
        value = json.loads(payload)
        elapsed = self._samples / SAMPLE_RATE
        words = value.get("result", [])
        start = self._offset + words[0]["start"] if words else self._last_end
        end = self._offset + words[-1]["end"] if words else elapsed
        if not math.isfinite(start) or not math.isfinite(end):
            raise ValueError("decoder_error")
        start = min(elapsed, max(self._last_end, start))
        end = min(elapsed, max(start, end))
        self._last_end = end
        return Recognition(value.get("text", ""), True, start, end)

    def accept(self, pcm: bytes) -> Recognition:
        self._samples += len(pcm) // 2
        complete = self._decoder.AcceptWaveform(pcm)
        # Periodic finalization bounds native decoder history during continuous speech.
        if self._samples / SAMPLE_RATE - self._offset >= 30:
            result = self._final(self._decoder.Result() if complete else self._decoder.FinalResult())
            self._offset = self._samples / SAMPLE_RATE
            self._new_decoder()
            return result
        if complete:
            return self._final(self._decoder.Result())
        return Recognition(json.loads(self._decoder.PartialResult()).get("partial", ""))

    def finish(self) -> Recognition:
        return self._final(self._decoder.FinalResult())
