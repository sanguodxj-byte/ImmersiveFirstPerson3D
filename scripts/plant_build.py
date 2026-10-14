# AI生成
# -*- coding: utf-8 -*-
"""植被与树木 3D 化：外部构建单一真源（低模多面体风格，严格对齐原版贴图外观）。

管线（对照家具/残骸管线的既有模式）：
    1. 从原版植物贴图取样"树皮/树叶/点缀"块，按原版色彩特征过滤串色并实心化后打包图集
       -> Textures/FPS3D/PlantAtlas.png（同时副本到 viewer/plant_atlas.png）
    2. 参数化生成 24 个低模原型网格（Y 轴向上、单位=米、底面中心在原点、
       右手系 CCW 面 —— ObjImporter3D 会反转绕序到 Unity 左手系）
       -> 1.7/Assets/plants/<原型>.obj
    3. 出 viewer/plant_uv.json（图集 UV 表 + 原型表），审核页与 C# 同源
    4. 自带几何守卫：着地 / 部件连通 / UV 越界 / 面数 / 法线朝外，
       守卫不过退出码非零，不给存量背书

口径：
    - 1 格 = 1 米；世界尺寸唯一真源 = 原版立牌占格（def 的 drawSize ×
      visualSizeRange × 内容像素/画布像素，见 VANILLA 表）：
      树带（含 def 归 TreeBase 的 saguaro）= 占格 × TREE_K，锚点为树带平均高
      3.0m（沿用 PlantLayer3D 世界高度约定，灵树 3.50m 为自然落点）；
      小株高度保留现实约定、宽度按贴图内容宽高比。
    - 坐标：右手系（x 东，y 上，z 南）；OBJ vt 原点在左下（与 Unity 一致），
      图集内取样区用 PIL 像素坐标（原点左上）标定，落图时统一翻 v。
    - 无光照 Cutout 下靠低模切面法线映射进取样区纵向明暗带，直接呈现立体分面与原版笔触。
"""
import json
import math
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw

VAN = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
MOD = r"D:\rim mod\FirstPerson3D"
ATLAS_OUT = os.path.join(MOD, "Textures", "FPS3D", "PlantAtlas.png")
OBJ_DIR = os.path.join(MOD, "1.7", "Assets", "plants")
JSON_OUT = os.path.join(MOD, "viewer", "plant_uv.json")
VIEW_ATLAS = os.path.join(MOD, "viewer", "plant_atlas.png")
VIEW_OBJ_DIR = os.path.join(MOD, "viewer", "plants")
SAMPLE_DOC = os.path.join(MOD, "docs", "plants", "sampling.png")

PAD = 2           # 图集内每块边缘复制宽度（防双线性/mip 串色，同家具口径）
STROKE_LUMA = 40  # 黑描边判定阈值（0-255）
ATLAS_W = 512

CORE = "Core/textures/things/plant"
ROY = "Royalty/textures/things/plant"

# ---------------------------------------------------------------------------
# 原版世界占格（1 格 = 1 米）—— 世界尺寸的唯一真源
#
# 口径：立牌占格 = graphicData.drawSize × plant.visualSizeRange(成熟=max)
#       世界内容尺寸 = 占格 × 内容像素/画布像素（内容 = alpha≥16 包围盒，
#       同 docs/plants/VanillaTextureHeights.md 口径）。
# 来源：RimWorld 1.6 Data/Core|Royalty/Defs/ThingDefs_Plants 继承链解析实测
#       （2026-10-13）；贴图像素只给同画布档内的相对比，跨档（64/128/256）
#       必须靠 drawSize×visualSizeRange 归一 —— 见 docs/plants/VanillaTextureWidths.md。
# 字段：(drawSize, vsrMax, 画布px, 内容宽px, 内容高px)
# ---------------------------------------------------------------------------
VANILLA = {
    # ---- 树（def 里 treeCategory=Full，IsTree）----
    "oak":       (1.0, 2.0, 128, 109, 124),
    "maple":     (1.0, 2.0, 128, 90, 112),
    "poplar":    (1.0, 2.0, 128, 58, 123),
    "pine":      (1.0, 3.0, 128, 78, 123),
    "cypress":   (1.0, 2.3, 128, 96, 126),
    "birch":     (1.0, 2.0, 128, 95, 121),
    "palm":      (1.0, 2.0, 128, 128, 123),
    "drago":     (1.0, 2.0, 128, 125, 107),
    "teak":      (1.0, 2.4, 128, 113, 126),
    "willow":    (1.0, 1.8, 128, 124, 123),
    "bamboo":    (1.0, 2.0, 128, 117, 121),
    "cocoa":     (1.0, 2.0, 128, 124, 124),
    "cecropia":  (1.0, 2.0, 128, 126, 121),
    "anima":     (1.0, 2.5, 256, 242, 243),
    "saguaro":   (1.0, 2.0, 64, 48, 64),      # def 归 TreeBase：立牌 2 格高，与树同档
    # ---- 小株（非树：保留现实高度，宽度按贴图宽高比）----
    "bush":        (1.15, 1.10, 64, 63, 51),
    "berry":       (1.0, 1.70, 64, 57, 49),
    "brambles":    (1.7, 0.85, 128, 98, 83),
    "agave":       (1.0, 1.40, 64, 63, 60),
    "alocasia":    (2.0, 1.10, 128, 125, 112),
    "shrublow":    (1.0, 1.05, 64, 64, 60),
    "pincushion":  (1.0, 0.65, 64, 54, 45),
    "healroot":    (1.0, 1.00, 64, 59, 52),
    "devilstrand": (1.0, 1.00, 64, 61, 58),
}

TREES = ("oak", "maple", "poplar", "pine", "cypress", "birch", "palm", "drago",
         "teak", "willow", "bamboo", "cocoa", "cecropia", "anima")

# 小株现实高度约定（米，2026-10-11 过审值）：方案 B 下保持不变。
SMALL_H = {
    "bush": 0.55, "berry": 0.5, "brambles": 0.45, "agave": 0.8, "alocasia": 0.9,
    "shrublow": 0.32, "pincushion": 0.38, "healroot": 0.32, "devilstrand": 0.36,
}

# 口径开关：B（默认）= 树忠实+小株现实；A = 全量忠实（小株也走占格×TREE_K）。
# 仅用于出对照图；入库口径为 B。
POLICY = os.environ.get("PLANT_POLICY", "B")


def _cells(name):
    """原版立牌的内容世界尺寸（格=米）：(宽, 高)。"""
    ds, vsr, cv, cw, ch = VANILLA[name]
    q = ds * vsr
    return q * cw / cv, q * ch / cv


# 锚点：树带平均高 = 既有世界约定 3.0m（PlantLayer3D.WorldHeightOf 的树档）。
# 该 k 同时是"原版占格 -> 第一人称米制"的唯一换算；灵树 3.50m 为自然落点。
TREE_K = 3.0 / (sum(_cells(t)[1] for t in TREES) / len(TREES))


def tree_size(name):
    w, h = _cells(name)
    return dict(H=round(h * TREE_K, 2), D=round(w * TREE_K, 2))


def small_size(name, H):
    """小株：高度保留现实约定，宽度按贴图内容宽高比（= 原版立牌形状）。
    方案 A 下高度也走占格推导（出对照图用）。"""
    if POLICY == "A":
        return tree_size(name)
    _, _, cv, cw, ch = VANILLA[name]
    return dict(H=H, D=round(H * cw / ch, 2))


# ---------------------------------------------------------------------------
# 原型清单：采样来源 + 世界尺寸 + 几何配方
# 相对系数取样：(cx0, cy0, cx1, cy1) 相对内容包围盒。
# ---------------------------------------------------------------------------
SPEC = {
    # ---- 树 14（H/D = 原版占格 × TREE_K，宽高比与贴图严格一致）----
    "oak":      dict(tex=f"{CORE}/treeoak/treeoaka", **tree_size("oak"),
                     build="oak", bark=(0.44, 0.62, 0.56, 0.96),
                     leaf=(0.12, 0.05, 0.48, 0.42), leaf2=(0.52, 0.14, 0.88, 0.52)),
    "maple":    dict(tex=f"{CORE}/treemaple/treemaplea", **tree_size("maple"),
                     build="maple", bark=(0.48, 0.76, 0.56, 0.98),
                     leaf=(0.14, 0.06, 0.48, 0.42), leaf2=(0.54, 0.14, 0.88, 0.52)),
    "poplar":   dict(tex=f"{CORE}/treepoplar/treepoplara", **tree_size("poplar"),
                     build="poplar", bark=(0.44, 0.82, 0.56, 0.98),
                     leaf=(0.28, 0.06, 0.72, 0.42), leaf2=(0.28, 0.44, 0.72, 0.78)),
    "pine":     dict(tex=f"{CORE}/treepine/treepinea", **tree_size("pine"),
                     build="pine", bark=(0.45, 0.88, 0.55, 0.99),
                     leaf=(0.34, 0.12, 0.66, 0.44), leaf2=(0.26, 0.46, 0.74, 0.78)),
    "cypress":  dict(tex=f"{CORE}/treecypress/treecypressa", **tree_size("cypress"),
                     build="cypress", bark=(0.43, 0.70, 0.58, 0.96),
                     leaf=(0.18, 0.06, 0.54, 0.38), leaf2=(0.48, 0.18, 0.84, 0.52)),
    "birch":    dict(tex=f"{CORE}/treebirch/treebircha", **tree_size("birch"),
                     build="birch", bark=(0.43, 0.56, 0.52, 0.96),
                     leaf=(0.16, 0.05, 0.52, 0.38), leaf2=(0.50, 0.14, 0.86, 0.48)),
    "palm":     dict(tex=f"{CORE}/treepalm/treepalma", **tree_size("palm"),
                     build="palm", bark=(0.45, 0.48, 0.55, 0.96),
                     leaf=(0.08, 0.04, 0.44, 0.32), leaf2=(0.56, 0.06, 0.92, 0.36)),
    "drago":    dict(tex=f"{CORE}/treedrago/treedragoa", **tree_size("drago"),
                     build="drago", bark=(0.40, 0.66, 0.60, 0.96),
                     leaf=(0.18, 0.06, 0.54, 0.28), leaf2=(0.48, 0.14, 0.84, 0.36)),
    "teak":     dict(tex=f"{CORE}/treeteak/treeteaka", **tree_size("teak"),
                     build="teak", bark=(0.43, 0.54, 0.56, 0.96),
                     leaf=(0.14, 0.05, 0.50, 0.40), leaf2=(0.52, 0.12, 0.88, 0.48)),
    "willow":   dict(tex=f"{CORE}/treewillow/treewillowa", **tree_size("willow"),
                     build="willow", bark=(0.44, 0.62, 0.56, 0.96),
                     leaf=(0.14, 0.04, 0.50, 0.34), leaf2=(0.52, 0.12, 0.88, 0.46)),
    "bamboo":   dict(tex=f"{CORE}/treebamboo/treebambooa", **tree_size("bamboo"),
                     build="bamboo", bark=(0.28, 0.62, 0.40, 0.95),
                     leaf=(0.48, 0.06, 0.86, 0.38), leaf2=(0.12, 0.14, 0.48, 0.48)),
    "cocoa":    dict(tex=f"{CORE}/treecocoa/treecocoaa", **tree_size("cocoa"),
                     build="cocoa", bark=(0.45, 0.72, 0.55, 0.97),
                     leaf=(0.12, 0.04, 0.48, 0.38), leaf2=(0.52, 0.10, 0.88, 0.44),
                     accent=(0.28, 0.52, 0.58, 0.82)),
    "cecropia": dict(tex=f"{CORE}/treececropia/treececropiaa", **tree_size("cecropia"),
                     build="cecropia", bark=(0.45, 0.54, 0.55, 0.96),
                     leaf=(0.10, 0.04, 0.46, 0.38), leaf2=(0.54, 0.08, 0.90, 0.42)),
    "anima":    dict(tex=f"{ROY}/treeanima/treeanimaa", **tree_size("anima"),
                     build="anima", bark=(0.42, 0.68, 0.58, 0.97),
                     leaf=(0.18, 0.04, 0.54, 0.38), leaf2=(0.56, 0.18, 0.92, 0.52),
                     accent=(0.36, 0.26, 0.46, 0.36)),
    # ---- 小株 10（高度=现实约定；宽度=贴图宽高比；saguaro 按 def 归树档）----
    "saguaro":    dict(tex=f"{CORE}/saguarocactus/saguarocactus", **tree_size("saguaro"),
                       build="saguaro", leaf=(0.24, 0.10, 0.62, 0.54), leaf2=(0.38, 0.32, 0.78, 0.76)),
    "bush":       dict(tex=f"{CORE}/bush/busha", **small_size("bush", 0.55),
                       build="bush", leaf=(0.14, 0.10, 0.54, 0.56), leaf2=(0.46, 0.24, 0.86, 0.72)),
    "berry":      dict(tex=f"{CORE}/berryplant/berrybusha", **small_size("berry", 0.5),
                       build="berry", leaf=(0.14, 0.08, 0.54, 0.48), leaf2=(0.46, 0.24, 0.86, 0.68),
                       accent=(0.22, 0.22, 0.78, 0.72)),
    "brambles":   dict(tex=f"{CORE}/brambles/bramblesa", **small_size("brambles", 0.45),
                       build="brambles", leaf=(0.14, 0.06, 0.52, 0.44), leaf2=(0.48, 0.18, 0.86, 0.56),
                       bark=(0.24, 0.62, 0.76, 0.94)),
    "agave":      dict(tex=f"{CORE}/agave/agavea", **small_size("agave", 0.8),
                       build="agave", leaf=(0.16, 0.12, 0.54, 0.58), leaf2=(0.46, 0.28, 0.84, 0.74)),
    "alocasia":   dict(tex=f"{CORE}/alocasia/alocasiaa", **small_size("alocasia", 0.9),
                       build="alocasia", leaf=(0.12, 0.08, 0.52, 0.54), leaf2=(0.48, 0.22, 0.88, 0.70)),
    "shrublow":   dict(tex=f"{CORE}/shrublow/shrublowa", **small_size("shrublow", 0.32),
                       build="shrublow", leaf=(0.16, 0.12, 0.54, 0.56), leaf2=(0.46, 0.24, 0.84, 0.68)),
    "pincushion": dict(tex=f"{CORE}/pincushioncactus/pincushioncactus", **small_size("pincushion", 0.38),
                       build="pincushion", leaf=(0.18, 0.14, 0.58, 0.60), leaf2=(0.42, 0.28, 0.82, 0.74)),
    "healroot":   dict(tex=f"{CORE}/healroot/healroota", **small_size("healroot", 0.32),
                       build="healroot", bark=(0.38, 0.56, 0.62, 0.94),
                       leaf=(0.22, 0.06, 0.78, 0.42), leaf2=(0.12, 0.28, 0.88, 0.68)),
    "devilstrand": dict(tex=f"{CORE}/devilstrand/devilstranda", **small_size("devilstrand", 0.36),
                        build="devilstrand", bark=(0.36, 0.60, 0.64, 0.95),
                        leaf=(0.18, 0.06, 0.82, 0.46), leaf2=(0.22, 0.48, 0.78, 0.66)),
}


