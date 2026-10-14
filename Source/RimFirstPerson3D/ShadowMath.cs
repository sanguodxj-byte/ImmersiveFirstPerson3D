using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L3：平面投影阴影 —— 纯函数层
    // ============================================================
    //
    // 没有阴影图也没有自定义着色器：把物体自己的网格用一个矩阵压扁到脚下地面，用半透明黑再画一遍。
    // 这里只放矩阵与系数（tools/LightSim 链接本文件验几何），绘制在 Shadows3D。
    //
    // 约定：L 是「指向太阳」的单位向量（与 SkyScenery.Env.sunDir 同号），地面为 y = groundY。
    public static class ShadowMath
    {
        /// <summary>
        /// 平行光投影：P' = P - L·(P.y-g)/L.y，再把高度压成 g + eps·(P.y-g)。
        /// eps 保持矩阵非奇异（行列式 = eps），高度仍单调，深度测试不会把影子各层判成同深。
        /// maxRatio 封顶「水平影长 / 物高」，太阳很低时影子不会拉到无穷远。
        /// </summary>
        public static Matrix4x4 Directional(Vector3 L, float groundY, float eps, float maxRatio)
        {
            float ly = Mathf.Max(1e-3f, L.y);
            float kx = L.x / ly, kz = L.z / ly;
            float hk = Mathf.Sqrt(kx * kx + kz * kz);
            if (hk > maxRatio && hk > 1e-5f)
            {
                float s = maxRatio / hk;
                kx *= s; kz *= s;
            }
            var m = Matrix4x4.identity;
            m.m01 = -kx; m.m03 = kx * groundY;
            m.m11 = eps; m.m13 = groundY - eps * groundY;
            m.m21 = -kz; m.m23 = kz * groundY;
            return m;
        }

        /// <summary>
        /// 点光投影到 y = groundY：经典平面阴影矩阵 M = (n·l)I - l⊗n，n = (0,1,0,-g)，l = (光心,1)。
        /// 只对低于光心的点有意义（w = L.y - P.y > 0）。
        /// </summary>
        public static Matrix4x4 Point(Vector3 light, float groundY)
        {
            float d = light.y - groundY;
            var m = new Matrix4x4();
            // 行 i, 列 j: d*δij - l_i * n_j，n = (0, 1, 0, -g)
            m.m00 = d; m.m01 = -light.x; m.m02 = 0f; m.m03 = light.x * groundY;
            m.m10 = 0f; m.m11 = d - light.y; m.m12 = 0f; m.m13 = light.y * groundY;
            m.m20 = 0f; m.m21 = -light.z; m.m22 = d; m.m23 = light.z * groundY;
            m.m30 = 0f; m.m31 = -1f; m.m32 = 0f; m.m33 = d + groundY;
            return m;
        }

        /// <summary>水平影长：高 h 的点在平行光下的影子离脚多远（受 maxRatio 封顶）。</summary>
        public static float Length(float h, Vector3 L, float maxRatio)
        {
            float ly = Mathf.Max(1e-3f, L.y);
            float hk = Mathf.Sqrt(L.x * L.x + L.z * L.z) / ly;
            return h * Mathf.Min(hk, maxRatio);
        }

        /// <summary>
        /// 影子浓度：太阳越高、天越亮越浓；低于 minSunY 线性淡出；晨昏减半；室内（skyVis=0）没有。
        /// </summary>
        public static float SunAlpha(float baseAlpha, Vector3 L, float glow, float skyVis, float twilight, float minSunY)
        {
            if (L.y <= 0f || glow <= 0.01f || skyVis <= 0.01f) return 0f;
            float fade = Mathf.Clamp01((L.y - minSunY * 0.5f) / (minSunY * 0.5f));
            return Mathf.Clamp01(baseAlpha * Mathf.Clamp01(glow) * Mathf.Clamp01(skyVis) * fade * (1f - 0.5f * Mathf.Clamp01(twilight)));
        }

        /// <summary>灯影浓度：离灯越远越淡，天越亮越淡（白天灯影看不见）。</summary>
        public static float LampAlpha(float baseAlpha, float dist, float radius, float glow, float lampLuma)
        {
            if (radius <= 0.01f || dist >= radius) return 0f;
            float k = 1f - dist / radius;
            return Mathf.Clamp01(baseAlpha * k * Mathf.Clamp01(1f - glow * 1.5f) * Mathf.Clamp01(lampLuma * 1.4f));
        }

        /// <summary>
        /// 一片三角形压到地面后朝向：+1 朝上（从上往下看是正面），-1 朝下。
        /// 从上往下看的朝向只由 xz 决定，eps 的符号改不了它（LightSim 54b）。
        /// 单面立牌朝下时把它绕 Y 转 180° 再投（换一面朝上，剪影不变）；闭合网格两面都有，不必管。
        /// </summary>
        public static float FacingAfter(Matrix4x4 m, Vector3 a, Vector3 b, Vector3 c)
        {
            var pa = m.MultiplyPoint(a); var pb = m.MultiplyPoint(b); var pc = m.MultiplyPoint(c);
            var n = Vector3.Cross(pb - pa, pc - pa);
            return n.y >= 0f ? 1f : -1f;
        }

        /// <summary>太阳方位角（度）：立牌的影子用「正对太阳」的那张，而不是正对相机的那张。</summary>
        public static float SunYaw(Vector3 L)
        {
            return Mathf.Atan2(L.x, L.z) * Mathf.Rad2Deg;
        }
    }
}
