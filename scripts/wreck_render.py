# AI生成
# -*- coding: utf-8 -*-
# 残骸样板预览渲染器：解析 Source/RimFirstPerson3D/WreckageSamples.cs 的几何，
# 用正交 z-buffer 平面着色渲染「45 度 / 正视 / 俯视」三视图，出对照表。
#
# 几何单一真源 = WreckageSamples.cs（本脚本只读不写 C#）。
# 运行：C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_render.py
import math
import os
import re

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CS = os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageSamples.cs")
CLEAN = os.path.join(ROOT, "docs", "wreckage", "clean")
OUTDIR = os.path.join(ROOT, "docs", "wreckage", "samples")
CELL = 420
HEAD = 70
FONT = r"C:\Windows\Fonts\msyh.ttc"
LIGHT = np.array([0.42, 0.82, 0.38], np.float64)
LIGHT /= np.linalg.norm(LIGHT)

RE_SUMMARY = re.compile(r"@tex\s+(\S+)\s+@footprint\s+([\d.]+)\s+([\d.]+)")
RE_METHOD = re.compile(r"public static Model3D (Build\w+)\(\)")
RE_ADD = re.compile(
    r"m\.Add\(Prim\.(\w+),\s*new Vector3\(([^)]*)\),\s*new Vector3\(([^)]*)\)(.*?)\);")


def pf(s):
    return [float(x.strip().rstrip("fF")) for x in s.split(",") if x.strip()]


def parse_cs():
    samples = []
    cur = None
    pending = {}
    with open(CS, encoding="utf-8") as f:
        for line in f:
            ms = RE_SUMMARY.search(line)
            if ms:
                pending = {"tex": ms.group(1),
                           "foot": (float(ms.group(2)), float(ms.group(3)))}
            mm = RE_METHOD.search(line)
            if mm:
                cur = {"method": mm.group(1), "tex": pending.get("tex"),
                       "foot": pending.get("foot"), "parts": []}
                samples.append(cur)
            ma = RE_ADD.search(line)
            if ma and cur is not None:
                pos = pf(ma.group(2))
                size = pf(ma.group(3))
                yaw = pitch = 0.0
                nums = re.findall(r"([-\d.]+)f", ma.group(4))
                if len(nums) >= 1:
                    yaw = float(nums[0])
                if len(nums) >= 2:
                    pitch = float(nums[1])
                cm = re.search(r"//\s*(.+)$", line)
                cur["parts"].append({"kind": ma.group(1), "pos": pos, "size": size,
                                     "yaw": yaw, "pitch": pitch,
                                     "tag": (cm.group(1).strip() if cm else "")})
    return samples


def rot_yxz(yaw_deg, pitch_deg):
    y = math.radians(yaw_deg)
    x = math.radians(pitch_deg)
    Ry = np.array([[math.cos(y), 0, math.sin(y)],
                   [0, 1, 0],
                   [-math.sin(y), 0, math.cos(y)]], np.float64)
    Rx = np.array([[1, 0, 0],
                   [0, math.cos(x), -math.sin(x)],
                   [0, math.sin(x), math.cos(x)]], np.float64)
    return Ry @ Rx


def box_tris(w, h, d, col):
    a, b, c = w / 2, h, d / 2
    V = [(-a, 0, -c), (a, 0, -c), (a, 0, c), (-a, 0, c),
         (-a, b, -c), (a, b, -c), (a, b, c), (-a, b, c)]
    F = [(0, 1, 2, 3, (0, -1, 0)), (4, 7, 6, 5, (0, 1, 0)),
         (0, 4, 5, 1, (0, 0, -1)), (2, 6, 7, 3, (0, 0, 1)),
         (0, 3, 7, 4, (-1, 0, 0)), (1, 5, 6, 2, (1, 0, 0))]
    out = []
    for q in F:
        n = np.array(q[4], float)
        out.append((V[q[0]], V[q[1]], V[q[2]], n, col))
        out.append((V[q[0]], V[q[2]], V[q[3]], n, col))
    return out


