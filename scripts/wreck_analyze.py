# AI生成
# -*- coding: utf-8 -*-
"""残骸贴图数值辅助诊断：alpha 轮廓 bbox / 四边近黑描边厚度 / 内部黑线密度。

只作辅助（不替代看图）。输出 JSON + 文本摘要，写文件后再读，避免管道 mojibake。
用系统 Python 运行（需 PIL）：
  C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_analyze.py
"""
import os
import sys
import json
import statistics as st

try:
    from PIL import Image
except Exception as e:  # noqa
    print("PIL missing:", e)
    sys.exit(2)

ROOT = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
OUT_DIR = r"D:\rim mod\FirstPerson3D\docs\wreckage\_vision"

# 7 样板的源贴图 basename（三视图物件取三张）
SAMPLES = {
    "ruinedtank": ["ruinedtank.png"],
    "crashedshippart": ["crashedshippart.png"],
    "ancientwarwalkershell": ["ancientwarwalkershell.png"],
    "largeengineblock": ["largeengineblock.png"],
    "giantwheela": ["giantwheela.png"],
    "concretebarrier": ["concretebarrier.png"],
    "rustedindustrialtruck": [
        "rustedindustrialtruck_south.png",
        "rustedindustrialtruck_east.png",
        "rustedindustrialtruck_north.png",
    ],
}

_A_THRESH = 40      # alpha<=此值视为透明
_BLACK_MAXC = 60    # 近黑：max(r,g,b) < 此值
_DARK_A = 180       # 算近黑时要求 alpha 足够


def index_pngs(root):
    idx = {}
    for dp, _dn, fn in os.walk(root):
        for f in fn:
            if f.lower().endswith(".png"):
                idx.setdefault(f.lower(), os.path.join(dp, f))
    return idx


def alpha_bbox(px, w, h):
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] > _A_THRESH:
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y < y0: y0 = y
                if y > y1: y1 = y
    if x1 < 0:
        return None
    return [x0, y0, x1, y1]


def is_dark(px, x, y):
    r, g, b, a = px[x, y]
    return a > _DARK_A and max(r, g, b) < _BLACK_MAXC


def border_scan(px, w, h, bbox):
    """逐行/逐列从 alpha 边界向内的连续近黑像素长度，取中位数=每边描边厚度(px)。"""
    x0, y0, x1, y1 = bbox
    tops, bots, lefts, rights = [], [], [], []
    for x in range(x0, x1 + 1):
        y = y0
        while y <= y1 and px[x, y][3] <= _A_THRESH:
            y += 1
        if y <= y1:
            c = 0
            while y <= y1 and is_dark(px, x, y):
                c += 1; y += 1
            tops.append(c)
        y = y1
        while y >= y0 and px[x, y][3] <= _A_THRESH:
            y -= 1
        if y >= y0:
            c = 0
            while y >= y0 and is_dark(px, x, y):
                c += 1; y -= 1
            bots.append(c)
    for y in range(y0, y1 + 1):
        x = x0
        while x <= x1 and px[x, y][3] <= _A_THRESH:
            x += 1
        if x <= x1:
            c = 0
            while x <= x1 and is_dark(px, x, y):
                c += 1; x += 1
            lefts.append(c)
        x = x1
        while x >= x0 and px[x, y][3] <= _A_THRESH:
            x -= 1
        if x >= x0:
            c = 0
            while x >= x0 and is_dark(px, x, y):
                c += 1; x -= 1
            rights.append(c)

    def med(v):
        return round(st.median(v), 2) if v else None

    def frac_pos(v):
        return round(sum(1 for i in v if i > 0) / len(v), 3) if v else None

    return {
        "top_px": med(tops), "bottom_px": med(bots),
        "left_px": med(lefts), "right_px": med(rights),
        "top_hit_rate": frac_pos(tops), "bottom_hit_rate": frac_pos(bots),
        "left_hit_rate": frac_pos(lefts), "right_hit_rate": frac_pos(rights),
    }


def analyze(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    bbox = alpha_bbox(px, w, h)
    res = {"path": path, "size": [w, h], "bbox": bbox}
    if bbox:
        bw = bbox[2] - bbox[0] + 1
        bh = bbox[3] - bbox[1] + 1
        res["bbox_wh"] = [bw, bh]
        res["bbox_aspect"] = round(bw / bh, 3)
        res["bbox_norm"] = [round(bw / w, 3), round(bh / h, 3)]
        res["border"] = border_scan(px, w, h, bbox)
    # 统计
    dark_total = 0
    opaque_total = 0
    x0, y0, x1, y1 = bbox if bbox else (0, 0, w - 1, h - 1)
    for y in range(h):
        for x in range(w):
            a = px[x, y][3]
            if a > _A_THRESH:
                opaque_total += 1
                if is_dark(px, x, y):
                    dark_total += 1
    res["opaque_px"] = opaque_total
    res["dark_px"] = dark_total
    res["dark_ratio_of_opaque"] = round(dark_total / opaque_total, 4) if opaque_total else None
    return res


def main():
    idx = index_pngs(ROOT)
    out = {}
    for name, files in SAMPLES.items():
        entry = []
        for f in files:
            p = idx.get(f.lower())
            if not p:
                entry.append({"file": f, "error": "not found"})
                continue
            try:
                entry.append(analyze(p))
            except Exception as e:  # noqa
                entry.append({"file": f, "error": repr(e)})
        out[name] = entry

    os.makedirs(OUT_DIR, exist_ok=True)
    jp = os.path.join(OUT_DIR, "analysis.json")
    with open(jp, "w", encoding="utf-8") as fh:
        json.dump(out, fh, ensure_ascii=False, indent=2)
    print("WROTE", jp)
    for name, entry in out.items():
        print("==", name)
        for e in entry:
            if "error" in e:
                print("   ", e["file"], "ERR", e["error"]); continue
            b = e.get("border", {})
            print("   ", os.path.basename(e["path"]), "size", e["size"],
                  "bbox", e.get("bbox_wh"), "asp", e.get("bbox_aspect"),
                  "border T/B/L/R", b.get("top_px"), b.get("bottom_px"),
                  b.get("left_px"), b.get("right_px"),
                  "hit", b.get("top_hit_rate"), b.get("bottom_hit_rate"),
                  b.get("left_hit_rate"), b.get("right_hit_rate"),
                  "dark%", e.get("dark_ratio_of_opaque"))


if __name__ == "__main__":
    main()