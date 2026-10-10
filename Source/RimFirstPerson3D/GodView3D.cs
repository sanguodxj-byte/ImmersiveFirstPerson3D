// AI生成
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 手持终端上的原版俯视。主相机保持第一人称透视。
    // 原版地图走 Graphics.DrawMesh，提交进本帧所有相机会看到的队列，不会被第一台相机吃掉。
    // 捕获窗内把这些提交改到 layer 31，并尽量绑到下面这台停用相机；
    // 该相机只留这一层，用 Camera.Render 写进屏幕 RT。主相机每帧去掉这一层。
    public static class GodView3D
    {
        public const int GodLayer = 31;
        public static int GodMask => 1 << GodLayer;

        // 3 倍于最初的 512×362，长宽比不变。指挥面板要把整屏原版 UI 收进这块纹理，
        // 512 宽时汉字只剩几个纹素，看不清指令。
        const int RtW = 1536;
        const int RtH = 1086;

        static Camera godCam;
        static RenderTexture rt;
        static bool redirecting;
        static bool patched;
        static bool loggedCaptureFail;
        static bool loggedTiltFallback;
        static Vector3 posedFocus;
        static float posedSize = CameraDriver.StartingSize;

        public static bool Capturing { get; private set; }
        public static Camera Camera => godCam;
        public static Texture ScreenTexture => rt;
        public static float OrthoSize => posedSize;
        public static float Aspect =>
            rt != null && rt.height > 0 ? rt.width / (float)rt.height : (float)RtW / RtH;

        public static void Patch(Harmony harmony)
        {
            if (patched) return;
            patched = true;

            var getter = AccessTools.PropertyGetter(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect));
            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(GodView3D), nameof(CurrentViewRectPostfix)));

            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Graphics)))
            {
                if (method.Name != "DrawMesh" && method.Name != "DrawMeshInstanced") continue;
                try
                {
                    HarmonyMethod prefix = PrefixFor(method);
                    if (prefix == null) continue;
                    harmony.Patch(method, prefix: prefix);
                }
                catch (Exception e)
                {
                    Log.Warning(ModEntry.LogPrefix + "god view patch skipped " + method + ": " + e.Message);
                }
            }
        }

        // 摆好正交相机，不绘制。点选要用和上一帧画面一致的位姿，所以查看期先 Pose 再读指针。
        public static void Pose(Vector3 focus)
        {
            EnsureCamera();
            if (godCam == null) return;

            float size = ResolveRootSize();
            posedSize = size;
            posedFocus = focus;
            godCam.orthographic = true;
            godCam.orthographicSize = size;
            godCam.nearClipPlane = 0.5f;
            godCam.farClipPlane = 180f;
            godCam.cullingMask = GodMask;
            godCam.targetTexture = rt;
            godCam.clearFlags = CameraClearFlags.SolidColor;
            godCam.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
            godCam.enabled = false;
            godCam.aspect = (float)RtW / RtH;
            godCam.transform.rotation = ResolveTilt();
            godCam.transform.position = new Vector3(focus.x, Altitude(size), focus.z);
        }

        // 只画地图网格和动态物。天气、fleck、指示层不进屏幕。
        public static void Capture(Map map, Vector3 focus)
        {
            if (map == null || map.Disposed || map.mapDrawer == null || map.dynamicDrawManager == null)
                return;
            Pose(focus);
            if (godCam == null) return;

            Capturing = true;
            try
            {
                map.mapDrawer.DrawMapMesh();
                map.dynamicDrawManager.DrawDynamicThings();
                godCam.Render();
                godCam.enabled = false;
            }
            catch (Exception e)
            {
                if (!loggedCaptureFail)
                {
                    loggedCaptureFail = true;
                    Log.Warning(ModEntry.LogPrefix + "god view capture failed: "
                        + e.GetType().Name + ": " + e.Message);
                }
            }
            finally
            {
                Capturing = false;
            }
        }

        static void EnsureCamera()
        {
            if (rt == null)
            {
                rt = new RenderTexture(RtW, RtH, 16, RenderTextureFormat.ARGB32)
                {
                    name = "Fps3D_GodView",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                    antiAliasing = 1
                };
                rt.Create();
            }
            if (godCam != null) return;

            var go = new GameObject("Fps3D_GodView");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            godCam = go.AddComponent<Camera>();
            godCam.enabled = false;
            godCam.allowHDR = false;
            godCam.allowMSAA = false;
            godCam.useOcclusionCulling = false;
            godCam.targetTexture = rt;
        }

        static float ResolveRootSize()
        {
            var drv = Find.CameraDriver;
            float size = drv != null ? drv.RootSize : 0f;
            if (size < 1f) size = CameraDriver.StartingSize;
            return size;
        }

        // CameraDriver.ApplyPositionToGameObject 的高度。不写 RootSize，避免改掉存着的原版缩放。
        static float Altitude(float size)
        {
            float min = 11f;
            float max = 60f;
            var drv = Find.CameraDriver;
            if (drv != null && drv.config != null)
            {
                min = drv.config.sizeRange.min;
                max = drv.config.sizeRange.max;
            }
            float span = Mathf.Max(0.01f, max - min);
            float t = Mathf.Clamp01((size - min) / span);
            return 15f + t * 50f;
        }

        static Quaternion ResolveTilt()
        {
            if (VanillaSuppressor.VanillaTiltCaptured)
                return VanillaSuppressor.VanillaMapRotation;
            if (!loggedTiltFallback)
            {
                loggedTiltFallback = true;
                Log.Warning(ModEntry.LogPrefix + "god view tilt not snapshotted, using straight down");
            }
            return Quaternion.Euler(90f, 0f, 0f);
        }

        // 视锥剔除读的是主相机位置。捕获窗内换成这台正交相机的焦点，公式与 CameraDriver.CurrentViewRect 相同。
        static void CurrentViewRectPostfix(ref CellRect __result)
        {
            if (!Capturing) return;
            float aspect = Aspect;
            float size = posedSize;
            var rect = new CellRect();
            rect.minX = Mathf.FloorToInt(posedFocus.x - size * aspect - 1f);
            rect.maxX = Mathf.CeilToInt(posedFocus.x + size * aspect);
            rect.minZ = Mathf.FloorToInt(posedFocus.z - size - 1f);
            rect.maxZ = Mathf.CeilToInt(posedFocus.z + size);
            __result = rect;
        }

        static HarmonyMethod PrefixFor(MethodInfo method)
        {
            ParameterInfo[] ps = method.GetParameters();
            bool hasLayer = false;
            bool hasCam = false;
            for (int i = 0; i < ps.Length; i++)
            {
                if (ps[i].Name == "layer" && ps[i].ParameterType == typeof(int)) hasLayer = true;
                if (ps[i].Name == "camera" && ps[i].ParameterType == typeof(Camera)) hasCam = true;
            }

            // 短重载没有相机参数，原样执行会进所有相机。捕获窗内改调带相机的重载。
            if (method.Name == "DrawMesh" && !hasCam && ps.Length == 5
                && ps[1].ParameterType == typeof(Vector3))
                return new HarmonyMethod(typeof(GodView3D), nameof(RedirectPosLayer));
            if (method.Name == "DrawMesh" && !hasCam && ps.Length == 4
                && ps[1].ParameterType == typeof(Matrix4x4))
                return new HarmonyMethod(typeof(GodView3D), nameof(RedirectMatrixLayer));
            if (!hasLayer || !hasCam) return null;
            return new HarmonyMethod(typeof(GodView3D), nameof(RetargetLayerAndCamera));
        }

        static void RetargetLayerAndCamera(ref int layer, ref Camera camera)
        {
            if (!Capturing || godCam == null) return;
            layer = GodLayer;
            camera = godCam;
        }

        static bool RedirectPosLayer(Mesh mesh, Vector3 position, Quaternion rotation, Material material, int layer)
        {
            if (!Capturing || godCam == null || redirecting) return true;
            redirecting = true;
            try
            {
                Graphics.DrawMesh(mesh, position, rotation, material, GodLayer, godCam);
            }
            finally
            {
                redirecting = false;
            }
            return false;
        }

        static bool RedirectMatrixLayer(Mesh mesh, Matrix4x4 matrix, Material material, int layer)
        {
            if (!Capturing || godCam == null || redirecting) return true;
            redirecting = true;
            try
            {
                Graphics.DrawMesh(mesh, matrix, material, GodLayer, godCam);
            }
            finally
            {
                redirecting = false;
            }
            return false;
        }
    }
}
