"""Explicit public-model provisioning, never imported by runtime inference."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import sys
from urllib.request import Request, urlopen

ROOT = Path(os.environ.get("PHANTOM_TRANSCRIPTION_WORKSPACE",
                           Path(__file__).resolve().parents[2])).resolve()
MODELS_ROOT = Path(os.environ.get("PHANTOM_TRANSCRIPTION_MODELS_ROOT",
                                  ROOT / ".local" / "models")).resolve()
MANIFEST = Path(__file__).with_name("whisper-models.json")


def model_spec(name):
    return json.loads(MANIFEST.read_text(encoding="utf-8"))["models"][name]


def model_directory(name):
    user_model = ROOT / ".local" / "models" / name
    return user_model if user_model.exists() else MODELS_ROOT / name


def valid_file(path, spec):
    if path.is_symlink() or any(parent.is_symlink() for parent in path.parents):
        return False
    if not path.is_file() or path.stat().st_size != spec["bytes"]:
        return False
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest() == spec["sha256"]


def verify_whisper(path, name):
    spec = model_spec(name)
    if not path.is_dir() or path.is_symlink():
        raise ValueError("model_unavailable")
    if {file.name for file in path.iterdir()} != set(spec["files"]):
        raise ValueError("model_unavailable")
    if not all(valid_file(path / file, info) for file, info in spec["files"].items()):
        raise ValueError("model_unavailable")


def provision(name, download=False):
    spec = model_spec(name)
    target = ROOT / ".local" / "models" / spec["directory"]
    cache = ROOT / ".local" / "speech-bench" / "models" / spec["directory"]
    target.mkdir(parents=True, exist_ok=True)
    for file, info in spec["files"].items():
        destination = target / file
        if valid_file(destination, info):
            continue
        if destination.exists():
            raise ValueError("Existing model failed verification; remove it explicitly before repair.")
        partial = target / (file + ".partial")
        created = False
        try:
            if valid_file(cache / file, info):
                with partial.open("xb") as sink:
                    created = True
                    with (cache / file).open("rb") as source:
                        shutil.copyfileobj(source, sink)
            elif download:
                url = f"https://huggingface.co/{spec['repository']}/resolve/{spec['revision']}/{file}"
                # No user token, audio, prompt or local file is sent to the model host.
                with urlopen(Request(url, headers={"User-Agent": "PhantomFudge-model-setup/1"}), timeout=120) as source:
                    with partial.open("xb") as sink:
                        created = True
                        remaining = info["bytes"]
                        while remaining:
                            block = source.read(min(1024 * 1024, remaining))
                            if not block:
                                raise ValueError("Incomplete public model download.")
                            sink.write(block)
                            remaining -= len(block)
                        if source.read(1):
                            raise ValueError("Unexpected public model size.")
            else:
                raise ValueError("Pinned Whisper model absent. Run Initialize-Transcription.ps1 -DownloadModel.")
            if not valid_file(partial, info):
                raise ValueError("Public model failed its pinned integrity check.")
            partial.replace(destination)
        finally:
            if created:
                partial.unlink(missing_ok=True)
    verify_whisper(target, name)
    print(json.dumps({"model": name, "revision": spec["revision"], "verified": True}))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", choices=("base.en", "small.en"), default="small.en")
    parser.add_argument("--download", action="store_true")
    args = parser.parse_args()
    try:
        provision(args.model, args.download)
    except (OSError, ValueError) as failure:
        print(str(failure), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
