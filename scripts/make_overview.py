# -*- coding: utf-8 -*-
"""AI生成 · 建筑与远古残骸全量模型缩略总览图合成脚本（scripts/make_overview.py）
输出到 D:\building_models_overview.png（无空格路径，供终端正常渲染显示）
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
INSPECT_DIR = os.path.join(ROOT, "inspect_buildings")
OUT_FILE_NO_SPACE = r"D:\building_models_overview.png"
OUT_FILE_LOCAL = os.path.join(INSPECT_DIR, "building_models_overview.png")

FONT_PATH = r"C:\Windows\Fonts\msyh.ttc"

CATEGORIES = [
    ("一、建造砖墙系列 (石材染色)", [
        ("wall_sandstone", "砂岩砖墙"),
        ("wall_granite", "花岗岩砖墙"),
        ("wall_limestone", "石灰岩砖墙"),
        ("wall_slate", "板岩砖墙"),
        ("wall_marble", "大理石砖墙"),
    ]),
    ("二、原木与金属光滑墙系列", [
        ("wall_wood", "原木板墙"),
        ("wall_steel", "钢铁墙"),
        ("wall_plasteel", "玻璃钢墙"),
        ("wall_gold", "黄金墙"),
    ]),
    ("三、平滑岩石墙系列 (天然石材)", [
        ("smoothed_sandstone", "平滑砂岩墙"),
        ("smoothed_granite", "平滑花岗岩墙"),
        ("smoothed_marble", "平滑大理石墙"),
    ]),
    ("四、防御装甲与异象/DLC满格墙系列", [
        ("fortified_wall", "远古加固防爆墙"),
        ("gravship_hull", "飞船船体墙"),
        ("burrow_wall", "虫穴岩土墙"),
        ("smoothed_ice", "平滑冰墙"),
        ("solid_ice", "天然冰墙"),
        ("gray_wall", "迷宫灰墙"),
        ("voidmetal_wall", "虚空金属墙"),
        ("fleshmass_wall", "扭曲血肉墙"),
    ]),
    ("五、矮墙掩体与围栏系列 (四向连接)", [
        ("sandbags", "沙袋掩体"),
        ("barricade_wood", "原木路障"),
        ("barricade_steel", "钢铁路障"),
        ("fence_wood", "木栅栏"),
        ("fence_steel", "金属栅栏"),
        ("ancient_fence", "远古混凝土护栏"),
    ]),
    ("六、穿墙与壁挂设备系列", [
        ("vent", "穿墙通风口"),
        ("cooler", "穿墙制冷机"),
        ("wall_lamp", "壁灯"),
        ("wall_torch", "壁炬 (3D火焰)"),
    ]),
    ("七、远古残骸野生建筑系列 (大型构件)", [
        ("wreck_ruinedtank", "废弃战车残骸"),
        ("wreck_crashedshippart", "飞船残骸部件"),
        ("wreck_ancientwarwalkershell", "远古双足机甲外壳"),
        ("wreck_largeengineblock", "巨型发动机缸体"),
        ("wreck_giantwheela", "巨型机械车轮 (已修黑斑)"),
        ("wreck_concretebarrier", "混凝土路障残骸"),
        ("wreck_rustedindustrialtruck", "锈蚀重型工业卡车"),
    ]),
]

def make_overview():
    f_title = ImageFont.truetype(FONT_PATH, 24)
    f_sec = ImageFont.truetype(FONT_PATH, 17)
    f_card = ImageFont.truetype(FONT_PATH, 14)
    f_sub = ImageFont.truetype(FONT_PATH, 12)
    f_legend = ImageFont.truetype(FONT_PATH, 12)

    # 布局参数：3 列排版
    COLS = 3
    CARD_W = 590
    IMG_H = 166   # 原 1280x360 缩放到 590x166
    CARD_HEADER_H = 28
    CARD_H = IMG_H + CARD_HEADER_H
    GAP_X = 14
    GAP_Y = 12
    MARGIN = 20
    HEADER_H = 68
    SEC_HEADER_H = 36

    TOTAL_W = MARGIN * 2 + COLS * CARD_W + (COLS - 1) * GAP_X

    # 先计算总高度
    total_h = MARGIN + HEADER_H
    for sec_title, items in CATEGORIES:
        total_h += SEC_HEADER_H
        num_rows = (len(items) + COLS - 1) // COLS
        total_h += num_rows * CARD_H + (num_rows - 1) * GAP_Y + 16
    total_h += MARGIN

    bg_color = (26, 30, 38)
    canvas = Image.new("RGB", (TOTAL_W, total_h), bg_color)
    draw = ImageDraw.Draw(canvas)

    # 1. 顶部大标题栏
    draw.rectangle([0, 0, TOTAL_W, HEADER_H + 8], fill=(18, 22, 28))
    draw.text((MARGIN, 14), "RimWorld FirstPerson3D · 建筑与远古残骸 3D 模型全量视觉验收总览 (共 37 件)",
              fill=(240, 245, 255), font=f_title)
    draw.text((MARGIN, 45), "每件模型展示四视角：① 45°透视  ② 主视·南  ③ 侧视·东 (掩体/围栏为连接态)  ④ 俯视顶面",
              fill=(120, 195, 255), font=f_legend)

    cur_y = MARGIN + HEADER_H + 6
    item_idx = 1

    for sec_title, items in CATEGORIES:
        # 分类横幅
        draw.rectangle([MARGIN, cur_y, TOTAL_W - MARGIN, cur_y + SEC_HEADER_H - 4], fill=(34, 40, 52))
        draw.rectangle([MARGIN, cur_y, MARGIN + 5, cur_y + SEC_HEADER_H - 4], fill=(60, 145, 255))
        draw.text((MARGIN + 14, cur_y + 6), sec_title, fill=(220, 232, 250), font=f_sec)
        cur_y += SEC_HEADER_H

        for i, (k, cn_name) in enumerate(items):
            col = i % COLS
            row = i // COLS
            x = MARGIN + col * (CARD_W + GAP_X)
            y = cur_y + row * (CARD_H + GAP_Y)

            # 卡片背景与边框
            card_box = [x, y, x + CARD_W, y + CARD_H]
            draw.rectangle(card_box, fill=(20, 23, 30), outline=(48, 56, 70), width=1)

            # 卡片标题条
            draw.rectangle([x, y, x + CARD_W, y + CARD_HEADER_H], fill=(30, 35, 46))
            draw.text((x + 8, y + 4), f"{item_idx:02d}. {cn_name}", fill=(245, 245, 250), font=f_card)
            
            # 英文 key 精确右对齐
            sub_w = f_sub.getbbox(k)[2] - f_sub.getbbox(k)[0]
            draw.text((x + CARD_W - sub_w - 10, y + 6), k, fill=(130, 148, 172), font=f_sub)
            item_idx += 1

            # 加载并缩放 4view 图片
            img_path = os.path.join(INSPECT_DIR, f"{k}_4view.png")
            if os.path.exists(img_path):
                src_im = Image.open(img_path)
                resized = src_im.resize((CARD_W, IMG_H), Image.LANCZOS)
                canvas.paste(resized, (x, y + CARD_HEADER_H))
            else:
                draw.text((x + 20, y + CARD_HEADER_H + 40), f"[MISSING: {k}_4view.png]",
                          fill=(255, 100, 100), font=f_card)

        num_rows = (len(items) + COLS - 1) // COLS
        cur_y += num_rows * CARD_H + (num_rows - 1) * GAP_Y + 20

    # 保存两份
    canvas.save(OUT_FILE_NO_SPACE, quality=95)
    canvas.save(OUT_FILE_LOCAL, quality=95)
    print(f"Overview image saved to: {OUT_FILE_NO_SPACE} ({TOTAL_W}x{total_h})")
    print(f"Local backup saved to: {OUT_FILE_LOCAL}")

if __name__ == "__main__":
    make_overview()
