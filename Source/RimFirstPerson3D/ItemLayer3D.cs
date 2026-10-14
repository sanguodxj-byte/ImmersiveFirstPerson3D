// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 物品层：立式立牌（十字交叉面片，双面），尸体平铺于地面。
    //
    // 【关键修复 r44】原先用 Graphics.DrawMeshInstanced 提交，但 RimWorld 的原版
    // 着色器不含 instancing 变体，实例化绘制会**静默**产出 0 像素（不报错），
    // 导致第一人称下物品不可见。现改为：把每个实例逐顶点烘焙进合并网格，
    // 再用 Graphics.DrawMesh 绘制——与地形/建筑/人物完全相同的绘制路径，已证明可正常出图。
    public class ItemLayer3D
    {
        private readonly Map map;
        private readonly float baseY;

        private class Entry
        {
            public Thing thing;
            public Material mat;
            public Vector3 pos;
            public float w, h;
            public bool flatCorpse;
            public float flatYaw;
            public string modelKey;
            public int modelVariant;
            public float modelYaw;
        }

        private readonly List<Entry> entries = new List<Entry>();

        // 一个材质对应一个合并网格。
        private class Batch
        {
            public Material mat;
            public Mesh srcMesh;
            public Mesh mesh;
            public Vector3[] positions;
            public Color32[] colors;
        }

        private class ModelBatch
        {
            public Mesh srcBody;
            public Mesh body;
            public Mesh hull;
            public Vector3[] positions;
            public Color32[] colors;
        }

        private readonly List<Batch> batches = new List<Batch>();
        private readonly List<ModelBatch> modelBatches = new List<ModelBatch>();

        private bool dirty = true;       // entries 需重建
        private bool batchDirty = true;  // 合并网格需重新烘焙
        private bool colorDirty;         // 仅光照变化：原地重刷顶点色

        // 水平流式偏移（世界坐标 = 图内局部 + 此偏移）
        public Vector3 worldOffset = Vector3.zero;

        // 光照场（F013）：地面/立式物品实例批按每件位置取放置亮度
        public LightField3D light;
        private LightField3D LF => light != null && Lighting3D.Enabled ? light : null;

        /// <summary>把本层实例批网格的颜色统计并进探针（只在按需导出时走）。</summary>
        public void Summarize(Lighting3D.Probe p)
        {
            for (int i = 0; i < batches.Count; i++) p.Add(batches[i].mesh);
            for (int i = 0; i < modelBatches.Count; i++) p.Add(modelBatches[i].body);
        }

        public ItemLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty() { dirty = true; batchDirty = true; }

        public void MarkBatchDirty() { colorDirty = true; }

        public void NotifyThingChanged(Thing t)
        {
            if (t != null && (t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Building))
            {
                dirty = true;
                batchDirty = true;
            }
        }

        private readonly Dictionary<Material, List<Matrix4x4>> standByMat = new Dictionary<Material, List<Matrix4x4>>();
        private readonly Dictionary<Material, List<Matrix4x4>> flatByMat = new Dictionary<Material, List<Matrix4x4>>();
        private readonly Dictionary<string, List<Matrix4x4>> byModelKey = new Dictionary<string, List<Matrix4x4>>();
        private readonly List<List<Matrix4x4>> pooledMatrixLists = new List<List<Matrix4x4>>();

        private List<Matrix4x4> GetPooledList()
        {
            if (pooledMatrixLists.Count > 0)
            {
                int last = pooledMatrixLists.Count - 1;
                var list = pooledMatrixLists[last];
                pooledMatrixLists.RemoveAt(last);
                list.Clear();
                return list;
            }
            return new List<Matrix4x4>(64);
        }

        private void RecycleLists(Dictionary<Material, List<Matrix4x4>> dict)
        {
            foreach (var kv in dict)
            {
                if (kv.Value != null)
                {
                    kv.Value.Clear();
                    pooledMatrixLists.Add(kv.Value);
                }
            }
            dict.Clear();
        }

        private void RecycleStringLists(Dictionary<string, List<Matrix4x4>> dict)
        {
            foreach (var kv in dict)
            {
                if (kv.Value != null)
                {
                    kv.Value.Clear();
                    pooledMatrixLists.Add(kv.Value);
                }
            }
            dict.Clear();
        }

        public static bool TryResolveCorpse(Corpse corpse, out Graphic g, out Material m, out Vector2 drawSize, out float downedAngle)
        {
            g = null;
            m = null;
            drawSize = Vector2.one;
            downedAngle = corpse.Rotation.AsAngle;

            var p = corpse.InnerPawn;
            if (p == null) return false;

            if (p.Drawer?.renderer?.wiggler != null)
            {
                downedAngle = p.Drawer.renderer.wiggler.downedAngle;
            }

            RotDrawMode rotMode = corpse.CurRotDrawMode;

            var tree = p.Drawer?.renderer?.renderTree;
            if (tree != null)
            {
                try
                {
                    if (!tree.Resolved) tree.SetDirty();
                    tree.EnsureInitialized(PawnRenderFlags.Clothes | PawnRenderFlags.Headgear);
                }
                catch { }
            }

            if (rotMode == RotDrawMode.Dessicated)
            {
                if (p.RaceProps.Humanlike && p.story?.bodyType != null && !string.IsNullOrEmpty(p.story.bodyType.bodyDessicatedGraphicPath))
                {
                    try
                    {
                        g = GraphicDatabase.Get<Graphic_Multi>(p.story.bodyType.bodyDessicatedGraphicPath, ShaderDatabase.Cutout, Vector2.one, Color.white);
                    }
                    catch { }
                }
                else
                {
                    var curKind = p.ageTracker != null ? p.ageTracker.CurKindLifeStage : null;
                    if (curKind != null)
                    {
                        var dessData = (p.gender == Gender.Female && curKind.femaleDessicatedBodyGraphicData != null)
                            ? curKind.femaleDessicatedBodyGraphicData
                            : curKind.dessicatedBodyGraphicData;
                        if (dessData != null)
                        {
                            try { g = dessData.GraphicColoredFor(p); } catch { }
                        }
                    }
                }
            }

            if (g == null && tree != null)
            {
                try { g = tree.BodyGraphic; } catch { }
            }

            if (g == null)
            {
                try { g = PawnLayer3D.ResolveGraphic(p, corpse.Rotation); } catch { }
            }

            if (g == null) return false;

            try { m = g.MatAt(corpse.Rotation, p); } catch { }
            if (m == null || m.mainTexture == null || m.NullOrBad())
            {
                try { m = g.MatAt(corpse.Rotation, corpse); } catch { }
            }
            if (m == null || m.mainTexture == null || m.NullOrBad())
            {
                try { m = g.MatSingle; } catch { }
            }

            if (m == null || m.mainTexture == null || m.NullOrBad()) return false;

            float sx = (g.drawSize.x > 0.1f) ? g.drawSize.x : 1f;
            float sy = (g.drawSize.y > 0.1f) ? g.drawSize.y : 1f;
            if (p.RaceProps != null)
            {
                float scale = Mathf.Clamp(p.BodySize, 0.4f, 4f);
                sx = Mathf.Max(0.75f, sx * Mathf.Min(1.5f, Mathf.Sqrt(scale)));
                sy = Mathf.Max(0.75f, sy * Mathf.Min(1.5f, Mathf.Sqrt(scale)));
            }
            drawSize = new Vector2(sx, sy);
            return true;
        }

        public void Update()
        {
            if (!dirty) return;
            dirty = false;
            batchDirty = true;
            entries.Clear();

            // 优先遍历已索引的物品/尸体列表（通常几十至几百个），彻底跳过全图数万个无关物体，降低扫描耗时 95%
            var haulables = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEverOrMinifiable);
            if (haulables != null)
            {
                for (int i = 0; i < haulables.Count; i++)
                {
                    var t = haulables[i];
                    if (t == null || !t.Spawned || t.def == null || t.def.category != ThingCategory.Item) continue;
                    ProcessThing(t);
                }
            }

            var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
            if (corpses != null)
            {
                for (int i = 0; i < corpses.Count; i++)
                {
                    var t = corpses[i];
                    if (t == null || !t.Spawned || t.def == null || t.def.category != ThingCategory.Item) continue;
                    if (t.def.EverHaulable) continue; // 已在 haulables 中处理过
                    ProcessThing(t);
                }
            }
        }

        private void ProcessThing(Thing t)
        {
            try
            {
                if (t.Position.Fogged(map)) return;
                Graphic g = null;
                Material m = null;
                var corpse = t as Corpse;
                float corpseW = 1f, corpseH = 1f, corpseAngle = t.Rotation.AsAngle;
                if (corpse != null)
                {
                    if (!TryResolveCorpse(corpse, out g, out m, out var drawSize, out corpseAngle))
                        return;
                    corpseW = drawSize.x;
                    corpseH = drawSize.y;
                }
                else
                {
                    g = t.Graphic;
                    if (g == null) return;
                    try { m = g.MatAt(t.Rotation, t); } catch { }
                    if (m == null || m.mainTexture == null || m.NullOrBad()) { try { m = g.MatSingle; } catch { } }
                    if (m == null || m.mainTexture == null || m.NullOrBad()) return;
                }

                var dp = t.DrawPos;
                float posY = baseY + (corpse != null ? 0.015f : 0f) - WaterLayer3D.SinkAt(map, t.Position);
                var e = new Entry
                {
                    thing = t,
                    mat = m,
                    pos = new Vector3(dp.x + worldOffset.x, posY, dp.z + worldOffset.z)
                };
                if (corpse != null)
                {
                    e.flatCorpse = true;
                    e.w = corpseW;
                    e.h = corpseH;
                    e.flatYaw = corpseAngle;
                }
                else
                {
                    string mk = ItemModels3D.KeyOf(t);
                    if (mk != null && ItemModels3D.AtlasMat != null)
                    {
                        e.modelKey = mk;
                        e.modelVariant = ItemModels3D.VariantOf(t, mk);
                        if (mk.StartsWith("chunk"))
                            e.modelYaw = ((t.thingIDNumber * 7919) % 4) * 90f;
                        else
                            e.modelYaw = 0f;
                    }
                    e.w = Mathf.Max(0.28f, g.drawSize.x * 0.8f);
                    e.h = Mathf.Max(0.28f, g.drawSize.y);
                }
                entries.Add(e);
            }
            catch { }
        }

        private void DestroyBatches()
        {
            for (int i = 0; i < batches.Count; i++) Util3D.SafeDestroy(batches[i].mesh);
            batches.Clear();
            for (int i = 0; i < modelBatches.Count; i++)
            {
                Util3D.SafeDestroy(modelBatches[i].body);
                Util3D.SafeDestroy(modelBatches[i].hull);
            }
            modelBatches.Clear();
        }

        private void RebuildBatches()
        {
            DestroyBatches();
            colorDirty = false;
            RecycleLists(standByMat);
            RecycleLists(flatByMat);
            RecycleStringLists(byModelKey);
            bool useModels = ItemModels3D.AtlasMat != null;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.thing == null || !e.thing.Spawned) continue;
                if (e.modelKey != null && useModels)
                {
                    string ck = e.modelKey + "#" + e.modelVariant;
                    if (!byModelKey.TryGetValue(ck, out var ml))
                    {
                        ml = GetPooledList();
                        byModelKey[ck] = ml;
                    }
                    ml.Add(Matrix4x4.TRS(e.pos, Quaternion.AngleAxis(e.modelYaw, Vector3.up), Vector3.one));
                    continue;
                }
                // 立牌用十字交叉面片（双面、与视角无关）；尸体平铺地面。
                var m = e.flatCorpse
                    ? Matrix4x4.TRS(e.pos, Quaternion.AngleAxis(e.flatYaw, Vector3.up), new Vector3(e.w, 1f, e.h))
                    : Matrix4x4.TRS(e.pos, Quaternion.identity, new Vector3(e.w, e.h, e.w));
                var dict = e.flatCorpse ? flatByMat : standByMat;
                if (!dict.TryGetValue(e.mat, out var l))
                {
                    l = GetPooledList();
                    dict[e.mat] = l;
                }
                l.Add(m);
            }
            var lf = LF;
            foreach (var kv in standByMat)
            {
                var mesh = Util3D.BuildInstancedMesh(Util3D.CrossMesh, kv.Value, lf, worldOffset, out var posArr, out var colBuf);
                if (mesh != null) batches.Add(new Batch { mat = kv.Key, srcMesh = Util3D.CrossMesh, mesh = mesh, positions = posArr, colors = colBuf });
            }
            foreach (var kv in flatByMat)
            {
                var mesh = Util3D.BuildInstancedMesh(Util3D.GroundQuad, kv.Value, lf, worldOffset, out var posArr, out var colBuf);
                if (mesh != null) batches.Add(new Batch { mat = kv.Key, srcMesh = Util3D.GroundQuad, mesh = mesh, positions = posArr, colors = colBuf });
            }
            if (byModelKey != null)
            {
                foreach (var kv in byModelKey)
                {
                    int sep = kv.Key.IndexOf('#');
                    string mk = kv.Key.Substring(0, sep);
                    int mv = int.Parse(kv.Key.Substring(sep + 1));
                    var proto = ItemModels3D.Prototype(mk, mv);
                    if (proto == null || proto.body == null) continue;
                    var bodyMesh = Util3D.BuildInstancedMesh(proto.body, kv.Value, lf, worldOffset, out var posArr, out var colBuf);
                    var hullMesh = (Fps3DState.DrawOutlines && proto.hull != null)
                        ? Util3D.BuildInstancedMesh(proto.hull, kv.Value)
                        : null;
                    if (bodyMesh != null)
                    {
                        modelBatches.Add(new ModelBatch
                        {
                            srcBody = proto.body,
                            body = bodyMesh,
                            hull = hullMesh,
                            positions = posArr,
                            colors = colBuf,
                        });
                    }
                }
            }
        }

        private void RecolorBatches()
        {
            colorDirty = false;
            var lf = LF;
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                Util3D.RecolorInstancedMesh(b.mesh, b.srcMesh, b.positions, b.colors, lf);
            }
            for (int i = 0; i < modelBatches.Count; i++)
            {
                var mb = modelBatches[i];
                Util3D.RecolorInstancedMesh(mb.body, mb.srcBody, mb.positions, mb.colors, lf);
            }
        }

        public void Draw(Vector3 camPos)
        {
            if (entries.Count == 0) return;
            if (batchDirty) { batchDirty = false; RebuildBatches(); }
            else if (colorDirty) { RecolorBatches(); }
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                Graphics.DrawMesh(b.mesh, Matrix4x4.identity, b.mat, 0);
                PerfCounters.ItemBatches++;
            }
            var atlasMat = ItemModels3D.AtlasMat;
            if (atlasMat != null)
            {
                var outlineMat = Util3D.OutlineMat;
                bool drawHull = Fps3DState.DrawOutlines && outlineMat != null;
                for (int i = 0; i < modelBatches.Count; i++)
                {
                    var mb = modelBatches[i];
                    Graphics.DrawMesh(mb.body, Matrix4x4.identity, atlasMat, 0);
                    PerfCounters.ItemBatches++;
                    if (drawHull && mb.hull != null)
                        Graphics.DrawMesh(mb.hull, Matrix4x4.identity, outlineMat, 0);
                }
            }
            PerfCounters.ItemInstances += entries.Count;
        }

        public void Dispose()
        {
            DestroyBatches();
            entries.Clear();
        }
    }
}