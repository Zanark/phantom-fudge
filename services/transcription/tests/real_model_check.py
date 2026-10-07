"""Consume only generated synthetic PCM on stdin; never persist or print speech."""

import asyncio
import base64
import json
from pathlib import Path
import sys
import time

from aiohttp import ClientSession, ClientWSTimeout

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from server import FORMAT, load_token


async def main():
    pcm = base64.b64decode(sys.stdin.buffer.read(2_000_000).strip(), validate=True)
    if not pcm or len(pcm) % 2 or len(pcm) > 640000:
        raise ValueError("Synthetic input must be at most 20 seconds of PCM16 mono 16000 Hz.")
    token = load_token()
    async with ClientSession(trust_env=False) as client:
        async with client.get("http://127.0.0.1:8765/health") as response:
            assert response.status == 200
            assert (await response.json())["modelAvailable"]
        async with client.ws_connect(
            "http://127.0.0.1:8765/v1/transcribe",
            headers={"Authorization": "Bearer " + token},
            timeout=ClientWSTimeout(ws_receive=15, ws_close=3),
        ) as ws:
            await ws.send_json({"type": "start", **FORMAT})
            assert await ws.receive_json() == {"type": "ready", **FORMAT}
            events = []

            async def receive():
                while True:
                    value = await ws.receive_json()
                    assert value["type"] != "error", value.get("code")
                    events.append(value)
                    if value["type"] == "stopped":
                        break

            receiver = asyncio.create_task(receive())
            start = time.monotonic()
            for offset in range(0, len(pcm), 3200):
                await ws.send_bytes(pcm[offset:offset + 3200])
                await asyncio.sleep(max(0, start + (offset + 3200) / 32000 - time.monotonic()))
            await ws.send_json({"type": "stop"})
            await receiver
            finals = [event for event in events if event["type"] == "final"]
            partials = [event for event in events if event["type"] == "partial" and event["text"]]
            text = " ".join(event["text"] for event in finals)
            keywords = {"one", "two", "three", "four", "five", "local", "speech"}
            assert keywords <= set(text.split()), "The recognizer missed expected synthetic keywords."
            assert partials, "No nonempty streaming partial was observed."
            assert [event["segmentId"] for event in finals] == list(range(1, len(finals) + 1))
            assert events[-2]["type"] == "final" and events[-1]["type"] == "stopped"
            previous_end = 0
            for event in finals:
                assert previous_end <= event["startSeconds"] <= event["endSeconds"] <= len(pcm) / 32000
                previous_end = event["endSeconds"]
            print(json.dumps({
                "realModelWebSocket": "passed", "inputKind": "local generated synthetic speech",
                "audioSeconds": round(len(pcm) / 32000, 3), "nonemptyPartials": len(partials),
                "finalEvents": len(finals), "expectedKeywordsMatched": len(keywords),
                "tailFlushedBeforeStopped": True, "audioFilesWritten": 0,
            }))
        # Silence exercises a second genuinely independent real recognizer session.
        await asyncio.sleep(0.1)
        async with client.ws_connect("http://127.0.0.1:8765/v1/transcribe", headers={"Authorization": "Bearer " + token}) as ws:
            await ws.send_json({"type": "start", **FORMAT})
            assert (await ws.receive_json())["type"] == "ready"
            await ws.send_bytes(b"\0" * 3200)
            await ws.send_json({"type": "stop"})
            final = None
            while True:
                event = await ws.receive_json()
                assert event["type"] != "error", event.get("code")
                if event["type"] == "final":
                    final = event
                if event["type"] == "stopped":
                    break
            assert final is not None and final["text"] == "" and final["segmentId"] == 1
            print('{"realModelSilenceAndSecondSession":"passed"}')


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except Exception as exc:
        # Assertion messages are test-authored; no response text or secret is emitted.
        print("Synthetic validation failed: " + type(exc).__name__)
        sys.exit(1)
