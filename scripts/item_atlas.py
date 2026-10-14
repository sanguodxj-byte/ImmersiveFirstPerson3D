# AI生成
# -*- coding: utf-8 -*-
"""物块与资源类 3D 建模原版贴图取样与图集打包（单一真源 scripts/item_atlas.py）。

管线（对齐 furn_atlas.py / animal_atlas.py 口径）：
  1. 解析原版（Core / Biotech / Anomaly / Odyssey）物块与资源 ThingDef 的
     texPath、color、stackLimit，并与原版贴图合成（如各类岩石块 ChunkSandstone~ChunkVacstone、
     石砖 BlocksSandstone~BlocksVacstone、织物 Cloth/Synthread/Devilstrand/Hyperweave 的原版染色）。
  2. 从每件原版贴图的非黑边真实内部像素中，按原版等轴测明暗与结构部位提取 4 个 24x24 实心纹理块：
       · top    (uv=0) : 顶面 / 受光亮面（保留原版表面颗粒、金属锈斑或木纹）
       · side   (uv=1) : 主立面 / 中间调面（保留原版金属折面、石材劈裂面、木皮或包装立面纹理）
       · shade  (uv=2) : 背光斜切面 / 底部暗面 / 阴影折面
       · detail (uv=3) : 标志性局部细节（钢铁红锈斑、原木年轮截面、玻璃钢孔洞槽口、零部件铜线圈、
                         高级零部件蓝光核、医药十字标、中性胺瓶盖、燃料桶盖等）
  3. 打包输出：
       -> Textures/FPS3D/ItemAtlas.png（同步到 viewer/item_atlas.png 与游戏模组目录）
       -> Source/RimFirstPerson3D/ItemUVGen.cs（C# UV 常量表，勿手改）
       -> viewer/item_uv.json（前端审核台与守卫同源数据）
       -> docs/items/sampling.png（原版贴图 + 4 区取样色块可视化对照表）
"""
import os
import json
import math
import shutil
import numpy as np
from PIL import Image, ImageDraw

VAN = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
MOD = r"D:\rim mod\FirstPerson3D"
GAME_MOD = r"D:\Steam\steamapps\common\RimWorld\Mods\FirstPerson3D"

ATLAS_OUT = os.path.join(MOD, "Textures", "FPS3D", "ItemAtlas.png")
VIEW_ATLAS = os.path.join(MOD, "viewer", "item_atlas.png")
CS_OUT = os.path.join(MOD, "Source", "RimFirstPerson3D", "ItemUVGen.cs")
JSON_OUT = os.path.join(MOD, "viewer", "item_uv.json")
SAMPLE_DOC = os.path.join(MOD, "docs", "items", "sampling.png")

CELL_INNER = 20
PAD = 2
CELL_STEP = CELL_INNER + PAD * 2  # 24px
REGIONS = ["top", "side", "shade", "detail"]

