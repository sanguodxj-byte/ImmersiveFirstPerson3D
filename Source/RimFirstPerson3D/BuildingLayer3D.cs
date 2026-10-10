// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 建筑层：满格建筑（墙/岩石/门）转盒体并附反绕描边壳；家具交给 Furniture3D
    // 按原版三视图重构的模型（本层只兜底剩余的非家具件：立板+地面贴片）。
    public class BuildingLayer3D
    {
        public const int ChunkSize = 16;

        private readonly Map map;
        private readonly float baseY;

        private class Chunk
        {
            public Dictionary<Material, Mesh> meshes = new Dictionary<Material, Mesh>();
            public Mesh hullMesh;
            public bool dirty = true;
            // 图内局部包围盒（不含 worldOffset），供视锥剔除用；无网格时 valid=false
            public Bounds bounds;
            public bool boundsValid;
        }

        private readonly Dictionary<int, Chunk> chunks = new Dictionary<int, Chunk>();
        private bool allDirty = true;

        public BuildingLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty() => allDirty = true;

        public void NotifyThingChanged(Thing t)
        {
            if (t == null || !t.Spawned || t.def.category != ThingCategory.Building) return;
            foreach (var c in GenAdj.OccupiedRect(t))
            {
                int key = (c.x / ChunkSize) * 10000 + (c.z / ChunkSize);
                if (!chunks.TryGetValue(key, out var ch)) { ch = new Chunk(); chunks[key] = ch; }
                ch.dirty = true;
            }
        }

        public int Update()
        {
            if (allDirty)
            {
                DisposeMeshes();
                for (int cx = 0; cx < (map.Size.x + ChunkSize - 1) / ChunkSize; cx++)
                    for (int cz = 0; cz < (map.Size.z + ChunkSize - 1) / ChunkSize; cz++)
                    {
                        int key = cx * 10000 + cz;
                        if (!chunks.TryGetValue(key, out var ch)) { ch = new Chunk(); chunks[key] = ch; }
                        ch.dirty = true;
                    }
                allDirty = false;
            }
            int budget = Fps3DState.ChunkRebuildBudget;
            int remaining = 0;
            foreach (var kv in chunks)
            {
                if (!kv.Value.dirty) continue;
                if (budget > 0)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    RebuildChunk(kv.Key / 10000, kv.Key % 10000, kv.Value);
                    PerfCounters.LastBuildingBuildMs = (float)sw.Elapsed.TotalMilliseconds;
                    budget--;
                }
                else remaining++;
            }
            return remaining;
        }

        private void RebuildChunk(int cx, int cz, Chunk chunk)
        {
            DisposeChunk(chunk);
            var rect = new CellRect(cx * ChunkSize, cz * ChunkSize, ChunkSize, ChunkSize);
            rect.ClipInsideMap(map);

            var builders = new Dictionary<Material, MeshBuilderX>();
            MeshBuilderX hull = null;

            foreach (var t in map.listerThings.AllThings)
            {
                if (t == null || !t.Spawned) continue;
                if (t.def.category != ThingCategory.Building) continue;
                if (t is Blueprint || t is Frame) continue;
                var occ = GenAdj.OccupiedRect(t);
                if (!occ.Overlaps(rect)) continue;
                try { BakeBuilding(t, occ, rect, builders, ref hull); }
                catch (System.Exception e)
                {
                    if (Util3D.VerboseLog) Log.WarningOnce("[FirstPerson3D] bake " + t.def.defName + ": " + e.Message, t.thingIDNumber);
                }
            }

            bool hasBounds = false;
            var acc = new Bounds();
            foreach (var kv in builders)
            {
                var mesh = kv.Value.ToMesh();
                if (mesh != null)
                {
                    chunk.meshes[kv.Key] = mesh;
                    if (!hasBounds) { acc = mesh.bounds; hasBounds = true; }
                    else acc.Encapsulate(mesh.bounds);
                }
            }
            chunk.hullMesh = hull?.ToMesh();
            if (chunk.hullMesh != null)
            {
                if (!hasBounds) { acc = chunk.hullMesh.bounds; hasBounds = true; }
                else acc.Encapsulate(chunk.hullMesh.bounds);
            }
            chunk.bounds = acc;
            chunk.boundsValid = hasBounds;
            chunk.dirty = false;
        }

        private void BakeBuilding(Thing t, CellRect occ, CellRect chunkRect,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull)
        {
            var g = t.Graphic;
            if (g == null) return;
            Material srcMat = null;
            try { srcMat = g.MatAt(t.Rotation, t); } catch { }
            if (srcMat == null || srcMat.mainTexture == null) { try { srcMat = g.MatSingle; } catch { } }
            if (srcMat == null || srcMat.mainTexture == null) return;

            var center = t.DrawPos;
            var pos = new Vector3(center.x, baseY, center.z);
            var st = FirstPerson3DMod.settings;
            float vS = st != null ? st.wallVScale : 0.55f;
            float outline = st != null ? st.wallOutline : 0.03f;

            bool isDoor = t is Building_Door;
            bool fullCell = t.def.Fillage == FillCategory.Full;

            // 只有建造墙走墙图集（顶面=墙帽段，立面=墙身段）。天然岩、平滑岩、模组墙
            // 继续用连接子格；图集加载不到时建造墙也退回连接子格。
            if (fullCell && !isDoor && t.def == ThingDefOf.Wall && WallAtlasTex() != null)
            {
                BakeWall(t, occ, chunkRect, srcMat, builders, ref hull, outline);
                return;
            }

            Material mat = Util3D.CloneForBox(srcMat);

            if (isDoor)
            {
                var door = (Building_Door)t;
                // 名单外的门（栅栏门、动物帘）和图集缺失时完全不碰原来的样子。
                bool header = WallAtlasTex() != null && HeaderDoors.Contains(t.def.defName);
                // 门扇厚 0.18（略薄于墙 1.0，视觉上像门板嵌在墙洞里）
                float dw = door.Rotation.IsHorizontal ? Metrics3D.DoorPanelThickness : 1f;
                float dd = door.Rotation.IsHorizontal ? 1f : Metrics3D.DoorPanelThickness;
                float dh = Metrics3D.DoorHeight;   // 2.05，取代原 WallHeight*0.82
                if (!header || !door.Open)
                {
                    var m = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
                    var b = BuilderFor(builders, mat);
                    b.AppendBox(w: dw, h: dh, d: dd, vScale: vS, matrix: m);
                    if (Fps3DState.DrawOutlines)
                    {
                        hull = hull ?? new MeshBuilderX();
                        hull.AppendBoxHull(dw, dh, dd, outline, m);
                    }
                }
                if (header) BakeDoorHeader(t, occ, chunkRect, srcMat, builders, ref hull, outline);
                return;
            }

            if (fullCell)
            {
                // 体素式面剔除：逐格发射，被相邻满格建筑挡住的面（墙与墙的接缝、
                // 岩体内部）一律不发，只留玩家看得到的外层；描边壳同样只包可见面。
                // 多格建筑跨 chunk 时各 chunk 只画自己格内的面，不再整盒重复。
                var b = BuilderFor(builders, mat);
                foreach (var c in occ)
                {
                    if (!chunkRect.Contains(c)) continue;
                    bool n = !SolidAt(c + new IntVec3(0, 0, -1));
                    bool s = !SolidAt(c + new IntVec3(0, 0, 1));
                    bool w = !SolidAt(c + new IntVec3(-1, 0, 0));
                    bool e = !SolidAt(c + new IntVec3(1, 0, 0));
                    b.AppendSolidCell(c.x, c.z, baseY, Fps3DState.WallHeight, n, s, w, e);
                    if (Fps3DState.DrawOutlines)
                    {
                        hull = hull ?? new MeshBuilderX();
                        hull.AppendSolidCellHull(c.x, c.z, baseY, Fps3DState.WallHeight,
                            outline, n, s, w, e);
                    }
                }
                return;
            }

            // 地面平贴建筑（蒸汽喷泉坑口）：原版贴图是俯视坑口，只能平铺在地面；
            // 走立板分支会把坑口竖成黑色幕墙（第一人称下的黑坑伪影）。
            if (t is Building_SteamGeyser)
            {
                float s = Mathf.Max(g.drawSize.x, g.drawSize.y);
                BuilderFor(builders, mat).AppendGroundQuad(s, s,
                    Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one));
                return;
            }

            // 家具：交给 Furniture3D 按三视图重构的模型逐件绘制，不再立板。
            if (Furniture3D.Handles(t)) return;

            // 残骸（地图野生建筑）：交给 Wreckage3D 逐件绘制真实几何，不再立板。
            if (Wreckage3D.Handles(t)) return;

            // 家具等：立板（沿建筑朝向）+ 地面贴片，双面。
            float yaw = t.Rotation.AsAngle;
            float slabH = Mathf.Min(1.7f, Mathf.Max(0.8f, g.drawSize.y));
            float slabW = Mathf.Max(0.5f, g.drawSize.x);
            var rot = Quaternion.AngleAxis(yaw, Vector3.up);
            var bld = BuilderFor(builders, mat);
            bld.AppendSlab(slabW, slabH, Matrix4x4.TRS(pos, rot, Vector3.one), doubleSided: true);
            bld.AppendGroundQuad(slabW, Mathf.Max(slabW, g.drawSize.y),
                Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one));
        }

        private static MeshBuilderX BuilderFor(Dictionary<Material, MeshBuilderX> builders, Material mat)
        {
            if (!builders.TryGetValue(mat, out var b))
            {
                b = new MeshBuilderX();
                builders[mat] = b;
            }
            return b;
        }

        // ---- 建造墙 / 门楣：取样图集（scripts/wall_atlas.py）----

        private static Texture2D wallAtlas;
        // 一个外观加一种染色共用一个材质（chunk 仍按材质合并），见 0003-wall-atlas.md。
        private static readonly Dictionary<string, Material> wallMats = new Dictionary<string, Material>();

        // 会切门洞的门。缺 DLC 时 defName 对不上名单就当普通邻居，栅栏门/动物帘故意不在名单里。
        private static readonly HashSet<string> HeaderDoors = new HashSet<string>
        {
            "Door", "Autodoor", "OrnateDoor", "SecurityDoor", "VacBarrier"
        };

        // 面的四个方向：0=北(-Z) 1=南(+Z) 2=西(-X) 3=东(+X)，与 SolidAt 的邻格偏移同序。
        private static readonly IntVec3[] DirOff =
        {
            new IntVec3(0, 0, -1), new IntVec3(0, 0, 1), new IntVec3(-1, 0, 0), new IntVec3(1, 0, 0)
        };

        private static Texture2D WallAtlasTex()
        {
            if (wallAtlas != null) return wallAtlas;
            try { wallAtlas = ContentFinder<Texture2D>.Get(WallUVGen.TexPath, reportFailure: false); }
            catch { wallAtlas = null; }
            if (wallAtlas == null) return null;
            // 立面段只有 20 像素高，双线性会把砖缝抹平（家具图集仍用双线性，别跟着改）。
            wallAtlas.filterMode = FilterMode.Point;
            wallAtlas.wrapMode = TextureWrapMode.Clamp;
            return wallAtlas;
        }

        // 外观键：Bricks / Planks 各有图，其余（含 Smooth、Metal、没有材料）退回 smooth。
        private static string AppearanceKey(Thing t)
        {
            var stuff = t.Stuff;
            var ap = stuff != null && stuff.stuffProps != null ? stuff.stuffProps.appearance : null;
            if (ap == null) return "smooth";
            if (ap.defName == "Bricks") return "bricks";
            if (ap.defName == "Planks") return "planks";
            return "smooth";
        }

        // 染色取这块墙 MatAt 材质上的 color，不另读颜色表。
        private static Material WallAtlasMat(Thing t, Material src, Texture2D atlas)
        {
            var col = src != null ? src.color : Color.white;
            var c32 = (Color32)col;
            string key = AppearanceKey(t) + "|" + c32.r + "," + c32.g + "," + c32.b + "," + c32.a;
            if (wallMats.TryGetValue(key, out var m)) return m;
            m = new Material(ShaderDatabase.Cutout);
            m.mainTexture = atlas;
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            m.color = col;
            wallMats[key] = m;
            return m;
        }

        // 这一面要不要发：邻居不是满格建筑，或者邻居是要切门洞的门
        // （门格算满格，但它那一面是门洞侧壁，发整面隧道两侧才不透）。
        private bool WallFaceExposed(IntVec3 c)
        {
            return !SolidAt(c) || HeaderDoorAt(c) != null;
        }

        private Building_Door HeaderDoorAt(IntVec3 c)
        {
            if (!c.InBounds(map)) return null;
            var d = c.GetEdifice(map) as Building_Door;
            if (d == null) return null;
            return HeaderDoors.Contains(d.def.defName) ? d : null;
        }

        // 体素式面剔除同原来的满格分支：逐格发射，只留玩家看得见的外层。
        private void BakeWall(Thing t, CellRect occ, CellRect chunkRect, Material srcMat,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull, float outline)
        {
            string ap = AppearanceKey(t);
            var b = BuilderFor(builders, WallAtlasMat(t, srcMat, WallAtlasTex()));
            var top = WallUVGen.R[ap + ".top"];
            var side = WallUVGen.R[ap + ".side"];
            float h = Fps3DState.WallHeight;
            float y1 = baseY + h;
            bool shell = Fps3DState.DrawOutlines;
            if (shell) hull = hull ?? new MeshBuilderX();
            foreach (var c in occ)
            {
                if (!chunkRect.Contains(c)) continue;
                b.AppendWallTop(c.x, c.z, y1, top);
                if (shell) hull.AppendWallTopHull(c.x, c.z, y1, outline);
                for (int d = 0; d < 4; d++)
                {
                    if (!WallFaceExposed(c + DirOff[d])) continue;
                    b.AppendWallFace(d, c.x, c.z, 0f, 1f, baseY, y1, side, baseY, h);
                    if (shell) hull.AppendWallFaceHull(d, c.x, c.z, 0f, 1f, baseY, y1, baseY, outline);
                }
            }
        }

        // 门楣盒：占满门格，底在 DoorHeight、顶在 WallHeight。朝房间的那一面发整条棱的
        // 横带加左右窄条，洞口宽度就正好是 DoorWidth；窄条用第 3 节的高度比例，
        // 砖缝与旁边的墙对齐。
        private void BakeDoorHeader(Thing t, CellRect occ, CellRect chunkRect, Material srcMat,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull, float outline)
        {
            string ap = AppearanceKey(t);
            var b = BuilderFor(builders, WallAtlasMat(t, srcMat, WallAtlasTex()));
            var top = WallUVGen.R[ap + ".top"];
            var side = WallUVGen.R[ap + ".side"];
            float h = Fps3DState.WallHeight;
            float y1 = baseY + h;
            float sill = baseY + Metrics3D.DoorHeight;
            float inset = (1f - Metrics3D.DoorWidth) * 0.5f;
            bool shell = Fps3DState.DrawOutlines;
            if (shell) hull = hull ?? new MeshBuilderX();
            var sh = hull;   // 本地函数不能捕获 ref 参数，这里固定成局部引用
            foreach (var c in occ)
            {
                if (!chunkRect.Contains(c)) continue;
                b.AppendWallTop(c.x, c.z, y1, top);
                if (shell) sh.AppendWallTopHull(c.x, c.z, y1, outline);
                // 门洞上沿：法线朝下，贴墙帽段
                b.AppendWallBottom(c.x, c.z, sill, top);
                if (shell) sh.AppendWallBottomHull(c.x, c.z, sill, outline);
                for (int d = 0; d < 4; d++)
                {
                    if (SolidAt(c + DirOff[d])) continue;
                    void Strip(float ua, float ub, float ya, float yb)
                    {
                        b.AppendWallFace(d, c.x, c.z, ua, ub, ya, yb, side, baseY, h);
                        if (shell) sh.AppendWallFaceHull(d, c.x, c.z, ua, ub, ya, yb, baseY, outline);
                    }
                    Strip(0f, 1f, sill, y1);              // 门楣横带
                    Strip(0f, inset, baseY, sill);        // 左条
                    Strip(1f - inset, 1f, baseY, sill);   // 右条
                }
            }
        }

        // 该格是否被满格建筑占据（墙/天然岩）。图外视为空气：
        // 图边墙的外侧面在流式邻图上看得见，不能剔。
        private bool SolidAt(IntVec3 c)
        {
            if (!c.InBounds(map)) return false;
            var ed = c.GetEdifice(map);
            return ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full;
        }

        // 水平流式偏移（世界坐标 = 图内局部 + 此偏移）
        public Vector3 worldOffset = Vector3.zero;

        public void Draw()
        {
            var m = Matrix4x4.Translate(worldOffset);
            foreach (var kv in chunks)
            {
                var chunk = kv.Value;
                if (chunk.meshes.Count == 0 && chunk.hullMesh == null) continue;
                // 屏幕外 / 超远 chunk 直接跳过提交（相机视锥缓存在 Frustum3D 每帧刷新）
                if (chunk.boundsValid)
                {
                    var wb = chunk.bounds;
                    wb.center += worldOffset;
                    if (!Frustum3D.Visible(wb)) continue;
                }
                foreach (var mkv in chunk.meshes)
                {
                    Graphics.DrawMesh(mkv.Value, m, mkv.Key, 0);
                    PerfCounters.BuildingMeshesDrawn++;
                    PerfCounters.BuildingQuads += mkv.Value.vertexCount / 4;
                }
                if (chunk.hullMesh != null && Fps3DState.DrawOutlines)
                {
                    Graphics.DrawMesh(chunk.hullMesh, m, Util3D.OutlineMat, 0);
                    PerfCounters.OutlineMeshesDrawn++;
                }
            }
            DrawFurniture();
            DrawWreckage();
        }

        // 家具逐件绘制（模型按 原型+朝向+占格 缓存，不并入 chunk 网格）。
        private void DrawFurniture()
        {
            if (!Furniture3D.Enabled) return;
            var all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || !t.Spawned || t.def == null) continue;
                if (t.def.category != ThingCategory.Building) continue;
                try
                {
                    if (!Furniture3D.Handles(t)) continue;
                    if (t.Position.Fogged(map)) continue;
                    var dp = t.DrawPos;
                    var wp = new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z);
                    if (!Frustum3D.VisiblePoint(wp, 4f)) continue;
                    Furniture3D.TryDraw(t, wp);
                }
                catch { }
            }
        }

        // 残骸逐件绘制（模型按样板缓存，不并入 chunk 网格）。
        private void DrawWreckage()
        {
            if (!Wreckage3D.Enabled) return;
            var all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || !t.Spawned || t.def == null) continue;
                if (t.def.category != ThingCategory.Building) continue;
                try
                {
                    if (!Wreckage3D.Handles(t)) continue;
                    if (t.Position.Fogged(map)) continue;
                    var dp = t.DrawPos;
                    var wp = new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z);
                    // 残骸样板最大 6x4，取半径 4 保守放行，避免贴边误剔。
                    if (!Frustum3D.VisiblePoint(wp, 4f)) continue;
                    Wreckage3D.TryDraw(t, wp);
                }
                catch { }
            }
        }

        private void DisposeChunk(Chunk chunk)
        {
            foreach (var mkv in chunk.meshes) Util3D.SafeDestroy(mkv.Value);
            chunk.meshes.Clear();
            Util3D.SafeDestroy(chunk.hullMesh);
            chunk.hullMesh = null;
        }

        private void DisposeMeshes()
        {
            foreach (var kv in chunks) DisposeChunk(kv.Value);
        }

        public void Dispose()
        {
            DisposeMeshes();
            chunks.Clear();
        }

        // 顶点/UV 直写式网格构建器。
        private class MeshBuilderX
        {
            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> tris = new List<int>();

            public void AppendBox(float w, float h, float d, float vScale, Matrix4x4 matrix)
            {
                float hw = w * 0.5f, hd = d * 0.5f;
                // The original linked-wall material already selects one atlas cell;
                // keep each box face inside that cell instead of walking into neighbors.
                float v1 = 1f;
                // 顶
                Quad(new Vector3(-hw, h, hd), new Vector3(hw, h, hd), new Vector3(hw, h, -hd), new Vector3(-hw, h, -hd),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), matrix);
                // 北
                Quad(new Vector3(hw, 0, -hd), new Vector3(-hw, 0, -hd), new Vector3(-hw, h, -hd), new Vector3(hw, h, -hd),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, v1), new Vector2(0, v1), matrix);
                // 南
                Quad(new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd), new Vector3(hw, h, hd), new Vector3(-hw, h, hd),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, v1), new Vector2(0, v1), matrix);
                // 西
                Quad(new Vector3(-hw, 0, -hd), new Vector3(-hw, 0, hd), new Vector3(-hw, h, hd), new Vector3(-hw, h, -hd),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, v1), new Vector2(0, v1), matrix);
                // 东
                Quad(new Vector3(hw, 0, hd), new Vector3(hw, 0, -hd), new Vector3(hw, h, -hd), new Vector3(hw, h, hd),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, v1), new Vector2(0, v1), matrix);
            }

            public void AppendBoxHull(float w, float h, float d, float inflate, Matrix4x4 matrix)
            {
                // 共享 builder 里 tris 与 verts 不同步：必须以 tris.Count 为翻转起点，
                // 用 verts.Count 会翻到前一个盒体的三角形上（黑面 bug）。
                int triStart = tris.Count;
                AppendBox(w + inflate * 2f, h + inflate, d + inflate * 2f, 1f, matrix);
                for (int i = triStart; i < tris.Count; i += 3)
                {
                    int tmp = tris[i];
                    tris[i] = tris[i + 2];
                    tris[i + 2] = tmp;
                }
            }

            public void AppendSlab(float w, float h, Matrix4x4 matrix, bool doubleSided)
            {
                Quad(new Vector3(-w * 0.5f, 0, 0), new Vector3(w * 0.5f, 0, 0),
                     new Vector3(w * 0.5f, h, 0), new Vector3(-w * 0.5f, h, 0),
                     new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), matrix);
                if (!doubleSided) return;
                Quad(new Vector3(w * 0.5f, 0, 0), new Vector3(-w * 0.5f, 0, 0),
                     new Vector3(-w * 0.5f, h, 0), new Vector3(w * 0.5f, h, 0),
                     new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), matrix);
            }

            public void AppendGroundQuad(float w, float d, Matrix4x4 matrix)
            {
                Quad(new Vector3(-w * 0.5f, 0.01f, d * 0.5f), new Vector3(w * 0.5f, 0.01f, d * 0.5f),
                     new Vector3(w * 0.5f, 0.01f, -d * 0.5f), new Vector3(-w * 0.5f, 0.01f, -d * 0.5f),
                     new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), matrix);
            }

            // 体素单元面：直接用图内格坐标（x..x+1 / z..z+1），只发未被相邻
            // 满格建筑挡住的面 + 顶面。风序与 AppendBox 对应面一致（法线朝外），
            // 侧/顶 UV 保持 0..1（墙贴图是图集子格，不能跨格平铺）。
            public void AppendSolidCell(float x, float z, float y0, float h,
                bool n, bool s, bool w, bool e)
            {
                float x1 = x + 1f, z1 = z + 1f, y1 = y0 + h;
                var id = Matrix4x4.identity;
                var a = new Vector2(0, 0); var b = new Vector2(1, 0);
                var c = new Vector2(1, 1); var d = new Vector2(0, 1);
                if (n) Quad(new Vector3(x1, y0, z), new Vector3(x, y0, z),
                    new Vector3(x, y1, z), new Vector3(x1, y1, z), a, b, c, d, id);
                if (s) Quad(new Vector3(x, y0, z1), new Vector3(x1, y0, z1),
                    new Vector3(x1, y1, z1), new Vector3(x, y1, z1), a, b, c, d, id);
                if (w) Quad(new Vector3(x, y0, z), new Vector3(x, y0, z1),
                    new Vector3(x, y1, z1), new Vector3(x, y1, z), a, b, c, d, id);
                if (e) Quad(new Vector3(x1, y0, z1), new Vector3(x1, y0, z),
                    new Vector3(x1, y1, z), new Vector3(x1, y1, z1), a, b, c, d, id);
                Quad(new Vector3(x, y1, z1), new Vector3(x1, y1, z1),
                    new Vector3(x1, y1, z), new Vector3(x, y1, z), a, b, c, d, id);
            }

            // 描边壳同样只包可见面：各面向外扩 inflate，风序翻转（从内侧可见），
            // 与 AppendBoxHull 同一手法，但逐面独立翻转。
            public void AppendSolidCellHull(float x, float z, float y0, float h,
                float inflate, bool n, bool s, bool w, bool e)
            {
                float xe = x - inflate, x1e = x + 1f + inflate;
                float ze = z - inflate, z1e = z + 1f + inflate;
                float y1e = y0 + h + inflate;
                var id = Matrix4x4.identity;
                var a = new Vector2(0, 0); var b = new Vector2(1, 0);
                var c = new Vector2(1, 1); var d = new Vector2(0, 1);
                int t0 = tris.Count;
                if (n) Quad(new Vector3(x1e, y0, ze), new Vector3(xe, y0, ze),
                    new Vector3(xe, y1e, ze), new Vector3(x1e, y1e, ze), a, b, c, d, id);
                FlipLast(t0);
                t0 = tris.Count;
                if (s) Quad(new Vector3(xe, y0, z1e), new Vector3(x1e, y0, z1e),
                    new Vector3(x1e, y1e, z1e), new Vector3(xe, y1e, z1e), a, b, c, d, id);
                FlipLast(t0);
                t0 = tris.Count;
                if (w) Quad(new Vector3(xe, y0, ze), new Vector3(xe, y0, z1e),
                    new Vector3(xe, y1e, z1e), new Vector3(xe, y1e, ze), a, b, c, d, id);
                FlipLast(t0);
                t0 = tris.Count;
                if (e) Quad(new Vector3(x1e, y0, z1e), new Vector3(x1e, y0, ze),
                    new Vector3(x1e, y1e, ze), new Vector3(x1e, y1e, z1e), a, b, c, d, id);
                FlipLast(t0);
                t0 = tris.Count;
                Quad(new Vector3(xe, y1e, z1e), new Vector3(x1e, y1e, z1e),
                    new Vector3(x1e, y1e, ze), new Vector3(xe, y1e, ze), a, b, c, d, id);
                FlipLast(t0);
            }

            private void FlipLast(int fromIndex)
            {
                for (int i = fromIndex; i < tris.Count; i += 3)
                {
                    int tmp = tris[i];
                    tris[i] = tris[i + 2];
                    tris[i + 2] = tmp;
                }
            }

            // ---- 墙图集版发射：UV 由调用方给的矩形线性映射，不借连接子格的 scale/offset ----
            //
            // 面方向 0=北(-Z) 1=南(+Z) 2=西(-X) 3=东(+X)。原点、右向、外法线逐面与
            // AppendSolidCell 的绕序对齐：u=0 落在该面第一个顶点上，u=1 落在第二个上，
            // 所以朝外的风序保持不变。棱长恒 1 m，故 u 就是棱坐标。

            private static readonly int[] Ox = { 1, 0, 0, 1 };
            private static readonly int[] Oz = { 0, 1, 0, 1 };
            private static readonly int[] Rx = { -1, 1, 0, 0 };
            private static readonly int[] Rz = { 0, 0, 1, -1 };
            private static readonly int[] Nx = { 0, 0, -1, 1 };
            private static readonly int[] Nz = { -1, 1, 0, 0 };

            private static Vector3 FacePoint(int dir, float x, float z, float u, float y, float outBias)
            {
                return new Vector3(x + Ox[dir] + Rx[dir] * u + Nx[dir] * outBias, y,
                                   z + Oz[dir] + Rz[dir] * u + Nz[dir] * outBias);
            }

            private static Vector2 RectUv(Vector4 r, float u, float v)
            {
                return new Vector2(r.x + u * (r.z - r.x), r.y + v * (r.w - r.y));
            }

            // 立面的一块（可以是切过门洞剩下的窄条）：棱坐标 [ua,ub] 是它在整条 1 m 棱上的
            // 位置，切过之后不重新归一化；v = (y - vBase) / vSpan，地面 0、墙顶 1。
            public void AppendWallFace(int dir, float x, float z, float ua, float ub,
                float ya, float yb, Vector4 r, float vBase, float vSpan)
            {
                float va = (ya - vBase) / vSpan, vb = (yb - vBase) / vSpan;
                Quad(FacePoint(dir, x, z, ua, ya, 0f), FacePoint(dir, x, z, ub, ya, 0f),
                    FacePoint(dir, x, z, ub, yb, 0f), FacePoint(dir, x, z, ua, yb, 0f),
                    RectUv(r, ua, va), RectUv(r, ub, va), RectUv(r, ub, vb), RectUv(r, ua, vb),
                    Matrix4x4.identity);
            }

            // 顶面：u 沿格子的 +X，v 沿格子的 +Z，整面贴一次不重复。
            public void AppendWallTop(float x, float z, float y, Vector4 r)
            {
                Quad(new Vector3(x, y, z + 1f), new Vector3(x + 1f, y, z + 1f),
                    new Vector3(x + 1f, y, z), new Vector3(x, y, z),
                    RectUv(r, 0f, 1f), RectUv(r, 1f, 1f), RectUv(r, 1f, 0f), RectUv(r, 0f, 0f),
                    Matrix4x4.identity);
            }

            // 底面（门洞上沿）：法线朝下，UV 口径同顶面。
            public void AppendWallBottom(float x, float z, float y, Vector4 r)
            {
                Quad(new Vector3(x, y, z), new Vector3(x + 1f, y, z),
                    new Vector3(x + 1f, y, z + 1f), new Vector3(x, y, z + 1f),
                    RectUv(r, 0f, 0f), RectUv(r, 1f, 0f), RectUv(r, 1f, 1f), RectUv(r, 0f, 1f),
                    Matrix4x4.identity);
            }

            // 描边壳只包实际发出的那些面：同一块面向外推 inflate、面内四边各扩 inflate
            // 后翻转绕序（从内侧可见），与 AppendSolidCellHull 同一手法。底边不落到地面以下。
            public void AppendWallFaceHull(int dir, float x, float z, float ua, float ub,
                float ya, float yb, float floorY, float inflate)
            {
                int t0 = tris.Count;
                float lo = Mathf.Max(ya - inflate, floorY), hi = yb + inflate;
                Quad(FacePoint(dir, x, z, ua - inflate, lo, inflate),
                    FacePoint(dir, x, z, ub + inflate, lo, inflate),
                    FacePoint(dir, x, z, ub + inflate, hi, inflate),
                    FacePoint(dir, x, z, ua - inflate, hi, inflate),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                    Matrix4x4.identity);
                FlipLast(t0);
            }

            public void AppendWallTopHull(float x, float z, float y, float inflate)
            {
                int t0 = tris.Count;
                Quad(new Vector3(x - inflate, y + inflate, z + 1f + inflate),
                    new Vector3(x + 1f + inflate, y + inflate, z + 1f + inflate),
                    new Vector3(x + 1f + inflate, y + inflate, z - inflate),
                    new Vector3(x - inflate, y + inflate, z - inflate),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                    Matrix4x4.identity);
                FlipLast(t0);
            }

            public void AppendWallBottomHull(float x, float z, float y, float inflate)
            {
                int t0 = tris.Count;
                Quad(new Vector3(x - inflate, y - inflate, z - inflate),
                    new Vector3(x + 1f + inflate, y - inflate, z - inflate),
                    new Vector3(x + 1f + inflate, y - inflate, z + 1f + inflate),
                    new Vector3(x - inflate, y - inflate, z + 1f + inflate),
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1),
                    Matrix4x4.identity);
                FlipLast(t0);
            }

            private void Quad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
                Vector2 uvBL, Vector2 uvBR, Vector2 uvTR, Vector2 uvTL, Matrix4x4 matrix)
            {
                int b = verts.Count;
                verts.Add(matrix.MultiplyPoint3x4(bl));
                verts.Add(matrix.MultiplyPoint3x4(br));
                verts.Add(matrix.MultiplyPoint3x4(tr));
                verts.Add(matrix.MultiplyPoint3x4(tl));
                uvs.Add(uvBL); uvs.Add(uvBR); uvs.Add(uvTR); uvs.Add(uvTL);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            public Mesh ToMesh()
            {
                if (verts.Count == 0) return null;
                var mesh = new Mesh();
                mesh.SetVertices(verts);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
