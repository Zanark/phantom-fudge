"""Check the cached real model on an ephemeral server using synthetic PCM on stdin."""

import asyncio
import base64
import json
import logging
import os
from pathlib import Path
import sys
import time

from aiohttp.test_utils import TestClient, TestServer

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from recognizer import VoskFactory
from server import FORMAT, RUNTIME, SOURCES, create_app


TOKEN = "synthetic_ephemeral_integration_" + "x" * 40
AUTH = {"Authorization": "Bearer " + TOKEN}
START = {"type": "start", "protocolVersion": 2, **FORMAT, "sources": list(SOURCES)}
KEYWORDS = {
    "computer": {"one", "two", "three", "four", "five", "local", "speech"},
    "microphone": {"six", "seven", "eight", "nine", "ten", "question", "weather"},
}


async def wait_idle(runtime):
    async with asyncio.timeout(3):
        while runtime.active:
            await asyncio.sleep(0.001)


async def main():
    if len(sys.argv) != 2:
        raise ValueError("An explicit cached model directory is required.")
    raw = sys.stdin.buffer.read(2_000_001)
    if len(raw) > 2_000_000:
        raise ValueError("The synthetic fixture is too large.")
    value = json.loads(raw)
    if not isinstance(value, dict) or set(value) != set(SOURCES):
        raise ValueError("Both synthetic source fixtures are required.")
    pcm = {source: base64.b64decode(value[source], validate=True) for source in SOURCES}
    if any(not data or len(data) % 2 or len(data) > 640000 for data in pcm.values()):
        raise ValueError("Each synthetic source must contain at most twenty seconds of PCM16.")

    app = create_app(TOKEN, None)
    runtime = app[RUNTIME]
    async with TestClient(TestServer(app)) as client:
        shared = await runtime.cpu(VoskFactory, Path(sys.argv[1]))
        decoders = []

        def factory():
            decoder = shared()
            decoders.append(decoder)
            return decoder

        runtime.factory = factory
        health = await client.get("/health")
        assert health.status == 200
        assert (await health.json())["modelAvailable"] is True
        async with client.ws_connect("/v1/transcribe", headers=AUTH) as ws:
            await ws.send_json(START)
            assert await ws.receive_json() == {**START, "type": "ready"}
            assert len(decoders) == 2
            assert decoders[0]._model is decoders[1]._model
            assert decoders[0]._decoder is not decoders[1]._decoder
            events = []

            async def receive():
                async with asyncio.timeout(40):
                    while True:
                        event = await ws.receive_json()
                        assert event["type"] != "error", "A safe service error occurred."
                        events.append(event)
                        if event["type"] == "stopped":
                            return
                        assert event["source"] in SOURCES
                        assert set(event) == (
                            {"type", "source", "text"} if event["type"] == "partial"
                            else {"type", "source", "text", "segmentId", "startSeconds", "endSeconds"}
                        )

            async with asyncio.TaskGroup() as tasks:
                tasks.create_task(receive())
                start = time.monotonic()
                for offset in range(0, max(map(len, pcm.values())), 3200):
                    for source in SOURCES:
                        chunk = pcm[source][offset:offset + 3200]
                        if chunk:
                            await ws.send_bytes(bytes([SOURCES[source]]) + chunk)
                    await asyncio.sleep(max(0, start + (offset + 3200) / 32000 - time.monotonic()))
                await ws.send_json({"type": "stop"})
            finals = [event for event in events if event["type"] == "final"]
            assert [event["segmentId"] for event in finals] == list(range(1, len(finals) + 1))
            assert [event["type"] for event in events[-3:]] == ["final", "final", "stopped"]
            assert [event["source"] for event in events[-3:-1]] == list(SOURCES)
            evidence = {}
            for source in SOURCES:
                source_finals = [event for event in finals if event["source"] == source]
                partials = [event for event in events
                            if event["type"] == "partial" and event["source"] == source and event["text"]]
                words = set(" ".join(event["text"] for event in source_finals).split())
                assert KEYWORDS[source] <= words, "Expected synthetic source keywords were missed."
                other = "microphone" if source == "computer" else "computer"
                assert not (KEYWORDS[other] & words), "Synthetic source keywords crossed tracks."
                assert partials, "A source did not emit nonempty streaming partials."
                duration = len(pcm[source]) / 32000
                previous_end = 0
                for event in source_finals:
                    assert previous_end <= event["startSeconds"] <= event["endSeconds"] <= duration
                    previous_end = event["endSeconds"]
                evidence[source] = {
                    "audioSeconds": round(duration, 3),
                    "nonemptyPartials": len(partials),
                    "finalEvents": len(source_finals),
                    "expectedKeywordsMatched": len(KEYWORDS[source]),
                }
        await wait_idle(runtime)

        for version in (2, 1):
            async with client.ws_connect("/v1/transcribe", headers=AUTH) as ws:
                start = ({"type": "start", **FORMAT} if version == 1
                         else {**START, "sources": ["microphone"]})
                await ws.send_json(start)
                assert await ws.receive_json() == {**start, "type": "ready"}
                silence = b"\0" * 3200
                await ws.send_bytes(silence if version == 1 else b"\x02" + silence)
                await ws.send_json({"type": "stop"})
                events = []
                async with asyncio.timeout(5):
                    while True:
                        event = await ws.receive_json()
                        assert event["type"] != "error"
                        events.append(event)
                        if event["type"] == "stopped":
                            break
                label = {"source": "microphone"} if version == 2 else {}
                assert events[-2] == {
                    "type": "final", **label, "text": "", "segmentId": 1,
                    "startSeconds": 0, "endSeconds": 0.1,
                }
                assert events[-1] == {"type": "stopped"}
                assert all(("source" in event) == (version == 2)
                           for event in events if event["type"] in ("partial", "final"))
            await wait_idle(runtime)

        info = await (await client.get("/v1/runtime", headers=AUTH)).json()
        assert info["protocolVersion"] == 1 and info["active"] is False and info["ready"] is True
    print(json.dumps({
        "realDualModelWebSocket": "passed", "inputKind": "generated in-memory synthetic speech",
        "sources": evidence, "sharedModelDistinctDecoders": True, "globalIdsAndSourceTiming": True,
        "canonicalStopTail": True, "microphoneOnlyAndV1Silence": "passed",
        "server": "ephemeral in-process loopback", "audioFilesWritten": 0,
    }))


if __name__ == "__main__":
    logging.disable(logging.CRITICAL)
    with open(os.devnull, "w") as sink:
        os.dup2(sink.fileno(), 2)

    def safe_failure(exception_type, exception, traceback):
        print("Synthetic dual-source validation failed: " + exception_type.__name__, flush=True)

    sys.excepthook = safe_failure
    asyncio.run(main())
