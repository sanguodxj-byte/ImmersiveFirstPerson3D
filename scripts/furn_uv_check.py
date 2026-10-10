# AI生成
# -*- coding: utf-8 -*-
"""家具三视图取样表的双向校验器（单一口径，含合成夹具自测）。

【口径】
1. 坐标 = 源图像素，原点在左上，半开区间；切点必须落在像素边界（本表天然满足）。
2. **黑色描边** := 与透明区（alpha<128）四邻相连的黑色像素（luma<40），含其 1 像素内扩。
   取样区**不得包含任何描边像素**（本 mod 的描边由反绕外壳网格画）。
   被不透明内容包围的黑色像素是**线稿**（如椅背内圈），允许保留。
3. **不空取**：一张图里所有「不透明且非描边」的像素，必须至少被一个取样区覆盖。
   （贴图所有像素都至少存在于一个面上。）
4. 同一张图内的取样区**不得互相重叠**（重叠=同一像素当两个面用，属重复取样）。
5. 生成的 UV 表与 C# 几何**必须双向对齐**：表里的键没人用 = 冗余；代码引用的键表里没有 = 断链。
6. 每件必须声明一处 **wood**（无笔画的干净木面区）：圆柱侧壁/顶盖与薄板立边在三视图里
   根本没有对应像素，它们的取样必须落在 wood 上，不许拿别的朝向的立面段糊。

用法：
    python scripts/furn_uv_check.py            # 校验，非 0 退出码表示有缺陷
    python scripts/furn_uv_check.py --fix      # 额外打印每个缺陷的收缩/扩张建议
"""
import os
import re
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import furn_atlas as FA  # noqa: E402

ALPHA_MIN = 128
LUMA_MAX = 40
STROKE_HEAVY = 0.55   # 取样区内笔画（黑线外扩一格）占比上限：超了说明这块基本是线稿，该改取内区
EDGE_SLACK = 6        # 内容包围盒左右各这么多列属"侧棱"，归垂直方向的那张图，不计覆盖
COVER_MIN = 0.98


def content_mask(src):
    """必须归属的"材质像素" = 不透明、且不在黑色笔画（含 1 像素外扩）里。"""
    return (src[:, :, 3] >= ALPHA_MIN) & (~FA.dilate(FA.black_mask(src)))