def cylinder_tris(dia, h, seg, col):
    r = dia / 2
    out = []
    ring0 = [(r * math.cos(2 * math.pi * i / seg), 0.0,
              r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
    ring1 = [(x, h, z) for (x, _, z) in ring0]
    for i in range(seg):
        j = (i + 1) % seg
        n = np.array([math.cos(2 * math.pi * (i + 0.5) / seg), 0,
                      math.sin(2 * math.pi * (i + 0.5) / seg)])
        out.append((ring0[i], ring0[j], ring1[j], n, col))
        out.append((ring0[i], ring1[j], ring1[i], n, col))
    for i in range(1, seg - 1):
        out.append((ring1[0], ring1[i], ring1[i + 1], np.array([0, 1, 0.]), col))
    for i in range(1, seg - 1):
        out.append((ring0[0], ring0[i + 1], ring0[i], np.array([0, -1, 0.]), col))
    return out


def cone_tris(dia, h, seg, col):
    r = dia / 2
    base = [(r * math.cos(2 * math.pi * i / seg), 0.0,
             r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]
    apex = (0.0, h, 0.0)
    out = []
    for i in range(seg):
        j = (i + 1) % seg
        e = np.array(base[j]) - np.array(base[i])
        n = np.cross(e, np.array(apex) - np.array(base[i]))
        nn = np.linalg.norm(n)
        n = n / nn if nn > 1e-9 else np.array([0, 1., 0])
        out.append((base[i], base[j], apex, n, col))
    for i in range(1, seg - 1):
        out.append((base[0], base[i + 1], base[i], np.array([0, -1, 0.]), col))
    return out


def wedge_tris(w, h, d, col):
    a, c = w / 2, d / 2
    V = [(-a, 0, -c), (a, 0, -c), (a, h, c), (-a, h, c), (a, 0, c), (-a, 0, c)]
    T = [(V[0], V[1], V[2], (0, 1, 0)), (V[0], V[2], V[3], (0, 1, 0)),
         (V[4], V[5], V[3], (0, 0, 1)), (V[4], V[3], V[2], (0, 0, 1)),
         (V[5], V[4], V[2], (0, 1, 0)), (V[5], V[2], V[1], (0, 1, 0)),
         (V[0], V[1], V[4], (0, -1, 0)), (V[0], V[4], V[5], (0, -1, 0)),
         (V[1], V[4], V[2], (1, 0, 0)), (V[0], V[5], V[3], (-1, 0, 0))]
    return [(p, q, r, np.array(n, float), col) for (p, q, r, n) in T]


def part_tris(p, col):
    k, size = p["kind"], p["size"]
    if k == "Box":
        t = box_tris(size[0], size[1], size[2], col)
    elif k == "Cylinder":
        t = cylinder_tris(size[0], size[1], 12, col)
    elif k == "Cone":
        t = cone_tris(size[0], size[1], 12, col)
    elif k == "Wedge":
        t = wedge_tris(size[0], size[1], size[2], col)
    else:
        t = box_tris(size[0], size[1], size[2], col)
    R = rot_yxz(p["yaw"], p["pitch"])
    off = np.array([p["pos"][0], p["pos"][1], p["pos"][2]], np.float64)
    out = []
    for (v0, v1, v2, n, c) in t:
        m = [R @ np.array(v, float) + off for v in (v0, v1, v2)]
        out.append((m[0], m[1], m[2], R @ n, c))
    return out


def base_color(tex):
    for cand in (tex, tex.split("_")[0]):
        p = os.path.join(CLEAN, cand + ".png")
        if os.path.exists(p):
            a = np.asarray(Image.open(p).convert("RGBA"), np.float64)
            m = a[..., 3] > 40
            if m.any():
                return a[..., :3][m].mean(axis=0) / 255.0
    return np.array([0.62, 0.60, 0.55])


def render(tris, eye, target, up, size):
    eye = np.asarray(eye, float)
    f = np.asarray(target, float) - eye
    f /= np.linalg.norm(f)
    up = np.asarray(up, float)
    r = np.cross(f, up)
    r /= np.linalg.norm(r)
    u = np.cross(r, f)
    img = np.zeros((size, size, 3), np.float32)
    img[:] = np.array([243, 243, 246], np.float32)
    zbuf = np.full((size, size), np.inf, np.float64)
    # 自动取景：按相机平面投影包围盒定比例并居中
    pj = []
    for t in tris:
        for v in (t[0], t[1], t[2]):
            W = np.asarray(v, float) - eye
            pj.append((np.dot(W, r), np.dot(W, u)))
    pj = np.array(pj)
    if pj.size == 0:
        return img.astype(np.uint8)
    cxm = (pj[:, 0].min() + pj[:, 0].max()) * 0.5
    cym = (pj[:, 1].min() + pj[:, 1].max()) * 0.5
    half = max(pj[:, 0].max() - pj[:, 0].min(),
               pj[:, 1].max() - pj[:, 1].min()) * 0.5
    sc = size * 0.5 * 0.86 / max(half, 1e-6)
    for (v0, v1, v2, n, col) in tris:
        n = np.asarray(n, float)
        nn = np.linalg.norm(n)
        shade = 0.34 + 0.66 * max(0.0, float(np.dot(n / nn, LIGHT))) if nn > 1e-9 else 0.5
        c = np.clip(np.asarray(col, float) * shade * 255.0, 0, 255)
        pts = []
        for v in (v0, v1, v2):
            W = np.asarray(v, float) - eye
            pts.append((size * 0.5 + (np.dot(W, r) - cxm) * sc,
                        size * 0.5 - (np.dot(W, u) - cym) * sc,
                        np.dot(W, f)))
        x = np.array([pts[0][0], pts[1][0], pts[2][0]])
        y = np.array([pts[0][1], pts[1][1], pts[2][1]])
        z = np.array([pts[0][2], pts[1][2], pts[2][2]])
        if min(z) <= 1e-4:
            continue
        x0 = max(int(np.floor(x.min())), 0)
        x1 = min(int(np.ceil(x.max())) + 1, size)
        y0 = max(int(np.floor(y.min())), 0)
        y1 = min(int(np.ceil(y.max())) + 1, size)
        if x1 <= x0 or y1 <= y0:
            continue
        den = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(den) < 1e-12:
            continue
        px, py = np.meshgrid(np.arange(x0, x1) + 0.5, np.arange(y0, y1) + 0.5)
        w0 = ((y[1] - y[2]) * (px - x[2]) + (x[2] - x[1]) * (py - y[2])) / den
        w1 = ((y[2] - y[0]) * (px - x[2]) + (x[0] - x[2]) * (py - y[2])) / den
        w2 = 1.0 - w0 - w1
        m = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not m.any():
            continue
        zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
        sub = zbuf[y0:y1, x0:x1]
        m &= (zz < sub)
        if not m.any():
            continue
        tgt = img[y0:y1, x0:x1]
        tgt[m] = c
        img[y0:y1, x0:x1] = tgt
        sub[m] = zz[m]
        zbuf[y0:y1, x0:x1] = sub
    return img.astype(np.uint8)


def main():
    os.makedirs(OUTDIR, exist_ok=True)
    samples = parse_cs()
    font = ImageFont.truetype(FONT, 18)
    font_s = ImageFont.truetype(FONT, 15)
    sheets = []
    print("parsed %d samples" % len(samples))
    for s in samples:
        col = base_color(s["tex"])
        tris = []
        for p in s["parts"]:
            tris += part_tris(p, col)
        allv = np.array([v for t in tris for v in (t[0], t[1], t[2])], float)
        lo, hi = allv.min(axis=0), allv.max(axis=0)
        ctr = (lo + hi) / 2
        span = float(np.max(hi - lo))
        views = [
            ("45 deg", np.array([ctr[0] + span, ctr[1] + span * 0.85, ctr[2] - span]),
             ctr, np.array([0, 1., 0])),
            ("front / south", np.array([ctr[0], ctr[1], ctr[2] - span * 2.2]),
             ctr, np.array([0, 1., 0])),
            ("top / down", np.array([ctr[0], ctr[1] + span * 2.2, ctr[2]]),
             ctr, np.array([0, 0., 1])),
        ]
        foot = s["foot"] or (1.0, 1.0)
        W, H = len(views) * CELL, HEAD + CELL + 30
        canvas = Image.new("RGB", (W, H), (255, 255, 255))
        dr = ImageDraw.Draw(canvas)
        dr.text((12, 8), "%s    tex=%s    footprint %.2f x %.2f cells    parts=%d"
                % (s["method"][5:], s["tex"], foot[0], foot[1], len(s["parts"])),
                fill=(20, 20, 30), font=font)
        for ci, (name, eye, tgt, up) in enumerate(views):
            im = render(tris, eye, tgt, up, CELL)
            x = ci * CELL
            canvas.paste(Image.fromarray(im), (x, HEAD))
            dr.rectangle([x, HEAD, x + CELL - 1, HEAD + CELL - 1], outline=(170, 170, 185))
            dr.text((x + 8, 48), name, fill=(40, 40, 70), font=font_s)
        dr.text((12, HEAD + CELL + 8),
                "geometry from WreckageSamples.cs; flat-shaded, shape check only.",
                fill=(90, 90, 110), font=font_s)
        canvas.save(os.path.join(OUTDIR, s["method"][5:] + ".png"))
        sheets.append(canvas)
        print("  ", s["method"][5:])
    if sheets:
        total_w = max(c.width for c in sheets)
        total_h = sum(c.height for c in sheets)
        allimg = Image.new("RGB", (total_w, total_h), (255, 255, 255))
        yy = 0
        for c in sheets:
            allimg.paste(c, (0, yy))
            yy += c.height
        allimg.save(os.path.join(OUTDIR, "_all.png"))
        print("saved:", os.path.join(OUTDIR, "_all.png"))


if __name__ == "__main__":
    main()