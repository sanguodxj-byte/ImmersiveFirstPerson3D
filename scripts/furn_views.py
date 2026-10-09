# -*- coding: utf-8 -*-
"""家具原版三视图放大对照表 —— 供逐像素视觉判读 UV 区段与几何。

用法:
    python scripts/furn_views.py            # 全部家具原型
    python scripts/furn_views.py bed chair  # 只出指定条目

输出 docs/furniture/views/<item>.png：
    每张 = 该物件的 south / east / north 三视图横向并排，
    最近邻放大、透明底铺成品红、叠加源图像素网格与行列刻度。
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

TEX = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
OUT = r"D:\rim mod\FirstPerson3D\docs\furniture\views"

F = r"Core/textures/things/building/furniture"
FB = F + "/bed"
BK = F + "/bookcase"
WL = F + "/walllamp"

# item -> [(朝向, 相对 TEX 的贴图路径)]；只列出原版实际存在的朝向
ITEMS = {
    "bed":        [(d, f"{FB}/bed_{d}.png") for d in ("south", "east", "north")],
    "doublebed":  [(d, f"{FB}/doublebed_{d}.png") for d in ("south", "east", "north")],
    "royalbed":   [(d, f"{FB}/royalbed_{d}.png") for d in ("south", "east", "north")],
    "hospitalbed": [(d, f"{FB}/hospitalbed_{d}.png") for d in ("south", "east", "north")],
    "bedroll":    [(d, f"{FB}/bedroll_{d}.png") for d in ("south", "east", "north")],
    "sleepspot":  [(d, f"{FB}/sleepspot_{d}.png") for d in ("south", "east", "north")],
    "diningchair": [(d, f"{F}/diningchair_{d}.png") for d in ("south", "east", "north")],
    "armchair":   [(d, f"{F}/armchair_{d}.png") for d in ("south", "east", "north")],
    "couch":      [(d, f"{F}/couch_{d}.png") for d in ("south", "east", "north")],
    "stool":      [("east", f"{F}/stool_east.png"), ("north", f"{F}/stool_north.png")],
    "table1x2":   [("east", f"{F}/table1x2_east.png"), ("north", f"{F}/table1x2_north.png")],
    "table2x2":   [("north", f"{F}/table2x2_north.png")],
    "table2x4":   [("east", f"{F}/table2x4_east.png"), ("north", f"{F}/table2x4_north.png")],
    "table3x3":   [("north", f"{F}/table3x3_north.png")],
    "endtable":   [(d, f"{F}/endtable_{d}.png") for d in ("south", "east", "north")],
    "dresser":    [(d, f"{F}/dresser_{d}.png") for d in ("south", "east", "north")],
    "shelf":      [(d, f"{F}/shelf_{d}.png") for d in ("south", "east", "north")],
    "shelfsmall": [(d, f"{F}/shelfsmall_{d}.png") for d in ("south", "east", "north")],
    "bookcase":   [(d, f"{BK}/bookcase_{d}.png") for d in ("south", "east", "north")],
    "bookcasesmall": [(d, f"{BK}/bookcasesmall_{d}.png") for d in ("south", "east", "north")],
    "lampstanding": [("menu", f"{F}/lampstanding.png")],
    "floodlight": [("menu", f"{F}/floodlight.png")],
    "walllamp":   [(d, f"{WL}/walllamp_{d}.png") for d in ("south", "east", "north")],
    "plantpot":   [("menu", f"{F}/plantpot.png")],
    "column":     [("menu", f"{F}/column.png")],
    "throne":     [(d, f"Royalty/textures/things/building/furniture/throne_{d}.png")
                   for d in ("south", "east", "north")],
    "grandthrone": [(d, f"Royalty/textures/things/building/furniture/grandthrone_{d}.png")
                    for d in ("south", "east", "north")],
    "drape":      [(d, f"Royalty/textures/things/building/furniture/drape_{d}.png")
                   for d in ("south", "east")],
}

BG = (255, 0, 255, 255)       # 透明区铺成品红，一眼区分"无像素"与"深色像素"
GAP = 26
HEAD = 34
SCALE_TARGET = 460            # 单个视图放大后的目标边长
SCALE_FORCE = 0               # >0 时强制倍数（--scale 8）
ONE_PER_FILE = False          # True 时每个朝向单独出图（--split）


def font(size=13):
    for p in (r"C:\Windows\Fonts\cons.ttf", r"C:\Windows\Fonts\arial.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except Exception:
            continue
    return ImageFont.load_default()


def upsample(px, scale, grid):
    """最近邻放大 + 源图像素网格 + 行列刻度。返回 (ndarray, 左刻度宽, 上刻度高)"""
    h, w = px.shape[:2]
    big = np.repeat(np.repeat(px, scale, 0), scale, 1)
    im = Image.fromarray(big, "RGBA")
    d = ImageDraw.Draw(im)
    f = font(11)
    # 网格：每 grid 个源像素一条细线，每 4*grid 一条粗线
    for gx in range(0, w + 1, grid):
        major = (gx % (4 * grid) == 0)
        d.line([(gx * scale, 0), (gx * scale, h * scale)],
               fill=(0, 255, 255, 90 if not major else 170), width=1)
    for gy in range(0, h + 1, grid):
        major = (gy % (4 * grid) == 0)
        d.line([(0, gy * scale), (w * scale, gy * scale)],
               fill=(0, 255, 255, 90 if not major else 170), width=1)
    # 刻度数字（写在图内左上角一列，避免额外留白）
    for gy in range(0, h, grid):
        if (gy // grid) % 2 == 0:
            d.text((2, gy * scale + 1), str(gy), fill=(255, 255, 0, 255), font=f)
    for gx in range(0, w, grid):
        if (gx // grid) % 2 == 0:
            d.text((gx * scale + 1, 2), str(gx), fill=(120, 255, 255, 255), font=f)
    return np.asarray(im), 0, 0


def sheet(name, views):
    tiles = []
    for tag, rel in views:
        path = os.path.join(TEX, rel.replace("/", os.sep))
        if not os.path.exists(path):
            print("  MISSING", path)
            continue
        im = Image.open(path).convert("RGBA")
        w, h = im.size
        base = np.zeros((h, w, 4), np.uint8)
        base[:, :] = BG
        src = np.asarray(im).astype(np.int32)
        a = src[:, :, 3:4]
        rgb = (src[:, :, :3] * a + base[:, :, :3].astype(np.int32) * (255 - a)) // 255
        px = np.dstack([rgb.astype(np.uint8), np.full((h, w, 1), 255, np.uint8)])
        scale = SCALE_FORCE or max(2, min(12, int(round(SCALE_TARGET / max(w, h)))))
        grid = max(1, min(w, h) // 32)
        big, _, _ = upsample(px, scale, grid)
        tiles.append((tag, big, (w, h, scale, grid)))

    if not tiles:
        return None
    row_h = max(t[1].shape[0] for t in tiles)
    total_w = sum(t[1].shape[1] for t in tiles) + GAP * (len(tiles) - 1)
    canvas = np.zeros((row_h + HEAD + 18, total_w, 4), np.uint8)
    canvas[:, :] = (28, 30, 38, 255)
    im = Image.fromarray(canvas, "RGBA")
    d = ImageDraw.Draw(im)
    f = font(15)
    x = 0
    for tag, big, (w, h, scale, grid) in tiles:
        tile = Image.fromarray(big, "RGBA")
        im.paste(tile, (x, HEAD))
        d.text((x + 2, 6), f"{name}_{tag}  src {w}x{h}  x{scale}  grid {grid}px",
               fill=(207, 214, 228, 255), font=f)
        x += big.shape[1] + GAP
    d.text((4, row_h + HEAD + 2), "数字=源图像素坐标(左上角为0)  品红=透明  青线=像素网格",
           fill=(150, 160, 180, 255), font=font(12))
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, f"{name}.png")
    im.save(p)
    return p


def tile_of(rel):
    """读贴图 -> (放大带网格的 ndarray, 'w x h x scale grid')"""
    path = os.path.join(TEX, rel.replace("/", os.sep))
    if not os.path.exists(path):
        print("  MISSING", path)
        return None, None
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    base = np.zeros((h, w, 4), np.uint8)
    base[:, :] = BG
    src = np.asarray(im).astype(np.int32)
    a = src[:, :, 3:4]
    rgb = (src[:, :, :3] * a + base[:, :, :3].astype(np.int32) * (255 - a)) // 255
    px = np.dstack([rgb.astype(np.uint8), np.full((h, w, 1), 255, np.uint8)])
    scale = SCALE_FORCE or max(2, min(12, int(round(SCALE_TARGET / max(w, h)))))
    grid = max(1, min(w, h) // 32)
    big, _, _ = upsample(px, scale, grid)
    return big, f"src {w}x{h} x{scale} grid {grid}px"


def main():
    global SCALE_FORCE, ONE_PER_FILE
    args = [a for a in sys.argv[1:]]
    for k in list(args):
        if k.startswith("--scale"):
            SCALE_FORCE = int(k.split("=", 1)[1]) if "=" in k else int(args[args.index(k) + 1])
            args.remove(k)
        elif k == "--split":
            ONE_PER_FILE = True
            args.remove(k)
    want = set(a.lower() for a in args if not a.isdigit())
    names = [k for k in ITEMS if not want or k in want]
    os.makedirs(OUT, exist_ok=True)
    for n in names:
        if ONE_PER_FILE:
            for tag, rel in ITEMS[n]:
                big, info = tile_of(rel)
                if big is None:
                    continue
                im = Image.fromarray(big, "RGBA")
                p = os.path.join(OUT, f"{n}_{tag}.png")
                im.save(p)
                print("sheet:", p, info)
        else:
            p = sheet(n, ITEMS[n])
            print("sheet:", p)


if __name__ == "__main__":
    main()
