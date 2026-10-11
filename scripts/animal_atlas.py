# AI生成
# -*- coding: utf-8 -*-
"""动物三视图贴图取样与图集打包（单一真源）。

管线（对齐家具 furn_atlas.py / 植物 plant_build.py 口径）：
  1. 解析原版（Core / Biotech / Ideology / Odyssey）动物 PawnKindDef 的
     texPath、color、colorTwo、shaderType、drawSize，并与原版三视图贴图
     （_east / _south / _north）及 CutoutComplex 遮罩（_eastm / _southm）合成，
     得到与游戏内逐像素一致的真实外观贴图。
  2. 测量每个物种在 _east（侧视：长×高）与 _south（正视：宽×高）中的有效剪影包围盒，
     导出真实贴图比例（长/高/宽），供 3D 建模严格对齐原版剪影。
  3. 从每个物种的原版三视图贴图中提取 6 个部位纹理块（去描边并实心化，杜绝 Cutout 穿孔）：
       · coat   (uv=0) : 背部 / 躯干主毛皮、羽翼、鳞甲或背壳
       · belly  (uv=1) : 胸腹 / 下摆长毛 / 侧翼 / 腹甲
       · head   (uv=2) : 头部 / 面盘 / 吻部 / 头胸甲
       · limb   (uv=3) : 四肢 / 蹄爪 / 耳廓 / 深色肢端
       · detail (uv=4) : 物种标志性特征区（爆炸羊黄囊、毒气羊绿囊、敲击兽弯角、
                         鸡冠肉垂、象牙、鹿角、鸭喙、企鹅橙喙、熊猫黑斑、狐白尾尖等）
       · eye    (uv=5) : 眼瞳 / 鼻头深色区
  4. 打包输出：
       -> 1.6/Textures/FPS3D/AnimalAtlas.png（同步副本到 viewer/animal_atlas.png）
       -> Source/RimFirstPerson3D/AnimalUVGen.cs（C# 常量表，勿手改）
       -> viewer/animal_uv.json（预览与守卫同源数据）
       -> docs/animals/sampling.png（原版贴图 + 取样框 + 6 区色板可视化对照表）
"""
import glob
import json
import math
import os
import shutil
import sys
import xml.etree.ElementTree as ET

import numpy as np
from PIL import Image, ImageDraw

VAN = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
RIM_DATA = r"D:\steam\steamapps\common\RimWorld\Data"
MOD = r"D:\rim mod\FirstPerson3D"

ATLAS_OUT = os.path.join(MOD, "1.6", "Textures", "FPS3D", "AnimalAtlas.png")
VIEW_ATLAS = os.path.join(MOD, "viewer", "animal_atlas.png")
CS_OUT = os.path.join(MOD, "Source", "RimFirstPerson3D", "AnimalUVGen.cs")
JSON_OUT = os.path.join(MOD, "viewer", "animal_uv.json")
SAMPLE_DOC = os.path.join(MOD, "docs", "animals", "sampling.png")

CELL_INNER = 20   # 每个部位纹理块内部尺寸 20x20
PAD = 2           # 四周边缘延展 2px（防双线性过滤/Mipmap 串色）
CELL_STEP = CELL_INNER + PAD * 2  # 24px
REGIONS = ["coat", "belly", "head", "limb", "detail", "eye"]


# ---------------------------------------------------------------------------
# 1. 解析 RimWorld XML 中的 PawnKindDef 外观定义
# ---------------------------------------------------------------------------

def _parse_col(s):
    if not s:
        return None
    s = s.strip().strip("()")
    parts = [float(x.strip()) for x in s.split(",")]
    if any(p > 1.001 for p in parts[:3]):
        return tuple(p / 255.0 for p in parts[:3])
    return tuple(parts[:3])


def load_xml_kinds():
    xmls = sorted(set(
        glob.glob(os.path.join(RIM_DATA, "*", "Defs", "**", "*Animal*.xml"), recursive=True) +
        glob.glob(os.path.join(RIM_DATA, "*", "Defs", "PawnKindDefs*", "*.xml"), recursive=True)
    ))
    kinds = {}
    for x in xmls:
        try:
            t = ET.parse(x)
            for pk in t.getroot().findall("PawnKindDef"):
                dn = pk.findtext("defName")
                race = pk.findtext("race") or dn
                ls = pk.find("lifeStages")
                if not dn or ls is None:
                    continue
                stages = ls.findall("li")
                if not stages:
                    continue
                bg = stages[-1].find("bodyGraphicData")
                if bg is None:
                    continue
                tp = bg.findtext("texPath") or ""
                if "Animal" not in tp:
                    continue
                c1 = _parse_col(bg.findtext("color"))
                c2 = _parse_col(bg.findtext("colorTwo"))
                sh = bg.findtext("shaderType") or "Cutout"
                ds_str = bg.findtext("drawSize") or "1.5"
                if "," in ds_str:
                    ds = max(float(v) for v in ds_str.strip("()").split(","))
                else:
                    ds = float(ds_str)
                key = dn.lower()
                # 若主 bodyGraphicData 指向灰白底图而 alternateGraphics 含彩色贴图（如 Horse1 / GuineaPig1），优先取首个彩色变体
                alt = pk.find("alternateGraphics")
                if alt is not None and c1 is None and key in ("horse", "guineapig", "hermitcrab"):
                    first_li = alt.find("li")
                    if first_li is not None and first_li.findtext("texPath"):
                        tp = first_li.findtext("texPath")
                kinds[key] = dict(
                    defName=dn,
                    race=(race or dn).lower(),
                    texPath=tp.replace("\\", "/"),
                    c1=c1,
                    c2=c2,
                    shader=sh,
                    drawSize=ds,
                )
        except Exception:
            pass
    return kinds


# ---------------------------------------------------------------------------
# 2. 定位原版贴图文件并按原版着色器规则合成（Cutout / CutoutComplex / CutoutSkin）
# ---------------------------------------------------------------------------

def build_tex_index():
    """扫描 VAN 下所有动物贴图目录，建立 小写相对路径 -> 磁盘实际路径 映射。"""
    idx = {}
    for dlc in ["Core", "Biotech", "Ideology", "Odyssey", "Royalty", "Anomaly"]:
        base = os.path.join(VAN, dlc, "textures")
        if not os.path.isdir(base):
            continue
        for root, _, files in os.walk(base):
            if "animal" not in root.lower():
                continue
            for f in files:
                if not f.lower().endswith(".png"):
                    continue
                full = os.path.join(root, f)
                rel = os.path.relpath(full, base).replace("\\", "/").lower()
                idx[rel] = full
    return idx


