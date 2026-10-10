# AI生成
# -*- coding: utf-8 -*-
"""全量截图检测：16 种连通性变体 × 6 个视角，全部渲染后拼成对照图。

用法：
    python shoot_all.py            # 全量（16x6 = 96 张）
    python shoot_all.py 俯视        # 只跑某几个视角
"""
import os
import subprocess
import sys
import time
import uuid
import urllib.parse

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "scripts"))
from _tmpsweep import scratch, env_for, profile, drop, sweep, sweep_mine  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
BASE = "http://127.0.0.1:8899/index.html"
OUT = os.path.join(HERE, "shots")

W, H = 640, 480

# 16 种连通性变体 -> 代表格子集合（与 index.html 的 FORMS 一致）
VARIANTS = {
    "V00": [[0, 0]],
    "V01": [[0, 0], [1, 0]],
    "V02": [[0, 0], [-1, 0]],
    "V03": [[0, 0], [1, 0], [-1, 0]],
    "V04": [[0, 0], [0, -1]],
    "V05": [[0, 0], [0, -1], [1, 0]],
    "V06": [[0, 0], [0, -1], [-1, 0]],
    "V07": [[0, 0], [0, -1], [1, 0], [-1, 0]],
    "V08": [[0, 0], [0, 1]],
    "V09": [[0, 0], [0, 1], [1, 0]],
    "V10": [[0, 0], [0, 1], [-1, 0]],
    "V11": [[0, 0], [0, 1], [1, 0], [-1, 0]],
    "V12": [[0, 0], [0, 1], [0, -1]],
    "V13": [[0, 0], [0, 1], [0, -1], [1, 0]],
    "V14": [[0, 0], [0, 1], [0, -1], [-1, 0]],
    "V15": [[0, 0], [0, 1], [0, -1], [1, 0], [-1, 0]],
}

VIEWS = ["45°", "俯视", "主视·南", "侧视·东", "低角度", "贴脸"]


def shoot(name, view):
    """截一张图，返回文件路径；失败返回 None。"""
    path = os.path.join(OUT, "%s_%s.png" % (name, urllib.parse.quote(view, safe="")))
    url = "%s?v=%s&bare=1&view=%s&form=%s" % (
        BASE, "%s-%s" % (int(time.time()), uuid.uuid4().hex[:8]),
        urllib.parse.quote(view), name)
    rd = scratch("shootall")          # D 盘沙盒：profile 与 Chrome 自带的 scoped_dir 都在里面
    subprocess.run(
        [CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
         "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
         "--window-size=%d,%d" % (W, H),
         "--virtual-time-budget=5000",
         "--user-data-dir=" + profile(rd),
         "--screenshot=" + path, url],
        env=env_for(rd),
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    if not drop(rd):
        print("  [leak] 沙盒没删干净: %s" % rd)
    return path if os.path.exists(path) and os.path.getsize(path) > 8000 else None


def contact_sheet(names, views, col_w=200):
    """把截图拼成对照图：每行一个变体，每列一个视角。"""
    rows, cols = len(names), len(views)
    cw = col_w
    ch = int(cw * H / W)
    pad, hdr = 4, 18
    sheet = Image.new("RGB", (cols * (cw + pad) + pad,
                              rows * (ch + pad + hdr) + pad), (24, 26, 32))
    d = ImageDraw.Draw(sheet)
    for c, v in enumerate(views):
        d.text((pad + c * (cw + pad) + 2, 4), v, fill=(200, 210, 225))
    for r, nm in enumerate(names):
        y = pad + hdr + r * (ch + pad + hdr)
        for c, v in enumerate(views):
            p = os.path.join(OUT, "%s_%s.png" % (nm, urllib.parse.quote(v, safe="")))
            x = pad + c * (cw + pad)
            if os.path.exists(p):
                im = Image.open(p).convert("RGB").resize((cw, ch), Image.LANCZOS)
                sheet.paste(im, (x, y))
        d.text((x + 2, y + ch + 2), nm, fill=(150, 200, 255))
    return sheet


def main():
    os.makedirs(OUT, exist_ok=True)
    sweep()                  # 启动先清上一轮残留
    only = sys.argv[1:] or VIEWS
    views = [v for v in VIEWS if v in only] or only
    names = list(VARIANTS.keys())

    ok, bad = 0, []
    for nm in names:
        sweep()                # 每个变体兜一次底（只动静置够久的，不碰并发在用的）
        for v in views:
            p = shoot(nm, v)
            if p:
                ok += 1
            else:
                bad.append("%s/%s" % (nm, v))
        print("  %s 完成" % nm)

    print("\n成功 %d / %d" % (ok, len(names) * len(views)))
    if bad:
        print("失败:", ", ".join(bad))
    for v in views:                      # 每个视角一张大对照图
        sheet = contact_sheet(names, [v], col_w=200)
        out = os.path.join(HERE, "_sheet_%s.png" % v.replace("·", "_"))
        sheet.save(out)
        print("对照图:", out)
    sweep_mine()               # 收尾：先收本进程 drop() 没删掉的
    sweep()                  # 再收别处被杀时留下的老沙盒


if __name__ == "__main__":
    main()