# AI生成
# -*- coding: utf-8 -*-
"""实测原版各种植被贴图高度，并以「小人的头部贴图」为单位换算倍数。

数据源：D:\\rim mod\\RimWorld_Vanilla_Textures_1.6（原版贴图离线导出，非 resources.assets）
口径：
  · 贴图高度 = alpha >= 16 的有效内容像素高度（排除透明边距），整图像素尺寸另列。
  · 基准单位 = 小人头部贴图 male_average_normal_south.png 的有效内容高度。
输出：
  · docs/plants/VanillaTextureHeights.md   （对照表）
  · docs/plants/vanilla_tex_compare.png    （按同一像素比例并排 + 头高倍数基准线）
"""
import os
import sys

from PIL import Image, ImageDraw

VAN = r"D:\rim mod\RimWorld_Vanilla_Textures_1.6"
MOD = r"D:\rim mod\FirstPerson3D"
PLANT_CORE = os.path.join(VAN, "Core", "textures", "things", "plant")
PLANT_ROY = os.path.join(VAN, "Royalty", "textures", "things", "plant")
HEAD = os.path.join(VAN, "Core", "textures", "things", "pawn", "humanlike", "heads", "male",
                    "male_average_normal_south.png")
HEAD_ALT = os.path.join(VAN, "Core", "textures", "things", "pawn", "humanlike", "heads", "female",
                        "female_average_normal_south.png")
BODY = os.path.join(VAN, "Core", "textures", "things", "pawn", "humanlike", "bodies",
                    "naked_male_south.png")

MD_OUT = os.path.join(MOD, "docs", "plants", "VanillaTextureHeights.md")
PNG_OUT = os.path.join(MOD, "docs", "plants", "vanilla_tex_compare.png")

SKIP_TOKENS = ("_leafless", "_immature", "_snowy", "snowoverlay", "burnedtree", "stumps", "chopped", "smashed")

GROUP_OF = [
    ("tree", "树"),
    ("bush", "灌木/小株"),
    ("berry", "浆果"),
    ("crop", "作物"),
    ("flower", "花"),
    ("grass", "草"),
    ("mushroom", "菌类"),
    ("cactus", "仙人掌"),
    ("vine", "藤蔓"),
    ("other", "其他"),
]


def bbox(path, alpha_min=16):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if px[x, y][3] >= alpha_min:
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y < y0: y0 = y
                if y > y1: y1 = y
    if x1 < 0:
        return None
    return dict(w=w, h=h, cw=x1 - x0 + 1, ch=y1 - y0 + 1, box=(x0, y0, x1, y1))


def klass(name):
    n = name.lower()
    if n.startswith("tree") or n in ("oak", "maple", "birch", "pine", "poplar", "cypress", "palm",
                                     "drago", "teak", "willow", "bamboo", "cecropia", "cocoa", "anima"):
        return "树"
    if "cactus" in n:
        return "仙人掌"
    if "berry" in n:
        return "浆果"
    if n in ("bush", "shrublow", "brambles", "alocasia", "agave", "pincushion") or n.startswith("shrub"):
        return "灌木/小株"
    if "strawberry" in n or "potato" in n or "rice" in n or "corn" in n or "cotton" in n \
            or "hay" in n or "hops" in n or "devilstrand" in n or "psychoid" in n or "smokeleaf" in n \
            or "healroot" in n or "tinctoria" in n:
        return "作物"
    if n in ("rose", "daylily", "dandelion", "clivia", "astragalus", "ambrosia", "rafflesia"):
        return "花"
    if "grass" in n or n in ("moss", "chokevine"):
        return "草"
    if "stool" in n or "shroom" in n or n in ("agarilux", "bryolux", "glowstool", "timbershroom"):
        return "菌类"
    return "其他"


