"""Verify persisted default setup through the production factory on an ephemeral port."""

import asyncio
import json
import time
from unittest.mock import patch

from quality_regression import LOCAL, write


async def run():
    from aiohttp.test_utils import TestClient, TestServer
    from configuration import load_config, metadata, paths
    from diagnostics import Diagnostics
    from server import FORMAT, RUNTIME, production_app

    config = load_config()
    if config != {"schemaVersion": 1, "engine": "hybrid-whisper", "model": "small.en",
                  "threads": 4, "vocabulary": "technical"}:
        raise AssertionError("Persisted default is not the selected accurate profile")
    folder = LOCAL / "setup-check"
    logger = Diagnostics(folder / "transcription.jsonl", config["engine"], config["model"])
    token = "synthetic_setup_check_" + "x" * 40
    with patch("server.Diagnostics", return_value=logger):
        app = await production_app(token, paths(config)[0])
    client = TestClient(TestServer(app))
    await client.start_server()
    try:
        auth = {"Authorization": "Bearer " + token}
        status = await (await client.get("/v1/runtime", headers=auth)).json()
        if (not status["ready"] or status["engine"] != "hybrid-whisper"
                or status["model"] != "small.en" or status["vocabulary"] != "technical"
                or not status["diagnosticsAvailable"]):
            raise AssertionError("Real production default failed to become ready")
        cases = json.loads((LOCAL / "fixtures.json").read_text(encoding="utf-8"))
        case = next(case for case in cases if case["id"] == "voice-1-requested-two-sum-clean")
        pcm = (LOCAL / case["path"]).read_bytes()
        ws = await client.ws_connect("/v1/transcribe", headers=auth)
        await ws.send_json({"type": "start", **FORMAT})
        if await ws.receive_json() != {"type": "ready", **FORMAT}:
            raise AssertionError("Strict v1 ready changed")
        events = []
        start = time.perf_counter()

        async def collect():
            async with asyncio.timeout(90):
                while True:
                    event = await ws.receive_json()
                    events.append(event)
                    if event["type"] in ("stopped", "error"):
                        return

        receiver = asyncio.create_task(collect())
        for offset in range(0, len(pcm), 3200):
            chunk = pcm[offset:offset + 3200]
            await asyncio.sleep(max(0, start + (offset + len(chunk)) / 32000 - time.perf_counter()))
            await ws.send_bytes(chunk)
        await ws.send_json({"type": "stop"})
        await receiver
        text = " ".join(e["text"] for e in events if e["type"] == "final" and e["text"])
        if events[-1]["type"] != "stopped" or "LeetCode" not in text:
            raise AssertionError("Production default did not deliver the measured synthetic final")
        await ws.close()
        runtime = app[RUNTIME]
        receipt = {"schema_version": 1, "kind": "real-production-factory-synthetic-setup-check",
                   "identity": metadata(config), "production_identity": {
                       key: status[key] for key in metadata(config)},
                   "ready": status["ready"], "diagnostics_available": status["diagnosticsAvailable"],
                   "max_stop_seconds": status["maxStopSeconds"],
                   "ports": "ephemeral only; port 8765 untouched", "source_case": case["id"],
                   "final_text": text, "stopped": events[-1]["type"] == "stopped",
                   "fault_count": runtime.fault_count, "drop_count": runtime.drop_count,
                   "logging": "isolated .local synthetic metadata log"}
        write(folder / "receipt.json", receipt)
        print(json.dumps({"ready": True, "model": config["model"], "stopped": True,
                          "fault_count": runtime.fault_count, "drop_count": runtime.drop_count}))
    finally:
        await client.close()


if __name__ == "__main__":
    asyncio.run(run())
