# AI生成
# -*- coding: utf-8 -*-
"""单件建筑原版贴图三视图 vs 3D 渲染多角度对照图生成器。

用法:
    python scripts/bench_review.py tablebutcher
"""
import os
import subprocess
import sys
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _tmpsweep import scratch, env_for, profile, drop, sweep
import furn_atlas as FA

MOD = r"D:\rim mod\FirstPerson3D"
TEX = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
OUT = os.path.join(MOD, "docs", "furniture", "audit")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
URL = "http://127.0.0.1:8899/furniture.html"

ITEM_TO_SPEC = {
    "tablebutcher": "tablebutcher",
    "tablesculpting": "tablesculpting",
    "fueledsmithy": "tablesmithingfueled",
    "electricsmithy": "tablesmithingelectric",
    "electricstove": "tablestoveelectric",
    "fueledstove": "tablestovefueled",
    "handtailoringbench": "tabletailorhand",
    "electrictailoringbench": "tabletailorelectric",
    "tablestonecutter": "tablestonecutter",
    "tablemachining": "tablemachining",
    "druglab": "tabledruglab",
    "brewery": "tablebrewery",
    "electricsmelter": "electricsmelter",
    "simpleresearchbench": "researchbenchsimple",
    "hitechresearchbench": "researchbenchhitech",
    "fabricationbench": "fabricationbench",
}


def font(sz=15):
    for p in (r"C:\Windows\Fonts\msyh.ttc", r"C:\Windows\Fonts\simhei.ttf", r"C:\Windows\Fonts\cons.ttf"):
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, sz)
            except Exception:
                pass
    return ImageFont.load_default()


def render_one(item):
    os.makedirs(OUT, exist_ok=True)
    raw_3d = os.path.join(OUT, f"_tmp_{item}_3d.png")
    rd = scratch("benchrev")
    try:
        cmd = [
            CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
            "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
            "--window-size=1360,380", "--virtual-time-budget=20000",
            "--timeout=60000", "--user-data-dir=" + profile(rd),
            "--screenshot=" + raw_3d,
            f"{URL}?item={item}&triptych=1&pretty=1&still=1",
        ]
        subprocess.run(cmd, env=env_for(rd), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=80)
    finally:
        drop(rd)
        sweep()

    im3d = Image.open(raw_3d).convert("RGB")
    try:
        os.remove(raw_3d)
    except OSError:
        pass

    spec_key = ITEM_TO_SPEC.get(item, item)
    spec = FA.SPEC.get(spec_key, {})

    W, H = 1360, 660
    canvas = Image.new("RGB", (W, H), (24, 26, 33))
    d = ImageDraw.Draw(canvas)
    f_title = font(16)
    f_sub = font(14)

    d.text((14, 8), f"{item} ({spec_key})  上: 原版贴图三视图 (south / east / north)   下: 3D 建模四视角 (南45 / 南正视 / 东45 / 北45)",
           fill=(220, 230, 245), font=f_title)

    views = ["south", "east", "north"]
    slot_w = W // 3
    for idx, v in enumerate(views):
        if v not in spec:
            continue
        rel = spec[v]["path"]
        p = os.path.join(TEX, rel.replace("/", os.sep))
        if not os.path.exists(p):
            continue
        src = Image.open(p).convert("RGBA")
        bg = Image.new("RGBA", src.size, (43, 50, 66, 255))
        bg.alpha_composite(src)
        rgb = bg.convert("RGB")
        sc = min((slot_w - 40) / rgb.width, 200 / rgb.height)
        nw, nh = max(1, int(rgb.width * sc)), max(1, int(rgb.height * sc))
        rgb = rgb.resize((nw, nh), Image.NEAREST)
        ox = idx * slot_w + (slot_w - nw) // 2
        oy = 42 + (200 - nh) // 2
        canvas.paste(rgb, (ox, oy))
        d.text((idx * slot_w + 18, 38), f"Vanilla {v}", fill=(140, 210, 255), font=f_sub)

    canvas.paste(im3d, (0, 268))
    labels = ["3D south 45", "3D south front", "3D east 45", "3D north 45"]
    cw = W // 4
    for i, lbl in enumerate(labels):
        d.text((i * cw + 14, 248), lbl, fill=(160, 235, 190), font=f_sub)

    out_path = os.path.join(OUT, f"{item}_review.png")
    canvas.save(out_path)
    print(out_path)
    return out_path


if __name__ == "__main__":
    for a in sys.argv[1:]:
        render_one(a)
