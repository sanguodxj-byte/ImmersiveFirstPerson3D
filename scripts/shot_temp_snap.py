# AI生成
# -*- coding: utf-8 -*-
"""看一眼当下出图通路占了多少盘（长批量跑到中途时用它自证没在填 C 盘）。

用法：python scripts/shot_temp_snap.py
"""
import glob
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _tmpsweep as T  # noqa: E402

CTMP = os.environ.get("TEMP") or os.environ.get("TMP")


def mb(path):
    total = 0
    for r, _, fs in os.walk(path):
        for f in fs:
            try:
                total += os.path.getsize(os.path.join(r, f))
            except OSError:
                pass
    return total / 1048576.0


def main():
    c = shutil.disk_usage("C:\\")
    d = shutil.disk_usage("D:\\")
    leaks = (glob.glob(os.path.join(CTMP, "scoped_dir*"))
             + glob.glob(os.path.join(CTMP, "HeadlessChrome*")))
    boxes = glob.glob(os.path.join(T.TMPROOT, "*"))
    print("C 盘可用 %.1f GB | D 盘可用 %.1f GB" % (c.free / 2 ** 30, d.free / 2 ** 30))
    print("C 盘 Chrome 特征目录: %d 个 / %.1f MB" % (len(leaks), sum(mb(x) for x in leaks)))
    print("D 盘出图沙盒: %d 个 / %.1f MB  (%s)" % (
        len(boxes), sum(mb(x) for x in boxes), T.TMPROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main())
