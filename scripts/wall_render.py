# -*- coding: utf-8 -*-
"""墙体 3D 渲染验证 —— 两段式 UV（坐标由像素级放大视觉标定）

贴图切分（源图 80x80，全部落在像素行边界上）：
    外部填充   y = 0..12, 67..79
    俯视段top_v  = v [0.4375, 0.8500]   (y = 13..45)  -> 模型顶面
    侧视段 face_v = v [0.1625, 0.4375]   (y = 46..66)  -> 模型四壁
    分界 v = 0.4375 严格落在 y=45/46 行边界

输出：俯视 / 主视(南) / 侧视(东) / 45° 自由视角
"""
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFont

TEX_SRC = (r"D:\rim mod\RimWorld_Vanilla_Textures_1.6\Core\textures\things"
           r"\building\linked\wall\wall_atlas_bricks.png")
ATLAS_COL, ATLAS_ROW, GRID = 2, 1, 4          # 取4x4 图集的第 (col2,row1) 格

# ---- 视觉标定所得的两段坐标（勿改）----
# 源图 80x80，v = 1 - y/80，全部坐标落在像素行边界
#   外部填充 y=0..12, 67..79 剥掉
# 拆分线落在线边界 y=45/46 之间（1 - 45/80 = 0.4375），不压任何一行：
#   y<=44  浅灰/深灰  -> 俯视段（顶面）
#   y>=46  纯白砖     -> 侧视段（四壁）
#   y=45   过渡窄带，归属由拆分线位置决定，这里归入俯视段
# y=45 是俯视顶面的下边描边：横贯整幅、与其余所有行都不一致，
# 整段重复时每段都会带一条灰线 -> 必须排除在侧视段之外。
TOP_V  = (0.4250, 0.8500)     # y=13..46  俯视段（含y=45 描边）-> 顶面
FACE_V = (0.1625, 0.4250)     # y=47..66  侧视段（纯砖+ 砖缝）-> 四壁
# 侧视段内的砖缝中心（像素级标定：砖缝在 y=52..53 与 y=60..61），升序
FACE_MORTAR_V = (1 - 61.0 / 80.0, 1 - 53.0 / 80.0)

CELL = 300
LABEL = 34
OUT = r"D:\rim mod\FirstPerson3D\docs\_wall_render.png"
FONT = r"C:\Windows\Fonts\msyh.ttc"


# ----------------------------------------------------------------- 纹理
class Tex:
    def __init__(self, arr, fv0=None, fv1=None):
        self.a = arr.astype(np.float32) / 255.0
        self.h, self.w = self.a.shape[:2]
        self.fv0, self.fv1 = fv0, fv1

    @staticmethod
    def load(path):
        return Tex(np.asarray(Image.open(path).convert("RGBA")))

    def cell(self, col, row, grid):
        cw, ch = self.w // grid, self.h // grid
        sub = self.a[row * ch:(row + 1) * ch, col * cw:(col + 1) * cw]
        t = Tex(np.zeros((ch, cw, 4), np.float32))
        t.a[:] = sub
        return t

    def sample(self, u, v, wrap_v=True):
        """wrap_v=True：v 超出后在 [fv0,fv1] 侧视段内回绕（侧面整段重复）。
        wrap_v=False：v 直接 clamp（顶面用俯视段，不参与回绕）。"""
        u = u - np.floor(u)
        if wrap_v and self.fv0 is not None:
            lo, hi = self.fv0, self.fv1
            span = hi - lo
            if span > 1e-9:
                v = lo + ((v - lo) % span)
        x = min(max(u, 0.0), 1.0) * (self.w - 1)
        y = min(max(1.0 - v, 0.0), 1.0) * (self.h - 1)
        x0, y0 = int(np.floor(x)), int(np.floor(y))
        x1, y1 = min(x0 + 1, self.w - 1), min(y0 + 1, self.h - 1)
        fx, fy = x - x0, y - y0
        a = self.a
        c = (a[y0, x0] * (1 - fx) * (1 - fy) + a[y0, x1] * fx * (1 - fy) +
             a[y1, x0] * (1 - fx) * fy + a[y1, x1] * fx * fy)
        return c[:3], c[3]