# ---------------------------------------------------------------------------
# 低模几何内核（右手系，y 上，底 y=0；面朝外 CCW —— 导入器反转后即 Unity 正面）
# 每个多面体切面计算真实面法线，并根据阳光方向映射进取样区纵向明暗带。
# ---------------------------------------------------------------------------

SUN_DIR = (0.38, 0.78, 0.49)  # 归一化主光方向（右前上方，兼顾 45°/主视·南/贴脸/俯视）


class MeshOut:
    def __init__(self):
        self.v = []   # (x,y,z)
        self.n = []   # (x,y,z)
        self.uv = []  # (u,v)
        self.f = []   # (i0,i1,i2) 0-based

    def add_vert(self, p, n, uv):
        self.v.append(p)
        self.n.append(n)
        self.uv.append(uv)
        return len(self.v) - 1

    def tri(self, a, b, c):
        self.f.append((a, b, c))


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def _mul(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def _norm(v):
    l = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    return (v[0] / l, v[1] / l, v[2] / l) if l > 1e-9 else (0.0, 1.0, 0.0)


def _face_normal(p0, p1, p2):
    return _norm(_cross(_sub(p1, p0), _sub(p2, p0)))


def _clamp(x, lo, hi):
    return lo if x < lo else (hi if x > hi else x)


def rot_yaw_pitch(p, yaw_deg, pitch_deg, origin=(0.0, 0.0, 0.0)):
    """绕 origin 先绕 X 转 pitch（度，正=顶端向 +z 倒），再绕 Y 转 yaw（度）。"""
    x, y, z = p[0] - origin[0], p[1] - origin[1], p[2] - origin[2]
    if pitch_deg:
        pa = math.radians(pitch_deg)
        cp, sp = math.cos(pa), math.sin(pa)
        y, z = y * cp - z * sp, y * sp + z * cp
    if yaw_deg:
        ya = math.radians(yaw_deg)
        cy, sy = math.cos(ya), math.sin(ya)
        x, z = x * cy - z * sy, x * sy + z * cy
    return (x + origin[0], y + origin[1], z + origin[2])


def add_faceted_tri(out, p0, p1, p2, region, shade_bias=0.0, u_seed=0.5):
    """写入一个低模平面三角形（独立面法线 + 按面受光度映射到取样区纵向明暗带）。"""
    n = _face_normal(p0, p1, p2)
    sun = _dot(n, SUN_DIR)
    lit = 0.52 + 0.24 * n[1] + 0.26 * sun + shade_bias
    lit = _clamp(lit, 0.10, 0.90)
    u0, v0, u1, v1 = region
    du = u1 - u0
    dv = v1 - v0
    uc = u0 + du * _clamp(u_seed, 0.18, 0.82)
    vc = v0 + dv * lit
    su = du * 0.12
    sv = dv * 0.055
    ia = out.add_vert(p0, n, (uc - su, _clamp(vc - sv, v0 + 0.02 * dv, v1 - 0.02 * dv)))
    ib = out.add_vert(p1, n, (uc + su, _clamp(vc - sv * 0.5, v0 + 0.02 * dv, v1 - 0.02 * dv)))
    ic = out.add_vert(p2, n, (uc, _clamp(vc + sv, v0 + 0.02 * dv, v1 - 0.02 * dv)))
    out.tri(ia, ib, ic)


def add_faceted_quad(out, p0, p1, p2, p3, region, shade_bias=0.0, u_seed=0.5):
    """写入一个低模平面四边形（拆两个共享受光度的三角形）。"""
    n = _face_normal(p0, p1, p2)
    sun = _dot(n, SUN_DIR)
    lit = _clamp(0.52 + 0.24 * n[1] + 0.26 * sun + shade_bias, 0.10, 0.90)
    u0, v0, u1, v1 = region
    du, dv = u1 - u0, v1 - v0
    uc = u0 + du * _clamp(u_seed, 0.18, 0.82)
    vc = v0 + dv * lit
    su, sv = du * 0.13, dv * 0.06
    va = _clamp(vc - sv, v0 + 0.02 * dv, v1 - 0.02 * dv)
    vb = _clamp(vc + sv, v0 + 0.02 * dv, v1 - 0.02 * dv)
    ia = out.add_vert(p0, n, (uc - su, va))
    ib = out.add_vert(p1, n, (uc + su, va))
    ic = out.add_vert(p2, n, (uc + su, vb))
    id_ = out.add_vert(p3, n, (uc - su, vb))
    out.tri(ia, ib, ic)
    out.tri(ia, ic, id_)


def _basis_from_dir(d):
    """给定单位方向向量 d，构造正交基 (right, fwd)。"""
    up = (0.0, 1.0, 0.0) if abs(d[1]) < 0.92 else (1.0, 0.0, 0.0)
    right = _norm(_cross(up, d))
    fwd = _norm(_cross(d, right))
    return right, fwd


def branch_tube(out, region, pts, radii, seg=7, cap_start=True, cap_end=True,
                bark_uv=True, shade_bias=0.0):
    """沿折线点列 pts=[p0,p1,...] 与半径列 radii=[r0,r1,...] 生成闭合棱柱管（树干/分枝/茎/仙人掌柱）。"""
    u0, v0, u1, v1 = region
    du, dv = u1 - u0, v1 - v0
    m = len(pts)
    rings = []
    arc = [0.0]
    for k in range(1, m):
        d = _sub(pts[k], pts[k - 1])
        arc.append(arc[-1] + math.sqrt(_dot(d, d)))
    total_len = max(1e-6, arc[-1])

    for k in range(m):
        if k == 0:
            tang = _norm(_sub(pts[1], pts[0]))
        elif k == m - 1:
            tang = _norm(_sub(pts[-1], pts[-2]))
        else:
            tang = _norm(_add(_norm(_sub(pts[k], pts[k - 1])), _norm(_sub(pts[k + 1], pts[k]))))
        right, fwd = _basis_from_dir(tang)
        r = radii[k]
        ring = []
        for i in range(seg):
            a = i / seg * math.pi * 2.0
            ca, sa = math.cos(a), math.sin(a)
            radial = _add(_mul(right, ca), _mul(fwd, sa))
            p = _add(pts[k], _mul(radial, r))
            ring.append((p, radial))
        rings.append(ring)

    for k in range(m - 1):
        v_lo = v0 + dv * (0.06 + 0.88 * (arc[k] / total_len))
        v_hi = v0 + dv * (0.06 + 0.88 * (arc[k + 1] / total_len))
        for i in range(seg):
            j = (i + 1) % seg
            p0, _ = rings[k][i]
            p1, _ = rings[k][j]
            p2, _ = rings[k + 1][j]
            p3, _ = rings[k + 1][i]
            if bark_uv:
                t0 = 1.0 - abs(2.0 * (i / seg) - 1.0)
                t1 = 1.0 - abs(2.0 * ((i + 1) / seg) - 1.0)
                n = _face_normal(p0, p1, p2)
                sun = _dot(n, SUN_DIR)
                u_shift = _clamp(0.12 * sun + shade_bias * 0.1, -0.12, 0.12)
                qa = u0 + du * _clamp(0.14 + 0.72 * t0 + u_shift, 0.04, 0.96)
                qb = u0 + du * _clamp(0.14 + 0.72 * t1 + u_shift, 0.04, 0.96)
                ia = out.add_vert(p0, n, (qa, v_lo))
                ib = out.add_vert(p1, n, (qb, v_lo))
                ic = out.add_vert(p2, n, (qb, v_hi))
                id_ = out.add_vert(p3, n, (qa, v_hi))
                out.tri(ia, ib, ic)
                out.tri(ia, ic, id_)
            else:
                add_faceted_quad(out, p0, p1, p2, p3, region,
                                 shade_bias=shade_bias, u_seed=0.25 + 0.5 * (i / seg))

    if cap_start and radii[0] > 1e-4:
        c0 = pts[0]
        for i in range(seg):
            j = (i + 1) % seg
            add_faceted_tri(out, c0, rings[0][j][0], rings[0][i][0], region, shade_bias=shade_bias - 0.15)
    if cap_end and radii[-1] > 1e-4:
        c1 = pts[-1]
        for i in range(seg):
            j = (i + 1) % seg
            add_faceted_tri(out, c1, rings[-1][i][0], rings[-1][j][0], region, shade_bias=shade_bias + 0.08)


def lowpoly_blob(out, region, center, rx, ry, rz, lon=7, lat=4, seed=1,
                 yaw=0.0, pitch=0.0, shade_bias=0.0, flat_bottom=0.25,
                 crown_pow=0.88, jitter=0.07):
    """低模多面体叶团（Crystal/Faceted Foliage Cluster）：
    center 为叶团底面中心（球心在 cy + ry），顶点带确定性有机切面起伏，
    每个三角切面独立法线与明暗分层 UV，360° 零极点接缝。
    """
    cx, cy, cz = center
    c_mid = (cx, cy + ry, cz)

    def _hash_jit(iy, ix):
        h = (seed * 73856093) ^ (iy * 19349663) ^ (ix * 83492791)
        return (((h & 0xFFFF) / 65535.0) * 2.0 - 1.0) * jitter

    rings = []
    for y in range(1, lat):
        t = y / lat
        phi = (1.0 - t) * math.pi
        ny = math.cos(phi)
        if ny < 0.0:
            ny = ny * (1.0 - flat_bottom)
        else:
            ny = math.pow(ny, crown_pow)
        ring_r = math.sin(phi)
        if t > 0.55:
            ring_r *= 1.06
        twist = (y % 2) * (math.pi / lon) * 0.55
        row = []
        for x in range(lon):
            th = x / lon * math.pi * 2.0 + twist
            jr = 1.0 + _hash_jit(y, x)
            jy = _hash_jit(y + 17, x + 31) * 0.18
            px = math.cos(th) * ring_r * rx * jr
            py = (ny + jy) * ry + ry
            pz = math.sin(th) * ring_r * rz * jr
            p = (cx + px, cy + py, cz + pz)
            if yaw or pitch:
                p = rot_yaw_pitch(p, yaw, pitch, c_mid)
            row.append(p)
        rings.append(row)

    bot_pt = (cx, cy + ry * flat_bottom * 0.25, cz)
    top_pt = (cx, cy + ry * 2.0, cz)
    if yaw or pitch:
        bot_pt = rot_yaw_pitch(bot_pt, yaw, pitch, c_mid)
        top_pt = rot_yaw_pitch(top_pt, yaw, pitch, c_mid)

    for x in range(lon):
        nx_ = (x + 1) % lon
        add_faceted_tri(out, bot_pt, rings[0][x], rings[0][nx_], region,
                        shade_bias=shade_bias - 0.16, u_seed=(x + 0.5) / lon)

    for y in range(len(rings) - 1):
        r0 = rings[y]
        r1 = rings[y + 1]
        sb = shade_bias + (-0.10 + 0.24 * ((y + 0.5) / (len(rings) - 1)))
        for x in range(lon):
            nx_ = (x + 1) % lon
            u_s = 0.15 + 0.70 * ((x + 0.5) / lon)
            add_faceted_tri(out, r0[x], r1[x], r0[nx_], region, shade_bias=sb, u_seed=u_s)
            add_faceted_tri(out, r0[nx_], r1[x], r1[nx_], region, shade_bias=sb + 0.03, u_seed=u_s)

    top_ring = rings[-1]
    for x in range(lon):
        nx_ = (x + 1) % lon
        add_faceted_tri(out, top_ring[x], top_pt, top_ring[nx_], region,
                        shade_bias=shade_bias + 0.16, u_seed=(x + 0.5) / lon)


def arched_frond(out, region, origin, yaw_deg, length, width, thickness,
                 arch_h, drop_h, segs=5, pitch_start=0.0, shade_bias=0.0):
    """3D 拱形立体叶片（用于棕榈叶、灵树长叶、号角树裂叶、龙舌兰剑叶、草药垂叶）：
    沿抛物线轨迹伸展，横截面为立体四角菱形（上脊、左翼、右翼、底脊），两端闭合收尖。
    """
    ox, oy, oz = origin
    rings = []
    for k in range(segs + 1):
        t = k / segs
        z_local = length * t
        y_local = arch_h * math.sin(t * math.pi * 0.72) - drop_h * (t * t)
        if pitch_start:
            pa = math.radians(pitch_start)
            z_local, y_local = (z_local * math.cos(pa) - y_local * math.sin(pa),
                                z_local * math.sin(pa) + y_local * math.cos(pa))
        w_env = math.sin(math.pow(t, 0.75) * math.pi)
        if k == 0:
            w_env = 0.22
        elif k == segs:
            w_env = 0.0
        hw = width * 0.5 * w_env
        ht = thickness * 0.5 * (1.0 - 0.65 * t)
        wing_drop = hw * 0.24
        p_top = rot_yaw_pitch((ox, oy + y_local + ht, oz + z_local), yaw_deg, 0.0, origin)
        p_right = rot_yaw_pitch((ox + hw, oy + y_local - wing_drop, oz + z_local), yaw_deg, 0.0, origin)
        p_bot = rot_yaw_pitch((ox, oy + y_local - ht - wing_drop * 0.5, oz + z_local), yaw_deg, 0.0, origin)
        p_left = rot_yaw_pitch((ox - hw, oy + y_local - wing_drop, oz + z_local), yaw_deg, 0.0, origin)
        rings.append((p_top, p_right, p_bot, p_left))

    r0 = rings[0]
    add_faceted_tri(out, r0[0], r0[1], r0[3], region, shade_bias=shade_bias - 0.10)
    add_faceted_tri(out, r0[2], r0[3], r0[1], region, shade_bias=shade_bias - 0.15)

    for k in range(segs):
        a = rings[k]
        b = rings[k + 1]
        sb = shade_bias + 0.08 * (1.0 - k / segs)
        if k == segs - 1:
            tip = b[0]
            add_faceted_tri(out, a[0], tip, a[1], region, shade_bias=sb + 0.08, u_seed=0.35)
            add_faceted_tri(out, a[1], tip, a[2], region, shade_bias=sb - 0.12, u_seed=0.65)
            add_faceted_tri(out, a[2], tip, a[3], region, shade_bias=sb - 0.12, u_seed=0.35)
            add_faceted_tri(out, a[3], tip, a[0], region, shade_bias=sb + 0.06, u_seed=0.65)
        else:
            add_faceted_quad(out, a[0], b[0], b[1], a[1], region, shade_bias=sb + 0.08, u_seed=0.35)
            add_faceted_quad(out, a[1], b[1], b[2], a[2], region, shade_bias=sb - 0.12, u_seed=0.65)
            add_faceted_quad(out, a[2], b[2], b[3], a[3], region, shade_bias=sb - 0.12, u_seed=0.35)
            add_faceted_quad(out, a[3], b[3], b[0], a[0], region, shade_bias=sb + 0.06, u_seed=0.65)


# ---------------------------------------------------------------------------
# 24 个原型的低模几何配方（严格对齐原版贴图剪影与结构特征）
# ---------------------------------------------------------------------------

def build_oak(o, g, regions):
    """橡树（treeoaka）：敦实棕干 + Y形三主枝 + 上宽下收的饱满扇球形多面体绿冠（左中右三峰并立）。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (0.01, H * 0.18, 0.01), (0.0, H * 0.40, 0.0)],
                [D * 0.120, D * 0.092, D * 0.078], seg=8)
    fork_y = H * 0.32
    for bx, by, bz, br in [
        (-D * 0.28, H * 0.56, D * 0.04, D * 0.048),
        (D * 0.28, H * 0.55, -D * 0.04, D * 0.048),
        (0.02, H * 0.60, -D * 0.18, D * 0.044),
    ]:
        branch_tube(o, regions["bark"],
                    [(0, fork_y, 0), (bx * 0.55, (fork_y + by) * 0.5, bz * 0.55), (bx, by, bz)],
                    [D * 0.068, br * 1.15, br * 0.75], seg=6, cap_start=False)

    # 下层托底 + 中上层极宽（对齐原版 treeoaka 上宽下收、三峰饱满的冠型）
    blobs = [
        (-0.22, 0.36, 0.06, 0.26, 0.15, 0.25, "leaf2", 1, -0.08),
        (0.22, 0.37, -0.04, 0.26, 0.15, 0.25, "leaf2", 2, -0.07),
        (0.00, 0.36, 0.18, 0.25, 0.14, 0.24, "leaf2", 3, -0.09),
        (0.00, 0.37, -0.18, 0.25, 0.14, 0.24, "leaf2", 4, -0.09),
        (-0.30, 0.49, 0.02, 0.31, 0.20, 0.29, "leaf", 5, 0.04),
        (0.30, 0.50, -0.02, 0.31, 0.20, 0.29, "leaf", 6, 0.05),
        (0.00, 0.46, 0.22, 0.29, 0.18, 0.28, "leaf", 7, 0.03),
        (0.00, 0.47, -0.22, 0.29, 0.18, 0.28, "leaf2", 8, -0.02),
        (-0.20, 0.60, -0.02, 0.28, 0.18, 0.27, "leaf", 9, 0.10),
        (0.20, 0.60, 0.02, 0.28, 0.18, 0.27, "leaf", 10, 0.10),
        (0.00, 0.62, 0.00, 0.32, 0.19, 0.31, "leaf", 11, 0.14),
    ]
    for dx, yf, dz, rxs, rys, rzs, rk, sd, sb in blobs:
        lowpoly_blob(o, regions[rk], (dx * D, H * yf, dz * D),
                     D * rxs, H * rys, D * rzs, lon=8, lat=4, seed=sd, shade_bias=sb)


def build_maple(o, g, regions):
    """枫树（treemaplea）：银灰白主干在中央半露直通冠顶 + 左右宽展层叠的火红枫叶云团。"""
    H, D = g["H"], g["D"]
    # 银灰白主干略微偏前 (+z=0.04D)，让正南主视能像原版贴图一样清晰看到白树干穿过红叶层
    branch_tube(o, regions["bark"],
                [(0, 0, 0.02 * D), (0.01 * D, H * 0.28, 0.04 * D),
                 (-0.01 * D, H * 0.56, 0.04 * D), (0.0, H * 0.78, 0.02 * D)],
                [D * 0.095, D * 0.074, D * 0.058, D * 0.038], seg=7)
    for y0, bx, by, bz in [
        (0.26, -0.28, 0.38, 0.02),
        (0.28, 0.28, 0.40, 0.02),
        (0.44, -0.26, 0.56, 0.00),
        (0.46, 0.26, 0.58, 0.00),
        (0.60, -0.18, 0.72, 0.00),
        (0.62, 0.18, 0.73, 0.00),
    ]:
        branch_tube(o, regions["bark"],
                    [(0, H * y0, 0.03 * D), (bx * D, H * by, bz * D)],
                    [D * 0.045, D * 0.026], seg=5, cap_start=False)

    # 左右宽展层叠红叶团 + 前后侧翼（留出正前中轴白干走廊，同时保证俯视饱满浑圆）
    clouds = [
        # 第1层（底宽翼）
        (-0.26, 0.25, 0.06, 0.27, 0.11, 0.25, "leaf2", 11, -0.08),
        (0.26, 0.26, 0.06, 0.27, 0.11, 0.25, "leaf2", 12, -0.07),
        (-0.14, 0.27, -0.18, 0.26, 0.11, 0.25, "leaf2", 13, -0.09),
        (0.14, 0.27, -0.18, 0.26, 0.11, 0.25, "leaf2", 23, -0.09),
        # 第2层（中下最宽翼）
        (-0.29, 0.39, 0.08, 0.29, 0.12, 0.27, "leaf", 14, 0.02),
        (0.29, 0.40, 0.08, 0.29, 0.12, 0.27, "leaf", 15, 0.03),
        (0.00, 0.41, -0.20, 0.32, 0.13, 0.26, "leaf2", 16, -0.03),
        # 第3层（中上宽翼）
        (-0.24, 0.54, 0.06, 0.27, 0.12, 0.25, "leaf", 17, 0.06),
        (0.24, 0.55, 0.06, 0.27, 0.12, 0.25, "leaf", 18, 0.07),
        (0.00, 0.56, -0.16, 0.29, 0.13, 0.25, "leaf", 19, 0.04),
        # 第4层（冠顶合拢）
        (-0.14, 0.68, 0.03, 0.23, 0.12, 0.23, "leaf", 20, 0.11),
        (0.14, 0.69, 0.03, 0.23, 0.12, 0.23, "leaf", 21, 0.11),
        (0.00, 0.73, 0.00, 0.25, 0.12, 0.25, "leaf", 22, 0.15),
    ]
    for dx, yf, dz, rxs, rys, rzs, rk, sd, sb in clouds:
        lowpoly_blob(o, regions[rk], (dx * D, H * yf, dz * D),
                     D * rxs, H * rys, D * rzs, lon=7, lat=4, seed=sd,
                     shade_bias=sb, flat_bottom=0.42)


def build_poplar(o, g, regions):
    """杨树（treepoplara）：底部短粗棕干 + 连绵一体的高耸修长纺锤柱状多层次绿冠。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (0, H * 0.20, 0), (0, H * 0.65, 0)],
                [D * 0.15, D * 0.11, D * 0.06], seg=7)
    tiers = [
        (0.14, 0.42, 0.20, "leaf2", 31, -0.10),
        (0.28, 0.47, 0.22, "leaf2", 32, -0.04),
        (0.44, 0.45, 0.22, "leaf", 33, 0.02),
        (0.60, 0.39, 0.20, "leaf", 34, 0.08),
        (0.74, 0.28, 0.16, "leaf", 35, 0.14),
    ]
    for yf, rxs, rys, rk, sd, sb in tiers:
        lowpoly_blob(o, regions[rk], (0, H * yf, 0),
                     D * rxs, H * rys, D * rxs, lon=8, lat=4, seed=sd,
                     shade_bias=sb, crown_pow=0.75)
    for k in range(6):
        a = math.radians(k * 60.0 + 25.0)
        yf = 0.22 + (k % 3) * 0.16
        rad = D * 0.16
        lowpoly_blob(o, regions["leaf" if k % 2 == 0 else "leaf2"],
                     (math.cos(a) * rad, H * yf, math.sin(a) * rad),
                     D * 0.28, H * 0.16, D * 0.28, lon=6, lat=3, seed=40 + k,
                     shade_bias=-0.02 + 0.04 * (k % 3))


