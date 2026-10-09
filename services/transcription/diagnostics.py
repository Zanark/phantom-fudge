"""Bounded metadata-only JSONL. No arbitrary messages, content or exception strings."""

from datetime import datetime, timezone
import json
import math
import os
from pathlib import Path
import sys
import threading


class Diagnostics:
    MAX_BYTES = 1024 * 1024
    BACKUPS = 3
    EVENTS = frozenset(("model_load", "session_start", "session_stop", "decode",
                        "finalize", "fault", "disconnect"))
    SOURCES = frozenset(("computer", "microphone", "service"))
    COUNTERS = ("decode_ms", "audio_ms", "queue_bytes", "queue_frames", "pending_finals",
                "fault_count", "drop_count")
    FAULTS = frozenset(("none", "auth", "origin", "busy", "bad_format", "bad_frame", "backlog",
                       "rate_limit", "timeout", "decoder_error", "model_unavailable",
                       "session_limit", "server_stopping"))

    def __init__(self, path, engine, model, max_bytes=None):
        if (engine not in ("hybrid-whisper", "vosk") or
                model not in ("small.en", "base.en", "vosk-small-en-us-0.15")):
            raise ValueError("diagnostic_schema")
        self.path = Path(path)
        self.engine = engine
        self.model = model
        self.max_bytes = self.MAX_BYTES if max_bytes is None else max_bytes
        self.failed = False
        self._lock = threading.Lock()

    def _failure(self):
        self.failed = True
        try:
            print("Local transcription diagnostics I/O failed; metadata logging is unavailable.",
                  file=sys.stderr, flush=True)
        except (OSError, ValueError):
            # The manager's stderr file can fail on the same full disk. Runtime metadata
            # still exposes diagnosticsAvailable:false; recognition must remain usable.
            pass

    def emit(self, event, source="service", fault="none", **counts):
        if event not in self.EVENTS or source not in self.SOURCES or fault not in self.FAULTS:
            raise ValueError("diagnostic_schema")
        if set(counts) - set(self.COUNTERS):
            raise ValueError("diagnostic_schema")
        record = {"schema_version": 1, "time_utc": datetime.now(timezone.utc).isoformat(),
                  "event": event, "source": source, "engine": self.engine,
                  "model": self.model, "fault": fault}
        for key in self.COUNTERS:
            value = counts.get(key, 0)
            if type(value) not in (int, float) or not math.isfinite(value) or value < 0:
                raise ValueError("diagnostic_schema")
            record[key] = round(value, 3)
        line = (json.dumps(record, separators=(",", ":")) + "\n").encode("utf-8")
        with self._lock:
            if self.failed:
                return False
            try:
                self.path.parent.mkdir(parents=True, exist_ok=True)
                if self.path.is_symlink() or any(parent.is_symlink() for parent in self.path.parents):
                    raise OSError()
                if self.path.exists() and self.path.stat().st_size + len(line) > self.max_bytes:
                    for index in range(self.BACKUPS, 0, -1):
                        previous = self.path if index == 1 else Path(str(self.path) + f".{index - 1}")
                        target = Path(str(self.path) + f".{index}")
                        if previous.exists():
                            os.replace(previous, target)
                with self.path.open("ab") as stream:
                    stream.write(line)
                return True
            except OSError:
                self._failure()
                return False
