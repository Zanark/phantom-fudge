"""Bounded ACP qualification. Offline by default; cloud requires explicit consent.

Only fixed synthetic public questions are accepted. Receipts omit system prompts,
raw protocol/session identifiers, paths, stderr and credentials.
"""
import argparse
import json
import os
from pathlib import Path
import queue
import re
import shutil
import subprocess
import threading
import time
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

QUESTIONS = [
    "Write me the solution for the leet code problem two sum",
    "okay so right mean the solution for the lead good problem to some",
]
FLAGS = [
    "--no-auto-update", "--no-custom-instructions", "--disable-builtin-mcps",
    "--no-ask-user", "--no-remote", "--no-remote-export", "--no-experimental",
    "--no-bash-env", "--disallow-temp-dir", "--available-tools=phantom_fudge_no_tools",
    "--deny-tool=shell", "--deny-tool=write", "--deny-tool=read", "--deny-tool=url",
    "--no-color", "--log-level", "none", "--no-eager-powershell-resolution",
    "--acp", "--model", "gpt-5.6-sol-fast", "--reasoning-effort", "low",
]
AUTH = ("opening your browser", "waiting for authorization", "device code",
        "authentication is required", "please sign in", "interactionrequired")


def answer_policy():
    source = (Path(__file__).resolve().parents[2] / "src" / "PhantomFudge.Core" /
              "Answers" / "AgencyAnswerPrompt.cs").read_text(encoding="utf-8")
    policy = re.search(r'Policy = """\n(.*?)\n        """;', source, re.S).group(1)
    policy = "\n".join(line[8:] for line in policy.splitlines())
    return policy


def envelope(question):
    return answer_policy() + "\n\n" + json.dumps({"userQuestion": question}, ensure_ascii=True).replace("@", "\\u0040")


def plain_content(content):
    return content if isinstance(content, str) else "".join(block.get("text", "") for block in content)


