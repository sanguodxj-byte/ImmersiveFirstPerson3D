// AI生成
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 供 GABS execute_debug_action 驱动的调试入口（DevMode 菜单 FirstPerson3D 分组）。
    public static class DebugActions3D
    {
        [DebugAction("FirstPerson3D", "toggle view mode (F8)")]
        private static void ToggleMode()
        {
            Fps3DState.Mode = Fps3DState.Mode switch
            {
                ViewMode.Off => ViewMode.FirstPerson,
                ViewMode.FirstPerson => ViewMode.Chase,
                _ => ViewMode.Off
            };
            Log.Message(ModEntry.LogPrefix + "mode -> " + Fps3DState.Mode);
        }

        [DebugAction("FirstPerson3D", "mode: first person")]
        private static void SetFirstPerson()
        {
            Fps3DState.Mode = ViewMode.FirstPerson;
        }

        [DebugAction("FirstPerson3D", "mode: chase")]
        private static void SetChase()
        {
            Fps3DState.Mode = ViewMode.Chase;
        }

        [DebugAction("FirstPerson3D", "mode: off")]
        private static void SetOff()
        {
            Fps3DState.Mode = ViewMode.Off;
        }

        [DebugAction("FirstPerson3D", "dump perf")]
        private static void DumpPerf()
        {
            Log.Message(ModEntry.LogPrefix + "perf " + PerfMon3D.SnapshotJson());
        }

        [DebugAction("FirstPerson3D", "level up")]
        public static void LevelUp()
        {
            if (LevelStack3D.SwitchLevel(1))
                Log.Message(ModEntry.LogPrefix + "level up ok -> map " + Find.CurrentMap.uniqueID);
            else
                Log.Message(ModEntry.LogPrefix + "level up: no upper level");
        }

        [DebugAction("FirstPerson3D", "level down")]
        public static void LevelDown()
        {
            if (LevelStack3D.SwitchLevel(-1))
                Log.Message(ModEntry.LogPrefix + "level down ok -> map " + Find.CurrentMap.uniqueID);
            else
                Log.Message(ModEntry.LogPrefix + "level down: no lower level");
        }

        // M3 验证：在同栈生成上层小图（70x70 口袋图），铺木地板平台+围墙，切到上层。
        [DebugAction("FirstPerson3D", "M3 create upper map")]
        public static void M3CreateUpper()
        {
            var cur = Find.CurrentMap;
            if (cur == null) return;
            // 必须经 LevelStack3D 生成：它会给上层图挂带有效世界格的父级，
            // 否则上层图 Tile 无效，原版每帧读取会持续报错。
            var newMap = LevelStack3D.CreateUpperMap(cur, new IntVec3(70, 1, 70),
                MapGeneratorDefOf.SpacePocket);
            if (newMap == null)
            {
                Log.Warning(ModEntry.LogPrefix + "M3 create upper: failed (base map tile invalid?)");
                return;
            }
            bool registered = LevelStack3D.RegisterStack(cur, newMap);
            int placed = 0;
            for (int x = 30; x <= 40; x++)
                for (int z = 30; z <= 40; z++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (!c.InBounds(newMap)) continue;
                    try
                    {
                        if (newMap.terrainGrid != null)
                            newMap.terrainGrid.SetTerrain(c, TerrainDefOf.WoodPlankFloor);
                        if (x == 30 || x == 40 || z == 30 || z == 40)
                        {
                            // ThingDefOf.Wall 是 madeFromStuff：必须显式给材质，否则每次生成刷红字。
                            GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), c, newMap);
                            placed++;
                        }
                    }
                    catch (System.Exception e)
                    {
                        Log.Warning(ModEntry.LogPrefix + "M3 place " + c + ": " + e.Message);
                    }
                }
            LevelStack3D.SwitchLevel(1);
            Log.Message(ModEntry.LogPrefix + "M3 upper map id=" + newMap.uniqueID
                + " registered=" + registered + " walls=" + placed
                + " level=" + LevelStack3D.LevelOf(newMap)
                + " baseY=" + LevelStack3D.LevelBaseY(newMap).ToString("F2"));
        }

        [DebugAction("FirstPerson3D", "toggle terrain")]
        private static void ToggleTerrain() => Fps3DState.DrawTerrain = !Fps3DState.DrawTerrain;

        [DebugAction("FirstPerson3D", "toggle plants")]
        private static void TogglePlants() => Fps3DState.DrawPlants = !Fps3DState.DrawPlants;

        [DebugAction("FirstPerson3D", "toggle items")]
        private static void ToggleItems() => Fps3DState.DrawItems = !Fps3DState.DrawItems;

        [DebugAction("FirstPerson3D", "toggle buildings")]
        private static void ToggleBuildings() => Fps3DState.DrawBuildings = !Fps3DState.DrawBuildings;

        [DebugAction("FirstPerson3D", "toggle pawns")]
        private static void TogglePawns() => Fps3DState.DrawPawns = !Fps3DState.DrawPawns;

        [DebugAction("FirstPerson3D", "toggle outlines")]
        private static void ToggleOutlines() => Fps3DState.DrawOutlines = !Fps3DState.DrawOutlines;

        // ---- 环境与生物层开关（SkyScenery / Water3D / Grass3D / Animal3D 验收用）----
        // 这些开关只存在于 ModSettings，没有原版面板，远控桥不在时只能靠这里切。

        [DebugAction("FirstPerson3D", "env: toggle water depth")]
        private static void ToggleWater()
        {
            var s = FirstPerson3DMod.settings; if (s == null) return;
            s.drawWater = !s.drawWater;
            SceneRegistry.DisposeAll();   // 地形层的水格跳过与水层互为补集，整场景重建最稳
            Log.Message(ModEntry.LogPrefix + "water depth -> " + s.drawWater);
        }

        [DebugAction("FirstPerson3D", "env: toggle grass cover")]
        private static void ToggleGrass()
        {
            var s = FirstPerson3DMod.settings; if (s == null) return;
            s.drawGrass = !s.drawGrass;
            Log.Message(ModEntry.LogPrefix + "grass cover -> " + s.drawGrass);
        }

        [DebugAction("FirstPerson3D", "env: toggle animal models")]
        private static void ToggleAnimalModels()
        {
            var s = FirstPerson3DMod.settings; if (s == null) return;
            s.animalModels = !s.animalModels;
            Log.Message(ModEntry.LogPrefix + "animal models -> " + s.animalModels + " (false = 回退立牌)");
        }

        [DebugAction("FirstPerson3D", "env: wind 0 / 1 / 2")]
        private static void CycleWind()
        {
            var s = FirstPerson3DMod.settings; if (s == null) return;
            s.windStrength = s.windStrength >= 2f ? 0f : (s.windStrength >= 1f ? 2f : 1f);
            Log.Message(ModEntry.LogPrefix + "wind -> " + s.windStrength);
        }

        [DebugAction("FirstPerson3D", "env: grass radius 12 / 28 / 48")]
        private static void CycleGrassRadius()
        {
            var s = FirstPerson3DMod.settings; if (s == null) return;
            s.grassRadius = s.grassRadius >= 48f ? 12f : (s.grassRadius >= 28f ? 48f : 28f);
            Log.Message(ModEntry.LogPrefix + "grass radius -> " + s.grassRadius);
        }

        [DebugAction("FirstPerson3D", "rebuild scenes")]
        private static void RebuildScenes()
        {
            SceneRegistry.DisposeAll();
            Log.Message(ModEntry.LogPrefix + "scenes disposed (will rebuild)");
        }
    }
}
