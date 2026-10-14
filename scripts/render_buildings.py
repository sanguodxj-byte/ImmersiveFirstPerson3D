# -*- coding: utf-8 -*-
"""AI生成 · 建筑与远古残骸高清四视图渲染脚本（scripts/render_buildings.py）
输出扁平目录 inspect_buildings/，供视觉验收逐个审查。
"""
import http.server
import json
import os
import socketserver
import subprocess
import sys
import threading
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
sys.path.insert(0, os.path.join(ROOT, "scripts"))
import _tmpsweep

CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
OUT_DIR = os.path.join(ROOT, "inspect_buildings")
W, H = 1280, 360

WALL_KEYS = [
    'wall_sandstone', 'wall_granite', 'wall_limestone', 'wall_slate', 'wall_marble',
    'wall_wood', 'wall_steel', 'wall_plasteel', 'wall_gold',
    'smoothed_sandstone', 'smoothed_granite', 'smoothed_marble',
    'fortified_wall', 'gravship_hull', 'burrow_wall',
    'smoothed_ice', 'solid_ice', 'gray_wall', 'voidmetal_wall', 'fleshmass_wall',
    'sandbags', 'barricade_wood', 'barricade_steel',
    'fence_wood', 'fence_steel', 'ancient_fence',
    'vent', 'cooler', 'wall_lamp', 'wall_torch',
]

WRECK_KEYS = [
    'ruinedtank', 'crashedshippart', 'ancientwarwalkershell',
    'largeengineblock', 'giantwheela', 'concretebarrier', 'rustedindustrialtruck',
]

def run():
    os.makedirs(OUT_DIR, exist_ok=True)
    class Handler(http.server.SimpleHTTPRequestHandler):
        def __init__(self, *a, **kw):
            super().__init__(*a, directory=ROOT, **kw)
        def log_message(self, *a):
            pass

    httpd = socketserver.TCPServer(("127.0.0.1", 0), Handler)
    port = httpd.server_address[1]
    threading.Thread(target=httpd.serve_forever, daemon=True).start()

    rd = _tmpsweep.scratch("bld_render")
    try:
        # 1. 渲染全部 30 个墙体/掩体/设备
        for k in WALL_KEYS:
            out_png = os.path.join(OUT_DIR, f"{k}_4view.png")
            url = f"http://127.0.0.1:{port}/viewer/walls.html?triptych=1&item={k}"
            cmd = [
                CHROME,
                f"--user-data-dir={_tmpsweep.profile(rd)}",
                "--headless=new",
                "--disable-gpu",
                f"--window-size={W},{H}",
                "--virtual-time-budget=4000",
                f"--screenshot={out_png}",
                url,
            ]
            subprocess.run(cmd, env=_tmpsweep.env_for(rd), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            size = os.path.getsize(out_png) if os.path.exists(out_png) else 0
            print(f"Wall: {k} -> {out_png} ({size} bytes)")

        # 2. 渲染全部 7 个远古残骸
        for k in WRECK_KEYS:
            out_png = os.path.join(OUT_DIR, f"wreck_{k}_4view.png")
            url = f"http://127.0.0.1:{port}/viewer/wreck.html?triptych=1&item={k}"
            cmd = [
                CHROME,
                f"--user-data-dir={_tmpsweep.profile(rd)}",
                "--headless=new",
                "--disable-gpu",
                f"--window-size={W},{H}",
                "--virtual-time-budget=4000",
                f"--screenshot={out_png}",
                url,
            ]
            subprocess.run(cmd, env=_tmpsweep.env_for(rd), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            size = os.path.getsize(out_png) if os.path.exists(out_png) else 0
            print(f"Wreck: {k} -> {out_png} ({size} bytes)")
    finally:
        httpd.shutdown()
        _tmpsweep.drop(rd)
        _tmpsweep.sweep_mine()

if __name__ == "__main__":
    run()
