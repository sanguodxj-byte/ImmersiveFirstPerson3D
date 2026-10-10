# AI生成
# 把 4K 验收截图压成可贴对话的 JPG（1280 宽，拼接对比图用）
import sys, os
from PIL import Image
SHOTS = r"C:\Users\Administrator\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Screenshots"
OUT = r"D:\rim mod\FirstPerson3D\shots"
os.makedirs(OUT, exist_ok=True)

def shrink(name, width=1280):
    src = os.path.join(SHOTS, name + ".png")
    dst = os.path.join(OUT, name + ".jpg")
    if not os.path.exists(src):
        print("missing", src); return None
    im = Image.open(src).convert("RGB")
    h = int(im.height * width / im.width)
    im.resize((width, h), Image.LANCZOS).save(dst, quality=88)
    print(dst)
    return dst

if __name__ == "__main__":
    for n in sys.argv[1:]:
        shrink(n)
