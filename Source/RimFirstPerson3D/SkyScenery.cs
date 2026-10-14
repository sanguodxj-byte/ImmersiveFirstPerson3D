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
        private static Material skyMaterial = null;

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
        private static Mesh horizonSkirtMesh;
        private static Mesh rainVolMesh;
        private static Mesh snowVolMesh;
        private static Mesh splashMesh;
        private static Mesh sandNearMesh;
        private static Mesh sandFarMesh;
        private static Mesh activeBoltMeshA;
        private static Mesh activeBoltMeshB;

        // ---- 闪电与雨雪零 GC 复用几何缓冲 ----
        private static readonly List<Vector3> boltVertsBuf = new List<Vector3>(512);
        private static readonly List<Color32> boltColorsBuf = new List<Color32>(512);
        private static readonly List<int> boltTrisBuf = new List<int>(1536);
        private static readonly List<Vector3> precipVertsBuf = new List<Vector3>(4096);
        private static readonly List<Color32> precipColorsBuf = new List<Color32>(4096);
        private static readonly List<int> precipTrisBuf = new List<int>(6144);
        private static readonly List<Vector3> splashVertsBuf = new List<Vector3>(1024);
        private static readonly List<Color32> splashColorsBuf = new List<Color32>(1024);
        private static readonly List<int> splashTrisBuf = new List<int>(1536);

        // ---- 着色器属性 ID 缓存（避免每帧字符串哈希查找）----
        private static readonly int PropSunSize = Shader.PropertyToID("_SunSize");
        private static readonly int PropSunSizeConvergence = Shader.PropertyToID("_SunSizeConvergence");
        private static readonly int PropAtmosphereThickness = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int PropExposure = Shader.PropertyToID("_Exposure");
        private static readonly int PropSkyTint = Shader.PropertyToID("_SkyTint");
        private static readonly int PropGroundColor = Shader.PropertyToID("_GroundColor");
        private static float lastSkyGlow = -1f;
        private static float lastSkyTwilight = -1f;
        private static float lastSkyFog = -1f;
        private static float lastSkyFlash = -1f;
        private static Color lastSkyCol = Color.clear;

        // ---- 纹理与动画缓存 ----
        private static Texture2D seamlessCloudTex;
        private static Texture2D starAtlasTex;
        private static Texture2D sandCurtainTex;
        private static Texture2D auroraRayTex;
        private static Vector2 cloudScroll;
        private static Vector2 precipScroll;
        private static float lastCloudTime = -1f;

        // ---- 大气美化：全闭合顶点色天穹 / 地平线远景大地裙边 / 太阳大气辉光 / 银河星云带 / 极光流光幕 ----
        private static Material gpuSkyDomeMat;      // 360°x180° 全闭合瑞利/米氏散射天穹（不依赖被剥离的 Skybox/Procedural）
        private static Mesh skyDomeMesh;            // 全天球闭合天穹网格（半径 678m，零经度/极点/地平线接缝）
        private static Vector3[] skyDomeDirs;       // 预缓存天穹单位方向向量
        private static Color32[] skyDomeCols;       // 零 GC 顶点色更新数组
        private static float lastDomeGlow = -1f, lastDomeTwilight = -1f, lastDomeFog = -1f, lastDomeFlash = -1f;
        private static Color lastDomeSky = Color.clear, lastDomeHor = Color.clear;
        private static Vector3 lastDomeSunDir = Vector3.zero;
        private static Material gpuHorizonSkirtMat; // 远景大地地平线延展裙边（消除地图边缘漏白与地平线接缝）
        private static Material gpuSunHaloMat;      // 太阳大气米氏前向散射辉光（加法发光）
        private static Material gpuMilkyWayMat;     // 夜间银河星云带（加法发光）
        private static Material gpuAuroraMat;       // 极光流光幕（加法发光 + 磁力线射线纹理滚动）
        private static Mesh milkyWayMesh;           // 沿银河大圆的软带
        private static Mesh auroraMesh;             // 环地平线的S形极光帘
        private static Texture2D coronaTex;         // 太阳辉光径向羽化+柔和光芒束贴图
        private static Texture2D galaxyBandTex;     // 银河带横向全彩星云 + 尘埃暗带贴图

        // ---- 天气切换帧间平滑状态（消除天气切换与昼夜跳变接缝）----
        private static bool hasSmoothedEnv;
        private static Env smoothedEnv;
        private static float lastSmoothTime = -1f;
        private static string lastDebugPreset = "";

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
            EnsureGpuMaterialsAndMeshes();

            var env = SampleCached(Find.CurrentMap);
            CurrentHorizonColor = env.horizon;
            UpdateSkyMaterial(env);
        }

        private static void UpdateSkyMaterial(Env env)
        {
            if (skyMaterial == null) return;
            float g = Mathf.Clamp01(env.glow);
            // 仅当环境参数发生实质变化时才写 Shader 属性，避免静态帧重复触发驱动状态更新
            if (Mathf.Abs(g - lastSkyGlow) < 0.001f
                && Mathf.Abs(env.twilight - lastSkyTwilight) < 0.001f
                && Mathf.Abs(env.fog - lastSkyFog) < 0.001f
                && Mathf.Abs(env.flash - lastSkyFlash) < 0.001f
                && Mathf.Abs(env.sky.r - lastSkyCol.r) + Mathf.Abs(env.sky.g - lastSkyCol.g) + Mathf.Abs(env.sky.b - lastSkyCol.b) < 0.002f)
                return;

            lastSkyGlow = g;
            lastSkyTwilight = env.twilight;
            lastSkyFog = env.fog;
            lastSkyFlash = env.flash;
            lastSkyCol = env.sky;

            // GPU 物理大气散射参数随昼夜动态演进：
            // 令 _GroundColor 与地平线散射色 env.horizon 平滑匹配，彻底消除 Skybox/Procedural 在 y=0 处的天地交界硬线。
            Color dayTint = Color.Lerp(new Color(0.74f, 0.85f, 0.99f), env.sky, 0.68f);
            Color nightTint = new Color(0.024f, 0.048f, 0.125f);
            skyMaterial.SetColor(PropSkyTint, Color.Lerp(nightTint, dayTint, Mathf.Pow(g, 0.75f)));
            skyMaterial.SetColor(PropGroundColor, Color.Lerp(env.horizon, env.midSky, 0.12f));
            skyMaterial.SetFloat(PropAtmosphereThickness,
                Mathf.Lerp(0.42f, Mathf.Lerp(0.96f, 1.52f, env.twilight * 0.80f + env.fog * 0.35f), Mathf.Clamp01(g * 1.25f)));
            skyMaterial.SetFloat(PropExposure, Mathf.Lerp(0.16f, 1.02f, Mathf.Pow(g, 0.68f)) + env.flash * 0.55f);
        }

        public static void Restore()
        {
            lastSkyGlow = -1f;
            hasSmoothedEnv = false;
        }

        private static void EnsureGpuMaterialsAndMeshes()
        {
            if (quadMesh != null) return;
            quadMesh = BuildDoubleQuad();

            // 1.7 Waylith 引擎下 ShaderDatabase.Transparent 强制 _MainTex=Clamp 且开启 depthWrite=true；
            // TransparentPostLight 保持 depthWrite=false 且不覆盖纹理 Repeat 环绕模式，同时支持 _MainTex * _Color * vColor。
            Shader transNoDepth = ShaderDatabase.TransparentPostLight ?? ShaderDatabase.Transparent;
            // 1.7 Waylith 引擎下 WorldOverlayAdditive 不乘顶点色 vColor，MoteGlow 支持加法混合 + depthWrite=false + 顶点色乘子。
            Shader addShader = ShaderDatabase.MoteGlow ?? ShaderDatabase.WorldOverlayAdditive ?? transNoDepth;

            // 1. 无接缝 3D 体积积云穹顶（天顶连续平展映射 + 地平线顶点 Alpha 平滑归零，彻底杜绝星球晨昏斜切线、圆弧接缝与地平线截断线）
            seamlessCloudTex = MakeSeamlessCloudTex(512);
            gpuCloudMat = new Material(transNoDepth)
            { name = "FirstPerson3D_GpuCloudsSeamless", mainTexture = seamlessCloudTex, renderQueue = 1015 };
            gpuCloudMesh = BuildSeamlessCloudDome(72, 32, 540f, 1.45f);

            // 远景大地地平线延展裙边 + 360°x180° 全闭合瑞利/米氏大气散射天穹（不依赖被剥离的 Skybox/Procedural）
            Shader vColShader = ShaderDatabase.VertexColor ?? transNoDepth;
            gpuSkyDomeMat = new Material(vColShader) { name = "FirstPerson3D_SeamlessSkyDome", renderQueue = 1001 };
            skyDomeMesh = BuildSeamlessSkyDomeMesh(64, 40, 678f);
            lastDomeGlow = -1f;

            gpuHorizonSkirtMat = new Material(transNoDepth) { name = "FirstPerson3D_HorizonSkirt", mainTexture = Util3D.WhiteTex, renderQueue = 1018 };
            horizonSkirtMesh = BuildHorizonSkirtMesh(72, 80f, 675f);

            // 2. GPU 日月星辰着色器
            gpuSunMat = new Material(addShader) { name = "FirstPerson3D_GpuSun", renderQueue = 1009 };
            gpuSunMat.mainTexture = MakeSunDiscTex(64, 0.25f);

            gpuMoonGlowMat = new Material(addShader) { name = "FirstPerson3D_GpuMoonGlow", renderQueue = 1007 };
            gpuMoonGlowMat.mainTexture = MakeRadialTex(64, 0.0f, 1.5f);

            gpuMoonMat = new Material(transNoDepth) { name = "FirstPerson3D_GpuMoon", renderQueue = 1008 };
            gpuMoonMat.mainTexture = MakeMoonTex(128);

            starAtlasTex = MakeStarAtlasTex(128);
            gpuStarMat = new Material(transNoDepth) { name = "FirstPerson3D_GpuStars", mainTexture = starAtlasTex, renderQueue = 1005 };
            starMesh = BuildStarDome(1360, 640f);

            // 3. 纯 GPU 加法发光闪电 (支持顶点色渐变羽化)
            gpuLightningMat = new Material(addShader) { name = "FirstPerson3D_GpuLightning", renderQueue = 1025 };
            gpuLightningMat.mainTexture = Util3D.WhiteTex;

            // 4. 屋顶/建模遮挡感知半透明雨雪材质（1.7 Cutout 在 2900，半透明雨雪须排在 2900 之后且关闭 depthWrite）
            gpuRainMat = new Material(transNoDepth) { name = "FirstPerson3D_GpuRainVColor", mainTexture = Util3D.WhiteTex, renderQueue = 2930 };
            gpuSnowMat = new Material(transNoDepth) { name = "FirstPerson3D_GpuSnowVColor", mainTexture = Util3D.WhiteTex, renderQueue = 2930 };
            gpuSplashMat = new Material(transNoDepth) { name = "FirstPerson3D_GpuSplashVColor", mainTexture = Util3D.WhiteTex, renderQueue = 2925 };

            rainVolMesh = new Mesh { name = "FirstPerson3D_DynamicOccludedRain" };
            snowVolMesh = new Mesh { name = "FirstPerson3D_DynamicOccludedSnow" };
            splashMesh = new Mesh { name = "FirstPerson3D_DynamicOccludedSplash" };

            sandCurtainTex = MakeSandCurtainTex(256);
            gpuSandMat = new Material(transNoDepth) { name = "FirstPerson3D_GpuSand", mainTexture = sandCurtainTex, renderQueue = 2928 };
            sandNearMesh = BuildPrecipRing(24, 36.0f, 4.5f, 9.0f, 6.5f, 1.0f, 1.0f, 1, 0f);
            sandFarMesh = BuildPrecipRing(28, 68.0f, 6.5f, 13.0f, 9.5f, 1.0f, 1.0f, 2, 0f);

            // 5. 太阳日冕辉光 + 全彩银河星云带 + 流光极光射线幕
            coronaTex = MakeCoronaTex(128);
            gpuSunHaloMat = new Material(addShader) { name = "FirstPerson3D_GpuSunHalo", mainTexture = coronaTex, renderQueue = 1006 };

            galaxyBandTex = MakeGalaxyBandTex(256, 64);
            gpuMilkyWayMat = new Material(addShader) { name = "FirstPerson3D_GpuMilkyWay", mainTexture = galaxyBandTex, renderQueue = 1004 };
            milkyWayMesh = BuildMilkyWayBand(144, 620f, 0.38f);

            auroraRayTex = MakeAuroraRayTex(128, 64);
            gpuAuroraMat = new Material(addShader) { name = "FirstPerson3D_GpuAurora", mainTexture = auroraRayTex, renderQueue = 1003 };
            auroraMesh = BuildAuroraCurtains(16, 560f, 809);
        }

        // ------------------------------------------------------------
        // 环境采样（含原版 WeatherManager.TransitionLerpFactor 与帧间阻尼平滑）
        // ------------------------------------------------------------

        private static Map cachedEnvMap;
        private static int cachedEnvFrame = -1;
        private static Env cachedEnv;

        /// <summary>当帧该图的环境（带帧缓存与平滑过渡）。光照与天空都从这里取，保证同一个数。</summary>
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
                    if (wm != null)
                    {
                        var curW = wm.curWeather;
                        var lastW = wm.lastWeather ?? curW;
                        float tLerp = Mathf.Clamp01(wm.TransitionLerpFactor);

                        // 优先采用原版已按 TransitionLerpFactor 平滑好的实时雨雪率，并对雾/雷暴/沙尘在 lastWeather 与 curWeather 间线性过渡
                        float curRain = curW != null ? Mathf.Max(curW.rainRate, WeatherFlag(curW, "rain")) : 0f;
                        float lastRain = lastW != null ? Mathf.Max(lastW.rainRate, WeatherFlag(lastW, "rain")) : curRain;
                        float curSnow = curW != null ? Mathf.Max(curW.snowRate, WeatherFlag(curW, "snow")) : 0f;
                        float lastSnow = lastW != null ? Mathf.Max(lastW.snowRate, WeatherFlag(lastW, "snow")) : curSnow;

                        e.rain = Mathf.Clamp01(Mathf.Max(wm.RainRate, Mathf.Lerp(lastRain, curRain, tLerp)));
                        e.snow = Mathf.Clamp01(Mathf.Max(wm.SnowRate, Mathf.Lerp(lastSnow, curSnow, tLerp)));
                        e.fog = Mathf.Lerp(WeatherFlag(lastW, "fog"), WeatherFlag(curW, "fog"), tLerp);
                        e.storm = Mathf.Clamp01(Mathf.Lerp(
                            WeatherFlag(lastW, "storm") + WeatherFlag(lastW, "thunder"),
                            WeatherFlag(curW, "storm") + WeatherFlag(curW, "thunder"), tLerp));
                        e.sand = Mathf.Clamp01(Mathf.Lerp(
                            WeatherFlag(lastW, "sand") + WeatherFlag(lastW, "dust"),
                            WeatherFlag(curW, "sand") + WeatherFlag(curW, "dust"), tLerp));
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
            return SmoothEnvTransition(e);
        }

        // 天气与昼夜帧间平滑器：消除天气切换瞬间的色彩、云量与雾效跳变接缝
        private static Env SmoothEnvTransition(Env target)
        {
            float now = Time.realtimeSinceStartup;
            string curPreset = DebugPreset ?? "";
            if (!hasSmoothedEnv || curPreset != lastDebugPreset || now - lastSmoothTime > 2.0f)
            {
                smoothedEnv = target;
                hasSmoothedEnv = true;
                lastSmoothTime = now;
                lastDebugPreset = curPreset;
                return target;
            }
            float dt = Mathf.Clamp(now - lastSmoothTime, 0f, 0.1f);
            lastSmoothTime = now;
            float k = 1f - Mathf.Exp(-dt * 2.8f);

            smoothedEnv.sky = Color.Lerp(smoothedEnv.sky, target.sky, k);
            smoothedEnv.midSky = Color.Lerp(smoothedEnv.midSky, target.midSky, k);
            smoothedEnv.horizon = Color.Lerp(smoothedEnv.horizon, target.horizon, k);
            smoothedEnv.ground = Color.Lerp(smoothedEnv.ground, target.ground, k);
            smoothedEnv.glow = Mathf.Lerp(smoothedEnv.glow, target.glow, k);
            smoothedEnv.celGlow = Mathf.Lerp(smoothedEnv.celGlow, target.celGlow, k);
            smoothedEnv.twilight = Mathf.Lerp(smoothedEnv.twilight, target.twilight, k);
            smoothedEnv.cloudiness = Mathf.Lerp(smoothedEnv.cloudiness, target.cloudiness, k);
            smoothedEnv.rain = Mathf.Lerp(smoothedEnv.rain, target.rain, k);
            smoothedEnv.snow = Mathf.Lerp(smoothedEnv.snow, target.snow, k);
            smoothedEnv.fog = Mathf.Lerp(smoothedEnv.fog, target.fog, k);
            smoothedEnv.storm = Mathf.Lerp(smoothedEnv.storm, target.storm, k);
            smoothedEnv.sand = Mathf.Lerp(smoothedEnv.sand, target.sand, k);
            smoothedEnv.aurora = Mathf.Lerp(smoothedEnv.aurora, target.aurora, k);
            smoothedEnv.wind = Mathf.Lerp(smoothedEnv.wind, target.wind, k);
            smoothedEnv.windAngle = target.windAngle;
            smoothedEnv.shine = Mathf.Lerp(smoothedEnv.shine, target.shine, k);
            smoothedEnv.flash = target.flash; // 雷暴闪光保持瞬态响应
            smoothedEnv.sunDir = Vector3.Slerp(smoothedEnv.sunDir, target.sunDir, k).normalized;
            smoothedEnv.moonDir = Vector3.Slerp(smoothedEnv.moonDir, target.moonDir, k).normalized;
            smoothedEnv.palette = target.palette;
            return smoothedEnv;
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

        // v8：太阳大气米氏前向散射辉光 + 柔和日冕光芒束（实心渐变，预乘RGB零黑边）
        private static Texture2D MakeCoronaTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_Corona" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    if (r >= 1f) continue;
                    float ang = Mathf.Atan2(dy, dx);
                    float core = Mathf.Pow(Mathf.Clamp01(1f - r), 5.2f);
                    float broad = Mathf.Pow(Mathf.Clamp01(1f - r), 2.0f) * 0.62f;
                    float ray1 = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang * 8f), 3.0f);
                    float ray2 = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(ang * 14f + 0.9f), 4.0f);
                    float rays = (ray1 * 0.22f + ray2 * 0.14f) * Mathf.Pow(1f - r, 1.45f) * SmoothStep01(0.04f, 0.24f, r);
                    float a = Mathf.Clamp01(core + broad + rays);
                    byte b = (byte)Mathf.RoundToInt(a * 255f);
                    px[y * n + x] = new Color32(b, (byte)Mathf.RoundToInt(b * 0.97f), (byte)Mathf.RoundToInt(b * 0.90f), b);
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        // 无接缝球面体积积云贴图（512x256，U 轴经度严格周期 Repeat，V 轴仰角 Clamp 并将地平线消光直接烘焙入 Alpha，零极点奇点、零边界接缝）
        private static Texture2D MakeSeamlessCloudTex(int n)
        {
            int w = n;
            int h = n / 2;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "FirstPerson3D_SeamlessCloud"
            };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1); // 0 地平线 .. 1 天顶
                float phi = (1f - v) * (Mathf.PI * 0.5f);
                float sp = Mathf.Sin(phi), dy = Mathf.Cos(phi);
                // 地平线处 (dy <= 0.08) Alpha 严格为 0，随仰角上升平滑渐显，彻底杜绝地平线云层截断硬边
                float horizonFade = SmoothStep01(0.08f, 0.38f, dy);
                if (horizonFade <= 0.001f) continue;

                float proj = 1.35f / Mathf.Max(0.28f, dy + 0.32f);
                for (int x = 0; x < w; x++)
                {
                    float theta = (x / (float)w) * Mathf.PI * 2f;
                    float dx = sp * Mathf.Cos(theta), dz = sp * Mathf.Sin(theta);
                    float uCoord = dx * proj * 0.35f + 10f;
                    float vCoord = dz * proj * 0.35f + 10f;

                    float macro = PeriodicFbm(uCoord, vCoord, 4, 4, 101);
                    float billow = PeriodicFbm(uCoord * 2f + 0.17f, vCoord * 2f + 0.31f, 4, 4, 211);
                    float fine = PeriodicFbm(uCoord * 4f + 0.41f, vCoord * 4f + 0.19f, 3, 4, 353);
                    float h0 = macro * 0.54f + billow * 0.30f + fine * 0.16f;

                    float hSun = PeriodicFbm(uCoord + 0.025f, vCoord - 0.025f, 4, 4, 101) * 0.65f
                               + PeriodicFbm((uCoord + 0.025f) * 2f + 0.17f, (vCoord - 0.025f) * 2f + 0.31f, 3, 4, 211) * 0.35f;
                    float slope = Mathf.Clamp01((h0 - hSun) * 5.2f + 0.58f);

                    float d = SmoothStep01(0.42f, 0.76f, h0) * horizonFade;
                    if (d <= 0.002f) continue;
                    float shade = Mathf.Lerp(0.76f, 1.0f, slope) * Mathf.Lerp(0.86f, 1.0f, d);
                    byte lum = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Pow(d, 0.85f) * 230f);
                    px[y * w + x] = new Color32(lum, lum, (byte)Mathf.Min(255, lum + 6), alpha);
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        // 无接缝球面经纬云层穹顶（同纬圈顶点 V 恒定，与 MakeSeamlessCloudTex 严格匹配）
        private static Mesh BuildSeamlessCloudDome(int lon, int lat, float radius, float uvScale)
        {
            const float maxPhi = Mathf.PI * 0.50f;
            var verts = new List<Vector3>((lon + 1) * (lat + 1));
            var uvs = new List<Vector2>((lon + 1) * (lat + 1));
            var colors = new List<Color32>((lon + 1) * (lat + 1));
            var tris = new List<int>(lon * lat * 12);
            for (int y = 0; y <= lat; y++)
            {
                float t = y / (float)lat;
                float phi = t * maxPhi;
                float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                float horFade = SmoothStep01(0.06f, 0.36f, cp);
                byte aByte = (byte)Mathf.RoundToInt(horFade * 255f);
                var vc = new Color32(255, 255, 255, aByte);
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    float dx = sp * Mathf.Cos(theta), dz = sp * Mathf.Sin(theta);
                    verts.Add(new Vector3(dx * radius, cp * radius, dz * radius));
                    uvs.Add(new Vector2(u, 1f - t));
                    colors.Add(vc);
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
            var m = new Mesh { name = "FirstPerson3D_SeamlessCloudDome" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // 远景大地地平线延展裙边（内径 innerR 至外径 outerR，封死地图边缘外露出的天空底色裂缝）
        private static readonly List<Color32> skirtColorsBuf = new List<Color32>(300);
        private static int skirtSegsCached;
        private static Color lastSkirtGround = Color.clear;
        private static Color lastSkirtHorizon = Color.clear;

        private static Mesh BuildHorizonSkirtMesh(int segs, float innerR, float outerR)
        {
            skirtSegsCached = segs;
            var verts = new List<Vector3>((segs + 1) * 4);
            var colors = new List<Color32>((segs + 1) * 4);
            var tris = new List<int>(segs * 36);
            float midR1 = Mathf.Lerp(innerR, outerR, 0.25f);
            float midR2 = Mathf.Lerp(innerR, outerR, 0.62f);

            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                verts.Add(new Vector3(cs * innerR, -0.18f, sn * innerR));
                verts.Add(new Vector3(cs * midR1,  -0.14f, sn * midR1));
                verts.Add(new Vector3(cs * midR2,  -0.08f, sn * midR2));
                verts.Add(new Vector3(cs * outerR,  0.00f, sn * outerR));
                colors.Add(new Color32(40, 48, 38, 0));
                colors.Add(new Color32(40, 48, 38, 255));
                colors.Add(new Color32(120, 145, 175, 255));
                colors.Add(new Color32(170, 210, 245, 255));
            }
            for (int i = 0; i < segs; i++)
            {
                int b = i * 4;
                for (int r = 0; r < 3; r++)
                {
                    int v0 = b + r, v1 = b + r + 1, v2 = b + 4 + r, v3 = b + 4 + r + 1;
                    tris.Add(v0); tris.Add(v2); tris.Add(v1);
                    tris.Add(v1); tris.Add(v2); tris.Add(v3);
                    tris.Add(v0); tris.Add(v1); tris.Add(v2);
                    tris.Add(v1); tris.Add(v3); tris.Add(v2);
                }
            }
            var m = new Mesh { name = "FirstPerson3D_HorizonSkirt" };
            m.SetVertices(verts);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static void UpdateHorizonSkirtColors(Env env)
        {
            if (horizonSkirtMesh == null || skirtSegsCached <= 0) return;
            if (Mathf.Abs(env.ground.r - lastSkirtGround.r) + Mathf.Abs(env.ground.g - lastSkirtGround.g) + Mathf.Abs(env.ground.b - lastSkirtGround.b) < 0.004f
                && Mathf.Abs(env.horizon.r - lastSkirtHorizon.r) + Mathf.Abs(env.horizon.g - lastSkirtHorizon.g) + Mathf.Abs(env.horizon.b - lastSkirtHorizon.b) < 0.004f)
                return;
            lastSkirtGround = env.ground;
            lastSkirtHorizon = env.horizon;

            Color cInner = Color.Lerp(env.ground, env.horizon, 0.18f);
            Color cMid = Color.Lerp(env.ground, env.horizon, 0.65f);
            Color cOuter = env.horizon;

            Color32 col0 = new Color32((byte)Mathf.RoundToInt(cInner.r * 255f), (byte)Mathf.RoundToInt(cInner.g * 255f), (byte)Mathf.RoundToInt(cInner.b * 255f), 0);
            Color32 col1 = new Color32((byte)Mathf.RoundToInt(cInner.r * 255f), (byte)Mathf.RoundToInt(cInner.g * 255f), (byte)Mathf.RoundToInt(cInner.b * 255f), 255);
            Color32 col2 = new Color32((byte)Mathf.RoundToInt(cMid.r * 255f), (byte)Mathf.RoundToInt(cMid.g * 255f), (byte)Mathf.RoundToInt(cMid.b * 255f), 255);
            Color32 col3 = new Color32((byte)Mathf.RoundToInt(cOuter.r * 255f), (byte)Mathf.RoundToInt(cOuter.g * 255f), (byte)Mathf.RoundToInt(cOuter.b * 255f), 255);

            skirtColorsBuf.Clear();
            for (int i = 0; i <= skirtSegsCached; i++)
            {
                skirtColorsBuf.Add(col0);
                skirtColorsBuf.Add(col1);
                skirtColorsBuf.Add(col2);
                skirtColorsBuf.Add(col3);
            }
            horizonSkirtMesh.SetColors(skirtColorsBuf);
        }

        // 360°x180° 全闭合瑞利/米氏散射天穹网格（零 UV 边界、零极点挤压、地平线上下半球颜色 100% 连续）
        private static Mesh BuildSeamlessSkyDomeMesh(int lon, int lat, float radius)
        {
            int count = (lon + 1) * (lat + 1);
            var verts = new Vector3[count];
            skyDomeDirs = new Vector3[count];
            skyDomeCols = new Color32[count];
            var tris = new int[lon * lat * 12];

            int idx = 0;
            for (int y = 0; y <= lat; y++)
            {
                float v = y / (float)lat;
                float phi = v * Mathf.PI;
                float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    Vector3 dir = new Vector3(sp * Mathf.Cos(theta), cp, sp * Mathf.Sin(theta));
                    skyDomeDirs[idx] = dir;
                    verts[idx] = dir * radius;
                    skyDomeCols[idx] = new Color32(120, 165, 220, 255);
                    idx++;
                }
            }
            int ti = 0;
            int stride = lon + 1;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * stride + x, b = a + stride;
                    tris[ti++] = a; tris[ti++] = a + 1; tris[ti++] = b;
                    tris[ti++] = a + 1; tris[ti++] = b + 1; tris[ti++] = b;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            var m = new Mesh { name = "FirstPerson3D_SeamlessSkyDome" };
            m.vertices = verts;
            m.colors32 = skyDomeCols;
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        private static void UpdateSkyDomeColors(Env env)
        {
            if (skyDomeMesh == null || skyDomeDirs == null || skyDomeCols == null) return;
            float g = Mathf.Clamp01(env.glow);
            if (Mathf.Abs(g - lastDomeGlow) < 0.001f
                && Mathf.Abs(env.twilight - lastDomeTwilight) < 0.001f
                && Mathf.Abs(env.fog - lastDomeFog) < 0.001f
                && Mathf.Abs(env.flash - lastDomeFlash) < 0.001f
                && (env.sunDir - lastDomeSunDir).sqrMagnitude < 0.0001f
                && Mathf.Abs(env.sky.r - lastDomeSky.r) + Mathf.Abs(env.sky.g - lastDomeSky.g) + Mathf.Abs(env.sky.b - lastDomeSky.b) < 0.002f
                && Mathf.Abs(env.horizon.r - lastDomeHor.r) + Mathf.Abs(env.horizon.g - lastDomeHor.g) + Mathf.Abs(env.horizon.b - lastDomeHor.b) < 0.002f)
                return;

            lastDomeGlow = g;
            lastDomeTwilight = env.twilight;
            lastDomeFog = env.fog;
            lastDomeFlash = env.flash;
            lastDomeSunDir = env.sunDir;
            lastDomeSky = env.sky;
            lastDomeHor = env.horizon;

            Vector3 sun = env.sunDir.normalized;
            Vector2 sunH = new Vector2(sun.x, sun.z);
            if (sunH.sqrMagnitude > 0.0001f) sunH.Normalize();
            float clearFactor = (1f - env.cloudiness * 0.78f) * (1f - env.fog * 0.85f) * (1f - env.sand * 0.85f);
            float sunVis = Mathf.Clamp01((sun.y + 0.10f) / 0.22f) * clearFactor;
            Color sunWarm = Color.Lerp(new Color(1f, 0.97f, 0.90f), new Color(1f, 0.50f, 0.18f), Mathf.Clamp01(1f - sun.y * 1.8f));
            Color flashCol = new Color(0.84f, 0.91f, 1.0f);

            int n = skyDomeDirs.Length;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = skyDomeDirs[i];
                float dy = d.y;
                float k = Mathf.Clamp01(dy);

                // 先计算该方位在 k（当 dy < 0 时 k = 0 即正地平线处）的完整散射颜色，确保 dy = 0 上下半球 100% 严格连续无断层
                Color col = k < 0.42f
                    ? Color.Lerp(env.horizon, env.midSky, Mathf.Pow(k / 0.42f, 0.74f))
                    : Color.Lerp(env.midSky, env.sky, Mathf.Pow((k - 0.42f) / 0.58f, 0.80f));

                float horGlow = Mathf.Exp(-k * k * 42f);
                col = Color.Lerp(col, env.horizon * 1.05f, horGlow * (0.24f + env.fog * 0.35f));

                if (env.twilight > 0.01f)
                {
                    float hLen = Mathf.Max(0.06f, Mathf.Sqrt(d.x * d.x + d.z * d.z));
                    float az = (d.x * sunH.x + d.z * sunH.y) / hLen;
                    float sunSide = Mathf.Pow(Mathf.Clamp01(az * 0.5f + 0.5f), 1.6f);
                    float antiSide = Mathf.Pow(Mathf.Clamp01(-az * 0.5f + 0.5f), 1.5f);
                    float lowBand = Mathf.Exp(-Mathf.Pow(k / 0.26f, 1.6f));
                    float midBand = Mathf.Exp(-Mathf.Pow((k - 0.22f) / 0.22f, 2.0f));
                    col = Color.Lerp(col, env.sunGlowHorizon, env.twilight * sunSide * lowBand * 0.82f);
                    col = Color.Lerp(col, env.sunGlowUpper, env.twilight * sunSide * midBand * 0.50f);
                    col = Color.Lerp(col, env.antiSunHorizon, env.twilight * antiSide * midBand * 0.42f);
                }

                if (sunVis > 0.01f && dy >= -0.05f)
                {
                    float nd = Mathf.Clamp01(d.x * sun.x + Mathf.Max(0f, dy) * sun.y + d.z * sun.z);
                    float mie = Mathf.Pow(nd, 4.5f) * 0.42f * sunVis;
                    col = Color.Lerp(col, sunWarm, mie);
                }

                if (env.flash > 0.02f)
                    col = Color.Lerp(col, flashCol, env.flash * Mathf.Lerp(0.45f, 0.72f, k));

                if (dy < 0f)
                {
                    float kd = SmoothStep01(0f, 0.24f, -dy);
                    col = Color.Lerp(col, env.ground, kd);
                }

                skyDomeCols[i] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(col.r) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(col.g) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(col.b) * 255f),
                    255);
            }
            skyDomeMesh.colors32 = skyDomeCols;
        }

        // v8：高清全彩银河星云带贴图（256x64，含银心暖金星云、翼部深邃蓝紫星云、大裂谷暗尘埃带与亮星团核）
        private static Texture2D MakeGalaxyBandTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_GalaxyBand" };
            var px = new Color32[w * h];
            Color wingBlue = new Color(0.32f, 0.50f, 0.95f);
            Color coreGold = new Color(0.96f, 0.84f, 0.68f);
            Color nebulaRose = new Color(0.90f, 0.45f, 0.76f);

            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;               // -1..1 跨带宽
                float prof = Mathf.Exp(-v * v * 3.1f);            // 高斯软剖面
                float coreProf = Mathf.Exp(-v * v * 8.5f);        // 银道面核心聚光
                float riftMask = Mathf.Exp(-v * v * 16.0f);       // 银道面中央暗尘埃裂谷带
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float cloud = PeriodicFbm(u * 2f, v * 0.45f + 0.5f, 4, 4, 307);
                    float fine = PeriodicFbm(u * 5f + 0.17f, v * 1.4f + 0.31f, 4, 4, 613);
                    float bright = Mathf.Clamp01((cloud * 0.68f + fine * 0.32f - 0.30f) * 1.85f);
                    float dust = SmoothStep01(0.45f, 0.63f, PeriodicFbm(u * 3f + 0.09f, v * 1.1f + 0.22f, 4, 3, 509)) * riftMask;
                    float knot = Mathf.Pow(Mathf.Clamp01((fine - 0.58f) * 2.5f), 2.0f) * coreProf;

                    float intensity = prof * (0.28f + 0.72f * bright) * (1f - dust * 0.72f) + knot * 0.32f * (1f - dust * 0.5f);
                    intensity = Mathf.Clamp01(intensity);

                    Color col = Color.Lerp(wingBlue, coreGold, Mathf.Clamp01(coreProf * 0.75f + cloud * 0.35f));
                    col = Color.Lerp(col, nebulaRose, Mathf.Clamp01(knot * 1.35f));

                    px[y * w + x] = new Color32(
                        (byte)Mathf.RoundToInt(col.r * intensity * 255f),
                        (byte)Mathf.RoundToInt(col.g * intensity * 255f),
                        (byte)Mathf.RoundToInt(col.b * intensity * 255f),
                        (byte)Mathf.RoundToInt(intensity * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        // v8：极光磁力线竖向流光射线贴图（底沿亮氧绿锋面 + 向上丝状射线渐隐）
        private static Texture2D MakeAuroraRayTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_AuroraRay" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h; // 0 底沿 .. 1 顶沿
                float botRise = SmoothStep01(0.0f, 0.16f, v);
                float topFade = Mathf.Pow(Mathf.Clamp01(1f - v), 1.35f);
                float brightRim = Mathf.Exp(-Mathf.Pow((v - 0.18f) / 0.09f, 2.0f)) * 0.45f;
                float vProfile = Mathf.Clamp01(botRise * topFade + brightRim);
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float r1 = PeriodicFbm(u * 4f, v * 0.35f, 3, 4, 809);
                    float r2 = PeriodicFbm(u * 11f + 0.23f, v * 0.20f, 3, 4, 947);
                    float striations = 0.38f + 0.62f * Mathf.Pow(Mathf.Clamp01(r1 * 0.55f + r2 * 0.45f), 1.35f);
                    float val = Mathf.Clamp01(vProfile * striations);
                    byte b = (byte)Mathf.RoundToInt(val * 255f);
                    px[y * w + x] = new Color32(b, b, b, b);
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        // v8：沿银河大圆的软带（含银心方向聚光与地平线大气消光顶点色，消除贴地硬切）
        private static Mesh BuildMilkyWayBand(int segs, float radius, float halfWidth)
        {
            var verts = new List<Vector3>((segs + 1) * 2);
            var uvs = new List<Vector2>((segs + 1) * 2);
            var colors = new List<Color32>((segs + 1) * 2);
            var tris = new List<int>(segs * 12);
            Vector3 galNorm = new Vector3(0.44f, 0.56f, -0.70f).normalized;
            Vector3 galRight = new Vector3(0.78f, -0.15f, 0.60f).normalized;
            Vector3 galUp = Vector3.Cross(galNorm, galRight).normalized;
            for (int i = 0; i <= segs; i++)
            {
                float a = i / (float)segs * Mathf.PI * 2f;
                Vector3 center = (Mathf.Cos(a) * galRight + Mathf.Sin(a) * galUp).normalized;
                Vector3 edgeA = (center + galNorm * halfWidth).normalized;
                Vector3 edgeB = (center - galNorm * halfWidth).normalized;
                verts.Add(edgeA * radius);
                verts.Add(edgeB * radius);
                float u = i / (float)segs * 2.0f;
                uvs.Add(new Vector2(u, 1f));
                uvs.Add(new Vector2(u, 0f));

                // 地平线大气消光 + 银心方向（偏南高空）自然增亮
                float horFade = SmoothStep01(0.02f, 0.24f, center.y);
                float coreBoost = 0.68f + 0.32f * Mathf.Pow(Mathf.Clamp01(Vector3.Dot(center, new Vector3(0.15f, 0.78f, 0.60f).normalized)), 1.5f);
                byte vByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(horFade * coreBoost) * 255f);
                var vc = new Color32(vByte, vByte, vByte, vByte);
                colors.Add(vc);
                colors.Add(vc);
            }
            for (int i = 0; i < segs; i++)
            {
                int b = i * 2;
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 1); tris.Add(b + 3); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            }
            var m = new Mesh { name = "FirstPerson3D_MilkyWayBand" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // v8：环地平线的流光 S 形极光帷幕（下翠绿 -> 中青碧 -> 上紫红，带射线 UV 供 GPU 实时流光滚动）
        private static Mesh BuildAuroraCurtains(int panels, float radius, int seed)
        {
            var rnd = new System.Random(seed);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var tris = new List<int>();
            var cLow = new Color(0.10f, 0.96f, 0.54f);
            var cMid = new Color(0.26f, 0.94f, 0.82f);
            var cHigh = new Color(0.66f, 0.24f, 0.95f);
            for (int i = 0; i < panels; i++)
            {
                float azCenter = (i / (float)panels) * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.14f;
                float halfSpan = 0.13f + (float)rnd.NextDouble() * 0.12f;
                float baseElev = 0.13f + (float)rnd.NextDouble() * 0.07f;
                float topElev = baseElev + 0.34f + (float)rnd.NextDouble() * 0.12f;
                float phase = (float)rnd.NextDouble() * Mathf.PI * 2f;
                const int hCols = 8;
                const int vRows = 6;
                int baseIdx = verts.Count;

                for (int r = 0; r <= vRows; r++)
                {
                    float vt = r / (float)vRows;
                    Color cc = vt < 0.45f
                        ? Color.Lerp(cLow, cMid, vt / 0.45f)
                        : Color.Lerp(cMid, cHigh, (vt - 0.45f) / 0.55f);
                    float vFade = Mathf.Sin(Mathf.Clamp01(vt) * Mathf.PI);
                    for (int c = 0; c <= hCols; c++)
                    {
                        float ut = c / (float)hCols;
                        float hFade = Mathf.Sin(Mathf.Clamp01(ut) * Mathf.PI);
                        float az = azCenter + Mathf.Lerp(-halfSpan, halfSpan, ut)
                            + Mathf.Sin(ut * Mathf.PI * 2f + phase) * 0.025f;
                        float wave = Mathf.Sin(az * 4.0f + phase) * 0.035f + Mathf.Cos(az * 7.0f - phase) * 0.018f;
                        float elev = Mathf.Lerp(baseElev, topElev, vt) + wave;
                        Vector3 dir = new Vector3(
                            Mathf.Cos(az) * Mathf.Cos(elev),
                            Mathf.Sin(elev),
                            Mathf.Sin(az) * Mathf.Cos(elev)).normalized;
                        verts.Add(dir * radius);
                        uvs.Add(new Vector2(ut * 2.0f + i * 0.37f, vt));
                        float w = Mathf.Clamp01(vFade * hFade);
                        colors.Add(new Color32(
                            (byte)Mathf.RoundToInt(cc.r * w * 255f),
                            (byte)Mathf.RoundToInt(cc.g * w * 255f),
                            (byte)Mathf.RoundToInt(cc.b * w * 255f),
                            (byte)Mathf.RoundToInt(w * 240f)));
                    }
                }
                int stride = hCols + 1;
                for (int r = 0; r < vRows; r++)
                    for (int c = 0; c < hCols; c++)
                    {
                        int a = baseIdx + r * stride + c, b = a + stride;
                        tris.Add(a); tris.Add(a + 1); tris.Add(b);
                        tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                        tris.Add(a); tris.Add(b); tris.Add(a + 1);
                        tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                    }
            }
            var m = new Mesh { name = "FirstPerson3D_AuroraCurtains" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------
        // 3D 几何网格构造器（100% 世界坐标系，零相机附着）
        // ------------------------------------------------------------

        private static Mesh BuildStarDome(int count, float radius)
        {
            var rnd = new System.Random(20261015);
            var verts = new List<Vector3>(count * 4);
            var uvs = new List<Vector2>(count * 4);
            var colors = new List<Color32>(count * 4);
            var tris = new List<int>(count * 12);
            Vector3 galNorm = new Vector3(0.44f, 0.56f, -0.70f).normalized;
            Vector3 galRight = new Vector3(0.78f, -0.15f, 0.60f).normalized;
            Vector3 galUp = Vector3.Cross(galNorm, galRight).normalized;

            for (int i = 0; i < count; i++)
            {
                Vector3 dir;
                // 32% 的恒星沿银河银道面聚集，营造真实星野密度梯度
                if (i % 3 == 0)
                {
                    float lon = (float)rnd.NextDouble() * Mathf.PI * 2f;
                    float lat = ((float)rnd.NextDouble() - 0.5f + ((float)rnd.NextDouble() - 0.5f)) * 0.34f;
                    Vector3 ring = Mathf.Cos(lon) * galRight + Mathf.Sin(lon) * galUp;
                    dir = (ring + galNorm * lat).normalized;
                    if (dir.y < 0.03f) dir.y = 0.03f + (float)rnd.NextDouble() * 0.45f;
                    dir.Normalize();
                }
                else
                {
                    float u = (float)rnd.NextDouble();
                    float v = (float)rnd.NextDouble();
                    float theta = u * Mathf.PI * 2f;
                    float phi = Mathf.Acos(1f - v * 0.94f);
                    float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                    dir = new Vector3(sp * Mathf.Cos(theta), cp, sp * Mathf.Sin(theta));
                }
                var p = dir * radius;

                Vector3 up = Vector3.Cross(p, Vector3.up);
                if (up.sqrMagnitude < 0.001f) up = Vector3.Cross(p, Vector3.right);
                up.Normalize();
                Vector3 right = Vector3.Cross(p, up).normalized;

                // 幂律视星等分布：少数璀璨亮星 + 大量深空微星
                float magRoll = (float)rnd.NextDouble();
                int row = magRoll < 0.08f ? 0 : (magRoll < 0.26f ? 1 : (magRoll < 0.58f ? 2 : 3));
                int col = rnd.Next(4);
                float s = row == 0 ? (2.8f + (float)rnd.NextDouble() * 1.6f)
                        : row == 1 ? (2.0f + (float)rnd.NextDouble() * 1.1f)
                        : (1.25f + (float)rnd.NextDouble() * 0.95f);

                // 地平线大气消光：贴近地平线的星辰自然柔和隐没
                float extinction = SmoothStep01(0.02f, 0.22f, dir.y);
                float bright = extinction * (0.75f + 0.25f * (float)rnd.NextDouble());
                byte bCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(bright) * 255f);
                var starCol = new Color32(255, 255, 255, bCol);

                const float pad = 0.01f;
                float u0 = col * 0.25f + pad, u1 = (col + 1) * 0.25f - pad;
                float v0 = row * 0.25f + pad, v1 = (row + 1) * 0.25f - pad;

                int b = verts.Count;
                verts.Add(p - right * s - up * s); verts.Add(p + right * s - up * s);
                verts.Add(p + right * s + up * s); verts.Add(p - right * s + up * s);
                uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u1, v0));
                uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u0, v1));
                colors.Add(starCol); colors.Add(starCol); colors.Add(starCol); colors.Add(starCol);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var m = new Mesh { name = "FirstPerson3D_Stars" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(colors);
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
            var pts = new List<Vector3>(34) { a, b };
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

        // 原位更新纯世界空间 3D 交叉等离子体分形闪电（复用 Mesh 与静态缓冲，雷暴期零 Mesh 销毁重建）
        private static void UpdateGpuAdditiveLightningBolt(ref Mesh targetMesh, Vector3 topWorld, Vector3 botWorld, int seed, float trunkRadius)
        {
            var rnd = new System.Random(seed);
            boltVertsBuf.Clear();
            boltColorsBuf.Clear();
            boltTrisBuf.Clear();

            void AddWorldCrossedSeg(Vector3 p0, Vector3 p1, float r0, float r1)
            {
                Vector3 tangent = p1 - p0;
                if (tangent.sqrMagnitude < 0.0001f) return;
                tangent.Normalize();
                // 节点前后微延伸重叠，消除折线拐角处分叉裂口
                Vector3 ext0 = p0 - tangent * (r0 * 0.32f);
                Vector3 ext1 = p1 + tangent * (r1 * 0.32f);
                Vector3 n1 = Vector3.Cross(tangent, Vector3.forward);
                if (n1.sqrMagnitude < 0.01f) n1 = Vector3.Cross(tangent, Vector3.right);
                n1.Normalize();
                Vector3 n2 = Vector3.Cross(tangent, n1).normalized;

                Color32 cCenter = new Color32(252, 254, 255, 250);
                Color32 cMidGlow = new Color32(135, 198, 255, 145);
                Color32 cOuter = new Color32(95, 150, 255, 0);

                for (int si = 0; si < 2; si++)
                {
                    Vector3 side = si == 0 ? n1 : n2;
                    int b = boltVertsBuf.Count;
                    boltVertsBuf.Add(ext0 - side * (r0 * 0.42f)); boltColorsBuf.Add(cCenter);
                    boltVertsBuf.Add(ext0 + side * (r0 * 0.42f)); boltColorsBuf.Add(cCenter);
                    boltVertsBuf.Add(ext1 + side * (r1 * 0.42f)); boltColorsBuf.Add(cCenter);
                    boltVertsBuf.Add(ext1 - side * (r1 * 0.42f)); boltColorsBuf.Add(cCenter);
                    boltTrisBuf.Add(b); boltTrisBuf.Add(b + 1); boltTrisBuf.Add(b + 2);
                    boltTrisBuf.Add(b); boltTrisBuf.Add(b + 2); boltTrisBuf.Add(b + 3);
                    boltTrisBuf.Add(b); boltTrisBuf.Add(b + 2); boltTrisBuf.Add(b + 1);
                    boltTrisBuf.Add(b); boltTrisBuf.Add(b + 3); boltTrisBuf.Add(b + 2);

                    int bMid = boltVertsBuf.Count;
                    boltVertsBuf.Add(ext0 - side * (r0 * 1.35f)); boltColorsBuf.Add(cMidGlow);
                    boltVertsBuf.Add(ext0 + side * (r0 * 1.35f)); boltColorsBuf.Add(cMidGlow);
                    boltVertsBuf.Add(ext1 + side * (r1 * 1.35f)); boltColorsBuf.Add(cMidGlow);
                    boltVertsBuf.Add(ext1 - side * (r1 * 1.35f)); boltColorsBuf.Add(cMidGlow);
                    boltTrisBuf.Add(bMid); boltTrisBuf.Add(bMid + 1); boltTrisBuf.Add(bMid + 2);
                    boltTrisBuf.Add(bMid); boltTrisBuf.Add(bMid + 2); boltTrisBuf.Add(bMid + 3);
                    boltTrisBuf.Add(bMid); boltTrisBuf.Add(bMid + 2); boltTrisBuf.Add(bMid + 1);
                    boltTrisBuf.Add(bMid); boltTrisBuf.Add(bMid + 3); boltTrisBuf.Add(bMid + 2);

                    int bOuter = boltVertsBuf.Count;
                    boltVertsBuf.Add(ext0 - side * (r0 * 3.1f)); boltColorsBuf.Add(cOuter);
                    boltVertsBuf.Add(ext0 + side * (r0 * 3.1f)); boltColorsBuf.Add(cOuter);
                    boltVertsBuf.Add(ext1 + side * (r1 * 3.1f)); boltColorsBuf.Add(cOuter);
                    boltVertsBuf.Add(ext1 - side * (r1 * 3.1f)); boltColorsBuf.Add(cOuter);
                    boltTrisBuf.Add(bOuter); boltTrisBuf.Add(bOuter + 1); boltTrisBuf.Add(bOuter + 2);
                    boltTrisBuf.Add(bOuter); boltTrisBuf.Add(bOuter + 2); boltTrisBuf.Add(bOuter + 3);
                    boltTrisBuf.Add(bOuter); boltTrisBuf.Add(bOuter + 2); boltTrisBuf.Add(bOuter + 1);
                    boltTrisBuf.Add(bOuter); boltTrisBuf.Add(bOuter + 3); boltTrisBuf.Add(bOuter + 2);
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
            if (targetMesh == null) targetMesh = new Mesh { name = "FirstPerson3D_WorldLightning3D" };
            else targetMesh.Clear();
            targetMesh.SetVertices(boltVertsBuf);
            targetMesh.SetColors(boltColorsBuf);
            targetMesh.SetTriangles(boltTrisBuf, 0);
            targetMesh.RecalculateBounds();
        }

        // 确定性哈希（0..1），用于世界格坐标生成固定雨雪轨迹，零相机附着
        private static float CellHash01(int x, int z, int salt)
        {
            uint h = (uint)(x * 374761393 + z * 668265263 + salt * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 16777215f;
        }

        // 检查世界格是否被屋顶或满格墙体遮挡，返回该格允许落雨的最低高度（被完全遮挡返回 > 9999f）
        private static float GetCellPrecipFloorY(Map map, int gx, int gz, float groundY)
        {
            if (map != null)
            {
                if (gx < 0 || gz < 0 || gx >= map.Size.x || gz >= map.Size.z)
                    return 99999f;
                var c = new IntVec3(gx, 0, gz);
                if (map.roofGrid != null && map.roofGrid.Roofed(c))
                    return 99999f;
                if (map.edificeGrid != null)
                {
                    var ed = map.edificeGrid[c];
                    if (ed != null && ed.def != null)
                    {
                        if (ed.def.Fillage == FillCategory.Full || ed.def.holdsRoof)
                            return 99999f;
                        if (ed.def.fillPercent > 0.05f)
                            return groundY + Mathf.Clamp(ed.def.fillPercent * 1.45f, 0.55f, 1.38f);
                    }
                }
            }
            return groundY + 0.04f;
        }

        // 基于真实世界格与屋顶/建筑遮挡裁剪的细雨丝与水花（杜绝屋内下雨与雨幕穿透 3D 建模）
        private static void UpdateWorldOccludedRainAndSplash(Map map, Vector3 camPos, Vector3 mapBase, float groundY, Env env, Vector2 wind, float now)
        {
            precipVertsBuf.Clear();
            precipColorsBuf.Clear();
            precipTrisBuf.Clear();
            splashVertsBuf.Clear();
            splashColorsBuf.Clear();
            splashTrisBuf.Clear();

            float rate = Mathf.Clamp01(env.rain);
            int camCellX = Mathf.FloorToInt(camPos.x - mapBase.x);
            int camCellZ = Mathf.FloorToInt(camPos.z - mapBase.z);
            const int radius = 24;
            const float colHeight = 14.5f;
            const float fallSpeed = 16.5f;

            // 雨丝颜色融合环境天色与地平线雾色，半透明纤细银蓝雨线（不再是粗白不透明棍棒）
            Color skyTint = Color.Lerp(env.horizon, new Color(0.76f, 0.84f, 0.92f), 0.36f);
            byte rCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(skyTint.r) * 255f);
            byte gCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(skyTint.g) * 255f);
            byte bCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(skyTint.b) * 255f);

            Vector3 axis = new Vector3(wind.x * 0.07f, -1.0f, wind.y * 0.07f).normalized;

            for (int dz = -radius; dz <= radius; dz++)
            {
                int gz = camCellZ + dz;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int distSq = dx * dx + dz * dz;
                    if (distSq > radius * radius) continue;
                    int gx = camCellX + dx;

                    float minY = GetCellPrecipFloorY(map, gx, gz, groundY);
                    if (minY > 9000f) continue;

                    // 距离分级采样：近处每格最多 2 根细雨丝，远处每 2 格 1 根，兼顾细腻度与零卡顿
                    int dropsInCell = distSq <= 90 ? 2 : (((gx + gz) & 1) == 0 ? 1 : 0);
                    if (rate < 0.45f && dropsInCell > 1) dropsInCell = 1;

                    float distFade = 1f - Mathf.Sqrt(distSq) / (radius + 1f);

                    for (int di = 0; di < dropsInCell; di++)
                    {
                        float h0 = CellHash01(gx, gz, di * 7 + 1);
                        float h1 = CellHash01(gx, gz, di * 7 + 2);
                        float h2 = CellHash01(gx, gz, di * 7 + 3);
                        if (h0 > rate * 1.15f + 0.12f) continue;

                        float wx = mapBase.x + gx + 0.14f + h0 * 0.72f;
                        float wz = mapBase.z + gz + 0.14f + h1 * 0.72f;

                        // 沿世界竖直柱下落，在触及地面/家具顶面 minY 时自然截断，绝不向下穿入建模内部
                        float phase = Mathf.Repeat(h2 * colHeight - now * (fallSpeed + h0 * 2.5f), colHeight);
                        float yBot = groundY + phase;
                        float len = 0.68f + h1 * 0.42f;
                        float yTop = yBot + len;

                        if (yTop <= minY + 0.18f) continue;
                        if (yBot < minY) yBot = minY;

                        Vector3 pBot = new Vector3(wx, yBot, wz);
                        Vector3 pTop = pBot - axis * (yTop - yBot);

                        // 检查倾斜雨丝上端是否探入相邻屋顶或墙体格
                        int topGx = Mathf.FloorToInt(pTop.x - mapBase.x);
                        int topGz = Mathf.FloorToInt(pTop.z - mapBase.z);
                        if ((topGx != gx || topGz != gz) && GetCellPrecipFloorY(map, topGx, topGz, groundY) > 9000f)
                            continue;

                        // 单张面向相机的极细雨丝带（半宽仅 5.5~8.5 毫米，总宽约 1.2~1.7 厘米）
                        Vector3 toCam = new Vector3(camPos.x - wx, 0f, camPos.z - wz);
                        Vector3 side = toCam.sqrMagnitude > 0.001f
                            ? new Vector3(-toCam.z, 0f, toCam.x).normalized
                            : Vector3.right;
                        float hw = 0.0055f + h2 * 0.0030f;

                        // 轻盈高透：头部 alpha 仅 22~36（约 9%~14% 不透明度），尾部渐隐至 0
                        byte aHead = (byte)Mathf.RoundToInt(Mathf.Clamp(rate, 0.25f, 1f) * distFade * (22f + h2 * 14f));
                        if (aHead < 4) continue;
                        Color32 cHead = new Color32(rCol, gCol, bCol, aHead);
                        Color32 cTail = new Color32(rCol, gCol, bCol, 0);

                        int b = precipVertsBuf.Count;
                        precipVertsBuf.Add(pBot - side * hw); precipColorsBuf.Add(cHead);
                        precipVertsBuf.Add(pBot + side * hw); precipColorsBuf.Add(cHead);
                        precipVertsBuf.Add(pTop + side * hw); precipColorsBuf.Add(cTail);
                        precipVertsBuf.Add(pTop - side * hw); precipColorsBuf.Add(cTail);

                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 1); precipTrisBuf.Add(b + 2);
                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 2); precipTrisBuf.Add(b + 3);
                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 2); precipTrisBuf.Add(b + 1);
                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 3); precipTrisBuf.Add(b + 2);
                    }

                    // 露天且无家具遮挡的地面水花微涟漪（半透明轻柔闪烁）
                    if (distSq <= 196 && minY <= groundY + 0.08f && ((gx * 3 + gz * 5) & 3) == 0)
                    {
                        float sh = CellHash01(gx, gz, 91);
                        float pulse = Mathf.Repeat(now * 2.6f + sh * 5f, 1f);
                        float sAlpha = Mathf.Sin(pulse * Mathf.PI) * rate * distFade;
                        byte aSplash = (byte)Mathf.RoundToInt(sAlpha * 24f);
                        if (aSplash >= 4)
                        {
                            float sx = mapBase.x + gx + 0.25f + sh * 0.5f;
                            float sz = mapBase.z + gz + 0.25f + CellHash01(gx, gz, 92) * 0.5f;
                            float sr = Mathf.Lerp(0.04f, 0.11f, pulse);
                            float sy = groundY + 0.03f;
                            Color32 cCenter = new Color32(rCol, gCol, bCol, aSplash);
                            Color32 cEdge = new Color32(rCol, gCol, bCol, 0);
                            int sb = splashVertsBuf.Count;
                            splashVertsBuf.Add(new Vector3(sx - sr, sy, sz - sr)); splashColorsBuf.Add(cEdge);
                            splashVertsBuf.Add(new Vector3(sx + sr, sy, sz - sr)); splashColorsBuf.Add(cEdge);
                            splashVertsBuf.Add(new Vector3(sx + sr, sy, sz + sr)); splashColorsBuf.Add(cCenter);
                            splashVertsBuf.Add(new Vector3(sx - sr, sy, sz + sr)); splashColorsBuf.Add(cCenter);
                            splashTrisBuf.Add(sb); splashTrisBuf.Add(sb + 1); splashTrisBuf.Add(sb + 2);
                            splashTrisBuf.Add(sb); splashTrisBuf.Add(sb + 2); splashTrisBuf.Add(sb + 3);
                            splashTrisBuf.Add(sb); splashTrisBuf.Add(sb + 2); splashTrisBuf.Add(sb + 1);
                            splashTrisBuf.Add(sb); splashTrisBuf.Add(sb + 3); splashTrisBuf.Add(sb + 2);
                        }
                    }
                }
            }

            if (rainVolMesh == null) rainVolMesh = new Mesh { name = "FirstPerson3D_WorldOccludedRain" };
            else rainVolMesh.Clear();
            if (precipVertsBuf.Count > 0)
            {
                rainVolMesh.SetVertices(precipVertsBuf);
                rainVolMesh.SetColors(precipColorsBuf);
                rainVolMesh.SetTriangles(precipTrisBuf, 0);
                rainVolMesh.RecalculateBounds();
            }

            if (splashMesh == null) splashMesh = new Mesh { name = "FirstPerson3D_WorldOccludedSplash" };
            else splashMesh.Clear();
            if (splashVertsBuf.Count > 0)
            {
                splashMesh.SetVertices(splashVertsBuf);
                splashMesh.SetColors(splashColorsBuf);
                splashMesh.SetTriangles(splashTrisBuf, 0);
                splashMesh.RecalculateBounds();
            }
        }

        // 基于真实世界格与屋顶/建筑遮挡裁剪的柔雪花
        private static void UpdateWorldOccludedSnow(Map map, Vector3 camPos, Vector3 mapBase, float groundY, Env env, float now)
        {
            precipVertsBuf.Clear();
            precipColorsBuf.Clear();
            precipTrisBuf.Clear();

            float rate = Mathf.Clamp01(env.snow);
            int camCellX = Mathf.FloorToInt(camPos.x - mapBase.x);
            int camCellZ = Mathf.FloorToInt(camPos.z - mapBase.z);
            const int radius = 22;
            const float colHeight = 12.0f;
            const float fallSpeed = 1.65f;

            Color snowTint = Color.Lerp(env.horizon, new Color(0.92f, 0.95f, 1.0f), 0.55f);
            byte rCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(snowTint.r) * 255f);
            byte gCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(snowTint.g) * 255f);
            byte bCol = (byte)Mathf.RoundToInt(Mathf.Clamp01(snowTint.b) * 255f);

            for (int dz = -radius; dz <= radius; dz++)
            {
                int gz = camCellZ + dz;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int distSq = dx * dx + dz * dz;
                    if (distSq > radius * radius) continue;
                    int gx = camCellX + dx;

                    float minY = GetCellPrecipFloorY(map, gx, gz, groundY);
                    if (minY > 9000f) continue;

                    float distFade = 1f - Mathf.Sqrt(distSq) / (radius + 1f);
                    int flakes = distSq <= 81 ? 2 : 1;
                    for (int fi = 0; fi < flakes; fi++)
                    {
                        float h0 = CellHash01(gx, gz, fi * 11 + 31);
                        float h1 = CellHash01(gx, gz, fi * 11 + 32);
                        float h2 = CellHash01(gx, gz, fi * 11 + 33);
                        if (h0 > rate * 1.15f + 0.1f) continue;

                        float y = groundY + Mathf.Repeat(h2 * colHeight - now * (fallSpeed + h0 * 0.45f), colHeight);
                        if (y < minY + 0.08f) continue;

                        float swayX = Mathf.Sin(now * 1.2f + h0 * 6.28f) * 0.18f;
                        float swayZ = Mathf.Cos(now * 0.95f + h1 * 6.28f) * 0.18f;
                        float wx = mapBase.x + gx + 0.2f + h0 * 0.6f + swayX;
                        float wz = mapBase.z + gz + 0.2f + h1 * 0.6f + swayZ;
                        float r = 0.028f + h2 * 0.024f;

                        Vector3 toCam = new Vector3(camPos.x - wx, 0f, camPos.z - wz);
                        Vector3 side = toCam.sqrMagnitude > 0.001f
                            ? new Vector3(-toCam.z, 0f, toCam.x).normalized
                            : Vector3.right;

                        byte aCenter = (byte)Mathf.RoundToInt(Mathf.Clamp(rate, 0.25f, 1f) * distFade * 95f);
                        if (aCenter < 6) continue;
                        Color32 cCenter = new Color32(rCol, gCol, bCol, aCenter);
                        Color32 cEdge = new Color32(rCol, gCol, bCol, 0);

                        int b = precipVertsBuf.Count;
                        precipVertsBuf.Add(new Vector3(wx, y - r, wz) - side * r); precipColorsBuf.Add(cEdge);
                        precipVertsBuf.Add(new Vector3(wx, y - r, wz) + side * r); precipColorsBuf.Add(cEdge);
                        precipVertsBuf.Add(new Vector3(wx, y + r, wz) + side * r); precipColorsBuf.Add(cCenter);
                        precipVertsBuf.Add(new Vector3(wx, y + r, wz) - side * r); precipColorsBuf.Add(cCenter);
                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 1); precipTrisBuf.Add(b + 2);
                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 2); precipTrisBuf.Add(b + 3);
                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 2); precipTrisBuf.Add(b + 1);
                        precipTrisBuf.Add(b); precipTrisBuf.Add(b + 3); precipTrisBuf.Add(b + 2);
                    }
                }
            }

            if (snowVolMesh == null) snowVolMesh = new Mesh { name = "FirstPerson3D_WorldOccludedSnow" };
            else snowVolMesh.Clear();
            if (precipVertsBuf.Count > 0)
            {
                snowVolMesh.SetVertices(precipVertsBuf);
                snowVolMesh.SetColors(precipColorsBuf);
                snowVolMesh.SetTriangles(precipTrisBuf, 0);
                snowVolMesh.RecalculateBounds();
            }
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

            // 0. 360°x180° 全闭合瑞利/米氏散射天穹底衬（renderQueue = 1001，零接缝覆盖整个天空球面）
            if (skyDomeMesh != null && gpuSkyDomeMat != null)
            {
                UpdateSkyDomeColors(env);
                gpuSkyDomeMat.color = Color.white;
                Graphics.DrawMesh(skyDomeMesh, Matrix4x4.TRS(mapOrigin, Quaternion.identity, Vector3.one), gpuSkyDomeMat, 0);
            }

            if (starMesh != null && gpuStarMat != null && env.glow < 0.58f)
            {
                float a = Mathf.Clamp01((0.58f - env.glow) / 0.46f) * (1f - env.cloudiness * 0.65f) * (1f - env.fog);
                gpuStarMat.color = new Color(1f, 1f, 1f, a * 0.98f);
                if (a > 0.02f)
                    Graphics.DrawMesh(starMesh, Matrix4x4.TRS(mapOrigin, Quaternion.identity, Vector3.one), gpuStarMat, 0);
            }

            // 夜间全彩银河星云带（仅暗天空可见，云/雾/沙尘遮挡）
            if (milkyWayMesh != null && gpuMilkyWayMat != null)
            {
                float mwA = Mathf.Clamp01((0.34f - env.glow) / 0.28f)
                    * (1f - env.cloudiness * 0.75f) * (1f - env.fog) * (1f - env.sand);
                if (mwA > 0.02f)
                {
                    gpuMilkyWayMat.color = new Color(0.86f, 0.90f, 1.0f, 1f) * (mwA * 0.92f);
                    Graphics.DrawMesh(milkyWayMesh, Matrix4x4.TRS(mapOrigin, Quaternion.identity, Vector3.one), gpuMilkyWayMat, 0);
                }
            }

            // 极光幕（仅极光天气事件的夜晚可见，带磁力线射线实时流光滚动）
            if (auroraMesh != null && gpuAuroraMat != null && env.aurora > 0.05f)
            {
                float nightVis = Mathf.Clamp01((0.34f - env.glow) / 0.28f)
                    * (1f - env.cloudiness * 0.78f) * (1f - env.fog * 0.85f);
                float aAmt = env.aurora * nightVis;
                if (aAmt > 0.02f)
                {
                    float pulse = 0.88f + 0.12f * Mathf.Sin(now * 1.35f);
                    gpuAuroraMat.mainTextureOffset = new Vector2((now * 0.032f) % 1f, 0f);
                    gpuAuroraMat.color = new Color(1f, 1f, 1f, aAmt * 0.92f * pulse);
                    Graphics.DrawMesh(auroraMesh, Matrix4x4.TRS(mapOrigin, Quaternion.identity, Vector3.one), gpuAuroraMat, 0);
                }
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
                var p = mapOrigin + env.sunDir * skyDist;
                var look = Quaternion.LookRotation(env.sunDir, up);

                // 太阳大气米氏前向散射辉光（低仰角自动过渡为琥珀金红）
                if (gpuSunHaloMat != null)
                {
                    var haloCol = Color.Lerp(new Color(1f, 0.96f, 0.86f), new Color(1f, 0.50f, 0.16f), low);
                    float ha = 0.68f * vis * Mathf.Max(0.55f, env.shine);
                    gpuSunHaloMat.color = new Color(haloCol.r, haloCol.g, haloCol.b, ha);
                    float haloSize = (42f + 26f * low) * scaleRatio;
                    Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(p, look, Vector3.one * haloSize), gpuSunHaloMat, 0);
                }

                gpuSunMat.color = new Color(1f, 1f, 1f, 0.99f * vis);
                float disc = (14.5f + 6.5f * low) * scaleRatio;
                Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(p, look, Vector3.one * disc), gpuSunMat, 0);
            }

            if (gpuMoonMat == null || env.glow > 0.45f || env.fog > 0.55f) return;
            float ma = Mathf.Clamp01((0.45f - env.glow) / 0.42f) * (1f - env.cloudiness * 0.70f);
            if (ma < 0.02f) return;
            var mp = mapOrigin + env.moonDir * 610f;
            Vector3 mUp = Mathf.Abs(env.moonDir.y) > 0.92f ? Vector3.forward : Vector3.up;
            var mlook = Quaternion.LookRotation(env.moonDir, mUp);
            if (gpuMoonGlowMat != null)
            {
                gpuMoonGlowMat.color = new Color(0.62f, 0.78f, 1.0f, 0.50f * ma);
                Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(mp, mlook, Vector3.one * (34f * scaleRatio)), gpuMoonGlowMat, 0);
            }
            gpuMoonMat.color = new Color(0.95f, 0.98f, 1.0f, 0.99f * ma);
            Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(mp, mlook, Vector3.one * (12.0f * scaleRatio)), gpuMoonMat, 0);
        }

        private static void DrawClouds(Vector3 mapOrigin, Env env, Vector2 wind, float dt)
        {
            if (gpuCloudMat == null || gpuCloudMesh == null) return;
            if (env.sand > 0.65f) return;

            // 确保禁用原版 World/Clouds 的轨道晨昏分界线（消除横贯天幕的橙黑斜向割裂带）
            Shader.SetGlobalFloat(ShaderPropertyIDs.PlanetSunLightEnabled, 0f);

            cloudScroll.x = (cloudScroll.x + wind.x * dt * 0.0024f) % 1f;
            cloudScroll.y = (cloudScroll.y + wind.y * dt * 0.0024f) % 1f;

            float dayFactor = Mathf.Clamp01(env.glow * 1.25f);
            bool clearNight = env.glow < 0.22f && env.rain < 0.1f && env.snow < 0.1f && env.storm < 0.1f;
            float cloudOp = Mathf.Clamp01(env.cloudiness * 0.78f) * Mathf.Lerp(clearNight ? 0.12f : 0.28f, 0.68f, dayFactor);
            if (env.storm > 0.2f) cloudOp = Mathf.Max(cloudOp, 0.62f);
            if (env.rain > 0.2f) cloudOp = Mathf.Max(cloudOp, 0.54f);
            if (cloudOp < 0.02f) return;

            Color cloudTint = Color.Lerp(new Color(0.24f, 0.30f, 0.44f), Color.Lerp(env.horizon, Color.white, 0.62f), dayFactor);
            if (env.twilight > 0.04f)
                cloudTint = Color.Lerp(cloudTint, new Color(1.0f, 0.74f, 0.50f), env.twilight * 0.58f);
            if (env.storm > 0.25f)
                cloudTint = Color.Lerp(cloudTint, new Color(0.36f, 0.40f, 0.48f), env.storm * 0.50f);
            if (env.flash > 0.05f)
                cloudTint = Color.Lerp(cloudTint, new Color(0.88f, 0.94f, 1.0f), Mathf.Clamp01(env.flash * 0.75f));

            gpuCloudMat.mainTextureOffset = new Vector2(cloudScroll.x, 0f);
            gpuCloudMat.color = new Color(cloudTint.r, cloudTint.g, cloudTint.b, cloudOp);
            Graphics.DrawMesh(gpuCloudMesh, Matrix4x4.TRS(mapOrigin, Quaternion.identity, Vector3.one), gpuCloudMat, 0);
        }

        private static void DrawLightning(Vector3 mapOrigin, Env env)
        {
            if (gpuLightningMat == null) return;
            float now = Time.realtimeSinceStartup;
            bool inStorm = env.storm > 0.45f || env.flash > 0.06f;
            if (!inStorm) return;

            // 在地图世界坐标中生成瞬态 3D 雷击（100% 真实世界坐标，复用静态缓冲与 Mesh，零 GC）
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

                Vector3 topA = new Vector3(strikeWorldXZ_A.x, mapOrigin.y + 38f, strikeWorldXZ_A.y);
                Vector3 botA = new Vector3(strikeWorldXZ_A.x + 2.5f, mapOrigin.y, strikeWorldXZ_A.y - 1.8f);
                Vector3 topB = new Vector3(strikeWorldXZ_B.x, mapOrigin.y + 39f, strikeWorldXZ_B.y);
                Vector3 botB = new Vector3(strikeWorldXZ_B.x - 2.2f, mapOrigin.y, strikeWorldXZ_B.y + 2.0f);
                UpdateGpuAdditiveLightningBolt(ref activeBoltMeshA, topA, botA, strikeSeedCounter, 0.92f);
                UpdateGpuAdditiveLightningBolt(ref activeBoltMeshB, topB, botB, strikeSeedCounter + 305, 0.78f);
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
            bool snowing = env.snow > 0.12f && env.snow >= env.rain * 0.5f;
            bool sanding = !snowing && env.sand > env.rain && env.sand > 0.2f;
            float rate = snowing ? env.snow : (sanding ? env.sand : env.rain);
            if (rate < 0.06f) return;

            float groundY = mapOrigin.y;
            int mapSpanX = map != null ? map.Size.x : 200;
            int mapSpanZ = map != null ? map.Size.z : 200;
            Vector3 mapBase = map != null ? Streaming3D.OffsetOf(map) : Vector3.zero;
            Vector3 camPos = CameraRig3D.LastCamPos;

            if (sanding)
            {
                if (gpuSandMat == null || sandNearMesh == null) return;
                precipScroll.x = (precipScroll.x + dt * 0.45f) % 1f;
                gpuSandMat.mainTextureOffset = new Vector2(precipScroll.x, 0f);
                gpuSandMat.color = new Color(1f, 1f, 1f, 0.52f * Mathf.Clamp01(rate));
                for (int gz = 40; gz < mapSpanZ; gz += 80)
                    for (int gx = 40; gx < mapSpanX; gx += 80)
                    {
                        Vector3 cellCenter = new Vector3(mapBase.x + gx, groundY, mapBase.z + gz);
                        float dx = cellCenter.x - camPos.x, dz = cellCenter.z - camPos.z;
                        if (dx * dx + dz * dz > 145f * 145f) continue;
                        if (!Frustum3D.Visible(new Bounds(cellCenter + Vector3.up * 8f, new Vector3(88f, 20f, 88f)))) continue;
                        Graphics.DrawMesh(sandNearMesh, Matrix4x4.TRS(cellCenter + Vector3.up * 6.5f, Quaternion.identity, Vector3.one), gpuSandMat, 0);
                        if (sandFarMesh != null)
                            Graphics.DrawMesh(sandFarMesh, Matrix4x4.TRS(cellCenter + Vector3.up * 9.5f, Quaternion.identity, Vector3.one), gpuSandMat, 0);
                    }
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (snowing)
            {
                if (gpuSnowMat == null) return;
                UpdateWorldOccludedSnow(map, camPos, mapBase, groundY, env, now);
                if (snowVolMesh != null && snowVolMesh.vertexCount > 0)
                {
                    gpuSnowMat.color = Color.white;
                    Graphics.DrawMesh(snowVolMesh, Matrix4x4.identity, gpuSnowMat, 0);
                }
            }
            else
            {
                if (gpuRainMat == null) return;
                UpdateWorldOccludedRainAndSplash(map, camPos, mapBase, groundY, env, wind, now);
                if (rainVolMesh != null && rainVolMesh.vertexCount > 0)
                {
                    gpuRainMat.color = Color.white;
                    Graphics.DrawMesh(rainVolMesh, Matrix4x4.identity, gpuRainMat, 0);
                }
                if (splashMesh != null && splashMesh.vertexCount > 0 && gpuSplashMat != null)
                {
                    gpuSplashMat.color = Color.white;
                    Graphics.DrawMesh(splashMesh, Matrix4x4.identity, gpuSplashMat, 0);
                }
            }
        }

        private static void ApplyWeatherFog(Env env)
        {
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
