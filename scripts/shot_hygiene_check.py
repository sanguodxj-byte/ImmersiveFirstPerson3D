# AI生成
# -*- coding: utf-8 -*-
"""截图通路卫生闸门：凡是启动无头 Chrome 的脚本，都必须走 _tmpsweep 那一条口径。

为什么要有这条：泄漏不是写错的 bug，是"没人知道有规矩"。
2026-10-11 的 C 盘撑爆来自每次 Chrome 启动在 %TEMP% 留一个 23~26MB 的 scoped_dir
（指定 --user-data-dir 也拦不住），720 格审核 = 16.5GB。已经统一改成
D 盘沙盒 + 出完图 drop()，但下一个新增的出图脚本只要 tempfile.mkdtemp 一下，
就会重新往 C 盘写 —— 靠人记得住是不成立的，必须机检。

用法：
    python scripts/shot_hygiene_check.py            # 扫全仓库 + 跑夹具自证
退出码 0 = 夹具过且存量 0 缺陷。

判据（每条都有合成夹具，见 FIXTURES；夹具不过就拒绝给存量背书）：
  no-import  启了 Chrome 却没 import _tmpsweep（没接上口径）
  no-env     启动 Chrome 的 subprocess 没传 env=（scoped_dir 落回 C 盘 %TEMP%）
  c-temp     用 tempfile.mkdtemp/gettempdir/NamedTemporaryFile 而没给 dir=（写 C 盘）
  no-drop    整个文件没有任何 drop()（沙盒只能等兜底扫，进程一死就没人管）
  sweep-zero sweep(age_s=0)：沙盒根是全仓库共用的，会把并发脚本正在用的沙盒删掉
"""
import ast
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SCAN_DIRS = ["scripts", "viewer", "tools"]
SKIP_NAME_PARTS = ("__pycache__", ".chrome", "vendor")


def _chrome_symbols(tree):
    """把"哪个变量里装着 chrome 可执行文件"算出来：cmd = [CHROME, ...] 这种写法
    在 dump 里看不到 CHROME（参数只是个 Name），上一版就是这么漏掉 furn_audit 的。
    不迭代到不动点会漏 cmd2 = cmd + [...] 这类接力。"""
    known = set()
    # 常量：直接赋值含 chrome.exe / CHROME 字面量
    for node in ast.walk(tree):
        if isinstance(node, (ast.Assign,)):
            dump = ast.dump(node.value)
            if "CHROME" in dump or "chrome.exe" in dump.lower():
                for t in node.targets:
                    if isinstance(t, ast.Name):
                        known.add(t.id)
    # 接力：值引用了已知 chrome 名
    for _ in range(4):
        added = False
        for node in ast.walk(tree):
            if isinstance(node, ast.Assign):
                dump = ast.dump(node.value)
                if any(n in dump for n in known) or "CHROME" in dump:
                    for t in node.targets:
                        if isinstance(t, ast.Name) and t.id not in known:
                            known.add(t.id)
                            added = True
        if not added:
            break
    return known


def _chrome_calls(tree):
    known = _chrome_symbols(tree)
    out = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        name = getattr(node.func, "attr", None) or getattr(node.func, "id", None) or ""
        if name not in ("run", "Popen", "check_output", "check_call", "call"):
            continue
        if not node.args:
            continue
        a0 = node.args[0]
        blob = ast.dump(a0)
        hit = ("CHROME" in blob or "chrome.exe" in blob.lower()
               or (isinstance(a0, ast.Name) and a0.id in known))
        if hit:
            out.append(node)
    return out


def _has_chrome(tree):
    return bool(_chrome_calls(tree))


def scan(src, path=""):
    """一个源文件 -> 缺陷列表。"""
    bad = []
    try:
        tree = ast.parse(src)
    except SyntaxError as e:
        return ["%s: 语法错 %s" % (path, e)]

    if not _has_chrome(tree):
        return bad

    imports = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom) and node.module:
            imports.add(node.module)
        elif isinstance(node, ast.Import):
            for a in node.names:
                imports.add(a.name)
    if "_tmpsweep" not in imports:
        bad.append("no-import: %s 启 Chrome 但没接 _tmpsweep 口径" % path)

    for c in _chrome_calls(tree):
        if not any(k.arg == "env" for k in c.keywords):
            bad.append("no-env: %s 第 %d 行启动 Chrome 没传 env=（scoped_dir 会落 C 盘）"
                       % (path, c.lineno))

    for node in ast.walk(tree):
        if not isinstance(node, ast.Call):
            continue
        f = node.func
        fname = getattr(f, "attr", None) or getattr(f, "id", None)
        holder = getattr(f, "value", None)
        mod = getattr(holder, "id", "") if isinstance(holder, ast.Name) else ""
        if mod == "tempfile" and fname in ("mkdtemp", "gettempdir", "NamedTemporaryFile",
                                           "mkstemp", "TemporaryDirectory"):
            if not any(k.arg == "dir" for k in node.keywords):
                bad.append("c-temp: %s 第 %d 行 tempfile.%s 没给 dir=（默认写 C 盘 %%TEMP%%）"
                           % (path, node.lineno, fname))
        if fname == "sweep":
            zero = (len(node.args) == 1 and isinstance(node.args[0], ast.Constant)
                    and node.args[0].value == 0)
            kw0 = any(k.arg == "age_s" and isinstance(k.value, ast.Constant)
                      and k.value.value == 0 for k in node.keywords)
            if zero or kw0:
                bad.append("sweep-zero: %s 第 %d 行 sweep(0) 会删掉并发脚本在用的沙盒"
                           % (path, node.lineno))

    calls = {getattr(n.func, "attr", None) or getattr(n.func, "id", None)
             for n in ast.walk(tree) if isinstance(n, ast.Call)}
    if "drop" not in calls:
        bad.append("no-drop: %s 没有 drop()，沙盒只能等兜底扫描" % path)
    return bad


