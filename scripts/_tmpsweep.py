# AI生成
# -*- coding: utf-8 -*-
"""无头 Chrome 出图的临时目录卫生 —— 所有截图脚本共用的一条口径。

病根（2026-10-11 走真出图通路实测，见 scripts/_tmpmeasure.py）：
每启动一次无头 Chrome，它都在 %TEMP% 新建一个 scoped_dir<PID>_<rand>，
本机 23~26MB（crashpad + CRX_INSTALL 现装一遍外部扩展），
**显式指定 --user-data-dir 也拦不住**。720 格审核 = 16.5GB 全砸 C 盘；
Python 侧 tempfile.mkdtemp 的 profile 再加 13.5MB/次，也在 C 盘。
旧口径"运行期起线程定期扫"是在和写入速度抢时间：静置阈值 90~120s，
2 个 worker 约 42MB/s，峰值先堆到 5~6GB；进程一死（Ctrl-C / 崩 / 超时被杀）
就再没人扫。

改法：不扫 C 盘，让 Chrome 根本不写 C 盘。
- 每次启动一个 D 盘项目内的独立沙盒，env 的 TEMP/TMP/TMPDIR 全指过去
  （已实测 scoped_dir 跟随 env TEMP），--user-data-dir 也落在沙盒里；
- 沙盒按"一次启动"独占，出完图整目录删 —— 不需要静置阈值，
  也就不存在误删并发 worker 正在用的那份；
- sweep() 退成崩溃兜底：只收没人认领的老沙盒 + 历史遗留在 C 盘的那几个前缀。

调用方必须跑 selftest() 才算通过（见文件末尾的夹具）。
"""
import glob
import os
import shutil
import sys
import tempfile
import threading
import time

MOD = r"D:\rim mod\FirstPerson3D"
TMPROOT = os.path.join(MOD, "temp_shots")   # 必须在 D 盘：见 _drive()
# 历史遗留：迁到 D 盘之前，各家脚本在 C 盘 %TEMP% 留下的 profile 前缀
LEGACY = ("scoped_dir*", "HeadlessChrome*", "furnshot-*", "shapeshot*",
          "plantshot-*", "skyshot-*", "furnguard-*")


def _drive(path):
    return os.path.splitdrive(os.path.abspath(path))[0].upper()


def scratch(prefix="shot"):
    """一次 Chrome 启动一个沙盒（D 盘，项目内，含 pid+序号避免互相抢锁）。"""
    os.makedirs(TMPROOT, exist_ok=True)
    return tempfile.mkdtemp(prefix="%s_%d_" % (prefix, os.getpid()), dir=TMPROOT)


def env_for(rd):
    """把子进程的 TEMP/TMP/TMPDIR 按到沙盒上：Chrome 的 scoped_dir 跟着走。"""
    return dict(os.environ, TEMP=rd, TMP=rd, TMPDIR=rd)


def profile(rd, name="profile"):
    """--user-data-dir 用的目录，落在沙盒内（每启动一份，并发不抢锁）。"""
    p = os.path.join(rd, name)
    os.makedirs(p, exist_ok=True)
    return p


def drop(rd):
    """整沙盒删除。Chrome 退出后 crashpad 偶尔还攥着 CrashpadMetrics-*.pma 不放，
    一次性 rmtree(ignore_errors=True) 会静默失败 -> 目录泄漏（实测 3631 个），所以重试。
    726 格实测重试 3s 仍有 10 次没删掉（1.4%），窗口放到 ~8s；剩下靠 sweep_mine() 收。"""
    for _ in range(24):
        try:
            shutil.rmtree(rd)
            return True
        except OSError:
            if not os.path.exists(rd):
                return True
            time.sleep(0.35)
    return not os.path.exists(rd)


def _tree_mb(path):
    total = 0
    for root, _, files in os.walk(path):
        for f in files:
            try:
                total += os.path.getsize(os.path.join(root, f))
            except OSError:
                pass
    return total


def _aged(path, age_s):
    """age_s<=0 一律算老；否则按 mtime 判"静置够了没"。"""
    if age_s <= 0:
        return True
    try:
        return time.time() - os.path.getmtime(path) >= age_s
    except OSError:
        return False


def sweep(age_s=180):
    """崩溃兜底：删掉没人认领的老沙盒 + C 盘 %TEMP% 里的历史遗留。
    正常路径靠 drop()，不该依赖这里。返回删除的目录数。

    默认只动静置 >=180s 的：沙盒根目录是全仓库共用的，一次活的沙盒只存在
    一两秒，180s 足够安全；而 sweep(0) 会把**并发跑的别的脚本**正在用的
    沙盒一起删掉（出图静默变空手而归），所以年龄阈值放进默认值而不是让调用方记。"""
    n = 0
    for rd in glob.glob(os.path.join(TMPROOT, "*")):
        if os.path.isdir(rd) and _aged(rd, age_s):
            if drop(rd):
                n += 1
    ctmp = os.environ.get("TEMP") or os.environ.get("TMP") or ""
    for pat in LEGACY:
        for d in glob.glob(os.path.join(ctmp, pat)):
            if os.path.isdir(d) and _aged(d, age_s) and drop(d):
                n += 1
    return n


