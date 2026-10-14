using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L1：胶片色调与调色 —— 纯函数层
    // ============================================================
    //
    // 只吃 Color 与 LightTuning，不碰 Map / Material（tools/LightSim 直接链接本文件跑判据）。
    // 取代 F013 的「Exposure × 逐通道 clamp」：
    //   1) 亮度域扩展 Reinhard，保色相，高光平滑滚到白而不是被硬截成色块；
    //   2) 高光向白收（防止篝火、晚霞把受光面推成纯橙）；
    //   3) 分离色调：暗部偏冷、亮部偏暖 —— 光影包标志性的「暖阳蓝影」；
    //   4) 饱和度，深夜无灯处按暗视觉去饱和。
    // 规范与默认值：docs/features/F016-shaderpack-look.md（L1）。
    public static class ToneMap
    {
        public static float Luma(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        /// <summary>扩展 Reinhard（亮度域）：L·(1+L/Wp²)/(1+L)。Wp 处正好映射到 1。</summary>
        public static float Reinhard(float l, float whitePoint)
        {
            if (l <= 0f) return 0f;
            float wp2 = Mathf.Max(0.01f, whitePoint * whitePoint);
            return l * (1f + l / wp2) / (1f + l);
        }

        private static float Smooth(float a, float b, float x)
        {
            float k = Mathf.Clamp01((x - a) / (b - a));
            return k * k * (3f - 2f * k);
        }

        /// <summary>
        /// 曝光后的完整色调链。输入是线性合成值（可以 >1），输出 0..1（下限由调用方取）。
        /// nightW：0 白天 .. 1 深夜无灯，只影响去饱和。
        /// </summary>
        public static Color Apply(Color c, ref LightTuning t, float nightW)
        {
            float ex = t.exposure;
            c.r = Mathf.Max(0f, c.r * ex);
            c.g = Mathf.Max(0f, c.g * ex);
            c.b = Mathf.Max(0f, c.b * ex);
            float l = Luma(c);
            if (l <= 1e-6f) return new Color(0f, 0f, 0f, 1f);

            // 1. 亮度域曲线：色相按比例跟着亮度走
            float l2 = Reinhard(l, t.whitePoint);
            float k = l2 / l;
            c.r *= k; c.g *= k; c.b *= k;

            // 2. 高光向白收：单通道被曲线推过 1 的部分换成「更白」而不是「更艳」
            float hd = Mathf.Clamp01(t.highlightDesat) * Smooth(0.75f, 1.0f, l2);
            if (hd > 0f)
            {
                c.r += (l2 - c.r) * hd;
                c.g += (l2 - c.g) * hd;
                c.b += (l2 - c.b) * hd;
            }

            // 3. 分离色调（乘性，权重按色调后的亮度）
            float st = Mathf.Clamp01(t.splitTone);
            if (st > 0f)
            {
                float sw = 1f - Smooth(0.15f, 0.55f, l2);
                float hw = 1f - sw;
                c.r *= Mathf.Lerp(1f, t.shadowTint.r, sw * st) * Mathf.Lerp(1f, t.highlightTint.r, hw * st);
                c.g *= Mathf.Lerp(1f, t.shadowTint.g, sw * st) * Mathf.Lerp(1f, t.highlightTint.g, hw * st);
                c.b *= Mathf.Lerp(1f, t.shadowTint.b, sw * st) * Mathf.Lerp(1f, t.highlightTint.b, hw * st);
            }

            // 4. 饱和度（夜里按暗视觉收）
            float sat = t.saturation <= 0f ? 1f : t.saturation;
            sat *= Mathf.Lerp(1f, t.nightDesat <= 0f ? 1f : t.nightDesat, Mathf.Clamp01(nightW));
            float lm = Luma(c);
            c.r = lm + (c.r - lm) * sat;
            c.g = lm + (c.g - lm) * sat;
            c.b = lm + (c.b - lm) * sat;

            c.r = Mathf.Clamp01(c.r);
            c.g = Mathf.Clamp01(c.g);
            c.b = Mathf.Clamp01(c.b);
            c.a = 1f;
            return c;
        }

        /// <summary>饱和度量：最大通道与最小通道之差 / 最大通道（HSV 的 S）。</summary>
        public static float Saturation(Color c)
        {
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return mx > 1e-5f ? (mx - mn) / mx : 0f;
        }

        // ------------------------------------------------------------
        // 色温：太阳仰角 → 开尔文 → RGB（最大通道归一）
        // ------------------------------------------------------------

        /// <summary>太阳仰角（度）→ 色温（K）。0°≈2000K、6°≈3200K、20°≈4800K、≥40°≈5400K（光影包的正午也偏暖，不取 6500K 纯白），段间线性。</summary>
        public static float SunKelvin(float elevDeg)
        {
            if (elevDeg <= 0f) return 2000f;
            if (elevDeg < 6f) return Mathf.Lerp(2000f, 3200f, elevDeg / 6f);
            if (elevDeg < 20f) return Mathf.Lerp(3200f, 4800f, (elevDeg - 6f) / 14f);
            if (elevDeg < 40f) return Mathf.Lerp(4800f, 5400f, (elevDeg - 20f) / 20f);
            return 5400f;
        }

        /// <summary>黑体近似（Tanner Helland 拟合，1000..40000 K），按最大通道归一。</summary>
        public static Color Kelvin(float k)
        {
            float t = Mathf.Clamp(k, 1000f, 40000f) / 100f;
            float r, g, b;
            if (t <= 66f)
            {
                r = 255f;
                g = 99.4708025861f * Mathf.Log(t) - 161.1195681661f;
            }
            else
            {
                r = 329.698727446f * Mathf.Pow(t - 60f, -0.1332047592f);
                g = 288.1221695283f * Mathf.Pow(t - 60f, -0.0755148492f);
            }
            if (t >= 66f) b = 255f;
            else if (t <= 19f) b = 0f;
            else b = 138.5177312231f * Mathf.Log(t - 10f) - 305.0447927307f;
            r = Mathf.Clamp(r, 0f, 255f) / 255f;
            g = Mathf.Clamp(g, 0f, 255f) / 255f;
            b = Mathf.Clamp(b, 0f, 255f) / 255f;
            float m = Mathf.Max(0.001f, Mathf.Max(r, Mathf.Max(g, b)));
            return new Color(r / m, g / m, b / m, 1f);
        }

        /// <summary>按太阳方向直接给直射色（sunDir.y = sin 仰角）。</summary>
        public static Color SunColorFor(Vector3 sunDir)
        {
            float elev = Mathf.Asin(Mathf.Clamp(sunDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            return Kelvin(SunKelvin(elev));
        }
    }
}
