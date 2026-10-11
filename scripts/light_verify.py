#!/usr/bin/env python3
# F013 光照实机验收驱动（GABS）。
#
# 做的事：开一张调试图 -> 进第一人称 -> 在附身者旁边起一段墙 + 一堆篝火（现成光源）
#   -> 依次切正午 / 深夜 / 关灯 / 关光照，每次让 mod 自己 dump 顶点色统计
#   -> 落到 temp_light/*.json，交给 scripts/light_check.py 判。
#
# 输入全部来自游戏本体（RimBridge 回读 + 游戏内截图），本脚本不自己造光照数值。
# 用法：python scripts/light_verify.py [--skip-walls]
import json
import os
import re
import subprocess
import sys
import time
import urllib.request

MCP = "http://localhost:8090/mcp"
REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "temp_light")
SHOT_DIR = os.path.expanduser(
    "~/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Screenshots")


def call(tool, args=None, timeout=180):
    req = urllib.request.Request(MCP)
    req.add_header("Content-Type", "application/json")
    p = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                    "params": {"name": tool, "arguments": args or {}}}).encode()
    with urllib.request.urlopen(req, p, timeout=timeout) as r:
        d = json.loads(r.read())
    if "error" in d:
        raise RuntimeError(json.dumps(d["error"], ensure_ascii=False)[:300])
    return d.get("result", {})


def gtool(name, args=None, timeout=180):
    """游戏侧工具调用。两件事要自己处理：
    - 别的 mod 一直在刷 error 日志，RimBridge 会把每次调用挡下来要人确认 -> 自动确认再重试。
    - 进第一人称那种大改帧时间的动作会把 GABP 长连接掐了（missing Content-Length）-> 重连再试。
    """
    last = None
    for _ in range(16):
        r = call("games_call_tool", {"gameId": "rimworld", "tool": name, "arguments": args or {}},
                 timeout=timeout)
        last = r
        res = r.get("result", r) if isinstance(r, dict) else {}
        txt = json.dumps(res, ensure_ascii=False)
        att = (res.get("structuredContent") or {}).get("attention") or {}
        aid = att.get("attentionId")
        if aid:
            # 无关 mod 每几秒就再抛一条 error，确认完立刻重试，慢一秒就又被打回
            try:
                call("games_ack_attention", {"gameId": "rimworld", "attentionId": aid}, 30)
            except Exception as e:
                log("ack 失败: " + str(e)[:100])
            time.sleep(0.2)
            continue
        if "GABP connection unavailable" in txt or '"availableTotal": 0' in txt:
            # 游戏一卡（大改帧时间的动作、后台最小化）GABP 长连接就断，工具表清空。重连要等它重新注册。
            try:
                call("games_connect", {"gameId": "rimworld"}, 150)
            except Exception as e:
                log("reconnect 失败: " + str(e)[:100])
            time.sleep(6)
            continue
        return res
    return last.get("result", last) if isinstance(last, dict) else last if isinstance(last, dict) else last


RESTORE_PS = r'''
$sig = @'
using System;using System.Runtime.InteropServices;
public class W{
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
Add-Type -TypeDefinition $sig
$p = Get-Process -Name RimWorldWin64 -ErrorAction SilentlyContinue | Select-Object -First 1
if ($p) {
  [W]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
  [W]::ShowWindow($p.MainWindowHandle, 3) | Out-Null
  [W]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
  Write-Output "restored $($p.Id)"
} else { Write-Output "no-process" }
'''


def restore_window():
    """GABS 起的游戏是后台最小化的；主线程一被挂起，桥就超时、截图也是空矩形。
    脚本落在无空格的临时路径里（仓库路径带空格，PowerShell -File 会咬不住）。"""
    try:
        tmp = os.path.join(os.environ.get("TEMP", "C:/Temp"), "fp3d_restore_window.ps1")
        with open(tmp, "w", encoding="utf-8") as f:
            f.write(RESTORE_PS)
        r = subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                            "-File", tmp], capture_output=True, text=True, timeout=60)
        log("窗口: " + ((r.stdout or "").strip() or (r.stderr or "").strip()[:160]))
    except Exception as e:
        log("窗口还原失败: " + str(e)[:160])


def proc_count():
    try:
        out = subprocess.run(["tasklist"], capture_output=True, text=True, timeout=30).stdout
        return out.lower().count("rimworldwin64")
    except Exception:
        return -1


def stop_and_wait(tries=12):
    """games_stop 会「成功但进程还在」；部署前要真正确认进程退了，否则 robocopy 撞上锁定的 DLL。"""
    V_stop = lambda: call("games_stop", {"gameId": "rimworld"}, 90)
    for i in range(tries):
        V_stop()
        time.sleep(8)
        if proc_count() == 0:
            log("进程已退出")
            return True
        log("等进程退出，当前 %d 个" % proc_count())
    return proc_count() == 0


