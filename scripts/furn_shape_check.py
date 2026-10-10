# AI生成
# -*- coding: utf-8 -*-
"""形状探针：同一机位渲两次（有模型 / ?empty=1 只渲场景），相减得到精确模型遮罩，
再按行统计，看剪影断没断、每段多亮。

为什么差分：家具里有不少近黑的面（柜体侧面的底座阴影），用"与背景色的色差"当遮罩
会把它们当成背景，于是好好的模型被切成几段 —— 假阳性。差分没有这个问题。
为什么按行而不是连通块：本项目的坏味道几乎都是"该连成一块的断成上下两截"
（部件被剔除 / 悬空 / 埋进别的面具里），按行的 x 跨度与像素数一眼能看出来。

用法：
    python scripts/furn_shape_check.py shelf south 侧视·东
    python scripts/furn_shape_check.py shelf south 侧视·东 --keep   # 留图便于比对
退出码：0 = 剪影单块；1 = 断裂（有部件没接上）。
"""
import os
import subprocess
import sys
import tempfile

import numpy as np
from PIL import Image

MOD = r"D:\rim mod\FirstPerson3D"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
PORT = "http://127.0.0.1:8899/furniture.html"


def shoot(item, rot, view, empty=False, flat=False):
    from urllib.parse import quote
    p = os.path.join(tempfile.gettempdir(),
                     "shape_%s_%s_%s%s%s.png" % (item, rot, view, "_empty" if empty else "",
                                      "_flat" if flat else ""))
    cmd = [CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
           "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
           "--window-size=620,500", "--virtual-time-budget=25000", "--timeout=90000",
           "--user-data-dir=" + tempfile.mkdtemp(prefix="shapeshot-"),
           "--screenshot=" + p,
           # still=1：与审核台同一口径（关掉 DOM 叠层、关阴影），否则差分里混进按钮条
           "%s?item=%s&rot=%s&view=%s&still=1%s%s" % (PORT, item, rot, quote(view),
                                                      "&empty=1" if empty else "",
                                                      "&flat=1" if flat else "")]
    subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=140)
    return p


from furn_audit import model_mask, bands   # 判据只有一份，在 furn_audit.py


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if len(args) < 3:
        print(__doc__)
        return 2
    item, rot, view = args[0], args[1], args[2]
    fl = "--flat" in sys.argv
    p, pe = shoot(item, rot, view, flat=fl), shoot(item, rot, view, empty=True)
    if not (os.path.exists(p) and os.path.exists(pe)):
        print("没出图：", p, pe)
        return 1
    a = np.asarray(Image.open(p).convert("RGB")).astype(int)
    a0 = np.asarray(Image.open(pe).convert("RGB")).astype(int)
    m = model_mask(a, a0)
    bs = bands(m)
    print("%s / %s / %s -> 剪影段数 %d  %s" % (item, rot, view, len(bs),
          "OK 单块" if len(bs) == 1 else "断裂：有部件没接上"))
    for (y0, y1) in bs:
        sub = m[y0:y1]
        xs = np.where(sub.any(axis=0))[0]
        lum = a[y0:y1][sub].mean()
        print("  y %3d..%3d (高%3d)  x %3d..%3d (宽%3d)  像素%5d  亮度%5.0f"
              % (y0, y1 - 1, y1 - y0, xs.min(), xs.max(), xs.max() - xs.min() + 1,
                 int(sub.sum()), lum))
    if "--keep" not in sys.argv:
        for f in (p, pe):
            os.remove(f)
    return 0 if len(bs) == 1 else 1


if __name__ == "__main__":
    sys.exit(main())