def _pine_saw_cone(o, region, y0, h, r_out, r_in, lobes=8, twist_deg=0.0, shade_bias=0.0):
    """松树锯齿针叶裙：环向 lobes 个外伸针叶尖与 lobes 个内折谷交替，带底封口。"""
    n2 = lobes * 2
    apex = (0.0, y0 + h, 0.0)
    rim = []
    inner = []
    for i in range(n2):
        a = math.radians(i * (360.0 / n2) + twist_deg)
        is_tip = (i % 2 == 0)
        rad = r_out if is_tip else r_in
        y_rim = (y0 - h * 0.06) if is_tip else (y0 + h * 0.05)
        rim.append((math.cos(a) * rad, y_rim, math.sin(a) * rad))
        inner.append((math.cos(a) * r_in * 0.32, y0 + h * 0.04, math.sin(a) * r_in * 0.32))
    for i in range(n2):
        j = (i + 1) % n2
        add_faceted_tri(o, rim[i], apex, rim[j], region,
                        shade_bias=shade_bias + (0.05 if i % 2 == 0 else -0.04),
                        u_seed=(i + 0.5) / n2)
        add_faceted_quad(o, inner[i], rim[i], rim[j], inner[j], region,
                         shade_bias=shade_bias - 0.18, u_seed=(i + 0.5) / n2)


