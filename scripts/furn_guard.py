# AI生成
# -*- coding: utf-8 -*-
"""建模守卫：查"离断/悬浮"与"重叠"，看的是部件清单，不是截图。

为什么不用截图判：审核台的剪影判据是"投影按行连续"，它既会误报（俯视里高而薄的件
必然与座面分离）也会漏报（部件互相插进 30cm 但投影贴在一起照样一段）。几何清单没有
这两个盲区：件与件之间要么接上、要么没接上；要么吞并、要么没吞并。

口径：每个部件 = 一个 XZ 截面（方/圆）× 一段 Y。
  · 接合 = 两实体的 3D 间隙 <= TOL；
  · 吞并 = 交集体积 / 较小件体积 > OVERLAP_MAX。
圆用真的圆盘采样，不用它的外接方框 —— 否则"腿贴在座盘下面"会被方框算成互相吞并。

用法：python scripts/furn_guard.py                 # 全部件 × 四朝向
      python scripts/furn_guard.py diningchair     # 指定件
      python scripts/furn_guard.py --refresh       # 先从检查页重取部件清单
退出码：0 = 无违规；1 = 有违规；2 = 守卫自身夹具未过（不给存量背书）
"""
import io
import json
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _tmpsweep import scratch, env_for, profile, drop, sweep  # noqa: E402

MOD = r"D:\rim mod\FirstPerson3D"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
URL = "http://127.0.0.1:8899/furniture.html?parts=1"
CACHE = os.path.join(MOD, "docs", "furniture", "guard", "parts.json")

CELL = 0.02           # XZ 采样格（米）
TOL = 0.025           # 接合容差：小于这个间隙算"贴上了"
SWALLOW = 0.85        # 一件有 85% 以上体积埋在另一件里 = 看不见的废几何
COFACE_TOL = 0.004    # 两面共面的判定厚度
COFACE_AREA = 0.0001  # 共面且重叠面积上限（1 cm²）：超了就是闪面（z-fighting）
GROUND = 0.006        # 离地多近算着地


def fetch():
    """让检查页把全量部件清单写进 <pre id=partjson>，用 --dump-dom 取回来。"""
    rd = scratch("furnguard")
    try:
        r = subprocess.run([CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
                            "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
                            "--window-size=420,320", "--virtual-time-budget=40000",
                            "--timeout=120000", "--user-data-dir=" + profile(rd),
                            "--dump-dom", URL],
                           env=env_for(rd),
                           stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=200)
    finally:
        drop(rd)
        sweep()   # 兜底：收掉别处被杀时留下的沙盒/C 盘历史遗留
    html = r.stdout.decode("utf-8", "replace")
    i = html.find('<pre id="partjson">')
    if i < 0:
        raise RuntimeError("检查页没给出 partjson（页面崩了？先跑 furn_audit）")
    j = html.find("</pre>", i)
    return json.loads(html[i + len('<pre id="partjson">'):j])


def load(refresh=False):
    if refresh or not os.path.exists(CACHE):
        data = fetch()
        os.makedirs(os.path.dirname(CACHE), exist_ok=True)
        io.open(CACHE, "w", encoding="utf-8", newline="\n").write(json.dumps(data))
        return data
    return json.load(io.open(CACHE, encoding="utf-8"))


def cells(p):
    """部件的 XZ 截面 -> 采样格集合。圆柱用圆盘（外接方框会把接触误算成吞并）。"""
    out = set()
    x0, x1, z0, z1 = p["x0"], p["x1"], p["z0"], p["z1"]
    i0, i1 = int(x0 / CELL) - 1, int(x1 / CELL) + 2
    j0, j1 = int(z0 / CELL) - 1, int(z1 / CELL) + 2
    for i in range(i0, i1):
        for j in range(j0, j1):
            x, z = (i + 0.5) * CELL, (j + 0.5) * CELL
            if p["k"] == "cyl":
                dx, dz = x - p["cx"], z - p["cz"]
                if dx * dx + dz * dz > p["r"] * p["r"]:
                    continue
            elif not (x0 <= x <= x1 and z0 <= z <= z1):
                continue
            out.add((i, j))
    return out


def vol(p, ncell):
    y = max(0.0, p["y1"] - p["y0"])
    if p["k"] == "cyl":
        return 3.141592653589793 * p["r"] * p["r"] * y
    return (p["x1"] - p["x0"]) * (p["z1"] - p["z0"]) * y


