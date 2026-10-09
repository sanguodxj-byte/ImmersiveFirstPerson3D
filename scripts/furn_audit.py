# AI生成
# -*- coding: utf-8 -*-
"""家具全角度全变体视觉审核：120 张渲染 -> 每件一张 4x6 拼版表。

行 = 朝向 south/north/east/west；列 = 机位 45°/俯视/主视·南/侧视·东/低角度/贴脸。
产物 docs/furniture/audit/<item>.png，供逐格目视判读（禁止用数值统计代替看图）。
"""
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageDraw, ImageFont

MOD = r"D:\rim mod\FirstPerson3D"
RAW = os.path.join(MOD, "docs", "furniture", "audit", "raw")
OUT = os.path.join(MOD, "docs", "furniture", "audit")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
PORT = "http://127.0.0.1:8899/furniture.html"

ITEMS = ["bed", "stool", "endtable", "dresser", "shelf"]
ROTS = ["south", "north", "east", "west"]
VIEWS = ["45°", "俯视", "主视·南", "侧视·东", "低角度", "贴脸"]
CELL = (620, 500)


def url(item, rot, view):
    from urllib.parse import quote
    return "%s?item=%s&rot=%s&view=%s" % (PORT, item, rot, quote(view))


def shoot(job):
    item, rot, view = job
    p = os.path.join(RAW, "%s__%s__%s.png" % (item, rot, view.replace("·", "_").replace("°", "")))
    if os.path.exists(p) and os.path.getsize(p) > 8000:
        return p
    cmd = [CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
           "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
           "--window-size=%d,%d" % CELL, "--virtual-time-budget=25000",
           "--timeout=40000", "--screenshot=" + p, url(item, rot, view)]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=90)
    return p


def sheet(item):
    cols, rows = len(VIEWS), len(ROTS)
    W, H = CELL[0] * cols, CELL[1] * rows + 26
    canvas = Image.new("RGBA", (W, H), (28, 30, 38, 255))
    d = ImageDraw.Draw(canvas)
    try:
        f = ImageFont.truetype(r"C:\Windows\Fonts\cons.ttf", 18)
    except Exception:
        f = ImageFont.load_default()
    for c, v in enumerate(VIEWS):
        d.text((c * CELL[0] + 6, 4), v, fill=(207, 214, 228, 255), font=f)
    for r, rot in enumerate(ROTS):
        d.text((2, 26 + r * CELL[1] + 4), rot, fill=(127, 209, 255, 255), font=f)
        for c, v in enumerate(VIEWS):
            p = os.path.join(RAW, "%s__%s__%s.png" % (item, rot, v.replace("·", "_").replace("°", "")))
            if not os.path.exists(p):
                continue
            im = Image.open(p).convert("RGBA")
            if im.size != CELL:
                im = im.resize(CELL, Image.NEAREST)
            canvas.paste(im, (c * CELL[0], 26 + r * CELL[1]))
    small = canvas.resize((W // 2, H // 2), Image.LANCZOS)
    path = os.path.join(OUT, "%s_sheet.png" % item)
    small.save(path)
    return path


def main():
    os.makedirs(RAW, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    jobs = [(i, r, v) for i in ITEMS for r in ROTS for v in VIEWS]
    print("shots:", len(jobs))
    with ThreadPoolExecutor(max_workers=4) as ex:
        list(ex.map(shoot, jobs))
    for i in ITEMS:
        print("sheet:", sheet(i))


if __name__ == "__main__":
    main()
