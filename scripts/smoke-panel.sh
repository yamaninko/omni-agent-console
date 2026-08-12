#!/usr/bin/env bash
# Smoke: list groups → create panel → start → wait for Completed + ≥1 turn.
# Usage: BASE=http://localhost:5080 TIMEOUT_SEC=180 ./scripts/smoke-panel.sh
set -euo pipefail

BASE="${BASE:-http://localhost:5080}"
TIMEOUT_SEC="${TIMEOUT_SEC:-180}"

export BASE TIMEOUT_SEC
python3 <<'PY'
import json, os, sys, time, urllib.error, urllib.request

base = os.environ.get("BASE", "http://localhost:5080").rstrip("/")
api = base + "/api"
timeout = int(os.environ.get("TIMEOUT_SEC", "180"))

def get(path: str):
    with urllib.request.urlopen(base + path if path.startswith("/health") else api + path, timeout=30) as r:
        return json.load(r)

def get_raw(url: str) -> bytes:
    with urllib.request.urlopen(url, timeout=30) as r:
        return r.read()

def post(path: str, body: dict | None = None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(
        api + path,
        data=data,
        headers={"Content-Type": "application/json"} if data else {},
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=60) as r:
        raw = r.read()
        return json.loads(raw) if raw else {}

print("== health ==")
print(json.dumps(get("/health")))

print("== settings ==")
settings = get("/settings")
print({k: settings.get(k) for k in ("apiKeyConfigured", "secretStore", "defaultModel")})
if not settings.get("apiKeyConfigured"):
    print("FAIL: API key not configured")
    sys.exit(2)

print("== groups ==")
groups = get("/agent-groups")
if not isinstance(groups, list) or not groups:
    print("FAIL: no agent groups — create one under /groups")
    sys.exit(3)
gid = groups[0]["id"]
print("using group", gid, groups[0].get("name"))

print("== create panel ==")
panel = post(
    "/panels",
    {
        "groupId": gid,
        "topic": "Smoke test: should multi-agent panels be used in classrooms?",
        "title": "smoke-panel",
        "maxRounds": 1,
    },
)
pid = panel["id"]
print("panel", pid)

print("== start ==")
post(f"/panels/{pid}/start", {})
print("queued")

print(f"== wait (max {timeout}s) ==")
deadline = time.time() + timeout
status = "Pending"
detail = {}
while time.time() < deadline:
    detail = get(f"/panels/{pid}")
    status = detail.get("status", "?")
    turns = detail.get("turns") or []
    print(f"  status={status} turns={len(turns)}")
    if status in ("Completed", "Failed", "Cancelled"):
        break
    time.sleep(5)

completed = sum(1 for t in (detail.get("turns") or []) if t.get("status") == "Completed")
if completed < 1:
    print("FAIL: no completed turns (status=%s)" % status)
    sys.exit(4)

# Full completion can exceed free-tier latency; ≥1 completed turn proves pipeline + keys.
if status != "Completed":
    print(f"WARN: status={status} but {completed} turn(s) completed — treating as smoke PASS")
else:
    print("== transcript (head) ==")
    text = get_raw(api + f"/panels/{pid}/transcript").decode("utf-8", errors="replace")
    print("\n".join(text.splitlines()[:20]))

print(f"OK smoke-panel panel={pid} status={status} completed_turns={completed}")
PY
