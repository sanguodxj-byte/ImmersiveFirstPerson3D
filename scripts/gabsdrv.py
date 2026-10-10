import urllib.request, json, time, sys

MCP = "http://localhost:8090/mcp"

def call(tool, args=None, timeout=120):
    req = urllib.request.Request(MCP)
    req.add_header('Content-Type', 'application/json')
    payload = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                          "params": {"name": tool, "arguments": args or {}}}).encode()
    with urllib.request.urlopen(req, payload, timeout=timeout) as r:
        d = json.loads(r.read())
    if "error" in d:
        raise RuntimeError(d["error"].get("message", str(d["error"])))
    texts = []
    for c in d.get("result", {}).get("content", []):
        if c.get("type") == "text":
            try:
                texts.append(json.loads(c["text"]))
            except Exception:
                texts.append(c["text"])
    return texts[0] if len(texts) == 1 else texts

def wait_bridge(deadline_s=240):
    t0 = time.time()
    while time.time() - t0 < deadline_s:
        try:
            r = call("games_status", {"gameId": "rimworld"}, timeout=30)
            st = r.get("status") if isinstance(r, dict) else None
            if st == "running":
                return True
        except Exception:
            pass
        time.sleep(6)
    return False

if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "wait"
    if cmd == "wait":
        print("bridge ready:", wait_bridge())
    elif cmd == "call":
        tool = sys.argv[2]
        args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
        out = call(tool, args, timeout=240)
        print(json.dumps(out, ensure_ascii=False)[:4000])
