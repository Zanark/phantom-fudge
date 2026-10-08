from pathlib import Path
import unittest
from unittest.mock import MagicMock, patch

import bench


class HarnessTests(unittest.TestCase):
    def test_outputs_cannot_escape_private_folder(self):
        with self.assertRaises(ValueError):
            bench.local_output(bench.ROOT / "public-result.json")
        with self.assertRaises(ValueError):
            bench.local_output(bench.LOCAL / ".." / "not-private.json")
        self.assertEqual(bench.local_output(bench.LOCAL / "result.json"), bench.LOCAL / "result.json")

    def test_no_implicit_resampling(self):
        stream = MagicMock()
        stream.getnchannels.return_value = 1
        stream.getsampwidth.return_value = 2
        stream.getframerate.return_value = 44100
        stream.getcomptype.return_value = "NONE"
        context = MagicMock()
        context.__enter__.return_value = stream
        with patch("bench.wave.open", return_value=context):
            with self.assertRaises(ValueError):
                bench.pcm_from_wav(Path("unused.wav"))

    def test_duplicate_case_ids_rejected(self):
        cases = [{"id": "one", "path": "one.wav", "reference": "one"},
                 {"id": "one", "path": "two.wav", "reference": "two"}]
        with patch("bench.read", return_value=cases), patch("bench.pcm_from_wav", return_value=b"\0\0"), patch("bench.sha", return_value="hash"):
            with self.assertRaisesRegex(ValueError, "Duplicate"):
                bench.load_cases(bench.LOCAL / "corpus.json")

    def test_empty_corpus_rejected(self):
        with patch("bench.read", return_value=[]):
            with self.assertRaises(ValueError):
                bench.load_cases(bench.LOCAL / "corpus.json")


if __name__ == "__main__":
    unittest.main()
