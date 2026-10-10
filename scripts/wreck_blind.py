# AI生成
# -*- coding: utf-8 -*-
# 盲测渲染器：复用 wreck_render 的解析/渲染，把每个样板渲染成
# 「无任何文字标签」的三视图拼接图，用随机中性文件名输出到指定目录，
# 供子代理盲辨识（文件名/路径不含语义信息）。
# 运行：python scripts/wreck_blind.py <out_dir>
import json
import os
import random
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import wreck_render as R  # noqa: E402

CELL = 460


def render_sample(key, parts):
    col = R.base_color(key)
    tris = []
    for p in parts:
        tris += R.part_tris(p, col)
    allv = np.array([v for t in tris for v in (t[0], t[1], t[2])], float)
    lo, hi = allv.min(axis=0), allv.max(axis=0)
    ctr = (lo + hi) / 2
    span = float(np.max(hi - lo))
    views = [
        np.array([ctr[0] + span, ctr[1] + span * 0.85, ctr[2] - span]),  # 45
        np.array([ctr[0], ctr[1], ctr[2] - span * 2.2]),                  # front
        np.array([ctr[0], ctr[1] + span * 2.2, ctr[2]]),                  # top
    ]
    ups = [np.array([0, 1., 0]), np.array([0, 1., 0]), np.array([0, 0., 1])]
    imgs = [R.render(tris, v, ctr, u, CELL) for v, u in zip(views, ups)]
    canvas = Image.new("RGB", (CELL * 3 + 16, CELL), (255, 255, 255))
    for i, im in enumerate(imgs):
        canvas.paste(Image.fromarray(im), (i * (CELL + 8), 0))
    return canvas


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    samples = R.parse_cs()
    rng = random.Random(20261010)
    names = []
    used = set()
    alphabet = "abcdefghjkmnpqrstuvwxyz23456789"
    for _ in samples:
        while True:
            n = "".join(rng.choice(alphabet) for _ in range(6)) + ".png"
            if n not in used:
                used.add(n)
                break
        names.append(n)
    mapping = {}
    for s, n in zip(samples, names):
        key = s["method"][5:]
        img = render_sample(key, s["parts"])
        img.save(os.path.join(out, n))
        mapping[n] = key
    with open(os.path.join(out, "_map_PRIVATE.json"), "w", encoding="utf-8") as f:
        json.dump(mapping, f, ensure_ascii=False, indent=1)
    print("OUT", out)
    print(json.dumps(mapping, ensure_ascii=True))


if __name__ == "__main__":
    main()