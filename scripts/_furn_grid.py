# AI生成
# -*- coding: utf-8 -*-
"""标定看图台：把源图放大 + 像素网格存成 PNG，直接在图上读取样区坐标。

用法：python scripts/_furn_grid.py couch.doublebed.south ...
      （不带参数 = 床家族 + couch + diningchair 全出）
输出：docs/furniture/fit/<item>_<view>.png
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furn_atlas as FA          # noqa: E402

OUT = os.path.join(FA.MOD, "docs", "furniture", "fit")
F = "Core/textures/things/building/furniture"
FB = F + "/bed"

DEFAULT = [f"couch.{v}" for v in ("south", "east", "north")] + \
          [f"doublebed.{v}" for v in ("south", "east")] + \
          [f"royalbed.{v}" for v in ("south", "east")] + \
          [f"hospitalbed.{v}" for v in ("south", "east")] + \
          [f"bedroll.{v}" for v in ("south", "east")] + \
          [f"bedrolldouble.south", "sleepspot.south", "doublesleepspot.south",
           "diningchair.south", "diningchair.east"]


def grid(src, scale=2, step=16):
    h, w = src.shape[:2]
    img = Image.fromarray(src).resize((w * scale, h * scale), Image.NEAREST)
    px = img.load()
    for x in range(0, w, step):
        for y in range(h * scale):
            i = x * scale
            if i < w * scale:
                px[i, y] = (0, 255, 0, 90) if px[i, y][3] < 200 else (0, 200, 0, 255)
    for y in range(0, h, step):
        for x in range(w * scale):
            j = y * scale
            if j < h * scale:
                px[x, j] = (0, 255, 0, 90) if px[x, j][3] < 200 else (0, 200, 0, 255)
    base = Image.new("RGBA", img.size, (30, 30, 34, 255))
    base.alpha_composite(img)
    dr = ImageDraw.Draw(base)
    f = FA.font(10)
    for x in range(0, w, step * 2):
        dr.text((x * scale + 1, 1), str(x), fill=(255, 240, 0, 255), font=f)
    for y in range(step, h, step * 2):
        dr.text((1, y * scale + 1), str(y), fill=(255, 240, 0, 255), font=f)
    return np.asarray(base)


def main():
    os.makedirs(OUT, exist_ok=True)
    for spec in (sys.argv[1:] or DEFAULT):
        item, view = spec.split(".")
        src = FA.load(find(item, view))
        sc = 4 if max(src.shape[:2]) <= 64 else (2 if max(src.shape[:2]) <= 128 else 1)
        p = os.path.join(OUT, f"{item}_{view}.png")
        Image.fromarray(grid(src, sc)).save(p)
        print(f"{p}  {src.shape[1]}x{src.shape[0]} scale x{sc}")


DIRS = (F, FB, F + "/walllamp", F + "/stelelarge", F + "/bookcase",
        "Core/textures/things/building/misc",
        "Royalty/textures/things/building/furniture")


def find(item, view):
    for d in DIRS:
        p = f"{d}/{item}_{view}.png"
        if os.path.exists(os.path.join(FA.TEX, p.replace("/", os.sep))):
            return p
    for d in DIRS:                       # 单图型（Graphic_Single）：没有朝向后缀
        p = f"{d}/{item}.png"
        if os.path.exists(os.path.join(FA.TEX, p.replace("/", os.sep))):
            return p
    raise FileNotFoundError(f"{item}_{view}.png 不在 {' '.join(DIRS)}")


if __name__ == "__main__":
    main()
