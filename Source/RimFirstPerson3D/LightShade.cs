using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // 着色合成式（F013）—— 纯函数层
    // ============================================================
    //
    // 这里只放数学：不碰 Map、不碰 Material、不碰 Quaternion/Mesh（那些在 Unity 播放器外跑不动）。
    // 游戏里的光场（LightField3D）实现 ILightField 把逐格数值喂进来，
    // 离线模拟场景台（tools/LightSim）用一个合成voxel场景实现同一个接口喂进来 ——
    // 两边跑的是同一份公式，所以模拟台通过才算数，不存在第二套规范。
    // 规范与常数出处：docs/features/F013-lighting.md

    /// <summary>光场采样接口：位置用图内局部坐标（Y 含层高基准）。</summary>
    public interface ILightField
    {
        /// <summary>天空可见度 0..1（露天 1，薄屋顶部分，厚屋顶 0）。</summary>
        float SkyVisAt(float x, float z);

        /// <summary>原版 glowGrid 的彩色辉光，归一化 0..1，未乘增益。</summary>
        Color LampAt(float x, float z);

        /// <summary>该点是否被太阳直射（体素投影，0..1，四邻双线性出软边）。</summary>
        float SunLitAt(Vector3 p, Vector3 n);

        /// <summary>顶点角接触阴影的占用比例（0..1）。</summary>
        float OccFraction(Vector3 p, Vector3 n);
    }

    /// <summary>当帧天象（由 SkyScenery.Env 填，光照与天空同一份数）。</summary>
    public struct LightEnv
    {
        public Vector3 sunDir;
        public Vector3 moonDir;
        public float glow;              // 0 夜 .. 1 正午，已含天气衰减
        public float twilight;          // 晨昏权重
        public Color skyZenith;         // 天顶色
        public Color skyHorizon;        // 地平色
        public Color sunGlowHorizon;    // 晨昏霞光色
        /// <summary>F016 L1：直射色按太阳仰角走色温曲线（false = F013 的白↔霞光二段插值）。</summary>
        public bool kelvinSun;
        /// <summary>F016 L7：极光期的弱环境光（默认黑 = 无），只加在露天、朝上的面上。</summary>
        public Color auroraAmbient;
    }

    /// <summary>可调参数（游戏里来自 FirstPerson3DSettings，模拟台里自己给）。</summary>
    public struct LightTuning
    {
        public float floor;
        public float exposure;
        /// <summary>
        /// 环境（天光）项增益，在 AmbientColor 内乘。调色板按最大通道归一后天光本身就能到 1.0，
        /// 再叠直射必然截断 —— 实机与模拟台都量到正午朝上面被压成纯白（等于看不见光照）。
        /// 天光只该占总亮度的一部分（晴天正午漫射 ≈ 法向直射的一半不到），这一档就是那个「一部分」。
        /// </summary>
        public float ambGain;
        public float sunStrength;
        public float lampStrength;
        public float lampGain;
        public float aoStrength;
        public float aoMin;
        public float softFoot;
        public float softTop;
        public float ambTint;
        public float moonStrength;

        // ---- F016 L1 胶片色调（toneMap=false 时整段不生效，F013 口径原样保留）----
        public bool toneMap;
        public float whitePoint;
        public float highlightDesat;
        public float splitTone;
        public Color shadowTint;
        public Color highlightTint;
        public float saturation;
        public float nightDesat;
    }

    public static class LightShade
    {
        private static Color Normalize(Color c)
        {
            float m = Mathf.Max(0.0001f, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
            return new Color(c.r / m, c.g / m, c.b / m, 1f);
        }

        /// <summary>
        /// 半球环境光：按 N.y 在天顶色与地平色之间插值，再按 ambTint 向白收敛，最后乘环境增益。
        /// 增益乘在这里而不是各合成式里，是因为山体那条通路（LightField3D.ShadeMountain）也用这个函数 ——
        /// 天光只留一份口径，免得两条通路算出两种曝光。
        /// </summary>
        public static Color AmbientColor(ref LightEnv e, ref LightTuning t, float hemi)
        {
            var zen = Normalize(e.skyZenith);
            var hor = Normalize(e.skyHorizon);
            var c = Color.Lerp(hor, zen, Mathf.Clamp01(hemi));
            return Color.Lerp(Color.white, c, Mathf.Clamp01(t.ambTint)) * Mathf.Max(0f, t.ambGain);
        }

        /// <summary>直射光颜色：正午近白，晨昏按霞光色转暖。</summary>
        public static Color SunColor(ref LightEnv e)
        {
            if (e.kelvinSun) return ToneMap.SunColorFor(e.sunDir);
            return Color.Lerp(Color.white, Normalize(e.sunGlowHorizon), Mathf.Clamp01(e.twilight));
        }

        /// <summary>
        /// 月夜色：偏冷的近白，不取夜空色相。
        /// 调光板归一后夜空几乎是纯蓝（Normalize 把「很暗」当「满亮度」重放），
        /// 乘上去只有蓝通道够量级，而蓝色亮度权重只有 0.0722 —— 明暗差被下限吃掉，
        /// 模拟台量到朝月面与背月面 Δ=0，等于夜里根本没有月光方向。
        /// 月光实际是弱而偏冷的直射白，夜空色相由环境项承担。
        /// </summary>
        public static Color MoonColor()
        {
            return new Color(0.78f, 0.85f, 1.0f, 1f);
        }

        /// <summary>向阳项。softFoot==softTop 时退化成硬分档（赛璐璐风格层）。</summary>
        public static float SunShape(float ndl, ref LightTuning t)
        {
            float a = t.softFoot, b = t.softTop;
            if (Mathf.Abs(b - a) < 0.001f) return ndl >= a ? 1f : 0f;
            float k = Mathf.Clamp01((ndl - a) / (b - a));
            return k * k * (3f - 2f * k);
        }

        /// <summary>灯具项：原色 × 原版增益 × 强度。</summary>
        /// <summary>夜视权重：天越黑、灯越弱越接近 1。</summary>
        public static float NightWeight(ref LightEnv e, Color lampTerm)
        {
            float lamp = Mathf.Clamp01(ToneMap.Luma(lampTerm) * 2f);
            return Mathf.Clamp01(1f - e.glow * 2f) * (1f - lamp);
        }

        public static Color LampColor(Color raw, ref LightTuning t)
        {
            return new Color(raw.r, raw.g, raw.b, 1f) * (t.lampGain * t.lampStrength);
        }

        /// <summary>下限与截断。下限按每通道取，保留灯具色相。</summary>
        public static Color ClampWithFloor(Color c, ref LightTuning t)
        {
            return ClampWithFloor(c, ref t, 0f);
        }

        /// <summary>
        /// 同上，带夜视权重（0 白天 .. 1 深夜无灯）。toneMap 开时走 F016 胶片色调链，
        /// 下限仍最后逐通道取（保留灯具色相）。
        /// </summary>
        public static Color ClampWithFloor(Color c, ref LightTuning t, float nightW)
        {
            if (t.toneMap)
            {
                var o = ToneMap.Apply(c, ref t, nightW);
                float fl = t.floor;
                o.r = Mathf.Max(o.r, fl); o.g = Mathf.Max(o.g, fl); o.b = Mathf.Max(o.b, fl);
                return o;
            }
            float f = t.floor, ex = t.exposure;
            c.r = Mathf.Clamp(Mathf.Max(c.r * ex, f), 0f, 1f);
            c.g = Mathf.Clamp(Mathf.Max(c.g * ex, f), 0f, 1f);
            c.b = Mathf.Clamp(Mathf.Max(c.b * ex, f), 0f, 1f);
            c.a = 1f;
            return c;
        }

        /// <summary>逐顶点合成：环境 + 直射 + 月夜 + 灯具，再过曝光与下限。</summary>
        public static Color Shade(ILightField f, Vector3 p, Vector3 n, bool ao,
            ref LightEnv e, ref LightTuning t)
        {
            float vis = f.SkyVisAt(p.x, p.z);
            float aoK = 1f;
            if (ao && t.aoStrength > 0f)
            {
                aoK = 1f - t.aoStrength * f.OccFraction(p, n);
                if (aoK < t.aoMin) aoK = t.aoMin;
            }

            float hemi = 0.5f + 0.5f * n.y;
            var col = AmbientColor(ref e, ref t, hemi) * (e.glow * vis * aoK);

            if (e.glow > 0.001f && e.sunDir.y > 0f)
            {
                float shape = SunShape(Vector3.Dot(n, e.sunDir), ref t);
                if (shape > 0f)
                    col += SunColor(ref e) * (shape * e.glow * t.sunStrength * f.SunLitAt(p, n) * aoK);
            }

            // 月光与天光同属天空光源：厚屋顶把它挡掉，否则屋里屋外一个亮法。
            if (e.glow < 0.5f && e.moonDir.y > 0f && vis > 0f)
            {
                float mndl = Mathf.Max(0f, Vector3.Dot(n, e.moonDir));
                col += MoonColor() * (mndl * t.moonStrength * (1f - e.glow) * vis * aoK);
            }

            if (e.auroraAmbient.r + e.auroraAmbient.g + e.auroraAmbient.b > 0f && vis > 0f)
                col += e.auroraAmbient * (vis * aoK * Mathf.Clamp01(0.5f + 0.5f * n.y));
            var lampC = LampColor(f.LampAt(p.x, p.z), ref t);
            col += lampC;
            return ClampWithFloor(col, ref t, NightWeight(ref e, lampC));
        }

        /// <summary>
        /// 逐件对象 / 实例批的放置亮度：该处「平均有多亮」，不带方向。
        /// 漫反射凸体的有效受光积分 —— 半球项取 0.70（上半球占主导），直射项取 0.62（向阳面与散射透光），
        /// 使得正午向阳处落在 ~0.54（低于朝上面 ~0.77 留出余量，同时保证树冠/草叶/人物通透鲜亮），
        /// 方向感由原型网格的形体明暗（FormShape）负责。
        /// </summary>
        public static Color Placement(ILightField f, Vector3 p, ref LightEnv e, ref LightTuning t)
        {
            float vis = f.SkyVisAt(p.x, p.z);
            var col = AmbientColor(ref e, ref t, 0.70f) * (e.glow * vis);
            if (e.glow > 0.001f && e.sunDir.y > 0f)
                col += SunColor(ref e) * (e.glow * t.sunStrength * 0.62f * f.SunLitAt(p, Vector3.up));
            if (e.glow < 0.5f && e.moonDir.y > 0f && vis > 0f)
                col += MoonColor() * (t.moonStrength * (1f - e.glow) * 0.45f * vis);
            var lampP = LampColor(f.LampAt(p.x, p.z), ref t);
            col += lampP;
            return ClampWithFloor(col, ref t, NightWeight(ref e, lampP));
        }

        /// <summary>
        /// 原型网格的形体明暗：围绕 1.0 调制，朝上参考面正好是 1。
        /// formLightDir 是固定风格光方向（与手持终端同一口径），刻意不跟太阳走 ——
        /// 原型网格是按图共享的缓存，跟太阳就得按档清缓存重烘，帧时间会崩。
        /// </summary>
        public static Color FormShape(Vector3 n, ref LightEnv e, ref LightTuning t,
            Vector3 formLightDir, float sunWeight, float ambWeight, float min, float max)
        {
            float glow = Mathf.Clamp01(e.glow);
            float dirUp = Vector3.Dot(Vector3.up, formLightDir);
            float s = 1f
                + glow * t.sunStrength * sunWeight * (SunShape(Vector3.Dot(n, formLightDir), ref t) - SunShape(dirUp, ref t))
                + glow * ambWeight * ((0.5f + 0.5f * n.y) - 1f);
            s = Mathf.Clamp(s, min, max);
            return new Color(s, s, s, 1f);
        }
    }
}
