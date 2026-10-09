"""Two generated in-memory PCM tracks against a real local model, on an ephemeral test server."""

import asyncio
import argparse
import base64
import json
import os
from pathlib import Path
import sys

from aiohttp import ClientSession, web

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "services" / "transcription"))
from recognizer import VoskFactory
from server import FORMAT, create_app


async def run(pcm, native_client=None, accurate=False):
    token = "synthetic_dual_source_probe_" + "x" * 40
    if accurate:
        from accurate import HybridFactory
        from configuration import load_config, paths
        config = load_config()
        vosk_path, whisper_path = paths(config)
        factory = HybridFactory(vosk_path, whisper_path, config["model"], config["threads"], config["vocabulary"])
    else:
        factory = VoskFactory(ROOT / ".local" / "models" / "vosk-model-small-en-us-0.15")
    app = create_app(token, factory)
    runner = web.AppRunner(app, access_log=None)
    await runner.setup()
    site = web.TCPSite(runner, "127.0.0.1", 0)
    await site.start()
    address = runner.addresses[0]
    uri = f"http://127.0.0.1:{address[1]}/v1/transcribe"
    sources = ["computer", "microphone"]
    partials = {source: 0 for source in sources}
    finals = {source: [] for source in sources}
    last_ends = {source: 0.0 for source in sources}
    last_id = 0
    stopped = False
    try:
        if native_client is not None:
            env = os.environ.copy()
            env["PHANTOM_AUDIO_TEST_TOKEN"] = token
            process = await asyncio.create_subprocess_exec(
                "dotnet", str(native_client), "--service-v2", uri.replace("http://", "ws://", 1),
                "--synthetic-tracks-stdin", "--expect-computer", "project meeting",
                "--expect-microphone", "question" if accurate else "one two three four five",
                stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE, env=env,
            )
            payload = json.dumps({source: base64.b64encode(data).decode("ascii")
                                  for source, data in pcm.items()}).encode("utf-8")
            try:
                stdout, stderr = await asyncio.wait_for(process.communicate(payload), 120 if accurate else 60)
            except TimeoutError:
                process.kill()
                await process.wait()
                raise RuntimeError("Synthetic native client exceeded its deadline.") from None
            if process.returncode != 0:
                raise RuntimeError("Synthetic native v2 client failed: " + stderr.decode("utf-8", "replace"))
            print(stdout.decode("utf-8", "replace").strip())
            print(json.dumps({"nativeClientToRealDualModel": "passed",
                              "accurateFinals": accurate, "realEndpointsOpened": 0, "audioFilesWritten": 0}))
            return
        async with ClientSession() as client:
            async with client.ws_connect(uri, headers={"Authorization": "Bearer " + token}) as ws:
                start = {"type": "start", "protocolVersion": 2, **FORMAT, "sources": sources}
                await ws.send_json(start)
                assert await ws.receive_json(timeout=5) == {**start, "type": "ready"}

                async def receive():
                    nonlocal last_id, stopped
                    while True:
                        item = await ws.receive_json(timeout=15)
                        kind = item.get("type")
                        if kind == "stopped":
                            stopped = True
                            return
                        assert kind in ("partial", "final"), "Unexpected protocol outcome."
                        source = item.get("source")
                        assert source in sources, "Missing or wrong source label."
                        if kind == "partial":
                            if item["text"]:
                                partials[source] += 1
                        else:
                            assert item["segmentId"] > last_id
                            last_id = item["segmentId"]
                            assert 0 <= last_ends[source] <= item["startSeconds"] <= item["endSeconds"]
                            last_ends[source] = item["endSeconds"]
                            finals[source].append(item["text"])

                receiver = asyncio.create_task(receive())
                try:
                    length = max(map(len, pcm.values())) + 32000
                    for offset in range(0, length, 3200):
                        for index, source in enumerate(sources, start=1):
                            frame = pcm[source][offset:offset + 3200]
                            frame += b"\0" * (3200 - len(frame))
                            await ws.send_bytes(bytes([index]) + frame)
                        await asyncio.sleep(0.1)
                    await ws.send_json({"type": "stop"})
                    await asyncio.wait_for(receiver, 15)
                finally:
                    if not receiver.done():
                        receiver.cancel()
                        await asyncio.gather(receiver, return_exceptions=True)
        computer = " ".join(finals["computer"]).lower()
        microphone = " ".join(finals["microphone"]).lower()
        assert stopped and all(partials.values()) and all(finals.values())
        assert "project" in computer and "meeting" in computer and "plan" in computer
        assert "question" in microphone and "one two three four five" in microphone
        assert "one two three four five" not in computer and "project plan" not in microphone
        print(json.dumps({
            "dualRealModel": "passed", "partialEvents": partials,
            "finalEvents": {source: len(values) for source, values in finals.items()},
            "sourceKeywordsSeparated": True, "tailDrained": stopped,
            "realEndpointsOpened": 0, "audioFilesWritten": 0,
        }))
    finally:
        await runner.cleanup()


def main():
    arguments = argparse.ArgumentParser()
    arguments.add_argument("--native-client", type=Path)
    arguments.add_argument("--accurate", action="store_true")
    options = arguments.parse_args()
    payload = sys.stdin.buffer.read(4_000_001)
    if len(payload) > 4_000_000:
        raise ValueError("Synthetic input is too large.")
    data = json.loads(payload)
    if set(data) != {"computer", "microphone"}:
        raise ValueError("Two synthetic sources are required.")
    pcm = {source: base64.b64decode(value, validate=True) for source, value in data.items()}
    if any(not value or len(value) % 2 or len(value) > 32000 * 30 for value in pcm.values()):
        raise ValueError("Invalid synthetic PCM length.")
    native_client = options.native_client.resolve(strict=True) if options.native_client else None
    if options.accurate and native_client is None:
        raise ValueError("--accurate currently requires the strict native-client probe.")
    asyncio.run(run(pcm, native_client, options.accurate))


if __name__ == "__main__":
    main()
