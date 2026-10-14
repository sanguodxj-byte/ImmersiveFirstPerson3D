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
    [StaticConstructorOnStartup]
    public static class GodView3D
    {
        public const int GodLayer = 31;
        public static int GodMask => 1 << GodLayer;

        // 与终端内口长宽比 (0.29 / 0.205 = 1.4146) 一致。RtW/RtH 只是回退尺寸：
        // 真正开多大由 EnsureTargetFor 按屏幕面在画面上的实际像素数决定。
        public const int RtW = 1360;
        public const int RtH = 960;

        // 原版界面是烘进这块 RT 再贴到终端屏上的。屏幕面抬起后固定在相机空间，
        // 所以它在画面里占多少像素只跟显示器分辨率有关：4K（3840×2160）下终端占画面高约 91%，
        // 那一块屏就是约 2790×1970 物理像素。若 RT 仍按 1360×960 开，等于把原版文字先按
        // 半分辨率位图化再放大两倍贴上去——字必然发虚。按实际像素开，1:1 贴，字才锐。
        public const int RtMinH = 720;
        public const int RtMaxH = 2880;

        static Camera godCam;
        static RenderTexture rt;
        static int rtW = RtW;
        static int rtH = RtH;
        static readonly Vector3[] cornerBuf = new Vector3[4];
        static int loggedTargetW = -1;
        static int loggedTargetH = -1;
        static bool redirecting;
        static bool patched;
        static bool loggedCaptureFail;
        static bool loggedTiltFallback;
        static Vector3 posedFocus;
        static float posedSize = CameraDriver.StartingSize;
        static float zoomOverride = -1f;

        public static bool Capturing { get; private set; }
        public static Camera Camera => godCam;
        public static RenderTexture RenderTarget { get { EnsureCamera(); return rt; } }
        public static Texture ScreenTexture => rt;
        public static float OrthoSize => posedSize;
        public static float Aspect =>
            rt != null && rt.height > 0 ? rt.width / (float)rt.height : (float)RtW / RtH;

        public static void AdjustZoom(float steps)
        {
            float cur = ResolveRootSize();
            float min = 11f, max = 60f;
            float speed = 2.6f;
            var drv = Find.CameraDriver;
            if (drv != null && drv.config != null)
            {
                min = drv.config.sizeRange.min;
                max = drv.config.sizeRange.max;
                speed = drv.config.zoomSpeed;
            }
            cur -= steps * speed * cur / 35f;
            zoomOverride = Mathf.Clamp(cur, min, max);
            posedSize = zoomOverride;
        }

        public static void SetOrthoSize(float size)
        {
            float min = 11f, max = 60f;
            var drv = Find.CameraDriver;
            if (drv != null && drv.config != null)
            {
                min = drv.config.sizeRange.min;
                max = drv.config.sizeRange.max;
            }
            zoomOverride = Mathf.Clamp(size, min, max);
            posedSize = zoomOverride;
        }

        public static void Patch(Harmony harmony)
        {
            if (patched) return;
            patched = true;

            var getter = AccessTools.PropertyGetter(typeof(CameraDriver), nameof(CameraDriver.CurrentViewRect));
            harmony.Patch(getter, postfix: new HarmonyMethod(typeof(GodView3D), nameof(CurrentViewRectPostfix)));
            harmony.Patch(AccessTools.Method(typeof(DesignatorManager), nameof(DesignatorManager.DesignatorManagerUpdate)),
                prefix: new HarmonyMethod(typeof(GodView3D), nameof(DesignatorUpdateWhileViewing)));

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
            // 用 rt 的实际长宽比，跟随 EnsureTargetFor 定出来的分辨率走，否则地图会被拉伸。
            godCam.aspect = Aspect;
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
                // 放置幽灵和拖选高亮只在捕获窗内画，才会进终端。平时那次提交打在第一人称相机上。
                try
                {
                    if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
                        Find.DesignatorManager.DesignatorManagerUpdate();
                }
                catch (Exception ghostEx)
                {
                    if (!loggedCaptureFail)
                    {
                        loggedCaptureFail = true;
                        Log.Warning(ModEntry.LogPrefix + "god view designator draw failed: "
                            + ghostEx.GetType().Name + ": " + ghostEx.Message);
                    }
                }
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

        static void CreateTarget(int w, int h)
        {
            rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32)
            {
                name = "Fps3D_GodView",
                // 双线性：目标尺寸按屏幕面像素数开，本来就接近 1:1；
                // 抬升动画 / 轻微侧视带来的一点非整数缩放由它抹平，比 Point 更抗锯齿。
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1
            };
            rt.Create();
            rtW = w;
            rtH = h;
            if (godCam != null) godCam.targetTexture = rt;
        }

        // 按屏幕面在画面上的实际像素数定 RT 分辨率。屏幕面抬起后固定在相机空间，
        // 所以量一次即可，全程不变（分辨率/窗口尺寸变了会重新量）。
        public static void EnsureTargetFor(Camera cam)
        {
            if (cam == null) return;
            float pw, ph;
            if (!TryMeasureScreenPixels(cam, out pw, out ph)) return;
            int wantH = Mathf.Clamp(Mathf.RoundToInt(ph), RtMinH, RtMaxH);
            int wantW = Mathf.Max(16, Mathf.RoundToInt(wantH * (pw / ph)));
            if (rt != null && wantH == rtH) return;

            if (rt != null)
            {
                if (godCam != null) godCam.targetTexture = null;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
            CreateTarget(wantW, wantH);

            if (wantW != loggedTargetW || wantH != loggedTargetH)
            {
                loggedTargetW = wantW;
                loggedTargetH = wantH;
                Log.Message(ModEntry.LogPrefix + "god view target " + wantW + "x" + wantH
                    + " (terminal screen " + Mathf.RoundToInt(pw) + "x" + Mathf.RoundToInt(ph) + " px)");
            }
        }

        // 屏幕面四角投影到屏幕像素，取外包。拿不到几何时返回 false，保持现有 RT 不动。
        static bool TryMeasureScreenPixels(Camera cam, out float pxW, out float pxH)
        {
            pxW = 0f;
            pxH = 0f;
            if (!HandTerminal3D.TryScreenCornersWorld(cam, cornerBuf)) return false;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(cornerBuf[i]);
                if (sp.z <= 0f) return false;
                if (sp.x < minX) minX = sp.x;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.y > maxY) maxY = sp.y;
            }
            pxW = maxX - minX;
            pxH = maxY - minY;
            return pxW > 8f && pxH > 8f;
        }

        static void EnsureCamera()
        {
            if (rt == null) CreateTarget(rtW, rtH);
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
            if (zoomOverride > 1f) return zoomOverride;
            var drv = Find.CameraDriver;
            float size = drv != null ? drv.RootSize : 0f;
            if (size < 1f) size = CameraDriver.StartingSize;
            zoomOverride = size;
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

        // 查看期平时跳过，改在捕获里画一次。否则幽灵每帧画两遍，还会打进主相机。
        static bool DesignatorUpdateWhileViewing()
        {
            if (!MapScreen3D.Viewing) return true;
            return Capturing;
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
