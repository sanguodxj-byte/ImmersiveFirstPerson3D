using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // 武器射击效果的纯数学（F015）—— 无 Verse、无贴图、无状态
    // ============================================================
    //
    // 与 HitFxMath / LightShade 同一理由：这份要在播放器外面被离线台链接着验。
    // 弹丸位置本身不自算 —— 用原版 Projectile.ExactPosition（含抛物线弹道的那条覆写），
    // 这里只管「看得见的那一小段」怎么画。规范：docs/features/F015-shot-effects.md

    public static class ShotFxMath
    {
        /// <summary>枪口火光时长（秒）。真枪的火光只有几毫秒，但游戏里要看得见。</summary>
        public const float FlashDur = 0.07f;

        /// <summary>落点烟尘时长（秒）。</summary>
        public const float PuffDur = 0.26f;

        /// <summary>拖焰长度 = 速度 × 这个保持时间，再夹进 [TracerMin, TracerMax]。</summary>
        public const float TracerHold = 0.05f;
        public const float TracerMin = 0.30f;
        public const float TracerMax = 2.60f;

        /// <summary>拖焰厚度（米）。太细在 4K 下会糊成一条不存在的线。</summary>
        public const float TracerThick = 0.055f;

        /// <summary>烟尘在窗口内长到几倍底尺寸。</summary>
        public const float PuffGrow = 2.1f;

        /// <summary>
        /// 线性衰减：t 在 [0,dur) 内从 1 退到 0，窗口外为 0。
        /// dur<=0 时一律 0（防除零，也防「忘了给时长」变成永远亮）。
        /// </summary>
        public static float Decay(float t, float dur)
        {
            if (dur <= 0f || t < 0f || t >= dur) return 0f;
            return 1f - t / dur;
        }

        /// <summary>
        /// 拖焰长度（米）：弹速 × TracerHold，夹进上下限；速度取不到就没有拖焰。
        /// 按速度而不是「全程比例」：同一把枪打近打远拖焰一样长才像回事，
        /// 按比例会让墙后 2 米的一枪拖出一条 2 米长的火鞭。
        /// </summary>
        public static float TracerLen(float speedMetersPerSec)
        {
            if (speedMetersPerSec <= 0f) return 0f;
            return Mathf.Clamp(speedMetersPerSec * TracerHold, TracerMin, TracerMax);
        }

        /// <summary>
        /// 烟尘尺寸：随时间长大（0.35 -> 1 倍之间插到 PuffGrow），与透明度反向。
        /// 边淡边长是 MC 那类爆点的读法；反过来（边浓边缩）看着像被吸回去。
        /// </summary>
        public static float PuffSize(float t, float baseSize)
        {
            float k = Decay(t, PuffDur);
            if (k <= 0f) return 0f;
            float grow = 1f + (PuffGrow - 1f) * (1f - k);
            return Mathf.Max(0f, baseSize) * grow;
        }

        /// <summary>烟尘不透明度：跟着衰减走，上限压到 0.75 免得盖住目标。</summary>
        public static float PuffAlpha(float t)
        {
            return Mathf.Clamp01(Decay(t, PuffDur) * 0.75f);
        }

        /// <summary>
        /// 拖焰尾端：从弹丸位置沿运动的反方向退 len 米。
        /// 方向为零（垂直打或速度取不到）时退回弹丸本身，长度 0 —— 调用方按长度决定画不画。
        /// </summary>
        public static Vector3 TracerTail(Vector3 pos, Vector3 vel, float len, out float actual)
        {
            float m = vel.magnitude;
            if (m < 1e-5f || len <= 0f)
            {
                actual = 0f;
                return pos;
            }
            actual = len;
            return pos - vel / m * len;
        }

        /// <summary>
        /// 枪口火光的世界位置：从发射点沿弹道的水平方向前移一段。
        /// 不挪的话火光埋在发射者躯干里（原版 shotOrigin 就在身上）。
        /// 方向退化（零向量）时原样返回，调用方仍会画，只是不偏移。
        /// </summary>
        public static Vector3 MuzzleAt(Vector3 origin, Vector3 dirHorizontal, float forward)
        {
            var d = dirHorizontal;
            d.y = 0f;
            float m = d.magnitude;
            if (m < 1e-4f) return origin;
            return origin + d / m * Mathf.Max(0f, forward);
        }
    }
}
