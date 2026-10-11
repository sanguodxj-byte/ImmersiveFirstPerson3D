// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 草地细节层：相机周围的地被草丛（16x16 Chunk 分块缓存 + 视锥剔除）
    // ============================================================
    //
    // 原版地表只是一张平铺贴图，第一人称贴地看时寸草不生。本层在相机周围
    // 半径 R 内，于天然肥沃地表格上按哈希散布 1~3 丛草，带三档色差。
    //
    // 性能重构：
    //   1. 16x16 Chunk 分块烘焙与缓存，彻底移除 2.5s 全量重扫定时器与移动全量推倒重烘。
    //   2. 稳态原地环视 0 开销；持续移动时仅对新进入半径的边沿块分帧增量烘焙（<=2 块/帧）。
    //   3. 视锥剔除（Frustum3D.Visible）：相机后方与侧后方草块不向 GPU 提交 DrawMesh。
    [StaticConstructorOnStartup]
    public class GrassLayer3D
    {
        public const int ChunkSize = 16;
        private readonly Map map;
        private readonly float baseY;
        public Vector3 worldOffset = Vector3.zero;

        // 光照场（F013）：草丛实例批按每丛位置取放置亮度
        public LightField3D light;
        private LightField3D LF => light != null && Lighting3D.Enabled ? light : null;

        /// <summary>把本层草丛网格的颜色统计并进探针（只在按需导出时走）。</summary>
        public void Summarize(Lighting3D.Probe p)
        {
            foreach (var kv in chunks)
            {
                var ch = kv.Value;
                for (int i = 0; i < 3; i++)
                {
                    if (ch.meshes[i] != null) p.Add(ch.meshes[i]);
                }
            }
        }

        private class GrassChunk
        {
            public readonly Mesh[] meshes = new Mesh[3];
            public readonly Vector3[][] positions = new Vector3[3][];
            public readonly Color32[][] colors = new Color32[3][];
            public Mesh shadowMesh;
            public Bounds bounds;
            public int tuftCount;
            public bool dirty = true;
            public bool colorDirty;
            public float lastUsedTime;
        }

        private readonly Dictionary<int, GrassChunk> chunks = new Dictionary<int, GrassChunk>();
        private readonly List<int> pruneKeys = new List<int>();

        private static Material[] mats;
        private static Material tuftShadowMat;

        private static Material TuftShadowMat
        {
            get
            {
                if (tuftShadowMat != null) return tuftShadowMat;
                tuftShadowMat = new Material(ShaderDatabase.Transparent)
                {
                    name = "Fps3D_GrassShadow",
                    mainTexture = ProcTex3D.Radial(32, 0.20f, 1.35f),
                    color = new Color(0f, 0f, 0f, 0.38f)
                };
                return tuftShadowMat;
            }
        }

        public GrassLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty()
        {
            foreach (var kv in chunks) kv.Value.dirty = true;
        }

        public void MarkAllColorDirty()
        {
            foreach (var kv in chunks)
                if (!kv.Value.dirty && kv.Value.tuftCount > 0)
                    kv.Value.colorDirty = true;
        }

        public void MarkCellDirty(IntVec3 c)
        {
            int key = (c.x / ChunkSize) * 10000 + (c.z / ChunkSize);
            if (chunks.TryGetValue(key, out var ch)) ch.dirty = true;
        }

        public void MarkCellColorDirty(IntVec3 c)
        {
            int key = (c.x / ChunkSize) * 10000 + (c.z / ChunkSize);
            if (chunks.TryGetValue(key, out var ch) && !ch.dirty && ch.tuftCount > 0)
                ch.colorDirty = true;
        }

        private static bool Enabled
        {
            get { var s = FirstPerson3DMod.settings; return s == null || s.drawGrass; }
        }

        private static float Radius
        {
            get { var s = FirstPerson3DMod.settings; return s != null ? Mathf.Clamp(s.grassRadius, 6f, 60f) : 28f; }
        }

        private static void EnsureMaterials()
        {
            if (mats != null) return;
            Texture2D tex = null;
            try { tex = ContentFinder<Texture2D>.Get("Things/Plant/Grass/GrassA", false); } catch { }
            if (tex == null)
            {
                try
                {
                    var def = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Grass");
                    var g = def != null ? def.graphic : null;
                    if (g != null) tex = g.MatSingle != null ? g.MatSingle.mainTexture as Texture2D : null;
                }
                catch { }
            }
            mats = new Material[3];
            Color[] tints =
            {
                new Color(0.95f, 1.02f, 0.80f, 1f),
                new Color(1.06f, 1.10f, 0.92f, 1f),
                new Color(0.82f, 0.94f, 0.70f, 1f)
            };
            for (int i = 0; i < 3; i++)
            {
                var m = Util3D.NewPlantMat(null);   // 原版 plantwindsway 摇摆着色器
                if (m == null) m = new Material(ShaderDatabase.Cutout);
                m.name = "Fps3D_Grass" + i;
                m.mainTexture = tex != null ? (Texture)tex : Util3D.WhiteTex;
                m.color = tex != null ? tints[i] : new Color(0.35f, 0.55f, 0.25f, 1f) * tints[i];
                if (tex != null)
                {
                    // 底对齐：草叶贴地，避免贴图底部留白造成的"悬空"。
                    var vr = Util3D.VisibleVRange(tex, Vector2.one, Vector2.zero);
                    if (vr.x > 0.01f || vr.y < 0.99f)
                    {
                        m.mainTextureScale = new Vector2(1f, vr.y - vr.x);
                        m.mainTextureOffset = new Vector2(0f, vr.x);
                    }
                }
                mats[i] = m;
            }
        }

        private bool Eligible(IntVec3 c, out float fertility)
        {
            fertility = 0f;
            var td = map.terrainGrid.TerrainAt(c);
            if (td == null || td.IsWater || !td.natural) return false;
            if (td.fertility < 0.5f) return false;
            if (td.defName.Contains("Sand") || td.defName.Contains("Ice")) return false;
            if (c.GetEdifice(map) != null) return false;
            try { if (c.Roofed(map)) return false; } catch { }
            try { if (map.snowGrid != null && map.snowGrid.GetDepth(c) > 0.3f) return false; } catch { }
            if (c.Fogged(map)) return false;
            fertility = td.fertility;
            return true;
        }

        private static uint Hash(int x, int z, int k)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(k * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        private void DisposeChunk(GrassChunk chunk)
        {
            if (chunk == null) return;
            for (int i = 0; i < 3; i++)
            {
                if (chunk.meshes[i] != null)
                {
                    Util3D.SafeDestroy(chunk.meshes[i]);
                    chunk.meshes[i] = null;
                }
                chunk.positions[i] = null;
                chunk.colors[i] = null;
            }
            if (chunk.shadowMesh != null)
            {
                Util3D.SafeDestroy(chunk.shadowMesh);
                chunk.shadowMesh = null;
            }
            chunk.tuftCount = 0;
        }

        private void RebuildChunk(int cx, int cz, GrassChunk chunk)
        {
            DisposeChunk(chunk);
            chunk.dirty = false;
            chunk.colorDirty = false;
            int x0 = cx * ChunkSize;
            int z0 = cz * ChunkSize;
            int x1 = Mathf.Min(x0 + ChunkSize, map.Size.x);
            int z1 = Mathf.Min(z0 + ChunkSize, map.Size.z);
            if (x0 >= x1 || z0 >= z1) return;

            var byTint = new List<Matrix4x4>[3];
            for (int i = 0; i < 3; i++) byTint[i] = new List<Matrix4x4>(64);
            var shadowMats = new List<Matrix4x4>(128);
            int count = 0;

            for (int z = z0; z < z1; z++)
            {
                for (int x = x0; x < x1; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (!Eligible(c, out float fert)) continue;
                    int n = fert >= 1.0f ? 3 : fert >= 0.7f ? 2 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        uint h = Hash(c.x, c.z, k);
                        float ox = 0.12f + ((h & 0xFF) / 255f) * 0.76f;
                        float oz = 0.12f + (((h >> 8) & 0xFF) / 255f) * 0.76f;
                        float sc = 0.75f + (((h >> 16) & 0xFF) / 255f) * 0.55f;
                        int b = (int)((h >> 5) % 3);
                        if (b < 0 || b > 2) b = 1;
                        var pos = new Vector3(c.x + ox + worldOffset.x, baseY, c.z + oz + worldOffset.z);
                        float w = 0.42f * sc;
                        byTint[b].Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(w, 0.5f, w)));
                        shadowMats.Add(Matrix4x4.TRS(pos + new Vector3(0f, 0.018f, 0f), Quaternion.identity, new Vector3(w * 1.35f, 1f, w * 1.35f)));
                        count++;
                    }
                }
            }

            chunk.tuftCount = count;
            if (count > 0)
            {
                var lf = LF;
                for (int i = 0; i < 3; i++)
                {
                    if (byTint[i].Count > 0)
                        chunk.meshes[i] = Util3D.BuildInstancedMesh(Util3D.CrossMesh, byTint[i], lf, worldOffset,
                            out chunk.positions[i], out chunk.colors[i]);
                }
                chunk.shadowMesh = Util3D.BuildInstancedMesh(Util3D.GroundQuad, shadowMats);
                Vector3 center = new Vector3((x0 + x1) * 0.5f + worldOffset.x, baseY + 0.25f, (z0 + z1) * 0.5f + worldOffset.z);
                Vector3 size = new Vector3(x1 - x0, 1.5f, z1 - z0);
                chunk.bounds = new Bounds(center, size);
            }
        }

        private void RecolorChunk(GrassChunk chunk)
        {
            chunk.colorDirty = false;
            var lf = LF;
            for (int i = 0; i < 3; i++)
            {
                if (chunk.meshes[i] != null)
                    Util3D.RecolorInstancedMesh(chunk.meshes[i], Util3D.CrossMesh, chunk.positions[i], chunk.colors[i], lf);
            }
        }

        public void Draw(Vector3 camPos)
        {
            if (!Enabled || !Fps3DState.DrawPlants) return;
            EnsureMaterials();
            var local = camPos - worldOffset;
            float r = Radius;
            int minCx = Mathf.Max(0, Mathf.FloorToInt((local.x - r) / ChunkSize));
            int maxCx = Mathf.Min((map.Size.x - 1) / ChunkSize, Mathf.FloorToInt((local.x + r) / ChunkSize));
            int minCz = Mathf.Max(0, Mathf.FloorToInt((local.z - r) / ChunkSize));
            int maxCz = Mathf.Min((map.Size.z - 1) / ChunkSize, Mathf.FloorToInt((local.z + r) / ChunkSize));

            float now = Time.realtimeSinceStartup;
            int totalTufts = 0;
            // 初始阶段快速就位，稳态跨界时至多 2 块/帧平滑构建
            int buildBudget = chunks.Count < 16 ? 8 : 2;
            int colorBudget = 8;
            var sMat = TuftShadowMat;
            Matrix4x4 shMatrix = Matrix4x4.identity;
            var env = Lighting3D.Current;
            if (env.glow > 0.05f && env.sunDir.y > 0.05f)
            {
                float off = Mathf.Clamp(0.12f / Mathf.Max(0.25f, env.sunDir.y), 0.06f, 0.24f);
                shMatrix = Matrix4x4.Translate(new Vector3(-env.sunDir.x * off, 0f, -env.sunDir.z * off));
            }

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    float chunkCenterX = (cx + 0.5f) * ChunkSize;
                    float chunkCenterZ = (cz + 0.5f) * ChunkSize;
                    float dx = chunkCenterX - local.x;
                    float dz = chunkCenterZ - local.z;
                    // 保守放行：超出半径+半对角线(12m)的块不参与
                    if (dx * dx + dz * dz > (r + 12f) * (r + 12f)) continue;

                    int key = cx * 10000 + cz;
                    if (!chunks.TryGetValue(key, out var chunk))
                    {
                        chunk = new GrassChunk();
                        chunks[key] = chunk;
                    }
                    chunk.lastUsedTime = now;

                    if (chunk.dirty)
                    {
                        if (buildBudget > 0)
                        {
                            RebuildChunk(cx, cz, chunk);
                            buildBudget--;
                        }
                    }
                    else if (chunk.colorDirty && colorBudget > 0)
                    {
                        RecolorChunk(chunk);
                        colorBudget--;
                    }

                    if (chunk.tuftCount > 0)
                    {
                        // 视锥剔除：屏幕外 chunk 直接不提交绘制
                        if (Frustum3D.Visible(chunk.bounds))
                        {
                            if (chunk.shadowMesh != null && sMat != null)
                            {
                                Graphics.DrawMesh(chunk.shadowMesh, shMatrix, sMat, 0);
                                PerfCounters.PlantBatches++;
                            }
                            for (int i = 0; i < 3; i++)
                            {
                                var mesh = chunk.meshes[i];
                                if (mesh != null)
                                {
                                    Graphics.DrawMesh(mesh, Matrix4x4.identity, mats[i], 0);
                                    PerfCounters.PlantBatches++;
                                }
                            }
                        }
                        totalTufts += chunk.tuftCount;
                    }
                }
            }
            PerfCounters.GrassInstances = totalTufts;

            // 远距 chunk 惰性卸载（超过 60 秒未进入半径且总数过多时释放，避免小范围折返反复销毁重建）
            if (chunks.Count > 48)
            {
                pruneKeys.Clear();
                foreach (var kv in chunks)
                {
                    if (now - kv.Value.lastUsedTime > 60f)
                        pruneKeys.Add(kv.Key);
                }
                for (int i = 0; i < pruneKeys.Count; i++)
                {
                    int k = pruneKeys[i];
                    DisposeChunk(chunks[k]);
                    chunks.Remove(k);
                }
            }
        }

        public void Dispose()
        {
            foreach (var kv in chunks) DisposeChunk(kv.Value);
            chunks.Clear();
        }
    }
}