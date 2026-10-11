// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 地图野生建筑（残骸）程序化 3D 渲染
    // ============================================================
    //
    // 【几何】由 WreckageSamples 的三视图重构模型提供（样板几何已冻结，见其文件头注释）。
    // 【材质】完全取样自原版贴图合并成的图集 1.6/Textures/FPS3D/WreckageAtlas.png，
    //        UV 常量表由 WreckageUVGen 自动生成；描边由 ModelBuilder 反绕外壳负责。
    //
    // 本类是残骸在**运行时**的唯一入口：BuildingLayer3D 在烘焙时对残骸短路
    // （不再生成立板回退），改由 DrawWreckage 逐件调用 TryDraw 绘制真实几何。
    // 之前 WreckageSamples / WreckageUVGen 只是库，没有接线，故残骸在游戏里
    // 仍是原版 2D —— 本文件即补上这条接线。
    [StaticConstructorOnStartup]
    public static class Wreckage3D
    {
        private class Built
        {
            public Mesh body;
            public Mesh hull;
        }

        // 缓存键 = 归一化后的样板名（模型按样板缓存，朝向在绘制时用旋转矩阵表达）。
        private static readonly Dictionary<string, Built> cache = new Dictionary<string, Built>();
        private static Material mat;
        private static Texture2D atlas;

        /// <summary>本系统的开关（独立于其它层，便于单点回退）。</summary>
        public static bool Enabled = true;

        /// <summary>defName（归一化：小写、去下划线）-> WreckageSamples 样板名。</summary>
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(7)
        {
            { "ruinedtank", "ruinedtank" },
            { "crashedshippart", "crashedshippart" },
            { "ancientwarwalkershell", "ancientwarwalkershell" },
            { "largeengineblock", "largeengineblock" },
            { "giantwheela", "giantwheela" },
            { "concretebarrier", "concretebarrier" },
            { "rustedindustrialtruck", "rustedindustrialtruck" },
        };

        private static readonly Dictionary<ThingDef, string> wreckageDefCache = new Dictionary<ThingDef, string>();

        private static string Key(Thing t)
        {
            if (t == null || t.def == null) return null;
            if (wreckageDefCache.TryGetValue(t.def, out var cached)) return cached;
            if (string.IsNullOrEmpty(t.def.defName))
            {
                wreckageDefCache[t.def] = null;
                return null;
            }
            string n = t.def.defName.ToLowerInvariant().Replace("_", "").Replace(" ", "");
            string name = Types.TryGetValue(n, out string val) ? val : null;
            wreckageDefCache[t.def] = name;
            return name;
        }

        /// <summary>该建筑是否由本层绘制（BuildingLayer3D 据此跳过立板回退）。</summary>
        public static bool Handles(Thing t)
        {
            if (!Enabled || t == null || t.def == null) return false;
            if (t.def.category != ThingCategory.Building) return false;
            if (t.def.Fillage == FillCategory.Full) return false;
            if (t is Building_Door) return false;
            return Key(t) != null;
        }

        // ---- 材质 ----

        private static Material Mat
        {
            get
            {
                if (mat != null) return mat;
                try { atlas = ContentFinder<Texture2D>.Get(WreckageUVGen.TexPath, reportFailure: false); }
                catch { atlas = null; }
                if (atlas == null) return null;
                atlas.filterMode = FilterMode.Bilinear;
                atlas.wrapMode = TextureWrapMode.Clamp;
                mat = Util3D.CutoutMatFor(atlas);
                return mat;
            }
        }

        // ---- 网格缓存（按样板名 + 朝向）----
        //
        // 朝向烘进网格而不是绘制时再转：顶点色里的形体明暗是按世界太阳算的，
        // 若网格保持模型空间，物体一转，被照亮的这一面就跟着一起转（F013）。

        private static Built Get(string name, float yaw)
        {
            string ck = name + "|" + Mathf.RoundToInt(yaw);
            Built b;
            if (cache.TryGetValue(ck, out b)) return b;
            var uv = WreckageSamples.Uv(name);
            var mb = new ModelBuilder(uv, false);
            mb.AddAll(WreckageSamples.Build(name), Matrix4x4.TRS(Vector3.zero, Quaternion.AngleAxis(yaw, Vector3.up), Vector3.one));
            var st = FirstPerson3DMod.settings;
            float ow = st != null ? st.outlineWidth : Metrics3D.OutlineWidth;
            b = new Built
            {
                body = mb.ToMesh(),
                hull = mb.ToHull(ow),
            };
            cache[ck] = b;
            return b;
        }

        /// <summary>地图切换/重载时清缓存（释放旧网格）。</summary>
        public static void Clear()
        {
            foreach (var kv in cache)
            {
                Util3D.SafeDestroy(kv.Value.body);
                Util3D.SafeDestroy(kv.Value.hull);
            }
            cache.Clear();
        }

        // ---- 绘制 ----

        /// <summary>画一件残骸；返回 false 表示本层没接管（调用方走原回退路径）。
        /// lit 是该件所在处的放置亮度（F013 实例乘子）。</summary>
        public static bool TryDraw(Thing t, Vector3 pos, Color lit)
        {
            if (!Handles(t)) return false;
            var m = Mat;
            if (m == null) return false;
            string name = Key(t);
            if (name == null) return false;

            // 残骸几何不含朝向：按建筑旋转烘进网格缓存，绘制矩阵只管位置。
            float yaw = t.Rotation.AsAngle;
            var b = Get(name, yaw);
            if (b == null || b.body == null) return false;
            var matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
            Graphics.DrawMesh(b.body, matrix, Lighting3D.MatFor(m, lit), 0);
            PerfCounters.BuildingMeshesDrawn++;
            if (Fps3DState.DrawOutlines && b.hull != null)
            {
                Graphics.DrawMesh(b.hull, matrix, Util3D.OutlineMat, 0);
                PerfCounters.OutlineMeshesDrawn++;
            }
            return true;
        }
    }
}