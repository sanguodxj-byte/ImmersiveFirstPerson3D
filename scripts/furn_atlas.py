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
    Textures/FPS3D/FurnitureAtlas.png   图集（只含原版像素，黑描边已剥离）
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
ATLAS_OUT = os.path.join(MOD, "Textures", "FPS3D", "FurnitureAtlas.png")
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
STL = F + "/stelelarge"
WL = F + "/walllamp"
ROY = "Royalty/textures/things/building/furniture"

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
                  "top": (35, 36, 93, 110), "pillow": (42, 7, 86, 32),
                  "front": (35, 110, 93, 123), "wood": (42, 116, 86, 122)},
        "north": {"path": f"{FB}/bed_north.png",
                  "top": (35, 6, 93, 80), "pillow": (42, 82, 86, 107),
                  "front": (35, 110, 93, 123)},
        "east": {"path": f"{FB}/bed_east.png",
                 "top": (36, 32, 118, 86), "pillow": (11, 36, 35, 80),
                 "front": (36, 88, 118, 95), "wood": (45, 40, 110, 76)},
    },
    "stool": {
        "north": {"path": f"{F}/stool_north.png",
                  "top": (10, 20, 53, 45), "front": (12, 45, 17, 62),
                  "discard": [(17, 45, 53, 64)], "wood": (20, 25, 45, 40)},
        "east": {"path": f"{F}/stool_east.png",
                 "top": (17, 11, 45, 41), "front": (19, 41, 24, 60)},
    },
    "endtable": {
        "south": {"path": f"{F}/endtable_south.png",
                  "top": (10, 5, 53, 28), "front": (20, 30, 44, 46),
                  "discard": [(10, 28, 53, 30),
                              (10, 30, 20, 46), (44, 30, 53, 46),
                              (10, 46, 53, 64)], "wood": (14, 10, 30, 20)},
        "north": {"path": f"{F}/endtable_north.png",
                  "top": (10, 5, 53, 28), "front": (20, 33, 44, 39),
                  "discard": [(10, 28, 53, 33), (10, 39, 53, 40),
                              (10, 33, 20, 39), (44, 33, 53, 40),
                              (10, 40, 53, 64)]},
        "east": {"path": f"{F}/endtable_east.png",
                 "top": (17, 4, 47, 28), "front": (27, 36, 37, 46),
                 "discard": [(17, 28, 47, 36), (17, 46, 47, 64)]},
    },
    "dresser": {
        "south": {"path": f"{F}/dresser_south.png",
                  "top": (0, 0, 128, 32), "front": (0, 32, 128, 64), "wood": (10, 4, 60, 28)},
        "north": {"path": f"{F}/dresser_north.png",
                  "top": (0, 0, 128, 32), "front": (0, 32, 128, 64)},
        "east": {"path": f"{F}/dresser_east.png",
                 "top": (0, 0, 64, 92), "front": (0, 92, 64, 128)},
    },
    "shelf": {
        "south": {"path": f"{F}/shelf_south.png",
                  "top": (43, 28, 213, 78), "front": (43, 78, 213, 93),
                  "discard": [(43, 93, 213, 99)], "wood": (60, 35, 120, 70)},
        "north": {"path": f"{F}/shelf_north.png",
                  "top": (43, 34, 213, 78), "front": (43, 78, 213, 93),
                  "discard": [(43, 28, 213, 34), (43, 93, 213, 99)]},
        "east": {"path": f"{F}/shelf_east.png",
                 "top": (33, 37, 88, 205), "front": (33, 205, 88, 218),
                 "discard": [(88, 37, 95, 218)]},
    },
    # ---- 线稿型（白面 + 黑框）：材质 = 白面本身，黑框全交给描边壳 ----
    # 白面是圆角的，所以按行分带、每带的 x 区间取该带各行最长不透明段的交集
    # （带内天然无洞）。分带坐标由 scripts/_furn_fit.py 试算给出。
    # ---- 桌（台面边 edge / 台面 top / 画出来的腿条 front；腿的圆盖用 wood）----
    "table1x2": {
        "north": {"path": f"{F}/table1x2_north.png",
                  "edge": (65, 64, 127, 72), "top": (65, 72, 127, 192),
                  "front": (71, 192, 80, 207), "wood": (80, 90, 120, 170)},
        "east": {"path": f"{F}/table1x2_east.png",
                 "edge": (64, 64, 192, 72), "top": (65, 72, 191, 128),
                 "front": (74, 128, 83, 143), "wood": (90, 80, 170, 120)},
    },
    "table2x2": {
        "north": {"path": f"{F}/table2x2_north.png",
                  "edge": (64, 64, 191, 72), "top": (65, 72, 191, 192),
                  "front": (74, 192, 83, 207), "wood": (90, 100, 170, 180)},
    },
    "table2x4": {
        "north": {"path": f"{F}/table2x4_north.png",
                  "edge": (65, 64, 191, 72), "top": (65, 72, 191, 320),
                  "front": (74, 320, 83, 335), "wood": (90, 120, 170, 300)},
        "east": {"path": f"{F}/table2x4_east.png",
                 "edge": (64, 64, 319, 72), "top": (65, 72, 319, 192),
                 "front": (74, 192, 83, 207), "wood": (120, 90, 260, 180)},
    },
    "table3x3": {
        "north": {"path": f"{F}/table3x3_north.png",
                  "edge": (65, 64, 255, 72), "top": (65, 72, 255, 256),
                  "front": (74, 256, 83, 271), "wood": (100, 110, 220, 240)},
    },
    # ---- production phase 1: exact vanilla workbench samples ----
    # A vanilla production bench is a 3x1 sprite: the south/north canvas has
    # a 192x64 top projection and the east canvas has a 64x192 projection.
    # Keep those aspect ratios intact. The lower 9px strip is the only
    # rectangular source for the vertical apron; do not use the transparent
    # canvas around it as a material sample.
    #
    # The generated atlas/UV file remains the only runtime source; no
    # generated FurnitureUVGen.cs or FurnitureAtlas.png edits belong here.
    "tablebutcher": {
        "south": {"path": f"Core/textures/things/building/production/tablebutcher_south.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 200, 85)},
        "north": {"path": f"Core/textures/things/building/production/tablebutcher_north.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 74, 85)},
        "east": {"path": f"Core/textures/things/building/production/tablebutcher_east.png",
                  "top": (19, 17, 77, 205), "front": (24, 205, 72, 213)},
    },
    "tablesculpting": {
        "south": {"path": f"Core/textures/things/building/production/tablesculpting_south.png",
                   "top": (19, 18, 130, 77), "front": (24, 77, 114, 85),
                   "stone": (94, 42, 112, 58), "stone_s": (148, 52, 166, 66)},
        "north": {"path": f"Core/textures/things/building/production/tablesculpting_north.png",
                   "top": (94, 18, 205, 77), "front": (110, 77, 200, 85),
                   "stone": (112, 42, 130, 58), "stone_s": (26, 26, 42, 40)},
        "east": {"path": f"Core/textures/things/building/production/tablesculpting_east.png",
                  "top": (19, 92, 77, 205), "front": (24, 205, 72, 213),
                  "stone": (44, 116, 60, 132), "stone_s": (48, 22, 62, 34)},
    },
    "tablesmithingfueled": {
        "south": {"path": f"Core/textures/things/building/production/tablesmithingfueled_south.png",
                   "top": (75, 36, 205, 74), "front": (158, 76, 198, 86),
                   "box_top": (20, 16, 70, 44), "box_front": (20, 44, 70, 73),
                   "hood": (92, 22, 130, 34)},
        "north": {"path": f"Core/textures/things/building/production/tablesmithingfueled_north.png",
                   "top": (19, 21, 149, 60), "front": (136, 22, 148, 36),
                   "box_top": (154, 16, 204, 44), "box_front": (154, 44, 204, 73),
                   "hood": (98, 62, 128, 74)},
        "east": {"path": f"Core/textures/things/building/production/tablesmithingfueled_east.png",
                  "top": (36, 19, 74, 141), "front": (62, 36, 72, 48),
                  "box_top": (20, 146, 70, 172), "box_front": (20, 172, 70, 196),
                  "hood": (22, 92, 34, 128)},
    },
    "tablesmithingelectric": {
        "south": {"path": f"Core/textures/things/building/production/tablesmithingelectric_south.png",
                   "top": (19, 36, 205, 74), "front": (158, 76, 198, 86),
                   "box_top": (38, 30, 70, 52), "box_front": (38, 52, 70, 70),
                   "hood": (92, 22, 130, 34)},
        "north": {"path": f"Core/textures/things/building/production/tablesmithingelectric_north.png",
                   "top": (19, 21, 205, 60), "front": (136, 22, 148, 36),
                   "box_top": (154, 16, 186, 38), "box_front": (154, 38, 186, 54),
                   "hood": (98, 62, 128, 74)},
        "east": {"path": f"Core/textures/things/building/production/tablesmithingelectric_east.png",
                  "top": (36, 19, 74, 201), "front": (62, 36, 72, 48),
                  "box_top": (42, 144, 72, 172), "box_front": (42, 172, 72, 198),
                  "hood": (22, 92, 34, 128)},
    },
    "tablestoveelectric": {
        "south": {"path": f"Core/textures/things/building/production/tablestoveelectric_south.png",
                   "top": (19, 17, 147, 74), "front": (24, 72, 72, 78),
                   "box_top": (152, 16, 202, 42), "box_front": (152, 44, 202, 76)},
        "north": {"path": f"Core/textures/things/building/production/tablestoveelectric_north.png",
                   "top": (77, 17, 205, 74), "front": (154, 73, 200, 78),
                   "box_top": (21, 16, 74, 42), "box_front": (21, 44, 74, 78)},
        "east": {"path": f"Core/textures/things/building/production/tablestoveelectric_east.png",
                  "top": (19, 74, 77, 202), "front": (24, 200, 72, 206),
                  "box_top": (22, 17, 74, 42), "box_front": (22, 44, 74, 68)},
    },
    "tablestovefueled": {
        "south": {"path": f"Core/textures/things/building/production/tablestovefueled_south.png",
                   "top": (75, 17, 205, 74), "front": (152, 73, 200, 78),
                   "box_top": (21, 16, 72, 42), "box_front": (22, 44, 72, 80)},
        "north": {"path": f"Core/textures/things/building/production/tablestovefueled_north.png",
                   "top": (19, 17, 149, 74), "front": (46, 73, 80, 78),
                   "box_top": (152, 16, 203, 42), "box_front": (152, 44, 202, 80)},
        "east": {"path": f"Core/textures/things/building/production/tablestovefueled_east.png",
                  "top": (19, 19, 77, 144), "front": (66, 24, 75, 68),
                  "box_top": (21, 146, 74, 172), "box_front": (22, 172, 72, 204)},
    },
    "tabletailorhand": {
        "south": {"path": f"Core/textures/things/building/production/tabletailorhand_south.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 200, 81)},
        "north": {"path": f"Core/textures/things/building/production/tabletailorhand_north.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 200, 81)},
        "east": {"path": f"Core/textures/things/building/production/tabletailorhand_east.png",
                  "top": (19, 17, 77, 205), "front": (24, 205, 72, 209)},
    },
    "tabletailorelectric": {
        "south": {"path": f"Core/textures/things/building/production/tabletailorelectric_south.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 200, 81),
                   "mach": (91, 14, 136, 28), "mach_s": (88, 39, 138, 46)},
        "north": {"path": f"Core/textures/things/building/production/tabletailorelectric_north.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 200, 81),
                   "mach": (88, 35, 133, 49), "mach_s": (87, 60, 137, 67)},
        "east": {"path": f"Core/textures/things/building/production/tabletailorelectric_east.png",
                  "top": (19, 17, 77, 205), "front": (24, 205, 72, 209),
                  "mach": (40, 84, 56, 134), "mach_s": (34, 134, 61, 143)},
    },
    "tablestonecutter": {
        "south": {"path": f"Core/textures/things/building/production/tablestonecutter_south.png",
                   "top": (19, 41, 205, 77), "front": (24, 77, 200, 84),
                   "disc": (94, 28, 134, 58)},
        "north": {"path": f"Core/textures/things/building/production/tablestonecutter_north.png",
                   "top": (19, 19, 205, 47), "front": (22, 47, 50, 54),
                   "disc": (92, 32, 132, 62)},
        "east": {"path": f"Core/textures/things/building/production/tablestonecutter_east.png",
                  "top": (45, 19, 77, 205), "front": (46, 205, 74, 213),
                  "disc": (28, 92, 58, 132)},
    },
    "tablemachining": {
        "south": {"path": f"Core/textures/things/building/production/tablemachining_south.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 200, 85),
                   "body": (82, 58, 122, 74), "pad": (76, 19, 146, 51)},
        "north": {"path": f"Core/textures/things/building/production/tablemachining_north.png",
                   "top": (19, 17, 205, 77), "front": (24, 77, 200, 85),
                   "body": (102, 20, 142, 36), "pad": (78, 43, 148, 75)},
        "east": {"path": f"Core/textures/things/building/production/tablemachining_east.png",
                  "top": (19, 17, 77, 205), "front": (24, 205, 72, 213),
                  "body": (58, 106, 74, 142), "pad": (20, 76, 52, 146)},
    },
    "tabledruglab": {
        "south": {"path": f"Core/textures/things/building/production/tabledruglab_south.png",
                   "top": (19, 17, 205, 77), "front": (24, 78, 200, 85),
                   "pad": (168, 46, 198, 70), "col": (180, 18, 188, 56),
                   "vat": (96, 44, 122, 68), "grn": (66, 20, 84, 36), "blu": (44, 44, 60, 62)},
        "north": {"path": f"Core/textures/things/building/production/tabledruglab_north.png",
                   "top": (19, 17, 205, 77), "front": (24, 78, 200, 85),
                   "pad": (24, 46, 54, 70), "col": (36, 18, 44, 56),
                   "vat": (100, 32, 126, 56), "grn": (140, 50, 158, 66), "blu": (162, 34, 178, 52)},
        "east": {"path": f"Core/textures/things/building/production/tabledruglab_east.png",
                  "top": (19, 17, 77, 205), "front": (24, 206, 72, 213),
                  "pad": (26, 40, 48, 62), "col": (34, 18, 42, 50),
                  "vat": (48, 86, 70, 112), "grn": (24, 134, 42, 150), "blu": (48, 156, 62, 172)},
    },
    "tablebrewery": {
        "south": {"path": f"Core/textures/things/building/production/tablebrewery_south.png",
                   "top": (19, 17, 141, 77), "front": (24, 77, 138, 85),
                   "vat": (154, 42, 182, 66), "vat_s": (154, 60, 182, 82)},
        "north": {"path": f"Core/textures/things/building/production/tablebrewery_north.png",
                   "top": (83, 17, 205, 77), "front": (86, 77, 200, 85),
                   "vat": (42, 42, 70, 66), "vat_s": (42, 60, 70, 82)},
        "east": {"path": f"Core/textures/things/building/production/tablebrewery_east.png",
                  "top": (19, 80, 77, 205), "front": (24, 205, 72, 213),
                  "vat": (46, 42, 74, 66), "vat_s": (46, 58, 74, 78)},
    },
    "electricsmelter": {
        "south": {"path": f"Core/textures/things/building/production/electricsmelter_south.png",
                   "top": (19, 17, 205, 77), "front": (24, 78, 68, 85),
                   "hood": (84, 38, 140, 44), "hood_s": (84, 52, 140, 68)},
        "north": {"path": f"Core/textures/things/building/production/electricsmelter_north.png",
                   "top": (19, 17, 205, 77), "front": (24, 78, 68, 85),
                   "hood": (84, 44, 140, 58), "hood_s": (84, 22, 140, 38)},
        "east": {"path": f"Core/textures/things/building/production/electricsmelter_east.png",
                  "top": (19, 17, 77, 205), "front": (24, 206, 72, 213),
                  "hood": (38, 84, 44, 140), "hood_s": (52, 84, 68, 140)},
    },
    "researchbenchsimple": {
        "south": {"path": f"Core/textures/things/building/production/researchbenchsimple_south.png",
                   "top": (67, 66, 253, 189), "front": (70, 189, 250, 197)},
        "north": {"path": f"Core/textures/things/building/production/researchbenchsimple_north.png",
                   "top": (67, 66, 253, 189), "front": (70, 189, 250, 197)},
        "east": {"path": f"Core/textures/things/building/production/researchbenchsimple_east.png",
                  "top": (66, 66, 190, 253), "front": (70, 253, 186, 261)},
    },
    "researchbenchhitech": {
        "south": {"path": f"Core/textures/things/building/production/researchbenchhitech_south.png",
                   "top": (66, 66, 382, 189), "front": (70, 189, 378, 197),
                   "tray": (324, 90, 358, 166), "flask": (102, 140, 120, 158)},
        "north": {"path": f"Core/textures/things/building/production/researchbenchhitech_north.png",
                   "top": (66, 66, 382, 189), "front": (70, 189, 378, 197),
                   "tray": (90, 90, 124, 166), "flask": (328, 140, 346, 158)},
        "east": {"path": f"Core/textures/things/building/production/researchbenchhitech_east.png",
                  "top": (66, 66, 190, 381), "front": (70, 381, 186, 389),
                  "tray": (90, 90, 166, 124), "flask": (98, 328, 116, 346)},
    },
    "fabricationbench": {
        "south": {"path": f"Core/textures/things/building/production/fabricationbench_south.png",
                   "top": (66, 68, 382, 189), "front": (70, 191, 378, 197),
                   "body": (70, 172, 130, 186), "mach": (156, 108, 194, 152)},
        "north": {"path": f"Core/textures/things/building/production/fabricationbench_north.png",
                   "top": (66, 68, 382, 189), "front": (214, 191, 240, 197),
                   "body": (368, 74, 380, 110), "mach": (156, 72, 194, 116)},
        "east": {"path": f"Core/textures/things/building/production/fabricationbench_east.png",
                  "top": (66, 66, 190, 381), "front": (132, 382, 184, 389),
                  "body": (166, 68, 186, 80), "mach": (140, 150, 178, 188)},
    },
    # ---- 书柜家族（正面 = 厚框 + 亮内腔，读作一块大面板；只声明 south/east，
    # north 是素背板，交给 south->north->east 回退）----
    "bookcase": {
        "south": {"path": f"{BK}/bookcase_south.png",
                  "top": (34, 26, 158, 40), "front": (34, 40, 158, 96),
                  "wood": (60, 50, 130, 90),
                  "discard": [(34, 96, 158, 104)]},
        "east": {"path": f"{BK}/bookcase_east.png",
                 "top": (32, 32, 96, 56), "front": (32, 56, 90, 169),
                 "wood": (44, 70, 80, 130)},
    },
    "bookcasesmall": {
        "south": {"path": f"{BK}/bookcasesmall_south.png",
                  "top": (31, 26, 96, 40), "front": (31, 40, 97, 96),
                  "wood": (45, 50, 85, 90),
                  "discard": [(31, 96, 97, 104)]},
        "east": {"path": f"{BK}/bookcasesmall_east.png",
                 "top": (32, 26, 95, 56), "front": (31, 56, 89, 108),
                 "wood": (45, 60, 75, 78)},
    },
    "shelfsmall": {
        "south": {"path": f"{F}/shelfsmall_south.png",
                  "top": (31, 26, 97, 56), "front": (31, 56, 97, 88),
                  "wood": (45, 62, 85, 84),
                  "discard": [(31, 88, 97, 104)]},
        "east": {"path": f"{F}/shelfsmall_east.png",
                 "top": (31, 27, 97, 48), "front": (31, 48, 89, 88),
                 "wood": (45, 55, 75, 82),
                 "discard": [(31, 88, 97, 109)]},
    },
    "armchair": {
        "south": {"path": f"{F}/armchair_south.png",
                  "back": (96, 84, 160, 112), "top": (104, 132, 152, 160),
                  "arml": (88, 136, 98, 162), "armr": (158, 136, 168, 162),
                  "front": (92, 164, 164, 173), "wood": (106, 136, 150, 158)},
        "north": {"path": f"{F}/armchair_north.png",
                  "arml": (84, 105, 99, 112), "armr": (156, 105, 172, 112),
                  "top": (100, 112, 156, 124), "back": (92, 132, 164, 168),
                  "front": (92, 140, 164, 168), "wood": (104, 140, 152, 164)},
        "east": {"path": f"{F}/armchair_east.png",
                 "back": (92, 86, 106, 156), "arml": (114, 142, 156, 158),
                 "top": (114, 124, 156, 156), "front": (92, 162, 156, 172),
                 "wood": (116, 126, 152, 138)},
    },
    # ---- 沙发：与扶手椅同一配方（箱体 + 靠背 + 两端扶手），只是 2x1 长轴上多两个坐垫。
    # south 是长边（坐垫/靠背/裙板三条横带 + 两端扶手柱），east 是端视（靠背立柱 + 箱体）。
    "couch": {
        "south": {"path": f"{F}/couch_south.png",
                  "back": (62, 84, 194, 110), "top": (68, 116, 188, 158),
                  "arml": (53, 128, 61, 158), "armr": (196, 128, 205, 158),
                  "front": (54, 163, 202, 172), "wood": (100, 120, 160, 150)},
        "north": {"path": f"{F}/couch_north.png",
                  "arml": (45, 104, 59, 111), "armr": (197, 104, 211, 111),
                  "back": (64, 114, 192, 144),
                  "front": (54, 148, 202, 170), "wood": (100, 120, 160, 140)},
        "east": {"path": f"{F}/couch_east.png",
                 "top": (116, 62, 156, 146), "back": (90, 28, 106, 206),
                 "arml": (116, 156, 156, 176), "front": (116, 198, 158, 210),
                 "wood": (120, 90, 150, 130)},
    },
    # ---- 餐椅：座面是画出来的椭圆（同凳：圆柱面 + 木侧），靠背 = 顶栏那条白带，
    # 腿带是线稿 -> 只取一条腿做立面，其余整带声明丢弃。
    "diningchair": {
        "south": {"path": f"{F}/diningchair_south.png",
                  "back": (19, 4, 44, 6), "top": (16, 26, 47, 44),
                  "discard": [(10, 44, 54, 62)], "wood": (22, 30, 44, 40)},
        "north": {"path": f"{F}/diningchair_north.png",
                  "top": (14, 26, 50, 44),
                  "discard": [(14, 16, 48, 26), (10, 44, 54, 62)]},
        "east": {"path": f"{F}/diningchair_east.png",
                 "top": (18, 27, 52, 44),
                 "discard": [(6, 2, 24, 30), (14, 44, 56, 62)],
                 "wood": (26, 30, 46, 40)},
    },
    # ---- 床家族：top = 床垫（**不含枕头**，枕头是独立板件），pillow = 枕头白块，
    # front = 图底部那条立面带（床尾栏）。south 头在图上端，east 头在图左端。
    "doublebed": {
        "south": {"path": f"{FB}/doublebed_south.png",
                  "top": (4, 28, 124, 112), "pillow": (16, 7, 56, 26),
                  "front": (4, 112, 124, 124), "wood": (40, 60, 90, 100)},
        "east": {"path": f"{FB}/doublebed_east.png",
                 "top": (32, 5, 124, 112), "pillow": (7, 16, 26, 56),
                 "front": (32, 112, 124, 124), "wood": (60, 40, 100, 90)},
    },
    "royalbed": {
        "south": {"path": f"{FB}/royalbed_south.png",
                  "top": (5, 42, 123, 112), "pillow": (18, 10, 56, 34),
                  "front": (4, 112, 124, 126), "wood": (40, 60, 90, 100)},
        "east": {"path": f"{FB}/royalbed_east.png",
                 "top": (32, 5, 124, 112), "pillow": (10, 18, 30, 56),
                 "front": (39, 112, 124, 126), "wood": (60, 40, 100, 90)},
    },
    "hospitalbed": {
        "south": {"path": f"{FB}/hospitalbed_south.png",
                  "top": (32, 38, 96, 112), "pillow": (42, 7, 86, 33),
                  "front": (31, 112, 97, 124), "wood": (50, 60, 80, 100)},
        "east": {"path": f"{FB}/hospitalbed_east.png",
                 "top": (32, 31, 124, 90), "pillow": (7, 36, 30, 80),
                 "front": (37, 90, 124, 98), "wood": (60, 50, 100, 80)},
    },
    # ---- 地铺（bedroll / sleeping spot）：没有床架、床头板，就一条薄垫 + 枕头 ----
    "bedroll": {
        "south": {"path": f"{FB}/bedroll_south.png",
                  "top": (8, 38, 60, 113), "pillow": (14, 10, 52, 34),
                  "front": (9, 113, 59, 121), "wood": (20, 60, 45, 100)},
        "east": {"path": f"{FB}/bedroll_east.png",
                 "top": (40, 9, 122, 50), "pillow": (10, 13, 36, 48),
                 "front": (40, 50, 122, 57), "wood": (70, 20, 110, 45)},
    },
    "bedrolldouble": {
        "south": {"path": f"{FB}/bedrolldouble_south.png",
                  "top": (10, 34, 120, 113), "pillow": (18, 10, 60, 28),
                  "front": (9, 113, 121, 121), "wood": (60, 80, 110, 110)},
        "east": {"path": f"{FB}/bedrolldouble_east.png",
                 "top": (34, 10, 120, 113), "pillow": (10, 18, 28, 60),
                 "front": (34, 113, 120, 120), "wood": (70, 20, 110, 45)},
    },
    # ---- 第三批（草稿，坐标待 _furn_fit.py fix 收缩）：单图型 Graphic_Single ----
    # 这类件原版只画一张立面图（四向共用），所以 front = 图里那条带，
    # top = 该件真正的水平面（灯罩顶/柱头/盆土），侧壁与圆柱一律 wood。
    "standinglamp": {
        "south": {"path": f"{F}/lampstanding.png",
                  "top": (22, 10, 39, 27), "neck": (26, 27, 35, 33),
                  "front": (17, 33, 45, 50), "wood": (24, 36, 40, 46)},
    },
    "column": {
        "south": {"path": f"{F}/column.png",
                  "top": (13, 3, 51, 26), "front": (17, 26, 47, 121),
                  "wood": (24, 40, 40, 90)},
    },
    "plantpot": {
        "south": {"path": f"{F}/plantpot.png",
                  "top": (20, 18, 44, 42), "front": (18, 42, 46, 59),
                  "wood": (26, 21, 38, 34)},
    },
    "penmarker": {
        "south": {"path": f"{F}/penmarker.png",
                  "tip": (28, 3, 35, 11), "top": (16, 11, 48, 26),
                  "front": (27, 26, 37, 53), "wood": (30, 30, 34, 50)},
    },
    "floodlight": {
        "south": {"path": f"{F}/floodlight.png",
                  "top": (67, 52, 190, 92), "pole": (111, 92, 145, 184),
                  "basel": (97, 150, 111, 178), "baser": (145, 150, 159, 178),
                  "wood": (112, 57, 144, 72)},
    },
    # ---- 第四批草稿（坐标待 _furn_fit.py fix 收缩）：Royalty 大件 + 石碑 ----
    "throne": {
        "south": {"path": f"{ROY}/throne_south.png",
                  "back": (85, 43, 170, 110), "arml": (76, 110, 96, 142),
                  "armr": (160, 110, 183, 142), "top": (96, 110, 160, 142),
                  "front": (76, 142, 183, 185), "wood": (110, 130, 160, 138)},
    },
    "grandthrone": {
        "south": {"path": f"{ROY}/grandthrone_south.png",
                  "spire": (118, 42, 138, 66), "gold": (115, 70, 141, 110),
                  "front": (34, 176, 100, 194), "top": (105, 148, 150, 172),
                  "wood": (105, 148, 150, 172)},
    },
    "drape": {
        "south": {"path": f"{ROY}/drape_south.png",
                  "top": (66, 74, 190, 100), "mid": (68, 100, 188, 124),
                  "front": (67, 124, 188, 148), "tassel": (69, 148, 81, 158),
                  "tassel2": (175, 148, 187, 158), "tassel3": (123, 148, 134, 158), "wood": (80, 80, 170, 96)},
    },
    "stelelarge": {
        "south": {"path": f"{STL}/largestelea.png",
                  "crest": (79, 42, 177, 70), "top": (64, 70, 192, 86),
                  "shoulder": (64, 86, 192, 96), "mid": (64, 96, 192, 108),
                  "face": (64, 108, 192, 120), "front": (64, 120, 192, 188),
                  "wood": (90, 100, 170, 115)},
    },
    "stelegrand": {
        "south": {"path": f"{F}/grandstele.png",
                  "crest": (228, 68, 284, 79), "top": (198, 79, 314, 101),
                  "shoulder": (170, 89, 198, 101), "mid": (138, 101, 374, 148),
                  "face": (107, 148, 405, 220), "front": (111, 220, 400, 426),
                  "wood": (180, 170, 340, 210)},
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

    # 宽度按最宽的一块自适应（2 的幂）：2x4 桌的台面带就有 254px 宽，写死 256 会溢出
    widest = max((c[2][2] - c[2][0]) for c in cells) + 2 * PAD
    W = 64
    while W < widest:
        W *= 2
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
    H = 64
    while H < y + row_h:
        H *= 2
    atlas = np.zeros((H, W, 4), np.uint8)
    stats = []
    for key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        src = load(path)
        sub = src[y0:y1, x0:x1].copy()
        # 透明像素与原版黑描边（含 1~2px 半透明/抗锯齿暗晕）一并用内侧真实材质补绘掉：
        lum_full = src[:, :, :3].astype(np.float32).mean(axis=2)
        raw_full = (src[:, :, 3] < 180) | black_mask(src)
        raw_sub = raw_full[y0:y1, x0:x1]
        lum_sub = lum_full[y0:y1, x0:x1]
        mask = raw_sub.copy()
        if (~raw_sub).any():
            med = float(np.median(lum_sub[~raw_sub]))
            d1 = dilate(raw_full)[y0:y1, x0:x1] & (lum_sub < min(150.0, med * 0.82))
            d2 = dilate(dilate(raw_full))[y0:y1, x0:x1] & (lum_sub < min(130.0, med * 0.74))
            cand = raw_sub | d1 | d2
            if (~cand).any():
                mask = cand
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
        "// AI生成",
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