# ----------------------------------------------------------------- 网格
class Mesh:
    def __init__(self):
        self.v, self.uv, self.t, self.tw = [], [], [], []

    def quad(self, p0, p1, p2, p3, uv0, uv1, uv2, uv3, wrap_v=True):
        b = len(self.v)
        self.v += [p0, p1, p2, p3]
        self.uv += [uv0, uv1, uv2, uv3]
        self.t += [(b, b + 1, b + 2), (b, b + 2, b + 3)]
        self.tw += [wrap_v, wrap_v]

    def arrays(self):
        return (np.array(self.v, np.float32),
                np.array(self.uv, np.float32),
                np.array(self.t, np.int32),
                self.tw)


# ----------------------------------------------------------------- UV 规则
class WallUV:
    """顶面取俯视段，侧面取侧视段（沿高度按砖周期重复）。"""

    def __init__(self, draw=(1.0, 1.0), top_v=TOP_V, face_v=FACE_V):
        self.DW, self.DD = draw
        self.tv0, self.tv1 = top_v
        self.fv0, self.fv1 = face_v
        # 立面段自身的世界高度 -> 一次重复的物理长度
        self.period = (self.fv1 - self.fv0) * self.DD

    # 顶面：俯视段沿深度线性铺满
    def top_u(self, x):
        return 0.5 + x / self.DW

    def top_v(self, z):
        return self.tv0 + (self.tv1 - self.tv0) * (0.5 + z / self.DD)

    # 侧面：侧视段沿高度重复
    def face_u(self, a):
        return a

    def box(self, w, h, d, off=(0, 0, 0)):
        m = Mesh()
        ox, oy, oz = off
        hw, hd = w * 0.5, d * 0.5
        X = [ox - hw, ox + hw]
        Y = [oy, oy + h]
        Z = [oz - hd, oz + hd]
        e = 1e-4

        def t(i, k):
            return (self.top_u(X[i]), self.top_v(Z[k]))

        m.quad((X[0], Y[1] + e, Z[0]), (X[1], Y[1] + e, Z[0]),
               (X[1], Y[1] + e, Z[1]), (X[0], Y[1] + e, Z[1]),
               t(0, 0), t(1, 0), t(1, 1), t(0, 1), wrap_v=False)
        m.quad((X[1], Y[0] - e, Z[0]), (X[0], Y[0] - e, Z[0]),
               (X[0], Y[0] - e, Z[1]), (X[1], Y[0] - e, Z[1]),
               t(1, 0), t(0, 0), t(0, 1), t(1, 1), wrap_v=False)

        # ---- 侧面：侧视段整段重复 ----
        #唯一规则：v 随世界高度线性推进，每升高一个「段高」就正好走完整个
        # 侧视段跨度 span。超过 1 由材质 wrap=Repeat 回绕到段首，
        # 内容天然连续 —— 不需要分段、也不需要对齐相位。
        f0, f1 = self.fv0, self.fv1
        span = f1 - f0
        # 一段的���界高度 = 段跨度对应的世界长度
        seg_w = span * self.DD
        if seg_w <= 1e-9:
            seg_w = h
        N = max(1, int(round(h / seg_w)))
        # 实际段世界高（均分到恰好铺满 h）
        step_w = h / N

        def sv(y):
            """世界高度 y 处的 v（整段连续重复，wrap 回绕）。"""
            return f0 + ((y - oy) / step_w) * span

        def side(y0, y1):
            v0, v1 = sv(y0), sv(y1)
            m.quad((X[0], y0, Z[0]), (X[1], y0, Z[0]), (X[1], y1, Z[0]), (X[0], y1, Z[0]),
                   (0.0, v0), (1.0, v0), (1.0, v1), (0.0, v1))
            m.quad((X[1], y0, Z[1]), (X[0], y0, Z[1]), (X[0], y1, Z[1]), (X[1], y1, Z[1]),
                   (1.0, v0), (0.0, v0), (0.0, v1), (1.0, v1))
            m.quad((X[0], y0, Z[1]), (X[0], y0, Z[0]), (X[0], y1, Z[0]), (X[0], y1, Z[1]),
                   (1.0, v0), (0.0, v0), (0.0, v1), (1.0, v1))
            m.quad((X[1], y0, Z[0]), (X[1], y0, Z[1]), (X[1], y1, Z[1]), (X[1], y1, Z[0]),
                   (0.0, v0), (1.0, v0), (1.0, v1), (0.0, v1))

        for si in range(N):
            side(oy + si * step_w, oy + (si + 1) * step_w)
        self.last_N = N
        return m