def check_item(item, spec=None):
    """-> 错误列表 [(code, msg, hint)]"""
    spec = spec or FA.SPEC
    errs = []
    if item not in spec:
        return [("noitem", "规范里没有 " + item, "")]
    # wood = 该件的"干净木面区"，圆柱/薄边这些三视图没画到的面全靠它，每件必须有且只有一处。
    woods = [f"{view}.wood" for view, d in spec[item].items() if "wood" in d]
    for view, d in spec[item].items():
        path = d["path"]
        full = os.path.join(FA.TEX, path.replace("/", os.sep))
        if not os.path.exists(full):
            errs.append(("missing", f"{item}.{view}: 找不到 {path}", ""))
            continue
        src = FA.load(path)
        H, W = src.shape[:2]
        st = FA.stroke_mask(src)
        ink = FA.dilate(FA.black_mask(src))
        for r in d.get("discard", []):
            dx0, dy0, dx1, dy1 = r
            sub = slice(dy0, dy1), slice(dx0, dx1)
            area = (dx1 - dx0) * (dy1 - dy0)
            if not (0 <= dx0 < dx1 <= W and 0 <= dy0 < dy1 <= H):
                errs.append(("oob", f"{item}.{view}.discard: 区 {r} 越出图像 {W}x{H}", ""))
                continue
            if int(ink[sub].sum()) < area * 0.30:
                errs.append(("abuse", f"{item}.{view}.discard: 区 {r} 笔画只占 "
                                      f"{int(ink[sub].sum())}/{area}，不是线稿带，不许丢",
                             "改当取样区"))
        ct = content_mask(src)
        ys, xs = np.where(ct)
        if len(ys) == 0:
            errs.append(("empty", f"{item}.{view}: 无有效内容像素", ""))
            continue
        cx0, cx1, cy0, cy1 = xs.min(), xs.max(), ys.min(), ys.max()
        cover = np.zeros((H, W), bool)
        rects = [(k, v) for k, v in d.items() if k not in ("path", "discard")]
        for name, (x0, y0, x1, y1) in rects:
            key = f"{item}.{view}.{name}"
            if not (0 <= x0 < x1 <= W and 0 <= y0 < y1 <= H):
                errs.append(("oob", f"{key}: 区 {(x0,y0,x1,y1)} 越出图像 {W}x{H}",
                             "夹到 [0,W) x [0,H)"))
                continue
            sub = slice(y0, y1), slice(x0, x1)
            n_stroke = int(st[sub].sum())
            area = (x1 - x0) * (y1 - y0)
            if int(ink[sub].sum()) > area * STROKE_HEAVY:
                rows = np.where(st[sub].any(axis=1))[0] + y0
                cols = np.where(st[sub].any(axis=0))[0] + x0
                errs.append(("stroke", f"{key}: 描边占比 {n_stroke}/{area}"
                                       f"（行 {rows[0]}..{rows[-1]} 列 {cols[0]}..{cols[-1]}）",
                             "这块基本压在轮廓上，重切"))
            n_hole = int((src[sub][:, :, 3] < ALPHA_MIN).sum())
            if n_hole > area * 0.02:
                hm = src[sub][:, :, 3] < ALPHA_MIN
                hr = np.where(hm.any(axis=1))[0]; hc = np.where(hm.any(axis=0))[0]
                errs.append(("hole", f"{key}: 含 {n_hole}/{area} 个透明像素"
                                      "（面会穿洞/发黑）",
                             f"透明占行 {hr[0]+y0}..{hr[-1]+y0} 列 {hc[0]+x0}..{hc[-1]+x0}"
                             f" -> 实心内区约 ({int(hc[0]+x0)},{int(hr[0]+y0)},"
                             f"{int(hc[-1]+x0+1)},{int(hr[-1]+y0+1)}) 之外"))
            # wood 是从同一块表面上再取一份"干净样本"，允许落在别的区里
            if name != "wood" and cover[sub].any():
                errs.append(("overlap", f"{key}: 与同图其它取样区重叠", "拆开或合并"))
            cover[sub] = True
        # 覆盖：内容像素必须至少落在一个取样区里。两类豁免——
        #   ① 包围盒左右各 EDGE_SLACK 列是"侧棱"，归垂直方向那张图；
        #   ② 紧贴线稿（描边外扩 2 格）的碎像素不是材质，是笔画，补绘只会糊。
        slack = np.zeros((H, W), bool)
        slack[:, cx0:cx0 + EDGE_SLACK] = True
        slack[:, max(0, cx1 - EDGE_SLACK + 1):cx1 + 1] = True
        exempt = FA.dilate(FA.dilate(FA.black_mask(src))) | slack
        for r in d.get("discard", []):
            exempt[r[1]:r[3], r[0]:r[2]] = True
        miss = ct & ~cover & ~exempt
        n, total = int(miss.sum()), int((ct & ~exempt).sum())
        if total and (1.0 - n / float(total)) < COVER_MIN:
            ys2, xs2 = np.where(miss)
            errs.append(("cover", f"{item}.{view}: {n}/{total} 内容像素无归属"
                                  f"（y {ys2.min()}..{ys2.max()}, x {xs2.min()}..{xs2.max()}）",
                         "扩区或补区"))
    if not woods:
        errs.append(("nowood", f"{item}: 没有声明 wood（干净木面区）",
                     "圆柱侧壁/顶盖与薄板立边要取样，只能落在这块上"))
    return errs


