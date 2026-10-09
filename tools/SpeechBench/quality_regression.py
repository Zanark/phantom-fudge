"""Synthetic PCM only. Ephemeral loopback service; no audio endpoints or cloud ASR."""

import argparse
import asyncio
import hashlib
import importlib.metadata
import json
import math
import os
from pathlib import Path
import statistics
import sys
import time

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
LOCAL = ROOT / ".local" / "speech-quality"
SERVICE = ROOT / "services" / "transcription"
sys.path.insert(0, str(SERVICE))
for key, value in {
    "HF_HUB_OFFLINE": "1", "TRANSFORMERS_OFFLINE": "1", "HF_HUB_DISABLE_TELEMETRY": "1",
    "HF_HUB_DISABLE_IMPLICIT_TOKEN": "1", "OMP_NUM_THREADS": "4", "OPENBLAS_NUM_THREADS": "1",
}.items():
    os.environ[key] = value


def write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def fixtures():
    import numpy as np
    from bench import sapi
    # bench defaults are for its historical run; this runner explicitly uses four
    # CPU inference threads, no affinity override, and its own output directory.
    os.environ["OMP_NUM_THREADS"] = "4"
    LOCAL.mkdir(parents=True, exist_ok=True)
    probe = sapi("probe", LOCAL / "voices.json")
    voices = [v for v in probe["installed_voices"] if v["enabled"] and v["culture"] == "en-US"][:2]
    if len(voices) < 2:
        raise RuntimeError("This regression requires two installed en-US synthetic voices.")
    corpus = json.loads((HERE / "quality-corpus.json").read_text(encoding="utf-8"))
    jobs = []
    folder = LOCAL / "fixtures"
    folder.mkdir(exist_ok=True)
    for index, voice in enumerate(voices):
        for entry in corpus:
            identifier = f"voice-{index + 1}-{entry['id']}"
            jobs.append({**entry, "id": identifier, "voice": voice["name"],
                         "pcm_path": str(folder / (identifier + ".source.pcm"))})
    write(LOCAL / "synthesis-jobs.json", jobs)
    result = sapi("synthesize", LOCAL / "synthesis.json", LOCAL / "synthesis-jobs.json")
    if result["status"] != "ok":
        raise RuntimeError("Synthetic in-memory TTS failed.")
    cases = []
    for index, job in enumerate(jobs):
        original = np.frombuffer(Path(job["pcm_path"]).read_bytes(), dtype="<i2").astype(np.float64)
        active = np.flatnonzero(np.abs(original) >= 100)
        # End-of-speech is fixed from the clean synthetic signal BEFORE noise is added.
        speech_end = (4000 + int(active[-1]) + 1) / 16000
        speech_start = (4000 + int(active[0])) / 16000
        rms = math.sqrt(float(np.mean(original[active] ** 2)))
        signal = np.concatenate((np.zeros(4000), original, np.zeros(24000)))
        for condition in ("clean", "noise-15db"):
            samples = signal.copy()
            if condition == "noise-15db":
                noise = np.random.default_rng(20261009 + index).normal(size=len(signal))
                noise *= rms / (10 ** (15 / 20)) / math.sqrt(float(np.mean(noise ** 2)))
                samples += noise
            identifier = job["id"] + "-" + condition
            pcm = np.rint(np.clip(samples, -32768, 32767)).astype("<i2").tobytes()
            path = folder / (identifier + ".pcm")
            path.write_bytes(pcm)
            cases.append({"id": identifier, "reference": job["text"], "voice": job["voice"],
                          "category": job["category"], "condition": condition,
                          "path": str(path.relative_to(LOCAL)), "sha256": sha(path),
                          "audio_seconds": len(pcm) / 32000, "speech_start_seconds": speech_start,
                          "speech_end_seconds": speech_end})
    rng = np.random.default_rng(20261009)
    for name, pcm in (
        ("digital-silence", b"\0" * 96000),
        ("noise-only", np.rint(rng.normal(0, 250, 48000)).astype("<i2").tobytes()),
        ("tone-only", np.rint(2000 * np.sin(np.arange(48000) * 2 * np.pi * 440 / 16000)).astype("<i2").tobytes()),
    ):
        path = folder / (name + ".pcm")
        path.write_bytes(pcm)
        cases.append({"id": name, "reference": "", "voice": None, "category": "non-speech",
                      "condition": name, "path": str(path.relative_to(LOCAL)), "sha256": sha(path),
                      "audio_seconds": len(pcm) / 32000, "speech_start_seconds": None,
                      "speech_end_seconds": None})
    write(LOCAL / "fixtures.json", cases)
    print(json.dumps({"synthetic_cases": len(cases), "voices": [v["name"] for v in voices]}))


