using System;
using UnityEngine;
using RimFirstPerson3D;

// ============================================================
// F016 光影包级画面：纯函数判据（与游戏同一份源码，csproj 链接）
// ============================================================
// 每条判据带反向对照：关掉该层或把参数反过来，判据必须报。
// 规范：docs/features/F016-shaderpack-look.md

namespace RimFirstPerson3D.LightSim
{
    static class ShaderpackChecks
    {
        static void Check(string n, bool ok, string d) { Program.Check(n, ok, d); }
        static float Luma(Color c) { return Program.Luma(c); }

        /// <summary>游戏默认的 F016 调色（与 Lighting3D.BeginMap 填的默认值一致）。</summary>
        internal static LightTuning Tuned()
        {
            var t = Program.Tuning();
            t.toneMap = true;
            t.exposure = ShaderpackDefaults.Exposure;
            t.whitePoint = ShaderpackDefaults.WhitePoint;
            t.highlightDesat = ShaderpackDefaults.HighlightDesat;
            t.splitTone = ShaderpackDefaults.SplitTone;
            t.shadowTint = ShaderpackDefaults.ShadowTint;
            t.highlightTint = ShaderpackDefaults.HighlightTint;
            t.saturation = ShaderpackDefaults.Saturation;
            t.nightDesat = ShaderpackDefaults.NightDesat;
            return t;
        }

        public static void Run(SyntheticField f)
        {
            Console.WriteLine("-- F016 L1 胶片色调 --");
            L1(f);
            Console.WriteLine("-- F016 L2 平滑光照 --");
            L2();
            Console.WriteLine("-- F016 L3 平面投影阴影 --");
            L3();
            Console.WriteLine("-- F016 L4 精灵泛光 --");
            L4();
            Console.WriteLine("-- F016 L5 大气 --");
            L5();
            Console.WriteLine("-- F016 L6 水面 --");
            L6();
            Console.WriteLine("-- F016 L7 天空与天气 --");
            L7(f);
            SkyChecks.Run();
            Console.WriteLine("-- F016 L9 屏幕层 --");
            L9();
        }

        static void L9()
        {
            Check("100 出门（暗→亮）给正脉冲、进门给负脉冲、门口薄屋顶不触发",
                ScreenFxMath.Jump(0f, 1f) > 0f && ScreenFxMath.Jump(1f, 0f) < 0f && ScreenFxMath.Jump(1f, 0.65f) == 0f, "");
            Check("101 适应脉冲：起点满、到时长归零、不回升",
                Mathf.Abs(ScreenFxMath.Adapt(0f, 1.2f, 1f) - 1f) < 1e-5f && ScreenFxMath.Adapt(1.2f, 1.2f, 1f) == 0f
                && ScreenFxMath.Adapt(0.3f, 1.2f, 1f) > ScreenFxMath.Adapt(0.6f, 1.2f, 1f), "");
            Check("101a 负向脉冲符号保持", ScreenFxMath.Adapt(0.1f, 0.8f, -1f) < 0f, "");
            Check("102 常驻暗角与受击暗角取大不相加", ScreenFxMath.BaseVignette(0.18f, 0f) == 0.18f
                && ScreenFxMath.BaseVignette(0.18f, 0.5f) == 0f && Mathf.Abs(ScreenFxMath.BaseVignette(0.18f, 0.1f) - 0.08f) < 1e-5f, "");
        }

