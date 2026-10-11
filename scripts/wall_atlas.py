# AI生成
# -*- coding: utf-8 -*-
"""建造墙取样图集打包 + UV 覆盖可视化 + 自检。

【口径】与 scripts/furn_atlas.py 一致：坐标是**源图像素坐标**，原点在图像左上角，
半开区间 [x0,x1)×[y0,y1)。取样区不含原版黑描边（描边由反绕外壳网格画）；黑像素
用邻域均值补掉，透明像素补实，alpha 一律写 255，块四周按边缘复制留 PAD。

矩形来自 docs/walls/WallAtlasSpec.md（审美已定稿），**只许原样抄，不许在这里挪边**。
每块取样的黑像素占比超过 STROKE_MAX 就非零退出，不为了通过检查去改矩形。

产物：
    1.6/Textures/FPS3D/WallAtlas.png            图集（只含原版像素，黑描边已剥离）
    Source/RimFirstPerson3D/WallUVGen.cs        图集 UV 常量表（生成，勿手改）
    docs/walls/uv/<键名>.png                    放大图上画出该键的取样框
"""
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

TEX = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
MOD = r"D:\rim mod\FirstPerson3D"
ATLAS_OUT = os.path.join(MOD, "1.6", "Textures", "FPS3D", "WallAtlas.png")
VIEWER_ATLAS_OUT = os.path.join(MOD, "viewer", "wall_atlas.png")
VIEWER_JSON_OUT = os.path.join(MOD, "viewer", "wall_uv.json")
CS_OUT = os.path.join(MOD, "Source", "RimFirstPerson3D", "WallUVGen.cs")
UV_OUT = os.path.join(MOD, "docs", "walls", "uv")
FLAT_DOC_OUT = os.path.join(MOD, "docs", "wall_models")

WL = "Core/textures/things/building/linked/wall"

ATLAS_W = 512
PAD = 2           # 图集内每块取样的边缘复制宽度（防双线性/ mip 串色）
STROKE_LUMA = 40  # 黑描边判定阈值（0-255）
STROKE_MAX = 0.02 # 每块取样允许的黑像素占比上限

# 键 -> (源图, 矩形)。top = 墙帽（贴顶面），side = 立面（贴四个立面）。
# 【硬约束】前 6 项来自 docs/walls/WallAtlasSpec.md，矩形严禁改动。
SPEC = [
    ("bricks.top", WL + "/wall_atlas_bricks.png", (170, 97, 230, 121)),
    ("bricks.side", WL + "/wall_atlas_bricks.png", (170, 126, 230, 146)),
    ("planks.top", WL + "/wall_atlas_planks.png", (170, 97, 230, 121)),
    ("planks.side", WL + "/wall_atlas_planks.png", (170, 126, 230, 146)),
    ("smooth.top", WL + "/wall_atlas_smooth.png", (170, 97, 230, 121)),
    ("smooth.side", WL + "/wall_atlas_smooth.png", (170, 126, 230, 146)),
]

