using System;
using System.Collections.Generic;
using UnityEngine;
using RimFirstPerson3D;

// ============================================================
// 离线模拟场景台：F013 光照合成式 + F014 受击曲线 + F015 射击效果曲线
// ============================================================
//
// 目的：进游戏之前，先在合成场景里把着色合成式的性质跑通。
// 公式不是复制来的：LightShade.cs 由 csproj 直接链接进本程序，
// 游戏里 LightField3D.Shade / Placement 走的也是同一份代码，所以这里过 == 那边过，
// 不存在第二套规范。
//
// 合成场景（24x24 格，层高基准 0，1 格 = 1 米）：
//   全图露天平地；x=4..14, z=8 一堵墙（顶高 2.70）；x=16..21, z=2..7 一间厚屋顶房（天空遮蔽 0）；
//   房内心 (18.5,4.5) 一盏红灯笼；墙北 x=1..3, z=8..10 一块手工投影；墙脚与房角给接触阴影。
//
// 每条判据都带反向对照：把关系反过来必须报，只测一边的断言等于没测。
// 用法：dotnet run --project tools/LightSim     退出码非 0 = 未通过

namespace RimFirstPerson3D.LightSim
{
    /// <summary>合成光场：手写小表，代替游戏里的逐格缓存。</summary>
    sealed class SyntheticField : ILightField
    {
        public const float WallTop = 2.70f;
        public const float RoomMinX = 16f, RoomMaxX = 22f, RoomMinZ = 2f, RoomMaxZ = 8f;
        public static readonly Vector3 LampPos = new Vector3(18.5f, 0f, 4.5f);

        public bool LampsOn = true;
        public bool ShadowsOn = true;
        public bool AoOn = true;

        private static bool InRoom(float x, float z)
        {
            return x >= RoomMinX && x < RoomMaxX && z >= RoomMinZ && z < RoomMaxZ;
        }

        // 墙所在的一格（x 4..14, z=8）
        private static bool InWall(float x, float z)
        {
            return x >= 4f && x < 15f && z >= 8f && z < 9f;
        }

        public float SkyVisAt(float x, float z)
        {
            if (InRoom(x, z)) return 0f;          // 厚屋顶：天光为 0
            if (InWall(x, z)) return 1f;          // 墙本身按露天算（立面要吃直射）
            return 1f;
        }

        public Color LampAt(float x, float z)
        {
            if (!LampsOn) return Color.black;
            float dx = x - LampPos.x, dz = z - LampPos.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            float k = Mathf.Clamp01(1f - d / 2.5f);
            k = k * k;
            return new Color(200f / 255f, 40f / 255f, 40f / 255f, 1f) * k;
        }

        public float SunLitAt(Vector3 p, Vector3 n)
        {
            if (!ShadowsOn) return 1f;
            // 厚屋顶遮住太阳：与游戏里的同一口径（LightField3D.UpdateCell 在 skyVis==0 时把
            // sunLo/sunHi 直接置 0），否则室内地板会在正午收到直射，判据 3 测的就不是屋顶而是别的。
            if (InRoom(p.x, p.z)) return 0f;
            var q = p + n * 0.5f;
            // 墙北（-X 侧）一块手工投影：太阳在 +X,-Z 方向，影子落在 -X,+Z 一侧
            if (q.x >= 1f && q.x < 4f && q.z >= 8f && q.z <= 11f && p.y - WallTop < 2.0f) return 0f;
            return 1f;
        }

        public float OccFraction(Vector3 p, Vector3 n)
        {
            if (!AoOn) return 0f;
            // 贴墙一格的脚部、房角、以及室内靠墙角
            if (n.y > 0.5f)
            {
                if (p.z > 7f && p.z < 10f && p.x > 3f && p.x < 16f) return 0.55f;   // 墙脚地面
                if (InRoom(p.x, p.z) && (p.x < RoomMinX + 1f || p.z < RoomMinZ + 1f)) return 0.7f;
                return 0f;
            }
            // 竖直面：两端贴地/贴顶的角
            if (Mathf.Abs(n.x) > 0.5f && p.z > 7.5f && p.z < 8.5f) return 0.35f;
            return 0f;
        }
    }

