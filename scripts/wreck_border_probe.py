# AI生成
# -*- coding: utf-8 -*-
"""描边厚度量化探针：逐层剥离并统计每层「边界相邻不透明像素数 / 其中近黑数」。

不落盘图像，仅打印表格，用于确定去黑边应移除的层数。
运行：
  C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_border_probe.py
"""
import os
import sys
try:
    from PIL import Image
except Exception as e:  # noqa
    print("PIL missing:", e); sys.exit(2)

ROOT = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
A_THRESH = 40
T = 70
KMAX = 8

SAMPLES = [
    "ruinedtank.png", "crashedshippart.png", "ancientwarwalkershell.png",
    "largeengineblock.png", "giantwheela.png", "concretebarrier.png",
    "rustedindustrialtruck_south.png", "rustedindustrialtruck_east.png",
    "rustedindustrialtruck_north.png",
]


def index_pngs(root):
    idx = {}
    for dp, _dn, fn in os.walk(root):
        for f in fn:
            if f.lower().endswith(".png"):
                idx.setdefault(f.lower(), os.path.join(dp, f))
    return idx


def in_bounds(x, y, w, h):
    return 0 <= x < w and 0 <= y < h


def at_boundary(px, w, h, x, y):
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if not in_bounds(nx, ny, w, h):
            return True
        if px[nx, ny][3] <= A_THRESH:
            return True
    return False


def probe(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    rows = []
    for _ in range(KMAX):
        bnd = 0
        dark = []
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                if a > A_THRESH and at_boundary(px, w, h, x, y):
                    bnd += 1
                    if max(r, g, b) < T:
                        dark.append((x, y))
        frac = (len(dark) / bnd) if bnd else 0.0
        for (x, y) in dark:
            r, g, b, a = px[x, y]
            px[x, y] = (r, g, b, 0)
        rows.append((bnd, len(dark), round(frac, 3)))
        if not dark:
            break
    return rows


def main():
    idx = index_pngs(ROOT)
    for f in SAMPLES:
        p = idx.get(f.lower())
        if not p:
            print(f, "NOT FOUND"); continue
        rows = probe(p)
        print("==", f)
        for i, (bnd, dark, frac) in enumerate(rows, 1):
            print("   L%d boundary=%d dark=%d frac=%.3f" % (i, bnd, dark, frac))
        if len(rows) == KMAX:
            print("   (still dark at L%d -> outline thicker than %d px)" % (KMAX, KMAX))


if __name__ == "__main__":
    main()