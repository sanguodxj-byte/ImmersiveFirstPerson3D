using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L4：精灵泛光、光晕尺寸、闪烁与镜头鬼影 —— 纯函数层
    // ============================================================
    public static class FlareMath
    {
        /// <summary>
        /// 光晕的世界直径：GlowRadius × K，再按屏幕上「不小于 minPx、不大于屏高 maxFrac」夹住。
        /// F013 AC-7 的「篝火那团占掉四分之一屏」就是没有这道上限。
        /// </summary>
        public static float HaloWorldSize(float glowRadius, float k, float dist, float fovDeg, float screenH,
            float minPx, float maxFrac)
        {
            float world = Mathf.Max(0.05f, glowRadius * k);
            float d = Mathf.Max(0.05f, dist);
            float worldPerPx = 2f * d * Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, screenH);
            float minW = minPx * worldPerPx;
            float maxW = maxFrac * screenH * worldPerPx;
            return Mathf.Clamp(world, minW, Mathf.Max(minW, maxW));
        }

        /// <summary>屏幕上占多少像素（验收用）。</summary>
        public static float ScreenPx(float worldSize, float dist, float fovDeg, float screenH)
        {
            float worldPerPx = 2f * Mathf.Max(0.05f, dist) * Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, screenH);
            return worldSize / worldPerPx;
        }

        /// <summary>
        /// 1/f 火光闪烁：四个八度的平滑值噪声，振幅按 1/2^k 递减，均值 1、幅度 ±amp。
        /// 烘焙取平均（=1），只调光晕与贴地光池，所以不会让 chunk 每帧重烘。
        /// </summary>
        public static float Flicker(float t, float seed, float amp)
        {
            float s = 0f, norm = 0f, a = 1f, f = 1.7f;
            for (int k = 0; k < 4; k++)
            {
                s += a * ValueNoise(t * f + seed * (k + 1) * 13.37f);
                norm += a;
                a *= 0.5f; f *= 2.03f;
            }
            return 1f + amp * (s / norm);
        }

        // [-1,1] 的一维平滑值噪声
        private static float ValueNoise(float x)
        {
            float i = Mathf.Floor(x);
            float fr = x - i;
            float u = fr * fr * (3f - 2f * fr);
            return Mathf.Lerp(Hash(i), Hash(i + 1f), u) * 2f - 1f;
        }

        private static float Hash(float n)
        {
            float v = Mathf.Sin(n * 127.1f + 311.7f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        /// <summary>
        /// F016 L8 随身光源的格子光衰减：与原版 GlowGrid 同形的 (1 - d/R)²，
        /// 乘 0.5 换算到 glowGrid 的归一化口径（原版灯具在光心约 0.5 量级，LampGain 3.6 再放大）。
        /// </summary>
        public static float PointFalloff(float dx, float dz, float radius)
        {
            if (radius <= 0.01f) return 0f;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d >= radius) return 0f;
            float k = 1f - d / radius;
            return 0.5f * k * k;
        }

        /// <summary>指数平滑到目标（时间常数 tau 秒），可见度渐变用。</summary>
        public static float Approach(float cur, float target, float dt, float tau)
        {
            if (tau <= 1e-4f) return target;
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / tau);
            return cur + (target - cur) * k;
        }

        /// <summary>
        /// 镜头鬼影：沿「太阳屏幕位置 → 屏幕中心」连线排开。sun 是视口坐标（0..1）。
        /// 第 i 枚在 t_i 处（t=0 太阳、t=1 中心、t=2 对称点），返回视口位置。
        /// </summary>
        public static readonly float[] GhostT = { 0.45f, 1.30f, 1.65f, 2.10f };
        public static readonly float[] GhostScale = { 0.06f, 0.035f, 0.09f, 0.14f };
        public static readonly float[] GhostGain = { 0.22f, 0.30f, 0.16f, 0.10f };

        public static Vector2 GhostPos(Vector2 sun, int i)
        {
            var c = new Vector2(0.5f, 0.5f);
            float t = GhostT[i];
            return new Vector2(sun.x + (c.x - sun.x) * t, sun.y + (c.y - sun.y) * t);
        }

        /// <summary>眩光强度：太阳越靠屏幕中心越强，出了屏幕边就没有；乘可见度与晴朗度。</summary>
        public static float GlareGain(Vector2 sunViewport, float sunVis, float cloudiness, float sunY)
        {
            if (sunY <= 0f) return 0f;
            float dx = sunViewport.x - 0.5f, dy = sunViewport.y - 0.5f;
            float off = Mathf.Sqrt(dx * dx + dy * dy);
            float onScreen = Mathf.Clamp01((0.85f - off) / 0.35f);
            return onScreen * Mathf.Clamp01(sunVis) * Mathf.Clamp01(1f - cloudiness) * Mathf.Clamp01(sunY * 4f);
        }
    }
}
