# AI生成
# -*- coding: utf-8 -*-
"""家具全角度全变体视觉审核：每件 4 朝向 x 6 机位 -> 每件一张 4x6 拼版表。

行 = 朝向 south/north/east/west；列 = 机位 45°/俯视/主视·南/侧视·东/低角度/贴脸。
产物 docs/furniture/audit/<item>.png，供逐格目视判读（禁止用数值统计代替看图）。
"""
import os
import glob
import shutil
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageDraw, ImageFont
import numpy as np

MOD = r"D:\rim mod\FirstPerson3D"
RAW = os.path.join(MOD, "docs", "furniture", "audit", "raw")
OUT = os.path.join(MOD, "docs", "furniture", "audit")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
PORT = "http://127.0.0.1:8899/furniture.html"
# swiftshader 软渲染很慢：本机同时跑 4 个 Chrome 时会超 40s 不出图（2026-10-10 实测 21/24 格空手而归）
WORKERS = int(os.environ.get("FURN_AUDIT_WORKERS", "2"))
SHOT_TIMEOUT_MS = 90000

ITEMS = ["bed", "stool", "endtable", "dresser", "shelf", "armchair"]
ROTS = ["south", "north", "east", "west"]
VIEWS = ["45°", "俯视", "主视·南", "侧视·东", "低角度", "贴脸"]
CELL = (620, 500)


def url(item, rot, view):
    from urllib.parse import quote
    return "%s?item=%s&rot=%s&view=%s" % (PORT, item, rot, quote(view))


def _crop_model(im):
    """裁到模型本体：HUD 在顶部 80px，背景是固定深色。"""
    a = np.asarray(im).astype(int)
    m = (np.abs(a - np.array([28, 30, 38])).sum(axis=2) > 40)
    m[:112, :] = False      # HUD 最高约 110px，裁掉免得混进近景
    ys, xs = np.where(m)
    if len(ys) == 0:
        return im
    return im.crop((max(0, xs.min() - 6), max(0, ys.min() - 6),
                    min(im.width, xs.max() + 6), min(im.height, ys.max() + 6)))


def is_blank(p):
    """页面抛异常时 HUD 会写 ERR，画面其余部分是纯背景 —— 判"没渲染出东西"。
    只看下半幅（模型必然占到的区域），阈值给足余量，不误伤大件/小件。"""
    im = np.asarray(Image.open(p).convert("RGB"))
    h, w = im.shape[:2]
    low = im[int(h * 0.45):, :, :]
    bg = np.abs(low.astype(int) - np.array([28, 30, 38])).sum(axis=2) <= 12
    return float(bg.mean()) > 0.98


def shoot(job):
    item, rot, view = job
    p = os.path.join(RAW, "%s__%s__%s.png" % (item, rot, view.replace("·", "_").replace("°", "")))
    if os.path.exists(p) and os.path.getsize(p) > 8000:
        return p, ("整幅空白（页面异常或模型没画出来）" if is_blank(p) else None)
    # profile 必须**每格一个**：多个 Chrome 共用一个 user-data-dir 会互相抢锁，
    # 结果是静默不出图（2026-10-10 实测 24 格里 21 格空手而归）。
    prof = tempfile.mkdtemp(prefix="furnshot-")
    cmd = [CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
           "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
           "--window-size=%d,%d" % CELL, "--virtual-time-budget=25000",
           "--timeout=%d" % SHOT_TIMEOUT_MS,
           "--user-data-dir=" + prof,
           "--screenshot=" + p, url(item, rot, view)]
    try:
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                       timeout=SHOT_TIMEOUT_MS / 1000.0 + 40)
    finally:
        shutil.rmtree(prof, ignore_errors=True)
    if not os.path.exists(p):
        return p, "没出图"
    return p, ("整幅空白（页面异常或模型没画出来）" if is_blank(p) else None)


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


def clean_scoped_tmp():
    tmp = os.environ.get("TEMP") or os.environ.get("TMP") or ""
    for d in glob.glob(os.path.join(tmp, "scoped_dir*")):
        shutil.rmtree(d, ignore_errors=True)


def overview():
    """总览：每件 x (45° / 低角度 / 俯视) 裁到模型近景，拼一张给人看整体观感。
    拼版表看的是逐格判读，这张看的是"摆在一起像不像一套家具"。"""
    views = ["45°", "低角度", "俯视"]
    CW, CH, LBL = 400, 300, 22
    sheet = Image.new("RGB", (CW * len(views) + 8 * (len(views) + 1),
                              (CH + LBL) * len(ITEMS) + 8), (24, 26, 33))
    d = ImageDraw.Draw(sheet)
    try:
        f = ImageFont.truetype(r"C:\Windows\Fonts\cons.ttf", 15)
    except Exception:
        f = ImageFont.load_default()
    for r, it in enumerate(ITEMS):
        for c, v in enumerate(views):
            p = os.path.join(RAW, "%s__%s__%s.png" % (it, "south", v.replace("·", "_").replace("°", "")))
            if not os.path.exists(p):
                continue
            x = 8 + c * (CW + 8)
            y = 8 + r * (CH + LBL) + LBL
            d.text((x, y - 17), "%s 南向 %s" % (it, v), fill=(200, 220, 240), font=f)
            im = _crop_model(Image.open(p).convert("RGB"))
            sc = min(CW / im.width, CH / im.height)
            im = im.resize((max(1, int(im.width * sc)), max(1, int(im.height * sc))), Image.LANCZOS)
            sheet.paste(im, (x + (CW - im.width) // 2, y + (CH - im.height) // 2))
    path = os.path.join(OUT, "overview.png")
    sheet.save(path)
    return path


def main():
    clean_scoped_tmp()
    os.makedirs(RAW, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    jobs = [(i, r, v) for i in ITEMS for r in ROTS for v in VIEWS]
    print("shots:", len(jobs), "workers:", WORKERS)
    bad = []
    with ThreadPoolExecutor(max_workers=WORKERS) as ex:
        for p, err in ex.map(shoot, jobs):
            if err:
                bad.append((os.path.basename(p), err))
    for name, err in bad:
        print("  [blank] %s: %s" % (name, err))
    for i in ITEMS:
        print("sheet:", sheet(i))
    print("overview:", overview())
    print("结论:", "PASS %d 格全出图" % len(jobs) if not bad else "FAIL %d 格没渲染出东西" % len(bad))
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
