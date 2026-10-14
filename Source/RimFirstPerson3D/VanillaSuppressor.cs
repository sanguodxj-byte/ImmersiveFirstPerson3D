// AI生成
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 屏蔽原版平铺绘制并停用原版地图相机；恢复时原样还原。
    [StaticConstructorOnStartup]
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
            // 地图标记与选中框：第一人称主相机跳过，终端俯视捕获窗内放行。
            harmony.Patch(AccessTools.Method(typeof(DesignationManager), nameof(DesignationManager.DrawDesignations)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipMapOverlays)));
            harmony.Patch(AccessTools.Method(typeof(SelectionDrawer), nameof(SelectionDrawer.DrawSelectionOverlays)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipMapOverlays)));
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
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipVanillaUIOnGUI)) { priority = Priority.Last });
            // 第一人称期间始终屏蔽原版底栏、顶栏殖民者条、左侧资源列表、右侧警报，防止信封/暂停/弹窗时穿透浮现
            harmony.Patch(AccessTools.Method(typeof(MainButtonsRoot), nameof(MainButtonsRoot.MainButtonsOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipVanillaHUD)));
            harmony.Patch(AccessTools.Method(typeof(ColonistBar), nameof(ColonistBar.ColonistBarOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipVanillaHUD)));
            harmony.Patch(AccessTools.Method(typeof(ResourceReadout), nameof(ResourceReadout.ResourceReadoutOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipVanillaHUD)));
            harmony.Patch(AccessTools.Method(typeof(AlertsReadout), nameof(AlertsReadout.AlertsReadoutOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipVanillaHUD)));
            // 原版 ThingOverlays 名牌/量表按俯视相机投影（锚在脚底+z 北偏），
            // 3D 模式全部悬空错位，一并屏蔽；名牌由 NameLabels3D 按头顶重画。
            harmony.Patch(AccessTools.Method(typeof(ThingOverlays), nameof(ThingOverlays.ThingOverlaysOnGUI)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipThingOverlays)));
            // WeatherManager.DrawAllWeather also draws live weather events before worker overlays.
            // Skip the complete visual entry point in 3D mode while keeping WeatherTick simulation.
            harmony.Patch(AccessTools.Method(typeof(WeatherManager), nameof(WeatherManager.DrawAllWeather)),
                prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SkipWeatherDraw)));

            // 修复 Graphic_Appearances 在 Waylith 引擎下因 .psd 后缀无法匹配外观以及 subGraphics 为空时的 NRE 崩溃
            var appearInit = AccessTools.Method(typeof(Graphic_Appearances), nameof(Graphic_Appearances.Init), new[] { typeof(GraphicRequest) });
            if (appearInit != null)
                harmony.Patch(appearInit, prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(GraphicAppearancesInitPrefix)));

            var matSingleProp = AccessTools.PropertyGetter(typeof(Graphic_Appearances), nameof(Graphic_Appearances.MatSingle));
            if (matSingleProp != null)
                harmony.Patch(matSingleProp, prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SafeMatSinglePrefix)));

            var subGraphicMethod = AccessTools.Method(typeof(Graphic_Appearances), nameof(Graphic_Appearances.SubGraphicFor), new[] { typeof(StuffAppearanceDef) });
            if (subGraphicMethod != null)
                harmony.Patch(subGraphicMethod, prefix: new HarmonyMethod(typeof(VanillaSuppressor), nameof(SafeSubGraphicForPrefix)));
        }

        // ---- 抑制开关：进入/退出 3D 模式时切换相机可用性 ----

        private static GameObject vanillaCamGo;
        private static bool suppressionActive;
        private static float nextCameraCheckTime;
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

        // 1.7（Waylith/UnityStubs）下 GameObject 没有 GetComponent(string) 重载，只有 GetComponent(Type)/<T>()。
        // 按类型名解析出 Type 再取组件，语义与「按名取组件」一致，可在 1.7（net10）下正常编译与运行。
        static Behaviour FindBehaviourByName(GameObject go, string typeName)
        {
            var t = System.Type.GetType(typeName);
            if (t == null)
            {
                var asms = System.AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length && t == null; i++)
                {
                    try
                    {
                        t = asms[i].GetType(typeName, false);
                        if (t == null)
                            t = asms[i].GetType("UnityStandardAssets.ImageEffects." + typeName, false);
                    }
                    catch { }
                }
            }
            return t == null ? null : go.GetComponent(t) as Behaviour;
        }

        public static void ApplyCameraSuppression()
        {
            if (vanillaCamGo == null)
            {
                vanillaCamGo = GameObject.Find("Camera");
                if (vanillaCamGo == null)
                {
                    Log.Warning(ModEntry.LogPrefix + "vanilla 'Camera' object not found");
                    return;
                }
            }
            if (mapCamera == null || mapCamera.gameObject != vanillaCamGo)
            {
                var currentMain = vanillaCamGo.GetComponent<Camera>();
                if (mapCamera != currentMain)
                {
                    if (suppressionActive)
                        RestoreCameras();
                    mapCamera = currentMain;
                    colorCurves = FindBehaviourByName(vanillaCamGo, "ColorCorrectionCurves");
                }
            }
            if (mapCamera == null) return;

            bool firstFrame = !suppressionActive;
            if (firstFrame)
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
                foreach (var c in Object.FindObjectsOfType<Camera>())
                {
                    if (c == null || c == mapCamera) continue;
                    extraCamStates[c] = CaptureCameraState(c);
                    c.enabled = false;
                }
                suppressionActive = true;
                nextCameraCheckTime = Time.unscaledTime + 1.5f;
                SuppressExternalCameraEffects(mapCamera);
            }

            // 每帧只重申第一人称主相机参数；不触碰副相机的 RT。
            var st = FirstPerson3DMod.settings;
            SkyScenery.Apply();
            mapCamera.orthographic = false;
            mapCamera.clearFlags = SkyScenery.HasNativeSkybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            mapCamera.fieldOfView = st != null ? st.fov : Fps3DState.FovDeg;
            mapCamera.nearClipPlane = Fps3DState.NearClip;
            mapCamera.farClipPlane = Fps3DState.FarClip;
            mapCamera.backgroundColor = SkyScenery.CurrentHorizonColor;
            // 第一人称主相机必须直接写入 backbuffer。
            if (mapCamera.targetTexture != null) mapCamera.targetTexture = null;
            if (mapCamera.rect.width < 0.9f || mapCamera.rect.height < 0.9f)
                mapCamera.rect = new Rect(0f, 0f, 1f, 1f);
            if (colorCurves != null && colorCurves.enabled) colorCurves.enabled = false;
            // layer 31 专供终端俯视。主相机去掉它，捕获提交不会盖住第一人称。
            int baseMask = mainCameraState != null ? mainCameraState.cullingMask : mapCamera.cullingMask;
            mapCamera.cullingMask = baseMask & ~GodView3D.GodMask;

            // 维持已捕获副相机与后处理组件禁用（零分配遍历）
            foreach (var kv in extraCamStates)
            {
                if (kv.Key != null && kv.Key.enabled)
                    kv.Key.enabled = false;
            }
            foreach (var kv in externalEffectStates)
            {
                if (kv.Key != null && kv.Key.enabled)
                    kv.Key.enabled = false;
            }

            // 到达周期复查时重扫场景相机
            float now = Time.unscaledTime;
            bool periodicScan = now >= nextCameraCheckTime;
            if (periodicScan)
            {
                nextCameraCheckTime = now + 1.5f;
                foreach (var c in Object.FindObjectsOfType<Camera>())
                {
                    if (c == null || c == mapCamera) continue;
                    if (!extraCamStates.ContainsKey(c))
                        extraCamStates[c] = CaptureCameraState(c);
                    c.enabled = false;
                }
                SuppressExternalCameraEffects(mapCamera);
            }
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
            foreach (var b in main.GetComponents<Behaviour>())
            {
                if (b == null || !b.enabled || b == main) continue;
                string fullName = b.GetType().FullName ?? string.Empty;
                string lower = fullName.ToLowerInvariant();
                bool mainEffect = lower.Contains("compositor") || lower.Contains("postprocess")
                    || lower.Contains("post_process") || lower.Contains("colorcorrection")
                    || lower.Contains("bloom") || lower.Contains("imageeffect");
                if (!mainEffect) continue;
                if (!externalEffectStates.ContainsKey(b))
                    externalEffectStates[b] = b.enabled;
                b.enabled = false;
            }
            var nivGo = GameObject.Find("NivarianModFXCamera");
            if (nivGo != null)
            {
                foreach (var b in nivGo.GetComponents<Behaviour>())
                {
                    if (b == null || !b.enabled) continue;
                    string fullName = b.GetType().FullName ?? string.Empty;
                    string lower = fullName.ToLowerInvariant();
                    if (!lower.Contains("camerasync") && !lower.Contains("bloom")) continue;
                    if (!externalEffectStates.ContainsKey(b))
                        externalEffectStates[b] = b.enabled;
                    b.enabled = false;
                }
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
              .Append(" uiW=").Append(UI.screenWidth)
              .Append(" scrW=").Append(Screen.width)
              .Append(" uiScale=").Append(Prefs.UIScale)
              .Append(" curMatrix=").Append(GUI.matrix)
              .Append(" main=").Append(cam != null)
              .Append("/en=").Append(cam != null && cam.enabled)
              .Append("/goAct=").Append(camGo != null && camGo.activeInHierarchy)
              .Append("/rt=").Append(cam != null && cam.targetTexture != null)
              .Append("/persp=").Append(cam != null && !cam.orthographic)
              .Append("/drvEn=").Append(drv != null && drv.enabled)
              .Append("/depth=").Append(cam != null ? cam.depth.ToString("F0") : "-");
            foreach (var c in Object.FindObjectsOfType<Camera>())
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
        private static bool SkipMapOverlays() => !Suppress || GodView3D.Capturing;
        private static bool SkipGravshipTerrainCurtain() => !Suppress;
        private static bool SkipFlush() => !Suppress || GodView3D.Capturing;
        private static bool SkipCameraUpdate() => !Suppress;
        private static bool SkipWeatherDraw() => !Suppress;
        private static bool SkipThingOverlays() => !Suppress || MapScreen3D.Viewing;
        private static bool SkipVanillaHUD()
        {
            if (MapScreen3D.Viewing) return true;
            return !Suppress;
        }
        private static bool SkipVanillaUIOnGUI()
        {
            // 查看期原版 UIRoot 照常跑，点击和窗口都是原版的。画面由指挥面板记走，不留在第一人称上。
            if (MapScreen3D.Viewing) return true;
            return !(Suppress && CameraRig3D.VanillaInputTakenOver);
        }

        private static readonly FieldInfo SubGraphicsField = AccessTools.Field(typeof(Graphic_Appearances), "subGraphics");
        private static Graphic fallbackGraphic;
        public static Graphic FallbackGraphic
        {
            get
            {
                if (fallbackGraphic == null)
                {
                    try
                    {
                        var tex = BaseContent.GreyTex ?? Util3D.WhiteTex;
                        var single = new Graphic_Single();
                        single.Init(new GraphicRequest(typeof(Graphic_Single), tex, ShaderDatabase.Cutout, Vector2.one, Color.white, Color.white, null, 0, null, null));
                        fallbackGraphic = single;
                    }
                    catch
                    {
                        fallbackGraphic = BaseContent.BadGraphic;
                    }
                }
                return fallbackGraphic;
            }
        }

        private static Material fallbackWallMat;
        public static Material FallbackWallMat
        {
            get
            {
                if (fallbackWallMat == null)
                {
                    try
                    {
                        fallbackWallMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.65f, 0.65f, 0.65f));
                    }
                    catch
                    {
                        fallbackWallMat = BaseContent.BadMat;
                    }
                }
                return fallbackWallMat;
            }
        }

        private static bool GraphicAppearancesInitPrefix(Graphic_Appearances __instance, GraphicRequest req)
        {
            try
            {
                var data = req.graphicData;
                __instance.data = data;
                __instance.path = req.path;
                __instance.color = req.color;
                __instance.drawSize = req.drawSize;
                List<StuffAppearanceDef> allDefsList = DefDatabase<StuffAppearanceDef>.AllDefsListForReading;
                var subGraphics = new Graphic[allDefsList.Count];

                for (int i = 0; i < subGraphics.Length; i++)
                {
                    StuffAppearanceDef stuffAppearance = allDefsList[i];
                    string text = req.path;
                    if (!stuffAppearance.pathPrefix.NullOrEmpty())
                        text = stuffAppearance.pathPrefix + "/" + text.Split('/').Last();

                    var allTex = ContentFinder<Texture2D>.GetAllInFolder(text);
                    Texture2D texture2D = null;
                    foreach (var x in allTex)
                    {
                        if (x == null || string.IsNullOrEmpty(x.name)) continue;
                        string clean = x.name;
                        if (clean.EndsWith(".dds", System.StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 4);
                        if (clean.EndsWith(".psd", System.StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 4);
                        if (clean.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(0, clean.Length - 4);
                        if (clean.EndsWith(stuffAppearance.defName, System.StringComparison.OrdinalIgnoreCase)
                            || clean.IndexOf(stuffAppearance.defName, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            texture2D = x;
                            break;
                        }
                    }
                    if (texture2D != null)
                    {
                        var single = new Graphic_Single();
                        single.Init(new GraphicRequest(typeof(Graphic_Single), texture2D, req.shader, req.drawSize, req.color, req.colorTwo, data, req.renderQueue, req.shaderParameters, null)
                        {
                            path = text + "/" + texture2D.name
                        });
                        subGraphics[i] = single;
                    }
                }
                int smoothIdx = StuffAppearanceDefOf.Smooth != null ? StuffAppearanceDefOf.Smooth.index : 0;
                Graphic fallback = (smoothIdx >= 0 && smoothIdx < subGraphics.Length) ? subGraphics[smoothIdx] : null;
                if (fallback == null)
                {
                    for (int i = 0; i < subGraphics.Length; i++)
                        if (subGraphics[i] != null) { fallback = subGraphics[i]; break; }
                }
                if (fallback == null)
                    fallback = FallbackGraphic;

                for (int num = 0; num < subGraphics.Length; num++)
                    if (subGraphics[num] == null) subGraphics[num] = fallback;

                SubGraphicsField?.SetValue(__instance, subGraphics);
                return false;
            }
            catch (System.Exception ex)
            {
                Log.Warning("[FirstPerson3D] GraphicAppearancesInitPrefix error: " + ex);
                return true;
            }
        }

        private static bool SafeMatSinglePrefix(Graphic_Appearances __instance, ref Material __result)
        {
            var subs = SubGraphicsField?.GetValue(__instance) as Graphic[];
            if (subs != null && subs.Length > 0)
            {
                int smoothIdx = StuffAppearanceDefOf.Smooth != null ? StuffAppearanceDefOf.Smooth.index : 0;
                if (smoothIdx >= 0 && smoothIdx < subs.Length && subs[smoothIdx] != null)
                {
                    try
                    {
                        var mat = subs[smoothIdx].MatSingle;
                        if (mat != null && !mat.NullOrBad()) { __result = mat; return false; }
                    }
                    catch { }
                }
                for (int i = 0; i < subs.Length; i++)
                {
                    if (subs[i] != null)
                    {
                        try
                        {
                            var mat = subs[i].MatSingle;
                            if (mat != null && !mat.NullOrBad()) { __result = mat; return false; }
                        }
                        catch { }
                    }
                }
            }
            __result = FallbackWallMat;
            return false;
        }

        private static bool SafeSubGraphicForPrefix(Graphic_Appearances __instance, StuffAppearanceDef app, ref Graphic __result)
        {
            var subs = SubGraphicsField?.GetValue(__instance) as Graphic[];
            if (subs == null || subs.Length == 0)
            {
                __result = FallbackGraphic;
                return false;
            }
            int idx = app != null ? app.index : (StuffAppearanceDefOf.Smooth != null ? StuffAppearanceDefOf.Smooth.index : 0);
            if (idx >= 0 && idx < subs.Length && subs[idx] != null && subs[idx] != BaseContent.BadGraphic)
            {
                __result = subs[idx];
                return false;
            }
            int smoothIdx = StuffAppearanceDefOf.Smooth != null ? StuffAppearanceDefOf.Smooth.index : 0;
            if (smoothIdx >= 0 && smoothIdx < subs.Length && subs[smoothIdx] != null && subs[smoothIdx] != BaseContent.BadGraphic)
            {
                __result = subs[smoothIdx];
                return false;
            }
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i] != null && subs[i] != BaseContent.BadGraphic)
                {
                    __result = subs[i];
                    return false;
                }
            }
            __result = FallbackGraphic;
            return false;
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
