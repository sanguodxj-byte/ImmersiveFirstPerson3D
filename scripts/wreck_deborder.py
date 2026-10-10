# AI生成
# -*- coding: utf-8 -*-
"""残骸贴图程序去黑边（逐层剥离法）。

定义（可复现）：描边 = 近黑像素(max(r,g,b) < T) 且 与「边界」相邻。
边界 = 透明像素(alpha<=A_THRESH) 或 出图边界（图像外框外视为透明）。
剥离：从边界向内逐层剥掉近黑像素，最多 KMAX 层；某层无可剥则提前停止。
擦除方式：alpha 置 0。内部暗部纹理（不贴边）不受影响。

输出：docs/wreckage/clean/<name>.png + clean_report.json / _stdout.txt
运行：
  C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_deborder.py
"""
import os
import sys
import json

try:
    from PIL import Image
except Exception as e:  # noqa
    print("PIL missing:", e)
    sys.exit(2)

ROOT = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
OUT_DIR = r"D:\rim mod\FirstPerson3D\docs\wreckage\clean"

A_THRESH = 40      # alpha<=此值视为透明
T = 70             # 近黑阈值 max(r,g,b) < T
KMAX = 16          # 最多剥离层数（自适应，通常提前停止）
STOP_FRAC = 0.5    # 当某层边界近黑占比 < 此值时停止剥离

SAMPLES = [
    "ruinedtank.png",
    "crashedshippart.png",
    "ancientwarwalkershell.png",
    "largeengineblock.png",
    "giantwheela.png",
    "concretebarrier.png",
    "rustedindustrialtruck_south.png",
    "rustedindustrialtruck_east.png",
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
    """该像素是否与边界相邻（4 邻域有透明像素或出图）。"""
    for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        nx, ny = x + dx, y + dy
        if not in_bounds(nx, ny, w, h):
            return True
        if px[nx, ny][3] <= A_THRESH:
            return True
    return False


def process(path, out_path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    layers = []
    total = 0
    for _ in range(KMAX):
        bnd = 0
        targets = []
        for y in range(h):
            for x in range(w):
                r, g, b, a = px[x, y]
                if a > A_THRESH and at_boundary(px, w, h, x, y):
                    bnd += 1
                    if max(r, g, b) < T:
                        targets.append((x, y))
        frac = (len(targets) / bnd) if bnd else 0.0
        if bnd == 0 or frac < STOP_FRAC:
            break
        for (x, y) in targets:
            r, g, b, a = px[x, y]
            px[x, y] = (r, g, b, 0)
        layers.append(len(targets))
        total += len(targets)
    im.save(out_path)
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] > A_THRESH:
                x0 = min(x0, x); x1 = max(x1, x)
                y0 = min(y0, y); y1 = max(y1, y)
    bb = [x0, y0, x1, y1] if x1 >= 0 else None
    nw = x1 - x0 + 1 if x1 >= 0 else 0
    nh = y1 - y0 + 1 if y1 >= 0 else 0
    return {
        "src": path, "dst": out_path, "size": [w, h],
        "layers": layers, "erased_px": total,
        "bbox_after": bb, "bbox_wh_after": [nw, nh],
        "bbox_aspect_after": round(nw / nh, 3) if nh else None,
    }


def main():
    idx = index_pngs(ROOT)
    os.makedirs(OUT_DIR, exist_ok=True)
    rep = {}
    for f in SAMPLES:
        p = idx.get(f.lower())
        if not p:
            rep[f] = {"error": "not found"}
            continue
        try:
            rep[f] = process(p, os.path.join(OUT_DIR, f))
        except Exception as e:  # noqa
            rep[f] = {"error": repr(e)}
    jp = os.path.join(OUT_DIR, "clean_report.json")
    with open(jp, "w", encoding="utf-8") as fh:
        json.dump(rep, fh, ensure_ascii=False, indent=2)
    print("WROTE", jp)
    for name, r in rep.items():
        if "error" in r:
            print(name, "ERR", r["error"]); continue
        print(name, "size", r["size"], "layers", r["layers"],
              "erased", r["erased_px"],
              "bbox_after", r["bbox_wh_after"], "asp", r["bbox_aspect_after"])


if __name__ == "__main__":
    main()