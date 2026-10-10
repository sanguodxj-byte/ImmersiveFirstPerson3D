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


def max_rect(avail):
    """bool 矩阵里面积最大的全 True 子矩形 -> (x0,y0,x1,y1)。直方图法，O(H*W)。"""
    H, W = avail.shape
    heights = np.zeros(W, dtype=np.int64)
    best_area, best = 0, (0, 0, 0, 0)
    for y in range(H):
        heights = np.where(avail[y], heights + 1, 0)
        stack = []                      # [(起点列, 高)]
        for x in range(W + 1):
            h = int(heights[x]) if x < W else 0
            start = x
            while stack and stack[-1][1] > h:
                sx, sh = stack.pop()
                if sh * (x - sx) > best_area:
                    best_area = sh * (x - sx)
                    best = (sx, y - sh + 1, x, y + 1)
                start = sx
            stack.append((start, h))
    return best


ORDER = {                      # 收缩优先级：先点名要的带优先占位，后面的只能捡剩下的
    "bedlike": ("top", "pillow", "front", "wood"),
    "soft": ("top", "back", "arml", "armr", "front", "wood"),
    "chair": ("top", "back", "front", "wood"),
    # 石碑是一摞横带：从最窄的碑冠往下铺，否则 top 先长大就把 face 的位子吃光了
    "bands": ("crest", "upper", "top", "mid", "face", "front", "neck", "base", "wood"),
}
# 逐件指定优先级；没列出的走 kind 启发式
ORDER_OF = {
    "stelelarge": "bands", "stelegrand": "bands",
    "floodlight": ("top", "pole", "basel", "baser", "wood"),
}


def grow(ct, ok, r):
    """向外吃进"实心、未认领、且不属于别的区声明范围"的像素（逐边贪心，每次吃增益最大的一边）。
    只收缩不扩张的话，床架外圈那几列木面就永远没人认领。"""
    x0, y0, x1, y1 = r
    for _ in range(400):
        gains = []
        if x0 > 0:
            col = ok[:, x0 - 1]
            gains.append((int(ct[y0:y1, x0 - 1].sum()), col[y0:y1].all(), -1, 0))
        if x1 < ok.shape[1]:
            col = ok[:, x1]
            gains.append((int(ct[y0:y1, x1].sum()), col[y0:y1].all(), 1, 0))
        if y0 > 0:
            row = ok[y0 - 1, :]
            gains.append((int(ct[y0 - 1, x0:x1].sum()), row[x0:x1].all(), 0, -1))
        if y1 < ok.shape[0]:
            row = ok[y1, :]
            gains.append((int(ct[y1, x0:x1].sum()), row[x0:x1].all(), 0, 1))
        cand = [g for g in gains if g[1] and g[0] > 0]
        if not cand:
            break
        _, _, dx, dy = max(cand, key=lambda g: g[0])
        if dx < 0:
            x0 -= 1
        elif dx > 0:
            x1 += 1
        elif dy < 0:
            y0 -= 1
        else:
            y1 += 1
    return (x0, y0, x1, y1)


def max_content_rect(valid, ct, box):
    """在 box 内找"整块都 valid（实心、未认领）"且**盖住内容像素最多**的子矩形。
    面积最大的那个往往是一条细高条（椭圆的中段），而覆盖内容最多的是一横条 ——
    闸门看的是覆盖率，所以这里必须按内容像素计权，不按面积。"""
    x0, y0, x1, y1 = box
    # 积分图：任意矩形的内容像素数 O(1) 取，否则这层四重循环跑不动
    itg = np.zeros((ct.shape[0] + 1, ct.shape[1] + 1), np.int64)
    itg[1:, 1:] = np.cumsum(np.cumsum(ct.astype(np.int64), axis=0), axis=1)

    def wsum(yy0, xx0, yy1, xx1):
        return int(itg[yy1, xx1] - itg[yy0, xx1] - itg[yy1, xx0] + itg[yy0, xx0])

    best, bw = None, -1
    for a in range(y0, y1):
        colok = np.ones(x1 - x0, bool)
        for b in range(a, y1):
            colok &= valid[b, x0:x1]
            if not colok.any():
                break                       # 再往下没有整列都合法的了
            run = []
            for i in range(x1 - x0 + 1):
                if i < x1 - x0 and colok[i]:
                    run.append(i)
                    continue
                if run:
                    w = wsum(a, x0 + run[0], b + 1, x0 + run[-1] + 1)
                    if w > bw:
                        bw, best = w, (x0 + run[0], a, x0 + run[-1] + 1, b + 1)
                    run = []
    return best or (0, 0, 0, 0)


