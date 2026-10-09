# AI生成
# -*- coding: utf-8 -*-
"""家具三视图取样规范（单一真源）+ 图集打包 + UV 覆盖可视化。

【口径】
- 坐标一律是**源图像素坐标**，原点在图像左上角，半开区间 [x0,x1)×[y0,y1)。
  切点必须落在像素行/列边界上（不许 0.5 像素）。
- 每个朝向图 = 「俯视区 top」+「立面区 front」，二者首尾相接、不重叠。
  另可加部件专用区（如 bed.pillow / endtable.drawer）。
- 取样区**不得包含原版黑色描边**（本 mod 的描边由反绕外壳网格画）。
- 运行时：顶面取「当前朝向那张图的 top」，朝向相机的立面取「当前朝向那张图的
  front」，其余三面取各自朝向图的 front（东/西共用 east，水平镜像）。

产物：
    1.6/Textures/FPS3D/FurnitureAtlas.png   图集（只含原版像素，黑描边已剥离）
    Source/RimFirstPerson3D/FurnitureUVGen.cs  图集 UV 常量表（生成，勿手改）
    docs/furniture/uv/<item>_<view>.png     放大图上画绿框 = 该图被取样的区域
"""
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

TEX = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
MOD = r"D:\rim mod\FirstPerson3D"
ATLAS_OUT = os.path.join(MOD, "1.6", "Textures", "FPS3D", "FurnitureAtlas.png")
CS_OUT = os.path.join(MOD, "Source", "RimFirstPerson3D", "FurnitureUVGen.cs")
UV_OUT = os.path.join(MOD, "docs", "furniture", "uv")
# 检查器页面（viewer/ 是 http://127.0.0.1:8899 的根）共用的产物：
# 图集副本 + UV 表 JSON。页面只读这两样，不再自建第二份口径。
VIEW_DIR = os.path.join(MOD, "viewer")
VIEW_TEX = os.path.join(VIEW_DIR, "furn_atlas.png")
VIEW_JSON = os.path.join(VIEW_DIR, "furn_uv.json")

F = "Core/textures/things/building/furniture"
FB = F + "/bed"
BK = F + "/bookcase"

PAD = 2          # 图集内每块取样的边缘复制宽度（防双线性/ mip 串色）
STROKE_LUMA = 40 # 黑描边判定阈值（0-255）

