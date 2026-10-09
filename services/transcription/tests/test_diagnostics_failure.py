import io
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from diagnostics import Diagnostics


class DiagnosticFailureTests(unittest.TestCase):
    def test_failed_disk_and_stderr_do_not_escape_into_recognition(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "occupied"
            path.write_text("synthetic", encoding="utf-8")
            logger = Diagnostics(path / "events.jsonl", "hybrid-whisper", "small.en")
            with patch("builtins.print", side_effect=OSError("synthetic disk failure")):
                self.assertFalse(logger.emit("decode", decode_ms=12, audio_ms=100))
            self.assertTrue(logger.failed)
            self.assertFalse(logger.emit("session_stop"))

    def test_closed_stderr_retains_observable_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "occupied"
            path.write_text("synthetic", encoding="utf-8")
            logger = Diagnostics(path / "events.jsonl", "vosk", "vosk-small-en-us-0.15")
            closed = io.StringIO()
            closed.close()
            with patch("sys.stderr", closed):
                self.assertFalse(logger.emit("model_load"))
            self.assertTrue(logger.failed)


if __name__ == "__main__":
    unittest.main()
