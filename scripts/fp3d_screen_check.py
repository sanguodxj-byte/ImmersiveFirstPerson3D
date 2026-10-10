# AI生成
# FP3D 抬屏验收：截图程序化像素分析（无视觉模型时的客观判据）
# 用法: python fp3d_screen_check.py <png> [mode]
#   mode = term  : 检测手持终端（暖灰机身带 + 深色握把 + 中央青色屏幕UI）
#   mode = vanilla: 检测原版俯视 UI（顶栏/底栏/资源条色带）
import sys
from PIL import Image

def region(img, x0, y0, x1, y1):
    W, H = img.size
    return img.crop((int(W*x0), int(H*y0), int(W*x1), int(H*y1)))

def stats(im):
    px = list(im.getdata())
    n = len(px)
    r = sum(p[0] for p in px)/n; g = sum(p[1] for p in px)/n; b = sum(p[2] for p in px)/n
    # 青色屏幕UI像素 (g、b 明显高于 r)
    cyan = sum(1 for p in px if p[1] > p[0]+25 and p[2] > p[0]+20 and p[1] > 60)/n
    # 暖灰机身像素（r>g>b 且差值小、中等亮度）
    warm = sum(1 for p in px if p[0] >= p[1] >= p[2] and 25 < p[0]-p[2] < 95 and 70 < p[0] < 210)/n
    dark = sum(1 for p in px if p[0] < 60 and p[1] < 60 and p[2] < 70)/n
    sky = sum(1 for p in px if p[2] > p[0]+20 and p[2] > 120)/n
    return dict(avg=(round(r),round(g),round(b)), cyan=round(cyan,4), warm=round(warm,4), dark=round(dark,4), sky=round(sky,4))

def main():
    path, mode = sys.argv[1], (sys.argv[2] if len(sys.argv) > 2 else "term")
    img = Image.open(path).convert("RGB")
    W, H = img.size
    print(f"{path}  {W}x{H}  mode={mode}")
    if mode == "term":
        # 终端到位时约占画面中下部：中央带（屏幕UI+机身）
        center = stats(region(img, 0.30, 0.25, 0.70, 0.75))
        bottom = stats(region(img, 0.25, 0.75, 0.75, 0.99))   # 握把/机身下缘
        upper  = stats(region(img, 0.0, 0.0, 1.0, 0.22))      # 天空/场景
        print("center :", center)
        print("bottom :", bottom)
        print("upper  :", upper)
        verdict = (center["cyan"] > 0.05 or center["warm"] > 0.10) and center["dark"] > 0.02
        print("TERMINAL-VISIBLE" if verdict else "NO-TERMINAL")
    elif mode == "vanilla":
        top = stats(region(img, 0.0, 0.0, 1.0, 0.055))        # 顶栏
        bottom = stats(region(img, 0.0, 0.945, 1.0, 1.0))     # 主按钮栏
        mid = stats(region(img, 0.0, 0.3, 1.0, 0.7))          # 地图区
        print("topbar :", top)
        print("botbar :", bottom)
        print("mapmid :", mid)
        # 原版 UI 特征：顶/底栏存在成片结构化色块（非纯场景），底栏含暖色按钮
        verdict = (top["dark"] > 0.15 or top["warm"] > 0.05) and (bottom["dark"] > 0.10 or bottom["warm"] > 0.05)
        print("VANILLA-UI-VISIBLE" if verdict else "NO-VANILLA-UI")

if __name__ == "__main__":
    main()