# ---------------------------------------------------------------------------
# 物块与资源清单：(key, 原版贴图相对路径, RGB染色乘子(0..255), 原型类别, 展示名)
# ---------------------------------------------------------------------------
ITEMS = [
    # 1. 天然岩石块 (ChunkRockBase) —— 6 种原版岩石色 × 3 种原版随机形态 (a/b/c)
    ("chunksandstone",   r"Core\textures\things\item\chunk\chunkstone\rocklowa.png", (126, 104,  94), "ChunkRock", "砂岩块"),
    ("chunkgranite",     r"Core\textures\things\item\chunk\chunkstone\rocklowb.png", (105,  95,  97), "ChunkRock", "花岗岩块"),
    ("chunklimestone",   r"Core\textures\things\item\chunk\chunkstone\rocklowc.png", (158, 153, 135), "ChunkRock", "石灰岩块"),
    ("chunkslate",       r"Core\textures\things\item\chunk\chunkstone\rocklowa.png", ( 90,  90,  90), "ChunkRock", "板岩块"),
    ("chunkmarble",      r"Core\textures\things\item\chunk\chunkstone\rocklowb.png", (132, 135, 132), "ChunkRock", "大理石块"),
    ("chunkvacstone",    r"Core\textures\things\item\chunk\chunkstone\rocklowc.png", (115, 106,  98), "ChunkRock", "真空岩块"),

    # 2. 钢渣块 / 金属残骸块 (ChunkSlagSteel)
    ("chunkslagsteel",   r"Core\textures\things\item\chunk\chunkslag\metaldebrisa.png", (255, 255, 255), "ChunkSlag", "钢渣块"),

    # 3. 切制石砖块 (StoneBlocksBase) —— 6 种原版岩石色
    ("blockssandstone",  r"Core\textures\things\item\resource\stoneblocks\stoneblocks_c.png", (126, 104,  94), "StoneBlocks", "砂岩砖"),
    ("blocksgranite",    r"Core\textures\things\item\resource\stoneblocks\stoneblocks_c.png", (105,  95,  97), "StoneBlocks", "花岗岩砖"),
    ("blockslimestone",  r"Core\textures\things\item\resource\stoneblocks\stoneblocks_c.png", (158, 153, 135), "StoneBlocks", "石灰岩砖"),
    ("blocksslate",      r"Core\textures\things\item\resource\stoneblocks\stoneblocks_c.png", ( 70,  70,  70), "StoneBlocks", "板岩砖"),
    ("blocksmarble",     r"Core\textures\things\item\resource\stoneblocks\stoneblocks_c.png", (132, 135, 132), "StoneBlocks", "大理石砖"),
    ("blocksvacstone",   r"Core\textures\things\item\resource\stoneblocks\stoneblocks_c.png", (115, 106,  98), "StoneBlocks", "真空岩砖"),

    # 4. 金属与矿石物块 (Steel / Plasteel / Uranium / Silver / Gold / Jade / Obsidian / Bioferrite)
    ("steel",            r"Core\textures\things\item\resource\steel\steel_c.png",             (255, 255, 255), "Steel",       "钢铁"),
    ("plasteel",         r"Core\textures\things\item\resource\plasteel\plasteel_c.png",       (255, 255, 255), "Plasteel",    "玻璃钢"),
    ("uranium",          r"Core\textures\things\item\resource\uranium\uranium_c.png",         (255, 255, 255), "Uranium",     "铀矿块"),
    ("silver",           r"Core\textures\things\item\resource\silver\silver_c.png",           (255, 255, 255), "NuggetHeap",  "白银"),
    ("gold",             r"Core\textures\things\item\resource\gold\gold_c.png",               (255, 255, 255), "NuggetHeap",  "黄金"),
    ("jade",             r"Core\textures\things\item\resource\jade\jade_c.png",               (255, 255, 255), "Jade",        "玉石"),
    ("obsidian",         r"Odyssey\textures\things\item\resource\obsidian\obsidian_c.png",    (255, 255, 255), "Obsidian",    "黑曜石"),
    ("bioferrite",       r"Anomaly\textures\things\item\resource\bioferrite\bioferrite_c.png",(255, 255, 255), "Bioferrite",  "活铁"),

    # 5. 原木与板材 (WoodLog / GravlitePanel)
    ("woodlog",          r"Core\textures\things\item\resource\woodlog\woodlog_c.png",         (255, 255, 255), "WoodLog",     "原木"),
    ("gravlitepanel",    r"Odyssey\textures\things\item\resource\gravlitepanels\gravlitepanels_c.png", (255, 255, 255), "HexPanel", "重力板"),

    # 6. 织物卷 (Cloth / Synthread / DevilstrandCloth / Hyperweave)
    ("cloth",            r"Core\textures\things\item\resource\cloth\cloth_c.png",             (162, 157, 152), "FabricRoll",  "布料"),
    ("synthread",        r"Core\textures\things\item\resource\synthread\synthread_c.png",     (255, 255, 255), "FabricRoll",  "合成纤维"),
    ("devilstrandcloth", r"Core\textures\things\item\resource\devilstrand\devilstrand_c.png", (255, 255, 255), "FabricRoll",  "魔菇布"),
    ("hyperweave",       r"Core\textures\things\item\resource\hyperweave\hyperweave_c.png",   (255, 255, 255), "FabricRoll",  "超织物"),

    # 7. 工业制品与容器物块 (Component / Chemfuel / Neutroamine / Medicine / Wastepack)
    ("componentindustrial", r"Core\textures\things\item\resource\componentindustrial\componentindustrial.png", (255, 255, 255), "CompInd",   "零部件"),
    ("componentspacer",     r"Core\textures\things\item\resource\componentspacer\componentspacer.png",         (255, 255, 255), "CompSpacer","高级零部件"),
    ("chemfuel",            r"Core\textures\things\item\resource\chemfuel.png",                                (255, 255, 255), "Chemfuel",  "化合燃料"),
    ("neutroamine",         r"Core\textures\things\item\resource\neutroamine\neutroamine_c.png",               (255, 255, 255), "Bottles",   "中性胺"),
    ("medicineindustrial",  r"Core\textures\things\item\resource\medicine\medicineindustrial\medicineindustrial_c.png", (255, 255, 255), "MedBox", "医药"),
    ("medicineultratech",   r"Core\textures\things\item\resource\medicine\medicineultratech\medicineultratech_c.png",   (255, 255, 255), "MedBox", "闪耀世界医药"),
    ("medicineherbal",      r"Core\textures\things\item\resource\medicine\medicineherbal\medicineherbal_c.png",         (255, 255, 255), "MedHerbal","草药"),
    ("wastepack",           r"Biotech\textures\things\item\resource\wastepack\wastepack_c.png",                (255, 255, 255), "Wastepack", "有毒废料包"),
]