def sweep_mine():
    """立刻收掉**本进程 pid** 建的沙盒，别处并发脚本的一份不碰。

    drop() 偶尔因 crashpad 还攥着句柄而失败（726 格实测 10 次 / 1.4%），
    残留就在这儿回收 —— 不用等静置阈值。只能在自家 worker 全部结束后调，
    否则会删掉本进程正在渲染的那几格。返回删除数。"""
    tag = "_%d_" % os.getpid()
    n = 0
    for rd in glob.glob(os.path.join(TMPROOT, "*")):
        if os.path.isdir(rd) and tag in os.path.basename(rd) and drop(rd):
            n += 1
    return n


def start_sweeper(interval=20, age_s=180):
    """长批量跑时的兜底线程（被杀/崩溃留下的沙盒）。返回 stop 事件。"""
    stop = threading.Event()

    def _run():
        while not stop.is_set():
            sweep(age_s)
            stop.wait(interval)

    threading.Thread(target=_run, daemon=True).start()
    return stop


# ---------------------------------------------------------------- 自测夹具
def selftest():
    """断言"该删的删、不该删的不删"。返回缺陷列表，空 = 过。

    没有夹具的清扫器不算修好：首轮 720 格"全绿"曾经只是因为
    shoot() 命中缓存、一次 Chrome 都没启（见 _tmpmeasure.py 的注释）。
    """
    bad = []
    if _drive(TMPROOT) != "D:":
        bad.append("TMPROOT 不在 D 盘：%s" % TMPROOT)

    # 1) 沙盒/env/profile 都落在 D 盘沙盒内，且 env 里没有 C 盘 TEMP
    rd = scratch("selftest")
    try:
        if not os.path.abspath(rd).startswith(TMPROOT):
            bad.append("scratch() 不在 TMPROOT 下：%s" % rd)
        e = env_for(rd)
        if e.get("TEMP") != rd or _drive(e.get("TEMP", "")) != "D:":
            bad.append("env_for() 没把 TEMP 按到沙盒：%r" % e.get("TEMP"))
        pr = profile(rd)
        if os.path.dirname(pr) != rd:
            bad.append("profile() 跑到了沙盒外：%s" % pr)
        fake = os.path.join(rd, "scoped_dir_inecho")
        os.makedirs(fake)
        with open(os.path.join(fake, "blob.bin"), "wb") as f:
            f.write(b"\0" * (1 << 20))     # 1MB：模拟 scoped_dir 里的扩展+crashpad
        mb_before = _tree_mb(rd)
        if not drop(rd):
            bad.append("drop() 没能删掉整沙盒")
        if mb_before < 1 << 20:
            bad.append("夹具没造出体积（%.1f KB）" % (mb_before / 1024.0))
    finally:
        shutil.rmtree(rd, ignore_errors=True)

    # 2) 老沙盒被 sweep 收掉（该删的删）
    old = scratch("selftestold")
    fresh = scratch("selftestfresh")
    ctmp = os.environ.get("TEMP") or os.environ.get("TMP") or ""
    fake_old = os.path.join(ctmp, "scoped_dir_selftest_old")
    fake_fresh = os.path.join(ctmp, "notchrome_selftest")
    try:
        for p in (old, fake_old):
            os.makedirs(p, exist_ok=True)
            open(os.path.join(p, "x"), "w").close()
            os.utime(p, (time.time() - 7200, time.time() - 7200))
        for p in (fresh, fake_fresh):
            os.makedirs(p, exist_ok=True)
            open(os.path.join(p, "x"), "w").close()
        sweep(age_s=600)
        if os.path.exists(old):
            bad.append("sweep 没删掉静置的老沙盒")
        if os.path.exists(fake_old):
            bad.append("sweep 没删掉 C 盘遗留的 scoped_dir")
        if not os.path.exists(fresh):
            bad.append("sweep 误删了正在用的新沙盒")
        if not os.path.exists(fake_fresh):
            bad.append("sweep 越界删了不匹配前缀的 %TEMP% 目录")
    finally:
        for p in (old, fresh, fake_old, fake_fresh):
            shutil.rmtree(p, ignore_errors=True)

    # 4) sweep_mine() 双向：本进程 pid 的沙盒立刻收掉，别人 pid 命名的那份不许碰
    mine = scratch("mine")
    other = os.path.join(TMPROOT, "shot_999999_notmine")
    try:
        os.makedirs(other, exist_ok=True)
        open(os.path.join(other, "x"), "w").close()
        sweep_mine()
        if os.path.exists(mine):
            bad.append("sweep_mine 没收回本进程留下的沙盒")
        if not os.path.exists(other):
            bad.append("sweep_mine 越界删了别的进程（别的 pid）的沙盒")
    finally:
        shutil.rmtree(other, ignore_errors=True)
        shutil.rmtree(mine, ignore_errors=True)
    return bad


if __name__ == "__main__":
    errs = selftest()
    for e in errs:
        print("  [selftest]", e)
    print("结论: %s" % ("PASS 临时目录卫生自测" if not errs else "FAIL %d 处" % len(errs)))
    sys.exit(0 if not errs else 1)
