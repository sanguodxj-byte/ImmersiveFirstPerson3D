# AI生成
# -*- coding: utf-8 -*-
# AI生成
# 用本机 8899 静态服务 + 无头 Chrome 对 wreck.html 做批量截图：
#   7 样板 x 5 视角 -> viewer/shots/<item>__<vi>.png
# 再把每样板 5 张拼成一张对照条 <item>__sheet.png，便于一次性视觉验收。
import os, subprocess, urllib.parse
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "viewer", "shots")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
BASE = "http://127.0.0.1:8899/wreck.html"
VER = "1791581401"
W, H = 1280, 900

ITEMS = ["ruinedtank", "crashedshippart", "ancientwarwalkershell",
         "largeengineblock", "giantwheela", "concretebarrier", "rustedindustrialtruck"]
VIEWS = ["45°", "俯视", "主视·南", "侧视·东", "低角度"]

os.makedirs(OUT, exist_ok=True)

def shot(url, path):
    args = [CHROME, "--headless=new", "--hide-scrollbars",
            "--use-angle=swiftshader", "--enable-unsafe-swiftshader",
            f"--window-size={W},{H}", "--virtual-time-budget=6000",
            f"--screenshot={path}", url]
    r = subprocess.run(args, capture_output=True)
    return os.path.exists(path) and os.path.getsize(path) > 2000

def make_sheet(item, files):
    ims = [Image.open(f).convert("RGB") for f in files]
    lab = 26
    sheet = Image.new("RGB", (W, (H + lab) * len(ims)), (20, 22, 30))
    d = ImageDraw.Draw(sheet)
    for i, (im, v) in enumerate(zip(ims, VIEWS)):
        y = i * (H + lab)
        sheet.paste(im, (0, y + lab))
        d.text((8, y + 7), f"{item}   {v}", fill=(207, 214, 228))
    out = os.path.join(OUT, f"{item}__sheet.png")
    sheet.save(out)
    return out

fail = []
for item in ITEMS:
    files = []
    for vi, view in enumerate(VIEWS):
        url = f"{BASE}?item={urllib.parse.quote(item)}&view={urllib.parse.quote(view)}&v={VER}"
        p = os.path.join(OUT, f"{item}__v{vi}.png")
        ok = shot(url, p)
        print("shot", item, view, "OK" if ok else "FAIL", os.path.getsize(p) if os.path.exists(p) else 0)
        if not ok:
            fail.append((item, view))
        files.append(p)
    print("sheet", make_sheet(item, files))

print("DONE fail=", fail)