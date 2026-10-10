// AI生成
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 地形层：16x16 格 chunk 合并网格，按材质分网格绘制。
    public class TerrainLayer3D
    {
        public const int ChunkSize = 16;

        private readonly Map map;
        private readonly float baseY;
        private readonly int chunksX;
        private readonly int chunksZ;

        private class Chunk
        {
            public Dictionary<Material, Mesh> meshes = new Dictionary<Material, Mesh>();
            public bool dirty = true;
            // 图内局部包围盒（不含 worldOffset），供视锥剔除用；无网格时 valid=false
            public Bounds bounds;
            public bool boundsValid;
        }

        private readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        private bool allDirty = true;

        public TerrainLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
            chunksX = (map.Size.x + ChunkSize - 1) / ChunkSize;
            chunksZ = (map.Size.z + ChunkSize - 1) / ChunkSize;
        }

        public void MarkAllDirty()
        {
            allDirty = true;
        }

        public void MarkCellDirty(IntVec3 c)
        {
            int cx = c.x / ChunkSize, cz = c.z / ChunkSize;
            int key = cx * 10000 + cz;
            if (!chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk();
                chunks[key] = chunk;
            }
            chunk.dirty = true;
        }

        // 每帧调用，按预算重建脏 chunk。返回剩余脏数。
        public int Update()
        {
            if (allDirty)
            {
                DisposeMeshes();
                EnsureChunkRecords();
                foreach (var kv in chunks)
                {
                    kv.Value.meshes.Clear();
                    kv.Value.dirty = true;
                }
                allDirty = false;
            }
            int budget = Fps3DState.ChunkRebuildBudget;
            int dirtyRemaining = 0;
            foreach (var kv in chunks)
            {
                if (!kv.Value.dirty) continue;
                if (budget > 0)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    RebuildChunk(kv.Key, kv.Value);
                    PerfCounters.LastTerrainBuildMs = (float)sw.Elapsed.TotalMilliseconds;
                    budget--;
                }
                else dirtyRemaining++;
            }
            return dirtyRemaining;
        }

        private void EnsureChunkRecords()
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                for (int cz = 0; cz < chunksZ; cz++)
                {
                    int key = cx * 10000 + cz;
                    if (!chunks.ContainsKey(key)) chunks[key] = new Chunk();
                }
            }
        }

        private void RebuildChunk(int key, Chunk chunk)
        {
            DisposeChunkMeshes(chunk);
            int cx = key / 10000, cz = key % 10000;

            var rect = new CellRect(cx * ChunkSize, cz * ChunkSize, ChunkSize, ChunkSize);
            rect.ClipInsideMap(map);
            int w = rect.Width, h = rect.Height;
            if (w <= 0 || h <= 0) { chunk.dirty = false; return; }

            // 体素式合批：同材质相邻格贪心合并成大矩形面（贴图 Repeat 平铺，
            // 每格仍是一个 tile），被满格建筑盖死的地表格直接剔除（永远不可见）。
            // 面片数从 O(格数) 降到 O(同材连通块数)。
            var mats = new List<Material>();
            var masks = new List<bool[]>();
            for (int iz = 0; iz < h; iz++)
            {
                for (int ix = 0; ix < w; ix++)
                {
                    var cell = new IntVec3(rect.minX + ix, 0, rect.minZ + iz);
                    var td = map.terrainGrid.TerrainAt(cell);
                    if (td == null || td.dontRender) continue;
                    if (SolidEdificeAt(cell)) continue;
                    // 水体格交给 WaterLayer3D（水底 + 岸壁 + 半透明水面），这里不画平面水贴图
                    if (td.IsWater) continue;
                    Material mat = TerrainMaterialFor(td);
                    if (mat == null) continue;
                    int mi = mats.IndexOf(mat);
                    if (mi < 0)
                    {
                        mi = mats.Count;
                        mats.Add(mat);
                        masks.Add(new bool[w * h]);
                    }
                    masks[mi][iz * w + ix] = true;
                }
            }

            var builders = new Dictionary<Material, QuadBuilder>();
            for (int mi = 0; mi < mats.Count; mi++)
            {
                var mask = masks[mi];
                var b = new QuadBuilder();
                int i = 0;
                while (i < mask.Length)
                {
                    if (!mask[i]) { i++; continue; }
                    int x0 = i % w, z0 = i / w;
                    int rw = 1;
                    while (x0 + rw < w && mask[z0 * w + x0 + rw]) rw++;
                    int rh = 1;
                    while (z0 + rh < h)
                    {
                        bool grow = true;
                        for (int k = 0; k < rw; k++)
                            if (!mask[(z0 + rh) * w + x0 + k]) { grow = false; break; }
                        if (!grow) break;
                        rh++;
                    }
                    for (int zz = z0; zz < z0 + rh; zz++)
                        for (int xx = x0; xx < x0 + rw; xx++)
                            mask[zz * w + xx] = false;
                    // 顺时针（从上往下看）保证法线朝上；v 向北递增，跨格连续。
                    float X = rect.minX + x0, Z = rect.minZ + z0;
                    b.Add(new Vector3(X, baseY, Z + rh), new Vector2(0, 0));
                    b.Add(new Vector3(X + rw, baseY, Z + rh), new Vector2(rw, 0));
                    b.Add(new Vector3(X + rw, baseY, Z), new Vector2(rw, rh));
                    b.Add(new Vector3(X, baseY, Z), new Vector2(0, rh));
                }
                if (b.HasGeometry) builders[mats[mi]] = b;
            }

            bool hasBounds = false;
            var acc = new Bounds();
            foreach (var kv in builders)
            {
                var mesh = kv.Value.ToMesh();
                if (mesh != null)
                {
                    chunk.meshes[kv.Key] = mesh;
                    if (!hasBounds) { acc = mesh.bounds; hasBounds = true; }
                    else acc.Encapsulate(mesh.bounds);
                }
            }
            chunk.bounds = acc;
            chunk.boundsValid = hasBounds;
            chunk.dirty = false;
        }

        // 满格建筑（墙/天然岩）整格盖死地表：第一人称下地表格永远不可见，剔除。
        private bool SolidEdificeAt(IntVec3 c)
        {
            var ed = c.GetEdifice(map);
            return ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full;
        }

        private static readonly Dictionary<TerrainDef, Material> matCache = new Dictionary<TerrainDef, Material>();

        private Material TerrainMaterialFor(TerrainDef td)
        {
            if (matCache.TryGetValue(td, out var mat)) return mat;
            mat = BuildTerrainMaterial(td);
            matCache[td] = mat;
            return mat;
        }

        private Material BuildTerrainMaterial(TerrainDef td)
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
            // 合批大面靠 Repeat 平铺保持每格一 tile；原版只画 0..1 的 UV，
            // 对它无影响（共享纹理改 wrapMode 安全）。
            if (tex is Texture2D tw && tw.wrapMode != TextureWrapMode.Repeat)
                tw.wrapMode = TextureWrapMode.Repeat;
            var m = new Material(src != null ? src.shader : (td.Shader ?? ShaderDatabase.TerrainHard));
            m.mainTexture = tex;
            m.color = td.color;
            return m;
        }

        // 水平流式偏移（世界坐标 = 图内局部 + 此偏移）
        public Vector3 worldOffset = Vector3.zero;

        public void Draw()
        {
            var m = Matrix4x4.Translate(worldOffset);
            foreach (var kv in chunks)
            {
                var chunk = kv.Value;
                if (chunk.meshes.Count == 0) continue;
                // 屏幕外 / 超远 chunk 直接跳过提交（相机视锥缓存在 Frustum3D 每帧刷新）
                if (chunk.boundsValid)
                {
                    var wb = chunk.bounds;
                    wb.center += worldOffset;
                    if (!Frustum3D.Visible(wb)) continue;
                }
                foreach (var mkv in chunk.meshes)
                {
                    Graphics.DrawMesh(mkv.Value, m, mkv.Key, 0);
                    PerfCounters.TerrainMeshesDrawn++;
                    PerfCounters.TerrainQuads += mkv.Value.vertexCount / 4;
                }
            }
        }

        private void DisposeChunkMeshes(Chunk chunk)
        {
            foreach (var mkv in chunk.meshes) Util3D.SafeDestroy(mkv.Value);
            chunk.meshes.Clear();
        }

        private void DisposeMeshes()
        {
            foreach (var kv in chunks) DisposeChunkMeshes(kv.Value);
        }

        public void Dispose()
        {
            DisposeMeshes();
            chunks.Clear();
        }

        private class QuadBuilder
        {
            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> tris = new List<int>();

            public bool HasGeometry => verts.Count > 0;

            public void Add(Vector3 v, Vector2 uv)
            {
                verts.Add(v);
                uvs.Add(uv);
            }

            public Mesh ToMesh()
            {
                int quads = verts.Count / 4;
                if (quads == 0) return null;
                for (int i = 0; i < verts.Count; i += 4)
                {
                    tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                    tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
                }
                var mesh = new Mesh();
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
