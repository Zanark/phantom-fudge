"""Own only this workspace's authenticated local Python service. No Docker or audio capture."""

from contextlib import contextmanager
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, ProxyHandler, Request, build_opener
import uuid

from configuration import ROOT, load_config, metadata, paths, verify_config


LOCAL = ROOT / ".local"
RECORD = LOCAL / "transcription-runtime.json"
SERVER = Path(__file__).with_name("server.py")
BASE_URL = "http://127.0.0.1:8765"


class RuntimeFailure(Exception):
    pass


class NoRedirects(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise RuntimeFailure("Local service redirected unexpectedly. No credentials were forwarded.")


def token():
    try:
        value = (LOCAL / "transcription.token").read_text(encoding="ascii").strip()
    except (OSError, UnicodeError):
        raise RuntimeFailure("Local token is missing or unreadable. Run Initialize-Transcription first.") from None
    if not re.fullmatch(r"[A-Za-z0-9_-]{32,512}", value):
        raise RuntimeFailure("The local service token is invalid. No process was changed.")
    return value


def request(path, local_token, method="GET", instance_id=None):
    headers = {"Authorization": "Bearer " + local_token}
    if instance_id is not None:
        headers["X-Phantom-Instance"] = instance_id
    req = Request(BASE_URL + path, headers=headers, method=method)
    opener = build_opener(ProxyHandler({}), NoRedirects())
    with opener.open(req, timeout=2) as response:
        payload = response.read(4097)
        if len(payload) > 4096:
            raise RuntimeFailure("Unexpected local service response. No process was changed.")
        result = json.loads(payload)
        if not isinstance(result, dict):
            raise RuntimeFailure("Unexpected local service response. No process was changed.")
        return result


def port_in_use():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.settimeout(0.3)
        return probe.connect_ex(("127.0.0.1", 8765)) == 0


@contextmanager
def lifecycle_lock():
    if os.name != "nt":
        raise RuntimeFailure("This native launcher is for Windows. The headless service itself is portable.")
    import msvcrt

    LOCAL.mkdir(parents=True, exist_ok=True)
    with (LOCAL / "transcription-runtime.lock").open("a+b") as lock:
        if lock.seek(0, os.SEEK_END) == 0:
            lock.write(b"0")
            lock.flush()
        deadline = time.monotonic() + 50
        while True:
            try:
                lock.seek(0)
                msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise RuntimeFailure("Another local startup/shutdown is still running.") from None
                time.sleep(0.1)
        try:
            yield
        finally:
            lock.seek(0)
            msvcrt.locking(lock.fileno(), msvcrt.LK_UNLCK, 1)


def read_record():
    if not RECORD.exists():
        return None
    try:
        data = json.loads(RECORD.read_text(encoding="utf-8"))
        instance = data["instanceId"]
        if data["schemaVersion"] != 1 or not isinstance(instance, str) or uuid.UUID(instance).hex != instance:
            raise ValueError()
        return data
    except (OSError, UnicodeError, ValueError, KeyError, TypeError):
        raise RuntimeFailure("The local runtime receipt is invalid. No process was changed.") from None


def write_record(data):
    temporary = RECORD.with_suffix(".tmp")
    temporary.write_text(json.dumps(data, indent=2), encoding="utf-8")
    temporary.replace(RECORD)


def process_identity(pid):
    """Inspect creation time, never signal a PID that may have been recycled."""
    import ctypes
    from ctypes import wintypes

    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.OpenProcess.argtypes = (wintypes.DWORD, wintypes.BOOL, wintypes.DWORD)
    kernel.OpenProcess.restype = wintypes.HANDLE
    kernel.GetProcessTimes.argtypes = (wintypes.HANDLE,) + (ctypes.POINTER(wintypes.FILETIME),) * 4
    kernel.GetProcessTimes.restype = wintypes.BOOL
    kernel.CloseHandle.argtypes = (wintypes.HANDLE,)
    kernel.CloseHandle.restype = wintypes.BOOL
    handle = kernel.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
    if not handle:
        if ctypes.get_last_error() == 87:  # A terminated/nonexistent process.
            return None
        raise RuntimeFailure("The recorded process cannot be inspected. No process or receipt was changed.")
    try:
        created, exited, system, user = (wintypes.FILETIME() for _ in range(4))
        if not kernel.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(exited),
                                      ctypes.byref(system), ctypes.byref(user)):
            raise RuntimeFailure("The recorded process identity is unavailable. No process was changed.")
        if exited.dwHighDateTime or exited.dwLowDateTime:
            return None
        return str((created.dwHighDateTime << 32) | created.dwLowDateTime)
    finally:
        kernel.CloseHandle(handle)


