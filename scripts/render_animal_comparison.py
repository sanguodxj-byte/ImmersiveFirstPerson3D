# AI生成
# -*- coding: utf-8 -*-
"""生成动物 3D 精修模型与 RimWorld 原版 2D 贴图多角度并排对比大图 (D:\\animal_vanilla_comparison.png)。

特性：
1. 选取 10 大代表性物种（马、灰熊、森林狼、长角敲击兽、大象、短吻鳄、眼镜蛇、野猪、耗牛、巨型蜘蛛）。
2. 从 RimWorld 贴图库解析并合成真实的 2D 原版贴图（_east, _south, _north）。
3. 通过无头 Chrome 批量截取 3D 渲染多角度图（东侧视、南正视、北后视、45°透视）。
4. 上下严格对应拼接：
   - 每列对应相同视角（第1列侧视，第2列正视，第3列后视，第4列透视与原版综合）
   - 原版 2D 贴图与 3D 解剖形态逐项对照，检验马鞍背、肩峰、收腰、新月角、象鼻、鳄吻、兜帽等精修细节。
"""
import io
import json
import os
import subprocess
import sys
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _tmpsweep import TMPROOT, scratch, env_for, profile, drop, sweep, sweep_mine
from animal_atlas import load_xml_kinds, build_tex_index, resolve_view_paths, composite_vanilla

MOD = r"D:\rim mod\FirstPerson3D"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
OUT_IMG = r"D:\animal_vanilla_comparison.png"

TARGETS = [
    ("horse",        "Horse (马 / CervidEquine)",         "马鞍背下凹弧线 / 深胸急剧收腰 / 粗壮斜长颈 / 长吻长脸 / 贴臀垂马尾"),
    ("bear_grizzly", "Grizzly Bear (灰熊 / UrsineSloth)", "高耸肌肉肩峰 (Shoulder Hump) / 厚沉垂腹 / 宽厚下颌 / 侧贴圆耳"),
    ("wolf_timber",  "Timber Wolf (森林狼 / Canid)",      "深胸急剧收腰流线犬躯 / 三角形外张颊毛 (Ruff) / 斜长狼吻 / 悬空蓬松扫帚尾"),
    ("thrumbo",      "Thrumbo (敲击兽 / Thrumbo)",        "前上挺拔昂立白玉弯刀大角 / 顺滑胸前白毛披肩 / 平顺微S长颈"),
    ("elephant",     "Elephant (大象 / Pachyderm)",       "四段向前向上勾曲象鼻 (Curled Hook) / 宽薄大扇耳 / 前探长白象牙"),
    ("alligator",    "Alligator (短吻鳄 / Reptile)",       "宽扁鸭嘴鳄吻 (Duckbill Snout) / 扁平头颅与凸起眼座 / 渐细长尾"),
    ("cobra",        "Cobra (蛇 / Serpent)",              "贴地优雅盘绕蛇身 / 直立平顺昂扬曲颈 / 圆润小卵形蛇头 / 纯正细蛇无外展兜帽"),
    ("wildboar",     "Wild Boar (野猪 / Porcine)",        "额顶延伸至背脊铁锈橙锯齿背鬃 / 向上弯翘白獠牙 / 前倾猪吻盘"),
    ("muffalo",      "Muffalo (长毛牛 / WoollyUngulate)", "前倾厚毛肩峰 / 大号水滴形灰蓝面盘 / 居中横缝眼 / 纯正无角长毛巨躯"),
    ("megaspider",   "Megaspider (巨型蜘蛛 / Insectoid)", "外展上扬新月角冠头盾 / 额角与前下方内拢双螯 / 多眼簇 / 卷腹尾刺"),
]

CW = 340
CH_VAN = 200
CH_3D = 240
HEADER_H = 40
CARD_H = HEADER_H + CH_VAN + CH_3D
CARD_W = CW * 4


