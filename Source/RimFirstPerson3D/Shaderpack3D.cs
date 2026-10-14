using System.Diagnostics;
using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 光影包级画面：画质档、各层开关、帧时间预算与自动降级
    // ============================================================
    //
    // 规范：docs/features/F016-shaderpack-look.md「画质档」。
    // 每层只问这里「我开不开」，不各自读设置；预算用 F016 各层自己的 CPU 计时（Begin/End）累加，
    // p95 不可靠地反映 GPU，所以门槛按本项目自己能量到的那一份定，且只用于降级，不用于升级。
    public enum ShaderTier { Off = 0, Low = 1, Medium = 2, High = 3, Ultra = 4 }

    public static class Shaderpack3D
    {
        private static FirstPerson3DSettings S => FirstPerson3DMod.settings;

        /// <summary>设置里选的档位。</summary>
        // 逐顶点热路径（LightField3D.LampAt / OccFraction）也会问档位：按设置字符串的引用缓存解析结果
        private static string chosenSrc;
        private static ShaderTier chosenCached = ShaderTier.Medium;

        public static ShaderTier Chosen
        {
            get
            {
                string raw = S != null ? S.shaderQuality : null;
                if (ReferenceEquals(raw, chosenSrc)) return chosenCached;
                chosenSrc = raw;
                chosenCached = Parse(raw);
                return chosenCached;
            }
        }

        private static ShaderTier Parse(string raw)
        {
            {
                string q = raw != null ? raw.Trim().ToLowerInvariant() : "medium";
                switch (q)
                {
                    case "off": return ShaderTier.Off;
                    case "low": return ShaderTier.Low;
                    case "high": return ShaderTier.High;
                    case "ultra": return ShaderTier.Ultra;
                    default: return ShaderTier.Medium;
                }
            }
        }

        // 自动降级挡数（0 = 不降）。只在本会话有效，不写回设置：玩家选的档不该被悄悄改掉。
        private static int downSteps;
        public static int DownSteps => downSteps;

        public static ShaderTier Tier
        {
            get
            {
                int t = (int)Chosen - downSteps;
                return (ShaderTier)Mathf.Clamp(t, 0, 4);
            }
        }

        private static bool On(ShaderTier min) => Lighting3D.Enabled && Tier >= min;

        // ---- 各层开关（档位表的唯一出处）----
        public static bool SmoothAo => On(ShaderTier.Low);                 // L2-1
        public static bool SoftShadows => On(ShaderTier.Medium);           // L2-2
        public static bool NearDense => On(ShaderTier.Medium);             // L2-3
        public static bool ObjShadows => On(ShaderTier.Medium);            // L3
        public static float ObjShadowRange => Tier >= ShaderTier.High ? 40f : 20f;
        public static bool LampShadows => On(ShaderTier.High);
        public static bool Sprites => On(ShaderTier.Low);                  // L4-A
        public static bool SunGhosts => On(ShaderTier.Medium);
        public static bool Bloom => On(ShaderTier.Ultra) && Caps.MainCamRT && Caps.BlitBilinear;   // L4-B
        public static bool Fog => On(ShaderTier.Medium);                   // L5
        public static bool IndoorShafts => On(ShaderTier.Medium);
        public static bool OutdoorShafts => On(ShaderTier.High);
        public static bool WaterSky => On(ShaderTier.Medium);              // L6-B
        public static bool WaterMirror => On(ShaderTier.Ultra) && Caps.MainCamRT && Caps.Oblique;  // L6-A
        public static bool CloudLight => On(ShaderTier.Medium);            // L7
        public static bool RainFx => On(ShaderTier.High);
        public static bool CloudShadows => On(ShaderTier.High);
        public static bool DynLights => On(ShaderTier.High);               // L8
        public static bool ScreenFx => On(ShaderTier.Low);                 // L9

        /// <summary>额外帧时间预算（毫秒，F016 各层 CPU 计时之和）。</summary>
        public static float BudgetMs
        {
            get
            {
                switch (Tier)
                {
                    case ShaderTier.Low: return 0.5f;
                    case ShaderTier.Medium: return 2.5f;
                    case ShaderTier.High: return 4.5f;
                    case ShaderTier.Ultra: return 7.0f;
                    default: return 0f;
                }
            }
        }

        // ---- 引擎能力（P0 探针写进设置，这里只读）----
        public static class Caps
        {
            public static bool MainCamRT => S != null && S.capsMainCamRT == 1;
            public static bool BlitBilinear => S != null && S.capsBlitBilinear == 1;
            public static bool EngineFog => S != null && S.capsEngineFog == 1;
            public static bool Oblique => S != null && S.capsOblique == 1;
        }

        // ---- 天气对烘焙的影响（L7 湿面）。在 Lighting3D.BeginMap 里调用，进同一份快照 ----
        public static float WetLevel { get; private set; }
        public static float AuroraLevel { get; private set; }

        public static void ApplyWeather(ref LightEnv env, ref LightTuning tune, SkyScenery.Env cur)
        {
            float wet = 0f;
            if (On(ShaderTier.High))
                wet = Mathf.Clamp01(Mathf.Max(cur.rain, cur.storm * 0.8f));
            // 量化到 1/4：雨势连续变化时不逐帧重烘
            wet = Mathf.Round(wet * 4f) / 4f;
            WetLevel = wet;
            // L7 极光：弱绿色环境光进烘焙（按 1/4 档，BakeKey 里也记）
            AuroraLevel = CloudLight ? Mathf.Round(Mathf.Clamp01(cur.aurora) * 4f) / 4f : 0f;
            env.auroraAmbient = WeatherMath.AuroraAmbient(AuroraLevel);
            if (wet <= 0f) return;
            tune.exposure *= Mathf.Lerp(1f, ShaderpackDefaults.WetDarken, wet);
            if (tune.toneMap)
                tune.saturation = (tune.saturation <= 0f ? 1f : tune.saturation) * Mathf.Lerp(1f, ShaderpackDefaults.WetSaturate, wet);
        }

        /// <summary>影响烘焙结果的 F016 开关指纹（并进 Lighting3D.ParamKey）。</summary>
        public static long BakeKey()
        {
            long h = 7;
            h = h * 31 + (SmoothAo ? 1 : 0);
            h = h * 31 + (SoftShadows ? 1 : 0);
            h = h * 31 + (NearDense ? 1 : 0);
            h = h * 31 + (long)(WetLevel * 4f);
            h = h * 31 + (long)(AuroraLevel * 4f);
            return h;
        }

        // ---- 计时与自动降级 ----
        private static readonly Stopwatch sw = new Stopwatch();
        private static float frameMs;
        private static float smoothMs;
        private static float overTime;
        private static float underTime;

        public static float SmoothMs => smoothMs;

        private static int timingDepth;
        public static void Begin() { if (timingDepth++ == 0) sw.Restart(); }
        public static void End()
        {
            if (timingDepth <= 0 || --timingDepth != 0) return;
            sw.Stop();
            frameMs += (float)sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>每帧末调一次（Fps3DRoot）。</summary>
        public static void EndFrame(float dt)
        {
            smoothMs = Mathf.Lerp(smoothMs, frameMs, 0.1f);
            frameMs = 0f;
            if (S == null || !S.shaderAutoDowngrade || Tier == ShaderTier.Off) { overTime = 0f; return; }
            float budget = BudgetMs;
            if (smoothMs > budget) { overTime += dt; underTime = 0f; }
            else { overTime = 0f; underTime += dt; }
            if (overTime >= 5f)
            {
                overTime = 0f;
                downSteps++;
                Verse.Log.Message(ModEntry.LogPrefix + "F016 over budget (" + smoothMs.ToString("0.00")
                    + " ms > " + budget.ToString("0.0") + "), tier -> " + Tier);
            }
            // 连续 60 s 远低于上一档的预算才回升一档，避免来回翻
            if (downSteps > 0 && underTime >= 60f)
            {
                underTime = 0f;
                downSteps--;
                int prev = (int)Tier;
                if (smoothMs > BudgetMs * 0.5f) downSteps++;   // 回升后的预算仍吃紧 -> 不升
                else Verse.Log.Message(ModEntry.LogPrefix + "F016 tier back -> " + (ShaderTier)prev);
            }
        }

        public static void ResetSession()
        {
            downSteps = 0; overTime = 0f; underTime = 0f; smoothMs = 0f;
            frameMs = 0f; timingDepth = 0; sw.Reset();
        }

        public static void Snapshot(System.Text.StringBuilder sb)
        {
            sb.Append("\"f016\":{\"tier\":\"").Append(Tier).Append("\",\"chosen\":\"").Append(Chosen)
              .Append("\",\"down\":").Append(downSteps)
              .Append(",\"ms\":").Append(smoothMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"budget\":").Append(BudgetMs.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"wet\":").Append(WetLevel.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"caps\":[").Append(S != null ? S.capsMainCamRT : -1).Append(',')
              .Append(S != null ? S.capsBlitBilinear : -1).Append(',')
              .Append(S != null ? S.capsEngineFog : -1).Append(',')
              .Append(S != null ? S.capsOblique : -1).Append("]}");
        }
    }
}
