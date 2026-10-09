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

            foreach (var kv in builders)
            {
                var mesh = kv.Value.ToMesh();
                if (mesh != null) chunk.meshes[kv.Key] = mesh;
            }
            chunk.hullMesh = hull?.ToMesh();
            chunk.dirty = false;
        }

        private void BakeBuilding(Thing t, CellRect occ, CellRect chunkRect,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull)
        {
            var g = t.Graphic;
            if (g == null) return;
            Material mat = null;
            try { mat = g.MatAt(t.Rotation, t); } catch { }
            if (mat == null || mat.mainTexture == null) { try { mat = g.MatSingle; } catch { } }
            if (mat == null || mat.mainTexture == null) return;
            mat = Util3D.CloneForBox(mat);

            var center = t.DrawPos;
            var pos = new Vector3(center.x, baseY, center.z);
            var st = FirstPerson3DMod.settings;
            float vS = st != null ? st.wallVScale : 0.55f;
            float outline = st != null ? st.wallOutline : 0.03f;

            bool isDoor = t is Building_Door;
            bool fullCell = t.def.Fillage == FillCategory.Full;

            if (isDoor)
            {
                var door = (Building_Door)t;
                // 门扇厚 0.18（略薄于墙 1.0，视觉上像门板嵌在墙洞里）
                float dw = door.Rotation.IsHorizontal ? 0.18f : 1f;
                float dd = door.Rotation.IsHorizontal ? 1f : 0.18f;
                float dh = Metrics3D.DoorHeight;   // 2.05，取代原 WallHeight*0.82
                var b = BuilderFor(builders, mat);
                b.AppendBox(w: dw, h: dh, d: dd, vScale: vS,
                    matrix: Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one));
                if (Fps3DState.DrawOutlines)
                {
                    hull = hull ?? new MeshBuilderX();
                    hull.AppendBoxHull(dw, dh, dd, outline,
                        Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one));
                }
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
                foreach (var mkv in kv.Value.meshes)
                {
                    Graphics.DrawMesh(mkv.Value, m, mkv.Key, 0);
                    PerfCounters.BuildingMeshesDrawn++;
                    PerfCounters.BuildingQuads += mkv.Value.vertexCount / 4;
                }
                if (kv.Value.hullMesh != null && Fps3DState.DrawOutlines)
                {
                    Graphics.DrawMesh(kv.Value.hullMesh, m, Util3D.OutlineMat, 0);
                    PerfCounters.OutlineMeshesDrawn++;
                }
            }
            DrawFurniture();
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
                    Furniture3D.TryDraw(t, new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z));
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
