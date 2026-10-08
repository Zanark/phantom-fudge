"""Validate only an owned Agency profile; never launch an engine or authenticate."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import time

root = Path(__file__).resolve().parent / ("agency-profile-" + str(time.time_ns()))
root.mkdir()
home = root / "home"
home.mkdir()
config = root / "agency.toml"
config.write_text(
    "[profiles.phantom-fudge-answer]\n"
    "[profiles.phantom-fudge-answer.mcps]\n"
    "[profiles.phantom-fudge-answer.plugins]\n"
    "default = []\n", encoding="utf-8")
allowed = {"SYSTEMROOT", "WINDIR", "SYSTEMDRIVE", "COMSPEC", "PATH", "PATHEXT", "TEMP", "TMP"}
env = {key: value for key, value in os.environ.items() if key.upper() in allowed}
env.update({
    "HOME": str(home), "USERPROFILE": str(home),
    "APPDATA": str(home / "AppData" / "Roaming"),
    "LOCALAPPDATA": str(home / "AppData" / "Local"),
    "AGENCY_NO_AUTO_UPDATE": "true", "AGENCY_NO_UPDATE_CHECK": "true",
    "AGENCY_AEC_ENABLED": "false", "COPILOT_OFFLINE": "true",
})
exe = r"C:\Users\mishrad\AppData\Roaming\agency\CurrentVersion\agency.exe"
try:
    for arguments in [
        ["--version"],
        ["--no-aec", "--verbosity", "off", "--log-dir", str(root / "logs"),
         "config", "check", "--skip-remotes", str(config)],
    ]:
        result = subprocess.run([exe, *arguments], cwd=root, env=env, stdin=subprocess.DEVNULL,
                                capture_output=True, timeout=30)
        print(json.dumps({"arguments": arguments, "exit": result.returncode,
                          "stdout": result.stdout.decode("utf-8"),
                          "stderr": result.stderr.decode("utf-8")}, ensure_ascii=True))
finally:
    shutil.rmtree(root)
