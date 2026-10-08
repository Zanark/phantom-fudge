import asyncio
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

from aiohttp.test_utils import TestClient, TestServer

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import manage
from recognizer import Recognition
from server import FORMAT, RUNTIME, create_app


TOKEN = "synthetic_management_token_" + "x" * 40
INSTANCE = "a" * 32
AUTH = {"Authorization": "Bearer " + TOKEN}
OWNED = {**AUTH, "X-Phantom-Instance": INSTANCE}


class EmptyRecognizer:
    def finish(self):
        return Recognition("", True, 0, 0)


class ManagementTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.stopped = asyncio.Event()
        self.app = create_app(TOKEN, EmptyRecognizer, instance_id=INSTANCE,
                              managed_stop=self.stopped.set)
        self.client = TestClient(TestServer(self.app))
        await self.client.start_server()

    async def asyncTearDown(self):
        await self.client.close()

    async def test_runtime_is_authenticated_and_contains_no_paths_or_token(self):
        denied = await self.client.get("/v1/runtime")
        self.assertEqual(denied.status, 401)
        response = await self.client.get("/v1/runtime", headers=AUTH)
        data = await response.json()
        self.assertEqual(data["instanceId"], INSTANCE)
        self.assertEqual(data["service"], "phantom-fudge-transcription")
        self.assertTrue(data["ready"])
        self.assertGreater(data["pid"], 0)
        self.assertNotIn(TOKEN, json.dumps(data))
        self.assertNotIn("path", json.dumps(data).lower())

    async def test_browser_cannot_manage_even_with_token(self):
        for route in ("/v1/runtime", "/v1/shutdown"):
            method = self.client.get if route.endswith("runtime") else self.client.post
            response = await method(route, headers={**OWNED, "Origin": "https://example.invalid"})
            self.assertEqual(response.status, 401)
        self.assertFalse(self.app[RUNTIME].stopping)

    async def test_wrong_instance_cannot_shutdown(self):
        response = await self.client.post("/v1/shutdown",
                                          headers={**AUTH, "X-Phantom-Instance": "b" * 32})
        self.assertEqual(response.status, 409)
        self.assertFalse(self.app[RUNTIME].stopping)

    async def test_unmanaged_service_rejects_managed_shutdown(self):
        self.app[RUNTIME].instance_id = None
        response = await self.client.post("/v1/shutdown", headers=OWNED)
        self.assertEqual(response.status, 409)

    async def test_active_transcription_is_not_interrupted(self):
        ws = await self.client.ws_connect("/v1/transcribe", headers=AUTH)
        await ws.send_json({"type": "start", **FORMAT})
        self.assertEqual((await ws.receive_json())["type"], "ready")
        response = await self.client.post("/v1/shutdown", headers=OWNED)
        self.assertEqual(response.status, 409)
        self.assertEqual((await response.json())["code"], "busy")
        self.assertFalse(self.app[RUNTIME].stopping)
        await ws.send_json({"type": "stop"})
        self.assertEqual((await ws.receive_json())["type"], "final")
        self.assertEqual((await ws.receive_json())["type"], "stopped")
        await ws.close()

    async def test_owned_idle_shutdown_marks_unready_and_schedules_exit(self):
        response = await self.client.post("/v1/shutdown", headers=OWNED)
        self.assertEqual(await response.json(), {"status": "stopping"})
        health = await self.client.get("/health")
        self.assertEqual(health.status, 503)
        await asyncio.wait_for(self.stopped.wait(), 1)

    async def test_body_and_query_not_accepted(self):
        response = await self.client.post("/v1/shutdown", headers=OWNED, data=b"unexpected")
        self.assertEqual(response.status, 400)
        response = await self.client.get("/v1/runtime?unexpected=1", headers=AUTH)
        self.assertEqual(response.status, 400)
        self.assertFalse(self.app[RUNTIME].stopping)


class ManagerTests(unittest.TestCase):
    def test_missing_receipt_does_not_adopt_a_foreign_service(self):
        with patch.object(manage, "request") as request:
            with self.assertRaises(manage.RuntimeFailure):
                manage.owned_info(TOKEN, None)
            request.assert_not_called()

    def test_mismatched_instance_is_not_owned(self):
        with patch.object(manage, "request", return_value={
            "service": "phantom-fudge-transcription", "protocolVersion": 1, "instanceId": "b" * 32
        }):
            with self.assertRaises(manage.RuntimeFailure):
                manage.owned_info(TOKEN, {"instanceId": INSTANCE})

    def test_invalid_receipt_does_not_trigger_an_action(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "runtime.json"
            path.write_text('{"schemaVersion":1,"instanceId":42}', encoding="utf-8")
            with patch.object(manage, "RECORD", path):
                with self.assertRaises(manage.RuntimeFailure):
                    manage.read_record()

    def test_redirects_never_forward_authentication(self):
        with self.assertRaises(manage.RuntimeFailure):
            manage.NoRedirects().redirect_request(None, None, 302, "", {},
                                                   "https://example.invalid")
