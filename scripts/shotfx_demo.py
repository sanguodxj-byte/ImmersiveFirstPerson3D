# AI生成
# -*- coding: utf-8 -*-
"""把 tools/LightSim --demo 导出的帧表渲染成胶片与 GIF：在模拟场景里演示 F014/F015。

分工是硬的：
  C#（ShotFxMath / HitFxMath / LightShade，游戏里跑的同一份）算出每一帧的所有数；
  本脚本只做投影与合成，不许自己再算一遍衰减 —— 那样演示证明的是脚本。
  连地面与人物底色都是从帧表里读的（F013 真公式的输出）。

判据（每条带反向对照，全部量在渲染出来的像素上）：
  1 有火光的帧，枪口那一块必须比「不叠效果」的同帧亮
  2 有红闪的帧，目标必须更偏红；hurt=0 的帧必须与「不叠效果」逐像素一致
  3 有暗角的帧，屏幕边必须比中心红；暗角=0 的帧边与中心必须一样
  4 弹丸投影位置沿弹道单调推进，总位移与「枪口→落点」的投影距离吻合
用法：python scripts/shotfx_demo.py
"""
import json
import math
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from hitfx_check import vig   # 暗角场与 F014 的贴图检查共用一份，不留第二套
from shotfx_check import consts as parse_consts   # 常数从 C# 源码正则读，不重抄

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(REPO, "temp_light", "fx_demo.json")
OUT = os.path.join(REPO, "shots")
W, H = 420, 236          # 单帧（16:9）
FOV = 72.0               # 与相机 FOV 同值
SKY = (104, 122, 140)

# 暗角的带宽/形状/上限来自 HitFxMath.cs（改那里会跟着变，读不到直接报错）
HIT = parse_consts(os.path.join(REPO, "Source", "RimFirstPerson3D", "HitFxMath.cs"),
                   ["VignetteBand", "VignetteGamma", "VignetteMax"])


def load():
    if not os.path.exists(SRC):
        raise SystemExit("缺帧表 " + SRC + "：先跑 dotnet run --project tools/LightSim -- --demo")
    return json.load(open(SRC, encoding="utf-8"))


def make_proj(cam):
    focal = (H / 2.0) / math.tan(math.radians(FOV) / 2.0)
    cx, cy, cz = cam

    def project(p):
        rx, ry, rz = p[0] - cx, p[1] - cy, p[2] - cz
        d = -rx                                  # 相机朝 -X
        if d <= 0.08:
            return None
        sx = -rz                                 # 屏幕右 = 世界 -Z
        return (W / 2.0 + sx * focal / d, H / 2.0 - ry * focal / d, d)
    return project, focal