def log(msg):
    print(time.strftime("[%H:%M:%S] ") + msg, flush=True)


def blob(res):
    return json.dumps(res, ensure_ascii=False)


_act_cache = {}


def action(path):
    """1.6 的调试菜单根只有 Actions/Outputs/Settings，模组分组挂在 Actions 下面：
    先按原路径试，报「找不到」再加 Actions/ 前缀试，最后才去菜单里搜。解析结果缓存住。"""
    if path in _act_cache:
        return gtool("rimworld_rimworld_execute_debug_action", {"path": _act_cache[path]}, 120)
    cands = [path, "Actions/" + path]
    for p in cands:
        r = gtool("rimworld_rimworld_execute_debug_action", {"path": p}, 120)
        if "Could not find debug action" not in blob(r):
            _act_cache[path] = p
            return r
    found = find_action(path.split("/")[-1])
    if found:
        _act_cache[path] = found
        return gtool("rimworld_rimworld_execute_debug_action", {"path": found}, 120)
    log("找不到调试动作: " + path)
    return None


def tool_json(res):
    """把 games_call_tool 的返回拆成内层 JSON（RimBridge 的 text 字段本身就是 JSON）。"""
    if not isinstance(res, dict):
        return None
    txt = None
    for c in res.get("content", []) or []:
        if c.get("type") == "text":
            txt = c.get("text")
            break
    if txt is None:
        return res.get("structuredContent")
    try:
        return json.loads(txt)
    except Exception:
        return {"text": txt}


def read_snapshot():
    """从 settings 树里取 lightSnapshot 的值（结构化取，不做字符串猎取）。"""
    r = gtool("rimworld_rimworld_get_mod_settings", {"modId": "sanguo.firstperson3d",
                                                     "maxDepth": 3, "maxCollectionEntries": 300}, 120)
    def walk(node):
        if isinstance(node, dict):
            if node.get("name") == "lightSnapshot":
                return node.get("value")
            for v in node.values():
                got = walk(v)
                if got:
                    return got
        elif isinstance(node, list):
            for v in node:
                got = walk(v)
                if got:
                    return got
        return None
    val = walk(tool_json(r))
    if val:
        return json.loads(val)
    log("settings 里没有 lightSnapshot，改读日志的 LIGHTDUMP 行")
    logs = tool_json(gtool("rimworld_rimbridge_list_logs", {"level": "message", "tail": 80}, 120))
    for e in (logs or {}).get("logs", []):
        m = e.get("Message", "")
        i = m.find("LIGHTDUMP ")
        if i >= 0:
            return json.loads(m[i + 10:])
    raise RuntimeError("既没读到 lightSnapshot，日志里也没有 LIGHTDUMP")


def in_map():
    st = tool_json(gtool("rimworld_rimworld_get_camera_state", {}, 90)) or {}
    return st.get("mapId") is not None or st.get("focusCell") is not None or "focus" in json.dumps(st)[:400]


def ensure_map(waitSec=300):
    if in_map():
        log("已在图里")
        return True
    log("开调试图")
    gtool("rimworld_rimworld_start_debug_game", {}, 120)
    # 不用 start_debug_game_ready：那张 ready 调用在图还没好时会一直占着 30 s 超时窗口，
    # 反复重试能把一轮验收拖成半小时。直接轮询相机状态，读得到图就算好。
    t0 = time.time()
    while time.time() - t0 < waitSec:
        time.sleep(8)
        if in_map():
            log("图已就绪，用了 %d s" % int(time.time() - t0))
            return True
    log("警告：等了 %d s 仍取不到图" % waitSec)
    return False


def find_action(needle):
    """调试动作的完整路径要从菜单里取，别猜分组名。"""
    roots = tool_json(gtool("rimworld_rimworld_list_debug_action_roots", {}, 90)) or {}
    cand = json.dumps(roots, ensure_ascii=False)
    paths = re.findall(r'"(?:path|fullPath|name)":\s*"([^"]+)"', cand)
    for p in paths:
        if needle in p:
            return p
    # 分组根逐个展开一层
    for p in paths:
        try:
            ch = blob(gtool("rimworld_rimworld_list_debug_action_children", {"path": p}, 60))
        except Exception:
            continue
        hit = re.findall(r'"[^"]*' + re.escape(needle) + r'[^"]*"', ch)
        if hit:
            return p + "/" + hit[0].strip('"')
    return None