def load_tinted_rgba(rel_path, tint_rgb):
    full = os.path.join(VAN, rel_path)
    im = Image.open(full).convert("RGBA")
    arr = np.array(im, dtype=np.float32)
    tr, tg, tb = tint_rgb[0] / 255.0, tint_rgb[1] / 255.0, tint_rgb[2] / 255.0
    arr[..., 0] = np.clip(arr[..., 0] * tr, 0, 255)
    arr[..., 1] = np.clip(arr[..., 1] * tg, 0, 255)
    arr[..., 2] = np.clip(arr[..., 2] * tb, 0, 255)
    return arr.astype(np.uint8)


def extract_interior_mask(arr):
    """提取原版贴图去黑边后的真实内部像素掩码（排除透明背景与外圈黑色描边）。"""
    a = arr[..., 3]
    r = arr[..., 0].astype(np.float32)
    g = arr[..., 1].astype(np.float32)
    b = arr[..., 2].astype(np.float32)
    luma = 0.299 * r + 0.587 * g + 0.114 * b

    opaque = a > 180
    if not np.any(opaque):
        return opaque, luma

    # 腐蚀 2px 剥离最外圈抗锯齿黑边
    h, w = opaque.shape
    eroded = opaque.copy()
    for _ in range(2):
        nxt = eroded.copy()
        nxt[1:, :] &= eroded[:-1, :]
        nxt[:-1, :] &= eroded[1:, :]
        nxt[:, 1:] &= eroded[:, :-1]
        nxt[:, :-1] &= eroded[:, 1:]
        eroded = nxt

    # 排除内部深黑描边线（自适应阈值：对板岩/黑曜石等暗色物块放宽亮度阈值）
    valid_lumas = luma[eroded] if np.any(eroded) else luma[opaque]
    p75 = np.percentile(valid_lumas, 75) if len(valid_lumas) > 0 else 100.0
    min_luma = max(18.0, min(48.0, p75 * 0.34))

    interior = eroded & (luma >= min_luma)
    if np.sum(interior) < 24:
        interior = opaque & (luma >= max(14.0, p75 * 0.25))
    if np.sum(interior) < 12:
        interior = opaque
    return interior, luma


def make_patch_from_pixels(pixels_rgb, seed=0):
    """将一组真实原版像素重采样并合成为 20x20 自然纹理块（保留原版明暗起伏与质感，全实心 alpha=255）。"""
    if len(pixels_rgb) == 0:
        pixels_rgb = np.array([[128, 128, 128]], dtype=np.uint8)
    rng = np.random.default_rng(seed + 101)
    lumas = 0.299 * pixels_rgb[:, 0] + 0.587 * pixels_rgb[:, 1] + 0.114 * pixels_rgb[:, 2]
    order = np.argsort(lumas)
    sorted_px = pixels_rgb[order]
    n = len(sorted_px)
    # 裁剪极端离群点（取 15%~85% 核心颜色区间），避免单像素杂色造成花斑
    i0 = int(n * 0.15)
    i1 = max(i0 + 1, int(n * 0.85))
    core_px = sorted_px[i0:i1]
    nc = len(core_px)

    patch = np.zeros((CELL_INNER, CELL_INNER, 4), dtype=np.uint8)
    for y in range(CELL_INNER):
        v_bias = 0.62 - 0.24 * (y / float(max(1, CELL_INNER - 1)))
        for x in range(CELL_INNER):
            jitter = rng.normal(0.0, 0.10)
            q = np.clip(v_bias + jitter, 0.05, 0.95)
            idx = int(q * (nc - 1))
            rgb = core_px[idx]
            patch[y, x, 0] = rgb[0]
            patch[y, x, 1] = rgb[1]
            patch[y, x, 2] = rgb[2]
            patch[y, x, 3] = 255
    return patch


