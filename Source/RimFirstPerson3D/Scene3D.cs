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
        private readonly PlantLayer3D plants;
        private readonly ItemLayer3D items;
        private readonly PawnLayer3D pawns;
        // 环境与生物层（SkyScenery 为全局，不按图）
        private readonly WaterLayer3D water;
        private readonly GrassLayer3D grass;
        private readonly AnimalLayer3D animals;

        public Scene3D(Map map)
        {
            this.map = map;
            this.baseY = LevelStack3D.LevelBaseY(map);
            worldOffset = Streaming3D.OffsetOf(map);
            terrain = new TerrainLayer3D(map, baseY) { worldOffset = worldOffset };
            buildings = new BuildingLayer3D(map, baseY) { worldOffset = worldOffset };
            plants = new PlantLayer3D(map, baseY) { worldOffset = worldOffset };
            items = new ItemLayer3D(map, baseY) { worldOffset = worldOffset };
            pawns = new PawnLayer3D(map, baseY) { worldOffset = worldOffset };
            water = new WaterLayer3D(map, baseY) { worldOffset = worldOffset };
            grass = new GrassLayer3D(map, baseY) { worldOffset = worldOffset };
            animals = new AnimalLayer3D(map, baseY) { worldOffset = worldOffset };
        }

        // 水平流式偏移（由 Streaming3D 注册；本场景世界原点 = 图内局部 + 此偏移）
        public readonly Vector3 worldOffset;

        public void MarkAllDirty()
        {
            terrain.MarkAllDirty();
            buildings.MarkAllDirty();
            plants.MarkAllDirty();
            items.MarkAllDirty();
            water.MarkAllDirty();
            grass.MarkAllDirty();
        }

        public void NotifyTerrainChanged(IntVec3 c)
        {
            terrain.MarkCellDirty(c);
            water.MarkCellDirty(c);
            grass.MarkCellDirty(c);
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
                }
            }
        }

        public void Update()
        {
            terrain.Update();
            buildings.Update();
            plants.Update();
            items.Update();
            var s = FirstPerson3DMod.settings;
            if (s == null || s.drawWater) water.Update();
        }

        public void Draw(Vector3 camPos)
        {
            var s = FirstPerson3DMod.settings;
            if (Fps3DState.DrawTerrain)
            {
                terrain.Draw();
                if (s == null || s.drawWater) water.Draw();
            }
            if (Fps3DState.DrawBuildings) buildings.Draw();
            if (Fps3DState.DrawPlants)
            {
                plants.Draw(camPos);
                grass.Draw(camPos);
            }
            if (Fps3DState.DrawItems) items.Draw(camPos);
            if (Fps3DState.DrawPawns)
            {
                pawns.Draw(camPos);
                animals.Draw(camPos);
            }
        }

        public void Dispose()
        {
            terrain.Dispose();
            buildings.Dispose();
            plants.Dispose();
            items.Dispose();
            pawns.Dispose();
            water.Dispose();
            grass.Dispose();
            animals.Dispose();
        }
    }
}
