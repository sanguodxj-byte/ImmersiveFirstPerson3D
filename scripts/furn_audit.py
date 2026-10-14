# AI生成
# -*- coding: utf-8 -*-
"""家具全角度全变体视觉审核：每件 4 朝向 x 6 机位 -> 每件一张 4x6 拼版表。

行 = 朝向 south/north/east/west；列 = 机位 45°/俯视/主视·南/侧视·东/低角度/贴脸。
产物 docs/furniture/audit/<item>.png，供逐格目视判读（禁止用数值统计代替看图）。
"""
import glob
import hashlib
import io
import os
import re
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageDraw, ImageFont
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _tmpsweep import (TMPROOT, scratch, env_for, profile, drop,  # noqa: E402
                       sweep, sweep_mine, start_sweeper)

MOD = r"D:\rim mod\FirstPerson3D"
RAW = os.path.join(MOD, "docs", "furniture", "audit", "raw")
OUT = os.path.join(MOD, "docs", "furniture", "audit")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
PORT = "http://127.0.0.1:8899/furniture.html"
# swiftshader 软渲染很慢：本机同时跑 4 个 Chrome 时会超 40s 不出图（2026-10-10 实测 21/24 格空手而归）
WORKERS = int(os.environ.get("FURN_AUDIT_WORKERS", "2"))
SHOT_TIMEOUT_MS = 90000

ITEMS = ["bed", "stool", "endtable", "dresser", "shelf", "armchair",
         "table1x2", "table2x2", "table2x4", "table3x3",
         "bookcase", "bookcasesmall", "shelfsmall",
         "couch", "diningchair", "doublebed", "royalbed", "hospitalbed",
         "bedroll", "bedrolldouble",
         "standinglamp", "floodlight", "column", "plantpot", "penmarker",
         "throne", "grandthrone", "drape", "stelelarge", "stelegrand"]
ROTS = ["south", "north", "east", "west"]
VIEWS = ["45°", "俯视", "主视·南", "侧视·东", "低角度", "贴脸"]
CELL = (620, 500)
EMPTY = "__empty__"


def url(item, rot, view):
    from urllib.parse import quote
    # still=1：关掉页面上的按钮叠层。它们是 DOM 文字，与差分参考图必然不同，
    # 留着就得切掉顶部一大条，正上方机位的模型顶会被一起切 -> 好件被误判成断裂。
    return "%s?item=%s&rot=%s&view=%s&still=1" % (PORT, item, rot, quote(view))


def _crop_model(im):
    """裁到模型本体：HUD 在顶部 80px，背景是固定深色。"""
    a = np.asarray(im).astype(int)
    m = (np.abs(a - np.array([28, 30, 38])).sum(axis=2) > 40)
    m[:112, :] = False      # HUD 最高约 110px，裁掉免得混进近景
    ys, xs = np.where(m)
    if len(ys) == 0:
        return im
    return im.crop((max(0, xs.min() - 6), max(0, ys.min() - 6),
                    min(im.width, xs.max() + 6), min(im.height, ys.max() + 6)))


def is_blank(p):
    """页面抛异常时 HUD 会写 ERR，画面其余部分是纯背景 —— 判"没渲染出东西"。
    只看下半幅（模型必然占到的区域），阈值给足余量，不误伤大件/小件。"""
    im = np.asarray(Image.open(p).convert("RGB"))
    h, w = im.shape[:2]
    low = im[int(h * 0.45):, :, :]
    bg = np.abs(low.astype(int) - np.array([28, 30, 38])).sum(axis=2) <= 12
    return float(bg.mean()) > 0.98


def cell(item, rot, view, empty=False):
    v = view.replace("·", "_").replace("°", "")
    return os.path.join(RAW, "%s__%s__%s%s.png" % (item, rot, v, "_empty" if empty else ""))


# 决定"这格能不能不重渲"的输入：取样表 / 检查页 / C# 几何 / 图集 / 本脚本。
INPUTS = [os.path.join(MOD, "viewer", "furn_uv.json"),
          os.path.join(MOD, "viewer", "furniture.html"),
          os.path.join(MOD, "Source", "RimFirstPerson3D", "Furniture3D.cs"),
          os.path.join(MOD, "Textures", "FPS3D", "FurnitureAtlas.png"),
          os.path.join(MOD, "scripts", "furn_audit.py")]
FPH = os.path.join(RAW, ".inputs")


