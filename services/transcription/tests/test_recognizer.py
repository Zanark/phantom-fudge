import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from recognizer import VoskRecognizer


class DecoderFixture:
    instances = []

    def __init__(self, model, rate):
        self.samples = 0
        self.__class__.instances.append(self)

    def SetWords(self, enabled):
        pass

    def SetPartialWords(self, enabled):
        pass

    def AcceptWaveform(self, pcm):
        self.samples += len(pcm) // 2
        return False

    def PartialResult(self):
        return json.dumps({"partial": "synthetic"})

    def FinalResult(self):
        return json.dumps({
            "text": "synthetic",
            "result": [{"start": 0.05, "end": self.samples / 16000}],
        })


class RecognizerTests(unittest.TestCase):
    def test_continuous_speech_rotates_decoder_and_preserves_absolute_timing(self):
        DecoderFixture.instances = []
        recognizer = VoskRecognizer(object(), DecoderFixture)
        for _ in range(29):
            self.assertFalse(recognizer.accept(b"\0" * 32000).final)
        first = recognizer.accept(b"\0" * 32000)
        self.assertTrue(first.final)
        self.assertEqual(first.end, 30)
        self.assertEqual(len(DecoderFixture.instances), 2)
        self.assertFalse(recognizer.accept(b"\0" * 32000).final)
        tail = recognizer.finish()
        self.assertEqual(tail.start, 30.05)
        self.assertEqual(tail.end, 31)
        self.assertGreaterEqual(tail.start, first.end)

    def test_nonfinite_decoder_timestamps_fail(self):
        class BadTime(DecoderFixture):
            def FinalResult(self):
                return '{"text":"synthetic","result":[{"start":NaN,"end":1.0}]}'
        recognizer = VoskRecognizer(object(), BadTime)
        with self.assertRaises(ValueError):
            recognizer.finish()


if __name__ == "__main__":
    unittest.main()