# ---------------------------------------------------------------------------
# 取样表：item -> view -> region -> (x0, y0, x1, y1)
#   top   = 俯视区（模型水平面）
#   front = 立面区（模型竖直面）
#   其余  = 部件专用区
# ---------------------------------------------------------------------------
SPEC = {
    # 坐标 = 源图像素，左上原点，[x0,x1)x[y0,y1)。top/front 按内容包围盒**密铺**，
    # 交界线 = 目视判读的"俯视段/立面段"分界（见 docs/furniture/uv/*.png 绿框）。
    "bed": {
        "south": {"path": f"{FB}/bed_south.png",
                  "top": (33, 0, 95, 110), "front": (33, 110, 95, 125)},
        "north": {"path": f"{FB}/bed_north.png",
                  "top": (33, 0, 95, 110), "front": (33, 110, 95, 125)},
        "east": {"path": f"{FB}/bed_east.png",
                 "top": (10, 28, 120, 88), "front": (10, 88, 120, 95)},
    },
    "stool": {
        "north": {"path": f"{F}/stool_north.png",
                  "top": (10, 20, 53, 45), "front": (12, 45, 17, 62),
                  "discard": [(17, 45, 53, 64)]},
        "east": {"path": f"{F}/stool_east.png",
                 "top": (17, 11, 45, 41), "front": (19, 41, 24, 60)},
    },
    "endtable": {
        "south": {"path": f"{F}/endtable_south.png",
                  "top": (10, 5, 53, 28), "front": (20, 33, 36, 39),
                  "discard": [(10, 28, 53, 33), (10, 39, 53, 40),
                              (10, 33, 20, 39), (36, 33, 53, 40),
                              (10, 40, 53, 64)]},
        "north": {"path": f"{F}/endtable_north.png",
                  "top": (10, 5, 53, 28), "front": (20, 33, 36, 39),
                  "discard": [(10, 28, 53, 33), (10, 39, 53, 40),
                              (10, 33, 20, 39), (36, 33, 53, 40),
                              (10, 40, 53, 64)]},
        # 东视图的下半带是"腿+轮廓"线稿带（笔画占 37%），不配当材质：
        # 整带声明丢弃，侧立面改取南视图的 front（同为该柜体的木面）。
        "east": {"path": f"{F}/endtable_east.png",
                 "top": (17, 0, 47, 28), "discard": [(17, 28, 47, 64)]},
    },
    "dresser": {
        "south": {"path": f"{F}/dresser_south.png",
                  "top": (0, 0, 128, 32), "front": (0, 32, 128, 64)},
        "north": {"path": f"{F}/dresser_north.png",
                  "top": (0, 0, 128, 32), "front": (0, 32, 128, 64)},
        "east": {"path": f"{F}/dresser_east.png",
                 "top": (0, 0, 64, 92), "front": (0, 92, 64, 128)},
    },
    "shelf": {
        "south": {"path": f"{F}/shelf_south.png",
                  "top": (43, 28, 213, 78), "front": (43, 78, 213, 93),
                  "discard": [(43, 93, 213, 99)]},
        "north": {"path": f"{F}/shelf_north.png",
                  "top": (43, 34, 213, 78), "front": (43, 78, 213, 93),
                  "discard": [(43, 28, 213, 34), (43, 93, 213, 99)]},
        "east": {"path": f"{F}/shelf_east.png",
                 "top": (33, 37, 88, 205), "front": (33, 205, 88, 218),
                 "discard": [(88, 37, 95, 218)]},
    },
}

VIEWS_ORDER = ("south", "east", "north")