def render_all_3d(targets):
    shots = {}
    for sp_key, title, desc in targets:
        rd = scratch("anim_comp_" + sp_key)
        url = f"http://127.0.0.1:8899/animals.html?single={sp_key}"
        tmp_png = os.path.join(TMPROOT, f"anim_3d_{sp_key}.png")
        cmd = [
            CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
            "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
            "--virtual-time-budget=4000", "--timeout=40000",
            f"--window-size={CARD_W},{CH_3D}",
            "--user-data-dir=" + profile(rd),
            "--screenshot=" + tmp_png,
            url
        ]
        try:
            subprocess.run(cmd, env=env_for(rd), timeout=50)
            if os.path.isfile(tmp_png):
                im = Image.open(tmp_png).convert("RGBA")
                if im.size != (CARD_W, CH_3D):
                    im = im.resize((CARD_W, CH_3D), Image.Resampling.LANCZOS)
                shots[sp_key] = im
                os.remove(tmp_png)
        finally:
            drop(rd)
            sweep_mine()
            sweep()
    return shots


def build_vanilla_cards(kinds, tex_idx, sp_key):
    info = kinds.get(sp_key)
    if not info:
        return None
    views = resolve_view_paths(info["texPath"], tex_idx)
    c1, c2, shader = info["c1"], info["c2"], info["shader"]

    res = {}
    for v in ["east", "south", "north"]:
        p = views.get(v)
        pm = views.get(v + "m")
        if p and os.path.isfile(p):
            arr, _ = composite_vanilla(p, pm, c1, c2, shader)
            res[v] = Image.fromarray(arr, "RGBA")
        else:
            res[v] = None
    return res


def get_fonts():
    font_path = r"C:\Windows\Fonts\msyh.ttc"
    if os.path.isfile(font_path):
        f_title = ImageFont.truetype(font_path, 16)
        f_desc = ImageFont.truetype(font_path, 13)
        f_col = ImageFont.truetype(font_path, 12)
        return f_title, f_desc, f_col
    f_def = ImageFont.load_default()
    return f_def, f_def, f_def


