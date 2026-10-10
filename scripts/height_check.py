# -*- coding: utf-8 -*-
"""生成 Y 轴换算的纯视觉验算图（v2：修正裁切与重叠）。

规则（老板反复强调）：只用眼睛看图判断，不许用数值统计代替看图。
"""
import os
from PIL import Image, ImageDraw, ImageFont

OUT = r"D:/rim mod/FirstPerson3D/docs/_height_scale.png"

PX = 150.0                                     # 每格(=每米)像素

BG     = (250, 250, 252)
INK    = (28, 30, 36)
GRID   = (214, 218, 226)
BLUE   = (58, 106, 190)
RED    = (200, 62, 58)
RED_L  = (240, 176, 174)
ORANGE = (222, 140, 32)
GREEN  = (46, 140, 90)
GREY   = (150, 155, 165)
SLAB   = (150, 158, 172)


def load_font(sz, bold=False):
    for c in (r"C:/Windows/Fonts/msyhbd.ttc" if bold else r"C:/Windows/Fonts/msyh.ttc",
              r"C:/Windows/Fonts/simhei.ttf", r"C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(c):
            try:
                return ImageFont.truetype(c, sz)
            except Exception:
                pass
    return ImageFont.load_default()


F_TITLE = load_font(29, True)
F_HEAD  = load_font(20, True)
F_SM    = load_font(15)
F_TINY  = load_font(13)
F_NUM   = load_font(14, True)


def grid_bg(d, box, ox, oy, unit, xmax, ymax, step=0.25):
    x0, y0, x1, y1 = box
    d.rectangle(box, fill=(255, 255, 255), outline=GRID, width=1)
    m = 0.0
    while m <= xmax + 1e-6:
        px = ox + m * unit
        if x0 <= px <= x1:
            major = abs(m - round(m)) < 1e-6
            d.line([(px, y0), (px, y1)], fill=(INK if major else GRID), width=1)
            if major:
                d.text((px, y1 + 2), f"{m:g}", font=F_TINY, fill=GREY, anchor="ma")
        m += step
    m = 0.0
    while m <= ymax + 1e-6:
        py = oy - m * unit
        if y0 <= py <= y1:
            major = abs(m - round(m)) < 1e-6
            d.line([(x0, py), (x1, py)], fill=(INK if major else GRID), width=1)
            if major:
                d.text((x0 - 4, py), f"{m:g}m", font=F_TINY, fill=GREY, anchor="rm")
        m += step
    d.line([(ox, y0), (ox, y1)], fill=INK, width=2)
    d.line([(x0, oy), (x1, oy)], fill=INK, width=2)


def person(d, ox, oy, unit, scale=1.0, color=INK, eye_label=None, eye=True):
    """标准 1.75m 站立人形（肩宽 0.45m）。"""
    H, SW = 1.75 * unit * scale, 0.45 * unit * scale
    hd, hw = 0.22 * unit * scale, 0.17 * unit * scale
    lw = max(2, int(0.055 * unit * scale))
    d.rectangle([ox - SW * 0.30, oy - H * 0.47, ox - SW * 0.30 + lw, oy], fill=color)
    d.rectangle([ox + SW * 0.30 - lw, oy - H * 0.47, ox + SW * 0.30, oy], fill=color)
    ty0, ty1 = oy - H * 0.82, oy - H * 0.45
    d.rectangle([ox - SW / 2, ty0, ox + SW / 2, ty1], fill=color)
    aw = max(2, int(0.05 * unit * scale))
    d.rectangle([ox - SW / 2 - aw, ty0 + 3, ox - SW / 2, oy - H * 0.42], fill=color)
    d.rectangle([ox + SW / 2, ty0 + 3, ox + SW / 2 + aw, oy - H * 0.42], fill=color)
    d.ellipse([ox - hw / 2, oy - H, ox + hw / 2, oy - H + hd], fill=color)
    if eye:
        ey = oy - 1.62 * unit * scale
        d.line([(ox - SW * 0.9, ey), (ox + SW * 0.9, ey)], fill=RED, width=2)
        if eye_label:
            d.text((ox + SW * 1.0, ey), eye_label, font=F_TINY, fill=RED, anchor="lm")


def lying(d, ox, mattress_y, unit, label, blanket=0.08):
    """躺姿：Pawn 躺在床上睡觉——原版确有此状态（原版是躺平 sprite）。

    侧视剖面：身体沿 X 平放，躺在「床垫面 + 被褥厚」之上。
    原版没有睡者高度概念（PawnRenderer.cs:414 偏移全在 XZ 平面），
    本 mod 3D 化后自定：比床垫面高一个被褥厚度，见 Metrics3D.PawnLyingSurface。
    """
    L = 1.75 * unit
    th = 0.20 * unit
    hd = 0.19 * unit
    top = mattress_y - blanket * unit          # 立牌底边 = 床垫面 + 被褥
    # 被褥层
    d.rectangle([ox - L / 2, top, ox + L / 2, mattress_y], fill=(226, 214, 236))
    # 身体
    d.rectangle([ox - L / 2 + hd, top, ox + L / 2, top + th], fill=RED_L)
    d.ellipse([ox - L / 2, top, ox - L / 2 + hd * 1.2, top + th], fill=RED)
    d.text((ox, top + th + 10), label, font=F_TINY, fill=RED, anchor="ma")
    return top


def chair(d, ox, oy, unit):
    """餐椅：座高 0.45，靠背顶 0.90，座面 0.45x0.45。"""
    s = 0.45 * unit
    bk = 0.04 * unit
    d.rectangle([ox - s / 2, oy - s, ox + s / 2, oy], fill=BLUE)
    d.rectangle([ox - s / 2, oy - 0.90 * unit, ox - s / 2 + bk, oy - s], fill=BLUE)
    d.rectangle([ox + s / 2 - bk, oy - 0.86 * unit, ox + s / 2, oy - s], fill=BLUE)
    for lx in (ox - s / 2 + 0.03 * unit, ox + s / 2 - 0.03 * unit - bk):
        d.rectangle([lx, oy - 0.04 * unit, lx + bk, oy], fill=BLUE)


def table(d, ox, oy, unit, w_m=1.4, dpt_m=0.8, h_m=0.75):
    hpx, wpx, dpx = h_m * unit, w_m * unit, dpt_m * unit
    d.rectangle([ox - wpx / 2, oy - hpx - dpx * 0.26,
                 ox + wpx / 2, oy - hpx + dpx * 0.26], fill=BLUE)
    lx = 0.055 * unit
    for sx in (ox - wpx / 2 + 0.08 * unit, ox + wpx / 2 - 0.08 * unit - lx):
        d.rectangle([sx, oy - hpx, sx + lx, oy], fill=BLUE)


def bed(d, ox, oy, unit, w_m=1.0, len_m=2.0, top_m=1.05):
    wpx, lpx = w_m * unit, len_m * unit
    base, mat, hb = 0.40 * unit, 0.15 * unit, 0.05 * unit
    d.rectangle([ox - lpx / 2, oy - base - mat, ox + lpx / 2, oy - base], fill=BLUE)
    d.rectangle([ox - lpx / 2, oy - base, ox + lpx / 2, oy], fill=SLAB)
    d.rectangle([ox + lpx / 2 - hb, oy - top_m * unit, ox + lpx / 2, oy - base], fill=BLUE)


W, H = 2160, 1268
img = Image.new("RGB", (W, H), (243, 244, 247))
d = ImageDraw.Draw(img)
U = PX

d.text((40, 24), "FirstPerson3D · Y 轴高度约定验算", font=F_TITLE, fill=INK)
d.text((40, 62), "唯一标尺 = 角色贴图的世界长宽；原版 def 数据不作参考。"
                 "常数：1 格 = 1.00 米，三轴同构。", font=F_SM, fill=GREY)

# ---------------- ① 比例自检 ----------------
P1 = (40, 96, 700, 892)
d.rectangle(P1, fill=BG, outline=GRID, width=2)
d.text(((P1[0] + P1[2]) / 2, P1[1] + 10), "①  比例自检：人 vs 家具",
       font=F_HEAD, fill=INK, anchor="ma")
d.text(((P1[0] + P1[2]) / 2, P1[1] + 38),
       "同一比例尺下，站立的人与家具的高度关系", font=F_SM, fill=GREY, anchor="ma")
ox, oy = 140, 812
grid_bg(d, (P1[0] + 8, P1[1] + 68, P1[2] - 8, P1[3] - 8), ox, oy, U, 3.4, 3.4)

table(d, ox + 2.55 * U, oy, U, 1.4, 0.8, 0.75)
d.text((ox + 2.55 * U, oy + 6), "餐桌 0.75", font=F_TINY, fill=BLUE, anchor="ma")
chair(d, ox + 1.55 * U, oy, U)
d.text((ox + 1.55 * U, oy + 6), "餐椅 座.45 背.90", font=F_TINY, fill=BLUE, anchor="ma")
person(d, ox + 0.55 * U, oy, U, eye_label="眼高 1.62")

# ---------------- ② 床的层级拆分 ----------------
P2 = (724, 96, 1584, 892)
d.rectangle(P2, fill=BG, outline=GRID, width=2)
d.text(((P2[0] + P2[2]) / 2, P2[1] + 10), "②  床的层级拆分",
       font=F_HEAD, fill=INK, anchor="ma")
d.text(((P2[0] + P2[2]) / 2, P2[1] + 38),
       "床架 0.40 + 床垫 0.15 = 面 0.55；睡者底边 0.63；床头板 1.05",
       font=F_SM, fill=GREY, anchor="ma")
ox2, oy2 = 824, 812
grid_bg(d, (P2[0] + 8, P2[1] + 68, P2[2] - 8, P2[3] - 8), ox2, oy2, U, 3.4, 3.4)

bed(d, ox2 + 1.55 * U, oy2, U, 1.0, 2.0)
d.text((ox2 + 1.55 * U, oy2 + 6), "单人床 1.0 x 2.0", font=F_TINY, fill=BLUE, anchor="ma")
top = lying(d, ox2 + 1.55 * U, oy2 - 0.55 * U, U,
            "Pawn 躺睡（原版唯一卧姿）", blanket=0.08)
bx_right = ox2 + 1.55 * U + 1.0 * U      # 床体右缘

# 标注：床垫面 0.55 与睡者底边 0.63 之间的被褥层。
# 0.55~0.63 这条缝只有 12px，放不下字——改成在床右侧做「放大镜」局部放大。
gap_bot = oy2 - 0.55 * U
gap_top = top
d.line([(bx_right, gap_top), (bx_right, gap_bot)], fill=(150, 120, 180), width=3)
d.line([(bx_right - 10, gap_top), (bx_right + 10, gap_top)], fill=(120, 90, 160), width=2)
d.line([(bx_right - 10, gap_bot), (bx_right + 10, gap_bot)], fill=BLUE, width=2)

# 局部放大框（放在床右侧空白区，避开身高尺 hx = ox2+2.95U）
# 0.55~0.63 只有 12px 放不下字，这里按 6x 放大画出层次。
mag_x, mag_w, mag_h = bx_right + 14, 128, 150
mag_y = oy2 - 1.20 * U
d.rectangle([mag_x, mag_y, mag_x + mag_w, mag_y + mag_h],
            fill=(252, 250, 255), outline=(150, 120, 180), width=2)
t_h, m_h, b_h = mag_h * 0.46, mag_h * 0.30, mag_h * 0.24   # 睡者 / 被褥 / 床垫
d.rectangle([mag_x, mag_y, mag_x + mag_w, mag_y + t_h], fill=RED_L)
d.rectangle([mag_x, mag_y + t_h, mag_x + mag_w, mag_y + t_h + m_h],
            fill=(226, 214, 236))
d.rectangle([mag_x, mag_y + t_h + m_h, mag_x + mag_w, mag_y + mag_h],
            fill=BLUE)
d.text((mag_x + mag_w + 6, mag_y + t_h / 2), "睡者底边 0.63", font=F_TINY,
       fill=(120, 90, 160), anchor="lm")
d.text((mag_x + mag_w + 6, mag_y + t_h + m_h / 2), "被褥 0.08", font=F_TINY,
       fill=(140, 110, 170), anchor="lm")
d.text((mag_x + mag_w + 6, mag_y + t_h + m_h + b_h / 2), "床垫面 0.55",
       font=F_TINY, fill=BLUE, anchor="lm")
d.text((mag_x + mag_w + 6, mag_y - 4), "6x", font=F_TINY,
       fill=(150, 120, 180), anchor="lb")

# 身高对比尺（0.55 与 0.63 只差 8px，标签要错开才不重叠）
hx = mag_x + mag_w + 130
d.line([(hx, oy2), (hx, oy2 - 1.75 * U)], fill=INK, width=4)
for hh, col, lbl, dy in ((0.00, GREY, "0.00", 0), (0.55, BLUE, "0.55", -9),
                         (0.63, (120, 90, 160), "0.63", 9),
                         (1.05, ORANGE, "1.05", 0),
                         (1.62, RED, "1.62", -9), (1.75, INK, "1.75", 0)):
    py = oy2 - hh * U
    d.line([(hx - 9, py), (hx + 9, py)], fill=col, width=2)
    d.text((hx + 14, py + dy), lbl, font=F_NUM, fill=col, anchor="lm")

# ---------------- ③ 室内剖面 ----------------
P3 = (1608, 96, 2140, 892)
d.rectangle(P3, fill=BG, outline=GRID, width=2)
d.text(((P3[0] + P3[2]) / 2, P3[1] + 10), "③  室内剖面自洽性",
       font=F_HEAD, fill=INK, anchor="ma")
d.text(((P3[0] + P3[2]) / 2, P3[1] + 38),
       "净高 2.70 / 门 2.05 / 窗台 .90+ 高 1.20", font=F_SM, fill=GREY, anchor="ma")

ox3, oy3 = 1672, 812
rh = 2.70 * U
rw = 2.30 * U
d.rectangle([ox3, oy3 - rh, ox3 + rw, oy3], fill=(255, 255, 255), outline=INK, width=3)
d.rectangle([ox3 - 6, oy3, ox3 + rw + 6, oy3 + 0.10 * U], fill=SLAB)          # 地板
d.rectangle([ox3 - 6, oy3 - rh - 0.12 * U, ox3 + rw + 6, oy3 - rh], fill=SLAB)  # 天花

for m in [i * 0.25 for i in range(0, 12)]:
    py = oy3 - m * U
    if oy3 - rh <= py <= oy3:
        d.line([(ox3, py), (ox3 + rw, py)], fill=(236, 238, 242), width=1)
        d.text((ox3 - 5, py), f"{m:g}", font=F_TINY, fill=GREY, anchor="rm")

dw, dh = 0.80 * U, 2.05 * U
dx = ox3 + 0.18 * U
d.rectangle([dx, oy3 - dh, dx + dw, oy3], fill=BG, outline=SLAB, width=2)
d.text((dx + dw / 2, oy3 - dh - 5), "门 2.05", font=F_TINY, fill=GREY, anchor="md")

wx, ww = ox3 + 1.42 * U, 0.62 * U
d.rectangle([wx, oy3 - 0.90 * U - 1.20 * U, wx + ww, oy3 - 0.90 * U],
            fill=(190, 218, 240), outline=(80, 120, 160), width=2)
d.text((wx + ww / 2, oy3 - 0.90 * U - 1.20 * U - 5), "窗台 .90 + 高 1.20",
       font=F_TINY, fill=(60, 100, 140), anchor="md")

person(d, ox3 + 0.62 * U, oy3, U * 0.80, eye=False)
rx = ox3 + rw - 0.20 * U
d.line([(rx, oy3), (rx, oy3 - 1.05 * U)], fill=ORANGE, width=4)
d.text((rx + 6, oy3 - 1.05 * U), "栏杆 1.05", font=F_TINY, fill=ORANGE, anchor="lb")

ax = ox3 + rw + 14
d.line([(ax, oy3), (ax + 8, oy3)], fill=GREEN, width=2)
d.line([(ax + 8, oy3), (ax + 8, oy3 - rh)], fill=GREEN, width=2)
d.line([(ax, oy3 - rh), (ax + 16, oy3 - rh)], fill=GREEN, width=2)
d.text((ax + 18, oy3 - rh / 2), "净高\n2.70", font=F_NUM, fill=GREEN, anchor="lm")

# ---------------- 底部刻度尺 ----------------
BY0, BY1 = 918, 1200
d.rectangle((40, BY0, 2140, BY1), fill=(255, 255, 255), outline=GRID, width=2)
d.text((58, BY0 + 10), "Y 轴高度刻度尺（1 格 = 1.00 米）", font=F_HEAD, fill=INK)

RULER = [
    ("踢脚", 0.10, GREY), ("窗台", 0.90, (60, 100, 140)),
    ("栏杆 / 吧台 / 床头板", 1.05, ORANGE), ("床垫面", 0.55, BLUE),
    ("睡者底边", 0.63, (120, 90, 160)), ("桌面", 0.75, BLUE),
    ("成人眼高", 1.62, RED), ("成人身高", 1.75, INK),
    ("门", 2.05, GREEN), ("室内净高", 2.70, GREEN),
]
bx0, bx1, mx = 340, 2100, 3.0
span = bx1 - bx0
d.line([(bx0, BY0 + 62), (bx1, BY0 + 62)], fill=INK, width=2)
for i, (name, val, col) in enumerate(RULER):
    x = bx0 + val / mx * span
    d.line([(x, BY0 + 42), (x, BY0 + 62)], fill=col, width=3)
    d.ellipse([x - 4, BY0 + 58, x + 4, BY0 + 66], fill=col)
    up = (i % 2 == 0)
    ty = BY0 + 88 if up else BY1 - 46
    d.text((x, ty), f"{name} {val:.2f}", font=F_TINY, fill=col,
           anchor="ma" if up else "md")
for m in [i * 0.5 for i in range(0, 7)]:
    x = bx0 + m / mx * span
    d.line([(x, BY0 + 56), (x, BY0 + 68)], fill=INK, width=2)
    d.text((x, BY0 + 70), f"{m:g}m", font=F_TINY, fill=GREY, anchor="ma")

d.text((58, BY1 - 30),
       "换算规则：Y(格) = 现实尺寸(米) / 1.00。X / Z 同理——1 格宽的格子里站一个 1.75 格高的人，"
       "肩宽 0.45 格，",
       font=F_SM, fill=GREY)
d.text((58, BY1 - 10),
       "人体占格宽 45% 居中；桌面 0.75 格落在人体胯部。"
       "原版无「坐」这一状态，Pawn 只有站立与躺睡两种姿态。",
       font=F_SM, fill=GREY)
d.text((58, BY1 + 8),
       "原版没有「睡者高度」概念（PawnRenderer.cs:414 的床偏移全在 XZ 平面），"
       "3D 化后自定为床垫面 0.55 + 被褥 0.08 = 0.63。",
       font=F_SM, fill=(120, 90, 160))

img.save(OUT)
print("written:", OUT)
