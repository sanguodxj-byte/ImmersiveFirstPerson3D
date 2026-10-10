# AI生成
# -*- coding: utf-8 -*-
# 复刻 Hand3D.BuildHandTexture（64x64 像素手：前臂 + 手掌 + 四指 + 拇指），导出 PNG 预览。
import zlib, struct, os

OUT = r"D:/rim mod/FirstPerson3D/shots"
os.makedirs(OUT, exist_ok=True)

S = 64
SKIN = (0.85, 0.67, 0.55)

m = [[False] * S for _ in range(S)]


def capsule(ax, ay, bx, by, r):
    dx, dy = bx - ax, by - ay
    l2 = dx * dx + dy * dy
    for y in range(S):
        for x in range(S):
            t = 0.0 if l2 <= 0 else max(0.0, min(1.0, ((x - ax) * dx + (y - ay) * dy) / l2))
            px, py = ax + t * dx, ay + t * dy
            if (x - px) ** 2 + (y - py) ** 2 <= r * r:
                m[y][x] = True


capsule(36.8, 44, 44, 66, 6.6)        # 前臂（向下出界，粗杆）
capsule(28.4, 30, 46.4, 42, 7.2)      # 手掌（加高，承接五指）
capsule(27.2, 8, 27.2, 34, 3.24)      # 食指
capsule(36.2, 3, 36.2, 34, 3.24)      # 中指（最长）
capsule(45.2, 6, 45.2, 34, 3.24)      # 无名指
capsule(54.2, 11, 54.2, 34, 3.24)     # 小指（最短）
capsule(27.2, 36, 10.4, 27, 4.2)      # 拇指（向左上伸出）


def solid(x, y):
    return 0 <= x < S and 0 <= y < S and m[y][x]


def C(r, g, b, a=255):
    return (max(0, min(255, int(round(r * 255)))),
            max(0, min(255, int(round(g * 255)))),
            max(0, min(255, int(round(b * 255)))), a)


mid = C(*SKIN, 255)
dark = C(SKIN[0] * 0.78, SKIN[1] * 0.78, SKIN[2] * 0.78, 255)
light = C(min(1, SKIN[0] * 1.12), min(1, SKIN[1] * 1.12), min(1, SKIN[2] * 1.12), 255)
edge = C(0.08, 0.06, 0.05, 255)

img = [[(0, 0, 0, 0) for _ in range(S)] for _ in range(S)]
for y in range(S):
    for x in range(S):
        if m[y][x]:
            nearRB = (not (solid(x + 1, y) and solid(x + 2, y))) or (not (solid(x, y + 1) and solid(x, y + 2)))
            nearLT = (not (solid(x - 1, y) and solid(x - 2, y))) or (not (solid(x, y - 1) and solid(x, y - 2)))
            img[y][x] = dark if nearRB else (light if nearLT else mid)
        else:
            adj = any(solid(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1))
            img[y][x] = edge if adj else (0, 0, 0, 0)


def write_png(path, w, h, rows):
    raw = bytearray()
    for row in rows:
        raw.append(0)
        for (R, G, B, A) in row:
            raw += bytes((R, G, B, A))

    def chunk(typ, data):
        return struct.pack('>I', len(data)) + typ + data + struct.pack('>I', zlib.crc32(typ + data) & 0xffffffff)

    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n')
        f.write(chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)))
        f.write(chunk(b'IDAT', zlib.compress(bytes(raw), 9)))
        f.write(chunk(b'IEND', b''))


# ① 8x 最近邻放大（透明背景）；PNG 行自上而下 = yDown，与设计一致
f = 8
big = []
for row in img:
    orow = []
    for p in row:
        orow += [p] * f
    big += [list(orow)] * f
write_png(os.path.join(OUT, "hand_texture_x8.png"), S * f, S * f, big)

# ② 合成到 1280x720 深色画面右下角（size=H*0.34，x=W-0.66*size，y=H-0.86*size）
W, H = 1280, 720
canvas = [[(34, 36, 40, 255) for _ in range(W)] for _ in range(H)]
size = int(H * 0.42)
x0 = int(W - size * 0.72)
y0 = int(H - size * 0.90)
for oy in range(size):
    sy = oy * S // size
    for ox in range(size):
        sx = ox * S // size
        R, G, B, A = img[sy][sx]
        cy, cx = y0 + oy, x0 + ox
        if 0 <= cy < H and 0 <= cx < W and A > 0:
            a = A / 255.0
            bg = canvas[cy][cx]
            canvas[cy][cx] = (int(R * a + bg[0] * (1 - a)),
                              int(G * a + bg[1] * (1 - a)),
                              int(B * a + bg[2] * (1 - a)), 255)
write_png(os.path.join(OUT, "hand_preview_1280x720.png"), W, H, canvas)

from collections import Counter
cnt = Counter(p for row in img for p in row)
print("mid", cnt[mid], "dark", cnt[dark], "light", cnt[light], "edge", cnt[edge], "clear", cnt[(0, 0, 0, 0)])
print("OK")
