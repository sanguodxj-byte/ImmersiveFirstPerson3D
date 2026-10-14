# AI生成
# -*- coding: utf-8 -*-
# AI生成
# 从 C# 真源生成 viewer/wreck_uv.json：
#   - 几何：解析 Source/RimFirstPerson3D/WreckageSamples.cs 的 m.Add(...) 列表
#   - UV  ：解析 Source/RimFirstPerson3D/WreckageUVGen.cs 的 R / SrcSize
# 单一口径：几何与运行时同源；UV 取样区与运行时 Uv() 同源。
# 说明：多视图残骸（卡车）在 R 里只有 "<名>.<朝向>" 键，无裸键；运行时 Uv() 会回退。
#       为便于视觉验证，本脚本对多视图残骸取 south 视图作为主区域（HUD 会标注 regionKey）。
import re, json, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC_SAMPLES = os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageSamples.cs")
SRC_UVGEN = os.path.join(ROOT, "Source", "RimFirstPerson3D", "WreckageUVGen.cs")
OUT = os.path.join(ROOT, "viewer", "wreck_uv.json")

def read(p):
    with open(p, "r", encoding="utf-8") as f:
        return f.read()

def f3(s):
    return [float(x) for x in re.findall(r"-?\d+\.?\d*", s)]

# ---------- 解析 WreckageSamples.cs ----------
def parse_samples(text):
    samples = {}
    # 按方法切段
    for mobj in re.finditer(r"public static Model3D (\w+)\(\)\s*\{(.*?)\n        \}", text, re.S):
        mname = mobj.group(1)
        body = mobj.group(2)
        name = mname[len("Build"):].lower() if mname.startswith("Build") else mname.lower()
        parts = []
        for a in re.finditer(
            r"m\.Add\(Prim\.(\w+),\s*new Vector3\(([^)]+)\),\s*new Vector3\(([^)]+)\)"
            r"(?:,\s*(-?\d+\.?\d*)f)?(?:,\s*(-?\d+\.?\d*)f)?(?:,\s*(-?\d+\.?\d*)f)?\)", body):
            parts.append({
                "kind": a.group(1),
                "pos": f3(a.group(2)),
                "size": f3(a.group(3)),
                "yaw": float(a.group(4)) if a.group(4) else 0.0,
                "pitch": float(a.group(5)) if a.group(5) else 0.0,
                "roll": float(a.group(6)) if a.group(6) else 0.0,
            })
        bounds = None
        bo = re.search(r"m\.bounds\s*=\s*new Vector3\(([^)]+)\)", body)
        if bo:
            bounds = f3(bo.group(1))
        samples[name] = {"parts": parts, "boundsOverride": bounds}
    return samples

# ---------- 解析 WreckageUVGen.cs ----------
def parse_uvgen(text):
    R, S = {}, {}
    for a in re.finditer(r'\{\s*"([\w.]+)"\s*,\s*new Vector4\(([^)]+)\)\s*\}', text):
        R[a.group(1)] = f3(a.group(2))
    for a in re.finditer(r'\{\s*"([\w.]+)"\s*,\s*new Vector2\(([^)]+)\)\s*\}', text):
        S[a.group(1)] = f3(a.group(2))
    return R, S

def recalc_bounds(parts):
    mx = mz = my = 0.0
    for p in parts:
        mx = max(mx, abs(p["pos"][0]) + p["size"][0] * 0.5)
        mz = max(mz, abs(p["pos"][2]) + p["size"][2] * 0.5)
        my = max(my, p["pos"][1] + p["size"][1])
    return [mx * 2.0, my, mz * 2.0]

def main():
    samples = parse_samples(read(SRC_SAMPLES))
    R, S = parse_uvgen(read(SRC_UVGEN))

    out_samples = {}
    for name, d in samples.items():
        parts = d["parts"]
        b = d["boundsOverride"] or recalc_bounds(parts)
        # 区域键：有裸键用裸键；否则退 south；再否则 ruinedtank
        if name in R:
            key = name
        elif (name + ".south") in R:
            key = name + ".south"
        else:
            key = "ruinedtank"
        sz = R[key]
        side = [sz[0],
                round(sz[1] + (sz[3] - sz[1]) * 0.45, 6),
                sz[2],
                round(sz[1] + (sz[3] - sz[1]) * 0.62, 6)]
        out_samples[name] = {
            "parts": parts,
            "bounds": b,
            "modelXZ": [b[0], b[2]],
            "top": sz,
            "side": side,
            "regionKey": key,
            "srcSize": S.get(key, [1, 1]),
        }

    # 图集尺寸
    atlas = os.path.join(ROOT, "Textures", "FPS3D", "WreckageAtlas.png")
    from PIL import Image
    W, H = Image.open(atlas).size

    out = {
        "size": [W, H],
        "tex": "./wreck_atlas.png",
        "samples": out_samples,
        "R": R,
        "SrcSize": S,
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("wrote", OUT, "atlas", W, "x", H, "samples", list(out_samples.keys()))
    for k, v in out_samples.items():
        print("  ", k, "regionKey=", v["regionKey"], "bounds=", v["bounds"], "parts=", len(v["parts"]))

if __name__ == "__main__":
    main()