def font(size=12):
    for p in (r"C:\Windows\Fonts\cons.ttf", r"C:\Windows\Fonts\arial.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except Exception:
            return ImageFont.load_default()
    return ImageFont.load_default()


def black_mask(src):
    """原版黑色笔画（轮廓 + 内线）：不透明且 luma < 阈值。一律不进材质。"""
    a = src[:, :, 3]
    lum = src[:, :, :3].astype(np.int32).mean(axis=2)
    return (a >= 128) & (lum < STROKE_LUMA)


def stroke_mask(src):
    """与透明区相邻的黑色笔画 = 剪影描边（仅用于报告，判"框压没压在轮廓上"）。"""
    """黑色描边 := 与透明区四邻相连的黑像素（luma<STROKE_LUMA），向内扩 1 像素。
    被不透明内容包围的黑像素是**线稿**，不算描边。"""
    a = src[:, :, 3]
    lum = src[:, :, :3].astype(np.int32).mean(axis=2)
    trans = a < 128
    black = lum < STROKE_LUMA
    s = black & dilate(trans)
    return dilate(s)


def dilate(m):
    """4 邻域膨胀一格。"""
    o = m.copy()
    o[1:, :] |= m[:-1, :]
    o[:-1, :] |= m[1:, :]
    o[:, 1:] |= m[:, :-1]
    o[:, :-1] |= m[:, 1:]
    return o


def strip_stroke(block, mask):
    """把描边像素用邻域均值迭代补绘掉（取值只来自非描边像素）。
    目的：材质不含原版黑描边，但**每个像素仍来自原图**。"""
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
        fill = m & (cnt[..., 0] > 0)
        if not fill.any():
            break
        out[fill] = acc[fill] / cnt[fill]
        m &= ~fill
    if m.any():                       # 整块都是描边时邻域取不到值 -> 用本块非描边均值兜底
        keep = ~mask
        base = out[keep].mean(axis=0) if keep.any() else np.array([128., 128., 128., 255.])
        out[m] = base
    return np.clip(out + 0.5, 0, 255).astype(np.uint8)


def load(path):
    im = Image.open(os.path.join(TEX, path.replace("/", os.sep))).convert("RGBA")
    return np.asarray(im)


def rects_of(item):
    """-> [(view, region, (x0,y0,x1,y1))]  不含 discard（丢弃带不进图集）"""
    out = []
    for view, d in SPEC[item].items():
        for k, r in d.items():
            if k in ("path", "discard"):
                continue
            out.append((view, k, tuple(r)))
    return out


def pack(items):
    """把取样区打包进图集。返回 (atlas ndarray, blocks)
    blocks = [(key, view, src_path, src_rect, atlas_rect)]  atlas_rect 含 PAD 边缘复制。"""
    cells = []
    for it in items:
        for view, region, (x0, y0, x1, y1) in rects_of(it):
            cells.append((f"{it}.{view}.{region}", SPEC[it][view]["path"],
                          (x0, y0, x1, y1)))
    cells.sort(key=lambda c: -(c[2][2] - c[2][0]) * (c[2][3] - c[2][1]))

    W = 256
    col, row_h, y = 0, 0, 0
    placed = []
    for key, path, (x0, y0, x1, y1) in cells:
        w, h = x1 - x0, y1 - y0
        aw, ah = w + 2 * PAD, h + 2 * PAD
        if col + aw > W:
            col = 0
            y += row_h
            row_h = 0
        placed.append((key, path, (x0, y0, x1, y1), (col, y, aw, ah)))
        row_h = max(row_h, ah)
        col += aw
    H = y + row_h
    H = ((H + 63) // 64) * 64
    atlas = np.zeros((H, W, 4), np.uint8)
    stats = []
    for key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        src = load(path)
        sub = src[y0:y1, x0:x1].copy()
        # 透明像素也要补实：模型的面是一整块，留 alpha=0 会在 Cutout 下穿洞、
        # 在不透明材质下直接渲成黑。取值仍来自本区原图像素。
        holes = sub[:, :, 3] < 128
        if holes.any():
            sub = strip_stroke(sub, holes)
        mask = black_mask(src)[y0:y1, x0:x1] | holes
        n_stroke = int(mask.sum())
        core = strip_stroke(sub, mask) if n_stroke else sub
        core[:, :, 3] = 255
        stats.append((key, x1 - x0, y1 - y0, n_stroke))
        # 边缘复制（clamp）而不是留透明，否则双线性会采到邻块
        big = np.zeros((ah, aw, 4), np.uint8)
        big[PAD:PAD + (y1 - y0), PAD:PAD + (x1 - x0)] = core
        big[:PAD, PAD:PAD + (x1 - x0)] = core[0:1]
        big[ah - PAD:, PAD:PAD + (x1 - x0)] = core[-1:]
        big[:, :PAD] = big[:, PAD:PAD + 1]
        big[:, aw - PAD:] = big[:, aw - PAD - 1:aw - PAD]
        atlas[ay:ay + ah, ax:ax + aw] = big
    return atlas, placed, (W, H), stats


def emit_cs(placed, atlas_size, path):
    W, H = atlas_size
    lines = [
        "// 自动生成 —— 由 scripts/furn_atlas.py 产出，请勿手改。",
        "// 键 = \"<原型>.<朝向>.<区段>\"，值 = 图集 UV (u0, v0, u1, v1)。",
        "// v 口径：Unity 纹理 v=0 在图像**底边**，故 v = 1 - y/H。",
        "using System.Collections.Generic;",
        "using UnityEngine;",
        "",
        "namespace RimFirstPerson3D",
        "{",
        "    public static class FurnitureUVGen",
        "    {",
        "        public const string TexPath = \"FPS3D/FurnitureAtlas\";",
        "        public static readonly Dictionary<string, Vector4> R = "
        "new Dictionary<string, Vector4>(" + str(len(placed)) + ")",
        "        {",
    ]
    for key, src, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        u0 = (ax + PAD) / float(W)
        u1 = (ax + aw - PAD) / float(W)
        v1 = 1.0 - (ay + PAD) / float(H)
        v0 = 1.0 - (ay + ah - PAD) / float(H)
        lines.append("            {{ \"{k}\", new Vector4({u0:.5f}f, {v0:.5f}f, {u1:.5f}f, {v1:.5f}f) }},"
                     .format(k=key, u0=u0, v0=v0, u1=u1, v1=v1))
    lines += [
        "        };",
        "",
        "        /// <summary>取样区在世界里的纵横比（宽/高，源图像素比），用于侧面平铺周期。</summary>",
        "        public static readonly Dictionary<string, Vector2> SrcSize =",
        "            new Dictionary<string, Vector2>(" + str(len(placed)) + ")",
        "        {",
    ]
    for key, src, (x0, y0, x1, y1), a in placed:
        lines.append("            {{ \"{k}\", new Vector2({w}f, {h}f) }},"
                     .format(k=key, w=x1 - x0, h=y1 - y0))
    lines += ["        };", "    }", "}", ""]
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))