def check_atlas():
    """产物级检查：图集里不得残留黑描边（补绘必须真的生效）。"""
    if not os.path.exists(FA.ATLAS_OUT):
        return [("noatlas", "缺图集产物，先跑 furn_atlas.py", "")]
    atlas = np.asarray(Image.open(FA.ATLAS_OUT).convert("RGBA"))
    _, placed, size, _ = FA.pack(list(FA.SPEC.keys()))
    errs = []
    for key, path, (x0, y0, x1, y1), (ax, ay, aw, ah) in placed:
        blk = atlas[ay + FA.PAD:ay + ah - FA.PAD, ax + FA.PAD:ax + aw - FA.PAD]
        if blk.shape[:2] != (y1 - y0, x1 - x0):
            errs.append(("size", f"{key}: 图集块尺寸 {blk.shape[:2]} != 取样区 "
                                 f"{(x1-x0, y1-y0)}", "重跑打包"))
            continue
        src = FA.load(path)[y0:y1, x0:x1]
        lum = blk[:, :, :3].astype(np.int32).mean(axis=2)
        black = (lum < LUMA_MAX) & (blk[:, :, 3] >= ALPHA_MIN)
        # 只追究"原封没动"的黑像素：邻域本就暗、补绘后仍偏暗属原版自己的明暗，合法。
        resid = int((black & np.all(blk.astype(np.int32) == src.astype(np.int32), axis=2)).sum())
        if resid:
            errs.append(("resid", f"{key}: {resid} 个黑色笔画像素未被补绘改写", ""))
    return errs


def dup_names(seg):
    """SPEC 字面量文本里重复出现的顶层件名。Python dict 会静默只留最后一份，
    前一份等于没写 —— 不查就会拿着旧坐标当真源。"""
    names = re.findall(r'^    "([a-z0-9]+)": \{', seg, re.M)
    return sorted({n for n in names if names.count(n) > 1})


def check_code_alignment():
    """生成表 <-> C# 几何 双向对齐。"""
    errs = []
    gen = FA.CS_OUT
    csdir = os.path.join(FA.MOD, "Source", "RimFirstPerson3D")
    if not os.path.exists(gen):
        return [("nogt", "缺 FurnitureUVGen.cs，先跑 furn_atlas.py", "")]
    # SPEC 是字面量 dict：同名键写两遍时 Python 静默只留最后一份，前一份等于不存在。
    src = open(os.path.join(FA.MOD, "scripts", "furn_atlas.py"), encoding="utf-8").read()
    seg = src[src.index("SPEC = {"):src.index("\nVIEWS_ORDER")]
    for n in dup_names(seg):
        errs.append(("dupkey", f"SPEC 里 {n} 写了多次，只有最后一份生效", "删掉重复块"))
    text = open(gen, encoding="utf-8").read()
    # 键必须三段、每段以字母开头：否则 "0.2.0" 这类版本号会被当成取样区误报。
    KEY = r'"([a-z][a-z0-9]*\.[a-z][a-z0-9]*\.[a-z][a-z0-9]*)"'
    keys = set(re.findall(KEY, text))
    # 区段名带大写/下划线时，上面的正则整条匹配不上 -> 该键对账时隐形。先堵住这个洞。
    for item, views in FA.SPEC.items():
        for view, d in views.items():
            for name in d:
                if name in ("path", "discard"):
                    continue
                if not re.fullmatch(r"[a-z][a-z0-9]*", name):
                    errs.append(("badkey", f"{item}.{view}.{name}: 区段名不是小写字母数字",
                                 "改名，否则它逃过表<->代码对账"))
    used = set()
    for fn in sorted(os.listdir(csdir)):
        if not fn.endswith(".cs") or fn == "FurnitureUVGen.cs":
            continue
        t = open(os.path.join(csdir, fn), encoding="utf-8").read()
        used |= set(re.findall(KEY, t))
    for k in sorted(used - keys):
        errs.append(("dangling", f"C# 引用了表里不存在的取样区 {k}", "补 SPEC 或改代码"))
    for k in sorted(keys - used):
        errs.append(("unused", f"取样区 {k} 生成了但没人用", "接进几何或删除"))
    return errs


# ---- 合成夹具：检查器必须"该报的报、不该报的不报" -------------------------
# 自造一张标准盒图，缺陷可完全隔离（真实贴图上任何框都会顺带触发别的判据）。
#   64x64 透明底；x=8..56,y=8..56 填中灰；1px 纯黑轮廓（=描边）；
#   y=32, x=16..48 一条内部黑线（=线稿，允许取样）。
FX = os.path.join(FA.MOD, "docs", "furniture", "_fx_box.png")
FXH = os.path.join(FA.MOD, "docs", "furniture", "_fx_hole.png")   # 带透明缺口的版本


