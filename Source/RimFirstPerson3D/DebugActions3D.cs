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

        // 光照导出（F013）：写进 settings.lightSnapshot 供 GABS 回读，同时打进日志。
        // scripts/light_check.py 吃的就是这份 JSON（各层网格顶点色按法线分桶的统计）。
        [DebugAction("FirstPerson3D", "dump lighting")]
        public static void DumpLighting()
        {
            var s = FirstPerson3DMod.settings;
            string j = Lighting3D.DumpJson();
            if (s != null) s.lightSnapshot = j;
            // 机检从日志里按 LIGHTDUMP 标记取这一行（scripts/light_check.py live）
            Log.Message(ModEntry.LogPrefix + "LIGHTDUMP " + j);
        }

        [DebugAction("FirstPerson3D", "lighting: toggle")]
        private static void ToggleLighting()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            s.lightOn = !s.lightOn;
            Log.Message(ModEntry.LogPrefix + "lighting -> " + (s.lightOn ? "on" : "off"));
        }

        // 受击反馈验收（F014）：toggle 做反证；hurt 不走伤害管线，直接把附身者的红闪与
        // 暗角点亮 —— 实机对照不该要求先在场面上找个人打架。
        [DebugAction("FirstPerson3D", "hitfx: toggle")]
        private static void ToggleHitFx()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            s.hitFx = !s.hitFx;
            Log.Message(ModEntry.LogPrefix + "hitfx -> " + (s.hitFx ? "on" : "off"));
        }

        [DebugAction("FirstPerson3D", "hitfx: hurt")]
        private static void HurtHitFx()
        {
            var me = CameraRig3D.Possessed;
            HitFx3D.Force(me, 1f);
            Log.Message(ModEntry.LogPrefix + "hitfx: hurt " + (me != null ? me.LabelShort : "no pawn"));
        }

        // 射击效果开关（F015）：真开火走原版射击作业，GABS 侧用 cmdClick 就能打一枪，
        // 所以这里只留反证用的 toggle，不做「假开火」—— 那会绕开 Launch/Impact 两个真值。
        [DebugAction("FirstPerson3D", "shotfx: toggle")]
        private static void ToggleShotFx()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            s.shotFx = !s.shotFx;
            Log.Message(ModEntry.LogPrefix + "shotfx -> " + (s.shotFx ? "on" : "off"));
        }

        // 光照验收开关（F013）：GABS 侧只有 execute_debug_action 能写这些参数
        //（get_mod_settings 是只读通道），所以每个判据要的对比组都在这里点名。
        [DebugAction("FirstPerson3D", "lighting: lamp on")]
        private static void LampOn() { SetLamp(1f); }

        [DebugAction("FirstPerson3D", "lighting: lamp off")]
        private static void LampOff() { SetLamp(0f); }

        [DebugAction("FirstPerson3D", "lighting: shadows on")]
        private static void ShadowsOn() { SetBool(s => s.lightShadows = true, "shadows on"); }

        [DebugAction("FirstPerson3D", "lighting: shadows off")]
        private static void ShadowsOff() { SetBool(s => s.lightShadows = false, "shadows off"); }

        [DebugAction("FirstPerson3D", "lighting: floor zero")]
        private static void FloorZero() { SetFloor(0f); }

        [DebugAction("FirstPerson3D", "lighting: floor normal")]
        private static void FloorNormal() { SetFloor(0.16f); }

        [DebugAction("FirstPerson3D", "sky: noon")]
        private static void SkyNoon() { SetSky("noon"); }

        [DebugAction("FirstPerson3D", "sky noon")]
        private static void SkyNoonPlain() { SetSky("noon"); }

        [DebugAction("FirstPerson3D", "sky: dusk")]
        private static void SkyDusk() { SetSky("dusk"); }

        [DebugAction("FirstPerson3D", "sky dusk")]
        private static void SkyDuskPlain() { SetSky("dusk"); }

        [DebugAction("FirstPerson3D", "sky: night")]
        private static void SkyNight() { SetSky("night"); }

        [DebugAction("FirstPerson3D", "sky night")]
        private static void SkyNightPlain() { SetSky("night"); }

        [DebugAction("FirstPerson3D", "sky: preset off")]
        private static void SkyOff() { SetSky(""); }

        [DebugAction("FirstPerson3D", "sky live")]
        private static void SkyLivePlain() { SetSky(""); }

        [DebugAction("FirstPerson3D", "lamp on")]
        private static void LampOnPlain() { SetLamp(1f); }

        [DebugAction("FirstPerson3D", "lamp off")]
        private static void LampOffPlain() { SetLamp(0f); }

        [DebugAction("FirstPerson3D", "lighting toggle")]
        private static void ToggleLightingPlain() { ToggleLighting(); }

        private static void SetLamp(float v)
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            s.lightLamp = v;
            Log.Message(ModEntry.LogPrefix + "lightLamp -> " + v);
        }

        private static void SetFloor(float v)
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            s.lightFloor = v;
            Log.Message(ModEntry.LogPrefix + "lightFloor -> " + v);
        }

        private static void SetSky(string p)
        {
            SkyScenery.DebugPreset = p;
            Log.Message(ModEntry.LogPrefix + "skyPreset -> '" + p + "'");
        }

        private static void SetBool(System.Action<FirstPerson3DSettings> f, string label)
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            f(s);
            Log.Message(ModEntry.LogPrefix + "lighting " + label);
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

        private static readonly string[] SkyPresets = { "", "dawn", "noon", "dusk", "night", "aurora", "rain", "storm", "snow", "fog_sand" };

        [DebugAction("FirstPerson3D", "env: cycle sky/weather preset")]
        private static void CycleSkyPreset()
        {
            int cur = System.Array.IndexOf(SkyPresets, SkyScenery.DebugPreset ?? "");
            int next = (cur + 1) % SkyPresets.Length;
            SkyScenery.DebugPreset = SkyPresets[next];
            Log.Message(ModEntry.LogPrefix + "skyPreset -> " + (string.IsNullOrEmpty(SkyScenery.DebugPreset) ? "live" : SkyScenery.DebugPreset));
        }
    }
}