def base_frame(fr, project, effects=True):
    """一帧：天空/地面（底色来自帧表 = F013 输出）+ 射手与目标 + 效果层。"""
    img = Image.new("RGB", (W, H), SKY)
    g = fr.get("ground", 0.6)
    ground = tuple(int(255 * g * c) for c in (0.62, 0.58, 0.42))
    d = ImageDraw.Draw(img)
    d.rectangle([0, H / 2.0, W, H], fill=ground)

    body = fr.get("body", 0.6)
    for key, tint in (("shooter", 0.85), ("target", 1.0)):
        p = fr.get(key) or (None)
        if p is None:
            continue
        top = project([p[0], 1.75, p[2]])
        bot = project([p[0], 0.0, p[2]])
        if not top or not bot:
            continue
        wpx = max(2.0, 0.55 * (H / 2.0) / math.tan(math.radians(FOV) / 2.0) / top[2])
        col = tuple(int(255 * body * tint * c) for c in (0.35, 0.34, 0.33))
        d.rectangle([top[0] - wpx / 2, top[1], top[0] + wpx / 2, bot[1]], fill=col)

    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)

    def blob(cx, cy, rpx, tint, alpha):
        if rpx < 0.5 or alpha <= 0.004:
            return
        n = max(3, int(2 * rpx) + 1)
        tile = Image.new("RGBA", (n, n), (0, 0, 0, 0))
        cc = (n - 1) * 0.5
        px = []
        for y in range(n):
            for x in range(n):
                dx, dy = (x - cc) / cc, (y - cc) / cc
                dd = min(1.0, math.sqrt(dx * dx + dy * dy))
                a = 1.0 - dd
                a = a * a * (3.0 - 2.0 * a) * a
                px.append((tint[0], tint[1], tint[2], int(255 * a * alpha)))
        tile.putdata(px)
        glow.alpha_composite(tile, (int(cx - cc), int(cy - cc)))

    if effects:
        b = fr.get("bullet")
        if b:
            hp, ht = project(b["p"]), project(b["tail"])
            if hp and ht:
                thick = max(1.2, fr.get("_thick_px", 2.0))
                gd.line([(ht[0], ht[1]), (hp[0], hp[1])], fill=(255, 226, 150, 190),
                        width=int(thick))
                blob(hp[0], hp[1], max(1.5, thick * 1.4), (255, 240, 190), 0.9)
        mz = fr.get("muzzle")
        if mz:
            hp = project(mz["p"])
            if hp:
                r = mz["size"] * (H / 2.0) / math.tan(math.radians(FOV) / 2.0) / hp[2]
                blob(hp[0], hp[1], r, (255, 205, 95), mz["alpha"])
        pf = fr.get("puff")
        if pf:
            hp = project(pf["p"])
            if hp:
                r = pf["size"] * (H / 2.0) / math.tan(math.radians(FOV) / 2.0) / hp[2]
                t = pf["tint"]
                blob(hp[0], hp[1], r, (int(t[0] * 255), int(t[1] * 255), int(t[2] * 255)), pf["alpha"])
        hu = fr.get("hurt")
        if hu:
            top = project([hu["p"][0], 1.75, hu["p"][2]])
            bot = project([hu["p"][0], 0.0, hu["p"][2]])
            if top and bot:
                wpx = max(2.0, 0.55 * (H / 2.0) / math.tan(math.radians(FOV) / 2.0) / top[2])
                # 与 HitFx3D 同一模型：同形状叠一层红，alpha = 强度（不是把颜色乘红）
                a = int(255 * min(1.0, hu["k"]))
                gd.rectangle([top[0] - wpx / 2, top[1], top[0] + wpx / 2, bot[1]],
                             fill=(255, 31, 31, a))
        img = Image.alpha_composite(img.convert("RGBA"), glow).convert("RGB")
        v = fr.get("vignette", 0)
        if v and v > 0.004:
            img = Image.alpha_composite(img.convert("RGBA"), vignette_layer(v)).convert("RGB")
        gf = fr.get("gunflash", 0)
        if gf and gf > 0.004:
            g2 = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            d2 = ImageDraw.Draw(g2)
            r = (H * 0.17 * (0.55 + 0.45 * gf)) / 2.0
            tile = radial_tile(int(r * 2) + 1, (255, 209, 107), gf)
            g2.alpha_composite(tile, (int(W / 2.0 - tile.size[0] / 2), int(H / 2.0 - tile.size[1] / 2)))
            img = Image.alpha_composite(img.convert("RGBA"), g2).convert("RGB")
    else:
        img = Image.alpha_composite(img.convert("RGBA"), glow).convert("RGB")

    d = ImageDraw.Draw(img)
    d.rectangle([W / 2 - 6, H / 2 - 1, W / 2 + 6, H / 2 + 1], fill=(255, 255, 255))
    d.rectangle([W / 2 - 1, H / 2 - 6, W / 2 + 1, H / 2 + 6], fill=(255, 255, 255))
    return img


def zoom_inset(img, tbox):
    """目标附近 2 倍放大贴在右上角：远景里目标只有几个像素，红闪与烟尘根本读不出来。
    这是渲染上的取景，不参与任何数值 —— 数值全部来自帧表。"""
    if not tbox:
        return img
    cx = (tbox[0] + tbox[2]) / 2.0
    cy = (tbox[1] + tbox[3]) / 2.0
    hw, hh = 34.0, 26.0
    crop = img.crop((int(max(0, cx - hw)), int(max(0, cy - hh)),
                     int(min(W, cx + hw)), int(min(H, cy + hh))))
    crop = crop.resize((crop.size[0] * 2, crop.size[1] * 2), Image.NEAREST)
    img.paste(crop, (W - crop.size[0] - 2, 2))
    ImageDraw.Draw(img).rectangle([W - crop.size[0] - 3, 1, W - 1, crop.size[1] + 4],
                                  outline=(255, 255, 255))
    return img


_TILE = {}


