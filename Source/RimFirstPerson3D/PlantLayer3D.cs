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
    public class PlantLayer3D
    {
        private readonly Map map;
        public Vector3 worldOffset = Vector3.zero;
        private readonly float baseY;

        private class Entry
        {
            public Plant plant;
            public Material mat;      // 分档色调后的材质
            public Vector3 pos;
            public Vector2 lastVisualSize;
            public float height;      // 世界高度（米），按类别固定
            public bool tree;
        }

        private readonly List<Entry> entries = new List<Entry>();

        // 一个材质对应一个合并网格（所有同材质的实例烘焙进一份网格）。
        private class Batch
        {
            public Material mat;
            public Mesh mesh;
        }

        private readonly List<Batch> batches = new List<Batch>();
        private Mesh shadowMesh;

        private bool dirty = true;
        private bool batchesDirty = true;
        private float builtScale = -1f;
        private float growthPollTimer;

        private static Material shadowMat;
        private static readonly Dictionary<Material, Material[]> tintCache = new Dictionary<Material, Material[]>();

        public PlantLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty() => dirty = true;

        public void NotifyThingChanged(Thing t)
        {
            if (t != null && t.def != null && t.def.category == ThingCategory.Plant) dirty = true;
        }

        // ---- 共享材质 ----

        private static Material ShadowMat
        {
            get
            {
                if (shadowMat != null) return shadowMat;
                shadowMat = new Material(ShaderDatabase.Transparent)
                {
                    name = "Fps3D_PlantShadow",
                    mainTexture = ProcTex3D.Radial(32, 0.15f, 1.6f),
                    color = new Color(0f, 0f, 0f, 0.32f)
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

        // ---- 更新 ----

        public void Update()
        {
            if (dirty)
            {
                RebuildEntries();
                dirty = false;
                growthPollTimer = 0f;
            }
            else
            {
                growthPollTimer += Time.deltaTime;
                if (growthPollTimer > 2.5f)
                {
                    growthPollTimer = 0f;
                    if (PollGrowthChanged()) batchesDirty = true;
                }
            }
        }

        private void RebuildEntries()
        {
            entries.Clear();
            foreach (var t in map.listerThings.AllThings)
            {
                if (t == null || !t.Spawned) continue;
                if (t.def.category != ThingCategory.Plant) continue;
                var plant = t as Plant;
                if (plant == null) continue;
                try
                {
                    var src = MatFor(plant);
                    if (src == null || src.mainTexture == null) continue;
                    var dp = plant.DrawPos;
                    int tintIdx = (plant.thingIDNumber * 7919) % 3;
                    if (tintIdx < 0) tintIdx += 3;
                    entries.Add(new Entry
                    {
                        plant = plant,
                        mat = TintOf(src, tintIdx),
                        pos = new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z),
                        lastVisualSize = SizeOf(plant),
                        height = WorldHeightOf(plant),
                        tree = plant.def.plant != null && plant.def.plant.IsTree
                    });
                }
                catch { }
            }
            batchesDirty = true;
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
            if (m == null) { try { m = g.MatSingle; } catch { } }
            return m;
        }

        private bool PollGrowthChanged()
        {
            bool changed = false;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var plant = e.plant;
                if (plant == null || plant.Destroyed || !plant.Spawned) { dirty = true; return false; }
                var size = SizeOf(plant);
                if ((size - e.lastVisualSize).sqrMagnitude > 0.0036f)
                {
                    e.lastVisualSize = size;
                    changed = true;
                }
            }
            return changed;
        }

        // ---- 合并网格重建（脏时一次性完成）----

        private void DestroyBatches()
        {
            for (int i = 0; i < batches.Count; i++) Util3D.SafeDestroy(batches[i].mesh);
            batches.Clear();
            Util3D.SafeDestroy(shadowMesh);
            shadowMesh = null;
        }

        private void RebuildBatches()
        {
            DestroyBatches();
            batchesDirty = false;
            var st = FirstPerson3DMod.settings;
            float hScale = st != null ? st.plantScale : 1f;
            builtScale = hScale;

            // 树与小株按"材质 + 造型"分别烘焙：树用三片星形（体积感），小株用十字交叉。
            var starByMat = new Dictionary<Material, List<Matrix4x4>>();
            var crossByMat = new Dictionary<Material, List<Matrix4x4>>();
            var shadowList = new List<Matrix4x4>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var size = e.lastVisualSize;
                if (size.x <= 0.01f) continue;
                if (e.tree)
                    shadowList.Add(Matrix4x4.TRS(e.pos + new Vector3(0f, 0.015f, 0f), Quaternion.identity,
                        new Vector3(size.x * 0.9f, 1f, size.x * 0.9f)));
                // 宽 = 原版冠幅 drawSize.x；高 = 类别约定的世界高度（米）× plantScale。
                float worldH = e.height > 0.01f ? e.height : 1f;
                var target = e.tree ? starByMat : crossByMat;
                var m = Matrix4x4.TRS(e.pos, Quaternion.identity,
                    new Vector3(size.x, Mathf.Max(0.05f, worldH * hScale), size.x));
                if (!target.TryGetValue(e.mat, out var list))
                {
                    list = new List<Matrix4x4>(256);
                    target[e.mat] = list;
                }
                list.Add(m);
            }
            foreach (var kv in starByMat)
            {
                var mesh = Util3D.BuildInstancedMesh(Util3D.StarMesh, kv.Value);
                if (mesh != null) batches.Add(new Batch { mat = kv.Key, mesh = mesh });
            }
            foreach (var kv in crossByMat)
            {
                var mesh = Util3D.BuildInstancedMesh(Util3D.CrossMesh, kv.Value);
                if (mesh != null) batches.Add(new Batch { mat = kv.Key, mesh = mesh });
            }
            shadowMesh = Util3D.BuildInstancedMesh(Util3D.GroundQuad, shadowList);
            PerfCounters.LastPlantBuildMs = 0f;
        }

        // ---- 绘制 ----

        public void Draw() => Draw(CameraRig3D.LastCamPos);

        public void Draw(Vector3 camPos)
        {
            if (entries.Count == 0) return;
            var st = FirstPerson3DMod.settings;
            float hScale = st != null ? st.plantScale : 1f;
            if (batchesDirty || Mathf.Abs(hScale - builtScale) > 0.0001f) RebuildBatches();

            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                Graphics.DrawMesh(b.mesh, Matrix4x4.identity, b.mat, 0);
                PerfCounters.PlantBatches++;
            }
            PerfCounters.PlantInstances += entries.Count;

            if (shadowMesh != null)
            {
                Graphics.DrawMesh(shadowMesh, Matrix4x4.identity, ShadowMat, 0);
                PerfCounters.PlantBatches++;
            }
        }

        public void Dispose()
        {
            DestroyBatches();
            entries.Clear();
        }
    }
}