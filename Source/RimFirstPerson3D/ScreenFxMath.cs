using UnityEngine;

namespace RimFirstPerson3D
{
    // F016 L9：屏幕层的曲线（纯函数，LightSim 验）
    public static class ScreenFxMath
    {
        /// <summary>
        /// 明暗适应：相机格 skyVis 跳变时起一个脉冲。暗→亮 = 白色刺眼（正），亮→暗 = 黑色未适应（负）。
        /// 返回 [-1,1]，|值| 从 1 线性退到 0 用 dur 秒（eased）。
        /// </summary>
        public static float Adapt(float sinceChange, float dur, float sign)
        {
            if (dur <= 0f || sinceChange < 0f || sinceChange >= dur) return 0f;
            float k = 1f - sinceChange / dur;
            return Mathf.Sign(sign) * k * k;
        }

        /// <summary>跳变判定：skyVis 变化超过 0.5 才算进出室内（门口的薄屋顶不触发）。</summary>
        public static float Jump(float prevVis, float vis)
        {
            float d = vis - prevVis;
            return Mathf.Abs(d) >= 0.5f ? Mathf.Sign(d) : 0f;
        }

        /// <summary>常驻暗角与受击暗角取大不相加：受击那层已经有 hit 的浓度，常驻层只补差。</summary>
        public static float BaseVignette(float baseA, float hitA)
        {
            return Mathf.Max(0f, baseA - Mathf.Max(0f, hitA));
        }
    }
}
