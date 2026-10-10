import urllib.request, json, time, sys

MCP = "http://localhost:8090/mcp"

def call(tool, args=None, timeout=150):
    req = urllib.request.Request(MCP)
    req.add_header('Content-Type', 'application/json')
    payload = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                          "params": {"name": tool, "arguments": args or {}}}).encode()
    with urllib.request.urlopen(req, payload, timeout=timeout) as r:
        d = json.loads(r.read())
    if "error" in d:
        raise RuntimeError(json.dumps(d["error"], ensure_ascii=False)[:300])
    texts = []
    for c in d.get("result", {}).get("content", []):
        if c.get("type") == "text":
            try:
                texts.append(json.loads(c["text"]))
            except Exception:
                texts.append(c["text"])
    return texts[0] if len(texts) == 1 else texts

def gtool(name, args=None, timeout=150):
    return call("games_call_tool", {"gameId": "rimworld", "tool": name, "arguments": args or {}}, timeout=timeout)

def step(label, fn, retries=2, backoff=8):
    for attempt in range(retries + 1):
        try:
            r = fn()
            print(f"[OK] {label}: {json.dumps(r, ensure_ascii=False)[:300]}", flush=True)
            return r
        except Exception as e:
            print(f"[..] {label} attempt {attempt+1} failed: {str(e)[:200]}", flush=True)
            if attempt >= retries:
                print(f"[FAIL] {label}", flush=True)
                return None
            time.sleep(backoff)
    return None

def main():
    # 1. bridge up
    t0 = time.time()
    connected = False
    while time.time() - t0 < 300:
        try:
            r = call("games_status", {"gameId": "rimworld"}, timeout=25)
            st = r.get("status") if isinstance(r, dict) else None
            tools = r.get("toolCount", 0) if isinstance(r, dict) else 0
            if st == "running" and tools and tools > 0:
                connected = True
                break
        except Exception:
            pass
        time.sleep(8)
    print("bridge connected:", connected, flush=True)
    if not connected:
        return
    time.sleep(3)

    # 2. start debug game (fresh map)
    r = step("start_debug_game", lambda: gtool("rimworld_rimworld_start_debug_game", {}, 150))
    if r is None:
        return
    # 3. wait ready
    step("game_ready", lambda: gtool("rimworld_rimworld_start_debug_game_ready", {}, 240))

    # 4. toggle first person
    step("fp_on", lambda: gtool("rimworld_rimworld_execute_debug_action", {"path": "FirstPerson3D/mode: first person"}))
    time.sleep(6)

    # 5. screenshots
    step("shot1", lambda: gtool("rimworld_rimworld_take_screenshot", {}))

    # 6. perf
    step("perf", lambda: gtool("rimworld_rimworld_execute_debug_action", {"path": "FirstPerson3D/dump perf"}))
    time.sleep(2)
    step("logs", lambda: gtool("rimworld_rimbridge_list_logs", {"level": "message", "tail": 30}))

if __name__ == "__main__":
    main()
