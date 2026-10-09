"""Shared native-manager/runtime contract; no implicit downloads or fallback."""

import hashlib
import json
import os
from pathlib import Path

from provision_whisper import model_spec, verify_whisper
from recognizer import verify_model

ROOT = Path(os.environ.get("PHANTOM_TRANSCRIPTION_WORKSPACE", Path(__file__).resolve().parent.parent.parent))
CONFIG = ROOT / ".local" / "transcription-config.json"
DEFAULT = {"schemaVersion": 1, "engine": "hybrid-whisper", "model": "small.en", "threads": 4,
           "vocabulary": "technical"}
PIPELINE_VERSION = 2


def load_config(path=CONFIG):
    if not path.exists():
        config = dict(DEFAULT)
    else:
        config = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(config, dict):
        raise ValueError("configuration")
    if "vocabulary" not in config:
        config["vocabulary"] = DEFAULT["vocabulary"]
    if os.environ.get("PHANTOM_TRANSCRIPTION_ENGINE"):
        config["engine"] = os.environ["PHANTOM_TRANSCRIPTION_ENGINE"]
    if (not isinstance(config, dict) or set(config) != set(DEFAULT)
            or type(config["schemaVersion"]) is not int or config["schemaVersion"] != 1
            or config["engine"] not in ("hybrid-whisper", "vosk")
            or config["model"] not in ("small.en", "base.en")
            or config["vocabulary"] not in ("none", "technical")
            or type(config["threads"]) is not int or not 1 <= config["threads"] <= 8):
        raise ValueError("configuration")
    return config


def metadata(config):
    spec = model_spec(config["model"])
    identity = {"pipelineVersion": PIPELINE_VERSION, "engine": config["engine"],
                "model": config["model"] if config["engine"] == "hybrid-whisper" else "vosk-small-en-us-0.15",
                "modelRevision": spec["revision"] if config["engine"] == "hybrid-whisper" else "0.15",
                "computeType": "int8" if config["engine"] == "hybrid-whisper" else "native",
                "threads": config["threads"], "vocabulary": config["vocabulary"]}
    runtime_hash = hashlib.sha256()
    for name in ("server.py", "recognizer.py", "accurate.py", "configuration.py",
                 "diagnostics.py", "provision_whisper.py", "requirements.txt",
                 "model.json", "whisper-models.json"):
        runtime_hash.update(name.encode("ascii") + b"\0")
        runtime_hash.update(Path(__file__).with_name(name).read_bytes())
    identity["runtimeRevision"] = runtime_hash.hexdigest()
    if config["vocabulary"] == "technical":
        identity["vocabularySha256"] = hashlib.sha256(
            Path(__file__).with_name("technical-vocabulary.json").read_bytes()).hexdigest()
    identity["configFingerprint"] = hashlib.sha256(
        json.dumps(identity, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()
    return identity


def paths(config):
    return (ROOT / ".local" / "models" / "vosk-model-small-en-us-0.15",
            ROOT / ".local" / "models" / model_spec(config["model"])["directory"])


def verify_config(config):
    vosk, whisper = paths(config)
    verify_model(vosk)
    if config["engine"] == "hybrid-whisper":
        verify_whisper(whisper, config["model"])
