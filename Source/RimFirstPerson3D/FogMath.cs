using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L5：雾壳与大气散射 —— 纯函数层
    // ============================================================
    //
    public static class FogMath
    {
        public static float ShellRadius(int i, float r0, float q)
        {
            return r0 * Mathf.Pow(q, i);
        }

        public static float ShellAlpha(int i, float density, float r0, float q)
        {
            float r = ShellRadius(i, r0, q);
            float prev = i == 0 ? 0f : ShellRadius(i - 1, r0, q);
            return 1f - Mathf.Exp(-Mathf.Max(0f, density) * (r - prev));
        }

        /// <summary>距离 d 处几何的透过率（壳叠出来的，与 exp(-σ·r_k) 一致）。</summary>
        public static float Transmittance(float d, float density, int shells, float r0, float q)
        {
            float t = 1f;
            for (int i = 0; i < shells; i++)
            {
                if (ShellRadius(i, r0, q) >= d) break;
                t *= 1f - ShellAlpha(i, density, r0, q);
            }
            return t;
        }

        public static float OpticalDepth(float distance, float density, float height, float directionY, float falloff)
        {
            if (distance <= 0f || density <= 0f) return 0f;
            float scale = Mathf.Max(0.1f, falloff);
            float endHeight = height + directionY * distance;
            if (height <= 0f && endHeight <= 0f) return density * distance;
            float airLength = distance, groundLength = 0f;
            float start = Mathf.Max(0f, height), end = Mathf.Max(0f, endHeight);
            if (height < 0f || endHeight < 0f)
            {
                float crossing = Mathf.Clamp(-height / directionY, 0f, distance);
                airLength = height < 0f ? distance - crossing : crossing;
                groundLength = distance - airLength;
            }
            float delta = directionY * airLength / scale;
            float average;
            // Both endpoints are nonnegative; descending rays must never evaluate exp(+height).
            if (Mathf.Abs(delta) < 0.01f)
                average = Mathf.Exp(-start / scale) * (1f - delta * 0.5f + delta * delta / 6f - delta * delta * delta / 24f);
            else
                average = (Mathf.Exp(-start / scale) - Mathf.Exp(-end / scale)) / delta;
            return density * (groundLength + airLength * Mathf.Clamp01(average));
        }

        public static float ContinuousTransmittance(float distance, float density, float height, float directionY, float falloff)
        {
            return Mathf.Exp(-OpticalDepth(distance, density, height, directionY, falloff));
        }

        public static Vector3 ViewRay(Matrix4x4 projection, Vector2 uv)
        {
            return new Vector3((uv.x * 2f - 1f + projection.m02) / projection.m00,
                (uv.y * 2f - 1f + projection.m12) / projection.m11, 1f);
        }

        public static float DepthDistance(Matrix4x4 projection, Vector2 uv, float depth)
        {
            float denominator = depth + projection.m22;
            return denominator > 0f ? projection.m23 / denominator * ViewRay(projection, uv).magnitude : 0f;
        }

        /// <summary>高度雾：离地越高越淡。</summary>
        public static float HeightFactor(float y, float groundY, float falloff)
        {
            return Mathf.Exp(-Mathf.Max(0f, y - groundY) / Mathf.Max(0.1f, falloff));
        }

        /// <summary>Henyey-Greenstein 相函数，按各向同性归一（各向同性时恒为 1）。</summary>
        public static float Mie(float cosTheta, float g)
        {
            float g2 = g * g;
            float denom = 1f + g2 - 2f * g * cosTheta;
            float hg = (1f - g2) / Mathf.Pow(Mathf.Max(1e-4f, denom), 1.5f);
            return hg;   // 4π·HG，各向同性 = 1
        }

        /// <summary>
        /// 雾色：地平色打底，朝太阳的方向叠米氏前向散射的暖光（晨昏更显）。夜里只剩地平色。
        /// </summary>
        public static Color FogColor(Color horizon, Color sun, Vector3 viewDir, Vector3 sunDir, float glow, float twilight)
        {
            float c = Vector3.Dot(viewDir, sunDir);
            float mie = Mie(c, ShaderpackDefaults.MieG);
            float k = Mathf.Clamp01(glow) * (0.06f + 0.10f * Mathf.Clamp01(twilight)) * Mathf.Clamp(mie, 0f, 12f) * (sunDir.y > -0.05f ? 1f : 0f);
            var col = horizon + sun * k;
            float m = Mathf.Max(1f, Mathf.Max(col.r, Mathf.Max(col.g, col.b)));
            col = col / m;
            col.a = 1f;
            return col;
        }

        /// <summary>天气 → 消光系数（每米）。晴天很淡，雾天近处就白。</summary>
        public static float Density(float baseDensity, float fog, float rain, float snow, float sand)
        {
            return Mathf.Max(0f, baseDensity) * (1f + 9f * Mathf.Clamp01(fog) + 2f * Mathf.Clamp01(rain)
                + 3f * Mathf.Clamp01(snow) + 7f * Mathf.Clamp01(sand));
        }

        /// <summary>室内（相机所在格 skyVis）压低雾密度。</summary>
        public static float IndoorK(float skyVis, float indoorK)
        {
            return Mathf.Lerp(indoorK, 1f, Mathf.Clamp01(skyVis));
        }

        /// <summary>光束强度：雾越浓越显，乘太阳亮度。</summary>
        public static float ShaftIntensity(float gain, float glow, float density, float baseDensity)
        {
            float dn = Mathf.Clamp01(density / Mathf.Max(1e-4f, baseDensity * 10f));
            return Mathf.Max(0f, gain) * Mathf.Clamp01(glow) * (0.4f + dn);
        }

        /// <summary>
        /// 屋顶开洞：本格露天、非实心，且四邻里至少两格有屋顶 —— 阳光从这里斜射进屋。
        /// roofed/solid 由调用方按格给（游戏里是 LightField3D，台子里是手写表）。
        /// </summary>
        public static bool IsRoofHole(bool selfRoofed, bool selfSolid, int roofedNeighbours)
        {
            return !selfRoofed && !selfSolid && roofedNeighbours >= 2;
        }
    }
}
