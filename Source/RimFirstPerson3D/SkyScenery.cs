// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 天空盒与天气系统（全 GPU Shader 硬件渲染驱动）
    // ============================================================
    //
    // 架构原则：
    //   1. 100% 走现代 GPU 着色器硬件管线，彻底废除 CPU 软件逐像素烘焙贴图；
    //   2. 100% 锚定在 RimWorld 地图 3D 世界坐标系（World-Space mapOrigin），
    //      绝不贴在相机镜头前，任意行走、环视、大仰角抬头具备完全物理真实的 3D 空间视差；
    //   3. 着色器矩阵：
    //      - 天空穹顶：Unity 物理大气散射片元着色器 (Skybox/Procedural)；
    //      - 3D 大气云层：RimWorld 原生 GPU 大气云层着色器 (ShaderDatabase.Clouds / WorldMaterials.Clouds)，
    //        由 GPU 在片元着色器内硬件采样双层 Perlin 噪声与 SunsetGradient 色阶，由世界太阳方向驱动光照散射；
    //      - 3D 闪电电弧：纯 GPU 加法发光着色器 (ShaderDatabase.WorldOverlayAdditive)，
    //        纯顶点颜色渐变羽化 + 加法光子叠加，零贴图、零边缘硬切；
    //      - 3D 空间雨雪：纯 GPU 顶点色半透明着色器 (ShaderDatabase.VertexColor)，
    //        由顶点颜色与透明度控制水滴尖锐度与雪花光晕，零贴图开销；
    //      - 太阳与月亮：纯三维世界天球发光着色器与大气辉光。
    [StaticConstructorOnStartup]
    public static class SkyScenery
    {
        public static string DebugPreset = "";
        public static Color CurrentHorizonColor { get; private set; } = new Color(0.68f, 0.84f, 0.97f);

        // ---- 原版天空盒接管/还原 ----
        private static Skybox cameraSkybox;
        private static Material originalMaterial;
        private static bool originalEnabled;
        private static bool skyboxAddedByMod;
        private static bool captured;
        private static Material skyMaterial;

        // ---- GPU Shader 材质库 ----
        private static Material gpuCloudMat;
        private static Material gpuSunMat;
        private static Material gpuMoonMat;
        private static Material gpuMoonGlowMat;
        private static Material gpuStarMat;
        private static Material gpuLightningMat;
        private static Material gpuRainMat;
        private static Material gpuSnowMat;
        private static Material gpuSplashMat;
        private static Material gpuSandMat;

        // ---- 3D 世界空间网格库（1:1 真实世界米制尺寸）----
        private static Mesh quadMesh;
        private static Mesh starMesh;
        private static Mesh gpuCloudMesh;
        private static Mesh rainVolMesh;
        private static Mesh snowVolMesh;
        private static Mesh splashMesh;
        private static Mesh sandNearMesh;
        private static Mesh sandFarMesh;
        private static Mesh activeBoltMeshA;
        private static Mesh activeBoltMeshB;

        // ---- 纹理与动画缓存 ----
        private static Texture2D noiseTexture;
        private static Texture2D sunsetRampTexture;
        private static Texture2D cloudTexture;
        private static Texture2D starAtlasTex;
        private static Texture2D sandCurtainTex;
        private static Vector2 cloudScroll;
        private static Vector2 precipScroll;
        private static float lastCloudTime = -1f;

        // ---- 瞬态 3D 闪电世界状态 ----
        private static Vector2 strikeWorldXZ_A;
        private static Vector2 strikeWorldXZ_B;
        private static float strikeStartTime = -99f;
        private static float nextStrikeTime = 0f;
        private static int strikeSeedCounter = 404;

        public static bool HasNativeSkybox => skyMaterial != null;

        /// <summary>当帧环境采样参数。</summary>
        public struct Env
        {
            public Color sky;
            public Color midSky;
            public Color horizon;
            public Color ground;
            public Color sunGlowHorizon;
            public Color sunGlowUpper;
            public Color antiSunHorizon;
            public float glow;
            public float celGlow;
            public float twilight;
            public float flash;
            public float aurora;
            public Vector3 sunDir;
            public Vector3 moonDir;
            public float cloudiness;
            public float rain, snow, sand, fog, storm, wind, windAngle;
            public float shine;
            public Color overlay;
            public int palette;
        }

        // ------------------------------------------------------------
        // 生命周期与 GPU 着色器材质初始化
        // ------------------------------------------------------------

        public static void Apply()
        {
            var cam = CameraRig3D.Cam;
            if (cam == null) return;
            if (cameraSkybox == null || cameraSkybox.gameObject != cam.gameObject)
            {
                cameraSkybox = cam.GetComponent<Skybox>();
                if (!captured)
                {
                    if (cameraSkybox != null)
                    {
                        originalMaterial = cameraSkybox.material;
                        originalEnabled = cameraSkybox.enabled;
                        skyboxAddedByMod = false;
                    }
                    else
                    {
                        originalMaterial = null;
                        originalEnabled = false;
                        skyboxAddedByMod = true;
                    }
                    captured = true;
                }
                if (cameraSkybox == null)
                    cameraSkybox = cam.gameObject.AddComponent<Skybox>();
            }
            if (skyMaterial == null)
            {
                var shader = Shader.Find("Skybox/Procedural");
                if (shader != null)
                {
                    skyMaterial = new Material(shader) { name = "FirstPerson3D_GpuProceduralSky" };
                    if (skyMaterial.HasProperty("_SunSize")) skyMaterial.SetFloat("_SunSize", 0.045f);
                    if (skyMaterial.HasProperty("_SunSizeConvergence")) skyMaterial.SetFloat("_SunSizeConvergence", 5.0f);
                    if (skyMaterial.HasProperty("_AtmosphereThickness")) skyMaterial.SetFloat("_AtmosphereThickness", 1.15f);
                    if (skyMaterial.HasProperty("_Exposure")) skyMaterial.SetFloat("_Exposure", 1.05f);
                }
            }
            EnsureGpuMaterialsAndMeshes();

            var env = SampleCached(Find.CurrentMap);
            CurrentHorizonColor = env.horizon;
            UpdateSkyMaterial(env);
        }

        private static void UpdateSkyMaterial(Env env)
        {
            if (skyMaterial == null) return;
            float g = Mathf.Clamp01(env.glow);
            // GPU 物理大气散射参数随昼夜动态演进：夜里将 _SkyTint、_GroundColor 与 _Exposure 一同收暗，
            // 否则 Skybox/Procedural 的瑞利散射基底会把深夜天空刷成白昼亮蓝。
            Color dayTint = Color.Lerp(new Color(0.76f, 0.86f, 0.98f), env.sky, 0.65f);
            Color nightTint = new Color(0.03f, 0.06f, 0.14f);
            if (skyMaterial.HasProperty("_SkyTint"))
                skyMaterial.SetColor("_SkyTint", Color.Lerp(nightTint, dayTint, Mathf.Pow(g, 0.75f)));
            if (skyMaterial.HasProperty("_GroundColor"))
                skyMaterial.SetColor("_GroundColor", Color.Lerp(env.ground, env.horizon, 0.55f));
            if (skyMaterial.HasProperty("_AtmosphereThickness"))
                skyMaterial.SetFloat("_AtmosphereThickness",
                    Mathf.Lerp(0.42f, Mathf.Lerp(0.95f, 1.55f, env.twilight * 0.8f + env.fog * 0.4f), Mathf.Clamp01(g * 1.25f)));
            if (skyMaterial.HasProperty("_Exposure"))
                skyMaterial.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.05f, Mathf.Pow(g, 0.68f)) + env.flash * 0.65f);
            if (cameraSkybox != null)
            {
                cameraSkybox.material = skyMaterial;
                cameraSkybox.enabled = true;
            }
        }

        public static void Restore()
        {
            if (cameraSkybox != null)
            {
                if (skyboxAddedByMod)
                {
                    cameraSkybox.enabled = false;
                    cameraSkybox.material = null;
                    Object.Destroy(cameraSkybox);
                }
                else if (captured)
                {
                    cameraSkybox.material = originalMaterial;
                    cameraSkybox.enabled = originalEnabled;
                }
            }
            RenderSettings.fog = false;
            captured = false;
            cameraSkybox = null;
            originalMaterial = null;
            skyboxAddedByMod = false;
        }

        private static void EnsureGpuMaterialsAndMeshes()
        {
            if (quadMesh != null) return;
            quadMesh = BuildDoubleQuad();

            // 1. GPU 大气云层着色器 (ShaderDatabase.Clouds / WorldMaterials.Clouds)
            Shader cloudShader = ShaderDatabase.Clouds;
            if (cloudShader == null) cloudShader = ShaderDatabase.Transparent;
            if (cloudShader != null)
            {
                gpuCloudMat = new Material(cloudShader) { name = "FirstPerson3D_GpuClouds", renderQueue = 1015 };
                noiseTexture = ContentFinder<Texture2D>.Get("Other/Perlin", false) ?? Util3D.WhiteTex;
                sunsetRampTexture = ContentFinder<Texture2D>.Get("World/SunsetGradient", false) ?? Util3D.WhiteTex;
                cloudTexture = ContentFinder<Texture2D>.Get("World/CloudMap", false) ?? Util3D.WhiteTex;
                gpuCloudMat.SetTexture(ShaderPropertyIDs.NoiseTex, noiseTexture);
                gpuCloudMat.SetTexture(ShaderPropertyIDs.SunsetColorRamp, sunsetRampTexture);
                gpuCloudMat.SetTexture(ShaderPropertyIDs.CloudMap, cloudTexture);
                gpuCloudMat.SetVector(ShaderPropertyIDs.Seed, new Vector2(0.42f, 0.77f));
                gpuCloudMat.SetFloat(ShaderPropertyIDs.PlanetRadius, 540f);
                gpuCloudMat.SetVector(ShaderPropertyIDs.PlanetOrigin, Vector3.zero);
            }
            gpuCloudMesh = BuildGpuCloudHemisphere(72, 32, 540f);

            // 2. GPU 日月星辰着色器
            Shader addShader = ShaderDatabase.WorldOverlayAdditive;
            if (addShader == null) addShader = Shader.Find("Custom/Unlit additive");
            if (addShader == null) addShader = ShaderDatabase.Transparent;

            gpuSunMat = new Material(addShader) { name = "FirstPerson3D_GpuSun", renderQueue = 1009 };
            gpuSunMat.mainTexture = MakeSunDiscTex(64, 0.25f);

            gpuMoonGlowMat = new Material(addShader) { name = "FirstPerson3D_GpuMoonGlow", renderQueue = 1007 };
            gpuMoonGlowMat.mainTexture = MakeRadialTex(64, 0.0f, 1.5f);

            gpuMoonMat = new Material(ShaderDatabase.Transparent) { name = "FirstPerson3D_GpuMoon", renderQueue = 1008 };
            gpuMoonMat.mainTexture = MakeMoonTex(128);

            starAtlasTex = MakeStarAtlasTex(128);
            gpuStarMat = new Material(ShaderDatabase.Transparent) { name = "FirstPerson3D_GpuStars", mainTexture = starAtlasTex, renderQueue = 1005 };
            starMesh = BuildStarDome(880, 640f);

            // 3. 纯 GPU 加法发光闪电 (ShaderDatabase.WorldOverlayAdditive) —— 零贴图硬边，纯顶点色高斯叠加
            gpuLightningMat = new Material(addShader) { name = "FirstPerson3D_GpuLightning", renderQueue = 1025 };
            gpuLightningMat.mainTexture = Util3D.WhiteTex;

            // 4. 纯 GPU 顶点色半透明雨雪网格 (ShaderDatabase.VertexColor)
            Shader vColorShader = ShaderDatabase.VertexColor ?? ShaderDatabase.Transparent;

            gpuRainMat = new Material(vColorShader) { name = "FirstPerson3D_GpuRainVColor", renderQueue = 2810 };
            gpuSnowMat = new Material(vColorShader) { name = "FirstPerson3D_GpuSnowVColor", renderQueue = 2810 };
            gpuSplashMat = new Material(vColorShader) { name = "FirstPerson3D_GpuSplashVColor", renderQueue = 2795 };

            rainVolMesh = BuildGpuVColorRainMesh(2200, 64f, 24f, new Vector2(0.5f, 0.2f), 20261018);
            snowVolMesh = BuildGpuVColorSnowMesh(1600, 64f, 20f, 20261019);
            splashMesh = BuildGpuVColorSplashMesh(260, 64f, 901);

            sandCurtainTex = MakeSandCurtainTex(256);
            gpuSandMat = new Material(ShaderDatabase.Transparent) { name = "FirstPerson3D_GpuSand", mainTexture = sandCurtainTex, renderQueue = 2802 };
            sandNearMesh = BuildPrecipRing(24, 36.0f, 4.5f, 9.0f, 6.5f, 1.0f, 1.0f, 1, 0f);
            sandFarMesh = BuildPrecipRing(28, 68.0f, 6.5f, 13.0f, 9.5f, 1.0f, 1.0f, 2, 0f);
        }

        // ------------------------------------------------------------
        // 环境采样
        // ------------------------------------------------------------

        private static Map cachedEnvMap;
        private static int cachedEnvFrame = -1;
        private static Env cachedEnv;

        /// <summary>当帧该图的环境（带帧缓存）。光照与天空都从这里取，保证同一个数。</summary>
        public static Env SampleCached(Map map)
        {
            int f = RealTime.frameCount;
            if (cachedEnvFrame == f && ReferenceEquals(cachedEnvMap, map)) return cachedEnv;
            cachedEnv = Sample(map);
            cachedEnvMap = map;
            cachedEnvFrame = f;
            return cachedEnv;
        }

        public static Env Sample(Map map)
        {
            var e = new Env
            {
                sky = new Color(0.09f, 0.30f, 0.76f),
                midSky = new Color(0.24f, 0.52f, 0.90f),
                horizon = new Color(0.68f, 0.84f, 0.97f),
                ground = new Color(0.16f, 0.18f, 0.16f),
                sunGlowHorizon = new Color(1.00f, 0.44f, 0.14f),
                sunGlowUpper = new Color(0.95f, 0.38f, 0.50f),
                antiSunHorizon = new Color(0.54f, 0.42f, 0.66f),
                overlay = Color.white,
                glow = 1f,
                celGlow = 1f,
                shine = 1f,
                sunDir = new Vector3(0f, 0.55f, -0.80f).normalized,
                moonDir = new Vector3(0.14f, 0.46f, -0.85f).normalized,
                cloudiness = 0.18f,
                wind = 0.45f,
                windAngle = 0.6f,
                palette = 0
            };

            Color skyMul = Color.white;
            float dayPct = 0.50f;
            float lat = 25f;

            if (map != null)
            {
                try
                {
                    dayPct = GenLocalDate.DayPercent(map);
                    if (map.Tile.Valid && Find.WorldGrid != null)
                    {
                        var loc = Find.WorldGrid.LongLatOf(map.Tile);
                        lat = loc.y;
                    }
                }
                catch { }

                try
                {
                    var sl = map.skyManager != null ? map.skyManager.CurSkyGlow : 1f;
                    e.glow = sl;
                    e.celGlow = Mathf.Clamp01(Mathf.Pow(sl, 0.82f));
                }
                catch { }

                try
                {
                    var wm = map.weatherManager;
                    var w = wm != null ? wm.curWeather : null;
                    if (w != null)
                    {
                        e.rain = WeatherFlag(w, "rain");
                        e.snow = WeatherFlag(w, "snow");
                        e.fog  = WeatherFlag(w, "fog");
                        e.storm = WeatherFlag(w, "storm") + WeatherFlag(w, "thunder");
                        e.sand = WeatherFlag(w, "sand") + WeatherFlag(w, "dust");
                        e.wind = wm.CurWindSpeedFactor;
                        e.windAngle = 0.6f;

                        if (wm.eventHandler != null)
                        {
                            var events = wm.eventHandler.LiveEventsListForReading;
                            for (int i = 0; i < events.Count; i++)
                            {
                                if (events[i] is WeatherEvent_LightningFlash lf)
                                    e.flash = Mathf.Max(e.flash, lf.SkyTargetLerpFactor);
                            }
                        }
                    }
                }
                catch { }

                try
                {
                    var gc = map.gameConditionManager;
                    if (gc != null)
                    {
                        var conds = gc.ActiveConditions;
                        for (int i = 0; i < conds.Count; i++)
                        {
                            var def = conds[i].def;
                            string cn = def != null ? def.defName : "";
                            if (!string.IsNullOrEmpty(cn) && cn.ToLowerInvariant().Contains("aurora"))
                                e.aurora = Mathf.Max(e.aurora, conds[i].SkyTargetLerpFactor(map));
                        }
                    }
                }
                catch { }

                try
                {
                    string b = map.Biome != null ? map.Biome.defName.ToLowerInvariant() : "";
                    if (b.Contains("desert") || b.Contains("arid")) e.palette = 1;
                    else if (b.Contains("tundra") || b.Contains("ice") || b.Contains("boreal")) e.palette = 2;
                    else if (b.Contains("jungle") || b.Contains("tropical")) e.palette = 3;
                }
                catch { }
            }

            // 调试预设覆盖（供 DevMode 或 GABS 验收各时段与各天气）
            if (!string.IsNullOrEmpty(DebugPreset))
                ApplyDebugPreset(DebugPreset, ref e, ref dayPct, ref skyMul);

            // 本地三维天球太阳与月亮轨迹：东(+X)升 -> 正午偏南/北高空 -> 西(-X)落
            float ha = (dayPct - 0.5f) * Mathf.PI * 2f;
            float rawElev = Mathf.Cos(ha);
            float maxAlt = Mathf.Lerp(0.56f, 0.38f, Mathf.Clamp01(Mathf.Abs(lat) / 75f));
            float sy = rawElev >= 0f
                ? Mathf.Lerp(rawElev * maxAlt, Mathf.Lerp(-0.02f, maxAlt, e.celGlow), 0.32f)
                : rawElev * 0.55f;
            float sx = -Mathf.Sin(ha);
            float zSign = lat < -1f ? 1f : -1f;
            float sz = zSign * 0.80f * (0.35f + 0.65f * Mathf.Max(0f, rawElev));
            e.sunDir = new Vector3(sx, sy, sz).normalized;

            float mElev = Mathf.Max(0.28f, -rawElev * 0.46f);
            e.moonDir = new Vector3(Mathf.Sin(ha) * 0.45f + 0.14f, mElev, zSign * 0.85f).normalized;

            float wet = e.snow > 0.2f ? Mathf.Max(e.snow, e.rain * 0.35f) : e.rain;
            float baseCloud = (e.glow < 0.2f && wet < 0.1f) ? 0.06f : (e.sand > 0.5f ? 0.05f : 0.18f);
            e.cloudiness = Mathf.Clamp01(baseCloud + wet * 0.65f + (e.sand > 0.5f ? 0f : e.fog * 0.55f) + e.storm * 0.28f);

            float g = Mathf.Clamp01(e.glow);
            var dayZenith = new Color(0.09f, 0.30f, 0.76f);
            var nightZenith = new Color(0.012f, 0.024f, 0.072f);
            var dayMid = new Color(0.24f, 0.52f, 0.90f);
            var nightMid = new Color(0.028f, 0.052f, 0.128f);
            var dayHorizon = new Color(0.68f, 0.84f, 0.97f);
            var nightHorizon = new Color(0.062f, 0.100f, 0.185f);

            e.sky = Color.Lerp(nightZenith, dayZenith, Mathf.Pow(g, 0.85f)) * skyMul;
            e.midSky = Color.Lerp(nightMid, dayMid, Mathf.Pow(g, 0.80f)) * skyMul;
            e.horizon = Color.Lerp(nightHorizon, dayHorizon, Mathf.Clamp01(Mathf.Pow(g, 0.70f) * 1.15f));

            float twilight = Mathf.Clamp01(1f - Mathf.Abs(e.sunDir.y - 0.12f) / 0.28f)
                * Mathf.Clamp01((g - 0.06f) / 0.35f)
                * (1f - e.cloudiness * 0.55f);
            e.twilight = twilight;
            if (twilight > 0.01f)
            {
                e.horizon = Color.Lerp(e.horizon, new Color(0.96f, 0.58f, 0.34f) * skyMul, twilight * 0.58f);
                e.midSky = Color.Lerp(e.midSky, new Color(0.44f, 0.38f, 0.72f) * skyMul, twilight * 0.42f);
            }

            var overcastZen = new Color(0.30f, 0.36f, 0.45f) * Mathf.Lerp(0.22f, 0.86f, g);
            var overcastHor = new Color(0.50f, 0.56f, 0.64f) * Mathf.Lerp(0.26f, 0.90f, g);
            float cov = Mathf.Clamp01((e.cloudiness - 0.22f) / 0.78f);
            e.sky = Color.Lerp(e.sky, overcastZen, cov * 0.78f);
            e.midSky = Color.Lerp(e.midSky, Color.Lerp(overcastZen, overcastHor, 0.5f), cov * 0.72f);
            e.horizon = Color.Lerp(e.horizon, overcastHor, cov * 0.68f);

            if (e.storm > 0.1f)
            {
                var stormDark = new Color(0.08f, 0.11f, 0.19f);
                var stormHor = new Color(0.16f, 0.21f, 0.32f);
                e.sky = Color.Lerp(e.sky, stormDark, e.storm * 0.88f);
                e.midSky = Color.Lerp(e.midSky, stormDark, e.storm * 0.82f);
                e.horizon = Color.Lerp(e.horizon, stormHor, e.storm * 0.75f);
            }
            if (e.snow > 0.1f)
            {
                var snowSky = new Color(0.62f, 0.70f, 0.80f) * Mathf.Lerp(0.38f, 0.92f, g);
                var snowHor = new Color(0.78f, 0.84f, 0.91f) * Mathf.Lerp(0.42f, 0.95f, g);
                e.sky = Color.Lerp(e.sky, snowSky, e.snow * 0.58f);
                e.midSky = Color.Lerp(e.midSky, snowHor, e.snow * 0.55f);
                e.horizon = Color.Lerp(e.horizon, snowHor, e.snow * 0.72f);
            }
            if (e.fog > 0.05f && e.sand < 0.2f && e.storm < 0.3f)
            {
                var haze = new Color(0.74f, 0.79f, 0.84f) * Mathf.Lerp(0.35f, 1f, g);
                e.sky = Color.Lerp(e.sky, haze, e.fog * 0.55f);
                e.midSky = Color.Lerp(e.midSky, haze, e.fog * 0.68f);
                e.horizon = Color.Lerp(e.horizon, haze, e.fog * 0.82f);
            }
            if (e.sand > 0.05f)
            {
                var dustTop = new Color(0.62f, 0.44f, 0.24f) * Mathf.Lerp(0.42f, 0.95f, g);
                var dustMid = new Color(0.78f, 0.58f, 0.32f) * Mathf.Lerp(0.45f, 0.98f, g);
                var dustHor = new Color(0.88f, 0.69f, 0.42f) * Mathf.Lerp(0.45f, 1f, g);
                e.sky = Color.Lerp(e.sky, dustTop, e.sand * 0.78f);
                e.midSky = Color.Lerp(e.midSky, dustMid, e.sand * 0.85f);
                e.horizon = Color.Lerp(e.horizon, dustHor, e.sand * 0.92f);
            }
            e.ground = Color.Lerp(new Color(0.08f, 0.10f, 0.09f), e.ground, 0.45f) * Mathf.Lerp(0.35f, 1f, g);
            e.sky.a = 1f;
            e.midSky.a = 1f;
            e.horizon.a = 1f;
            e.ground.a = 1f;
            return e;
        }

        private static void ApplyDebugPreset(string key, ref Env e, ref float dayPct, ref Color skyMul)
        {
            string k = key.Trim().ToLowerInvariant();
            e.rain = 0f; e.snow = 0f; e.sand = 0f; e.fog = 0f; e.storm = 0f; e.flash = 0f; e.aurora = 0f;
            switch (k)
            {
                case "dawn":
                    dayPct = 0.272f; e.celGlow = 0.56f; e.glow = 0.56f; e.shine = 0.94f;
                    skyMul = new Color(0.98f, 0.90f, 0.86f); e.wind = 0.45f; break;
                case "noon":
                    dayPct = 0.50f; e.celGlow = 1.0f; e.glow = 1.0f; e.shine = 1.0f;
                    skyMul = Color.white; e.wind = 0.50f; break;
                case "dusk":
                    dayPct = 0.728f; e.celGlow = 0.50f; e.glow = 0.50f; e.shine = 0.95f;
                    skyMul = new Color(1.0f, 0.86f, 0.78f); e.wind = 0.40f; break;
                case "night":
                    dayPct = 0.02f; e.celGlow = 0f; e.glow = 0.05f; e.shine = 0.65f;
                    skyMul = new Color(0.72f, 0.80f, 0.98f); e.wind = 0.25f; break;
                case "aurora":
                    dayPct = 0.04f; e.celGlow = 0f; e.glow = 0.16f; e.shine = 0.70f; e.aurora = 0.92f;
                    skyMul = new Color(0.72f, 0.95f, 0.88f); e.wind = 0.30f; break;
                case "rain":
                    dayPct = 0.58f; e.celGlow = 0.88f; e.glow = 0.54f; e.shine = 0.25f;
                    e.rain = 0.92f; e.fog = 0.16f; e.storm = 0.12f; e.wind = 1.15f;
                    skyMul = new Color(0.74f, 0.78f, 0.85f); break;
                case "storm":
                    dayPct = 0.81f; e.celGlow = 0.16f; e.glow = 0.34f; e.shine = 0.30f;
                    e.rain = 1.0f; e.fog = 0.18f; e.storm = 1.0f; e.flash = 0.72f; e.wind = 1.65f;
                    skyMul = new Color(0.62f, 0.70f, 0.86f); break;
                case "snow":
                    dayPct = 0.44f; e.celGlow = 0.82f; e.glow = 0.68f; e.shine = 0.35f;
                    e.rain = 0.20f; e.snow = 0.95f; e.fog = 0.24f; e.wind = 0.85f;
                    skyMul = new Color(0.86f, 0.90f, 0.96f); break;
                case "fog":
                case "sand":
                case "fog_sand":
                    dayPct = 0.64f; e.celGlow = 0.78f; e.glow = 0.58f; e.shine = 0.35f;
                    e.sand = 0.88f; e.fog = 0.42f; e.wind = 1.55f;
                    skyMul = new Color(0.94f, 0.78f, 0.56f); break;
            }
        }

        private static float WeatherFlag(WeatherDef w, string token)
        {
            if (w == null || string.IsNullOrEmpty(w.defName) || string.IsNullOrEmpty(token)) return 0f;
            return w.defName.ToLowerInvariant().Contains(token) ? 1f : 0f;
        }

        // ------------------------------------------------------------
        // 数学与过程采样辅助函数
        // ------------------------------------------------------------

        private static float SmoothStep01(float a, float b, float x)
        {
            if (Mathf.Approximately(a, b)) return x >= b ? 1f : 0f;
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private static float PeriodicFbm(float u, float v, int octaves, int period, int seed)
        {
            float sum = 0f, amp = 0.5f, maxAmp = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float freq = 1 << i;
                float nx = Mathf.Sin((u * freq + seed * 0.17f) * Mathf.PI * 2f);
                float ny = Mathf.Cos((v * freq + seed * 0.23f) * Mathf.PI * 2f);
                sum += (nx * ny * 0.5f + 0.5f) * amp;
                maxAmp += amp;
                amp *= 0.5f;
            }
            return maxAmp > 0f ? sum / maxAmp : 0.5f;
        }

        private static float PeriodicPuffs(float u, float v, int octaves, int seed)
        {
            return PeriodicFbm(u, v, octaves, 4, seed);
        }

        private static Mesh BuildDoubleQuad()
        {
            var m = new Mesh { name = "FirstPerson3D_DQuad" };
            m.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0)
            });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Texture2D MakeRadialTex(int n, float inner, float gamma)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_Radial" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = r <= inner ? 1f : Mathf.Clamp01(1f - (r - inner) / Mathf.Max(0.001f, 1f - inner));
                    a = Mathf.Pow(a, gamma);
                    byte b = (byte)Mathf.RoundToInt(a * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, b);
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeSunDiscTex(int n, float lowFactor)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_SunDisc" };
            var px = new Color32[n * n];
            Color edgeCol = Color.Lerp(new Color(1f, 0.92f, 0.72f), new Color(1f, 0.58f, 0.18f), lowFactor);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    if (r >= 1f) continue;
                    float a = r <= 0.54f ? 1f : Mathf.Pow(Mathf.Clamp01(1f - (r - 0.54f) / 0.46f), 1.45f);
                    float coreMix = Mathf.Clamp01(1f - r / 0.72f);
                    float cr = Mathf.Lerp(edgeCol.r, 1f, coreMix);
                    float cg = Mathf.Lerp(edgeCol.g, 1f, Mathf.Pow(coreMix, 0.72f));
                    float cb = Mathf.Lerp(edgeCol.b, 0.96f, Mathf.Pow(coreMix, 0.52f));
                    px[y * n + x] = new Color32(
                        (byte)Mathf.RoundToInt(cr * 255f),
                        (byte)Mathf.RoundToInt(cg * 255f),
                        (byte)Mathf.RoundToInt(cb * 255f),
                        (byte)Mathf.RoundToInt(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeMoonTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_MoonDisc" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = ((x + 0.5f) / n - 0.5f) * 2.2f;
                    float dy = ((y + 0.5f) / n - 0.5f) * 2.2f;
                    float r2 = dx * dx + dy * dy;
                    float r = Mathf.Sqrt(r2);
                    if (r <= 1.0f)
                    {
                        float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - r2));
                        float maria = PeriodicFbm(dx * 0.45f + 0.32f, dy * 0.45f + 0.58f, 3, 4, 701);
                        float craters = PeriodicPuffs(dx * 0.55f + 0.5f, dy * 0.55f + 0.5f, 4, 911);
                        float seaMask = SmoothStep01(0.42f, 0.62f, maria);
                        float shade = Mathf.Lerp(0.98f, 0.42f, seaMask) * (0.86f + 0.16f * craters);
                        float limb = 0.76f + 0.24f * Mathf.Pow(nz, 0.45f);
                        float lum = Mathf.Clamp01(shade * limb);
                        float edgeA = SmoothStep01(1.0f, 0.93f, r);
                        px[y * n + x] = new Color32(
                            (byte)Mathf.RoundToInt(lum * 236f),
                            (byte)Mathf.RoundToInt(lum * 244f),
                            (byte)Mathf.RoundToInt(Mathf.Min(1f, lum * 1.06f) * 255f),
                            (byte)Mathf.RoundToInt(edgeA * 255f));
                    }
                    else if (r < 1.15f)
                    {
                        float halo = Mathf.Pow(1f - (r - 1.0f) / 0.15f, 2.0f) * 0.42f;
                        px[y * n + x] = new Color32(210, 228, 255, (byte)Mathf.RoundToInt(halo * 255f));
                    }
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeStarAtlasTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_StarAtlas" };
            var px = new Color32[n * n];
            int cell = n / 4;
            Color[] tints =
            {
                new Color(0.72f, 0.86f, 1.00f),
                new Color(0.98f, 0.99f, 1.00f),
                new Color(1.00f, 0.91f, 0.72f),
                new Color(1.00f, 0.76f, 0.54f)
            };
            float[] mags = { 1.0f, 0.82f, 0.58f, 0.38f };
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                {
                    Color tint = tints[col];
                    float mag = mags[row];
                    for (int py = 0; py < cell; py++)
                        for (int pxx = 0; pxx < cell; pxx++)
                        {
                            float dx = (pxx + 0.5f) / cell - 0.5f;
                            float dy = (py + 0.5f) / cell - 0.5f;
                            float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                            float a = Mathf.Pow(Mathf.Clamp01(1f - r), row == 0 ? 1.6f : 2.1f);
                            if (row <= 1)
                            {
                                float cross = (Mathf.Exp(-Mathf.Abs(dx) * 16f) * Mathf.Exp(-Mathf.Abs(dy) * 3.0f)
                                    + Mathf.Exp(-Mathf.Abs(dy) * 16f) * Mathf.Exp(-Mathf.Abs(dx) * 3.0f)) * (row == 0 ? 0.48f : 0.25f);
                                a = Mathf.Clamp01(a + cross * Mathf.Clamp01(1f - r));
                            }
                            a *= mag;
                            float core = Mathf.Pow(Mathf.Clamp01(1f - r * 1.6f), 1.8f);
                            float cr = Mathf.Clamp01(Mathf.Lerp(tint.r, 1f, core * 0.70f));
                            float cg = Mathf.Clamp01(Mathf.Lerp(tint.g, 1f, core * 0.70f));
                            float cb = Mathf.Clamp01(Mathf.Lerp(tint.b, 1f, core * 0.70f));
                            int x = col * cell + pxx, y = row * cell + py;
                            px[y * n + x] = new Color32(
                                (byte)Mathf.RoundToInt(cr * 255f),
                                (byte)Mathf.RoundToInt(cg * 255f),
                                (byte)Mathf.RoundToInt(cb * 255f),
                                (byte)Mathf.RoundToInt(a * 255f));
                        }
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeSandCurtainTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_SandCurtainTex" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                float v = y / (float)(n - 1);
                float vertFade = SmoothStep01(0.0f, 0.16f, v) * Mathf.Pow(Mathf.Clamp01(1f - v), 1.35f);
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;
                    float billow = PeriodicFbm(u, v * 0.6f, 3, 4, 881);
                    float stream = PeriodicFbm(u * 2.0f, v * 4.0f, 4, 3, 991);
                    float d = Mathf.Clamp01((billow * 0.65f + stream * 0.35f - 0.32f) * 1.8f) * vertFade;
                    float shade = Mathf.Lerp(0.76f, 1.0f, billow);
                    px[y * n + x] = new Color32(
                        (byte)Mathf.RoundToInt(238f * shade),
                        (byte)Mathf.RoundToInt(192f * shade),
                        (byte)Mathf.RoundToInt(125f * shade),
                        (byte)Mathf.RoundToInt(d * 185f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        // ------------------------------------------------------------
        // 3D 几何网格构造器（100% 世界坐标系，零相机附着）
        // ------------------------------------------------------------

        private static Mesh BuildGpuCloudHemisphere(int lon, int lat, float radius)
        {
            const float maxPhi = Mathf.PI * 0.48f;
            var verts = new List<Vector3>((lon + 1) * (lat + 1));
            var uvs = new List<Vector2>((lon + 1) * (lat + 1));
            var tris = new List<int>(lon * lat * 12);
            for (int y = 0; y <= lat; y++)
            {
                float t = y / (float)lat;
                float phi = t * maxPhi;
                float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    verts.Add(new Vector3(sp * Mathf.Cos(theta) * radius, cp * radius, sp * Mathf.Sin(theta) * radius));
                    uvs.Add(new Vector2(u * 2.0f, (1f - t) * 2.0f));
                }
            }
            int stride = lon + 1;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * stride + x, b = a + stride;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var m = new Mesh { name = "FirstPerson3D_GpuCloudHemisphere" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildStarDome(int count, float radius)
        {
            var rnd = new System.Random(20261015);
            var verts = new List<Vector3>(count * 4);
            var uvs = new List<Vector2>(count * 4);
            var tris = new List<int>(count * 12);
            for (int i = 0; i < count; i++)
            {
                float u = (float)rnd.NextDouble();
                float v = (float)rnd.NextDouble();
                float theta = u * Mathf.PI * 2f;
                float phi = Mathf.Acos(1f - v * 0.94f);
                float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                var p = new Vector3(sp * Mathf.Cos(theta), cp, sp * Mathf.Sin(theta)) * radius;

                Vector3 up = Vector3.Cross(p, Vector3.up);
                if (up.sqrMagnitude < 0.001f) up = Vector3.Cross(p, Vector3.right);
                up.Normalize();
                Vector3 right = Vector3.Cross(p, up).normalized;

                float s = 1.35f + (float)rnd.NextDouble() * 2.85f;
                int col = rnd.Next(4), row = rnd.Next(4);
                const float pad = 0.01f;
                float u0 = col * 0.25f + pad, u1 = (col + 1) * 0.25f - pad;
                float v0 = row * 0.25f + pad, v1 = (row + 1) * 0.25f - pad;

                int b = verts.Count;
                verts.Add(p - right * s - up * s); verts.Add(p + right * s - up * s);
                verts.Add(p + right * s + up * s); verts.Add(p - right * s + up * s);
                uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u1, v0));
                uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u0, v1));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var m = new Mesh { name = "FirstPerson3D_Stars" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildPrecipRing(int count, float rMin, float rStep, float halfH, float width, float uTile, float vTile, int seedOffset, float uShear)
        {
            var verts = new List<Vector3>(count * 4);
            var uvs = new List<Vector2>(count * 4);
            var tris = new List<int>(count * 12);
            for (int i = 0; i < count; i++)
            {
                float a = ((i + seedOffset * 0.37f) / count) * Mathf.PI * 2f;
                float r = rMin + ((i * 3 + seedOffset) % 5) * rStep;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                var right = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                int b = verts.Count;
                verts.Add(p - right * width + Vector3.down * halfH);
                verts.Add(p + right * width + Vector3.down * halfH);
                verts.Add(p + right * width + Vector3.up * halfH);
                verts.Add(p - right * width + Vector3.up * halfH);
                float u0 = (i * 0.19f) % 1.0f;
                float v0 = (i * 0.31f) % 1.0f;
                uvs.Add(new Vector2(u0, v0));
                uvs.Add(new Vector2(u0 + uTile, v0));
                uvs.Add(new Vector2(u0 + uTile + uShear, v0 + vTile));
                uvs.Add(new Vector2(u0 + uShear, v0 + vTile));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var m = new Mesh { name = "FirstPerson3D_PrecipRing" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static List<Vector3> SubdivideBoltPath(Vector3 a, Vector3 b, int gens, float swayScale, System.Random rnd)
        {
            var pts = new List<Vector3> { a, b };
            float disp = swayScale;
            for (int g = 0; g < gens; g++)
            {
                var next = new List<Vector3>(pts.Count * 2) { pts[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector3 mid = (pts[i] + pts[i + 1]) * 0.5f;
                    mid.x += ((float)rnd.NextDouble() - 0.5f) * disp;
                    mid.y += ((float)rnd.NextDouble() - 0.5f) * disp * 0.22f;
                    mid.z += ((float)rnd.NextDouble() - 0.5f) * disp;
                    next.Add(mid);
                    next.Add(pts[i + 1]);
                }
                pts = next;
                disp *= 0.52f;
            }
            return pts;
        }

        // 构造纯世界空间 3D 交叉等离子体分形闪电（ShaderDatabase.WorldOverlayAdditive 纯加法顶点色，零贴图硬边）
        private static Mesh BuildGpuAdditiveLightningBolt(Vector3 topWorld, Vector3 botWorld, int seed, float trunkRadius)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>();
            var colors = new List<Color32>();
            var tris = new List<int>();

            void AddWorldCrossedSeg(Vector3 p0, Vector3 p1, float r0, float r1)
            {
                Vector3 tangent = p1 - p0;
                if (tangent.sqrMagnitude < 0.0001f) return;
                tangent.Normalize();
                Vector3 n1 = Vector3.Cross(tangent, Vector3.forward);
                if (n1.sqrMagnitude < 0.01f) n1 = Vector3.Cross(tangent, Vector3.right);
                n1.Normalize();
                Vector3 n2 = Vector3.Cross(tangent, n1).normalized;

                Color32 cCenter = new Color32(250, 253, 255, 245);
                Color32 cOuter = new Color32(110, 185, 255, 0);

                for (int si = 0; si < 2; si++)
                {
                    Vector3 side = si == 0 ? n1 : n2;
                    int b = verts.Count;
                    verts.Add(p0 - side * (r0 * 0.45f)); colors.Add(cCenter);
                    verts.Add(p0 + side * (r0 * 0.45f)); colors.Add(cCenter);
                    verts.Add(p1 + side * (r1 * 0.45f)); colors.Add(cCenter);
                    verts.Add(p1 - side * (r1 * 0.45f)); colors.Add(cCenter);
                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);

                    int bOuter = verts.Count;
                    verts.Add(p0 - side * (r0 * 1.6f)); colors.Add(cOuter);
                    verts.Add(p0 + side * (r0 * 1.6f)); colors.Add(cOuter);
                    verts.Add(p1 + side * (r1 * 1.6f)); colors.Add(cOuter);
                    verts.Add(p1 - side * (r1 * 1.6f)); colors.Add(cOuter);
                    tris.Add(bOuter); tris.Add(bOuter + 1); tris.Add(bOuter + 2);
                    tris.Add(bOuter); tris.Add(bOuter + 2); tris.Add(bOuter + 3);
                    tris.Add(bOuter); tris.Add(bOuter + 2); tris.Add(bOuter + 1);
                    tris.Add(bOuter); tris.Add(bOuter + 3); tris.Add(bOuter + 2);
                }
            }

            var mainPts = SubdivideBoltPath(topWorld, botWorld, 5, 11.0f, rnd);
            for (int i = 0; i < mainPts.Count - 1; i++)
            {
                float t0 = i / (float)(mainPts.Count - 1);
                float t1 = (i + 1) / (float)(mainPts.Count - 1);
                float r0 = Mathf.Lerp(trunkRadius, trunkRadius * 0.52f, t0);
                float r1 = Mathf.Lerp(trunkRadius, trunkRadius * 0.52f, t1);
                AddWorldCrossedSeg(mainPts[i], mainPts[i + 1], r0, r1);

                if (i >= 3 && i <= mainPts.Count - 6 && i % 4 == 0 && rnd.NextDouble() < 0.82)
                {
                    Vector3 bStart = mainPts[i];
                    float bSign = (i % 8 == 0) ? 1f : -1f;
                    Vector3 bEnd = bStart + new Vector3(
                        bSign * (6.5f + (float)rnd.NextDouble() * 9.5f),
                        -(8.0f + (float)rnd.NextDouble() * 11.0f),
                        ((float)rnd.NextDouble() - 0.5f) * 12.0f);
                    if (bEnd.y < botWorld.y + 1.0f) bEnd.y = botWorld.y + 1.0f;
                    var bPts = SubdivideBoltPath(bStart, bEnd, 3, 3.8f, rnd);
                    for (int k = 0; k < bPts.Count - 1; k++)
                    {
                        float bt0 = k / (float)(bPts.Count - 1);
                        float bt1 = (k + 1) / (float)(bPts.Count - 1);
                        AddWorldCrossedSeg(bPts[k], bPts[k + 1],
                            Mathf.Lerp(r0 * 0.65f, trunkRadius * 0.26f, bt0),
                            Mathf.Lerp(r0 * 0.65f, trunkRadius * 0.22f, bt1));
                    }
                }
            }
            var m = new Mesh { name = "FirstPerson3D_WorldLightning3D" };
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildGpuVColorRainMesh(int count, float boxSpan, float boxHeight, Vector2 windVec, int seed)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>(count * 8);
            var colors = new List<Color32>(count * 8);
            var tris = new List<int>(count * 24);
            Vector3 axis = new Vector3(windVec.x * 0.16f, -1.0f, windVec.y * 0.16f).normalized;
            Vector3 side1 = new Vector3(0.866f, 0f, 0.5f);
            Vector3 side2 = new Vector3(-0.5f, 0f, 0.866f);

            Color32 cHead = new Color32(230, 245, 255, 220);
            Color32 cTail = new Color32(180, 215, 255, 0);

            for (int i = 0; i < count; i++)
            {
                float x = ((float)rnd.NextDouble() - 0.5f) * boxSpan;
                float z = ((float)rnd.NextDouble() - 0.5f) * boxSpan;
                float y = (float)rnd.NextDouble() * boxHeight;
                float len = 1.15f + (float)rnd.NextDouble() * 0.85f;
                float hw = 0.045f + (float)rnd.NextDouble() * 0.02f;
                Vector3 pBot = new Vector3(x, y, z);
                Vector3 pTop = pBot - axis * len;

                for (int si = 0; si < 2; si++)
                {
                    Vector3 s = si == 0 ? side1 : side2;
                    int b = verts.Count;
                    verts.Add(pBot - s * hw); colors.Add(cHead);
                    verts.Add(pBot + s * hw); colors.Add(cHead);
                    verts.Add(pTop + s * hw); colors.Add(cTail);
                    verts.Add(pTop - s * hw); colors.Add(cTail);

                    tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                }
            }
            var m = new Mesh { name = "FirstPerson3D_GpuVColorRain" };
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildGpuVColorSnowMesh(int count, float boxSpan, float boxHeight, int seed)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>(count * 8);
            var colors = new List<Color32>(count * 8);
            var tris = new List<int>(count * 24);

            Color32 cCenter = new Color32(250, 252, 255, 235);
            Color32 cEdge = new Color32(200, 225, 255, 0);

            for (int i = 0; i < count; i++)
            {
                float x = ((float)rnd.NextDouble() - 0.5f) * boxSpan;
                float z = ((float)rnd.NextDouble() - 0.5f) * boxSpan;
                float y = (float)rnd.NextDouble() * boxHeight;
                float r = 0.065f + (float)rnd.NextDouble() * 0.065f;
                float ang = (float)rnd.NextDouble() * Mathf.PI;
                float c = Mathf.Cos(ang) * r, s = Mathf.Sin(ang) * r;

                int b1 = verts.Count;
                verts.Add(new Vector3(x - c, y - r, z - s)); colors.Add(cEdge);
                verts.Add(new Vector3(x + c, y - r, z + s)); colors.Add(cEdge);
                verts.Add(new Vector3(x + c, y + r, z + s)); colors.Add(cCenter);
                verts.Add(new Vector3(x - c, y + r, z - s)); colors.Add(cCenter);
                tris.Add(b1); tris.Add(b1 + 1); tris.Add(b1 + 2);
                tris.Add(b1); tris.Add(b1 + 2); tris.Add(b1 + 3);
                tris.Add(b1); tris.Add(b1 + 2); tris.Add(b1 + 1);
                tris.Add(b1); tris.Add(b1 + 3); tris.Add(b1 + 2);

                int b2 = verts.Count;
                verts.Add(new Vector3(x - s, y - r, z + c)); colors.Add(cEdge);
                verts.Add(new Vector3(x + s, y - r, z - c)); colors.Add(cEdge);
                verts.Add(new Vector3(x + s, y + r, z - c)); colors.Add(cCenter);
                verts.Add(new Vector3(x - s, y + r, z + c)); colors.Add(cCenter);
                tris.Add(b2); tris.Add(b2 + 1); tris.Add(b2 + 2);
                tris.Add(b2); tris.Add(b2 + 2); tris.Add(b2 + 3);
                tris.Add(b2); tris.Add(b2 + 2); tris.Add(b2 + 1);
                tris.Add(b2); tris.Add(b2 + 3); tris.Add(b2 + 2);
            }
            var m = new Mesh { name = "FirstPerson3D_GpuVColorSnow" };
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildGpuVColorSplashMesh(int count, float boxSpan, int seed)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>(count * 4);
            var colors = new List<Color32>(count * 4);
            var tris = new List<int>(count * 12);

            Color32 cSplashCenter = new Color32(230, 245, 255, 160);
            Color32 cSplashEdge = new Color32(190, 220, 255, 0);

            for (int i = 0; i < count; i++)
            {
                float x = ((float)rnd.NextDouble() - 0.5f) * boxSpan;
                float z = ((float)rnd.NextDouble() - 0.5f) * boxSpan;
                float y = 0.04f + ((i % 5) * 0.015f);
                float r = 0.22f + (float)rnd.NextDouble() * 0.28f;
                int b = verts.Count;
                verts.Add(new Vector3(x - r, y, z - r)); colors.Add(cSplashEdge);
                verts.Add(new Vector3(x + r, y, z - r)); colors.Add(cSplashEdge);
                verts.Add(new Vector3(x + r, y, z + r)); colors.Add(cSplashCenter);
                verts.Add(new Vector3(x - r, y, z + r)); colors.Add(cSplashCenter);

                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var m = new Mesh { name = "FirstPerson3D_GpuVColorSplash" };
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------
        // 每帧绘制（100% 锚定在地图 3D 世界坐标 mapOrigin，零相机坐标跟随）
        // ------------------------------------------------------------

        private static Vector3 GetMapWorldOrigin(Map map)
        {
            if (map == null) return Vector3.zero;
            Vector3 off = Streaming3D.OffsetOf(map);
            float baseY = LevelStack3D.LevelBaseY(map);
            return new Vector3(off.x + map.Size.x * 0.5f, baseY, off.z + map.Size.z * 0.5f);
        }

        public static void DrawDistantRing()
        {
            if (!Fps3DState.Active || CameraRig3D.Cam == null) return;
            var map = Find.CurrentMap;
            var env = SampleCached(map);
            CurrentHorizonColor = env.horizon;
            var cam = CameraRig3D.Cam;
            if (cam != null) cam.backgroundColor = env.horizon;
            UpdateSkyMaterial(env);
            ApplyWeatherFog(env);

            Vector3 mapOrigin = GetMapWorldOrigin(map);
            DrawCelestial(map, mapOrigin, env);
        }

        private static void DrawCelestial(Map map, Vector3 mapOrigin, Env env)
        {
            if (quadMesh == null) return;
            float now = Time.realtimeSinceStartup;
            if (lastCloudTime < 0f) lastCloudTime = now;
            float dt = Mathf.Clamp(now - lastCloudTime, 0f, 0.1f);
            lastCloudTime = now;
            var wind = new Vector2(Mathf.Cos(env.windAngle), Mathf.Sin(env.windAngle)) * Mathf.Max(0.15f, env.wind);

            if (starMesh != null && gpuStarMat != null && env.glow < 0.55f)
            {
                float a = Mathf.Clamp01((0.55f - env.glow) / 0.45f) * (1f - env.cloudiness * 0.65f) * (1f - env.fog);
                gpuStarMat.color = new Color(1f, 1f, 1f, a * 0.96f);
                if (a > 0.02f)
                    Graphics.DrawMesh(starMesh, Matrix4x4.TRS(mapOrigin, Quaternion.identity, Vector3.one), gpuStarMat, 0);
            }

            DrawSunAndMoon(mapOrigin, env);
            DrawClouds(mapOrigin, env, wind, dt);
            DrawLightning(mapOrigin, env);
            DrawPrecip(map, mapOrigin, env, wind, dt);
        }

        private static void DrawSunAndMoon(Vector3 mapOrigin, Env env)
        {
            const float skyDist = 600f;
            const float scaleRatio = skyDist / 112f;

            float above = Mathf.Clamp01((env.sunDir.y + 0.05f) / 0.18f);
            float vis = above * (1f - env.cloudiness * 0.80f) * (1f - env.fog * 0.72f) * Mathf.Clamp01(env.glow * 1.35f);
            Vector3 up = Mathf.Abs(env.sunDir.y) > 0.92f ? Vector3.forward : Vector3.up;
            if (vis > 0.02f && gpuSunMat != null)
            {
                float low = 1f - Mathf.Clamp01(env.sunDir.y * 2.2f);
                gpuSunMat.color = new Color(1f, 1f, 1f, 0.98f * vis);
                var p = mapOrigin + env.sunDir * skyDist;
                var look = Quaternion.LookRotation(env.sunDir, up);
                float disc = (14.5f + 6.5f * low) * scaleRatio;
                Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(p, look, Vector3.one * disc), gpuSunMat, 0);
            }

            if (gpuMoonMat == null || env.glow > 0.42f || env.fog > 0.55f) return;
            float ma = Mathf.Clamp01((0.42f - env.glow) / 0.42f) * (1f - env.cloudiness * 0.70f);
            if (ma < 0.02f) return;
            var mp = mapOrigin + env.moonDir * 610f;
            Vector3 mUp = Mathf.Abs(env.moonDir.y) > 0.92f ? Vector3.forward : Vector3.up;
            var mlook = Quaternion.LookRotation(env.moonDir, mUp);
            if (gpuMoonGlowMat != null)
            {
                gpuMoonGlowMat.color = new Color(0.65f, 0.78f, 1.0f, 0.44f * ma);
                Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(mp, mlook, Vector3.one * (30f * scaleRatio)), gpuMoonGlowMat, 0);
            }
            gpuMoonMat.color = new Color(0.94f, 0.97f, 1.0f, 0.98f * ma);
            Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(mp, mlook, Vector3.one * (11.5f * scaleRatio)), gpuMoonMat, 0);
        }

        private static void DrawClouds(Vector3 mapOrigin, Env env, Vector2 wind, float dt)
        {
            if (gpuCloudMat == null || gpuCloudMesh == null) return;
            if (env.sand > 0.65f) return;

            // 驱动 GPU 大气云层 Shader (ShaderDatabase.Clouds)
            cloudScroll.x = (cloudScroll.x + wind.x * dt * 0.003f) % 1f;
            cloudScroll.y = (cloudScroll.y + wind.y * dt * 0.003f) % 1f;
            gpuCloudMat.SetVector(ShaderPropertyIDs.Seed, new Vector2(cloudScroll.x, cloudScroll.y));

            // 太阳光方向（驱动日出日落颜色渐变 SunsetGradient 与前向散射）
            Vector3 sunDir = env.sunDir.sqrMagnitude > 0.01f ? env.sunDir.normalized : Vector3.up;
            Shader.SetGlobalVector(ShaderPropertyIDs.PlanetSunLightDirection, sunDir);

            float cloudOp = Mathf.Clamp01(env.cloudiness * 1.15f) * Mathf.Lerp(0.28f, 1.0f, Mathf.Clamp01(env.glow * 1.3f));
            if (env.storm > 0.2f) cloudOp = Mathf.Max(cloudOp, 0.88f);
            if (env.rain > 0.2f) cloudOp = Mathf.Max(cloudOp, 0.75f);
            gpuCloudMat.SetFloat(ShaderPropertyIDs.CloudShaderOpacity, cloudOp);
            gpuCloudMat.SetFloat(ShaderPropertyIDs.GameTime, Time.realtimeSinceStartup * 0.05f);
            Color cloudTint = Color.Lerp(new Color(0.20f, 0.26f, 0.40f), Color.white, Mathf.Clamp01(env.glow * 1.25f));
            if (env.twilight > 0.05f) cloudTint = Color.Lerp(cloudTint, new Color(1.0f, 0.76f, 0.56f), env.twilight * 0.55f);
            gpuCloudMat.color = cloudTint;

            // 绘制在天穹云层球面上（半径 540m，世界中心 mapOrigin）
            Graphics.DrawMesh(gpuCloudMesh, Matrix4x4.TRS(mapOrigin, Quaternion.identity, Vector3.one), gpuCloudMat, 0);
        }

        private static void DrawLightning(Vector3 mapOrigin, Env env)
        {
            if (gpuLightningMat == null) return;
            float now = Time.realtimeSinceStartup;
            bool inStorm = env.storm > 0.45f || env.flash > 0.06f;
            if (!inStorm) return;

            // 在地图世界坐标中生成瞬态 3D 雷击（100% 真实世界坐标，Vector3.one 1:1 米制尺寸）
            if (now >= nextStrikeTime || (env.flash > 0.25f && now - strikeStartTime > 0.45f))
            {
                strikeStartTime = now;
                nextStrikeTime = now + (DebugPreset == "storm" ? 1.15f : (2.8f + (strikeSeedCounter % 5) * 0.85f));
                strikeSeedCounter += 137;

                float angA = (strikeSeedCounter * 1.618f) % (Mathf.PI * 2f);
                float distA = 32f + (strikeSeedCounter % 5) * 14f;
                strikeWorldXZ_A = new Vector2(mapOrigin.x + Mathf.Cos(angA) * distA, mapOrigin.z + Mathf.Sin(angA) * distA);

                float angB = angA + 0.42f;
                float distB = 45f + (strikeSeedCounter % 4) * 12f;
                strikeWorldXZ_B = new Vector2(mapOrigin.x + Mathf.Cos(angB) * distB, mapOrigin.z + Mathf.Sin(angB) * distB);

                Util3D.SafeDestroy(activeBoltMeshA);
                Util3D.SafeDestroy(activeBoltMeshB);
                Vector3 topA = new Vector3(strikeWorldXZ_A.x, mapOrigin.y + 35f, strikeWorldXZ_A.y);
                Vector3 botA = new Vector3(strikeWorldXZ_A.x + 2.5f, mapOrigin.y, strikeWorldXZ_A.y - 1.8f);
                Vector3 topB = new Vector3(strikeWorldXZ_B.x, mapOrigin.y + 36f, strikeWorldXZ_B.y);
                Vector3 botB = new Vector3(strikeWorldXZ_B.x - 2.2f, mapOrigin.y, strikeWorldXZ_B.y + 2.0f);
                activeBoltMeshA = BuildGpuAdditiveLightningBolt(topA, botA, strikeSeedCounter, 1.55f);
                activeBoltMeshB = BuildGpuAdditiveLightningBolt(topB, botB, strikeSeedCounter + 305, 1.35f);
            }

            float age = now - strikeStartTime;
            const float duration = 0.42f;
            if (age < 0f || age > duration || activeBoltMeshA == null) return;

            float normT = age / duration;
            float pulse1 = Mathf.Exp(-normT * 7.5f);
            float pulse2 = 0.85f * Mathf.Exp(-Mathf.Pow((normT - 0.36f) * 11f, 2f));
            float boltVis = Mathf.Clamp01(Mathf.Max(pulse1, pulse2));
            if (boltVis < 0.04f) return;

            gpuLightningMat.color = new Color(1f, 1f, 1f, boltVis);
            Graphics.DrawMesh(activeBoltMeshA, Matrix4x4.identity, gpuLightningMat, 0);
            if (activeBoltMeshB != null && normT < 0.65f)
                Graphics.DrawMesh(activeBoltMeshB, Matrix4x4.identity, gpuLightningMat, 0);
        }

        private static void DrawPrecip(Map map, Vector3 mapOrigin, Env env, Vector2 wind, float dt)
        {
            bool snowing = env.snow > 0.15f && env.snow >= env.rain * 0.5f;
            bool sanding = !snowing && env.sand > env.rain && env.sand > 0.2f;
            float rate = snowing ? env.snow : (sanding ? env.sand : env.rain);
            if (rate < 0.08f) return;

            float groundY = mapOrigin.y;
            int mapSpanX = map != null ? map.Size.x : 200;
            int mapSpanZ = map != null ? map.Size.z : 200;
            Vector3 mapBase = map != null ? Streaming3D.OffsetOf(map) : Vector3.zero;

            if (sanding)
            {
                if (gpuSandMat == null || sandNearMesh == null) return;
                precipScroll.x = (precipScroll.x + dt * 0.45f) % 1f;
                gpuSandMat.mainTextureOffset = new Vector2(precipScroll.x, 0f);
                gpuSandMat.color = new Color(1f, 1f, 1f, 0.58f * Mathf.Clamp01(rate));
                // 在地图世界坐标上布置流沙风幔阵
                for (int gz = 40; gz < mapSpanZ; gz += 80)
                    for (int gx = 40; gx < mapSpanX; gx += 80)
                    {
                        Vector3 cellCenter = new Vector3(mapBase.x + gx, groundY, mapBase.z + gz);
                        Graphics.DrawMesh(sandNearMesh, Matrix4x4.TRS(cellCenter + Vector3.up * 6.5f, Quaternion.identity, Vector3.one), gpuSandMat, 0);
                        if (sandFarMesh != null)
                            Graphics.DrawMesh(sandFarMesh, Matrix4x4.TRS(cellCenter + Vector3.up * 9.5f, Quaternion.identity, Vector3.one), gpuSandMat, 0);
                    }
                return;
            }

            // 全地图世界坐标 3D 雨雪体积网格阵列（60m x 60m 世界区块平铺整个地图，100% 固定在世界坐标，零相机坐标依赖）
            float now = Time.realtimeSinceStartup;
            const float tileSpan = 60f;
            if (snowing)
            {
                if (snowVolMesh == null || gpuSnowMat == null) return;
                float fallY = Mathf.Repeat(now * 1.85f, 20f);
                float swayX = Mathf.Sin(now * 1.1f) * 0.75f;
                float swayZ = Mathf.Cos(now * 0.9f) * 0.75f;
                gpuSnowMat.color = new Color(1f, 1f, 1f, 0.88f * Mathf.Clamp01(rate));

                for (float wz = 30f; wz < mapSpanZ; wz += tileSpan)
                    for (float wx = 30f; wx < mapSpanX; wx += tileSpan)
                    {
                        Vector3 tileOrigin = new Vector3(mapBase.x + wx, groundY, mapBase.z + wz);
                        Graphics.DrawMesh(snowVolMesh, Matrix4x4.TRS(tileOrigin + new Vector3(swayX, -fallY, swayZ), Quaternion.identity, Vector3.one), gpuSnowMat, 0);
                        Graphics.DrawMesh(snowVolMesh, Matrix4x4.TRS(tileOrigin + new Vector3(-swayX, 20f - fallY, -swayZ), Quaternion.Euler(0f, 90f, 0f), Vector3.one), gpuSnowMat, 0);
                    }
            }
            else
            {
                if (rainVolMesh == null || gpuRainMat == null) return;
                float fallY = Mathf.Repeat(now * 16.0f, 22f);
                float windYaw = env.windAngle * Mathf.Rad2Deg;
                Quaternion rainRotA = Quaternion.Euler(0f, windYaw, 0f);
                Quaternion rainRotB = Quaternion.Euler(0f, windYaw + 90f, 0f);
                gpuRainMat.color = new Color(1f, 1f, 1f, 0.78f * Mathf.Clamp01(rate));
                if (gpuSplashMat != null)
                    gpuSplashMat.color = new Color(1f, 1f, 1f, 0.65f * Mathf.Clamp01(env.rain));

                for (float wz = 30f; wz < mapSpanZ; wz += tileSpan)
                    for (float wx = 30f; wx < mapSpanX; wx += tileSpan)
                    {
                        Vector3 tileOrigin = new Vector3(mapBase.x + wx, groundY, mapBase.z + wz);
                        Graphics.DrawMesh(rainVolMesh, Matrix4x4.TRS(tileOrigin + new Vector3(0f, -fallY, 0f), rainRotA, Vector3.one), gpuRainMat, 0);
                        Graphics.DrawMesh(rainVolMesh, Matrix4x4.TRS(tileOrigin + new Vector3(0f, 22f - fallY, 0f), rainRotB, Vector3.one), gpuRainMat, 0);
                        if (env.rain > 0.15f && splashMesh != null && gpuSplashMat != null)
                            Graphics.DrawMesh(splashMesh, Matrix4x4.TRS(tileOrigin, Quaternion.identity, Vector3.one), gpuSplashMat, 0);
                    }
            }
        }

        private static void ApplyWeatherFog(Env env)
        {
            float thick = Mathf.Clamp01(env.fog * 0.95f + env.rain * 0.28f + env.snow * 0.22f + env.sand * 0.6f + env.storm * 0.12f);
            var st = FirstPerson3DMod.settings;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;

            float g = Mathf.Clamp01(env.glow);
            var hazeBase = Color.Lerp(new Color(0.08f, 0.11f, 0.19f), new Color(0.78f, 0.82f, 0.88f), g);
            var haze = Color.Lerp(env.horizon, hazeBase, 0.45f);
            if (env.sand > 0.15f) haze = Color.Lerp(haze, new Color(0.78f, 0.60f, 0.36f) * Mathf.Lerp(0.38f, 1f, g), env.sand);
            if (env.snow > 0.15f) haze = Color.Lerp(haze, new Color(0.84f, 0.88f, 0.93f) * Mathf.Lerp(0.40f, 1f, g), 0.55f);
            if (env.storm > 0.3f) haze = new Color(0.12f, 0.16f, 0.25f) * Mathf.Lerp(0.45f, 1f, g);

            RenderSettings.fogColor = Color.Lerp(env.horizon, haze, Mathf.Clamp01(thick * 1.1f));
            float start = thick >= 0.06f ? Mathf.Lerp(54f, 8.0f, thick) : 78f;
            float end = thick >= 0.06f ? Mathf.Lerp(125f, 32f, thick) : 138f;
            if (st != null && st.fogOn) end = Mathf.Min(end, Mathf.Max(20f, st.fogEnd));
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = Mathf.Max(start + 10f, end);
        }

        private static void PaletteOf(int palette, out Color low, out Color high, out Color tree)
        {
            switch (palette)
            {
                case 1:
                    low = new Color(0.58f, 0.46f, 0.30f); high = new Color(0.78f, 0.66f, 0.48f); tree = new Color(0.36f, 0.34f, 0.22f); break;
                case 2:
                    low = new Color(0.52f, 0.58f, 0.62f); high = new Color(0.90f, 0.93f, 0.96f); tree = new Color(0.14f, 0.22f, 0.20f); break;
                case 3:
                    low = new Color(0.12f, 0.26f, 0.18f); high = new Color(0.30f, 0.46f, 0.30f); tree = new Color(0.08f, 0.18f, 0.10f); break;
                default:
                    low = new Color(0.19f, 0.27f, 0.25f); high = new Color(0.42f, 0.47f, 0.45f); tree = new Color(0.10f, 0.20f, 0.12f); break;
            }
        }
    }
}
