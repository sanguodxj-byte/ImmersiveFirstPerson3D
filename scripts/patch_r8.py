# -*- coding: utf-8 -*-
# r8: 放弃自建相机，接管原版相机为透视第一人称
import io

def rd(p): return io.open(p, encoding='utf-8').read()
def wr(p, s): io.open(p, 'w', encoding='utf-8', newline='\n').write(s)

base = r'D:\rim mod\FirstPerson3D\Source\RimFirstPerson3D'

# 1) VanillaSuppressor: 不再停用相机，改为透视化配置 + 恢复
s = rd(base + r'\VanillaSuppressor.cs')
old_start = s.index("        private static GameObject vanillaCamGo;")
old_end = s.index("        // ---- Harmony 前缀 ----")
new_block = """        private static GameObject vanillaCamGo;
        private static bool camConverted;
        private static float origNear, origFar, origOrthoSize, origDepth;
        private static bool origOrtho;

        public static void ApplyCameraSuppression()
        {
            var camGo = GameObject.Find("Camera");
            if (camGo == null)
            {
                Log.Warning(ModEntry.LogPrefix + "vanilla 'Camera' object not found");
                return;
            }
            vanillaCamGo = camGo;
            if (mapCamera == null)
            {
                mapCamera = camGo.GetComponent<Camera>();
                colorCurves = camGo.GetComponent("ColorCorrectionCurves") as Behaviour;
            }
            if (mapCamera != null && !camConverted)
            {
                camConverted = true;
                origOrtho = mapCamera.orthographic;
                origOrthoSize = mapCamera.orthographicSize;
                origNear = mapCamera.nearClipPlane;
                origFar = mapCamera.farClipPlane;
                origDepth = mapCamera.depth;
                mapCamera.orthographic = false;
                mapCamera.fieldOfView = Fps3DState.FovDeg;
                mapCamera.nearClipPlane = Fps3DState.NearClip;
                mapCamera.farClipPlane = Fps3DState.FarClip;
                mapCamera.backgroundColor = new Color(0.09f, 0.11f, 0.16f);
                if (colorCurves != null) colorCurves.enabled = false;
            }
            // Subcameras 保持原样：水印/光照子相机挂在原版相机管线上，停用会破坏全局。
        }

        public static void RestoreCameras()
        {
            if (mapCamera != null && camConverted)
            {
                camConverted = false;
                mapCamera.orthographic = origOrtho;
                mapCamera.orthographicSize = origOrthoSize;
                mapCamera.nearClipPlane = origNear;
                mapCamera.farClipPlane = origFar;
                mapCamera.depth = origDepth;
            }
            if (colorCurves != null) colorCurves.enabled = true;
        }

"""
s = s[:old_start] + new_block + s[old_end:]
wr(base + r'\VanillaSuppressor.cs', s)
print("suppressor ok")

# 2) CameraRig3D: 驱动原版相机而非自建
c = rd(base + r'\CameraRig3D.cs')
old = """        public static void EnsureCreated()
        {
            if (camGO != null) return;
            camGO = new GameObject("FirstPerson3D_Camera");
            Object.DontDestroyOnLoad(camGO);
            cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = Fps3DState.FovDeg;
            cam.nearClipPlane = Fps3DState.NearClip;
            cam.farClipPlane = Fps3DState.FarClip;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.11f, 0.16f);
            cam.cullingMask = 1 << 0;
            cam.depth = 10f;
            cam.enabled = false;
            camGO.AddComponent<AudioListener>();
            camGO.AddComponent<Fps3DCamProbe>();
            try { camGO.tag = "MainCamera"; } catch { }
            // 真实立方体探针：MeshRenderer 路径
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Fps3D_ProbeCube";
            cube.transform.SetParent(camGO.transform, false);
            cube.transform.localPosition = new Vector3(0f, -0.25f, 0.6f);
            cube.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
            var cm = cube.GetComponent<MeshRenderer>();
            cm.sharedMaterial = Util3D.CutoutMatFor(Util3D.WhiteTex);
            Object.DontDestroyOnLoad(cube);
        }"""
new = """        public static void EnsureCreated()
        {
            if (camGO != null) return;
            var camGo = GameObject.Find("Camera");
            if (camGo == null) return;
            camGO = camGo;
            cam = camGo.GetComponent<Camera>();
            DontMoveVanilla = false;
        }

        public static bool DontMoveVanilla;"""
assert old in c, "EnsureCreate old not found"
c = c.replace(old, new)

old = """        public static void SetEnabled(bool on)
        {
            EnsureCreated();
            if (camGO.GetComponent<AudioListener>() != null) camGO.GetComponent<AudioListener>().enabled = on;
            cam.enabled = on;
        }"""
new = """        public static void SetEnabled(bool on)
        {
            // 原版相机始终由游戏驱动开关；此处仅同步状态。
        }"""
assert old in c, "SetEnabled old not found"
c = c.replace(old, new)
wr(base + r'\CameraRig3D.cs', c)
print("camera ok")

# 3) SyncFollow 不再搬动原版相机位置（它就是驱动本体）
c = rd(base + r'\CameraRig3D.cs')
old = """                VanillaSuppressor.SyncVanillaViewCenter(new Vector3(eye.x, 0f, eye.z));
                var vanillaCamGo = GameObject.Find("Camera");
                if (vanillaCamGo != null) vanillaCamGo.transform.position = eye;"""
new = """                // 相机本体即原版相机，无需搬运 AudioListener。"""
assert old in c
c = c.replace(old, new)
wr(base + r'\CameraRig3D.cs', c)
print("sync ok")

# 4) 删除探针文件（GL/立方体已完成使命）
import os
os.remove(base + r'\Fps3DCamProbe.cs')
print("probe removed")