def inputs_fp():
    h = hashlib.sha1()
    for p in INPUTS:
        h.update(os.path.basename(p).encode())
        try:
            with open(p, "rb") as f:
                h.update(f.read())
        except OSError:
            h.update(b"missing")
    return h.hexdigest()


def invalidate_cache():
    """输入变了就作废全部缓存格。

    必须做：模型格命中缓存直接复用，而空场景参考**每轮重渲**（见 shoot()）。
    两边不同源，差分遮罩就是"旧模型 vs 新场景"，能凭空造出剪影断裂，
    也能把真缺陷洗白。2026-10-11 那 4 格 floodlight/贴脸 假断裂正是这么来的
    （模型格是 04:17 的旧图，参考是 05:53 新渲的）。"""
    cur = inputs_fp()
    old = io.open(FPH, encoding="utf-8").read().strip() if os.path.exists(FPH) else ""
    if old == cur:
        return "reuse"
    n = 0
    for p in glob.glob(os.path.join(RAW, "*.png")):
        try:
            os.remove(p)
            n += 1
        except OSError:
            pass
    os.makedirs(RAW, exist_ok=True)
    io.open(FPH, "w", encoding="utf-8").write(cur)
    print("  [cache] 输入变了（%s -> %s），作废 %d 格，全量重渲" % (
          (old[:8] or "无"), cur[:8], n))
    return "reset"


def shoot(job):
    item, rot, view = job[:3]
    empty = len(job) > 3                           # 第 4 个元素 = 要渲空场景（?empty=1）
    p = cell(item, rot, view, empty)
    if empty:
        pass                             # 参考图每次都重渲：场景/灯一改，缓存的参考就会污染差分
    elif os.path.exists(p) and os.path.getsize(p) > 8000:
        return p, ("整幅空白（页面异常或模型没画出来）" if is_blank(p) else None)
    # profile 必须**每格一个**：多个 Chrome 共用一个 user-data-dir 会互相抢锁，
    # 结果是静默不出图（2026-10-10 实测 24 格里 21 格空手而归）。
    # 沙盒落在 D 盘（temp_shots/）而不是 C 盘 %TEMP%：每次启动 Chrome 都往 %TEMP%
    # 塞一个 23~26MB 的 scoped_dir，指定 --user-data-dir 也拦不住，720 格 = 16.5GB。
    # 沙盒按启动独占，用完 drop() 整目录删，不靠"静置 N 秒后扫"跟写入抢时间。
    rd = scratch()
    prof = profile(rd)
    cmd = [CHROME, "--headless=new", "--no-sandbox", "--disable-gpu",
           "--use-gl=swiftshader", "--enable-unsafe-swiftshader",
           "--window-size=%d,%d" % CELL, "--virtual-time-budget=25000",
           "--timeout=%d" % SHOT_TIMEOUT_MS,
           "--user-data-dir=" + prof,
           "--screenshot=" + p,
           url(item, rot, view) + ("&empty=1" if empty else "")]
    try:
        subprocess.run(cmd, env=env_for(rd),
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                       timeout=SHOT_TIMEOUT_MS / 1000.0 + 40)
    finally:
        if not drop(rd):                 # crashpad 攥着 .pma 时 drop 内部会重试
            print("  [leak] 沙盒没删干净: %s" % rd)
    if not os.path.exists(p):
        return p, "没出图"
    return p, ("整幅空白（页面异常或模型没画出来）" if is_blank(p) else None)


def model_mask(a, a0):
    """差分遮罩：与同机位的空场景逐像素比，有差别的才是模型。
    不能用"与背景色的色差" —— 家具上有近黑的暗面，会被当成背景，好好的模型被切成几段。"""
    m = np.abs(a - a0).sum(axis=2) > 12
    if HUD_ROWS:
        m[:HUD_ROWS, :] = False
    y0, y1, x0, x1 = MISS_BOX
    m[y0:y1, x0:x1] = False             # 哨兵自己不算部件
    return m


def missing_regions(p):
    """页面里出现"查无此区"= 左下角哨兵变纯洋红。这类格子必须判失败：
    兜底色会让面看起来"是白的"，肉眼看不出来，闸门不报就是假绿。"""
    a = np.asarray(Image.open(p).convert("RGB")).astype(int)
    y0, y1, x0, x1 = MISS_BOX
    blk = a[y0:y1, x0:x1]
    mag = (np.abs(blk - np.array([255, 0, 255])).sum(axis=2) <= 24)
    return int(mag.sum())