def clear_dead_record(record):
    pid, created = record.get("pid"), record.get("processCreatedAt")
    if (type(pid) is not int or not 1 <= pid <= 0xFFFFFFFF
            or (created is not None and (
                not isinstance(created, str) or not re.fullmatch(r"[1-9][0-9]{0,19}", created)))):
        raise RuntimeFailure("An incomplete or legacy service receipt remains. No second process was started; inspect the retained runtime receipt.")
    current = process_identity(pid)
    if current is not None and (created is None or current == created):
        raise RuntimeFailure("The recorded service process is still alive but not listening. Its receipt was retained.")
    # A missing PID or a different creation time proves the recorded process is gone.
    RECORD.unlink()


def owned_info(local_token, record):
    if record is None:
        raise RuntimeFailure("Port 8765 is owned by an untracked service. Stop it with its own launcher first.")
    try:
        info = request("/v1/runtime", local_token)
    except (HTTPError, URLError, OSError, ValueError):
        raise RuntimeFailure("The existing service could not be authenticated. No process was changed.") from None
    if (info.get("service") != "phantom-fudge-transcription" or info.get("protocolVersion") != 1
            or info.get("instanceId") != record["instanceId"]):
        raise RuntimeFailure("Port 8765 belongs to a different service instance. No process was changed.")
    return info


def supports_current_audio(info):
    versions = info.get("speechProtocolVersions")
    return isinstance(versions, list) and any(type(version) is int and version == 2 for version in versions)


def supports_current_engine(info, config):
    return info.get("configFingerprint") == metadata(config)["configFingerprint"]


def validate_start(config):
    try:
        verify_config(config)
        if config["engine"] == "hybrid-whisper":
            import faster_whisper
    except (ImportError, OSError, ValueError):
        raise RuntimeFailure("Selected local ASR is not provisioned. Run Initialize-Transcription.ps1 -DownloadModel; no fallback or service shutdown occurred.") from None


def stop_owned(local_token, record, info):
    if info.get("active") is not False:
        raise RuntimeFailure("Transcription may be active. Click Stop in the app before updating/stopping the local service.")
    try:
        response = request("/v1/shutdown", local_token, method="POST", instance_id=record["instanceId"])
    except (HTTPError, URLError, OSError, ValueError):
        raise RuntimeFailure("The owned service did not accept shutdown. No OS process was terminated.") from None
    if response.get("status") != "stopping":
        raise RuntimeFailure("The owned service did not confirm shutdown.")
    deadline = time.monotonic() + 12
    while port_in_use() and time.monotonic() < deadline:
        time.sleep(0.1)
    if port_in_use():
        raise RuntimeFailure("The owned service has not stopped yet; the runtime receipt was retained.")
    RECORD.unlink()


