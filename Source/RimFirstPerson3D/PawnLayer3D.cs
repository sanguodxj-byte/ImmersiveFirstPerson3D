// AI生成
using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace RimFirstPerson3D
{
    // Pawn 层：立式立牌，始终面向相机。
    // 复用原版渲染树：rootNode.AppendRequests 拿到 paint 顺序的绘制请求
    // （含各层材质、头部偏移、缩放、朝向的完整矩阵），再把平面画布立起来。
    public class PawnLayer3D
    {
        private readonly Map map;
        private readonly float baseY;

        // 水平流式偏移（世界坐标 = 图内局部 + 此偏移）
        public Vector3 worldOffset = Vector3.zero;

        // 光照场（F013）：立牌用实例材质乘子受光，按脚下格取样。
        public LightField3D light;

        public PawnLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty() { }

        public void Update() { }

        /// <summary>
        /// 立牌底边的 Y 坐标：站立在地面，躺睡在床上。
        ///
        /// 原版没有"睡者高度"概念（PawnRenderer.cs:414 的床偏移全在 XZ 平面），
        /// 躺睡 sprite 直接画在地面 altitude 层，所以 3D 化时必须自己定。
        /// 高度依据见 Metrics3D.PawnLyingSurface。
        /// </summary>
        private float LyingYFor(Pawn pawn, float groundY)
        {
            if (pawn == null || pawn.Destroyed) return groundY;
            try
            {
                var bed = pawn.CurrentBed();
                if (bed == null) return groundY;

                // 床卷铺在地上，没有床架；原版 bed_showSleeperBody=false 也不画睡者。
                if (bed.def.defName == "Bedroll" || bed.def.defName == "DoubleBedroll")
                    return groundY + Metrics3D.BedrollLyingSurface;

                return groundY + Metrics3D.PawnLyingSurface;
            }
            catch
            {
                return groundY;
            }
        }

        public void Draw(Vector3 camPos)
        {
            var quad = Util3D.BillboardQuad;
            var pawns = map.mapPawns.AllPawnsSpawned;
            if (pawns == null) return;
            for (int i = 0; i < pawns.Count; i++)
            {
                try { DrawPawn(pawns[i], camPos, quad); }
                catch { }
            }
        }

        private void DrawPawn(Pawn pawn, Vector3 camPos, Mesh quad)
        {
            if (pawn == null || pawn.Destroyed) return;
            if (pawn.CarriedBy != null) return;
            // 动物（非人形、非机械族）改走 Animal3D 程序化模型，由 AnimalLayer3D 绘制
            if (Animal3D.HandlesPawn(pawn)) return;
            var cell = pawn.Position;
            if (cell.Fogged(map)) return;

            // DrawPos.y 是原版绘制海拔层（Pawn 层=23 → y≈8.4 悬空），不是真实高度：
            // 只取 x/z，立牌底边锚定地面 baseY；水平加流式偏移。
            // 例外：躺睡的 Pawn 不在地面上 —— 要抬到床面上，见 LyingYFor。
            var dp = pawn.DrawPos;
            float px = dp.x + worldOffset.x;
            float pz = dp.z + worldOffset.z;
            // 附身者的绘制 XZ 由连续位移控制器接管（脚点），其他 pawn 仍用 DrawPos。
            var xz = CameraRig3D.PawnDrawXZ(pawn);
            if (xz.HasValue) { px = xz.Value.x; pz = xz.Value.y; }
            // 身处水体则整具下沉（水面落差 + 水深），脚踩河床而非浮在水面上方。
            float sink = WaterLayer3D.SinkAt(map, cell);
            var pos = new Vector3(px, LyingYFor(pawn, baseY) - sink, pz);
            if (!Frustum3D.VisiblePoint(pos, 2.5f)) return;

            var tree = pawn.Drawer.renderer.renderTree;
            if (tree == null) return;
            try
            {
                if (!tree.Resolved) tree.SetDirty();
                tree.EnsureInitialized(PawnRenderFlags.Clothes | PawnRenderFlags.Headgear);
            }
            catch { }
            if (tree.rootNode == null) return;

            // 放置亮度按 pawn 脚下格中心取（图内局部坐标，与光照场同一系），
            // 不受附身者连续位移与流式偏移影响。
            var lit = Lighting3D.InstanceLevel(pawn, light,
                new Vector3(cell.x + 0.5f, baseY, cell.z + 0.5f));
            // 受击红闪强度（F014）：0 = 没在闪。整只 pawn 一个值，走三条绘制通路都不再各算各的。
            float hurt = HitFx3D.Strength(pawn);

            // 立牌朝向相机的偏航
            float yaw = Mathf.Atan2(camPos.x - pos.x, camPos.z - pos.z) * Mathf.Rad2Deg;
            // 相对相机视角选择四向贴图
            float camDirAngle = Mathf.Atan2(camPos.x - pos.x, -(camPos.z - pos.z)) * Mathf.Rad2Deg;
            float relAngle = pawn.Rotation.AsAngle - camDirAngle;
            Rot4 viewRot = SnapRot4(relAngle);

            var st = FirstPerson3DMod.settings;
            float H = st != null ? st.pawnHeight : Metrics3D.PawnHeight;
            float W = H * (st != null ? st.pawnWidthScale : 0.72f);
            float headY = H * (st != null ? st.headYFrac : 0.78f);
            float headS = H * (st != null ? st.headScale : 0.34f);
            int depth = 0;

            if (pawn.RaceProps.Humanlike)
            {
                // 首选：原版远缩放 blit 路线——整只 pawn（含服装头部）烘进图集页，
                // mesh 自带图集 UV，材质用图集贴图。1.6 把 pawn 图形贴图重映射进
                // 全局图集后，自建 0-1 UV 面片配任何节点材质都会采样错位被 cutout
                // 裁没（r19-r21 实证：动物不进图集故可见，人形全隐）。
                PawnTextureAtlasFrameSet fs;
                if (GlobalTextureAtlasManager.TryGetPawnFrameSet(pawn, out fs, out _) && fs != null && fs.atlas != null)
                {
                    Mesh blit = null;
                    try { blit = BlitMesh(pawn.Drawer.renderer, fs, viewRot, PawnDrawMode.BodyAndHead); }
                    catch { }
                    if (blit != null)
                    {
                        Material mat = Lighting3D.MatFor(
                            MaterialPool.MatFrom(new MaterialRequest(fs.atlas, ShaderDatabase.Cutout)), lit);
                        // blit mesh 是躺地平面 quad（原版俯视用、v 朝北、中心未必在锚点）：
                        // 绕 X -90° 竖起后 mesh z 轴映射为世界 y，按 bounds 精确贴地；
                        // 横纵解耦：高=pawnHeight，宽=pawnHeight*pawnWidthScale（原生帧偏宽扁）。
                        var b = blit.bounds;
                        float sy = H / Mathf.Max(0.05f, b.size.z);
                        float sx = W / Mathf.Max(0.05f, b.size.x);
                        var rot = Quaternion.AngleAxis(yaw + 180f, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);
                        var fwd = rot * Vector3.forward;
                        var center = pos + new Vector3(0f, -b.min.z * sy, 0f) + fwd * 0.02f;
                        var matrix = Matrix4x4.TRS(center, rot, new Vector3(sx, sy, 1f));
                        Graphics.DrawMesh(blit, matrix, mat, 0);
                        HitFx3D.DrawOverlay(blit, matrix, mat, hurt);
                        PerfCounters.PawnQuads++;
                        return;
                    }
                }
                // 兜底：分层走树（图集不可用时）
                WalkNode(tree.rootNode, pawn, pos, yaw, viewRot, H, W, headY, headS, quad, ref depth, 0, lit, hurt);
            }
            else
            {
                var body = tree.BodyGraphic;
                if (body == null) return;
                Material mat = null;
                try { mat = body.MatAt(viewRot, pawn); } catch { }
                if (mat == null) return;
                var ds = body.drawSize;
                float aH = Mathf.Max(0.3f, ds.y);
                float aW = Mathf.Max(0.3f, ds.x);
                DrawQuad(pos, yaw, aW, aH, mat, quad, 0f, lit, hurt);
            }
        }

        // GetBlitMeshUpdatedFrame 是私有实例方法：反射委托 (renderer, frameSet, rot, mode) => mesh
        private static readonly Func<PawnRenderer, PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh> BlitMesh =
            AccessTools.MethodDelegate<Func<PawnRenderer, PawnTextureAtlasFrameSet, Rot4, PawnDrawMode, Mesh>>(
                AccessTools.Method(typeof(PawnRenderer), "GetBlitMeshUpdatedFrame"));

        private static void DumpTree(PawnRenderNode node, System.Text.StringBuilder sb, int depth)
        {
            if (node == null || depth > 4) return;
            object g = null;
            try { g = node.PrimaryGraphic; } catch { }
            string gName = "none";
            if (g is Graphic gg)
            {
                try { gName = gg.MatAt(Rot4.South, null) != null ? gg.path ?? "mat-ok" : "mat-null"; } catch { gName = "mat-err"; }
            }
            sb.Append(" [").Append(node.GetType().Name).Append(" g=").Append(gName)
              .Append(" kids=").Append(node.children == null ? -1 : node.children.Length).Append("]");
            if (node.children == null) return;
            for (int i = 0; i < node.children.Length; i++)
                DumpTree(node.children[i], sb, depth + 1);
        }

        private void WalkNode(PawnRenderNode node, Pawn pawn, Vector3 pos, float yaw, Rot4 viewRot,
            float H, float W, float headY, float headS, Mesh quad, ref int depth, int guard, Color lit, float hurt)
        {
            if (node == null || guard > 8) return;
            var typeName = node.GetType().Name;
            bool isBody = typeName == "PawnRenderNode_Body";
            bool isHead = typeName == "PawnRenderNode_Head";
            bool isHair = typeName == "PawnRenderNode_Hair" || typeName == "PawnRenderNode_Beard";
            bool isApparel = typeName.Contains("Apparel");
            bool onHead = false;
            if (isApparel && node.apparel != null)
            {
                try
                {
                    var lastLayer = node.apparel.def.apparel.LastLayer;
                    onHead = lastLayer == ApparelLayerDefOf.Overhead || lastLayer == ApparelLayerDefOf.EyeCover;
                }
                catch { }
            }

            if (isBody || isHead || isHair || isApparel)
            {
                var g = node.PrimaryGraphic;
                if (g != null)
                {
                    Material mat = null;
                    try { mat = g.MatAt(viewRot, pawn); } catch { }
                    if (mat != null && mat.mainTexture != null)
                    {
                        bool headZone = isHead || isHair || onHead;
                        float qw = headZone ? headS : W;
                        float qh = headZone ? headS * 1.05f : H;
                        var qpos = headZone ? pos + new Vector3(0f, headY - headS * 0.5f + 0.02f, 0f) : pos;
                        DrawQuad(qpos, yaw, qw, qh, mat, quad, 0.012f * depth, lit, hurt);
                        depth++;
                    }
                }
            }

            var children = node.children;
            if (children == null) return;
            for (int i = 0; i < children.Length; i++)
                WalkNode(children[i], pawn, pos, yaw, viewRot, H, W, headY, headS, quad, ref depth, guard + 1, lit, hurt);
        }

        private static PawnDrawParms BuildParms(Pawn pawn, Vector3 pos, Rot4 viewRot)
        {
            var p = PawnDrawParms.DefaultFor(pawn);
            p.matrix = Matrix4x4.TRS(pos + pawn.ageTracker.CurLifeStage.bodyDrawOffset,
                Quaternion.identity, Vector3.one);
            p.facing = viewRot;
            try { p.rotDrawMode = pawn.Drawer.renderer.CurRotDrawMode; } catch { }
            try { p.posture = pawn.GetPosture(); } catch { }
            p.flags = PawnRenderFlags.Clothes | PawnRenderFlags.Headgear;
            p.tint = Color.white;
            return p;
        }

        private void DrawQuad(Vector3 pos, float yaw, float w, float h, Material mat, Mesh quad,
            float depthOffset, Color lit, float hurt)
        {
            var rot = Quaternion.AngleAxis(yaw, Vector3.up);
            var fwd = rot * Vector3.forward;
            var matrix = Matrix4x4.TRS(pos + fwd * depthOffset, rot, new Vector3(w, h, 1f));
            Graphics.DrawMesh(quad, matrix, Lighting3D.MatFor(mat, lit), 0);
            HitFx3D.DrawOverlay(quad, matrix, mat, hurt);
            PerfCounters.PawnQuads++;
        }

        public static Rot4 SnapRot4(float angle)
        {
            float norm = Mathf.Repeat(angle, 360f);
            int idx = Mathf.RoundToInt(norm / 90f) & 3;
            return new Rot4(idx);
        }

        public void Dispose() { }
    }
}