def _save_if_changed(im, path):
    """内容没变就不写盘：否则每跑一次校验器就把夹具图改一遍，工作树永远脏。"""
    import io
    buf = io.BytesIO()
    im.save(buf, "PNG")
    data = buf.getvalue()
    if os.path.exists(path) and open(path, "rb").read() == data:
        return
    with open(path, "wb") as f:
        f.write(data)


def make_fixture_png():
    a = np.zeros((64, 64, 4), np.uint8)
    a[8:57, 8:57] = (200, 200, 200, 255)
    a[7:58, 7] = (10, 10, 10, 255)
    a[7:58, 57] = (10, 10, 10, 255)
    a[7, 7:58] = (10, 10, 10, 255)
    a[57, 7:58] = (10, 10, 10, 255)
    a[32, 16:49] = (10, 10, 10, 255)
    os.makedirs(os.path.dirname(FX), exist_ok=True)
    _save_if_changed(Image.fromarray(a, "RGBA"), FX)
    b = a.copy()
    b[44:56, 40:56] = (0, 0, 0, 0)          # 12x16 透明缺口 -> 触发 hole
    _save_if_changed(Image.fromarray(b, "RGBA"), FXH)


def fixtures():
    """-> [(名字, 期望错误码集合, 实际错误码集合)]  每项只植入一类缺陷。"""
    make_fixture_png()
    real_tex = FA.TEX
    FA.TEX = os.path.dirname(FX)
    P = os.path.basename(FX)
    # 内容区 = x/y ∈ [9,55]（第 8 与 56 行被描边内扩吃掉）
    # 全覆盖区 + 一个纯压在轮廓上的窄框 = 只该报 stroke
    # 每例都带一处 wood（干净木面区，落在已覆盖的实心区内 -> 不改覆盖、不改重叠）
    W = {"wood": (10, 10, 20, 20)}
    cases = [
        ("fx-clean", {"south": dict({"path": P, "top": (9, 9, 56, 40),
                                     "front": (9, 40, 56, 56)}, **W)}, set()),
        # 与 fx-clean 同框但**不声明 wood**：圆柱/薄边无处取样，必须报 nowood
        ("fx-nowood", {"south": {"path": P, "top": (9, 9, 56, 40),
                                 "front": (9, 40, 56, 56)}}, {"nowood"}),
        ("fx-stroke", {"south": dict({"path": P, "all": (9, 9, 56, 56),
                                      "ring": (7, 7, 58, 9)},
                                     **{"wood": (20, 20, 30, 30)})}, {"stroke"}),
        # 越界的区会被整块跳过，因此必然连带 cover —— 属预期的连锁报告，不是误报
        ("fx-oob", {"south": dict({"path": P, "top": (9, 9, 56, 40),
                                   "bad": (9, 40, 56, 70)}, **W)}, {"oob", "cover"}),
        ("fx-overlap", {"south": dict({"path": P, "a": (9, 9, 56, 48),
                                       "b": (9, 40, 56, 56)}, **W)}, {"overlap"}),
        ("fx-cover", {"south": dict({"path": P, "top": (20, 20, 40, 30)},
                                    **{"wood": (22, 22, 30, 28)})}, {"cover"}),
        ("fx-missing", {"south": {"path": "zz_none.png", "top": (1, 1, 2, 2),
                                  "wood": (1, 1, 2, 2)}}, {"missing"}),
        # 带缺口的图：两块取样区无重叠且覆盖全部"材质像素"，只该报 hole
        ("fx-hole", {"south": dict({"path": os.path.basename(FXH), "top": (9, 9, 56, 40),
                                    "front": (9, 40, 56, 56)}, **W)}, {"hole"}),
    ]
    out = []
    try:
        for name, views, want in cases:
            got = set(c for c, _, _ in check_item("fx", {"fx": views}))
            out.append((name, want, got))
    finally:
        FA.TEX = real_tex
    # 顶层件名重复：该报的报（bed 写两遍），不该报的不报（各写一遍）
    out.append(("fx-dupkey", ["bed"],
                dup_names('SPEC = {\n    "bed": {\n    },\n    "bed": {\n    },\n'
                          '    "stool": {\n    },\n}')))
    out.append(("fx-dupkey-clean", [],
                dup_names('SPEC = {\n    "bed": {\n    },\n    "stool": {\n    },\n}')))
    # 页面取样键：拿 arch 名当键（漏写 item:）必须报 viewkey；补上 item: 就必须干净。
    # 每例只植入一类缺陷：反向判据把"条目名"也算可达，于是 viewkey 单例不连带 noviewitem。
    one = {"diningchair": {"south": {}}, "bed": {"south": {}}}
    one2 = {"diningchair": {"south": {}}}
    bad_html = "const ITEMS = {\n  diningchair: {arch:'chair', w:1, d:1},\n};\n"
    good_html = ("const ITEMS = {\n  diningchair: {arch:'chair', item:'diningchair', w:1, d:1},"
                 "\n  bed: {arch:'bed', w:1, d:2},\n};\n")
    orphan_html = "const ITEMS = {\n  bed: {arch:'bed', w:1, d:2},\n};\n"
    out.append(("fx-viewkey", {"viewkey"}, set(c for c, _, _ in check_viewer(one2, bad_html))))
    out.append(("fx-viewkey-clean", set(), set(c for c, _, _ in check_viewer(one, good_html))))
    out.append(("fx-noviewitem", {"noviewitem"},
                set(c for c, _, _ in check_viewer(one, orphan_html))))
    # 区段名对账：几何里写了 "face" 就不该报，没写就该报
    geom = 'Region(k, v, "face"); Region(k, v, "mid");'
    out.append(("fx-deadname", {"deadname"},
                set(c for c, _, _ in check_names(geom, ["face", "mid", "crest"]))))
    out.append(("fx-deadname-clean", set(),
                set(c for c, _, _ in check_names(geom, ["face", "mid"]))))
    # Arch<->case：漏一个 case 必须报，齐了必须干净
    s_missing = "enum Arch { Bed, Stool }  case Arch.Bed: break;"
    s_ok = "enum Arch { Bed, Stool }  case Arch.Bed: break; case Arch.Stool: break;"
    out.append(("fx-nocase", {"nocase"}, set(c for c, _, _ in check_arch_cases(s_missing))))
    out.append(("fx-nocase-clean", set(), set(c for c, _, _ in check_arch_cases(s_ok))))
    return out


