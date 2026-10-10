# AI生成
# -*- coding: utf-8 -*-
"""生成墙体专用图集 atlas.png

**素材实况**（wall_atlas_bricks.png 320x320，取 row1,col2 的 80x80 格，逐行实测）：
    y= 0..11 透明
    y=12     暗边 67
    y=13..15 亮灰边 132~136    <- 上灰边
    y=16..41 墙顶主体 100~105
    y=42     131~133
    y=43..44 亮灰边 132~135    <- 下灰边
    y=45..66 白色砖条 200~238（砖缝暗线 199）  <- 立面砖
    y=67..79 透明

**每行 80 列像素完全一致**（无横向变化），所以立面砖靠纵向平铺。

**顶面灰边 = 朝向/连通性表达器**（素材自带，不是我们画的）
单格四个方向（N/S/E/W）有无相邻墙 = 2^4 = 16 种，与原版图集16 格数量一致。
亮灰边出现在哪个方向，就表示那个方向有墙相连；灰边消失处表示该方向是墙端。
所有 16 种变体都由**同一份真实顶面像素**裁切/搬运得到，不自绘任何颜色。

图集布局（128x160，每格 32x32）：
    行0 y=0..31立面砖循环段（21 行 x 80 列，原样不缩放）
    行1..4        16 种顶面连通性变体（4x4，与原版 16 格一一对应）
        变体序号 = (北?1:0)*8 + (南?1:0)*4 + (西?1:0)*2 + (东?1:0)
"""
import numpy as np
from PIL import Image

SRC = (r"D:\rim mod\RimWorld_Vanilla_Textures_1.6\Core\textures\things"
       r"\building\linked\wall\wall_atlas_bricks.png")
OUT = r"D:\rim mod\FirstPerson3D\viewer\atlas.png"

CELL = 32                # 图集每格
FACE_GRID = 4# 立面占 4x4 格区（128x128）的前若干行
TOP_VARIANTS = 16                # 2^4 = 全部连通性组合
TOP_GRID = 4                     # 16 个变体排成 4x4
W = CELL * 8                     # 图集宽 256（连续段 8 格宽=两段并排）
STRIP_ROWS = 2                  # 连续段贴图 2 行（东西向 / 南北向）
H = CELL * (1 + TOP_GRID + STRIP_ROWS)   # 立面1 + 变体4 + 连续段2 = 224

# 素材坐标（源格 80x80）
SRC_TOP_Y0 = 12                          # 顶面段起始 y=12（32 行到 y=43）
SRC_FACE_Y0, SRC_FACE_Y1 = 45, 66         # 立面砖条段 y=45..65（21 行）
SRC_FACE_ROWS = SRC_FACE_Y1 - SRC_FACE_Y0

# 顶面 32x32 内的结构行号（实测）
TOP_EDGE_UP = slice(0, 3)      # 上亮灰边 y=0..2
TOP_BODY = slice(3, 29)        # 墙顶主体 y=3..28
TOP_EDGE_DN = slice(29, 32)    # 下亮灰边 y=29..31


