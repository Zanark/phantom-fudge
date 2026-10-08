"""Authenticated, memory-only, local streaming transcription. No model downloads."""

import asyncio
from concurrent.futures import ThreadPoolExecutor
from contextlib import suppress
from dataclasses import dataclass
import hmac
import json
import logging
import math
import os
from pathlib import Path
import re
import sys
import time
import uuid

from aiohttp import WSMsgType, web

from recognizer import Recognition, VoskFactory


FORMAT = {"sampleRate": 16000, "channels": 1, "encoding": "pcm_s16le"}
SOURCES = {"computer": 1, "microphone": 2}
SOURCE_NAMES = {value: name for name, value in SOURCES.items()}
ERRORS = {
    "auth": "Local authentication failed. Check the configured token.",
    "origin": "Browser connections are not supported.",
    "busy": "Another transcription session is active.",
    "bad_format": "Start requires PCM16 little-endian mono audio at 16000 Hz.",
    "bad_frame": "An audio frame or control message is invalid.",
    "backlog": "Transcription cannot keep up. Stop and restart the session.",
    "rate_limit": "Audio arrived faster than the supported live stream.",
    "timeout": "The client did not send the expected message in time.",
    "decoder_error": "Local speech recognition failed. Restart the service.",
    "model_unavailable": "The local model is unavailable. Run setup and restart the service.",
    "session_limit": "The session duration limit was reached. Start a new session.",
    "server_stopping": "The local service is stopping.",
}


@dataclass(frozen=True)
class Limits:
    frame_bytes: int = 32000
    control_bytes: int = 1024
    queue_frames: int = 32
    queue_bytes: int = 64000
    start_seconds: float = 5.0
    idle_seconds: float = 15.0
    operation_seconds: float = 5.0
    send_seconds: float = 2.0
    session_seconds: float = 28800.0
    burst_bytes: int = 64000
    bytes_per_second: int = 32000
    burst_frames: int = 200
    frames_per_second: int = 100


class ProtocolError(Exception):
    def __init__(self, code):
        self.code = code
        super().__init__(code)


def error(code):
    return {"type": "error", "code": code, "message": ERRORS[code]}


def load_token(environ=None):
    environ = os.environ if environ is None else environ
    token_file = environ.get("PHANTOM_TRANSCRIPTION_TOKEN_FILE")
    if token_file:
        with Path(token_file).open("r", encoding="ascii") as stream:
            token = stream.read(514).strip()
    else:
        token = environ.get("PHANTOM_TRANSCRIPTION_TOKEN", "")
    if not re.fullmatch(r"[A-Za-z0-9_-]{32,512}", token):
        raise ValueError("auth")
    return token


def parse_control(message, limits):
    if message.type != WSMsgType.TEXT or len(message.data.encode("utf-8")) > limits.control_bytes:
        raise ProtocolError("bad_frame")
    try:
        value = json.loads(message.data)
    except (ValueError, RecursionError):
        raise ProtocolError("bad_frame") from None
    if not isinstance(value, dict):
        raise ProtocolError("bad_frame")
    return value


def parse_start(value):
    if type(value.get("sampleRate")) is not int or type(value.get("channels")) is not int:
        raise ProtocolError("bad_format")
    if value == {"type": "start", **FORMAT}:
        return 1, ("computer",)
    sources = value.get("sources")
    if (type(value.get("protocolVersion")) is int
            and value.get("protocolVersion") == 2
            and isinstance(sources, list) and sources
            and sources == [source for source in SOURCES if source in sources]
            and value == {"type": "start", "protocolVersion": 2, **FORMAT, "sources": sources}):
        return 2, tuple(sources)
    raise ProtocolError("bad_format")


