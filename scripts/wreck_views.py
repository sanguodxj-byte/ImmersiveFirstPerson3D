# AI生成
# -*- coding: utf-8 -*-
"""地图野生建筑 / 残骸 原版三视图对照表 —— 供逐像素建模与 UV 判读。

原版三视图 = 物件 south/east/north 朝向图（非正俯视，而是带俯角的方向图）。
每张输出 = 该物件现存全部朝向横向并排，最近邻放大、透明底铺品红、
叠加源图像素网格与行列刻度，并写入 manifest.json。

用法:
    python scripts/wreck_views.py              # 全部残骸对象
    python scripts/wreck_views.py ruinedtank   # 只出指定条目
    python scripts/wreck_views.py --scale=6    # 强制放大倍数

输出:
    docs/wreckage/views/<item>.png
    docs/wreckage/manifest.json
"""
import json
import os
import sys
from collections import OrderedDict

import numpy as np
from PIL import Image, ImageDraw, ImageFont

TEX = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
OUT = r"D:\rim mod\FirstPerson3D\docs\wreckage\views"
MANIFEST = r"D:\rim mod\FirstPerson3D\docs\wreckage\manifest.json"
PIX_PER_CELL = 128  # 项目约定：128px == 1 格 (Metrics3D / HeightConvention)

RUINS = "Core/textures/things/building/ruins"
EXO = "Core/textures/things/building/exotic"
ODY = "Odyssey/textures/things/building/ancient/shipping"
BIO = "Biotech/textures/things/building/ruins"

# 分组 -> (递归扫的目录, 显式单文件 [(目录, 词干)])
PLAN = [
    ("载具残骸", [RUINS + "/rustedcars"],
     [(RUINS, "ruinedapc"), (RUINS, "ruinedtank"), (RUINS, "podcar"),
      (RUINS, "rustedcarframe"), (RUINS, "rustedmilitaryjeep"), (RUINS, "rustedtruck"),
      (ODY, "rustedforklift"), (ODY, "rustedindustrialtruck")]),
    ("飞船残骸", [EXO, EXO + "/shipchunk"],
     [(RUINS, "ancientrusteddropship"), (RUINS, "ancientdropshipengine"),
      (RUINS, "ancientbustedshipbeacon")]),
    ("战争机器残骸", [RUINS + "/ancientmechdetritus", RUINS + "/destroyedmechdropbeacon", BIO],
     [(RUINS, "ancientwarspiderremains"), (RUINS, "ancientwarsprinterremains"),
      (RUINS, "ancientwarwalkerclaw"), (RUINS, "ancientwarwalkerfoot"),
      (RUINS, "ancientwarwalkerleg"), (RUINS, "ancientwarwalkershell"),
      (RUINS, "ancientwarwalkertorso")]),
    ("引擎机械残骸", [RUINS + "/rustedengineblock", RUINS + "/ancientgenerator",
                 RUINS + "/ancientsecurityturret", RUINS + "/ancientmegacannontripod"],
     [(RUINS, "largeengineblock"), (RUINS, "jetengine")]),
    ("轮件杂物", [RUINS + "/giantwheel", RUINS + "/wheel",
               RUINS + "/rustedwashingmachine", RUINS + "/shoppingcart"], []),
    ("废墟路障管件", [RUINS + "/ancientbarrel", RUINS + "/ancientconcretebarrier",
                RUINS + "/ancientcrate", RUINS + "/ancientfuelnode", RUINS + "/ancientlamppost",
                RUINS + "/ancientmilitarycrate", RUINS + "/ancientpipes",
                RUINS + "/ancientrazorwire", RUINS + "/ancientspacercrate",
                RUINS + "/ancienttanktrap"],
     [(RUINS, "concretebarrier"), (RUINS, "cratelarge"), (RUINS, "cratelong"),
      (RUINS, "cratesmall"), (RUINS, "pipelinesection"),
      (RUINS, "rustedbarbedwirefence_atlas")]),
    ("废墟箱桶吊机", [ODY], []),
]

ORI_ORDER = {"south": 0, "east": 1, "north": 2, "west": 3, "view": 4}


def split_ori(stem):
    for suf in ("_east", "_north", "_south", "_west"):
        if stem.endswith(suf):
            return stem[:-len(suf)], suf[1:]
    return stem, "view"


def rel_of(path):
    return os.path.relpath(path, TEX).replace(os.sep, "/")


def scan_dir(items, rel_dir):
    base = os.path.join(TEX, rel_dir.replace("/", os.sep))
    if not os.path.isdir(base):
        return
    for root, _, files in os.walk(base):
        for fn in sorted(files):
            if not fn.endswith(".png") or fn.endswith("_menuicon.png"):
                continue
            stem, tag = split_ori(fn[:-4])
            items.setdefault(stem, {})[tag] = rel_of(os.path.join(root, fn))


