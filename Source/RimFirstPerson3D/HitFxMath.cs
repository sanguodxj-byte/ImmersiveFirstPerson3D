using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // 受击反馈的纯数学（F014）—— 无 Verse、无贴图、无状态
    // ============================================================
    //
    // 单独成文件的理由与 LightShade.cs 一样：这是要在播放器外面跑的判据对象。
    // tools/LightSim 链接这份代码，在离线合成场景里把衰减曲线的性质验完，
    // 再进游戏看真像素。规范：docs/features/F014-hit-feedback.md

    public static class HitFxMath
    {
        /// <summary>生物红闪时长（秒）。Minecraft 是 hurtTime 10 tick = 0.5 s。</summary>
        public const float HurtDur = 0.5f;

        /// <summary>玩家受击暗角余韵（秒）。MC 的伤害暗角比生物红闪拖得长。</summary>
        public const float VignDur = 0.9f;

        /// <summary>暗角脉冲频率（Hz）。MC 的 damage bob 是一冲一缓，不是直线衰减。</summary>
        public const float PulseHz = 2.2f;

        /// <summary>低血压暗角的起点：血量比例低于这个值才开始常驻红边。</summary>
        public const float LowHealthFrom = 0.40f;

        /// <summary>低血压暗角最浓到多少。</summary>
        public const float LowHealthMax = 0.35f;

        /// <summary>
        /// 生物红闪强度：t 秒内从 peak 线性降到 0，窗口外为 0。
        /// 线性而不是 ease-out：MC 的红闪就是逐 tick 均匀退掉的。
        /// </summary>
        public static float Hurt(float t, float peak)
        {
            if (t < 0f || t >= HurtDur) return 0f;
            return Mathf.Clamp01(peak) * (1f - t / HurtDur);
        }

        /// <summary>
        /// 玩家受击暗角浓度：先冲到峰值再按 (1-t) 线性退，叠一个余弦脉冲。
        /// 峰值 = peak，之后单调下降 —— 脉冲只改变下降的形状，不允许中途回弹到峰值。
        /// </summary>
        public static float Vignette(float t, float peak)
        {
            if (t < 0f || t >= VignDur) return 0f;
            float k = 1f - t / VignDur;
            float pulse = 0.5f + 0.5f * Mathf.Cos(t * PulseHz * Mathf.PI * 2f);
            return Mathf.Clamp01(peak * k * (0.55f + 0.45f * pulse));
        }

        /// <summary>
        /// 低血压常驻暗角：血量比例从高到低，过了 LowHealthFrom 才开始变浓，到 0 最浓。
        /// MC 在只剩一两颗心时屏幕边缘常驻红雾，跟受击瞬时闪是两回事，所以分开算。
        /// </summary>
        public static float LowHealth(float hpFrac)
        {
            float k = Mathf.Clamp01(hpFrac);
            if (k >= LowHealthFrom) return 0f;
            return Mathf.Clamp01((LowHealthFrom - k) / LowHealthFrom * LowHealthMax);
        }

        /// <summary>
        /// 两路暗角合成：取大不相加。相加会让「刚被打 + 已经残血」直接顶到 1，
        /// 整屏红得看不见敌人 —— 反馈要读得出来才有用。
        /// </summary>
        public static float Combine(float vignette, float low)
        {
            return Mathf.Clamp01(Mathf.Max(vignette, low));
        }

        /// <summary>
        /// 伤害量 → 闪的强度。MC 不看伤害大小（一律满闪），但这里一刀 5 点和一发狙击
        /// 60 点该有区别；按 24 点伤害封顶，且最低也给 0.55，免得小口径打痒痒看不见。
        /// </summary>
        public static float PeakOf(float damage)
        {
            if (damage <= 0f) return 0f;
            return Mathf.Clamp01(0.55f + 0.45f * Mathf.Clamp01(damage / 24f));
        }
    }
}