def start():
    local_token = token()
    with lifecycle_lock():
        config = load_config()
        record = read_record()
        if port_in_use():
            info = owned_info(local_token, record)
            if info.get("ready") is not True:
                raise RuntimeFailure("The owned service is not ready. Stop it, check setup, then retry.")
            if supports_current_audio(info) and supports_current_engine(info, config):
                print("Local Python transcription is already ready for computer and microphone input. Docker is not required.")
                return
            if info.get("active") is not False:
                raise RuntimeFailure("The owned service needs an ASR update but transcription may be active. Stop capture in the app first; no service was changed.")
            # Provisioning failures must not take a healthy old idle instance down.
            validate_start(config)
            stop_owned(local_token, record, info)
            print("Restarting the owned idle service for the selected ASR configuration.")
        elif record is not None:
            clear_dead_record(record)

        validate_start(config)
        model = paths(config)[0]
        instance = uuid.uuid4().hex
        record = {"schemaVersion": 1, "instanceId": instance, "pid": None}
        write_record(record)
        env = os.environ.copy()
        env.update({
            "PHANTOM_TRANSCRIPTION_TOKEN_FILE": str(LOCAL / "transcription.token"),
            "PHANTOM_TRANSCRIPTION_MODEL_PATH": str(model),
            "PHANTOM_TRANSCRIPTION_HOST": "127.0.0.1",
            "PHANTOM_TRANSCRIPTION_INSTANCE": instance,
            "PYTHONDONTWRITEBYTECODE": "1",
            "OPENBLAS_NUM_THREADS": "1",
            "OMP_NUM_THREADS": "1",
            "HF_HUB_OFFLINE": "1",
            "TRANSFORMERS_OFFLINE": "1",
            "HF_HUB_DISABLE_TELEMETRY": "1",
            "HF_HUB_DISABLE_IMPLICIT_TOKEN": "1",
        })
        with (LOCAL / "transcription-service.log").open("wb") as log:
            process = subprocess.Popen(
                [sys.executable, "-B", "-X", "utf8", str(SERVER)], cwd=ROOT, env=env, stdin=subprocess.DEVNULL,
                stdout=log, stderr=log,
                creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP,
                close_fds=True,
            )
        record["pid"] = process.pid
        write_record(record)
        record["processCreatedAt"] = process_identity(process.pid)
        write_record(record)
        deadline = time.monotonic() + 45
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeFailure("Local Python service exited during startup. Check setup and the local generic log.")
            if port_in_use():
                info = owned_info(local_token, record)
                if info.get("pid") != process.pid:
                    raise RuntimeFailure("The service process identity did not match the launched child.")
                write_record(record)
                if info.get("ready") is not True:
                    raise RuntimeFailure("Local model did not load. Use Stop-Transcription, repair setup and restart.")
                if not supports_current_audio(info):
                    raise RuntimeFailure("The service does not support the installed client audio protocol. Update the service files.")
                if not supports_current_engine(info, config):
                    raise RuntimeFailure("The service did not load the selected ASR configuration. No fallback is accepted.")
                print("Local Python transcription is ready at 127.0.0.1:8765. Start audio explicitly in the app.")
                return
            time.sleep(0.15)
        # Preserve the instance receipt: a slow/hung native initializer is not a successful startup.
        raise RuntimeFailure("Local startup exceeded 45 seconds. No ready service is claimed; retain the runtime receipt for cleanup.")


def stop():
    local_token = token()
    with lifecycle_lock():
        record = read_record()
        if not port_in_use():
            if record is not None:
                clear_dead_record(record)
            print("No managed local Python service is running.")
            return
        info = owned_info(local_token, record)
        stop_owned(local_token, record, info)
        print("Owned local Python transcription service stopped.")


def status():
    with lifecycle_lock():
        if not port_in_use():
            print("Local Python transcription is not listening.")
            return
        info = owned_info(token(), read_record())
        print(json.dumps({key: info.get(key) for key in (
            "ready", "active", "pid", "engine", "model", "configFingerprint",
            "diagnosticsAvailable", "faultCount", "dropCount")}))


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in ("start", "stop", "status"):
        print("Usage: manage.py start|stop|status", file=sys.stderr)
        return 64
    try:
        {"start": start, "stop": stop, "status": status}[sys.argv[1]]()
    except RuntimeFailure as error:
        print(str(error), file=sys.stderr)
        return 1
    except (OSError, ValueError):
        print("Local runtime operation failed. Check setup and workspace access; no unrelated process was changed.",
              file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
