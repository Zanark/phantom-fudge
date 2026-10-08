"""Explicit offline installed-CLI protocol probe. Never contacts a real model."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

SETTINGS = {
    "disableAllHooks": True,
    "memory": False,
    "ide": {"autoConnect": False},
    "customAgents": {"defaultLocalOnly": True},
    "autoUpdate": False,
    "bashEnv": False,
    "beep": False,
    "notifications": False,
}
FLAGS = [
    "--no-auto-update", "--no-custom-instructions", "--disable-builtin-mcps",
    "--no-ask-user", "--no-remote", "--no-remote-export", "--no-experimental",
    "--no-bash-env", "--disallow-temp-dir", "--available-tools=", "--deny-tool=shell",
    "--deny-tool=write", "--deny-tool=url", "--output-format",
    "json", "--stream", "on", "--no-color", "--log-level", "none",
]


def main():
    args = argparse.ArgumentParser()
    args.add_argument("--exe", required=True)
    args.add_argument("--case", choices=["text", "tool", "error", "truncated", "redirect"], default="text")
    args.add_argument("--tools", choices=["empty", "bare", "sentinel"], default="sentinel")
    args.add_argument("--keep", action="store_true", help="Keep synthetic protocol receipts beside this helper")
    args.add_argument("--prompt-case", choices=["plain", "file", "quoted-file", "slash"], default="plain")
    args.add_argument("--home-skills", choices=["none", "parent", "child"], default="none")
    args = args.parse_args()
    root = Path(__file__).resolve().parent
    state = root / ("probe-" + args.case)
    state = state.with_name(state.name + "-" + str(time.time_ns()))
    state.mkdir(exist_ok=False)
    home = state / "home"
    home.mkdir()
    parent_home = state / "synthetic-parent-home"
    parent_home.mkdir()
    home_sentinel = "PHANTOM_SYNTHETIC_HOME_SKILL_SENTINEL_19ca"
    if args.home_skills != "none":
        seeded_home = parent_home if args.home_skills == "parent" else home
        for relative in [(".agents", "skills"), (".copilot", "skills"), ("skills",)]:
            skill_dir = seeded_home.joinpath(*relative, "synthetic-home-sentinel")
            skill_dir.mkdir(parents=True)
            (skill_dir / "SKILL.md").write_text(
                "---\nname: synthetic-home-sentinel\ndescription: " + home_sentinel + "\n---\nSynthetic probe only.",
                encoding="utf-8")
        (seeded_home / "AGENTS.md").write_text(home_sentinel, encoding="utf-8")
    (home / "config.json").write_text(json.dumps(SETTINGS), encoding="utf-8")
    work = state / "empty"
    work.mkdir()
    sentinel = state / "attachment-sentinel.txt"
    sentinel_text = "PHANTOM_SYNTHETIC_ATTACHMENT_SENTINEL_9b3821"
    sentinel.write_text(sentinel_text, encoding="utf-8")
    prompt = {
        "plain": "Give a synthetic response.",
        "file": "Give a synthetic response. @" + str(sentinel),
        "quoted-file": 'Give a synthetic response. @"' + str(sentinel) + '"',
        "slash": "/help",
    }[args.prompt_case]
    requests = []

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *unused):
            pass

        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            requests.append({
                "path": self.path, "model": body.get("model"),
                "tools_count": len(body.get("tools", [])),
                "tools": [x["function"]["name"] for x in body.get("tools", [])],
                "user_messages": [x["content"] for x in body.get("messages", []) if x["role"] == "user"],
                "authorization": "present" if self.headers.get("Authorization") else "absent",
                "redirect_target": self.server is redirect_server,
                "sentinel_transmitted": sentinel_text in json.dumps(body),
                "home_sentinel_transmitted": home_sentinel in json.dumps(body),
            })
            if args.case == "redirect" and self.server is server:
                self.send_response(307)
                self.send_header("Location", f"http://127.0.0.1:{redirect_server.server_port}/v1/chat/completions")
                self.send_header("Content-Length", "0")
                self.end_headers()
                return
            if args.case == "error" or (len(requests) > 1 and args.case != "redirect"):
                self.send_response(400)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(b'{"error":{"message":"synthetic failure","type":"invalid_request_error"}}')
                return
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.end_headers()
            pieces = [{"role": "assistant"}, {"content": "Synthetic "}, {"content": "answer \U0001f642."}]
            if args.case == "tool":
                pieces = [{"role": "assistant"}, {"tool_calls": [{"index": 0, "id": "call_synthetic", "type": "function", "function": {"name": "shell", "arguments": '{"command":"echo synthetic"}'}}]}]
            for delta in pieces:
                item = {"id": "synthetic", "object": "chat.completion.chunk", "created": 1, "model": "synthetic", "choices": [{"index": 0, "delta": delta, "finish_reason": None}]}
                self.wfile.write(("data: " + json.dumps(item) + "\n\n").encode())
                self.wfile.flush()
                time.sleep(0.15)
            if args.case == "truncated":
                self.close_connection = True
                return
            item["choices"][0] = {"index": 0, "delta": {}, "finish_reason": "tool_calls" if args.case == "tool" else "stop"}
            self.wfile.write(("data: " + json.dumps(item) + "\n\ndata: [DONE]\n\n").encode())
            self.wfile.flush()

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    redirect_server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    threading.Thread(target=redirect_server.serve_forever, daemon=True).start()
    allowed = {"SYSTEMROOT", "WINDIR", "PATH", "PATHEXT", "COMSPEC", "TEMP", "TMP", "PROCESSOR_ARCHITECTURE", "LOCALAPPDATA", "APPDATA"}
    env = {k: v for k, v in os.environ.items() if k.upper() in allowed}
    env.update({"HOME": str(parent_home), "USERPROFILE": str(parent_home)})
    env.update({
        "COPILOT_HOME": str(home), "HOME": str(home), "USERPROFILE": str(home),
        "APPDATA": str(home / "AppData" / "Roaming"),
        "LOCALAPPDATA": str(home / "AppData" / "Local"),
        "COPILOT_OFFLINE": "true", "COPILOT_PROVIDER_BASE_URL": f"http://127.0.0.1:{server.server_port}/v1",
        "COPILOT_PROVIDER_TYPE": "openai", "COPILOT_PROVIDER_WIRE_API": "completions", "COPILOT_MODEL": "synthetic",
    })
    flags = [("--available-tools" if args.tools == "bare" else "--available-tools=phantom_fudge_no_tools") if x == "--available-tools=" and args.tools != "empty" else x for x in FLAGS]
    process = subprocess.Popen([args.exe, *flags], cwd=work, env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    started = time.monotonic()
    try:
        stdout, stderr = process.communicate(prompt.encode("utf-8"), timeout=35)
    except subprocess.TimeoutExpired:
        subprocess.run(["taskkill.exe", "/PID", str(process.pid), "/T", "/F"], capture_output=True)
        stdout, stderr = process.communicate(timeout=5)
    finally:
        server.shutdown()
        redirect_server.shutdown()
    (state / "stdout.jsonl").write_bytes(stdout)
    (state / "stderr.txt").write_bytes(stderr)
    (state / "requests.json").write_text(json.dumps(requests, indent=2), encoding="utf-8")
    print(json.dumps({"exit": process.returncode, "seconds": round(time.monotonic() - started, 2), "requests": requests, "stdout": stdout.decode("utf-8"), "stderr": stderr.decode("utf-8")}, ensure_ascii=True))
    if not args.keep:
        shutil.rmtree(state)


if __name__ == "__main__":
    main()