def gap_axis(a0, a1, b0, b1):
    if a1 < b0:
        return b0 - a1
    if b1 < a0:
        return a0 - b1
    return 0.0


def joined(pa, pb, ca, cb):
    """两实体的 3D 间隙 <= TOL？XZ 用格距近似，Y 用区间。"""
    yg = gap_axis(pa["y0"], pa["y1"], pb["y0"], pb["y1"])
    if yg > TOL:
        return False
    if not ca or not cb:
        return False
    # 膨胀双方一格后看是否相遇：格数 × CELL 近似欧氏距离的下界
    best = None
    for (i, j) in ca:
        for (k, l) in cb:
            d = max(abs(i - k), abs(j - l)) * CELL
            if best is None or d < best:
                best = d
            if best <= TOL:
                return True
    return (best or 9e9) <= TOL


def overlap_ratio(pa, pb, ca, cb):
    """交集体积 / 较小件体积。"""
    yg = min(pa["y1"], pb["y1"]) - max(pa["y0"], pb["y0"])
    if yg <= 0:
        return 0.0
    inter = len(ca & cb) * CELL * CELL * yg
    small = min(vol(pa, ca), vol(pb, cb))
    return 0.0 if small <= 0 else inter / small


def coface(a, b, i, j):
    """共面闪面：两块的面在同一平面上又互相盖住 -> 渲染时逐帧互相吃掉（书架层板那次就是它）。
    注意：只查"面对面贴在一起"，不查普通穿插 —— 扶手插进靠背、床垫坐在床架里是
    刚体拼接该有的做法，从外面看不见，报了只是噪音。"""
    out = []
    axes = (("x", "x0", "x1"), ("y", "y0", "y1"), ("z", "z0", "z1"))
    for nm, lo, hi in axes:
        others = [k for k, l, u in axes if k != nm]
        # 只算"同向共面"：两件的外表面在同一平面上朝同一侧（a 的正面撞 b 的正面）。
        # a.hi 撞 b.lo 是叠放（床垫坐在床架上）—— 两面朝向相反，各自只从一侧可见，不闪。
        for av in (None,):
            pass
        for pa, pb in ((a[hi], b[hi]), (a[lo], b[lo])):
            av = pa
            if abs(pa - pb) > COFACE_TOL:
                continue
            if nm == "y" and abs(av) < GROUND:
                continue        # 都坐在地板上的底面：永远看不见，不算闪面
            area = 1.0
            ok = True
            for k in others:
                ov = min(a[k + "1"], b[k + "1"]) - max(a[k + "0"], b[k + "0"])
                if ov <= 0:
                    ok = False
                    break
                area *= ov
            if ok and area > COFACE_AREA:
                out.append(("coface", "第 %d、%d 件在 %s=%.3f 共面且重叠 %.1f cm²（闪面）"
                            % (i, j, nm, av, area * 10000)))
    return out


def box_str(p):
    return "%s[%.2f,%.2f]x[%.2f,%.2f]x[%.2f,%.2f]" % (
        "柱" if p["k"] == "cyl" else "板", p["x0"], p["x1"], p["y0"], p["y1"], p["z0"], p["z1"])


def check(parts):
    """-> [(码, 说明)]"""
    errs = []
    n = len(parts)
    if n == 0:
        return [("noparts", "没有部件")]
    cs = [cells(p) for p in parts]
    adj = [[] for _ in range(n)]
    for i in range(n):
        for j in range(i + 1, n):
            if joined(parts[i], parts[j], cs[i], cs[j]):
                adj[i].append(j)
                adj[j].append(i)
            r = overlap_ratio(parts[i], parts[j], cs[i], cs[j])
            if r > SWALLOW:
                errs.append(("swallow", "#%d %s 有 %.0f%% 埋在 #%d %s 里（废几何）"
                             % (i, box_str(parts[i]), 100 * r, j, box_str(parts[j]))))
            for code, msg in coface(parts[i], parts[j], i, j):
                errs.append((code, msg + "  [#%d %s | #%d %s]"
                             % (i, box_str(parts[i]), j, box_str(parts[j]))))
    seen = set([0])
    stack = [0]
    while stack:
        k = stack.pop()
        for m in adj[k]:
            if m not in seen:
                seen.add(m)
                stack.append(m)
    if len(seen) != n:
        islands, left = [], set(range(n)) - seen
        while left:
            st = [left.pop()]
            comp = set(st)
            while st:
                k = st.pop()
                for m in adj[k]:
                    if m in left:
                        left.discard(m)
                        comp.add(m)
                        st.append(m)
            islands.append(sorted(comp))
        for comp in islands:
            errs.append(("split", "孤岛 %s：%s" % (comp, " ".join(
                "#%d %s" % (k, box_str(parts[k])) for k in comp))))
    if not any(p["y0"] <= GROUND for p in parts):
        errs.append(("floating", "整件最低点 y=%.3f，没有部件着地"
                     % min(p["y0"] for p in parts)))
    return errs


