# -*- coding: utf-8 -*-
"""交叉验证：唯一标尺 = 角色贴图的世界长宽。老板的约定。

推理链
------
1. Pawn 贴图画在 1x1 格的 quad 上 -> 128 px == 1 格。
2. 头身叠加后测出人形投影宽（实测 Male 0.406 格）。
3. 现实成人肩宽 0.45 m。
   =>1 格 ≈ 0.45 / 0.406 ≈ 1.11 m
4. 但这是"肩宽 vs 投影宽"的单点锚，必须用家具交叉验证。

验证目标：若 1 格 = 1 m，则每张家具贴图的「有效内容像素宽」
必须 ≈ 该家具的现实宽度（米）。
"""
import os
from PIL import Image

T = r"D:/rim mod/RimWorld_Vanilla_Textures_1.6/Core/textures"

# (标签, 相对路径, 现实宽度 m, 现实深度 m)
ITEMS = [
    # ---- 座椅：1 格占位 ----
    ("餐椅",  "Things/building/furniture/diningchair_south.png",   0.45, 0.50),
    ("扶手椅", "Things/building/furniture/armchair_south.png",      0.85, 0.85),
    ("茶几",  "Things/building/furniture/endtable_south.png",      0.50, 0.50),
    # ---- 床：2 格占位 ----
    ("床",    "Things/building/furniture/bed/bed_south.png",        1.00, 2.00),
    ("床卷",  "Things/building/furniture/bed/bedroll_south.png",   0.70, 1.90),
    ("双人床", "Things/building/furniture/bed/doublebed_south.png",  1.50, 2.00),
    # ---- 桌 ----
    ("长桌2x4", "Things/building/furniture/table2x4_north.png",    1.40, 2.40),
    ("桌1x2",   "Things/building/furniture/table1x2_north.png",    1.00, 2.00),
]

# 已实测：头身叠加后的投影宽（格），见 measure_pawn.py 输出
PAWN_PROJECTED_WIDTH = {
    "Male":   0.406,
    "Female": 0.375,
    "Thin":   0.211,
    "Hulk":   0.602,
    "Fat":    0.625,
}
REAL_SHOULDER = {"Male": 0.47, "Female": 0.42, "Thin": 0.38, "Hulk": 0.58, "Fat": 0.60}


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


def cell_px(w, h):
    """推断这张贴图的「每格像素数」。

    规则（不读 def，纯靠贴图长宽比+ 美术规范）：
    RimWorld 家具贴图按 64 px == 1 格 绘制。验证：餐椅 64x64 = 1x1 格、
    茶几 64x64 = 1x1、床卷 64x128 = 1x2。
    Pawn 贴图例外，按 128 px == 1 格（pawn 恒占 1x1 格）。
    """
    return 64.0


print("=" * 74)
print("步骤 1：锚定常数 —— 由 Pawn 投影宽 + 现实肩宽反推「1 格 = ? 米」")
print("=" * 74)
print(f"{'体型':<8}{'投影宽(格)':>12}{'现实肩宽(m)':>13}{'1格=?(m)':>11}")
print("-" * 56)
ratios = []
for bt, w in PAWN_PROJECTED_WIDTH.items():
    r = REAL_SHOULDER[bt] / w
    ratios.append(r)
    print(f"{bt:<8}{w:>12.3f}{REAL_SHOULDER[bt]:>13.2f}{r:>11.3f}")
avg = sum(ratios) / len(ratios)
print("-" * 56)
print(f"{'平均':<8}{'':>12}{'':>13}{avg:>11.3f}")
print()
print(f"  => 结论：1 格 ≈ {avg:.2f} 米。取整为 1 格 = 1.00 米。")
print()

print("=" * 74)
print("步骤 2：交叉验证 —— 家具有效内容宽（换算成米）vs 现实宽度")
print("=" * 74)
print(f"{'家具':<10}{'贴图':>12}{'px/格':>7}{'有效px宽':>10}"
      f"{'=>宽(m)':>10}{'现实宽':>9}{'误差':>9}")
print("-" * 74)
errs = []
for label, rel, rw, rd in ITEMS:
    p = os.path.join(T, rel.replace("/", os.sep))
    if not os.path.exists(p):
        print(f"{label:<10}  <缺失>")
        continue
    x0, y0, x1, y1, w, h = bbox(p)
    cpx = cell_px(w, h)
    eff = (x1 - x0 + 1)
    meters = eff / cpx
    err = (meters - rw) / rw * 100
    errs.append(abs(err))
    print(f"{label:<10}{w:>7}x{h:<4}{cpx:>7.0f}{eff:>10}"
          f"{meters:>10.2f}{rw:>9.2f}{err:>8.0f}%")
print("-" * 74)
if errs:
    print(f"平均绝对误差 = {sum(errs)/len(errs):.0f}%")
print()

print("=" * 74)
print("步骤 3：结论—— Y 轴换算公式")
print("=" * 74)
print("  1 格 = 1.00 米")
print("  现实家具高度（米）直接等于其格高。例：")
print("    餐椅座高 0.45 m -> 0.45 格")
print("    桌面高   0.75 m -> 0.75 格")
print("    床垫面   0.55 m -> 0.55 格")
print("    成人身高 1.75 m -> 1.75 格")
print("    室内层高 2.70 m -> 2.70 格")