def build_pine(o, g, regions):
    """松树（treepinea）：底部棕干 + 5 层星形多面体锯齿针叶锥裙（层叠尖塔剪影）。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (0, H * 0.16, 0), (0, H * 0.72, 0)],
                [D * 0.10, D * 0.082, D * 0.035], seg=7)
    tiers = [
        (0.11, 0.34, 0.50, 0.35, 9, 0.0, "leaf2", -0.10),
        (0.27, 0.32, 0.43, 0.30, 8, 22.5, "leaf2", -0.04),
        (0.43, 0.30, 0.35, 0.24, 8, 8.0, "leaf", 0.02),
        (0.58, 0.27, 0.26, 0.18, 7, 31.0, "leaf", 0.08),
        (0.73, 0.27, 0.17, 0.11, 6, 15.0, "leaf", 0.14),
    ]
    for yf, hf, ro, ri, lb, tw, rk, sb in tiers:
        _pine_saw_cone(o, regions[rk], H * yf, H * hf, D * ro, D * ri,
                       lobes=lb, twist_deg=tw, shade_bias=sb)


def build_cypress(o, g, regions):
    """柏树（treecypressa）：对齐原版宽喇叭板根棕灰树干 + 分叉枝 + 中上部宽阔蓬松的灰蓝绿云朵圆拱冠。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (0, H * 0.12, 0), (0, H * 0.28, 0), (0, H * 0.45, 0)],
                [D * 0.22, D * 0.135, D * 0.100, D * 0.082], seg=8)
    for k in range(4):
        a = math.radians(k * 90.0 + 35.0)
        ca, sa = math.cos(a), math.sin(a)
        branch_tube(o, regions["bark"],
                    [(ca * D * 0.20, 0, sa * D * 0.20), (ca * D * 0.07, H * 0.20, sa * D * 0.07)],
                    [D * 0.055, D * 0.030], seg=5)
    for bx, by, bz in [(-0.26, 0.56, 0.04), (0.26, 0.55, -0.05), (0.02, 0.58, 0.18)]:
        branch_tube(o, regions["bark"],
                    [(0, H * 0.36, 0), (bx * D, H * by, bz * D)],
                    [D * 0.062, D * 0.038], seg=6, cap_start=False)

    blobs = [
        (-0.22, 0.38, 0.05, 0.26, 0.14, 0.25, "leaf2", 51, -0.08),
        (0.22, 0.39, -0.04, 0.26, 0.14, 0.25, "leaf2", 52, -0.07),
        (0.02, 0.38, 0.18, 0.24, 0.14, 0.24, "leaf2", 53, -0.08),
        (-0.02, 0.39, -0.18, 0.24, 0.14, 0.24, "leaf2", 54, -0.08),
        (-0.30, 0.50, -0.04, 0.31, 0.18, 0.29, "leaf", 55, 0.04),
        (0.30, 0.51, 0.04, 0.31, 0.18, 0.29, "leaf", 56, 0.05),
        (0.00, 0.48, 0.20, 0.29, 0.17, 0.28, "leaf", 57, 0.04),
        (0.00, 0.49, -0.20, 0.29, 0.17, 0.28, "leaf2", 60, -0.02),
        (-0.18, 0.60, 0.03, 0.28, 0.17, 0.27, "leaf", 58, 0.11),
        (0.18, 0.60, -0.03, 0.28, 0.17, 0.27, "leaf", 59, 0.11),
        (0.00, 0.62, 0.00, 0.31, 0.18, 0.30, "leaf", 68, 0.14),
    ]
    for dx, yf, dz, rxs, rys, rzs, rk, sd, sb in blobs:
        lowpoly_blob(o, regions[rk], (dx * D, H * yf, dz * D),
                     D * rxs, H * rys, D * rzs, lon=8, lat=4, seed=sd, shade_bias=sb)


def build_birch(o, g, regions):
    """桦树（treebircha）：修长挺拔的白底黑斑桦木干 + 双分枝 + 浑圆饱满的多簇嫩绿冠。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (-0.01, H * 0.26, 0.0), (0.01, H * 0.50, 0.0), (0.0, H * 0.64, 0.0)],
                [D * 0.086, D * 0.070, D * 0.056, D * 0.038], seg=8, bark_uv=True)
    for bx, by, bz in [(-0.20, 0.58, 0.04), (0.20, 0.57, -0.04)]:
        branch_tube(o, regions["bark"],
                    [(0.01, H * 0.44, 0), (bx * D, H * by, bz * D)],
                    [D * 0.044, D * 0.026], seg=6, cap_start=False, bark_uv=True)

    blobs = [
        (0.00, 0.42, 0.00, 0.32, 0.16, 0.32, "leaf2", 61, -0.06),
        (-0.26, 0.48, 0.05, 0.29, 0.18, 0.28, "leaf2", 62, -0.03),
        (0.26, 0.49, -0.04, 0.29, 0.18, 0.28, "leaf2", 63, -0.03),
        (0.03, 0.48, 0.22, 0.27, 0.17, 0.27, "leaf", 64, 0.02),
        (-0.04, 0.49, -0.22, 0.27, 0.17, 0.27, "leaf", 65, 0.02),
        (-0.16, 0.59, -0.03, 0.28, 0.17, 0.27, "leaf", 66, 0.09),
        (0.16, 0.59, 0.03, 0.28, 0.17, 0.27, "leaf", 67, 0.10),
        (0.00, 0.62, 0.00, 0.30, 0.18, 0.29, "leaf", 69, 0.14),
    ]
    for dx, yf, dz, rxs, rys, rzs, rk, sd, sb in blobs:
        lowpoly_blob(o, regions[rk], (dx * D, H * yf, dz * D),
                     D * rxs, H * rys, D * rzs, lon=8, lat=4, seed=sd, shade_bias=sb)


def build_palm(o, g, regions):
    """棕榈树（treepalma）：微曲渐细环纹棕干 + 冠心球 + 10 片宽大拱形下垂的立体羽状椰叶。"""
    H, D = g["H"], g["D"]
    trunk_pts = [
        (0.0, 0.0, 0.0),
        (0.02, H * 0.24, -0.03),
        (0.05, H * 0.50, -0.07),
        (0.03, H * 0.76, -0.04),
    ]
    trunk_r = [D * 0.068, D * 0.052, D * 0.042, D * 0.036]
    branch_tube(o, regions["bark"], trunk_pts, trunk_r, seg=8, bark_uv=True)
    top = trunk_pts[-1]
    lowpoly_blob(o, regions["bark"], (top[0], top[1] - H * 0.04, top[2]),
                 D * 0.065, H * 0.045, D * 0.065, lon=7, lat=3, seed=70, shade_bias=-0.08)

    for k in range(6):
        yaw = k * 60.0 + 15.0
        rk = "leaf" if k % 2 == 0 else "leaf2"
        arched_frond(o, regions[rk], (top[0], top[1] - 0.02, top[2]),
                     yaw_deg=yaw, length=D * 0.49, width=D * 0.19, thickness=0.07,
                     arch_h=H * 0.18, drop_h=H * 0.30, segs=5, shade_bias=0.04 if k % 2 == 0 else -0.02)
    for k in range(4):
        yaw = k * 90.0 + 45.0
        arched_frond(o, regions["leaf"], (top[0], top[1] + 0.01, top[2]),
                     yaw_deg=yaw, length=D * 0.38, width=D * 0.16, thickness=0.06,
                     arch_h=H * 0.22, drop_h=H * 0.14, segs=4, shade_bias=0.12)


def build_drago(o, g, regions):
    """龙血树（treedragoa）：粗壮灰褐主干 + 6 根斜撑伞骨状分枝（中间镂空可见）+ 宽扁多瓣伞状冠盖。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (0, H * 0.18, 0), (0, H * 0.38, 0)],
                [D * 0.155, D * 0.125, D * 0.110], seg=8)
    for k in range(6):
        a = math.radians(k * 60.0 + 18.0)
        ca, sa = math.cos(a), math.sin(a)
        p0 = (ca * D * 0.05, H * 0.33, sa * D * 0.05)
        p1 = (ca * D * 0.18, H * 0.50, sa * D * 0.18)
        p2 = (ca * D * 0.28, H * 0.68, sa * D * 0.28)
        branch_tube(o, regions["bark"], [p0, p1, p2],
                    [D * 0.052, D * 0.042, D * 0.032], seg=6)

    lowpoly_blob(o, regions["leaf"], (0, H * 0.64, 0),
                 D * 0.40, H * 0.16, D * 0.40, lon=9, lat=4, seed=80,
                 shade_bias=0.08, flat_bottom=0.55)
    for k in range(6):
        a = math.radians(k * 60.0 + 18.0)
        cx = math.cos(a) * D * 0.27
        cz = math.sin(a) * D * 0.27
        rk = "leaf" if k % 2 == 0 else "leaf2"
        lowpoly_blob(o, regions[rk], (cx, H * 0.62, cz),
                     D * 0.25, H * 0.14, D * 0.25, lon=7, lat=4, seed=81 + k,
                     shade_bias=0.04 if k % 2 == 0 else -0.04, flat_bottom=0.50)


def build_teak(o, g, regions):
    """柚木（treeteaka）：粗壮挺直红棕主干 + 分枝 + 宽阔浑圆、棱角分明的大块多面体深绿叶团。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (0, H * 0.24, 0), (0, H * 0.46, 0)],
                [D * 0.120, D * 0.098, D * 0.084], seg=8)
    for bx, by, bz in [(-0.26, 0.58, 0.04), (0.26, 0.57, -0.04), (0.0, 0.62, -0.18)]:
        branch_tube(o, regions["bark"],
                    [(0, H * 0.38, 0), (bx * D, H * by, bz * D)],
                    [D * 0.060, D * 0.040], seg=6, cap_start=False)

    blobs = [
        (-0.22, 0.39, 0.06, 0.26, 0.15, 0.25, "leaf2", 91, -0.06),
        (0.22, 0.40, -0.05, 0.26, 0.15, 0.25, "leaf2", 92, -0.05),
        (0.02, 0.40, 0.19, 0.25, 0.14, 0.24, "leaf2", 93, -0.07),
        (-0.02, 0.41, -0.19, 0.25, 0.14, 0.24, "leaf2", 94, -0.07),
        (-0.29, 0.51, -0.03, 0.31, 0.19, 0.29, "leaf", 95, 0.05),
        (0.29, 0.52, 0.04, 0.31, 0.19, 0.29, "leaf", 96, 0.06),
        (0.00, 0.49, 0.21, 0.28, 0.18, 0.27, "leaf", 97, 0.04),
        (0.00, 0.50, -0.21, 0.28, 0.18, 0.27, "leaf2", 99, -0.01),
        (-0.18, 0.61, 0.02, 0.28, 0.18, 0.27, "leaf", 100, 0.11),
        (0.18, 0.61, -0.02, 0.28, 0.18, 0.27, "leaf", 101, 0.11),
        (0.00, 0.63, 0.00, 0.31, 0.19, 0.30, "leaf", 98, 0.14),
    ]
    for dx, yf, dz, rxs, rys, rzs, rk, sd, sb in blobs:
        lowpoly_blob(o, regions[rk], (dx * D, H * yf, dz * D),
                     D * rxs, H * rys, D * rzs, lon=7, lat=4, seed=sd,
                     shade_bias=sb, jitter=0.10)


def build_willow(o, g, regions):
    """柳树（treewillowa）：微曲深棕主干 + 宽厚浅绿伞形顶冠 + 20 束垂直柔顺悬垂的纤细柳丝帘幕。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (-0.02, H * 0.25, 0.01), (0.01, H * 0.56, 0.0)],
                [D * 0.115, D * 0.090, D * 0.072], seg=8)
    for k in range(4):
        a = math.radians(k * 90.0 + 30.0)
        branch_tube(o, regions["bark"],
                    [(0, H * 0.46, 0), (math.cos(a) * D * 0.22, H * 0.68, math.sin(a) * D * 0.22)],
                    [D * 0.050, D * 0.030], seg=5, cap_start=False)

    lowpoly_blob(o, regions["leaf"], (0, H * 0.60, 0),
                 D * 0.44, H * 0.18, D * 0.44, lon=9, lat=4, seed=101,
                 shade_bias=0.08, flat_bottom=0.45)
    for k in range(6):
        a = math.radians(k * 60.0 + 12.0)
        lowpoly_blob(o, regions["leaf" if k % 2 == 0 else "leaf2"],
                     (math.cos(a) * D * 0.24, H * 0.58, math.sin(a) * D * 0.24),
                     D * 0.26, H * 0.15, D * 0.26, lon=7, lat=3, seed=102 + k,
                     shade_bias=0.04, flat_bottom=0.40)

    n_curtain = 20
    for k in range(n_curtain):
        a = math.radians(k * (360.0 / n_curtain) + 7.0)
        ca, sa = math.cos(a), math.sin(a)
        r_start = D * (0.35 if k % 2 == 0 else 0.27)
        r_mid = r_start + D * 0.045
        r_end = r_mid + D * 0.015
        y_top = H * (0.76 if k % 2 == 0 else 0.71)
        drop_len = H * (0.45 if k % 3 == 0 else (0.39 if k % 3 == 1 else 0.33))
        pts = [
            (ca * r_start, y_top, sa * r_start),
            (ca * r_mid, y_top - drop_len * 0.30, sa * r_mid),
            (ca * r_end, y_top - drop_len * 0.68, sa * r_end),
            (ca * r_end, y_top - drop_len, sa * r_end),
        ]
        radii = [D * 0.034, D * 0.040, D * 0.028, D * 0.010]
        rk = "leaf" if k % 2 == 0 else "leaf2"
        branch_tube(o, regions[rk], pts, radii, seg=4, cap_start=False, bark_uv=False,
                    shade_bias=0.05 if k % 2 == 0 else -0.04)