def spec_region_names(spec=None):
    spec = spec or FA.SPEC
    return sorted({r for it in spec.values() for v in it.values()
                   for r in v if r not in ("path", "discard")})


def check_names(cstext=None, names=None):
    """区段名必须被几何点名过。AllRegions 是从 SPEC 机械生成的，拿它跟自己对账等于自证；
    真正独立的事实是"几何代码里有没有人写 Region(key, v, "face")"。
    写了 SPEC 却没人点名的区 = 白占图集，还会让人以为那个面有对应像素。"""
    if cstext is None:
        f = os.path.join(FA.MOD, "Source", "RimFirstPerson3D", "Furniture3D.cs")
        if not os.path.exists(f):
            return [("nocs", "缺 Furniture3D.cs", "")]
        cstext = open(f, encoding="utf-8").read()
    names = names if names is not None else spec_region_names()
    out = []
    for n in names:
        if ('"%s"' % n) not in cstext:
            out.append(("deadname", "区段名 %s 在 SPEC 里有，几何从未点名引用" % n,
                        "接进某个面，或从 SPEC 删掉"))
    return out


def check_arch_cases(cstext=None):
    """enum Arch 里每个原型都得有对应 case。少一个 case 不是编译错误 —— switch 直接
    走空，那件家具只剩一个面都不发的空壳，游戏里表现为"家具消失"。
    本轮就是切片误删了 Armchair/Throne 的 case，靠 deadname 才撞见。"""
    if cstext is None:
        f = os.path.join(FA.MOD, "Source", "RimFirstPerson3D", "Furniture3D.cs")
        if not os.path.exists(f):
            return [("nocs", "缺 Furniture3D.cs", "")]
        cstext = open(f, encoding="utf-8").read()
    m = re.search(r"enum Arch \{([^}]*)\}", cstext, re.S)
    if not m:
        return [("noenum", "找不到 enum Arch", "")]
    arches = [a.strip() for a in m.group(1).replace("\n", " ").split(",") if a.strip()]
    out = []
    for a in arches:
        if ("case Arch.%s:" % a) not in cstext:
            out.append(("nocase", "Arch.%s 没有对应 case（几何会走空）" % a, "补 case"))
    return out


