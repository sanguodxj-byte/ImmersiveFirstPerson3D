// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // M4 邻接图无缝流式：为流式进来的邻接地图登记世界水平偏移，
    // 提供跨图边界行走传送与按需卸载。渲染组 = 垂直栈（LevelStack3D）∪ streamed。
    public static class Streaming3D
    {
        private static readonly Dictionary<int, Vector3> placement = new Dictionary<int, Vector3>();
        private static readonly List<Map> streamed = new List<Map>();
        // 全部已登记世界位置的地图（主图=零偏移 + 流式邻图）：
        // 渲染集与跨图寻的都以它为准，否则跨到邻图后主图会掉出渲染集、
        // 且西边界找不到零偏移目标回不了家。
        private static readonly Dictionary<int, Map> byId = new Dictionary<int, Map>();
        private static int homeMapId = -1;
        // 流式注册时刻：地图生成同帧的物件事数据未走完首个 tick，
        // 立即烘场景会把坏数据永久烤进网格（表现为该图整片渲染黑块，
        // 只有整场景重建才能恢复）。新图延迟到 0.6s 后才允许建场景。
        private static readonly Dictionary<int, float> streamedAt = new Dictionary<int, float>();
        private const float SceneBuildDelay = 0.6f;

        public static Vector3 OffsetOf(Map map)
        {
            if (map != null && placement.TryGetValue(map.uniqueID, out var o)) return o;
            return Vector3.zero;
        }

        public static void RegisterPlacement(Map map, Vector3 offset)
        {
            if (map == null) return;
            placement[map.uniqueID] = offset;
            byId[map.uniqueID] = map;
            if (!streamed.Contains(map)) streamed.Add(map);
            streamedAt[map.uniqueID] = Time.time;
            // 丢弃同帧可能已创建的场景，延迟后按干净数据重建
            SceneRegistry.DisposeOf(map);
        }

        // 场景是否可建：非流式图恒 true；流式图过了延迟期才 true。
        public static bool SceneReady(Map map)
        {
            if (map == null) return false;
            return !streamedAt.TryGetValue(map.uniqueID, out var t)
                || Time.time - t >= SceneBuildDelay;
        }

        // 渲染集合：当前地图垂直栈 + 全部已登记世界位置地图（主图与流式邻图对称）。
        public static List<Map> RenderMaps(Map current)
        {
            var list = new List<Map>();
            if (current != null && !current.Disposed && SceneReady(current)) list.Add(current);
            foreach (var m in LevelStack3D.StackMapsAt(current))
                if (m != null && !m.Disposed && SceneReady(m) && !list.Contains(m)) list.Add(m);
            foreach (var kv in byId)
            {
                var m = kv.Value;
                if (m == null || m.Disposed || !SceneReady(m)) continue;
                if (!list.Contains(m)) list.Add(m);
            }
            return list;
        }

        // 东向流式：在当前 tile 的正东真实邻接 tile 上生成 100x100 常规地图
        //（GetOrGenerateMap 走真实 tile——无 Tile=-1 的 GenStep 全家桶崩溃），
        // 边界内侧放可辨识的木地板+围墙平台。
        public static void StreamEast()
        {
            var cur = Find.CurrentMap;
            if (cur == null) return;
            if (streamed.Count > 0)
            {
                Log.Message(ModEntry.LogPrefix + "M4 stream: already streamed " + streamed.Count);
                return;
            }
            // 取世界 +X 方向最贴近的邻接 tile 作为"东邻"
            var layer = cur.Tile.Layer;
            var center = layer.GetTileCenter(cur.Tile);
            var east = RimWorld.Planet.PlanetTile.Invalid;
            float best = -2f;
            int n = layer.GetTileNeighborCount(cur.Tile);
            for (int i = 0; i < n; i++)
            {
                var nb = layer.GetTileNeighbor(cur.Tile, i);
                var v = layer.GetTileCenter(nb) - center;
                float score = v.x / Mathf.Max(0.001f, v.magnitude);
                if (score > best)
                {
                    best = score;
                    east = nb;
                }
            }
            if (!east.Valid)
            {
                Log.Message(ModEntry.LogPrefix + "M4 stream: no neighbor tile found");
                return;
            }
            // 用模组自定义的 StreamNode 世界对象（纯 MapParent，基类 ShouldRemoveMapNow
            // 恒 false 不会被 vanilla 自动废弃；Camp def 会因无人自动移除已弃用）。
            // MapParent.MapGeneratorDef 无 mapGenerator 时回落 Encounter（常规地表）。
            var nodeDef = DefDatabase<WorldObjectDef>.GetNamed("FP3D_StreamNode", false);
            if (nodeDef == null)
            {
                Log.Message(ModEntry.LogPrefix + "M4 stream: FP3D_StreamNode def not loaded");
                return;
            }
            var nm = GetOrGenerateMapUtility.GetOrGenerateMap(
                east, new IntVec3(100, 1, 100), nodeDef);
            // 主图登记为零偏移世界地图：进入邻图后主图仍在渲染集，
            // 西边界（邻图 x<=0）的 MapAtOffset 才找得到回家的目标。
            if (homeMapId == -1)
            {
                homeMapId = cur.uniqueID;
                RegisterPlacement(cur, Vector3.zero);
                SceneRegistry.DisposeOf(cur); // 主图场景推倒重建，保证数据干净
            }
            // 新生成的图默认全图战争迷雾：提前全图开雾，避免迷雾面片参与渲染，
            // 也让流式区域对玩家直接可见（无缝大地图语义下无需迷雾）。
            int fogged = 0;
            foreach (var c in nm.AllCells)
                if (c.Fogged(nm)) fogged++;
            nm.fogGrid.ClearAllFog();
            var offset = new Vector3(cur.Size.x, 0, 0);
            RegisterPlacement(nm, offset);
            int walls = 0;
            for (int x = 4; x <= 13; x++)
                for (int z = 46; z <= 55; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (!c.InBounds(nm)) continue;
                    try
                    {
                        if (nm.terrainGrid != null)
                            nm.terrainGrid.SetTerrain(c, TerrainDefOf.WoodPlankFloor);
                        if ((x == 4 || x == 13 || z == 46 || z == 55) && c.Walkable(nm))
                        {
                            // ThingDefOf.Wall 是 madeFromStuff：不带材质会让 ThingoMaker 走
                            // "stuff=null. Assigning default." 错误分支（每次生成刷一条红字）。
                            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), c, nm);
                            walls++;
                        }
                    }
                    catch (System.Exception e)
                    {
                        Log.Warning(ModEntry.LogPrefix + "M4 place " + c + ": " + e.Message);
                    }
                }
            Log.Message(ModEntry.LogPrefix + "M4 stream east ok: map id=" + nm.uniqueID
                + " offset=(" + offset.x.ToString("F0") + ",0," + offset.z.ToString("F0")
                + ") walls=" + walls + " foggedBefore=" + fogged);
        }

        // 卸载全部流式邻图（当前地图不在流式列表时才允许）。
        public static void UnloadStreamed()
        {
            var cur = Find.CurrentMap;
            // 主图也登记在世界位置表中，站在主图上卸载邻图属正常路径；只有站在流式邻图上才拒绝。
            if (cur != null && cur.uniqueID != homeMapId
                && streamed.Contains(cur))
            {
                Log.Message(ModEntry.LogPrefix + "M4 unload: standing on streamed map, refused");
                return;
            }
            foreach (var m in streamed)
            {
                if (m == null || m.Disposed) continue;
                if (m.uniqueID == homeMapId)
                {
                    // 主图只是登记了世界位置，不属于可卸载的流式邻图
                    placement.Remove(m.uniqueID);
                    streamedAt.Remove(m.uniqueID);
                    byId.Remove(m.uniqueID);
                    continue;
                }
                try
                {
                    SceneRegistry.DisposeOf(m);
                    Current.Game.DeinitAndRemoveMap(m, false);
                    Log.Message(ModEntry.LogPrefix + "M4 unloaded map " + m.uniqueID);
                }
                catch (System.Exception e)
                {
                    Log.Warning(ModEntry.LogPrefix + "M4 unload map " + m.uniqueID + ": " + e.Message);
                }
                placement.Remove(m.uniqueID);
                streamedAt.Remove(m.uniqueID);
                byId.Remove(m.uniqueID);
            }
            streamed.Clear();
            homeMapId = -1;
        }

        // 跨图边界行走：附身 pawn 走到当前图边缘且有对应流式邻图时传送过去。
        public static bool TryCrossBoundary(Pawn p)
        {
            var st = FirstPerson3DMod.settings;
            if (p == null || !p.Spawned || p.CarriedBy != null) { if (st != null) st.diagCross = "p-invalid"; return false; }
            var cur = p.MapHeld;
            if (cur == null) { if (st != null) st.diagCross = "no-map"; return false; }
            var o = OffsetOf(cur);
            var c = p.Position;
            // 按世界坐标边界精确匹配邻居（兼容不同尺寸地图：
            // 邻图偏移差不一定等于 cur.Size，主图 250 宽 vs 邻图 100 宽）。
            Map target = null;
            float wx = o.x + c.x + 0.5f, wz = o.z + c.z + 0.5f;
            foreach (var kv in byId)
            {
                var m = kv.Value;
                if (m == null || m.Disposed || m == cur) continue;
                var mo = OffsetOf(m);
                bool zOverlap = wz >= mo.z - 1f && wz <= mo.z + m.Size.z + 1f;
                bool xOverlap = wx >= mo.x - 1f && wx <= mo.x + m.Size.x + 1f;
                if (c.x >= cur.Size.x - 1 && Mathf.Abs(mo.x - (o.x + cur.Size.x)) < 1f && zOverlap) { target = m; break; }
                if (c.x <= 0 && Mathf.Abs((mo.x + m.Size.x) - o.x) < 1f && zOverlap) { target = m; break; }
                if (c.z >= cur.Size.z - 1 && Mathf.Abs(mo.z - (o.z + cur.Size.z)) < 1f && xOverlap) { target = m; break; }
                if (c.z <= 0 && Mathf.Abs((mo.z + m.Size.z) - o.z) < 1f && xOverlap) { target = m; break; }
            }
            if (target == null || target.Disposed)
            {
                if (st != null)
                {
                    string detail = "streamed=0";
                    if (streamed.Count > 0)
                    {
                        var m0 = streamed[0];
                        detail = "m0=id" + m0.uniqueID + " disp=" + m0.Disposed
                            + " placed=" + (placement.ContainsKey(m0.uniqueID) ? OffsetOf(m0).ToString("F0") : "none");
                    }
                    st.diagCross = "no-target c=" + c + " " + detail;
                }
                return false;
            }

            var to = OffsetOf(target);
            var nc = new IntVec3(
                Mathf.Clamp(c.x - Mathf.RoundToInt(to.x - o.x), 2, target.Size.x - 3),
                0,
                Mathf.Clamp(c.z - Mathf.RoundToInt(to.z - o.z), 2, target.Size.z - 3));
            // 落点开避搜索：找 nc 附近"可站立且 3x3 内无建筑"的开阔格，
            // 避免传送进遗址建筑内部导致第一人称相机被墙体几何体包围。
            var found = IntVec3.Invalid;
            for (int r = 0; r <= 8 && found == IntVec3.Invalid; r++)
            {
                foreach (var c2 in CellRect.CenteredOn(nc, r).ClipInsideMap(target))
                {
                    if (!c2.Standable(target) || c2.Fogged(target)) continue;
                    bool clear = true;
                    foreach (var t in target.thingGrid.ThingsAt(c2))
                        if (t.def != null && t.def.Fillage != FillCategory.None) { clear = false; break; }
                    if (!clear) continue;
                    // 3x3 邻域也无建筑，保证相机不被墙体贴脸
                    for (int dx = -1; dx <= 1 && clear; dx++)
                        for (int dz = -1; dz <= 1 && clear; dz++)
                        {
                            var c3 = c2 + new IntVec3(dx, 0, dz);
                            if (!c3.InBounds(target)) continue;
                            foreach (var t3 in target.thingGrid.ThingsAt(c3))
                                if (t3.def != null && t3.def.Fillage == FillCategory.Full) { clear = false; break; }
                        }
                    if (clear) { found = c2; break; }
                }
            }
            if (found != IntVec3.Invalid) nc = found;
            bool wasDrafted = p.Drafted; // DeSpawn/Spawn 会掉征召，跨图后恢复以保证 WASD 连续
            p.DeSpawn();
            GenSpawn.Spawn(p, nc, target);
            if (wasDrafted && p.drafter != null) p.drafter.Drafted = true;
            Current.Game.CurrentMap = target;
            if (st != null) st.diagCross = "crossed -> map " + target.uniqueID + " at " + nc + " streamed=" + streamed.Count;
            Log.Message(ModEntry.LogPrefix + "M4 crossed boundary -> map " + target.uniqueID
                + " at " + nc);
            return true;
        }

        // 世界邻图登记：把世界上"已经生成"的邻接地图登记进偏移表，供 TryCrossBoundary
        // 在走到边缘前就能寻到目标。本函数只补登记，不生成新图（不调用 GetOrGenerateMap），
        // 也不写第二套跨边逻辑——落点/传送仍由 TryCrossBoundary 按登记偏移处理。
        public static void EnsureNeighbors(Map cur)
        {
            if (cur == null || cur.Disposed) return;
            // 主图只登记一次：homeMapId 为 -1 时按零偏移登记当前图（等价 StreamEast 的主图登记）。
            // 已在 byId 里的图不要再次 RegisterPlacement——那会拆掉场景并重新计 0.6s 延迟。
            if (homeMapId == -1)
            {
                homeMapId = cur.uniqueID;
                if (!byId.ContainsKey(cur.uniqueID))
                    RegisterPlacement(cur, Vector3.zero);
            }
            // 世界格无效（如口袋图）没有邻接 tile，跳过邻图登记。
            if (!cur.Tile.Valid) return;
            var layer = cur.Tile.Layer;
            if (layer == null) return;
            var curOff = OffsetOf(cur);
            var center = layer.GetTileCenter(cur.Tile);
            int n = layer.GetTileNeighborCount(cur.Tile);
            for (int i = 0; i < n; i++)
            {
                var nb = layer.GetTileNeighbor(cur.Tile, i);
                var nm = Current.Game.FindMap(nb);
                // 邻接 tile 上还没有地图：不生成、跳过（本任务不调用 GetOrGenerateMap）。
                if (nm == null || nm.Disposed || nm == cur) continue;
                // 该地图已在 byId 里：不改它的偏移。
                if (byId.ContainsKey(nm.uniqueID)) continue;
                // 放置方向取星球空间差值 v 的主轴，与 StreamEast 把"星球 +X"当东边同一近似。
                var v = layer.GetTileCenter(nb) - center;
                Vector3 off;
                if (Mathf.Abs(v.x) >= Mathf.Abs(v.z))
                {
                    if (v.x > 0) off = curOff + new Vector3(cur.Size.x, 0f, 0f);
                    else off = curOff - new Vector3(nm.Size.x, 0f, 0f);
                }
                else
                {
                    if (v.z > 0) off = curOff + new Vector3(0f, 0f, cur.Size.z);
                    else off = curOff - new Vector3(0f, 0f, nm.Size.z);
                }
                // 同一侧若已被另一张已登记地图占用，跳过，避免两张图叠到同一坐标。
                if (HasPlacementNear(off))
                {
                    Log.Message(ModEntry.LogPrefix + "M4 ensureNeighbors: slot ("
                        + off.x.ToString("F0") + ",0," + off.z.ToString("F0")
                        + ") occupied, skip map " + nm.uniqueID);
                    continue;
                }
                RegisterPlacement(nm, off);
                Log.Message(ModEntry.LogPrefix + "M4 ensureNeighbors: map " + nm.uniqueID
                    + " offset=(" + off.x.ToString("F0") + ",0," + off.z.ToString("F0") + ")");
            }
        }

        // 偏移表里是否已有地图占在 o 附近（同一侧占用检测）。
        private static bool HasPlacementNear(Vector3 o)
        {
            foreach (var kv in byId)
            {
                var m = kv.Value;
                if (m == null || m.Disposed) continue;
                if (!placement.TryGetValue(m.uniqueID, out var po)) continue;
                if ((po - o).sqrMagnitude < 1f) return true;
            }
            return false;
        }

        private static Map MapAtOffset(Vector3 o)
        {
            foreach (var m in streamed)
                if (m != null && !m.Disposed && placement.TryGetValue(m.uniqueID, out var mo)
                    && (mo - o).sqrMagnitude < 1f)
                    return m;
            return null;
        }

        // ---- 存档：导出地图 ID 与世界水平偏移（Vector3 只取 x/z） ----

        public static void CaptureSaveData(List<int> ids, List<float> offX, List<float> offZ, ref int homeId)
        {
            ids.Clear();
            offX.Clear();
            offZ.Clear();
            foreach (var kv in placement)
            {
                var m = byId.TryGetValue(kv.Key, out var map) ? map : null;
                if (m == null || m.Disposed) continue;
                ids.Add(kv.Key);
                offX.Add(kv.Value.x);
                offZ.Add(kv.Value.z);
            }
            homeId = homeMapId;
        }

        public static void RestoreFromSaveData(
            List<int> ids, List<float> offX, List<float> offZ, int homeId, IReadOnlyList<Map> maps)
        {
            if (ids == null || maps == null) return;
            var lookup = new Dictionary<int, Map>(maps.Count);
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && !maps[i].Disposed) lookup[maps[i].uniqueID] = maps[i];
            }
            for (int i = 0; i < ids.Count; i++)
            {
                if (!lookup.TryGetValue(ids[i], out var map) || map == null) continue;
                float x = (offX != null && i < offX.Count) ? offX[i] : 0f;
                float z = (offZ != null && i < offZ.Count) ? offZ[i] : 0f;
                placement[ids[i]] = new Vector3(x, 0f, z);
                byId[ids[i]] = map;
                if (!streamed.Contains(map)) streamed.Add(map);
            }
            homeMapId = homeId;
            // 读档不走 RegisterPlacement 的 0.6s 延迟：地图数据已完整落盘，
            // 但同帧仍可能被加载收尾改动，这里统一给一次短暂延迟。
            float now = Time.time;
            for (int i = 0; i < ids.Count; i++) streamedAt[ids[i]] = now;
        }

        public static void ResetAll()
        {
            placement.Clear();
            streamed.Clear();
            byId.Clear();
            streamedAt.Clear();
            homeMapId = -1;
        }
    }
}
