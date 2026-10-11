// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 建筑层：满格建筑（墙/岩石/门）转盒体并附反绕描边壳；家具交给 Furniture3D
    // 按原版三视图重构的模型（本层只兜底剩余的非家具件：立板+地面贴片）。
    [StaticConstructorOnStartup]
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
        private readonly HashSet<Thing> rebuildSeen = new HashSet<Thing>();
        private readonly List<Thing> cachedFurniture = new List<Thing>();
        private readonly List<Thing> cachedWreckage = new List<Thing>();
        private bool furnitureListDirty = true;

        // 光照场（F013），由 Scene3D 注入。为空或光照关闭时不写顶点色。
        public LightField3D light;
        public MountainField3D mountain;
        private LightField3D LightOrNull => light != null && Lighting3D.Enabled ? light : null;

        public BuildingLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void MarkAllDirty()
        {
            allDirty = true;
            furnitureListDirty = true;
        }

        public void NotifyThingChanged(Thing t)
        {
            if (t == null || !t.Spawned || t.def.category != ThingCategory.Building) return;
            furnitureListDirty = true;
            // 满格或四向连接矮墙/围栏变化时把四个正交邻居的 chunk 也标脏。
            bool full = t.def.Fillage == FillCategory.Full || WallModels3D.IsLinkedBarrier(t.def);
            foreach (var c in GenAdj.OccupiedRect(t))
            {
                MarkChunkDirty(c);
                if (!full) continue;
                for (int d = 0; d < 4; d++)
                {
                    var n = c + DirOff[d];
                    if (!n.InBounds(map)) continue;
                    MarkChunkDirty(n);
                }
            }
        }

        private void MarkChunkDirty(IntVec3 c)
        {
            int key = (c.x / ChunkSize) * 10000 + (c.z / ChunkSize);
            if (!chunks.TryGetValue(key, out var ch)) { ch = new Chunk(); chunks[key] = ch; }
            ch.dirty = true;
        }

        /// <summary>光照场变化驱动的重建（F013）：只标已经存在的 chunk。
        /// 从没建过的块本来就会带着正确光照烘出来，为它新建一个脏条目等于把空地也拖进重建预算。</summary>
        public void MarkCellDirty(IntVec3 c)
        {
            int key = (c.x / ChunkSize) * 10000 + (c.z / ChunkSize);
            if (chunks.TryGetValue(key, out var ch)) ch.dirty = true;
        }

        /// <summary>把本层所有网格的颜色统计并进探针（只在按需导出时走）。</summary>
        public void Summarize(Lighting3D.Probe p)
        {
            foreach (var kv in chunks)
                foreach (var mkv in kv.Value.meshes)
                    p.Add(mkv.Value);
        }

        public int Update()
        {
            if (allDirty)
            {
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

            rebuildSeen.Clear();
            for (int z = rect.minZ; z <= rect.maxZ; z++)
            {
                for (int x = rect.minX; x <= rect.maxX; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    var ed = c.GetEdifice(map);
                    if (ed != null && ed.Spawned && ed.def != null && ed.def.category == ThingCategory.Building && rebuildSeen.Add(ed))
                    {
                        if (!(ed is Frame))
                        {
                            var occ = GenAdj.OccupiedRect(ed);
                            try { BakeBuilding(ed, occ, rect, builders, ref hull); }
                            catch (System.Exception e)
                            {
                                if (Util3D.VerboseLog) Log.WarningOnce("[FirstPerson3D] bake " + ed.def.defName + ": " + e.Message, ed.thingIDNumber);
                            }
                        }
                    }
                    var list = c.GetThingList(map);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var t = list[i];
                        if (t != ed && t != null && t.Spawned && t.def != null && t.def.category == ThingCategory.Building && rebuildSeen.Add(t))
                        {
                            if (!(t is Blueprint || t is Frame))
                            {
                                var occ = GenAdj.OccupiedRect(t);
                                try { BakeBuilding(t, occ, rect, builders, ref hull); }
                                catch (System.Exception e)
                                {
                                    if (Util3D.VerboseLog) Log.WarningOnce("[FirstPerson3D] bake " + t.def.defName + ": " + e.Message, t.thingIDNumber);
                                }
                            }
                        }
                    }
                }
            }
            rebuildSeen.Clear();

            bool hasBounds = false;
            var acc = new Bounds();
            var lf = LightOrNull;
            foreach (var kv in builders)
            {
                var mesh = kv.Value.ToMesh(lf);
                if (mesh != null)
                {
                    chunk.meshes[kv.Key] = mesh;
                    if (!hasBounds) { acc = mesh.bounds; hasBounds = true; }
                    else acc.Encapsulate(mesh.bounds);
                }
            }
            chunk.hullMesh = hull?.ToMesh(null);
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
            if (MountainField3D.IsRock(t)) return;
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
            var wArch = WallModels3D.ArchOf(t);

            // 全系列满格墙体与穿墙设备（建造砖/木/光滑墙、平滑岩墙、加固防爆墙、飞船船体墙、
            // 虫穴墙、平滑冰墙、迷宫灰墙、虚空金属刺墙、扭曲血肉墙、穿墙通风口、穿墙制冷机）
            // 统一走 WallAtlas 图集与 3D 表面浮雕构造。
            if (fullCell && !isDoor && WallModels3D.IsFullWallOrInWall(wArch) && WallAtlasTex() != null)
            {
                BakeWall(t, wArch, occ, chunkRect, srcMat, builders, ref hull, outline);
                return;
            }

            // 四向连接矮墙掩体与围栏（沙袋、路障、木/石栅栏、金属栅栏、远古混凝土围栏）
            if (!isDoor && WallModels3D.IsLowBarrierOrFence(wArch) && WallAtlasTex() != null)
            {
                BakeLowBarrier(t, wArch, occ, chunkRect, srcMat, builders, ref hull, outline);
                return;
            }

            // 壁挂灯具（壁灯 WallLamp、壁炬 TorchWallLamp）
            if (!isDoor && WallModels3D.IsWallAttachment(wArch) && WallAtlasTex() != null)
            {
                BakeWallAttachment(t, wArch, chunkRect, srcMat, builders, ref hull, outline);
                return;
            }

            if (fullCell && !isDoor && t.def.building != null && t.def.building.isNaturalRock)
            {
                BakeRock(occ, chunkRect, srcMat, builders);
                return;
            }

            if (isDoor)
            {
                var door = (Building_Door)t;
                Rot4 doorRot = DoorRotation(door);
                try { srcMat = g.MatAt(doorRot, t); } catch { }
                if (srcMat == null || srcMat.mainTexture == null) { try { srcMat = g.MatSingle; } catch { } }
                if (srcMat == null || srcMat.mainTexture == null) return;

                bool header = WallAtlasTex() != null && HeaderDoors.Contains(t.def.defName);
                bool horiz = doorRot.IsHorizontal;
                float inset = header ? (1f - Metrics3D.DoorWidth) * 0.5f : 0f;
                float span = (horiz ? occ.Height : occ.Width) - inset * 2f;
                float dw = horiz ? Metrics3D.DoorPanelThickness : span;
                float dd = horiz ? span : Metrics3D.DoorPanelThickness;
                float dh = t.def.defName == "FenceGate" ? Metrics3D.FenceHeight : Metrics3D.DoorHeight;
                if (!door.Open && chunkRect.Contains(door.Position))
                {
                    var doorMat = DoorMaterialAndUv(srcMat, out Vector4 doorUv);
                    var m = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
                    var b = BuilderFor(builders, doorMat);
                    b.AppendDoorPanel(dw, dh, dd, horiz, doorUv, m);
                    if (Fps3DState.DrawOutlines)
                    {
                        hull = hull ?? new MeshBuilderX();
                        hull.AppendBoxHull(dw, dh, dd, outline, m);
                    }
                }
                if (header) BakeDoorHeader(door, doorRot, occ, chunkRect, srcMat, builders, ref hull, outline);
                return;
            }

            Material mat = Util3D.CloneForBox(srcMat);

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

        // (u0, v0, u1, v1)，v=0 在贴图底部。每米贴一次这一格。
        private static readonly Vector4 RockInteriorUv = new Vector4(0.78125f, 0.78125f, 0.96875f, 0.96875f);
        // 主贴图实例 + shader + color + _ColorTwo。清掉连接子格的 scale/offset 后，同外观可共用。
        private static readonly Dictionary<string, Material> rockMats = new Dictionary<string, Material>();

        // 会切门洞的门。缺 DLC 时 defName 对不上名单就当普通邻居，栅栏门故意不在名单里。
        private static readonly HashSet<string> HeaderDoors = new HashSet<string>
        {
            "Door", "Autodoor", "OrnateDoor", "SecurityDoor", "VacBarrier",
            "AnimalFlap", "GrayDoor", "AncientBlastDoor"
        };

        private struct DoorTexEntry
        {
            public Texture2D tex;
            public Vector4 uvRect;
        }

        private static readonly Dictionary<int, DoorTexEntry> doorTexCache = new Dictionary<int, DoorTexEntry>();
        private static readonly Dictionary<string, Material> doorMats = new Dictionary<string, Material>();

        private Rot4 DoorRotation(Building_Door door)
        {
            if (door.def.size == IntVec2.One)
            {
                bool preferFences = door.def.building != null && door.def.building.preferConnectingToFences;
                return DoorUtility.DoorRotationAt(door.Position, map, preferFences);
            }
            return door.Rotation;
        }

        private static Material DoorMaterialAndUv(Material src, out Vector4 uvRect)
        {
            uvRect = new Vector4(0f, 0.203125f, 0.5f, 0.796875f);
            if (src == null || src.mainTexture == null) return src;
            Texture srcTex = src.mainTexture;
            int texId = srcTex.GetInstanceID();
            if (!doorTexCache.TryGetValue(texId, out var entry))
            {
                entry = ExtractDoorTexture(srcTex);
                doorTexCache[texId] = entry;
            }
            uvRect = entry.uvRect;
            int shaderId = src.shader != null ? src.shader.GetInstanceID() : 0;
            var c32 = (Color32)src.color;
            Color two = src.HasProperty("_ColorTwo") ? src.GetColor("_ColorTwo") : Color.black;
            var t32 = (Color32)two;
            string key = texId + "|" + shaderId + "|"
                + c32.r + "," + c32.g + "," + c32.b + "," + c32.a + "|"
                + t32.r + "," + t32.g + "," + t32.b + "," + t32.a;
            if (doorMats.TryGetValue(key, out var m)) return m;
            m = new Material(src)
            {
                mainTexture = entry.tex != null ? (Texture)entry.tex : srcTex,
                mainTextureScale = Vector2.one,
                mainTextureOffset = Vector2.zero
            };
            doorMats[key] = m;
            return m;
        }

        private static DoorTexEntry ExtractDoorTexture(Texture source)
        {
            int w = Mathf.Max(1, source.width);
            int h = Mathf.Max(1, source.height);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "FP3D_Door_" + source.name,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                var px = tex.GetPixels32();

                int x0 = w, x1 = -1, y0 = h, y1 = -1;
                int sumR = 0, sumG = 0, sumB = 0, count = 0;
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        var c = px[row + x];
                        if (c.a >= 128)
                        {
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                            int luma = (c.r + c.g + c.b) / 3;
                            if (luma >= 55)
                            {
                                sumR += c.r; sumG += c.g; sumB += c.b; count++;
                            }
                        }
                    }
                }

                if (x1 < x0 || y1 < y0)
                {
                    x0 = 0; x1 = w / 2 - 1; y0 = 0; y1 = h - 1;
                }
                else
                {
                    float RowLuma(int ry)
                    {
                        int s = 0, n = x1 - x0 + 1;
                        int r = ry * w;
                        for (int x = x0; x <= x1; x++)
                        {
                            var c = px[r + x];
                            s += (c.r + c.g + c.b) / 3;
                        }
                        return (float)s / Mathf.Max(1, n);
                    }
                    float ColLuma(int cx)
                    {
                        int s = 0, n = y1 - y0 + 1;
                        for (int y = y0; y <= y1; y++)
                        {
                            var c = px[y * w + cx];
                            s += (c.r + c.g + c.b) / 3;
                        }
                        return (float)s / Mathf.Max(1, n);
                    }

                    // 剥离顶底与左外沿的原版黑描边（3D 外轮廓由反绕壳负责），右侧中缝仅留 1 列暗线
                    for (int k = 0; k < 3 && y1 - y0 > 12; k++)
                    {
                        if (RowLuma(y0) < 55f) y0++;
                        if (RowLuma(y1) < 55f) y1--;
                    }
                    for (int k = 0; k < 2 && x1 - x0 > 12; k++)
                    {
                        if (ColLuma(x0) < 55f) x0++;
                    }
                    while (x1 - x0 > 12 && ColLuma(x1) < 55f && ColLuma(x1 - 1) < 55f)
                    {
                        x1--;
                    }
                }

                byte fillR = count > 0 ? (byte)(sumR / count) : (byte)200;
                byte fillG = count > 0 ? (byte)(sumG / count) : (byte)200;
                byte fillB = count > 0 ? (byte)(sumB / count) : (byte)200;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a < 128)
                        px[i] = new Color32(fillR, fillG, fillB, 255);
                    else
                        px[i].a = 255;
                }
                tex.SetPixels32(px);
                tex.Apply(false, true);

                var uv = new Vector4(
                    (float)x0 / w,
                    (float)y0 / h,
                    (float)(x1 + 1) / w,
                    (float)(y1 + 1) / h);
                return new DoorTexEntry { tex = tex, uvRect = uv };
            }
            catch
            {
                return new DoorTexEntry
                {
                    tex = null,
                    uvRect = new Vector4(0f, 0.203125f, 0.5f, 0.796875f)
                };
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

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

        // 外观键：按 WallModels3D 原型映射到 WallAtlas.png 中的前缀。
        private static string AppearanceKey(Thing t)
        {
            var arch = WallModels3D.ArchOf(t);
            if (arch != WallModels3D.WallArch.None)
                return WallModels3D.AtlasPrefix(arch);
            var stuff = t != null ? t.Stuff : null;
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

        // 复制原版材质但采样整张图集。CloneForBox 会留下连接子格的 scale/offset，山体不能用。
        private static Material RockMat(Material src)
        {
            int texId = src.mainTexture != null ? src.mainTexture.GetInstanceID() : 0;
            var shader = src.shader;
            int shaderId = shader != null ? shader.GetInstanceID() : 0;
            Color two = Color.black;
            if (src.HasProperty("_ColorTwo")) two = src.GetColor("_ColorTwo");
            var c32 = (Color32)src.color;
            var t32 = (Color32)two;
            string key = texId + "|" + shaderId + "|"
                + c32.r + "," + c32.g + "," + c32.b + "," + c32.a + "|"
                + t32.r + "," + t32.g + "," + t32.b + "," + t32.a;
            if (rockMats.TryGetValue(key, out var m)) return m;
            m = new Material(src);
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            rockMats[key] = m;
            return m;
        }

        // 这一面要不要发：邻居不是满格建筑，或者邻居是正对着该面的通行开口门。
        // 若邻居是门且当前墙位于门的支撑侧（门框两侧），门自身的门楣与 3D 门框内壁已经完整封口，
        // 墙体不再向门内重复发射内侧面与反绕黑壳（避免 3cm 黑壳穿出 2.5cm 门框把门洞内壁染黑）。
        private bool WallFaceExposed(IntVec3 wallCell, int dir)
        {
            var n = wallCell + DirOff[dir];
            if (!SolidAt(n)) return true;
            var door = HeaderDoorAt(n);
            if (door == null) return false;
            Rot4 doorRot = DoorRotation(door);
            bool horiz = doorRot.IsHorizontal;
            // dir 0/1 为南北(-Z/+Z)，dir 2/3 为东西(-X/+X)。
            // !horiz 时东西(2/3)为支撑侧，南北(0/1)为通行侧；horiz 时南北(0/1)为支撑侧，东西(2/3)为通行侧。
            bool isSupportSide = horiz ? (dir < 2) : (dir >= 2);
            return !isSupportSide;
        }

        private Building_Door HeaderDoorAt(IntVec3 c)
        {
            if (!c.InBounds(map)) return null;
            var d = c.GetEdifice(map) as Building_Door;
            if (d == null) return null;
            return HeaderDoors.Contains(d.def.defName) ? d : null;
        }

        // 体素式面剔除 + 3D 立面浮雕：逐格发射可见外层，并在非门暴露面上叠加对应墙种的 3D 浮雕结构。
        private void BakeWall(Thing t, WallModels3D.WallArch wArch, CellRect occ, CellRect chunkRect, Material srcMat,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull, float outline)
        {
            string ap = WallModels3D.AtlasPrefix(wArch);
            var b = BuilderFor(builders, WallAtlasMat(t, srcMat, WallAtlasTex()));
            var top = WallModels3D.GetUv(ap, "top");
            var side = WallModels3D.GetUv(ap, "side");
            float h = wArch == WallModels3D.WallArch.SolidIce ? Metrics3D.RockHeight : Fps3DState.WallHeight;
            float y1 = baseY + h;
            bool shell = Fps3DState.DrawOutlines;
            if (shell) hull = hull ?? new MeshBuilderX();
            var uvs = WallModels3D.BuildUvSpecs(wArch);

            foreach (var c in occ)
            {
                if (!chunkRect.Contains(c)) continue;
                b.AppendWallTop(c.x, c.z, y1, top);
                if (shell) hull.AppendWallTopHull(c.x, c.z, y1, outline);

                bool[] openFace = new bool[4];
                bool anyOpen = false;
                for (int d = 0; d < 4; d++)
                {
                    if (!WallFaceExposed(c, d)) continue;
                    b.AppendWallFace(d, c.x, c.z, 0f, 1f, baseY, y1, side, baseY, h);
                    if (shell) hull.AppendWallFaceHull(d, c.x, c.z, 0f, 1f, baseY, y1, baseY, outline);
                    if (!SolidAt(c + DirOff[d]) && HeaderDoorAt(c + DirOff[d]) == null)
                    {
                        openFace[d] = true;
                        anyOpen = true;
                    }
                }

                if (anyOpen)
                {
                    var relief = new Model3D();
                    int cellHash = c.x * 73856093 ^ c.z * 19349663;
                    if (wArch == WallModels3D.WallArch.Vent || wArch == WallModels3D.WallArch.Cooler)
                    {
                        WallModels3D.AddInWallDeviceRelief(relief, wArch, t.Rotation, h,
                            openFace[0], openFace[1], openFace[2], openFace[3]);
                    }
                    else
                    {
                        for (int d = 0; d < 4; d++)
                            if (openFace[d]) WallModels3D.AddExposedFaceRelief(relief, wArch, d, h, cellHash);
                    }
                    if (relief.parts.Count > 0)
                    {
                        var mb = new ModelBuilder(uvs, false);
                        mb.AddAll(relief, Matrix4x4.Translate(new Vector3(c.x + 0.5f, baseY, c.z + 0.5f)));
                        b.AppendFromModelBuilder(mb, null, 0f);
                    }
                }
            }
        }

        // 四向连接矮墙掩体与围栏（Sandbags / Barricade / Fence / AncientFence）
        private void BakeLowBarrier(Thing t, WallModels3D.WallArch wArch, CellRect occ, CellRect chunkRect, Material srcMat,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull, float outline)
        {
            var b = BuilderFor(builders, WallAtlasMat(t, srcMat, WallAtlasTex()));
            bool shell = Fps3DState.DrawOutlines;
            if (shell) hull = hull ?? new MeshBuilderX();
            var uvs = WallModels3D.BuildUvSpecs(wArch);
            float hw = outline * 0.65f;

            foreach (var c in occ)
            {
                if (!chunkRect.Contains(c)) continue;
                bool ln = WallModels3D.BarrierLinksTo(map, c, wArch, c + DirOff[0]);
                bool ls = WallModels3D.BarrierLinksTo(map, c, wArch, c + DirOff[1]);
                bool lw = WallModels3D.BarrierLinksTo(map, c, wArch, c + DirOff[2]);
                bool le = WallModels3D.BarrierLinksTo(map, c, wArch, c + DirOff[3]);
                int cellHash = c.x * 73856093 ^ c.z * 19349663;
                var model = WallModels3D.BuildCellModel(wArch, ln, ls, lw, le, t.Rotation, cellHash);
                var mb = new ModelBuilder(uvs, false);
                mb.AddAll(model, Matrix4x4.Translate(new Vector3(c.x + 0.5f, baseY, c.z + 0.5f)));
                b.AppendFromModelBuilder(mb, shell ? hull : null, hw);
            }
        }

        // 壁挂灯具（WallLamp / TorchWallLamp）：按朝向贴合在相邻墙体的可见外立面上
        private void BakeWallAttachment(Thing t, WallModels3D.WallArch wArch, CellRect chunkRect, Material srcMat,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull, float outline)
        {
            var c = t.Position;
            if (!chunkRect.Contains(c)) return;
            var b = BuilderFor(builders, WallAtlasMat(t, srcMat, WallAtlasTex()));
            bool shell = Fps3DState.DrawOutlines;
            if (shell) hull = hull ?? new MeshBuilderX();
            var uvs = WallModels3D.BuildUvSpecs(wArch);

            var wallCell = t.Rotation.FacingCell;
            var anchor = new Vector3(
                c.x + 0.5f + wallCell.x * 0.44f,
                baseY + Metrics3D.TorchHeadHeight - 0.15f,
                c.z + 0.5f + wallCell.z * 0.44f);
            var model = WallModels3D.BuildCellModel(wArch, false, false, false, false, t.Rotation);
            var mb = new ModelBuilder(uvs, false);
            mb.AddAll(model, Matrix4x4.Translate(anchor));
            b.AppendFromModelBuilder(mb, shell ? hull : null, outline * 0.55f);
        }

        // 天然岩：顶在 RockHeight，不收斜坡、不发描边。挖开后露出的是竖直侧面。
        // 同高的天然岩不发侧面；别的满格只露出墙顶以上一条；空气发整面，竖向三等分，
        // 每条都贴完整内格，浅色岩层大约每 1 m 一次，而不是被拉成一整面。
        private void BakeRock(CellRect occ, CellRect chunkRect, Material srcMat,
            Dictionary<Material, MeshBuilderX> builders)
        {
            var b = BuilderFor(builders, RockMat(srcMat));
            float y0 = baseY;
            float y1 = baseY + Metrics3D.RockHeight;
            float wallTop = baseY + Fps3DState.WallHeight;
            float step = Metrics3D.RockHeight / 3f;
            foreach (var c in occ)
            {
                if (!chunkRect.Contains(c)) continue;
                b.AppendWallTop(c.x, c.z, y1, RockInteriorUv);
                for (int d = 0; d < 4; d++)
                {
                    var n = c + DirOff[d];
                    if (n.InBounds(map))
                    {
                        var ed = n.GetEdifice(map);
                        if (ed != null && ed.def != null && ed.def.building != null
                            && ed.def.building.isNaturalRock)
                            continue;
                        if (ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full)
                        {
                            // v 按整高映射，这条只取矩形靠上的一段（约 8%），不把整格拉到 0.22 m。
                            b.AppendWallFace(d, c.x, c.z, 0f, 1f, wallTop, y1,
                                RockInteriorUv, y0, Metrics3D.RockHeight);
                            continue;
                        }
                    }
                    for (int s = 0; s < 3; s++)
                    {
                        float ya = y0 + step * s;
                        float yb = s == 2 ? y1 : ya + step;
                        b.AppendWallFace(d, c.x, c.z, 0f, 1f, ya, yb, RockInteriorUv, ya, yb - ya);
                    }
                }
            }
        }

        // 查找门两侧相邻的满格墙，使门楣与门框优先继承所在墙体的外观和颜色（如平滑花岗岩墙、加固防爆墙、飞船船体墙、砖墙等）。
        private Thing FindAdjacentWall(CellRect occ, bool horiz, out Material wallSrcMat)
        {
            wallSrcMat = null;
            int[] prefDirs = horiz ? new[] { 0, 1, 2, 3 } : new[] { 2, 3, 0, 1 };
            for (int k = 0; k < 4; k++)
            {
                int d = prefDirs[k];
                foreach (var c in occ)
                {
                    var n = c + DirOff[d];
                    if (occ.Contains(n) || !n.InBounds(map)) continue;
                    var ed = n.GetEdifice(map);
                    if (ed != null && WallModels3D.IsFullWallOrInWall(WallModels3D.ArchOf(ed)) && ed.Graphic != null)
                    {
                        try { wallSrcMat = ed.Graphic.MatAt(ed.Rotation, ed); } catch { }
                        if (wallSrcMat == null) { try { wallSrcMat = ed.Graphic.MatSingle; } catch { } }
                        if (wallSrcMat != null) return ed;
                    }
                }
            }
            return null;
        }

        // 门楣与 3D 门框：占满门格，门楣底在 DoorHeight、顶在 WallHeight；
        // 通行方向发门楣横带 + 左右门框立条，支撑方向在 inset 深度发立体门框内壁（jamb），
        // 外侧无邻墙时封死支撑侧外立面，彻底消除转角/端头门的悬空细丝与穿帮黑框。
        private void BakeDoorHeader(Building_Door door, Rot4 doorRot, CellRect occ, CellRect chunkRect, Material srcMat,
            Dictionary<Material, MeshBuilderX> builders, ref MeshBuilderX hull, float outline)
        {
            bool horiz = doorRot.IsHorizontal;
            Thing styleRef = FindAdjacentWall(occ, horiz, out Material adjMat) ?? (Thing)door;
            Material styleMat = adjMat ?? srcMat;
            string ap = AppearanceKey(styleRef);
            var b = BuilderFor(builders, WallAtlasMat(styleRef, styleMat, WallAtlasTex()));
            var top = WallModels3D.GetUv(ap, "top");
            var side = WallModels3D.GetUv(ap, "side");
            float h = Fps3DState.WallHeight;
            float y1 = baseY + h;
            float sill = baseY + Metrics3D.DoorHeight;
            float inset = (1f - Metrics3D.DoorWidth) * 0.5f;
            bool shell = Fps3DState.DrawOutlines;
            if (shell) hull = hull ?? new MeshBuilderX();
            var sh = hull;

            foreach (var c in occ)
            {
                if (!chunkRect.Contains(c)) continue;
                b.AppendWallTop(c.x, c.z, y1, top);
                if (shell) sh.AppendWallTopHull(c.x, c.z, y1, outline);
                // 门洞上沿底面：法线朝下，贴墙帽段（不发 BottomHull，避免在 2.02m 处翻出穿墙黑框）
                b.AppendWallBottom(c.x, c.z, sill, top);

                for (int d = 0; d < 4; d++)
                {
                    var n = c + DirOff[d];
                    if (occ.Contains(n)) continue; // 多格门内部相邻格跳过

                    bool isPassage = horiz ? (d >= 2) : (d < 2);
                    bool neighborSolid = SolidAt(n);

                    // 1. 门楣层 [sill, y1]：任何未被满格建筑遮挡的面都发射门楣横带
                    if (!neighborSolid)
                    {
                        b.AppendWallFace(d, c.x, c.z, 0f, 1f, sill, y1, side, baseY, h);
                        if (shell) sh.AppendWallFaceHull(d, c.x, c.z, 0f, 1f, sill, y1, sill, outline);
                    }

                    // 2. 门洞层 [baseY, sill]
                    if (isPassage)
                    {
                        if (!neighborSolid)
                        {
                            // 判断该通行面上 u=0 与 u=1 两端是否处于门占格的外端
                            // d=0(北): u=0 为东(+X), u=1 为西(-X)
                            // d=1(南): u=0 为西(-X), u=1 为东(+X)
                            // d=2(西): u=0 为北(-Z), u=1 为南(+Z)
                            // d=3(东): u=0 为南(+Z), u=1 为北(-Z)
                            bool hasU0 = (d == 0 && c.x == occ.maxX) || (d == 1 && c.x == occ.minX)
                                      || (d == 2 && c.z == occ.minZ) || (d == 3 && c.z == occ.maxZ);
                            bool hasU1 = (d == 0 && c.x == occ.minX) || (d == 1 && c.x == occ.maxX)
                                      || (d == 2 && c.z == occ.maxZ) || (d == 3 && c.z == occ.minZ);
                            if (hasU0) b.AppendWallFace(d, c.x, c.z, 0f, inset, baseY, sill, side, baseY, h);
                            if (hasU1) b.AppendWallFace(d, c.x, c.z, 1f - inset, 1f, baseY, sill, side, baseY, h);
                        }
                    }
                    else
                    {
                        // d 是支撑侧（门框端）：
                        // (a) 在距离该侧边界 inset (0.025m) 处向门洞中心 (d ^ 1) 发射立体门框内壁 (1.0m x 2.05m)
                        int dInward = d ^ 1;
                        b.AppendWallFaceOffset(dInward, c.x, c.z, 0f, 1f, baseY, sill, side, baseY, h, -(1f - inset));

                        // (b) 若支撑侧外部没有满格建筑遮挡（如转角门或独立门），发射外侧封口立面
                        if (!neighborSolid)
                        {
                            b.AppendWallFace(d, c.x, c.z, 0f, 1f, baseY, sill, side, baseY, h);
                            if (shell) sh.AppendWallFaceHull(d, c.x, c.z, 0f, 1f, baseY, sill, baseY, outline);
                        }
                    }
                }
            }
        }

        // 该格是否被满格建筑占据（墙/天然岩）。图外视为空气：
        // 图边墙的外侧面在流式邻图上看得见，不能剔。
        private bool SolidAt(IntVec3 c)
        {
            if (!c.InBounds(map)) return false;
            var ed = c.GetEdifice(map);
            if (MountainField3D.IsRock(ed))
            {
                if (mountain != null && mountain.Ready)
                {
                    int i = c.z * map.Size.x + c.x;
                    if (mountain.Cells[i] == 1)
                    {
                        float minH = Mathf.Min(mountain.Shape.CellMin(c.x, c.z), mountain.Ceilings[i]);
                        if (minH >= Fps3DState.WallHeight) return true;
                    }
                }
                return false;
            }
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

        private void EnsureSpecialLists()
        {
            if (!furnitureListDirty) return;
            furnitureListDirty = false;
            cachedFurniture.Clear();
            cachedWreckage.Clear();
            var all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null || !t.Spawned || t.def == null) continue;
                if (t.def.category != ThingCategory.Building) continue;
                if (Furniture3D.Handles(t)) cachedFurniture.Add(t);
                else if (Wreckage3D.Handles(t)) cachedWreckage.Add(t);
            }
        }

        // 家具逐件绘制（模型按 原型+朝向+占格 缓存，不并入 chunk 网格）。
        private void DrawFurniture()
        {
            if (!Furniture3D.Enabled) return;
            EnsureSpecialLists();
            for (int i = 0; i < cachedFurniture.Count; i++)
            {
                var t = cachedFurniture[i];
                if (t == null || !t.Spawned || t.def == null) continue;
                try
                {
                    if (t.Position.Fogged(map)) continue;
                    var dp = t.DrawPos;
                    var wp = new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z);
                    if (!Frustum3D.VisiblePoint(wp, 4f)) continue;
                    Furniture3D.TryDraw(t, wp, InstanceLit(t, dp));
                }
                catch { }
            }
        }

        // 残骸逐件绘制（模型按样板缓存，不并入 chunk 网格）。
        private void DrawWreckage()
        {
            if (!Wreckage3D.Enabled) return;
            EnsureSpecialLists();
            for (int i = 0; i < cachedWreckage.Count; i++)
            {
                var t = cachedWreckage[i];
                if (t == null || !t.Spawned || t.def == null) continue;
                try
                {
                    if (t.Position.Fogged(map)) continue;
                    var dp = t.DrawPos;
                    var wp = new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z);
                    // 残骸样板最大 6x4，取半径 4 保守放行，避免贴边误剔。
                    if (!Frustum3D.VisiblePoint(wp, 4f)) continue;
                    Wreckage3D.TryDraw(t, wp, InstanceLit(t, dp));
                }
                catch { }
            }
        }

        // 逐件对象的放置亮度（F013）：按图内局部位置取，灯具自身抬到自发光下限。
        private Color InstanceLit(Thing t, Vector3 mapLocal)
        {
            return Lighting3D.InstanceLevel(t, light, new Vector3(mapLocal.x, baseY, mapLocal.z));
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
            cachedFurniture.Clear();
            cachedWreckage.Clear();
            rebuildSeen.Clear();
        }

        // 顶点/UV/法线直写式网格构建器。
        //
        // 法线不交给 RecalculateNormals：每个面的外法线由本项目已验证的绕序直接算
        // （Cross(b-a, c-a)），与下面的 Nx/Nz 表同口径。光照（F013）按法线吃向阳项，
        // 符号必须由我们自己声明，不能依赖 Unity 内部约定。
        private class MeshBuilderX
        {
            private readonly List<Vector3> verts = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<int> tris = new List<int>();
            private readonly List<Color32> colors = new List<Color32>();

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
            public void AppendDoorPanel(float w, float h, float d, bool horiz, Vector4 uvRect, Matrix4x4 matrix)
            {
                float hw = w * 0.5f, hd = d * 0.5f;
                float u0 = uvRect.x, v0 = uvRect.y, u1 = uvRect.z, v1 = uvRect.w;
                // 门板窄边与顶面取门板左下方纯色区，避免把门把手或观察窗拉到窄边上
                float eu0 = Mathf.Lerp(u0, u1, 0.08f);
                float eu1 = Mathf.Lerp(u0, u1, 0.28f);
                float ev0 = Mathf.Lerp(v0, v1, 0.12f);
                float ev1 = Mathf.Lerp(v0, v1, 0.25f);
                var edgeBL = new Vector2(eu0, ev0);
                var edgeBR = new Vector2(eu1, ev0);
                var edgeTR = new Vector2(eu1, ev1);
                var edgeTL = new Vector2(eu0, ev1);

                // 顶面与底面
                Quad(new Vector3(-hw, h, hd), new Vector3(hw, h, hd), new Vector3(hw, h, -hd), new Vector3(-hw, h, -hd),
                    edgeBL, edgeBR, edgeTR, edgeTL, matrix);

                if (!horiz)
                {
                    // 门扇沿 X 轴展开（w = 门宽, d = 门厚）：北(-Z)与南(+Z)为双叶对称门面，西(-X)与东(+X)为窄边
                    // 北面 (-Z)：从东(+hw) -> 中(0) -> 西(-hw)
                    Quad(new Vector3(hw, 0, -hd), new Vector3(0f, 0, -hd), new Vector3(0f, h, -hd), new Vector3(hw, h, -hd),
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), matrix);
                    Quad(new Vector3(0f, 0, -hd), new Vector3(-hw, 0, -hd), new Vector3(-hw, h, -hd), new Vector3(0f, h, -hd),
                        new Vector2(u1, v0), new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), matrix);
                    // 南面 (+Z)：从西(-hw) -> 中(0) -> 东(+hw)
                    Quad(new Vector3(-hw, 0, hd), new Vector3(0f, 0, hd), new Vector3(0f, h, hd), new Vector3(-hw, h, hd),
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), matrix);
                    Quad(new Vector3(0f, 0, hd), new Vector3(hw, 0, hd), new Vector3(hw, h, hd), new Vector3(0f, h, hd),
                        new Vector2(u1, v0), new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), matrix);
                    // 西窄边 (-X) 与 东窄边 (+X)
                    Quad(new Vector3(-hw, 0, -hd), new Vector3(-hw, 0, hd), new Vector3(-hw, h, hd), new Vector3(-hw, h, -hd),
                        edgeBL, edgeBR, edgeTR, edgeTL, matrix);
                    Quad(new Vector3(hw, 0, hd), new Vector3(hw, 0, -hd), new Vector3(hw, h, -hd), new Vector3(hw, h, hd),
                        edgeBL, edgeBR, edgeTR, edgeTL, matrix);
                }
                else
                {
                    // 门扇沿 Z 轴展开（d = 门宽, w = 门厚）：西(-X)与东(+X)为双叶对称门面，北(-Z)与南(+Z)为窄边
                    // 西面 (-X)：从北(-hd) -> 中(0) -> 南(+hd)
                    Quad(new Vector3(-hw, 0, -hd), new Vector3(-hw, 0, 0f), new Vector3(-hw, h, 0f), new Vector3(-hw, h, -hd),
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), matrix);
                    Quad(new Vector3(-hw, 0, 0f), new Vector3(-hw, 0, hd), new Vector3(-hw, h, hd), new Vector3(-hw, h, 0f),
                        new Vector2(u1, v0), new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), matrix);
                    // 东面 (+X)：从南(+hd) -> 中(0) -> 北(-hd)
                    Quad(new Vector3(hw, 0, hd), new Vector3(hw, 0, 0f), new Vector3(hw, h, 0f), new Vector3(hw, h, hd),
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), matrix);
                    Quad(new Vector3(hw, 0, 0f), new Vector3(hw, 0, -hd), new Vector3(hw, h, -hd), new Vector3(hw, h, 0f),
                        new Vector2(u1, v0), new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), matrix);
                    // 北窄边 (-Z) 与 南窄边 (+Z)
                    Quad(new Vector3(hw, 0, -hd), new Vector3(-hw, 0, -hd), new Vector3(-hw, h, -hd), new Vector3(hw, h, -hd),
                        edgeBL, edgeBR, edgeTR, edgeTL, matrix);
                    Quad(new Vector3(-hw, 0, hd), new Vector3(hw, 0, hd), new Vector3(hw, h, hd), new Vector3(-hw, h, hd),
                        edgeBL, edgeBR, edgeTR, edgeTL, matrix);
                }
            }

            public void AppendWallFace(int dir, float x, float z, float ua, float ub,
                float ya, float yb, Vector4 r, float vBase, float vSpan)
            {
                AppendWallFaceOffset(dir, x, z, ua, ub, ya, yb, r, vBase, vSpan, 0f);
            }

            public void AppendWallFaceOffset(int dir, float x, float z, float ua, float ub,
                float ya, float yb, Vector4 r, float vBase, float vSpan, float outBias)
            {
                float va = (ya - vBase) / vSpan, vb = (yb - vBase) / vSpan;
                Quad(FacePoint(dir, x, z, ua, ya, outBias), FacePoint(dir, x, z, ub, ya, outBias),
                    FacePoint(dir, x, z, ub, yb, outBias), FacePoint(dir, x, z, ua, yb, outBias),
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

            public void AppendFromModelBuilder(ModelBuilder mb, MeshBuilderX hullBuilder, float hullWidth)
            {
                if (mb == null) return;
                mb.AppendToLists(
                    verts, normals, uvs, tris,
                    hullBuilder?.verts, hullBuilder?.normals, hullBuilder?.uvs, hullBuilder?.tris,
                    hullWidth);
            }

            private void Quad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl,
                Vector2 uvBL, Vector2 uvBR, Vector2 uvTR, Vector2 uvTL, Matrix4x4 matrix)
            {
                int b = verts.Count;
                var a0 = matrix.MultiplyPoint3x4(bl);
                var a1 = matrix.MultiplyPoint3x4(br);
                var a2 = matrix.MultiplyPoint3x4(tr);
                var a3 = matrix.MultiplyPoint3x4(tl);
                verts.Add(a0); verts.Add(a1); verts.Add(a2); verts.Add(a3);
                // 本项目约定：三角形 (0,1,2) 为正面，外法线 = Cross(v1-v0, v2-v0)。
                var n = Vector3.Cross(a1 - a0, a2 - a0);
                if (n.sqrMagnitude < 1e-8f) n = Vector3.up;
                n = n.normalized;
                normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
                uvs.Add(uvBL); uvs.Add(uvBR); uvs.Add(uvTR); uvs.Add(uvTL);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }

            /// <summary>
            /// 出网格。field 非空时逐顶点烘光照进顶点色 RGB（F013）；
            /// 为空（描边壳、光照关闭）时保持原来的不写颜色通道，画面与光照落地前一致。
            /// </summary>
            public Mesh ToMesh(LightField3D field)
            {
                if (verts.Count == 0) return null;
                var mesh = new Mesh();
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                if (field != null)
                {
                    colors.Clear();
                    for (int i = 0; i < verts.Count; i++)
                        colors.Add(Lighting3D.To32(field.Shade(verts[i], normals[i], true)));
                    mesh.SetColors(colors);
                }
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
