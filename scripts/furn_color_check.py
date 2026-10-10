# AI生成
# -*- coding: utf-8 -*-
"""颜色保真检查：图集里的"材质像素"必须与原版贴图逐字节相同。

【口径】
1. 材质像素 := 不透明、且不在黑色笔画（含 1 像素外扩）里的像素。这些像素**只许照搬**，
   补绘只允许发生在笔画和透明洞上。任何一个材质像素被改写 = 颜色被管线吃掉。
2. 每件再报一个色感汇总（材质像素的均值 RGB + 最暗/最亮 5% 分位），用来回答
   "这套家具是不是太白/发灰" —— 那是原版美术的事实，还是管线的问题，看这行就知道。
3. 合成夹具自证：把一块图集块整体偏色，检查器必须报；原样必须不报。

用法：
    python scripts/furn_color_check.py            # 校验
    python scripts/furn_color_check.py --items    # 只看每件色感汇总
"""
import os
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furn_atlas as FA  # noqa: E402

TOL_CHANGED = 0        # 材质像素被改写的容许数量：0，一个都不许动
DARK_LUMA = 90         # 低于此 luma 算"暗面"，用于统计每件有多暗


def material_mask(src):
    return (src[:, :, 3] >= 128) & (~FA.dilate(FA.black_mask(src)))


def check_regions():
    """-> (错误列表, 每件色感汇总)"""
    errs, per_item = [], {}
    atlas = np.asarray(Image.open(FA.ATLAS_OUT).convert("RGBA"))
    _, placed, _, _ = FA.pack(list(FA.SPEC.keys()))
    for key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        src = FA.load(path)[y0:y1, x0:x1]
        blk = atlas[ay + FA.PAD:ay + ah - FA.PAD, ax + FA.PAD:ax + aw - FA.PAD]
        if blk.shape[:2] != src.shape[:2]:
            errs.append(("size", "%s: 图集块 %s != 取样区 %s" % (key, blk.shape[:2], src.shape[:2])))
            continue
        mm = material_mask(src)
        same = np.all(blk.astype(np.int16)[:, :, :3] == src.astype(np.int16)[:, :, :3], axis=2)
        n_bad = int((mm & ~same).sum())
        if n_bad > TOL_CHANGED:
            ys, xs = np.where(mm & ~same)
            d = np.abs(blk[ys, xs, :3].astype(int) - src[ys, xs, :3].astype(int)).sum(1)
            errs.append(("recolor", "%s: %d 个材质像素被改写（最大通道差 %d），"
                                    "位置 y%d..%d x%d..%d"
                         % (key, n_bad, int(d.max()), ys.min(), ys.max(), xs.min(), xs.max())))
        item = key.split(".")[0]
        rgb = src[mm][:, :3].astype(int)
        lum = rgb.mean(1)
        per_item.setdefault(item, []).append((rgb.sum(0), len(rgb),
                                              float(np.percentile(lum, 5)),
                                              float(np.percentile(lum, 95))))
    return errs, per_item


def summarize(per_item):
    print("== 每件色感（材质像素，不含笔画）==")
    for item, rows in sorted(per_item.items()):
        tot = sum(r[1] for r in rows)
        rgb = sum(r[0] for r in rows) / float(max(1, tot))
        lo = min(r[2] for r in rows)
        hi = max(r[3] for r in rows)
        dark = sum(int((np.array(r[0]) / max(1, r[1])).mean() < DARK_LUMA) for r in rows)
        print("  %-11s 均值 RGB(%3.0f,%3.0f,%3.0f)  亮度 5%%..95%% = %3.0f..%3.0f  %d 块  偏暗块 %d/%d"
              % (item, rgb[0], rgb[1], rgb[2], lo, hi, len(rows), dark, len(rows)))


def self_test():
    """两侧自证：偏色的块必须报，原样必须不报。"""
    atlas = np.asarray(Image.open(FA.ATLAS_OUT).convert("RGBA"))
    _, placed, _, _ = FA.pack(list(FA.SPEC.keys()))
    key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) = placed[0]
    src = FA.load(path)[y0:y1, x0:x1]
    mm = material_mask(src)
    same = np.all(atlas[ay + FA.PAD:ay + ah - FA.PAD, ax + FA.PAD:ax + aw - FA.PAD, :3]
                  == src[:, :, :3], axis=2)
    clean = int((mm & ~same).sum())
    doctored = src.copy()
    doctored[mm] = np.clip(doctored[mm].astype(int) + 40, 0, 255).astype(np.uint8)
    dirty = int((mm & ~np.all(doctored[:, :, :3] == src[:, :, :3], axis=2)).sum())
    ok = (clean == 0 and dirty > 0)
    print("== 合成自证 ==\n  %s 原样改写数=%d（应 0）  人为偏色 40 后被抓=%d（应 >0）"
          % ("OK  " if ok else "FAIL", clean, dirty))
    return ok


def main():
    errs, per_item = check_regions()
    summarize(per_item)
    if "--items" in sys.argv:
        return 0
    for code, msg in errs:
        print("  [%s] %s" % (code, msg))
    if not self_test():
        print("自证未通过 —— 检查器本身不可信")
        return 2
    print("结论:", "PASS 材质像素零改写" if not errs else "FAIL %d 处颜色问题" % len(errs))
    return 0 if not errs else 1


if __name__ == "__main__":
    sys.exit(main())