def sample_four_regions(key, arr):
    """从原版贴图内部像素提取 top(亮面)、side(主立面)、shade(背光暗面)、detail(特征色/锈斑/截面) 四块原版纹理。"""
    interior, luma = extract_interior_mask(arr)
    ys, xs = np.where(interior)
    px = arr[ys, xs, :3]
    lum = luma[ys, xs]

    r = px[:, 0].astype(np.float32)
    g = px[:, 1].astype(np.float32)
    b = px[:, 2].astype(np.float32)

    # 将主体底色像素与局部特征色（白卷芯/青标签/橙铁锈/黑瓶盖）分离，防止主立面出现异色斑点
    body_mask = np.ones(len(px), dtype=bool)
    if key == "devilstrandcloth":
        body_mask = (r - g) > 30
    elif key == "neutroamine":
        body_mask = (b - r) < 16
    elif key == "steel":
        body_mask = (r - b) < 20
    elif key == "chemfuel":
        body_mask = (r - g) > 25

    b_px = px[body_mask] if np.sum(body_mask) >= 16 else px
    b_lum = lum[body_mask] if np.sum(body_mask) >= 16 else lum

    # 按原版等轴测明暗分位切分：
    #   top  : 62% ~ 96% 分位（受光顶面/亮斜面）
    #   side : 32% ~ 68% 分位（主立面/中间调）
    #   shade: 8%  ~ 36% 分位（背光侧面/底面阴影）
    q08, q32, q36, q62, q68, q96 = np.percentile(b_lum, [8, 32, 36, 62, 68, 96])

    top_px = b_px[(b_lum >= q62) & (b_lum <= q96)]
    if len(top_px) < 8:
        top_px = b_px[b_lum >= q62]
    side_px = b_px[(b_lum >= q32) & (b_lum <= q68)]
    if len(side_px) < 8:
        side_px = b_px
    shade_px = b_px[(b_lum >= q08) & (b_lum <= q36)]
    if len(shade_px) < 8:
        shade_px = b_px[b_lum <= q36]

    # detail 区：按物种/物块特征提取原版贴图里的特征色像素（如钢铁橙褐锈斑、原木浅黄年轮截面、
    # 零部件暖黄铜圈、高级零部件青蓝高光、医药十字标、草药捆绳等）
    r = px[:, 0].astype(np.float32)
    g = px[:, 1].astype(np.float32)
    b = px[:, 2].astype(np.float32)
    sat = np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)

    if key == "steel":
        # 钢铁原版贴图上的铁锈橙斑 (r - b 最高区域)
        score = (r - b) * 1.5 + sat
        th = np.percentile(score, 80)
        det_px = px[score >= th]
    elif key == "woodlog":
        # 原木前端浅米黄年轮截面 (高亮度 + 暖黄)
        score = lum + (r - b) * 0.6
        th = np.percentile(score, 82)
        det_px = px[score >= th]
    elif key == "componentindustrial":
        # 工业零部件上的黄铜/橙褐元件
        score = (r - b) * 1.4 + sat
        th = np.percentile(score, 78)
        det_px = px[score >= th]
    elif key == "componentspacer":
        # 高级零部件上的亮白/青银高光核
        score = lum + (b - r) * 0.8
        th = np.percentile(score, 82)
        det_px = px[score >= th]
    elif key in ("cloth", "synthread", "devilstrandcloth", "hyperweave"):
        # 织物卷左下端面浅灰白卷芯（低饱和度区域）
        low_sat = px[sat <= np.percentile(sat, 18)]
        det_px = low_sat if len(low_sat) >= 6 else px[lum >= np.percentile(lum, 82)]
    elif key == "medicineindustrial":
        # 工业医药箱上的翠绿十字像素 (g - r 最高)
        score = (g - r) * 2.2 + (g - b) * 1.4
        th = np.percentile(score, 90)
        det_px = px[score >= th]
    elif key == "medicineultratech":
        # 闪耀世界医药箱顶部的翠绿内底与亮白十字高对比区 (g - r 最高处)
        score = (g - r) * 2.5 + sat
        th = np.percentile(score, 92)
        det_px = px[score >= th]
    elif key == "medicineherbal":
        # 草药包顶部的翠绿药草叶 (g - r 最高)
        score = (g - r) * 2.0 + g * 0.5
        th = np.percentile(score, 85)
        det_px = px[score >= th]
    elif key == "neutroamine":
        # 中性胺瓶身青蓝色标签 (b - r 最高且中等亮度)
        score = (b - r) * 2.0 + sat
        th = np.percentile(score, 82)
        det_px = px[score >= th]
    elif key == "chemfuel":
        # 化合燃料桶右上角深色桶盖与凹槽阴影 (低红度暗部)
        score = -lum - (r - g) * 0.5
        th = np.percentile(score, 86)
        det_px = px[score >= th]
    else:
        # 其余物块：取高对比棱线/高光晶面或高饱和特征区
        score = sat + lum * 0.35
        th = np.percentile(score, 75)
        det_px = px[score >= th]

    if len(det_px) < 8:
        det_px = top_px

    seed_base = sum(ord(ch) for ch in key) * 17
    return {
        "top":    make_patch_from_pixels(top_px, seed_base + 1),
        "side":   make_patch_from_pixels(side_px, seed_base + 2),
        "shade":  make_patch_from_pixels(shade_px, seed_base + 3),
        "detail": make_patch_from_pixels(det_px, seed_base + 4),
    }