def build_bamboo(o, g, regions):
    """竹子（treebambooa）：6 根青黄修长丛生竹竿 + 自中下部至顶端层层水平舒展的羽状竹叶丛。"""
    H, D = g["H"], g["D"]
    culms = [
        (-0.16, 0.06, 1.00, -0.05, 0.02),
        (0.04, -0.10, 0.96, 0.03, -0.04),
        (0.18, 0.05, 0.90, 0.06, 0.02),
        (-0.06, -0.04, 0.85, -0.02, -0.02),
        (0.10, 0.15, 0.78, 0.04, 0.05),
        (-0.20, -0.09, 0.72, -0.06, -0.03),
    ]
    for idx, (dx, dz, hs, bx, bz) in enumerate(culms):
        h = H * hs
        x0, z0 = dx * D, dz * D
        x1, z1 = (dx + bx) * D, (dz + bz) * D
        xm, zm = (x0 + x1) * 0.5, (z0 + z1) * 0.5
        r = D * 0.028
        branch_tube(o, regions["bark"],
                    [(x0, 0, z0), (xm, h * 0.5, zm), (x1, h, z1)],
                    [r, r * 0.90, r * 0.72], seg=6, bark_uv=True)

        for li, yf in enumerate((0.40, 0.58, 0.76, 0.93)):
            if hs < 0.80 and li == 0:
                continue
            ly = h * yf
            lx = x0 + (x1 - x0) * yf
            lz = z0 + (z1 - z0) * yf
            ang = math.radians((idx * 67 + li * 95) % 360)
            off = D * 0.12
            cx = lx + math.cos(ang) * off
            cz = lz + math.sin(ang) * off
            w_scale = 0.26 if yf > 0.65 else 0.22
            rk = "leaf" if (idx + li) % 2 == 0 else "leaf2"
            lowpoly_blob(o, regions[rk], (cx, ly, cz),
                         D * w_scale, H * 0.055, D * (w_scale * 0.82),
                         lon=6, lat=3, seed=110 + idx * 10 + li,
                         yaw=(idx * 55 + li * 40), shade_bias=0.08 * (yf - 0.5),
                         flat_bottom=0.55)


def build_cocoa(o, g, regions):
    """可可树（treecocoaa）：纯深棕 Y 形分叉主干 + 宽展分瓣绿冠 + 树干与分枝下方悬挂的紫红可可果荚。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (-0.01, H * 0.22, 0.0), (0.0, H * 0.44, 0.0)],
                [D * 0.105, D * 0.085, D * 0.074], seg=8)
    branches = [
        [(0, H * 0.39, 0), (-D * 0.18, H * 0.51, 0.02), (-D * 0.34, H * 0.62, 0.04)],
        [(0, H * 0.39, 0), (D * 0.18, H * 0.50, -0.02), (D * 0.34, H * 0.61, -0.03)],
        [(0, H * 0.41, 0), (0.02, H * 0.55, -D * 0.18)],
    ]
    for bpts in branches:
        branch_tube(o, regions["bark"], bpts,
                    [D * 0.062, D * 0.048, D * 0.036][:len(bpts)], seg=6, cap_start=False)

    # 左右宽展 V 形双翼冠 + 中央饱满冠（对齐原版 treecocoaa 上宽下收的宽阔冠型）
    blobs = [
        (-0.22, 0.46, 0.05, 0.25, 0.14, 0.24, "leaf2", 121, -0.06),
        (0.22, 0.46, -0.04, 0.25, 0.14, 0.24, "leaf2", 122, -0.05),
        (0.00, 0.46, 0.16, 0.25, 0.14, 0.24, "leaf2", 123, -0.06),
        (0.00, 0.47, -0.18, 0.25, 0.14, 0.24, "leaf2", 124, -0.06),
        (-0.32, 0.56, -0.02, 0.31, 0.18, 0.28, "leaf", 125, 0.05),
        (0.32, 0.57, 0.03, 0.31, 0.18, 0.28, "leaf", 126, 0.06),
        (-0.14, 0.62, 0.04, 0.27, 0.17, 0.26, "leaf", 128, 0.10),
        (0.14, 0.62, -0.04, 0.27, 0.17, 0.26, "leaf", 129, 0.10),
        (0.00, 0.58, 0.00, 0.31, 0.18, 0.29, "leaf", 127, 0.08),
    ]
    for dx, yf, dz, rxs, rys, rzs, rk, sd, sb in blobs:
        lowpoly_blob(o, regions[rk], (dx * D, H * yf, dz * D),
                     D * rxs, H * rys, D * rzs, lon=8, lat=4, seed=sd, shade_bias=sb)

    pods = [
        (0.068 * D, H * 0.20, 0.045 * D),
        (-0.065 * D, H * 0.29, 0.048 * D),
        (0.060 * D, H * 0.36, -0.042 * D),
        (-0.15 * D, H * 0.41, 0.035 * D),
        (0.15 * D, H * 0.40, -0.025 * D),
        (-0.27 * D, H * 0.46, 0.05 * D),
        (0.27 * D, H * 0.45, -0.03 * D),
        (0.02 * D, H * 0.43, 0.14 * D),
    ]
    for idx, (px, py, pz) in enumerate(pods):
        lowpoly_blob(o, regions["accent"], (px, py, pz),
                     0.056, 0.098, 0.056, lon=6, lat=3, seed=130 + idx,
                     shade_bias=0.08, flat_bottom=0.0)


def build_cecropia(o, g, regions):
    """号角树（treececropiaa）：修长浅棕主干直达冠顶 + 三向分叉顶枝 + 宽厚饱满的掌状下垂裂叶团。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (0.01, H * 0.26, 0.0), (-0.01, H * 0.52, 0.0), (0.0, H * 0.78, 0.0)],
                [D * 0.092, D * 0.074, D * 0.060, D * 0.046], seg=8)
    hubs = [
        (-D * 0.25, H * 0.76, D * 0.07),
        (D * 0.25, H * 0.75, D * 0.07),
        (0.0, H * 0.79, -D * 0.20),
    ]
    for hi, (hx, hy, hz) in enumerate(hubs):
        branch_tube(o, regions["bark"],
                    [(0, H * 0.58, 0), (hx * 0.55, (H * 0.58 + hy) * 0.52, hz * 0.55), (hx, hy, hz)],
                    [D * 0.054, D * 0.042, D * 0.032], seg=6, cap_start=False)
        lowpoly_blob(o, regions["leaf"], (hx, hy - H * 0.04, hz),
                     D * 0.24, H * 0.11, D * 0.24, lon=7, lat=4, seed=140 + hi,
                     shade_bias=0.10, flat_bottom=0.35)
        for k in range(6):
            yaw = k * 60.0 + (18.0 if hx < 0 else 48.0)
            a_rad = math.radians(yaw)
            lx = hx + math.sin(a_rad) * D * 0.16
            lz = hz + math.cos(a_rad) * D * 0.16
            ly = hy - H * 0.22
            rk = "leaf" if k % 2 == 0 else "leaf2"
            lowpoly_blob(o, regions[rk], (lx, ly, lz),
                         D * 0.12, H * 0.17, D * 0.16, lon=6, lat=4,
                         seed=145 + hi * 10 + k, yaw=yaw, pitch=-36.0,
                         shade_bias=0.04 if k % 2 == 0 else -0.05, flat_bottom=0.15)


