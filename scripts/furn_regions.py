# AI生成
# -*- coding: utf-8 -*-
"""把 SPEC 的全部取样区机械写回 Furniture3D.cs 的 AllRegions 清单。

为什么要有这个：AllRegions 是"表 <-> 几何"对账的字面量来源，手抄必漂
（漏一个键就是一条 unused，多一个键就是一条 dangling）。加完 SPEC 跑一次即可。

用法：python scripts/furn_regions.py
"""
import io
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furn_atlas as FA  # noqa: E402

CS = os.path.join(FA.MOD, "Source", "RimFirstPerson3D", "Furniture3D.cs")
MARK = re.compile(r"(        internal static readonly string\[\] AllRegions =\n        \{\n)"
                  r".*?(\n        \};)", re.S)


def keys():
    out = []
    for item in FA.SPEC:
        for view, d in FA.SPEC[item].items():
            for region in d:
                if region not in ("path", "discard"):
                    out.append("%s.%s.%s" % (item, view, region))
    return sorted(out)


def main():
    ks = keys()
    body = "\n".join('            "%s",' % k for k in ks)
    t = io.open(CS, encoding="utf-8").read()
    if not MARK.search(t):
        print("找不到 AllRegions 块 —— 别硬写，先看 Furniture3D.cs")
        return 1
    t2 = MARK.sub(lambda m: m.group(1) + body + m.group(2), t, count=1)
    io.open(CS, "w", encoding="utf-8", newline="\n").write(t2)
    print("AllRegions 已按 SPEC 重写：%d 个区（%d 件）" % (len(ks), len(FA.SPEC)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