def pad_cell(inner):
    """四周外延 2px 边缘像素，防止双线性过滤和 mipmap 采样越界串色。"""
    out = np.zeros((CELL_STEP, CELL_STEP, 4), dtype=np.uint8)
    out[PAD:PAD + CELL_INNER, PAD:PAD + CELL_INNER] = inner
    for i in range(PAD):
        out[i, PAD:PAD + CELL_INNER] = inner[0]
        out[PAD + CELL_INNER + i, PAD:PAD + CELL_INNER] = inner[-1]
        out[PAD:PAD + CELL_INNER, i] = inner[:, 0]
        out[PAD:PAD + CELL_INNER, PAD + CELL_INNER + i] = inner[:, -1]
    out[:PAD, :PAD] = inner[0, 0]
    out[:PAD, PAD + CELL_INNER:] = inner[0, -1]
    out[PAD + CELL_INNER:, :PAD] = inner[-1, 0]
    out[PAD + CELL_INNER:, PAD + CELL_INNER:] = inner[-1, -1]
    return out


def build_all():
    os.makedirs(os.path.dirname(ATLAS_OUT), exist_ok=True)
    os.makedirs(os.path.dirname(SAMPLE_DOC), exist_ok=True)

    # 每行放 4 个物块（每个物块 4 个区 = 16 格/行，宽 16*24 = 384 -> 取 512x256 2的幂次图集）
    items_per_row = 5
    atlas_w = 512
    atlas_h = 256
    atlas = np.zeros((atlas_h, atlas_w, 4), dtype=np.uint8)

    uv_map = {}
    meta_items = {}
    sample_rows = []

    for idx, (key, rel_path, tint, arch, cn_name) in enumerate(ITEMS):
        arr = load_tinted_rgba(rel_path, tint)
        patches = sample_four_regions(key, arr)
        row = idx // items_per_row
        col_group = idx % items_per_row
        base_x = col_group * (4 * CELL_STEP)
        base_y = row * CELL_STEP

        uv_map[key] = {}
        for r_i, r_name in enumerate(REGIONS):
            cx = base_x + r_i * CELL_STEP
            cy = base_y
            padded = pad_cell(patches[r_name])
            atlas[cy:cy + CELL_STEP, cx:cx + CELL_STEP] = padded

            u0 = (cx + PAD) / float(atlas_w)
            u1 = (cx + PAD + CELL_INNER) / float(atlas_w)
            v1 = 1.0 - (cy + PAD) / float(atlas_h)
            v0 = 1.0 - (cy + PAD + CELL_INNER) / float(atlas_h)
            uv_map[key][r_name] = [round(u0, 5), round(v0, 5), round(u1, 5), round(v1, 5)]

        meta_items[key] = {
            "arch": arch,
            "label": cn_name,
            "tint": list(tint),
            "tex": rel_path.replace("\\", "/"),
        }
        sample_rows.append((key, cn_name, arch, arr, patches))

    atlas_img = Image.fromarray(atlas, mode="RGBA")
    atlas_img.save(ATLAS_OUT)
    shutil.copy2(ATLAS_OUT, VIEW_ATLAS)
    game_atlas = os.path.join(GAME_MOD, "Textures", "FPS3D", "ItemAtlas.png")
    if os.path.exists(os.path.dirname(game_atlas)):
        shutil.copy2(ATLAS_OUT, game_atlas)

    # 生成 Source/RimFirstPerson3D/ItemUVGen.cs
    total_entries = len(ITEMS) * len(REGIONS)
    lines = [
        "// AI生成",
        "// 自动生成 —— 由 scripts/item_atlas.py 产出，请勿手改。",
        "// 键 = \"<物块名>.<部位>\"（top/side/shade/detail），值 = 图集 UV (u0, v0, u1, v1)。",
        "using System.Collections.Generic;",
        "using UnityEngine;",
        "",
        "namespace RimFirstPerson3D",
        "{",
        "    public static class ItemUVGen",
        "    {",
        "        public const string TexPath = \"FPS3D/ItemAtlas\";",
        f"        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>({total_entries})",
        "        {",
    ]
    for key in sorted(uv_map.keys()):
        for r_name in REGIONS:
            u0, v0, u1, v1 = uv_map[key][r_name]
            lines.append(f'            {{ "{key}.{r_name}", new Vector4({u0:.5f}f, {v0:.5f}f, {u1:.5f}f, {v1:.5f}f) }},')
    lines += [
        "        };",
        "    }",
        "}",
        "",
    ]
    with open(CS_OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))

    # 生成 viewer/item_uv.json
    with open(JSON_OUT, "w", encoding="utf-8") as f:
        json.dump({
            "size": [atlas_w, atlas_h],
            "regions": REGIONS,
            "uv": uv_map,
            "items": meta_items,
        }, f, ensure_ascii=False, indent=2)

    # 生成 docs/items/sampling.png（原版染色贴图 + 4 区取样色卡对照）
    cols = 4
    rows = (len(sample_rows) + cols - 1) // cols
    cw, ch = 280, 96
    doc = Image.new("RGBA", (cols * cw, rows * ch), (28, 32, 40, 255))
    draw = ImageDraw.Draw(doc)
    for i, (key, cn_name, arch, arr, patches) in enumerate(sample_rows):
        r_idx, c_idx = divmod(i, cols)
        x0, y0 = c_idx * cw, r_idx * ch
        draw.rectangle([x0 + 2, y0 + 2, x0 + cw - 3, y0 + ch - 3], outline=(58, 66, 82, 255), width=1)
        vim = Image.fromarray(arr, mode="RGBA")
        vim.thumbnail((68, 68), Image.Resampling.NEAREST)
        doc.alpha_composite(vim, (x0 + 10 + (68 - vim.width) // 2, y0 + 18 + (68 - vim.height) // 2))
        draw.text((x0 + 86, y0 + 8), f"{key} ({arch})", fill=(230, 236, 245, 255))
        for k_i, r_name in enumerate(REGIONS):
            px_img = Image.fromarray(patches[r_name], mode="RGBA").resize((36, 36), Image.Resampling.NEAREST)
            bx = x0 + 86 + k_i * 45
            by = y0 + 30
            doc.alpha_composite(px_img, (bx, by))
            draw.rectangle([bx, by, bx + 36, by + 36], outline=(180, 190, 210, 255), width=1)
            draw.text((bx + 2, by + 40), r_name[:4], fill=(170, 182, 200, 255))

    doc.save(SAMPLE_DOC)
    print(f"Built ItemAtlas ({atlas_w}x{atlas_h}) for {len(ITEMS)} items -> {ATLAS_OUT}")
    print(f"Saved sampling sheet -> {SAMPLE_DOC}")


if __name__ == "__main__":
    build_all()
