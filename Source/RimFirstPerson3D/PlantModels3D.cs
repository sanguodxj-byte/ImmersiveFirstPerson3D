// AI生成
using System.Collections.Generic;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 植被与树木 3D 模型：运行时入口
    // ============================================================
    //
    // 【几何】外部构建（scripts/plant_build.py 单一真源）：24 个原型
    //         （14 树 + 10 小株）参数化生成，落盘 1.7\Assets/plants/<原型>.obj，
    //         本类经 ObjImporter3D 运行时导入并按原型缓存。审核页
    //         viewer/plants.html 加载的就是同一批 OBJ 文件，零第二份口径。
    // 【材质】Textures/FPS3D/PlantAtlas.png（取样自原版植物贴图，无光照
    //         Cutout 平涂 —— 与家具/残骸同一套做法）。三档色调派生自同一
    //         材质，按 thingIDNumber 哈希分档，打破大片同色。
    // 【朝向】模型自身无朝向语义，按 thingIDNumber 哈希给 0-360° 随机朝向
    //         打破克隆感；生长缩放 = lerp(0.3, 1, plant.Growth)。
    // 【描边】与 ModelBuilder 反绕外壳同法的网格层描边：按位置焊接顶点、
    //         面积加权平均法线、沿法线外扩、三角反绕。
    //
    // 接线：PlantLayer3D 把接管件让给本层（不再进星形/十字面片烘焙），
    // 未接管的植物（庄稼/花/草等）继续走原面片路径。
    //
    // 【批绘，勿改回逐件】野图接管植物数以千计（比建筑多一个量级），
    // 逐件 Graphics.DrawMesh（本体+描边）会把帧打到秒级、整机无响应 ——
    // 实机踩过。绘制一律走 PlantLayer3D 的合并烘焙批次：按（原型×色调）
    // 分组，把每实例的矩阵逐顶点烘进一份网格（Util3D.BuildInstancedMesh），
    // 全图 draw call 数 = 组数（≤ 24原型×3色调，实际远少）。
    // 本类只做三件事：defName→原型映射、原型网格缓存（本体+描边壳）、
    // 三档色调材质；生长缩放与朝向哈希由 PlantLayer3D 在烘焙矩阵时给出。
    [StaticConstructorOnStartup]
    public static class PlantModels3D
    {
        public class Built
        {
            public Mesh body;
            public Mesh hull;
        }

        private static readonly Dictionary<string, Built> cache = new Dictionary<string, Built>();
        private static Material[] mats;   // 三档色调
        private static Texture2D atlas;

        /// <summary>本系统开关（独立于其它层，便于单点回退）。</summary>
        public static bool Enabled = true;

        /// <summary>defName -> 原型名（原型清单见 scripts/plant_build.py SPEC）。</summary>
        private static readonly Dictionary<string, string> Types = new Dictionary<string, string>(30, System.StringComparer.OrdinalIgnoreCase)
        {
            { "plant_treeoak", "oak" },
            { "plant_treemaple", "maple" },
            { "plant_treepoplar", "poplar" },
            { "plant_treepine", "pine" },
            { "plant_treecypress", "cypress" },
            { "plant_treebirch", "birch" },
            { "plant_treepalm", "palm" },
            { "plant_treedrago", "drago" },
            { "plant_treeteak", "teak" },
            { "plant_treewillow", "willow" },
            { "plant_treebamboo", "bamboo" },
            { "plant_treecocoa", "cocoa" },
            { "plant_treecocoa_wild", "cocoa" },
            { "plant_treececropia", "cecropia" },
            { "plant_treeanima", "anima" },
            { "plant_bush", "bush" },
            { "plant_berry", "berry" },
            { "plant_berrybush", "berry" },
            { "plant_brambles", "brambles" },
            { "plant_shrublow", "shrublow" },
            { "plant_shrub_low", "shrublow" },
            { "plant_agave", "agave" },
            { "plant_alocasia", "alocasia" },
            { "plant_pincushioncactus", "pincushion" },
            { "plant_saguarocactus", "saguaro" },
            { "plant_sagarocactus", "saguaro" },
            { "plant_healroot", "healroot" },
            { "plant_healrootwild", "healroot" },
            { "plant_devilstrand", "devilstrand" },
        };

        /// <summary>该植物的接管原型名；未接管返回 null（回退面片路径）。</summary>
        public static string ArchOf(Plant p)
        {
            if (!Enabled || p == null || p.def == null || string.IsNullOrEmpty(p.def.defName))
                return null;
            return Types.TryGetValue(p.def.defName, out var a) ? a : null;
        }

        public static bool IsTreeArch(string arch)
        {
            if (string.IsNullOrEmpty(arch)) return false;
            switch (arch)
            {
                case "oak":
                case "maple":
                case "poplar":
                case "pine":
                case "cypress":
                case "birch":
                case "palm":
                case "drago":
                case "teak":
                case "willow":
                case "bamboo":
                case "cocoa":
                case "cecropia":
                case "anima":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>该植物是否由本层绘制（PlantLayer3D 据此跳过面片烘焙）。</summary>
        public static bool Handles(Thing t)
        {
            return t is Plant p && ArchOf(p) != null;
        }

        // ---- 材质 ----

        /// <summary>三档色调材质（i = 0/1/2）；图集缺失返回 null。</summary>
        public static Material TintMat(int i)
        {
            var arr = Mats;
            return arr != null && i >= 0 && i < arr.Length ? arr[i] : null;
        }

        private static Material[] Mats
        {
            get
            {
                if (mats != null) return mats;
                try { atlas = ContentFinder<Texture2D>.Get("FPS3D/PlantAtlas", reportFailure: false); }
                catch { atlas = null; }
                if (atlas == null) return null;
                atlas.filterMode = FilterMode.Bilinear;
                atlas.wrapMode = TextureWrapMode.Clamp;
                var baseMat = Util3D.CutoutMatFor(atlas);
                // 三档色调（与 PlantLayer3D 面片分档同色系）。
                Color[] tints =
                {
                    new Color(1.00f, 1.00f, 1.00f, 1f),
                    new Color(0.90f, 0.96f, 0.80f, 1f),
                    new Color(0.80f, 0.88f, 0.74f, 1f)
                };
                mats = new Material[3];
                for (int i = 0; i < 3; i++)
                {
                    var m = new Material(baseMat);
                    m.color = tints[i];
                    mats[i] = m;
                }
                return mats;
            }
        }

        // ---- 网格缓存（按原型；跨图共享）----

        private static string ObjPath(string arch)
        {
            var mod = ModLister.GetActiveModWithIdentifier(ModEntry.HarmonyId);
            if (mod == null) return null;
            return Path.Combine(mod.RootDir.FullName, "1.7", "Assets", "plants", arch + ".obj");
        }

        /// <summary>取原型网格（本体+描边壳）；OBJ 缺失返回 body == null 的空哨兵。</summary>
        public static Built Prototype(string arch)
        {
            if (cache.TryGetValue(arch, out var b)) return b;
            Built built = null;
            try
            {
                var path = ObjPath(arch);
                var imported = path != null ? ObjImporter3D.Load(path) : null;
                if (imported != null && imported.mesh != null)
                {
                    BakePlantShading(imported.mesh);
                    var st = FirstPerson3DMod.settings;
                    float ow = st != null ? st.outlineWidth : Metrics3D.OutlineWidth;
                    built = new Built
                    {
                        body = imported.mesh,
                        hull = BuildHull(imported.mesh, ow)
                    };
                }
            }
            catch (System.Exception e)
            {
                Log.Warning(ModEntry.LogPrefix + "plant model load failed: " + arch + " -> " + e.Message);
            }
            if (built == null)
            {
                // 缺文件 = 该原型没接管（回退面片），缓存空哨兵避免每帧重试。
                built = new Built();
            }
            cache[arch] = built;
            return built;
        }

        /// <summary>设置/资源变化时清缓存（网格跨图复用，平时不清）。</summary>
        public static void Clear()
        {
            foreach (var kv in cache)
            {
                Util3D.SafeDestroy(kv.Value.body);
                Util3D.SafeDestroy(kv.Value.hull);
            }
            cache.Clear();
        }

        // ---- 生长缩放 ----

        /// <summary>生长进度 -> 整株缩放（幼苗小、成株满高）。</summary>
        public static float GrowthScale(float growth)
        {
            return Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(growth));
        }

        // ---- 植物模型形体明暗与垂直 AO 烘焙 ----
        private static void BakePlantShading(Mesh m)
        {
            if (m == null) return;
            var verts = m.vertices;
            var nors = m.normals;
            if (verts == null || verts.Length == 0) return;
            var b = m.bounds;
            float yMin = b.min.y, ySpan = Mathf.Max(0.1f, b.size.y);
            var dir = Lighting3D.FormLightDir;
            var cols = new Color32[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = (nors != null && i < nors.Length && nors[i].sqrMagnitude > 1e-6f)
                    ? nors[i].normalized : Vector3.up;
                float ndl = Mathf.Clamp01(0.5f + 0.5f * Vector3.Dot(n, dir));
                float hy = Mathf.Clamp01((verts[i].y - yMin) / ySpan);
                // 树冠顶部饱满受光(1.08)，叶丛底层与树根部具有明显环境遮蔽(AO ~0.55)，背阴侧自然明暗
                float ao = Mathf.Lerp(0.55f, 1.08f, Mathf.Pow(hy, 0.75f));
                float diff = Mathf.Lerp(0.65f, 1.05f, ndl);
                float shade = diff * ao;
                byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(shade) * 255f);
                cols[i] = new Color32(v, v, v, 255);
            }
            m.colors32 = cols;
        }

        // ---- 描边外壳（与 ModelBuilder.EmitHull 同法的独立网格版）----

        /// <summary>
        /// 从导入网格生成反绕外壳：按位置焊接顶点、面积加权平均法线、外扩 width、反绕。
        /// 焊接量化 1/2048（约 0.5mm），与 ModelBuilder.Key 同口径。
        /// </summary>
        public static Mesh BuildHull(Mesh src, float width)
        {
            if (src == null || width <= 0f) return null;
            var verts = src.vertices;
            var tris = src.triangles;
            if (verts == null || verts.Length == 0 || tris == null || tris.Length == 0) return null;

            var map = new Dictionary<long, int>(verts.Length);
            var uniq = new List<Vector3>(verts.Length);
            var localOf = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                var v = verts[tris[i]];
                long k = Key(v);
                if (!map.TryGetValue(k, out int idx))
                {
                    idx = uniq.Count;
                    map[k] = idx;
                    uniq.Add(v);
                }
                localOf[i] = idx;
            }

            // 面积加权平均法线：叉积长度正比三角形面积，累加即得加权和。
            var acc = new Vector3[uniq.Count];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                var a = verts[tris[t]]; var b = verts[tris[t + 1]]; var c = verts[tris[t + 2]];
                var n = Vector3.Cross(b - a, c - a);
                acc[localOf[t]] += n;
                acc[localOf[t + 1]] += n;
                acc[localOf[t + 2]] += n;
            }

            Vector3 center = src.bounds.center;
            var hv = new Vector3[uniq.Count];
            for (int i = 0; i < uniq.Count; i++)
            {
                var n = acc[i];
                Vector3 dir = n.sqrMagnitude > 1e-8f ? n.normalized : (uniq[i] - center).normalized;
                hv[i] = uniq[i] + dir * width;
            }
            var ht = new int[tris.Length];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                // 反绕序：正面朝内，背面剔除后只剩轮廓外圈可见 —— 即描边。
                ht[t] = localOf[t + 2];
                ht[t + 1] = localOf[t + 1];
                ht[t + 2] = localOf[t];
            }
            var mesh = new Mesh { name = "Fps3D_PlantOutline" };
            if (hv.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = hv;
            mesh.triangles = ht;
            // 壳网格不采样贴图，但必须有全量 UV 通道：批烘焙（BuildInstancedMesh）会按
            // 顶点数索引 src.uv，缺通道会被 Unity 返回空数组 -> IndexOutOfRange（实机踩过）。
            mesh.uv = new Vector2[hv.Length];
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static long Key(Vector3 v)
        {
            long x = Mathf.RoundToInt(v.x * 2048f) + 1048576L;
            long y = Mathf.RoundToInt(v.y * 2048f) + 1048576L;
            long z = Mathf.RoundToInt(v.z * 2048f) + 1048576L;
            return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L);
        }
    }
}
