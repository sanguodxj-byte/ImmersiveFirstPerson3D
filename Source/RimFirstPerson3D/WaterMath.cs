using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L6：水面 —— 菲涅尔、天色反射、太阳亮带 —— 纯函数层
    // ============================================================
    public static class WaterMath
    {
        /// <summary>Schlick 菲涅尔：F0 + (1-F0)(1-cosθ)^5，cosθ = 视线与法线夹角余弦。</summary>
        public static float Fresnel(float cosTheta, float f0)
        {
            float c = 1f - Mathf.Clamp01(cosTheta);
            float c2 = c * c;
            return f0 + (1f - f0) * c2 * c2 * c;
        }

        public static Vector3 Reflect(Vector3 d, Vector3 n)
        {
            return d - n * (2f * Vector3.Dot(d, n));
        }

        /// <summary>反射方向看到的天色：按 R.y 在地平色与天顶色间插（与 SkyScenery 半球同口径），夜里压暗。</summary>
        public static Color SkyAlong(Vector3 r, Color zenith, Color horizon, float glow)
        {
            float t = Mathf.Sqrt(Mathf.Clamp01(r.y));
            var c = Color.Lerp(horizon, zenith, t);
            float k = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(glow));
            return new Color(c.r * k, c.g * k, c.b * k, 1f);
        }

        /// <summary>太阳高光：反射方向对准太阳的 Phong 瓣。</summary>
        public static float Glint(Vector3 r, Vector3 sunDir, float power)
        {
            if (sunDir.y <= 0f) return 0f;
            return Mathf.Pow(Mathf.Max(0f, Vector3.Dot(r, sunDir)), power);
        }

        /// <summary>
        /// 一个水面顶点的最终颜色（材质色换成白，颜色全写进顶点色）：
        /// rgb = lerp(水体色 × 光照, 天色反射, F) + 太阳色 × 高光 × 受光；alpha = lerp(水体 alpha, 0.95, F)。
        /// </summary>
        public static Color Shade(Color waterTint, Color lit, Color sky, float fresnel, Color sunCol, float glint, float sunLit)
        {
            var b = new Color(waterTint.r * lit.r, waterTint.g * lit.g, waterTint.b * lit.b, 1f);
            var c = Color.Lerp(b, sky, fresnel);
            float g = glint * Mathf.Clamp01(sunLit);
            c.r = Mathf.Clamp01(c.r + sunCol.r * g);
            c.g = Mathf.Clamp01(c.g + sunCol.g * g);
            c.b = Mathf.Clamp01(c.b + sunCol.b * g);
            c.a = Mathf.Lerp(waterTint.a, 0.95f, fresnel);
            return c;
        }

        /// <summary>
        /// 太阳在水面上的反射点：相机 C 看向 (sun.x, -sun.y, sun.z) 方向与水面 y=h 的交点。
        /// 太阳在地平线下或相机在水面下时返回 false。
        /// </summary>
        public static bool GlintPoint(Vector3 cam, float h, Vector3 sunDir, out Vector3 p)
        {
            p = Vector3.zero;
            if (sunDir.y <= 0.01f || cam.y <= h) return false;
            var d = new Vector3(sunDir.x, -sunDir.y, sunDir.z);
            float t = (cam.y - h) / sunDir.y;
            p = cam + d * t;
            return true;
        }

        /// <summary>镜面反射矩阵（关于平面 y = h）：反射相机的 worldToCamera = 主相机 × 本矩阵。</summary>
        // Waylith uses reverse depth: near z=w, far z=0; the retained half-space is plane·point >= 0.
        public static bool TryOblique(Matrix4x4 projection, Vector4 plane, out Matrix4x4 result)
        {
            result = projection;
            float norm = Mathf.Sqrt(plane.x * plane.x + plane.y * plane.y + plane.z * plane.z);
            if (!float.IsFinite(norm) || norm < 1e-6f || !float.IsFinite(plane.w)) return false;
            for (int i = 0; i < 16; i++) if (!float.IsFinite(projection[i])) return false;
            float det = Matrix4x4.ToNumerics(projection).GetDeterminant();
            if (!float.IsFinite(det) || Mathf.Abs(det) < 1e-8f) return false;
            plane = new Vector4(plane.x / norm, plane.y / norm, plane.z / norm, plane.w / norm);
            var inverse = projection.inverse;
            float maxDot = float.NegativeInfinity;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    var q = inverse * new Vector4(x, y, 0f, 1f);
                    float dot = plane.x * q.x + plane.y * q.y + plane.z * q.z + plane.w * q.w;
                    if (!float.IsFinite(dot)) return false;
                    maxDot = Mathf.Max(maxDot, dot);
                }
            if (maxDot <= 1e-6f) return false;
            var row = projection.GetRow(3);
            result.SetRow(2, new Vector4(row.x - plane.x / maxDot, row.y - plane.y / maxDot,
                row.z - plane.z / maxDot, row.w - plane.w / maxDot));
            for (int i = 0; i < 16; i++) if (!float.IsFinite(result[i])) { result = projection; return false; }
            return true;
        }

        public static Matrix4x4 FlipProjectionX(Matrix4x4 projection)
        {
            var row = projection.GetRow(0);
            projection.SetRow(0, new Vector4(-row.x, -row.y, -row.z, -row.w));
            return projection;
        }

        public static bool TryViewport(Matrix4x4 viewProjection, Vector3 world, out Vector2 uv)
        {
            var clip = viewProjection * new Vector4(world.x, world.y, world.z, 1f);
            uv = Vector2.zero;
            if (!float.IsFinite(clip.w) || clip.w <= 1e-6f) return false;
            uv = new Vector2((clip.x / clip.w + 1f) * 0.5f, (1f - clip.y / clip.w) * 0.5f);
            return float.IsFinite(uv.x) && float.IsFinite(uv.y);
        }

        public static Matrix4x4 MirrorY(float h)
        {
            var m = Matrix4x4.identity;
            m.m11 = -1f;
            m.m13 = 2f * h;
            return m;
        }
    }
}
