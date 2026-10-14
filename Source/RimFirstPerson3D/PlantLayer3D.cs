// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 植物层
    // ============================================================
    //
    // 改造点：
    //   树木  —— 三片 60° 交叉面片（比双片十字更有体积感），脚下带柔和阴影盘
    //   小株  —— 十字交叉面片（草/灌木/作物从任意方向看都不裸露"十字缝"）
    //   色差  —— 三档色调分批，打破大片同色
    //   高度  —— **世界高度按类别固定**（不跟贴图 drawSize 走，避免任意高度）：
    //            各种草 0.5m、树 3.0m、灵树 3.5m。宽度仍取原版 drawSize.x（冠幅），
    //            高度再乘设置项 plantScale。见 WorldHeightOf()。
    //
    // 【关键修复 r44】原先用 Graphics.DrawMeshInstanced 提交，但 RimWorld 的原版
    // 着色器不含 instancing 变体，实例化绘制会**静默**产出 0 像素（不报错），
    // 导致第一人称下所有植被不可见（原版视角正常只是因为走的是原版绘制路径）。
    // 现改为：脏重建时把每个实例逐顶点烘焙进合并网格，再用 Graphics.DrawMesh 绘制
    // ——与地形/建筑/人物完全相同的绘制路径，已证明可正常出图。
    //
    // 摇摆：材质改用原版 materials/map/plantwindsway 着色器（GPU 顶点动画，由地图
    // WindManager 每帧驱动），因此植被随风摆动，且与合并网格兼容、零 CPU 开销
    // ——不再需要逐帧改矩阵，也不受"烘焙后朝向固定"限制。
    [StaticConstructorOnStartup]
    public class PlantLayer3D
    {
        private readonly Map map;
        public Vector3 worldOffset = Vector3.zero;

        // 光照场（F013）：实例批按每株所在位置取放置亮度，写进合并网格的顶点色。
        public LightField3D light;
        private LightField3D LF => light != null && Lighting3D.Enabled ? light : null;

        public const int ChunkSize = 32;
        private readonly int chunksX, chunksZ;
        private readonly float baseY;

        private class Batch
        {
            public Material mat;
            public Mesh srcMesh;
            public Mesh mesh;
            public Vector3[] positions;
            public Color32[] colors;
            public byte[] tints;
        }

        private class ModelBatch
        {
            public Mesh srcBody;
            public Mesh body;
            public Mesh hull;
            public Vector3[] positions;
            public Color32[] colors;
            public byte[] tints;
            public int tint;
            public int count;
        }

        private class InstGroup
        {
            public readonly List<Matrix4x4> mats = new List<Matrix4x4>(32);
            public readonly List<byte> tints = new List<byte>(32);
        }

        private class PlantChunk
        {
            public readonly List<Batch> batches = new List<Batch>();
            public readonly List<ModelBatch> modelBatches = new List<ModelBatch>();
            public Mesh shadowMesh;
            public Bounds bounds;
            public int plantCount;
            public bool dirty = true;
            public bool colorDirty;
        }

        private readonly Dictionary<int, PlantChunk> chunks = new Dictionary<int, PlantChunk>();
        private readonly List<int> sortedDirtyKeys = new List<int>();
        private readonly List<int> colorDirtyKeys = new List<int>();
        private bool allDirty = true;
        private float builtScale = -1f;

        /// <summary>把本层实例批网格的颜色统计并进探针（只在按需导出时走）。</summary>
        public void Summarize(Lighting3D.Probe p)
        {
            foreach (var kv in chunks)
            {
                var ch = kv.Value;
                for (int i = 0; i < ch.batches.Count; i++) p.Add(ch.batches[i].mesh);
                for (int i = 0; i < ch.modelBatches.Count; i++) p.Add(ch.modelBatches[i].body);
            }
        }

        private static Material shadowMat;
        private static readonly Dictionary<Material, Material[]> tintCache = new Dictionary<Material, Material[]>();

        public PlantLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
            chunksX = (map.Size.x + ChunkSize - 1) / ChunkSize;
            chunksZ = (map.Size.z + ChunkSize - 1) / ChunkSize;
        }

        public void MarkAllDirty() => allDirty = true;

        public void MarkAllColorDirty()
        {
            foreach (var kv in chunks)
                if (!kv.Value.dirty && kv.Value.plantCount > 0)
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
            if (chunks.TryGetValue(key, out var ch) && !ch.dirty && ch.plantCount > 0)
                ch.colorDirty = true;
        }

        public void NotifyThingChanged(Thing t)
        {
            if (t == null || t.def == null || t.def.category != ThingCategory.Plant) return;
            var pos = t.Position;
            if (!pos.InBounds(map)) return;
            int key = (pos.x / ChunkSize) * 10000 + (pos.z / ChunkSize);
            if (!chunks.TryGetValue(key, out var ch))
            {
                ch = new PlantChunk();
                chunks[key] = ch;
            }
            ch.dirty = true;
        }

        // ---- 共享材质 ----

        private static Material ShadowMat
        {
            get
            {
                if (shadowMat != null) return shadowMat;
                shadowMat = new Material(ShaderDatabase.TransparentPostLight ?? ShaderDatabase.Transparent)
                {
                    name = "Fps3D_PlantShadow",
                    mainTexture = ProcTex3D.Radial(64, 0.25f, 1.35f),
                    color = new Color(0f, 0f, 0f, 0.22f),
                    renderQueue = 2915
                };
                return shadowMat;
            }
        }

        /// <summary>同一原版材质派生三档色调（原色 / 偏黄 / 偏深）。</summary>
        private static Material TintOf(Material src, int idx)
        {
            if (src == null) return null;
            if (!tintCache.TryGetValue(src, out var arr))
            {
                arr = new Material[3];
                Color[] tints =
                {
                    new Color(1.00f, 1.00f, 1.00f, 1f),
                    new Color(0.90f, 0.96f, 0.80f, 1f),
                    new Color(0.80f, 0.88f, 0.74f, 1f)
                };
                for (int i = 0; i < 3; i++)
                {
                    arr[i] = Util3D.NewPlantMat(src);   // 用原版 plantwindsway 着色器 -> 随风摇摆
                    arr[i].color = src.color * tints[i];
                }
                AlignBottomToGround(src, arr);
                tintCache[src] = arr;
            }
            return arr[idx];
        }

        // 原版植物贴图是俯视(XZ)方片，内容常四周留透明边；立起来当高度后，底部留白会
        // 表现为"离地悬空"。按贴图不透明像素范围重映射材质 UV：内容底边对齐片底(y=0)、
        // 内容顶部对齐片顶(约定高度)，消除悬空；对已满幅的贴图不改动（退化为原样）。
        private static void AlignBottomToGround(Material src, Material[] arr)
        {
            try
            {
                var tex = src.mainTexture as Texture2D;
                if (tex == null || arr == null) return;
                var vr = Util3D.VisibleVRange(tex, src.mainTextureScale, src.mainTextureOffset);  // 归一化 v 范围（0=底 1=顶）
                if (vr.x <= 0.01f && vr.y >= 0.99f) return;      // 已满幅
                float span = vr.y - vr.x;
                if (span <= 0.02f) return;
                Vector2 s = src.mainTextureScale;
                Vector2 o = src.mainTextureOffset;
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) continue;
                    arr[i].mainTextureScale = new Vector2(s.x, s.y * span);
                    arr[i].mainTextureOffset = new Vector2(o.x, o.y + s.y * vr.x);
                }
            }
            catch { }
        }

        public static Vector2 SizeOf(Plant plant)
        {
            try
            {
                var g = plant.Graphic;
                if (g != null && g.drawSize.sqrMagnitude > 0.0001f) return g.drawSize;
            }
            catch { }
            return new Vector2(1f, 1f);
        }

        // 世界高度约定（米）：各种草 0.5、树 3.0、灵树 3.5。
        // 不再跟随贴图 drawSize.y（那是任意值），按类别给固定高度，保证第一人称空间感一致。
        private static float WorldHeightOf(Plant plant)
        {
            try
            {
                var def = plant.def;
                if (def != null)
                {
                    // 灵树（Anima tree）：RimWorld Core 的 defName 为 Plant_TreeAnima，这里按名判定。
                    if (def.defName != null && def.defName.IndexOf("Anima", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return 3.5f;
                    if (def.plant != null && def.plant.IsTree)
                        return 3.0f;   // 树
                }
            }
            catch { }
            return 0.5f;               // 各种草/草本
        }

        private static Material MatFor(Thing t)
        {
            var g = t.Graphic;
            if (g == null) return null;
            Material m = null;
            try { m = g.MatAt(t.Rotation, t); } catch { }
            if (m == null || m.NullOrBad()) { try { m = g.MatSingle; } catch { } }
            if (m != null && m.NullOrBad()) return null;
            return m;
        }

        private void DisposeChunk(PlantChunk chunk)
        {
            if (chunk == null) return;
            for (int i = 0; i < chunk.batches.Count; i++) Util3D.SafeDestroy(chunk.batches[i].mesh);
            chunk.batches.Clear();
            for (int i = 0; i < chunk.modelBatches.Count; i++)
            {
                Util3D.SafeDestroy(chunk.modelBatches[i].body);
                Util3D.SafeDestroy(chunk.modelBatches[i].hull);
            }
            chunk.modelBatches.Clear();
            Util3D.SafeDestroy(chunk.shadowMesh);
            chunk.shadowMesh = null;
            chunk.plantCount = 0;
        }

        private void EnsureChunkRecords()
        {
            for (int cx = 0; cx < chunksX; cx++)
            {
                for (int cz = 0; cz < chunksZ; cz++)
                {
                    int key = cx * 10000 + cz;
                    if (!chunks.TryGetValue(key, out var ch))
                    {
                        ch = new PlantChunk();
                        chunks[key] = ch;
                    }
                    ch.dirty = true;
                }
            }
        }

        private void RebuildChunk(int cx, int cz, PlantChunk chunk)
        {
            DisposeChunk(chunk);
            chunk.dirty = false;
            chunk.colorDirty = false;
            int x0 = cx * ChunkSize;
            int z0 = cz * ChunkSize;
            int x1 = Mathf.Min(x0 + ChunkSize, map.Size.x);
            int z1 = Mathf.Min(z0 + ChunkSize, map.Size.z);
            if (x0 >= x1 || z0 >= z1) return;

            var starByMat = new Dictionary<Material, InstGroup>();
            var crossByMat = new Dictionary<Material, InstGroup>();
            var shadowList = new List<Matrix4x4>();
            var byArch = PlantModels3D.TintMat(0) != null
                ? new Dictionary<string, InstGroup>()
                : null;

            var st = FirstPerson3DMod.settings;
            float hScale = st != null ? st.plantScale : 1f;
            int count = 0;

            for (int z = z0; z < z1; z++)
            {
                for (int x = x0; x < x1; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    var plant = c.GetPlant(map);
                    if (plant == null || !plant.Spawned) continue;
                    count++;

                    try
                    {
                        var arch = PlantModels3D.ArchOf(plant);
                        var proto = arch != null ? PlantModels3D.Prototype(arch) : null;
                        if (proto != null && proto.body != null && byArch != null)
                        {
                            int h = plant.thingIDNumber;
                            int tint = ((h * 7919) % 3 + 3) % 3;
                            float yaw = ((h * 7919) % 360 + 360) % 360;
                            float scale = PlantModels3D.GrowthScale(plant.Growth) * Mathf.Max(0.05f, hScale);
                            var dp = plant.DrawPos;
                            var m = Matrix4x4.TRS(
                                new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z),
                                Quaternion.AngleAxis(yaw, Vector3.up),
                                new Vector3(scale, scale, scale));
                            if (!byArch.TryGetValue(arch, out var grp))
                            {
                                grp = new InstGroup();
                                byArch[arch] = grp;
                            }
                            grp.mats.Add(m);
                            grp.tints.Add((byte)tint);

                            var size = SizeOf(plant);
                            bool isTreeModel = plant.def.plant != null && plant.def.plant.IsTree;
                            if (isTreeModel)
                            {
                                float shRadius = Mathf.Clamp(size.x * 0.85f, 1.05f, 1.55f);
                                shadowList.Add(Matrix4x4.TRS(
                                    new Vector3(dp.x + worldOffset.x, baseY + 0.022f, dp.z + worldOffset.z),
                                    Quaternion.identity,
                                    new Vector3(shRadius, 1f, shRadius)));
                            }
                            continue;
                        }

                        var src = MatFor(plant);
                        if (src == null || src.mainTexture == null || src.NullOrBad() || src.mainTexture == BaseContent.BadTex) continue;
                        var plantDp = plant.DrawPos;
                        int tintIdx = (plant.thingIDNumber * 7919) % 3;
                        if (tintIdx < 0) tintIdx += 3;
                        var mat = TintOf(src, 0);
                        if (mat == null) continue;

                        var visualSize = SizeOf(plant);
                        if (visualSize.x <= 0.01f) continue;
                        bool isTree = plant.def.plant != null && plant.def.plant.IsTree;
                        float height = WorldHeightOf(plant);
                        float worldH = height > 0.01f ? height : 1f;

                        var pos = new Vector3(plantDp.x + worldOffset.x, baseY, plantDp.z + worldOffset.z);
                        if (isTree)
                        {
                            float shR = Mathf.Clamp(visualSize.x * 0.85f, 1.05f, 1.55f);
                            shadowList.Add(Matrix4x4.TRS(pos + new Vector3(0f, 0.022f, 0f), Quaternion.identity,
                                new Vector3(shR, 1f, shR)));
                        }

                        var target = isTree ? starByMat : crossByMat;
                        var pMatrix = Matrix4x4.TRS(pos, Quaternion.identity,
                            new Vector3(visualSize.x, Mathf.Max(0.05f, worldH * hScale), visualSize.x));
                        if (!target.TryGetValue(mat, out var pGrp))
                        {
                            pGrp = new InstGroup();
                            target[mat] = pGrp;
                        }
                        pGrp.mats.Add(pMatrix);
                        pGrp.tints.Add((byte)tintIdx);
                    }
                    catch { }
                }
            }

            chunk.plantCount = count;
            if (count > 0)
            {
                var lf = LF;
                foreach (var kv in starByMat)
                {
                    var tintsArr = kv.Value.tints.ToArray();
                    var mesh = Util3D.BuildInstancedMesh(Util3D.StarMesh, kv.Value.mats, lf, worldOffset, out var posArr, out var colBuf, 2.2f, tintsArr);
                    if (mesh != null) chunk.batches.Add(new Batch { mat = kv.Key, srcMesh = Util3D.StarMesh, mesh = mesh, positions = posArr, colors = colBuf, tints = tintsArr });
                }
                foreach (var kv in crossByMat)
                {
                    var tintsArr = kv.Value.tints.ToArray();
                    var mesh = Util3D.BuildInstancedMesh(Util3D.CrossMesh, kv.Value.mats, lf, worldOffset, out var posArr, out var colBuf, 1.45f, tintsArr);
                    if (mesh != null) chunk.batches.Add(new Batch { mat = kv.Key, srcMesh = Util3D.CrossMesh, mesh = mesh, positions = posArr, colors = colBuf, tints = tintsArr });
                }
                if (shadowList.Count > 0)
                    chunk.shadowMesh = Util3D.BuildInstancedMesh(Util3D.GroundQuad, shadowList);

                if (byArch != null)
                {
                    foreach (var kv in byArch)
                    {
                        string arch = kv.Key;
                        var proto = PlantModels3D.Prototype(arch);
                        if (proto == null || proto.body == null) continue;
                        float sampleOffY = PlantModels3D.IsTreeArch(arch) ? 2.2f : 1.45f;
                        var tintsArr = kv.Value.tints.ToArray();
                        var bodyMesh = Util3D.BuildInstancedMesh(proto.body, kv.Value.mats, lf, worldOffset, out var posArr, out var colBuf, sampleOffY, tintsArr);
                        var mb = new ModelBatch
                        {
                            srcBody = proto.body,
                            body = bodyMesh,
                            hull = Fps3DState.DrawOutlines ? Util3D.BuildInstancedMesh(proto.hull, kv.Value.mats) : null,
                            positions = posArr,
                            colors = colBuf,
                            tints = tintsArr,
                            tint = 0,
                            count = kv.Value.mats.Count
                        };
                        if (mb.body != null) chunk.modelBatches.Add(mb);
                    }
                }

                Vector3 center = new Vector3((x0 + x1) * 0.5f + worldOffset.x, baseY + 1.5f, (z0 + z1) * 0.5f + worldOffset.z);
                Vector3 size = new Vector3(x1 - x0, 5.0f, z1 - z0);
                chunk.bounds = new Bounds(center, size);
            }
        }

        private void RecolorChunk(PlantChunk chunk)
        {
            chunk.colorDirty = false;
            var lf = LF;
            for (int i = 0; i < chunk.batches.Count; i++)
            {
                var b = chunk.batches[i];
                Util3D.RecolorInstancedMesh(b.mesh, b.srcMesh, b.positions, b.colors, lf, b.tints);
            }
            for (int i = 0; i < chunk.modelBatches.Count; i++)
            {
                var mb = chunk.modelBatches[i];
                Util3D.RecolorInstancedMesh(mb.body, mb.srcBody, mb.positions, mb.colors, lf, mb.tints);
            }
        }

        // ---- 更新 ----

        public void Update()
        {
            var st = FirstPerson3DMod.settings;
            float hScale = st != null ? st.plantScale : 1f;
            if (Mathf.Abs(hScale - builtScale) > 0.0001f)
            {
                builtScale = hScale;
                allDirty = true;
            }

            if (allDirty)
            {
                EnsureChunkRecords();
                allDirty = false;
            }

            sortedDirtyKeys.Clear();
            colorDirtyKeys.Clear();
            foreach (var kv in chunks)
            {
                if (kv.Value.dirty) sortedDirtyKeys.Add(kv.Key);
                else if (kv.Value.colorDirty) colorDirtyKeys.Add(kv.Key);
            }

            if (sortedDirtyKeys.Count > 0 || colorDirtyKeys.Count > 0)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                if (sortedDirtyKeys.Count > 3)
                {
                    Vector3 camPos = CameraRig3D.LastCamPos - worldOffset;
                    float camX = camPos.x, camZ = camPos.z;
                    sortedDirtyKeys.Sort((a, b) =>
                    {
                        float ax = (a / 10000 + 0.5f) * ChunkSize - camX;
                        float az = (a % 10000 + 0.5f) * ChunkSize - camZ;
                        float bx = (b / 10000 + 0.5f) * ChunkSize - camX;
                        float bz = (b % 10000 + 0.5f) * ChunkSize - camZ;
                        return (ax * ax + az * az).CompareTo(bx * bx + bz * bz);
                    });
                }

                int budget = Fps3DState.ChunkRebuildBudget;
                for (int i = 0; i < sortedDirtyKeys.Count && budget > 0; i++)
                {
                    int k = sortedDirtyKeys[i];
                    if (chunks.TryGetValue(k, out var ch) && ch.dirty)
                    {
                        RebuildChunk(k / 10000, k % 10000, ch);
                        budget--;
                    }
                }
                // 时间预算见 Fps3DState.ChunkColorBudgetMs：太阳在动时光场每帧标脏的是
                // 全图扫描带，其中绝大多数块不在视野里 —— 屏幕外的直接不刷。
                float colorMs = Fps3DState.ChunkColorBudgetMs;
                for (int i = 0; i < colorDirtyKeys.Count && colorMs > 0f; i++)
                {
                    int k = colorDirtyKeys[i];
                    if (!chunks.TryGetValue(k, out var ch) || !ch.colorDirty || ch.dirty) continue;
                    if (!Frustum3D.Visible(ch.bounds)) continue;
                    double before = sw.Elapsed.TotalMilliseconds;
                    RecolorChunk(ch);
                    colorMs -= (float)(sw.Elapsed.TotalMilliseconds - before);
                }
                PerfCounters.LastPlantBuildMs = (float)sw.Elapsed.TotalMilliseconds;
            }
        }

        // ---- 绘制 ----

        public void Draw() => Draw(CameraRig3D.LastCamPos);

        public void Draw(Vector3 camPos)
        {
            int inst = 0;
            int batchesCount = 0;
            var shadowM = ShadowMat;
            Matrix4x4 shadowMatrix = Matrix4x4.identity;
            var env = Lighting3D.Current;
            if (shadowM != null)
                shadowM.color = new Color(0f, 0f, 0f, 0.20f * Mathf.Clamp01(env.glow));
            if (env.glow > 0.05f && env.sunDir.y > 0.05f)
            {
                float offScale = Mathf.Clamp(0.28f / Mathf.Max(0.25f, env.sunDir.y), 0.12f, 0.55f);
                shadowMatrix = Matrix4x4.Translate(new Vector3(-env.sunDir.x * offScale, 0f, -env.sunDir.z * offScale));
            }

            var modelBaseMat = PlantModels3D.TintMat(0);
            var outlineMat = Util3D.OutlineMat;
            bool allowOutline = Fps3DState.DrawOutlines && outlineMat != null;
            const float MaxPlantHullDistSq = 48f * 48f;

            foreach (var kv in chunks)
            {
                var ch = kv.Value;
                if (ch.plantCount == 0 || ch.batches.Count == 0 && ch.modelBatches.Count == 0) continue;

                if (!Frustum3D.Visible(ch.bounds)) continue;

                float dx = ch.bounds.center.x - camPos.x;
                float dz = ch.bounds.center.z - camPos.z;
                bool drawHullHere = allowOutline && (dx * dx + dz * dz <= MaxPlantHullDistSq);

                for (int i = 0; i < ch.batches.Count; i++)
                {
                    var b = ch.batches[i];
                    Graphics.DrawMesh(b.mesh, Matrix4x4.identity, b.mat, 0);
                    batchesCount++;
                }
                for (int i = 0; i < ch.modelBatches.Count; i++)
                {
                    var mb = ch.modelBatches[i];
                    Graphics.DrawMesh(mb.body, Matrix4x4.identity, modelBaseMat, 0);
                    if (drawHullHere && mb.hull != null)
                        Graphics.DrawMesh(mb.hull, Matrix4x4.identity, outlineMat, 0);
                    batchesCount++;
                    PerfCounters.PlantMeshesDrawn++;
                }
                if (ch.shadowMesh != null && shadowM != null)
                {
                    Graphics.DrawMesh(ch.shadowMesh, shadowMatrix, shadowM, 0);
                    batchesCount++;
                }
                inst += ch.plantCount;
            }

            PerfCounters.PlantInstances = inst;
            PerfCounters.PlantBatches += batchesCount;
        }

        public void Dispose()
        {
            foreach (var kv in chunks) DisposeChunk(kv.Value);
            chunks.Clear();
        }
    }
}