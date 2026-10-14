// AI生成
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 单张地图的 3D 场景：聚合各渲染层，负责脏更新与绘制。
    public class Scene3D
    {
        public readonly Map map;
        public readonly float baseY;

        private readonly TerrainLayer3D terrain;
        private readonly BuildingLayer3D buildings;
        private readonly MountainField3D mountain;
        private readonly MountainLayer3D mountains;
        private readonly PlantLayer3D plants;
        private readonly ItemLayer3D items;
        private readonly PawnLayer3D pawns;
        // 环境与生物层（SkyScenery 为全局，不按图）
        private readonly WaterLayer3D water;
        private readonly GrassLayer3D grass;
        private readonly AnimalLayer3D animals;

        // 光照场（F013）：本图逐格亮度缓存，地形/墙体/水体烘顶点色时读它。
        private readonly LightField3D field;
        private readonly GlowLayer3D glow;
        // 上次烘进网格的天空亮度（量化到 1/16）。夜里 glow 连续变化，不量化就会每帧重烘。
        private int lastBakedGlow = -999;
        // 整层重烘的最短间隔（秒）：glow 卡在档位边界时不许来回翻整张图（至少 20s 稳定窗口）
        private const float RebakeMinInterval = 20f;
        private float rebakeTimer;
        // 上次烘进网格的光照参数指纹
        private long lastLightKey = long.MinValue;

        public LightField3D Light => this.field;
        public MountainField3D Mountain => mountain;
        public MountainLayer3D Mountains => mountains;

        public Scene3D(Map map)
        {
            this.map = map;
            this.baseY = LevelStack3D.LevelBaseY(map);
            worldOffset = Streaming3D.OffsetOf(map);
            mountain = map.GetComponent<MountainField3D>();
            mountain.UpdateField();
            field = new LightField3D(map, baseY, mountain);
            mountains = new MountainLayer3D(map, baseY, worldOffset, mountain, field);
            terrain = new TerrainLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
            buildings = new BuildingLayer3D(map, baseY) { worldOffset = worldOffset, light = field, mountain = mountain };
            plants = new PlantLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
            items = new ItemLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
            pawns = new PawnLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
            water = new WaterLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
            grass = new GrassLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
            animals = new AnimalLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
            glow = new GlowLayer3D(map, baseY) { worldOffset = worldOffset, light = field };
        }

        // 水平流式偏移（由 Streaming3D 注册；本场景世界原点 = 图内局部 + 此偏移）
        public readonly Vector3 worldOffset;

        public void MarkAllDirty()
        {
            mountains.MarkAllDirty();
            terrain.MarkAllDirty();
            buildings.MarkAllDirty();
            plants.MarkAllDirty();
            items.MarkAllDirty();
            water.MarkAllDirty();
            grass.MarkAllDirty();
        }

        public void ForcePrimeLighting()
        {
            Lighting3D.BeginMap(map);
            Lighting3D.ClearLitMats();
            field.Prime(Lighting3D.Current);
            terrain.MarkAllColorDirty();
            buildings.MarkAllColorDirty();
            mountains.MarkAllColorDirty();
            water.MarkAllColorDirty();
            plants.MarkAllColorDirty();
            grass.MarkAllColorDirty();
            items.MarkBatchDirty();
        }

        public void NotifyTerrainChanged(IntVec3 c)
        {
            terrain.MarkCellDirty(c);
            buildings.NotifyRoofOrTerrainChanged(c);
            water.MarkCellDirty(c);
            grass.MarkCellDirty(c);
            plants.MarkCellDirty(c);
        }

        public void NotifyRoofChanged(IntVec3 c)
        {
            mountains.MarkCellDirty(c);
            buildings.NotifyRoofOrTerrainChanged(c);
            grass.MarkCellDirty(c);
            plants.MarkCellDirty(c);
        }

        public void NotifyThingChanged(Thing t)
        {
            if (t == null || t.def == null) return;
            plants.NotifyThingChanged(t);
            items.NotifyThingChanged(t);
            buildings.NotifyThingChanged(t);
            // 满格建筑压死地表格：增删时地表可见性翻转，地形 chunk 需联动重建
            if (t.def.category == ThingCategory.Building && t.def.Fillage == FillCategory.Full)
            {
                foreach (var c in GenAdj.OccupiedRect(t))
                {
                    terrain.MarkCellDirty(c);
                    grass.MarkCellDirty(c);
                    plants.MarkCellDirty(c);
                }
            }
        }

        // 逐层隔离：单层抛异常只丢该层该帧，不再中断后续层。
        // 之前 Update/Draw 整体一个大 try（在 Fps3DRoot），任何一层出错都会把排在后面的层
        // 全部吃掉 —— 动物层排在最后，最容易表现为"动物完全不显示"。隔离后既能保住其它层，
        // 又能由日志直接点名是哪一层、什么异常。
        // 同层 2 秒内只报一次，避免每帧刷屏把 Player.log 打爆。
        private static readonly System.Collections.Generic.Dictionary<string, float> lastErrAt
            = new System.Collections.Generic.Dictionary<string, float>();

        private static void Report(string layer, System.Exception e)
        {
            float now = Time.realtimeSinceStartup;
            float prev;
            if (lastErrAt.TryGetValue(layer, out prev) && now - prev < 2f) return;
            lastErrAt[layer] = now;
            var st = FirstPerson3DMod.settings;
            if (st != null) st.diagCross = "layerErr " + layer + " " + e.GetType().Name + ": " + e.Message;
            Log.Warning(ModEntry.LogPrefix + "layer " + layer + " error: " + e.GetType().Name
                + ": " + e.Message + "\n" + e.StackTrace);
        }

        public void Update()
        {
            var s = FirstPerson3DMod.settings;
            mountain.UpdateField();
            if (mountain.ShapeChanged) mountains.MarkAllDirty();
            var mountainChanges = mountain.Changed;
            for (int i = 0; i < mountainChanges.Count; i++)
            {
                int cell = mountainChanges[i];
                mountains.MarkCellDirty(new IntVec3(cell % map.Size.x, 0, cell / map.Size.x));
            }
            // 光照先推进：本轮标脏的 chunk 与当帧环境要和后面的几何重建、逐件绘制看到同一份数。
            try { TickLighting(); } catch (System.Exception e) { Report("lighting", e); }
            try { terrain.Update(); } catch (System.Exception e) { Report("terrain", e); }
            try { buildings.Update(); } catch (System.Exception e) { Report("buildings", e); }
            try { mountains.Update(); } catch (System.Exception e) { Report("mountains", e); }
            try { plants.Update(); } catch (System.Exception e) { Report("plants", e); }
            try { items.Update(); } catch (System.Exception e) { Report("items", e); }
            try { glow.Update(); } catch (System.Exception e) { Report("glow", e); }
            if (s == null || s.drawWater)
            {
                try { water.Update(); } catch (System.Exception e) { Report("water", e); }
            }
        }

        // 光照推进（F013）：扫一条带 -> 真变了的格标脏对应 chunk -> 天空亮度换档时整层重烘。
        // 关闭光照后不再写顶点色，画面回到光照落地前（AC-4 的反证口径）。
        private void TickLighting()
        {
            Lighting3D.BeginMap(map);
            if (!Lighting3D.Enabled) return;
            var env = Lighting3D.Current;
            // 光照参数（下限/强度/AO/软化/切块边长）改了必须重烘：几何没变，chunk 不会自己重来
            long key = Lighting3D.ParamKey();
            if (key != lastLightKey)
            {
                lastLightKey = key;
                Lighting3D.ClearLitMats();
                field.Prime(env);
                terrain.MarkAllDirty();
                buildings.MarkAllColorDirty();
                mountains.MarkAllColorDirty();
                water.MarkAllColorDirty();
                plants.MarkAllColorDirty();
                grass.MarkAllColorDirty();
                items.MarkBatchDirty();
            }
            // 没铺满就先铺满：数组初值全 0 时逐帧扫会带来整图 chunk 连续标脏的重建风暴（实机把桥顶超时）
            if (!field.Primed)
            {
                field.Prime(env);
                return;
            }
            field.Sweep(env);
            var ch = field.Changed;
            for (int i = 0; i < ch.Count; i++)
            {
                terrain.MarkCellColorDirty(ch[i]);
                buildings.MarkCellColorDirty(ch[i]);
                water.MarkCellColorDirty(ch[i]);
                plants.MarkCellColorDirty(ch[i]);
                grass.MarkCellColorDirty(ch[i]);
            }
            // F016 L8：随身光源挪动 -> 新旧两处标脏（与灯具变化同一条通路）
            var dyn = DynamicLights3D.Tick(map, Time.deltaTime);
            if (dyn != null)
            {
                for (int i = 0; i < dyn.Count; i++)
                {
                    terrain.MarkCellColorDirty(dyn[i]);
                    buildings.MarkCellColorDirty(dyn[i]);
                    water.MarkCellColorDirty(dyn[i]);
                    plants.MarkCellColorDirty(dyn[i]);
                    grass.MarkCellColorDirty(dyn[i]);
                    mountains.MarkCellColorDirty(dyn[i]);
                }
                if (dyn.Count > 0) items.MarkBatchDirty();
            }
            var lc = field.LampChanged;
            for (int i = 0; i < lc.Count; i++)
            {
                mountains.MarkCellColorDirty(lc[i]);
            }
            if (lc.Count > 0)
            {
                items.MarkBatchDirty();
            }
            // glow 是连续的（日出到日落几十分钟里一直在爬）。烘网格吃的是量化后的值，
            // 换档才整层重烘；再叠一层最短间隔，防止值正好卡在档位边界上来回翻。
            // 向阳投影的变化不需要在这里处理：光场扫到就会逐格标脏对应 chunk。
            rebakeTimer += Time.deltaTime;
            int qg = Mathf.RoundToInt(env.glow * 16f);
            if (lastBakedGlow == -999)
            {
                lastBakedGlow = qg;
            }
            else if (Mathf.Abs(qg - lastBakedGlow) >= 2 && rebakeTimer >= RebakeMinInterval)
            {
                rebakeTimer = 0f;
                lastBakedGlow = qg;
                terrain.MarkAllColorDirty();
                buildings.MarkAllColorDirty();
                water.MarkAllColorDirty();
                plants.MarkAllColorDirty();
                grass.MarkAllColorDirty();
                items.MarkBatchDirty();
            }
            // 原型网格的形体明暗走固定风格光，不随太阳变，所以这里不需要清原型缓存。
            // （以前每过 5° 清一次：7 个植物 OBJ 反复重导入、上百万顶点的实例批反复重建。）
        }

        /// <summary>光照导出（scripts/light_check.py 的输入）：本图各层网格的顶点色统计。</summary>
        public void WriteLightJson(System.Text.StringBuilder sb)
        {
            sb.Append("{\"id\":").Append(map.uniqueID);
            sb.Append(",\"baseY\":").Append(baseY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(",\"cells\":").Append(field.CellCount);
            sb.Append(",\"layers\":[");
            // 每层各给一份读取额度：串行共享会被前一层吃光，后面的层就只剩「通道有无」，
            // 机检的极值与朝向判据就再也看不到那几层（实机：foliage 7.5 万顶点全没样本）。
            const int LayerBudget = 45000;
            var pt = new Lighting3D.Probe { budget = LayerBudget };
            terrain.Summarize(pt);
            pt.Write(sb, "terrain");
            sb.Append(',');
            var pb = new Lighting3D.Probe { budget = LayerBudget };
            buildings.Summarize(pb);
            mountains.Summarize(pb);
            pb.Write(sb, "buildings");
            sb.Append(',');
            var pw = new Lighting3D.Probe { budget = LayerBudget };
            water.Summarize(pw);
            pw.Write(sb, "water");
            sb.Append(',');
            var pp = new Lighting3D.Probe { budget = LayerBudget };
            plants.Summarize(pp);
            grass.Summarize(pp);
            items.Summarize(pp);
            pp.Write(sb, "foliage");
            sb.Append("]}");
        }

        public void Draw(Vector3 camPos)
        {
            var s = FirstPerson3DMod.settings;
            // 逐件对象的实例乘子在绘制时才取样，先把本帧环境指回本图（同帧同图有缓存，不重复算）
            Lighting3D.BeginMap(map);
            // F016 L3：本图的投影上下文（各逐件绘制点 Cast 时用）
            Shadows3D.BeginScene(map, field, baseY, worldOffset);
            if (Fps3DState.DrawTerrain)
            {
                try { terrain.Draw(); } catch (System.Exception e) { Report("terrain", e); }
                if (s == null || s.drawWater)
                {
                    try { water.Draw(); } catch (System.Exception e) { Report("water", e); }
                }
            }
            if (Fps3DState.DrawBuildings)
            {
                try { buildings.Draw(); } catch (System.Exception e) { Report("buildings", e); }
                try { mountains.Draw(); } catch (System.Exception e) { Report("mountains", e); }
            }
            // 灯具发光片画在实体之后：加法混合，靠 renderQueue 排在 opaque 后面
            try { glow.Draw(); } catch (System.Exception e) { Report("glow", e); }
            if (Fps3DState.DrawPlants)
            {
                try { plants.Draw(camPos); } catch (System.Exception e) { Report("plants", e); }
                try { grass.Draw(camPos); } catch (System.Exception e) { Report("grass", e); }
            }
            if (Fps3DState.DrawItems)
            {
                try { items.Draw(camPos); } catch (System.Exception e) { Report("items", e); }
            }
            if (Fps3DState.DrawPawns)
            {
                try { pawns.Draw(camPos); } catch (System.Exception e) { Report("pawns", e); }
                try { animals.Draw(camPos); } catch (System.Exception e) { Report("animals", e); }
            }
            // 弹丸与射击效果画在最前一层：加法混合，压在 pawn 之后才不会被身体挡住
            try { ShotFx3D.DrawMap(map, baseY, worldOffset); } catch (System.Exception e) { Report("shotfx", e); }
        }

        public void Dispose()
        {
            DynamicLights3D.Forget(map);
            terrain.Dispose();
            buildings.Dispose();
            mountains.Dispose();
            plants.Dispose();
            items.Dispose();
            pawns.Dispose();
            water.Dispose();
            grass.Dispose();
            animals.Dispose();
            glow.Dispose();
        }
    }
}
