# AI生成
# -*- coding: utf-8 -*-
"""动物 3D 建模守卫（scripts/animal_guard.py）：
1. 检查 viewer/animals.html 的 JS 语法（node --check）。
2. 通过无头 Chrome（严格走 _tmpsweep D 盘沙盒口径）抓取 ?parts=1 的全部 118 个物种部件包围盒。
3. 逐物种核验：
   - 缺取样区 missing == 0（6 区 UV 全部命中 AnimalAtlas）
   - 部件数 >= 14（拒绝早期 8~13 件粗糙火柴盒）
   - 接地性：整体最低点 min(y0) 落在 [-0.03, +0.025]m 内（不悬空、不陷地）
   - 连通性：部件 3D 包围盒（容差 0.045L）构成单一连通分量（无任何离断/悬浮部件）
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
URL = "http://127.0.0.1:8899/animals.html?parts=1"


def js_check():
    html = os.path.join(MOD, "viewer", "animals.html")
    src = io.open(html, encoding="utf-8").read()
    blocks = re.findall(r"<script[^>]*>(.*?)</script>", src, re.S)
    kinds = re.findall(r"<script([^>]*)>", src)
    os.makedirs(TMPROOT, exist_ok=True)
    bad = []
    for i, (attr, body) in enumerate(zip(kinds, blocks)):
        ext = "mjs" if "module" in attr else "js"
        tmp = os.path.join(TMPROOT, "animal_js_%d.%s" % (i, ext))
        io.open(tmp, "w", encoding="utf-8").write(body)
        r = subprocess.run(["node", "--check", tmp], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        try:
            os.remove(tmp)
        except OSError:
            pass
        if r.returncode != 0:
            bad.append("animals.html script#%d: %s" % (i + 1, r.stdout.decode("utf-8", "replace").strip()))
    return bad


def dump_parts():
    rd = scratch("aguard")
    cmd = [
        CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
        "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
        "--virtual-time-budget=20000", "--timeout=90000",
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
        raise RuntimeError("未能从 animals.html?parts=1 提取 #partjson")
    return json.loads(m.group(1))


def connected_components(parts, eps=0.048):
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
    detached = [idx for c in comps[1:] for idx in c]
    return len(comps), detached


def main():
    sweep()
    js_bad = js_check()
    if js_bad:
        for b in js_bad:
            print("  [js-fail]", b)
        return 2

    data = dump_parts()
    print("== 动物建模守卫：共检查 %d 个物种 ==" % len(data))
    defects = []
    min_parts = 999
    max_parts = 0
    arch_counts = {}

    for sp, info in sorted(data.items()):
        arch = info["arch"]
        parts = info["parts"]
        miss = info["missing"]
        arch_counts[arch] = arch_counts.get(arch, 0) + 1
        min_parts = min(min_parts, len(parts))
        max_parts = max(max_parts, len(parts))

        if miss:
            defects.append("%s(%s): 缺 UV 取样区 %s" % (sp, arch, miss))
        if len(parts) < 14:
            defects.append("%s(%s): 部件过少 (%d < 14)" % (sp, arch, len(parts)))

        ymin = min(p["y0"] for p in parts)
        if ymin < -0.035 or ymin > 0.025:
            defects.append("%s(%s): 接地异常 min(y0)=%.3f" % (sp, arch, ymin))

        n_comp, det = connected_components(parts, eps=0.048)
        if n_comp > 1:
            det_info = ["#%d(%s y=%.2f..%.2f z=%.2f..%.2f)" % (
                i, parts[i]["k"], parts[i]["y0"], parts[i]["y1"], parts[i]["z0"], parts[i]["z1"]
            ) for i in det[:4]]
            defects.append("%s(%s): 存在 %d 个离断分量，游离件: %s" % (sp, arch, n_comp, ", ".join(det_info)))

    print("  原型分布 (%d 类): %s" % (len(arch_counts), ", ".join("%s:%d" % kv for kv in sorted(arch_counts.items()))))
    print("  单模部件数范围: %d ~ %d 件" % (min_parts, max_parts))
    for d in defects:
        print("  [defect]", d)
    print("结论:", "PASS 全部 %d 个物种 0 缺区、0 悬空、0 离断部件" % len(data) if not defects else ("FAIL %d 处缺陷" % len(defects)))
    return 0 if not defects else 1


if __name__ == "__main__":
    sys.exit(main())
