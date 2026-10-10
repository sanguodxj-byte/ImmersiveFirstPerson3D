// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 草地细节层：相机周围的地被草丛
    // ============================================================
    //
    // 原版地表只是一张平铺贴图，第一人称贴地看时寸草不生。本层在相机周围
    // 半径 R 内，于天然肥沃地表格上按哈希散布 1~3 丛草，带三档色差。
    // 位置/缩放/色档全由格坐标哈希决定，相机移动时不会"闪"。
    //
    // 【关键修复 r44】原先用 Graphics.DrawMeshInstanced 提交，但 RimWorld 的原版
    // 着色器不含 instancing 变体，实例化绘制会**静默**产出 0 像素（不报错），
    // 导致第一人称下看不到任何草。现改为：把每个实例逐顶点烘焙进合并网格，
    // 再用 Graphics.DrawMesh 绘制——与地形/建筑/人物完全相同的绘制路径，已证明可正常出图。
    // 造型改用十字交叉面片（双面），因此与视角无关、无需逐帧朝相机。
    //
    // 摇摆：材质改用原版 materials/map/plantwindsway 着色器（GPU 顶点动画，由 WindManager
    // 每帧驱动），草叶同样随风摆动；十字片本身与视角无关，无需朝相机。
    public class GrassLayer3D
    {
        private readonly Map map;
        private readonly float baseY;
        public Vector3 worldOffset = Vector3.zero;

        private struct Tuft
        {
            public Vector3 pos;
            public float w, h;
            public byte tint;
        }

        private readonly List<Tuft> tufts = new List<Tuft>(8192);
        private IntVec3 lastCenter = IntVec3.Invalid;
        private float rescanTimer;
        private bool dirty = true;
        private bool meshDirty = true;

        private static Material[] mats;
        private readonly Mesh[] grassMeshes = new Mesh[3];

        public GrassLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty() { dirty = true; meshDirty = true; }
        public void MarkCellDirty(IntVec3 c) { dirty = true; meshDirty = true; }

        private static bool Enabled
        {
            get { var s = FirstPerson3DMod.settings; return s == null || s.drawGrass; }
        }

        private static float Radius
        {
            get { var s = FirstPerson3DMod.settings; return s != null ? Mathf.Clamp(s.grassRadius, 6f, 60f) : 28f; }
        }

        // 重建步长（格）：相机跨越这么多格才重扫/重新烘焙，避免每走 1 米就重建整片合并网格。
        // 半径远大于步长，因此草不会在视野边缘出现"空洞"。
        private static int RescanStep
        {
            get { return Mathf.Max(3, Mathf.RoundToInt(Radius / 7f)); }
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
                new Color(0.84f, 0.90f, 0.70f, 1f),
                new Color(1.00f, 1.00f, 1.00f, 1f),
                new Color(0.70f, 0.80f, 0.58f, 1f)
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
            // 任何建筑物（含家具）所在格不长草，否则草会从床、桌子里戳出来；
            // 有屋顶的格也不长（室内裸土地面不该是草地）。
            if (c.GetEdifice(map) != null) return false;
            try { if (c.Roofed(map)) return false; } catch { }
            try { if (map.snowGrid != null && map.snowGrid.GetDepth(c) > 0.3f) return false; } catch { }
            if (c.Fogged(map)) return false;
            fertility = td.fertility;
            return true;
        }

        private void Rescan(IntVec3 center)
        {
            tufts.Clear();
            int r = Mathf.CeilToInt(Radius);
            float r2 = Radius * Radius;
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dz * dz > r2) continue;
                    var c = new IntVec3(center.x + dx, 0, center.z + dz);
                    if (!c.InBounds(map)) continue;
                    if (!Eligible(c, out float fert)) continue;
                    int n = fert >= 1.0f ? 3 : fert >= 0.7f ? 2 : 1;
                    for (int k = 0; k < n; k++)
                    {
                        uint h = Hash(c.x, c.z, k);
                        float ox = 0.12f + ((h & 0xFF) / 255f) * 0.76f;
                        float oz = 0.12f + (((h >> 8) & 0xFF) / 255f) * 0.76f;
                        float sc = 0.75f + (((h >> 16) & 0xFF) / 255f) * 0.55f;
                        tufts.Add(new Tuft
                        {
                            pos = new Vector3(c.x + ox + worldOffset.x, baseY, c.z + oz + worldOffset.z),
                            w = 0.42f * sc,
                            // 世界高度约定：各种草 0.5m（不再随哈希/肥力随机，保证第一人称空间感一致）。
                            h = 0.5f,
                            tint = (byte)((h >> 5) % 3)
                        });
                    }
                }
        }

        private static uint Hash(int x, int z, int k)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(k * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        // 把当前全部草丛按色档烘焙成 3 张合并网格（脏时一次性完成）。
        private void RebuildMeshes()
        {
            meshDirty = false;
            for (int i = 0; i < 3; i++) { Util3D.SafeDestroy(grassMeshes[i]); grassMeshes[i] = null; }
            var byTint = new List<Matrix4x4>[3];
            for (int i = 0; i < 3; i++) byTint[i] = new List<Matrix4x4>(tufts.Count / 3 + 16);
            for (int i = 0; i < tufts.Count; i++)
            {
                var tf = tufts[i];
                int b = tf.tint; if (b < 0 || b > 2) b = 1;
                byTint[b].Add(Matrix4x4.TRS(tf.pos, Quaternion.identity, new Vector3(tf.w, tf.h, tf.w)));
            }
            for (int i = 0; i < 3; i++)
                grassMeshes[i] = Util3D.BuildInstancedMesh(Util3D.CrossMesh, byTint[i]);
        }

        public void Draw(Vector3 camPos)
        {
            if (!Enabled || !Fps3DState.DrawPlants) return;
            EnsureMaterials();
            var local = camPos - worldOffset;
            var center = new IntVec3(Mathf.FloorToInt(local.x), 0, Mathf.FloorToInt(local.z));
            rescanTimer += Time.deltaTime;
            bool moved = !lastCenter.IsValid
                || Mathf.Abs(center.x - lastCenter.x) >= RescanStep
                || Mathf.Abs(center.z - lastCenter.z) >= RescanStep;
            if (dirty || moved || rescanTimer > 2.5f)
            {
                dirty = false;
                rescanTimer = 0f;
                lastCenter = center;
                Rescan(center);
                meshDirty = true;
            }
            if (tufts.Count == 0) return;
            if (meshDirty) RebuildMeshes();

            for (int i = 0; i < 3; i++)
            {
                var mesh = grassMeshes[i];
                if (mesh == null) continue;
                Graphics.DrawMesh(mesh, Matrix4x4.identity, mats[i], 0);
            }
            PerfCounters.GrassInstances += tufts.Count;
        }

        public void Dispose()
        {
            for (int i = 0; i < 3; i++) { Util3D.SafeDestroy(grassMeshes[i]); grassMeshes[i] = null; }
            tufts.Clear();
        }
    }
}