# 夹具：每条判据都要"该报的报、不该报的不报"
CLEAN = '''
import subprocess
from _tmpsweep import scratch, env_for, profile, drop, sweep
CHROME = "chrome.exe"
def shoot(u, p):
    rd = scratch("x")
    try:
        subprocess.run([CHROME, "--headless=new", "--user-data-dir=" + profile(rd),
                        "--screenshot=" + p, u], env=env_for(rd))
    finally:
        drop(rd)
        sweep()
'''

CLEAN_IND = '''
import subprocess
from _tmpsweep import scratch, env_for, profile, drop, sweep
CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe"
def shoot(u, p):
    rd = scratch("x")
    cmd = [CHROME, "--headless=new", "--user-data-dir=" + profile(rd),
           "--screenshot=" + p, u]
    try:
        subprocess.run(cmd, env=env_for(rd))
    finally:
        drop(rd)
        sweep()
'''

FX = [
    ("no-env", CLEAN.replace(', env=env_for(rd))', ')'), ["no-env"]),
    # 真仓库里多数脚本是 subprocess.run(cmd, ...)：参数只是个变量名。
    # 上一版判据只看字面量，把 furn_audit / plant_shots / sky_shots / furn_shape_check
    # 全放过了 —— 这条夹具就是专门盯这个假阴性。
    ("indirect-no-env", CLEAN_IND.replace(", env=env_for(rd)", ""), ["no-env"]),
    ("indirect-c-temp", CLEAN_IND.replace('rd = scratch("x")',
                                          'import tempfile\n    rd = tempfile.mkdtemp()'),
     ["c-temp"]),
    ("indirect-clean", CLEAN_IND, []),
    ("c-temp", CLEAN.replace("rd = scratch(\"x\")",
                             "import tempfile\n    rd = tempfile.mkdtemp(prefix='y')"),
     ["c-temp"]),
    ("no-import", CLEAN.replace("from _tmpsweep import scratch, env_for, profile, drop, sweep",
                                "def scratch(p): return '.'"), ["no-import"]),
    ("no-drop", CLEAN.replace("drop(rd)", "pass"), ["no-drop"]),
    ("sweep-zero", CLEAN.replace("sweep()", "sweep(0)"), ["sweep-zero"]),
    ("clean", CLEAN, []),
    # node --check 这类普通 subprocess 不算启 Chrome（否则 js_check 被误判成要传 env）
    ("other-subprocess",
     'import subprocess\nsubprocess.run(["node", "--check", "a.js"])\n', []),
    # 不启 Chrome 的文件必须完全跳过（否则一堆普通脚本被误伤）
    ("no-chrome", "import os\nprint(os.getcwd())\n", []),
]


def fixtures():
    bad = []
    for name, src, expect in FX:
        got = scan(src, "fx-" + name)
        kinds = [g.split(":")[0] for g in got]
        if sorted(kinds) != sorted(expect):
            bad.append("夹具 %s: 期望 %r 实得 %r" % (name, expect, kinds))
    return bad


def repo_files():
    for d in SCAN_DIRS:
        base = os.path.join(ROOT, d)
        if not os.path.isdir(base):
            continue
        for root, dirs, files in os.walk(base):
            dirs[:] = [x for x in dirs if x not in SKIP_NAME_PARTS and not x.startswith(".")]
            for f in files:
                if f.endswith(".py"):
                    yield os.path.join(root, f)


def main():
    fx = fixtures()
    for m in fx:
        print("  [fixture]", m)
    if fx:
        print("结论: FAIL 夹具未过 —— 闸门本身不可信，拒绝给存量背书")
        return 2

    launchers, bad = [], []
    for p in repo_files():
        try:
            src = open(p, encoding="utf-8").read()
        except Exception as e:
            print("  [skip] %s: %s" % (p, e))
            continue
        import ast as _a
        try:
            t = _a.parse(src)
        except SyntaxError:
            continue
        if _has_chrome(t):
            launchers.append(os.path.relpath(p, ROOT))
        for m in scan(src, os.path.relpath(p, ROOT)):
            bad.append(m)

    print("== 启 Chrome 的脚本（%d 个）==" % len(launchers))
    for f in launchers:
        print("  ", f)
    for m in bad:
        print("  [defect]", m)
    print("结论: %s" % ("PASS 截图通路全部在一条口径上" if not bad
                        else "FAIL %d 处缺陷" % len(bad)))
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