        static void L7(SyntheticField f)
        {
            var sun = new Vector3(0.9f, 0.1f, 0f).normalized;
            var gh = new Color(1f, 0.44f, 0.14f);
            var nearSun = WeatherMath.CloudVertex(new Vector3(0.95f, 0.2f, 0f).normalized, sun, gh, 1f, 0f);
            var away = WeatherMath.CloudVertex(new Vector3(-0.95f, 0.2f, 0f).normalized, sun, gh, 1f, 0f);
            Check("90 云银边：靠近太阳的云亮于背阳云", Luma(nearSun) > Luma(away) + 0.03f, "near=" + nearSun + " away=" + away);
            var zen = WeatherMath.CloudVertex(Vector3.up, sun, gh, 1f, 0f);
            var hor = WeatherMath.CloudVertex(new Vector3(0f, 0.05f, 1f).normalized, sun, gh, 1f, 0f);
            Check("90a 近地平线（云底）暗于天顶", Luma(hor) < Luma(zen) - 0.1f, "");
            var dusk = WeatherMath.CloudVertex(new Vector3(0.95f, 0.1f, 0f).normalized, sun, gh, 0.5f, 1f);
            Check("90b 晨昏时朝阳低云染霞光（r > b）", dusk.r > dusk.b * 1.4f, "dusk=" + dusk);
            Check("90c 打光乘子不超过 1（只压不提，乘在云色上不会溢）", nearSun.r <= 1f && zen.r <= 1f, "");
            Check("91 室内看不见雨幕、露天大雨看得见", WeatherMath.RainCurtain(1f, 0f, 0f) == 0f && WeatherMath.RainCurtain(1f, 0f, 1f) > 0.3f, "");
            Check("92 夜里没有云影、晴天也没有", WeatherMath.CloudShadow(1f, 0f, -0.3f) == 0f && WeatherMath.CloudShadow(0f, 1f, 0.6f) == 0f
                && WeatherMath.CloudShadow(0.8f, 1f, 0.6f) > 0.1f, "");
            // 极光：进合成式，只加露天
            var t = Tuned();
            var night = Program.Day(); night.glow = 0f; night.sunDir = new Vector3(0f, -1f, 0f); night.moonDir = new Vector3(0f, -1f, 0f);
            night.skyZenith = new Color(0.012f, 0.024f, 0.072f); night.skyHorizon = new Color(0.062f, 0.100f, 0.185f);
            f.LampsOn = false;
            var t0 = t; t0.floor = 0f;
            var plain = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref night, ref t0);
            var au = night; au.auroraAmbient = WeatherMath.AuroraAmbient(1f);
            var lit = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref au, ref t0);
            var roomAu = LightShade.Shade(f, new Vector3(19f, 0f, 5f), Vector3.up, false, ref au, ref t0);
            var roomNo = LightShade.Shade(f, new Vector3(19f, 0f, 5f), Vector3.up, false, ref night, ref t0);
            Check("93 极光夜露天地面偏绿、变亮", lit.g > plain.g + 0.02f && lit.g > lit.r, "plain=" + plain + " aurora=" + lit);
            Check("93a 厚屋顶下不受极光（反向对照）", Mathf.Abs(roomAu.g - roomNo.g) < 1e-4f, "");
            Check("94 闪电全屏闪封顶 0.32", WeatherMath.FlashScreen(5f) <= 0.32f + 1e-5f && WeatherMath.FlashScreen(0f) == 0f, "");
            var ld = WeatherMath.LightningDir(3);
            Check("94a 闪电方向高仰角、换一道闪电换一个方位", ld.y > 0.8f && (WeatherMath.LightningDir(4) - ld).magnitude > 0.1f, "");
        }

        static void L6()
        {
            float f0 = ShaderpackDefaults.WaterF0;
            Check("80 菲涅尔：垂直看 = F0、掠射 → 1、单调", Mathf.Abs(WaterMath.Fresnel(1f, f0) - f0) < 1e-5f
                && WaterMath.Fresnel(0f, f0) > 0.99f && WaterMath.Fresnel(0.3f, f0) > WaterMath.Fresnel(0.8f, f0), "");
            var tint = new Color(0.10f, 0.22f, 0.30f, 0.6f); var lit = new Color(0.8f, 0.8f, 0.8f);
            var sky = new Color(0.6f, 0.75f, 0.95f);
            var down = WaterMath.Shade(tint, lit, sky, WaterMath.Fresnel(0.95f, f0), Color.white, 0f, 1f);
            var graze = WaterMath.Shade(tint, lit, sky, WaterMath.Fresnel(0.05f, f0), Color.white, 0f, 1f);   // 眼高 1.62 m 看 30 m 外 ≈ cos 0.05
            Check("81 低头看透（alpha 低、颜色是水体），远看是镜子（alpha 高、颜色是天）",
                down.a < 0.65f && graze.a > 0.85f && Luma(graze) > Luma(down) + 0.2f, "down=" + down + " graze=" + graze);
            var r = WaterMath.Reflect(new Vector3(0.6f, -0.8f, 0f), Vector3.up);
            Check("82 反射方向对称（入射向下 → 出射向上）", Mathf.Abs(r.x - 0.6f) < 1e-5f && Mathf.Abs(r.y - 0.8f) < 1e-5f, "r=" + r);
            var sun = new Vector3(0f, 0.3f, 1f).normalized;
            var cam = new Vector3(0f, 1.62f, 0f);
            Check("83 太阳反射点在相机朝太阳方向的水面上", WaterMath.GlintPoint(cam, 0f, sun, out var gp) && gp.z > 0f && Mathf.Abs(gp.y) < 1e-4f,
                "p=" + gp);
            var toG = (gp - cam).normalized;
            var rg = WaterMath.Reflect(toG, Vector3.up);
            Check("83a 从相机看反射点，反射方向正对太阳（高光最大）", WaterMath.Glint(rg, sun, 200f) > 0.99f, "");
            Check("83b 夜里（太阳在地平线下）没有亮带", !WaterMath.GlintPoint(cam, 0f, -sun, out _), "");
            var mm = WaterMath.MirrorY(0.5f);
            var mp = mm.MultiplyPoint(new Vector3(1f, 2f, 3f));
            Check("84 镜像矩阵把 y=2 映到 y=-1（关于 y=0.5）", Mathf.Abs(mp.y + 1f) < 1e-5f && mp.x == 1f && mp.z == 3f, "p=" + mp);
            var shadowed = WaterMath.Shade(tint, lit, sky, 0.1f, Color.white, 1f, 0f);
            var sunny = WaterMath.Shade(tint, lit, sky, 0.1f, Color.white, 1f, 1f);
            Check("85 墙影里的水面没有太阳高光", Luma(sunny) > Luma(shadowed) + 0.3f, "");
            var projection = Matrix4x4.Perspective(72f, 1.6f, 0.1f, 500f);
            var near = projection * new Vector4(0f, 0f, -0.1f, 1f);
            var far = projection * new Vector4(0f, 0f, -500f, 1f);
            Check("86 reverse depth endpoints", Mathf.Abs(near.z / near.w - 1f) < 1e-5f && Mathf.Abs(far.z / far.w) < 1e-5f, "");
            bool valid = WaterMath.TryOblique(projection, new Vector4(0f, 0f, -1f, -20f), out var oblique);
            var before = oblique * new Vector4(0f, 0f, -10f, 1f);
            var on = oblique * new Vector4(0f, 0f, -20f, 1f);
            var after = oblique * new Vector4(0f, 0f, -30f, 1f);
            Check("86a reverse depth near clipping", valid && before.z > before.w && Mathf.Abs(on.z - on.w) < 1e-4f && after.z >= 0f && after.z < after.w, "");
            var plain = projection * new Vector4(0f, 0f, -10f, 1f);
            Check("86b unclipped inverse control", plain.z >= 0f && plain.z <= plain.w, "");
            bool angled = WaterMath.TryOblique(projection, new Vector4(0f, 1f, -0.2f, -2f), out var tilted);
            var below = tilted * new Vector4(0f, -1f, -10f, 1f);
            var above = tilted * new Vector4(0f, 1f, -10f, 1f);
            Check("86c tilted retained half-space", angled && below.z > below.w && above.z >= 0f && above.z < above.w, "");
            Check("86d invalid clipping inputs", !WaterMath.TryOblique(projection, Vector4.zero, out _)
                && !WaterMath.TryOblique(Matrix4x4.zero, new Vector4(0f, 0f, -1f, -20f), out _)
                && !WaterMath.TryOblique(projection, new Vector4(float.NaN, 0f, -1f, 0f), out _), "");
            var flipped = WaterMath.FlipProjectionX(projection);
            bool u0 = WaterMath.TryViewport(projection, new Vector3(2f, 1f, -10f), out var uv0);
            bool u1 = WaterMath.TryViewport(flipped, new Vector3(2f, 1f, -10f), out var uv1);
            Check("87 mirrored projection UV", u0 && u1 && Mathf.Abs(uv0.x + uv1.x - 1f) < 1e-5f && Mathf.Abs(uv0.y - uv1.y) < 1e-5f, "");
            Check("87a winding compensation determinant", Matrix4x4.ToNumerics(mm).GetDeterminant() < 0f
                && Matrix4x4.ToNumerics(flipped).GetDeterminant() * Matrix4x4.ToNumerics(projection).GetDeterminant() < 0f, "");
            var previous = flipped * Matrix4x4.Translate(new Vector3(-1f, 0f, 0f));
            WaterMath.TryViewport(previous, new Vector3(2f, 1f, -10f), out var previousUv);
            Check("87b previous view projection differs after movement", Mathf.Abs(previousUv.x - uv1.x) > 0.01f, "");
            Check("87c behind-camera sampling rejected", !WaterMath.TryViewport(projection, new Vector3(0f, 0f, 10f), out _), "");
        }

        static void L5()
        {
            float r0 = ShaderpackDefaults.FogR0, q = ShaderpackDefaults.FogQ; int n = ShaderpackDefaults.FogShells;
            float sig = 0.03f;
            float worst = 0f;
            for (int k = 0; k < n; k++)
            {
                float r = FogMath.ShellRadius(k, r0, q);
                float tr = FogMath.Transmittance(r + 0.01f, sig, n, r0, q);
                worst = Mathf.Max(worst, Mathf.Abs(tr - Mathf.Exp(-sig * r)));
            }
            Check("70 雾壳在壳半径处的透过率 = exp(-σr)", worst < 1e-3f, "maxErr=" + worst.ToString("0.00000"));
            Check("70a 最外壳半径 ≈ 135 m", Mathf.Abs(FogMath.ShellRadius(n - 1, r0, q) - 135f) < 15f, "r=" + FogMath.ShellRadius(n - 1, r0, q).ToString("0.0"));
            Check("70b 10 m 内不受雾（第一层壳之内）", FogMath.Transmittance(9f, sig, n, r0, q) == 1f, "");
            float tNear = FogMath.Transmittance(30f, sig, n, r0, q), tFar = FogMath.Transmittance(120f, sig, n, r0, q);
            Check("71 越远越被吞", tFar < tNear - 0.2f, "30m=" + tNear.ToString("0.00") + " 120m=" + tFar.ToString("0.00"));
            Check("71a 零密度完全透明（反向对照）", FogMath.Transmittance(120f, 0f, n, r0, q) == 1f, "");
            Check("72 高度雾：地面 1、6 m 处 1/e、越高越淡",
                FogMath.HeightFactor(0f, 0f, 6f) == 1f && Mathf.Abs(FogMath.HeightFactor(6f, 0f, 6f) - Mathf.Exp(-1f)) < 1e-4f
                && FogMath.HeightFactor(20f, 0f, 6f) < FogMath.HeightFactor(6f, 0f, 6f), "");
            float continuousError = 0f, maximumStep = 0f;
            float previousT = 1f;
            for (int i = 1; i <= 30000; i++)
            {
                float d = i * 0.01f;
                float t = FogMath.ContinuousTransmittance(d, sig, 0f, 0f, 6f);
                continuousError = Mathf.Max(continuousError, Mathf.Abs(t - Mathf.Exp(-sig * d)));
                maximumStep = Mathf.Max(maximumStep, Mathf.Abs(t - previousT));
                previousT = t;
            }
            Check("72a continuous distance fog matches Beer-Lambert between shell radii", continuousError < 1e-6f, "maxErr=" + continuousError);
            Check("72b continuous fog has no shell boundary jump", maximumStep < sig * 0.01f + 1e-6f, "maxStep=" + maximumStep);
            Check("72c legacy shell discontinuity negative control",
                Mathf.Abs(FogMath.Transmittance(10.001f, sig, n, r0, q) - FogMath.Transmittance(9.999f, sig, n, r0, q)) > 0.25f, "");
            Check("72d horizontal height integral", Mathf.Abs(FogMath.OpticalDepth(100f, sig, 6f, 0f, 6f) - sig * 100f / Mathf.Exp(1f)) < 1e-5f, "");
            float groundCrossing = sig * (6f * (1f - Mathf.Exp(-1f)) / 0.5f + 8f);
            Check("72e descending ray crosses ground continuously", Mathf.Abs(FogMath.OpticalDepth(20f, sig, 6f, -0.5f, 6f) - groundCrossing) < 1e-5f, "");
            Check("72f underground upward ray", Mathf.Abs(FogMath.OpticalDepth(20f, sig, -6f, 0.5f, 6f)
                - sig * (12f + 12f * (1f - Mathf.Exp(-4f / 6f)))) < 1e-5f, "");
            Check("72g tall descending ray has no exponential overflow", Mathf.Abs(FogMath.OpticalDepth(40000f, 0.02f, 20000f, -0.5f, 200f) - 8f) < 1e-5f, "");
            Check("72h upward ray converges to finite column density", Mathf.Abs(FogMath.OpticalDepth(100000f, 0.02f, 20f, 0.8f, 200f) - 5f * Mathf.Exp(-0.1f)) < 1e-5f, "");
            float integralError = 0f;
            foreach (float height in new[] { -2f, 0f, 2.2f, 6f, 100f })
                foreach (float dy in new[] { -1f, -0.15f, -0.000001f, 0f, 0.000001f, 0.15f, 1f })
                    foreach (float distance in new[] { 0.001f, 5f, 60f, 150f })
                    {
                        double reference = 0;
                        const int samples = 4096;
                        for (int j = 0; j < samples; j++)
                            reference += System.Math.Exp(-System.Math.Max(0, height + dy * distance * (j + 0.5) / samples) / 6.0);
                        reference *= sig * distance / samples;
                        integralError = Mathf.Max(integralError, Mathf.Abs(FogMath.OpticalDepth(distance, sig, height, dy, 6f) - (float)reference));
                    }
            Check("72i height integral agrees with numerical quadrature", integralError < 2e-5f, "maxErr=" + integralError);
            Check("72j zero density and zero distance stay transparent", FogMath.ContinuousTransmittance(120f, 0f, 2.2f, -0.5f, 6f) == 1f
                && FogMath.ContinuousTransmittance(0f, sig, 2.2f, 0f, 6f) == 1f, "");
            float horizontal = FogMath.OpticalDepth(120f, sig, 2.2f, 0f, 6f);
            Check("72k near horizontal rays remain continuous", Mathf.Abs(FogMath.OpticalDepth(120f, sig, 2.2f, 1e-7f, 6f) - horizontal) < 1e-5f
                && Mathf.Abs(FogMath.OpticalDepth(120f, sig, 2.2f, -1e-7f, 6f) - horizontal) < 1e-5f, "");
            float depthError = 0f;
            var fogProjection = Matrix4x4.Perspective(72f, 16f / 9f, 0.1f, 1200f);
            foreach (var point in new[] { new Vector3(0f, 0f, -0.1f), new Vector3(2f, 3f, -10f), new Vector3(-20f, -4f, -120f), new Vector3(0f, 0f, -1200f) })
            {
                var clip = fogProjection * new Vector4(point.x, point.y, point.z, 1f);
                var uv = new Vector2((clip.x / clip.w + 1f) * 0.5f, (clip.y / clip.w + 1f) * 0.5f);
                depthError = Mathf.Max(depthError, Mathf.Abs(FogMath.DepthDistance(fogProjection, uv, clip.z / clip.w) - point.magnitude));
                var ray = FogMath.ViewRay(fogProjection, uv);
                Check("72l Vulkan top-down reconstruction " + point, Mathf.Abs(ray.x - point.x / -point.z) < 1e-5f && Mathf.Abs(ray.y - point.y / -point.z) < 1e-5f, "");
            }
            Check("72m reverse depth reconstructs ray distance", depthError < 0.001f, "maxErr=" + depthError);
            float crossingBefore = FogMath.ContinuousTransmittance(11.999f, sig, 6f, -0.5f, 6f);
            float crossingAfter = FogMath.ContinuousTransmittance(12.001f, sig, 6f, -0.5f, 6f);
            Check("72n ground crossing has no opacity seam", crossingBefore >= crossingAfter && crossingBefore - crossingAfter < 0.0001f, "");
            fogProjection.m02 = 0.15f;
            fogProjection.m12 = -0.2f;
            var shiftedPoint = new Vector4(2f, 3f, -10f, 1f);
            var shiftedClip = fogProjection * shiftedPoint;
            var shiftedUv = new Vector2((shiftedClip.x / shiftedClip.w + 1f) * 0.5f, (shiftedClip.y / shiftedClip.w + 1f) * 0.5f);
            var shiftedRay = FogMath.ViewRay(fogProjection, shiftedUv);
            Check("72o shifted projection reconstructs the same ray", Mathf.Abs(shiftedRay.x - 0.2f) < 1e-5f && Mathf.Abs(shiftedRay.y - 0.3f) < 1e-5f, "");
            Check("73 米氏前向散射：朝太阳最亮、背太阳最暗，g=0 退化为各向同性",
                FogMath.Mie(1f, 0.7f) > FogMath.Mie(0f, 0.7f) && FogMath.Mie(0f, 0.7f) > FogMath.Mie(-1f, 0.7f)
                && Mathf.Abs(FogMath.Mie(0.3f, 0f) - 1f) < 1e-4f, "fwd=" + FogMath.Mie(1f, 0.7f).ToString("0.0"));
            var hor = new Color(0.68f, 0.84f, 0.97f); var sun = new Color(1f, 0.6f, 0.3f);
            var sd = new Vector3(1f, 0.1f, 0f).normalized;
            var toward = FogMath.FogColor(hor, sun, sd, sd, 0.6f, 1f);
            var away = FogMath.FogColor(hor, sun, -sd, sd, 0.6f, 1f);
            Check("74 朝太阳的雾更暖（r/b 更大）", toward.r / toward.b > away.r / away.b + 0.05f, "toward=" + toward + " away=" + away);
            var night = FogMath.FogColor(hor, sun, sd, -sd, 0f, 0f);
            Check("74a 夜里雾色就是地平色（不叠太阳）", Mathf.Abs(night.r - hor.r) < 1e-3f && Mathf.Abs(night.b - hor.b) < 1e-3f, "");
            Check("75 室内雾密度为室外 0.2", Mathf.Abs(FogMath.IndoorK(0f, 0.2f) - 0.2f) < 1e-4f && FogMath.IndoorK(1f, 0.2f) == 1f, "");
            Check("76 雾天远浓于晴天", FogMath.Density(0.01f, 1f, 0f, 0f, 0f) > 5f * FogMath.Density(0.01f, 0f, 0f, 0f, 0f), "");
            Check("77 光束：雾浓更显、夜里没有",
                FogMath.ShaftIntensity(0.65f, 1f, 0.1f, 0.01f) > FogMath.ShaftIntensity(0.65f, 1f, 0.01f, 0.01f)
                && FogMath.ShaftIntensity(0.65f, 0f, 0.1f, 0.01f) == 0f, "");
            Check("78 屋顶开洞判定：露天且两邻有顶才算；实心/本格有顶/邻顶不足都不算",
                FogMath.IsRoofHole(false, false, 2) && !FogMath.IsRoofHole(true, false, 4)
                && !FogMath.IsRoofHole(false, true, 4) && !FogMath.IsRoofHole(false, false, 1), "");
        }

        static void L4()
        {
            // 篝火 GlowRadius 10，2 m 外，FOV 72，1080p：旧版一团占四分之一屏，新版不超过 12%
            float hs = FlareMath.HaloWorldSize(10f, ShaderpackDefaults.HaloWorldK, 2f, 72f, 1080f, 6f, 0.12f);
            float px = FlareMath.ScreenPx(hs, 2f, 72f, 1080f);
            Check("60 近处篝火光晕不超过屏高 12%", px <= 1080f * 0.12f + 0.5f, "px=" + px.ToString("0.0"));
            float hsFar = FlareMath.HaloWorldSize(3f, ShaderpackDefaults.HaloWorldK, 120f, 72f, 1080f, 6f, 0.12f);
            Check("60a 远处小灯光晕不小于 6 px", FlareMath.ScreenPx(hsFar, 120f, 72f, 1080f) >= 5.99f, "");
            float raw = 10f * ShaderpackDefaults.HaloWorldK;
            Check("60b 不夹时近处会超上限（反向对照）", FlareMath.ScreenPx(raw * 4f, 2f, 72f, 1080f) > 1080f * 0.12f, "");
            float mn = 9f, mx = -9f, sum = 0f; int n = 0;
            for (float t = 0f; t < 20f; t += 0.01f) { float v = FlareMath.Flicker(t, 1.3f, 0.16f); mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); sum += v; n++; }
            Check("61 闪烁在 1±amp 内、均值≈1", mn >= 0.84f - 1e-3f && mx <= 1.16f + 1e-3f && Mathf.Abs(sum / n - 1f) < 0.03f,
                "min=" + mn.ToString("0.000") + " max=" + mx.ToString("0.000") + " mean=" + (sum / n).ToString("0.000"));
            Check("61a 闪烁确实在动（不是常数）", mx - mn > 0.05f, "");
            float v0 = 0f; for (int i = 0; i < 10; i++) v0 = FlareMath.Approach(v0, 1f, 0.02f, 0.05f);
            Check("62 可见度 0.2 s 内基本到位", v0 > 0.95f, "v=" + v0.ToString("0.000"));
            var c = FlareMath.GhostPos(new Vector2(0.8f, 0.7f), 1);
            Check("63 鬼影在太阳与屏幕中心连线上、过了中心", c.x < 0.5f && c.y < 0.5f, "g=" + c.x.ToString("0.00") + "," + c.y.ToString("0.00"));
            Check("65 随身光源衰减：光心 0.5、半径处 0、单调", Mathf.Abs(FlareMath.PointFalloff(0f, 0f, 6f) - 0.5f) < 1e-5f
                && FlareMath.PointFalloff(6f, 0f, 6f) == 0f && FlareMath.PointFalloff(2f, 0f, 6f) > FlareMath.PointFalloff(4f, 0f, 6f), "");
            Check("64 太阳出屏/被挡/阴天没有眩光",
                FlareMath.GlareGain(new Vector2(1.6f, 0.5f), 1f, 0f, 0.5f) == 0f && FlareMath.GlareGain(new Vector2(0.5f, 0.5f), 0f, 0f, 0.5f) == 0f
                && FlareMath.GlareGain(new Vector2(0.5f, 0.5f), 1f, 1f, 0.5f) == 0f && FlareMath.GlareGain(new Vector2(0.5f, 0.5f), 1f, 0f, 0.5f) > 0.5f, "");
        }

        static void L2()
        {
            Check("46 MC 角 AO：两侧都挡 = 3（即使角邻空）", AoMath.CornerLevel(true, true, false) == 3, "");
            Check("46a 只挡一侧 + 角 = 2，全空 = 0", AoMath.CornerLevel(true, false, true) == 2 && AoMath.CornerLevel(false, false, false) == 0, "");
            Check("46b 共享顶点：对角都挡 = 满，邻边两格只有 2/3",
                AoMath.SharedVertex(true, false, false, true) == 1f && Mathf.Abs(AoMath.SharedVertex(true, true, false, false) - 2f / 3f) < 1e-4f, "");
            // 一个暗角：沿 0-2 切会把暗拖成对角线；翻转后改 1-3 切
            var tris = new[] { 0, 1, 2, 0, 2, 3 };
            var cols = new[] { new Color32(40, 40, 40, 255), new Color32(220, 220, 220, 255), new Color32(220, 220, 220, 255), new Color32(220, 220, 220, 255) };
            int fl = AoMath.FlipQuads(tris, cols);
            Check("47 单个暗角：翻到 1-3 对角", fl == 1 && tris[2] == 3 && tris[3] == 1 && tris[4] == 2 && tris[5] == 3, string.Join(",", tris));
            var tris2 = new[] { 0, 1, 2, 0, 2, 3 };
            var cols2 = new[] { new Color32(220, 220, 220, 255), new Color32(40, 40, 40, 255), new Color32(220, 220, 220, 255), new Color32(220, 220, 220, 255) };
            Check("47a 暗角在 1：原本的 0-2 切法正确，不翻（反向对照）", AoMath.FlipQuads(tris2, cols2) == 0 && tris2[2] == 2, string.Join(",", tris2));
            int again = AoMath.FlipQuads(tris, cols);
            Check("47b 已翻好的再跑一遍不来回翻", again == 0, "");
            var fan = new[] { 0, 1, 8, 1, 2, 8 };
            Check("47c 扇形图元不动", AoMath.FlipQuads(fan, new Color32[9]) == 0 && fan[2] == 8, "");
            // 半影：墙脚（1 步）边锐，6 步外边虚
            float wNear = AoMath.PenumbraWidth(1f, ShaderpackDefaults.PenumbraK, ShaderpackDefaults.PenumbraMin, ShaderpackDefaults.PenumbraMax);
            float wFar = AoMath.PenumbraWidth(6f, ShaderpackDefaults.PenumbraK, ShaderpackDefaults.PenumbraMin, ShaderpackDefaults.PenumbraMax);
            Check("48 墙脚半影 < 0.3 m、6 m 外 > 0.8 m（远宽于近）", wNear < 0.3f && wFar > 0.8f, "near=" + wNear.ToString("0.00") + " far=" + wFar.ToString("0.00"));
            Check("48a 软化后 0.2 m 处：近处已全亮，远处还在过渡", AoMath.SoftEdge(0.2f, wNear) == 1f && AoMath.SoftEdge(0.2f, wFar) < 1f, "");
        }

        static void L3()
        {
            var L = new Vector3(0.55f, 0.60f, -0.45f).normalized;
            const float g = 0.01f;
            var m = ShadowMath.Directional(L, g, 0.001f, 6f);
            var top = new Vector3(3f, 1.8f, 4f);
            var p = m.MultiplyPoint(top);
            // 沿 -L 走到地面的真值
            float tHit = (top.y - g) / L.y;
            var truth = top - L * tHit;
            Check("50 平行光投影点落在 -L 射线与地面的交点", (new Vector3(p.x, 0f, p.z) - new Vector3(truth.x, 0f, truth.z)).magnitude < 1e-3f
                && Mathf.Abs(p.y - g) < 0.01f, "p=" + p + " truth=" + truth);
            var foot = new Vector3(3f, g, 4f);
            var pf = m.MultiplyPoint(foot);
            Check("50a 脚点不动（接地处影子与物体相连）", (pf - foot).magnitude < 1e-4f, "pf=" + pf);
            var dir = new Vector3(p.x - foot.x, 0f, p.z - foot.z).normalized;
            var want = new Vector3(-L.x, 0f, -L.z).normalized;
            float ang = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, want), -1f, 1f)) * Mathf.Rad2Deg;
            Check("51 影子方向与背光方向夹角 < 3°", ang < 3f, "ang=" + ang.ToString("0.00"));
            float len = new Vector3(p.x - foot.x, 0f, p.z - foot.z).magnitude;
            float pred = ShadowMath.Length(top.y - g, L, 6f);
            Check("51a 影长与预测误差 < 5%", Mathf.Abs(len - pred) / pred < 0.05f, "len=" + len.ToString("0.000") + " pred=" + pred.ToString("0.000"));
            var low = new Vector3(0.99f, 0.02f, 0f).normalized;
            var ml = ShadowMath.Directional(low, g, 0.001f, 6f);
            var pl = ml.MultiplyPoint(new Vector3(0f, 1f + g, 0f));
            Check("52 低太阳影长被封顶（不拉到无穷远）", new Vector3(pl.x, 0f, pl.z).magnitude <= 6.01f, "len=" + new Vector3(pl.x, 0f, pl.z).magnitude.ToString("0.00"));
            var mNo = ShadowMath.Directional(low, g, 0.001f, 1000f);
            var pNo = mNo.MultiplyPoint(new Vector3(0f, 1f + g, 0f));
            Check("52b 不封顶时同一太阳影长远超封顶值（反向对照）", new Vector3(pNo.x, 0f, pNo.z).magnitude > 20f, "len=" + new Vector3(pNo.x, 0f, pNo.z).magnitude.ToString("0.0"));
            float determinant = Matrix4x4.ToNumerics(m).GetDeterminant();
            Check("53 行列式 = eps（非奇异、绕序有定义）", Mathf.Abs(determinant - 0.001f) < 1e-5f, "det=" + determinant);
            // 绕序：从上往下看的朝向只由 xz 决定（eps 改不了它），单面立牌靠「转 180° 换一面」翻上来
            var a = new Vector3(-0.5f, 0f, 0f); var b = new Vector3(0.5f, 0f, 0f); var c = new Vector3(0.5f, 1f, 0f);
            var mp = ShadowMath.Directional(new Vector3(0f, 0.6f, 0.8f), g, 0.001f, 6f);
            var mn = ShadowMath.Directional(new Vector3(0f, 0.6f, 0.8f), g, -0.001f, 6f);
            System.Func<Vector3, Vector3> r180 = v => new Vector3(-v.x, v.y, -v.z);
            Check("54 立牌转 180° 后压扁朝向翻转", ShadowMath.FacingAfter(mp, a, b, c) == -ShadowMath.FacingAfter(mp, r180(a), r180(b), r180(c)), "");
            Check("54b eps 取反不改从上看的朝向（反向对照：别指望靠 eps 翻面）",
                ShadowMath.FacingAfter(mp, a, b, c) == ShadowMath.FacingAfter(mn, a, b, c), "");

            var lamp = new Vector3(5f, 1.2f, 5f);
            var mpnt = ShadowMath.Point(lamp, g);
            var head = new Vector3(6f, 0.9f, 5f);
            var ph = mpnt.MultiplyPoint(head);
            float t2 = (lamp.y - g) / (lamp.y - head.y);
            var truth2 = lamp + (head - lamp) * t2;
            Check("55 点光投影落在 光心→点 射线与地面的交点", (ph - truth2).magnitude < 1e-3f, "p=" + ph + " truth=" + truth2);
            var pfoot = mpnt.MultiplyPoint(new Vector3(6f, g, 5f));
            Check("55a 点光下脚点不动", (pfoot - new Vector3(6f, g, 5f)).magnitude < 1e-3f, "p=" + pfoot);
            Check("55b 影子背离灯（离灯更远）", (new Vector3(ph.x, 0, ph.z) - new Vector3(lamp.x, 0, lamp.z)).magnitude > 1f, "");

            Check("56 室内没有太阳影", ShadowMath.SunAlpha(0.38f, L, 1f, 0f, 0f, 0.12f) == 0f, "");
            Check("56a 露天正午有影", ShadowMath.SunAlpha(0.38f, L, 1f, 1f, 0f, 0.12f) > 0.3f, "");
            Check("56b 夜里没有太阳影", ShadowMath.SunAlpha(0.38f, -L, 0f, 1f, 0f, 0.12f) == 0f, "");
            Check("57 灯影：白天看不见、夜里近处有、出半径为 0",
                ShadowMath.LampAlpha(0.38f, 1f, 8f, 1f, 0.8f) == 0f && ShadowMath.LampAlpha(0.38f, 1f, 8f, 0f, 0.8f) > 0.2f
                && ShadowMath.LampAlpha(0.38f, 9f, 8f, 0f, 0.8f) == 0f, "");
        }

        static void L1(SyntheticField f)
        {
            var t = Tuned();
            var day = Program.Day(); day.kelvinSun = true;
            f.LampsOn = false;
            var up = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref day, ref t);
            Check("40 正午朝上面没有任何通道 >= 0.98", up.r < 0.98f && up.g < 0.98f && up.b < 0.98f, "rgb=" + up);
            Check("40a 正午朝上面落在 150..200/255", Luma(up) * 255f >= 150f && Luma(up) * 255f <= 200f,
                "luma=" + (Luma(up) * 255f).ToString("0.0"));
            var hot = t; hot.exposure = 6f; hot.whitePoint = 0.5f; hot.highlightDesat = 0f;
            var blown = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref day, ref hot);
            Check("40b 曝光拉爆时判据必须报（反向对照）", blown.r >= 0.98f || blown.g >= 0.98f, "rgb=" + blown);

            var east = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.right, false, ref day, ref t);
            var west = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.left, false, ref day, ref t);
            float ratio = Luma(east) / Mathf.Max(1e-4f, Luma(west));
            Check("41 向阳/背阴亮度比 >= 2.5", ratio >= 2.5f, "ratio=" + ratio.ToString("0.00"));
            Check("42 背阴立面偏冷（b > r，暖阳蓝影）", west.b > west.r * 1.08f, "rgb=" + west);
            Check("42a 向阳立面偏暖（r > b）", east.r > east.b * 1.05f, "rgb=" + east);
            var noSplit = t; noSplit.splitTone = 0f;
            var day0 = Program.Day();
            var westFlat = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.left, false, ref day0, ref noSplit);
            Check("42b 关分离色调后背阴面冷度下降（反向对照）",
                westFlat.b / Mathf.Max(1e-4f, westFlat.r) < west.b / Mathf.Max(1e-4f, west.r) - 0.02f,
                "flat b/r=" + (westFlat.b / westFlat.r).ToString("0.00") + " tuned=" + (west.b / west.r).ToString("0.00"));

            // 日落档：太阳仰角 5°
            var dusk = Program.Day(); dusk.kelvinSun = true; dusk.glow = 0.55f; dusk.twilight = 0.8f;
            dusk.sunDir = new Vector3(0.99f, Mathf.Sin(5f * Mathf.Deg2Rad), 0f).normalized;
            var duskSun = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.right, false, ref dusk, ref t);
            Check("43 仰角 5° 向阳面 r/b >= 1.35", duskSun.r / Mathf.Max(1e-4f, duskSun.b) >= 1.35f, "rgb=" + duskSun);
            var k5 = ToneMap.Kelvin(ToneMap.SunKelvin(5f)); var k60 = ToneMap.Kelvin(ToneMap.SunKelvin(60f));
            Check("43a 色温随仰角单调变白", k5.b < k60.b - 0.2f && ToneMap.SunKelvin(5f) < ToneMap.SunKelvin(30f),
                "b5=" + k5.b.ToString("0.00") + " b60=" + k60.b.ToString("0.00"));

            // 夜视去饱和：深夜月光下的朝月面 vs 正午同面
            var night = Program.Day(); night.glow = 0f; night.kelvinSun = true;
            night.sunDir = new Vector3(-0.5f, -0.6f, 0.4f).normalized;
            night.moonDir = new Vector3(0.2f, 0.7f, -0.3f).normalized;
            night.skyZenith = new Color(0.012f, 0.024f, 0.072f); night.skyHorizon = new Color(0.062f, 0.100f, 0.185f);
            var bright = t; bright.floor = 0f;
            var nUp = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref night, ref bright);
            var noDesat = bright; noDesat.nightDesat = 1f;
            var nUp2 = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref night, ref noDesat);
            float sn = ToneMap.Saturation(nUp), s0 = ToneMap.Saturation(nUp2);
            Check("44 深夜无灯饱和度 < 不去饱和时的 0.6", sn < s0 * 0.6f, "desat=" + sn.ToString("0.00") + " raw=" + s0.ToString("0.00"));
            var dUp = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref day, ref bright);
            var dUp2 = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref day, ref noDesat);
            Check("44b 白天不受夜视去饱和影响（反向对照）", Mathf.Abs(ToneMap.Saturation(dUp) - ToneMap.Saturation(dUp2)) < 0.01f,
                "day=" + ToneMap.Saturation(dUp).ToString("0.00"));

            // 灯具色相保留
            f.LampsOn = true;
            var lampT = t;
            var atLamp = LightShade.Shade(f, new Vector3(18.5f, 0.8f, 4.5f), Vector3.up, false, ref night, ref lampT);
            Check("45 灯下色相仍偏红（色调链不洗掉灯色）", atLamp.r > atLamp.g * 1.5f && atLamp.r > atLamp.b * 1.5f, "rgb=" + atLamp);
            Check("45a 曲线单调：亮度越大映射越大", ToneMap.Reinhard(0.5f, 2.2f) < ToneMap.Reinhard(1f, 2.2f)
                && ToneMap.Reinhard(1f, 2.2f) < ToneMap.Reinhard(3f, 2.2f) && Mathf.Abs(ToneMap.Reinhard(2.2f, 2.2f) - 1f) < 1e-3f, "");
        }
    }
}
