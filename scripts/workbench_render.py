# -*- coding: utf-8 -*-
"""Render the first production-bench batch from the furniture audit page."""
import os
import subprocess
import urllib.parse

from PIL import Image, ImageDraw

ROOT = r"D:\rim mod\FirstPerson3D"
BASE = "http://127.0.0.1:8899/furniture.html"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
OUT = os.path.join(ROOT, "viewer", "shots", "workbenches")
ITEMS = ["tablebutcher", "tablesculpting", "fueledsmithy", "electricsmithy"]
VIEWS = ["45°", "主视·南", "侧视·东", "低角度"]
W, H = 900, 700


def shoot(item, view):
    name = f"{item}__{view}.png"
    path = os.path.join(OUT, name)
    query = urllib.parse.urlencode({
        "item": item,
        "view": view,
        "rot": "south",
        "pretty": "1",
        "unlit": "1",
        "v": "workbench-v2",
    })
    url = BASE + "?" + query
    subprocess.run([
        CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
        "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
        f"--window-size={W},{H}", "--virtual-time-budget=4500",
        f"--screenshot={path}", url,
    ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    return path


def sheet(paths):
    thumb_w, thumb_h = 360, 280
    pad, head = 12, 32
    out = Image.new("RGB", (len(VIEWS) * (thumb_w + pad) + pad,
                             len(ITEMS) * (thumb_h + head + pad) + pad),
                    (28, 30, 38))
    draw = ImageDraw.Draw(out)
    for c, view in enumerate(VIEWS):
        draw.text((pad + c * (thumb_w + pad), 8), view, fill=(220, 230, 245))
    for r, item in enumerate(ITEMS):
        y = pad + head + r * (thumb_h + head + pad)
        draw.text((pad, y + thumb_h + 5), item, fill=(160, 205, 255))
        for c, view in enumerate(VIEWS):
            x = pad + c * (thumb_w + pad)
            im = Image.open(paths[(item, view)]).convert("RGB")
            im.thumbnail((thumb_w, thumb_h), Image.Resampling.LANCZOS)
            tile = Image.new("RGB", (thumb_w, thumb_h), (43, 50, 66))
            tile.paste(im, ((thumb_w - im.width) // 2, (thumb_h - im.height) // 2))
            out.paste(tile, (x, y))
    p = os.path.join(OUT, "workbenches_contact_sheet_v3_unlit.png")
    out.save(p)
    return p


def main():
    os.makedirs(OUT, exist_ok=True)
    paths = {}
    for item in ITEMS:
        for view in VIEWS:
            paths[(item, view)] = shoot(item, view)
            print(paths[(item, view)])
    print(sheet(paths))


if __name__ == "__main__":
    main()