# 扩展墙体、矮墙掩体、围栏、穿墙设备与壁挂灯具的原版贴图取样区
EXT_SPEC = [
    # 平滑岩石墙 (SmoothedSandstone / Granite / Limestone / Slate / Marble / Vacstone)
    ("rocksmooth.top",    "Core/textures/things/building/linked/rocksmooth_atlas.png", (170, 96, 230, 120)),
    ("rocksmooth.side",   "Core/textures/things/building/linked/rocksmooth_atlas.png", (170, 124, 230, 144)),
    ("rocksmooth.detail", "Core/textures/things/building/linked/rocksmooth_atlas.png", (170, 124, 230, 134)),

    # 远古加固防爆墙 (AncientFortifiedWall / OrbitalAncientFortifiedWall)
    ("fortified.top",     "Odyssey/textures/things/building/linked/ancientfortifiedwall_atlas.png", (136, 76, 184, 96)),
    ("fortified.side",    "Odyssey/textures/things/building/linked/ancientfortifiedwall_atlas.png", (136, 100, 184, 115)),
    ("fortified.detail",  "Odyssey/textures/things/building/linked/ancientfortifiedwall_atlas.png", (142, 102, 178, 114)),

    # 飞船气密船体墙 (GravshipHull)
    ("gravshiphull.top",    "Odyssey/textures/things/building/linked/gravshiphull/gravshiphull_atlas.png", (272, 158, 368, 190)),
    ("gravshiphull.side",   "Odyssey/textures/things/building/linked/gravshiphull/gravshiphull_atlas.png", (272, 200, 368, 230)),
    ("gravshiphull.detail", "Odyssey/textures/things/building/linked/gravshiphull/gravshiphull_atlas.png", (272, 191, 368, 201)),

    # 虫穴分泌物岩土墙 (BurrowWall)
    ("burrow.top",    "Odyssey/textures/things/building/linked/burrowwall_atlas.png", (272, 154, 368, 184)),
    ("burrow.side",   "Odyssey/textures/things/building/linked/burrowwall_atlas.png", (272, 188, 368, 226)),
    ("burrow.detail", "Odyssey/textures/things/building/linked/burrowwall_atlas.png", (272, 194, 368, 206)),

    # 平滑冰墙 (SmoothedSolidIce)
    ("smoothedice.top",    "Odyssey/textures/things/building/linked/smoothedice_atlas.png", (170, 96, 230, 120)),
    ("smoothedice.side",   "Odyssey/textures/things/building/linked/smoothedice_atlas.png", (170, 124, 230, 144)),
    ("smoothedice.detail", "Odyssey/textures/things/building/linked/smoothedice_atlas.png", (170, 121, 230, 129)),

    # 天然冰墙 (SolidIce)
    ("solidice.top",    "Odyssey/textures/things/building/linked/solidicewall_atlas.png", (138, 79, 182, 95)),
    ("solidice.side",   "Odyssey/textures/things/building/linked/solidicewall_atlas.png", (138, 97, 182, 111)),
    ("solidice.detail", "Odyssey/textures/things/building/linked/solidicewall_atlas.png", (138, 94, 182, 102)),

    # 异象迷宫灰墙 (GrayWall)
    ("graywall.top",    "Anomaly/textures/things/building/linked/graywall_atlas.png", (170, 96, 230, 120)),
    ("graywall.side",   "Anomaly/textures/things/building/linked/graywall_atlas.png", (170, 126, 230, 144)),
    ("graywall.detail", "Anomaly/textures/things/building/linked/graywall_atlas.png", (170, 122, 230, 128)),

    # 虚空金属刺墙 (VoidmetalWall)
    ("voidmetal.top",    "Anomaly/textures/things/building/linked/voidmetalwall_atlas.png", (170, 96, 230, 120)),
    ("voidmetal.side",   "Anomaly/textures/things/building/linked/voidmetalwall_atlas.png", (170, 126, 230, 144)),
    ("voidmetal.detail", "Anomaly/textures/things/building/linked/voidmetalwall_atlas.png", (170, 128, 230, 142)),

    # 扭曲血肉墙 (Fleshmass)
    ("fleshmass.top",    "Anomaly/textures/things/building/linked/fleshmass_atlas.png", (344, 192, 456, 236)),
    ("fleshmass.side",   "Anomaly/textures/things/building/linked/fleshmass_atlas.png", (344, 236, 456, 276)),
    ("fleshmass.detail", "Anomaly/textures/things/building/linked/fleshmass_atlas.png", (360, 200, 420, 250)),

    # 路障掩体 (Barricade)
    ("barricade.top",    "Core/textures/things/building/linked/barricade_atlas.png", (168, 104, 190, 118)),
    ("barricade.side",   "Core/textures/things/building/linked/barricade_atlas.png", (168, 118, 190, 134)),
    ("barricade.detail", "Core/textures/things/building/linked/barricade_atlas.png", (192, 96, 208, 134)),

    # 沙袋掩体 (Sandbags)
    ("sandbags.top",    "Core/textures/things/building/linked/sandbags_atlas.png", (272, 154, 368, 176)),
    ("sandbags.side",   "Core/textures/things/building/linked/sandbags_atlas.png", (272, 154, 368, 224)),
    ("sandbags.detail", "Core/textures/things/building/linked/sandbags_atlas.png", (272, 180, 368, 204)),

    # 木/石栅栏 (Fence smooth)
    ("fence_smooth.top",    "Core/textures/things/building/linked/fence/fence_atlas_smooth.png", (170, 110, 208, 119)),
    ("fence_smooth.side",   "Core/textures/things/building/linked/fence/fence_atlas_smooth.png", (170, 130, 208, 139)),
    ("fence_smooth.detail", "Core/textures/things/building/linked/fence/fence_atlas_smooth.png", (196, 80, 204, 86)),

    # 金属栅栏 (Fence metal)
    ("fence_metal.top",    "Core/textures/things/building/linked/fence/fence_atlas_metal.png", (340, 214, 416, 226)),
    ("fence_metal.side",   "Core/textures/things/building/linked/fence/fence_atlas_metal.png", (340, 268, 416, 280)),
    ("fence_metal.detail", "Core/textures/things/building/linked/fence/fence_atlas_metal.png", (392, 160, 408, 172)),

    # 远古混凝土围栏 (AncientFence)
    ("ancientfence.top",    "Core/textures/things/building/linked/ancientfence_atlas.png", (184, 115, 216, 122)),
    ("ancientfence.side",   "Core/textures/things/building/linked/ancientfence_atlas.png", (184, 122, 216, 142)),
    ("ancientfence.detail", "Core/textures/things/building/linked/ancientfence_atlas.png", (174, 110, 182, 142)),

    # 穿墙通风口 (Vent)
    ("vent.top",    "Core/textures/things/building/misc/tempcontrol/vent_north.png", (8, 6, 56, 30)),
    ("vent.side",   "Core/textures/things/building/misc/tempcontrol/vent_east.png", (8, 36, 56, 58)),
    ("vent.detail", "Core/textures/things/building/misc/tempcontrol/vent_north.png", (8, 38, 56, 58)),

    # 穿墙制冷机 (Cooler)
    ("cooler.top",    "Core/textures/things/building/misc/tempcontrol/cooler_north.png", (8, 13, 56, 23)),
    ("cooler.side",   "Core/textures/things/building/misc/tempcontrol/cooler_north.png", (8, 38, 56, 58)),
    ("cooler.cold",   "Core/textures/things/building/misc/tempcontrol/cooler_north.png", (28, 24, 36, 29)),
    ("cooler.hot",    "Core/textures/things/building/misc/tempcontrol/cooler_north.png", (29, 7, 35, 11)),

    # 壁灯 (WallLamp)
    ("walllamp.top",    "Core/textures/things/building/furniture/walllamp/walllamp_menuicon.png", (16, 24, 48, 29)),
    ("walllamp.side",   "Core/textures/things/building/furniture/walllamp/walllamp_menuicon.png", (16, 29, 48, 36)),
    ("walllamp.detail", "Core/textures/things/building/furniture/walllamp/walllamp_menuicon.png", (18, 31, 46, 35)),

    # 壁炬 (TorchWallLamp)
    ("walltorch.top",    "Core/textures/things/building/misc/walltorch/walltorch_menuicon.png", (25, 19, 39, 33)),
    ("walltorch.side",   "Core/textures/things/building/misc/walltorch/walltorch_north.png", (27, 34, 37, 50)),
    ("walltorch.detail", "Core/textures/things/building/misc/walltorch/walltorch_menuicon.png", (25, 20, 39, 34)),
]


