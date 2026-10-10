# -*- coding: utf-8 -*-
# r10: 相机 clearFlags 修复（SolidColor 才会用背景色）
import io

def rd(p): return io.open(p, encoding='utf-8').read()
def wr(p, s): io.open(p, 'w', encoding='utf-8', newline='\n').write(s)

base = r'D:\rim mod\FirstPerson3D\Source\RimFirstPerson3D'

v = rd(base + r'\VanillaSuppressor.cs')
old = """        private static GameObject vanillaCamGo;
        private static bool camConverted;
        private static float origNear, origFar, origOrthoSize, origDepth;
        private static bool origOrtho;"""
new = """        private static GameObject vanillaCamGo;
        private static bool camConverted;
        private static float origNear, origFar, origOrthoSize, origDepth;
        private static bool origOrtho;
        private static CameraClearFlags origClearFlags;"""
assert old in v, "fields"
v = v.replace(old, new)

old = """                origOrtho = mapCamera.orthographic;
                origOrthoSize = mapCamera.orthographicSize;
                origNear = mapCamera.nearClipPlane;
                origFar = mapCamera.farClipPlane;
                origDepth = mapCamera.depth;
                mapCamera.orthographic = false;"""
new = """                origOrtho = mapCamera.orthographic;
                origOrthoSize = mapCamera.orthographicSize;
                origNear = mapCamera.nearClipPlane;
                origFar = mapCamera.farClipPlane;
                origDepth = mapCamera.depth;
                origClearFlags = mapCamera.clearFlags;
                mapCamera.orthographic = false;
                // Depth-only 清屏不使用背景色，天空会显示成残留色；必须 SolidColor。
                mapCamera.clearFlags = CameraClearFlags.SolidColor;"""
assert old in v, "convert block"
v = v.replace(old, new)

old = """                camConverted = false;
                mapCamera.orthographic = origOrtho;
                mapCamera.orthographicSize = origOrthoSize;
                mapCamera.nearClipPlane = origNear;
                mapCamera.farClipPlane = origFar;
                mapCamera.depth = origDepth;"""
new = """                camConverted = false;
                mapCamera.orthographic = origOrtho;
                mapCamera.orthographicSize = origOrthoSize;
                mapCamera.nearClipPlane = origNear;
                mapCamera.farClipPlane = origFar;
                mapCamera.depth = origDepth;
                mapCamera.clearFlags = origClearFlags;"""
assert old in v, "restore block"
v = v.replace(old, new)
wr(base + r'\VanillaSuppressor.cs', v)
print("suppressor ok")

e = rd(base + r'\ModEntry.cs')
e = e.replace('initialized (r9)', 'initialized (r10)')
wr(base + r'\ModEntry.cs', e)
print("entry ok")