FIXTURES = [
    # (名字, 部件, 期望码集)
    ("叠放", [{"k": "box", "x0": -.3, "x1": .3, "z0": -.3, "z1": .3, "y0": 0, "y1": .4},
              {"k": "box", "x0": -.2, "x1": .2, "z0": -.2, "z1": .2, "y0": .4, "y1": .5}], set()),
    ("悬空岛", [{"k": "box", "x0": -.3, "x1": .3, "z0": -.3, "z1": .3, "y0": 0, "y1": .4},
                {"k": "box", "x0": -.2, "x1": .2, "z0": -.2, "z1": .2, "y0": .9, "y1": 1.0}],
     {"split"}),
    ("整件离地", [{"k": "box", "x0": -.3, "x1": .3, "z0": -.3, "z1": .3, "y0": .3, "y1": .4}],
     {"floating"}),
    ("整块埋进", [{"k": "box", "x0": 0, "x1": .4, "z0": 0, "z1": .4, "y0": 0, "y1": .4},
                  {"k": "box", "x0": .05, "x1": .35, "z0": .05, "z1": .35, "y0": .02, "y1": .38}],
     {"swallow"}),
    # 共面闪面：两块板的外表面在同一平面上互相盖住（书架层板与侧板那次）
    ("同向共面", [{"k": "box", "x0": 0, "x1": .4, "z0": 0, "z1": .02, "y0": 0, "y1": .4},
                  {"k": "box", "x0": .1, "x1": .3, "z0": .018, "z1": .04, "y0": 0, "y1": .4}],
     {"coface"}),
    # 正常榫接：扶手插进靠背一半体积、各自深度不同 —— 不可见，不该报
    ("穿插接缝", [{"k": "box", "x0": 0, "x1": .4, "z0": 0, "z1": .16, "y0": 0, "y1": .85},
                  {"k": "box", "x0": .3, "x1": .5, "z0": .01, "z1": .15, "y0": 0, "y1": .62}],
     set()),
    # 圆柱腿贴在方座下面：外接方框会算成吞并，圆盘不会 —— 这条盯住"守卫自己别误报"
    ("腿接圆座", [{"k": "cyl", "cx": 0, "cz": 0, "r": .21, "x0": -.21, "x1": .21,
                   "z0": -.21, "z1": .21, "y0": .39, "y1": .45},
                  {"k": "cyl", "cx": .15, "cz": .15, "r": .03, "x0": .12, "x1": .18,
                   "z0": .12, "z1": .18, "y0": 0, "y1": .42}], set()),
]


def selftest():
    bad = []
    for name, parts, want in FIXTURES:
        got = set(c for c, _ in check(parts))
        if got != want:
            bad.append((name, sorted(want), sorted(got)))
    return bad


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    refresh = "--refresh" in sys.argv
    bad = selftest()
    for name, want, got in bad:
        print("  [fixture] %s: 期望%s 实得%s" % (name, want, got))
    if bad:
        print("守卫夹具未过 —— 判据本身不可信，拒绝给存量背书")
        return 2
    data = load(refresh)
    n_err = 0
    for key in sorted(data):
        item = key.split(".")[0]
        if args and item not in args:
            continue
        for code, msg in check(data[key]):
            print("  [%s] %s: %s" % (code, key, msg))
            n_err += 1
    if n_err:
        print("（同一件四个朝向重复报的，是同一处几何）")
    print("结论:", "PASS 部件级无离断/悬浮/吞并" if n_err == 0 else "FAIL %d 处" % n_err)
    return 0 if n_err == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
