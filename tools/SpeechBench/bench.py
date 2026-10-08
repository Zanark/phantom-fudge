"""Opt-in, file-only, local speech benchmark. No microphone, playback or ASR network client."""

import argparse
import ctypes
import hashlib
import importlib.metadata
import importlib.util
import json
import math
import os
from pathlib import Path
import platform
import statistics
import subprocess
import sys
import time
import wave

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
LOCAL = ROOT / ".local" / "speech-bench"
MODEL_ID = "Systran/faster-whisper-base.en"
MODEL_REVISION = "3d3d5dee26484f91867d81cb899cfcf72b96be6c"
MODEL = LOCAL / "models" / "faster-whisper-base.en"
VOSK = ROOT / ".local" / "models" / "vosk-model-small-en-us-0.15"
for key, value in {
    "HF_HUB_DISABLE_TELEMETRY": "1", "DO_NOT_TRACK": "1",
    "HF_HUB_DISABLE_IMPLICIT_TOKEN": "1", "OMP_NUM_THREADS": "2",
    "MKL_NUM_THREADS": "2", "OPENBLAS_NUM_THREADS": "2", "NUMEXPR_NUM_THREADS": "2",
    "HF_HOME": str(LOCAL / "hf-cache"),
}.items():
    os.environ[key] = value