class Runtime:
    def __init__(self, token, factory, limits, instance_id=None, managed_stop=None):
        self.token = token.encode("ascii")
        self.factory = factory
        self.limits = limits
        self.active = False
        self.stopping = False
        self.failed = False
        self.sockets = set()
        self.executor = ThreadPoolExecutor(max_workers=1, thread_name_prefix="speech")
        self.instance_id = instance_id
        self.managed_stop = managed_stop

    @property
    def ready(self):
        return self.factory is not None and not self.failed and not self.stopping

    async def cpu(self, function, *args):
        future = asyncio.get_running_loop().run_in_executor(self.executor, function, *args)
        try:
            return await asyncio.wait_for(asyncio.shield(future), self.limits.operation_seconds)
        except Exception:
            # A native call cannot safely be killed. Do not enqueue more work behind it.
            self.failed = True
            future.add_done_callback(lambda result: result.exception() if not result.cancelled() else None)
            raise ProtocolError("decoder_error") from None
        except asyncio.CancelledError:
            # Keep the one-session admission gate until the outstanding call is finished.
            try:
                await asyncio.wait_for(asyncio.shield(future), self.limits.operation_seconds)
            except Exception:
                self.failed = True
                future.add_done_callback(lambda result: result.exception() if not result.cancelled() else None)
            raise


RUNTIME = web.AppKey("runtime", Runtime)


async def send(ws, payload, limits):
    try:
        await asyncio.wait_for(ws.send_json(payload), limits.send_seconds)
    except TimeoutError:
        raise ProtocolError("backlog") from None


async def close_with_error(ws, code, limits):
    with suppress(Exception):
        await send(ws, error(code), limits)
    with suppress(Exception):
        await asyncio.wait_for(ws.close(code=1008), limits.send_seconds)


async def health(request):
    runtime = request.app[RUNTIME]
    return web.json_response(
        {"status": "ready" if runtime.ready else "unavailable", "modelAvailable": runtime.ready},
        status=200 if runtime.ready else 503,
        headers={"Cache-Control": "no-store"},
    )


def authorized(request, runtime):
    authorization = request.headers.getall("Authorization", [])
    supplied = authorization[0].removeprefix("Bearer ") if (
        len(authorization) == 1 and authorization[0].startswith("Bearer ")
    ) else ""
    return hmac.compare_digest(supplied.encode("utf-8"), runtime.token)


async def runtime_info(request):
    runtime = request.app[RUNTIME]
    if any(request.headers.getall("Origin", [])) or not authorized(request, runtime):
        return web.json_response(error("auth"), status=401)
    if request.query_string or request.can_read_body:
        return web.json_response(error("bad_frame"), status=400)
    return web.json_response({
        "service": "phantom-fudge-transcription",
        "protocolVersion": 1,
        "speechProtocolVersions": [1, 2],
        "instanceId": runtime.instance_id,
        "pid": os.getpid(),
        "ready": runtime.ready,
        "active": runtime.active,
    }, headers={"Cache-Control": "no-store"})


async def managed_shutdown(request):
    runtime = request.app[RUNTIME]
    if any(request.headers.getall("Origin", [])) or not authorized(request, runtime):
        return web.json_response(error("auth"), status=401)
    if request.query_string or request.can_read_body:
        return web.json_response(error("bad_frame"), status=400)
    if (not runtime.instance_id or not runtime.managed_stop or
            request.headers.get("X-Phantom-Instance") != runtime.instance_id):
        return web.json_response(error("bad_frame"), status=409)
    if runtime.active:
        return web.json_response(error("busy"), status=409)
    runtime.stopping = True
    asyncio.get_running_loop().call_later(0.2, runtime.managed_stop)
    return web.json_response({"status": "stopping"}, headers={"Cache-Control": "no-store"})


class SourceState:
    def __init__(self, decoder, limits):
        self.decoder = decoder
        self.total_bytes = 0
        self.last_partial = None
        self.audio_credit = float(limits.burst_bytes)
        self.frame_credit = float(limits.burst_frames)
        self.last_receive = time.monotonic()
        self.last_end = 0.0
        self.queued_bytes = 0
        self.queued_frames = 0