def resolve_view_paths(tex_path, tex_idx):
    """给定 XML texPath（如 Things/Pawn/Animal/Muffalo/Muffalo），解析 east/south/north 及遮罩路径。"""
    rel = tex_path.lower().strip("/")
    folder = os.path.dirname(rel)
    prefix = os.path.basename(rel)

    def _find_view(p_base, view):
        cand = f"{p_base}_{view}.png"
        if cand in tex_idx:
            return tex_idx[cand]
        return None

    out = {}
    if _find_view(rel, "east") is None:
        # 有些 texPath 指向文件夹或带性别后缀（如 DuckMale / DeerMale / GoatMale / SheepMale）
        # 在同文件夹下寻找成人非干尸非驼包贴图
        prefix_dir = folder + "/"
        candidates = [
            k[:-9] for k in sorted(tex_idx.keys())
            if k.startswith(prefix_dir)
            and k.endswith("_east.png")
            and "dessicated" not in k
            and "pack" not in k
            and "flying" not in k
            and "baby" not in k
            and "puppy" not in k
            and "kitten" not in k
            and "calf" not in k
            and "foal" not in k
            and "chick" not in k
            and "duckling" not in k
            and "piglet" not in k
            and "lamb" not in k
        ]
        # 优先挑含 male 或与文件夹同名的贴图（公鹿有角、公火鸡/孔雀有尾屏、公鸭有绿头）
        male_cands = [c for c in candidates if "male" in os.path.basename(c) or "tom" in os.path.basename(c)]
        if male_cands:
            rel = male_cands[0]
        elif candidates:
            rel = candidates[0]

    for v in ["east", "south", "north"]:
        p = _find_view(rel, v)
        if p:
            out[v] = p
        pm = _find_view(rel, v + "m")
        if pm:
            out[v + "m"] = pm
    return out


def composite_vanilla(img_path, mask_path, c1, c2, shader):
    """按 RimWorld 原版着色器（Cutout / CutoutComplex / CutoutSkin）合成贴图，同时返回遮罩数组。"""
    im = Image.open(img_path).convert("RGBA")
    arr = np.array(im, dtype=np.float32) / 255.0
    mask_r = None
    if mask_path and os.path.isfile(mask_path):
        m_im = Image.open(mask_path).convert("RGB").resize(im.size, Image.Resampling.BILINEAR)
        m_arr = np.array(m_im, dtype=np.float32) / 255.0
        mask_r = m_arr[:, :, 0]
        mask_g = m_arr[:, :, 1]
    if c1 is not None:
        col1 = np.array(c1, dtype=np.float32).reshape((1, 1, 3))
        if shader == "CutoutComplex" and mask_r is not None:
            mr = mask_r[:, :, None]
            tint = (1.0 - mr) + mr * col1
            if c2 is not None:
                col2 = np.array(c2, dtype=np.float32).reshape((1, 1, 3))
                mg = mask_g[:, :, None]
                tint = tint * (1.0 - mg) + col2 * mg
            arr[:, :, :3] = np.clip(arr[:, :, :3] * tint, 0.0, 1.0)
        else:
            # 普通 Cutout 染色：但若原图像素本身有高饱和固有色（如鸡的红冠），保留高饱和红冠
            r, g, b = arr[:, :, 0], arr[:, :, 1], arr[:, :, 2]
            red_excess = r - np.maximum(g, b)
            keep = np.clip((red_excess - 0.22) * 3.0, 0.0, 1.0)[:, :, None]
            # 若原图前下部含纯白象牙/獠牙高光（如大象象牙 r=1,g=1,b>=0.93），保留象牙白
            h_img, w_img = r.shape
            xs_norm = np.broadcast_to(np.linspace(0.0, 1.0, w_img)[None, :], (h_img, w_img))
            ys_norm = np.broadcast_to(np.linspace(0.0, 1.0, h_img)[:, None], (h_img, w_img))
            ivory = ((r > 0.96) & (g > 0.96) & (b > 0.88) & (r - b > 0.02) & (xs_norm > 0.68) & (ys_norm > 0.42)).astype(np.float32)[:, :, None]
            keep = np.maximum(keep, ivory * 0.92)
            tinted = arr[:, :, :3] * col1
            arr[:, :, :3] = np.clip(tinted * (1.0 - keep) + arr[:, :, :3] * keep, 0.0, 1.0)
    out = np.clip(arr * 255.0 + 0.5, 0, 255).astype(np.uint8)
    return out, mask_r


# ---------------------------------------------------------------------------
# 3. 像素级解剖分区提取与实心化（去黑描边、补齐透明像素）
# ---------------------------------------------------------------------------

def _luma(rgb):
    return 0.299 * rgb[..., 0] + 0.587 * rgb[..., 1] + 0.114 * rgb[..., 2]


def _make_swatch(pixels_rgb, base_patch=None, seed=0):
    """给定候选像素集合 (N, 3)，生成一块色泽均匀、带柔和原版笔触过渡的 20x20 实心纹理块。"""
    if len(pixels_rgb) == 0:
        pixels_rgb = np.array([[160, 150, 140]], dtype=np.float32)
    pixels_rgb = np.asarray(pixels_rgb, dtype=np.float32)
    lum = _luma(pixels_rgb)
    # 聚焦主色核心分位（25%~78%），滤除跨区杂色与深色描边残留，避免旋转部件接缝出现条带或棋盘噪点
    if len(pixels_rgb) >= 8:
        lo, hi = np.percentile(lum, [25, 78])
        keep = (lum >= lo - 1e-3) & (lum <= hi + 1e-3)
        if keep.sum() >= 3:
            pixels_rgb = pixels_rgb[keep]
            lum = lum[keep]

    med = np.median(pixels_rgb, axis=0)
    out = np.zeros((CELL_INNER, CELL_INNER, 4), dtype=np.uint8)
    for y in range(CELL_INNER):
        t_y = 1.0 - (y / max(1, CELL_INNER - 1))
        for x in range(CELL_INNER):
            # 围绕真实中位色做极柔和的上下明暗与微笔触过渡（0.95 ~ 1.04），立体明暗主要由 3D 法线光照承担
            h = ((x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)) & 0xFFFF
            jitter = ((h / 65535.0) - 0.5) * 0.025
            scale = 0.955 + 0.085 * t_y + jitter
            c = med * scale
            out[y, x, 0] = int(np.clip(round(c[0]), 0, 255))
            out[y, x, 1] = int(np.clip(round(c[1]), 0, 255))
            out[y, x, 2] = int(np.clip(round(c[2]), 0, 255))
            out[y, x, 3] = 255
    return out


