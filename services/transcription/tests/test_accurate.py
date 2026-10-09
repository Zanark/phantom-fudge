"""No model downloads/endpoints: deterministic PCM, fake decoders and ephemeral servers."""

import asyncio
from contextlib import nullcontext
from dataclasses import replace
import json
from pathlib import Path
import sys
import threading
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import uuid

from aiohttp.test_utils import TestClient, TestServer

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from accurate import FinalSpan, HybridRecognizer, MAX_SPAN_BYTES, WhisperFinalizer
from configuration import DEFAULT, load_config, metadata
from diagnostics import Diagnostics
import manage
from recognizer import Recognition
from server import FORMAT, Limits, RUNTIME, SOURCES, create_app, production_app

ROOT = Path(__file__).resolve().parents[3]
TOKEN = "synthetic_accurate_test_" + "x" * 40
AUTH = {"Authorization": "Bearer " + TOKEN}
START = {"type": "start", "protocolVersion": 2, **FORMAT, "sources": list(SOURCES)}


class Provisional:
    def __init__(self, endpoint_bytes=6400):
        self.data = bytearray()
        self.endpoint_bytes = endpoint_bytes

    def accept(self, pcm):
        self.data.extend(pcm)
        return Recognition("untrusted provisional", len(self.data) >= self.endpoint_bytes)


