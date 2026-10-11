// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 程序化小贴图
    // ============================================================
    public static class ProcTex3D
    {
        /// <summary>径向渐变（中心不透明 -> 边缘透明），用于阴影盘/光斑。</summary>
        public static Texture2D Radial(int n, float inner, float gamma)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Fps3D_Radial" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = r <= inner ? 1f : Mathf.Clamp01(1f - (r - inner) / Mathf.Max(0.001f, 1f - inner));
                    a = Mathf.Pow(a, gamma);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>
        /// 屏幕暗角（中心透明 -> 四边不透明），供受击红雾（F014）与低血压常驻红边。
        /// 带按**像素**算到最近屏幕边的距离，不是按归一化半径 —— 后者会把 16:9 的横边
        /// 拉得比竖边宽一倍，红雾看着就是歪的。
        /// inner = 带厚占短边的比例，gamma 控制由边向内的衰减形状。
        /// </summary>
        public static Texture2D Vignette(int w, int h, float inner, float gamma)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Fps3D_Vignette" };
            var px = new Color32[w * h];
            float band = Mathf.Max(1f, Mathf.Min(w, h) * Mathf.Clamp01(inner));
            for (int y = 0; y < h; y++)
            {
                float dy = Mathf.Min(y + 0.5f, h - 0.5f - y);
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Min(x + 0.5f, w - 0.5f - x);
                    float d = Mathf.Min(dx, dy);
                    float a = d >= band ? 0f : Mathf.Clamp01(1f - d / band);
                    a = Mathf.Pow(a, gamma);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        /// <summary>
        /// 预乘 alpha 的纯加法光斑（中心满、边缘 0，smooth 再高斯柔化）。
        /// 与 Radial 的区别是 RGB 也乘了 alpha：加法混合下不这么做会带一圈黑边。
        /// 灯具发光片（F013）与射击火光/烟尘（F015）共用这一份。
        /// </summary>
        public static Texture2D AdditiveRadial(int n)
        {
            try
            {
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
                {
                    name = "Fps3D_AdditiveRadial",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                var px = new Color32[n * n];
                float c = (n - 1) * 0.5f;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x - c) / c, dy = (y - c) / c;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        a *= a;
                        byte v = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(a));
                        px[y * n + x] = new Color32(v, v, v, v);
                    }
                }
                t.SetPixels32(px);
                t.Apply(false, false);
                return t;
            }
            catch { return null; }
        }

        /// <summary>可平铺水波纹：亮度/透明度随 FBM 噪声起伏，供水面平铺滚动。</summary>
        public static Texture2D Ripple(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Fps3D_Ripple" };
            var px = new Color32[n * n];
            const float S = 5f;
            for (int y = 0; y < n; y++)
            {
                float v = y / (float)n;
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)n;
                    float a = Fbm(u * S, v * S), b = Fbm((u - 1f) * S, v * S);
                    float c = Fbm(u * S, (v - 1f) * S), d = Fbm((u - 1f) * S, (v - 1f) * S);
                    float val = a * (1 - u) * (1 - v) + b * u * (1 - v) + c * (1 - u) * v + d * u * v;
                    // 高光细线：噪声峰值处变亮
                    float hi = Mathf.Clamp01((val - 0.58f) / 0.12f);
                    byte l = (byte)(255f * Mathf.Lerp(0.78f, 1f, hi));
                    byte al = (byte)(255f * Mathf.Lerp(0.82f, 1f, hi));
                    px[y * n + x] = new Color32(l, l, 255, al);
                }
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private static float Fbm(float x, float y)
        {
            float v = 0f, amp = 0.5f, f = 1f;
            for (int i = 0; i < 4; i++)
            {
                v += Mathf.PerlinNoise(x * f + 3.3f, y * f + 9.9f) * amp;
                f *= 2.01f;
                amp *= 0.5f;
            }
            return v;
        }
    }

    // ============================================================
    // 水体层：真实深度的水
    // ============================================================
    //
    // 原版水是一张贴图 + 深度 shader 做的平面效果；3D 化后应该有**真正的深度**：
    //   水底   —— 按水深下沉的河床（沙质贴图，越深越暗）
    //   岸壁   —— 水底与岸/浅水之间的竖直落差面，让人看见水是"凹进去"的
    //   水面   —— 半透明、按深度连续着色、平铺水波纹并缓慢流动（河流沿定向滚动）
    //   浅岸   —— 与陆地相邻的一圈浅水更亮更透，形成自然的岸线过渡
    //
    // 深度读数来自「水面 - 河床」的落差，以及「水面/河床颜色随深度连续变化」：
    //   水面越深越暗蓝、越不透；河床越深越暗。两者叠加后，浅→深会呈现明显的暗化梯度，
    //   俯视/斜视都能一眼区分浅滩与深水，而不再是一片等色平面。
    // 深度分档见 DepthBuckets：把连续水深量化成 N 档材质并在档间线性插值，
    // 因原版 Transparent/Cutout 着色器是否读顶点色不确定，故不依赖顶点色。
    //
    // 深度常量见 Metrics3D（现实水深：齐膝/齐胸/深水）。
    // 地形层会跳过 IsWater 格（见 TerrainLayer3D），沼泽不算水体，只做半透明覆盖。
    [StaticConstructorOnStartup]
    public class WaterLayer3D
    {
        public const int ChunkSize = 16;

        private enum Cls { None = 0, Shore, Shallow, Chest, Deep, Marsh }

        private class Chunk
        {
            public readonly List<Mesh> surf = new List<Mesh>();
            public readonly List<Material> surfMat = new List<Material>();
            // 河床按深度分档，深档更暗，俯视时可读出水的深浅
            public readonly List<Mesh> bed = new List<Mesh>();
            public readonly List<Material> bedMat = new List<Material>();
            // 朝陆地的岸壁，按邻格地面材质分网格
            public readonly List<Mesh> shore = new List<Mesh>();
            public readonly List<Material> shoreMat = new List<Material>();
            public bool dirty = true;
            public bool hasWater;
        }

        private readonly Map map;
        private readonly float baseY;
        private readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        private bool allDirty = true;

        public Vector3 worldOffset = Vector3.zero;

        // 光照场（F013），由 Scene3D 注入
        public LightField3D light;

        /// <summary>把本层网格的颜色统计并进探针（只在按需导出时走）。</summary>
        public void Summarize(Lighting3D.Probe p)
        {
            foreach (var kv in chunks)
            {
                var c = kv.Value;
                for (int i = 0; i < c.surf.Count; i++) p.Add(c.surf[i]);
                for (int i = 0; i < c.bed.Count; i++) p.Add(c.bed[i]);
                for (int i = 0; i < c.shore.Count; i++) p.Add(c.shore[i]);
            }
        }

        private static Texture2D rippleTex;
        private static readonly Dictionary<int, Material> bedMats = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> surfMats = new Dictionary<int, Material>();
        private static readonly Dictionary<TerrainDef, Material> landMats = new Dictionary<TerrainDef, Material>();
        private static float scrollT;

        // 深度分档：把连续水深量化成 N 档材质，颜色/透明度在档间线性插值，
        // 视觉上接近连续渐变，又不必依赖顶点色（原版 Transparent 着色器是否读顶点色不确定）。
        private const int DepthBuckets = 6;

        public WaterLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty() => allDirty = true;

        public void MarkCellDirty(IntVec3 c)
        {
            // 岸线分类依赖邻格：边界格要把相邻块一并标脏。
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var n = new IntVec3(c.x + dx, 0, c.z + dz);
                    if (!n.InBounds(map)) continue;
                    int key = (n.x / ChunkSize) * 10000 + (n.z / ChunkSize);
                    if (!chunks.TryGetValue(key, out var ch)) { ch = new Chunk(); chunks[key] = ch; }
                    ch.dirty = true;
                }
        }

        public void Update()
        {
            if (allDirty)
            {
                int cxMax = (map.Size.x + ChunkSize - 1) / ChunkSize;
                int czMax = (map.Size.z + ChunkSize - 1) / ChunkSize;
                for (int cx = 0; cx < cxMax; cx++)
                    for (int cz = 0; cz < czMax; cz++)
                    {
                        int key = cx * 10000 + cz;
                        if (!chunks.TryGetValue(key, out var ch))
                        {
                            ch = new Chunk();
                            chunks[key] = ch;
                        }
                        ch.dirty = true;
                    }
                allDirty = false;
            }
            int budget = Fps3DState.ChunkRebuildBudget;
            foreach (var kv in chunks)
            {
                if (!kv.Value.dirty) continue;
                if (budget <= 0) break;
                RebuildChunk(kv.Key / 10000, kv.Key % 10000, kv.Value);
                budget--;
            }
        }

        // ---- 分类 ----

        private static Cls Classify(TerrainDef td)
        {
            if (td == null) return Cls.None;
            string n = td.defName;
            if (n == "Marsh" || n.StartsWith("Marsh")) return Cls.Marsh;
            if (!td.IsWater) return Cls.None;
            if (n.Contains("ChestDeep")) return Cls.Chest;
            if (n.Contains("Deep")) return Cls.Deep;
            return Cls.Shallow;
        }

        private static bool IsMoving(TerrainDef td)
        {
            if (td == null) return false;
            if (td.defName.Contains("Moving")) return true;
            return td.tags != null && td.tags.Contains("River");
        }

        private static float DepthOf(Cls c)
        {
            switch (c)
            {
                case Cls.Shore: return Metrics3D.WaterDepthShore;
                case Cls.Shallow: return Metrics3D.WaterDepthShallow;
                case Cls.Chest: return Metrics3D.WaterDepthChest;
                case Cls.Deep: return Metrics3D.WaterDepthDeep;
                default: return 0f;
            }
        }

        private Cls ClassAt(IntVec3 c) => ClassOfCell(map, c);

        private static Cls ClassOfCell(Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return Cls.None;
            var cls = Classify(map.terrainGrid.TerrainAt(c));
            if (cls == Cls.Shallow)
            {
                // 与陆地四邻接触的浅水 = 浅岸
                if (Classify(TerrainAtOrNull(map, c + IntVec3.North)) == Cls.None
                    || Classify(TerrainAtOrNull(map, c + IntVec3.South)) == Cls.None
                    || Classify(TerrainAtOrNull(map, c + IntVec3.East)) == Cls.None
                    || Classify(TerrainAtOrNull(map, c + IntVec3.West)) == Cls.None)
                    cls = Cls.Shore;
            }
            return cls;
        }

        private static TerrainDef TerrainAtOrNull(Map map, IntVec3 c)
            => (map != null && c.InBounds(map)) ? map.terrainGrid.TerrainAt(c) : null;

        /// <summary>
        /// 身处该格的单位（玩家/小人/动物/物品）应下沉的高度（米）：水面落差 + 该处水深。
        /// 非水体（陆地、沼泽）返回 0。用于让身在水体中者脚踩河床，而不浮在水面上方。
        /// </summary>
        public static float SinkAt(Map map, IntVec3 c)
        {
            float depth = DepthOf(ClassOfCell(map, c));
            return depth <= 0f ? 0f : Metrics3D.WaterSurfaceDrop + depth;
        }

        // ---- 重建 ----

        private void RebuildChunk(int cx, int cz, Chunk chunk)
        {
            DisposeChunk(chunk);
            chunk.dirty = false;
            var rect = new CellRect(cx * ChunkSize, cz * ChunkSize, ChunkSize, ChunkSize);
            rect.ClipInsideMap(map);
            if (rect.IsEmpty) return;

            var surfBuilders = new Dictionary<int, QuadBuilder>();
            var shoreBuilders = new Dictionary<Material, QuadBuilder>();
            var bedBuilders = new Dictionary<int, QuadBuilder>();
            bool any = false;

            foreach (var c in rect)
            {
                var td = map.terrainGrid.TerrainAt(c);
                var cls = ClassAt(c);
                if (cls == Cls.None) continue;
                any = true;
                float X = c.x, Z = c.z;
                float depth = DepthOf(cls);
                bool moving = IsMoving(td);

                // 水面（含沼泽覆盖）。key：沼泽单独一档（900+moving）；其余 = 深度档 * 2 + moving。
                bool marsh = cls == Cls.Marsh;
                int bucket = BucketOf(depth);
                int key = marsh ? (900 + (moving ? 1 : 0)) : (bucket * 2 + (moving ? 1 : 0));
                if (!surfBuilders.TryGetValue(key, out var sb)) { sb = new QuadBuilder(); surfBuilders[key] = sb; }
                // 水面：比周围地面低一截（WaterSurfaceDrop），做出"水凹下去"的落差；沼泽只是湿地覆盖，维持在地面高度。
                float ys = marsh ? (baseY + Metrics3D.WaterSurfaceLift) : (baseY - Metrics3D.WaterSurfaceDrop);
                sb.Quad(new Vector3(X, ys, Z + 1), new Vector3(X + 1, ys, Z + 1),
                        new Vector3(X + 1, ys, Z), new Vector3(X, ys, Z),
                        new Vector2(X * 0.5f, (Z + 1) * 0.5f), new Vector2((X + 1) * 0.5f, (Z + 1) * 0.5f),
                        new Vector2((X + 1) * 0.5f, Z * 0.5f), new Vector2(X * 0.5f, Z * 0.5f));

                if (depth <= 0f) continue;
                // 水底（按深度分档，越深越暗）：自水面再下沉 depth。
                float yb = ys - depth;
                BedBuilder(bucket).Quad(new Vector3(X, yb, Z + 1), new Vector3(X + 1, yb, Z + 1),
                                        new Vector3(X + 1, yb, Z), new Vector3(X, yb, Z),
                                        new Vector2(X, Z + 1), new Vector2(X + 1, Z + 1), new Vector2(X + 1, Z), new Vector2(X, Z));
                // 岸壁：朝向更浅邻格的竖面。邻格是陆地/沼泽时用该格地面材质并一直填到地面顶，
                // 把"水面比地面低"的这一截落差补上；邻格是更浅的水时只填到其河床，避免冒出其水面。
                IntVec3 nN = c + IntVec3.North, nS = c + IntVec3.South;
                IntVec3 nE = c + IntVec3.East, nW = c + IntVec3.West;
                Cls cN = ClassAt(nN), cS = ClassAt(nS), cE = ClassAt(nE), cW = ClassAt(nW);
                float dN = DepthOf(cN), dS = DepthOf(cS), dE = DepthOf(cE), dW = DepthOf(cW);
                if (dN < depth) EmitBank(nN, X, X + 1, Z + 1, yb, 2, bucket);
                if (dS < depth) EmitBank(nS, X, X + 1, Z, yb, 0, bucket);
                if (dE < depth) EmitBank(nE, Z, Z + 1, X + 1, yb, 3, bucket);
                if (dW < depth) EmitBank(nW, Z, Z + 1, X, yb, 1, bucket);
            }

            QuadBuilder BedBuilder(int b)
            {
                if (!bedBuilders.TryGetValue(b, out var bb)) { bb = new QuadBuilder(); bedBuilders[b] = bb; }
                return bb;
            }

            void EmitBank(IntVec3 nb, float a0, float a1, float fixedCoord, float y0, int side, int bucket)
            {
                float y1 = baseY; // 缺省填到地面顶（陆地/沼泽/地图外）
                QuadBuilder target = BedBuilder(bucket);
                // 邻格不是水（含 MarshyTerrain，它的名字以 Marsh 开头，ClassAt 会把它当成沼泽）才贴地面。
                if (nb.InBounds(map))
                {
                    TerrainDef ntd = map.terrainGrid.TerrainAt(nb);
                    if (ntd != null && !ntd.IsWater)
                    {
                        Material lm = LandMaterial(ntd);
                        if (lm != null)
                        {
                            if (!shoreBuilders.TryGetValue(lm, out target))
                            {
                                target = new QuadBuilder();
                                shoreBuilders[lm] = target;
                            }
                        }
                    }
                    else
                    {
                        // 邻格是更浅的水：岸壁只填到其河床（水面之下），不冒出其水面。
                        y1 = baseY - Metrics3D.WaterSurfaceDrop - DepthOf(ClassAt(nb));
                    }
                }
                Bank(target, a0, a1, fixedCoord, y0, y1, side);
            }

            chunk.hasWater = any;
            if (!any) return;
            var lf = Lighting3D.Enabled ? light : null;
            foreach (var kv in surfBuilders)
            {
                var m = kv.Value.ToMesh(lf);
                if (m == null) continue;
                chunk.surf.Add(m);
                chunk.surfMat.Add(SurfMaterial(kv.Key, (kv.Key & 1) == 1));
            }
            foreach (var bkv in bedBuilders)
            {
                var bm = bkv.Value.ToMesh(lf);
                if (bm == null) continue;
                chunk.bed.Add(bm);
                chunk.bedMat.Add(BedMaterial(bkv.Key));
            }
            foreach (var skv in shoreBuilders)
            {
                var sm = skv.Value.ToMesh(lf, whiteColors: true);
                if (sm == null) continue;
                chunk.shore.Add(sm);
                chunk.shoreMat.Add(skv.Key);
            }
        }

        /// <summary>
        /// 岸壁。side：0 = 墙在 z=Z 面向 +Z，2 = 墙在 z=Z 面向 -Z，
        /// 1 = 墙在 x=X 面向 +X，3 = 墙在 x=X 面向 -X。绕序沿用项目盒体各面约定。
        /// u 是沿边的世界米（x 或 z），v 是 y0→y1 的实际米数。
        /// </summary>
        private static void Bank(QuadBuilder b, float a0, float a1, float fixedCoord, float y0, float y1, int side)
        {
            float h = y1 - y0;
            switch (side)
            {
                case 0: // z = Z，面向 +Z（南面写法）
                    b.Quad(new Vector3(a0, y0, fixedCoord), new Vector3(a1, y0, fixedCoord),
                           new Vector3(a1, y1, fixedCoord), new Vector3(a0, y1, fixedCoord),
                           new Vector2(a0, 0f), new Vector2(a1, 0f), new Vector2(a1, h), new Vector2(a0, h));
                    break;
                case 2: // z = Z，面向 -Z（北面写法）
                    b.Quad(new Vector3(a1, y0, fixedCoord), new Vector3(a0, y0, fixedCoord),
                           new Vector3(a0, y1, fixedCoord), new Vector3(a1, y1, fixedCoord),
                           new Vector2(a1, 0f), new Vector2(a0, 0f), new Vector2(a0, h), new Vector2(a1, h));
                    break;
                case 1: // x = X，面向 +X（东面写法）
                    b.Quad(new Vector3(fixedCoord, y0, a1), new Vector3(fixedCoord, y0, a0),
                           new Vector3(fixedCoord, y1, a0), new Vector3(fixedCoord, y1, a1),
                           new Vector2(a1, 0f), new Vector2(a0, 0f), new Vector2(a0, h), new Vector2(a1, h));
                    break;
                default: // x = X，面向 -X（西面写法）
                    b.Quad(new Vector3(fixedCoord, y0, a0), new Vector3(fixedCoord, y0, a1),
                           new Vector3(fixedCoord, y1, a1), new Vector3(fixedCoord, y1, a0),
                           new Vector2(a0, 0f), new Vector2(a1, 0f), new Vector2(a1, h), new Vector2(a0, h));
                    break;
            }
        }

        // ---- 材质 ----

        // key：沼泽 = 900 + moving；其余 = 深度档 * 2 + moving（见 RebuildChunk）。
        private static Material SurfMaterial(int key, bool moving)
        {
            if (surfMats.TryGetValue(key, out var m) && m != null) return m;
            if (rippleTex == null) rippleTex = ProcTex3D.Ripple(128);
            bool marsh = key >= 900;
            int bucket = marsh ? 0 : key / 2;
            float t = DepthBuckets > 1 ? bucket / (float)(DepthBuckets - 1) : 0f;
            m = new Material(ShaderDatabase.Transparent)
            { name = "Fps3D_Water_" + key + (moving ? "_mv" : ""), mainTexture = rippleTex };
            m.color = marsh ? MarshTint() : SurfaceTint(t);
            surfMats[key] = m;
            return m;
        }

        private static int BucketOf(float depth)
        {
            if (depth <= 0f) return 0;
            float t = depth / Metrics3D.WaterDepthDeep;
            int b = Mathf.RoundToInt(t * (DepthBuckets - 1));
            return Mathf.Clamp(b, 0, DepthBuckets - 1);
        }

        // t∈[0,1]：0 最浅、1 最深。水面越浅越清亮透，越深越暗蓝、越不透。
        private static Color SurfaceTint(float t)
        {
            var shallow = new Color(0.46f, 0.74f, 0.72f, 0.34f);
            var deep = new Color(0.05f, 0.21f, 0.42f, 0.62f);
            return Color.Lerp(shallow, deep, t);
        }

        // 河床随深度变暗：浅处湿沙、深处近黑蓝，让深浅在俯视时直接可读。
        private static Color BedTint(float t)
        {
            var shallow = new Color(0.62f, 0.58f, 0.46f, 1f);
            var deep = new Color(0.09f, 0.13f, 0.19f, 1f);
            return Color.Lerp(shallow, deep, t);
        }

        private static Color MarshTint() => new Color(0.26f, 0.34f, 0.20f, 0.50f);

        private static Material BedMaterial(int bucket)
        {
            if (bedMats.TryGetValue(bucket, out var m) && m != null) return m;
            Texture tex = null;
            try { tex = TerrainDefOf.Sand.DrawMatSingle?.mainTexture; } catch { }
            if (tex == null) { try { tex = TerrainDefOf.Gravel.DrawMatSingle?.mainTexture; } catch { } }
            if (tex is Texture2D t2 && t2.wrapMode != TextureWrapMode.Repeat) t2.wrapMode = TextureWrapMode.Repeat;
            m = new Material(ShaderDatabase.Cutout) { name = "Fps3D_WaterBed_" + bucket };
            m.mainTexture = tex != null ? tex : Util3D.WhiteTex;
            float t = DepthBuckets > 1 ? bucket / (float)(DepthBuckets - 1) : 0f;
            m.color = BedTint(t);
            bedMats[bucket] = m;
            return m;
        }

        // 邻格陆地材质：DrawMatSingle 主贴图、Repeat、td.color。与 TerrainLayer3D 各做一份缓存。
        private static Material LandMaterial(TerrainDef td)
        {
            if (td == null) return null;
            if (landMats.TryGetValue(td, out var mat)) return mat;
            mat = BuildLandMaterial(td);
            landMats[td] = mat;
            return mat;
        }

        private static Material BuildLandMaterial(TerrainDef td)
        {
            Material src = null;
            try { src = td.DrawMatSingle; } catch { }
            Texture tex = null;
            if (src != null) tex = src.mainTexture;
            if (tex == null)
            {
                var t2 = ContentFinder<Texture2D>.Get(td.texturePath, reportFailure: false);
                tex = t2;
            }
            if (tex == null) return null;
            if (tex is Texture2D tw && tw.wrapMode != TextureWrapMode.Repeat)
                tw.wrapMode = TextureWrapMode.Repeat;
            // FadeRough 按顶点 xz 取样，竖面会被拉成一条。Cutout 读网格 UV，沿边 u / 高度 v 才生效。
            var m = new Material(ShaderDatabase.Cutout) { name = "Fps3D_Shore_" + td.defName };
            m.mainTexture = tex;
            // 岸壁是水的侧剖面：压暗并偏冷，形成清晰的"水线"，让人一眼看出水是凹进去的。
            var c = td.color;
            m.color = new Color(c.r * 0.55f, c.g * 0.58f, c.b * 0.68f, 1f);
            return m;
        }

        // ---- 绘制 ----

        public void Draw()
        {
            var m = Matrix4x4.Translate(worldOffset);
            float t = Time.realtimeSinceStartup;
            if (Mathf.Abs(t - scrollT) > 0.0001f)
            {
                scrollT = t;
                foreach (var kv in surfMats)
                {
                    if (kv.Value == null) continue;
                    bool moving = (kv.Key & 1) == 1;
                    kv.Value.mainTextureOffset = moving
                        ? new Vector2(t * 0.045f, t * 0.012f)
                        : new Vector2(Mathf.Sin(t * 0.25f) * 0.03f, t * 0.006f);
                }
            }
            foreach (var kv in chunks)
            {
                var c = kv.Value;
                if (!c.hasWater) continue;
                for (int i = 0; i < c.bed.Count; i++)
                {
                    if (c.bed[i] == null || c.bedMat[i] == null) continue;
                    Graphics.DrawMesh(c.bed[i], m, c.bedMat[i], 0);
                    PerfCounters.WaterQuads += c.bed[i].vertexCount / 4;
                }
                for (int s = 0; s < c.shore.Count; s++)
                {
                    if (c.shore[s] == null || c.shoreMat[s] == null) continue;
                    Graphics.DrawMesh(c.shore[s], m, c.shoreMat[s], 0);
                    PerfCounters.WaterQuads += c.shore[s].vertexCount / 4;
                }
                for (int i = 0; i < c.surf.Count; i++)
                {
                    if (c.surf[i] == null) continue;
                    Graphics.DrawMesh(c.surf[i], m, c.surfMat[i], 0);
                    PerfCounters.WaterQuads += c.surf[i].vertexCount / 4;
                }
            }
        }

        private static void DisposeChunk(Chunk c)
        {
            for (int i = 0; i < c.surf.Count; i++) Util3D.SafeDestroy(c.surf[i]);
            c.surf.Clear();
            c.surfMat.Clear();
            for (int i = 0; i < c.bed.Count; i++) Util3D.SafeDestroy(c.bed[i]);
            c.bed.Clear();
            c.bedMat.Clear();
            for (int i = 0; i < c.shore.Count; i++) Util3D.SafeDestroy(c.shore[i]);
            c.shore.Clear();
            c.shoreMat.Clear();
            c.hasWater = false;
        }

        public void Dispose()
        {
            foreach (var kv in chunks) DisposeChunk(kv.Value);
            chunks.Clear();
        }

        // ---- 四边形直写 ----
        private class QuadBuilder
        {
            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<int> tris = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
                var n = Vector3.Cross(b - a, c - a);
                n = n.sqrMagnitude < 1e-8f ? Vector3.up : n.normalized;
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            /// <summary>
            /// field 非空时逐顶点烘光照进顶点色 RGB（F013）。
            /// Map/Transparent 是否吃顶点色尚未证实：吃了算赚到，不吃也只是水面维持原样，
            /// 不会像深度分档那样依赖它。whiteColors 是光照关闭时岸壁的原有中性填充。
            /// </summary>
            public Mesh ToMesh(LightField3D field = null, bool whiteColors = false)
            {
                if (verts.Count == 0) return null;
                var mesh = new Mesh { name = "Fps3D_Water" };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                if (field != null)
                {
                    var cols = new Color32[verts.Count];
                    for (int i = 0; i < verts.Count; i++)
                        cols[i] = Lighting3D.To32(field.Shade(verts[i], normals[i], true));
                    mesh.SetColors(cols);
                }
                else if (whiteColors)
                {
                    var cols = new Color32[verts.Count];
                    var w = new Color32(255, 255, 255, 255);
                    for (int i = 0; i < cols.Length; i++) cols[i] = w;
                    mesh.SetColors(cols);
                }
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