def write(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding="utf-8")


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def sha(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def local_output(path):
    path = Path(path).resolve()
    if not path.is_relative_to(LOCAL.resolve()):
        raise ValueError("Outputs must stay inside ignored .local\\speech-bench.")
    return path


def affinity():
    if os.name == "nt":
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.GetCurrentProcess.restype = ctypes.c_void_p
        kernel.SetProcessAffinityMask.argtypes = [ctypes.c_void_p, ctypes.c_size_t]
        if not kernel.SetProcessAffinityMask(kernel.GetCurrentProcess(), 15):
            raise ctypes.WinError(ctypes.get_last_error())


def sapi(mode, output, input_path=None, repeats=2):
    command = ["powershell.exe", "-NoProfile", "-NonInteractive", "-File", str(HERE / "sapi.ps1"),
               "-Mode", mode, "-OutputJson", str(output), "-Repeats", str(repeats)]
    if input_path:
        command += ["-InputJson", str(input_path)]
    subprocess.run(command, check=True, cwd=ROOT, timeout=600)
    return read(output)


def provision():
    # This is the only inference-tool command permitted to contact the public model host.
    from huggingface_hub import HfApi, snapshot_download
    metadata = HfApi(token=False).model_info(MODEL_ID, revision=MODEL_REVISION)
    revision = metadata.sha
    MODEL.parent.mkdir(parents=True, exist_ok=True)
    snapshot_download(
        MODEL_ID, revision=revision, local_dir=MODEL, token=False, max_workers=2,
        allow_patterns=["config.json", "model.bin", "tokenizer.json", "vocabulary.txt",
                        "preprocessor_config.json", "README.md"],
    )
    files = {str(p.relative_to(MODEL)): {"sha256": sha(p), "bytes": p.stat().st_size}
             for p in sorted(MODEL.iterdir()) if p.is_file()}
    write(LOCAL / "model-download.json", {
        "model_id": MODEL_ID, "revision": revision,
        "source": "https://huggingface.co/" + MODEL_ID,
        "upstream": "https://github.com/openai/whisper",
        "license": (metadata.card_data.to_dict() if metadata.card_data else {}).get("license", "not declared"),
        "hash_provenance": "Observed SHA256 after HTTPS download; not publisher-signed checksums.",
        "files": files, "downloaded_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    })
    print(json.dumps({"model_id": MODEL_ID, "revision": revision, "bytes": sum(f["bytes"] for f in files.values())}))


def pcm_from_wav(path):
    with wave.open(str(path), "rb") as stream:
        if (stream.getnchannels(), stream.getsampwidth(), stream.getframerate(), stream.getcomptype()) != (1, 2, 16000, "NONE"):
            raise ValueError("Replay requires uncompressed 16 kHz mono signed 16-bit PCM WAV; convert locally first.")
        return stream.readframes(stream.getnframes())


def make_fixtures():
    import numpy as np
    folder = LOCAL / "fixtures"
    folder.mkdir(parents=True, exist_ok=True)
    probe = sapi("probe", LOCAL / "sapi-probe.json")
    voices = [v for v in probe["installed_voices"] if v["enabled"] and v["culture"] == "en-US"][:2]
    if not voices:
        raise RuntimeError("No installed en-US synthesis voices. No voice pack will be installed.")
    jobs = []
    for index, voice in enumerate(voices):
        for entry in read(HERE / "corpus.json"):
            jobs.append({**entry, "voice": voice["name"], "voice_id": voice["id"],
                         "id": f"voice-{index + 1}-{entry['id']}",
                         "pcm_path": str(folder / f"voice-{index + 1}-{entry['id']}.pcm")})
    write(LOCAL / "synthesis-jobs.json", jobs)
    result = sapi("synthesize", LOCAL / "synthesis-result.json", LOCAL / "synthesis-jobs.json")
    if result["status"] != "ok":
        raise RuntimeError(result)
    cases = []
    for index, job in enumerate(jobs):
        original = np.frombuffer(Path(job["pcm_path"]).read_bytes(), dtype="<i2").astype(np.float64)
        # Equal endpoint opportunity for every engine; leading 250 ms, trailing 1000 ms.
        signal = np.concatenate([np.zeros(4000), original, np.zeros(16000)])
        active = original[np.abs(original) >= 100]
        speech_rms = float(np.sqrt(np.mean(active ** 2)))
        for condition in ("clean", "noise-15db"):
            samples = signal.copy()
            if condition == "noise-15db":
                noise = np.random.default_rng(20261008 + index).normal(size=len(signal))
                noise *= speech_rms / (10 ** (15 / 20)) / math.sqrt(float(np.mean(noise ** 2)))
                samples += noise
            clipped = int(np.count_nonzero((samples > 32767) | (samples < -32768)))
            pcm = np.rint(np.clip(samples, -32768, 32767)).astype("<i2").tobytes()
            path = folder / f"{job['id']}-{condition}.wav"
            with wave.open(str(path), "wb") as stream:
                stream.setparams((1, 2, 16000, 0, "NONE", "not compressed"))
                stream.writeframes(pcm)
            cases.append({
                "id": f"{job['id']}-{condition}", "path": str(path),
                "reference": job["text"], "category": job["category"], "condition": condition,
                "voice": job["voice"], "voice_id": job["voice_id"],
                "duration_seconds": len(pcm) / 32000, "sha256": sha(path),
                "clipped_samples": clipped, "noise_seed": 20261008 + index,
                "active_speech_rms": speech_rms, "snr_db": 15 if condition != "clean" else None,
            })
    write(LOCAL / "fixtures.json", cases)
    print(f"Created {len(cases)} file-only fixtures; {sum(c['duration_seconds'] for c in cases):.3f} seconds.")


def load_cases(manifest):
    cases = read(manifest)
    ids = set()
    for case in cases:
        if case["id"] in ids:
            raise ValueError("Duplicate corpus case id")
        ids.add(case["id"])
        path = Path(case["path"])
        if not path.is_absolute():
            path = Path(manifest).resolve().parent / path
        case["path"] = str(path.resolve())
        pcm = pcm_from_wav(path)
        case["sha256"] = sha(path)
        case["duration_seconds"] = len(pcm) / 32000
        if not pcm or not isinstance(case["reference"], str):
            raise ValueError("Every case needs nonempty WAV audio and a reference string")
        case.setdefault("voice", "owner-supplied")
        case.setdefault("condition", "owner-supplied")
        case.setdefault("category", "owner-supplied")
    if not cases:
        raise ValueError("The corpus must contain at least one case")
    return cases


def worker(engine, manifest, output, repeats, paced):
    affinity()
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    from metrics import errors
    cases = load_cases(manifest)
    audio = {c["id"]: pcm_from_wav(c["path"]) for c in cases}
    result = {"engine": engine, "status": "ok", "rows": [], "threads_requested": 2, "affinity_mask": 15}
    started = time.perf_counter()
    if engine == "vosk":
        import vosk
        source = ROOT / "services" / "transcription" / "recognizer.py"
        spec = importlib.util.spec_from_file_location("speechbench_production_recognizer", source)
        production = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = production
        spec.loader.exec_module(production)
        result["import_seconds"] = time.perf_counter() - started
        verify = time.perf_counter()
        production.verify_model(VOSK)
        result["verification_seconds"] = time.perf_counter() - verify
        vosk.SetLogLevel(-1)
        load = time.perf_counter()
        model = vosk.Model(model_path=str(VOSK))
        result["cold_load_seconds"] = time.perf_counter() - load
        result["production_source_sha256"] = sha(source)
        result["version"] = importlib.metadata.version("vosk")
        result["model_manifest"] = read(ROOT / "services" / "transcription" / "model.json")

        def decode(pcm, real_time=False):
            start = time.perf_counter()
            recognizer = production.VoskRecognizer(model, vosk.KaldiRecognizer)
            finals, events = [], []
            last_partial = ""
            for offset in range(0, len(pcm), 3200):
                chunk = pcm[offset:offset + 3200]
                fed = (offset + len(chunk)) / 32000
                if real_time:
                    time.sleep(max(0, start + fed - time.perf_counter()))
                recognized = recognizer.accept(chunk)
                if recognized.text and (recognized.final or recognized.text != last_partial):
                    events.append({"kind": "final" if recognized.final else "partial",
                                   "audio_fed_seconds": fed, "wall_seconds": time.perf_counter() - start,
                                   "text": recognized.text})
                    last_partial = "" if recognized.final else recognized.text
                if recognized.final and recognized.text:
                    finals.append(recognized.text)
            final = recognizer.finish()
            if final.text:
                finals.append(final.text)
                events.append({"kind": "finish", "audio_fed_seconds": len(pcm) / 32000,
                               "wall_seconds": time.perf_counter() - start, "text": final.text})
            return " ".join(finals), events
    else:
        import numpy as np
        from faster_whisper import WhisperModel
        result["import_seconds"] = time.perf_counter() - started
        provenance = read(LOCAL / "model-download.json")
        verify = time.perf_counter()
        for name, info in provenance["files"].items():
            if sha(MODEL / name) != info["sha256"]:
                raise ValueError("Whisper model checksum mismatch")
        result["verification_seconds"] = time.perf_counter() - verify
        load = time.perf_counter()
        model = WhisperModel(str(MODEL), device="cpu", compute_type="int8", cpu_threads=2,
                             num_workers=1, local_files_only=True)
        result["cold_load_seconds"] = time.perf_counter() - load
        result["version"] = importlib.metadata.version("faster-whisper")
        result["model_manifest"] = provenance
        result["parameters"] = {"device": "cpu", "compute_type": "int8", "beam_size": 5, "language": "en",
                                "vad_filter": False, "temperature": 0, "condition_on_previous_text": False}
        # The PCM-to-float conversion is preprocessing, outside every timed decode.
        audio = {key: np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768 for key, pcm in audio.items()}

        def decode(samples, real_time=False):
            segments, _ = model.transcribe(samples, language="en", beam_size=5, temperature=0,
                                           vad_filter=False, condition_on_previous_text=False)
            return " ".join(segment.text.strip() for segment in segments), []

    warmup = time.perf_counter()
    decode(audio[cases[0]["id"]])
    result["warmup_seconds"] = time.perf_counter() - warmup
    for repeat in range(repeats):
        for case in cases:
            start = time.perf_counter()
            text, events = decode(audio[case["id"]])
            elapsed = time.perf_counter() - start
            result["rows"].append({"id": case["id"], "repeat": repeat, "text": text,
                                   "decode_seconds": elapsed, "events": events,
                                   **errors(case["reference"], text)})
        print(f"{engine}: repeat {repeat + 1}/{repeats} complete", flush=True)
    if engine == "vosk" and paced:
        # Separate delivery experiment: no device; sleep to emulate 100 ms live PCM arrival.
        selected = [c for c in cases if c["condition"] == "clean"][:2] or cases[:2]
        result["paced_pcm"] = []
        for case in selected:
            start = time.perf_counter()
            text, events = decode(audio[case["id"]], real_time=True)
            result["paced_pcm"].append({"id": case["id"], "text": text, "events": events,
                                        "elapsed_seconds": time.perf_counter() - start,
                                        "duration_seconds": case["duration_seconds"]})
    write(output, result)


def verify_results(folder):
    from metrics import aggregate, errors
    receipt = read(folder / "summary.json")
    cases = read(folder / "cases.json")
    expected = {c["id"]: c for c in cases}
    if sha(folder / "cases.json") != receipt["fixture_manifest_sha256"]:
        raise ValueError("Fixture manifest changed after timing")
    for case in cases:
        if sha(case["path"]) != case["sha256"]:
            raise ValueError("Fixture audio changed after timing")
    for engine in receipt["order"]:
        result = read(folder / f"{engine}.json")
        if result["status"] != "ok":
            continue
        seen = set()
        for row in result["rows"]:
            pair = row["id"], row["repeat"]
            if pair in seen:
                raise ValueError("Duplicate benchmark row")
            seen.add(pair)
            computed = errors(expected[row["id"]]["reference"], row["text"])
            if any(row[key] != value for key, value in computed.items()):
                raise ValueError("Stored error counts do not match transcripts")
            if not math.isfinite(row["decode_seconds"]) or row["decode_seconds"] <= 0:
                raise ValueError("Invalid elapsed decode time")
        if seen != {(case_id, rep) for case_id in expected for rep in range(receipt["repeats"])}:
            raise ValueError("Missing benchmark rows")
        aggregate_score = aggregate([r for r in result["rows"] if r["repeat"] == 0])
        if any(receipt["summary"][engine][key] != value for key, value in aggregate_score.items()):
            raise ValueError("Summary does not match individual cases")
        totals = [sum(r["decode_seconds"] for r in result["rows"] if r["repeat"] == rep)
                  for rep in range(receipt["repeats"])]
        if totals != receipt["summary"][engine]["repeat_decode_seconds"]:
            raise ValueError("Timing totals do not match individual cases")
    print("Verified fixture hashes, exact row coverage, every transcript score, aggregates and timing totals.")


def run(manifest, output, repeats, engines, paced):
    from metrics import NORMALIZATION, aggregate, errors
    output = local_output(output)
    output.mkdir(parents=True, exist_ok=True)
    cases = load_cases(manifest)
    write(output / "cases.json", cases)
    results = {}
    for engine in engines:
        target = output / f"{engine}.json"
        start = time.perf_counter()
        try:
            if engine == "sapi":
                result = sapi("recognize", target, output / "cases.json", repeats)
                for row in result.get("rows", []):
                    case = next(c for c in cases if c["id"] == row["id"])
                    row.update(errors(case["reference"], row["text"]))
            else:
                command = [sys.executable, str(HERE / "bench.py"), "worker", "--engine", engine,
                           "--manifest", str(output / "cases.json"), "--output", str(target),
                           "--repeats", str(repeats)]
                if paced:
                    command.append("--paced")
                subprocess.run(command, check=True, cwd=ROOT, timeout=600)
                result = read(target)
            result["subprocess_total_seconds"] = time.perf_counter() - start
        except (subprocess.SubprocessError, OSError) as exc:
            result = {"status": "unavailable", "error": str(exc)}
        write(target, result)
        results[engine] = result
        print(f"{engine}: {result['status']}", flush=True)
    summary = {}
    for engine, result in results.items():
        if result["status"] != "ok":
            summary[engine] = {"status": result["status"], "error": result.get("error")}
            continue
        rows = result["rows"]
        totals = [sum(r["decode_seconds"] for r in rows if r["repeat"] == rep) for rep in range(repeats)]
        duration = sum(c["duration_seconds"] for c in cases)
        # Accuracy is counted once, not multiplied by warm repeats.
        first = [r for r in rows if r["repeat"] == 0]
        conditions = {}
        for condition in sorted({c["condition"] for c in cases}):
            ids = {c["id"] for c in cases if c["condition"] == condition}
            conditions[condition] = aggregate([r for r in first if r["id"] in ids])
        changed = [c["id"] for c in cases if len({r["text"] for r in rows if r["id"] == c["id"]}) > 1]
        summary[engine] = {
            "status": "ok", **aggregate(first), "by_condition": conditions,
            "cold_load_seconds": result["cold_load_seconds"], "warmup_seconds": result["warmup_seconds"],
            "repeat_decode_seconds": totals, "median_decode_seconds": statistics.median(totals),
            "real_time_factor": statistics.median(totals) / duration,
            "transcripts_changed_across_repeats": changed,
        }
    hardware = subprocess.run(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
         "$c=Get-CimInstance Win32_Processor; $o=Get-CimInstance Win32_OperatingSystem; "
         "@{cpu=$c.Name; cores=$c.NumberOfCores; logical_processors=$c.NumberOfLogicalProcessors; "
         "os=$o.Caption; os_version=$o.Version; memory_bytes=$o.TotalVisibleMemorySize*1024} | ConvertTo-Json -Compress"],
        capture_output=True, text=True, check=True,
    )
    receipt = {
        "created_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "command": sys.argv, "python": sys.version, "platform": platform.platform(),
        "hardware": json.loads(hardware.stdout), "normalization": NORMALIZATION,
        "audio_seconds": sum(c["duration_seconds"] for c in cases), "case_count": len(cases),
        "repeats": repeats, "order": engines, "affinity_mask": 15, "native_threads_requested": 2,
        "packages": {d.metadata["Name"]: d.version for d in importlib.metadata.distributions()},
        "fixture_manifest_sha256": sha(output / "cases.json"),
        "source_sha256": {p.name: sha(p) for p in HERE.iterdir() if p.is_file()},
        "summary": summary,
    }
    write(output / "summary.json", receipt)
    print(json.dumps(summary, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("provision", help="Explicit public model download; no audio is read or sent.")
    sub.add_parser("fixtures", help="Synthesize original corpus into memory and private files, never speakers.")
    verify = sub.add_parser("verify", help="Recompute accuracy/timing summaries and verify unchanged audio.")
    verify.add_argument("--output", type=Path, default=LOCAL / "results")
    for mode in ("run", "worker"):
        p = sub.add_parser(mode)
        p.add_argument("--manifest", type=Path, default=LOCAL / "fixtures.json")
        p.add_argument("--output", type=Path, default=LOCAL / "results")
        p.add_argument("--repeats", type=int, default=2, choices=range(1, 6))
        p.add_argument("--paced", action="store_true")
        if mode == "worker":
            p.add_argument("--engine", required=True, choices=("vosk", "whisper"))
        else:
            p.add_argument("--engines", nargs="+", default=["sapi", "vosk", "whisper"],
                           choices=("sapi", "vosk", "whisper"))
    args = parser.parse_args()
    if args.command == "provision":
        provision()
    elif args.command == "fixtures":
        make_fixtures()
    elif args.command == "verify":
        verify_results(local_output(args.output))
    elif args.command == "worker":
        worker(args.engine, args.manifest, local_output(args.output), args.repeats, args.paced)
    else:
        run(args.manifest, args.output, args.repeats, args.engines, args.paced)


if __name__ == "__main__":
    main()