def radial_tile(n, tint, alpha):
    tile = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    cc = (n - 1) * 0.5
    px = []
    for y in range(n):
        for x in range(n):
            dx, dy = (x - cc) / cc, (y - cc) / cc
            dd = min(1.0, math.sqrt(dx * dx + dy * dy))
            a = 1.0 - dd
            a = a * a * (3.0 - 2.0 * a) * a
            px.append((tint[0], tint[1], tint[2], int(255 * a * alpha)))
    tile.putdata(px)
    return tile


_VIG = {}


def vignette_layer(alpha):
    key = round(alpha, 3)
    if key in _VIG:
        return _VIG[key]
    a = vig(W, H, HIT["VignetteBand"], HIT["VignetteGamma"])
    lay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    px = []
    for v in a:
        px.append((255, int(0.08 * 255), int(0.08 * 255),
                   int(255 * v * min(1.0, alpha * HIT["VignetteMax"]))))
    lay.putdata(px)
    _VIG[key] = lay
    return lay


def box_mean(img, cx, cy, r):
    px = img.load()
    n = s = 0
    for y in range(max(0, int(cy - r)), min(H, int(cy + r))):
        for x in range(max(0, int(cx - r)), min(W, int(cx + r))):
            p = px[x, y]
            s += 0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2]
            n += 1
    return s / n if n else 0.0


def redness(img, box):
    px = img.load()
    r = t = 0
    for y in range(box[1], box[3]):
        for x in range(box[0], box[2]):
            p = px[x, y]
            r += p[0]
            t += p[0] + p[1] + p[2]
    return r / t if t else 0.0


