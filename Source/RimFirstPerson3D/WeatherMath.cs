using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L7：天空、云与天气 —— 纯函数层
    // ============================================================
    public static class WeatherMath
    {
        /// <summary>
        /// 云穹顶点的打光乘子（≤1，乘在云材质色上）：靠近太阳的云边亮（银边）、
        /// 越靠地平线越厚越暗（云底），晨昏时近地平线朝太阳的一侧染霞光色。
        /// dir 是穹顶顶点方向（单位向量，y≥0）。
        /// </summary>
        public static Color CloudVertex(Vector3 dir, Vector3 sunDir, Color glowHorizon, float glow, float twilight)
        {
            float toSun = Mathf.Clamp01(Vector3.Dot(dir, sunDir));
            float silver = Mathf.Pow(toSun, 6f) * Mathf.Clamp01(glow * 1.5f);
            float thick = Mathf.Lerp(0.72f, 0.92f, Mathf.Clamp01(dir.y * 2.2f));   // 近地平线 = 看到的是云底
            float k = Mathf.Clamp01(thick + 0.10f * silver);
            var c = new Color(k, k, k, 1f);
            float low = 1f - Mathf.Clamp01(dir.y * 3f);
            float dusk = Mathf.Clamp01(twilight) * low * (0.35f + 0.65f * toSun);
            if (dusk > 0f)
            {
                float m = Mathf.Max(0.05f, Mathf.Max(glowHorizon.r, Mathf.Max(glowHorizon.g, glowHorizon.b)));
                var gh = new Color(glowHorizon.r / m, glowHorizon.g / m, glowHorizon.b / m, 1f);
                c = Color.Lerp(c, new Color(gh.r * k, gh.g * k, gh.b * k, 1f), dusk * 0.7f);
            }
            c.a = 1f;
            return c;
        }

        /// <summary>雨幕浓度：雨势 × 露天程度（室内看不见雨幕）。</summary>
        public static float RainCurtain(float rain, float storm, float skyVis)
        {
            return Mathf.Clamp01(Mathf.Max(rain, storm * 0.9f)) * Mathf.Clamp01(skyVis) * 0.38f;
        }

        /// <summary>云影浓度：云量 × 白天亮度，只在露天。</summary>
        public static float CloudShadow(float cloudiness, float glow, float sunY)
        {
            if (sunY <= 0.05f) return 0f;
            return Mathf.Clamp01(cloudiness) * Mathf.Clamp01(glow) * 0.28f;
        }

        /// <summary>极光期的弱绿色环境光（加在露天朝上面，进烘焙，按 1/4 档）。</summary>
        public static Color AuroraAmbient(float aurora)
        {
            float a = Mathf.Round(Mathf.Clamp01(aurora) * 4f) / 4f;
            return new Color(0.02f, 0.10f, 0.06f, 1f) * a;
        }

        /// <summary>闪电全屏冷白闪的强度（env.flash 的瞬时值，封顶免得整屏发白）。</summary>
        public static float FlashScreen(float flash)
        {
            return Mathf.Clamp01(flash) * 0.32f;
        }

        /// <summary>闪电方向：按一个整数种子取一个固定的高仰角方向（每次闪电换一个）。</summary>
        public static Vector3 LightningDir(int seed)
        {
            float a = (seed * 137.508f) % 360f * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * 0.45f, 0.89f, Mathf.Sin(a) * 0.45f).normalized;
        }
    }
}
