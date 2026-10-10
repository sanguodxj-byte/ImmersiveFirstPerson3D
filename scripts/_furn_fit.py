# AI生成
# -*- coding: utf-8 -*-
"""取样区标定试算台：改这里的 rects/discard，直接看校验器的判词，不碰 SPEC。

用法：python scripts/_furn_fit.py            # 跑全部草稿视图
      python scripts/_furn_fit.py armchair   # 只跑一件
"""
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furn_atlas as FA          # noqa: E402
import furn_uv_check as CK       # noqa: E402

F = "Core/textures/things/building/furniture"
FB = F + "/bed"

BEDFAM = ("doublebed", "royalbed", "hospitalbed", "bedroll", "bedrolldouble",
          "sleepspot", "doublesleepspot")

DRAFT = {
    "armchair": {
        "south": {"path": f"{F}/armchair_south.png",
                  "top": (94, 84, 164, 110), "back": (90, 110, 170, 136),
                  "front": (82, 136, 176, 170), "wood": (100, 140, 160, 160),
                  "discard": [(82, 164, 176, 176), (84, 76, 174, 84)]},
        "north": {"path": f"{F}/armchair_north.png",
                  "arml": (82, 100, 102, 108), "armr": (152, 100, 174, 108),
                  "top": (80, 108, 176, 122), "back": (86, 122, 170, 168),
                  "wood": (100, 130, 160, 160),
                  "discard": [(80, 122, 88, 132), (168, 122, 176, 132),
                              (78, 164, 178, 176), (78, 96, 178, 100)]},
        "east": {"path": f"{F}/armchair_east.png",
                 "top": (92, 84, 108, 118), "back": (86, 120, 155, 144),
                 "front": (88, 144, 160, 168), "wood": (100, 128, 140, 142),
                 "discard": [(84, 100, 94, 120), (106, 100, 118, 120),
                             (82, 82, 94, 92), (106, 82, 118, 92),
                             (84, 166, 162, 176)]},
    },
    "couch": {v: {"path": f"{F}/couch_%s.png" % v} for v in ("south", "north", "east")},
    "diningchair": {v: {"path": f"{F}/diningchair_%s.png" % v}
                    for v in ("south", "north", "east")},
}
for _it in BEDFAM:
    DRAFT[_it] = {v: {"path": f"{FB}/{_it}_{v}.png"}
                  for v in ("south", "north", "east")}


def band_ink(src, r):
    ink = FA.dilate(FA.black_mask(src))
    x0, y0, x1, y1 = r
    sub = ink[y0:y1, x0:x1]
    return int(sub.sum()), (x1 - x0) * (y1 - y0)


def row_span(opaque, y):
    """第 y 行最长连续不透明段的 [x0,x1)，没有则 None。"""
    xs = np.where(opaque[y])[0]
    if len(xs) == 0:
        return None
    best, a, p = (xs[0], xs[0] + 1), xs[0], xs[0]   # 初值是一段长度 1，否则"首尾全跨"永远换不掉
    for x in xs[1:]:
        if x > p + 1:
            if (p + 1 - a) > (best[1] - best[0]):
                best = (a, p + 1)
            a = x
        p = x
    if (p + 1 - a) > (best[1] - best[0]):
        best = (a, p + 1)
    return best


def fit_bands(src, k=3, vert=False):
    """把图切成 k 条横向带：每条带的 x 区间 = 该带各行最长不透明段的交集，
    目标 = 覆盖内容像素最多。返回 (带列表, 覆盖率)。vert=True 先转置再切（宽体件用）。
    只在**内容包围盒**内切带：源图四周的透明边距不是床板，切进去会得到全 None 的带。"""
    if vert:
        src = np.transpose(src, (1, 0, 2))
    ct = CK.content_mask(src)
    op = src[:, :, 3] >= 128
    H, W = ct.shape
    rows = np.where(ct.any(axis=1))[0]
    if len(rows) == 0:
        return [], 0.0
    rlo, rhi = int(rows[0]), int(rows[-1]) + 1
    spans = [row_span(op, y) for y in range(H)]
    total = int(ct.sum())

    def seg(y0, y1):
        rows = [y for y in range(y0, y1) if spans[y]]
        if not rows:
            return None
        a, b = rows[0], rows[-1] + 1          # 夹到首尾有内容的行
        ss = spans[a:b]
        if any(s is None for s in ss):        # 中间夹一行全透明 -> 这块会有洞，弃
            return None
        x0 = max(s[0] for s in ss)
        x1 = min(s[1] for s in ss)
        if x1 <= x0:
            return None
        n = int(ct[a:b, x0:x1].sum())
        return (x0, a, x1, b, n)

    import itertools
    cand = list(range(rlo + 4, rhi - 3, 4))
    if len(cand) < k - 1:
        return [], 0.0
    best = None
    for cuts in itertools.combinations(cand, k - 1):
        bnd = (rlo,) + cuts + (rhi,)
        segs = [seg(bnd[i], bnd[i + 1]) for i in range(k)]
        if any(s is None for s in segs):
            continue
        cov = sum(s[4] for s in segs)
        if best is None or cov > best[0]:
            best = (cov, [s[:4] for s in segs])
    if best is None:
        return [], 0.0
    rs = best[1]
    if vert:
        rs = [(y0, x0, y1, x1) for (x0, y0, x1, y1) in rs]
    return rs, best[0] / float(total)


def main():
    if "pack" in sys.argv:
        for item in (sys.argv[2:] or DRAFT):
            for view, d in DRAFT[item].items():
                try:
                    src = FA.load(d["path"])
                except Exception as e:
                    print(f"{item}.{view} 读图失败 {e}")
                    continue
                for k in (2, 3, 4):
                    for vert in (False, True):
                        bs, cov = fit_bands(src, k, vert)
                        print(f"{item}.{view} k={k}{'V' if vert else 'H'} "
                              f"覆盖 {100*cov:.1f}%: " + "  ".join(str(tuple(int(x) for x in b))
                                                                   for b in bs))
        return
    only = sys.argv[1:] or list(DRAFT)
    for item in only:
        spec = {item: DRAFT[item]}
        for code, msg, hint in CK.check_item(item, spec):
            print(f"  [{code}] {msg}" + ((" | " + hint) if hint else ""))
        for view, d in DRAFT[item].items():
            src = FA.load(d["path"])
            for r in d.get("discard", []):
                n, a = band_ink(src, r)
                print(f"    discard {view} {r}: 笔画 {n}/{a} = {100.0*n/a:.0f}%")
            for k, r in d.items():
                if k in ("path", "discard"):
                    continue
                x0, y0, x1, y1 = r
                ct = CK.content_mask(src)[y0:y1, x0:x1]
                print(f"    区 {view}.{k} {r}: 内容 {int(ct.sum())}/{(x1-x0)*(y1-y0)}"
                      f" = {100.0*ct.sum()/((x1-x0)*(y1-y0)):.0f}%")


if __name__ == "__main__":
    main()
