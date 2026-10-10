# AI生成
import os, glob, zipfile

ROOT = r"D:\rim mod\FirstPerson3D"
OUT = os.path.join(ROOT, "docs", "wreckage_delivery_v2.zip")

items = []
items += [os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageSamples.cs")]
items += [os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageUVGen.cs")]
items += [os.path.join(ROOT, "1.6", "Textures", "FPS3D", "WreckageAtlas.png")]
items += sorted(glob.glob(os.path.join(ROOT, "docs", "wreckage", "samples", "*.png")))
items += sorted(glob.glob(os.path.join(ROOT, "docs", "wreckage", "clean", "*.png")))
items += [os.path.join(ROOT, "docs", "wreckage", "clean", "clean_report.json")]
items += [os.path.join(ROOT, "docs", "wreckage", "README.md")]
items += [os.path.join(ROOT, "scripts", "wreck_atlas.py")]
items += [os.path.join(ROOT, "scripts", "wreck_render.py")]
items += [os.path.join(ROOT, "scripts", "wreck_blind.py")]

missing = [p for p in items if not os.path.exists(p)]
if missing:
    print("MISSING:", missing)

n = 0
with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for p in items:
        if os.path.exists(p):
            arc = os.path.relpath(p, ROOT).replace("\\", "/")
            z.write(p, arc)
            n += 1

print("WROTE", OUT)
print("FILES", n)
print("SIZE", os.path.getsize(OUT))