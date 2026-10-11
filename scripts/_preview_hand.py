# AI生成
# -*- coding: utf-8 -*-
"""离屏复刻 Hand3D 的手部贴图与摆放，出图 + 判据。

真值取自 C# 源码本身，不在这份脚本里重抄一遍参数：
  EnsureTex() 的径向剖面 -> radial()（平顶 / 过渡 / 黑边 / 边缘 alpha 淡出）
  SizeFrac / MarginRightFrac / MarginBottomFrac -> 正则读出来，读不到直接报错

判据（带反向对照）：
  1 不透明圆饼整块在屏内
  2 离右、下边缘各留 >=5% 屏高（贴在角落就是原来那版的症状）
  3 反向：换回旧系数（左上角 = 屏幕边 - 0.78/0.88×size）判据必须报
用法：python scripts/_preview_hand.py      退出码非 0 = 未通过
"""
import math
import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CS = os.path.join(REPO, "Source", "RimFirstPerson3D", "Hand3D.cs")
OUT = os.path.join(REPO, "shots")
S = 128          # 与 EnsureTex 同
R = 60.0         # 与 EnsureTex 同


def consts():
    src = open(CS, encoding="utf-8").read()
    out = {}
    for name in ("SizeFrac", "MarginRightFrac", "MarginBottomFrac"):
        m = re.search(r"public const float %s\s*=\s*([0-9.]+)f\s*;" % name, src)
        if not m:
            raise SystemExit("Hand3D.cs 里找不到 %s —— 摆放常数改名了，脚本跟着改" % name)
        out[name] = float(m.group(1))
    return out


def radial(d):
    """EnsureTex 的剖面：d = 归一化半径。返回 (灰度 0..255, alpha 0..255)。"""
    if d >= 1.0:
        return 0, 0
    if d < 0.458:
        L = 244.0
    elif d < 0.745:
        u = (d - 0.458) / 0.287
        L = 244.0 * (1.0 - u) ** 0.35
    else:
        L = 0.0
    a = 255.0
    if d > 0.97:
        a = 255.0 * max(0.0, 1.0 - (d - 0.97) / 0.03)
    return int(round(L)), int(round(a))


def hand_img():
    C = (S - 1) * 0.5
    img = []
    for y in range(S):
        row = []
        for x in range(S):
            d = math.hypot(x - C, y - C) / R
            L, a = radial(d)
            row.append((L, L, L, a))
        img.append(row)
    return img


def blit(canvas, img, W, H, rect):
    """把 S×S 的灰度手按 StretchToFill 贴到 rect（GUI 坐标：y 向下）。返回画到的像素包围盒。"""
    x0, y0, rw, rh = rect
    minx = miny = 10 ** 9
    maxx = maxy = -10 ** 9
    for oy in range(int(rh)):
        sy = oy * S // int(rh)
        for ox in range(int(rw)):
            sx = ox * S // int(rw)
            Rr, Gg, Bb, A = img[sy][sx]
            cx, cy = int(x0) + ox, int(y0) + oy
            if not (0 <= cx < W and 0 <= cy < H):
                continue
            if A <= 8:
                continue
            a = A / 255.0
            bg = canvas[cy][cx]
            skin = (222, 176, 145)
            canvas[cy][cx] = (int(skin[0] * (Rr / 255.0) * a + bg[0] * (1 - a)),
                              int(skin[1] * (Gg / 255.0) * a + bg[1] * (1 - a)),
                              int(skin[2] * (Bb / 255.0) * a + bg[2] * (1 - a)), 255)
            minx = min(minx, cx); maxx = max(maxx, cx)
            miny = min(miny, cy); maxy = max(maxy, cy)
    return (minx, miny, maxx, maxy)


def write_png(path, W, H, canvas):
    rows = []
    for y in range(H):
        rows.append([(c[0], c[1], c[2], 255) for c in canvas[y]])
    try:
        from PIL import Image
        im = Image.new("RGB", (W, H))
        im.putdata([p for r in rows for p in r])
        im.save(path)
    except Exception:
        raise SystemExit("需要 Pillow 才能出图")


def main():
    os.makedirs(OUT, exist_ok=True)
    c = consts()
    img = hand_img()
    fails = []
    for (W, H, tag) in ((1280, 720, "16x9"), (1024, 768, "4x3")):
        size = H * c["SizeFrac"]
        cx = W - H * c["MarginRightFrac"]
        cy = H - H * c["MarginBottomFrac"]
        rect = (cx - size * 0.5, cy - size * 0.5, size, size)
        canvas = [[(58, 66, 72, 255) for _ in range(W)] for _ in range(H)]
        for x in range(W):
            for y in range(int(H * 0.55), H):
                canvas[y][x] = (74, 68, 54, 255)
        box = blit(canvas, img, W, H, rect)
        out = os.path.join(OUT, "hand_place_%s.png" % tag)
        write_png(out, W, H, canvas)
        # 判据 1：不透明像素整块在屏内 —— 用画到的包围盒对屏幕边
        if box[0] <= 1 or box[2] >= W - 2 or box[1] <= 1 or box[3] >= H - 2:
            fails.append("%s 手被屏幕切到: bbox=%s 屏=%dx%d" % (tag, box, W, H))
        # 判据 2：离右、下各留 >=5% 屏高
        mr = (W - box[2]) / H
        mb = (H - box[3]) / H
        if mr < 0.05:
            fails.append("%s 离右边缘太近: %.3f 屏高" % (tag, mr))
        if mb < 0.05:
            fails.append("%s 离下边缘太近: %.3f 屏高" % (tag, mb))
        print("%s size=%.0f 掌心=(%.0f,%.0f) 不透明包围盒=%s 右余=%.3f 下余=%.3f -> %s"
              % (tag, size, cx, cy, box, mr, mb, os.path.basename(out)))

    # 反向对照：旧系数（左上角 = 屏幕边 - 0.78/0.88×size）必须被同一套判据绊倒
    W, H = 1280, 720
    size = H * consts()["SizeFrac"]
    rect = (W - size * 0.78, H - size * 0.88, size, size)
    canvas = [[(0, 0, 0, 255) for _ in range(W)] for _ in range(H)]
    box = blit(canvas, img, W, H, rect)
    clipped = box[2] >= W - 2 or box[3] >= H - 2
    print("反向对照 旧系数 0.78/0.88: bbox=%s -> %s"
          % (box, "判据绊倒（符合预期）" if clipped else "没绊倒：判据没有牙齿！"))
    if not clipped:
        fails.append("反向对照失效：旧系数不再被绊倒，判据是假的")

    print("== %s ==" % ("全部通过" if not fails else "未通过: " + "; ".join(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
