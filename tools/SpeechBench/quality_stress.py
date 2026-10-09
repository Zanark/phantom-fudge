"""Two paced synthetic sources, long speech, exact span ledger and Stop draining."""

import argparse
import asyncio
from dataclasses import replace
import hashlib
import json
from pathlib import Path
import sys
import time

from quality_regression import LOCAL, ROOT, SERVICE, sha, write

sys.path.insert(0, str(SERVICE))


async def run(model, vocabulary, force_rotation=False, decoder_fallback=False, rotation_seconds=15, suffix=""):
    from aiohttp.test_utils import TestClient, TestServer
    from accurate import FinalSpan, HybridFactory, HybridRecognizer, SPAN_SECONDS
    from configuration import DEFAULT, metadata, paths
    from diagnostics import Diagnostics
    from metrics import errors
    from recognizer import Recognition
    from server import FORMAT, RUNTIME, SOURCES, create_app

    cases = json.loads((LOCAL / "fixtures.json").read_text(encoding="utf-8"))
    if force_rotation and rotation_seconds > SPAN_SECONDS:
        raise ValueError("Requested probe exceeds the current production span ceiling; older 30-second experiments are archived.")
    config = {**DEFAULT, "model": model, "vocabulary": vocabulary}
    identity = metadata(config)
    label = f"{model}-{vocabulary}-dual-" + ("rotation" if force_rotation else "long")
    if force_rotation and rotation_seconds != 30:
        label += str(rotation_seconds)
    if decoder_fallback:
        label += "-fallback"
    label += suffix
    folder = LOCAL / label
    audio, references = {}, {}
    for source, prefix in (("computer", "voice-1-"), ("microphone", "voice-2-")):
        chosen = [case for case in cases if case["id"].startswith(prefix)
                  and case["condition"] == "clean"]
        pieces, texts = [], []
        # Computer has no artificial inter-utterance silence: >60 seconds exercises
        # forced 30-second rotation. Mic retains natural endpoints and stops earlier.
        for _ in range(2 if source == "computer" else 1):
            for case in chosen:
                pcm = (LOCAL / case["path"]).read_bytes()
                if source == "computer":
                    pcm = pcm[int(case["speech_start_seconds"] * 16000) * 2:
                              int(case["speech_end_seconds"] * 16000) * 2]
                pieces.append(pcm)
                texts.append(case["reference"])
        audio[source] = b"".join(pieces)
        references[source] = " ".join(texts)
    begin_load = time.perf_counter()
    base = HybridFactory(*paths(config), model, 4, vocabulary)
    decode_quality = []
    if decoder_fallback:
        original = base.finalize.model

        class DecodeFallbackProbe:
            def transcribe(self, audio, **options):
                options["temperature"] = (0.0, 0.2, 0.4)
                segments, info = original.transcribe(audio, **options)

                def tracked():
                    for segment in segments:
                        decode_quality.append({"temperature": segment.temperature,
                                               "compression_ratio": segment.compression_ratio,
                                               "avg_logprob": segment.avg_logprob})
                        yield segment
                return tracked(), info
        base.finalize.model = DecodeFallbackProbe()
    load_seconds = time.perf_counter() - begin_load
    owners, span_rows, running_hash = {}, [], {s: hashlib.sha256() for s in SOURCES}
    source_names = iter(SOURCES)

    class Factory:
        def __call__(self):
            source = next(source_names)
            if force_rotation and source == "computer":
                class RotationProbe:
                    def __init__(self):
                        self.decoder = base.provisional()
                        self.samples = 0

                    def accept(self, pcm):
                        self.samples += len(pcm) // 2
                        result = self.decoder.accept(pcm)
                        # Test-only natural-endpoint suppression. The production
                        # span ceiling still applies; real inference is unchanged.
                        return Recognition(result.text, self.samples >= rotation_seconds * 16000)
                inner = HybridRecognizer(RotationProbe)
            else:
                inner = base()

            class Decoder:
                def accept(self, pcm):
                    result = inner.accept(pcm)
                    if isinstance(result, FinalSpan):
                        if force_rotation and source == "computer":
                            result = replace(result, reason="rotation")
                        owners[id(result)] = source
                    return result

                def finish(self):
                    result = inner.finish()
                    owners[id(result)] = source
                    return result
            return Decoder()

        def finalize(self, span):
            source = owners.pop(id(span))
            start = time.perf_counter()
            result = base.finalize(span)
            running_hash[source].update(span.pcm)
            span_rows.append({"source": source, "start_sample": span.start_sample,
                              "end_sample": span.end_sample, "bytes": len(span.pcm),
                              "decode_seconds": time.perf_counter() - start, "reason": span.reason})
            return result

    logger = Diagnostics(folder / "transcription.jsonl", "hybrid-whisper", model)
    app = create_app("synthetic_dual_stress_" + "x" * 40, Factory(),
                     engine_metadata=identity, diagnostics=logger)
    client = TestClient(TestServer(app))
    await client.start_server()
    try:
        ws = await client.ws_connect("/v1/transcribe", headers={
            "Authorization": "Bearer " + "synthetic_dual_stress_" + "x" * 40})
        await ws.send_json({"type": "start", "protocolVersion": 2, **FORMAT, "sources": list(SOURCES)})
        ready = await ws.receive_json()
        if ready != {"type": "ready", "protocolVersion": 2, **FORMAT, "sources": list(SOURCES)}:
            raise ValueError("Strict ready schema changed")
        events = []
        start = time.perf_counter()

        async def receive():
            async with asyncio.timeout(240):
                while True:
                    event = await ws.receive_json()
                    events.append({**event, "wall_seconds": time.perf_counter() - start})
                    if event["type"] in ("stopped", "error"):
                        return

        receiver = asyncio.create_task(receive())
        # 40 ms packets per source exercise the real 32-frame intake bound.
        longest = max(map(len, audio.values()))
        for offset in range(0, longest, 1280):
            await asyncio.sleep(max(0, start + min(offset + 1280, longest) / 32000 - time.perf_counter()))
            if receiver.done():
                break
            for source, pcm in audio.items():
                chunk = pcm[offset:offset + 1280]
                if chunk:
                    await ws.send_bytes(bytes([SOURCES[source]]) + chunk)
        stop_at = time.perf_counter() - start
        stop_sent = False
        if not receiver.done():
            stop_sent = True
            await ws.send_json({"type": "stop"})
        await receiver
        await ws.close()
        runtime = app[RUNTIME]
        ledger = {}
        for source in SOURCES:
            spans = [span for span in span_rows if span["source"] == source]
            previous = 0
            contiguous = True
            for span in spans:
                contiguous &= span["start_sample"] == previous
                previous = span["end_sample"]
            finals = [e for e in events if e["type"] == "final" and e["source"] == source]
            text = " ".join(e["text"] for e in finals if e["text"])
            ledger[source] = {"input_bytes": len(audio[source]), "span_bytes": sum(s["bytes"] for s in spans),
                              "contiguous": contiguous, "all_samples_owned_once": previous == len(audio[source]) // 2,
                              "input_sha256": hashlib.sha256(audio[source]).hexdigest(),
                              "decoded_pcm_sha256": running_hash[source].hexdigest(),
                              "text": text, "reference": references[source],
                              "score": errors(references[source], text)}
        receipt = {"schema_version": 1, "kind": "synthetic-dual-paced-long-speech",
                   "identity": identity, "load_and_verify_seconds": load_seconds,
                   "packet_ms_per_source": 40, "span_ledger": ledger, "spans": span_rows,
                   "endpoint_policy": (f"test-only suppression of Computer Vosk endpoints until {rotation_seconds} seconds; "
                                       "real Vosk decoding and real Whisper finals" if force_rotation else "production"),
                   "forced_rotation_count": sum(span["reason"] == "rotation" for span in span_rows),
                   "rotation_seconds": rotation_seconds if force_rotation else None,
                   "decoder_fallback_probe": decoder_fallback, "decode_quality": decode_quality,
                   "stop_to_stopped_seconds": (events[-1]["wall_seconds"] - stop_at
                                               if stop_sent and events[-1]["type"] == "stopped" else None),
                   "final_ids": [e["segmentId"] for e in events if e["type"] == "final"],
                   "errors": [e for e in events if e["type"] == "error"],
                   "status": events[-1]["type"], "queue": {
                       "max_bytes_per_source": runtime.max_queue_bytes,
                       "max_frames_per_source": runtime.max_queue_frames,
                       "max_pending_finals_per_source": runtime.max_pending_finals,
                       "fault_count": runtime.fault_count, "drop_count": runtime.drop_count},
                   "limitations": ["Synthetic PCM only; no microphone, speaker or cloud model.",
                                   "Spans are exact non-overlapping PCM ownership, not a zero-WER promise."]}
        write(folder / "receipt.json", receipt)
        print(json.dumps({key: receipt[key] for key in ("status", "queue", "stop_to_stopped_seconds")}))
        if receipt["status"] != "stopped" or receipt["errors"]:
            raise AssertionError("Dual paced stream faulted; finalization was explicitly incomplete")
        for source, data in ledger.items():
            if (not data["contiguous"] or not data["all_samples_owned_once"]
                    or data["input_sha256"] != data["decoded_pcm_sha256"]):
                raise AssertionError(f"{source}: duplicated or lost PCM")
        if receipt["final_ids"] != list(range(1, len(receipt["final_ids"]) + 1)):
            raise AssertionError("Non-global or duplicated final ID")
        if force_rotation and not any(span["reason"] == "rotation" for span in span_rows):
            raise AssertionError("Long-speech fixture did not exercise forced rotation")
    finally:
        await client.close()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", choices=("base.en", "small.en"), default="small.en")
    parser.add_argument("--vocabulary", choices=("none", "technical"), default="none")
    parser.add_argument("--force-rotation", action="store_true")
    parser.add_argument("--decoder-fallback", action="store_true")
    parser.add_argument("--rotation-seconds", type=int, choices=(15, 20, 30), default=15)
    parser.add_argument("--suffix", default="")
    args = parser.parse_args()
    asyncio.run(run(args.model, args.vocabulary, args.force_rotation, args.decoder_fallback,
                    args.rotation_seconds, args.suffix))