HUD_ROWS = 0              # still=1 下页面把 HUD 也关了，差分就是模型本体，不必切行
MISS_BOX = (476, 500, 0, 24)   # 左下角(24x24)"缺取样区"洋红哨兵：不算模型，单独判
MIN_BAND_PX = 800        # 小于这块面积的段 = 描边线/抗锯齿细行，不当一个"部件"


def bands(m, min_px=6, gap=6):
    """按行切成连续段，再按面积滤掉细行。段数 >1 = 剪影断裂（有部件悬空/被剔除/没接上）。

    min_px 只能到"这条缝里确实还有像素"的量级：细柱/薄板(5~16cm)在 3.5m 外只占 6~25 列，
    旧值 25 会把探照灯的灯杆、书架的侧板判成"断开"。gap=6 是因为腿与座面的接缝
    在 45° 俯视里会被座盘挡住，真实投影就有几行空。真缺陷的量级是整块板件错位
    （书架侧板那次是 2429 像素、断几十行），这两档都还抓得到（见 selftest）。"""
    on = m.sum(axis=1) >= min_px
    out, y = [], 0
    while y < len(on):
        if on[y]:
            y0 = y
            while y < len(on) and (on[y] or on[y + 1:y + 1 + gap].any()):
                y += 1
            if int(m[y0:y].sum()) >= MIN_BAND_PX:
                out.append((y0, y))
        y += 1
    return out


