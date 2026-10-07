import json
import sys
import urllib.request


try:
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    with opener.open("http://127.0.0.1:8765/health", timeout=2) as response:
        healthy = response.status == 200 and json.load(response) == {"status": "ready", "modelAvailable": True}
except Exception:
    healthy = False
sys.exit(0 if healthy else 1)
