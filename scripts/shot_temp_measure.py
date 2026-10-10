# AI生成
# -*- coding: utf-8 -*-
"""量一次"截图通路到底往 C 盘写了多少"——出图临时目录卫生的回归尺。

用法：
    python scripts/shot_temp_measure.py [次数]      # 默认 3 次真出图
退出码 0 = C 盘新建字节为 0；非 0 = 又有人把 Chrome 的临时件写回 C 盘了。

为什么必须实测而不是读代码：
2026-10-11 那次"清扫器全绿"是假的 —— shoot() 的非 empty 分支命中缓存直接 return，
一次 Chrome 都没启，量出来当然是 0 泄漏。这里强制走 empty 分支（每次都真渲）。
"""
import glob
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import _tmpsweep as T  # noqa: E402
import furn_audit as FA  # noqa: E402  复用审核台的真出图参数

CTMP = os.environ.get("TEMP") or os.environ.get("TMP")


def tree_mb(root):
    total = 0
    for d in glob.glob(os.path.join(root, "*")):
        if os.path.isdir(d):
            for r, _, fs in os.walk(d):
                for f in fs:
                    try:
                        total += os.path.getsize(os.path.join(r, f))
                    except OSError:
                        pass
    return total / 1048576.0


CHROME_PATS = ("scoped_dir*", "HeadlessChrome*")   # Chrome 自己的签名
# 不数"任何新建目录"：无关进程也会往 %TEMP% 丢空目录（实测一次批量里混进
# 2 个 GUID 空目录，与 Chrome 无关），那样尺子会假报警。


def chrome_dirs(root):
    out = []
    for pat in CHROME_PATS:
        out += [d for d in glob.glob(os.path.join(root, pat)) if os.path.isdir(d)]
    return out


def dir_mb(path):
    total = 0
    for r, _, fs in os.walk(path):
        for f in fs:
            try:
                total += os.path.getsize(os.path.join(r, f))
            except OSError:
                pass
    return total / 1048576.0


def batch(n, workers=3):
    """并发批量出图（和生产脚本同样的 3 worker）：量 C 盘写入 + 数 drop() 失败残留。
    用法：python scripts/shot_temp_measure.py --batch 30
    """
    from concurrent.futures import ThreadPoolExecutor
    b_names = {os.path.basename(d) for d in chrome_dirs(CTMP)}
    jobs = [(FA.ITEMS[i % len(FA.ITEMS)], "south", "45°", True) for i in range(n)]
    t0 = time.time()
    fail = 0
    with ThreadPoolExecutor(max_workers=workers) as ex:
        for p, err in ex.map(FA.shoot, jobs):
            if err:
                fail += 1
    dt = time.time() - t0
    new = [d for d in chrome_dirs(CTMP) if os.path.basename(d) not in b_names]
    left = [d for d in glob.glob(os.path.join(T.TMPROOT, "*")) if os.path.isdir(d)]
    print("%d 格 / %d worker: %.0fs（%.1fs/格），出图失败 %d" % (n, workers, dt, dt / n, fail))
    print("C 盘 Chrome 特征目录新增: %d 个 / %.1f MB" % (len(new), sum(dir_mb(d) for d in new)))
    print("D 盘沙盒残留(drop 没删掉的): %d 个 / %.1f MB"
          % (len(left), sum(dir_mb(d) for d in left)))
    got = T.sweep_mine()
    left2 = [d for d in glob.glob(os.path.join(T.TMPROOT, "*")) if os.path.isdir(d)]
    print("sweep_mine() 收回 %d 个，收尾后残留 %d 个" % (got, len(left2)))
    ok = len(new) == 0 and not left2 and not fail
    print("结论: %s" % ("PASS C 盘零写入、沙盒全回收" if ok else "FAIL"))
    return 0 if ok else 1


def main():
    if "--batch" in sys.argv:
        i = sys.argv.index("--batch")
        n = int(sys.argv[i + 1]) if len(sys.argv) > i + 1 else 24
        return batch(n)
    runs = int(sys.argv[1]) if len(sys.argv) > 1 else 3
    print("通路: furn_audit.shoot(empty 分支, 每次都真启 Chrome)  次数: %d" % runs)
    print("沙盒根: %s (%s 盘)" % (T.TMPROOT, T._drive(T.TMPROOT)))
    n_leak, mb_leak, d_peak = 0, 0.0, 0.0
    for i in range(runs):
        b_bytes = sum(dir_mb(d) for d in chrome_dirs(CTMP))
        b_names = {os.path.basename(d) for d in chrome_dirs(CTMP)}
        t0 = time.time()
        p, err = FA.shoot((FA.ITEMS[0], "south", "45°", True))
        dt = time.time() - t0
        if err:
            print("  出图失败:", err)
            return 1
        now = chrome_dirs(CTMP)
        new = [d for d in now if os.path.basename(d) not in b_names]
        mb_new = sum(dir_mb(d) for d in new)
        # 清扫器会把上一份也算进来，用字节增量而不是绝对数
        mb_leak += mb_new
        n_leak += len(new)
        d_peak = max(d_peak, tree_mb(T.TMPROOT))
        print("  #%d %.1fs 图 %dKB  C 盘 Chrome 目录新建 %d 个/%.2f MB  D 盘沙盒 %.2f MB"
              % (i + 1, dt, os.path.getsize(p) // 1024, len(new), mb_new,
                 tree_mb(T.TMPROOT)))
    print("\n== 结论 ==")
    print("每次出图在 C 盘留下的 Chrome 特征目录: %.1f 个, %.2f MB（外推 720 格 = %.2f GB）"
          % (n_leak / runs, mb_leak / runs, mb_leak / runs * 720 / 1024.0))
    print("D 盘沙盒峰值: %.2f MB（正常应接近 0：drop() 用完整目录删）" % d_peak)
    ok = n_leak == 0
    print("结论: %s" % ("PASS 出图不写 C 盘" if ok else "FAIL 仍有 %d 个目录落在 C 盘" % n_leak))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
