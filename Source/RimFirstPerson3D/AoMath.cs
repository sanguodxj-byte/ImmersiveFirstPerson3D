using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L2：MC 式平滑光照 —— 顶点角 AO、四边形对角线翻转、接触变硬的半影
    // ============================================================
    public static class AoMath
    {
        /// <summary>
        /// 经典 MC 角 AO（面内两侧邻 s1、s2 与角邻 c）：两侧都挡 = 最暗（3），否则数挡了几个。
        /// 返回 0..3（0 不挡）。
        /// </summary>
        public static int CornerLevel(bool s1, bool s2, bool c)
        {
            if (s1 && s2) return 3;
            return (s1 ? 1 : 0) + (s2 ? 1 : 0) + (c ? 1 : 0);
        }

        /// <summary>
        /// 共享顶点（不知道属于哪块面片）的对称版：顶点四周四格 a(+u+v) b(+u-v) c(-u+v) d(-u-v)。
        /// 对角两格都挡（内折角，等价于 s1 && s2）→ 满；否则按挡住的格数，三格封顶。返回 0..1。
        /// </summary>
        public static float SharedVertex(bool a, bool b, bool c, bool d)
        {
            if ((a && d) || (b && c)) return 1f;
            int n = (a ? 1 : 0) + (b ? 1 : 0) + (c ? 1 : 0) + (d ? 1 : 0);
            return Mathf.Min(3, n) / 3f;
        }

        /// <summary>
        /// 对角线翻转：本项目四边形 (b,b+1,b+2)(b,b+2,b+3) 沿 0-2 对角。
        /// 0、2 两角之和比 1、3 暗时改沿 1-3 切（(b,b+1,b+3)(b+1,b+2,b+3)），
        /// 暗角就关在一个三角里，不会顺着对角线拖出一条斜纹 —— MC 平滑光照的各向异性修正。
        /// 只认这个固定模式的连续 6 个下标，其它图元（八边形、扇形）原样不动。返回翻了几块。
        /// </summary>
        public static int FlipQuads(int[] tris, Color32[] cols)
        {
            if (tris == null || cols == null) return 0;
            int flipped = 0;
            for (int i = 0; i + 5 < tris.Length; i += 6)
            {
                int b = tris[i];
                bool std = tris[i + 1] == b + 1 && tris[i + 2] == b + 2 && tris[i + 3] == b && tris[i + 4] == b + 2 && tris[i + 5] == b + 3;
                bool alt = tris[i + 1] == b + 1 && tris[i + 2] == b + 3 && tris[i + 3] == b + 1 && tris[i + 4] == b + 2 && tris[i + 5] == b + 3;
                if (!std && !alt) continue;
                if (b + 3 >= cols.Length) continue;
                float l0 = L(cols[b]), l1 = L(cols[b + 1]), l2 = L(cols[b + 2]), l3 = L(cols[b + 3]);
                bool want = l0 + l2 < l1 + l3 - 0.5f;   // 半个色阶的死区，相等时不来回翻
                if (want && std)
                {
                    tris[i + 2] = b + 3; tris[i + 3] = b + 1; tris[i + 4] = b + 2; tris[i + 5] = b + 3;
                    flipped++;
                }
                else if (!want && alt && l0 + l2 > l1 + l3 + 0.5f)
                {
                    tris[i + 2] = b + 2; tris[i + 3] = b; tris[i + 4] = b + 2; tris[i + 5] = b + 3;
                    flipped++;
                }
            }
            return flipped;
        }

        private static float L(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>半影宽（米）：遮挡物离受影点越远越宽 —— 墙脚锐、远处虚。</summary>
        public static float PenumbraWidth(float occluderDist, float k, float min, float max)
        {
            return Mathf.Clamp(Mathf.Max(0f, occluderDist) * k, min, max);
        }

        /// <summary>
        /// 一维盒式软化：在宽 w 的窗口内对二值阴影边做 3 点平均后的结果（离影边 x 米，x&gt;0 在亮侧）。
        /// 用来在台子里验「近处边锐、远处边虚」，游戏里实际是 LightField3D 的 3 点采样。
        /// </summary>
        public static float SoftEdge(float x, float w)
        {
            float s = 0f;
            for (int i = -1; i <= 1; i++) s += (x + i * w * 0.5f) >= 0f ? 1f : 0f;
            return s / 3f;
        }
    }
}
