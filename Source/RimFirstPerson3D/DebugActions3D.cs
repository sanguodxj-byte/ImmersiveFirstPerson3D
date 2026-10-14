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

        // F016 P0：引擎能力探针（结果写进 settings.renderCaps 并持久化 caps* 门控字段）
        [DebugAction("FirstPerson3D", "probe: render caps")]
        public static void ProbeRenderCaps()
        {
            RenderProbe3D.Run();
        }

        [DebugAction("FirstPerson3D", "shader quality: cycle")]
        private static void CycleShaderQuality()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            string[] order = { "off", "low", "medium", "high", "ultra" };
            int i = System.Array.IndexOf(order, (s.shaderQuality ?? "medium").ToLowerInvariant());
            s.shaderQuality = order[(i + 1 + order.Length) % order.Length];
            Shaderpack3D.ResetSession();
            Log.Message(ModEntry.LogPrefix + "shaderQuality -> " + s.shaderQuality);
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

        [DebugAction("FirstPerson3D", "test: harvest")]
        private static void TestHarvest() => RunTestScenario("harvest");

        [DebugAction("FirstPerson3D", "test: harvest_work")]
        private static void TestHarvestWork() => RunTestScenario("harvest_work");

        [DebugAction("FirstPerson3D", "test: workbench")]
        private static void TestWorkbench() => RunTestScenario("workbench");

        [DebugAction("FirstPerson3D", "test: workbench_work")]
        private static void TestWorkbenchWork() => RunTestScenario("workbench_work");

        [DebugAction("FirstPerson3D", "test: blueprints")]
        private static void TestBlueprints() => RunTestScenario("blueprints");

        [DebugAction("FirstPerson3D", "test: blueprints_build")]
        private static void TestBlueprintsBuild() => RunTestScenario("blueprints_build");

        [DebugAction("FirstPerson3D", "test: destructive")]
        private static void TestDestructive() => RunTestScenario("destructive");

        [DebugAction("FirstPerson3D", "test: destructive_mine")]
        private static void TestDestructiveMine() => RunTestScenario("destructive_mine");

        [DebugAction("FirstPerson3D", "test: attack_pawn")]
        private static void TestAttackPawn() => RunTestScenario("attack_pawn");

        [DebugAction("FirstPerson3D", "test: roofs")]
        private static void TestRoofs() => RunTestScenario("roofs");

        [DebugAction("FirstPerson3D", "test: interact (E)")]
        private static void TestInteract() => CameraRig3D.Interact();

        [DebugAction("FirstPerson3D", "test: attack (LMB)")]
        private static void TestAttack() => CameraRig3D.Attack();

        private static void EnsurePawnCapable(Pawn pawn)
        {
            if (pawn == null) return;
            try
            {
                if (pawn.skills != null && pawn.skills.skills != null)
                {
                    for (int i = 0; i < pawn.skills.skills.Count; i++)
                    {
                        var sk = pawn.skills.skills[i];
                        if (sk != null && sk.Level < 10) sk.Level = 10;
                    }
                }
                if (pawn.workSettings != null)
                {
                    pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                    foreach (var wt in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (!pawn.WorkTypeIsDisabled(wt))
                            pawn.workSettings.SetPriority(wt, 1);
                    }
                }
            }
            catch { }
        }

        private static void StepPawnJob(Pawn pawn, int ticks)
        {
            if (pawn == null || pawn.jobs == null) return;
            for (int i = 0; i < ticks; i++)
            {
                if (pawn.jobs.curDriver == null) break;
                try
                {
                    pawn.pather?.PatherTick();
                    pawn.jobs.JobTrackerTick();
                    pawn.jobs.JobTrackerTickInterval(1);
                }
                catch { break; }
            }
        }

        public static void RunTestScenario(string name)
        {
            var map = Find.CurrentMap;
            var s = FirstPerson3DMod.settings;
            if (map == null || s == null) return;

            SkyScenery.DebugPreset = "noon";
            Fps3DState.Mode = ViewMode.FirstPerson;
            s.freeCam = false;
            var pawn = CameraRig3D.Possessed;
            if (pawn == null || !pawn.Spawned || pawn.Map != map)
            {
                var list = map.mapPawns?.FreeColonistsSpawned;
                if (list != null && list.Count > 0) pawn = list[0];
            }
            if (pawn == null) return;
            pawn.jobs?.StopAll();
            pawn.pather?.StopDead();
            EnsurePawnCapable(pawn);

            // 找一块远离山体（16x16 无岩石无天然山顶）的平地放置测试锚点
            IntVec3 anchor = FindFlatAnchor(map, pawn.Position);
            ClearArea(map, new CellRect(anchor.x - 8, anchor.z - 2, 17, 14));

            var blocks = DefDatabase<ThingDef>.GetNamedSilentFail("BlocksSandstone") ?? ThingDefOf.WoodLog;

            if (name == "harvest" || name == "harvest_work")
            {
                var berryDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Berry")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Potato");
                if (berryDef != null)
                {
                    var frontPlant = (Plant)GenSpawn.Spawn(ThingMaker.MakeThing(berryDef), new IntVec3(anchor.x, 0, anchor.z + 1), map);
                    frontPlant.Growth = 1.0f;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var plantCell = new IntVec3(anchor.x + dx, 0, anchor.z + 2);
                        var p = (Plant)GenSpawn.Spawn(ThingMaker.MakeThing(berryDef), plantCell, map);
                        p.Growth = 1.0f;
                    }
                }
                var benchDef = DefDatabase<ThingDef>.GetNamedSilentFail("TableButcher");
                if (benchDef != null)
                {
                    var b = GenSpawn.Spawn(ThingMaker.MakeThing(benchDef, ThingDefOf.WoodLog),
                        new IntVec3(anchor.x + 3, 0, anchor.z + 3), map, Rot4.South);
                    b.SetFaction(Faction.OfPlayer);
                }
                CameraRig3D.TeleportAndAim(anchor, 0f, 32f);
                if (name == "harvest_work")
                {
                    CameraRig3D.Interact();
                    StepPawnJob(pawn, 70);
                    if (Find.TickManager != null && !Find.TickManager.Paused)
                        Find.TickManager.TogglePaused();
                }
            }
            else if (name == "workbench" || name == "workbench_work")
            {
                var benchDef = DefDatabase<ThingDef>.GetNamedSilentFail("TableSculpting")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("TableButcher");
                Building_WorkTable bench = null;
                if (benchDef != null)
                {
                    // Rot4.North 时 InteractionCell 恰好是 (anchor.x, 0, anchor.z)，小人无需绕路即可原地操作工作台
                    bench = (Building_WorkTable)GenSpawn.Spawn(ThingMaker.MakeThing(benchDef, ThingDefOf.WoodLog),
                        new IntVec3(anchor.x, 0, anchor.z + 1), map, Rot4.North);
                    bench.SetFaction(Faction.OfPlayer);
                }
                var wood = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                wood.stackCount = 75;
                GenSpawn.Spawn(wood, new IntVec3(anchor.x, 0, anchor.z), map);
                CameraRig3D.TeleportAndAim(anchor, 0f, 24f);
                if (name == "workbench_work" && bench != null)
                {
                    CameraRig3D.Interact();
                    // 推进 JobTrackerTick 让小人把脚下木头放上雕刻台并进入 DoRecipeWork 阶段
                    StepPawnJob(pawn, 180);
                    if (Find.TickManager != null && !Find.TickManager.Paused)
                        Find.TickManager.TogglePaused();
                }
            }
            else if (name == "blueprints" || name == "blueprints_build")
            {
                // 建造蓝图：墙体蓝图 + 门蓝图 + 屠宰台蓝图 + 床铺蓝图 + 沙袋蓝图
                for (int dx = -4; dx <= -2; dx++)
                {
                    GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, new IntVec3(anchor.x + dx, 0, anchor.z + 4),
                        map, Rot4.North, Faction.OfPlayer, blocks);
                }
                GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Door, new IntVec3(anchor.x - 1, 0, anchor.z + 4),
                    map, Rot4.North, Faction.OfPlayer, ThingDefOf.WoodLog);
                GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, new IntVec3(anchor.x, 0, anchor.z + 4),
                    map, Rot4.North, Faction.OfPlayer, blocks);

                var benchDef = DefDatabase<ThingDef>.GetNamedSilentFail("TableButcher");
                if (benchDef != null)
                {
                    GenConstruct.PlaceBlueprintForBuild(benchDef, new IntVec3(anchor.x + 2, 0, anchor.z + 3),
                        map, Rot4.South, Faction.OfPlayer, ThingDefOf.WoodLog);
                }
                var bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
                if (bedDef != null)
                {
                    GenConstruct.PlaceBlueprintForBuild(bedDef, new IntVec3(anchor.x - 2, 0, anchor.z + 2),
                        map, Rot4.South, Faction.OfPlayer, ThingDefOf.WoodLog);
                }
                if (ThingDefOf.Sandbags != null)
                {
                    for (int dx = 0; dx <= 2; dx++)
                    {
                        if (name == "blueprints_build" && dx == 0)
                        {
                            var frame = (Frame)ThingMaker.MakeThing(ThingDefOf.Sandbags.frameDef, ThingDefOf.Cloth);
                            frame.SetFactionDirect(Faction.OfPlayer);
                            var cloth = ThingMaker.MakeThing(ThingDefOf.Cloth);
                            cloth.stackCount = 5;
                            frame.resourceContainer.TryAdd(cloth);
                            GenSpawn.Spawn(frame, new IntVec3(anchor.x, 0, anchor.z + 1), map, Rot4.North);
                        }
                        else
                        {
                            GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Sandbags, new IntVec3(anchor.x + dx, 0, anchor.z + 2),
                                map, Rot4.North, Faction.OfPlayer, ThingDefOf.Cloth);
                        }
                    }
                }
                var clothStack = ThingMaker.MakeThing(ThingDefOf.Cloth);
                clothStack.stackCount = 75;
                GenSpawn.Spawn(clothStack, new IntVec3(anchor.x + 1, 0, anchor.z), map);
                var woodStack = ThingMaker.MakeThing(ThingDefOf.WoodLog);
                woodStack.stackCount = 75;
                GenSpawn.Spawn(woodStack, new IntVec3(anchor.x - 1, 0, anchor.z), map);

                CameraRig3D.TeleportAndAim(anchor, 0f, name == "blueprints_build" ? 28f : 23f);
                if (name == "blueprints_build")
                {
                    CameraRig3D.Interact();
                    StepPawnJob(pawn, 70);
                    if (Find.TickManager != null && !Find.TickManager.Paused)
                        Find.TickManager.TogglePaused();
                }
            }
            else if (name == "destructive")
            {
                var treeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreeOak")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreePoplar");
                if (treeDef != null)
                {
                    var tr = (Plant)GenSpawn.Spawn(ThingMaker.MakeThing(treeDef),
                        new IntVec3(anchor.x, 0, anchor.z + 1), map);
                    tr.Growth = 1.0f;
                }
                var mineDef = DefDatabase<ThingDef>.GetNamedSilentFail("MineableSteel")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("Sandstone");
                if (mineDef != null)
                {
                    GenSpawn.Spawn(ThingMaker.MakeThing(mineDef), new IntVec3(anchor.x - 2, 0, anchor.z + 2), map);
                }
                var wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, blocks),
                    new IntVec3(anchor.x + 2, 0, anchor.z + 2), map, Rot4.North);
                wall.SetFaction(Faction.OfPlayer);
                CameraRig3D.TeleportAndAim(anchor, 0f, 14f);
                CameraRig3D.Attack();
                StepPawnJob(pawn, 120);
                if (Find.TickManager != null && !Find.TickManager.Paused)
                    Find.TickManager.TogglePaused();
            }
            else if (name == "destructive_mine")
            {
                var mineDef = DefDatabase<ThingDef>.GetNamedSilentFail("MineableSteel")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("Sandstone");
                Thing rock = null;
                if (mineDef != null)
                {
                    rock = GenSpawn.Spawn(ThingMaker.MakeThing(mineDef), new IntVec3(anchor.x, 0, anchor.z + 1), map);
                }
                var treeDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreeOak");
                if (treeDef != null)
                {
                    var tr = (Plant)GenSpawn.Spawn(ThingMaker.MakeThing(treeDef),
                        new IntVec3(anchor.x + 2, 0, anchor.z + 2), map);
                    tr.Growth = 1.0f;
                }
                CameraRig3D.TeleportAndAim(anchor, 0f, 18f);
                CameraRig3D.Attack();
                StepPawnJob(pawn, 60);
                if (rock != null && rock.MaxHitPoints > 10)
                    rock.HitPoints = Mathf.RoundToInt(rock.MaxHitPoints * 0.52f);
                if (Find.TickManager != null && !Find.TickManager.Paused)
                    Find.TickManager.TogglePaused();
            }
            else if (name == "attack_pawn")
            {
                var gunDef = DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Autopistol")
                    ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
                if (gunDef != null && pawn.equipment != null && pawn.equipment.Primary == null)
                {
                    var gun = (ThingWithComps)ThingMaker.MakeThing(gunDef);
                    pawn.equipment.AddEquipment(gun);
                }
                var kind = PawnKindDefOf.SpaceRefugee ?? PawnKindDefOf.Colonist;
                var targetPawn = PawnGenerator.GeneratePawn(kind, null);
                GenSpawn.Spawn(targetPawn, new IntVec3(anchor.x, 0, anchor.z + 3), map);
                targetPawn.pather?.StopDead();
                CameraRig3D.TeleportAndAim(anchor, 0f, 10f);
                if (Find.TickManager != null)
                {
                    if (Find.TickManager.Paused) Find.TickManager.TogglePaused();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                }
                CameraRig3D.Attack();
            }
            else if (name == "roofs")
            {
                // 左侧房屋（dx = -6..-2, dz = 5..9）：砂岩砖墙 + 自然泥地（Soil，非地板）-> 房顶采样砂岩砖墙样式
                BuildTestHouse(map, new CellRect(anchor.x - 6, anchor.z + 5, 5, 5), blocks, TerrainDefOf.Soil);
                // 右侧房屋（dx = 2..6, dz = 5..9）：砂岩砖墙 + 3x3 人工木地板（WoodPlankFloor）-> 房顶采样木地板样式
                BuildTestHouse(map, new CellRect(anchor.x + 2, anchor.z + 5, 5, 5), blocks, TerrainDefOf.WoodPlankFloor);
                CameraRig3D.TeleportAndAim(anchor, 0f, -4f);
            }

            SceneRegistry.GetOrCreate(map)?.MarkAllDirty();
        }

        private static void BuildTestHouse(Map map, CellRect r, ThingDef wallStuff, TerrainDef floorDef)
        {
            for (int z = r.minZ; z <= r.maxZ; z++)
            {
                for (int x = r.minX; x <= r.maxX; x++)
                {
                    var c = new IntVec3(x, 0, z);
                    if (!c.InBounds(map)) continue;
                    map.terrainGrid.SetTerrain(c, floorDef);
                    bool border = x == r.minX || x == r.maxX || z == r.minZ || z == r.maxZ;
                    bool frontOpen = z == r.minZ && x > r.minX && x < r.maxX;
                    if (border && !frontOpen)
                    {
                        var w = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, wallStuff), c, map);
                        w.SetFaction(Faction.OfPlayer);
                    }
                    map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
                }
            }
        }

        private static IntVec3 FindFlatAnchor(Map map, IntVec3 start)
        {
            var mountain = map.GetComponent<MountainField3D>();
            for (int rad = 0; rad <= 60; rad += 4)
            {
                for (int dz = -rad; dz <= rad; dz += 4)
                {
                    for (int dx = -rad; dx <= rad; dx += 4)
                    {
                        int cx = Mathf.Clamp(start.x + dx, 20, map.Size.x - 20);
                        int cz = Mathf.Clamp(start.z + dz, 20, map.Size.z - 25);
                        var rect = new CellRect(cx - 8, cz - 2, 17, 14);
                        bool ok = true;
                        foreach (var c in rect)
                        {
                            if (!c.InBounds(map) || map.terrainGrid.TerrainAt(c).IsWater) { ok = false; break; }
                            if (mountain != null && mountain.Ready && mountain.Cells[c.z * map.Size.x + c.x] == 1) { ok = false; break; }
                        }
                        if (ok) return new IntVec3(cx, 0, cz);
                    }
                }
            }
            return start;
        }

        private static void ClearArea(Map map, CellRect rect)
        {
            rect.ClipInsideMap(map);
            foreach (var c in rect)
            {
                map.roofGrid.SetRoof(c, null);
                map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                var list = c.GetThingList(map);
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var t = list[i];
                    if (t == null || t is Pawn) continue;
                    t.Destroy(DestroyMode.Vanish);
                }
            }
        }

        // ---- HAR / 动态表情兼容验证（dump pawn render）----
        // 把在场人形 pawn 的渲染树节点形态（FA 节点 / HAR 额外部件 / 原版头）与
        // FacialAnimation 控制器的推进度写进日志，供 GABS list_logs 断言：
        //   PAWNDUMP{...fa=n har=n head=1 faTick=12345@T=12345 job=GotoWander...}
        // faTick 与 T（当前游戏 tick）一致 = PreDraw 推进链在跑；faTick 远小于 T = 表情冻结。
        // FacialAnimation 是软依赖，全部走反射，缺失时安全降级。
        [DebugAction("FirstPerson3D", "dump pawn render")]
        public static void DumpPawnRender()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Message(ModEntry.LogPrefix + "PAWNDUMP nomap");
                return;
            }
            int T = Find.TickManager.TicksGame;
            var sb = new System.Text.StringBuilder();
            sb.Append("PAWNDUMP T=").Append(T).Append(" mode=").Append(Fps3DState.Mode);
            var poss = CameraRig3D.Possessed;
            int n = 0;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || !pawn.RaceProps.Humanlike) continue;
                if (n++ >= 8) break;
                var tree = pawn.Drawer != null && pawn.Drawer.renderer != null ? pawn.Drawer.renderer.renderTree : null;
                if (tree == null || tree.rootNode == null) continue;
                int fa = 0, har = 0, head = 0, total = 0;
                DumpWalk(tree.rootNode, ref fa, ref har, ref head, ref total, 0);
                bool fpSelf = pawn == poss && Fps3DState.Mode == ViewMode.FirstPerson;
                sb.Append(" {id=").Append(pawn.thingIDNumber).Append(" def=").Append(pawn.def.defName);
                sb.Append(" nodes=").Append(total).Append(" fa=").Append(fa).Append(" har=").Append(har);
                sb.Append(" head=").Append(head).Append(" fpSelf=").Append(fpSelf ? 1 : 0);
                sb.Append(" faTick=").Append(FaLastTick(pawn));
                sb.Append(" masterCtrl=").Append(MasterControllerState(pawn));
                sb.Append(" faCheck=").Append(FaCheckNow(pawn));
                sb.Append(" isColonist=").Append(pawn.IsColonist);
                sb.Append(" job=").Append(pawn.CurJobDef != null ? pawn.CurJobDef.defName : "-");
                try { sb.Append(' ').Append(PawnLayer3D.DiagnoseRequests(pawn, pawn.Rotation)); }
                catch (System.Exception e) { sb.Append(" diagErr ").Append(e.GetType().Name); }
                // PreDraw 实操：与绘制循环同法逐请求调一次，看是否抛异常、lastTick 是否被推进
                int tickBefore = FaLastTick(pawn);
                string preErr = PawnLayer3D.DiagnosePreDraw(pawn);
                sb.Append(" preDraw ").Append("before=").Append(tickBefore).Append(" after=").Append(FaLastTick(pawn))
                    .Append(" err=").Append(preErr ?? "none");
                sb.Append('}');
            }
            Log.Message(ModEntry.LogPrefix + sb);
        }

        private static void DumpWalk(PawnRenderNode node, ref int fa, ref int har, ref int head, ref int total, int guard)
        {
            if (node == null || guard > 12) return;
            total++;
            var name = node.GetType().Name;
            if (name.StartsWith("NLFacialAnimation", System.StringComparison.Ordinal)) fa++;
            else if (name == "AlienPawnRenderNode_BodyAddon") har++;
            else if (name == "PawnRenderNode_Head") head++;
            var kids = node.children;
            if (kids == null) return;
            for (int i = 0; i < kids.Length; i++)
                DumpWalk(kids[i], ref fa, ref har, ref head, ref total, guard + 1);
        }

        // 着色器/材质采样（定位「非原版服饰不吃光」）：列出每个可见人形 pawn 各请求的
        // 节点类型 + 着色器名 + 主贴图 + 材质色，看吃不吃 _Color 乘子、是不是 MatPropBlock 系。
        [DebugAction("FirstPerson3D", "dump pawn shaders")]
        public static void DumpPawnShaders()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Message(ModEntry.LogPrefix + "PAWNSHADER nomap"); return; }
            var sb = new System.Text.StringBuilder();
            sb.Append("PAWNSHADER mode=").Append(Fps3DState.Mode);
            int n = 0;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || !pawn.RaceProps.Humanlike) continue;
                if (n++ >= 12) break;
                var tree = pawn.Drawer?.renderer?.renderTree;
                if (tree?.rootNode == null) continue;
                sb.Append(" [").Append(pawn.def.defName).Append(':').Append(pawn.thingIDNumber).Append(']');
                try
                {
                    tree.EnsureInitialized(PawnRenderFlags.Clothes | PawnRenderFlags.Headgear);
                    var list = new System.Collections.Generic.List<PawnGraphicDrawRequest>();
                    var parms = BuildParmsLocal(pawn);
                    tree.rootNode.AppendRequests(parms, list);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var node = list[i].node;
                        var mat = node != null && node.CheckMaterialEveryDrawRequest
                            ? node.Worker.GetFinalizedMaterial(node, parms) : list[i].material;
                        if (node == null) continue;
                        var tname = node.GetType().Name;
                        if (tname.StartsWith("NLFacialAnimation", System.StringComparison.Ordinal)) continue;  // 表情层另论
                        var sh = mat != null && mat.shader != null ? mat.shader.name : "nullMat";
                        var tex = mat != null && mat.mainTexture != null ? mat.mainTexture.name : "-";
                        var col = mat != null ? mat.color : Color.clear;
                        sb.Append(" (").Append(tname).Append('|').Append(sh).Append('|').Append(tex)
                            .Append('|').Append(col.r.ToString("0.00")).Append(',').Append(col.g.ToString("0.00")).Append(',').Append(col.b.ToString("0.00")).Append(')');
                    }
                }
                catch (System.Exception e) { sb.Append(" ERR ").Append(e.GetType().Name).Append(':').Append(e.Message); }
            }
            Log.Message(ModEntry.LogPrefix + sb);
        }

        private static PawnDrawParms BuildParmsLocal(Pawn pawn)
        {
            var p = PawnDrawParms.DefaultFor(pawn);
            p.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one);
            p.facing = pawn.Rotation;
            try { p.rotDrawMode = pawn.Drawer.renderer.CurRotDrawMode; } catch { }
            p.flags = PawnRenderFlags.Clothes | PawnRenderFlags.Headgear;
            p.tint = Color.white;
            return p;
        }

        // 反射读 NLFacialAnimationMasterNode.controller：构造时 CheckUpdateableInitial() 失败会被置 null，
        // 之后 PreDraw 里的 UpdateAnimation 就是空跑 —— 这是「表情冻结」的直接原因，必须能看见。
        private static string MasterControllerState(Pawn pawn)
        {
            try
            {
                var tree = pawn.Drawer?.renderer?.renderTree;
                if (tree?.rootNode == null) return "noTree";
                PawnRenderNode master = null;
                FindMaster(tree.rootNode, ref master, 0);
                if (master == null) return "noMasterNode";
                var f = master.GetType().GetField("controller",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);
                if (f == null) return "noField";
                object c = f.GetValue(master);
                if (c == null) return "null";
                // controller 非空时的 CheckUpdateableInitial 各条件复述（便于定位为何 null）
                var fa = f.FieldType;
                return fa.Name;
            }
            catch (System.Exception e) { return "EXC:" + e.GetType().Name; }
        }

        // 现场复跑 CheckUpdateableInitial 并读 FA 设置当前值：若现在返回 true 而构造时为 false，
        // 说明是构造时机的瞬时状态（如 Settings 尚未加载 / pawn 状态未就绪）害死了 controller。
        private static string FaCheckNow(Pawn pawn)
        {
            try
            {
                ThingComp comp = null;
                foreach (var c in pawn.AllComps)
                {
                    if (c.GetType().FullName == "FacialAnimation.FacialAnimationControllerComp") { comp = c; break; }
                }
                if (comp == null) return "noComp";
                var t = comp.GetType();
                var mCheck = t.GetMethod("CheckUpdateableInitial",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                string now = mCheck != null ? (mCheck.Invoke(comp, null) as bool?).ToString() : "noMethod";
                // FacialAnimationMod.Settings 两个关键开关
                string settings = "?";
                try
                {
                    var faMod = System.Type.GetType("FacialAnimation.FacialAnimationMod, FacialAnimation");
                    if (faMod != null)
                    {
                        var sProp = faMod.GetProperty("Settings", System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        object s = sProp?.GetValue(null);
                        if (s != null)
                        {
                            var all = s.GetType().GetField("EnableAnimationsForAllPawns");
                            var non = s.GetType().GetField("EnableAnimationsForNonOfTheAbove");
                            settings = "all=" + all?.GetValue(s) + " non=" + non?.GetValue(s);
                        }
                        else settings = "Settings=null";
                    }
                }
                catch (System.Exception e) { settings = "EXC:" + e.Message; }
                string rot = "?";
                try { rot = pawn.Drawer.renderer.CurRotDrawMode.ToString(); } catch { }
                return "checkNow=" + now + " " + settings + " rot=" + rot;
            }
            catch (System.Exception e) { return "EXC:" + e.GetType().Name; }
        }

        private static void FindMaster(PawnRenderNode node, ref PawnRenderNode master, int guard)
        {
            if (node == null || master != null || guard > 12) return;
            if (node.GetType().Name == "NLFacialAnimationMasterNode") { master = node; return; }
            var kids = node.children;
            if (kids == null) return;
            for (int i = 0; i < kids.Length; i++) FindMaster(kids[i], ref master, guard + 1);
        }

        // 反射读 FacialAnimation.FacialAnimationControllerComp 的 lastTick：
        // FA 每帧由 NLFacialAnimationMasterNodeWorker.PreDraw -> UpdateStatus 更新它，
        // 停在旧值说明本模组的 3D 立牌路径没把 PreDraw 链接上（表情冻结）。
        private static int FaLastTick(Pawn pawn)
        {
            try
            {
                ThingComp comp = null;
                foreach (var c in pawn.AllComps)
                {
                    if (c.GetType().FullName == "FacialAnimation.FacialAnimationControllerComp")
                    {
                        comp = c;
                        break;
                    }
                }
                if (comp == null) return -1;   // 该 pawn 无动态表情
                var t = comp.GetType();
                object v = null;
                for (var bt = t; bt != null && v == null; bt = bt.BaseType)
                {
                    var f = bt.GetField("lastTick", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    if (f != null) v = f.GetValue(comp);
                }
                return v is int i ? i : -2;
            }
            catch { return -2; }
        }
        // ---- 异像实体贴图链路诊断（dump entity render）----
        // 复跑 PawnLayer3D 对非人形 pawn 的完整判定，逐级输出实体为何可见/不可见：
        //   AnimHandles / Humanlike / Frustum / Fogged / ResolveGraphic 五级 / drawSize / mat
        // 输出形如：ENTITYDUMP def=Nociosphere anim=0 hum=0 vis=1 fog=0 g1=- g2=- g3=- g4=- g5=+ ds=3.6,3.6 mat=ok
        [DebugAction("FirstPerson3D", "dump entity render")]
        public static void DumpEntityRender()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Message(ModEntry.LogPrefix + "ENTITYDUMP nomap"); return; }
            var sb = new System.Text.StringBuilder();
            sb.Append("ENTITYDUMP mode=").Append(Fps3DState.Mode);
            int n = 0;
            foreach (var pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.RaceProps == null || pawn.RaceProps.Humanlike) continue;
                string dd = pawn.def != null ? pawn.def.defName : "?";
                // 只看非动物图集接管件（即 PawnLayer3D 的 else 分支对象）
                if (Animal3D.HandlesPawn(pawn)) continue;
                if (n++ >= 8) break;
                sb.Append(" {def=").Append(dd);
                sb.Append(" pos=").Append(pawn.Position);
                sb.Append(" kind=").Append(pawn.kindDef != null ? pawn.kindDef.defName : "null");
                sb.Append(" anim=").Append(Animal3D.HandlesPawn(pawn) ? 1 : 0);
                sb.Append(" carried=").Append(pawn.CarriedBy != null ? 1 : 0);
                var dp = pawn.DrawPos;
                sb.Append(" drawPos=").Append(dp);
                sb.Append(" fogged=").Append(pawn.Position.Fogged(map) ? 1 : 0);
                // 五级回退逐级结果
                string g1 = "-", g2 = "-", g3 = "-", g4 = "-", g5 = "-";
                try
                {
                    var tree = pawn.Drawer != null && pawn.Drawer.renderer != null ? pawn.Drawer.renderer.renderTree : null;
                    var bg = tree != null ? tree.BodyGraphic : null;
                    g1 = bg != null ? "+" : "null";
                    if (bg != null) g1 += "/" + (bg.MatSingle != null ? "mat" : "nomat");
                }
                catch (System.Exception e) { g1 = "EXC:" + e.GetType().Name; }
                try
                {
                    var ck = pawn.ageTracker != null ? pawn.ageTracker.CurKindLifeStage : null;
                    g2 = ck == null ? "nullStage" : (ck.bodyGraphicData != null ? "+" : "nullGd");
                }
                catch (System.Exception e) { g2 = "EXC:" + e.GetType().Name; }
                try
                {
                    g3 = pawn.kindDef != null ? (pawn.kindDef.lifeStages != null && pawn.kindDef.lifeStages.Count > 0 ? "+" : "noStages") : "noKind";
                }
                catch (System.Exception e) { g3 = "EXC:" + e.GetType().Name; }
                try
                {
                    g4 = pawn.def != null ? ((pawn.def.graphic != null ? "+" : (pawn.def.graphicData != null ? "gd" : "null"))) : "noDef";
                }
                catch (System.Exception e) { g4 = "EXC:" + e.GetType().Name; }
                try
                {
                    var root = pawn.Drawer?.renderer?.renderTree?.rootNode;
                    g5 = root != null ? (root.PrimaryGraphic != null ? "+" : "null") : "noTree";
                }
                catch (System.Exception e) { g5 = "EXC:" + e.GetType().Name; }
                sb.Append(" g1=").Append(g1).Append(" g2=").Append(g2).Append(" g3=").Append(g3)
                  .Append(" g4=").Append(g4).Append(" g5=").Append(g5);
                // 最终 ResolveGraphic 结果
                try
                {
                    var g = PawnLayer3D.ResolveGraphic(pawn, Rot4.South);
                    if (g == null) sb.Append(" resolve=null");
                    else
                    {
                        var m = g.MatAt(Rot4.South, pawn) ?? g.MatSingle;
                        sb.Append(" resolve=+ path=").Append(g.path ?? "?")
                          .Append(" mat=").Append(m != null && !m.NullOrBad() ? "ok" : "bad")
                          .Append(" ds=").Append(g.drawSize);
                    }
                }
                catch (System.Exception e) { sb.Append(" resolve=EXC:").Append(e.GetType().Name); }
                sb.Append('}');
            }
            if (n == 0) sb.Append(" {noEntityFound}");
            Log.Message(ModEntry.LogPrefix + sb);
        }
    }
}
