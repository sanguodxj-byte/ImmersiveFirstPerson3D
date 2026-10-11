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
        private readonly Dictionary<ThingDef, Material> materials = new Dictionary<ThingDef, Material>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
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
            else if (colorRebakeTimer >= 0.45f && (
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
            int budget = Mathf.Max(1, Fps3DState.ChunkRebuildBudget);
            int colorBudget = budget * 2;
            for (int visited = 0; visited < chunks.Length && (LastChunksBuilt < budget || colorBudget > 0); visited++)
            {
                int i = nextChunk;
                nextChunk = (nextChunk + 1) % chunks.Length;
                var ch = chunks[i];
                if (ch.dirty)
                {
                    if (LastChunksBuilt < budget)
                    {
                        Rebuild(i, ch);
                        LastChunksBuilt++;
                    }
                }
                else if (ch.colorDirty && colorBudget > 0)
                {
                    Recolor(ch);
                    colorBudget--;
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
                    var material = MaterialFor(mountain.RockDefAt(x, z));
                    if (material == null) continue;
                    triangles.Clear();
                    MountainMesh3D.BuildCell(mountain.Shape, x, z, mountain.Cells, mountain.Ceilings, triangles);
                    if (triangles.Count == 0) continue;
                    if (!builders.TryGetValue(material, out var builder))
                    {
                        builder = new Builder(baseY, light);
                        builders.Add(material, builder);
                    }
                    for (int j = 0; j < triangles.Count; j++) builder.Add(triangles[j]);
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

        private Material MaterialFor(ThingDef def)
        {
            if (def == null) return null;
            if (materials.TryGetValue(def, out var cached)) return cached;
            var src = def.graphic?.MatSingle;
            if (src == null || src.mainTexture == null) return null;
            Texture2D tex = ExtractInterior(src.mainTexture, def, false);
            if (tex == null) return null;
            var material = new Material(src)
            {
                mainTexture = tex,
                mainTextureScale = Vector2.one,
                mainTextureOffset = Vector2.zero
            };
            if (src.HasProperty("_MaskTex"))
            {
                var mask = src.GetTexture("_MaskTex");
                if (mask != null) material.SetTexture("_MaskTex", ExtractInterior(mask, def, true));
            }
            materials.Add(def, material);
            return material;
        }

        private Texture2D ExtractInterior(Texture source, ThingDef def, bool mask)
        {
            // 原图集内格必须独立成 Repeat 纹理；对图集整体取模会串到其它连接格。
            int left = Mathf.RoundToInt(source.width * 0.78125f);
            int bottom = Mathf.RoundToInt(source.height * 0.78125f);
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * 0.1875f));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * 0.1875f));
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32,
                mask ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, true, mask)
                {
                    name = "FP3D_Mountain_" + def.defName,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat,
                    anisoLevel = 4
                };
                tex.ReadPixels(new Rect(left, bottom, width, height), 0, 0);
                tex.Apply(true, true);
                textures.Add(tex);
                return tex;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
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
                int ia = GetOrAddVertex(a, ResolveNormal(V(t.na), face), projection);
                int ib = GetOrAddVertex(b, ResolveNormal(V(t.nb), face), projection);
                int ic = GetOrAddVertex(c, ResolveNormal(V(t.nc), face), projection);
                indices.Add(ia); indices.Add(ib); indices.Add(ic);
            }

            private static Vector3 ResolveNormal(Vector3 n, Vector3 face)
            {
                if (n.sqrMagnitude < 1e-8f) return face;
                n.Normalize();
                if (Vector3.Dot(n, face) < 0f) n = -n;
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
                    // Continuous slope-aware UV: incorporates elevation y so steep slopes on all 4 aspects
                    // don't stretch texels, while remaining 100% seamless across shared vertices.
                    uv = new Vector2(p.x + p.y * 0.22f, p.z + p.y * 0.52f);
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
