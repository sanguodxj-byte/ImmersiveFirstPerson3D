using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace RimFirstPerson3D
{
    // 捕捉地形/物件增删事件，转发给对应地图的场景。
    public static class ChangeNotify
    {
        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(TerrainGrid), nameof(TerrainGrid.SetTerrain)),
                postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterSetTerrain)));
            harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.SpawnSetup)),
                postfix: new HarmonyMethod(typeof(ChangeNotify), nameof(AfterSpawnSetup)));
            harmony.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawn)),
                prefix: new HarmonyMethod(typeof(ChangeNotify), nameof(BeforeDeSpawn)));
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
        }

        public static void NotifyThing(Map map, Thing t)
        {
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