def assemble_card(sp_key, title, desc, van_dict, im_3d, fonts):
    f_title, f_desc, f_col = fonts
    card = Image.new("RGBA", (CARD_W, CARD_H), (32, 36, 44, 255))
    draw = ImageDraw.Draw(card)

    # 1. 顶部标题栏
    draw.rectangle([0, 0, CARD_W, HEADER_H], fill=(24, 28, 36, 255), outline=(56, 64, 80, 255))
    # 标题文字
    draw.text((16, 9), title, fill=(127, 209, 255, 255), font=f_title)
    draw.text((360, 11), desc, fill=(200, 212, 228, 255), font=f_desc)

    # 2. 原版 2D 贴图视口（前三列分别对应 东侧视、南正视、北后视；第4列为多视角对比综合）
    col_labels = ["原版 2D · 东侧视 (_east)", "原版 2D · 南正视 (_south)", "原版 2D · 北后视 (_north)", "原版 2D 综合对比 / 特征区"]
    view_keys = ["east", "south", "north"]

    for col in range(4):
        x0 = col * CW
        y0 = HEADER_H
        x1 = x0 + CW
        y1 = y0 + CH_VAN

        # 底色网格分界线
        draw.rectangle([x0, y0, x1, y1], outline=(46, 54, 68, 255))
        # 视口小标
        draw.text((x0 + 10, y0 + 6), col_labels[col], fill=(160, 175, 195, 255), font=f_col)

        if col < 3:
            vk = view_keys[col]
            v_img = van_dict.get(vk) if van_dict else None
            if v_img:
                # 保持宽高比居中缩放
                vw, vh = v_img.size
                scale = min((CW - 36) / vw, (CH_VAN - 40) / vh, 1.35)
                nw, nh = int(vw * scale), int(vh * scale)
                scaled = v_img.resize((nw, nh), Image.Resampling.LANCZOS)
                px = x0 + (CW - nw) // 2
                py = y0 + 26 + (CH_VAN - 30 - nh) // 2
                card.paste(scaled, (px, py), scaled)
        else:
            # 第4列：并排显示 east 与 south 小图
            if van_dict and van_dict.get("east") and van_dict.get("south"):
                e_img = van_dict["east"]
                s_img = van_dict["south"]
                # 放置两个小图
                scale = min((CW // 2 - 20) / e_img.size[0], (CH_VAN - 45) / e_img.size[1], 1.1)
                ew, eh = int(e_img.size[0] * scale), int(e_img.size[1] * scale)
                e_scaled = e_img.resize((ew, eh), Image.Resampling.LANCZOS)
                card.paste(e_scaled, (x0 + 12, y0 + 26 + (CH_VAN - 30 - eh) // 2), e_scaled)

                scale_s = min((CW // 2 - 20) / s_img.size[0], (CH_VAN - 45) / s_img.size[1], 1.1)
                sw, sh = int(s_img.size[0] * scale_s), int(s_img.size[1] * scale_s)
                s_scaled = s_img.resize((sw, sh), Image.Resampling.LANCZOS)
                card.paste(s_scaled, (x0 + CW // 2 + 6, y0 + 26 + (CH_VAN - 30 - sh) // 2), s_scaled)

    # 3. 3D 模型渲染视口（直接贴入下半部）
    if im_3d:
        card.paste(im_3d, (0, HEADER_H + CH_VAN))

    # 绘制 3D 各列外框与视角标签
    labels_3d = ["精修 3D · 东侧视 (模型侧线与脊腹曲线)", "精修 3D · 南正视 (模型面宽与颊颈轮廓)", "精修 3D · 北后视 (后背饱满与后腿收放)", "精修 3D · 45°立体透视 (真实空间体积)"]
    for col in range(4):
        x0 = col * CW
        y0 = HEADER_H + CH_VAN
        x1 = x0 + CW
        y1 = y0 + CH_3D
        draw.rectangle([x0, y0, x1, y1], outline=(54, 62, 78, 255))
        draw.text((x0 + 10, y0 + 8), labels_3d[col], fill=(120, 200, 160, 255), font=f_col)

    return card


def main():
    print("1. 加载原版贴图索引与 PawnKind 外观定义...")
    kinds = load_xml_kinds()
    tex_idx = build_tex_index()
    fonts = get_fonts()

    targets = TARGETS
    if len(sys.argv) > 1:
        req = set(sys.argv[1:])
        targets = [t for t in TARGETS if t[0] in req]
        if not targets:
            print(f"警告：未找到指定的物种 {req}，使用全量列表")
            targets = TARGETS

    print(f"2. 渲染指定动物 ({len(targets)} 种) 3D 多角度模型...")
    shots_3d = render_all_3d(targets)
    print(f"   已完成 3D 渲染: {len(shots_3d)} / {len(targets)}")

    print("3. 组装逐物种原版 vs 精修 3D 对应卡片...")
    total_h = len(targets) * CARD_H
    final_sheet = Image.new("RGBA", (CARD_W, total_h), (20, 24, 30, 255))

    for idx, (sp_key, title, desc) in enumerate(targets):
        print(f"   组装卡片 #{idx+1}: {title}...")
        van_dict = build_vanilla_cards(kinds, tex_idx, sp_key)
        im_3d = shots_3d.get(sp_key)
        card = assemble_card(sp_key, title, desc, van_dict, im_3d, fonts)
        final_sheet.paste(card, (0, idx * CARD_H))
        single_path = rf"D:\anim_comp_{sp_key}.png"
        card.save(single_path, "PNG")

    if len(targets) == len(TARGETS):
        print(f"4. 导出高质量对比图至 {OUT_IMG}...")
        final_sheet.save(OUT_IMG, "PNG")
        print(f"   完成！尺寸: {final_sheet.size}, 文件大小: {os.path.getsize(OUT_IMG) // 1024} KB")
    else:
        print(f"4. 单物种卡片已保存至 D:\\anim_comp_{targets[0][0]}.png")


if __name__ == "__main__":
    main()
