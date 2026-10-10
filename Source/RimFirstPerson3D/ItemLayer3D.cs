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
        }

        private readonly List<Entry> entries = new List<Entry>();

        // 一个材质对应一个合并网格。
        private class Batch
        {
            public Material mat;
            public Mesh mesh;
        }

        private readonly List<Batch> batches = new List<Batch>();

        private bool dirty = true;       // entries 需重建
        private bool batchDirty = true;  // 合并网格需重新烘焙

        // 水平流式偏移（世界坐标 = 图内局部 + 此偏移）
        public Vector3 worldOffset = Vector3.zero;

        public ItemLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty() { dirty = true; batchDirty = true; }

        public void NotifyThingChanged(Thing t)
        {
            if (t != null && t.def.category == ThingCategory.Item) { dirty = true; batchDirty = true; }
        }

        public void Update()
        {
            if (!dirty) return;
            dirty = false;
            batchDirty = true;
            entries.Clear();
            foreach (var t in map.listerThings.AllThings)
            {
                if (t == null || !t.Spawned) continue;
                if (t.def.category != ThingCategory.Item) continue;
                try
                {
                    var g = t.Graphic;
                    if (g == null) continue;
                    Material m = null;
                    try { m = g.MatAt(t.Rotation, t); } catch { }
                    if (m == null || m.mainTexture == null) { try { m = g.MatSingle; } catch { } }
                    if (m == null || m.mainTexture == null) continue;
                    var dp = t.DrawPos;
                    var e = new Entry
                    {
                        thing = t,
                        mat = m,
                        pos = new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z)
                    };
                    var corpse = t as Corpse;
                    if (corpse != null)
                    {
                        e.flatCorpse = true;
                        e.w = Mathf.Max(0.7f, g.drawSize.x);
                        e.h = Mathf.Max(0.7f, g.drawSize.y);
                        e.flatYaw = t.Rotation.AsAngle;
                    }
                    else
                    {
                        e.w = Mathf.Max(0.28f, g.drawSize.x * 0.8f);
                        e.h = Mathf.Max(0.28f, g.drawSize.y);
                    }
                    entries.Add(e);
                }
                catch { }
            }
        }

        private void DestroyBatches()
        {
            for (int i = 0; i < batches.Count; i++) Util3D.SafeDestroy(batches[i].mesh);
            batches.Clear();
        }

        private void RebuildBatches()
        {
            DestroyBatches();
            var standByMat = new Dictionary<Material, List<Matrix4x4>>();
            var flatByMat = new Dictionary<Material, List<Matrix4x4>>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.thing == null || !e.thing.Spawned) continue;
                // 立牌用十字交叉面片（双面、与视角无关）；尸体平铺地面。
                var m = e.flatCorpse
                    ? Matrix4x4.TRS(e.pos, Quaternion.AngleAxis(e.flatYaw, Vector3.up), new Vector3(e.w, 1f, e.h))
                    : Matrix4x4.TRS(e.pos, Quaternion.identity, new Vector3(e.w, e.h, e.w));
                var dict = e.flatCorpse ? flatByMat : standByMat;
                if (!dict.TryGetValue(e.mat, out var l)) { l = new List<Matrix4x4>(64); dict[e.mat] = l; }
                l.Add(m);
            }
            foreach (var kv in standByMat)
            {
                var mesh = Util3D.BuildInstancedMesh(Util3D.CrossMesh, kv.Value);
                if (mesh != null) batches.Add(new Batch { mat = kv.Key, mesh = mesh });
            }
            foreach (var kv in flatByMat)
            {
                var mesh = Util3D.BuildInstancedMesh(Util3D.GroundQuad, kv.Value);
                if (mesh != null) batches.Add(new Batch { mat = kv.Key, mesh = mesh });
            }
        }

        public void Draw(Vector3 camPos)
        {
            if (entries.Count == 0) return;
            if (batchDirty) { batchDirty = false; RebuildBatches(); }
            for (int i = 0; i < batches.Count; i++)
            {
                var b = batches[i];
                Graphics.DrawMesh(b.mesh, Matrix4x4.identity, b.mat, 0);
                PerfCounters.ItemBatches++;
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