def extract_species_regions(key, east_rgba, south_rgba, north_rgba, mask_r):
    """从 _east / _south / _north 原版合成图中提取 6 个部位的真实像素集合与取样框坐标。
    全部坐标相对于 _east 图内容包围盒归一化，并在 sampling.png 中画框备查。
    """
    H, W, _ = east_rgba.shape
    alpha = east_rgba[:, :, 3]
    lum = _luma(east_rgba[:, :, :3].astype(np.float32))

    # 有效内容掩码（alpha > 128）
    opaque = alpha > 140
    ys, xs = np.where(opaque)
    if len(xs) == 0:
        empty = np.array([[180, 170, 160]], dtype=np.float32)
        return {r: _make_swatch(empty, seed=i) for i, r in enumerate(REGIONS)}, (0, 0, W, H), (0.7, 0.5, 0.4), {}

    x0, x1 = int(xs.min()), int(xs.max()) + 1
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    bw = max(1, x1 - x0)
    bh = max(1, y1 - y0)

    # 测量 _south 的有效宽度
    sw_ratio = (bw / float(W)) * 0.55
    if south_rgba is not None:
        s_op = south_rgba[:, :, 3] > 140
        sys_, sxs = np.where(s_op)
        if len(sxs) > 0:
            sw_ratio = (sxs.max() - sxs.min() + 1) / float(south_rgba.shape[1])

    ew_ratio = bw / float(W)
    eh_ratio = bh / float(H)

    # 腐蚀 1 像素去掉最外圈抗锯齿黑边，确定内部有效像素
    inner_opaque = opaque.copy()
    if H > 16 and W > 16:
        inner_opaque[1:, :] &= opaque[:-1, :]
        inner_opaque[:-1, :] &= opaque[1:, :]
        inner_opaque[:, 1:] &= opaque[:, :-1]
        inner_opaque[:, :-1] &= opaque[:, 1:]
    if inner_opaque.sum() < 20:
        inner_opaque = opaque

    # 判断该动物的主体本身是否为深色物种（如黑豹、鸵鸟、企鹅、乌鸦、牦牛、麝牛、大猩猩、熊）
    all_inner_lum = lum[inner_opaque]
    median_lum = float(np.median(all_inner_lum)) if len(all_inner_lum) > 0 else 120.0
    stroke_thresh = 22.0 if median_lum < 68.0 else 42.0
    non_stroke = inner_opaque & (lum >= stroke_thresh)
    if non_stroke.sum() < 15:
        non_stroke = inner_opaque & (lum >= 12.0)
    if non_stroke.sum() < 5:
        non_stroke = inner_opaque

    # 不腐蚀 1px 几何边缘但排除黑描边的掩码（专门用于提取鸡冠、鸟喙、象牙、角尖等细小特征区）
    clean_opaque = opaque & (lum >= max(24.0, stroke_thresh - 10.0))
    if clean_opaque.sum() < 5:
        clean_opaque = opaque

    xs_grid = np.broadcast_to(np.arange(W)[None, :], (H, W))
    ys_grid = np.broadcast_to(np.arange(H)[:, None], (H, W))
    r_c = east_rgba[:, :, 0].astype(np.float32)
    g_c = east_rgba[:, :, 1].astype(np.float32)
    b_c = east_rgba[:, :, 2].astype(np.float32)

    def _mask_pixels(m_cond, fallback_box=(0.30, 0.30, 0.70, 0.70), min_cnt=3):
        if m_cond.sum() >= min_cnt:
            vy, vx = np.where(m_cond)
            return east_rgba[vy, vx, :3].astype(np.float32), (int(vx.min()), int(vy.min()), int(vx.max()) + 1, int(vy.max()) + 1)
        return _box_pixels(*fallback_box)

    def _box_pixels(fx0, fy0, fx1, fy1, allow_dark=False):
        px0 = max(x0, min(x1 - 1, int(round(x0 + fx0 * bw))))
        px1 = max(px0 + 1, min(x1, int(round(x0 + fx1 * bw))))
        py0 = max(y0, min(y1 - 1, int(round(y0 + fy0 * bh))))
        py1 = max(py0 + 1, min(y1, int(round(y0 + fy1 * bh))))
        sub_m = (inner_opaque if allow_dark else non_stroke)[py0:py1, px0:px1]
        sub_rgb = east_rgba[py0:py1, px0:px1, :3]
        if sub_m.sum() >= 4:
            return sub_rgb[sub_m].astype(np.float32), (px0, py0, px1, py1)
        # 放宽到整图 non_stroke 中离该框中心最近的像素
        cy, cx = (py0 + py1) * 0.5, (px0 + px1) * 0.5
        vy, vx = np.where(inner_opaque if allow_dark else non_stroke)
        if len(vx) == 0:
            vy, vx = np.where(opaque)
        d2 = (vx - cx) ** 2 + (vy - cy) ** 2
        pick = np.argsort(d2)[:max(8, min(36, len(d2)))]
        bx0, bx1 = int(vx[pick].min()), int(vx[pick].max()) + 1
        by0, by1 = int(vy[pick].min()), int(vy[pick].max()) + 1
        return east_rgba[vy[pick], vx[pick], :3].astype(np.float32), (bx0, by0, bx1, by1)

    # 1) coat (uv=0): 背部/躯干主色
    if key == "boomalope":
        px_coat, box_coat = _box_pixels(0.52, 0.35, 0.88, 0.68)
    elif key == "toxalope":
        px_coat, box_coat = _box_pixels(0.52, 0.35, 0.92, 0.72)
    elif key == "boomrat":
        px_coat, box_coat = _box_pixels(0.45, 0.45, 0.85, 0.85)
    elif key == "panda":
        px_coat, box_coat = _mask_pixels(inner_opaque & (lum > 165.0), (0.22, 0.20, 0.50, 0.65), min_cnt=6)
    elif key == "megasloth":
        # 大地懒棕黄躯干（排除肩前亮米白毛领 lum > 198）
        px_coat, box_coat = _mask_pixels(non_stroke & (lum >= 135.0) & (lum <= 198.0), (0.08, 0.25, 0.48, 0.68), min_cnt=8)
    elif key == "ibex":
        px_coat, box_coat = _box_pixels(0.15, 0.35, 0.88, 0.68)
    elif key == "duck":
        # 公鸭灰白背部躯干（排除白翼高光与栗胸）
        px_coat, box_coat = _mask_pixels(
            non_stroke & (xs_grid < x0 + 0.68 * bw) & (lum >= 135.0) & (lum <= 185.0),
            (0.05, 0.35, 0.65, 0.88), min_cnt=6
        )
    elif key == "turkey":
        # 火鸡深蓝黑躯干（中右段，排除左侧棕黄大扇尾）
        px_coat, box_coat = _box_pixels(0.32, 0.35, 0.82, 0.85, allow_dark=True)
    elif key == "peacock":
        # 孔雀翠绿大尾屏（左上半部）
        px_coat, box_coat = _box_pixels(0.04, 0.06, 0.48, 0.72)
    elif key == "penguin":
        px_coat, box_coat = _box_pixels(0.08, 0.25, 0.42, 0.82, allow_dark=True)
    elif key == "seaturtle":
        px_coat, box_coat = _mask_pixels(
            non_stroke & (xs_grid < x0 + 0.65 * bw) & (r_c - b_c > 35.0) & (lum >= 95.0) & (lum <= 145.0),
            (0.25, 0.20, 0.65, 0.65), min_cnt=6
        )
    else:
        px_coat, box_coat = _box_pixels(0.24, 0.20, 0.60, 0.56)

    # 2) belly (uv=1): 腹部/胸侧/下摆/折叠翼/扇尾/毛领
    if key == "penguin":
        px_belly, box_belly = _mask_pixels(non_stroke & (lum > 215.0) & (r_c - b_c < 20.0), (0.46, 0.45, 0.78, 0.88), min_cnt=6)
    elif key in ("thrumbo", "alphathrumbo"):
        px_belly, box_belly = _box_pixels(0.28, 0.72, 0.68, 0.96, allow_dark=True)
    elif key == "megasloth":
        # 大地懒标志性的肩颈米白亮毛领
        px_belly, box_belly = _mask_pixels(non_stroke & (lum > 198.0), (0.50, 0.10, 0.76, 0.65), min_cnt=6)
    elif key == "boomalope":
        # 爆炸羊深棕腹底（严禁混入背上黄囊）
        px_belly, box_belly = _box_pixels(0.15, 0.68, 0.88, 0.96)
    elif key == "toxalope":
        px_belly, box_belly = _box_pixels(0.20, 0.68, 0.82, 0.96)
    elif key == "turkey":
        # 火鸡左侧棕黄色大扇尾
        px_belly, box_belly = _box_pixels(0.02, 0.08, 0.28, 0.88)
    elif key == "peacock":
        # 孔雀浅灰蓝收拢翼
        px_belly, box_belly = _box_pixels(0.48, 0.36, 0.74, 0.88)
    elif key == "duck":
        # 公鸭侧翼亮白羽带
        px_belly, box_belly = _mask_pixels(non_stroke & (xs_grid < x0 + 0.62 * bw) & (lum > 195.0), (0.25, 0.35, 0.52, 0.65), min_cnt=5)
    elif key == "ostrich":
        if mask_r is not None:
            px_belly, box_belly = _mask_pixels(
                non_stroke & (mask_r < 0.35) & (xs_grid < x0 + 0.55 * bw),
                (0.05, 0.55, 0.38, 0.92), min_cnt=5
            )
        else:
            px_belly, box_belly = _box_pixels(0.05, 0.55, 0.38, 0.92)
    elif key == "tortoise":
        # 陆龟浅米黄壳缘裙边
        px_belly, box_belly = _box_pixels(0.18, 0.66, 0.72, 0.95)
    elif key == "seaturtle":
        # 海龟浅米黄壳缘与腹甲
        px_belly, box_belly = _mask_pixels(non_stroke & (lum > 148.0) & (r_c - b_c > 22.0), (0.25, 0.30, 0.72, 0.85), min_cnt=6)
    elif key == "alligator":
        # 鳄鱼浅灰黄肚皮
        px_belly, box_belly = _mask_pixels(non_stroke & (lum > 105.0) & (xs_grid < x0 + 0.75 * bw), (0.25, 0.68, 0.72, 0.95), min_cnt=6)
    elif key == "deer":
        # 鹿浅白肚皮与尾底
        px_belly, box_belly = _mask_pixels(clean_opaque & (lum > 185.0), (0.25, 0.62, 0.70, 0.92), min_cnt=4)
    elif key == "lynx":
        # 猞猁纯白腮毛与前胸白毛
        px_belly, box_belly = _mask_pixels(clean_opaque & (lum > 185.0), (0.72, 0.35, 0.98, 0.78), min_cnt=3)
    elif key == "husky":
        px_belly, box_belly = _mask_pixels(non_stroke & (lum > 168.0), (0.65, 0.35, 0.96, 0.85), min_cnt=6)
    elif key == "wolf_timber":
        px_belly, box_belly = _mask_pixels(non_stroke & (r_c - b_c > 18.0) & (lum > 110.0), (0.25, 0.62, 0.75, 0.94), min_cnt=6)
    elif mask_r is not None and key in ("ibex", "raccoon", "badger"):
        px_belly, box_belly = _mask_pixels(non_stroke & (mask_r < 0.45), (0.30, 0.58, 0.68, 0.88), min_cnt=6)
    else:
        px_belly, box_belly = _box_pixels(0.28, 0.56, 0.66, 0.86)

    # 3) head (uv=2): 头部 / 面部 / 吻部
    if key == "ostrich" and mask_r is not None:
        px_head, box_head = _mask_pixels(
            non_stroke & (mask_r < 0.40) & (xs_grid > x0 + 0.55 * bw) & (ys_grid < y0 + 0.55 * bh),
            (0.68, 0.04, 0.94, 0.38), min_cnt=4
        )
    elif key == "duck":
        # 公鸭标志性翠绿头
        px_head, box_head = _mask_pixels(clean_opaque & ((g_c - np.maximum(r_c, b_c)) > 14.0), (0.55, 0.04, 0.82, 0.36), min_cnt=4)
    elif key == "turkey":
        # 火鸡天蓝色头皮
        px_head, box_head = _mask_pixels(clean_opaque & ((b_c - r_c) > 15.0) & (b_c > 110.0), (0.72, 0.04, 0.98, 0.36), min_cnt=2)
    elif key == "peacock":
        # 孔雀宝蓝色头颈与前胸（右侧且 b > g）
        px_head, box_head = _mask_pixels(
            clean_opaque & (xs_grid > x0 + 0.52 * bw) & ((b_c - g_c) > 15.0),
            (0.72, 0.06, 0.98, 0.72), min_cnt=3
        )
    elif key in ("cassowary", "flamingo"):
        px_head, box_head = _box_pixels(0.62, 0.06, 0.92, 0.42)
    elif key == "muffalo":
        px_head, box_head = _box_pixels(0.72, 0.35, 0.96, 0.78)
    elif key == "megasloth":
        px_head, box_head = _box_pixels(0.76, 0.32, 0.96, 0.68)
    elif key == "panda":
        px_head, box_head = _box_pixels(0.76, 0.25, 0.96, 0.68)
    elif key == "tortoise":
        px_head, box_head = _box_pixels(0.74, 0.35, 0.98, 0.85)
    elif key == "seaturtle":
        px_head, box_head = _box_pixels(0.72, 0.25, 0.98, 0.78)
    elif key == "iguana":
        px_head, box_head = _box_pixels(0.76, 0.22, 0.96, 0.62)
    else:
        px_head, box_head = _box_pixels(0.68, 0.20, 0.94, 0.65)

    # 4) limb (uv=3): 特征深色肩带 / 栗色胸 / 鳍肢 / 背鬃
    if key == "panda":
        px_limb, box_limb = _mask_pixels(inner_opaque & (lum < 60.0), (0.52, 0.20, 0.78, 0.92), min_cnt=6)
    elif key == "duck":
        # 公鸭栗棕色前胸
        px_limb, box_limb = _mask_pixels(
            clean_opaque & (xs_grid > x0 + 0.68 * bw) & (ys_grid > y0 + 0.32 * bh) & ((r_c - b_c) > 22.0) & (r_c < 155.0),
            (0.74, 0.36, 0.98, 0.88), min_cnt=4
        )
    elif key == "wildboar":
        # 野猪深棕红背鬃
        px_limb, box_limb = _mask_pixels(
            clean_opaque & (ys_grid < y0 + 0.38 * bh) & ((r_c - b_c) > 30.0),
            (0.25, 0.04, 0.75, 0.32), min_cnt=4
        )
    elif key == "turkey":
        # 火鸡尾屏内圈深暖棕羽色
        px_limb, box_limb = _mask_pixels(
            clean_opaque & (xs_grid < x0 + 0.42 * bw) & ((r_c - b_c) > 15.0) & (lum < 105.0),
            (0.14, 0.22, 0.38, 0.68), min_cnt=4
        )
    elif key == "peacock":
        # 孔雀折翼前下缘暖棕羽斑
        px_limb, box_limb = _mask_pixels(
            clean_opaque & (xs_grid < x0 + 0.55 * bw) & (ys_grid > y0 + 0.55 * bh) & ((r_c - b_c) > 18.0),
            (0.20, 0.62, 0.48, 0.92), min_cnt=3
        )
    elif key == "macaw":
        # 金刚鹦鹉蔚蓝飞羽
        px_limb, box_limb = _mask_pixels(clean_opaque & ((b_c - r_c) > 15.0), (0.05, 0.35, 0.45, 0.85), min_cnt=3)
    elif key in ("fox_red", "raccoon", "badger", "wolverine"):
        px_limb, box_limb = _mask_pixels(inner_opaque & (lum < 88.0) & (lum >= 18.0), (0.52, 0.68, 0.84, 0.96), min_cnt=6)
    elif key in ("thrumbo", "alphathrumbo"):
        px_limb, box_limb = _box_pixels(0.25, 0.75, 0.72, 0.98, allow_dark=True)
    else:
        px_limb, box_limb = _box_pixels(0.22, 0.72, 0.78, 0.96)

    # 5) detail (uv=4): 物种标志性特征区（囊泡 / 角 / 象牙 / 鸡冠 / 鸟喙 / 鬃毛 / 斑纹）
    px_detail, box_detail = None, None
    if mask_r is not None and key in ("boomalope", "boomrat"):
        m_sac = non_stroke & (mask_r < 0.42)
        if m_sac.sum() >= 6:
            thr = np.percentile(lum[m_sac], 45)
            px_detail, box_detail = _mask_pixels(m_sac & (lum >= thr), (0.15, 0.08, 0.65, 0.55), min_cnt=4)
        else:
            px_detail, box_detail = _mask_pixels(m_sac, (0.15, 0.08, 0.65, 0.55), min_cnt=4)
    elif key in ("toxalope", "wasterat"):
        px_detail, box_detail = _mask_pixels(
            clean_opaque & (ys_grid < y0 + 0.55 * bh) & ((g_c - r_c) > 10.0) & (g_c > 108.0),
            (0.15, 0.05, 0.62, 0.45), min_cnt=3
        )
    elif key in ("chicken", "turkey", "cassowary"):
        # 鸡冠 / 火鸡肉垂 / 鹤鸵红垂：r - g > 62 排除黄羽和棕尾，精准锁定鲜红冠垂
        px_detail, box_detail = _mask_pixels(
            clean_opaque & ((r_c - g_c) > 62.0) & (r_c > 115.0),
            (0.68, 0.02, 0.98, 0.38), min_cnt=2
        )
    elif key in ("duck", "goose", "swan", "penguin"):
        # 水禽亮黄/橙红扁喙：右上方高饱和暖色像素
        px_detail, box_detail = _mask_pixels(
            clean_opaque & (xs_grid > x0 + 0.65 * bw) & ((r_c - b_c) > 55.0) & (r_c > 165.0),
            (0.78, 0.04, 0.99, 0.38), min_cnt=2
        )
    elif key == "macaw":
        # 金刚鹦鹉米白面颊与弯喙
        px_detail, box_detail = _mask_pixels(clean_opaque & (lum > 140.0), (0.72, 0.08, 0.98, 0.42), min_cnt=3)
    elif key == "peacock":
        # 孔雀深青蓝冠羽与尾屏眼斑
        px_detail, box_detail = _mask_pixels(clean_opaque & (b_c > r_c + 20.0) & (lum < 95.0), (0.50, 0.04, 0.78, 0.35), min_cnt=3)
    elif key == "ostrich":
        # 鸵鸟前端橙黄扁喙（排除粉红长颈，取最右侧高黄橙饱和度像素）
        m_beak = clean_opaque & (xs_grid > x0 + 0.80 * bw) & (ys_grid < y0 + 0.35 * bh) & ((r_c - b_c) > 75.0)
        if m_beak.sum() >= 1:
            by, bx = np.where(m_beak)
            score = (r_c[by, bx] - b_c[by, bx]) + (g_c[by, bx] - b_c[by, bx])
            top_k = np.argsort(-score)[:min(4, len(score))]
            vy, vx = by[top_k], bx[top_k]
            px_detail = east_rgba[vy, vx, :3].astype(np.float32)
            box_detail = (int(vx.min()), int(vy.min()), int(vx.max()) + 1, int(vy.max()) + 1)
    elif key == "flamingo":
        sat = np.maximum(np.maximum(r_c, g_c), b_c) - np.minimum(np.minimum(r_c, g_c), b_c)
        px_detail, box_detail = _mask_pixels(clean_opaque & (sat > 32.0), (0.68, 0.04, 0.96, 0.42), min_cnt=3)
    elif key in ("elephant", "mastodon", "walrus", "wildboar", "cat_scimitar", "scimitarcat", "rhinoceros", "rhino"):
        # 象牙 / 剑齿 / 獠牙 / 犀牛角：直接取前部最亮的 Top-6 像素簇
        m_front = clean_opaque & (xs_grid > x0 + 0.68 * bw)
        if m_front.sum() >= 3:
            fy, fx = np.where(m_front)
            flum = lum[fy, fx]
            top_k = np.argsort(-flum)[:min(6, len(flum))]
            vy, vx = fy[top_k], fx[top_k]
            px_detail = east_rgba[vy, vx, :3].astype(np.float32)
            box_detail = (int(vx.min()), int(vy.min()), int(vx.max()) + 1, int(vy.max()) + 1)
    elif key == "alligator":
        # 短吻鳄前颌亮白利齿
        px_detail, box_detail = _mask_pixels(
            clean_opaque & (xs_grid > x0 + 0.72 * bw) & (lum > 145.0),
            (0.75, 0.55, 0.99, 0.92), min_cnt=2
        )
    elif key in ("deer", "moose", "elk", "caribou", "gazelle"):
        # 鹿角 / 羚羊角：贴图顶部浅米白/浅棕角像素
        m_top = clean_opaque & (ys_grid < y0 + 0.34 * bh) & (xs_grid > x0 + 0.42 * bw)
        if m_top.sum() >= 4:
            thr = np.percentile(lum[m_top], 70)
            px_detail, box_detail = _mask_pixels(m_top & (lum >= thr), (0.55, 0.02, 0.96, 0.32), min_cnt=3)
    elif key == "ibex":
        # 羱羊顶部弯角
        px_detail, box_detail = _box_pixels(0.28, 0.02, 0.85, 0.30)
    elif key == "husky":
        # 哈士奇标志性高饱和冰蓝眼瞳
        px_detail, box_detail = _mask_pixels(
            clean_opaque & (xs_grid > x0 + 0.65 * bw) & (ys_grid < y0 + 0.48 * bh) & ((b_c - r_c) > 45.0) & (b_c > 145.0),
            (0.75, 0.18, 0.90, 0.38), min_cnt=1
        )
    elif key == "ostrich":
        # 鸵鸟最前端喙尖的暖橙黄色像素（取 x > x0 + 0.86*bw 中 G/B 比值最高的橙黄像素）
        m_beak = clean_opaque & (xs_grid > x0 + 0.84 * bw) & (ys_grid < y0 + 0.36 * bh) & ((r_c - b_c) > 48.0)
        if m_beak.sum() >= 1:
            by, bx = np.where(m_beak)
            score = (r_c[by, bx] - b_c[by, bx]) + (g_c[by, bx] - b_c[by, bx])
            top_k = np.argsort(-score)[:min(4, len(score))]
            vy, vx = by[top_k], bx[top_k]
            px_detail = east_rgba[vy, vx, :3].astype(np.float32)
            box_detail = (int(vx.min()), int(vy.min()), int(vx.max()) + 1, int(vy.max()) + 1)
    elif key in ("bison", "yak", "muskox", "cow"):
        # 野牛 / 牦牛 / 麝牛 / 家牛头侧浅黄褐/米白弯角（排除红棕毛色 r - g >= 20）
        m_horn = clean_opaque & (xs_grid > x0 + 0.56 * bw) & (xs_grid < x0 + 0.88 * bw) & (ys_grid > y0 + 0.12 * bh) & (ys_grid < y0 + 0.52 * bh) & ((r_c - g_c) < 19.0) & (g_c > 72.0)
        px_detail, box_detail = _mask_pixels(m_horn, (0.62, 0.22, 0.80, 0.45), min_cnt=3)
    elif key == "tortoise":
        px_detail, box_detail = _box_pixels(0.05, 0.25, 0.35, 0.60)
    elif key == "seaturtle":
        px_detail, box_detail = _mask_pixels(clean_opaque & (r_c - b_c > 42.0) & (lum < 95.0), (0.04, 0.08, 0.32, 0.60), min_cnt=4)
    elif key in ("thrumbo", "alphathrumbo"):
        px_detail, box_detail = _mask_pixels(
            clean_opaque & (ys_grid < y0 + 0.24 * bh) & (lum > 175.0),
            (0.45, 0.02, 0.90, 0.24), min_cnt=3
        )
    elif key in ("fox_red", "fox_arctic", "fox_fennec"):
        px_detail, box_detail = _mask_pixels(
            clean_opaque & (lum > np.percentile(lum[non_stroke], 85)),
            (0.02, 0.55, 0.30, 0.95), min_cnt=4
        )
    elif key in ("horse", "donkey", "tiger"):
        px_detail, box_detail = _mask_pixels(
            inner_opaque & (lum < np.percentile(lum[non_stroke], 25)) & (lum >= 20.0),
            (0.45, 0.05, 0.82, 0.40), min_cnt=5
        )

    if px_detail is None:
        # 通用特征区：取与主毛色 (coat) 色差最大的前上部/背部区域（如牛角、鹿角、马鬃、狐白尾尖）
        coat_mean = px_coat.mean(axis=0)
        diff = np.linalg.norm(east_rgba[:, :, :3].astype(np.float32) - coat_mean[None, None, :], axis=-1)
        cand_m = non_stroke & (diff > np.percentile(diff[non_stroke], 72))
        if cand_m.sum() >= 6:
            vy, vx = np.where(cand_m)
            px_detail = east_rgba[vy, vx, :3].astype(np.float32)
            box_detail = (int(vx.min()), int(vy.min()), int(vx.max()) + 1, int(vy.max()) + 1)
        else:
            px_detail, box_detail = _box_pixels(0.55, 0.08, 0.90, 0.36)

    # 6) eye (uv=5): 前部眼瞳/鼻头深色像素
    xs_grid = np.broadcast_to(np.arange(W)[None, :], (H, W))
    m_eye = inner_opaque & (xs_grid > x0 + 0.58 * bw) & (lum < 62.0)
    if m_eye.sum() >= 2:
        vy, vx = np.where(m_eye)
        px_eye = east_rgba[vy, vx, :3].astype(np.float32)
        # 稍微压暗确保眼瞳清晰有神
        px_eye = np.clip(px_eye * 0.55, 14.0, 58.0)
        box_eye = (int(vx.min()), int(vy.min()), int(vx.max()) + 1, int(vy.max()) + 1)
    else:
        px_eye = np.array([[26.0, 24.0, 28.0], [36.0, 32.0, 36.0]], dtype=np.float32)
        box_eye = (int(x0 + 0.82 * bw), int(y0 + 0.35 * bh), int(x0 + 0.88 * bw) + 1, int(y0 + 0.42 * bh) + 1)

    seed_base = sum(ord(c) * (i + 1) for i, c in enumerate(key))
    swatches = {
        "coat": _make_swatch(px_coat, seed=seed_base + 1),
        "belly": _make_swatch(px_belly, seed=seed_base + 2),
        "head": _make_swatch(px_head, seed=seed_base + 3),
        "limb": _make_swatch(px_limb, seed=seed_base + 4),
        "detail": _make_swatch(px_detail, seed=seed_base + 5),
        "eye": _make_swatch(px_eye, seed=seed_base + 6),
    }
    boxes = {
        "coat": box_coat,
        "belly": box_belly,
        "head": box_head,
        "limb": box_limb,
        "detail": box_detail,
        "eye": box_eye,
    }
    return swatches, (x0, y0, x1, y1), (round(ew_ratio, 3), round(eh_ratio, 3), round(sw_ratio, 3)), boxes


