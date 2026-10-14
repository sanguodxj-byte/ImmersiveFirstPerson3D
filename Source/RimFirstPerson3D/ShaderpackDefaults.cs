using UnityEngine;

namespace RimFirstPerson3D
{
    // F016 默认值的唯一真源（游戏设置项的默认值与 tools/LightSim 都引用这里）。
    // 规范：docs/features/F016-shaderpack-look.md
    public static class ShaderpackDefaults
    {
        // ---- L1 胶片色调 ----
        public const float Exposure = 1.00f;
        public const float WhitePoint = 2.2f;
        public const float HighlightDesat = 0.6f;
        public const float SplitTone = 0.35f;
        public static readonly Color ShadowTint = new Color(0.90f, 0.97f, 1.10f, 1f);
        public static readonly Color HighlightTint = new Color(1.08f, 1.00f, 0.88f, 1f);
        public const float Saturation = 1.12f;
        public const float NightDesat = 0.50f;

        // ---- L2 平滑光照 ----
        public const float PenumbraK = 0.18f;      // 半影宽 = 遮挡距离 × K（米）
        public const float PenumbraMin = 0.15f;
        public const float PenumbraMax = 1.5f;
        public const float NearDenseRadius = 24f;  // 相机多少米内地面切到 1 m

        // ---- L3 物体投影 ----
        public const float ShadowAlpha = 0.38f;
        public const float ShadowMinSunY = 0.12f;  // 太阳低于此 sin 仰角时影子淡出
        public const float ShadowMaxLen = 15f;     // 影长封顶（米），与 ShadowMaxSteps 同口径
        public const float LampShadowRadius = 8f;  // 灯影只给灯具多少米内的物体

        // ---- L4 泛光 ----
        public const float BloomIntensity = 0.55f;
        public const float HaloWorldK = 0.18f;     // 光晕世界半径 = GlowRadius × K
        public const float HaloMinPx = 6f;
        public const float HaloMaxScreenFrac = 0.12f;

        // ---- L5 大气 ----
        public const float FogDensity = 0.010f;     // 每米消光（晴天）
        public const float FogHeightFalloff = 6f;   // 高度雾衰减（米）
        public const float FogIndoorK = 0.2f;
        public const int FogShells = 8;
        public const float FogR0 = 10f;
        public const float FogQ = 1.45f;
        public const float MieG = 0.7f;
        public const float ShaftGain = 0.65f;

        // ---- L6 水面 ----
        public const float WaterF0 = 0.02f;

        // ---- L7 天气 ----
        public const float WetDarken = 0.82f;
        public const float WetSaturate = 1.10f;

        // ---- L9 屏幕 ----
        public const float AdaptBrightSec = 1.2f;
        public const float AdaptDarkSec = 0.8f;
        public const float VignetteBase = 0.18f;
    }
}