def save(name, d):
    os.makedirs(OUT, exist_ok=True)
    p = os.path.join(OUT, name)
    with open(p, "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=1)
    layers = {k: (v["meshes"], v["verts"], round(v["min"], 1), round(v["max"], 1))
              for k, v in (d.get("maps", [{}])[0].get("layers") or {}).items()} if d.get("maps") else {}
    log("%s  on=%s glow=%.2f sunY=%.2f 层统计=%s" %
        (name, d.get("on"), d.get("glow", 0), d["sunDir"][1], layers))
    return p


def setup(first_person=True):
    restore_window()
    try:
        call("games_connect", {"gameId": "rimworld"}, 150)
    except Exception as e:
        log("connect: " + str(e)[:120])
    ensure_map()
    if first_person:
        r = action("FirstPerson3D/mode: first person")
        log("mode: " + blob(r)[:160])
        time.sleep(14)
        restore_window()
        st = blob(gtool("rimworld_rimworld_get_camera_state", {}, 90))
        log("camera: " + st[:400])
        return st
    return ""


def place_props(st):
    """在相机焦点附近起一段墙 + 一堆篝火 + 一盏灯：
    没有墙就没有向阳/背阳面，没有篝火就没有光源。"""
    nums = re.findall(r'"(?:x|z|focusCellX|focusCellZ|cellX|cellZ)"\s*:\s*(\d+)', st)
    if len(nums) >= 2:
        cx, cz = int(nums[0]), int(nums[1])
    else:
        cx = cz = 60
        log("取不到焦点，按 (60,60) 布")
    log("以 (%d,%d) 为基准布墙与灯具" % (cx, cz))
    for k in range(8):
        gtool("rimworld_rimworld_spawn_thing", {"defName": "Wall", "x": cx - 4 + k, "z": cz - 5}, 60)
    for t, x, z in [("Campfire", cx, cz + 3), ("TableLamp", cx + 2, cz + 3)]:
        r = gtool("rimworld_rimworld_spawn_thing", {"defName": t, "x": x, "z": z}, 60)
        log("spawn %s: %s" % (t, blob(r)[:120]))
    time.sleep(8)


def once():
    """通路自检：进图 -> 进第一人称 -> dump -> 读回 JSON。"""
    st = setup()
    r = action("FirstPerson3D/dump lighting")
    log("dump call: " + blob(r)[:200])
    time.sleep(3)
    save("light_once.json", read_snapshot())


LOG_PATH = os.path.expanduser(
    "~/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/FirstPerson3D_light.log")


def read_log_dumps():
    """读 mod 自己落盘的光照导出（每 2 s 一行 JSON）。没有桥门控，也不是一次性抓取。"""
    if not os.path.exists(LOG_PATH):
        return []
    out = []
    with open(LOG_PATH, encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if line.startswith("{"):
                try:
                    out.append(json.loads(line))
                except Exception:
                    pass
    return out


def enable_3d():
    """设置里的 mode 在标题界面会被 Leave3D 写回 Off，只能进图后再开。
    先试调试动作，取不到就直接按 F8（模组自己的循环开关）。"""
    r = gtool("rimworld_rimworld_execute_debug_action",
              {"path": "FirstPerson3D/mode: first person"}, 90)
    if "Could not find debug action" not in blob(r):
        log("调试动作已切第一人称")
        return True
    log("调试动作不可用，改用 F8 合成按键")
    try:
        subprocess.run(["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
                        "-File", os.path.join(REPO, "scripts", "sendinput.ps1"),
                        "-Action", "key", "-Key", "F8"], capture_output=True, text=True, timeout=60)
        time.sleep(6)
        return True
    except Exception as e:
        log("F8 失败: " + str(e)[:140])
        return False


def cur_mode():
    try:
        r = tool_json(gtool("rimworld_rimworld_get_mod_settings",
                            {"modId": "sanguo.firstperson3d", "maxDepth": 3,
                             "maxCollectionEntries": 300}, 90))
        acc = {}

        def walk(n):
            if isinstance(n, dict):
                if "name" in n and "value" in n:
                    acc[n["name"]] = n["value"]
                for v in n.values():
                    walk(v)
            elif isinstance(n, list):
                for v in n:
                    walk(v)
        walk(r)
        return acc.get("mode"), acc.get("lightFileDump")
    except Exception as e:
        log("读设置失败: " + str(e)[:100])
        return None, None


def drive():
    """按自然时间推进跑一轮：白天 -> 入夜 -> 点一盏篝火，全程靠落盘 dump 看趋势。"""
    os.makedirs(OUT, exist_ok=True)
    restore_window()
    try:
        call("games_connect", {"gameId": "rimworld"}, 150)
    except Exception as e:
        log("connect: " + str(e)[:120])
    ensure_map()
    m, fd = cur_mode()
    log("进图后 mode=%s lightFileDump=%s" % (m, fd))
    if m != "FirstPerson":
        enable_3d()
    time.sleep(10)
    restore_window()
    m, fd = cur_mode()
    log("开 3D 后 mode=%s lightFileDump=%s" % (m, fd))
    if fd is not True:
        # 别指望跑完 7 分钟才发现没数据：dump 通道没开就等于整轮白跑。
        # 设置项在内存里，改磁盘上的 Config 只在下次启动生效，所以这里是退出而不是自愈。
        log("[FAIL] lightFileDump 未开，光照不会落盘。"
            "先退出游戏，把 Config/Mod_FirstPerson3D_FirstPerson3DMod.xml 里 "
            "<lightFileDump>True</lightFileDump> 补上再重启，然后重跑本脚本。")
        sys.exit(3)

    st = blob(gtool("rimworld_rimworld_get_camera_state", {}, 60))
    log("camera: " + st[:200])
    if "--skip-props" not in sys.argv:
        nums = re.findall(r'mapPosition..:\s*\{[^{}]*?x..:\s*(\d+)[^{}]*?z..:\s*(\d+)', st)
        cx, cz = (int(nums[0][0]), int(nums[0][1])) if nums else (110, 110)
        log("基准 (%d,%d)" % (cx, cz))
        for k in range(8):
            gtool("rimworld_rimworld_spawn_thing", {"defName": "Wall", "x": cx - 4 + k, "z": cz - 5}, 60)
        r = gtool("rimworld_rimworld_spawn_thing", {"defName": "Campfire", "x": cx, "z": cz + 2}, 60)
        log("campfire: " + blob(r)[:150])
        time.sleep(10)

    snap("drive_day.png")
    gtool("rimworld_rimworld_set_time_speed", {"speed": 3}, 60)
    t0 = time.time()
    seen_night = False
    while time.time() - t0 < 420:
        time.sleep(24)
        ds = read_log_dumps()
        m, fd = cur_mode()
        if not ds:
            log("dump 0 行，mode=%s dump=%s" % (m, fd))
            continue
        last = ds[-1]
        day = sum(1 for d in ds if d["sunDir"][1] > 0.2 and d.get("glow", 0) > 0.4)
        night = sum(1 for d in ds if d["sunDir"][1] < -0.05)
        def brief(d):
            out = {}
            for m in d.get("maps", []):
                for p in (m.get("layers") or []):
                    out[p["name"]] = (p["verts"], round(p["min"], 1), round(p["max"], 1))
            return out
        log("n=%d mode=%s glow=%.2f sunY=%+.2f 昼=%d 夜=%d 层=%s" % (
            len(ds), m, last.get("glow", 0), last["sunDir"][1], day, night, brief(last)))
        if night and not seen_night:
            snap("drive_night.png")
            seen_night = True
        if day >= 2 and night >= 2:
            break
    snap("drive_end.png")
    with open(os.path.join(OUT, "drive_dumps.json"), "w", encoding="utf-8") as f:
        json.dump(read_log_dumps(), f, ensure_ascii=False)
    log("落盘 dump 行数=%d，产物 %s" % (len(read_log_dumps()), OUT))


def snap(name):
    try:
        r = gtool("rimworld_rimworld_take_screenshot", {"fileName": name}, 120)
        log("shot %s: %s" % (name, blob(r)[:160]))
    except Exception as e:
        log("shot 失败: " + str(e)[:120])


def main():
    if "--drive" in sys.argv:
        drive()
        return
    if "--once" in sys.argv:
        once()
        return
    skip_walls = "--skip-walls" in sys.argv
    st = setup()
    if not skip_walls:
        place_props(st)

    log("正午")
    action("FirstPerson3D/sky: noon")
    time.sleep(10)
    action("FirstPerson3D/dump lighting")
    time.sleep(2)
    save("light_noon.json", read_snapshot())
    shot1 = gtool("rimworld_rimworld_take_screenshot", {"fileName": "fp3d_light_noon.png"}, 120)
    log("shot noon: " + blob(shot1)[:200])

    log("深夜")
    action("FirstPerson3D/sky: night")
    time.sleep(10)
    action("FirstPerson3D/dump lighting")
    time.sleep(2)
    save("light_night.json", read_snapshot())
    shot2 = gtool("rimworld_rimworld_take_screenshot", {"fileName": "fp3d_light_night.png"}, 120)
    log("shot night: " + blob(shot2)[:200])

    log("深夜 + 灯具增益 0")
    action("FirstPerson3D/lighting: lamp off")
    time.sleep(8)
    action("FirstPerson3D/dump lighting")
    time.sleep(2)
    save("light_night_lampoff.json", read_snapshot())
    action("FirstPerson3D/lighting: lamp on")

    log("光照关闭")
    action("FirstPerson3D/lighting: toggle")
    time.sleep(8)
    action("FirstPerson3D/dump lighting")
    time.sleep(2)
    save("light_off.json", read_snapshot())
    action("FirstPerson3D/lighting: toggle")
    action("FirstPerson3D/sky: preset off")
    log("完成，产物在 %s" % OUT)


if __name__ == "__main__":
    main()