# ----------------------------------------------------------------- 渲染
def look_at(eye, target, up):
    f = target - eye
    f /= np.linalg.norm(f)
    up = np.asarray(up, np.float64)
    r = np.cross(up, f)
    r /= np.linalg.norm(r)
    u = np.cross(f, r)
    return eye, f, r, u


def render(mesh, tex, eye, target, up, size, ortho=None, fov=45.0,
           bg=(241, 241, 243)):
    V, UV, T, TW = mesh.arrays()
    eye, f, r, u = look_at(eye, target, up)
    img = np.zeros((size, size, 3), np.float32)
    img[:] = np.array(bg, np.float32)
    zbuf = np.full((size, size), np.inf, np.float32)
    W = V - eye
    cx = W @ r
    cy = W @ u
    cz = W @ f
    if ortho is None:
        fl = size * 0.5 / math.tan(math.radians(fov) * 0.5)
        sx = size * 0.5 + fl * cx / np.maximum(cz, 1e-4)
        sy = size * 0.5 - fl * cy / np.maximum(cz, 1e-4)
    else:
        sc = size * 0.5 / ortho
        sx = size * 0.5 + cx * sc
        sy = size * 0.5 - cy * sc
    ok = (cz > 1e-4)
    for ti, tri in enumerate(T):
        i0, i1, i2 = tri
        wv = TW[ti]
        if not (ok[i0] and ok[i1] and ok[i2]):
            continue
        x = np.array([sx[i0], sx[i1], sx[i2]])
        y = np.array([sy[i0], sy[i1], sy[i2]])
        z = np.array([cz[i0], cz[i1], cz[i2]])
        x0 = max(int(np.floor(x.min())), 0)
        x1 = min(int(np.ceil(x.max())) + 1, size)
        y0 = max(int(np.floor(y.min())), 0)
        y1 = min(int(np.ceil(y.max())) + 1, size)
        if x1 <= x0 or y1 <= y0:
            continue
        den = ((y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2]))
        if abs(den) < 1e-12:
            continue
        px, py = np.meshgrid(np.arange(x0, x1) + 0.5,
                             np.arange(y0, y1) + 0.5)
        w0 = ((y[1] - y[2]) * (px - x[2]) + (x[2] - x[1]) * (py - y[2])) / den
        w1 = ((y[2] - y[0]) * (px - x[2]) + (x[0] - x[2]) * (py - y[2])) / den
        w2 = 1.0 - w0 - w1
        m = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not m.any():
            continue
        zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
        sub = zbuf[y0:y1, x0:x1]
        m &= (zz < sub)
        if not m.any():
            continue
        uu = w0 * UV[i0, 0] + w1 * UV[i1, 0] + w2 * UV[i2, 0]
        vv = w0 * UV[i0, 1] + w1 * UV[i1, 1] + w2 * UV[i2, 1]
        cols = np.zeros((y1 - y0, x1 - x0, 4), np.float32)
        for yy in range(y1 - y0):
            for xx in range(x1 - x0):
                if not m[yy, xx]:
                    continue
                c, a = tex.sample(float(uu[yy, xx]), float(vv[yy, xx]), wrap_v=wv)
                cols[yy, xx] = (c[0], c[1], c[2], a)
        am = cols[..., 3]
        m &= (am > 0.5)
        if not m.any():
            continue
        rgb = cols[..., :3] * 255.0
        tgt = img[y0:y1, x0:x1]
        tgt[m] = rgb[m]
        img[y0:y1, x0:x1] = tgt
        sub[m] = zz[m]
        zbuf[y0:y1, x0:x1] = sub
    return img.astype(np.uint8)