class Client:
    def __init__(self, command, work, env):
        self.start = time.monotonic()
        self.process = subprocess.Popen(command, cwd=work, env=env, stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                        creationflags=subprocess.CREATE_NO_WINDOW)
        self.queue = queue.Queue()
        self.total = 0
        self.serial = 0
        self.stages = {"launchMs": self.elapsed()}
        self.types = set()
        self.threads = []
        for stream, channel in ((self.process.stdout, "stdout"), (self.process.stderr, "stderr")):
            thread = threading.Thread(target=self.pump, args=(stream, channel), daemon=True)
            thread.start()
            self.threads.append(thread)

    def elapsed(self):
        return round((time.monotonic() - self.start) * 1000, 1)

    def pump(self, stream, channel):
        while data := stream.readline(262145):
            self.queue.put((channel, data))
        self.queue.put((channel, None))

    def call(self, method, params):
        self.serial += 1
        self.process.stdin.write((json.dumps({"jsonrpc": "2.0", "id": self.serial,
                                             "method": method, "params": params}) + "\n").encode())
        self.process.stdin.flush()
        output = []
        first = None
        started = time.monotonic()
        while time.monotonic() - started < 120:
            try:
                channel, data = self.queue.get(timeout=0.25)
            except queue.Empty:
                continue
            if data is None:
                if channel == "stdout":
                    raise RuntimeError("unexpected_eof")
                continue
            self.total += len(data)
            if len(data) > 262144 or self.total > 2097152:
                raise RuntimeError("output_limit")
            text = data.decode("utf-8").strip()
            if channel == "stderr" or not text.startswith("{"):
                if any(marker in text.lower() for marker in AUTH):
                    raise RuntimeError("authentication_required")
                if channel == "stdout" and text and not (
                        text == "📦 Resolving Copilot CLI..." or
                        re.fullmatch(r"✅ Copilot CLI resolved \(in \d{1,6} ms\)", text)):
                    raise RuntimeError("unexpected_notice")
                continue
            record = json.loads(text)
            if record.get("id") == self.serial and "method" not in record:
                if "error" in record:
                    raise RuntimeError("rpc_error_" + str(record["error"].get("code")))
                return record["result"], "".join(output), first, round((time.monotonic() - started) * 1000, 1)
            if record.get("method") != "session/update":
                raise RuntimeError("unexpected_rpc_or_tool")
            params = record["params"]
            update = params["update"]
            kind = update["sessionUpdate"]
            self.types.add(kind)
            if kind.startswith("tool") or kind == "plan":
                raise RuntimeError("tool_or_plan")
            if kind == "agent_message_chunk":
                content = update["content"]
                if content["type"] != "text":
                    raise RuntimeError("nontext_content")
                if content["text"].startswith(("Info: Disabled tools: ", 'Info: Unknown tool name in the tool allowlist: "phantom_fudge_no_tools"')):
                    continue
                if content["text"] and first is None:
                    first = round((time.monotonic() - started) * 1000, 1)
                output.append(content["text"])
        raise RuntimeError("timeout")

    def close(self):
        if self.process.poll() is None:
            subprocess.run(["taskkill.exe", "/PID", str(self.process.pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        self.process.wait(timeout=10)
        for thread in self.threads:
            thread.join(timeout=5)
        for stream in (self.process.stdin, self.process.stdout, self.process.stderr):
            stream.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--offline-engine")
    parser.add_argument("--allow-cloud", action="store_true")
    parser.add_argument("--handshake-only", action="store_true")
    parser.add_argument("--controlled-agent", action="store_true")
    parser.add_argument("--catalog-only", action="store_true")
    parser.add_argument("--model", choices=("gpt-5.4-mini", "claude-haiku-5.5", "gpt-5.6-sol-fast"), default="gpt-5.6-sol-fast")
    parser.add_argument("--questions", type=int, choices=range(1, len(QUESTIONS) + 1), default=1)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    if not args.offline_engine and not args.allow_cloud:
        raise SystemExit("Choose --offline-engine or explicitly consent with --allow-cloud.")
    root = Path(__file__).resolve().parents[2] / ".local" / ("acp-probe-" + uuid.uuid4().hex)
    work, home, profile = root / "work", root / "home", root / "profile"
    for path in (work, home, profile):
        path.mkdir(parents=True)
    (work / "agency.toml").write_text(
        "[profiles.phantom-fudge-answer]\n[profiles.phantom-fudge-answer.mcps]\n"
        "[profiles.phantom-fudge-answer.plugins]\ndefault = []\n", encoding="utf-8")
    (profile / "settings.json").write_text(json.dumps({
        "disableAllHooks": True, "memory": False, "ide": {"autoConnect": False},
        "customAgents": {"defaultLocalOnly": True}, "autoUpdate": False,
        "bashEnv": False, "beep": False, "notifications": False, "experimental": False,
    }), encoding="utf-8")
    expected_settings = json.loads((profile / "settings.json").read_text(encoding="utf-8"))
    if args.controlled_agent:
        agents = profile / "agents"
        agents.mkdir()
        (agents / "phantom-fudge-answer.agent.md").write_text(
            "---\nname: phantom-fudge-answer\ndescription: Direct standalone technical answers without tools.\n"
            "tools: []\n---\n" + answer_policy() + "\n", encoding="utf-8")
    keep = {"SYSTEMROOT", "WINDIR", "SYSTEMDRIVE", "COMSPEC", "PATHEXT", "TEMP", "TMP",
            "APPDATA", "LOCALAPPDATA", "PROCESSOR_ARCHITECTURE", "NUMBER_OF_PROCESSORS", "PATH"}
    env = {key: value for key, value in os.environ.items() if key.upper() in keep}
    env.update(HOME=str(home), USERPROFILE=str(home), COPILOT_HOME=str(profile),
               COPILOT_AUTO_UPDATE="false", OTEL_SDK_DISABLED="true", NO_COLOR="1")
    observed = []

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *unused):
            pass

        def do_POST(self):
            body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
            messages = [x["content"] for x in body.get("messages", []) if x["role"] == "user"]
            messages = [plain_content(value) for value in messages]
            observed.append({"toolsCount": len(body.get("tools", [])),
                             "userMessageCount": len(messages),
                             "assistantMessageCount": sum(message["role"] == "assistant" for message in body.get("messages", [])),
                             "questionEnvelopeCount": sum(any(envelope(question) in value for value in messages) for question in QUESTIONS),
                             "model": body.get("model"),
                             "fixedPolicyInSystemRole": any(answer_policy() in plain_content(message.get("content", ""))
                                 for message in body.get("messages", []) if message["role"] == "system"),
                             "questionExact": any(envelope(q) in x for q in QUESTIONS for x in messages)})
            self.send_response(200)
            self.send_header("Content-Type", "text/event-stream")
            self.end_headers()
            for delta, finish in (({"role": "assistant"}, None), ({"content": "Synthetic answer."}, None), ({}, "stop")):
                item = {"id": "synthetic", "object": "chat.completion.chunk", "created": 1, "model": args.model,
                        "choices": [{"index": 0, "delta": delta, "finish_reason": finish}]}
                self.wfile.write(("data: " + json.dumps(item) + "\n\n").encode())
                self.wfile.flush()
            self.wfile.write(b"data: [DONE]\n\n")
            self.wfile.flush()

    server = None
    if args.offline_engine:
        server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=server.serve_forever, daemon=True).start()
        env.update(APPDATA=str(home / "AppData" / "Roaming"), LOCALAPPDATA=str(home / "AppData" / "Local"),
                   COPILOT_OFFLINE="true", COPILOT_PROVIDER_BASE_URL=f"http://127.0.0.1:{server.server_port}/v1",
                   COPILOT_PROVIDER_TYPE="openai", COPILOT_PROVIDER_WIRE_API="completions")
        command = [args.offline_engine, *FLAGS]
    else:
        command = [str(Path(os.environ["APPDATA"]) / "agency" / "CurrentVersion" / "agency.exe"),
                   "--verbosity", "error", "--log-dir", str(root / "logs"), "copilot",
                   "--profile-only", "phantom-fudge-answer", "--no-default-mcps",
                   "--no-config-plugins", "--no-aec", "--no-input-processing", "--", *FLAGS]
    if args.controlled_agent:
        command.extend(["--agent", "phantom-fudge-answer"])
    command[command.index("--model") + 1] = args.model
    receipt = {"mode": "offline_mock" if server else "agency_cloud", "model": args.model,
               "reasoningEffort": "low", "modelAttempts": 0, "calls": [], "failure": None}
    client = None
    try:
        client = Client(command, work, env)
        init, _, _, _ = client.call("initialize", {"protocolVersion": 1, "clientCapabilities": {}})
        receipt["protocolVersion"] = init.get("protocolVersion")
        receipt["agentVersion"] = init.get("agentInfo", {}).get("version")
        receipt["capabilities"] = init.get("agentCapabilities")
        receipt["engineReadyMs"] = client.elapsed()
        receipt["ownedProfileCheck"] = {
            "settingsSemanticallyUnchanged": expected_settings == json.loads((profile / "settings.json").read_text(encoding="utf-8")),
            "settingsKeys": sorted(json.loads((profile / "settings.json").read_text(encoding="utf-8"))),
            "nonemptyCustomizationCategories": [
                f"{category}/{name}" for category, path in (
                    ("profile", profile), ("home-copilot", home / ".copilot"),
                    ("home-claude", home / ".claude"), ("home-agents", home / ".agents"),
                    ("work-github", work / ".github"), ("work-agents", work / ".agents"))
                for name in ("agents", "skills", "hooks", "installed-plugins", "plugins")
                if (path / name).is_dir() and any((path / name).iterdir())]}
        ids = set()
        for index in range(0 if args.handshake_only else 1 if args.catalog_only else args.questions):
            session, _, _, session_ms = client.call("session/new", {"cwd": str(work), "mcpServers": []})
            sid = session["sessionId"]
            if sid in ids:
                raise RuntimeError("reused_session")
            ids.add(sid)
            if args.controlled_agent:
                agent_option = next((option for option in session.get("configOptions", []) if option["id"] == "agent"), {})
                if not any(value.get("value") == "phantom-fudge-answer" for value in agent_option.get("options", [])):
                    raise RuntimeError("controlled_agent_not_advertised")
                configured, _, _, _ = client.call("session/set_config_option", {
                    "sessionId": sid, "configId": "agent", "value": "phantom-fudge-answer"})
                session["configOptions"] = configured["configOptions"]
            receipt["selectedConfiguration"] = {
                option["id"]: option.get("currentValue") for option in session.get("configOptions", [])}
            receipt["selectedModelAdvertised"] = any(
                option.get("id") == "model" and any(value.get("value") == args.model for value in option.get("options", []))
                for option in session.get("configOptions", []))
            receipt["candidateModelsAdvertised"] = {
                model: any(option.get("id") == "model" and any(value.get("value") == model for value in option.get("options", []))
                           for option in session.get("configOptions", []))
                for model in ("gpt-5.4-mini", "claude-haiku-5.5", "gpt-5.6-sol-fast")}
            receipt["reasoningEffortsAdvertised"] = [
                value["value"] for option in session.get("configOptions", []) if option.get("id") == "reasoning_effort"
                for value in option.get("options", []) if value.get("value") in ("none", "minimal", "low", "medium", "high", "xhigh", "max")]
            if server and len(observed) != index:
                raise RuntimeError("model_request_during_session_preparation")
            if args.catalog_only:
                client.call("session/close", {"sessionId": sid})
                continue
            receipt["modelAttempts"] += 1
            terminal, answer, first, complete = client.call("session/prompt", {
                "sessionId": sid, "prompt": [{"type": "text", "text": envelope(QUESTIONS[index])}]})
            receipt["calls"].append({"questionIndex": index, "sessionNewMs": session_ms,
                                     "firstTextAfterPromptMs": first, "completionAfterPromptMs": complete,
                                     "totalFromProcessLaunchMs": client.elapsed(),
                                     "stopReason": terminal.get("stopReason"), "answer": answer})
            if terminal.get("stopReason") != "end_turn" or not answer:
                raise RuntimeError("incomplete_answer")
            client.call("session/close", {"sessionId": sid})
        receipt["updateTypes"] = sorted(client.types)
        if server and not args.handshake_only and not args.catalog_only and (
                len(observed) != args.questions or any(
                    entry["toolsCount"] != 0 or entry["userMessageCount"] != 1 or
                    entry["assistantMessageCount"] != 0 or entry["questionEnvelopeCount"] != 1 or
                    not entry["questionExact"] or (args.controlled_agent and not entry["fixedPolicyInSystemRole"])
                    for entry in observed)):
            raise RuntimeError("offline_wire_violation")
    except Exception as error:
        receipt["failure"] = str(error) if isinstance(error, RuntimeError) else type(error).__name__
    finally:
        if client:
            client.close()
        if server:
            server.shutdown()
        for retry in range(6):
            try:
                shutil.rmtree(root)
                break
            except OSError:
                if retry == 5:
                    receipt["cleanupFailure"] = "owned_directory_busy"
                else:
                    time.sleep(1)
    receipt["offlineRequests"] = observed
    destination = Path(args.output)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(receipt, indent=2), encoding="utf-8")
    print(json.dumps(receipt, indent=2))
    return 1 if receipt["failure"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