    public static class Program
    {
        static readonly List<string> fails = new List<string>();
        static int passed;

        internal static LightTuning Tuning()
        {
            return new LightTuning
            {
                floor = 0.16f, exposure = 0.62f, ambGain = 0.45f, sunStrength = 1f, lampStrength = 1f,
                lampGain = 3.6f, aoStrength = 0.45f, aoMin = 0.35f,
                softFoot = -0.10f, softTop = 0.35f, ambTint = 0.55f, moonStrength = 0.45f,
            };
        }

        internal static LightEnv Day()
        {
            return new LightEnv
            {
                sunDir = new Vector3(0.55f, 0.60f, -0.45f).normalized,
                moonDir = new Vector3(0.1f, -0.8f, 0.1f).normalized,   // 白天月亮在地平线下
                glow = 1f, twilight = 0f,
                skyZenith = new Color(0.09f, 0.30f, 0.76f),
                skyHorizon = new Color(0.68f, 0.84f, 0.97f),
                sunGlowHorizon = new Color(1f, 0.44f, 0.14f),
            };
        }

        static LightEnv Night()
        {
            var e = Day();
            e.glow = 0f;
            e.sunDir = new Vector3(-0.55f, -0.6f, 0.45f).normalized;   // 太阳在地平线下
            e.moonDir = new Vector3(0.2f, 0.7f, -0.3f).normalized;
            e.skyZenith = new Color(0.012f, 0.024f, 0.072f);
            e.skyHorizon = new Color(0.062f, 0.100f, 0.185f);
            return e;
        }

        static float Luma(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { passed++; Console.WriteLine("  [ok ] " + name + "  " + detail); }
            else { fails.Add(name); Console.WriteLine("  [FAIL] " + name + "  " + detail); }
        }

        /// <summary>有序性判据：a 必须明显大于 b，并且反向必须不成立（只测一边等于没测）。</summary>
        static void CheckGreater(string name, float a, float b, float minGap)
        {
            bool fwd = a - b >= minGap;
            bool rev = b - a >= minGap;
            Check(name, fwd && !rev,
                "a=" + a.ToString("0.000") + " b=" + b.ToString("0.000") +
                (rev ? " （反向也成立 -> 判据无意义）" : ""));
        }

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            var f = new SyntheticField();
            var t = Tuning();
            var day = Day();
            var night = Night();

            Console.WriteLine("== LightSim：合成场景 + 真实公式（LightShade.cs）==");

            // --demo：先跑完判据，再把 F014/F015 的效果按帧导出一份给离线演示台渲染
            bool demo = args != null && args.Length > 0 && args[0] == "--demo";

