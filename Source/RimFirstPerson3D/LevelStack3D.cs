using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 多层堆叠：显式栈注册表（模组挂接的第二张 Map），未注册地图回退按 Tile 分组。
    // 楼层 = 栈内顺序，Y = 楼层 * 层高。
    public static class LevelStack3D
    {
        private static readonly Dictionary<int, int> stackOfMap = new Dictionary<int, int>();
        private static readonly Dictionary<int, List<Map>> stackMaps = new Dictionary<int, List<Map>>();
        private static int nextStackId = 1;

        // 把 upperMap 挂到 baseMap 所在栈顶（含把 baseMap 的 Tile 回退组冻结为显式栈）。
        public static bool RegisterStack(Map baseMap, Map upperMap)
        {
            if (baseMap == null || upperMap == null || baseMap == upperMap) return false;
            if (stackOfMap.ContainsKey(upperMap.uniqueID)) return false;
            var list = new List<Map>();
            // 只取同栈既有地图；上层图与底层图共用世界格，Tile 回退分组会把它也列出，
            // 必须先排除，否则会被误判为"已在栈内"而注册失败。
            foreach (var m in StackMapsAt(baseMap))
                if (m != null && m != upperMap) list.Add(m);
            if (list.Contains(upperMap)) return false;
            list.Add(upperMap);
            int sid = nextStackId++;
            stackMaps[sid] = list;
            foreach (var m in list) stackOfMap[m.uniqueID] = sid;
            return true;
        }

        // 当前地图所在栈的全部地图（按楼层从低到高）。
        public static List<Map> StackMapsAt(Map map)
        {
            if (map != null && stackOfMap.TryGetValue(map.uniqueID, out var sid)
                && stackMaps.TryGetValue(sid, out var list))
                return list;
            var l = new List<Map>();
            if (map == null) return l;
            // 仅对拥有有效世界格的地图做 Tile 回退分组；口袋图的 Tile 无效（-1），
            // 比较它会走进 PlanetLayer 的越界查询并每帧刷错误，故只把自己计入。
            bool validTile = map.Tile.Valid;
            if (!validTile)
            {
                l.Add(map);
                return l;
            }
            foreach (var m in Find.Maps)
                if (m.Tile.Valid && m.Tile == map.Tile) l.Add(m);
            l.Sort((a, b) => a.uniqueID.CompareTo(b.uniqueID));
            return l;
        }

        public static int LevelOf(Map map)
        {
            var stack = StackMapsAt(map);
            int idx = stack.IndexOf(map);
            return Mathf.Max(0, idx);
        }

        public static float LevelBaseY(Map map)
        {
            return LevelOf(map) * Fps3DState.StoryHeight;
        }

        // 切换到同栈的上/下一层（返回是否切换成功）。
        public static bool SwitchLevel(int delta)
        {
            var cur = Find.CurrentMap;
            if (cur == null) return false;
            var stack = StackMapsAt(cur);
            int idx = stack.IndexOf(cur);
            int next = idx + delta;
            if (next < 0 || next >= stack.Count) return false;
            Current.Game.CurrentMap = stack[next];
            return true;
        }

        // ---- 存档：导出为平行数组（地图 ID + 所属栈 ID） ----

        public static void CaptureSaveData(List<int> mapIds, List<int> groupIds)
        {
            foreach (var kv in stackMaps)
            {
                var list = kv.Value;
                if (list == null) continue;
                foreach (var m in list)
                {
                    if (m == null || m.Disposed) continue;
                    mapIds.Add(m.uniqueID);
                    groupIds.Add(kv.Key);
                }
            }
        }

        public static void RestoreFromSaveData(List<int> mapIds, List<int> groupIds, IReadOnlyList<Map> maps)
        {
            if (mapIds == null || groupIds == null || maps == null) return;
            int n = Mathf.Min(mapIds.Count, groupIds.Count);
            var lookup = new Dictionary<int, Map>(maps.Count);
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i] != null && !maps[i].Disposed) lookup[maps[i].uniqueID] = maps[i];
            }
            for (int i = 0; i < n; i++)
            {
                if (!lookup.TryGetValue(mapIds[i], out var map) || map == null) continue;
                int sid = groupIds[i];
                if (!stackMaps.TryGetValue(sid, out var list))
                {
                    list = new List<Map>();
                    stackMaps[sid] = list;
                }
                if (!list.Contains(map)) list.Add(map);
                stackOfMap[map.uniqueID] = sid;
                if (sid >= nextStackId) nextStackId = sid + 1;
            }
            // 保持存档写入顺序（即注册时的楼层顺序），不做重排。
        }

        public static void ResetAll()
        {
            stackOfMap.Clear();
            stackMaps.Clear();
            nextStackId = 1;
        }

        // 生成叠层上层图（同世界格的口袋图）。
        // MapInfo.Tile 取 parent.Tile：parent 为 null 时上层图的 Tile 无效，
        // 原版每帧的日期/温度/天空读取会持续报 "invalid tile -1"。
        // 因此必须给它一个带有效世界格、且随存档保存的父级。
        public static Map CreateUpperMap(Map baseMap, IntVec3 size, MapGeneratorDef generatorDef)
        {
            if (baseMap == null || !baseMap.Tile.Valid) return null;
            var parent = WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.PocketMap) as PocketMapParent;
            if (parent == null) return null;
            parent.sourceMap = baseMap;
            parent.mapGenerator = generatorDef;
            parent.Tile = baseMap.Tile;          // 与底层图共用世界格，保证 Tile 有效
            Find.World.pocketMaps.Add(parent);   // 随 World 存档，读档后父级引用仍在
            return MapGenerator.GenerateMap(size, parent, generatorDef, null, null,
                isPocketMap: true);
        }
    }
}