# ---------------------------------------------------------------------------
# 4. 打包全物种图集 + 生成 AnimalUVGen.cs / animal_uv.json / sampling.png
# ---------------------------------------------------------------------------

def pad_cell(cell_rgba):
    """四周扩展 PAD 像素（复制边缘像素），彻底消除双线性插值串色。"""
    return np.pad(cell_rgba, ((PAD, PAD), (PAD, PAD), (0, 0)), mode="edge")


def main():
    kinds = load_xml_kinds()
    tex_idx = build_tex_index()

    # 补充貼图文件夹存在但 PawnKindDef 名略异的别名，确保全覆盖
    extra_aliases = {
        "wolf_timber": "wolf_timber",
        "wolf_arctic": "wolf_arctic",
        "greatwolf": "wolf_great",
        "bear": "bear_grizzly",
        "rhino": "rhinoceros",
        "labrador": "labradorretriever",
        "scimitarcat": "cat_scimitar",
        "dryad": "dryad_basic",
        "beaver": "alphabeaver",
    }
    for alias, target in extra_aliases.items():
        if alias not in kinds and target in kinds:
            kinds[alias] = dict(kinds[target], defName=alias)

    species_keys = sorted(kinds.keys())
    print(f"Processing {len(species_keys)} animal species...")

    # 每行排 6 个物种，每个物种占 6 个 24x24 格 = 144x24
    sp_per_row = 7
    strip_w = len(REGIONS) * CELL_STEP  # 6 * 24 = 144
    atlas_w = 1024
    rows = (len(species_keys) + sp_per_row - 1) // sp_per_row
    atlas_h = 512
    while rows * CELL_STEP > atlas_h:
        atlas_h *= 2

    atlas = np.zeros((atlas_h, atlas_w, 4), dtype=np.uint8)
    uv_map = {}
    metrics_map = {}
    sample_records = []

    for idx, key in enumerate(species_keys):
        info = kinds[key]
        vpaths = resolve_view_paths(info["texPath"], tex_idx)
        if "east" not in vpaths:
            print(f"  [WARN] missing east tex for {key} ({info['texPath']})")
            continue
        east_rgba, mask_r = composite_vanilla(
            vpaths["east"], vpaths.get("eastm"), info["c1"], info["c2"], info["shader"]
        )
        south_rgba = None
        if "south" in vpaths:
            south_rgba, _ = composite_vanilla(
                vpaths["south"], vpaths.get("southm"), info["c1"], info["c2"], info["shader"]
            )
        north_rgba = None
        if "north" in vpaths:
            north_rgba, _ = composite_vanilla(
                vpaths["north"], vpaths.get("northm"), info["c1"], info["c2"], info["shader"]
            )

        swatches, bbox, ratios, boxes = extract_species_regions(
            key, east_rgba, south_rgba, north_rgba, mask_r
        )
        metrics_map[key] = dict(
            drawSize=round(float(info["drawSize"]), 3),
            eastW=ratios[0],
            eastH=ratios[1],
            southW=ratios[2],
            texPath=info["texPath"],
        )

        r_idx, c_idx = divmod(idx, sp_per_row)
        base_x = c_idx * strip_w
        base_y = r_idx * CELL_STEP

        for ri, reg_name in enumerate(REGIONS):
            padded = pad_cell(swatches[reg_name])
            ox = base_x + ri * CELL_STEP
            oy = base_y
            atlas[oy:oy + CELL_STEP, ox:ox + CELL_STEP] = padded

            # UV 坐标指向去掉 PAD 后的内部安全区 [ox+PAD .. ox+PAD+CELL_INNER]
            ix0 = ox + PAD
            iy0 = oy + PAD
            ix1 = ix0 + CELL_INNER
            iy1 = iy0 + CELL_INNER
            u0 = ix0 / float(atlas_w)
            u1 = ix1 / float(atlas_w)
            # Unity 纹理 v=0 在底边：v = 1 - y/H
            v0 = 1.0 - (iy1 / float(atlas_h))
            v1 = 1.0 - (iy0 / float(atlas_h))
            uv_map[f"{key}.{reg_name}"] = (round(u0, 5), round(v0, 5), round(u1, 5), round(v1, 5))

        sample_records.append((key, east_rgba, south_rgba, swatches, boxes, info["drawSize"], ratios))

    os.makedirs(os.path.dirname(ATLAS_OUT), exist_ok=True)
    os.makedirs(os.path.dirname(VIEW_ATLAS), exist_ok=True)
    atlas_im = Image.fromarray(atlas, "RGBA")
    atlas_im.save(ATLAS_OUT)
    shutil.copyfile(ATLAS_OUT, VIEW_ATLAS)
    print(f"Saved {ATLAS_OUT} ({atlas_w}x{atlas_h}) with {len(uv_map)} regions.")

    # 生成 viewer/animal_uv.json
    json_data = {
        "atlasW": atlas_w,
        "atlasH": atlas_h,
        "regions": REGIONS,
        "uv": uv_map,
        "species": metrics_map,
    }
    with open(JSON_OUT, "w", encoding="utf-8") as f:
        json.dump(json_data, f, ensure_ascii=False, indent=2)
    print(f"Saved {JSON_OUT}")

    # 生成 Source/RimFirstPerson3D/AnimalUVGen.cs
    cs_lines = [
        "// AI生成",
        "// 自动生成 —— 由 scripts/animal_atlas.py 产出，请勿手改。",
        "// 键 = \"<物种名>.<部位>\"（coat/belly/head/limb/detail/eye），值 = 图集 UV (u0, v0, u1, v1)。",
        "// v 口径：Unity 纹理 v=0 在图像底边，故 v = 1 - y/H。",
        "using System.Collections.Generic;",
        "using UnityEngine;",
        "",
        "namespace RimFirstPerson3D",
        "{",
        "    public static class AnimalUVGen",
        "    {",
        "        public const string TexPath = \"FPS3D/AnimalAtlas\";",
        f"        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>({len(uv_map)})",
        "        {",
    ]
    for k, (u0, v0, u1, v1) in sorted(uv_map.items()):
        cs_lines.append(f"            {{ \"{k}\", new Vector4({u0:.5f}f, {v0:.5f}f, {u1:.5f}f, {v1:.5f}f) }},")
    cs_lines.append("        };")
    cs_lines.append("")
    cs_lines.append("        // 原版贴图实测剪影比例：(eastW, eastH, southW)")
    cs_lines.append(f"        public static readonly Dictionary<string, Vector3> Proportions = new Dictionary<string, Vector3>({len(metrics_map)})")
    cs_lines.append("        {")
    for k, m in sorted(metrics_map.items()):
        cs_lines.append(
            f"            {{ \"{k}\", new Vector3({m['eastW']:.3f}f, {m['eastH']:.3f}f, {m['southW']:.3f}f) }},"
        )
    cs_lines.append("        };")
    cs_lines.append("    }")
    cs_lines.append("}")
    with open(CS_OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(cs_lines) + "\n")
    print(f"Saved {CS_OUT}")

    # 生成 docs/animals/sampling.png（挑选重点物种展示原版 _east + _south + 取样框 + 6 个部位色块）
    os.makedirs(os.path.dirname(SAMPLE_DOC), exist_ok=True)
    showcase_keys = [
        "thrumbo", "alphathrumbo", "muffalo", "bison", "yak", "muskox",
        "boomalope", "toxalope", "dromedary", "alpaca", "elephant", "mastodon",
        "rhino", "hippo", "cow", "horse", "donkey", "deer",
        "elk", "caribou", "moose", "ibex", "goat", "gazelle",
        "sheep", "pig", "wildboar", "bear_grizzly", "panda", "megasloth",
        "warg", "wolf_timber", "fox_red", "husky", "labrador", "yorkshireterrier",
        "cougar", "lynx", "tiger", "cat", "hare", "squirrel",
        "beaver", "raccoon", "capybara", "boomrat", "rat", "monkey",
        "gorilla", "chicken", "duck", "goose", "turkey", "ostrich",
        "cassowary", "flamingo", "penguin", "peacock", "tortoise", "seaturtle",
        "armadillo", "iguana", "alligator", "bullfrog", "cobra", "megaspider",
        "spelopede", "megascarab", "walrus", "seal", "hermitcrab", "lavasnail",
    ]
    rec_by_key = {r[0]: r for r in sample_records}
    picked = [rec_by_key[k] for k in showcase_keys if k in rec_by_key]

    cols = 4
    rows_s = (len(picked) + cols - 1) // cols
    cw, ch = 360, 118
    sheet = Image.new("RGBA", (cols * cw, rows_s * ch), (28, 32, 40, 255))
    draw = ImageDraw.Draw(sheet)
    reg_cols = {
        "coat": (100, 220, 120, 255),
        "belly": (90, 190, 255, 255),
        "head": (255, 210, 90, 255),
        "limb": (210, 140, 255, 255),
        "detail": (255, 95, 120, 255),
        "eye": (255, 255, 255, 255),
    }

    for i, (key, e_rgba, s_rgba, sws, bxs, ds, rats) in enumerate(picked):
        r, c = divmod(i, cols)
        x0, y0 = c * cw, r * ch
        draw.rectangle([x0 + 2, y0 + 2, x0 + cw - 3, y0 + ch - 3], outline=(55, 62, 76, 255))
        draw.text(
            (x0 + 6, y0 + 4),
            f"{key} ds={ds} e={rats[0]:.2f}x{rats[1]:.2f} s={rats[2]:.2f}",
            fill=(210, 225, 245, 255),
        )
        # 画 _east + 取样框
        e_im = Image.fromarray(e_rgba, "RGBA")
        e_draw = ImageDraw.Draw(e_im)
        for rn, (bx0, by0, bx1, by1) in bxs.items():
            e_draw.rectangle([bx0, by0, bx1 - 1, by1 - 1], outline=reg_cols[rn])
        e_im.thumbnail((86, 86), Image.Resampling.NEAREST)
        sheet.alpha_composite(e_im, (x0 + 8 + (86 - e_im.width) // 2, y0 + 24 + (86 - e_im.height) // 2))

        # 画 _south
        if s_rgba is not None:
            s_im = Image.fromarray(s_rgba, "RGBA")
            s_im.thumbnail((86, 86), Image.Resampling.NEAREST)
            sheet.alpha_composite(s_im, (x0 + 98 + (86 - s_im.width) // 2, y0 + 24 + (86 - s_im.height) // 2))

        # 画 6 个部位 24x24 色板
        for ri, rn in enumerate(REGIONS):
            rx = x0 + 190 + (ri % 3) * 54
            ry = y0 + 26 + (ri // 3) * 44
            sw_im = Image.fromarray(sws[rn], "RGBA").resize((26, 26), Image.Resampling.NEAREST)
            sheet.alpha_composite(sw_im, (rx, ry))
            draw.rectangle([rx, ry, rx + 26, ry + 26], outline=reg_cols[rn])
            draw.text((rx + 29, ry + 6), rn[:3], fill=reg_cols[rn])

    sheet.convert("RGB").save(SAMPLE_DOC)
    print(f"Saved {SAMPLE_DOC} ({sheet.size})")


if __name__ == "__main__":
    main()
