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

        private class Surface
        {
            public Material mat;
            public Mesh mesh;
            public Vector3[] verts;
            public Vector3[] normals;
            public byte[] alphas;
            public Color32[] colors;
            public bool isEdgeFan;
        }

        private class Chunk
        {
            public readonly Dictionary<Material, Mesh> meshes = new Dictionary<Material, Mesh>();
            public readonly List<Surface> surfaces = new List<Surface>();
            public bool dirty = true;
            public bool colorDirty;
            // 图内局部包围盒（不含 worldOffset），供视锥剔除用；无网格时 valid=false
            public Bounds bounds;
            public bool boundsValid;
        }

        private readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        private bool allDirty = true;

        // 光照场（F013），由 Scene3D 注入；为空或光照关闭时顶点色 RGB 保持全白。
        public LightField3D light;

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

        public void MarkAllColorDirty()
        {
            foreach (var kv in chunks)
                if (!kv.Value.dirty && kv.Value.surfaces.Count > 0)
                    kv.Value.colorDirty = true;
        }

        public void MarkCellDirty(IntVec3 c)
        {
            // 软边扇读 8 邻格，改一格要连带邻块重建。
            TouchChunk(c);
            for (int i = 0; i < 8; i++)
                TouchChunk(c + GenAdj.AdjacentCellsAroundBottom[i]);
        }

        public void MarkCellColorDirty(IntVec3 c)
        {
            TouchChunkColor(c);
            for (int i = 0; i < 8; i++)
                TouchChunkColor(c + GenAdj.AdjacentCellsAroundBottom[i]);
        }

        private void TouchChunk(IntVec3 c)
        {
            if (!c.InBounds(map)) return;
            int cx = c.x / ChunkSize, cz = c.z / ChunkSize;
            int key = cx * 10000 + cz;
            if (!chunks.TryGetValue(key, out var chunk))
            {
                chunk = new Chunk();
                chunks[key] = chunk;
            }
            chunk.dirty = true;
        }

        private void TouchChunkColor(IntVec3 c)
        {
            if (!c.InBounds(map)) return;
            int cx = c.x / ChunkSize, cz = c.z / ChunkSize;
            int key = cx * 10000 + cz;
            if (chunks.TryGetValue(key, out var chunk) && !chunk.dirty && chunk.surfaces.Count > 0)
                chunk.colorDirty = true;
        }

        // 每帧调用，按预算重建脏 chunk。返回剩余脏数。
        public int Update()
        {
            if (allDirty)
            {
                EnsureChunkRecords();
                foreach (var kv in chunks)
                {
                    kv.Value.dirty = true;
                }
                allDirty = false;
            }
            int budget = Fps3DState.ChunkRebuildBudget;
            int colorBudget = budget * 4 + 4;
            int dirtyRemaining = 0;
            foreach (var kv in chunks)
            {
                var ch = kv.Value;
                if (ch.dirty)
                {
                    if (budget > 0)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        RebuildChunk(kv.Key, ch);
                        PerfCounters.LastTerrainBuildMs = (float)sw.Elapsed.TotalMilliseconds;
                        budget--;
                    }
                    else dirtyRemaining++;
                }
                else if (ch.colorDirty)
                {
                    if (colorBudget > 0)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        RecolorChunk(ch);
                        PerfCounters.LastTerrainBuildMs = (float)sw.Elapsed.TotalMilliseconds;
                        colorBudget--;
                    }
                }
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
                    // 水体格交给 WaterLayer3D（水底 + 岸壁 + 半透明水面），这里不画平面水贴图
                    if (!DrawsGround(cell, out var td)) continue;
                    Material mat = TerrainMaterialFor(td);
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
                    // 绕序不动（法线朝上）。UV 用世界格，v 跟 +Z。
                    //
                    // 光照开启时把大矩形切成不超过 LightQuadStep 米的小块：顶点色是逐顶点的，
                    // 一整块 16x16 只有 4 个角能受光，中央的灯会被糊成四角渐变。
                    // UV 走世界坐标，所以细分不改变取样位置，贴图与切之前逐像素一致。
                    int step = Lighting3D.Enabled
                        ? Mathf.Clamp(Mathf.RoundToInt(Lighting3D.QuadStep), 1, 16) : 16;
                    for (int sz2 = 0; sz2 < rh; sz2 += step)
                    {
                        int dh = Mathf.Min(step, rh - sz2);
                        for (int sx2 = 0; sx2 < rw; sx2 += step)
                        {
                            int dw = Mathf.Min(step, rw - sx2);
                            float X = rect.minX + x0 + sx2, Z = rect.minZ + z0 + sz2;
                            b.AddQuad(
                                new Vector3(X, baseY, Z + dh), new Vector2(X, Z + dh),
                                new Vector3(X + dw, baseY, Z + dh), new Vector2(X + dw, Z + dh),
                                new Vector3(X + dw, baseY, Z), new Vector2(X + dw, Z),
                                new Vector3(X, baseY, Z), new Vector2(X, Z));
                        }
                    }
                }
                if (b.HasGeometry) builders[mats[mi]] = b;
            }

            // 只给已经画了底面的格铺软边扇，写进软边材质的 mesh。
            var edgeBuilders = new Dictionary<Material, QuadBuilder>();
            for (int iz = 0; iz < h; iz++)
            {
                for (int ix = 0; ix < w; ix++)
                {
                    var cell = new IntVec3(rect.minX + ix, 0, rect.minZ + iz);
                    if (!DrawsGround(cell, out var self)) continue;
                    AddSoftEdges(cell, self, edgeBuilders);
                }
            }

            bool hasBounds = false;
            var acc = new Bounds();
            var lf = Lighting3D.Enabled ? light : null;
            foreach (var kv in builders)
            {
                var surf = kv.Value.ToSurface(kv.Key, lf, isEdgeFan: false);
                if (surf != null)
                {
                    chunk.meshes[kv.Key] = surf.mesh;
                    chunk.surfaces.Add(surf);
                    if (!hasBounds) { acc = surf.mesh.bounds; hasBounds = true; }
                    else acc.Encapsulate(surf.mesh.bounds);
                }
            }
            foreach (var kv in edgeBuilders)
            {
                var surf = kv.Value.ToSurface(kv.Key, lf, isEdgeFan: true);
                if (surf != null)
                {
                    chunk.meshes[kv.Key] = surf.mesh;
                    chunk.surfaces.Add(surf);
                    if (!hasBounds) { acc = surf.mesh.bounds; hasBounds = true; }
                    else acc.Encapsulate(surf.mesh.bounds);
                }
            }
            chunk.bounds = acc;
            chunk.boundsValid = hasBounds;
            chunk.dirty = false;
            chunk.colorDirty = false;
        }

        private void RecolorChunk(Chunk chunk)
        {
            chunk.colorDirty = false;
            var lf = Lighting3D.Enabled ? light : null;
            for (int s = 0; s < chunk.surfaces.Count; s++)
            {
                var surf = chunk.surfaces[s];
                var v = surf.verts;
                var n = surf.normals;
                var a = surf.alphas;
                var c = surf.colors;
                if (lf != null)
                {
                    for (int i = 0; i < v.Length; i++)
                    {
                        var lit = Lighting3D.To32(lf.Shade(v[i], n[i], true));
                        c[i] = new Color32(lit.r, lit.g, lit.b, a[i]);
                    }
                }
                else
                {
                    for (int i = 0; i < v.Length; i++)
                        c[i] = new Color32(255, 255, 255, a[i]);
                }
                surf.mesh.colors32 = c;
            }
        }

        // 满格建筑（墙/天然岩）整格盖死地表：第一人称下地表格永远不可见，剔除。
        // 门（Building_Door）虽然 Fillage == Full，但门扇薄且开门后贯通，必须保留地面。
        private bool SolidEdificeAt(IntVec3 c)
        {
            var ed = c.GetEdifice(map);
            if (ed == null || ed.def == null || ed.def.Fillage != FillCategory.Full) return false;
            if (ed is RimWorld.Building_Door || ed.def.IsDoor) return false;
            return true;
        }

        // 会画底面的格才允许铺扇：dontRender、水、满格建筑盖住的格都跳过。
        private bool DrawsGround(IntVec3 cell, out TerrainDef td)
        {
            td = map.terrainGrid.TerrainAt(cell);
            if (td == null || td.dontRender) return false;
            if (SolidEdificeAt(cell)) return false;
            if (td.IsWater) return false;
            return TerrainMaterialFor(td) != null;
        }

        // 邻居盖住地面时不当过渡来源。不把该格换成 Underwall。
        private bool BlocksBlend(IntVec3 c)
        {
            var ed = c.GetEdifice(map);
            if (ed == null || ed.def == null) return false;
            if (ed is RimWorld.Building_Door || ed.def.IsDoor) return false;
            return ed.def.Fillage == FillCategory.Full || ed.def.coversFloor;
        }

        // 在本格上为更高（或同）优先级的邻居地形各铺一次 8 三角扇。
        private void AddSoftEdges(IntVec3 cell, TerrainDef self, Dictionary<Material, QuadBuilder> builders)
        {
            var grid = map.terrainGrid;
            if (grid.FoundationAt(cell) != null) return;

            edgeOverlay.Clear();
            for (int i = 0; i < 8; i++)
            {
                IntVec3 n = cell + GenAdj.AdjacentCellsAroundBottom[i];
                if (!n.InBounds(map))
                {
                    edgeAround[i] = self;
                    continue;
                }
                if (BlocksBlend(n))
                {
                    edgeAround[i] = null;
                    continue;
                }
                TerrainDef nd = grid.TerrainAt(n);
                edgeAround[i] = nd;
                if (nd == null || nd == self) continue;
                if (grid.FoundationAt(n) != null) continue;
                if (nd.edgeType == TerrainDef.TerrainEdgeType.Hard) continue;
                if (nd.IsWater) continue;
                if (nd.renderPrecedence < self.renderPrecedence) continue;
                if (!edgeOverlay.Contains(nd)) edgeOverlay.Add(nd);
            }

            if (edgeOverlay.Count == 0) return;
            float y = baseY + 0.01f;
            for (int oi = 0; oi < edgeOverlay.Count; oi++)
            {
                TerrainDef od = edgeOverlay[oi];
                Material mat = EdgeMaterialFor(od);
                if (mat == null) continue;
                for (int i = 0; i < 8; i++) edgeFlags[i] = false;
                for (int i = 0; i < 8; i++)
                {
                    if (edgeAround[i] != od) continue;
                    if (i % 2 == 0)
                    {
                        edgeFlags[(i - 1 + 8) % 8] = true;
                        edgeFlags[i] = true;
                        edgeFlags[(i + 1) % 8] = true;
                    }
                    else edgeFlags[i] = true;
                }
                if (!builders.TryGetValue(mat, out var b))
                {
                    b = new QuadBuilder();
                    builders[mat] = b;
                }
                b.AddFan(cell.x, cell.z, y, edgeFlags);
            }
        }

        private static readonly Dictionary<TerrainDef, Material> matCache = new Dictionary<TerrainDef, Material>();
        private static readonly Dictionary<TerrainDef, Material> edgeMatCache = new Dictionary<TerrainDef, Material>();
        // 扇的邻格 / 顶点色 flag，按 chunk 复用，避免每格分配。
        private readonly TerrainDef[] edgeAround = new TerrainDef[8];
        private readonly bool[] edgeFlags = new bool[8];
        private readonly List<TerrainDef> edgeOverlay = new List<TerrainDef>(4);

        private Material TerrainMaterialFor(TerrainDef td)
        {
            if (matCache.TryGetValue(td, out var mat)) return mat;
            mat = BuildBaseTerrainMaterial(td);
            matCache[td] = mat;
            return mat;
        }

        private Material EdgeMaterialFor(TerrainDef td)
        {
            if (edgeMatCache.TryGetValue(td, out var mat)) return mat;
            mat = BuildEdgeTerrainMaterial(td);
            edgeMatCache[td] = mat;
            return mat;
        }

        // 地表主面材质：原版 TerrainHard / TerrainFadeRough 不乘顶点色 RGB，导致地面阴影与灯具光照完全失效。
        // 主面 alpha 恒为 255、UV 为世界坐标 Repeat 平铺，改用 ShaderDatabase.Cutout 后逐顶点阴影/AO/灯具/昼夜全部生效。
        private Material BuildBaseTerrainMaterial(TerrainDef td)
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
            var m = new Material(ShaderDatabase.Cutout);
            m.mainTexture = tex;
            m.color = td.color;
            if (src != null) m.renderQueue = src.renderQueue;
            else m.renderQueue = 2000 + td.renderPrecedence;
            return m;
        }

        // 软边扇材质：保留 TerrainFadeRough 的噪声 alpha 过渡，绘制时按 chunk 中心亮度乘材质色。
        private Material BuildEdgeTerrainMaterial(TerrainDef td)
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
            var m = new Material(src != null ? src.shader : (td.Shader ?? ShaderDatabase.TerrainHard));
            m.mainTexture = tex;
            m.color = td.color;
            // 原版 FadeRough 用这张噪声切软边；不挂上的话顶点色 alpha 切不出过渡。
            if (m.HasProperty(ShaderPropertyIDs.AlphaAddTex))
            {
                Texture add = null;
                if (src != null)
                {
                    try { add = src.GetTexture(ShaderPropertyIDs.AlphaAddTex); } catch { }
                }
                if (add == null)
                {
                    try { add = TexGame.AlphaAddTex; } catch { }
                }
                if (add is Texture2D aw && aw.wrapMode != TextureWrapMode.Repeat)
                    aw.wrapMode = TextureWrapMode.Repeat;
                if (add != null) m.SetTexture(ShaderPropertyIDs.AlphaAddTex, add);
            }
            if (src != null) m.renderQueue = src.renderQueue;
            else m.renderQueue = 2000 + td.renderPrecedence;
            return m;
        }

        // 水平流式偏移（世界坐标 = 图内局部 + 此偏移）
        public Vector3 worldOffset = Vector3.zero;

        /// <summary>把本层网格的颜色统计并进探针（只在按需导出时走）。</summary>
        public void Summarize(Lighting3D.Probe p)
        {
            foreach (var kv in chunks)
                foreach (var mkv in kv.Value.meshes)
                    p.Add(mkv.Value);
        }

        public void Draw()
        {
            var m = Matrix4x4.Translate(worldOffset);
            var lf = Lighting3D.Enabled ? light : null;
            foreach (var kv in chunks)
            {
                var chunk = kv.Value;
                if (chunk.surfaces.Count == 0) continue;
                // 屏幕外 / 超远 chunk 直接跳过提交（相机视锥缓存在 Frustum3D 每帧刷新）
                if (chunk.boundsValid)
                {
                    var wb = chunk.bounds;
                    wb.center += worldOffset;
                    if (!Frustum3D.Visible(wb)) continue;
                }
                Color edgeLit = Color.white;
                if (lf != null)
                {
                    edgeLit = lf.Shade(chunk.bounds.center, Vector3.up, false);
                }
                for (int i = 0; i < chunk.surfaces.Count; i++)
                {
                    var surf = chunk.surfaces[i];
                    var drawMat = surf.isEdgeFan && lf != null ? Lighting3D.MatFor(surf.mat, edgeLit) : surf.mat;
                    Graphics.DrawMesh(surf.mesh, m, drawMat, 0);
                    PerfCounters.TerrainMeshesDrawn++;
                    PerfCounters.TerrainQuads += surf.mesh.vertexCount / 4;
                }
            }
        }

        private void DisposeChunkMeshes(Chunk chunk)
        {
            for (int i = 0; i < chunk.surfaces.Count; i++) Util3D.SafeDestroy(chunk.surfaces[i].mesh);
            chunk.surfaces.Clear();
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
            private static readonly Color32 White = new Color32(255, 255, 255, 255);
            private static readonly Color32 Clear = new Color32(255, 255, 255, 0);

            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Color32> colors = new List<Color32>();
            private readonly List<int> tris = new List<int>();

            public bool HasGeometry => verts.Count > 0;

            public void AddQuad(Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc, Vector3 d, Vector2 ud)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                uvs.Add(ua); uvs.Add(ub); uvs.Add(uc); uvs.Add(ud);
                // 外法线按本项目绕序自己算，交给光照当向阳项的法线。
                var n = Vector3.Cross(b - a, c - a);
                n = n.sqrMagnitude < 1e-8f ? Vector3.up : n.normalized;
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                colors.Add(White); colors.Add(White); colors.Add(White); colors.Add(White);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            // 9 顶点扇：8 边 + 中心。顶点序与 SectionLayer_Terrain 一致，UV = 世界 xz。
            public void AddFan(int cellX, int cellZ, float y, bool[] flags)
            {
                int count = verts.Count;
                float x = cellX;
                float z = cellZ;
                AddFanVert(x + 0.5f, y, z, flags[0]);
                AddFanVert(x, y, z, flags[1]);
                AddFanVert(x, y, z + 0.5f, flags[2]);
                AddFanVert(x, y, z + 1f, flags[3]);
                AddFanVert(x + 0.5f, y, z + 1f, flags[4]);
                AddFanVert(x + 1f, y, z + 1f, flags[5]);
                AddFanVert(x + 1f, y, z + 0.5f, flags[6]);
                AddFanVert(x + 1f, y, z, flags[7]);
                AddFanVert(x + 0.5f, y, z + 0.5f, false);
                for (int i = 0; i < 8; i++)
                {
                    tris.Add(count + i);
                    tris.Add(count + (i + 1) % 8);
                    tris.Add(count + 8);
                }
            }

            private void AddFanVert(float x, float y, float z, bool on)
            {
                verts.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(x, z));
                normals.Add(Vector3.up);   // 扇是水平面，法线朝上
                colors.Add(on ? White : Clear);
            }

            /// <summary>
            /// 出网格。field 非空时把光照写进顶点色 **RGB**，alpha 原样保留：
            /// 原版 TerrainFadeRough 用顶点色 alpha 切软边，那是遮罩通道，不是光照通道。
            /// </summary>
            public Surface ToSurface(Material mat, LightField3D field, bool isEdgeFan)
            {
                if (verts.Count == 0) return null;
                var vArr = verts.ToArray();
                var nArr = normals.ToArray();
                var aArr = new byte[vArr.Length];
                var cArr = new Color32[vArr.Length];
                for (int i = 0; i < vArr.Length; i++)
                {
                    byte alpha = colors[i].a;
                    aArr[i] = alpha;
                    if (field != null)
                    {
                        var lit = Lighting3D.To32(field.Shade(vArr[i], nArr[i], true));
                        cArr[i] = new Color32(lit.r, lit.g, lit.b, alpha);
                    }
                    else
                    {
                        cArr[i] = new Color32(255, 255, 255, alpha);
                    }
                }
                var mesh = new Mesh();
                mesh.vertices = vArr;
                mesh.normals = nArr;
                mesh.SetUVs(0, uvs);
                mesh.colors32 = cArr;
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return new Surface
                {
                    mat = mat,
                    mesh = mesh,
                    verts = vArr,
                    normals = nArr,
                    alphas = aArr,
                    colors = cArr,
                    isEdgeFan = isEdgeFan
                };
            }
        }
    }
}
