# AI生成
# -*- coding: utf-8 -*-
# 组装残骸图集并生成 WreckageUVGen.cs（生成物，请勿手改）
#
# 输入：docs/wreckage/clean/*.png + clean_report.json（含 bbox_after 紧贴剪影框）
# 输出：1.6/Textures/FPS3D/WreckageAtlas.png（RGBA，边缘 clamp 防串色）
#       Source/RimFirstPerson3D/WreckageUVGen.cs
#
# 运行：C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_atlas.py
import json
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CLEAN = os.path.join(ROOT, "docs", "wreckage", "clean")
OUT_PNG = os.path.join(ROOT, "1.6", "Textures", "FPS3D", "WreckageAtlas.png")
OUT_CS = os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageUVGen.cs")
ATLAS_W = 1024
GAP = 2


def load_report():
    p = os.path.join(CLEAN, "clean_report.json")
    with open(p, encoding="utf-8") as f:
        data = json.load(f)
    if isinstance(data, dict):
        for k in ("items", "entries", "list", "results"):
            if k in data and isinstance(data[k], list):
                return data[k]
        # name -> entry
        out = []
        for k, v in data.items():
            if isinstance(v, dict):
                v = dict(v)
                v.setdefault("name", k)
                out.append(v)
        if out:
            return out
        raise SystemExit("无法解析 clean_report.json 结构：%s" % list(data.keys())[:8])
    return data


def entry_name(e):
    for k in ("name", "dst", "src", "file"):
        if k in e and e[k]:
            return os.path.splitext(os.path.basename(str(e[k])))[0]
    raise SystemExit("报告条目缺少名称字段：%s" % e)


def entry_path(e):
    for k in ("dst", "out", "path", "file"):
        if k in e and e[k]:
            p = str(e[k])
            if not os.path.isabs(p):
                p = os.path.join(CLEAN, os.path.basename(p))
            if os.path.exists(p):
                return p
    n = entry_name(e)
    p = os.path.join(CLEAN, n + ".png")
    if os.path.exists(p):
        return p
    raise SystemExit("找不到清理贴图：%s" % n)


def entry_bbox(e, w, h):
    b = e.get("bbox_after") or e.get("bbox")
    if isinstance(b, (list, tuple)) and len(b) == 4:
        x0, y0, x1, y1 = [int(round(float(v))) for v in b]
        x0 = max(0, min(x0, w - 1))
        y0 = max(0, min(y0, h - 1))
        x1 = max(x0 + 1, min(x1 + 1, w))
        y1 = max(y0 + 1, min(y1 + 1, h))
        return x0, y0, x1, y1
    return 0, 0, w, h


def key_of(name):
    # rustedindustrialtruck_south/east/north -> rustedindustrialtruck.south ...
    for suf in ("_south", "_east", "_north", "_west"):
        if name.endswith(suf):
            return name[: -len(suf)] + "." + suf[1:]
    return name


def main():
    rep = load_report()
    blocks = []
    for e in rep:
        name = entry_name(e)
        path = entry_path(e)
        im = Image.open(path).convert("RGBA")
        w, h = im.size
        x0, y0, x1, y1 = entry_bbox(e, w, h)
        crop = im.crop((x0, y0, x1, y1))
        blocks.append({"key": key_of(name), "img": crop, "w": x1 - x0, "h": y1 - y0})

    # shelf 打包：高降序，行内左到右
    blocks.sort(key=lambda b: (-b["h"], -b["w"]))
    cx = cy = row_h = 0
    for b in blocks:
        if cx + b["w"] > ATLAS_W:
            cy += row_h + GAP
            cx = 0
            row_h = 0
        b["x"], b["y"] = cx, cy
        cx += b["w"] + GAP
        row_h = max(row_h, b["h"])
    total_h = cy + row_h
    atlas_h = ((total_h + 3) // 4) * 4

    atlas = np.zeros((atlas_h, ATLAS_W, 4), np.uint8)
    for b in blocks:
        arr = np.asarray(b["img"], np.uint8)
        x, y, w, h = b["x"], b["y"], b["w"], b["h"]
        atlas[y:y + h, x:x + w] = arr
        # 边缘 clamp replication（右 / 下），防双线性与 mip 串色
        if x + w + GAP <= ATLAS_W:
            atlas[y:y + h, x + w:x + w + GAP] = arr[:, w - 1:w]
        if y + h + GAP <= atlas_h:
            atlas[y + h:y + h + GAP, x:x + w] = arr[h - 1:h, :]

    os.makedirs(os.path.dirname(OUT_PNG), exist_ok=True)
    Image.fromarray(atlas, "RGBA").save(OUT_PNG)

    # ---- 生成 C# UV 表 ----
    rows_R = []
    rows_S = []
    for b in sorted(blocks, key=lambda b: b["key"]):
        x, y, w, h = b["x"], b["y"], b["w"], b["h"]
        u0 = x / ATLAS_W
        u1 = (x + w) / ATLAS_W
        v1 = 1.0 - y / atlas_h          # 顶边（Unity v 向上）
        v0 = 1.0 - (y + h) / atlas_h    # 底边
        rows_R.append('            { "%s", new Vector4(%.5ff, %.5ff, %.5ff, %.5ff) },'
                      % (b["key"], u0, v0, u1, v1))
        rows_S.append('            { "%s", new Vector2(%df, %df) },'
                      % (b["key"], w, h))

    cs = []
    cs.append("// 自动生成 —— 由 scripts/wreck_atlas.py 产出，请勿手改。")
    cs.append("// 键 = 残骸样板名（多视图残骸为 \"<名>.<朝向>\"），值 = 图集 UV (u0, v0, u1, v1)。")
    cs.append("// v 口径：Unity 纹理 v=0 在图像**底边**，故 v = 1 - y/H。")
    cs.append("// 源贴图：docs/wreckage/clean/*.png（已程序去黑边）。")
    cs.append("using System.Collections.Generic;")
    cs.append("using UnityEngine;")
    cs.append("")
    cs.append("namespace RimFirstPerson3D")
    cs.append("{")
    cs.append("    public static class WreckageUVGen")
    cs.append("    {")
    cs.append('        public const string TexPath = "FPS3D/WreckageAtlas";')
    cs.append("        public static readonly Dictionary<string, Vector4> R = new Dictionary<string, Vector4>(%d)" % len(rows_R))
    cs.append("        {")
    cs.extend(rows_R)
    cs.append("        };")
    cs.append("")
    cs.append("        /// <summary>取样区在世界里的纵横比（宽/高，源图像素比），用于侧面平铺周期。</summary>")
    cs.append("        public static readonly Dictionary<string, Vector2> SrcSize =")
    cs.append("            new Dictionary<string, Vector2>(%d)" % len(rows_S))
    cs.append("        {")
    cs.extend(rows_S)
    cs.append("        };")
    cs.append("    }")
    cs.append("}")
    cs.append("")
    with open(OUT_CS, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(cs))

    print("atlas %dx%d  blocks=%d" % (ATLAS_W, atlas_h, len(blocks)))
    for b in sorted(blocks, key=lambda b: b["key"]):
        print("  %-34s %4dx%-4d @(%d,%d)" % (b["key"], b["w"], b["h"], b["x"], b["y"]))
    print("saved:", OUT_PNG)
    print("saved:", OUT_CS)


if __name__ == "__main__":
    main()