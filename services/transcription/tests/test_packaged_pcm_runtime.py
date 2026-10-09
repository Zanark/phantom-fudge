"""Opt-in real bundled-model smoke test using generated PCM, never an audio device."""

import json
import os
from pathlib import Path
import shutil
import runpy
import subprocess
import unittest
import uuid

ROOT = Path(__file__).resolve().parents[3]

PROGRAM = runpy.run_path(str(ROOT / "tools" / "Installer" / "runtime_audit.py"))["DLL_PROBE"] + r"""
import faulthandler, importlib.metadata as metadata, importlib.util, json, os, pathlib, socket, sys
from unittest.mock import patch
import ctranslate2, faster_whisper, numpy as np, onnxruntime, vosk
from accurate import FinalSpan, HybridFactory
from configuration import DEFAULT, paths

payload = pathlib.Path(os.environ["PHANTOM_PACKAGED_RUNTIME"])
assert pathlib.Path(sys.executable).resolve() == payload / "runtime" / "python" / "python.exe"
assert sys.flags.isolated and sys.dont_write_bytecode
for module in (ctranslate2, faster_whisper, np, onnxruntime, vosk):
    assert pathlib.Path(module.__file__).resolve().is_relative_to(payload)
assert importlib.util.find_spec("av") is None
assert metadata.version("faster-whisper") == "1.2.1+phantomfudge1"
assert metadata.version("ctranslate2") == "4.6.0+phantomfudge2"

def reject_socket(*args, **kwargs):
    raise AssertionError("Offline synthetic test attempted a network socket.")

socket.socket = reject_socket
socket.create_connection = reject_socket
faulthandler.dump_traceback_later(45, repeat=True)
print("phase=model-load", flush=True)
factory = HybridFactory(*paths(DEFAULT), DEFAULT["model"], DEFAULT["threads"],
                        vocabulary=DEFAULT["vocabulary"])
print("phase=model-loaded", flush=True)
samples = np.arange(16000, dtype=np.float32) / np.float32(16000)
audio = (0.08 * np.sin(2 * np.pi * 173 * samples)
         + 0.03 * np.sin(2 * np.pi * 431 * samples)).astype(np.float32)
pcm = (audio * 32767).astype("<i2").tobytes()

decoder = factory()
print("phase=vosk-pcm", flush=True)
provisional = decoder.accept(pcm)
spans = [provisional] if isinstance(provisional, FinalSpan) else []
spans.append(decoder.finish())
assert b"".join(span.pcm for span in spans) == pcm
vad_calls = []
original_run = onnxruntime.InferenceSession.run

def counted_run(instance, *args, **kwargs):
    vad_calls.append(1)
    return original_run(instance, *args, **kwargs)

with patch.object(onnxruntime.InferenceSession, "run", counted_run):
    print("phase=production-final", flush=True)
    finals = [factory.finalize(span) for span in spans]
print("phase=production-final-complete", flush=True)
assert all(result.final and isinstance(result.text, str) for result in finals)
assert vad_calls, "Production finalizer must exercise real Silero/ONNX VAD."

# The production VAD may correctly reject generated tones as non-speech.
# Bypass VAD only for this second probe, forcing the unchanged real encoder/decoder path.
model = factory.finalize.model
with patch.object(model, "encode", wraps=model.encode) as encode, \
        patch.object(model, "generate_with_fallback", wraps=model.generate_with_fallback) as generate:
    print("phase=forced-native-decode", flush=True)
    segments, info = model.transcribe(
        audio, language="en", task="transcribe", beam_size=1, temperature=0,
        condition_on_previous_text=False, vad_filter=False, max_new_tokens=2,
        word_timestamps=False)
    list(segments)
    assert encode.call_count >= 1, "The real CTranslate2 encoder was not exercised."
    assert generate.call_count >= 1, "The real CTranslate2 decoder was not exercised."
    encoder_calls, decoder_calls = encode.call_count, generate.call_count
print("phase=forced-native-decode-complete", flush=True)

try:
    model.transcribe("unsupported-not-opened.wav")
except RuntimeError as error:
    assert "PCM" in str(error)
else:
    raise AssertionError("Encoded-file input must fail explicitly without PyAV.")

faulthandler.cancel_dump_traceback_later()
print(json.dumps({
    "python": sys.version.split()[0], "voskPcm": True, "productionFinal": True,
    "vadInvocations": len(vad_calls), "encoderInvocations": encoder_calls,
    "decoderInvocations": decoder_calls, "syntheticPcmSeconds": 1,
    "encodedFileRejected": True, "captureOpened": False, "serviceStarted": False,
    "pythonNetworkSocketsOpened": 0, "recognitionQualityAssessed": False,
    "dependencyGraph": "modified installed PCM-only distributions",
    "dllLoadProbe": _dll_evidence()
}))
"""


@unittest.skipUnless(os.environ.get("PHANTOM_PACKAGED_RUNTIME"),
                     "Set PHANTOM_PACKAGED_RUNTIME explicitly to opt into real synthetic PCM inference.")
class PackagedPcmTests(unittest.TestCase):
    def test_real_hybrid_final_and_forced_native_whisper_decode_without_capture(self):
        payload = Path(os.environ["PHANTOM_PACKAGED_RUNTIME"]).resolve()
        python = payload / "runtime" / "python" / "python.exe"
        self.assertTrue(python.is_file())
        workspace = ROOT / ".local" / "packaged-pcm-tests" / uuid.uuid4().hex
        workspace.mkdir(parents=True)
        system = Path(os.environ["SystemRoot"])
        environment = {
            "SystemRoot": str(system), "WINDIR": str(system),
            "PATH": str(python.parent) + os.pathsep + str(system / "System32"),
            "TEMP": str(workspace), "TMP": str(workspace),
            "USERPROFILE": str(workspace / "profile"),
            "LOCALAPPDATA": str(workspace / "local-app-data"),
            "APPDATA": str(workspace / "app-data"),
            "PHANTOM_PACKAGED_RUNTIME": str(payload),
            "PHANTOM_TRANSCRIPTION_WORKSPACE": str(workspace),
            "PHANTOM_TRANSCRIPTION_MODELS_ROOT": str(payload / "models"),
            "HF_HOME": str(workspace / "huggingface"),
            "HF_HUB_OFFLINE": "1", "TRANSFORMERS_OFFLINE": "1",
            "HF_HUB_DISABLE_IMPLICIT_TOKEN": "1", "HF_HUB_DISABLE_TELEMETRY": "1",
            "OPENBLAS_NUM_THREADS": "1", "OMP_NUM_THREADS": "1",
        }
        try:
            try:
                result = subprocess.run([str(python), "-I", "-B", "-X", "utf8", "-c", PROGRAM],
                                        cwd=workspace, env=environment, stdin=subprocess.DEVNULL,
                                        capture_output=True, text=True, timeout=120)
            except subprocess.TimeoutExpired as error:
                self.fail(f"Synthetic child timed out; phases={error.stdout!r}; trace={error.stderr!r}")
            self.assertEqual(result.returncode, 0, result.stdout[-3000:] + result.stderr[-12000:])
            evidence = json.loads(result.stdout.strip().splitlines()[-1])
            self.assertTrue(evidence["productionFinal"])
            self.assertGreater(evidence["encoderInvocations"], 0)
            self.assertGreater(evidence["decoderInvocations"], 0)
            print(json.dumps(evidence))
        finally:
            shutil.rmtree(workspace)


if __name__ == "__main__":
    unittest.main()
