import asyncio
from dataclasses import replace
import json
from pathlib import Path
import sys
import threading
import time
import unittest
from unittest.mock import patch

from aiohttp import WSMsgType
from aiohttp.test_utils import TestClient, TestServer

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from recognizer import Recognition, verify_model
from server import FORMAT, Limits, RUNTIME, create_app, load_token


TOKEN = "synthetic_test_token_not_a_real_secret_" + "a" * 32
HEADERS = {"Authorization": "Bearer " + TOKEN}


class FakeRecognizer:
    def __init__(self):
        self.samples = 0
        self.thread_ids = []
        self.calls = 0

    def accept(self, pcm):
        self.thread_ids.append(threading.get_ident())
        self.samples += len(pcm) // 2
        self.calls += 1
        if self.calls == 2:
            return Recognition("synthetic final", True, 0, self.samples / 16000)
        return Recognition("synthetic partial")

    def finish(self):
        end = self.samples / 16000
        return Recognition("synthetic tail", True, end, end)


class ProtocolTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.instances = []

        def factory():
            decoder = FakeRecognizer()
            self.instances.append(decoder)
            return decoder

        self.app = create_app(TOKEN, factory)
        self.client = TestClient(TestServer(self.app))
        await self.client.start_server()

    async def asyncTearDown(self):
        await self.client.close()

    async def start(self):
        ws = await self.client.ws_connect("/v1/transcribe", headers=HEADERS)
        await ws.send_json({"type": "start", **FORMAT})
        self.assertEqual(await ws.receive_json(), {"type": "ready", **FORMAT})
        return ws

    async def test_ready_partial_final_stop_timing_and_cpu_thread(self):
        ws = await self.start()
        await ws.send_bytes(b"\0" * 3200)
        self.assertEqual(await ws.receive_json(), {"type": "partial", "text": "synthetic partial"})
        await ws.send_bytes(b"\0" * 3200)
        first = await ws.receive_json()
        self.assertEqual(first["type"], "final")
        self.assertEqual(first["segmentId"], 1)
        self.assertEqual(first["endSeconds"], 0.2)
        self.assertEqual(set(first), {"type", "text", "segmentId", "startSeconds", "endSeconds"})
        await ws.send_json({"type": "stop"})
        tail = await ws.receive_json()
        self.assertEqual(tail["segmentId"], 2)
        self.assertEqual(tail["startSeconds"], 0.2)
        self.assertEqual(await ws.receive_json(), {"type": "stopped"})
        self.assertTrue(all(t != threading.get_ident() for t in self.instances[0].thread_ids))

    async def test_auth_failure_http_json_no_secret_echo(self):
        for headers in ({}, {"Authorization": "Bearer incorrect"}, {"Authorization": "Basic " + TOKEN}):
            async with self.client.get("/v1/transcribe", headers=headers) as response:
                self.assertEqual(response.status, 401)
                body = await response.json()
                self.assertEqual(body["code"], "auth")
                self.assertNotIn(TOKEN, json.dumps(body))
        self.assertFalse(self.instances)

    async def test_constant_time_token_comparison(self):
        import server
        with patch.object(server.hmac, "compare_digest", wraps=server.hmac.compare_digest) as compare:
            await self.client.get("/v1/transcribe", headers={"Authorization": "Bearer wrong"})
            compare.assert_called_once()

    async def test_origin_rejected_even_with_valid_token(self):
        for origin in ("https://example.invalid", "null", "file://", " "):
            response = await self.client.get("/v1/transcribe", headers={**HEADERS, "Origin": origin})
            # HTTP parsers strip optional header whitespace, making a whitespace-only Origin empty.
            if origin != " ":
                self.assertEqual(response.status, 403)
                self.assertEqual((await response.json())["code"], "origin")
        self.assertFalse(self.instances)

    async def test_query_token_never_supported(self):
        response = await self.client.get("/v1/transcribe?token=synthetic", headers=HEADERS)
        self.assertEqual(response.status, 400)
        self.assertEqual((await response.json())["code"], "bad_frame")

    async def test_duplicate_authorization_is_rejected(self):
        response = await self.client.get("/v1/transcribe", headers=[
            ("Authorization", "Bearer " + TOKEN), ("Authorization", "Bearer " + TOKEN),
        ])
        self.assertEqual(response.status, 401)
        self.assertEqual((await response.json())["code"], "auth")
        self.assertFalse(self.instances)

    async def test_missing_model_is_not_healthy(self):
        self.app[RUNTIME].factory = None
        response = await self.client.get("/health")
        self.assertEqual(response.status, 503)
        self.assertEqual(await response.json(), {"status": "unavailable", "modelAvailable": False})
        response = await self.client.get("/v1/transcribe", headers=HEADERS)
        self.assertEqual(response.status, 503)
        self.assertEqual((await response.json())["code"], "model_unavailable")

    async def test_busy_before_start_and_separate_sessions(self):
        first = await self.client.ws_connect("/v1/transcribe", headers=HEADERS)
        response = await self.client.get("/v1/transcribe", headers=HEADERS)
        self.assertEqual(response.status, 409)
        self.assertEqual((await response.json())["code"], "busy")
        await first.send_json({"type": "start", **FORMAT})
        await first.receive_json()
        await first.send_json({"type": "stop"})
        await first.receive_json()
        await first.receive_json()
        await first.close()
        await asyncio.sleep(0.02)
        second = await self.start()
        self.assertEqual(len(self.instances), 2)
        self.assertIsNot(self.instances[0], self.instances[1])
        await second.close()

    async def test_reject_format_before_recognizer_created(self):
        for field, value in (("sampleRate", 48000), ("sampleRate", 16000.0),
                             ("channels", 2), ("channels", True), ("encoding", "float32")):
            ws = await self.client.ws_connect("/v1/transcribe", headers=HEADERS)
            await ws.send_json({"type": "start", **FORMAT, field: value})
            self.assertEqual((await ws.receive_json())["code"], "bad_format")
            await ws.close()
            await asyncio.sleep(0.01)
        self.assertFalse(self.instances)

    async def test_invalid_pcm_and_control_frames(self):
        for payload in (b"", b"\x00", b"\0" * 32002, "not-json", "[]", '{"type":"start"}', '"' + "a" * 1025 + '"'):
            ws = await self.start()
            if isinstance(payload, bytes):
                await ws.send_bytes(payload)
            else:
                await ws.send_str(payload)
            self.assertEqual((await ws.receive_json())["code"], "bad_frame")
            await ws.close()
            await asyncio.sleep(0.01)

    async def test_v1_start_does_not_accept_extra_source_or_version_fields(self):
        for extra in ({"protocolVersion": 1}, {"sources": ["computer"]}, {"unexpected": True}):
            ws = await self.client.ws_connect("/v1/transcribe", headers=HEADERS)
            await ws.send_json({"type": "start", **FORMAT, **extra})
            failure = await ws.receive_json()
            self.assertEqual(failure["code"], "bad_format")
            self.assertEqual(set(failure), {"type", "code", "message"})
            await ws.close()
            await asyncio.sleep(0.01)
        self.assertFalse(self.instances)

    async def test_maximum_legal_frame_is_accepted(self):
        ws = await self.start()
        await ws.send_bytes(b"\0" * 32000)
        self.assertEqual((await ws.receive_json())["type"], "partial")
        await ws.send_json({"type": "stop"})
        self.assertEqual((await ws.receive_json())["endSeconds"], 1.0)
        self.assertEqual(await ws.receive_json(), {"type": "stopped"})

    async def test_compression_is_not_negotiated(self):
        ws = await self.client.ws_connect("/v1/transcribe", headers=HEADERS, compress=15)
        self.assertEqual(ws.compress, 0)
        await ws.close()

    async def test_transport_rejects_oversized_message(self):
        ws = await self.start()
        await ws.send_bytes(b"\0" * 64000)
        response = await ws.receive()
        self.assertEqual(response.type, WSMsgType.CLOSE)
        self.assertEqual(response.data, 1009)

    async def test_start_and_idle_timeouts(self):
        self.app[RUNTIME].limits = replace(Limits(), start_seconds=0.03, idle_seconds=0.03)
        ws = await self.client.ws_connect("/v1/transcribe", headers=HEADERS)
        self.assertEqual((await ws.receive_json())["code"], "timeout")
        await ws.close()
        await asyncio.sleep(0.01)
        ws = await self.start()
        self.assertEqual((await ws.receive_json())["code"], "timeout")

    async def test_backlog_is_explicit_and_health_stays_responsive(self):
        self.app[RUNTIME].limits = replace(Limits(), queue_frames=1)

        class Slow(FakeRecognizer):
            def accept(self, pcm):
                time.sleep(0.12)
                return super().accept(pcm)

        self.app[RUNTIME].factory = Slow
        ws = await self.start()
        for _ in range(6):
            await ws.send_bytes(b"\0" * 3200)
        response = await self.client.get("/health")
        self.assertEqual(response.status, 200)
        events = []
        while True:
            event = await ws.receive_json()
            events.append(event)
            if event["type"] == "error":
                break
        self.assertEqual(events[-1]["code"], "backlog")

    async def test_fast_audio_is_rejected(self):
        self.app[RUNTIME].limits = replace(Limits(), burst_bytes=3200, bytes_per_second=1)
        ws = await self.start()
        await ws.send_bytes(b"\0" * 3200)
        await ws.receive_json()
        await ws.send_bytes(b"\0" * 3200)
        self.assertEqual((await ws.receive_json())["code"], "rate_limit")

    async def test_session_duration_limit(self):
        self.app[RUNTIME].limits = replace(Limits(), session_seconds=0.05)
        ws = await self.start()
        await ws.send_bytes(b"\0" * 3200)
        self.assertEqual((await ws.receive_json())["code"], "session_limit")

    async def test_decoder_exception_is_generic_and_fails_readiness(self):
        class Broken(FakeRecognizer):
            def accept(self, pcm):
                raise ValueError("private synthetic text and secret path")
        self.app[RUNTIME].factory = Broken
        ws = await self.start()
        await ws.send_bytes(b"\0" * 3200)
        failure = await ws.receive_json()
        self.assertEqual(failure["code"], "decoder_error")
        self.assertNotIn("private", json.dumps(failure))
        self.assertEqual((await self.client.get("/health")).status, 503)

    async def test_decoder_timeout_fails_closed(self):
        class Slow(FakeRecognizer):
            def accept(self, pcm):
                time.sleep(0.15)
                return super().accept(pcm)
        self.app[RUNTIME].factory = Slow
        self.app[RUNTIME].limits = replace(Limits(), operation_seconds=0.03)
        ws = await self.start()
        await ws.send_bytes(b"\0" * 3200)
        self.assertEqual((await ws.receive_json())["code"], "decoder_error")
        self.assertEqual((await self.client.get("/health")).status, 503)
        await asyncio.sleep(0.16)

    async def test_shutdown_notifies_client(self):
        ws = await self.start()
        await self.app.shutdown()
        self.assertEqual((await ws.receive_json())["code"], "server_stopping")

    async def test_disconnect_releases_admission(self):
        ws = await self.start()
        await ws.close()
        await asyncio.sleep(0.02)
        second = await self.start()
        await second.close()


class ConfigurationTests(unittest.TestCase):
    def test_absent_or_weak_tokens_fail(self):
        for value in ("", "short", "a" * 513, "a" * 32 + "\nextra"):
            with self.assertRaises(ValueError):
                load_token({"PHANTOM_TRANSCRIPTION_TOKEN": value})

    def test_token_environment(self):
        self.assertEqual(load_token({"PHANTOM_TRANSCRIPTION_TOKEN": TOKEN}), TOKEN)

    def test_token_file_precedence_without_reading_real_secret(self):
        from io import StringIO
        with patch.object(Path, "open", return_value=StringIO(TOKEN + "\n")):
            self.assertEqual(load_token({
                "PHANTOM_TRANSCRIPTION_TOKEN_FILE": "synthetic-only",
                "PHANTOM_TRANSCRIPTION_TOKEN": "wrong",
            }), TOKEN)

    def test_absent_model_fails_without_download(self):
        with self.assertRaises(ValueError):
            verify_model(Path(__file__).parent / "deliberately-absent-model")


if __name__ == "__main__":
    unittest.main()
