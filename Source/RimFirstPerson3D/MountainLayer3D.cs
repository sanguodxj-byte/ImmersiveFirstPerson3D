using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace RimFirstPerson3D
{
    public sealed class MountainLayer3D
    {
        private const int ChunkSize = BuildingLayer3D.ChunkSize;
        private readonly Map map;
        private readonly float baseY;
        private readonly Vector3 offset;
        private readonly MountainField3D mountain;
        private readonly LightField3D light;
        private readonly int chunksX;
        private readonly Chunk[] chunks;
        private readonly List<MountainTriangle3D> triangles = new List<MountainTriangle3D>(64);
        private readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private static float[] roughStoneGrain;
        private int nextChunk;
        private int lastQGlow = -999;
        private int lastQSunX = -999;
        private int lastQSunY = -999;
        private int lastQSunZ = -999;
        private float colorRebakeTimer = 999f;
        private long parameters = long.MinValue;

        private sealed class Surface
        {
            public Material material;
            public Mesh mesh;
            public Vector3[] vertices;
            public Vector3[] normals;
            public Color32[] colors;
            public int triCount;
        }

        private sealed class Chunk
        {
            public bool dirty = true;
            public bool colorDirty;
            public Bounds bounds;
            public readonly Dictionary<Material, Mesh> meshes = new Dictionary<Material, Mesh>();
            public readonly List<Surface> surfaces = new List<Surface>();
        }

        public float LastBuildMs { get; private set; }
        public int LastChunksBuilt { get; private set; }
        public int PendingChunks { get; private set; }

        public MountainLayer3D(Map map, float baseY, Vector3 offset, MountainField3D mountain, LightField3D light)
        {
            this.map = map;
            this.baseY = baseY;
            this.offset = offset;
            this.mountain = mountain;
            this.light = light;
            chunksX = (map.Size.x + ChunkSize - 1) / ChunkSize;
            int chunksZ = (map.Size.z + ChunkSize - 1) / ChunkSize;
            chunks = new Chunk[chunksX * chunksZ];
            for (int i = 0; i < chunks.Length; i++) chunks[i] = new Chunk();
        }

        public void MarkAllDirty()
        {
            for (int i = 0; i < chunks.Length; i++) chunks[i].dirty = true;
        }

        public void MarkAllColorDirty()
        {
            for (int i = 0; i < chunks.Length; i++)
                if (!chunks[i].dirty && chunks[i].surfaces.Count > 0)
                    chunks[i].colorDirty = true;
        }

        public void MarkCellDirty(IntVec3 c)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = c.x + dx, z = c.z + dz;
                    if (x < 0 || z < 0 || x >= map.Size.x || z >= map.Size.z) continue;
                    chunks[z / ChunkSize * chunksX + x / ChunkSize].dirty = true;
                }
        }

        public void MarkCellColorDirty(IntVec3 c)
        {
            if (c.x < 0 || c.z < 0 || c.x >= map.Size.x || c.z >= map.Size.z) return;
            var ch = chunks[c.z / ChunkSize * chunksX + c.x / ChunkSize];
            if (!ch.dirty && ch.surfaces.Count > 0) ch.colorDirty = true;
        }

        public void Update()
        {
            if (!mountain.Ready) return;
            var env = Lighting3D.Current;
            colorRebakeTimer += Time.deltaTime;
            int qg = Mathf.RoundToInt(env.glow * 16f);
            int qsx = Mathf.RoundToInt(env.sunDir.x * 16f);
            int qsy = Mathf.RoundToInt(env.sunDir.y * 16f);
            int qsz = Mathf.RoundToInt(env.sunDir.z * 16f);
            long param = Lighting3D.ParamKey();
            if (param != parameters)
            {
                parameters = param;
                lastQGlow = qg;
                lastQSunX = qsx; lastQSunY = qsy; lastQSunZ = qsz;
                colorRebakeTimer = 0f;
                MarkAllColorDirty();
            }
            else if (lastQGlow == -999)
            {
                lastQGlow = qg;
                lastQSunX = qsx; lastQSunY = qsy; lastQSunZ = qsz;
            }
            else if (colorRebakeTimer >= 2.0f && (
                Mathf.Abs(qg - lastQGlow) >= 2 ||
                Mathf.Abs(qsx - lastQSunX) + Mathf.Abs(qsy - lastQSunY) + Mathf.Abs(qsz - lastQSunZ) >= 2))
            {
                lastQGlow = qg;
                lastQSunX = qsx; lastQSunY = qsy; lastQSunZ = qsz;
                colorRebakeTimer = 0f;
                MarkAllColorDirty();
            }
            LastChunksBuilt = 0;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            double freqMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            int budget = Mathf.Max(1, Fps3DState.ChunkRebuildBudget);
            int colorBudget = budget + 1;
            float colorMs = Fps3DState.ChunkColorBudgetMs;
            // 第一轮：仅处理相机视锥内的山体 chunk；视锥外的 colorDirty 留到进入视野时再刷
            for (int i = 0; i < chunks.Length && (LastChunksBuilt < budget || (colorBudget > 0 && colorMs > 0f)); i++)
            {
                var ch = chunks[i];
                if (!ch.dirty && !ch.colorDirty) continue;
                if (ch.surfaces.Count > 0)
                {
                    var wb = ch.bounds;
                    wb.center += offset;
                    if (!Frustum3D.Visible(wb)) continue;
                }
                if (ch.dirty)
                {
                    if (LastChunksBuilt < budget)
                    {
                        Rebuild(i, ch);
                        LastChunksBuilt++;
                    }
                }
                else if (ch.colorDirty && colorBudget > 0 && colorMs > 0f)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    Recolor(ch);
                    colorMs -= (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * freqMs);
                    colorBudget--;
                }
            }
            // 第二轮：仅利用剩余预算后台推进视锥外几何脏块（dirty），不刷视锥外的 colorDirty
            for (int visited = 0; visited < chunks.Length && LastChunksBuilt < budget; visited++)
            {
                int i = nextChunk;
                nextChunk = (nextChunk + 1) % chunks.Length;
                var ch = chunks[i];
                if (ch.dirty)
                {
                    Rebuild(i, ch);
                    LastChunksBuilt++;
                }
            }
            PendingChunks = 0;
            for (int i = 0; i < chunks.Length; i++) if (chunks[i].dirty) PendingChunks++;
            LastBuildMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0
                / System.Diagnostics.Stopwatch.Frequency);
        }

        private void Recolor(Chunk chunk)
        {
            chunk.colorDirty = false;
            bool enabled = Lighting3D.Enabled;
            for (int s = 0; s < chunk.surfaces.Count; s++)
            {
                var surf = chunk.surfaces[s];
                if (surf.mesh == null || surf.vertices == null) continue;
                var verts = surf.vertices;
                var nors = surf.normals;
                var cols = surf.colors;
                int n = verts.Length;
                if (enabled)
                {
                    for (int i = 0; i < n; i++)
                        cols[i] = Lighting3D.To32(light.ShadeMountain(verts[i], nors[i]));
                }
                else
                {
                    var white = new Color32(255, 255, 255, 255);
                    for (int i = 0; i < n; i++) cols[i] = white;
                }
                surf.mesh.colors32 = cols;
            }
        }

        private void Rebuild(int index, Chunk chunk)
        {
            int x0 = index % chunksX * ChunkSize, z0 = index / chunksX * ChunkSize;
            int x1 = Mathf.Min(map.Size.x, x0 + ChunkSize), z1 = Mathf.Min(map.Size.z, z0 + ChunkSize);
            var builders = new Dictionary<Material, Builder>();
            for (int z = z0; z < z1; z++)
                for (int x = x0; x < x1; x++)
                {
                    if (mountain.Cells[z * map.Size.x + x] == 0) continue;
                    var rockDef = mountain.RockDefAt(x, z);
                    var hostDef = mountain.HostRockDefAt(x, z) ?? rockDef;
                    var cliffMat = MaterialFor(rockDef, hostDef);
                    var slopeMat = MaterialFor(hostDef, hostDef) ?? cliffMat;
                    if (cliffMat == null) continue;
                    triangles.Clear();
                    MountainMesh3D.BuildCell(mountain.Shape, x, z, mountain.Cells, mountain.Ceilings, triangles);
                    if (triangles.Count == 0) continue;
                    for (int j = 0; j < triangles.Count; j++)
                    {
                        var tri = triangles[j];
                        float minY = Mathf.Min(tri.a.y, Mathf.Min(tri.b.y, tri.c.y));
                        // 矿脉只暴露在下半部陡壁与山脚（<= 3.6m），高处山坡与山脊统一使用母岩材质，避免山峰顶出现突兀方块矿斑
                        var mat = (tri.surface && minY > 3.6f) ? slopeMat : cliffMat;
                        if (!builders.TryGetValue(mat, out var builder))
                        {
                            builder = new Builder(baseY, light);
                            builders.Add(mat, builder);
                        }
                        builder.Add(tri);
                    }
                }
            Clear(chunk);
            var bounds = new Bounds();
            bool first = true;
            foreach (var pair in builders)
            {
                var surf = pair.Value.ToSurface(pair.Key);
                if (surf == null || surf.mesh == null) continue;
                chunk.meshes.Add(pair.Key, surf.mesh);
                chunk.surfaces.Add(surf);
                if (first) { bounds = surf.mesh.bounds; first = false; }
                else bounds.Encapsulate(surf.mesh.bounds);
            }
            chunk.bounds = bounds;
            chunk.dirty = false;
            chunk.colorDirty = false;
        }

        private Material MaterialFor(ThingDef def, ThingDef hostDef)
        {
            if (def == null) return null;
            if (hostDef == null) hostDef = def;
            string key = def.defName + "|" + hostDef.defName;
            if (materials.TryGetValue(key, out var cached)) return cached;
            Texture2D tex = BuildMountainTexture(def, hostDef);
            if (tex == null) return null;
            var src = def.graphic?.MatSingle;
            var material = new Material(ShaderDatabase.Cutout)
            {
                mainTexture = tex,
                mainTextureScale = Vector2.one,
                mainTextureOffset = Vector2.zero,
                color = Color.white
            };
            if (src != null) material.renderQueue = src.renderQueue;
            materials.Add(key, material);
            return material;
        }

        private static float[] GetRoughStoneGrain(int size)
        {
            if (roughStoneGrain != null && roughStoneGrain.Length == size * size)
                return roughStoneGrain;
            roughStoneGrain = new float[size * size];
            Texture src = null;
            try { src = ContentFinder<Texture2D>.Get("Terrain/Surfaces/RoughStone", reportFailure: false); }
            catch { src = null; }
            if (src != null)
            {
                var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                var prev = RenderTexture.active;
                try
                {
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    var tmp = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    tmp.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                    var px = tmp.GetPixels32();
                    Object.Destroy(tmp);
                    float sum = 0f;
                    for (int i = 0; i < px.Length; i++)
                    {
                        float l = (px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f) / 255f;
                        roughStoneGrain[i] = l;
                        sum += l;
                    }
                    float avg = sum / Mathf.Max(1, px.Length);
                    for (int i = 0; i < roughStoneGrain.Length; i++)
                        roughStoneGrain[i] = Mathf.Clamp((roughStoneGrain[i] - avg) * 0.85f, -0.14f, 0.14f);
                    return roughStoneGrain;
                }
                catch { }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    roughStoneGrain[y * size + x] = PeriodicNoise(x / (float)size, y / (float)size, 16, 16, 0x33A19u) * 0.08f;
            return roughStoneGrain;
        }

        private static Color CalibrateStoneColor(ThingDef hostDef)
        {
            string name = hostDef != null ? (hostDef.defName ?? "") : "";
            if (name.IndexOf("Slate", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.50f, 0.53f, 0.58f, 1f);
            if (name.IndexOf("Granite", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.62f, 0.57f, 0.58f, 1f);
            if (name.IndexOf("Sandstone", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.72f, 0.60f, 0.49f, 1f);
            if (name.IndexOf("Limestone", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.76f, 0.72f, 0.63f, 1f);
            if (name.IndexOf("Marble", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return new Color(0.80f, 0.82f, 0.80f, 1f);

            Color raw = new Color(0.62f, 0.59f, 0.55f, 1f);
            if (hostDef?.graphicData != null && hostDef.graphicData.color != Color.white)
                raw = hostDef.graphicData.color;
            else if (hostDef?.graphic?.MatSingle != null)
                raw = hostDef.graphic.MatSingle.color;

            float luma = 0.299f * raw.r + 0.587f * raw.g + 0.114f * raw.b;
            float targetLuma = Mathf.Lerp(0.52f, 0.80f, Mathf.Clamp01((luma - 0.20f) / 0.55f));
            float scale = luma > 0.01f ? (targetLuma / luma) : 1f;
            return new Color(
                Mathf.Clamp01(raw.r * scale),
                Mathf.Clamp01(raw.g * scale),
                Mathf.Clamp01(raw.b * scale),
                1f);
        }

        private static Color CalibrateOreColor(ThingDef oreDef, Color hostColor)
        {
            Color raw = hostColor;
            if (oreDef?.graphicData != null)
                raw = oreDef.graphicData.color;
            else if (oreDef?.graphic?.MatSingle != null)
                raw = oreDef.graphic.MatSingle.color;

            float luma = 0.299f * raw.r + 0.587f * raw.g + 0.114f * raw.b;
            float target = Mathf.Clamp(luma * 1.25f + 0.15f, 0.48f, 0.92f);
            float scale = luma > 0.01f ? (target / luma) : 1f;
            return new Color(
                Mathf.Clamp01(raw.r * scale),
                Mathf.Clamp01(raw.g * scale),
                Mathf.Clamp01(raw.b * scale),
                1f);
        }

        private static uint HashGrid(int x, int y, int px, int py, uint seed)
        {
            int wx = ((x % px) + px) % px;
            int wy = ((y % py) + py) % py;
            uint h = seed ^ ((uint)wx * 374761393u) ^ ((uint)wy * 668265263u);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        private static float PeriodicNoise(float u, float v, int px, int py, uint seed)
        {
            float x = u * px;
            float y = v * py;
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            float fx = x - ix;
            float fy = y - iy;
            float su = fx * fx * (3f - 2f * fx);
            float sv = fy * fy * (3f - 2f * fy);

            float n00 = (HashGrid(ix, iy, px, py, seed) & 0xFFFF) / 32767.5f - 1f;
            float n10 = (HashGrid(ix + 1, iy, px, py, seed) & 0xFFFF) / 32767.5f - 1f;
            float n01 = (HashGrid(ix, iy + 1, px, py, seed) & 0xFFFF) / 32767.5f - 1f;
            float n11 = (HashGrid(ix + 1, iy + 1, px, py, seed) & 0xFFFF) / 32767.5f - 1f;

            return Mathf.Lerp(Mathf.Lerp(n00, n10, su), Mathf.Lerp(n01, n11, su), sv);
        }

        private static void CellularRock(
            float u, float v, int gx, int gy, uint seed,
            out float d1, out float d2, out float bevel, out uint cellHash)
        {
            float x = u * gx;
            float y = v * gy;
            int ix = Mathf.FloorToInt(x);
            int iy = Mathf.FloorToInt(y);
            d1 = 99f;
            d2 = 99f;
            bevel = 0f;
            cellHash = 0u;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = ix + dx;
                    int cy = iy + dy;
                    uint h = HashGrid(cx, cy, gx, gy, seed);
                    float jx = ((h & 0x3FF) / 1023f) * 0.78f + 0.11f;
                    float jy = (((h >> 10) & 0x3FF) / 1023f) * 0.78f + 0.11f;
                    float px = cx + jx - x;
                    float py = (cy + jy - y) * 1.35f; // 横向块状岩体比例
                    float dist = Mathf.Sqrt(px * px + py * py);
                    if (dist < d1)
                    {
                        d2 = d1;
                        d1 = dist;
                        cellHash = h;
                        // 块面内左上受光、右下背光的岩石凿面斜率
                        bevel = Mathf.Clamp((-py * 0.85f + px * 0.35f) * 0.55f, -0.45f, 0.45f);
                    }
                    else if (dist < d2)
                    {
                        d2 = dist;
                    }
                }
            }
        }

        private Texture2D BuildMountainTexture(ThingDef def, ThingDef hostDef)
        {
            const int Size = 256;
            var grain = GetRoughStoneGrain(Size);
            Color baseStone = CalibrateStoneColor(hostDef);
            bool isOre = def != null && def.building != null && def.building.isResourceRock;
            Color oreColor = isOre ? CalibrateOreColor(def, baseStone) : baseStone;

            uint seed = (uint)((hostDef != null ? hostDef.shortHash : 17) * 2654435761u);
            var px = new Color32[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                float v = y / (float)Size;
                int row = y * Size;
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;

                    // 1. 各向同性周期坐标扭曲（形成自然岩石节理与块面走势，无单一方向条带）
                    float warpU = u + 0.055f * PeriodicNoise(u, v, 3, 3, seed ^ 0x11A3u)
                                    + 0.025f * PeriodicNoise(u, v, 7, 7, seed ^ 0x22B7u);
                    float warpV = v + 0.055f * PeriodicNoise(u, v, 3, 3, seed ^ 0x33C9u)
                                    + 0.025f * PeriodicNoise(u, v, 7, 7, seed ^ 0x44DFu);

                    // 2. 多尺度天然岩体色块与冷暖风化斑块（各向同性，消除斑马纹/等高线波纹）
                    float macroPatch = PeriodicNoise(warpU, warpV, 2, 2, seed ^ 0x55E1u) * 0.10f
                                     + PeriodicNoise(warpU, warpV, 4, 4, seed ^ 0x59F2u) * 0.08f;
                    float warmShift = PeriodicNoise(warpU, warpV, 3, 3, seed ^ 0xA147u) * 0.045f
                                    + PeriodicNoise(u, v, 8, 8, seed ^ 0xA981u) * 0.025f;

                    // 3. 正交交错岩石劈理棱线（Ridged Multifractal：提供粗粝山石块面感，无封闭多边形网格）
                    float r1 = 1f - Mathf.Abs(PeriodicNoise(warpU, warpV, 4, 4, seed ^ 0x66F3u));
                    float r2 = 1f - Mathf.Abs(PeriodicNoise(warpU + 0.37f, warpV + 0.61f, 8, 8, seed ^ 0x7711u));
                    float r3 = 1f - Mathf.Abs(PeriodicNoise(u + 0.19f, v + 0.43f, 16, 16, seed ^ 0x7B29u));
                    float ridgeRelief = (r1 * r1 * r1 - 0.44f) * 0.22f
                                      + (r2 * r2 - 0.48f) * 0.14f
                                      + (r3 * r3 - 0.48f) * 0.08f;

                    // 4. 稀疏短促的天然岩体节理暗缝（仅局部断续出现，不连成贯穿条纹）
                    float cleftA = Mathf.Abs(PeriodicNoise(warpU + warpV * 0.4f, warpV - warpU * 0.4f, 5, 5, seed ^ 0x8823u));
                    float cleftB = Mathf.Abs(PeriodicNoise(warpU - warpV * 0.5f, warpV + warpU * 0.5f, 6, 6, seed ^ 0x8D41u));
                    float cleftGate = PeriodicNoise(u, v, 4, 4, seed ^ 0x9935u);
                    float cleft = 1.0f;
                    if (cleftGate > 0.22f)
                    {
                        float minCleft = Mathf.Min(cleftA, cleftB);
                        if (minCleft < 0.045f)
                            cleft = Mathf.Lerp(0.68f, 1.0f, minCleft / 0.045f);
                    }

                    // 5. 叠加原版 RoughStone 自然岩石表面颗粒度与细微晶粒
                    float fineSpeckle = PeriodicNoise(u, v, 24, 24, seed ^ 0x9F17u) * 0.045f;
                    float micro = grain[row + x] * 1.55f + fineSpeckle;
                    float shade = (1.0f + macroPatch + ridgeRelief + micro) * cleft;
                    shade = Mathf.Clamp(shade, 0.66f, 1.26f);

                    float r = baseStone.r * (shade + warmShift * 0.60f);
                    float g = baseStone.g * shade;
                    float b = baseStone.b * (shade - warmShift * 0.60f);

                    // 6. 矿脉格：沿天然岩层裂隙嵌入金属/矿物脉络（全实心填充）
                    if (isOre)
                    {
                        float veinPath = Mathf.Abs(PeriodicNoise(warpU, warpV, 5, 5, seed ^ 0xAA47u));
                        float veinGate = PeriodicNoise(u, v, 4, 4, seed ^ 0xBB59u);
                        if (veinPath < 0.11f && veinGate > -0.10f)
                        {
                            float core = 1f - (veinPath / 0.11f);
                            float glint = 0.96f + 0.28f * core + 0.14f * PeriodicNoise(u, v, 16, 16, seed ^ 0xCC6Bu);
                            r = Mathf.Lerp(r * 0.80f, oreColor.r * glint, Mathf.Clamp01(core * 1.4f));
                            g = Mathf.Lerp(g * 0.80f, oreColor.g * glint, Mathf.Clamp01(core * 1.4f));
                            b = Mathf.Lerp(b * 0.80f, oreColor.b * glint, Mathf.Clamp01(core * 1.4f));
                        }
                    }

                    px[row + x] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                        255);
                }
            }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, true, false)
            {
                name = "FP3D_Mountain_" + def.defName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 4
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            textures.Add(tex);
            return tex;
        }

        public void Draw()
        {
            var matrix = Matrix4x4.Translate(offset);
            for (int i = 0; i < chunks.Length; i++)
            {
                var chunk = chunks[i];
                if (chunk.surfaces.Count == 0) continue;
                var bounds = chunk.bounds;
                bounds.center += offset;
                if (!Frustum3D.Visible(bounds)) continue;
                for (int s = 0; s < chunk.surfaces.Count; s++)
                {
                    var surf = chunk.surfaces[s];
                    if (surf.mesh == null) continue;
                    Graphics.DrawMesh(surf.mesh, matrix, surf.material, 0);
                    PerfCounters.BuildingMeshesDrawn++;
                    PerfCounters.BuildingQuads += (surf.triCount + 1) / 2;
                }
            }
        }

        public void Summarize(Lighting3D.Probe probe)
        {
            for (int i = 0; i < chunks.Length; i++)
                foreach (var pair in chunks[i].meshes) probe.Add(pair.Value);
        }

        internal IEnumerable<Mesh> Meshes()
        {
            for (int i = 0; i < chunks.Length; i++)
                foreach (var pair in chunks[i].meshes) yield return pair.Value;
        }

        private static void Clear(Chunk chunk)
        {
            foreach (var pair in chunk.meshes) Util3D.SafeDestroy(pair.Value);
            chunk.meshes.Clear();
            chunk.surfaces.Clear();
        }

        public void Dispose()
        {
            for (int i = 0; i < chunks.Length; i++) Clear(chunks[i]);
            foreach (var pair in materials) if (pair.Value != null) Object.Destroy(pair.Value);
            for (int i = 0; i < textures.Count; i++) if (textures[i] != null) Object.Destroy(textures[i]);
            materials.Clear();
            textures.Clear();
            triangles.Clear();
        }

        private sealed class Builder
        {
            private struct VertexKey : System.IEquatable<VertexKey>
            {
                public readonly int px, py, pz;
                public readonly short nx, ny, nz;
                public readonly byte proj;

                public VertexKey(Vector3 p, Vector3 n, int projection)
                {
                    px = Mathf.RoundToInt(p.x * 1024f);
                    py = Mathf.RoundToInt(p.y * 1024f);
                    pz = Mathf.RoundToInt(p.z * 1024f);
                    nx = (short)Mathf.RoundToInt(n.x * 4096f);
                    ny = (short)Mathf.RoundToInt(n.y * 4096f);
                    nz = (short)Mathf.RoundToInt(n.z * 4096f);
                    proj = (byte)projection;
                }

                public bool Equals(VertexKey other)
                {
                    return px == other.px && py == other.py && pz == other.pz
                        && nx == other.nx && ny == other.ny && nz == other.nz
                        && proj == other.proj;
                }

                public override bool Equals(object obj) => obj is VertexKey other && Equals(other);

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int h = px * 73856093 ^ py * 19349663 ^ pz * 83492791;
                        h = h * 397 ^ ((nx << 16) | (ushort)ny);
                        h = h * 397 ^ ((nz << 8) | proj);
                        return h;
                    }
                }
            }

            private readonly float baseY;
            private readonly LightField3D light;
            private readonly List<Vector3> vertices = new List<Vector3>(512);
            private readonly List<Vector3> normals = new List<Vector3>(512);
            private readonly List<Vector2> uvs = new List<Vector2>(512);
            private readonly List<Color32> colors = new List<Color32>(512);
            private readonly List<int> indices = new List<int>(1536);
            private readonly Dictionary<VertexKey, int> vertexMap = new Dictionary<VertexKey, int>(512);

            public Builder(float baseY, LightField3D light) { this.baseY = baseY; this.light = light; }

            private static Vector3 V(MountainPoint3D p) => new Vector3(p.x, p.y, p.z);

            public void Add(MountainTriangle3D t)
            {
                var a = V(t.a); var b = V(t.b); var c = V(t.c);
                var face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-12f) return;
                face.Normalize();
                int projection = t.surface ? 3 : Mathf.Abs(face.y) > 0.5f ? 1 : Mathf.Abs(face.x) > 0.5f ? 0 : 2;
                int ia = GetOrAddVertex(a, ResolveNormal(V(t.na), face, t.surface), projection);
                int ib = GetOrAddVertex(b, ResolveNormal(V(t.nb), face, t.surface), projection);
                int ic = GetOrAddVertex(c, ResolveNormal(V(t.nc), face, t.surface), projection);
                indices.Add(ia); indices.Add(ib); indices.Add(ic);
            }

            private static Vector3 ResolveNormal(Vector3 n, Vector3 face, bool surface)
            {
                if (n.sqrMagnitude < 1e-8f) return face;
                n.Normalize();
                if (Vector3.Dot(n, face) < 0f) n = -n;
                if (surface)
                {
                    // Blend triangle facet normal (82%) with smooth terrain normal (18%) for crisp chiseled rock planes
                    n = (n * 0.18f + face * 0.82f).normalized;
                }
                return n;
            }

            private int GetOrAddVertex(Vector3 p, Vector3 n, int projection)
            {
                var key = new VertexKey(p, n, projection);
                if (vertexMap.TryGetValue(key, out int existing))
                    return existing;

                Vector2 uv;
                if (projection == 3)
                {
                    // Orthogonal horizontal basis (0.80, 0.35) x (-0.35, 0.80) coupled with elevation y
                    // so rock slabs and strata never stretch or degenerate on any compass slope.
                    uv = new Vector2(
                        p.x * 0.80f + p.z * 0.35f + p.y * 0.22f,
                        -p.x * 0.35f + p.z * 0.80f + p.y * 0.55f);
                }
                else if (projection == 1)
                {
                    uv = new Vector2(p.x, p.z);
                }
                else if (projection == 0)
                {
                    uv = new Vector2(p.z, p.y);
                }
                else
                {
                    uv = new Vector2(p.x, p.y);
                }
                p.y += baseY;
                int idx = vertices.Count;
                vertexMap.Add(key, idx);
                vertices.Add(p);
                normals.Add(n);
                uvs.Add(uv / Metrics3D.MountainTextureMeters);
                colors.Add(Lighting3D.To32(Lighting3D.Enabled ? light.ShadeMountain(p, n) : Color.white));
                return idx;
            }

            public Surface ToSurface(Material mat)
            {
                if (vertices.Count == 0) return null;
                var vArr = vertices.ToArray();
                var nArr = normals.ToArray();
                var cArr = colors.ToArray();
                var mesh = new Mesh { name = "FP3D_Mountain", indexFormat = IndexFormat.UInt32 };
                mesh.vertices = vArr;
                mesh.normals = nArr;
                mesh.SetUVs(0, uvs);
                mesh.colors32 = cArr;
                mesh.SetTriangles(indices, 0);
                mesh.RecalculateBounds();
                return new Surface
                {
                    material = mat,
                    mesh = mesh,
                    vertices = vArr,
                    normals = nArr,
                    colors = cArr,
                    triCount = indices.Count / 3
                };
            }
        }
    }
}