def main():
    plants = []
    for root, prefix in ((PLANT_CORE, ""), (PLANT_ROY, "Royalty/")):
        if not os.path.isdir(root):
            continue
        for name in sorted(os.listdir(root)):
            d = os.path.join(root, name)
            if not os.path.isdir(d):
                continue
            low = name.lower()
            if any(t in low for t in SKIP_TOKENS):
                continue
            pngs = [f for f in sorted(os.listdir(d)) if f.lower().endswith(".png")]
            if not pngs:
                continue
            # 主形态贴图：优先 *a.png / <name>.png
            pick = None
            for cand in (f"{name}a.png", f"{name}.png"):
                for f in pngs:
                    if f.lower() == cand.lower():
                        pick = f
                        break
                if pick:
                    break
            if pick is None:
                pick = pngs[0]
            p = os.path.join(d, pick)
            r = bbox(p)
            if r is None:
                continue
            plants.append(dict(name=name, rel=prefix + pick, path=p, group=klass(name), **r))

    head = bbox(HEAD)
    head_alt = bbox(HEAD_ALT) if os.path.exists(HEAD_ALT) else None
    body = bbox(BODY) if os.path.exists(BODY) else None

    unit = head["ch"]
    print(f"基准：头部贴图 male_average_normal_south.png  整图 {head['w']}x{head['h']}  "
          f"有效内容 {head['cw']}x{head['ch']}  ->  1 单位 = {unit} px")
    if head_alt:
        print(f"      女性头 female_average_normal_south.png  有效内容 {head_alt['cw']}x{head_alt['ch']}  "
              f"({head_alt['ch'] / unit:.2f} 单位)")
    if body:
        print(f"      身体贴图 naked_male_south.png            有效内容 {body['cw']}x{body['ch']}  "
              f"({body['ch'] / unit:.2f} 单位)")
    print()

    plants.sort(key=lambda p: (-p["ch"], p["name"]))
    lines = []
    lines.append("# 原版植被贴图高度 / 小人头部贴图 对照")
    lines.append("")
    lines.append("数据源：`RimWorld_Vanilla_Textures_1.6`（原版贴图离线导出）。")
    lines.append("")
    lines.append("口径：**贴图高度 = 有效内容像素高度**（alpha≥16 的包围盒高，去掉透明边距）；"
                 "「整图」列为 PNG 原始像素尺寸。基准单位 = 小人头部贴图 "
                 f"`pawn/humanlike/heads/male/male_average_normal_south.png` 的有效内容高度 "
                 f"= **{unit} px**。")
    lines.append("")
    lines.append("参考：头部整图 %dx%d、有效 %dx%d；身体（裸男·南）有效 %dx%d = %.2f 单位。"
                 % (head["w"], head["h"], head["cw"], head["ch"],
                    body["cw"] if body else 0, body["ch"] if body else 0,
                    (body["ch"] / unit) if body else 0))
    lines.append("")
    lines.append("| # | 植被（贴图） | 类别 | 整图 px | 有效内容 px | 内容高 / 头高 |")
    lines.append("|---|--------------|------|---------|-------------|---------------|")
    for i, p in enumerate(plants, 1):
        lines.append(f"| {i} | {p['name']} | {p['group']} | {p['w']}x{p['h']} | "
                     f"{p['cw']}x{p['ch']} | **{p['ch'] / unit:.2f}x** |")
    lines.append("")

    # 分组均值
    lines.append("## 分组均值（内容高 / 头高）")
    lines.append("")
    lines.append("| 类别 | 件数 | 均值 | 范围 |")
    lines.append("|------|------|------|------|")
    groups = {}
    for p in plants:
        groups.setdefault(p["group"], []).append(p["ch"] / unit)
    for g in sorted(groups, key=lambda k: -sum(groups[k]) / len(groups[k])):
        v = groups[g]
        lines.append(f"| {g} | {len(v)} | {sum(v) / len(v):.2f}x | {min(v):.2f}~{max(v):.2f}x |")
    lines.append("")

    with open(MD_OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print("\n".join(lines[:8]))
    print(f"... 表共 {len(plants)} 行 -> {MD_OUT}")

    # ---- 对照图：统一像素比例，头高 = 基准线 ----
    scale = 64.0 / unit          # 1 单位 = 64 px
    cols = 7
    pad = 10
    label_h = 26
    cell_w = int(max(p["cw"] for p in plants) * scale) + pad * 2
    cell_h = int(max(p["ch"] for p in plants) * scale) + label_h + pad
    rows = (len(plants) + cols - 1) // cols
    W, H = cols * cell_w, rows * cell_h + 80
    canvas = Image.new("RGBA", (W, H), (24, 26, 30, 255))
    dr = ImageDraw.Draw(canvas)

    head_h = int(unit * scale)
    for m in range(1, 7):
        y = H - 30 - int(m * head_h)
        if y < 40:
            break
        dr.line([(0, y), (W, y)], fill=(255, 120, 90, 110), width=1)
        dr.text((4, y - 14), f"{m}x ({m * unit}px)", fill=(255, 150, 120, 255))

    for i, p in enumerate(plants):
        cx = (i % cols) * cell_w
        cy = (i // cols) * cell_h + 70
        im = Image.open(p["path"]).convert("RGBA")
        im = im.resize((max(1, int(p["w"] * scale)), max(1, int(p["h"] * scale))), Image.NEAREST)
        base = cy + cell_h - label_h - pad
        canvas.alpha_composite(im, (cx + pad, base - im.size[1]))
        dr.text((cx + pad, cy + cell_h - label_h + 2),
                f"{p['name']} {p['ch'] / unit:.2f}x", fill=(230, 230, 235, 255))
    dr.text((6, 8), f"原版植被贴图 vs 小人头部贴图（1 单位 = 头高 {unit}px）",
            fill=(255, 255, 255, 255))
    canvas.save(PNG_OUT)
    print(f"对照图 -> {PNG_OUT}  ({W}x{H})")

    # ================= 宽度报告 =================
    wunit = head["cw"]
    WMD_OUT = os.path.join(MOD, "docs", "plants", "VanillaTextureWidths.md")
    WPNG_OUT = os.path.join(MOD, "docs", "plants", "vanilla_width_compare.png")

    plants.sort(key=lambda p: (-p["cw"], p["name"]))
    wl = []
    wl.append("# 原版植被贴图宽度 / 小人头部贴图 对照")
    wl.append("")
    wl.append("数据源：`RimWorld_Vanilla_Textures_1.6`（原版贴图离线导出）。")
    wl.append("")
    wl.append("口径：**贴图宽度 = 有效内容像素宽度**（alpha≥16 的包围盒宽，去掉透明边距）；"
              "「整图」列为 PNG 原始像素尺寸。基准单位 = 小人头部贴图 "
              "`pawn/humanlike/heads/male/male_average_normal_south.png` 的有效内容宽度 "
              f"= **{wunit} px**。")
    wl.append("")
    wl.append("参考：头部整图 %dx%d、有效 %dx%d；身体（裸男·南）有效 %dx%d = %.2f 单位。"
              % (head["w"], head["h"], head["cw"], head["ch"],
                 body["cw"] if body else 0, body["ch"] if body else 0,
                 (body["cw"] / wunit) if body else 0))
    wl.append("")
    wl.append("| # | 植被（贴图） | 类别 | 整图 px | 有效内容 px | 内容宽 / 头宽 | 宽高比(宽/高) |")
    wl.append("|---|--------------|------|---------|-------------|---------------|---------------|")
    for i, p in enumerate(plants, 1):
        wl.append(f"| {i} | {p['name']} | {p['group']} | {p['w']}x{p['h']} | "
                  f"{p['cw']}x{p['ch']} | **{p['cw'] / wunit:.2f}x** | {p['cw'] / p['ch']:.2f} |")
    wl.append("")

    wl.append("## 分组均值（内容宽 / 头宽）")
    wl.append("")
    wl.append("| 类别 | 件数 | 均值 | 范围 |")
    wl.append("|------|------|------|------|")
    wg = {}
    for p in plants:
        wg.setdefault(p["group"], []).append(p["cw"] / wunit)
    for g in sorted(wg, key=lambda k: -sum(wg[k]) / len(wg[k])):
        v = wg[g]
        wl.append(f"| {g} | {len(v)} | {sum(v) / len(v):.2f}x | {min(v):.2f}~{max(v):.2f}x |")
    wl.append("")

    with open(WMD_OUT, "w", encoding="utf-8") as f:
        f.write("\n".join(wl))
    print(f"宽度表 {len(plants)} 行 -> {WMD_OUT}")

    # ---- 宽度对照图：横向条，1 单位 = 64 px ----
    wscale = 64.0 / wunit
    bar_h = 26
    bgap = 6
    lab_w = 300
    grid_w = int(7 * wunit * wscale)
    W2 = lab_w + grid_w + 40
    H2 = 60 + len(plants) * (bar_h + bgap)
    c2 = Image.new("RGBA", (W2, H2), (24, 26, 30, 255))
    d2 = ImageDraw.Draw(c2)
    for m in range(1, 8):
        x = lab_w + int(m * wunit * wscale)
        d2.line([(x, 46), (x, H2 - 10)], fill=(255, 120, 90, 90), width=1)
        d2.text((x - 8, 30), f"{m}x", fill=(255, 150, 120, 255))
    d2.text((6, 8), f"原版植被贴图宽度 vs 小人头部贴图宽（1 单位 = 头宽 {wunit}px）",
            fill=(255, 255, 255, 255))
    for i, p in enumerate(plants):
        y = 54 + i * (bar_h + bgap)
        bw = max(2, int(p["cw"] * wscale))
        d2.rectangle([lab_w, y, lab_w + bw, y + bar_h], fill=(90, 170, 230, 200))
        d2.text((6, y + 6), f"{p['name']} {p['cw'] / wunit:.2f}x ({p['cw']}px)",
                fill=(225, 228, 235, 255))
    c2.save(WPNG_OUT)
    print(f"宽度对照图 -> {WPNG_OUT}  ({W2}x{H2})")


if __name__ == "__main__":
    main()