VIEW = os.path.join(FA.MOD, "viewer", "furniture.html")


def viewer_keys(text):
    """检查页 ITEMS 里每件**实际**用的取样件名 —— 与 build() 里 `key = it.item || it.arch` 同口径。"""
    m = re.search(r"const ITEMS = \{(.*?)\n\};", text, re.S)
    if not m:
        return None
    out = {}
    for line in m.group(1).splitlines():
        mm = re.match(r"^\s*([a-z][a-z0-9]*):\s*\{([^}]*)\}", line)
        if not mm:
            continue
        arch = re.search(r"arch:\s*'([a-z]+)'", mm.group(2))
        if not arch:
            continue
        it = re.search(r"item:\s*'([a-z0-9]+)'", mm.group(2))
        out[mm.group(1)] = it.group(1) if it else arch.group(1)
    return out


def check_viewer(spec=None, text=None):
    """几何页与 SPEC 双向对账。页面上的取样键是 `it.item || it.arch` —— 配方名与件名不是一回事
    （Arch.Chair 的件叫 diningchair），漏写 item: 就拿 'chair.xxx' 去查表，查不到走兜底，
    闸门全绿而模型糊成一片。反向：SPEC 里的件页面必须能指到，否则审核压根不出这件的图。"""
    spec = spec or FA.SPEC
    if text is None:
        if not os.path.exists(VIEW):
            return [("noview", "缺 viewer/furniture.html", "")]
        text = open(VIEW, encoding="utf-8").read()
    keys = viewer_keys(text)
    if keys is None:
        return [("noview", "检查页里没有 ITEMS 表", "")]
    errs = []
    for name, key in sorted(keys.items()):
        if key not in spec:
            errs.append(("viewkey", "检查页 %s 用取样件名 '%s'，SPEC 里没有它的区" % (name, key),
                         "给该条补 item:'<件名>'（arch 名不等于件名）"))
    # 可达 = 页面条目名（?item= 用的就是它）或某条目的取样键。两个名字允许不同：
    # 条目叫 bedrolldouble、取样键也叫 bedrolldouble，而 arch 叫 bedroll。
    reach = set(keys) | set(keys.values())
    for it in sorted(spec):
        if it not in reach:
            errs.append(("noviewitem", "SPEC 有 %s，检查页没有一件指向它" % it,
                         "补 ITEMS 条目，否则审核根本不出这件的图"))
    return errs


def main():
    fix = "--fix" in sys.argv
    bad = 0
    print("== 合成夹具自测（该报的报、不该报的不报）==")
    fx_bad = 0
    for name, want, got in fixtures():
        ok = (got == want)
        print(("  OK   " if ok else "  FAIL ") + f"{name}: 期望{sorted(want)} 实得{sorted(got)}")
        if not ok:
            fx_bad += 1
    if fx_bad:
        print(f"夹具未通过（{fx_bad} 项）—— 检查器本身不可信，拒绝给存量背书")
        return 2

    print("== 存量 SPEC ==")
    for item in FA.SPEC:
        errs = check_item(item)
        for code, msg, hint in errs:
            print(f"  [{code}] {msg}" + (("\n        " + hint) if (hint and fix) else ""))
        bad += len(errs)
    print("== 图集产物（补绘后不得残留黑描边）==")
    for code, msg, hint in check_atlas():
        print(f"  [{code}] {msg}")
        bad += 1
    print("== 表 <-> 代码对齐 ==")
    for code, msg, hint in check_code_alignment():
        print(f"  [{code}] {msg}")
        bad += 1
    print("== Arch <-> case ==")
    for code, msg, hint in check_arch_cases():
        print(f"  [{code}] {msg}")
        bad += 1
    print("== 区段名 <-> 几何点名 ==")
    for code, msg, hint in check_names():
        print(f"  [{code}] {msg}")
        bad += 1
    print("== 检查页 ITEMS <-> SPEC ==")
    for code, msg, hint in check_viewer():
        print(f"  [{code}] {msg}")
        bad += 1
    print("结论:", "PASS 0 缺陷" if bad == 0 else f"FAIL {bad} 处缺陷")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
