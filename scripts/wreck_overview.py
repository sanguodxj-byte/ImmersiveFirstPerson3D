# AI生成
# -*- coding: utf-8 -*-
# 把 7 个残骸样板的「带纹理 45 度视图」拼成一张总览对照图，便于一眼验收材质/几何。
# 输入：viewer/shots/<item>__v0.png（由 wreck_shots.py 生成）
# 输出：viewer/shots/_overview_7.png
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "viewer", "shots")
ITEMS = ["ruinedtank", "crashedshippart", "ancientwarwalkershell",
         "largeengineblock", "giantwheela", "concretebarrier", "rustedindustrialtruck"]
LABEL = {
    "ruinedtank": "RuinedTank 坦克残骸",
    "crashedshippart": "CrashedShipPart 飞船残骸",
    "ancientwarwalkershell": "AncientWarwalkerShell 步行机残骸",
    "largeengineblock": "LargeEngineBlock 发动机残骸",
    "giantwheela": "GiantWheelA 巨轮",
    "concretebarrier": "ConcreteBarrier 路障",
    "rustedindustrialtruck": "RustedIndustrialTruck 卡车残骸",
}
COLS, ROWS = 2, 4
CW, CH = 640, 450
LAB = 30
FONT = r"C:\Windows\Fonts\msyh.ttc"


def main():
    canvas = Image.new("RGB", (CW * COLS, (CH + LAB) * ROWS), (18, 20, 28))
    d = ImageDraw.Draw(canvas)
    for i, item in enumerate(ITEMS):
        p = os.path.join(OUT, f"{item}__v0.png")
        im = Image.open(p).convert("RGB").resize((CW, CH))
        c, r = i % COLS, i // COLS
        x, y = c * CW, r * (CH + LAB)
        canvas.paste(im, (x, y + LAB))
        d.text((x + 10, y + 7), LABEL.get(item, item), fill=(207, 214, 228))
    out = os.path.join(OUT, "_overview_7.png")
    canvas.save(out)
    print("wrote", out, canvas.size)


if __name__ == "__main__":
    main()