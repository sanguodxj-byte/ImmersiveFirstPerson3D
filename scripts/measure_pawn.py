# -*- coding: utf-8 -*-
"""把 Pawn 的「身体 + 头」两个分离图层按原版偏移叠加，测出人形的完整世界长宽。

依据（老板定的唯一参考：贴图的世界长宽）：
  - pawn 贴图画在1x1 格的 quad 上 -> 128 px == 1 格
  - headOffset  = (0.04, 0.34) 格  -> 头中心相对身体中心沿朝向前移 0.34 格
  - bodyGraphicScale = (1, 0.9)    -> 身体贴图在深度方向压扁到 0.9
叠加后从「头顶」到「脚底」的像素跨度 = 人在世界里投影的视觉长度。
"""
import os
from PIL import Image

T = r"D:/rim mod/RimWorld_Vanilla_Textures_1.6/Core/textures"

BODY = {
    "naked_male_south":   ((1.0, 0.9),  "Male"),
    "naked_female_south": ((1.0, 1.0),  "Female"),
    "naked_thin_south":   ((0.6, 1.0),  "Thin"),
    "naked_hulk_south":   ((1.0, 1.25), "Hulk"),
    "naked_fat_south":    ((1.5, 1.0),  "Fat"),
}
HEAD = {                       # 各体型头部所在子目录
    "Male":   "heads/male/male_average_normal_south.png",
    "Female": "heads/female/female_average_normal_south.png",
    "Thin":   "heads/male/male_average_pointy_south.png",
    "Hulk":   "heads/male/male_average_wide_south.png",
    "Fat":    "heads/male/male_average_wide_south.png",
}
HEAD_OFFSET_Z = 0.34           # 格，来自 BodyTypeDef.headOffset.y（四体型一致）


def bbox(path, alpha_min=16):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] >= alpha_min:
                x0 = min(x0, x); x1 = max(x1, x)
                y0 = min(y0, y); y1 = max(y1, y)
    return None if x1 < 0 else (x0, y0, x1, y1, w, h)


print("== 身体图层单独 ==")
for name in BODY:
    p = os.path.join(T, "Things/pawn/humanlike/bodies", name + ".png")
    b = bbox(p)
    x0, y0, x1, y1, w, h = b
    print(f"  {name:22} x {x0:>3}..{x1:<3}({x1-x0+1:>3}px)  "
          f"y {y0:>3}..{y1:<3}({y1-y0+1:>3}px)   "
          f"占宽 {(x1-x0+1)/w*100:5.1f}%  占高 {(y1-y0+1)/h*100:5.1f}%")

print()
print("== 头图层单独 ==")
for k, rel in HEAD.items():
    p = os.path.join(T, "Things/pawn/humanlike", rel.replace("/", os.sep))
    if not os.path.exists(p):
        print(f"  {k:22} <缺失> {rel}")
        continue
    x0, y0, x1, y1, w, h = bbox(p)
    print(f"  {k:22} x {x0:>3}..{x1:<3}({x1-x0+1:>3}px)  "
          f"y {y0:>3}..{y1:<3}({y1-y0+1:>3}px)   "
          f"占宽 {(x1-x0+1)/w*100:5.1f}%  占高 {(y1-y0+1)/h*100:5.1f}%")

print()
print("== 头身叠加后的完整人形（128px == 1 格）==")
print("   叠加规则：头中心沿朝向前移 headOffset.y = 0.34 格；")
print("   身体深度方向按 bodyGraphicScale.y 压缩后再定位。")
print()
print(f"{'体型':<8}{'投影宽':>9}{'投影长':>9}   {'宽/长':>7}")
print("-" * 42)

for name, (scale, bodytype) in BODY.items():
    bp = os.path.join(T, "Things/pawn/humanlike/bodies", name + ".png")
    hp = os.path.join(T, "Things/pawn/humanlike",
                      HEAD[bodytype].replace("/", os.sep))
    if not (os.path.exists(bp) and os.path.exists(hp)):
        continue
    bx0, by0, bx1, by1, bw, bh = bbox(bp)
    hx0, hy0, hx1, hy1, hw, hh = bbox(hp)

    # 身体：深度方向缩放 scaleY，中心在贴图中心 (w/2, h/2)
    bcy = (by0 + by1 + 1) / 2.0
    bcy_scaled = bh / 2.0 + (bcy - bh / 2.0) * scale[1]

    # 头：不缩放（头部独立图层），中心沿朝向 +HEAD_OFFSET_Z 格
    #像素/格 = 贴图边长(px) / 1 格
    hcy = (hy0 + hy1 + 1) / 2.0
    hcy_shift = hh / 2.0 + (hcy - hh / 2.0) + HEAD_OFFSET_Z * hh

    top = min(bcy_scaled - (by1 - by0 + 1) / 2.0 * scale[1],
              hcy_shift - (hy1 - hy0 + 1) / 2.0)
    bot = max(bcy_scaled + (by1 - by0 + 1) / 2.0 * scale[1],
              hcy_shift + (hy1 - hy0 + 1) / 2.0)

    span = bot - top                     # 从头到脚的像素跨度
    span_cells = span / bh               # 转成格
    width_cells = (bx1 - bx0 + 1) / bw

    print(f"{bodytype:<8}{width_cells:>8.3f}格{span_cells:>8.3f}格"
          f"   {width_cells/span_cells:>7.3f}")

print()
print("现实参照：成人肩宽约0.45 m，身高约 1.75 m->  宽/长 = 0.257")