class Session:
    def __init__(self, runtime, ws, decoders, protocol_version=1):
        self.runtime = runtime
        self.limits = runtime.limits
        self.ws = ws
        self.protocol_version = protocol_version
        self.sources = {source: SourceState(decoder, self.limits) for source, decoder in decoders.items()}
        self.queue = asyncio.Queue(maxsize=self.limits.queue_frames * len(self.sources))
        self.queue_bytes = self.limits.queue_bytes * len(self.sources)
        self.queued_bytes = 0
        self.segment_id = 0

    async def receive(self):
        while True:
            try:
                message = await asyncio.wait_for(self.ws.receive(), self.limits.idle_seconds)
            except TimeoutError:
                raise ProtocolError("timeout") from None
            if message.type in (WSMsgType.CLOSE, WSMsgType.CLOSED, WSMsgType.CLOSING):
                return False
            if message.type == WSMsgType.ERROR:
                raise ProtocolError("bad_frame")
            if message.type == WSMsgType.TEXT:
                if parse_control(message, self.limits) != {"type": "stop"}:
                    raise ProtocolError("bad_frame")
                # Wait for a slot rather than faulting a full, valid audio queue on stop.
                await self.queue.put(None)
                return True
            if message.type != WSMsgType.BINARY:
                raise ProtocolError("bad_frame")
            pcm = message.data
            source = "computer"
            if self.protocol_version == 2:
                source = SOURCE_NAMES.get(pcm[0]) if pcm else None
                if source not in self.sources:
                    raise ProtocolError("bad_frame")
                pcm = pcm[1:]
            if not pcm or len(pcm) % 2 or len(pcm) > self.limits.frame_bytes:
                raise ProtocolError("bad_frame")
            state = self.sources[source]
            now = time.monotonic()
            elapsed = now - state.last_receive
            state.last_receive = now
            state.audio_credit = min(self.limits.burst_bytes, state.audio_credit + elapsed * self.limits.bytes_per_second)
            state.frame_credit = min(self.limits.burst_frames, state.frame_credit + elapsed * self.limits.frames_per_second)
            if state.audio_credit < len(pcm) or state.frame_credit < 1:
                raise ProtocolError("rate_limit")
            state.audio_credit -= len(pcm)
            state.frame_credit -= 1
            state.total_bytes += len(pcm)
            if state.total_bytes / 32000 > self.limits.session_seconds:
                raise ProtocolError("session_limit")
            if (self.queued_bytes + len(pcm) > self.queue_bytes
                    or state.queued_bytes + len(pcm) > self.limits.queue_bytes
                    or state.queued_frames >= self.limits.queue_frames):
                raise ProtocolError("backlog")
            try:
                self.queue.put_nowait((source, pcm))
            except asyncio.QueueFull:
                raise ProtocolError("backlog") from None
            self.queued_bytes += len(pcm)
            state.queued_bytes += len(pcm)
            state.queued_frames += 1

    async def publish(self, source, result):
        state = self.sources[source]
        if not isinstance(result, Recognition) or not isinstance(result.text, str) or len(result.text) > 8192:
            raise ProtocolError("decoder_error")
        label = {"source": source} if self.protocol_version == 2 else {}
        if result.final:
            duration = state.total_bytes / 32000
            tolerance = 0.1 if self.protocol_version == 1 else 0
            if not (type(result.start) in (int, float) and type(result.end) in (int, float)
                    and 0 <= state.last_end <= result.start <= result.end <= duration + tolerance
                    and math.isfinite(result.start) and math.isfinite(result.end)):
                raise ProtocolError("decoder_error")
            self.segment_id += 1
            state.last_end = result.end
            state.last_partial = None
            start, end = round(result.start, 3), round(result.end, 3)
            if self.protocol_version == 2:
                # Millisecond rounding must not extend a very short source past its PCM.
                wire_duration = (state.total_bytes // 32) / 1000
                start, end = min(start, wire_duration), min(end, wire_duration)
            await send(self.ws, {
                "type": "final", **label, "text": result.text, "segmentId": self.segment_id,
                "startSeconds": start, "endSeconds": end,
            }, self.limits)
        elif result.text != state.last_partial:
            state.last_partial = result.text
            await send(self.ws, {"type": "partial", **label, "text": result.text}, self.limits)

    async def recognize(self):
        while True:
            item = await self.queue.get()
            if item is None:
                for source, state in self.sources.items():
                    result = await self.runtime.cpu(state.decoder.finish)
                    if not isinstance(result, Recognition) or not result.final:
                        raise ProtocolError("decoder_error")
                    await self.publish(source, result)
                await send(self.ws, {"type": "stopped"}, self.limits)
                return
            source, pcm = item
            self.queued_bytes -= len(pcm)
            state = self.sources[source]
            state.queued_bytes -= len(pcm)
            state.queued_frames -= 1
            await self.publish(source, await self.runtime.cpu(state.decoder.accept, pcm))

    async def run(self):
        receiver = asyncio.create_task(self.receive())
        worker = asyncio.create_task(self.recognize())
        try:
            done, _ = await asyncio.wait((receiver, worker), return_when=asyncio.FIRST_COMPLETED)
            if worker in done:
                await worker
            elif await receiver:
                await worker
        finally:
            for task in (receiver, worker):
                if not task.done():
                    task.cancel()
            await asyncio.gather(receiver, worker, return_exceptions=True)
            while not self.queue.empty():
                self.queue.get_nowait()


async def transcribe(request):
    runtime = request.app[RUNTIME]
    headers = {"Cache-Control": "no-store", "Connection": "close"}
    if any(request.headers.getall("Origin", [])):
        return web.json_response(error("origin"), status=403, headers=headers)
    if not authorized(request, runtime):
        return web.json_response(error("auth"), status=401, headers=headers)
    if request.query_string or request.can_read_body:
        return web.json_response(error("bad_frame"), status=400, headers=headers)
    if not runtime.ready:
        return web.json_response(error("model_unavailable"), status=503, headers=headers)
    if runtime.active:
        return web.json_response(error("busy"), status=409, headers=headers)
    # No await separates admission from reservation, including the start handshake.
    runtime.active = True
    ws = web.WebSocketResponse(
        # Aiohttp's framing ceiling is exclusive. A small allowance lets us report
        # slightly oversized audio as a protocol error before the hard transport cap.
        max_msg_size=runtime.limits.frame_bytes + 1 + runtime.limits.control_bytes + 1,
        compress=False, autoping=True,
        heartbeat=10, timeout=runtime.limits.send_seconds, writer_limit=16384,
    )
    try:
        await ws.prepare(request)
        runtime.sockets.add(ws)
        message = await asyncio.wait_for(ws.receive(), runtime.limits.start_seconds)
        value = parse_control(message, runtime.limits)
        protocol_version, sources = parse_start(value)
        decoders = {source: await runtime.cpu(runtime.factory) for source in sources}
        ready = {"type": "ready", **FORMAT}
        if protocol_version == 2:
            ready.update(protocolVersion=2, sources=list(sources))
        await send(ws, ready, runtime.limits)
        await Session(runtime, ws, decoders, protocol_version).run()
    except ProtocolError as exc:
        if exc.code == "decoder_error":
            runtime.failed = True
        if ws.prepared:
            await close_with_error(ws, exc.code, runtime.limits)
        else:
            return web.json_response(error(exc.code), status=400, headers=headers)
    except TimeoutError:
        await close_with_error(ws, "timeout", runtime.limits)
    except (ConnectionError, asyncio.CancelledError):
        pass
    except Exception:
        if ws.prepared:
            await close_with_error(ws, "decoder_error", runtime.limits)
        else:
            return web.json_response(error("bad_frame"), status=400, headers=headers)
    finally:
        runtime.sockets.discard(ws)
        if ws.prepared:
            with suppress(Exception):
                await asyncio.wait_for(ws.close(), runtime.limits.send_seconds)
        runtime.active = False
    return ws


@web.middleware
async def safe_errors(request, handler):
    try:
        return await handler(request)
    except web.HTTPException as exc:
        return web.json_response(error("bad_frame"), status=exc.status)
    except Exception:
        return web.json_response(error("decoder_error"), status=500)


async def shutdown(app):
    runtime = app[RUNTIME]
    runtime.stopping = True
    await asyncio.gather(
        *(close_with_error(ws, "server_stopping", runtime.limits) for ws in tuple(runtime.sockets)),
        return_exceptions=True,
    )


async def cleanup(app):
    app[RUNTIME].executor.shutdown(wait=False, cancel_futures=True)


def create_app(token, factory, limits=None, instance_id=None, managed_stop=None):
    if not re.fullmatch(r"[A-Za-z0-9_-]{32,512}", token):
        raise ValueError("auth")
    if instance_id is not None and uuid.UUID(instance_id).hex != instance_id:
        raise ValueError("instance")
    app = web.Application(
        client_max_size=1024, middlewares=[safe_errors],
        handler_args={"max_line_size": 1024, "max_field_size": 1024, "max_headers": 32,
                      "read_bufsize": 4096, "keepalive_timeout": 5, "auto_decompress": False},
    )
    app[RUNTIME] = Runtime(token, factory, limits or Limits(), instance_id, managed_stop)
    app.router.add_get("/health", health)
    app.router.add_get("/v1/transcribe", transcribe)
    app.router.add_get("/v1/runtime", runtime_info)
    app.router.add_post("/v1/shutdown", managed_shutdown)
    app.on_shutdown.append(shutdown)
    app.on_cleanup.append(cleanup)
    return app


async def production_app(token, model_path, instance_id=None):
    def request_exit():
        raise web.GracefulExit()

    app = create_app(token, None, instance_id=instance_id,
                     managed_stop=request_exit if instance_id else None)
    runtime = app[RUNTIME]
    try:
        runtime.factory = await asyncio.get_running_loop().run_in_executor(runtime.executor, VoskFactory, model_path)
    except Exception:
        print("Local speech model unavailable; health is not ready.", flush=True)
    return app


def main():
    # Native decoder errors can contain model paths. No content-bearing diagnostics,
    # access logs, exception traces, or native stderr are retained by this service.
    logging.disable(logging.CRITICAL)
    with open(os.devnull, "w") as sink:
        os.dup2(sink.fileno(), 2)
    try:
        token = load_token()
        model_path = Path(os.environ.get("PHANTOM_TRANSCRIPTION_MODEL_PATH", ""))
        host = os.environ.get("PHANTOM_TRANSCRIPTION_HOST", "127.0.0.1")
        if host not in ("127.0.0.1", "0.0.0.0"):
            raise ValueError("host")
        if not os.environ.get("PHANTOM_TRANSCRIPTION_MODEL_PATH"):
            raise ValueError("model")
        instance_id = os.environ.get("PHANTOM_TRANSCRIPTION_INSTANCE") or None
        if instance_id is not None and uuid.UUID(instance_id).hex != instance_id:
            raise ValueError("instance")
    except (OSError, ValueError):
        print("Local speech setup is missing or invalid. Run Initialize-Transcription first.", flush=True)
        return 2
    try:
        web.run_app(
            production_app(token, model_path, instance_id), host=host, port=8765, access_log=None,
            print=None, backlog=16, shutdown_timeout=8,
        )
    except Exception:
        print("Local speech service could not start. Check setup and port availability.", flush=True)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
