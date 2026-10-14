using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L3：动态物体投影（平面投影阴影）
    // ============================================================
    //
    // 物体自己的网格经 ShadowMath 的矩阵压扁到脚下地面，用 TransparentPostLight 的半透明黑再画一遍。
    // 材质保留源贴图（alpha 抠出剪影），rgb 乘 0，alpha = 贴图 alpha × 浓度。
    //
    // 调用约定：Scene3D.Draw 先 BeginScene（本图的光场、层高基准、世界偏移），各绘制点再 Cast。
    // 立牌类（人物、动物回退立牌）传 billboard=true 与「正对太阳」的矩阵：影子的形状不随相机转。
    // 树走 PlantLayer3D 已有的树冠影，这里不重复。
    public static class Shadows3D
    {
        private static LightField3D field;
        private static float baseY;
        private static Vector3 worldOffset;
        private static Map map;

        private static readonly Dictionary<long, Material> mats = new Dictionary<long, Material>();
        private static readonly Dictionary<int, Vector3[]> firstTri = new Dictionary<int, Vector3[]>();
        private const int MatCap = 2048;
        private const float Lift = 0.012f;
        private const float Eps = 0.002f;

        public static int Draws;

        public static void BeginScene(Map m, LightField3D f, float by, Vector3 offset)
        {
            map = m; field = f; baseY = by; worldOffset = offset;
        }

        /// <summary>本帧太阳影是否开（白天、露天在 Cast 里再判）。</summary>
        public static bool SunActive
        {
            get
            {
                if (!Shaderpack3D.ObjShadows) return false;
                var e = Lighting3D.Current;
                return e.glow > 0.05f && e.sunDir.y > ShaderpackDefaults.ShadowMinSunY * 0.5f;
            }
        }

        public static bool AnyActive => Shaderpack3D.ObjShadows && (SunActive || Shaderpack3D.LampShadows);

        /// <summary>立牌影用的偏航：正对太阳（ShadowMath.SunYaw），夜里灯影另算。</summary>
        public static float SunYaw => ShadowMath.SunYaw(Lighting3D.Current.sunDir);

        private static float Alpha => FirstPerson3DMod.settings != null
            ? Mathf.Clamp01(FirstPerson3DMod.settings.shadowAlpha) : ShaderpackDefaults.ShadowAlpha;

        /// <summary>
        /// 投一件物体的影。feetWorld：物体脚下点（世界坐标），用来取室内外与灯，以及做距离剔除。
        /// srcTex：源材质主贴图（抠剪影）；闭合网格可传 null（整块实心）。
        /// sunBillboardYawFix：立牌从「正对太阳」矩阵转 180° 的版本，朝下时换用它（ShadowMath.FacingAfter）。
        /// </summary>
        public static void Cast(Mesh mesh, Matrix4x4 obj, Vector3 feetWorld, Texture srcTex,
            bool billboard = false, Matrix4x4? flipped = null)
        {
            if (mesh == null || field == null || !Shaderpack3D.ObjShadows) return;
            var cam = CameraRig3D.Cam;
            if (cam == null) return;
            float range = Shaderpack3D.ObjShadowRange;
            if ((cam.transform.position - feetWorld).sqrMagnitude > range * range) return;
            Shaderpack3D.Begin();
            try
            {
                var e = Lighting3D.Current;
                var local = feetWorld - worldOffset;
                float ground = feetWorld.y + Lift;

                // ---- 闪电影（L7）：一道闪电的 0.15 s 里，影子方向切到闪电方位 ----
                if (Shaderpack3D.RainFx && e.flash > 0.3f)
                {
                    float vis = field.SkyVisAt(local.x, local.z);
                    float a = Alpha * Mathf.Clamp01(e.flash) * vis;
                    if (a > 0.01f)
                    {
                        var P = ShadowMath.Directional(WeatherMath.LightningDir(Weather3D.LightningSeed), ground, Eps, 3f);
                        Submit(mesh, P, obj, flipped, billboard, feetWorld, srcTex, a, 6f);
                    }
                }
                // ---- 太阳影 ----
                else if (SunActive)
                {
                    float vis = field.SkyVisAt(local.x, local.z);
                    float a = ShadowMath.SunAlpha(Alpha, e.sunDir, e.glow, vis, e.twilight, ShaderpackDefaults.ShadowMinSunY);
                    if (a > 0.01f)
                    {
                        var P = ShadowMath.Directional(e.sunDir, ground, Eps, ShaderpackDefaults.ShadowMaxLen / 2.5f);
                        Submit(mesh, P, obj, flipped, billboard, feetWorld, srcTex, a, ShaderpackDefaults.ShadowMaxLen);
                    }
                }

                // ---- 灯影（夜里，最近的一盏）----
                if (Shaderpack3D.LampShadows && e.glow < 0.6f)
                {
                    if (GlowLayer3D.Nearest(map, local, ShaderpackDefaults.LampShadowRadius, out var lampLocal, out float lampR, out float lampLuma))
                    {
                        float d = new Vector2(lampLocal.x - local.x, lampLocal.z - local.z).magnitude;
                        float a = ShadowMath.LampAlpha(Alpha, d, Mathf.Min(lampR, ShaderpackDefaults.LampShadowRadius), e.glow, lampLuma);
                        // 光心不能比物体低：低于 1.2 m 的灯（篝火）抬到 1.2 m，免得 w<0 把影子翻到天上
                        var lw = lampLocal + worldOffset;
                        lw.y = Mathf.Max(lw.y, feetWorld.y + 1.2f);
                        if (a > 0.01f && d > 0.2f)
                        {
                            var P = ShadowMath.Point(lw, ground);
                            Submit(mesh, P, obj, flipped, billboard, feetWorld, srcTex, a, 6f);
                        }
                    }
                }
            }
            finally { Shaderpack3D.End(); }
        }

        private static void Submit(Mesh mesh, Matrix4x4 P, Matrix4x4 obj, Matrix4x4? flipped, bool billboard,
            Vector3 feet, Texture tex, float alpha, float reach)
        {
            var useObj = obj;
            if (billboard && flipped.HasValue)
            {
                var tri = FirstTri(mesh);
                if (tri != null)
                {
                    var M = P * obj;
                    if (ShadowMath.FacingAfter(M, tri[0], tri[1], tri[2]) < 0f) useObj = flipped.Value;
                }
            }
            if (!Frustum3D.VisiblePoint(feet, reach)) return;
            var mat = MatFor(tex, alpha);
            if (mat == null) return;
            Graphics.DrawMesh(mesh, P * useObj, mat, 0);
            Draws++;
        }

        private static Vector3[] FirstTri(Mesh m)
        {
            int id = m.GetInstanceID();
            if (firstTri.TryGetValue(id, out var t)) return t;
            Vector3[] r = null;
            try
            {
                var v = m.vertices;
                var tr = m.triangles;
                if (v != null && tr != null && tr.Length >= 3)
                    r = new[] { v[tr[0]], v[tr[1]], v[tr[2]] };
            }
            catch { }
            if (firstTri.Count > 4096) firstTri.Clear();
            firstTri[id] = r;
            return r;
        }

        private static Material MatFor(Texture tex, float alpha)
        {
            int qa = Mathf.Clamp(Mathf.RoundToInt(alpha * 32f), 1, 32);
            long key = ((long)(tex != null ? tex.GetInstanceID() : 0) << 6) | (uint)qa;
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            Shader sh = null;
            try { sh = ShaderDatabase.TransparentPostLight; } catch { }
            if (sh == null) try { sh = ShaderDatabase.Transparent; } catch { }
            if (sh == null) return null;
            if (mats.Count >= MatCap) Clear();
            m = new Material(sh)
            {
                name = "Fps3D_ObjShadow",
                mainTexture = tex != null ? tex : Util3D.WhiteTex,
                color = new Color(0f, 0f, 0f, qa / 32f),
                // 地面、墙之后，半透明（水、草影、发光）之前
                renderQueue = 2460,
            };
            mats[key] = m;
            return m;
        }

        public static void Clear()
        {
            foreach (var kv in mats) if (kv.Value != null) Object.Destroy(kv.Value);
            mats.Clear();
        }
    }
}