def overlay(items, placed):
    """每 (item,view) 出一张放大图：绿框=被取样的区，红=黑描边像素。"""
    os.makedirs(UV_OUT, exist_ok=True)
    done = []
    for it in items:
        for view in VIEWS_ORDER:
            if view not in SPEC[it]:
                continue
            path = SPEC[it][view]["path"]
            src = load(path)
            h, w = src.shape[:2]
            scale = max(2, min(10, 700 // max(w, h)))
            big = Image.fromarray(src, "RGBA").resize((w * scale, h * scale), Image.NEAREST)
            d = ImageDraw.Draw(big)
            f = font(11)
            for r in SPEC[it][view].get("discard", []):
                d.rectangle([r[0] * scale, r[1] * scale, (r[2] - 1) * scale, (r[3] - 1) * scale],
                            outline=(255, 60, 60, 255), width=2)
                d.text((r[0] * scale + 3, r[3] * scale - 16), "discard",
                       fill=(255, 120, 120, 255), font=f)
            for (key, sp, (x0, y0, x1, y1), a) in placed:
                if not key.startswith(it + "." + view + "."):
                    continue
                region = key.split(".")[-1]
                col = (0, 255, 0, 255) if region != "front" else (255, 255, 0, 255)
                d.rectangle([x0 * scale, y0 * scale, (x1 - 1) * scale, (y1 - 1) * scale],
                            outline=col, width=2)
                d.text((x0 * scale + 3, y0 * scale + 3), region, fill=(0, 255, 0, 255), font=f)
            p = os.path.join(UV_OUT, f"{it}_{view}.png")
            big.save(p)
            done.append(p)
    return done


def emit_viewer(placed, atlas, atlas_size):
    """导出 viewer 用的图集副本 + UV 表（Unity 口径 v=1-y/H，页面自己翻）。"""
    import json
    shutil.copyfile(ATLAS_OUT, VIEW_TEX)
    W, H = atlas_size
    d = {}
    for key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        d[key] = [round((ax + PAD) / W, 5), round(1.0 - (ay + ah - PAD) / H, 5),
                  round((ax + aw - PAD) / W, 5), round(1.0 - (ay + PAD) / H, 5),
                  x1 - x0, y1 - y0]
    with open(VIEW_JSON, "w", encoding="utf-8") as f:
        json.dump({"uv": d, "size": [W, H]}, f, ensure_ascii=False, indent=1)


def main():
    items = sys.argv[1:] or list(SPEC.keys())
    atlas, placed, size, stats = pack(items)
    os.makedirs(os.path.dirname(ATLAS_OUT), exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(ATLAS_OUT)
    emit_cs(placed, size, CS_OUT)
    emit_viewer(placed, atlas, size)
    tot = sum(s[1] * s[2] for s in stats)
    stp = sum(s[3] for s in stats)
    print("atlas:", ATLAS_OUT, size, "blocks:", len(placed),
          "取样像素:", tot, "补绘描边:", stp, "(%.1f%%)" % (100.0 * stp / max(1, tot)))
    print("cs:", CS_OUT)
    for p in overlay(items, placed):
        print("uv:", p)


if __name__ == "__main__":
    main()
