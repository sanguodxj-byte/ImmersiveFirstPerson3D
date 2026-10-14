using System;
using UnityEngine;

namespace RimFirstPerson3D.LightSim
{
    static class SkyChecks
    {
        static void Check(string name, bool ok, string detail = "") => Program.Check(name, ok, detail);

        public static void Run()
        {
            bool closed = true, poles = true, unit = true;
            foreach (bool hemisphere in new[] { false, true })
            {
                int lat = hemisphere ? 32 : 40, lon = hemisphere ? 72 : 64;
                for (int y = 0; y <= lat; y++)
                {
                    var first = SkyMath.SphereDirection(0, y, lon, lat, hemisphere);
                    var last = SkyMath.SphereDirection(lon, y, lon, lat, hemisphere);
                    closed &= first.x == last.x && first.y == last.y && first.z == last.z;
                    for (int x = 0; x <= lon; x++)
                    {
                        var dir = SkyMath.SphereDirection(x, y, lon, lat, hemisphere);
                        unit &= float.IsFinite(dir.x) && float.IsFinite(dir.y) && float.IsFinite(dir.z)
                            && Mathf.Abs(dir.magnitude - 1f) < 2e-6f;
                        if (y == 0 || (!hemisphere && y == lat))
                            poles &= dir.x == 0f && dir.z == 0f && dir.y == (y == 0 ? 1f : -1f);
                    }
                }
            }
            Check("sky01 球面经度闭合使用严格同点", closed);
            Check("sky02 两极不存在三角函数残余偏移", poles);
            Check("sky03 全部球面方向有限且归一", unit);

            float worstPeriod = 0f;
            bool noiseRange = true;
            float minimum = 1f, maximum = 0f;
            for (int y = -16; y <= 16; y++)
                for (int x = -16; x <= 16; x++)
                {
                    float u = x * 0.037f + 0.013f, v = y * 0.041f - 0.017f;
                    float n = SkyMath.CloudNoise(u, v);
                    noiseRange &= float.IsFinite(n) && n >= 0f && n <= 1f;
                    minimum = Mathf.Min(minimum, n); maximum = Mathf.Max(maximum, n);
                    worstPeriod = Mathf.Max(worstPeriod, Mathf.Abs(n - SkyMath.CloudNoise(u + 1f, v)));
                    worstPeriod = Mathf.Max(worstPeriod, Mathf.Abs(n - SkyMath.CloudNoise(u, v - 1f)));
                }
            Check("sky04 云场双轴周期包括负坐标", worstPeriod < 2e-5f, "max=" + worstPeriod);
            Check("sky05 云场有限且具有非平凡变化", noiseRange && maximum - minimum > 0.1f,
                "range=" + minimum + ".." + maximum);

            const float epsilon = 1e-5f;
            float worstBoundary = 0f;
            for (int i = 0; i <= 100; i++)
            {
                float v = i * 0.01f;
                worstBoundary = Mathf.Max(worstBoundary,
                    Mathf.Abs(SkyMath.CloudNoise(-epsilon, v) - SkyMath.CloudNoise(epsilon, v)));
                worstBoundary = Mathf.Max(worstBoundary,
                    Mathf.Abs(SkyMath.CloudNoise(v, -epsilon) - SkyMath.CloudNoise(v, epsilon)));
            }
            Check("sky06 云场循环边界连续", worstBoundary < 0.002f, "max=" + worstBoundary);

            var seamA = SkyMath.CloudUv(new Vector3(0.8f, 0.6f, -epsilon), 0.35f);
            var seamB = SkyMath.CloudUv(new Vector3(0.8f, 0.6f, epsilon), 0.35f);
            Check("sky07 云采样跨原经度接缝连续", (seamA - seamB).magnitude < 0.001f);
            var pole = SkyMath.CloudUv(Vector3.up, 0.35f);
            bool poleContinuous = float.IsFinite(pole.x) && float.IsFinite(pole.y);
            bool horizonFinite = true;
            for (int i = 0; i < 72; i++)
            {
                float angle = i * Mathf.PI * 2f / 72f;
                var nearPole = SkyMath.CloudUv(new Vector3(Mathf.Cos(angle) * epsilon, 1f, Mathf.Sin(angle) * epsilon), 0.35f);
                poleContinuous &= (nearPole - pole).magnitude < 0.001f;
                var horizon = SkyMath.CloudUv(new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)), 0.35f);
                horizonFinite &= float.IsFinite(horizon.x) && float.IsFinite(horizon.y);
            }
            Check("sky08 天顶云采样不依赖经度", poleContinuous);
            Check("sky09 地平线投影无除零", horizonFinite);
            var north = SkyMath.CloudUv(new Vector3(0f, 0.8f, 0.6f), 0.35f);
            var south = SkyMath.CloudUv(new Vector3(0f, 0.8f, -0.6f), 0.35f);
            Check("sky10 双轴投影保留纵向云运动", Mathf.Abs(north.y - south.y) > 0.1f);

            bool wrapped = true;
            for (int i = -1000; i <= 1000; i++)
            {
                float v = i * 0.137f;
                float w = SkyMath.Wrap01(v);
                wrapped &= float.IsFinite(w) && w >= 0f && w < 1f && Mathf.Abs((v - w) - Mathf.Round(v - w)) < 2e-5f;
            }
            Check("sky11 风向偏移双向规整保持周期等价", wrapped);
        }
    }
}
