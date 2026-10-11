#!/usr/bin/env python3
# F013 光照出图：等到真正的正午与深夜各出一张干净截图（遮掉 Debug log 窗口）。
#
# 为什么不用 sky: noon 预设：这条桥的 execute_debug_action 现在解析不到
# 「FirstPerson3D/sky: noon」（实测回 "Could not find debug action 'Actions/sky: noon\\'"），
# 而游戏内时钟按 speed 3 推进，一个白天约 330 s，等得起，也不引入第二条改光通路。
import json
import sys
import time

sys.path.insert(0, "scripts")
import light_verify as lv  # noqa: E402

DAY_GLOW = 0.6
DAY_SUNY = 0.40
NIGHT_GLOW = 0.05
NIGHT_SUNY = -0.25


def close_panel():
    try:
        lv.gtool("rimworld_rimworld_close_window", {"windowType": "DebugLogWindow"}, 60)
    except Exception as e:
        lv.log("关窗失败: " + str(e)[:100])


def main():
    limit = int(sys.argv[1]) if len(sys.argv) > 1 else 900
    lv.gtool("rimworld_rimworld_set_time_speed", {"speed": 3}, 60)
    t0 = time.time()
    shot_day = shot_night = False
    while time.time() - t0 < limit:
        ds = lv.read_log_dumps()
        d = ds[-1] if ds else None
        if not d:
            lv.log("还没有 dump")
            time.sleep(10)
            continue
        g, sy = d.get("glow", 0), d["sunDir"][1]
        lv.log("glow=%.2f sunY=%+.2f n=%d" % (g, sy, len(ds)))
        if not shot_day and g > DAY_GLOW and sy > DAY_SUNY:
            close_panel()
            time.sleep(3)
            lv.snap("amb2_noon.png")
            json.dump(d, open("temp_light/shot_noon_dump.json", "w", encoding="utf-8"),
                      ensure_ascii=False)
            shot_day = True
        elif shot_day and not shot_night and g < NIGHT_GLOW and sy < NIGHT_SUNY:
            close_panel()
            time.sleep(3)
            lv.snap("amb2_night.png")
            json.dump(d, open("temp_light/shot_night_dump.json", "w", encoding="utf-8"),
                      ensure_ascii=False)
            shot_night = True
            break
        time.sleep(12)
    with open("temp_light/drive_dumps2.json", "w", encoding="utf-8") as f:
        json.dump(lv.read_log_dumps(), f, ensure_ascii=False)
    lv.log("出图：白天=%s 夜=%s" % (shot_day, shot_night))
    return 0 if (shot_day and shot_night) else 1


if __name__ == "__main__":
    sys.exit(main())