def scan_file(items, rel_dir, stem):
    for suf in ("", "_south", "_east", "_north", "_west"):
        p = os.path.join(TEX, (rel_dir + "/" + stem + suf + ".png").replace("/", os.sep))
        if os.path.exists(p):
            items.setdefault(stem, {})[suf[1:] if suf else "view"] = rel_of(p)


def build_items():
    items, groups = OrderedDict(), {}
    for grp, dirs, files in PLAN:
        for d in dirs:
            before = set(items)
            scan_dir(items, d)
            for k in items:
                if k not in before:
                    groups.setdefault(k, grp)
        for rd, stem in files:
            if stem not in items:
                scan_file(items, rd, stem)
            groups.setdefault(stem, grp)
    return items, groups


BG = (255, 0, 255, 255)
GAP, HEAD, SCALE_TARGET, SCALE_FORCE = 26, 34, 460, 0


def font(size=13):
    for p in (r"C:\Windows\Fonts\cons.ttf", r"C:\Windows\Fonts\arial.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except Exception:
            continue
    return ImageFont.load_default()


def tile(rel):
    path = os.path.join(TEX, rel.replace("/", os.sep))
    if not os.path.exists(path):
        return None, None, None
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
    big = np.repeat(np.repeat(px, scale, 0), scale, 1)
    im2 = Image.fromarray(big, "RGBA")
    d = ImageDraw.Draw(im2)
    f = font(11)
    for gx in range(0, w + 1, grid):
        d.line([(gx * scale, 0), (gx * scale, h * scale)],
               fill=(0, 255, 255, 170 if gx % (4 * grid) == 0 else 90), width=1)
    for gy in range(0, h + 1, grid):
        d.line([(0, gy * scale), (w * scale, gy * scale)],
               fill=(0, 255, 255, 170 if gy % (4 * grid) == 0 else 90), width=1)
    for gy in range(0, h, grid):
        if (gy // grid) % 2 == 0:
            d.text((2, gy * scale + 1), str(gy), fill=(255, 255, 0, 255), font=f)
    for gx in range(0, w, grid):
        if (gx // grid) % 2 == 0:
            d.text((gx * scale + 1, 2), str(gx), fill=(120, 255, 255, 255), font=f)
    return np.asarray(im2), (w, h, scale), grid


def sheet(name, views):
    tiles = []
    for tag in sorted(views, key=lambda t: ORI_ORDER.get(t, 9)):
        big, dim, grid = tile(views[tag])
        if big is None:
            print("  MISSING", views[tag])
            continue
        tiles.append((tag, big, dim, grid))
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
    for tag, big, (w, h, sc), grid in tiles:
        im.paste(Image.fromarray(big, "RGBA"), (x, HEAD))
        d.text((x + 2, 6), f"{name}_{tag}  src {w}x{h}  x{sc}  grid {grid}px",
               fill=(207, 214, 228, 255), font=f)
        x += big.shape[1] + GAP
    d.text((4, row_h + HEAD + 2), "数字=源图像素坐标(左上0)  品红=透明  青线=像素网格",
           fill=(150, 160, 180, 255), font=font(12))
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, f"{name}.png")
    im.save(p)
    return p


def main():
    global SCALE_FORCE
    args = list(sys.argv[1:])
    for k in list(args):
        if k.startswith("--scale"):
            SCALE_FORCE = int(k.split("=", 1)[1]) if "=" in k else int(args[args.index(k) + 1])
            args.remove(k)
    want = set(a.lower() for a in args if not a.isdigit())
    items, groups = build_items()
    names = [n for n in items if not want or n.lower() in want]
    os.makedirs(OUT, exist_ok=True)
    manifest = []
    for n in names:
        p = sheet(n, items[n])
        print("sheet:", p)
        views = OrderedDict()
        for tag in sorted(items[n], key=lambda t: ORI_ORDER.get(t, 9)):
            rel = items[n][tag]
            ap = os.path.join(TEX, rel.replace("/", os.sep))
            if os.path.exists(ap):
                with Image.open(ap) as im:
                    w, h = im.size
                views[tag] = {"tex": rel, "px": [w, h],
                              "cell": [round(w / PIX_PER_CELL, 4), round(h / PIX_PER_CELL, 4)]}
        manifest.append({"name": n, "group": groups.get(n, ""), "views": views})
    with open(MANIFEST, "w", encoding="utf-8") as f:
        json.dump({"pix_per_cell": PIX_PER_CELL, "count": len(manifest), "items": manifest},
                  f, ensure_ascii=False, indent=2)
    print("manifest:", MANIFEST, len(manifest), "items")


if __name__ == "__main__":
    main()