            // 1 向阳立面 vs 背阳立面
            var east = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.right, false, ref day, ref t);
            var west = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.left, false, ref day, ref t);
            CheckGreater("1 正午向阳立面亮于背阳立面", Luma(east), Luma(west), 0.15f);

            // 2 朝上 vs 朝下
            var up = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref day, ref t);
            var down = LightShade.Shade(f, new Vector3(2f, 2.7f, 2f), Vector3.down, false, ref day, ref t);
            CheckGreater("2 正午朝上面亮于朝下面", Luma(up), Luma(down), 0.15f);

            // 3 露天 vs 厚屋顶室内（同一时刻、无灯）
            f.LampsOn = false;
            var open = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref day, ref t);
            var roofed = LightShade.Shade(f, new Vector3(19f, 0f, 5f), Vector3.up, false, ref day, ref t);
            CheckGreater("3 厚屋顶室内暗于露天", Luma(open), Luma(roofed), 0.20f);
            f.LampsOn = true;

            // 4 灯下亮于同室无灯处，且保留灯具色相
            var atLamp = LightShade.Shade(f, new Vector3(18.5f, 0.8f, 4.5f), Vector3.up, false, ref night, ref t);
            var farLamp = LightShade.Shade(f, new Vector3(21.5f, 0.8f, 7.5f), Vector3.up, false, ref night, ref t);
            CheckGreater("4 夜里有灯处亮于室内无灯处", Luma(atLamp), Luma(farLamp), 0.15f);
            Check("5 灯具色相保留（红灯偏红）", atLamp.r > atLamp.g * 1.5f && atLamp.r > atLamp.b * 1.5f,
                "rgb=" + atLamp.r.ToString("0.00") + "/" + atLamp.g.ToString("0.00") + "/" + atLamp.b.ToString("0.00"));

            // 6 接触阴影：墙脚地面暗于同朝向的空旷地面
            var foot = LightShade.Shade(f, new Vector3(9.5f, 0f, 8.5f), Vector3.up, true, ref day, ref t);
            var plain = LightShade.Shade(f, new Vector3(9.5f, 0f, 15f), Vector3.up, true, ref day, ref t);
            CheckGreater("6 墙脚接触阴影暗于空旷地面", Luma(plain), Luma(foot), 0.05f);
            // 反向对照：关掉 AO 后两者必须不再有明显差
            f.AoOn = false;
            var footNo = LightShade.Shade(f, new Vector3(9.5f, 0f, 8.5f), Vector3.up, true, ref day, ref t);
            var plainNo = LightShade.Shade(f, new Vector3(9.5f, 0f, 15f), Vector3.up, true, ref day, ref t);
            Check("6b 关掉 AO 后不再有该差（反向对照）", Math.Abs(Luma(footNo) - Luma(plainNo)) < 0.02f,
                "Δ=" + Math.Abs(Luma(footNo) - Luma(plainNo)).ToString("0.000"));
            f.AoOn = true;

            // 7 投影：影子里暗于影子外（同法线同高度）
            var inShadow = LightShade.Shade(f, new Vector3(2.5f, 0f, 9f), Vector3.up, false, ref day, ref t);
            var inSun = LightShade.Shade(f, new Vector3(2.5f, 0f, 15f), Vector3.up, false, ref day, ref t);
            CheckGreater("7 墙影内暗于同高度露天", Luma(inSun), Luma(inShadow), 0.15f);
            f.ShadowsOn = false;
            var inShadowNo = LightShade.Shade(f, new Vector3(2.5f, 0f, 9f), Vector3.up, false, ref day, ref t);
            Check("7b 关掉投影后两者相等（反向对照）", Math.Abs(Luma(inShadowNo) - Luma(inSun)) < 0.02f,
                "Δ=" + Math.Abs(Luma(inShadowNo) - Luma(inSun)).ToString("0.000"));
            f.ShadowsOn = true;

            // 8 亮度下限：全黑处正好等于 floor，且不得为 0
            f.LampsOn = false; f.AoOn = false;
            var pitch = LightShade.Shade(f, new Vector3(21f, 0.5f, 7f), Vector3.up, true, ref night, ref t);
            Check("8 全黑处贴到亮度下限", Math.Abs(Luma(pitch) - t.floor) < 0.01f,
                "luma=" + Luma(pitch).ToString("0.000") + " floor=" + t.floor);
            f.LampsOn = true; f.AoOn = true;

            // 9 不许饱和：正午露天朝上面必须留出头部空间（否则等于平涂看不见光照）
            Check("9 正午朝上面未被截成纯白", up.r < 0.95f && up.g < 0.95f && up.b < 0.95f,
                "rgba=" + up);
            var hot = Tuning(); hot.exposure = 1.6f; hot.aoStrength = 0f;
            var blown = LightShade.Shade(f, new Vector3(2f, 0f, 2f), Vector3.up, false, ref day, ref hot);
            Check("9b 曝光过高时判据必须报（反向对照）", blown.r >= 0.95f, "rgba=" + blown);

            // 10 逐件对象的放置亮度：低于同处朝上面、高于下限（植被不该和地面一样顶满）
            var place = LightShade.Placement(f, new Vector3(2f, 0f, 2f), ref day, ref t);
            CheckGreater("10 放置亮度低于朝上面顶点色", Luma(up), Luma(place), 0.10f);
            Check("10b 放置亮度高于下限", Luma(place) > t.floor + 0.05f, "luma=" + Luma(place).ToString("0.000"));

            // 11 夜里无太阳方向项：东立面与西立面应当一样（只剩天光与灯）
            var nEast = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.right, false, ref night, ref t);
            var nWest = LightShade.Shade(f, new Vector3(9.5f, 1.35f, 8.5f), Vector3.left, false, ref night, ref t);
            Check("11 夜里太阳方向项为 0（东西同亮）", Math.Abs(Luma(nEast) - Luma(nWest)) < 0.02f,
                "Δ=" + Math.Abs(Luma(nEast) - Luma(nWest)).ToString("0.000"));

            // 12 月夜弱直射：朝月面亮于背月面
            var moonFace = LightShade.Shade(f, new Vector3(2f, 1f, 2f),
                new Vector3(0.2f, 0.7f, -0.3f).normalized, false, ref night, ref t);
            var moonBack = LightShade.Shade(f, new Vector3(2f, 1f, 2f),
                new Vector3(-0.2f, -0.7f, 0.3f).normalized, false, ref night, ref t);
            CheckGreater("12 朝月面亮于背月面", Luma(moonFace), Luma(moonBack), 0.02f);

            // 13 风格层：softFoot==softTop 时向阳项是硬分档
            var hard = Tuning(); hard.softFoot = 0f; hard.softTop = 0f;
            float a1 = LightShade.SunShape(0.4f, ref hard);
            float a2 = LightShade.SunShape(-0.4f, ref hard);
            Check("13 硬分档只出 0 或 1", (a1 == 1f || a1 == 0f) && (a2 == 1f || a2 == 0f) && a1 != a2,
                a1 + " / " + a2);

            // 14 灯具增益单调且不越界
            var t2 = Tuning(); t2.lampStrength = 2f;
            var lampHi = LightShade.Shade(f, new Vector3(18.5f, 0.8f, 4.5f), Vector3.up, false, ref night, ref t2);
            var lampLo = LightShade.Shade(f, new Vector3(18.5f, 0.8f, 4.5f), Vector3.up, false, ref night, ref t);
            Check("14 灯具增益单调且截断在 1 以内", Luma(lampHi) >= Luma(lampLo) - 0.001f
                  && lampHi.r <= 1.0001f && lampHi.g <= 1.0001f && lampHi.b <= 1.0001f,
                "lo=" + Luma(lampLo).ToString("0.000") + " hi=" + Luma(lampHi).ToString("0.000"));

            // 15 原型形体明暗：朝参考光面 = 1，背参考光面 < 1，且被夹在 [min,max]
            var style = new Vector3(0.25f, 0.7f, 0.65f).normalized;
            float fUp = Luma(LightShade.FormShape(Vector3.up, ref day, ref t, style, 0.55f, 0.30f, 0.30f, 1.60f));
            float fBack = Luma(LightShade.FormShape(-style, ref day, ref t, style, 0.55f, 0.30f, 0.30f, 1.60f));
            Check("15 朝上参考面正好是 1", Math.Abs(fUp - 1f) < 0.001f, fUp.ToString("0.000"));
            Check("15b 背对风格光必须小于 1", fBack < fUp - 0.05f,
                "back=" + fBack.ToString("0.000") + " up=" + fUp.ToString("0.000"));

            // 16 晨昏转暖：twilight=1 时直射色应当偏红
            var dusk = Day(); dusk.twilight = 1f; dusk.glow = 0.5f;
            var warm = LightShade.SunColor(ref dusk);
            Check("16 晨昏直射转暖（r>b）", warm.r > warm.b + 0.2f, "rgb=" + warm.r.ToString("0.00")
                  + "/" + warm.g.ToString("0.00") + "/" + warm.b.ToString("0.00"));

            // ============================================================
            // F014 受击反馈曲线（HitFxMath.cs —— 游戏里跑的就是这一份）
            // 只验行为性质，不拿常数验常数：MC 的 0.5 s / 10 tick 那层映射写在文档里，
            // 判据管的是「窗口内单调退到 0、峰值不越界、低血起点与上限、合成取大不相加」。
            // ============================================================
            Console.WriteLine("-- F014 受击曲线 --");

            Check("17 红闪起点=峰值", Mathf.Abs(HitFxMath.Hurt(0f, 1f) - 1f) < 1e-4f,
                "v=" + HitFxMath.Hurt(0f, 1f));
            Check("17b 红闪半程正好一半",
                Mathf.Abs(HitFxMath.Hurt(HitFxMath.HurtDur * 0.5f, 1f) - 0.5f) < 1e-4f,
                "v=" + HitFxMath.Hurt(HitFxMath.HurtDur * 0.5f, 1f));
            bool mono = true, fell = false;
            float prev = 2f;
            for (int i = 0; i <= 32; i++)
            {
                float tt = HitFxMath.HurtDur * i / 32f;
                float v = HitFxMath.Hurt(tt, 1f);
                if (v > prev + 1e-5f) mono = false;
                prev = v;
            }
            fell = HitFxMath.Hurt(0.01f, 1f) < HitFxMath.Hurt(0f, 1f) - 1e-4f;
            Check("18 红闪全程不回升", mono, "32 点采样");
            Check("18b 红闪确实在退（不是平线）", fell, "0.01s=" + HitFxMath.Hurt(0.01f, 1f));
            Check("19 窗口边界：差一毫秒仍亮，到点归零",
                HitFxMath.Hurt(HitFxMath.HurtDur - 0.001f, 1f) > 0f
                && HitFxMath.Hurt(HitFxMath.HurtDur, 1f) == 0f,
                "内=" + HitFxMath.Hurt(HitFxMath.HurtDur - 0.001f, 1f)
                + " 界=" + HitFxMath.Hurt(HitFxMath.HurtDur, 1f));
            Check("19b 窗口外（负时刻与超时）都是 0",
                HitFxMath.Hurt(-0.2f, 1f) == 0f && HitFxMath.Hurt(9f, 1f) == 0f, "");
            Check("20 峰值越界与负值都被夹住",
                HitFxMath.Hurt(0f, 3f) <= 1.0001f && HitFxMath.Hurt(0f, -1f) == 0f,
                "3=" + HitFxMath.Hurt(0f, 3f) + " -1=" + HitFxMath.Hurt(0f, -1f));

            Check("21 玩家暗角起点=峰值", Mathf.Abs(HitFxMath.Vignette(0f, 1f) - 1f) < 1e-4f,
                "v=" + HitFxMath.Vignette(0f, 1f));
            bool over = false, lin = false, allZero = false;
            for (int i = 0; i <= 48; i++)
            {
                float tt = HitFxMath.VignDur * i / 48f;
                float v = HitFxMath.Vignette(tt, 1f);
                float env = 1f - tt / HitFxMath.VignDur;
                if (v > 1.0001f || v < 0f) over = true;
                if (v > env + 1e-4f) over = true;                 // 脉冲只许在包络之下
                if (Mathf.Abs(v - env) > 0.02f) lin = true;       // 存在偏离直线的点 = 脉冲生效
                if (tt > 0.05f && tt < HitFxMath.VignDur - 0.05f && v <= 0f) allZero = true;
            }
            Check("22 暗角不越界、且不越过 (1-t) 包络", !over, "");
            Check("22b 暗角确实是脉冲衰减（不是直线）", lin, "");
            Check("22c 窗口中段不许提前归零", !allZero, "");
            Check("23 暗角到点归零", HitFxMath.Vignette(HitFxMath.VignDur, 1f) == 0f
                  && HitFxMath.Vignette(-0.1f, 1f) == 0f, "");

            Check("24 满血与半血没有常驻红边",
                HitFxMath.LowHealth(1f) == 0f && HitFxMath.LowHealth(0.5f) == 0f, "");
            Check("24b 过起点才开始变浓",
                HitFxMath.LowHealth(HitFxMath.LowHealthFrom) == 0f
                && HitFxMath.LowHealth(HitFxMath.LowHealthFrom - 0.01f) > 0f,
                "界=" + HitFxMath.LowHealth(HitFxMath.LowHealthFrom)
                + " 内=" + HitFxMath.LowHealth(HitFxMath.LowHealthFrom - 0.01f).ToString("0.000"));
            bool lm = true;
            float lp = 2f;      // 起始哨兵必须高于任何可能取值，否则第一次比较就误判
            for (int i = 0; i <= 40; i++)
            {
                float hp = i / 40f;
                float v = HitFxMath.LowHealth(hp);
                if (v > lp + 1e-5f) lm = false;      // 血越少越浓：随 hp 上升必须不回升
                lp = v;
            }
            Check("25 越残血越浓（单调）", lm, "");
            Check("25b 见底的浓度就是上限",
                Mathf.Abs(HitFxMath.LowHealth(0f) - HitFxMath.LowHealthMax) < 1e-5f,
                "v=" + HitFxMath.LowHealth(0f));

            Check("26 两路暗角取大不相加（残血再挨一发不该整屏发白）",
                Mathf.Abs(HitFxMath.Combine(0.8f, 0.8f) - 0.8f) < 1e-5f,
                "v=" + HitFxMath.Combine(0.8f, 0.8f));
            Check("26b 合成不越界且保留较大者",
                HitFxMath.Combine(1f, 1f) <= 1.0001f && HitFxMath.Combine(0f, 0.3f) > 0.29f, "");

            Check("27 零伤害不闪", HitFxMath.PeakOf(0f) == 0f, "");
            Check("27b 小伤害也给到可见下限", HitFxMath.PeakOf(1f) >= 0.55f,
                "v=" + HitFxMath.PeakOf(1f));
            Check("27c 大伤害封顶为 1", HitFxMath.PeakOf(1000f) >= 0.999f, "v=" + HitFxMath.PeakOf(1000f));
            bool pm2 = true;
            float pp = -1f;
            for (int i = 0; i <= 24; i++)
            {
                float v = HitFxMath.PeakOf(i * 2f);
                if (v < pp - 1e-5f) pm2 = false;
                pp = v;
            }
            Check("27d 强度随伤害单调不降", pm2, "");

            // ============================================================
            // F015 射击效果（ShotFxMath.cs）
            // ============================================================
            Console.WriteLine("-- F015 射击效果 --");

            Check("28 衰减：起点 1、半程 0.5、到点 0",
                ShotFxMath.Decay(0f, 1f) == 1f
                && Mathf.Abs(ShotFxMath.Decay(0.5f, 1f) - 0.5f) < 1e-5f
                && ShotFxMath.Decay(1f, 1f) == 0f, "");
            Check("28b 窗口外与非法时长都是 0",
                ShotFxMath.Decay(-0.1f, 1f) == 0f && ShotFxMath.Decay(2f, 1f) == 0f
                && ShotFxMath.Decay(0f, 0f) == 0f && ShotFxMath.Decay(0f, -1f) == 0f,
                "忘给时长也亮 = 特效赖着不走");

            Check("29 拖焰：零速没有、随速度单调、上下限夹住",
                ShotFxMath.TracerLen(0f) == 0f
                && ShotFxMath.TracerLen(20f) > ShotFxMath.TracerLen(10f)
                && ShotFxMath.TracerLen(99999f) <= ShotFxMath.TracerMax + 1e-4f
                && ShotFxMath.TracerLen(0.01f) >= ShotFxMath.TracerMin - 1e-4f,
                "10=" + ShotFxMath.TracerLen(10f).ToString("0.00")
                + " 20=" + ShotFxMath.TracerLen(20f).ToString("0.00"));
            Check("29b 反向：负速度不给出长度", ShotFxMath.TracerLen(-5f) == 0f, "");

            var pos = new Vector3(3f, 1.2f, 7f);
            var vel = new Vector3(2f, 0f, 0f);
            float act;
            var tail = ShotFxMath.TracerTail(pos, vel, 0.8f, out act);
            Check("30 拖焰尾端在运动的反方向、长度对得上",
                act > 0.79f && act < 0.81f
                && Mathf.Abs((pos - tail).magnitude - 0.8f) < 1e-4f
                && tail.x < pos.x && Mathf.Abs(tail.y - pos.y) < 1e-5f,
                "tail=" + tail);
            float act2 = 1f;
            var tailZeroDir = ShotFxMath.TracerTail(pos, Vector3.zero, 0.8f, out act2);
            var tailZeroLen = ShotFxMath.TracerTail(pos, vel, 0f, out act2);
            Check("30b 零方向或零长度：退回原位且长度为 0",
                act2 == 0f && tailZeroDir == pos && tailZeroLen == pos, "");

            Check("31 烟尘边淡边长（反着来就像被吸回去）",
                ShotFxMath.PuffSize(0.02f, 1f) < ShotFxMath.PuffSize(0.2f, 1f)
                && ShotFxMath.PuffAlpha(0.02f) > ShotFxMath.PuffAlpha(0.2f),
                "size " + ShotFxMath.PuffSize(0.02f, 1f).ToString("0.00")
                + "->" + ShotFxMath.PuffSize(0.2f, 1f).ToString("0.00")
                + " alpha " + ShotFxMath.PuffAlpha(0.02f).ToString("0.00")
                + "->" + ShotFxMath.PuffAlpha(0.2f).ToString("0.00"));
            Check("31b 窗口外烟尘尺寸与透明度都是 0",
                ShotFxMath.PuffSize(ShotFxMath.PuffDur + 0.01f, 1f) == 0f
                && ShotFxMath.PuffAlpha(ShotFxMath.PuffDur + 0.01f) == 0f, "");
            Check("31c 负尺寸不给出负数", ShotFxMath.PuffSize(0.05f, -3f) >= 0f,
                "v=" + ShotFxMath.PuffSize(0.05f, -3f));
            Check("31d 透明度有上限，不许把目标整个盖掉",
                ShotFxMath.PuffAlpha(0f) <= 0.75f + 1e-4f, "v=" + ShotFxMath.PuffAlpha(0f));

            var mz = ShotFxMath.MuzzleAt(new Vector3(1f, 1f, 1f), new Vector3(4f, 9f, 1f), 0.32f);
            var flat = new Vector3(4f, 0f, 1f).normalized;
            Check("32 枪口沿弹道的水平方向前移，仰角不参与（y 不许跟着跑）",
                Mathf.Abs(mz.y - 1f) < 1e-4f
                && Mathf.Abs((mz.x - 1f) - flat.x * 0.32f) < 1e-4f
                && Mathf.Abs((mz.z - 1f) - flat.z * 0.32f) < 1e-4f, "at=" + mz);
            Check("32b 方向退化时原地不动（不炸出 NaN 位置）",
                ShotFxMath.MuzzleAt(pos, Vector3.zero, 0.5f) == pos
                && ShotFxMath.MuzzleAt(pos, new Vector3(0f, 5f, 0f), 0.5f) == pos, "");
            Check("32c 负前移按 0 处理（不许把火光挪到枪托后面）",
                ShotFxMath.MuzzleAt(new Vector3(1f, 0f, 1f), Vector3.right, -2f).x > 0.999f,
                "x=" + ShotFxMath.MuzzleAt(new Vector3(1f, 0f, 1f), Vector3.right, -2f).x);

            Check("33 火光比烟尘短命：火光没了、烟尘还在",
                ShotFxMath.Decay(ShotFxMath.FlashDur, ShotFxMath.FlashDur) == 0f
                && ShotFxMath.PuffAlpha(ShotFxMath.FlashDur) > 0f,
                "flash=" + ShotFxMath.FlashDur + " puff@flash=" + ShotFxMath.PuffAlpha(ShotFxMath.FlashDur).ToString("0.00"));

            // ============================================================
            // 山体高度与陡壁几何判据（MountainShape3D.cs / MountainMesh3D.cs）
            // ============================================================
            Console.WriteLine("-- 山体高度与陡壁几何 --");
            int mw = 32, md = 32;
            var fp = new bool[mw * md];
            var cells = new byte[mw * md];
            // 1x1 孤立岩柱 (4,4) + 12x12 主山体 (10..21, 10..21)
            fp[4 * mw + 4] = true; cells[4 * mw + 4] = 1;
            for (int z = 10; z <= 21; z++)
                for (int x = 10; x <= 21; x++)
                {
                    fp[z * mw + x] = true;
                    cells[z * mw + x] = 1;
                }
            var mShape = new MountainShape3D(mw, md, 12345);
            mShape.Build(fp);
            float minMtnCenterH = float.MaxValue;
            float minMtnMaxH = float.MaxValue;
            for (int z = 0; z < md; z++)
                for (int x = 0; x < mw; x++)
                {
                    if (!fp[z * mw + x]) continue;
                    float ch = mShape.HeightAt(x + 0.5f, z + 0.5f);
                    float cm = mShape.CellMax(x, z);
                    if (ch < minMtnCenterH) minMtnCenterH = ch;
                    if (cm < minMtnMaxH) minMtnMaxH = cm;
                }
            Check("34 所有实际属于山体的格高度严格 > 2.0m（含 1x1 孤立岩与山脚外沿格）",
                minMtnCenterH > 2.0f && minMtnMaxH > 2.0f,
                "minCenterH=" + minMtnCenterH.ToString("0.00") + "m minMaxH=" + minMtnMaxH.ToString("0.00") + "m");
            Check("34b 远离山体的平地格高度为 0",
                mShape.HeightAt(1.5f, 1.5f) == 0f && mShape.CellMax(1, 1) == 0f,
                "openH=" + mShape.HeightAt(1.5f, 1.5f).ToString("0.00"));
            var mTris = new List<MountainTriangle3D>();
            MountainMesh3D.BuildCell(mShape, 10, 15, cells, null, mTris);
            float maxEdgeCellTop = 0f;
            for (int i = 0; i < mTris.Count; i++)
            {
                maxEdgeCellTop = Mathf.Max(maxEdgeCellTop, Mathf.Max(mTris[i].a.y, Mathf.Max(mTris[i].b.y, mTris[i].c.y)));
            }
            Check("35 山体外沿首格直接拔升至 > 2.0m 陡峭岩壁",
                maxEdgeCellTop > 2.0f, "edgeCellTop=" + maxEdgeCellTop.ToString("0.00") + "m");

            Console.WriteLine("== 结果: " + (fails.Count == 0 ? "全部通过" : "未通过 " + string.Join(", ", fails))
                              + "（通过 " + passed + " 条）==");
            if (fails.Count == 0 && demo) return FxDemo.Run();
            return fails.Count == 0 ? 0 : 1;
        }
    }
}
