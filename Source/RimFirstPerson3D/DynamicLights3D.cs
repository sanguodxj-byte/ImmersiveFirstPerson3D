using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L8：动态光源（随身发光的 pawn / 装备 / 服装）
    // ============================================================
    //
    // 原版 glowGrid 只认地图上的建筑与火：拿在手里、穿在身上、本身会发光的 pawn 不进格子光。
    // 这里每 0.25 s 扫一遍本图 pawn，把带 CompGlower 且正在发光的那些作为「虚拟灯」：
    //   位置量化到 1 m（小步挪动不重烘）；半径取 GlowRadius，封顶 8 m；颜色取 GlowColor。
    // 光照通路不另起：LightField3D.LampAt 把虚拟灯加在 glowGrid 值上，烘焙与实例乘子自然吃到；
    // 灯动了只把新旧两处半径内的格标脏，仍走 ChunkRebuildBudget。
    // 枪口火光（F015）太快，不进烘焙，只走光晕与贴地光池。
    public static partial class DynamicLights3D
    {
        public const float MaxRadius = 8f;
        public static int Version { get; private set; }
        private const float Poll = 0.25f;

        public struct VLight
        {
            public int x, z;          // 量化后的格（光心 = x+0.5, z+0.5）
            public float radius;
            public Color color;       // 与 glowGrid 归一化口径一致（0..1）
        }

        private sealed class MapState
        {
            public readonly List<VLight> lights = new List<VLight>();
            public readonly List<VLight> prev = new List<VLight>();
            public float poll = Poll;
            public readonly List<IntVec3> dirty = new List<IntVec3>();
        }

        private static readonly Dictionary<int, MapState> states = new Dictionary<int, MapState>();

        public static int Count(Map m) => m != null && states.TryGetValue(m.uniqueID, out var s) ? s.lights.Count : 0;

        /// <summary>每帧每图（Scene3D.TickLighting）推进；返回需要重烘的格（可能为空）。</summary>
        public static List<IntVec3> Tick(Map map, float dt)
        {
            if (map == null) return null;
            if (!states.TryGetValue(map.uniqueID, out var st)) { st = new MapState(); states[map.uniqueID] = st; }
            st.dirty.Clear();
            if (!Shaderpack3D.DynLights)
            {
                if (st.lights.Count > 0) { MarkAround(map, st.lights, st.dirty); st.lights.Clear(); Version++; }
                return st.dirty;
            }
            st.poll += dt;
            if (st.poll < Poll) return st.dirty;
            st.poll = 0f;
            st.prev.Clear();
            st.prev.AddRange(st.lights);
            st.lights.Clear();
            Scan(map, st.lights);
            if (!Same(st.prev, st.lights))
            {
                MarkAround(map, st.prev, st.dirty);
                MarkAround(map, st.lights, st.dirty);
                Version++;
            }
            return st.dirty;
        }

        private static void Scan(Map map, List<VLight> outL)
        {
            IReadOnlyList<Pawn> pawns = null;
            try { pawns = map.mapPawns.AllPawnsSpawned; } catch { }
            if (pawns == null) return;
            for (int i = 0; i < pawns.Count && outL.Count < 32; i++)
            {
                var p = pawns[i];
                if (p == null || !p.Spawned) continue;
                if (TryGlow(p, out float r, out Color c)
                    || TryGlow(p.equipment != null ? p.equipment.Primary : null, out r, out c)
                    || TryApparel(p, out r, out c))
                {
                    var dp = p.DrawPos;
                    outL.Add(new VLight
                    {
                        x = Mathf.FloorToInt(dp.x),
                        z = Mathf.FloorToInt(dp.z),
                        radius = Mathf.Min(MaxRadius, r),
                        color = c,
                    });
                }
            }
        }

        private static bool TryApparel(Pawn p, out float r, out Color c)
        {
            r = 0f; c = Color.black;
            List<Apparel> worn = null;
            try { worn = p.apparel != null ? p.apparel.WornApparel : null; } catch { }
            if (worn == null) return false;
            for (int i = 0; i < worn.Count; i++)
                if (TryGlow(worn[i], out r, out c)) return true;
            return false;
        }

        private static bool TryGlow(Thing t, out float r, out Color c)
        {
            r = 0f; c = Color.black;
            if (t == null) return false;
            CompGlower g = null;
            try { g = t.TryGetComp<CompGlower>(); } catch { }
            if (g == null) return false;
            try
            {
                // 地图上的建筑/火已经在 glowGrid 里；这里只收「不在格子光里」的随身光源
                if (t.Spawned && t.def.category == ThingCategory.Building) return false;
                if (!(t is Pawn) && !g.Props.glowRadius.Equals(0f) && !(t.ParentHolder is Pawn_EquipmentTracker || t.ParentHolder is Pawn_ApparelTracker)) return false;
                r = g.Props.glowRadius;
                var gc = g.Props.glowColor.ToColor;
                c = new Color(gc.r, gc.g, gc.b, 1f);
            }
            catch { return false; }
            return r > 0.5f;
        }

        private static bool Same(List<VLight> a, List<VLight> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].x != b[i].x || a[i].z != b[i].z || Mathf.Abs(a[i].radius - b[i].radius) > 0.01f
                    || Mathf.Abs(a[i].color.r - b[i].color.r) > 0.001f
                    || Mathf.Abs(a[i].color.g - b[i].color.g) > 0.001f
                    || Mathf.Abs(a[i].color.b - b[i].color.b) > 0.001f) return false;
            return true;
        }

        private static void MarkAround(Map map, List<VLight> ls, List<IntVec3> outCells)
        {
            for (int i = 0; i < ls.Count; i++)
            {
                int r = Mathf.CeilToInt(ls[i].radius);
                for (int dz = -r; dz <= r; dz += 4)
                    for (int dx = -r; dx <= r; dx += 4)
                    {
                        // chunk 级标脏：每 4 格一个代表点足够覆盖到所有受影响 chunk（chunk 边 16 m）
                        var c = new IntVec3(ls[i].x + dx, 0, ls[i].z + dz);
                        if (c.InBounds(map) && !outCells.Contains(c)) outCells.Add(c);
                    }
            }
        }

        /// <summary>本图虚拟灯在 (x,z) 处的彩色辉光（与 glowGrid 归一化口径一致）。热路径：灯数很少。</summary>
        public static Color At(int mapId, float x, float z)
        {
            if (!states.TryGetValue(mapId, out var st) || st.lights.Count == 0) return Color.black;
            float r = 0f, g = 0f, b = 0f;
            for (int i = 0; i < st.lights.Count; i++)
            {
                var l = st.lights[i];
                float k = LightFalloff(x - (l.x + 0.5f), z - (l.z + 0.5f), l.radius);
                if (k <= 0f) continue;
                r += l.color.r * k; g += l.color.g * k; b += l.color.b * k;
            }
            return new Color(r, g, b, 1f);
        }

        /// <summary>衰减式在 FlareMath.PointFalloff（纯函数，LightSim 验）。</summary>
        public static float LightFalloff(float dx, float dz, float radius) => FlareMath.PointFalloff(dx, dz, radius);

        public static void Forget(Map m)
        {
            if (m != null) states.Remove(m.uniqueID);
        }
    }
}