class SpanTests(unittest.TestCase):
    def test_every_byte_exactly_once_across_endpoints_and_stop(self):
        instances = []

        def factory():
            item = Provisional()
            instances.append(item)
            return item

        decoder = HybridRecognizer(factory)
        chunks = [bytes([number, 0]) * 1600 for number in range(1, 8)]
        spans = []
        for pcm in chunks:
            result = decoder.accept(pcm)
            if isinstance(result, FinalSpan):
                spans.append(result)
        spans.append(decoder.finish())
        self.assertEqual(b"".join(span.pcm for span in spans), b"".join(chunks))
        self.assertEqual([(s.start_sample, s.end_sample) for s in spans],
                         [(0, 3200), (3200, 6400), (6400, 9600), (9600, 11200)])
        self.assertEqual(b"".join(bytes(item.data) for item in instances), b"".join(chunks))
        with self.assertRaises(ValueError):
            decoder.finish()
        with self.assertRaises(ValueError):
            decoder.accept(chunks[0])

    def test_empty_endpoint_and_immediate_stop_never_replay_words(self):
        decoder = HybridRecognizer(lambda: Provisional(3200))
        first = decoder.accept(b"\1\0" * 1600)
        tail = decoder.finish()
        self.assertEqual(len(first.pcm), 3200)
        self.assertEqual(tail.pcm, b"")
        self.assertEqual((tail.start_sample, tail.end_sample), (1600, 1600))

    def test_long_rotation_exact_boundary_and_tail(self):
        decoder = HybridRecognizer(lambda: Provisional(30 * 32000))
        spans = []
        for index in range(65):
            result = decoder.accept(bytes([index, 0]) * 16000)
            if isinstance(result, FinalSpan):
                spans.append(result)
        spans.append(decoder.finish())
        self.assertEqual([s.end_sample for s in spans], [240000, 480000, 720000, 960000, 1040000])
        self.assertEqual(sum(len(s.pcm) for s in spans), 65 * 32000)
        self.assertTrue(all(len(s.pcm) <= MAX_SPAN_BYTES for s in spans))

    def test_one_source_rotation_does_not_reset_other(self):
        computer = HybridRecognizer(lambda: Provisional(3200))
        microphone = HybridRecognizer(lambda: Provisional(6400))
        microphone.accept(b"\2\0" * 1600)
        mic_native = microphone._decoder
        for _ in range(10):
            computer.accept(b"\1\0" * 1600)
        self.assertIs(microphone._decoder, mic_native)
        tail = microphone.finish()
        self.assertEqual(tail.pcm, b"\2\0" * 1600)
        self.assertEqual(tail.start_sample, 0)

    def test_span_refuses_mismatched_accounting_and_unbounded_buffers(self):
        for args in ((b"\0\0", 0, 2, "test"), (b"", -1, -1, "test"),
                     (b"", 0.0, 0.0, "test"), (bytearray(), 0, 0, "test"),
                     (b"\0" * (MAX_SPAN_BYTES + 2), 0, MAX_SPAN_BYTES // 2 + 1, "test")):
            with self.assertRaises(ValueError):
                FinalSpan(*args)

    def test_decoder_failure_is_not_returned_as_success(self):
        class Broken(Provisional):
            def accept(self, pcm):
                raise ValueError("synthetic_content_not_for_logs")
        decoder = HybridRecognizer(Broken)
        with self.assertRaises(ValueError):
            decoder.accept(b"\0\1")


class WhisperTests(unittest.TestCase):
    def test_empty_and_digital_silence_never_invoke_model(self):
        class Never:
            def transcribe(self, *args, **kwargs):
                raise AssertionError("silent input invoked model")
        final = WhisperFinalizer(Path("."), "small.en", model=Never())
        for pcm in (b"", b"\0" * 32000):
            result = final(FinalSpan(pcm, 16000, 16000 + len(pcm) // 2, "test"))
            self.assertEqual(result.text, "")
            self.assertEqual(result.start, 1)
            self.assertTrue(result.final)

    def test_whisper_uses_vad_no_hints_and_owned_absolute_span(self):
        calls = []

        class Fake:
            def transcribe(self, audio, **options):
                calls.append(options)
                return iter([SimpleNamespace(text=" genuine words ", start=0.05, end=0.1)]), None
        final = WhisperFinalizer(Path("."), "small.en", model=Fake())
        result = final(FinalSpan(b"\0\x10" * 1600, 8000, 9600, "test"))
        self.assertEqual(result, Recognition("genuine words", True, 0.5, 0.6))
        self.assertIsNone(calls[0]["initial_prompt"])
        self.assertIsNone(calls[0]["hotwords"])
        self.assertTrue(calls[0]["vad_filter"])
        self.assertFalse(calls[0]["condition_on_previous_text"])

    def test_vad_or_whisper_generator_failure_propagates(self):
        class Broken:
            def transcribe(self, *args, **kwargs):
                def fail():
                    raise RuntimeError("synthetic_secret")
                    yield
                return fail(), None
        final = WhisperFinalizer(Path("."), "small.en", model=Broken())
        with self.assertRaises(RuntimeError):
            final(FinalSpan(b"\0\x10" * 1600, 0, 1600, "test"))

    def test_missing_selected_model_never_falls_back(self):
        with patch("accurate.verify_whisper", side_effect=ValueError("model_unavailable")):
            with self.assertRaises(ValueError):
                WhisperFinalizer(Path("missing"), "small.en")

    def test_vocabulary_is_only_disclosed_general_terms_not_reference_text(self):
        calls = []

        class Fake:
            def transcribe(self, audio, **options):
                calls.append(options)
                return iter(()), None
        final = WhisperFinalizer(Path("."), "small.en", model=Fake(), vocabulary="technical")
        final(FinalSpan(b"\0\x10" * 1600, 0, 1600, "test"))
        terms = json.loads((Path(__file__).resolve().parents[1] /
                            "technical-vocabulary.json").read_text(encoding="utf-8"))["terms"]
        self.assertEqual(calls[0]["hotwords"], ", ".join(terms))
        self.assertIsNone(calls[0]["initial_prompt"])
        self.assertNotIn("Write me", calls[0]["hotwords"])
        self.assertNotIn("two sum", calls[0]["hotwords"].lower())
        self.assertTrue(all(len(term.split()) <= 2 for term in terms))


class FakeHybridFactory:
    def __init__(self, endpoint=6400, delay=0, fail=False, gate=None):
        self.endpoint = endpoint
        self.delay = delay
        self.fail = fail
        self.gate = gate
        self.spans = []
        self.started = threading.Event()

    def __call__(self):
        return HybridRecognizer(lambda: Provisional(self.endpoint))

    def finalize(self, span):
        self.started.set()
        if self.gate:
            self.gate.wait(2)
        time.sleep(self.delay)
        if self.fail:
            raise RuntimeError("synthetic_private_exception")
        self.spans.append(span)
        text = str(span.pcm[0]) if span.pcm else ""
        return Recognition(text, True, span.start_sample / 16000, span.end_sample / 16000)


class HybridProtocolTests(unittest.IsolatedAsyncioTestCase):
    async def asyncSetUp(self):
        self.factory = FakeHybridFactory()
        self.app = create_app(TOKEN, self.factory)
        self.client = TestClient(TestServer(self.app))
        await self.client.start_server()

    async def asyncTearDown(self):
        if self.factory.gate:
            self.factory.gate.set()
        await self.client.close()

    async def start(self, v2=True):
        ws = await self.client.ws_connect("/v1/transcribe", headers=AUTH)
        await ws.send_json(START if v2 else {"type": "start", **FORMAT})
        ready = await ws.receive_json()
        self.assertEqual(ready, {**START, "type": "ready"} if v2 else {"type": "ready", **FORMAT})
        return ws

    async def collect(self, ws):
        events = []
        async with asyncio.timeout(4):
            while True:
                event = await ws.receive_json()
                events.append(event)
                if event["type"] in ("error", "stopped"):
                    return events

    async def test_v1_and_v2_schemas_stay_strict_with_accurate_finals(self):
        for v2 in (False, True):
            ws = await self.start(v2)
            await ws.send_bytes((b"\1" if v2 else b"") + b"\7\0" * 3200)
            await ws.send_json({"type": "stop"})
            events = await self.collect(ws)
            finals = [e for e in events if e["type"] == "final"]
            self.assertEqual([e["text"] for e in finals], ["7", ""] + ([""] if v2 else []))
            expected = {"type", "text", "segmentId", "startSeconds", "endSeconds"} | ({"source"} if v2 else set())
            self.assertTrue(all(set(e) == expected for e in finals))
            self.assertEqual(events[-1], {"type": "stopped"})
            await ws.close()
            while self.app[RUNTIME].active:
                await asyncio.sleep(0.001)

    async def test_separate_source_spans_and_global_ids_no_lost_or_duplicated_tail(self):
        ws = await self.start()
        for source, value, count in (("computer", 7, 3200), ("microphone", 9, 1600),
                                     ("computer", 8, 1600)):
            await ws.send_bytes(bytes([SOURCES[source]]) + bytes([value, 0]) * count)
        await ws.send_json({"type": "stop"})
        events = await self.collect(ws)
        finals = [e for e in events if e["type"] == "final"]
        self.assertEqual([e["segmentId"] for e in finals], list(range(1, len(finals) + 1)))
        self.assertEqual([e["text"] for e in finals if e["source"] == "computer"], ["7", "8"])
        self.assertEqual([e["text"] for e in finals if e["source"] == "microphone"], ["9"])
        self.assertEqual(sum(len(s.pcm) for s in self.factory.spans), 12800)
        self.assertEqual([(e["startSeconds"], e["endSeconds"]) for e in finals
                          if e["source"] == "computer"], [(0, 0.2), (0.2, 0.3)])

    async def test_slow_final_does_not_block_intake_or_other_source_partial(self):
        gate = threading.Event()
        self.factory.gate = gate
        ws = await self.start()
        await ws.send_bytes(b"\1" + b"\7\0" * 3200)
        async with asyncio.timeout(2):
            while not self.factory.started.is_set():
                await asyncio.sleep(0.001)
        await ws.send_bytes(b"\2" + b"\x09\0" * 1600)
        event = await asyncio.wait_for(ws.receive_json(), 1)
        self.assertEqual(event, {"type": "partial", "source": "microphone", "text": "untrusted provisional"})
        await ws.send_bytes(b"\1" + b"\x08\0" * 1600)
        await asyncio.sleep(0.02)
        gate.set()
        event = await ws.receive_json()
        self.assertEqual((event["type"], event["text"]), ("final", "7"))
        self.assertEqual((await ws.receive_json())["type"], "partial")
        await ws.send_json({"type": "stop"})
        self.assertEqual((await self.collect(ws))[-1]["type"], "stopped")

    async def test_slow_provisional_source_cannot_block_the_other_source(self):
        blocked, release = threading.Event(), threading.Event()
        created = []

        def factory():
            source_index = len(created)
            created.append(source_index)

            class Decoder(Provisional):
                def accept(self, pcm):
                    if source_index == 0:
                        blocked.set()
                        release.wait(2)
                    return super().accept(pcm)
            return HybridRecognizer(Decoder)

        factory.finalize = self.factory.finalize
        self.app[RUNTIME].factory = factory
        ws = await self.start()
        try:
            await ws.send_bytes(b"\1" + b"\7\0" * 1600)
            async with asyncio.timeout(1):
                while not blocked.is_set():
                    await asyncio.sleep(0.001)
            await ws.send_bytes(b"\2" + b"\x09\0" * 1600)
            event = await asyncio.wait_for(ws.receive_json(), 0.5)
            self.assertEqual((event["type"], event["source"]), ("partial", "microphone"))
            self.assertEqual(len(self.app[RUNTIME].source_executors), 2)
        finally:
            release.set()
        await ws.send_json({"type": "stop"})
        self.assertEqual((await self.collect(ws))[-1]["type"], "stopped")

    async def test_stop_waits_for_full_final_queue_then_drains_every_pcm_span(self):
        self.factory.endpoint = 3200
        self.factory.delay = 0.02
        ws = await self.start()
        for source in (1, 2):
            await ws.send_bytes(bytes([source]) + bytes([source + 2, 0]) * 1600)
            await ws.send_bytes(bytes([source]) + bytes([source + 2, 0]) * 1600)
        await ws.send_json({"type": "stop"})
        events = await self.collect(ws)
        self.assertEqual(events[-1], {"type": "stopped"})
        self.assertEqual(sum(len(s.pcm) for s in self.factory.spans), 12800)
        self.assertEqual(sum(bool(e.get("text")) for e in events if e["type"] == "final"), 4)
        self.assertEqual(self.app[RUNTIME].drop_count, 0)

    async def test_excess_final_backlog_faults_instead_of_silent_drop_or_unbounded_tasks(self):
        self.factory.endpoint = 3200
        self.factory.delay = 0.15
        ws = await self.start()
        for _ in range(3):
            await ws.send_bytes(b"\1" + b"\7\0" * 1600)
        events = await self.collect(ws)
        self.assertEqual(events[-1]["code"], "backlog")
        self.assertLessEqual(self.app[RUNTIME].max_pending_finals, 2)
        self.assertEqual(self.app[RUNTIME].fault_count, 1)
        self.assertEqual(self.app[RUNTIME].drop_count, 1)

    async def test_finalizer_failure_is_generic_and_never_claims_stopped(self):
        self.factory.fail = True
        ws = await self.start()
        await ws.send_bytes(b"\1" + b"\7\0" * 1600)
        await ws.send_json({"type": "stop"})
        events = await self.collect(ws)
        self.assertEqual(events[-1]["code"], "decoder_error")
        self.assertNotIn("synthetic_private", json.dumps(events))
        self.assertTrue(self.app[RUNTIME].failed)

    async def test_final_timeout_marks_runtime_unready_and_stops_admitting_sessions(self):
        self.app[RUNTIME].limits = replace(Limits(), final_seconds=0.01)
        self.factory.delay = 0.03
        ws = await self.start()
        await ws.send_bytes(b"\1" + b"\7\0" * 1600)
        await ws.send_json({"type": "stop"})
        self.assertEqual((await self.collect(ws))[-1]["code"], "decoder_error")
        self.assertFalse(self.app[RUNTIME].ready)

    async def test_total_stop_deadline_faults_instead_of_waiting_without_bound(self):
        self.app[RUNTIME].limits = replace(Limits(), stop_seconds=0.01)
        self.factory.delay = 0.04
        ws = await self.start()
        await ws.send_bytes(b"\1" + b"\7\0" * 1600)
        await ws.send_json({"type": "stop"})
        events = await self.collect(ws)
        self.assertEqual(events[-1]["code"], "decoder_error")
        self.assertNotIn("stopped", [e["type"] for e in events])

    async def test_deferred_partial_is_size_checked_while_final_is_pending(self):
        class Oversized:
            calls = 0

            def accept(self, pcm):
                self.calls += 1
                return (FinalSpan(pcm, 0, len(pcm) // 2, "test") if self.calls == 1
                        else Recognition("x" * 8193))

        def factory():
            return Oversized()

        self.factory.delay = 0.05
        factory.finalize = self.factory.finalize
        self.app[RUNTIME].factory = factory
        ws = await self.start()
        await ws.send_bytes(b"\1" + b"\7\0" * 1600)
        await ws.send_bytes(b"\1" + b"\7\0" * 1600)
        events = await self.collect(ws)
        self.assertEqual(events[-1]["code"], "decoder_error")
        self.assertNotIn("x" * 8193, json.dumps(events))


class ConfigAndManagerTests(unittest.TestCase):
    def test_fingerprint_changes_for_every_engine_setting(self):
        identity = metadata(DEFAULT)
        self.assertTrue(manage.supports_current_engine(identity, DEFAULT))
        for key, value in (("model", "base.en"), ("engine", "vosk"), ("threads", 2),
                           ("vocabulary", "none")):
            self.assertFalse(manage.supports_current_engine(identity, {**DEFAULT, key: value}))
        self.assertFalse(manage.supports_current_engine({}, DEFAULT))

    def test_runtime_code_change_also_changes_fingerprint(self):
        identity = metadata(DEFAULT)
        original = Path.read_bytes

        def changed(path):
            content = original(path)
            return content + b"\n" if path.name == "accurate.py" else content

        with patch.object(Path, "read_bytes", changed):
            self.assertFalse(manage.supports_current_engine(identity, DEFAULT))

    def test_bad_configuration_is_not_accepted(self):
        examples = [[], {}, {**DEFAULT, "engine": "cloud"}, {**DEFAULT, "model": "medium"},
                    {**DEFAULT, "threads": True}, {**DEFAULT, "threads": 32},
                    {**DEFAULT, "vocabulary": "reference sentence"}]
        for config in examples:
            with self.subTest(config=config), patch.object(Path, "exists", return_value=True), \
                    patch.object(Path, "read_text", return_value=json.dumps(config)), \
                    self.assertRaises(ValueError):
                load_config(Path("synthetic-config.json"))

    def test_stale_owned_active_runtime_is_never_stopped(self):
        info = {"ready": True, "active": True, "speechProtocolVersions": [1, 2]}
        with patch.object(manage, "token", return_value=TOKEN), \
                patch.object(manage, "lifecycle_lock", return_value=nullcontext()), \
                patch.object(manage, "load_config", return_value=DEFAULT), \
                patch.object(manage, "read_record", return_value={"instanceId": "a" * 32}), \
                patch.object(manage, "port_in_use", return_value=True), \
                patch.object(manage, "owned_info", return_value=info), \
                patch.object(manage, "stop_owned") as stop, \
                patch.object(manage, "validate_start") as verify:
            with self.assertRaises(manage.RuntimeFailure):
                manage.start()
            stop.assert_not_called()
            verify.assert_not_called()

    def test_stale_owned_idle_service_verified_before_shutdown(self):
        calls = []
        info = {"ready": True, "active": False, "speechProtocolVersions": [1, 2]}
        with patch.object(manage, "token", return_value=TOKEN), \
                patch.object(manage, "lifecycle_lock", return_value=nullcontext()), \
                patch.object(manage, "load_config", return_value=DEFAULT), \
                patch.object(manage, "read_record", return_value={"instanceId": "a" * 32}), \
                patch.object(manage, "port_in_use", return_value=True), \
                patch.object(manage, "owned_info", return_value=info), \
                patch.object(manage, "validate_start", side_effect=lambda _: calls.append("verified")), \
                patch.object(manage, "stop_owned", side_effect=lambda *_: (_ for _ in ()).throw(
                    manage.RuntimeFailure("test-stop"))) as stop:
            with self.assertRaisesRegex(manage.RuntimeFailure, "test-stop"):
                manage.start()
            self.assertEqual(calls, ["verified"])
            stop.assert_called_once()

    def test_missing_model_does_not_stop_old_service(self):
        info = {"ready": True, "active": False, "speechProtocolVersions": [1, 2]}
        with patch.object(manage, "token", return_value=TOKEN), \
                patch.object(manage, "lifecycle_lock", return_value=nullcontext()), \
                patch.object(manage, "load_config", return_value=DEFAULT), \
                patch.object(manage, "read_record", return_value={"instanceId": "a" * 32}), \
                patch.object(manage, "port_in_use", return_value=True), \
                patch.object(manage, "owned_info", return_value=info), \
                patch.object(manage, "validate_start", side_effect=manage.RuntimeFailure("missing")), \
                patch.object(manage, "stop_owned") as stop:
            with self.assertRaises(manage.RuntimeFailure):
                manage.start()
            stop.assert_not_called()

    def test_current_engine_is_reused_without_reloading_or_stopping(self):
        info = {"ready": True, "active": True, "speechProtocolVersions": [1, 2], **metadata(DEFAULT)}
        with patch.object(manage, "token", return_value=TOKEN), \
                patch.object(manage, "lifecycle_lock", return_value=nullcontext()), \
                patch.object(manage, "load_config", return_value=DEFAULT), \
                patch.object(manage, "read_record", return_value={"instanceId": "a" * 32}), \
                patch.object(manage, "port_in_use", return_value=True), \
                patch.object(manage, "owned_info", return_value=info), \
                patch.object(manage, "stop_owned") as stop:
            manage.start()
            stop.assert_not_called()

    def test_unreachable_owned_receipt_is_not_overwritten_or_duplicated(self):
        with patch.object(manage, "token", return_value=TOKEN), \
                patch.object(manage, "lifecycle_lock", return_value=nullcontext()), \
                patch.object(manage, "load_config", return_value=DEFAULT), \
                patch.object(manage, "read_record", return_value={"instanceId": "a" * 32}), \
                patch.object(manage, "port_in_use", return_value=False), \
                patch.object(manage, "write_record") as write, \
                patch.object(manage.subprocess, "Popen") as spawn:
            with self.assertRaises(manage.RuntimeFailure):
                manage.start()
            write.assert_not_called()
            spawn.assert_not_called()


class DiagnosticTests(unittest.TestCase):
    def setUp(self):
        self.folder = ROOT / ".local" / "speech-quality" / "test-diagnostics" / uuid.uuid4().hex
        self.path = self.folder / "test.jsonl"

    def tearDown(self):
        if self.folder.is_dir():
            for file in self.folder.iterdir():
                file.unlink()
            self.folder.rmdir()

    def test_rotates_and_never_accepts_content_fields(self):
        logger = Diagnostics(self.path, "hybrid-whisper", "small.en", max_bytes=500)
        for _ in range(10):
            self.assertTrue(logger.emit("decode", "computer", decode_ms=2, audio_ms=100))
        files = list(self.folder.iterdir())
        self.assertLessEqual(len(files), 4)
        self.assertTrue(all(file.stat().st_size <= 500 for file in files))
        with self.assertRaises(ValueError):
            logger.emit("decode", transcript="secret")
        with self.assertRaises(ValueError):
            logger.emit("unknown-content")
        with self.assertRaises(ValueError):
            logger.emit("fault", fault="synthetic_full_exception")
        for file in files:
            for line in file.read_text(encoding="utf-8").splitlines():
                record = json.loads(line)
                self.assertEqual(record["source"], "computer")
                self.assertNotIn("secret", line)

    def test_io_failure_is_observable_and_prints_only_generic_status(self):
        logger = Diagnostics(self.path, "hybrid-whisper", "small.en")
        with patch.object(Path, "mkdir", side_effect=OSError("private-path")), \
                patch("diagnostics.print") as notice:
            self.assertFalse(logger.emit("model_load"))
            self.assertTrue(logger.failed)
            notice.assert_called_once()
            self.assertNotIn("private-path", str(notice.call_args))


class DiagnosticProtocolTests(unittest.IsolatedAsyncioTestCase):
    async def test_selected_accurate_model_failure_never_loads_legacy_fallback(self):
        with patch("server.HybridFactory", side_effect=ValueError("private-model-path")), \
                patch("server.VoskFactory") as legacy, patch("server.Diagnostics"), \
                patch("server.print") as generic:
            app = await production_app(TOKEN, Path("missing"), config=DEFAULT)
            async with TestClient(TestServer(app)) as client:
                self.assertEqual((await client.get("/health")).status, 503)
                response = await client.get("/v1/transcribe", headers=AUTH)
                self.assertEqual(response.status, 503)
                self.assertEqual((await response.json())["code"], "model_unavailable")
            legacy.assert_not_called()
            self.assertNotIn("private-model-path", str(generic.call_args))

    async def test_logging_failure_exposed_only_on_authenticated_runtime(self):
        logger = SimpleNamespace(failed=True)
        app = create_app(TOKEN, lambda: None, diagnostics=logger, engine_metadata=metadata(DEFAULT))
        async with TestClient(TestServer(app)) as client:
            response = await client.get("/v1/runtime", headers=AUTH)
            data = await response.json()
            self.assertIs(data["diagnosticsAvailable"], False)
            self.assertEqual(data["engine"], "hybrid-whisper")
            self.assertNotIn(TOKEN, json.dumps(data))
            self.assertNotIn("path", json.dumps(data).lower())
            denied = await client.get("/v1/runtime")
            self.assertEqual(denied.status, 401)
            self.assertNotIn("engine", await denied.json())


if __name__ == "__main__":
    unittest.main()