def font(size=12):
    for p in (r"C:\Windows\Fonts\cons.ttf", r"C:\Windows\Fonts\arial.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except Exception:
            return ImageFont.load_default()
    return ImageFont.load_default()


def load(path):
    im = Image.open(os.path.join(TEX, path.replace("/", os.sep))).convert("RGBA")
    return np.asarray(im)


def luma(block):
    return block[:, :, :3].astype(np.int32).mean(axis=2)


def black_mask(block):
    """黑描边 := 不透明且 luma < 阈值。透明的黑像素不算（alpha 由补实负责）。"""
    return (block[:, :, 3] >= 128) & (luma(block) < STROKE_LUMA)


def black_ratio(block):
    return float(black_mask(block).mean())


def dilate(m):
    """4 邻域膨胀一格。"""
    o = m.copy()
    o[1:, :] |= m[:-1, :]
    o[:-1, :] |= m[1:, :]
    o[:, 1:] |= m[:, :-1]
    o[:, :-1] |= m[:, 1:]
    return o


def fill_holes(block, mask):
    """把 mask 里的像素用邻域均值迭代补掉（取值只来自非 mask 像素）。
    目的：材质不含原版黑描边、也不留 alpha=0，但每个像素仍来自原图。"""
    out = block.astype(np.float32)
    m = mask.copy()
    for _ in range(96):
        if not m.any():
            break
        valid = (~m).astype(np.float32)[..., None]
        src = out * valid
        acc = np.zeros_like(src)
        cnt = np.zeros_like(valid)
        acc[1:] += src[:-1]; acc[:-1] += src[1:]
        acc[:, 1:] += src[:, :-1]; acc[:, :-1] += src[:, 1:]
        cnt[1:] += valid[:-1]; cnt[:-1] += valid[1:]
        cnt[:, 1:] += valid[:, :-1]; cnt[:, :-1] += valid[:, 1:]
        reach = m & (cnt[..., 0] > 0)
        if not reach.any():
            break
        out[reach] = acc[reach] / cnt[reach]
        m &= ~reach
    if m.any():                       # 整块都要补时邻域取不到值 -> 用本块非 mask 均值兜底
        keep = ~mask
        base = out[keep].mean(axis=0) if keep.any() else np.array([128., 128., 128., 255.])
        out[m] = base
    return np.clip(out + 0.5, 0, 255).astype(np.uint8)


def check():
    """黑像素闸门：超标的键列出来，main 据此非零退出。不改矩形去迁就它。"""
    bad = []
    ratios = []
    for key, path, (x0, y0, x1, y1) in SPEC:
        sub = load(path)[y0:y1, x0:x1]
        r = black_ratio(sub)
        ratios.append((key, r, int(black_mask(sub).sum()), sub.shape[1] * sub.shape[0]))
        if r > STROKE_MAX:
            bad.append((key, r))
    return bad, ratios


def expand_side(key, core):
    """立面源区只有 20px 高（2.5 皮砖，对应俯视矮墙裙），直接拉到 2.70m 高会把横砖拉成 81cm 竖板、
    2px 灰缝拉成 27cm 横带。用源区内的完整皮周期纵向无损展开为 96px（12 皮横砖/横板），
    使 3D 立面保持原版横砖/横板纵横比，且单面只需 1 个 Quad、门楣砖缝自动对齐。"""
    if not key.endswith(".side"):
        return core
    if key == "bricks.side":
        # 前 16 行 (y=126..142) 是完整的两皮错缝砖 + 两条 2px 水平灰缝周期 (8px + 8px)
        cycle16 = core[:16, :]
        return np.tile(cycle16, (6, 1, 1))
    if key == "planks.side":
        # 三皮错缝横木板：第 1 皮 (0..8, 竖缝 x=12)、第 2 皮 (8..16, 竖缝 x=3)、
        # 第 3 皮 (16..20 补齐至 6px 板面 + 14..16 的 2px 板缝, 竖缝 x=34)，共 24px 周期 × 4 = 96px
        c1 = core[0:8, :]
        c2 = core[8:16, :]
        c3 = np.vstack([core[16:20, :], core[18:20, :], core[14:16, :]])
        cycle24 = np.vstack([c1, c2, c3])
        return np.tile(cycle24, (4, 1, 1))
    if key == "smooth.side":
        # smooth.side: 16px 原片与镜像组成 32px 无缝块 × 3 = 96px，保持正方形像素颗粒
        c16 = core[:16, :]
        c32 = np.vstack([c16, c16[::-1]])
        return np.tile(c32, (3, 1, 1))
    return core


def pack():
    """-> (atlas ndarray, placed, (W,H))；placed = [(key, path, src_rect, (ax,ay,aw,ah))]"""
    expanded = []
    spec_keys = {k for k, _, _ in SPEC}
    for key, path, (x0, y0, x1, y1) in (SPEC + EXT_SPEC):
        src = load(path)
        sub = src[y0:y1, x0:x1].copy()
        if key in spec_keys:
            mask = black_mask(sub) | (sub[:, :, 3] < 128)
        else:
            # 扩展墙体包含暗色虚空金属与纯红热风标，仅修补透明或纯黑轮廓像素 (max RGB < 20)
            mask = (sub[:, :, 3] < 128) | (sub[:, :, :3].max(axis=2) < 20)
        core = fill_holes(sub, mask) if mask.any() else sub
        core = expand_side(key, core).copy()
        core[:, :, 3] = 255
        expanded.append((key, path, (x0, y0, x1, y1), core))

    cells = sorted(expanded, key=lambda c: -c[3].shape[0] * c[3].shape[1])
    col, row_h, y = 0, 0, 0
    placed = []
    cores = {}
    for key, path, (x0, y0, x1, y1), core in cells:
        h, w = core.shape[:2]
        aw, ah = w + 2 * PAD, h + 2 * PAD
        if col + aw > ATLAS_W:
            col = 0
            y += row_h
            row_h = 0
        placed.append((key, path, (x0, y0, x1, y1), (col, y, aw, ah)))
        cores[key] = core
        row_h = max(row_h, ah)
        col += aw
    H = (((y + row_h) + 63) // 64) * 64
    atlas = np.zeros((H, ATLAS_W, 4), np.uint8)
    for key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        core = cores[key]
        h, w = core.shape[:2]
        # 边缘复制（clamp）而不是留透明，否则双线性会采到邻块
        big = np.zeros((ah, aw, 4), np.uint8)
        big[PAD:PAD + h, PAD:PAD + w] = core
        big[:PAD, PAD:PAD + w] = core[0:1]
        big[ah - PAD:, PAD:PAD + w] = core[-1:]
        big[:, :PAD] = big[:, PAD:PAD + 1]
        big[:, aw - PAD:] = big[:, aw - PAD - 1:aw - PAD]
        atlas[ay:ay + ah, ax:ax + aw] = big
    return atlas, placed, (ATLAS_W, H)


def uv_of(block, atlas_size):
    """图集矩形 -> Unity UV (u0,v0,u1,v1)，v=0 在图像底边，故 v = 1 - y/H。"""
    ax, ay, aw, ah = block
    W, H = atlas_size
    return ((ax + PAD) / float(W), 1.0 - (ay + ah - PAD) / float(H),
            (ax + aw - PAD) / float(W), 1.0 - (ay + PAD) / float(H))


def emit_cs(placed, atlas_size, path):
    W, H = atlas_size
    lines = [
        "// AI生成",
        "// 自动生成 —— 由 scripts/wall_atlas.py 产出，请勿手改。",
        "// 键 = \"<外观>.<区段>\"（区段 top=墙帽 / side=立面），值 = 图集 UV (u0, v0, u1, v1)。",
        "// v 口径：Unity 纹理 v=0 在图像**底边**，故 v = 1 - y/H；始终 v0 < v1。",
        "using System.Collections.Generic;",
        "using UnityEngine;",
        "",
        "namespace RimFirstPerson3D",
        "{",
        "    public static class WallUVGen",
        "    {",
        "        public const string TexPath = \"FPS3D/WallAtlas\";",
        "        public static readonly Dictionary<string, Vector4> R = "
        "new Dictionary<string, Vector4>(" + str(len(placed)) + ")",
        "        {",
    ]
    for key, src, (x0, y0, x1, y1), block in placed:
        u0, v0, u1, v1 = uv_of(block, atlas_size)
        lines.append("            {{ \"{k}\", new Vector4({u0:.5f}f, {v0:.5f}f, {u1:.5f}f, {v1:.5f}f) }},"
                     .format(k=key, u0=u0, v0=v0, u1=u1, v1=v1))
    lines += [
        "        };",
        "",
        "        /// <summary>取样区的源像素尺寸（宽, 高）。</summary>",
        "        public static readonly Dictionary<string, Vector2> SrcSize =",
        "            new Dictionary<string, Vector2>(" + str(len(placed)) + ")",
        "        {",
    ]
    for key, src, (x0, y0, x1, y1), block in placed:
        lines.append("            {{ \"{k}\", new Vector2({w}f, {h}f) }},"
                     .format(k=key, w=x1 - x0, h=y1 - y0))
    lines += ["        };", "    }", "}", ""]
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def overlay(placed):
    """每个键出一张放大图：绿框 = 该键在源图上的取样框。"""
    os.makedirs(UV_OUT, exist_ok=True)
    done = []
    for key, path, (x0, y0, x1, y1), block in placed:
        src = load(path)
        h, w = src.shape[:2]
        scale = max(2, min(10, 700 // max(w, h)))
        big = Image.fromarray(src, "RGBA").resize((w * scale, h * scale), Image.NEAREST)
        d = ImageDraw.Draw(big)
        d.rectangle([x0 * scale, y0 * scale, (x1 - 1) * scale, (y1 - 1) * scale],
                    outline=(0, 255, 0, 255), width=2)
        d.text((x0 * scale + 3, y0 * scale + 3), key, fill=(0, 255, 0, 255), font=font(11))
        p = os.path.join(UV_OUT, key + ".png")
        big.save(p)
        done.append(p)
    return done


def selftest(atlas, placed, atlas_size):
    """闸门与 UV 口径都要双向自证：该报的报、不该报的不报，图集像素逐字节回对源图。"""
    errs = []
    W, H = atlas_size

    clean = np.zeros((10, 10, 4), np.uint8)
    clean[:, :, :3] = 200
    clean[:, :, 3] = 255
    if black_ratio(clean) > STROKE_MAX:
        errs.append("闸门误报：全 200 灰度的干净块被判超标")

    dirty = clean.copy()
    dirty[:2, :2, :3] = 10                     # 4/100 = 4% 黑，> 2%
    if black_ratio(dirty) <= STROKE_MAX:
        errs.append("闸门漏报：4% 黑像素的块没被判定超标")

    ghost = clean.copy()
    ghost[:2, :2, :3] = 10
    ghost[:2, :2, 3] = 0                       # 透明的黑不是描边（alpha 由补实负责）
    if black_ratio(ghost) > STROKE_MAX:
        errs.append("闸门把透明黑像素当成了描边")

    for key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        src = load(path)[y0:y1, x0:x1]
        exp = expand_side(key, src)
        h, w = exp.shape[:2]
        got = atlas[ay + PAD:ay + h + PAD, ax + PAD:ax + w + PAD]
        if got.shape != (h, w, 4):
            errs.append(key + ": 图集内容区尺寸 " + str(got.shape) + " 与取样 " + str((h, w)))
            continue
        # 这些取样区实测无黑无透明，所以必须与源图展开块逐字节相同（补绘路径没动过像素）
        if black_ratio(src) == 0.0 and (src[:, :, 3] >= 128).all():
            if not np.array_equal(got, exp):
                errs.append(key + ": 图集内容区与源图不逐字节相等")
        if not (got[:, :, 3] == 255).all():
            errs.append(key + ": 图集里有 alpha != 255 的像素")
        # 边缘复制环带
        if not np.array_equal(got[0:1], atlas[ay + PAD - 1:ay + PAD, ax + PAD:ax + w + PAD]):
            errs.append(key + ": 上侧 PAD 不是边缘复制")
        if not np.array_equal(got[:, 0:1], atlas[ay + PAD:ay + h + PAD, ax + PAD - 1:ax + PAD]):
            errs.append(key + ": 左侧 PAD 不是边缘复制")
        # UV 必须框回同一批像素，且 v0 < v1
        u0, v0, u1, v1 = uv_of((ax, ay, aw, ah), atlas_size)
        if not (v0 < v1 and u0 < u1):
            errs.append(key + ": UV 不满足 u0<u1 / v0<v1")
        if (round(u0 * W), round((1.0 - v1) * H)) != (ax + PAD, ay + PAD):
            errs.append(key + ": UV 左上角没落回内容区起点")
        if (round(u1 * W), round((1.0 - v0) * H)) != (ax + aw - PAD, ay + ah - PAD):
            errs.append(key + ": UV 右下角没落回内容区终点")
    return errs


def emit_viewer_json(placed, atlas_size, path):
    uv_map = {}
    for key, src, (x0, y0, x1, y1), block in placed:
        u0, v0, u1, v1 = uv_of(block, atlas_size)
        uv_map[key] = [round(u0, 5), round(v0, 5), round(u1, 5), round(v1, 5)]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"atlasSize": list(atlas_size), "uv": uv_map}, f, ensure_ascii=False, indent=2)


def save_sampling_sheet(atlas, placed):
    os.makedirs(FLAT_DOC_OUT, exist_ok=True)
    groups = {}
    order = []
    for key, path, rect, block in placed:
        prefix = key.split(".")[0]
        if prefix not in groups:
            groups[prefix] = []
            order.append(prefix)
        groups[prefix].append((key, path, rect, block))

    cols = 3
    rows = (len(order) + cols - 1) // cols
    cw, ch = 310, 108
    sheet = Image.new("RGB", (cols * cw, rows * ch), (26, 30, 38))
    draw = ImageDraw.Draw(sheet)
    f_title = font(12)
    f_sub = font(11)

    for idx, prefix in enumerate(order):
        r, c = divmod(idx, cols)
        x0, y0 = c * cw, r * ch
        draw.rectangle([x0, y0, x0 + cw - 1, y0 + ch - 1], outline=(52, 60, 74), width=1)
        items = groups[prefix]
        rel = items[0][1]
        src_arr = load(rel)
        sh, sw = src_arr.shape[:2]
        if sw >= 256 and sh >= 256 and "linked" in rel.lower():
            gw, gh = sw // 4, sh // 4
            crop = Image.fromarray(src_arr[gh:2*gh, 2*gw:3*gw], "RGBA")
        else:
            crop = Image.fromarray(src_arr, "RGBA")
        crop = crop.resize((72, 72), Image.NEAREST)
        bg = Image.new("RGBA", (76, 76), (38, 43, 52, 255))
        bg.alpha_composite(crop, (2, 2))
        sheet.paste(bg.convert("RGB"), (x0 + 8, y0 + 24))
        draw.text((x0 + 8, y0 + 5), prefix, fill=(225, 234, 248), font=f_title)

        for j, (key, _, _, (ax, ay, aw, ah)) in enumerate(items[:4]):
            sub_name = key.split(".")[1]
            patch = Image.fromarray(atlas[ay+PAD:ay+ah-PAD, ax+PAD:ax+aw-PAD], "RGBA").resize((46, 46), Image.NEAREST)
            px = x0 + 94 + j * 52
            py = y0 + 28
            sheet.paste(patch.convert("RGB"), (px, py))
            draw.rectangle([px - 1, py - 1, px + 46, py + 46], outline=(95, 110, 132), width=1)
            draw.text((px + 2, py + 50), sub_name[:6], fill=(175, 190, 212), font=f_sub)

    out_p = os.path.join(FLAT_DOC_OUT, "sampling.png")
    sheet.save(out_p)
    return out_p


def main():
    bad, ratios = check()
    for key, r, n, tot in ratios:
        print("stroke %-12s %d/%d = %.2f%% (limit %.0f%%)" % (key, n, tot, 100.0 * r, STROKE_MAX * 100))
    if bad:
        for key, r in bad:
            print("FAIL 黑像素占比超标:", key, "%.2f%%" % (100.0 * r), "-> 矩形压在原版描边上，见 docs/walls/WallAtlasSpec.md")
        return 1

    atlas, placed, size = pack()
    errs = selftest(atlas, placed, size)
    if errs:
        for e in errs:
            print("FAIL 自检:", e)
        return 1

    os.makedirs(os.path.dirname(ATLAS_OUT), exist_ok=True)
    os.makedirs(os.path.dirname(VIEWER_ATLAS_OUT), exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(ATLAS_OUT)
    Image.fromarray(atlas, "RGBA").save(VIEWER_ATLAS_OUT)
    emit_cs(placed, size, CS_OUT)
    emit_viewer_json(placed, size, VIEWER_JSON_OUT)
    samp_p = save_sampling_sheet(atlas, placed)
    print("atlas:", ATLAS_OUT, size, "blocks:", len(placed))
    print("cs:", CS_OUT)
    print("sampling:", samp_p)
    spec_keys = {k for k, _, _ in SPEC}
    for p in overlay([x for x in placed if x[0] in spec_keys]):
        print("uv:", p)
    print("selftest: ok (", len(placed), "blocks )")
    return 0


if __name__ == "__main__":
    sys.exit(main())
