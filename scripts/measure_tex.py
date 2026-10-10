# -*- coding: utf-8 -*-
"""实测原版贴图「有效内容」的像素范围。

老板定的唯一参考：贴图的世界长宽。def的 size/drawSize 一律不看。
有效内容 = alpha > 阈值 的像素包围盒（排除透明边距/描边外的空白）。
"""
import os
from PIL import Image

T = r"D:/rim mod/RimWorld_Vanilla_Textures_1.6/Core/textures"

SAMPLES = [
    ("角色/男/南",  r"Things/pawn/humanlike/bodies/naked_male_south.png"),
    ("角色/女/南",  r"Things/pawn/humanlike/bodies/naked_female_south.png"),
    ("角色/瘦/南",  r"Things/pawn/humanlike/bodies/naked_thin_south.png"),
    ("角色/壮/南",  r"Things/pawn/humanlike/bodies/naked_hulk_south.png"),
    ("角色/男/北",  r"Things/pawn/humanlike/bodies/naked_male_north.png"),
    ("角色/男/东",  r"Things/pawn/humanlike/bodies/naked_male_east.png"),
    ("头/男/南",    r"Things/pawn/humanlike/heads/male/male_average_normal_south.png"),
    ("剪影/成人",   r"Things/pawn/humanlike/silhouettes/silhouette_humanadult.png"),
    ("剪影/儿童",   r"Things/pawn/humanlike/silhouettes/silhouette_humanchild.png"),
    ("剪影/婴儿",   r"Things/pawn/humanlike/silhouettes/silhouette_humanbaby.png"),
    ("床",          r"Things/building/furniture/bed/bed_south.png"),
    ("床·北",       r"Things/building/furniture/bed/bed_north.png"),
    ("床卷",        r"Things/building/furniture/bed/bedroll_south.png"),
    ("餐桌椅/南",   r"Things/building/furniture/diningchair_south.png"),
    ("扶手椅/南",   r"Things/building/furniture/armchair_south.png"),
    ("茶几/南",     r"Things/building/furniture/endtable_south.png"),
    ("茶几/北",     r"Things/building/furniture/endtable_north.png"),
    ("桌1x2/北",    r"Things/building/furniture/table1x2_north.png"),
    ("桌2x4/北",    r"Things/building/furniture/table2x4_north.png"),
]


def bbox(path, alpha_min=16):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] >= alpha_min:
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y < y0: y0 = y
                if y > y1: y1 = y
    if x1 < 0:
        return None
    # RimWorld 贴图 v 轴向上：v = 1 - y/h
    return dict(w=w, h=h,
                x0=x0, x1=x1, cw=x1 - x0 + 1,
                y0=y0, y1=y1, ch=y1 - y0 + 1,
                fx=(x1 - x0 + 1) / w, fy=(y1 - y0 + 1) / h)


print(f"{'样本':<12}{'贴图':>12}  {'有效X':>10} {'有效Y':>10}  {'占宽':>7}{'占高':>7}")
print("-" * 68)
for label, rel in SAMPLES:
    p = os.path.join(T, rel.replace("/", os.sep))
    if not os.path.exists(p):
        print(f"{label:<12}  <缺失> {rel}")
        continue
    r = bbox(p)
    if r is None:
        print(f"{label:<12}{r['w']}x{r['h']}  <全透明>")
        continue
    print(f"{label:<12}{r['w']}x{r['h']:>6}  "
          f"{r['x0']:>3}..{r['x1']:<3}   {r['y0']:>3}..{r['y1']:<3}  "
          f"{r['fx']*100:>6.1f}%{r['fy']*100:>6.1f}%")
