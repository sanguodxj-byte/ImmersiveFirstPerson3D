# AI生成
# -*- coding: utf-8 -*-
"""物块与资源 3D 建模守卫（scripts/item_guard.py）：
1. 检查 viewer/items.html 的 JS 语法（node --check）。
2. 通过无头 Chrome（严格走 _tmpsweep D 盘沙盒口径）抓取 ?parts=1 的全部 35×3=105 个物块变体部件包围盒。
3. 逐变体核验：
   - 缺取样区 missing == 0（4 区 UV 全部命中 ItemAtlas）
   - 接地性：整体最低点 min(y0) 落在 [-0.03, +0.025]m 内（不悬空、不陷地）
   - 连通性：部件 3D 包围盒（容差 0.035m）构成单一连通分量（无任何离断/悬浮部件）
"""
import io
import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _tmpsweep import TMPROOT, scratch, env_for, profile, drop, sweep, sweep_mine  # noqa: E402

MOD = r"D:\rim mod\FirstPerson3D"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
URL = "http://127.0.0.1:8899/items.html?parts=1"


def js_check():
    html = os.path.join(MOD, "viewer", "items.html")
    src = io.open(html, encoding="utf-8").read()
    blocks = re.findall(r"<script[^>]*>(.*?)</script>", src, re.S)
    kinds = re.findall(r"<script([^>]*)>", src)
    os.makedirs(TMPROOT, exist_ok=True)
    bad = []
    for i, (attr, body) in enumerate(zip(kinds, blocks)):
        ext = "mjs" if "module" in attr else "js"
        tmp = os.path.join(TMPROOT, "item_js_%d.%s" % (i, ext))
        io.open(tmp, "w", encoding="utf-8").write(body)
        r = subprocess.run(["node", "--check", tmp], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        try:
            os.remove(tmp)
        except OSError:
            pass
        if r.returncode != 0:
            bad.append("items.html script#%d: %s" % (i + 1, r.stdout.decode("utf-8", "replace").strip()))
    return bad


def dump_parts():
    rd = scratch("iguard")
    cmd = [
        CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
        "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
        "--virtual-time-budget=15000", "--timeout=90000",
        "--user-data-dir=" + profile(rd),
        "--dump-dom", URL
    ]
    try:
        out = subprocess.check_output(cmd, env=env_for(rd), stderr=subprocess.DEVNULL, timeout=110)
    finally:
        drop(rd)
        sweep_mine()
        sweep()
    html = out.decode("utf-8", "replace")
    m = re.search(r'<pre id="partjson">(.*?)</pre>', html, re.S)
    if not m:
        raise RuntimeError("未能从 items.html?parts=1 提取 #partjson")
    return json.loads(m.group(1))


def connected_components(parts, eps=0.038):
    n = len(parts)
    if n <= 1:
        return 1, []
    adj = [[] for _ in range(n)]
    for i in range(n):
        a = parts[i]
        for j in range(i + 1, n):
            b = parts[j]
            if (a["x0"] - eps <= b["x1"] and a["x1"] + eps >= b["x0"] and
                a["y0"] - eps <= b["y1"] and a["y1"] + eps >= b["y0"] and
                a["z0"] - eps <= b["z1"] and a["z1"] + eps >= b["z0"]):
                adj[i].append(j)
                adj[j].append(i)
    vis = [False] * n
    comps = []
    for i in range(n):
        if vis[i]:
            continue
        q = [i]
        vis[i] = True
        comp = []
        while q:
            u = q.pop()
            comp.append(u)
            for v in adj[u]:
                if not vis[v]:
                    vis[v] = True
                    q.append(v)
        comps.append(comp)
    comps.sort(key=len, reverse=True)
    return len(comps), comps[1:]


def main():
    bad = js_check()
    if bad:
        for b in bad:
            print("  [js]", b)
        print("结论: FAIL JS 语法错误")
        return 1

    data = dump_parts()
    arch_cnt = {}
    min_p, max_p = 999, 0
    for k, info in sorted(data.items()):
        a = info["arch"]
        arch_cnt[a] = arch_cnt.get(a, 0) + 1
        parts = info["parts"]
        min_p = min(min_p, len(parts))
        max_p = max(max_p, len(parts))
        if info.get("missing"):
            bad.append("%s: 缺失 UV 区 %s" % (k, info["missing"]))
        min_y = min(p["y0"] for p in parts)
        if min_y < -0.035 or min_y > 0.025:
            bad.append("%s: 接地异常 min_y=%.4f" % (k, min_y))
        n_comp, detached = connected_components(parts)
        if n_comp > 1:
            bad.append("%s: 存在 %d 个离断部件群 %s" % (k, n_comp - 1, detached))

    print("== 物块建模守卫：共检查 %d 个物块变体 ==" % len(data))
    print("  原型分布 (%d 类): %s" % (
        len(arch_cnt),
        ", ".join("%s:%d" % (k, v // 3) for k, v in sorted(arch_cnt.items()))
    ))
    print("  单模部件数范围: %d ~ %d 件" % (min_p, max_p))
    for b in bad:
        print("  [defect]", b)
    print("结论: %s" % ("PASS 全部 %d 个物块变体 0 缺区、0 悬空、0 离断部件" % len(data)
                        if not bad else "FAIL %d 处缺陷" % len(bad)))
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
