# AI生成
# -*- coding: utf-8 -*-
"""近景验收表：把审核原图裁到模型本体、按原生分辨率拼一张，逐件目视。

闸门（出图/剪影）只证明"画出来了且没断"，不证明形状对 —— 形状要人看。
用法：python scripts/furn_closeup.py                 # 默认 = 最近一批
      python scripts/furn_closeup.py bed armchair    # 指定件
      python scripts/furn_closeup.py --shots south/45°,south/贴脸 bed
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furn_audit as A          # noqa: E402

DEFAULT_ITEMS = ["couch", "diningchair", "doublebed", "royalbed", "hospitalbed",
                 "bedroll", "bedrolldouble"]
DEFAULT_SHOTS = [("south", "45°"), ("south", "贴脸"), ("east", "贴脸"), ("north", "贴脸")]
CW, CH, LBL = 300, 260, 20


def build(items, shots, name="closeup"):
    sheet = Image.new("RGB", (CW * len(shots) + 8 * (len(shots) + 1),
                              (CH + LBL) * len(items) + 8), (24, 26, 33))
    d = ImageDraw.Draw(sheet)
    try:
        f = ImageFont.truetype(r"C:\Windows\Fonts\cons.ttf", 15)
    except Exception:
        f = ImageFont.load_default()
    for r, it in enumerate(items):
        for c, (rot, v) in enumerate(shots):
            p = os.path.join(A.RAW, "%s__%s__%s.png"
                             % (it, rot, v.replace("·", "_").replace("°", "")))
            x, y = 8 + c * (CW + 8), 8 + r * (CH + LBL) + LBL
            d.text((x, y - 16), "%s %s %s" % (it, rot, v), fill=(200, 220, 240), font=f)
            if not os.path.exists(p):
                d.text((x, y), "无此格", fill=(255, 120, 120), font=f)
                continue
            im = A._crop_model(Image.open(p).convert("RGB"))
            sc = min(CW / im.width, CH / im.height)
            im = im.resize((max(1, int(im.width * sc)), max(1, int(im.height * sc))),
                           Image.LANCZOS)
            sheet.paste(im, (x + (CW - im.width) // 2, y + (CH - im.height) // 2))
    out = os.path.join(A.OUT, "%s.png" % name)
    sheet.save(out)
    return out


def main():
    args = sys.argv[1:]
    shots = DEFAULT_SHOTS
    if args and args[0] == "--shots":
        shots = [tuple(s.split("/")) for s in args[1].split(",")]
        args = args[2:]
    p = build(args or DEFAULT_ITEMS, shots)
    print(p)


if __name__ == "__main__":
    main()