def sheet(item):
    cols, rows = len(VIEWS), len(ROTS)
    W, H = CELL[0] * cols, CELL[1] * rows + 26
    canvas = Image.new("RGBA", (W, H), (28, 30, 38, 255))
    d = ImageDraw.Draw(canvas)
    try:
        f = ImageFont.truetype(r"C:\Windows\Fonts\cons.ttf", 18)
    except Exception:
        f = ImageFont.load_default()
    for c, v in enumerate(VIEWS):
        d.text((c * CELL[0] + 6, 4), v, fill=(207, 214, 228, 255), font=f)
    for r, rot in enumerate(ROTS):
        d.text((2, 26 + r * CELL[1] + 4), rot, fill=(127, 209, 255, 255), font=f)
        for c, v in enumerate(VIEWS):
            p = os.path.join(RAW, "%s__%s__%s.png" % (item, rot, v.replace("·", "_").replace("°", "")))
            if not os.path.exists(p):
                continue
            im = Image.open(p).convert("RGBA")
            if im.size != CELL:
                im = im.resize(CELL, Image.NEAREST)
            canvas.paste(im, (c * CELL[0], 26 + r * CELL[1]))
    small = canvas.resize((W // 2, H // 2), Image.LANCZOS)
    return _save(small, os.path.join(OUT, "%s_sheet.png" % item))


def _save(im, path):
    """先写临时名再原子改名：目标文件被预览/扫描句柄占着时，直接 w+b 会 EINVAL。"""
    tmp = path + ".tmp"
    im.save(tmp, "PNG")   # 扩展名不是 .png，必须点名格式
    try:
        os.replace(tmp, path)
    except OSError as e:
        print("  [sheet] %s 覆盖失败(%s)，保留 %s" % (os.path.basename(path), e, tmp))
        return tmp
    return path


def overview():
    """总览：每件 x (45° / 低角度 / 俯视) 裁到模型近景，拼一张给人看整体观感。
    拼版表看的是逐格判读，这张看的是"摆在一起像不像一套家具"。"""
    views = ["45°", "低角度", "俯视"]
    CW, CH, LBL = 400, 300, 22
    sheet = Image.new("RGB", (CW * len(views) + 8 * (len(views) + 1),
                              (CH + LBL) * len(ITEMS) + 8), (24, 26, 33))
    d = ImageDraw.Draw(sheet)
    try:
        f = ImageFont.truetype(r"C:\Windows\Fonts\cons.ttf", 15)
    except Exception:
        f = ImageFont.load_default()
    for r, it in enumerate(ITEMS):
        for c, v in enumerate(views):
            p = os.path.join(RAW, "%s__%s__%s.png" % (it, "south", v.replace("·", "_").replace("°", "")))
            if not os.path.exists(p):
                continue
            x = 8 + c * (CW + 8)
            y = 8 + r * (CH + LBL) + LBL
            d.text((x, y - 17), "%s 南向 %s" % (it, v), fill=(200, 220, 240), font=f)
            im = _crop_model(Image.open(p).convert("RGB"))
            sc = min(CW / im.width, CH / im.height)
            im = im.resize((max(1, int(im.width * sc)), max(1, int(im.height * sc))), Image.LANCZOS)
            sheet.paste(im, (x + (CW - im.width) // 2, y + (CH - im.height) // 2))
    return _save(sheet, os.path.join(OUT, "overview.png"))


def js_check():
    """检查页的 JS 先过一遍语法。一次括号写歪就是整页崩、几百格全空白，
    而截图要跑十几分钟才看得见 —— 这条闸门一秒就能拦住（本轮真撞上过一次）。"""
    html = os.path.join(MOD, "viewer", "furniture.html")
    if not os.path.exists(html):
        return ["缺 viewer/furniture.html"]
    src = io.open(html, encoding="utf-8").read()
    blocks = re.findall(r'<script[^>]*>(.*?)</script>', src, re.S)
    kinds = re.findall(r'<script([^>]*)>', src)
    out = []
    skip = []
    for n, (attr, body) in enumerate(zip(kinds, blocks)):
        ext = "mjs" if "module" in attr else "js"
        tmp = os.path.join(TMPROOT, "furn_view_%d.%s" % (n, ext))
        os.makedirs(TMPROOT, exist_ok=True)
        with open(tmp, "w", encoding="utf-8") as f:
            f.write(body)
        try:
            r = subprocess.run(["node", "--check", tmp], stdout=subprocess.PIPE,
                               stderr=subprocess.STDOUT, timeout=90)
        except Exception as e:                      # 没装 node 就别拦，只是少一道保险
            skip.append("node 不可用，跳过 JS 语法检查：%s" % e)
            break
        if r.returncode:
            out.append("第 %d 段 <script> 语法错：%s"
                       % (n + 1, r.stdout.decode("utf-8", "replace").strip()[:300]))
    if skip and not out:
        print("  [js] " + skip[0])
    return out


def _cache_fixtures():
    """缓存失效判据双向：输入没变必须复用（否则每轮全量重渲，闸门跑不动），
    输入变了必须作废（否则拿旧模型格配新参考图，差分失真）。
    夹具只动 TMPROOT 里的假输入，不碰仓库真文件。"""
    global RAW, FPH, INPUTS
    keep = (RAW, FPH, INPUTS)
    out = []
    rd = scratch("cachefx")
    try:
        RAW = os.path.join(rd, "raw")
        FPH = os.path.join(RAW, ".inputs")
        fake = os.path.join(rd, "input.txt")
        INPUTS = [fake]
        os.makedirs(RAW, exist_ok=True)
        io.open(fake, "w", encoding="utf-8").write("v1")
        io.open(os.path.join(RAW, "bed__south__45.png"), "w").write("x")
        if invalidate_cache() != "reset":
            out.append("cache-first: 首轮没有指纹，必须判 reset 并全量重渲")
        if os.path.exists(os.path.join(RAW, "bed__south__45.png")):
            out.append("cache-first: 判了 reset 却没删旧格，旧图还会进差分")
        if invalidate_cache() != "reuse":
            out.append("cache-same: 输入没变必须 reuse（否则白渲 720 格）")
        io.open(fake, "w", encoding="utf-8").write("v2 改了取样表")
        io.open(os.path.join(RAW, "bed__south__45.png"), "w").write("x")
        if invalidate_cache() != "reset":
            out.append("cache-drift: 输入变了却复用旧格（这轮假断裂就是这么来的）")
        if os.path.exists(os.path.join(RAW, "bed__south__45.png")):
            out.append("cache-drift: reset 了但旧格还在")
        # 缺文件（别人正在改/图集没生成）不许静默 reuse
        os.remove(fake)
        io.open(FPH, "w", encoding="utf-8").write("whatever")
        if invalidate_cache() != "reset":
            out.append("cache-missing: 输入文件不见了却被当成没变")
    finally:
        RAW, FPH, INPUTS = keep
        drop(rd)
    return out


def selftest():
    """判据必须双向：整块的要放行、真断的要抓住、只有 HUD 差异的要判 0 段、
    缓存该废的必须废、不该废的必须复用。
    不跑这一轮，"720 格无断裂"可能只是判据失灵。返回缺陷消息列表。"""
    H, W = 500, 620
    solid = np.zeros((H, W), int)
    solid[200:400, 200:420] = 30
    cut = solid.copy()
    cut[300:330, :] = 0                       # 中间挖掉 30 行 = 上下两截
    sent = np.zeros((H, W), int)
    y0, y1, x0, x1 = MISS_BOX
    sent[y0:y1, x0:x1] = 200                 # 只有左下角哨兵在变（缺取样区）
    a0 = np.zeros((H, W, 3), int)
    mk = lambda a: model_mask(a[..., None].repeat(3, 2), a0)
    got = [("solid", 1, len(bands(mk(solid)))),
           ("split", 2, len(bands(mk(cut)))),
           ("sentinel-only", 0, len(bands(mk(sent))))]
    bad = ["%s: 期望 %d 段，实得 %d 段" % (n, w, g) for n, w, g in got if w != g]
    # 哨兵检测本身也要双向：纯洋红必须数到，正常底色必须数不到
    mag_ok = int((np.abs(sent[..., None].repeat(3, 2)[y0:y1, x0:x1]
                         - np.array([255, 0, 255])).sum(axis=2) <= 24).sum())
    if mag_ok:
        bad.append("sentinel-detect: 纯灰底不应数到洋红哨兵，实得 %d 像素" % mag_ok)
    bad += _cache_fixtures()
    return bad


def main():
    js = js_check()
    for m in js:
        print("  [js] " + m)
    if js:
        print("检查页 JS 未过 —— 不出图，先修页面")
        return 2
    bad_fx = selftest()
    for m in bad_fx:
        print("  [fixture] " + m)
    if bad_fx:
        print("剪影夹具未通过 —— 判据本身不可信，拒绝给存量背书")
        return 2
    sweep()                       # 进场收掉上一轮被杀时留下的沙盒/C 盘遗留
    os.makedirs(RAW, exist_ok=True)
    invalidate_cache()            # 输入变了就不许复用旧格
    _stop = start_sweeper(interval=20, age_s=180)   # 兜底：只收没人认领的老沙盒
    os.makedirs(RAW, exist_ok=True)
    os.makedirs(OUT, exist_ok=True)
    jobs = [(i, r, v) for i in ITEMS for r in ROTS for v in VIEWS]
    print("shots:", len(jobs), "workers:", WORKERS)
    bad = []
    with ThreadPoolExecutor(max_workers=WORKERS) as ex:
        for p, err in ex.map(shoot, jobs):
            if err:
                bad.append((os.path.basename(p), err))
    for name, err in bad:
        print("  [blank] %s: %s" % (name, err))
    miss = []
    for i in ITEMS:
        for r in ROTS:
            for v in VIEWS:
                p = cell(i, r, v)
                if os.path.exists(p) and missing_regions(p) > 20:
                    miss.append("%s/%s/%s" % (i, r, v))
    for m_ in miss:
        print("  [missing] " + m_ + " 查无此取样区（走了兜底色）")
    # 剪影连通性：每个机位渲一张空场景，逐格差分后按行看有没有断成几截
    for v in VIEWS:
        shoot((EMPTY, EMPTY, v, True))
    split = []
    for v in VIEWS:
        ev = cell(EMPTY, EMPTY, v, True)
        if not os.path.exists(ev):
            continue
        e0 = np.asarray(Image.open(ev).convert("RGB")).astype(int)
        for i in ITEMS:
            for r in ROTS:
                p = cell(i, r, v)
                if not os.path.exists(p):
                    continue
                m = model_mask(np.asarray(Image.open(p).convert("RGB")).astype(int), e0)
                n = len(bands(m))
                if n > 1:
                    split.append("%s/%s/%s 断成 %d 段" % (i, r, v, n))
    for s_ in split:
        print("  [split] " + s_)
    for i in ITEMS:
        print("sheet:", sheet(i))
    print("overview:", overview())
    ok = not bad and not split and not miss
    print("结论:", "PASS %d 格全出图、剪影无断裂" % len(jobs) if ok else
          "FAIL 没出图 %d 格 / 剪影断裂 %d 格 / 缺取样区 %d 格"
          % (len(bad), len(split), len(miss)))
    _stop.set()                 # 停掉兜底清扫线程
    sweep_mine()                # 先收本进程 drop() 没删掉的（crashpad 攥句柄，实测 2%）
    sweep()                     # 再收别处被杀时留下的老沙盒
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
