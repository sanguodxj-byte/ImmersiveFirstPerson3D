"""Static UV/configuration gate for production-bench furniture additions.

This deliberately does not regenerate outputs.  ``furn_atlas.py`` remains the
single generator for FurnitureAtlas.png and FurnitureUVGen.cs.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = os.path.join(ROOT, "scripts", "furn_atlas.py")
CS = os.path.join(ROOT, "Source", "RimFirstPerson3D", "Furniture3D.cs")
VIEWER = os.path.join(ROOT, "viewer", "furniture.html")
TEX = os.path.join(r"D:\rim mod\RimWorld_Vanilla_Textures_1.6",
                   "Core", "textures", "things", "building", "production")
ITEMS = {
    "tablebutcher": "tablebutcher",
    "tablesculpting": "tablesculpting",
    "tablesmithingfueled": "tablesmithingfueled",
    "tablesmithingelectric": "tablesmithingelectric",
}
VIEWER_ITEMS = {
    "tablebutcher": "tablebutcher",
    "tablesculpting": "tablesculpting",
    "tablesmithingfueled": "fueledsmithy",
    "tablesmithingelectric": "electricsmithy",
}
EXPECTED = {
    "south": "(16, 16, 208, 80)",
    "north": "(16, 16, 208, 80)",
    "east": "(16, 16, 80, 208)",
}


def main():
    text = open(SPEC, encoding="utf-8").read()
    cs = open(CS, encoding="utf-8").read()
    viewer = open(VIEWER, encoding="utf-8").read()
    errors = []
    for item, stem in ITEMS.items():
        if ('"%s"' % item) not in text:
            errors.append("missing SPEC: " + item)
        for view in ("south", "east", "north"):
            path = os.path.join(TEX, stem + "_" + view + ".png")
            if not os.path.isfile(path):
                errors.append("missing source: " + path)
            if ("%s.%s.top" % (item, view)) not in cs:
                errors.append("missing AllRegions: %s.%s" % (item, view))
            expected = EXPECTED[view]
            if '"top": ' + expected not in text:
                errors.append("top aspect contract changed: " + item + "." + view)
            if '"front": ' + ("(16, 80, 208, 89)" if view != "east" else "(16, 208, 80, 217)") not in text:
                errors.append("front aspect contract changed: " + item + "." + view)
    if not re.search(r"r\.arch == Arch\.ProductionBench", cs):
        errors.append("production matching is not exact")
    if "ProductionBenchLong = 3.00f" not in cs:
        errors.append("production geometry is not 3m long")
    if re.search(r'"(?:tablebutcher|tablesculpting|tablesmithingfueled|tablesmithingelectric)\.(?:south|north|east)\.(?:device|wood)"', cs):
        errors.append("production SPEC still contains an uncalibrated device/wood patch")
    if "function productionUv" not in viewer:
        errors.append("viewer does not use productionUv")
    for item, viewer_item in VIEWER_ITEMS.items():
        if not re.search(r"%s:\s*\{arch:'productionbench', item:'%s', w:3, d:1\}" % (viewer_item, item), viewer):
            errors.append("viewer size contract is not 3x1: " + item)
    if "const deviceH" in viewer or "'device','device'" in viewer:
        errors.append("viewer still has the old forced device patch")
    if errors:
        for error in errors:
            print("ERROR", error)
        return 1
    print("OK: four exact production defs, three source views each, safe fallback gate")
    return 0


if __name__ == "__main__":
    sys.exit(main())
