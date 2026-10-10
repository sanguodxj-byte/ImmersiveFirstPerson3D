# AI生成  -- 打包本轮「带纹理材质验收」产物
import os, zipfile

ROOT = r"D:\rim mod\FirstPerson3D"
OUT = os.path.join(ROOT, "docs", "wreckage_textured_v1.zip")

items = []
shots = os.path.join(ROOT, "viewer", "shots")
if os.path.isdir(shots):
    for n in sorted(os.listdir(shots)):
        if n.lower().endswith(".png"):
            items.append(os.path.join(shots, n))

items += [
    os.path.join(ROOT, "docs", "wreckage", "README.md"),
    os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageSamples.cs"),
    os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageUVGen.cs"),
    os.path.join(ROOT, "scripts", "wreck_viewer_json.py"),
    os.path.join(ROOT, "scripts", "wreck_shots.py"),
    os.path.join(ROOT, "scripts", "wreck_overview.py"),
]

with zipfile.ZipFile(OUT, "w", zipfile.ZIP_DEFLATED) as z:
    for p in items:
        if os.path.isfile(p):
            arc = os.path.relpath(p, ROOT).replace("\\", "/")
            z.write(p, arc)
        else:
            print("MISS", p)

sz = os.path.getsize(OUT)
cnt = len(zipfile.ZipFile(OUT).namelist())
print("ZIP", OUT, cnt, "files", sz, "bytes")