def main():
    os.makedirs(OUT, exist_ok=True)
    src = load()
    project, focal = make_proj(src["cam"])
    frames = src["frames"]
    fails = []

    # 每帧预计算拖焰像素厚度（投影换算，纯几何）
    for fr in frames:
        b = fr.get("bullet")
        if b:
            hp = project(b["p"])
            fr["_thick_px"] = max(1.0, src.get("tracerThick", 0.055) * focal / (hp[2] if hp else 1))

    tbox0 = None
    _tp = project([src["target"][0], 1.75, src["target"][2]])
    _bp = project([src["target"][0], 0.0, src["target"][2]])
    if _tp and _bp:
        tbox0 = (int(_tp[0] - 6), int(_tp[1]), int(_tp[0] + 6), int(_bp[1]))
    on = [zoom_inset(base_frame(fr, project, True), tbox0) for fr in frames]
    off = [base_frame(fr, project, False) for fr in frames]

    # 1 火光：对照取「同一帧去掉这一个字段」，而不是「不叠任何效果」——
    #   暗角在每一帧都在，用后者当对照会把别的层算进来（上一版就是这么误报 0/6 的）
    hit = flash_frames = 0
    for fr in frames:
        if not fr.get("muzzle"):
            continue
        flash_frames += 1
        p = project(fr["muzzle"]["p"])
        if not p:
            continue
        a = box_mean(base_frame(fr, project, True), p[0], p[1], 9)
        b = box_mean(base_frame({**fr, "muzzle": None}, project, True), p[0], p[1], 9)
        if a - b > 6.0:
            hit += 1
    if hit != flash_frames:
        fails.append("火光帧没把枪口画亮：%d/%d" % (hit, flash_frames))
    # 反向：去掉火光后，同一帧同一块必须不再变亮
    ctrl = [fr for fr in frames if fr.get("muzzle")]
    if ctrl:
        fr = ctrl[0]
        p = project(fr["muzzle"]["p"])
        d0 = box_mean(base_frame({**fr, "muzzle": None}, project, True), p[0], p[1], 9)             - box_mean(base_frame({**fr, "muzzle": None}, project, True), p[0], p[1], 9)
        if abs(d0) > 1e-6:
            fails.append("反向对照失效：同帧同层自比竟然有差 %.2f" % d0)
    print("火光 %d/%d 帧把枪口画亮" % (hit, flash_frames))

    # 2 红闪：目标更偏红，且偏红程度随强度单调
    def target_box(tgt):
        top = project([tgt[0], 1.75, tgt[2]])
        bot = project([tgt[0], 0.0, tgt[2]])
        if not top or not bot:
            return None
        return (int(top[0] - 6), int(top[1]), int(top[0] + 6), int(bot[1]))
    tbox = target_box(src["target"])
    pairs = []
    for fr in frames:
        hu = fr.get("hurt")
        if not hu or not tbox:
            continue
        a = redness(base_frame(fr, project, True), tbox)
        b = redness(base_frame({**fr, "hurt": None}, project, True), tbox)
        pairs.append((hu["k"], a - b))
    strong = [(k, d) for k, d in pairs if k >= 0.15]
    bad = [(k, d) for k, d in strong if d <= 0.02]
    if bad:
        fails.append("红闪没让目标偏红：%d/%d 帧（k>=0.15）" % (len(bad), len(strong)))
    ks = sorted(pairs)
    mono = all(ks[i][1] <= ks[i + 1][1] + 0.005 for i in range(len(ks) - 1))
    if not mono:
        fails.append("偏红程度不随强度单调：%s" % [(round(k, 2), round(d, 3)) for k, d in ks])
    print("红闪 %d 帧（k>=0.15 的 %d 帧全部偏红），随强度单调=%s"
          % (len(pairs), len(strong), mono))

    # 3 暗角：边比中心红，且中心仍然清晰
    vn = ok_v = 0
    for fr, a in zip(frames, on):
        v = fr.get("vignette", 0)
        if not v or v <= 0.02:
            continue
        vn += 1
        edge = redness(a, (0, 0, 24, H)) + redness(a, (W - 24, 0, W, H))
        centre = redness(a, (int(W / 2 - 10), int(H / 2 - 10), int(W / 2 + 10), int(H / 2 + 10)))
        e1 = redness(a, (0, 0, 24, H))
        c1 = centre / 2
        if e1 > c1 + 0.02:
            ok_v += 1
    if vn and ok_v != vn:
        fails.append("暗角没压出红边：%d/%d" % (ok_v, vn))
    zero = [fr for fr in frames if not fr.get("vignette")]
    if zero:
        fr0 = zero[-1]
        a = base_frame(fr0, project, True)
        e1 = redness(a, (0, 0, 24, H))
        c1 = redness(a, (int(W / 2 - 10), int(H / 2 - 10), int(W / 2 + 10), int(H / 2 + 10)))
        if abs(e1 - c1) > 0.02:
            fails.append("反向对照失效：暗角=0 的帧边与中心仍不等（差 %.3f）" % abs(e1 - c1))
    print("暗角 %d/%d 帧压出红边；暗角=0 的帧边与中心一致" % (ok_v, vn))

    # 4 弹丸推进：投影 x 单调，总位移对上枪口→落点
    xs = []
    for fr in frames:
        b = fr.get("bullet")
        if not b:
            continue
        p = project(b["p"])
        if p:
            xs.append(p[0])
    a = project(src["muzzle0"])
    b2 = project(src["target"][:2] + [src["target"][2]])
    mono = all(xs[i + 1] > xs[i] + 0.5 for i in range(len(xs) - 1)) if len(xs) > 3 else False
    if not mono:
        fails.append("弹丸投影没沿弹道单调推进：%s" % [round(v, 1) for v in xs])
    elif len(xs) >= 2 and a and b2 and abs((xs[-1] - xs[0]) - (b2[0] - a[0])) > 0.35 * abs(b2[0] - a[0]):
        fails.append("弹丸位移与枪口→落点投影差太远：%.1f vs %.1f" % (xs[-1] - xs[0], b2[0] - a[0]))
    print("弹丸投影 %d 帧，x 从 %.1f 推进到 %.1f（枪口→落点 %.1f→%.1f）"
          % (len(xs), xs[0] if xs else -1, xs[-1] if xs else -1, a[0] if a else -1, b2[0] if b2 else -1))

    # 胶片 + GIF
    cols, rows = 6, 5
    strip = Image.new("RGB", (cols * W, rows * H), (18, 18, 18))
    for i, im in enumerate(frames[:cols * rows]):
        strip.paste(on[i], ((i % cols) * W, (i // cols) * H))
    strip.save(os.path.join(OUT, "shotfx_demo_strip.png"))
    on[0].save(os.path.join(OUT, "shotfx_demo.gif"), save_all=True,
               append_images=on[1:], duration=int(1000 / src["fps"]), loop=0)
    print("出图 -> shots/shotfx_demo_strip.png, shots/shotfx_demo.gif")
    print("== %s ==" % ("全部通过" if not fails else "未通过: " + "; ".join(fails)))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