def build_anima(o, g, regions):
    """灵树（treeanimaa）：苍劲深棕分叉干（4大主枝）+ 节点温润粉白灵珠 + 三维宽扇形簇拥的青白灵叶群。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0, 0, 0), (-0.02, H * 0.14, 0.01), (0.0, H * 0.28, 0.0)],
                [D * 0.165, D * 0.135, D * 0.115], seg=8)

    # 4 大分枝节点在 X 与 Z 上大幅张开（主视宽展对齐 _anima_zoom.png，俯视亦饱满立体）
    nodes = [
        # 0: 上中主冠簇
        ([(0, H * 0.24, 0), (-D * 0.08, H * 0.38, -D * 0.08), (-D * 0.08, H * 0.50, -D * 0.10)],
         (0.0, 0.02, 0.11), 0.0, H * 0.46, D * 0.62),
        # 1: 右上宽侧簇
        ([(0, H * 0.23, 0), (D * 0.24, H * 0.33, D * 0.06), (D * 0.42, H * 0.41, D * 0.08)],
         (0.0, 0.01, 0.10), -34.0, H * 0.42, D * 0.56),
        # 2: 左下宽侧簇
        ([(-D * 0.04, H * 0.21, 0), (-D * 0.26, H * 0.28, D * 0.08), (-D * 0.44, H * 0.34, D * 0.12)],
         (0.0, 0.01, 0.10), 36.0, H * 0.40, D * 0.58),
        # 3: 中下前侧簇
        ([(0, H * 0.18, 0.02), (D * 0.06, H * 0.24, D * 0.26)],
         (0.0, 0.01, 0.09), -10.0, H * 0.36, D * 0.48),
    ]
    for ni, (bpts, poff, fan_roll, blen, bwidth) in enumerate(nodes):
        branch_tube(o, regions["bark"], bpts,
                    [D * 0.095, D * 0.076, D * 0.062][:len(bpts)], seg=7, cap_start=False)
        nx, ny, nz = bpts[-1]
        lowpoly_blob(o, regions["accent"],
                     (nx + poff[0], ny - 0.05 + poff[1], nz + poff[2]),
                     0.115, 0.115, 0.115, lon=7, lat=4, seed=150 + ni,
                     shade_bias=0.10, flat_bottom=0.0, jitter=0.02)

        # 三维莲座扇形灵叶簇：前排+后排沿扇面与深度双向舒展，外侧大角度斜伸再向上弯曲
        n_fan = 6
        for row_i, z_base in enumerate((0.06, -0.14)):
            cnt = n_fan if row_i == 0 else (n_fan - 1)
            for k in range(cnt):
                u_t = (k / max(1, cnt - 1)) * 2.0 - 1.0
                roll_rad = math.radians(fan_roll * 0.55)
                s_len = blen * (1.0 - 0.14 * abs(u_t)) * (1.0 if row_i == 0 else 0.92)
                sx = (bwidth * u_t * 0.95) + math.sin(roll_rad) * s_len * 0.42
                sz = z_base * D + (1.0 - abs(u_t)) * (0.20 if row_i == 0 else -0.22) * D
                p0 = (nx + sx * 0.10, ny, nz + sz * 0.25)
                p1 = (nx + sx * 0.70, ny + s_len * 0.42, nz + sz * 0.78)
                p2 = (nx + sx * 0.95, ny + s_len * 0.85, nz + sz * 0.95)
                p3 = (nx + sx * 0.88, ny + s_len * 1.00, nz + sz * 0.88)
                r_blade = D * 0.052
                rk = "leaf" if (k + ni + row_i) % 2 == 0 else "leaf2"
                branch_tube(o, regions[rk], [p0, p1, p2, p3],
                            [r_blade * 0.75, r_blade, r_blade * 0.82, r_blade * 0.25],
                            seg=5, cap_start=False, bark_uv=False,
                            shade_bias=0.08 if (k + row_i) % 2 == 0 else -0.03)


def build_bush(o, g, regions):
    """灌木（busha）：中央饱满低模主叶团 + 外围 6 团高低错落的侧叶团，浑圆紧实贴地。"""
    H, D = g["H"], g["D"]
    r = D * 0.5
    lobes = [
        (0.0, 0.0, 0.0, 0.62, 0.52, 0.62, "leaf", 160, 0.08),
        (-0.34, 0.0, 0.18, 0.46, 0.40, 0.44, "leaf2", 161, -0.04),
        (0.34, 0.0, -0.16, 0.46, 0.41, 0.44, "leaf", 162, 0.04),
        (0.14, 0.0, 0.34, 0.44, 0.38, 0.42, "leaf2", 163, -0.02),
        (-0.16, 0.0, -0.34, 0.44, 0.38, 0.42, "leaf2", 164, -0.05),
        (0.30, 0.0, 0.24, 0.38, 0.34, 0.38, "leaf", 165, 0.02),
        (-0.30, 0.0, -0.22, 0.38, 0.34, 0.38, "leaf2", 166, -0.06),
    ]
    for dx, dy, dz, sx, sy, sz, rk, sd, sb in lobes:
        lowpoly_blob(o, regions[rk], (dx * r, dy * H, dz * r),
                     r * sx, H * sy, r * sz, lon=7, lat=4, seed=sd,
                     shade_bias=sb, flat_bottom=0.45)


def build_berry(o, g, regions):
    """浆果丛（berrybusha）：紧凑翠绿低模灌木丛 + 外表面饱满露出的 16 颗纯天蓝色浆果。"""
    build_bush(o, g, regions)
    H, D = g["H"], g["D"]
    # 沿灌木外表面球壳布置，确保浆果大半露出叶面，不陷进叶团内部
    berries = [
        (0.00, 0.90, 0.08), (-0.12, 0.86, -0.06), (0.14, 0.84, 0.05),
        (-0.22, 0.72, 0.18), (0.24, 0.70, -0.14), (0.08, 0.74, -0.26),
        (-0.14, 0.68, -0.26), (0.28, 0.58, 0.18), (-0.30, 0.56, -0.04),
        (0.06, 0.60, 0.34), (-0.18, 0.50, 0.30), (0.22, 0.48, -0.28),
        (0.34, 0.44, -0.04), (-0.28, 0.42, -0.22), (0.14, 0.54, 0.24),
        (-0.08, 0.62, 0.28),
    ]
    for idx, (dx, yf, dz) in enumerate(berries):
        lowpoly_blob(o, regions["accent"], (dx * D, yf * H, dz * D),
                     0.042, 0.042, 0.042, lon=6, lat=3, seed=170 + idx,
                     shade_bias=0.12, flat_bottom=0.0, jitter=0.02)


def build_brambles(o, g, regions):
    """荆棘丛（bramblesa）：还原原版下部盘根错节的棕色木质藤枝 + 上部枝头一朵朵扁平伞状黄绿叶盖。"""
    H, D = g["H"], g["D"]
    stems = [
        ((-0.26, 0.18), (-0.20, 0.28, 0.12), (-0.22, 0.56, 0.14), 0.19, "leaf"),
        ((0.24, 0.20), (0.18, 0.26, 0.14), (0.20, 0.52, 0.16), 0.18, "leaf2"),
        ((-0.04, 0.28), (-0.02, 0.22, 0.20), (0.00, 0.46, 0.22), 0.17, "leaf"),
        ((0.02, -0.04), (0.00, 0.38, -0.02), (-0.02, 0.74, 0.00), 0.22, "leaf"),
        ((-0.28, -0.14), (-0.22, 0.32, -0.10), (-0.24, 0.64, -0.12), 0.19, "leaf2"),
        ((0.26, -0.16), (0.20, 0.30, -0.12), (0.22, 0.60, -0.14), 0.19, "leaf"),
        ((0.06, -0.28), (0.04, 0.26, -0.20), (0.05, 0.52, -0.22), 0.17, "leaf2"),
    ]
    for idx, ((bx, bz), (mx, my, mz), (tx, ty, tz), cr, rk) in enumerate(stems):
        branch_tube(o, regions["bark"],
                    [(bx * D, 0.0, bz * D), (mx * D, my * H, mz * D), (tx * D, ty * H, tz * D)],
                    [D * 0.036, D * 0.030, D * 0.022], seg=5)
        lowpoly_blob(o, regions[rk], (tx * D, (ty - 0.06) * H, tz * D),
                     D * cr, H * 0.16, D * cr, lon=7, lat=3, seed=180 + idx,
                     shade_bias=0.06 if rk == "leaf" else -0.02, flat_bottom=0.55)

    for k, (x0, z0, x1, z1) in enumerate([
        (-0.08, 0.08, -0.36, 0.26),
        (0.08, 0.06, 0.35, 0.24),
        (-0.06, -0.08, -0.34, -0.26),
        (0.06, -0.08, 0.34, -0.25),
    ]):
        branch_tube(o, regions["bark"],
                    [(x0 * D, H * 0.18, z0 * D), ((x0 + x1) * 0.5 * D, H * 0.10, (z0 + z1) * 0.5 * D),
                     (x1 * D, 0.01, z1 * D)],
                    [D * 0.030, D * 0.026, D * 0.018], seg=5)


def build_agave(o, g, regions):
    """龙舌兰（agavea）：三层莲座状放射生长的宽厚肉质剑叶（带中脊棱角与叶尖微翘）。"""
    H, D = g["H"], g["D"]
    lowpoly_blob(o, regions["leaf2"], (0, 0, 0),
                 D * 0.16, H * 0.12, D * 0.16, lon=7, lat=3, seed=190, shade_bias=-0.10)
    rings = [
        (7, 12.0, D * 0.46, D * 0.16, 0.065, H * 0.36, -H * 0.06, -0.03),
        (6, 38.0, D * 0.36, D * 0.15, 0.065, H * 0.54, -H * 0.18, 0.04),
        (4, 5.0, D * 0.22, D * 0.13, 0.060, H * 0.64, -H * 0.28, 0.10),
    ]
    for n_b, a0, ln, wd, thk, ah, dh, sb in rings:
        for k in range(n_b):
            yaw = k * (360.0 / n_b) + a0
            rk = "leaf" if k % 2 == 0 else "leaf2"
            arched_frond(o, regions[rk], (0.0, H * 0.03, 0.0),
                         yaw_deg=yaw, length=ln, width=wd, thickness=thk,
                         arch_h=ah, drop_h=dh, segs=4, shade_bias=sb)


def _alocasia_broad_leaf(o, reg_leaf, reg_stem, yaw_deg, length, width, arch_y, tip_y, shade_bias=0.0):
    """海芋单枚大阔叶：短粗斜茎 + 从基部向外大幅舒展的箭头形/心形厚叶板（带后耳裂与叶脉折角）。"""
    p_base = (0.0, 0.0, 0.0)
    p_hub = rot_yaw_pitch((0.0, arch_y * 0.42, length * 0.16), yaw_deg, 0.0)
    branch_tube(o, reg_stem, [p_base, p_hub], [0.032, 0.024], seg=5)

    L = length * 0.86
    W = width * 0.54
    tk = 0.036

    def lp(lx, ly, lz):
        return rot_yaw_pitch((p_hub[0] + lx, p_hub[1] + ly, p_hub[2] + lz), yaw_deg, 0.0, p_hub)

    h0 = p_hub[1]
    c0 = lp(0.0, 0.02, -L * 0.10)
    c1 = lp(0.0, arch_y * 0.85 - h0, L * 0.24)
    c2 = lp(0.0, arch_y * 0.96 - h0, L * 0.62)
    c3 = lp(0.0, tip_y - h0, L * 1.00)

    r0 = lp(W * 0.72, 0.08, -L * 0.18)
    r1 = lp(W * 1.05, arch_y * 0.88 - h0 + 0.05, L * 0.26)
    r2 = lp(W * 0.76, arch_y * 0.90 - h0 - 0.02, L * 0.66)

    l0 = lp(-W * 0.72, 0.08, -L * 0.18)
    l1 = lp(-W * 1.05, arch_y * 0.88 - h0 + 0.05, L * 0.26)
    l2 = lp(-W * 0.76, arch_y * 0.90 - h0 - 0.02, L * 0.66)

    dn = (0.0, -tk, 0.0)
    c0b, c1b, c2b, c3b = _add(c0, dn), _add(c1, dn), _add(c2, dn), _add(c3, dn)
    r0b, r1b, r2b = _add(r0, dn), _add(r1, dn), _add(r2, dn)
    l0b, l1b, l2b = _add(l0, dn), _add(l1, dn), _add(l2, dn)

    add_faceted_tri(o, c0, c1, r0, reg_leaf, shade_bias=shade_bias + 0.04, u_seed=0.35)
    add_faceted_tri(o, r0, c1, r1, reg_leaf, shade_bias=shade_bias + 0.06, u_seed=0.45)
    add_faceted_quad(o, c1, c2, r2, r1, reg_leaf, shade_bias=shade_bias + 0.05, u_seed=0.40)
    add_faceted_tri(o, c2, c3, r2, reg_leaf, shade_bias=shade_bias + 0.02, u_seed=0.50)

    add_faceted_tri(o, c0, l0, c1, reg_leaf, shade_bias=shade_bias + 0.10, u_seed=0.65)
    add_faceted_tri(o, l0, l1, c1, reg_leaf, shade_bias=shade_bias + 0.12, u_seed=0.55)
    add_faceted_quad(o, c1, l1, l2, c2, reg_leaf, shade_bias=shade_bias + 0.10, u_seed=0.60)
    add_faceted_tri(o, c2, l2, c3, reg_leaf, shade_bias=shade_bias + 0.07, u_seed=0.50)

    add_faceted_tri(o, c0b, r0b, c1b, reg_stem, shade_bias=-0.14)
    add_faceted_tri(o, r0b, r1b, c1b, reg_stem, shade_bias=-0.14)
    add_faceted_quad(o, c1b, r1b, r2b, c2b, reg_stem, shade_bias=-0.14)
    add_faceted_tri(o, c2b, r2b, c3b, reg_stem, shade_bias=-0.14)

    add_faceted_tri(o, c0b, c1b, l0b, reg_stem, shade_bias=-0.14)
    add_faceted_tri(o, l0b, c1b, l1b, reg_stem, shade_bias=-0.14)
    add_faceted_quad(o, c1b, c2b, l2b, l1b, reg_stem, shade_bias=-0.14)
    add_faceted_tri(o, c2b, c3b, l2b, reg_stem, shade_bias=-0.14)

    edges = [(c0, r0, r0b, c0b), (r0, r1, r1b, r0b), (r1, r2, r2b, r1b), (r2, c3, c3b, r2b),
             (c3, l2, l2b, c3b), (l2, l1, l1b, l2b), (l1, l0, l0b, l1b), (l0, c0, c0b, l0b)]
    for e0, e1, e2, e3 in edges:
        add_faceted_quad(o, e0, e1, e2, e3, reg_stem, shade_bias=-0.08)


def build_alocasia(o, g, regions):
    """海芋（alocasiaa）：5 片向四周大角度舒展铺开的箭头形巨型热带宽叶（对齐原版五叶放射剪影）。"""
    H, D = g["H"], g["D"]
    lowpoly_blob(o, regions["leaf2"], (0, 0, 0),
                 D * 0.09, H * 0.08, D * 0.09, lon=6, lat=3, seed=200, shade_bias=-0.10)
    leaves = [
        (38.0, 0.54, 0.42, 0.68, 0.36, 0.08),
        (112.0, 0.50, 0.40, 0.58, 0.28, 0.04),
        (172.0, 0.48, 0.38, 0.50, 0.20, 0.00),
        (242.0, 0.52, 0.40, 0.62, 0.30, 0.06),
        (312.0, 0.54, 0.42, 0.72, 0.40, 0.10),
    ]
    for yaw, ls, ws, ay, ty, sb in leaves:
        _alocasia_broad_leaf(o, regions["leaf"], regions["leaf2"],
                             yaw_deg=yaw, length=D * ls, width=D * ws,
                             arch_y=H * ay, tip_y=H * ty, shade_bias=sb)


def build_shrublow(o, g, regions):
    """矮灌木（shrublowa）：还原原版向四角放射伸展的星形/十字形分瓣贴地绿叶丛。"""
    H, D = g["H"], g["D"]
    lowpoly_blob(o, regions["leaf"], (0, 0, 0),
                 D * 0.22, H * 0.42, D * 0.22, lon=7, lat=3, seed=210,
                 shade_bias=0.06, flat_bottom=0.50)
    lobes = [
        (42.0, D * 0.48, D * 0.24, H * 0.44, H * 0.32, "leaf", 0.06),
        (132.0, D * 0.46, D * 0.23, H * 0.40, H * 0.30, "leaf2", 0.00),
        (225.0, D * 0.48, D * 0.24, H * 0.42, H * 0.32, "leaf", 0.04),
        (310.0, D * 0.45, D * 0.22, H * 0.38, H * 0.28, "leaf2", -0.02),
        (88.0, D * 0.32, D * 0.18, H * 0.48, H * 0.22, "leaf", 0.08),
    ]
    for yaw, ln, wd, ah, dh, rk, sb in lobes:
        arched_frond(o, regions[rk], (0, H * 0.04, 0),
                     yaw_deg=yaw, length=ln, width=wd, thickness=H * 0.26,
                     arch_h=ah, drop_h=dh, segs=4, shade_bias=sb)


def build_pincushion(o, g, regions):
    """球形仙人掌（pincushioncactus）：带 10 道竖向凹凸棱槽的圆鼓形低模仙人掌球，顶部微凹收口。"""
    H, D = g["H"], g["D"]
    rx, rz = D * 0.5, D * 0.5
    ribs = 10
    n2 = ribs * 2
    lat = 5
    rings = []
    for y in range(1, lat):
        t = y / lat
        phi = (1.0 - t) * math.pi * 0.88 + 0.06 * math.pi
        ring_r = math.sin(phi)
        py = (math.cos(phi) * 0.5 + 0.5) * H * 0.96
        row = []
        for i in range(n2):
            a = i / n2 * math.pi * 2.0
            rib_k = 1.03 if (i % 2 == 0) else 0.89
            row.append((math.cos(a) * rx * ring_r * rib_k, py, math.sin(a) * rz * ring_r * rib_k))
        rings.append(row)

    bot_pt = (0.0, 0.0, 0.0)
    top_pt = (0.0, H * 0.92, 0.0)
    for i in range(n2):
        j = (i + 1) % n2
        add_faceted_tri(o, bot_pt, rings[0][i], rings[0][j], regions["leaf2"], shade_bias=-0.16)
    for y in range(len(rings) - 1):
        r0, r1 = rings[y], rings[y + 1]
        sb = -0.08 + 0.20 * (y / (len(rings) - 1))
        for i in range(n2):
            j = (i + 1) % n2
            rk = "leaf" if i % 2 == 0 else "leaf2"
            add_faceted_quad(o, r0[i], r1[i], r1[j], r0[j], regions[rk],
                             shade_bias=sb + (0.05 if i % 2 == 0 else -0.05),
                             u_seed=(i + 0.5) / n2)
    top_ring = rings[-1]
    for i in range(n2):
        j = (i + 1) % n2
        add_faceted_tri(o, top_ring[i], top_pt, top_ring[j], regions["leaf"], shade_bias=0.10)


def build_saguaro(o, g, regions):
    """柱状仙人掌（saguarocactus）：带棱粗壮主柱 + 圆润过渡肘部的左右高举双臂，顶端圆拱封顶。"""
    H, D = g["H"], g["D"]
    r_main = D * 0.20
    branch_tube(o, regions["leaf"],
                [(0, 0, 0), (0, H * 0.45, 0), (0, H * 0.88, 0), (0, H * 0.97, 0), (0, H, 0)],
                [r_main * 1.04, r_main, r_main * 0.94, r_main * 0.68, r_main * 0.15],
                seg=8, bark_uv=False, shade_bias=0.04)

    arms = [
        [(0.0, H * 0.45, 0.0),
         (-D * 0.25, H * 0.46, 0.0),
         (-D * 0.35, H * 0.54, 0.0),
         (-D * 0.36, H * 0.77, 0.0),
         (-D * 0.36, H * 0.83, 0.0)],
        [(0.0, H * 0.35, 0.0),
         (D * 0.24, H * 0.36, 0.0),
         (D * 0.34, H * 0.44, 0.0),
         (D * 0.35, H * 0.68, 0.0),
         (D * 0.35, H * 0.74, 0.0)],
    ]
    r_arm = r_main * 0.72
    for apts in arms:
        branch_tube(o, regions["leaf2"], apts,
                    [r_arm * 0.75, r_arm, r_arm * 0.96, r_arm * 0.90, r_arm * 0.20],
                    seg=8, cap_start=False, bark_uv=False, shade_bias=0.00)


def build_healroot(o, g, regions):
    """草药（healroota）：还原原版底部微弯青绿根茎 + 中部四周垂落的深青绿羽叶 + 顶部饱满发光的天青色花芯簇。"""
    H, D = g["H"], g["D"]
    branch_tube(o, regions["bark"],
                [(0.01 * D, 0, 0), (-0.01 * D, H * 0.24, 0.01 * D), (0.0, H * 0.54, 0.0)],
                [D * 0.062, D * 0.052, D * 0.042], seg=7, bark_uv=False, shade_bias=-0.06)
    for sx, sz in [(-0.09, 0.05), (0.09, -0.04)]:
        branch_tube(o, regions["bark"],
                    [(sx * D, 0, sz * D), (sx * 0.30 * D, H * 0.32, sz * 0.30 * D)],
                    [D * 0.035, D * 0.026], seg=5, cap_end=False, bark_uv=False, shade_bias=-0.10)

    for k in range(9):
        yaw = k * 40.0 + 14.0
        arched_frond(o, regions["leaf2"], (0.0, H * 0.50, 0.0),
                     yaw_deg=yaw, length=D * 0.45, width=D * 0.16, thickness=0.030,
                     arch_h=H * 0.20, drop_h=H * 0.34, segs=4, shade_bias=-0.02)

    # 顶部 5 颗发光天青色花苞簇（高低错落簇拥）
    for idx, (dx, dy, dz, rs) in enumerate([
        (0.0, 0.58, 0.0, 0.21),
        (-0.12, 0.54, 0.05, 0.16),
        (0.12, 0.54, -0.04, 0.16),
        (0.03, 0.55, 0.13, 0.15),
        (-0.04, 0.55, -0.13, 0.15),
    ]):
        lowpoly_blob(o, regions["leaf"], (dx * D, dy * H, dz * D),
                     D * rs, H * 0.20, D * rs, lon=7, lat=3, seed=230 + idx,
                     shade_bias=0.16, flat_bottom=0.20)


def _build_one_devilstrand(o, regions, cx, cz, h, d):
    """单朵魔鬼菇：上细下敦米白菌柄 + 菌盖下缘波浪纯白褶边裙 + 鲜红伞盖 + 贴合伞面的纯白低模圆斑。"""
    branch_tube(o, regions["bark"],
                [(cx, 0.0, cz), (cx, h * 0.16, cz), (cx, h * 0.44, cz), (cx, h * 0.62, cz)],
                [d * 0.14, d * 0.18, d * 0.15, d * 0.12], seg=8, bark_uv=False, shade_bias=0.06)

    n_frill = 14
    y_frill = h * 0.46
    r_in = d * 0.13
    top_c = (cx, h * 0.56, cz)
    frill_pts = []
    for i in range(n_frill):
        a = i / n_frill * math.pi * 2.0
        is_wave = (i % 2 == 0)
        rad = d * (0.38 if is_wave else 0.31)
        vy = y_frill - (h * 0.05 if is_wave else -h * 0.02)
        frill_pts.append((cx + math.cos(a) * rad, vy, cz + math.sin(a) * rad))
    for i in range(n_frill):
        j = (i + 1) % n_frill
        p_in0 = (cx + (frill_pts[i][0] - cx) * (r_in / (d * 0.35)), h * 0.42,
                 cz + (frill_pts[i][2] - cz) * (r_in / (d * 0.35)))
        p_in1 = (cx + (frill_pts[j][0] - cx) * (r_in / (d * 0.35)), h * 0.42,
                 cz + (frill_pts[j][2] - cz) * (r_in / (d * 0.35)))
        add_faceted_tri(o, frill_pts[i], top_c, frill_pts[j], regions["leaf2"], shade_bias=0.10)
        add_faceted_quad(o, p_in0, frill_pts[i], frill_pts[j], p_in1, regions["leaf2"], shade_bias=-0.04)

    # 饱满圆拱的鲜红大菌盖（0.48h ~ 1.0h），直接构建带共面纯白斑点的低模伞盖网格（零飞碟凸起）
    r_cap = d * 0.48
    ry_cap = h * 0.26
    cy = h * 0.48
    lon, lat = 9, 4
    rings = []
    for y in range(1, lat):
        t = y / lat
        phi = (1.0 - t) * math.pi
        ny = math.cos(phi)
        ny = ny * 0.35 if ny < 0.0 else math.pow(ny, 0.88)
        ring_r = math.sin(phi) * (1.06 if t > 0.55 else 1.0)
        twist = (y % 2) * (math.pi / lon) * 0.55
        row = []
        for x in range(lon):
            th = x / lon * math.pi * 2.0 + twist
            row.append((cx + math.cos(th) * ring_r * r_cap,
                        cy + (ny + 1.0) * ry_cap,
                        cz + math.sin(th) * ring_r * r_cap))
        rings.append(row)
    bot_pt = (cx, cy + ry_cap * 0.16, cz)
    top_pt = (cx, cy + ry_cap * 2.0, cz)
    for x in range(lon):
        nx_ = (x + 1) % lon
        add_faceted_tri(o, bot_pt, rings[0][x], rings[0][nx_], regions["leaf"], shade_bias=-0.12)

    def _emit_cap_tri(p0, p1, p2, sb, with_spot=False):
        if not with_spot:
            add_faceted_tri(o, p0, p1, p2, regions["leaf"], shade_bias=sb)
            return
        # 把切面直接无缝剖分为中央共面纯白斑点三角形 + 外圈三块红底梯形（零凸起、零深度冲突、同绕序）
        mc = ((p0[0] + p1[0] + p2[0]) / 3.0,
              (p0[1] + p1[1] + p2[1]) / 3.0,
              (p0[2] + p1[2] + p2[2]) / 3.0)
        s = 0.52
        q0 = _add(mc, _mul(_sub(p0, mc), s))
        q1 = _add(mc, _mul(_sub(p1, mc), s))
        q2 = _add(mc, _mul(_sub(p2, mc), s))
        add_faceted_tri(o, q0, q1, q2, regions["leaf2"], shade_bias=0.14)
        add_faceted_quad(o, p0, p1, q1, q0, regions["leaf"], shade_bias=sb)
        add_faceted_quad(o, p1, p2, q2, q1, regions["leaf"], shade_bias=sb)
        add_faceted_quad(o, p2, p0, q0, q2, regions["leaf"], shade_bias=sb)

    for y in range(len(rings) - 1):
        r0, r1 = rings[y], rings[y + 1]
        sb = -0.04 + 0.18 * (y / max(1, len(rings) - 1))
        for x in range(lon):
            nx_ = (x + 1) % lon
            spot_a = (y == 1 and x in (0, 3, 6))
            spot_b = (y == 1 and x in (1, 4, 7))
            _emit_cap_tri(r0[x], r1[x], r0[nx_], sb, with_spot=spot_a)
            _emit_cap_tri(r0[nx_], r1[x], r1[nx_], sb + 0.03, with_spot=spot_b)

    top_ring = rings[-1]
    for x in range(lon):
        nx_ = (x + 1) % lon
        _emit_cap_tri(top_ring[x], top_pt, top_ring[nx_], 0.16, with_spot=(x in (1, 5, 8)))


def build_devilstrand(o, g, regions):
    """魔鬼菇（devilstranda）：主菇（米白柄 + 纯白波浪菌裙 + 鲜红伞盖 + 贴合纯白圆斑）+ 伴生小菇。"""
    H, D = g["H"], g["D"]
    _build_one_devilstrand(o, regions, -D * 0.04, 0.0, H, D * 0.92)
    _build_one_devilstrand(o, regions, D * 0.24, D * 0.16, H * 0.56, D * 0.52)


def build_devilstrand(o, g, regions):
    """魔鬼菇（devilstranda）：主菇（米白柄 + 纯白波浪菌裙 + 红底白斑圆拱伞盖）+ 侧旁伴生小菇。"""
    H, D = g["H"], g["D"]
    _build_one_devilstrand(o, regions, -D * 0.04, 0.0, H, D * 0.92)
    _build_one_devilstrand(o, regions, D * 0.24, D * 0.16, H * 0.56, D * 0.52)


BUILDERS = {
    "oak": build_oak, "maple": build_maple, "poplar": build_poplar,
    "pine": build_pine, "cypress": build_cypress, "birch": build_birch,
    "palm": build_palm, "drago": build_drago, "teak": build_teak,
    "willow": build_willow, "bamboo": build_bamboo, "cocoa": build_cocoa,
    "cecropia": build_cecropia, "anima": build_anima, "bush": build_bush,
    "berry": build_berry, "brambles": build_brambles, "agave": build_agave,
    "alocasia": build_alocasia, "shrublow": build_shrublow,
    "pincushion": build_pincushion, "saguaro": build_saguaro,
    "healroot": build_healroot, "devilstrand": build_devilstrand,
}


def build_geo(name, g, regions):
    o = MeshOut()
    BUILDERS[g["build"]](o, g, regions)
    return o


# ---------------------------------------------------------------------------
# 取样：相对系数 -> 像素矩形 -> 按原版材质语义净化并对齐纵向受光梯度
# ---------------------------------------------------------------------------

def content_bbox(im):
    a = np.asarray(im)[:, :, 3]
    ys, xs = np.where(a > 16)
    if len(xs) == 0:
        return (0, 0, im.width, im.height)
    return (int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1)


def purify_mask(name, key, rgb, a):
    """按原版贴图的部件语义剔除串色杂质像素（如枫树白干上的红叶、可可棕干上的红果、浆果叶上的蓝点），
    被剔除的位置交给 solidify 用纯净邻域像素实心填充。"""
    valid = a >= 200
    r = rgb[:, :, 0].astype(np.int32)
    g = rgb[:, :, 1].astype(np.int32)
    b = rgb[:, :, 2].astype(np.int32)
    luma = 0.299 * r + 0.587 * g + 0.114 * b

    if not (name == "birch" and key == "bark"):
        valid = valid & (luma >= 38)

    if name == "maple" and key == "bark":
        valid = valid & ((r - b) < 28) & (luma >= 80)
    elif name == "maple" and key in ("leaf", "leaf2"):
        # 枫叶只取纯正火红/橙红像素，剔除混入的银灰白树干与深黑底
        valid = valid & (r >= 105) & (g <= 85) & (b <= 75) & ((r - g) >= 25)
    elif name == "cocoa" and key == "bark":
        valid = valid & (r > g) & (g >= 38) & ((r - g) < 46)
    elif name == "cocoa" and key == "accent":
        valid = valid & (r >= 85) & (g <= 50)
    elif name == "berry" and key == "accent":
        valid = valid & (b > g + 8)
    elif name == "berry" and key in ("leaf", "leaf2"):
        valid = valid & (g >= b)
    elif name == "anima" and key == "bark":
        valid = valid & (r >= g) & (g >= b) & (luma <= 115) & (luma >= 42)
    elif name == "anima" and key in ("leaf", "leaf2"):
        valid = valid & (g >= r) & (b >= r - 12) & (luma >= 95)
    elif name == "anima" and key == "accent":
        valid = valid & (r >= 160) & (g >= 145) & (b >= 150)
    elif name == "devilstrand" and key == "bark":
        valid = valid & (luma >= 115) & ((r - g) < 45)
    elif name == "devilstrand" and key == "leaf":
        # 魔鬼菇伞盖底色只取纯正深红像素（剔除白斑及粉白过渡边，白斑由几何低模斑点呈现）
        valid = valid & (r >= 90) & (g <= 68) & (b <= 62) & ((r - g) >= 35)
    elif name == "devilstrand" and key == "leaf2":
        valid = valid & (luma >= 165)
    elif name == "healroot" and key == "leaf":
        # 草药顶部花芯取亮天青蓝色
        valid = valid & (b >= 78) & (g >= 68) & (b >= r + 20)

    out_a = a.copy()
    if valid.any():
        out_a[~valid] = 0
    return out_a


def solidify(patch_rgb, patch_a, name="", key=""):
    """块内半透明/透明及异色杂质像素用邻域纯净实心像素均值填充，
    并叠加与原版一致的纵向明暗层次（上亮下暗），使低模切面在无光照下呈现鲜明立体感。"""
    clean_a = purify_mask(name, key, patch_rgb, patch_a)
    h, w = clean_a.shape
    out = patch_rgb.copy()
    outa = clean_a.copy()
    for _ in range(10):
        bad = outa < 200
        if not bad.any():
            break
        n = 0
        ys, xs = np.where(bad)
        for y, x in zip(ys, xs):
            acc = np.zeros(3, dtype=np.float64)
            ca = 0.0
            cnt = 0
            for dy in range(-3, 4):
                for dx in range(-3, 4):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < h and 0 <= xx < w and not bad[yy, xx]:
                        wgt = outa[yy, xx] / 255.0
                        acc += out[yy, xx].astype(np.float64) * wgt
                        ca += wgt
                        cnt += 1
            if cnt >= 2 and ca > 0.3:
                out[y, x] = np.clip(acc / ca, 0, 255).astype(np.uint8)
                outa[y, x] = 255
                n += 1
        if n == 0:
            break
    bad = outa < 200
    if bad.any():
        if (outa >= 200).any():
            mean = out[outa >= 200].mean(axis=0)
        elif (patch_a >= 200).any():
            mean = patch_rgb[patch_a >= 200].mean(axis=0)
        else:
            mean = np.array([90.0, 120.0, 70.0])
        out[bad] = mean.astype(np.uint8)
        outa[bad] = 255

    if not (name == "birch" and key == "bark"):
        arr_f = out.astype(np.float32)
        mean_col = arr_f.mean(axis=(0, 1), keepdims=True)
        detail = (arr_f - mean_col) * 0.65
        t_y = np.linspace(1.15, 0.76, h, dtype=np.float32).reshape(h, 1, 1)
        shaded = np.clip(mean_col * t_y + detail, 0, 255).astype(np.uint8)
        out = shaded

    return out, outa


def derive_rects(g, im):
    """相对系数 -> 像素矩形（内容包围盒坐标）。"""
    bb = content_bbox(im)
    bx0, by0, bx1, by1 = bb
    w, h = bx1 - bx0, by1 - by0
    out = {}
    for key in ("bark", "leaf", "leaf2", "accent"):
        cf = g.get(key)
        if not cf:
            continue
        cx0, cy0, cx1, cy1 = cf
        x0 = max(bx0, min(bx1 - 4, int(bx0 + cx0 * w)))
        y0 = max(by0, min(by1 - 4, int(by0 + cy0 * h)))
        x1 = max(x0 + 4, min(bx1, int(bx0 + cx1 * w)))
        y1 = max(y0 + 4, min(by1, int(by0 + cy1 * h)))
        out[key] = (x0, y0, x1, y1)
    return out


# ---------------------------------------------------------------------------
# 图集打包与尺寸归一化
# ---------------------------------------------------------------------------

def pack_atlas(patches):
    """patches: name -> (w,h)。返回布局 name -> (x,y)（含 PAD 间距）与图集高。"""
    order = sorted(patches.items(), key=lambda kv: -kv[1][1])
    x, y, row_h = PAD, PAD, 0
    pos = {}
    for name, (w, h) in order:
        if x + w + PAD > ATLAS_W:
            x = PAD
            y += row_h + PAD
            row_h = 0
        pos[name] = (x, y)
        x += w + PAD
        row_h = max(row_h, h)
    return pos, y + row_h + PAD


def normalize(o, g):
    """落地 + 归一化到约定世界尺寸：y 平移到 min=0，y 缩放到总高 H，xz 缩放到冠幅 D。"""
    vs = np.array(o.v)
    vs[:, 1] -= vs[:, 1].min()
    H, D = g["H"], g["D"]
    max_y = vs[:, 1].max()
    ky = H / max_y if max_y > 1e-6 else 1.0
    max_xz = max(np.abs(vs[:, 0]).max(), np.abs(vs[:, 2]).max()) * 2.0
    kx = D / max_xz if max_xz > 1e-6 else 1.0
    vs[:, 1] *= ky
    vs[:, 0] *= kx
    vs[:, 2] *= kx
    for i in range(len(o.v)):
        o.v[i] = (float(vs[i, 0]), float(vs[i, 1]), float(vs[i, 2]))


def main():
    os.makedirs(os.path.dirname(ATLAS_OUT), exist_ok=True)
    os.makedirs(OBJ_DIR, exist_ok=True)
    os.makedirs(VIEW_OBJ_DIR, exist_ok=True)
    os.makedirs(os.path.dirname(SAMPLE_DOC), exist_ok=True)

    imgs = {}
    rects_all = {}
    errors = []

    # ---- 1. 取样矩形 ----
    for name, g in SPEC.items():
        path = os.path.join(VAN, g["tex"] + ".png")
        if not os.path.exists(path):
            errors.append(f"{name}: 缺源贴图 {path}")
            continue
        im = Image.open(path).convert("RGBA")
        imgs[name] = im
        rects_all[name] = derive_rects(g, im)

    if errors:
        for e in errors:
            print("ERR", e)
        sys.exit(1)

    # ---- 2. 取样块：净化串色 + 实心填充 ----
    patches = {}
    fills = {}
    for name, g in SPEC.items():
        im = imgs[name]
        for key, rect in rects_all[name].items():
            x0, y0, x1, y1 = rect
            sub = np.asarray(im)[y0:y1, x0:x1]
            rgb = sub[:, :, :3].copy()
            a = sub[:, :, 3].copy()
            rgb2, a2 = solidify(rgb, a, name=name, key=key)
            fills[f"{name}.{key}"] = int((a < 200).sum())
            patches[f"{name}.{key}"] = dict(rgb=rgb2, a=a2, src=name, key=key, rect=rect)

    # ---- 3. 打包图集 ----
    layout, atlas_h = pack_atlas({k: (p["rgb"].shape[1], p["rgb"].shape[0]) for k, p in patches.items()})
    atlas = np.zeros((atlas_h, ATLAS_W, 4), dtype=np.uint8)
    regions = {}
    for k, p in patches.items():
        x, y = layout[k]
        h, w = p["rgb"].shape[:2]
        atlas[y:y + h, x:x + w, :3] = p["rgb"]
        atlas[y:y + h, x:x + w, 3] = p["a"]
        if y > 0:
            atlas[y - PAD:y, x:x + w] = atlas[y:y + 1, x:x + w]
        if y + h < atlas_h:
            atlas[y + h:y + h + PAD, x:x + w] = atlas[y + h - 1:y + h, x:x + w]
        if x > 0:
            atlas[y:y + h, x - PAD:x] = atlas[y:y + h, x:x + 1]
        if x + w < ATLAS_W:
            atlas[y:y + h, x + w:x + w + PAD] = atlas[y:y + h, x + w - 1:x + w]
        ah = atlas_h
        regions[k] = [x / ATLAS_W, 1 - (y + h) / ah, (x + w) / ATLAS_W, 1 - y / ah]

    for k, p in patches.items():
        x, y = layout[k]
        h, w = p["rgb"].shape[:2]
        for ey in (y - PAD, y + h):
            for ex in (x - PAD, x + w):
                yy = min(max(ey, 0), atlas_h - 1)
                xx = min(max(ex, 0), ATLAS_W - 1)
                sy = 0 if ey < y else h - 1
                sx = 0 if ex < x else w - 1
                atlas[yy, xx] = atlas[y + sy, x + sx]

    Image.fromarray(atlas).save(ATLAS_OUT)
    Image.fromarray(atlas).save(VIEW_ATLAS)

    # ---- 4. 几何 + OBJ ----
    arch_meta = []
    guard_fail = []
    for name, g in SPEC.items():
        reg = {}
        for key in ("bark", "leaf", "leaf2", "accent"):
            if f"{name}.{key}" in regions:
                reg[key] = regions[f"{name}.{key}"]
        if "leaf" not in reg and "leaf2" in reg:
            reg["leaf"] = reg["leaf2"]
        if "leaf2" not in reg and "leaf" in reg:
            reg["leaf2"] = reg["leaf"]
        for must in ("leaf", "leaf2"):
            if must not in reg:
                guard_fail.append(f"{name}: 缺 {must} 取样区")
                continue
        o = build_geo(name, g, reg)
        normalize(o, g)
        # ---- 守卫 ----
        if not o.f:
            guard_fail.append(f"{name}: 零面")
            continue
        vs = np.array(o.v)
        if abs(vs[:, 1].min()) > 0.02:
            guard_fail.append(f"{name}: 不着地 min_y={vs[:,1].min():.3f}")
        if vs[:, 1].max() > g["H"] + 0.02:
            guard_fail.append(f"{name}: 超高 {vs[:,1].max():.3f} > {g['H']}")
        if len(o.f) > 2400:
            guard_fail.append(f"{name}: 面数过多 {len(o.f)}")
        for u, v in o.uv:
            if not (-0.001 <= u <= 1.001 and -0.001 <= v <= 1.001):
                guard_fail.append(f"{name}: UV 越界 ({u:.3f},{v:.3f})")
                break
        # 世界尺寸口径守卫：宽高比必须等于贴图内容宽高比；高度按口径（树/小株）核对
        _, _, _cv, cw, ch = VANILLA[name]
        aspect = cw / ch
        if abs(g["D"] / g["H"] - aspect) > 0.02:
            guard_fail.append(f"{name}: 宽高比偏离贴图 D/H={g['D'] / g['H']:.3f} vs {aspect:.3f}")
        if name in TREES or name == "saguaro" or POLICY == "A":
            ew, eh = _cells(name)
            if (abs(g["H"] - round(eh * TREE_K, 2)) > 1e-9
                    or abs(g["D"] - round(ew * TREE_K, 2)) > 1e-9):
                guard_fail.append(f"{name}: 世界尺寸偏离原版占格推导 H={g['H']} D={g['D']}")
        elif abs(g["H"] - SMALL_H[name]) > 1e-9:
            guard_fail.append(f"{name}: 小株高度偏离现实约定 {g['H']} != {SMALL_H[name]}")
        arch_meta.append(dict(name=name, H=g["H"], D=g["D"], tris=len(o.f),
                              verts=len(o.v), build=g["build"]))
        obj_path = os.path.join(OBJ_DIR, name + ".obj")
        with open(obj_path, "w", encoding="utf-8") as f:
            f.write(f"# AI生成 plant_build.py {name}\n")
            for p in o.v:
                f.write(f"v {p[0]:.4f} {p[1]:.4f} {p[2]:.4f}\n")
            for t in o.uv:
                f.write(f"vt {t[0]:.5f} {t[1]:.5f}\n")
            for n_ in o.n:
                f.write(f"vn {n_[0]:.4f} {n_[1]:.4f} {n_[2]:.4f}\n")
            f.write("usemtl body\n")
            for a, b, c in o.f:
                f.write(f"f {a+1}/{a+1}/{a+1} {b+1}/{b+1}/{b+1} {c+1}/{c+1}/{c+1}\n")
        shutil.copyfile(obj_path, os.path.join(VIEW_OBJ_DIR, name + ".obj"))

    if guard_fail:
        for e in guard_fail:
            print("GUARD-FAIL", e)
        sys.exit(2)

    # ---- 5. 取样可视化（人判语义）----
    cols = 6
    cell = 150
    rows = (len(SPEC) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * cell), (40, 44, 52))
    d = ImageDraw.Draw(sheet)
    colors = dict(bark=(255, 120, 40), leaf=(80, 255, 120), leaf2=(80, 200, 255), accent=(255, 80, 220))
    for i, (name, g) in enumerate(SPEC.items()):
        im = imgs[name]
        x, y = (i % cols) * cell, (i // cols) * cell
        thumb = im.copy()
        thumb.thumbnail((cell - 10, cell - 24))
        sc = thumb.width / im.width
        t = ImageDraw.Draw(thumb)
        for key, (rx0, ry0, rx1, ry1) in rects_all[name].items():
            t.rectangle([rx0 * sc, ry0 * sc, rx1 * sc, ry1 * sc],
                        outline=colors[key], width=1)
        sheet.paste(thumb, (x + 5, y + 18), thumb)
        d.rectangle([x, y, x + cell - 1, y + cell - 1], outline=(90, 95, 105))
        d.text((x + 3, y + 3), name, fill=(230, 230, 230))
    sheet.save(SAMPLE_DOC)

    # ---- 6. JSON（审核页与运行时同源）----
    out = dict(
        atlas="FPS3D/PlantAtlas.png",
        atlasPx=[ATLAS_W, atlas_h],
        pad=PAD,
        arch=[dict(name=m["name"], height=m["H"], diameter=m["D"],
                   obj=f"plants/{m['name']}.obj", build=m["build"],
                   regions={k: regions[f"{m['name']}.{k}"] for k in
                            ("bark", "leaf", "leaf2", "accent") if f"{m['name']}.{k}" in regions})
              for m in arch_meta],
    )
    with open(JSON_OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)

    tris = sum(m["tris"] for m in arch_meta)
    print(f"OK arch={len(arch_meta)} atlas={ATLAS_W}x{atlas_h} tris_total={tris} "
          f"tris_max={max(m['tris'] for m in arch_meta)}")
    for m in sorted(arch_meta, key=lambda m: -m["tris"])[:6]:
        print(f"  {m['name']}: {m['tris']} tris, {m['verts']} verts")


if __name__ == "__main__":
    main()
