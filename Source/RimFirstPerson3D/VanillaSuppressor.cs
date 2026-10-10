// AI生成
using System.Collections.Generic;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 屏蔽原版平铺绘制并停用原版地图相机；恢复时原样还原。
    public static class VanillaSuppressor
    {
        private static bool applied;
        private static Camera mapCamera;
        private static Behaviour colorCurves;

        // 第一人称期间主画面始终跳过原版绘制。终端俯视只在 GodView3D.Capturing 那一小段放行。
        public static bool Suppress => Fps3DState.Active;

        public static void Patch(Harmony harmony)
        {
            if (applied) return;
            applied = true;

            // 静态层：地形/雪/区域/电力等 section 网格。
            harmony.Patch(AccessTools.Method(typeof(MapDrawer), nameof(MapDrawer.DrawMapMesh)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipDrawMapMesh)));
            // 动态层：pawn/物品/建筑动态绘制。
            harmony.Patch(AccessTools.Method(typeof(DynamicDrawManager), nameof(DynamicDrawManager.DrawDynamicThings)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipDynamicThings)));
            // Odyssey's gravship terrain curtain is a separate world-component draw path.
            // It can persist across map-camera mode changes as a large opaque rectangle.
            harmony.Patch(AccessTools.Method(typeof(WorldComponent_GravshipController), "DrawTerrainCurtain"),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipGravshipTerrainCurtain)));
            // DrawBatch：fleck/overlay 等批处理提交（所有同名 Flush 重载一并跳过）。
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(DrawBatch)))
            {
                if (m.Name == "Flush")
                    harmony.Patch(m, prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipFlush)));
            }
            harmony.Patch(AccessTools.Method(typeof(Widgets), "DrawBoxSolid", new[] { typeof(Rect), typeof(Color) }),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SuppressRimTalkBackdrop)));
            // 原版相机输入与更新。
            harmony.Patch(AccessTools.Method(typeof(CameraDriver), "Update"),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipCameraUpdate)));
            harmony.Patch(AccessTools.Method(typeof(CameraDriver), nameof(CameraDriver.CameraDriverOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipCameraUpdate)));
            // 第一人称接管期：原版整套 UI（窗口栈/主按钮/地图点击/设计器/快捷键）
            // 暂停处理，输入与绘制一并跳过，由模组全权消费，不穿透到原界面。
            harmony.Patch(AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipVanillaUIOnGUI)));
            // 原版 ThingOverlays 名牌/量表按俯视相机投影（锚在脚底+z 北偏），
            // 3D 模式全部悬空错位，一并屏蔽；名牌由 NameLabels3D 按头顶重画。
            harmony.Patch(AccessTools.Method(typeof(ThingOverlays), nameof(ThingOverlays.ThingOverlaysOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipThingOverlays)));
            // WeatherManager.DrawAllWeather also draws live weather events before worker overlays.
            // Skip the complete visual entry point in 3D mode while keeping WeatherTick simulation.
            harmony.Patch(AccessTools.Method(typeof(WeatherManager), nameof(WeatherManager.DrawAllWeather)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipWeatherDraw)));
        }

        // ---- 抑制开关：进入/退出 3D 模式时切换相机可用性 ----

        private static GameObject vanillaCamGo;
        private static bool suppressionActive;
        private static Quaternion vanillaMapRotation = Quaternion.identity;
        private static bool vanillaTiltCaptured;

        // 第一次接管、第一人称欧拉写上之前的场景倾斜。上帝相机只用这份，不读已经被改过的主相机。
        public static bool VanillaTiltCaptured => vanillaTiltCaptured;
        public static Quaternion VanillaMapRotation => vanillaMapRotation;
        private static CameraState mainCameraState;
        private static bool colorCurvesWasEnabled;
        private static bool colorCurvesStateCaptured;
        private static readonly Dictionary<Camera, CameraState> extraCamStates =
            new Dictionary<Camera, CameraState>();
        private static readonly Dictionary<Camera, CameraState> deferredDataCameraStates =
            new Dictionary<Camera, CameraState>();
        private static readonly Dictionary<Behaviour, bool> externalEffectStates =
            new Dictionary<Behaviour, bool>();

        private sealed class CameraState
        {
            public bool enabled;
            public RenderTexture targetTexture;
            public float depth;
            public CameraClearFlags clearFlags;
            public Color backgroundColor;
            public Rect rect;
            public bool orthographic;
            public float orthographicSize;
            public float fieldOfView;
            public float nearClipPlane;
            public float farClipPlane;
            public RenderingPath renderingPath;
            public int cullingMask;
            public bool allowHDR;
            public bool useOcclusionCulling;
            public bool wasDataCamera;
        }

        public static void ApplyCameraSuppression()
        {
            var camGo = GameObject.Find("Camera");
            if (camGo == null)
            {
                Log.Warning(ModEntry.LogPrefix + "vanilla 'Camera' object not found");
                return;
            }
            vanillaCamGo = camGo;
            var currentMain = camGo.GetComponent<Camera>();
            if (mapCamera != currentMain)
            {
                if (suppressionActive)
                    RestoreCameras();
                mapCamera = currentMain;
                colorCurves = camGo.GetComponent("ColorCorrectionCurves") as Behaviour;
            }
            else if (mapCamera == null)
            {
                mapCamera = currentMain;
                colorCurves = camGo.GetComponent("ColorCorrectionCurves") as Behaviour;
            }
            if (mapCamera == null) return;

            if (!suppressionActive)
            {
                if (!vanillaTiltCaptured)
                {
                    vanillaMapRotation = mapCamera.transform.rotation;
                    vanillaTiltCaptured = true;
                }
                // 在主相机仍处于原版状态时刷新一次副相机 RT，然后拍摄完整快照。
                // 接管期间不再调用 UpdatePositions，避免透视参数反复污染数据相机。
                UpdateSubcameraTargets();
                mainCameraState = CaptureCameraState(mapCamera);
                colorCurvesStateCaptured = colorCurves != null;
                colorCurvesWasEnabled = colorCurves != null && colorCurves.enabled;
                extraCamStates.Clear();
                deferredDataCameraStates.Clear();
                foreach (var c in Camera.allCameras)
                {
                    if (c == null || c == mapCamera) continue;
                    extraCamStates[c] = CaptureCameraState(c);
                    c.enabled = false;
                }
                suppressionActive = true;
            }

            // 每帧只重申第一人称主相机参数；不触碰副相机的 RT。
            var st = FirstPerson3DMod.settings;
            SkyScenery.Apply();
            mapCamera.orthographic = false;
            mapCamera.clearFlags = CameraClearFlags.SolidColor;
            mapCamera.fieldOfView = st != null ? st.fov : Fps3DState.FovDeg;
            mapCamera.nearClipPlane = Fps3DState.NearClip;
            mapCamera.farClipPlane = Fps3DState.FarClip;
            mapCamera.backgroundColor = new Color(
                st != null ? st.skyR : 0.09f,
                st != null ? st.skyG : 0.11f,
                st != null ? st.skyB : 0.16f);
            // 第一人称主相机必须直接写入 backbuffer。
            if (mapCamera.targetTexture != null) mapCamera.targetTexture = null;
            if (mapCamera.rect.width < 0.9f || mapCamera.rect.height < 0.9f)
                mapCamera.rect = new Rect(0f, 0f, 1f, 1f);
            if (colorCurves != null && colorCurves.enabled) colorCurves.enabled = false;
            // layer 31 专供终端俯视。主相机去掉它，捕获提交不会盖住第一人称。
            int baseMask = mainCameraState != null ? mainCameraState.cullingMask : mapCamera.cullingMask;
            mapCamera.cullingMask = baseMask & ~GodView3D.GodMask;

            // 新出现的相机也必须在本帧被纳入快照并禁用，防止第三方相机漏出一帧。
            foreach (var c in Camera.allCameras)
            {
                if (c == null || c == mapCamera) continue;
                if (!extraCamStates.ContainsKey(c))
                    extraCamStates[c] = CaptureCameraState(c);
                c.enabled = false;
            }
            SuppressExternalCameraEffects(mapCamera);
        }

        private static CameraState CaptureCameraState(Camera c)
        {
            if (c == null) return null;
            return new CameraState
            {
                enabled = c.enabled,
                targetTexture = c.targetTexture,
                depth = c.depth,
                clearFlags = c.clearFlags,
                backgroundColor = c.backgroundColor,
                rect = c.rect,
                orthographic = c.orthographic,
                orthographicSize = c.orthographicSize,
                fieldOfView = c.fieldOfView,
                nearClipPlane = c.nearClipPlane,
                farClipPlane = c.farClipPlane,
                renderingPath = c.renderingPath,
                cullingMask = c.cullingMask,
                allowHDR = c.allowHDR,
                useOcclusionCulling = c.useOcclusionCulling,
                wasDataCamera = IsDataCamera(c)
            };
        }

        private static void RestoreCameraState(
            Camera c,
            CameraState state,
            bool restoreEnabled,
            bool forceTargetTexture = false)
        {
            if (c == null || state == null) return;
            c.depth = state.depth;
            c.clearFlags = state.clearFlags;
            c.backgroundColor = state.backgroundColor;
            c.rect = state.rect;
            c.orthographic = state.orthographic;
            c.orthographicSize = state.orthographicSize;
            c.fieldOfView = state.fieldOfView;
            c.nearClipPlane = state.nearClipPlane;
            c.farClipPlane = state.farClipPlane;
            c.renderingPath = state.renderingPath;
            c.cullingMask = state.cullingMask;
            c.allowHDR = state.allowHDR;
            c.useOcclusionCulling = state.useOcclusionCulling;
            if (forceTargetTexture || !state.wasDataCamera || !HasValidRenderTexture(c))
                c.targetTexture = state.targetTexture;
            if (restoreEnabled) c.enabled = state.enabled;
        }

        private static bool HasValidRenderTexture(Camera c)
        {
            if (c == null || c.targetTexture == null) return false;
            try
            {
                return c.targetTexture.width > 0
                    && c.targetTexture.height > 0
                    && c.targetTexture.IsCreated();
            }
            catch
            {
                return false;
            }
        }

        private static Camera GetWaterDepthCamera()
        {
            if (Current.SubcameraDriver == null || RimWorld.SubcameraDefOf.WaterDepth == null)
                return null;
            return Current.SubcameraDriver.GetSubcamera(
                RimWorld.SubcameraDefOf.WaterDepth);
        }

        // 数据相机：渲染到 Depth / 单双通道浮点 RT（如 WaterDepth）的相机。
        // 这类相机输出的是数据而非画面，恢复期若目标纹理丢失绝不能写进主画面。
        private static bool IsDataCamera(Camera cam)
        {
            if (cam == null) return false;
            if (cam == GetWaterDepthCamera()) return true;
            var rt = cam.targetTexture;
            if (rt != null)
            {
                switch (rt.format)
                {
                    case RenderTextureFormat.Depth:
                    case RenderTextureFormat.Shadowmap:
                    case RenderTextureFormat.RFloat:
                    case RenderTextureFormat.RGFloat:
                    case RenderTextureFormat.RHalf:
                    case RenderTextureFormat.RGHalf:
                        return true;
                }
            }
            string n = cam.name == null ? string.Empty : cam.name.ToLowerInvariant();
            return n.Contains("waterdepth")
                || n.Contains("gravshipmask")
                || n.Contains("subcamera");
        }

        private static void UpdateSubcameraTargets()
        {
            if (mapCamera == null || Current.SubcameraDriver == null) return;
            try
            {
                Current.SubcameraDriver.Init();
                Current.SubcameraDriver.UpdatePositions(mapCamera);
            }
            catch (System.Exception e)
            {
                Log.Warning(ModEntry.LogPrefix + "subcamera target refresh failed: " + e.Message);
            }
        }

        private static void SuppressExternalCameraEffects(Camera main)
        {
            if (main == null) return;
            foreach (var b in UnityEngine.Object.FindObjectsOfType<Behaviour>())
            {
                if (b == null || !b.enabled) continue;
                string fullName = b.GetType().FullName ?? string.Empty;
                string objectName = b.gameObject == null ? string.Empty : b.gameObject.name;
                string lower = fullName.ToLowerInvariant();
                string objectLower = objectName.ToLowerInvariant();
                bool mainEffect = b.gameObject == main.gameObject
                    && (lower.Contains("compositor") || lower.Contains("postprocess")
                        || lower.Contains("post_process") || lower.Contains("colorcorrection")
                        || lower.Contains("bloom") || lower.Contains("imageeffect"));
                bool nivarianEffect = objectLower == "nivarianmodfxcamera"
                    && (lower.Contains("camerasync") || lower.Contains("bloom"));
                if (!mainEffect && !nivarianEffect) continue;
                if (!externalEffectStates.ContainsKey(b))
                    externalEffectStates[b] = b.enabled;
                b.enabled = false;
            }
        }

        // ---- 诊断/急救通道（GABS cmdAction）：FP→Off 后画面冻结的定位工具 ----

        // 实时相机体检：主相机 enabled/激活/RT/驱动 + Suppress 状态 + 全相机清单。
        public static string CameraDiagnose()
        {
            var sb = new System.Text.StringBuilder();
            var camGo = GameObject.Find("Camera");
            var cam = camGo != null ? camGo.GetComponent<Camera>() : null;
            var drv = camGo != null ? camGo.GetComponent<CameraDriver>() : null;
            sb.Append("active=").Append(Fps3DState.Active)
              .Append(" supp=").Append(Suppress)
              .Append(" main=").Append(cam != null)
              .Append("/en=").Append(cam != null && cam.enabled)
              .Append("/goAct=").Append(camGo != null && camGo.activeInHierarchy)
              .Append("/rt=").Append(cam != null && cam.targetTexture != null)
              .Append("/persp=").Append(cam != null && !cam.orthographic)
              .Append("/drvEn=").Append(drv != null && drv.enabled)
              .Append("/depth=").Append(cam != null ? cam.depth.ToString("F0") : "-");
            foreach (var c in Camera.allCameras)
                sb.Append('|').Append(c.name).Append('/').Append(c.enabled ? "on" : "off");
            return sb.ToString();
        }

        // 主相机急救：强制启用、清离屏 RT、全屏 rect、激活物体与驱动。
        public static void HealMainCamera()
        {
            var camGo = GameObject.Find("Camera");
            if (camGo == null) return;
            camGo.SetActive(true);
            var cam = camGo.GetComponent<Camera>();
            if (cam != null)
            {
                cam.enabled = true;
                cam.targetTexture = null;
                cam.rect = new Rect(0f, 0f, 1f, 1f);
            }
            var drv = camGo.GetComponent<CameraDriver>();
            if (drv != null) drv.enabled = true;
        }

        private static void RestoreDeferredDataCameras()
        {
            if (deferredDataCameraStates.Count == 0) return;
            var restored = new List<Camera>();
            foreach (var kv in deferredDataCameraStates)
            {
                var c = kv.Key;
                var state = kv.Value;
                if (c == null || state == null)
                {
                    restored.Add(c);
                    continue;
                }
                if (!HasValidRenderTexture(c))
                {
                    c.enabled = false;
                    continue;
                }
                RestoreCameraState(c, state, false);
                c.enabled = state.enabled;
                restored.Add(c);
            }
            foreach (var c in restored)
                deferredDataCameraStates.Remove(c);
        }

        public static void RestoreCameras()
        {
            if (!suppressionActive)
            {
                RestoreDeferredDataCameras();
                SkyScenery.Restore();
                return;
            }

            // 先恢复主相机原始输出目标与投影状态，再由原版逻辑重建副相机目标。
            suppressionActive = false;
            if (mapCamera != null && mainCameraState != null)
                RestoreCameraState(mapCamera, mainCameraState, false, true);
            if (vanillaTiltCaptured && mapCamera != null)
                mapCamera.transform.rotation = vanillaMapRotation;
            if (colorCurvesStateCaptured && colorCurves != null)
                colorCurves.enabled = colorCurvesWasEnabled;

            UpdateSubcameraTargets();

            // 先恢复非数据相机状态。数据相机必须保留有效的离屏目标，否则保持禁用。
            foreach (var kv in extraCamStates)
            {
                var c = kv.Key;
                var state = kv.Value;
                if (c == null || state == null) continue;

                if (state.wasDataCamera)
                {
                    bool targetReady = HasValidRenderTexture(c);
                    if (!targetReady)
                    {
                        deferredDataCameraStates[c] = state;
                        c.enabled = false;
                        continue;
                    }
                    RestoreCameraState(c, state, false);
                    c.enabled = state.enabled;
                }
                else
                {
                    RestoreCameraState(c, state, true, true);
                }
            }
            extraCamStates.Clear();
            mainCameraState = null;
            colorCurvesStateCaptured = false;

            foreach (var kv in externalEffectStates)
            {
                if (kv.Key != null)
                    kv.Key.enabled = kv.Value;
            }
            externalEffectStates.Clear();
            SkyScenery.Restore();
        }

        // ---- Harmony 前缀 ----

        // 捕获窗内放行，让 DrawMesh 前缀把提交转到终端相机。其余帧继续跳过。
        private static bool SkipDrawMapMesh() => !Suppress || GodView3D.Capturing;
        private static bool SkipDynamicThings() => !Suppress || GodView3D.Capturing;
        private static bool SkipGravshipTerrainCurtain() => !Suppress;
        private static bool SkipFlush() => !Suppress || GodView3D.Capturing;
        private static bool SkipCameraUpdate() => !Suppress;
        private static bool SkipWeatherDraw() => !Suppress;
        private static bool SkipThingOverlays() => !Suppress;
        private static bool SkipVanillaUIOnGUI()
        {
            // 指挥面板查看期必须让原版 UI 跑起来，CommandPanel3D 再把它收进终端屏幕。
            // 这里若返回 false，后面的前缀和函数体都不会执行。
            if (MapScreen3D.Viewing) return true;
            return !(Suppress && CameraRig3D.VanillaInputTakenOver);
        }

        private static bool SuppressRimTalkBackdrop(Rect rect, Color color)
        {
            // RimTalk draws this fixed translucent backdrop from MapComponentOnGUI.
            // It is the only known screen-space obstruction and must not cover 3D view.
            if (!Suppress || rect.x < -1f || rect.y < -1f
                || rect.width < 1400f || rect.width > 1700f
                || rect.height < 150f || rect.height > 240f
                || color.a < 0.45f || color.a > 0.55f
                || color.r < 0.08f || color.r > 0.12f
                || color.g < 0.08f || color.g > 0.12f
                || color.b < 0.08f || color.b > 0.12f)
                return true;

            return false;
        }
    }
}
