// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 天空盒与远景
    // ============================================================
    //
    // 组成（全部程序化生成，不引入外部资源）：
    //   1. 穹顶：相机为中心的球。天顶是蓝，乘上原版 CurSky 的天气色；
    //      朝太阳的一侧烘出暖光。晴天 CurSky 几乎是白色，不能直接拿来当天空色。
    //   2. 太阳 + 光晕，夜间对侧一颗弱月。方向来自 GenCelestial。
    //   3. 两层云冠：包住上半球，贴图只沿风向滚动。云量跟着雨/雪/雾/雷暴过渡。
    //   4. 星空：夜间淡入。
    //   5. 雨丝或雪花围在相机周围；浓雾、扬沙会收紧线性雾。
    // 原版天气绘制仍由 VanillaSuppressor 关掉，这里只做第一人称里看得见的那一层。
    public static class SkyScenery
    {
        // ---- 原版天空盒接管/还原 ----
        private static Skybox cameraSkybox;
        private static Material originalMaterial;
        private static bool originalEnabled;
        private static bool skyboxAddedByMod;
        private static bool captured;
        private static Material skyMaterial;

        // ---- 穹顶 ----
        private static Material domeMaterial;
        private static Texture2D domeTexture;
        private static Mesh domeMesh;

        // ---- 太阳 / 云 / 星 ----
        private static Material sunMat, haloMat, cloudMat, cloudMatFar, starMat, moonMat;
        private static Material rainMat, snowMat;
        private static Mesh quadMesh, cloudMesh, starMesh, precipMesh;
        private static Texture2D cloudTex, sunTex;
        private static Vector2 cloudScroll, precipScroll;
        private static float lastCloudTime = -1f;

        public static bool HasNativeSkybox => skyMaterial != null;

        /// <summary>当帧环境采样：天空色、地面色、日照强度、太阳方向、云量、配色。</summary>
        private struct Env
        {
            public Color sky;      // 天顶
            public Color horizon;  // 地平线
            public Color ground;   // 地面远色
            public float glow;     // 0 夜 .. 1 正午（CurSky，已含天气压光）
            public float flash;    // 闪电把天空突然抬亮的部分
            public Vector3 sunDir; // 指向太阳的单位向量（世界）
            public float cloudiness;
            public float rain, snow, sand, fog, storm, wind, windAngle;
            public float shine;
            public Color overlay;
            public int palette;    // 0 温带 1 沙漠 2 苔原/雪 3 丛林
        }

        // ------------------------------------------------------------
        // 生命周期
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
                    skyMaterial = new Material(shader) { name = "FirstPerson3D_ProceduralSky" };
                    // 程序化天空盒自带的太阳跟场景方向光走，环世界里经常偏到一边。太阳由我们自己画。
                    if (skyMaterial.HasProperty("_SunSize")) skyMaterial.SetFloat("_SunSize", 0f);
                    if (skyMaterial.HasProperty("_AtmosphereThickness")) skyMaterial.SetFloat("_AtmosphereThickness", 1.15f);
                    if (skyMaterial.HasProperty("_Exposure")) skyMaterial.SetFloat("_Exposure", 1f);
                }
            }
            EnsureDome();
            EnsureCelestial();

            var env = Sample(Find.CurrentMap);
            if (skyMaterial != null)
            {
                if (skyMaterial.HasProperty("_SkyTint")) skyMaterial.SetColor("_SkyTint", Color.Lerp(Color.white, env.sky, 0.6f));
                if (skyMaterial.HasProperty("_GroundColor")) skyMaterial.SetColor("_GroundColor", Color.Lerp(new Color(0.25f, 0.27f, 0.24f), env.ground, 0.35f));
                cameraSkybox.material = skyMaterial;
                cameraSkybox.enabled = true;
            }
            if (DomeNeedsUpdate(env)) UpdateDomeTexture(env);
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
            captured = false;
            cameraSkybox = null;
            originalMaterial = null;
            skyboxAddedByMod = false;
        }

        // ------------------------------------------------------------
        // 环境采样
        // ------------------------------------------------------------

        private static Env Sample(Map map)
        {
            var e = new Env
            {
                sky = new Color(0.32f, 0.52f, 0.88f),
                horizon = new Color(0.64f, 0.78f, 0.92f),
                ground = new Color(0.16f, 0.18f, 0.16f),
                overlay = new Color(1f, 1f, 1f),
                glow = 1f,
                shine = 1f,
                sunDir = new Vector3(0.3f, 0.8f, 0.5f).normalized,
                cloudiness = 0.18f,
                wind = 0.4f,
                palette = 0
            };
            if (map == null) return e;

            Color skyMul = Color.white;
            try
            {
                if (map.skyManager != null)
                {
                    var cur = map.skyManager.CurSky;
                    skyMul = cur.colors.sky;
                    e.overlay = cur.colors.overlay;
                    e.ground = cur.colors.shadow;
                    e.glow = Mathf.Clamp01(cur.glow);
                    e.shine = Mathf.Clamp(cur.lightsourceShineIntensity, 0f, 2f);
                    float cel = 0f;
                    try { cel = GenCelestial.CurCelestialSunGlow(map); } catch { }
                    e.flash = Mathf.Clamp01(e.glow - cel);
                }
            }
            catch { }

            try
            {
                Vector3 sun = GenCelestial.CurSunPositionInWorldSpace();
                if (sun.sqrMagnitude > 0.0001f) e.sunDir = sun.normalized;
            }
            catch { }

            try
            {
                var wm = map.weatherManager;
                if (wm != null)
                {
                    float t = wm.TransitionLerpFactor;
                    e.rain = wm.RainRate;
                    e.snow = wm.SnowRate;
                    try { e.sand = wm.SandRate; } catch { }
                    e.fog = Mathf.Lerp(WeatherFlag(wm.lastWeather, "fog"), WeatherFlag(wm.curWeather, "fog"), t);
                    e.storm = Mathf.Lerp(WeatherFlag(wm.lastWeather, "thunder"), WeatherFlag(wm.curWeather, "thunder"), t);
                    e.wind = wm.CurWindSpeedFactor;
                    try
                    {
                        if (map.windManager != null) e.wind = map.windManager.WindSpeed;
                    }
                    catch { }
                    e.windAngle = (map.uniqueID % 360) * Mathf.Deg2Rad;
                }
            }
            catch { }

            // 雪天的 rainRate 也是 1，云量只取更湿的那一项，避免叠两次。
            float wet = e.snow > 0.2f ? Mathf.Max(e.snow, e.rain * 0.35f) : e.rain;
            e.cloudiness = Mathf.Clamp01(0.16f + wet * 0.62f + e.fog * 0.7f + e.sand * 0.4f + e.storm * 0.28f);

            float g = Mathf.Clamp01(e.glow);
            var dayZenith = new Color(0.30f, 0.50f, 0.86f);
            var nightZenith = new Color(0.015f, 0.02f, 0.06f);
            var dayHorizon = new Color(0.62f, 0.78f, 0.94f);
            var nightHorizon = new Color(0.05f, 0.07f, 0.12f);
            e.sky = Color.Lerp(nightZenith, dayZenith, g) * skyMul;
            e.horizon = Color.Lerp(nightHorizon, dayHorizon, Mathf.Clamp01(g * 1.25f));
            float sunLow = 1f - Mathf.Clamp01(e.sunDir.y * 2.4f);
            e.horizon = Color.Lerp(e.horizon, new Color(0.98f, 0.55f, 0.28f) * skyMul, sunLow * 0.72f * g);
            var overcast = new Color(0.50f, 0.54f, 0.60f) * Mathf.Lerp(0.28f, 1f, g);
            e.sky = Color.Lerp(e.sky, overcast, e.cloudiness * 0.72f);
            e.horizon = Color.Lerp(e.horizon, overcast, e.cloudiness * 0.55f + e.fog * 0.35f);
            if (e.fog > 0.05f)
            {
                var haze = new Color(0.74f, 0.77f, 0.80f) * Mathf.Lerp(0.35f, 1f, g);
                e.sky = Color.Lerp(e.sky, haze, e.fog * 0.65f);
                e.horizon = Color.Lerp(e.horizon, haze, e.fog * 0.8f);
            }
            if (e.sand > 0.05f)
            {
                var dust = new Color(0.72f, 0.55f, 0.32f) * Mathf.Lerp(0.4f, 1f, g);
                e.sky = Color.Lerp(e.sky, dust, e.sand * 0.45f);
                e.horizon = Color.Lerp(e.horizon, dust, e.sand * 0.65f);
            }
            e.ground = Color.Lerp(new Color(0.04f, 0.05f, 0.05f), e.ground, 0.35f) * Mathf.Lerp(0.4f, 1f, g);
            if (e.flash > 0.02f)
            {
                var bolt = new Color(0.85f, 0.90f, 1f);
                e.sky = Color.Lerp(e.sky, bolt, e.flash);
                e.horizon = Color.Lerp(e.horizon, bolt, e.flash * 0.65f);
            }

            try
            {
                string b = map.Biome != null ? map.Biome.defName.ToLowerInvariant() : "";
                if (b.Contains("desert") || b.Contains("arid")) e.palette = 1;
                else if (b.Contains("tundra") || b.Contains("ice") || b.Contains("boreal")) e.palette = 2;
                else if (b.Contains("jungle") || b.Contains("tropical")) e.palette = 3;
            }
            catch { }
            return e;
        }

        private static float WeatherFlag(WeatherDef w, string token)
        {
            if (w == null || string.IsNullOrEmpty(w.defName) || string.IsNullOrEmpty(token)) return 0f;
            return w.defName.ToLowerInvariant().Contains(token) ? 1f : 0f;
        }

        // ------------------------------------------------------------
        // 穹顶
        // ------------------------------------------------------------

        private static void EnsureDome()
        {
            if (domeMaterial != null) return;
            var domeShader = Shader.Find("Unlit/Texture");
            if (domeShader == null) return;
            domeTexture = new Texture2D(96, 64, TextureFormat.RGB24, false)
            { name = "FirstPerson3D_SkyGradient", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            domeMaterial = new Material(domeShader)
            { name = "FirstPerson3D_SkyDome", mainTexture = domeTexture, renderQueue = 1000 };
            domeMesh = BuildDome();
        }

        private static Color domeSky = new Color(-1f, -1f, -1f, -1f);
        private static Color domeHorizon = new Color(-1f, -1f, -1f, -1f);
        private static float domeGlow = -1f;
        private static float domeCloud = -1f;
        private static float domeFog = -1f;
        private static float domeFlash = -1f;
        private static Vector3 domeSun;

        /// <summary>
        /// 穹顶贴图只在配色、太阳位置或天气有肉眼可见变化时重算。
        /// 网格 v=0 是天顶（phi=0），v=1 是天底。
        /// </summary>
        private static bool DomeNeedsUpdate(Env env)
        {
            bool changed = Mathf.Abs(env.glow - domeGlow) > 0.02f
                || Mathf.Abs(env.cloudiness - domeCloud) > 0.03f
                || Mathf.Abs(env.fog - domeFog) > 0.03f
                || Mathf.Abs(env.flash - domeFlash) > 0.04f
                || (env.sunDir - domeSun).sqrMagnitude > 0.0008f
                || ((Vector4)(env.sky - domeSky)).sqrMagnitude > 0.0015f
                || ((Vector4)(env.horizon - domeHorizon)).sqrMagnitude > 0.0015f;
            if (changed)
            {
                domeSky = env.sky;
                domeHorizon = env.horizon;
                domeGlow = env.glow;
                domeCloud = env.cloudiness;
                domeFog = env.fog;
                domeFlash = env.flash;
                domeSun = env.sunDir;
            }
            return changed;
        }

        private static void UpdateDomeTexture(Env env)
        {
            if (domeTexture == null) return;
            int w = domeTexture.width;
            int h = domeTexture.height;
            var px = new Color[w * h];
            Vector3 sun = env.sunDir.sqrMagnitude > 0.01f ? env.sunDir.normalized : Vector3.up;
            var warm = Color.Lerp(new Color(1f, 0.96f, 0.88f), new Color(1f, 0.48f, 0.18f), 1f - Mathf.Clamp01(sun.y * 2.2f));
            float shine = Mathf.Max(env.shine, env.glow * 0.85f);
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1); // 0 天顶 .. 1 天底
                float phi = v * Mathf.PI;
                float above = 0.5f - v;
                Color band;
                if (above >= 0f)
                {
                    float k = Mathf.Clamp01(above / 0.5f);
                    band = Color.Lerp(env.horizon, env.sky, Mathf.Pow(k, 0.62f));
                    float haze = Mathf.Exp(-Mathf.Pow((k - 0.08f) / 0.10f, 2f));
                    band = Color.Lerp(band, env.horizon * 1.08f, haze * (0.28f + env.fog * 0.45f));
                }
                else band = Color.Lerp(env.horizon, env.ground, Mathf.Clamp01(-above / 0.18f));
                float sp = Mathf.Sin(phi);
                float cp = Mathf.Cos(phi);
                for (int x = 0; x < w; x++)
                {
                    float theta = x / (float)(w - 1) * Mathf.PI * 2f;
                    var dir = new Vector3(sp * Mathf.Cos(theta), cp, sp * Mathf.Sin(theta));
                    float nd = Mathf.Clamp01(Vector3.Dot(dir, sun));
                    float broad = Mathf.Pow(nd, 5f) * shine * Mathf.Clamp01(env.glow + 0.15f);
                    float disc = Mathf.Pow(nd, 64f) * shine;
                    var color = Color.Lerp(band, warm, Mathf.Clamp01(broad * 0.62f + disc));
                    px[y * w + x] = color;
                }
            }
            domeTexture.SetPixels(px);
            domeTexture.Apply(false, false);
        }

        private static Mesh BuildDome()
        {
            const int lon = 32, lat = 32;
            var vertices = new List<Vector3>((lon + 1) * (lat + 1));
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(lon * lat * 12);
            for (int y = 0; y <= lat; y++)
            {
                float phi = y / (float)lat * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    vertices.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
                    uvs.Add(new Vector2(u, y / (float)lat));
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + lon + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(a);
                    triangles.Add(b + 1); triangles.Add(b); triangles.Add(a + 1);
                }
            var mesh = new Mesh { name = "FirstPerson3D_SkyDomeMesh" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------
        // 太阳 / 云 / 星
        // ------------------------------------------------------------

        private static void EnsureCelestial()
        {
            if (quadMesh != null) return;
            var tShader = ShaderDatabase.Transparent;
            if (tShader == null) return;

            quadMesh = BuildDoubleQuad();

            sunTex = MakeRadialTex(64, 0.18f, 1.0f);
            sunMat = new Material(tShader) { name = "FirstPerson3D_Sun", mainTexture = sunTex, renderQueue = 1010 };
            haloMat = new Material(tShader) { name = "FirstPerson3D_SunHalo", mainTexture = MakeRadialTex(64, 0.0f, 0.55f), renderQueue = 1009 };

            cloudTex = MakeCloudTex(256);
            cloudMat = new Material(tShader) { name = "FirstPerson3D_CloudsNear", mainTexture = cloudTex, renderQueue = 1022 };
            cloudMatFar = new Material(tShader) { name = "FirstPerson3D_CloudsFar", mainTexture = cloudTex, renderQueue = 1020 };
            cloudMesh = BuildCloudCap(40, 12);

            moonMat = new Material(tShader) { name = "FirstPerson3D_Moon", mainTexture = MakeRadialTex(32, 0.35f, 1.1f), renderQueue = 1008 };

            rainMat = new Material(tShader) { name = "FirstPerson3D_Rain", mainTexture = MakeRainTex(32, 128), renderQueue = 2800 };
            snowMat = new Material(tShader) { name = "FirstPerson3D_Snow", mainTexture = MakeSnowTex(64), renderQueue = 2800 };
            precipMesh = BuildPrecip(28);

            starMat = new Material(tShader) { name = "FirstPerson3D_Stars", mainTexture = MakeRadialTex(16, 0.1f, 1.0f), renderQueue = 1005 };
            starMesh = BuildStarDome(420, 118f, 0.55f);
        }

        /// <summary>双面单位四边形（中心原点，面向 ±Z）。</summary>
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

        /// <summary>径向渐变贴图：inner 以内全亮，到 1 衰减到 0，alpha 同步。</summary>
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
                    byte b = (byte)(a * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, b);
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>
        /// 可平铺云纹。v=0（贴图底）是云冠边缘，透明度收到 0，避免一圈硬边。
        /// 只滚动 U，边缘的淡出不会被卷上来。
        /// </summary>
        private static Texture2D MakeCloudTex(int n)
        {
            int h = n / 2;
            var t = new Texture2D(n, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_CloudTex" };
            var px = new Color32[n * h];
            const float S = 5f;
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)h;
                float along = y / (float)(h - 1);
                float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.38f, along));
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;
                    float a = Fbm(u * S, v * S), b = Fbm((u - 1f) * S, v * S);
                    float c = Fbm(u * S, (v - 1f) * S), d = Fbm((u - 1f) * S, (v - 1f) * S);
                    float val = (a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v);
                    float alpha = Mathf.Clamp01((val - 0.46f) / 0.24f);
                    alpha = alpha * alpha * (3f - 2f * alpha) * rim;
                    byte g = (byte)(255f * Mathf.Lerp(0.78f, 1f, alpha));
                    px[y * n + x] = new Color32(g, g, 255, (byte)(alpha * 220f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static float Fbm(float x, float y)
        {
            float v = 0f, amp = 0.5f, f = 1f;
            for (int i = 0; i < 4; i++)
            {
                v += Mathf.PerlinNoise(x * f + 13.7f, y * f + 7.1f) * amp;
                f *= 2.03f;
                amp *= 0.5f;
            }
            return v;
        }

        /// <summary>上半球云冠。单位球，v=1 天顶、v=0 靠近地平线的收边。双面，相机在球心。</summary>
        private static Mesh BuildCloudCap(int lon, int lat)
        {
            const float maxPhi = 1.22f;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float t = y / (float)lat;
                float phi = t * maxPhi;
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float theta = u * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta)));
                    uvs.Add(new Vector2(u * 3f, 1f - t));
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
            var m = new Mesh { name = "FirstPerson3D_CloudCap" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Texture2D MakeRainTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_RainTex" };
            var px = new Color32[w * h];
            var rnd = new System.Random(19);
            for (int s = 0; s < 14; s++)
            {
                int x = rnd.Next(w);
                int y0 = rnd.Next(h);
                int len = 18 + rnd.Next(36);
                byte a = (byte)(140 + rnd.Next(90));
                for (int k = 0; k < len; k++)
                {
                    int y = (y0 + k) % h;
                    px[y * w + x] = new Color32(210, 220, 235, a);
                    if (x + 1 < w) px[y * w + x + 1] = new Color32(210, 220, 235, (byte)(a / 3));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static Texture2D MakeSnowTex(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "FirstPerson3D_SnowTex" };
            var px = new Color32[n * n];
            var rnd = new System.Random(23);
            for (int s = 0; s < 28; s++)
            {
                int cx = rnd.Next(n), cy = rnd.Next(n);
                int rad = 1 + rnd.Next(2);
                byte a = (byte)(160 + rnd.Next(80));
                for (int y = -rad; y <= rad; y++)
                    for (int x = -rad; x <= rad; x++)
                    {
                        if (x * x + y * y > rad * rad) continue;
                        int xx = (cx + x + n) % n, yy = (cy + y + n) % n;
                        px[yy * n + xx] = new Color32(245, 248, 255, a);
                    }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>围在相机周围的竖条，用来滚雨丝或雪花。半径约 6 米。</summary>
        private static Mesh BuildPrecip(int count)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const float halfH = 5f;
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                float r = 4.2f + (i % 5) * 0.7f;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                var right = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                float width = 1.4f;
                int b = verts.Count;
                verts.Add(p - right * width + Vector3.down * halfH);
                verts.Add(p + right * width + Vector3.down * halfH);
                verts.Add(p + right * width + Vector3.up * halfH);
                verts.Add(p - right * width + Vector3.up * halfH);
                float u0 = (i % 4) * 0.25f;
                uvs.Add(new Vector2(u0, 0f));
                uvs.Add(new Vector2(u0 + 0.25f, 0f));
                uvs.Add(new Vector2(u0 + 0.25f, 1f));
                uvs.Add(new Vector2(u0, 1f));
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var m = new Mesh { name = "FirstPerson3D_Precip" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>星点球面：随机分布的小面片，朝向球心。</summary>
        private static Mesh BuildStarDome(int count, float radius, float size)
        {
            var rnd = new System.Random(20261010);
            var verts = new List<Vector3>(count * 4);
            var uvs = new List<Vector2>(count * 4);
            var cols = new List<Color>(count * 4);
            var tris = new List<int>(count * 12);
            for (int i = 0; i < count; i++)
            {
                // 只在地平线以上
                float y = (float)rnd.NextDouble() * 0.95f + 0.05f;
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float rxz = Mathf.Sqrt(1f - y * y);
                var n = new Vector3(Mathf.Cos(a) * rxz, y, Mathf.Sin(a) * rxz);
                var p = n * radius;
                var right = Vector3.Cross(Vector3.up, n).normalized;
                if (right.sqrMagnitude < 0.01f) right = Vector3.right;
                var up = Vector3.Cross(n, right).normalized;
                float s = size * (0.5f + (float)rnd.NextDouble());
                float bright = 0.55f + (float)rnd.NextDouble() * 0.45f;
                var c = new Color(bright, bright, bright * (0.9f + 0.1f * (float)rnd.NextDouble()), 1f);
                int b = verts.Count;
                verts.Add(p - right * s - up * s); verts.Add(p + right * s - up * s);
                verts.Add(p + right * s + up * s); verts.Add(p - right * s + up * s);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                cols.Add(c); cols.Add(c); cols.Add(c); cols.Add(c);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2); tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1); tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }
            var m = new Mesh { name = "FirstPerson3D_Stars" };
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------
        // 每帧绘制
        // ------------------------------------------------------------

        public static void DrawDistantRing(Vector3 camPos)
        {
            if (!Fps3DState.Active || CameraRig3D.Cam == null) return;
            var map = Find.CurrentMap;
            var env = Sample(map);
            var cam = CameraRig3D.Cam;
            if (cam != null) cam.backgroundColor = env.horizon;
            ApplyWeatherFog(env);

            // 1. 穹顶
            if (domeMesh != null && domeMaterial != null)
                Graphics.DrawMesh(domeMesh, Matrix4x4.TRS(camPos, Quaternion.identity, Vector3.one * 130f), domeMaterial, 0);

            DrawCelestial(camPos, env);

            // 远景：只保留跟随相机的穹顶（地平线由穹顶地面色承担）。
            // 原「世界坐标锚定的远山环 + 地裙」已删除——它们是固定假景，
            // 站在地图边缘会贴着/糊进几何体，观感不合理。
        }

        private static void DrawCelestial(Vector3 camPos, Env env)
        {
            if (quadMesh == null) return;
            float now = Time.realtimeSinceStartup;
            if (lastCloudTime < 0f) lastCloudTime = now;
            float dt = Mathf.Clamp(now - lastCloudTime, 0f, 0.1f);
            lastCloudTime = now;
            var wind = new Vector2(Mathf.Cos(env.windAngle), Mathf.Sin(env.windAngle)) * Mathf.Max(0.12f, env.wind);

            DrawSun(camPos, env);
            DrawClouds(camPos, env, wind, dt);
            DrawPrecip(camPos, env, wind, dt);

            if (starMesh != null && starMat != null && env.glow < 0.55f)
            {
                float a = Mathf.Clamp01((0.55f - env.glow) / 0.45f) * (1f - env.cloudiness * 0.65f) * (1f - env.fog);
                starMat.color = new Color(1f, 1f, 1f, a * 0.9f);
                if (a > 0.02f)
                    Graphics.DrawMesh(starMesh, Matrix4x4.TRS(camPos, Quaternion.identity, Vector3.one), starMat, 0);
            }
        }

        private static void DrawSun(Vector3 camPos, Env env)
        {
            float above = Mathf.Clamp01((env.sunDir.y + 0.05f) / 0.18f);
            float vis = above * (1f - env.cloudiness * 0.82f) * (1f - env.fog * 0.75f) * Mathf.Clamp01(env.glow * 1.35f);
            Vector3 up = Mathf.Abs(env.sunDir.y) > 0.92f ? Vector3.forward : Vector3.up;
            if (vis > 0.02f && sunMat != null)
            {
                float low = 1f - Mathf.Clamp01(env.sunDir.y * 2.5f);
                var sunCol = Color.Lerp(new Color(1f, 0.98f, 0.90f), new Color(1f, 0.55f, 0.22f), low);
                sunMat.color = new Color(sunCol.r, sunCol.g, sunCol.b, 0.95f * vis);
                haloMat.color = new Color(sunCol.r, sunCol.g, sunCol.b, 0.28f * vis * Mathf.Max(0.45f, env.shine));
                var p = camPos + env.sunDir * 112f;
                var look = Quaternion.LookRotation(env.sunDir, up);
                float disc = 6.5f + 4f * low;
                Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(p, look, Vector3.one * disc), sunMat, 0);
                Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(p, look, Vector3.one * (28f + 18f * low)), haloMat, 0);
            }

            // 太阳落到地平线下之后，对侧留一颗小月亮。
            if (moonMat == null || env.glow > 0.42f || env.fog > 0.55f) return;
            var moonDir = -env.sunDir;
            if (moonDir.y < 0.08f) moonDir.y = 0.08f;
            moonDir.Normalize();
            float ma = Mathf.Clamp01((0.42f - env.glow) / 0.42f) * (1f - env.cloudiness * 0.7f);
            moonMat.color = new Color(0.82f, 0.86f, 0.95f, 0.85f * ma);
            if (ma < 0.02f) return;
            var mp = camPos + moonDir * 112f;
            var mlook = Quaternion.LookRotation(moonDir, up);
            Graphics.DrawMesh(quadMesh, Matrix4x4.TRS(mp, mlook, Vector3.one * 3.2f), moonMat, 0);
        }

        private static void DrawClouds(Vector3 camPos, Env env, Vector2 wind, float dt)
        {
            if (cloudMesh == null || cloudMat == null) return;
            cloudScroll += new Vector2(wind.x, wind.y) * dt * 0.010f;
            cloudMat.mainTextureOffset = cloudScroll;
            if (cloudMatFar != null) cloudMatFar.mainTextureOffset = cloudScroll * 0.55f + new Vector2(0.37f, 0.1f);

            var lit = Color.Lerp(new Color(0.55f, 0.60f, 0.68f), Color.white, env.glow);
            lit = Color.Lerp(lit, env.horizon, 0.25f);
            if (env.storm > 0.2f) lit = Color.Lerp(lit, new Color(0.32f, 0.34f, 0.40f), env.storm * 0.65f);
            if (env.snow > 0.2f) lit = Color.Lerp(lit, new Color(0.90f, 0.93f, 0.97f), 0.45f);
            if (env.flash > 0.02f) lit = Color.Lerp(lit, Color.white, env.flash);
            float aNear = Mathf.Lerp(0.28f, 0.92f, env.cloudiness);
            float aFar = Mathf.Lerp(0.18f, 0.62f, env.cloudiness);
            cloudMat.color = new Color(lit.r, lit.g, lit.b, aNear);
            Graphics.DrawMesh(cloudMesh, Matrix4x4.TRS(camPos, Quaternion.identity, Vector3.one * 108f), cloudMat, 0);
            if (cloudMatFar != null)
            {
                cloudMatFar.color = new Color(Mathf.Min(1f, lit.r + 0.08f), Mathf.Min(1f, lit.g + 0.08f), Mathf.Min(1f, lit.b + 0.1f), aFar);
                Graphics.DrawMesh(cloudMesh, Matrix4x4.TRS(camPos, Quaternion.identity, Vector3.one * 124f), cloudMatFar, 0);
            }
        }

        private static void DrawPrecip(Vector3 camPos, Env env, Vector2 wind, float dt)
        {
            if (precipMesh == null) return;
            bool snowing = env.snow > 0.15f && env.snow >= env.rain * 0.5f;
            float rate = snowing ? env.snow : env.rain;
            if (!snowing && env.sand > env.rain && env.sand > 0.2f) rate = env.sand;
            if (rate < 0.08f) return;
            var mat = snowing ? snowMat : rainMat;
            if (env.sand > 0.2f && !snowing && env.rain < 0.2f) mat = rainMat;
            if (mat == null) return;
            float speed = snowing ? 0.35f : (env.sand > env.rain && env.rain < 0.2f ? 0.8f : 2.4f);
            precipScroll += new Vector2(wind.x * dt * 0.15f, speed * dt);
            mat.mainTextureOffset = precipScroll;
            float a = Mathf.Lerp(0.25f, snowing ? 0.7f : 0.55f, Mathf.Clamp01(rate));
            Color tint = snowing ? new Color(0.95f, 0.97f, 1f, a)
                : env.sand > env.rain && env.rain < 0.2f ? new Color(0.85f, 0.7f, 0.42f, a * 0.8f)
                : new Color(0.75f, 0.82f, 0.9f, a);
            mat.color = tint;
            Graphics.DrawMesh(precipMesh, Matrix4x4.TRS(camPos, Quaternion.identity, Vector3.one), mat, 0);
        }

        private static void ApplyWeatherFog(Env env)
        {
            float thick = Mathf.Clamp01(env.fog * 0.95f + env.rain * 0.28f + env.snow * 0.22f + env.sand * 0.6f + env.storm * 0.12f);
            if (thick < 0.08f) return;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            var haze = Color.Lerp(env.horizon, new Color(0.78f, 0.80f, 0.82f), 0.45f);
            if (env.sand > 0.15f) haze = Color.Lerp(haze, new Color(0.72f, 0.56f, 0.34f), env.sand);
            if (env.snow > 0.15f) haze = Color.Lerp(haze, new Color(0.86f, 0.89f, 0.93f), 0.55f);
            RenderSettings.fogColor = haze;
            float end = Mathf.Lerp(120f, 16f, thick);
            float start = Mathf.Lerp(48f, 3.5f, thick);
            var st = FirstPerson3DMod.settings;
            if (st != null && st.fogOn) end = Mathf.Min(end, Mathf.Max(20f, st.fogEnd));
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = Mathf.Max(start + 6f, end);
        }

        // ------------------------------------------------------------
        // 远山（三层视差 + 大气透视 + 树影）
        // ------------------------------------------------------------

        private static void PaletteOf(int palette, out Color low, out Color high, out Color tree)
        {
            switch (palette)
            {
                case 1: // 沙漠
                    low = new Color(0.58f, 0.46f, 0.30f); high = new Color(0.78f, 0.66f, 0.48f); tree = new Color(0.36f, 0.34f, 0.22f); break;
                case 2: // 苔原 / 雪
                    low = new Color(0.52f, 0.58f, 0.62f); high = new Color(0.90f, 0.93f, 0.96f); tree = new Color(0.14f, 0.22f, 0.20f); break;
                case 3: // 丛林
                    low = new Color(0.12f, 0.26f, 0.18f); high = new Color(0.30f, 0.46f, 0.30f); tree = new Color(0.08f, 0.18f, 0.10f); break;
                default: // 温带
                    low = new Color(0.19f, 0.27f, 0.25f); high = new Color(0.42f, 0.47f, 0.45f); tree = new Color(0.10f, 0.20f, 0.12f); break;
            }
        }

        }
}
