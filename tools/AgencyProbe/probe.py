"""One explicitly authorized synthetic Agency prompt; never accepts meeting text."""
import argparse
import json
import os
from pathlib import Path
import queue
import subprocess
import threading
import time
import uuid


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--allow-cloud", action="store_true")
    args = parser.parse_args()
    if not args.allow_cloud:
        raise SystemExit("The fixed synthetic prompt requires --allow-cloud.")
    exe = Path(os.environ["APPDATA"]) / "agency" / "CurrentVersion" / "agency.exe"
    root = Path(os.environ["LOCALAPPDATA"]) / "PhantomFudge" / "agency-probe" / uuid.uuid4().hex
    work, home, profile = root / "work", root / "home", root / "profile"
    for path in (work, home, profile):
        path.mkdir(parents=True)
    (work / "agency.toml").write_text(
        "[profiles.phantom-fudge-answer]\n[profiles.phantom-fudge-answer.mcps]\n"
        "[profiles.phantom-fudge-answer.plugins]\ndefault = []\n", encoding="utf-8")
    (profile / "settings.json").write_text(json.dumps({
        "disableAllHooks": True, "memory": False, "ide": {"autoConnect": False},
        "customAgents": {"defaultLocalOnly": True}, "autoUpdate": False,
        "bashEnv": False, "beep": False, "notifications": False, "experimental": False
    }), encoding="utf-8")
    keep = {"SYSTEMROOT", "WINDIR", "SYSTEMDRIVE", "COMSPEC", "PATHEXT", "TEMP", "TMP",
            "APPDATA", "LOCALAPPDATA", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS"}
    env = {key: value for key, value in os.environ.items() if key.upper() in keep}
    env.update(PATH=os.pathsep.join(p for p in os.environ.get("PATH", "").split(os.pathsep) if Path(p).is_absolute()),
               HOME=str(home), USERPROFILE=str(home), COPILOT_HOME=str(profile),
               COPILOT_AUTO_UPDATE="false", OTEL_SDK_DISABLED="true", NO_COLOR="1")
    command = [str(exe), "--verbosity", "error", "--log-dir", str(root / "logs"), "copilot",
               "--profile-only", "phantom-fudge-answer", "--no-default-mcps", "--no-config-plugins",
               "--no-aec", "--no-input-processing", "--",
               "--no-auto-update", "--no-custom-instructions", "--disable-builtin-mcps",
               "--no-ask-user", "--no-remote", "--no-remote-export", "--no-experimental",
               "--no-bash-env", "--disallow-temp-dir", "--available-tools=phantom_fudge_no_tools",
               "--deny-tool=shell", "--deny-tool=write", "--deny-tool=read", "--deny-tool=url",
               "--output-format", "json", "--stream", "on", "--no-color", "--log-level", "none"]
    process = subprocess.Popen(command, cwd=work, env=env, stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               creationflags=subprocess.CREATE_NO_WINDOW)
    messages = queue.Queue()

    def pump(stream, channel):
        while True:
            data = stream.readline(262145)
            if not data:
                break
            messages.put((channel, data))
        messages.put((channel, None))

    threads = [threading.Thread(target=pump, args=(process.stdout, "stdout"), daemon=True),
               threading.Thread(target=pump, args=(process.stderr, "stderr"), daemon=True)]
    for thread in threads:
        thread.start()
    process.stdin.write(b"Reply with exactly PHANTOM_OK and no other text.")
    process.stdin.close()
    records, types, chunks, notices = [], [], [], []
    finished, total, failure = 0, 0, None
    start = time.monotonic()
    try:
        while finished < 2:
            if time.monotonic() - start > 120:
                failure = "timeout"
                break
            try:
                channel, data = messages.get(timeout=0.25)
            except queue.Empty:
                continue
            if data is None:
                finished += 1
                continue
            total += len(data)
            if total > 2097152 or len(data) > 262144:
                failure = "output_limit"
                break
            text = data.decode("utf-8", errors="replace").strip()
            if any(marker in text.lower() for marker in (
                    "opening your browser", "waiting for authorization", "device code",
                    "authentication is required", "please sign in", "interactionrequired")):
                failure = "authentication_required"
                break
            if channel == "stdout" and text.startswith("{"):
                event = json.loads(text)
                records.append(event)
                kind = event.get("type", "")
                types.append(kind)
                if kind.startswith("tool.") or "tool_call" in kind:
                    failure = "tool_attempt"
                    break
                if kind == "assistant.message_delta":
                    chunks.append(event.get("data", {}).get("deltaContent", ""))
                if kind == "session.mcp_servers_loaded":
                    for server in event.get("data", {}).get("servers", []):
                        if server.get("status") != "disabled":
                            failure = "active_mcp"
                if kind == "session.skills_loaded":
                    if any(skill.get("source") != "builtin" for skill in event.get("data", {}).get("skills", [])):
                        failure = "custom_skills"
                if failure:
                    break
            elif text:
                notices.append(text)
        if failure:
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        process.wait(timeout=10)
    finally:
        if process.poll() is None:
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        for stream in (process.stdout, process.stderr):
            stream.close()
    output = "".join(chunks)
    result = {"failure": failure, "exitCode": process.returncode, "eventTypes": types,
              "outputMatches": output.strip() == "PHANTOM_OK", "outputCharacters": len(output),
              "seconds": time.monotonic() - start, "receiptDirectory": str(root)}
    # All model input is the fixed public-safe marker; keep raw probe events local, not in the repo.
    (root / "events.json").write_text(json.dumps(records, indent=2), encoding="utf-8")
    (root / "notices.json").write_text(json.dumps(notices, indent=2), encoding="utf-8")
    (root / "result.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0 if not failure and result["outputMatches"] and process.returncode == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
