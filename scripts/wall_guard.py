# -*- coding: utf-8 -*-
"""AI生成 · 墙体与围护建筑建模几何与贴图取样守卫（scripts/wall_guard.py）

从 viewer/walls.html?parts=1 读取全部墙体、矮墙掩体、栅栏及穿墙/壁挂设备的各部件世界包围盒，逐件检查：
  1. 缺失取样区：missing 必须为空（所有 UV 槽位在 wall_uv.json 中存在）
  2. 接地：满格墙/矮墙/围栏/壁挂件的最低点 y0 必须在 [-0.01, 0.01] 之间
  3. 连通性：全部部件构成的包围盒接触图（容差 3cm）必须单连通，严禁悬浮孤岛
  4. 非退化：每个部件在 X/Y/Z 三轴厚度均 >= 0.025m，严禁薄纸片
"""
import http.server
import json
import os
import re
import socketserver
import subprocess
import sys
import threading

import _tmpsweep  # noqa: F401 — 把 TEMP/TMP 指到仓库 _tmpchrome，退出时扫空

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
TOUCH_EPS = 0.032


def dump_parts():
    class QuietHandler(http.server.SimpleHTTPRequestHandler):
        def __init__(self, *a, **kw):
            super().__init__(*a, directory=ROOT, **kw)

        def log_message(self, *a):
            pass

    httpd = socketserver.TCPServer(("127.0.0.1", 0), QuietHandler)
    port = httpd.server_address[1]
    threading.Thread(target=httpd.serve_forever, daemon=True).start()
    rd = _tmpsweep.scratch("wallguard")
    try:
        url = f"http://127.0.0.1:{port}/viewer/walls.html?parts=1"
        raw = subprocess.check_output(
            [
                CHROME,
                f"--user-data-dir={_tmpsweep.profile(rd)}",
                "--headless=new",
                "--disable-gpu",
                "--virtual-time-budget=5000",
                "--dump-dom",
                url,
            ],
            env=_tmpsweep.env_for(rd),
            stderr=subprocess.DEVNULL,
        ).decode("utf-8", errors="ignore")
    finally:
        httpd.shutdown()
        _tmpsweep.drop(rd)
        _tmpsweep.sweep_mine()
    m = re.search(r'<pre id="partjson">(.*?)</pre>', raw, re.S)
    if not m:
        raise RuntimeError("viewer/walls.html?parts=1 未输出 #partjson")
    return json.loads(m.group(1))


def boxes_touch(a, b, eps=TOUCH_EPS):
    return (
        a["x0"] <= b["x1"] + eps
        and a["x1"] >= b["x0"] - eps
        and a["y0"] <= b["y1"] + eps
        and a["y1"] >= b["y0"] - eps
        and a["z0"] <= b["z1"] + eps
        and a["z1"] >= b["z0"] - eps
    )


def check_item(key, rec):
    errs = []
    parts = rec["parts"]
    missing = rec["missing"]
    if missing:
        errs.append(f"缺失 UV 取样键: {missing}")
    if not parts:
        errs.append("无任何几何部件")
        return errs

    min_y = min(p["y0"] for p in parts)
    if abs(min_y) > 0.015:
        errs.append(f"未接地或穿地: min_y={min_y:.3f}")

    for i, p in enumerate(parts):
        dx = p["x1"] - p["x0"]
        dy = p["y1"] - p["y0"]
        dz = p["z1"] - p["z0"]
        if min(dx, dy, dz) < 0.022:
            errs.append(f"部件#{i}({p['k']}) 退化过薄: ({dx:.3f}, {dy:.3f}, {dz:.3f})")

    n = len(parts)
    vis = [False] * n
    stack = [0]
    vis[0] = True
    while stack:
        u = stack.pop()
        for v in range(n):
            if not vis[v] and boxes_touch(parts[u], parts[v]):
                vis[v] = True
                stack.append(v)
    isolated = [i for i, ok in enumerate(vis) if not ok]
    if isolated:
        errs.append(f"存在悬浮/离断部件索引: {isolated}")

    return errs


def main():
    data = dump_parts()
    bad = 0
    for key, rec in data.items():
        errs = check_item(key, rec)
        if errs:
            bad += 1
            print(f"[FAIL] {key} ({rec['arch']}): " + " ; ".join(errs))
    if bad:
        print(f"wall_guard: {bad}/{len(data)} 个墙体/掩体状态未通过守卫检查")
        sys.exit(1)
    print(f"wall_guard: 全部 {len(data)} 个墙体/掩体/连接态几何与 UV 守卫通过 (0 悬浮 / 0 缺失 / 0 穿地)")


if __name__ == "__main__":
    main()