def load_source():
    im = Image.open(SRC).convert("RGBA")
    W_, H_ = im.size
    cell = im.crop((2 * W_ // 4, 1 * H_ // 4, 3 * W_ // 4, 2 * H_ // 4))
    return np.asarray(cell).astype(np.uint8)


def build_top_base(src):
    """真实顶面 32x32x4（原样，无任何加工）。"""
    return src[SRC_TOP_Y0:SRC_TOP_Y0 + CELL, 0:CELL, :].copy()


def build_top_variant(base, n, s, w, e):
    """第 (n,s,w,e) 号连通性变体。**只用 base 里的像素搬运，不新增颜色。**

    亮灰边 = 该方向是**墙段端头**（该侧无墙相连）；无亮边 = 该侧与邻格相连。
    抹平时填的是 base 自己 BODY 区的一行（墙顶灰），不是新画的颜色。
    """
    img = base.copy()
    # 抹平用的墙顶灰：取 BODY 区中间一行（无任何灰边）
    body_row = base[TOP_BODY.start, :, :]                   # (32,4) 一行
    body_full = np.repeat(body_row[None, :, :], CELL, axis=0)  # (32,32,4) 铺满
    # 左右灰边用的亮色：取上亮灰边那一列（3x1x4），沿高度铺满成3 列
    up_col = base[:, 0:1, :]                               # (32,1,4) 上灰边最左一列
    left_edge = np.repeat(up_col, 3, axis=1)               # (32,3,4)

    if not n:                                     # 北侧与邻格相连 -> 抹掉上亮灰边
        img[TOP_EDGE_UP, :, :] = body_row
    if not s:                                     # 南侧与邻格相连 -> 抹掉下亮灰边
        img[TOP_EDGE_DN, :, :] = body_row
    if w:                                         # 西侧是端头 -> 左侧留亮灰边
        img[:, 0:3, :] = left_edge
    else:                                         # 西侧相连 -> 抹掉左侧
        img[:, 0:3, :] = body_full[:, 0:3, :]
    if e:                                         # 东侧是端头 -> 右侧留亮灰边
        img[:, -3:, :] = left_edge
    else:                                         # 东侧相连 -> 抹掉右侧
        img[:, -3:, :] = body_full[:, -3:, :]
    return img


def build_top_strip(base, horizontal=True, long_cells=4):
    """**连通段连续铺贴图** —— 整段墙共用一张，格子交界处不出现任何亮边。

    这才是墙体顶面的正确表现：亮灰边只画在**整段墙的侧向轮廓**上，
    格子交界处是连续的墙顶灰，看不出分格。

    horizontal=True  -> 长边沿 x（东西向墙），上下两条亮边 =墙的两个侧面
    horizontal=False -> 长边沿 z（南北向墙），左右两条亮边

    做法：把 base 的主体灰**横向重采样**到 long_cells*32 列（最近邻，
    保持材质颗粒），亮边同样横向铺开。这是像素搬运 + 重采样，不新增颜色。
    """
    LONG = long_cells * CELL
    def stretch(block):
        # 最近邻横向重采样block 的列 -> LONG 列
        idx = (np.arange(LONG) * block.shape[1] // LONG)
        return block[:, idx, :]
    out = np.zeros((CELL, LONG, 4), np.uint8)
    out[:, :, 3] = 255
    out[TOP_BODY] = stretch(base[TOP_BODY, :, :])
    out[TOP_EDGE_UP] = stretch(base[TOP_EDGE_UP, :, :])      # 上亮边
    out[TOP_EDGE_DN] = stretch(base[TOP_EDGE_DN, :, :])      # 下亮边
    if not horizontal:
        out = np.transpose(out, (1, 0, 2)).copy()            # 转成南北向
    return out


def build_face(src):
    """立面砖条段 y=45..65（21 行），横向不缩放，纵向整段重复。"""
    return src[SRC_FACE_Y0:SRC_FACE_Y1, :, :].copy()


def main():
    src = load_source()
    atlas = np.zeros((H, W, 4), np.uint8)

    # ---------------- 行0：立面砖循环段（原样 21 行 x 80 列）
    face = build_face(src)
    atlas[0:SRC_FACE_ROWS, 0:80, :] = face

    # ---------------- 顶面 16 种连通性变体（4x4）
    base = build_top_base(src)
    for idx in range(TOP_VARIANTS):
        n = 1 if (idx & 8) else 0
        s = 1 if (idx & 4) else 0
        w = 1 if (idx & 2) else 0
        e = 1 if (idx & 1) else 0
        r, c = divmod(idx, TOP_GRID)
        y0 = CELL + r * CELL                  # 行1 起
        x0 = c * CELL
        atlas[y0:y0 + CELL, x0:x0 + CELL] = build_top_variant(base, n, s, w, e)

    # ---------------- 连续段贴图（整段墙共用，格子交界无亮边）
    # 行5：东西向段（长边沿 x，亮边在上下= 墙的两个侧面）
    atlas[(1 + TOP_GRID) * CELL:(2 + TOP_GRID) * CELL, 0:4 * CELL] = \
        build_top_strip(base, horizontal=True)
    # 行5 右半：南北向段（长边沿 z，亮边在左右）—— 不转置，
    # 运行时把 u 映射到长边、v 映射到厚度方向即可。
    atlas[(1 + TOP_GRID) * CELL:(2 + TOP_GRID) * CELL, 4 * CELL:8 * CELL] = \
        build_top_strip(base, horizontal=True)

    # ---------------- 未使用区域置透明
    atlas[SRC_FACE_ROWS:CELL, :, 3] = 0        # 行0 下半（立面只用 21 行）
    for r in range(1, 1 + TOP_GRID):            # 16 变体每行只用前 32 列
        atlas[r * CELL:(r + 1) * CELL, CELL:4 * CELL, 3] = 0
    atlas[CELL:CELL + 0, :, 3] = 0

    Image.fromarray(atlas, "RGBA").save(OUT)

    print("saved:", OUT, atlas.shape)
    print("  立面 = src y=%d..%d（%d 行砖条，原样，横 80 列）" %
          (SRC_FACE_Y0, SRC_FACE_Y1 - 1, SRC_FACE_ROWS))
    print("  顶面变体 = %d 种（2^4），用于单格/端头" % TOP_VARIANTS)
    print("  连续段   = 行%d 左半=东西向 / 右半=南北向（各128列=4格宽，交界无亮边）" % (1 + TOP_GRID))
    print("  变体索引 = 北*8 + 南*4 + 西*2 + 东*1（1=该方向有墙，亮灰边保留）")
    for idx in range(TOP_VARIANTS):
        n, s, w, e = (idx & 8) and 'N' or '-', (idx & 4) and 'S' or '-', \
                     (idx & 2) and 'W' or '-', (idx & 1) and 'E' or '-'
        r, c = divmod(idx, TOP_GRID)
        print("    #%2d (%d,%d) %s%s%s%s" % (idx, r, c, n, s, w, e))


if __name__ == "__main__":
    main()