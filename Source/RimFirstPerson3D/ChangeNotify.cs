// AI生成
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimFirstPerson3D
{
    // 捕捉地形/物件增删事件，转发给对应地图的场景。
    public static class ChangeNotify
    {
        // 门的开合状态缓存（thingIDNumber -> 上次见到的 Open）。
        // 只用于检测"刚刚变化"，不参与渲染；门还会自己自动关闭（Tick 里），必须单独盯。
        private static readonly Dictionary<int, bool> doorOpen = new Dictionary<int, bool>();

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.SetTerrain)),
                postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterSetTerrain)));
            harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterSpawnSetup)));
            harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn)),
                prefix: new HarmonyMethod(typeof(ChangeNotify), nameof(BeforeDeSpawn)));
            harmony.Patch(AccessTools.Method(typeof(RoofGrid), nameof(RoofGrid.SetRoof)),
                prefix: new HarmonyMethod(typeof(ChangeNotify), nameof(BeforeSetRoof)),
                postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterSetRoof)));
            harmony.Patch(AccessTools.Method(typeof(RoofGrid), nameof(RoofGrid.RemoveRoofUnsafe)),
                postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterRemoveRoof)));

            // 门开/关既不是 Spawn 也不是 DeSpawn。Building_Door.Tick 是 protected，
            // 改挂它自己改状态的私有方法 DoorOpen / DoorTryClose（只影响门，不给全地图 Thing 加回调）。
            var doorOpenM = AccessTools.Method(typeof(Building_Door), "DoorOpen");
            if (doorOpenM != null)
                harmony.Patch(doorOpenM,
                    postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterDoorOpenChanged)));
            var doorTryCloseM = AccessTools.Method(typeof(Building_Door), "DoorTryClose");
            if (doorTryCloseM != null)
                harmony.Patch(doorTryCloseM,
                    postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterDoorOpenChanged)));

            // 尸体腐烂阶段改变（Fresh -> Rotting -> Dessicated），精准标脏触发重新解析外观，无需轮询
            var rotStageChangedM = AccessTools.Method(typeof(Corpse), nameof(Corpse.RotStageChanged));
            if (rotStageChangedM != null)
                harmony.Patch(rotStageChangedM,
                    postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterRotStageChanged)));
        }

        private static void AfterRotStageChanged(Corpse __instance)
        {
            if (__instance == null) return;
            SceneRegistry.NotifyThing(__instance.MapHeld, __instance);
        }

        private static void AfterDoorOpenChanged(Building_Door __instance)
        {
            if (__instance == null) return;
            int id = __instance.thingIDNumber;
            bool open = __instance.Open;
            if (doorOpen.TryGetValue(id, out var prev) && prev == open) return;
            if (doorOpen.Count > 8192) doorOpen.Clear();
            doorOpen[id] = open;
            SceneRegistry.NotifyThing(__instance.MapHeld, __instance);
        }

        // 手动开关门后立刻标脏，不等下一 Tick 的状态比对，手感即时。
        public static void NotifyDoorToggled(Building_Door door)
        {
            if (door == null) return;
            doorOpen[door.thingIDNumber] = door.Open;
            SceneRegistry.NotifyThing(door.MapHeld, door);
        }

        private static void BeforeSetRoof(RoofGrid __instance, IntVec3 c, RoofDef def, out bool __state)
        {
            __state = __instance.RoofAt(c) != def;
        }

        private static void AfterSetRoof(Map ___map, IntVec3 c, bool __state)
        {
            if (__state) SceneRegistry.NotifyRoof(___map, c);
        }

        private static void AfterRemoveRoof(Map ___map, int index)
        {
            if (___map != null && ___map.Size.x > 0)
                SceneRegistry.NotifyRoof(___map, new IntVec3(index % ___map.Size.x, 0, index / ___map.Size.x));
        }

        private static void AfterSetTerrain(Map ___map, IntVec3 c)
        {
            SceneRegistry.NotifyTerrain(___map, c);
        }

        private static void AfterSpawnSetup(Thing __instance, Map map)
        {
            SceneRegistry.NotifyThing(map, __instance);
        }

        private static void BeforeDeSpawn(Thing __instance)
        {
            SceneRegistry.NotifyThing(__instance.MapHeld, __instance);
        }
    }

    // 当前会话所有活跃场景的注册表（每 Map 一个 Scene3D）。
    public static class SceneRegistry
    {
        private static readonly Dictionary<Map, Scene3D> scenes = new Dictionary<Map, Scene3D>();

        public static IEnumerable<Scene3D> All => scenes.Values;

        public static Scene3D GetOrCreate(Map map)
        {
            if (map == null || map.Disposed) return null;
            if (!scenes.TryGetValue(map, out var scene) || scene == null)
            {
                scene = new Scene3D(map);
                scenes[map] = scene;
            }
            return scene;
        }

        public static Scene3D Find(Map map)
        {
            if (map == null) return null;
            scenes.TryGetValue(map, out var scene);
            return scene;
        }

        // 卸载指定地图的场景（M4 流式卸载用）。
        public static void DisposeOf(Map map)
        {
            if (map == null) return;
            if (scenes.TryGetValue(map, out var s))
            {
                s?.Dispose();
                scenes.Remove(map);
            }
        }

        public static void NotifyTerrain(Map map, IntVec3 c)
        {
            Find(map)?.NotifyTerrainChanged(c);
            InvalidateMountainCaps();
        }

        public static void NotifyRoof(Map map, IntVec3 c)
        {
            map?.GetComponent<MountainField3D>()?.NotifyCell(c);
            Find(map)?.NotifyRoofChanged(c);
            InvalidateMountainCaps();
        }

        private static void InvalidateMountainCaps()
        {
            foreach (var pair in scenes) pair.Value.Mountain.InvalidateCaps();
        }

        public static void NotifyThing(Map map, Thing t)
        {
            if (t?.def?.category == ThingCategory.Building)
            {
                map?.GetComponent<MountainField3D>()?.NotifyThing(t);
                InvalidateMountainCaps();
            }
            Find(map)?.NotifyThingChanged(t);
        }

        // 清理已卸载地图的场景。
        public static void PruneDisposed()
        {
            List<Map> dead = null;
            foreach (var kv in scenes)
            {
                if (kv.Key == null || kv.Key.Disposed)
                {
                    (dead ?? (dead = new List<Map>())).Add(kv.Key);
                }
            }
            if (dead == null) return;
            foreach (var m in dead)
            {
                scenes[m]?.Dispose();
                scenes.Remove(m);
            }
        }

        public static void DisposeAll()
        {
            foreach (var scene in scenes.Values) scene?.Dispose();
            scenes.Clear();
        }
    }
}