def fix_view(src, d, order):
    """把声明的矩形各自收缩到"最大实心(不透明、无重叠)子矩形"，再向外吃进无主的实心像素。
    wood 特殊：它允许压在别的区里，但必须避开黑笔画（补绘后不能残留描边）。"""
    op = src[:, :, 3] >= 128
    ct = CK.content_mask(src)
    clean = op & ~FA.dilate(FA.black_mask(src))
    claimed = np.zeros(op.shape, bool)
    declared = np.zeros(op.shape, bool)
    for name in order:
        if name in d and name != "wood":
            x0, y0, x1, y1 = d[name]
            declared[y0:y1, x0:x1] = True
    out = {}
    # 没列进 ORDER 的区名也要走一遍：静默丢掉一个区，打印出来的"新 SPEC"就少一块，
    # 粘回去等于把该件的这个面悄悄删了。
    order = tuple(order) + tuple(k for k in d if k not in ("path", "discard")
                                 and k not in order)
    for name in order:
        if name not in d:
            continue
        x0, y0, x1, y1 = d[name]
        good = clean if name == "wood" else op
        avail = (good & ~claimed) if name != "wood" else good   # noqa: E712
        box = np.zeros(op.shape, bool)
        box[y0:y1, x0:x1] = True
        r = max_content_rect(avail & box, ct, (x0, y0, x1, y1))
        if name != "wood":
            free = op & ~claimed & (~declared | box)
            r = grow(ct, free, r)
        out[name] = tuple(int(v) for v in r)
        if name != "wood":
            claimed[r[1]:r[3], r[0]:r[2]] = True
    return out


def leftover(src, d, rects):
    """收缩后没人认领的内容像素（与检查器同一口径：笔画、边缘余量不算）。"""
    ct = CK.content_mask(src)
    cov = np.zeros(ct.shape, bool)
    for name, r in rects.items():
        if name == "wood":
            continue
        cov[r[1]:r[3], r[0]:r[2]] = True
    for r in d.get("discard", []):
        cov[r[1]:r[3], r[0]:r[2]] = True
    miss = ct & ~cov & ~FA.dilate(FA.dilate(FA.black_mask(src)))
    if not miss.any():
        return "无"
    ys, xs = np.where(miss)
    return f"{int(miss.sum())}px y{ys.min()}..{ys.max()} x{xs.min()}..{xs.max()}"


def main_fix(items):
    """python scripts/_furn_fit.py fix couch doublebed ...  -> 打印可直接粘回 SPEC 的块"""
    for item in items:
        views = FA.SPEC.get(item)
        if not views:
            print(f"# {item}: SPEC 里没有")
            continue
        kind = ORDER_OF.get(item) or (
            "bedlike" if any("pillow" in v for v in views.values()) and
            "back" not in views.get("south", {}) else
            ("soft" if "arml" in views.get("south", {}) else "chair"))
        order = kind if isinstance(kind, tuple) else ORDER[kind]
        print(f'    "{item}": {{')
        for view, d in views.items():
            src = FA.load(d["path"])
            rects = fix_view(src, d, order)
            print(f'        "{view}": {{"path": "{d["path"]}",')
            print("                  " + ", ".join(f'"{k}": {v}' for k, v in rects.items()) + "},")
            lo = leftover(src, d, rects)
            if lo != "无":
                print(f"        # 无归属: {lo}")
        print("    },")


def selftest_rect():
    """max_content_rect 必须按"内容像素"取优，不是按面积：两块合法区，一块大而无内容、
    一块小而全是内容 —— 选错的那次，粘回 SPEC 就是白丢覆盖率。"""
    v = np.zeros((20, 20), bool)
    v[2:12, 2:8] = True            # 60 格，面积大
    v[6:11, 12:19] = True          # 35 格，面积小
    ct = np.zeros((20, 20), int)
    ct[6:11, 12:19] = 1            # 内容只在小块里
    big = max_content_rect(v, ct, (0, 0, 20, 20))
    ct2 = np.zeros((20, 20), int)
    ct2[2:12, 2:8] = 1             # 内容只在大块里
    small = max_content_rect(v, ct2, (0, 0, 20, 20))
    bad = []
    if big != (12, 6, 19, 11):
        bad.append(("按内容取优", big, (12, 6, 19, 11)))
    if small != (2, 2, 8, 12):
        bad.append(("反向", small, (2, 2, 8, 12)))
    return bad


def main():
    if len(sys.argv) > 1 and sys.argv[1] == "fix":
        for n, got, want in selftest_rect():
            print(f"  [夹具] max_content_rect {n}: 实得 {got} 应为 {want}")
        if selftest_rect():
            print("夹具未过 —— 标定器本身不可信，拒绝出坐标")
            return 2
        main_fix(sys.argv[2:] or list(FA.SPEC))
        return
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
