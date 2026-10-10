---
topics: [backlog]
doc_kind: note
created: 2026-10-09
updated: 2026-10-10
---

# 任务列表

> 只保留未完成项。完成后再从本表删除，并在对应 `docs/features/` 文末注明落地文件。
> 架构与现状：`docs/Architecture.md`。高度：`docs/HeightConvention.md`。

推荐顺序是依赖顺序。同一阶段里没有互相引用的条目可以并行。

能直接派给快速模型的实现任务在 `docs/tasks/README.md`。墙的取样与机械部分（`T-W1`）已落地，待实机验收。需要看图才能定的取样和轮廓留在本线，不写进那些任务。

| ID | 名称 | 状态 | 阶段 | 依赖 | 链接 |
|----|------|------|------|------|------|
| F002 | 上帝视角接到终端屏幕 | 已编码 | 1 视角 | 无 | `docs/features/F002-terminal-godview.md` |
| F003 | 连续移动（MC 手感） | 已编码 | 2 操作 | 无 | `docs/features/F003-free-move.md` |
| F004 | 准星交互 | 已编码 | 2 操作 | F003 | `docs/features/F004-crosshair-interact.md` |
| F005 | 墙体几何与原版材质 | 已编码 | 3 大型网格 | 无 | `docs/features/F005-walls.md` |
| F006 | 山体与大型岩体 | spec | 3 大型网格 | 无 | `docs/features/F006-mountains.md` |
| F007 | 家具与生产建筑目录 | 部分落地 | 3 大型网格 | 无 | `docs/features/F007-furniture-catalog.md` |
| F008 | 屋顶与楼板 | spec | 4 楼层 | F005 | `docs/features/F008-roofs-floors.md` |
| F009 | 走上楼与走下楼 | spec | 4 楼层 | F003, F008 | `docs/features/F009-vertical-travel.md` |
| F010 | 邻图随走动加载 | spec | 5 地图 | F003 | `docs/features/F010-streaming.md` |
| F011 | 自由探索闭环 | spec | 6 玩法 | F003, F004, F010 | `docs/features/F011-exploration.md` |
| F012 | 性能门槛 | spec | 7 性能 | F006, F010 | `docs/features/F012-perf-budget.md` |

## 阶段说明

1. **视角。** 资产和抬起动画已经有了。把原版俯视从「占满主相机」改到「贴在屏幕上」。
2. **操作。** 现在的 WASD 是寻路作业。自由探索和跨图都要先变成连续位移，否则上下楼和邻图仍是传送。
3. **大型网格。** 墙、山、家具分三条线，共用 `Metrics3D` 和「原版贴图当材质」。谁也不等谁。
4. **楼层。** 先有天花板和楼板，人再沿楼梯换图。栈注册表保持 `docs/decisions/0001-stacked-maps.md`。
5. **地图。** 东向调试加载扩成以玩家为中心的邻接加载与卸载。
6. **玩法。** 在能走、能瞄准、能跨图之后，补上第一人称里的探索节奏（照明、揭示、兴趣点、战斗入口）。战斗数值仍走原版。
7. **性能。** 山体和多图会放大绘制量。门槛用现有 `PerfMon3D` 读数，不提前做一套新监控。

## 明确不单列的事

- 人物和动物做成蒙皮网格。终态画面要完整，但当前立牌和动物原型已经能辨认。等 F012 有余量再开，避免和墙山抢同一批绘制预算。
- 重写需求、工作、射击结算。探索玩法调用原版 verb 和 job。
- 把多张地图合并成一张巨型 `Map`。见决策 0001。
