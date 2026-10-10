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
    //   水面   —— 半透明、按深度着色、平铺水波纹并缓慢流动（河流沿定向滚动）
    //   浅岸   —— 与陆地相邻的一圈浅水更亮更透，形成自然的岸线过渡
    //
    // 深度常量见 Metrics3D（现实水深：齐膝/齐胸/深水）。
    // 地形层会跳过 IsWater 格（见 TerrainLayer3D），沼泽不算水体，只做半透明覆盖。
    public class WaterLayer3D
    {
        public const int ChunkSize = 16;

        private enum Cls { None = 0, Shore, Shallow, Chest, Deep, Marsh }

        private class Chunk
        {
            public readonly List<Mesh> surf = new List<Mesh>();
            public readonly List<Material> surfMat = new List<Material>();
            public Mesh bed;
            public bool dirty = true;
            public bool hasWater;
        }

        private readonly Map map;
        private readonly float baseY;
        private readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        private bool allDirty = true;

        public Vector3 worldOffset = Vector3.zero;

        private static Texture2D rippleTex;
        private static Material bedMat;
        private static readonly Dictionary<int, Material> surfMats = new Dictionary<int, Material>();
        private static float scrollT;

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
                foreach (var kv in chunks) DisposeChunk(kv.Value);
                chunks.Clear();
                int cxMax = (map.Size.x + ChunkSize - 1) / ChunkSize;
                int czMax = (map.Size.z + ChunkSize - 1) / ChunkSize;
                for (int cx = 0; cx < cxMax; cx++)
                    for (int cz = 0; cz < czMax; cz++)
                        chunks[cx * 10000 + cz] = new Chunk();
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

        private Cls ClassAt(IntVec3 c)
        {
            if (!c.InBounds(map)) return Cls.None;
            var td = map.terrainGrid.TerrainAt(c);
            var cls = Classify(td);
            if (cls == Cls.Shallow)
            {
                // 与陆地四邻接触的浅水 = 浅岸
                if (Classify(TerrainOrNull(c + IntVec3.North)) == Cls.None
                    || Classify(TerrainOrNull(c + IntVec3.South)) == Cls.None
                    || Classify(TerrainOrNull(c + IntVec3.East)) == Cls.None
                    || Classify(TerrainOrNull(c + IntVec3.West)) == Cls.None)
                    cls = Cls.Shore;
            }
            return cls;
        }

        private TerrainDef TerrainOrNull(IntVec3 c) => c.InBounds(map) ? map.terrainGrid.TerrainAt(c) : null;

        // ---- 重建 ----

        private void RebuildChunk(int cx, int cz, Chunk chunk)
        {
            DisposeChunk(chunk);
            chunk.dirty = false;
            var rect = new CellRect(cx * ChunkSize, cz * ChunkSize, ChunkSize, ChunkSize);
            rect.ClipInsideMap(map);
            if (rect.IsEmpty) return;

            var surfBuilders = new Dictionary<int, QuadBuilder>();
            var bed = new QuadBuilder();
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

                // 水面（含沼泽覆盖）
                int key = ((int)cls) * 2 + (moving ? 1 : 0);
                if (!surfBuilders.TryGetValue(key, out var sb)) { sb = new QuadBuilder(); surfBuilders[key] = sb; }
                float ys = baseY + Metrics3D.WaterSurfaceLift;
                sb.Quad(new Vector3(X, ys, Z + 1), new Vector3(X + 1, ys, Z + 1),
                        new Vector3(X + 1, ys, Z), new Vector3(X, ys, Z),
                        new Vector2(X * 0.5f, (Z + 1) * 0.5f), new Vector2((X + 1) * 0.5f, (Z + 1) * 0.5f),
                        new Vector2((X + 1) * 0.5f, Z * 0.5f), new Vector2(X * 0.5f, Z * 0.5f));

                if (depth <= 0f) continue;
                // 水底
                float yb = baseY - depth;
                bed.Quad(new Vector3(X, yb, Z + 1), new Vector3(X + 1, yb, Z + 1),
                         new Vector3(X + 1, yb, Z), new Vector3(X, yb, Z),
                         new Vector2(X, Z + 1), new Vector2(X + 1, Z + 1), new Vector2(X + 1, Z), new Vector2(X, Z));
                // 岸壁：朝向更浅（含陆地 depth=0）的邻格发一面竖墙
                float dN = DepthOf(ClassAt(c + IntVec3.North));
                float dS = DepthOf(ClassAt(c + IntVec3.South));
                float dE = DepthOf(ClassAt(c + IntVec3.East));
                float dW = DepthOf(ClassAt(c + IntVec3.West));
                if (dN < depth) Bank(bed, X, X + 1, Z + 1, baseY - depth, baseY - dN, 2);
                if (dS < depth) Bank(bed, X, X + 1, Z, baseY - depth, baseY - dS, 0);
                if (dE < depth) Bank(bed, Z, Z + 1, X + 1, baseY - depth, baseY - dE, 3);
                if (dW < depth) Bank(bed, Z, Z + 1, X, baseY - depth, baseY - dW, 1);
            }

            chunk.hasWater = any;
            if (!any) return;
            foreach (var kv in surfBuilders)
            {
                var m = kv.Value.ToMesh();
                if (m == null) continue;
                chunk.surf.Add(m);
                chunk.surfMat.Add(SurfMaterial((Cls)(kv.Key / 2), (kv.Key & 1) == 1));
            }
            chunk.bed = bed.ToMesh();
        }

        /// <summary>
        /// 岸壁。side：0 = 墙在 z=Z 面向 +Z，2 = 墙在 z=Z 面向 -Z，
        /// 1 = 墙在 x=X 面向 +X，3 = 墙在 x=X 面向 -X。绕序沿用项目盒体各面约定。
        /// </summary>
        private static void Bank(QuadBuilder b, float a0, float a1, float fixedCoord, float y0, float y1, int side)
        {
            float h = y1 - y0;
            var uv0 = new Vector2(0, 0); var uv1 = new Vector2(1, 0);
            var uv2 = new Vector2(1, h); var uv3 = new Vector2(0, h);
            switch (side)
            {
                case 0: // z = Z，面向 +Z（南面写法）
                    b.Quad(new Vector3(a0, y0, fixedCoord), new Vector3(a1, y0, fixedCoord),
                           new Vector3(a1, y1, fixedCoord), new Vector3(a0, y1, fixedCoord), uv0, uv1, uv2, uv3);
                    break;
                case 2: // z = Z，面向 -Z（北面写法）
                    b.Quad(new Vector3(a1, y0, fixedCoord), new Vector3(a0, y0, fixedCoord),
                           new Vector3(a0, y1, fixedCoord), new Vector3(a1, y1, fixedCoord), uv0, uv1, uv2, uv3);
                    break;
                case 1: // x = X，面向 +X（东面写法）
                    b.Quad(new Vector3(fixedCoord, y0, a1), new Vector3(fixedCoord, y0, a0),
                           new Vector3(fixedCoord, y1, a0), new Vector3(fixedCoord, y1, a1), uv0, uv1, uv2, uv3);
                    break;
                default: // x = X，面向 -X（西面写法）
                    b.Quad(new Vector3(fixedCoord, y0, a0), new Vector3(fixedCoord, y0, a1),
                           new Vector3(fixedCoord, y1, a1), new Vector3(fixedCoord, y1, a0), uv0, uv1, uv2, uv3);
                    break;
            }
        }

        // ---- 材质 ----

        private static Material SurfMaterial(Cls cls, bool moving)
        {
            int key = ((int)cls) * 2 + (moving ? 1 : 0);
            if (surfMats.TryGetValue(key, out var m) && m != null) return m;
            if (rippleTex == null) rippleTex = ProcTex3D.Ripple(128);
            var shader = ShaderDatabase.Transparent;
            m = new Material(shader) { name = "Fps3D_Water_" + cls + (moving ? "_mv" : ""), mainTexture = rippleTex };
            m.color = TintOf(cls);
            surfMats[key] = m;
            return m;
        }

        private static Color TintOf(Cls c)
        {
            switch (c)
            {
                case Cls.Deep: return new Color(0.05f, 0.20f, 0.36f, 0.90f);
                case Cls.Chest: return new Color(0.10f, 0.34f, 0.50f, 0.80f);
                case Cls.Shallow: return new Color(0.20f, 0.50f, 0.62f, 0.66f);
                case Cls.Shore: return new Color(0.42f, 0.68f, 0.74f, 0.48f);
                case Cls.Marsh: return new Color(0.26f, 0.34f, 0.20f, 0.50f);
                default: return new Color(0.2f, 0.5f, 0.6f, 0.6f);
            }
        }

        private static Material BedMaterial()
        {
            if (bedMat != null) return bedMat;
            Texture tex = null;
            try { tex = TerrainDefOf.Sand.DrawMatSingle?.mainTexture; } catch { }
            if (tex == null) { try { tex = TerrainDefOf.Gravel.DrawMatSingle?.mainTexture; } catch { } }
            if (tex is Texture2D t2 && t2.wrapMode != TextureWrapMode.Repeat) t2.wrapMode = TextureWrapMode.Repeat;
            bedMat = new Material(ShaderDatabase.Cutout) { name = "Fps3D_WaterBed" };
            bedMat.mainTexture = tex != null ? tex : Util3D.WhiteTex;
            // 水下河床偏暗偏蓝，透过水面看才像有深度
            bedMat.color = new Color(0.42f, 0.46f, 0.40f, 1f);
            return bedMat;
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
            var bm = BedMaterial();
            foreach (var kv in chunks)
            {
                var c = kv.Value;
                if (!c.hasWater) continue;
                if (c.bed != null)
                {
                    Graphics.DrawMesh(c.bed, m, bm, 0);
                    PerfCounters.WaterQuads += c.bed.vertexCount / 4;
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
            Util3D.SafeDestroy(c.bed);
            c.bed = null;
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
            private readonly List<int> tris = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            public Mesh ToMesh()
            {
                if (verts.Count == 0) return null;
                var mesh = new Mesh { name = "Fps3D_Water" };
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