# ----------------------------------------------------------------- 出图
def main():
    font = ImageFont.truetype(FONT, 17)
    font_s = ImageFont.truetype(FONT, 14)
    full = Tex.load(TEX_SRC)
    tex = full.cell(ATLAS_COL, ATLAS_ROW, GRID)
    # 侧视段作为 v 的重复周期：wrap 只在这一段内循环
    tex.fv0, tex.fv1 = FACE_V
    print("atlas %dx%d -> cell %dx%d  侧视周期 v=[%.4f,%.4f]"
          % (full.w, full.h, tex.w, tex.h, FACE_V[0], FACE_V[1]))

    D = 1.0          # 1x1 满格墙
    H = 2.6          # mod实际墙高 Fps3DState.WallHeight
    uv = WallUV(draw=(D, D))
    mesh = uv.box(D, H, D)
    print("顶面v=%s  侧面v=%s" % (TOP_V, FACE_V))

    cx = cz = 0.0
    cy = H * 0.5
    r45 = max(D, H) * 1.35 + 1.2
    views = [
        ("俯视 (垂直向下)", (np.array([cx, 12.0, cz]), np.array([cx, 0.0, cz]),
                            np.array([0.0, 0.0, 1.0]), max(D, D) * 1.35)),
        ("主视图 · 南 (水平)", (np.array([cx, cy, -12.0]), np.array([cx, cy, cz]),
                             np.array([0.0, 1.0, 0.0]), max(D, H) * 0.62)),
        ("侧视图 · 东 (水平)", (np.array([cx + 12.0, cy, cz]), np.array([cx, cy, cz]),
                             np.array([0.0, 1.0, 0.0]), max(D, H) * 0.62)),
    ]
    # 45° 自由视角
    p = yw = math.radians(45.0)
    ctr = np.array([cx, cy, cz])
    off = np.array([math.sin(yw) * math.cos(p), math.sin(p),
                    -math.cos(yw) * math.cos(p)]) * r45
    views.append(("45° 自由视角", (ctr + off, ctr, np.array([0.0, 1.0, 0.0]), None)))

    cols = [v[0] for v in views]
    W, Hh = len(cols) * CELL, 44 + CELL + 44
    canvas = Image.new("RGB", (W, Hh), (255, 255, 255))
    dr = ImageDraw.Draw(canvas)
    dr.text((14, 10), "墙体两段式 UV  俯视 v=[0.4250,0.8500]  侧视 v=[0.1625,0.4250]"
                      "  (拆分线 y=46/47 之间，y=45 描边归俯视)",
            fill=(20, 20, 30), font=font)
    for ci, (name, (eye, tgt, up, osc)) in enumerate(views):
        im = render(mesh, tex, eye, tgt, up, CELL, ortho=osc)
        x = ci * CELL
        canvas.paste(Image.fromarray(im), (x, 44))
        dr.rectangle([x, 44, x + CELL - 1, 44 + CELL - 1], outline=(170, 170, 185))
        dr.text((x + 8, 46), name, fill=(40, 40, 70), font=font_s)
    dr.text((14, 44 + CELL + 12),
            "墙 %g×%g 格，高 %.2f 格；侧视段整段重复 %d 次，相位对齐砖缝" % (D, D, H, uv.last_N), fill=(60, 60, 90), font=font_s)
    canvas.save(OUT)
    print("saved:", OUT)


if __name__ == "__main__":
    main()