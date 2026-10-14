using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L7：云层打光、云影、雨幕、积水亮点、闪电冷白闪
    // ============================================================
    //
    // 湿面压暗与极光环境光走烘焙（Shaderpack3D.ApplyWeather），这里只画不进烘焙的东西。
    public static class Weather3D
    {
        // ---- 云层打光 ----
        private static Mesh cachedCloudMesh;
        private static Color32[] cloudBase;
        private static Vector3[] cloudDirs;
        private static Color32[] cloudBuf;
        private static bool cloudLit;

        public static void LightClouds(Mesh dome, SkyScenery.Env env)
        {
            if (dome == null) return;
            if (cloudBase == null || cachedCloudMesh != dome || cloudBase.Length != dome.vertexCount)
            {
                try
                {
                    cachedCloudMesh = dome;
                    cloudBase = dome.colors32;
                    var v = dome.vertices;
                    cloudDirs = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++) cloudDirs[i] = v[i].normalized;
                    cloudBuf = new Color32[v.Length];
                    cloudLit = false;
                }
                catch { cloudBase = null; cachedCloudMesh = null; return; }
            }
            if (!Shaderpack3D.CloudLight)
            {
                if (cloudLit) { dome.colors32 = cloudBase; cloudLit = false; }
                return;
            }

            Shaderpack3D.Begin();
            try
            {
                for (int i = 0; i < cloudDirs.Length; i++)
                {
                    var k = WeatherMath.CloudVertex(cloudDirs[i], env.sunDir, env.sunGlowHorizon, env.glow, env.twilight);
                    var b = cloudBase[i];
                    cloudBuf[i] = new Color32((byte)(k.r * b.r), (byte)(k.g * b.g), (byte)(k.b * b.b), b.a);
                }
                dome.colors32 = cloudBuf;
                cloudLit = true;
            }
            finally { Shaderpack3D.End(); }
        }

        // ---- 整帧层：云影、雨幕、积水亮点、闪电 ----
        private const int ShadowGrid = 48;
        private const float ShadowCell = 3f;
        private static Mesh shadowMesh;
        private static Material shadowMat;
        private static float shadowPoll = 99f;
        private static Vector3 shadowOrigin;

        private static Mesh rainMesh;
        private static Material rainMat;
        private static Material glintMat;
        private static Material flashMat;
        private static Mesh flashQuad;
        private static float lastFlash;
        private static int flashSeed;

        public static int LightningSeed => flashSeed;

        public static void Draw(Camera cam)
        {
            var map = Find.CurrentMap;
            if (cam == null || map == null) return;
            var sc = SceneRegistry.Find(map);
            if (sc == null || sc.Light == null) return;
            var env = Lighting3D.Current;
            Shaderpack3D.Begin();
            try
            {
                if (env.flash > lastFlash + 0.2f) flashSeed++;   // 新的一道闪电
                lastFlash = env.flash;

                var local = cam.transform.position - sc.worldOffset;
                float vis = sc.Light.SkyVisAt(local.x, local.z);
                if (Shaderpack3D.CloudShadows) DrawCloudShadows(cam, sc, env);
                if (Shaderpack3D.RainFx)
                {
                    DrawRain(cam, env, vis);
                    DrawPuddles(cam, sc, env, map);
                }
                if (Shaderpack3D.CloudLight) DrawFlash(cam, env, vis);
            }
            finally { Shaderpack3D.End(); }
        }

        private static void DrawCloudShadows(Camera cam, Scene3D sc, SkyScenery.Env env)
        {
            float a = WeatherMath.CloudShadow(env.cloudiness, env.glow, env.sunDir.y);
            var tex = SkyScenery.CloudTexture;
            if (a <= 0.01f || tex == null) return;
            if (shadowMat == null)
            {
                Shader sh = null;
                try { sh = ShaderDatabase.TransparentPostLight; } catch { }
                if (sh == null) return;
                shadowMat = new Material(sh) { name = "Fps3D_CloudShadow", mainTexture = tex, renderQueue = 2455 };
            }
            // 网格以相机所在的 ShadowCell 格对齐，每 1 s 按露天程度重写顶点 alpha（室内地面不落云影）
            var p = cam.transform.position - sc.worldOffset;
            var origin = new Vector3(Mathf.Floor(p.x / ShadowCell) * ShadowCell - ShadowGrid * ShadowCell * 0.5f, sc.baseY + 0.025f,
                                     Mathf.Floor(p.z / ShadowCell) * ShadowCell - ShadowGrid * ShadowCell * 0.5f);
            shadowPoll += Time.deltaTime;
            if (shadowMesh == null || shadowPoll >= 1f || origin != shadowOrigin)
            {
                shadowPoll = 0f;
                shadowOrigin = origin;
                BuildShadowGrid(sc, origin);
            }
            // 云影随云层同步滚动：UV = 世界 xz / 尺度 + 云层偏移（沿太阳方向错开，影子落在云的背光侧）
            var sd = env.sunDir;
            float k = 1f / 140f;
            shadowMat.mainTextureScale = new Vector2(k * ShadowCell, k * ShadowCell);
            var scroll = SkyScenery.CloudScroll;
            shadowMat.mainTextureOffset = new Vector2(scroll.x * 4f + origin.x * k - sd.x * 0.2f, scroll.y * 4f + origin.z * k - sd.z * 0.2f);
            shadowMat.color = new Color(0f, 0f, 0f, a);
            Graphics.DrawMesh(shadowMesh, Matrix4x4.Translate(origin + sc.worldOffset), shadowMat, 0);
        }

        private static void BuildShadowGrid(Scene3D sc, Vector3 origin)
        {
            int n = ShadowGrid + 1;
            if (shadowMesh == null)
            {
                shadowMesh = new Mesh { name = "Fps3D_CloudShadowGrid" };
                var v = new Vector3[n * n];
                var uv = new Vector2[n * n];
                for (int z = 0; z < n; z++)
                    for (int x = 0; x < n; x++)
                    {
                        v[z * n + x] = new Vector3(x * ShadowCell, 0f, z * ShadowCell);
                        uv[z * n + x] = new Vector2(x, z);
                    }
                var t = new List<int>(ShadowGrid * ShadowGrid * 6);
                for (int z = 0; z < ShadowGrid; z++)
                    for (int x = 0; x < ShadowGrid; x++)
                    {
                        int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                        // 朝上：本项目绕序 (0,1,2) 正面，外法线 = Cross(v1-v0, v2-v0)
                        t.Add(a); t.Add(c); t.Add(d); t.Add(a); t.Add(d); t.Add(b);
                    }
                shadowMesh.vertices = v;
                shadowMesh.uv = uv;
                shadowMesh.triangles = t.ToArray();
                shadowMesh.bounds = new Bounds(new Vector3(n * ShadowCell * 0.5f, 0f, n * ShadowCell * 0.5f), new Vector3(n * ShadowCell, 4f, n * ShadowCell));
            }
            var cols = new Color32[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float vis = sc.Light.SkyVisAt(origin.x + x * ShadowCell, origin.z + z * ShadowCell);
                    cols[z * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(vis) * 255f));
                }
            shadowMesh.colors32 = cols;
        }

        private static void DrawRain(Camera cam, SkyScenery.Env env, float vis)
        {
            float a = WeatherMath.RainCurtain(env.rain, env.storm, vis);
            if (a <= 0.01f) return;
            if (rainMat == null)
            {
                Shader sh = null;
                try { sh = ShaderDatabase.TransparentPostLight; } catch { }
                if (sh == null) return;
                rainMat = new Material(sh) { name = "Fps3D_RainCurtain", mainTexture = MakeStreakTex(64, 128), renderQueue = 2990 };
                rainMesh = BuildCylinder(24);
            }
            var p = cam.transform.position;
            float wind = Mathf.Clamp(env.wind, 0f, 2f);
            var lightK = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(env.glow + env.flash));
            for (int i = 0; i < 3; i++)
            {
                float r = 2.5f + i * 3.5f;
                float speed = 2.2f + i * 0.6f;
                rainMat.mainTextureOffset = new Vector2(Time.time * 0.03f * wind, Time.time * speed);
                rainMat.mainTextureScale = new Vector2(6f + i * 3f, 2f);
                rainMat.color = new Color(0.78f * lightK, 0.82f * lightK, 0.90f * lightK, a * (1f - i * 0.22f));
                var tilt = Quaternion.Euler(Mathf.Clamp(wind * 8f, 0f, 14f), env.windAngle, 0f);
                Graphics.DrawMesh(rainMesh, Matrix4x4.TRS(p + new Vector3(0f, -3f, 0f), tilt, new Vector3(r, 9f, r)), rainMat, 0);
            }
        }

        private static readonly List<Vector3> puddles = new List<Vector3>(32);
        private static float puddlePoll = 99f;

        private static void DrawPuddles(Camera cam, Scene3D sc, SkyScenery.Env env, Map map)
        {
            float wet = Shaderpack3D.WetLevel;
            if (wet <= 0.01f) return;
            if (glintMat == null)
            {
                Shader sh = null;
                try { sh = ShaderDatabase.MoteGlow; } catch { }
                if (sh == null) return;
                glintMat = new Material(sh) { name = "Fps3D_PuddleGlint", mainTexture = ProcTex3D.AdditiveRadial(32), renderQueue = 2958 };
            }
            puddlePoll += Time.deltaTime;
            var p = cam.transform.position - sc.worldOffset;
            if (puddlePoll >= 2f)
            {
                puddlePoll = 0f;
                puddles.Clear();
                int seed = Mathf.FloorToInt(p.x / 8f) * 73856093 ^ Mathf.FloorToInt(p.z / 8f) * 19349663;
                var rnd = new System.Random(seed);
                for (int i = 0; i < 24; i++)
                {
                    float x = p.x + (float)(rnd.NextDouble() * 24 - 12), z = p.z + (float)(rnd.NextDouble() * 24 - 12);
                    if (sc.Light.SkyVisAt(x, z) < 0.9f) continue;
                    if (sc.Light.Solid(Mathf.FloorToInt(x), Mathf.FloorToInt(z))) continue;
                    puddles.Add(new Vector3(x, sc.baseY + 0.02f, z));
                }
            }
            var k = Mathf.Lerp(0.05f, 0.18f, Mathf.Clamp01(env.glow)) * wet;
            for (int i = 0; i < puddles.Count; i++)
            {
                float tw = 0.6f + 0.4f * Mathf.Sin(Time.time * 3.1f + i * 1.7f);
                var c = new Color(0.8f, 0.86f, 1f) * (k * tw); c.a = 1f;
                var w = puddles[i] + sc.worldOffset;
                Graphics.DrawMesh(Util3D.GroundQuad, Matrix4x4.TRS(w, Quaternion.identity, new Vector3(0.6f, 1f, 0.6f)), Lighting3D.MatFor(glintMat, c), 0);
            }
        }

        private static void DrawFlash(Camera cam, SkyScenery.Env env, float vis)
        {
            float f = WeatherMath.FlashScreen(env.flash) * Mathf.Lerp(0.35f, 1f, vis);
            if (f <= 0.01f) return;
            if (flashMat == null)
            {
                Shader sh = null;
                try { sh = ShaderDatabase.MoteGlow; } catch { }
                if (sh == null) return;
                flashMat = new Material(sh) { name = "Fps3D_LightningFlash", mainTexture = Util3D.WhiteTex, renderQueue = 3995 };
                flashQuad = ScreenFx3D.ScreenQuad;
            }
            var c = new Color(0.82f, 0.88f, 1f) * f; c.a = 1f;
            ScreenFx3D.DrawFullscreen(cam, flashQuad, Lighting3D.MatFor(flashMat, c));
        }

        private static Mesh BuildCylinder(int seg)
        {
            var m = new Mesh { name = "Fps3D_RainCylinder" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v.Add(d); v.Add(d + Vector3.up);
                uv.Add(new Vector2(i / (float)seg, 0f)); uv.Add(new Vector2(i / (float)seg, 1f));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 3); t.Add(a); t.Add(a + 3); t.Add(a + 2);
                t.Add(a); t.Add(a + 3); t.Add(a + 1); t.Add(a); t.Add(a + 2); t.Add(a + 3);
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        private static Texture2D MakeStreakTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Fps3D_RainStreak", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            var rnd = new System.Random(1234);
            for (int i = 0; i < w * h; i++) px[i] = new Color32(255, 255, 255, 0);
            for (int k = 0; k < 90; k++)
            {
                int x = rnd.Next(w), y0 = rnd.Next(h), len = 6 + rnd.Next(14);
                byte a = (byte)(90 + rnd.Next(120));
                for (int j = 0; j < len; j++)
                {
                    int y = (y0 + j) % h;
                    float fade = 1f - j / (float)len;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * fade));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }
    }
}