async def measure(engine, model, paced, repeats, suffix, vocabulary):
    from aiohttp.test_utils import TestClient, TestServer
    from accurate import HybridFactory
    from configuration import DEFAULT, metadata, paths
    from diagnostics import Diagnostics
    from metrics import NORMALIZATION, aggregate, errors
    from recognizer import VoskFactory
    from server import FORMAT, Limits, RUNTIME, create_app

    cases = json.loads((LOCAL / "fixtures.json").read_text(encoding="utf-8"))
    config = {**DEFAULT, "engine": engine, "model": model, "vocabulary": vocabulary}
    label = ((model if engine == "hybrid-whisper" else "vosk") +
             ("-technical" if vocabulary == "technical" else "") +
             ("-paced" if paced else "-batch") + suffix)
    source_hashes = {p.name: sha(p) for p in (
        SERVICE / "accurate.py", SERVICE / "recognizer.py", SERVICE / "server.py",
        SERVICE / "configuration.py", SERVICE / "technical-vocabulary.json",
        HERE / "quality_regression.py", HERE / "quality-corpus.json")}
    identity = metadata(config)
    loaded = time.perf_counter()
    vosk_path, whisper_path = paths(config)
    factory = (HybridFactory(vosk_path, whisper_path, model, 4, vocabulary) if engine == "hybrid-whisper"
               else VoskFactory(vosk_path))
    load_seconds = time.perf_counter() - loaded
    diagnostic = Diagnostics(LOCAL / label / "transcription.jsonl", engine, identity["model"])
    # Batch is explicitly separate from paced latency; it cannot prove queue capacity.
    limits = Limits() if paced else Limits(queue_bytes=64 * 1024 * 1024, queue_frames=8192,
                                           burst_bytes=64 * 1024 * 1024, burst_frames=8192)
    app = create_app("synthetic_quality_token_" + "x" * 40, factory, limits,
                     engine_metadata=identity, diagnostics=diagnostic)
    client = TestClient(TestServer(app))
    await client.start_server()
    rows = []
    try:
        for repeat in range(repeats):
            for case in cases:
                pcm = (LOCAL / case["path"]).read_bytes()
                if hashlib.sha256(pcm).hexdigest() != case["sha256"]:
                    raise ValueError("Synthetic fixture hash mismatch")
                events = []
                begin = time.perf_counter()
                ws = await client.ws_connect("/v1/transcribe", headers={
                    "Authorization": "Bearer " + "synthetic_quality_token_" + "x" * 40})
                await ws.send_json({"type": "start", **FORMAT})
                ready = await ws.receive_json()
                if ready != {"type": "ready", **FORMAT}:
                    raise ValueError("Changed strict protocol")
                ready_seconds = time.perf_counter() - begin
                stream_start = time.perf_counter()

                async def receive():
                    async with asyncio.timeout(150):
                        while True:
                            event = await ws.receive_json()
                            events.append({**event, "wall_seconds": time.perf_counter() - stream_start})
                            if event["type"] in ("stopped", "error"):
                                return

                receiver = asyncio.create_task(receive())
                for offset in range(0, len(pcm), 3200):
                    chunk = pcm[offset:offset + 3200]
                    if paced:
                        await asyncio.sleep(max(0, stream_start + (offset + len(chunk)) / 32000 - time.perf_counter()))
                    await ws.send_bytes(chunk)
                stopped_at = time.perf_counter() - stream_start
                await ws.send_json({"type": "stop"})
                await receiver
                await ws.close()
                async with asyncio.timeout(2):
                    while app[RUNTIME].active:
                        await asyncio.sleep(0.001)
                final_events = [e for e in events if e["type"] == "final" and e["text"]]
                partial_events = [e for e in events if e["type"] == "partial" and e["text"]]
                text = " ".join(e["text"] for e in final_events)
                score = errors(case["reference"], text)
                last_final = final_events[-1]["wall_seconds"] if final_events else None
                row = {key: value for key, value in case.items() if key != "path"}
                row.update(repeat=repeat, text=text, score=score, ready_seconds=ready_seconds,
                           first_partial_seconds=partial_events[0]["wall_seconds"] if partial_events else None,
                           first_final_seconds=final_events[0]["wall_seconds"] if final_events else None,
                           last_final_seconds=last_final,
                           end_of_speech_to_final_seconds=(
                               last_final - case["speech_end_seconds"]
                               if paced and last_final is not None and case["speech_end_seconds"] is not None else None),
                           stop_to_stopped_seconds=events[-1]["wall_seconds"] - stopped_at,
                           elapsed_seconds=events[-1]["wall_seconds"],
                           faults=[e["code"] for e in events if e["type"] == "error"])
                rows.append(row)
                write(LOCAL / label / "progress.json", {"rows": rows})
                print(f"{label} {repeat}:{case['id']} edits={score['errors']} final={row['end_of_speech_to_final_seconds']}", flush=True)
        runtime = app[RUNTIME]
        receipt = {"schema_version": 1, "kind": "synthetic-paced-service" if paced else "synthetic-batch-service",
                   "created_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                   "identity": identity, "normalization": NORMALIZATION,
                   "limitations": ["Synthetic PCM, not the user's microphone or recording.",
                                   "No endpoint opened, no playback, no cloud ASR.",
                                   "No reference prompts, replacements, prior transcripts or adaptation.",
                                   "Vocabulary: " + vocabulary + "; see technical-vocabulary.json for all individual terms.",
                                   "End of speech is last clean synthesized sample >=100/32768, before noise.",
                                   "First model call is cold; subsequent rows are warm. No row omitted.",
                                   "WER retains compound-word spelling differences, including LeetCode versus leet code."],
                   "versions": {name: importlib.metadata.version(name) for name in (
                       "vosk", "faster-whisper", "ctranslate2", "numpy", "onnxruntime", "aiohttp")},
                   "load_and_verify_seconds": load_seconds, "repeats": repeats,
                   "totals": aggregate([r["score"] for r in rows]),
                   "queue": {"max_bytes_per_source": runtime.max_queue_bytes,
                             "max_frames_per_source": runtime.max_queue_frames,
                             "max_pending_finals_per_source": runtime.max_pending_finals,
                             "fault_count": runtime.fault_count, "drop_count": runtime.drop_count},
                   "source_sha256": source_hashes,
                   "rows": rows}
        for key in ("first_partial_seconds", "end_of_speech_to_final_seconds", "stop_to_stopped_seconds"):
            values = sorted(row[key] for row in rows if row[key] is not None)
            receipt[key] = ({"p50": statistics.median(values),
                             "p95": values[max(0, math.ceil(len(values) * 0.95) - 1)], "max": max(values)}
                            if values else None)
        write(LOCAL / label / "receipt.json", receipt)
        print(json.dumps({key: receipt[key] for key in (
            "identity", "totals", "load_and_verify_seconds", "end_of_speech_to_final_seconds", "queue")}))
    finally:
        await client.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=("fixtures", "measure"))
    parser.add_argument("--engine", choices=("vosk", "hybrid-whisper"), default="hybrid-whisper")
    parser.add_argument("--model", choices=("base.en", "small.en"), default="small.en")
    parser.add_argument("--paced", action="store_true")
    parser.add_argument("--repeats", type=int, default=1)
    parser.add_argument("--suffix", default="")
    parser.add_argument("--vocabulary", choices=("none", "technical"), default="none")
    args = parser.parse_args()
    if args.mode == "fixtures":
        fixtures()
    else:
        asyncio.run(measure(args.engine, args.model, args.paced, args.repeats, args.suffix, args.vocabulary))


if __name__ == "__main__":
    main()
