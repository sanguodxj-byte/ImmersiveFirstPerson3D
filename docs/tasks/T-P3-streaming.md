---
topics: [streaming, maps]
doc_kind: task
created: 2026-10-10
updated: 2026-10-10
feature: F010
status: hold
---

# T-P3: 给现有跨图补上「已有邻图」的登记

派发对象：快速模型。流式加载已经有原型。本任务只补：走到边缘前，把世界上已经生成的邻图登记进现有偏移表。跨图传送、落点搜索、调试生成都不要重写。

不依赖连续移动。没有贴墙碰撞时，现有落点搜索必须留着，否则相机会进墙。

## 已经有的，保持

`Streaming3D`：

- `placement`、`byId`、`streamed`、`homeMapId`、`streamedAt`。新图 `SceneBuildDelay` 0.6 秒后才建场景。
- `RenderMaps` = 当前图的垂直栈 ∪ `byId`。
- `StreamEast`：只在 `streamed.Count == 0` 时，用 `GetOrGenerateMap` 在最接近星球 +X 的邻接 tile 上生成 100×100，世界对象是 `FP3D_StreamNode`。主图第一次登记为偏移 0，并 `SceneRegistry.DisposeOf` 一次。清雾，放一圈木地板和木墙。这个早退条件不要放宽。
- `TryCrossBoundary`：四条边都认，用偏移对齐而不是假定两张图一样大。落点先钳在 `[2, size-3]`，再在半径 8 内找可站立、且 3×3 没有满格建筑的格子。然后 `DeSpawn` / `GenSpawn.Spawn`，恢复征召，`CurrentMap = target`。诊断写 `settings.diagCross`。
- `Fps3DRoot` 每帧在附身者非空时调用 `TryCrossBoundary`。
- `UnloadStreamed`：人站在流式图上就拒绝。调试卸载对非主图调用 `DeinitAndRemoveMap`。
- 存档走 `CaptureSaveData` / `RestoreFromSaveData`，读档不走 `RegisterPlacement` 的那次立即拆场景。

这些函数的签名和上述行为不要改。尤其不要删 `TryCrossBoundary` 里的 3×3 搜索。那是连续碰撞落地之后的另一次修改，不是本任务。

## 要补的登记

新增 `Streaming3D.EnsureNeighbors(Map cur)`，从 `Fps3DRoot` 里现有的 `TryCrossBoundary` 调用旁边调用一次。

- `cur` 为空或已销毁则返回。
- `homeMapId == -1` 时，只做一次和 `StreamEast` 相同的主图登记：`RegisterPlacement(cur, Vector3.zero)`。已经在 `byId` 里的图不要再次 `RegisterPlacement`，那个函数会拆掉场景并重新计 0.6 秒。
- 用 `cur.Tile.Layer` 的 `GetTileNeighborCount` / `GetTileNeighbor` / `GetTileCenter`，和 `StreamEast` 同一套星球空间差值 `v = neighborCenter - center`。
- 邻接 tile 上已有地图才登记：`Current.Game.FindMap(tile)`。返回空就跳过。不要调用 `GetOrGenerateMap`。
- 放置方向取 `v` 的主轴，这是 `StreamEast` 把「星球 +X」当成地图东边的同一近似，不要另做测地线：
  - `|v.x| >= |v.z|` 且 `v.x > 0`：偏移 = 当前图偏移 + `(cur.Size.x, 0, 0)`
  - `|v.x| >= |v.z|` 且 `v.x <= 0`：偏移 = 当前图偏移 - `(邻图.Size.x, 0, 0)`
  - 否则 `v.z > 0`：偏移 = 当前图偏移 + `(0, 0, cur.Size.z)`
  - 否则：偏移 = 当前图偏移 - `(0, 0, 邻图.Size.z)`
- 该地图已经在 `byId` 里就不要改它的偏移。同一侧已经有另一张图时跳过并打一条 `Log.Message`，不要叠到同一坐标。
- 登记用 `RegisterPlacement`，这样 0.6 秒延迟和渲染集仍然生效。`TryCrossBoundary` 会按这个偏移找到目标，不要再写第二套跨边。

## 验收

- [ ] 当前图的世界邻接 tile 上已经有地图时，走到对应边缘，`TryCrossBoundary` 能找到它。偏移公式和上面四条一致。
- [ ] 邻接 tile 没有地图时，走动不会生成新图。`streamEast` 在 `streamed` 为空时仍会生成测试图。
- [ ] `TryCrossBoundary` 的落点搜索、`diagCross`、存档恢复、0.6 秒延迟都还在。
- [ ] `dotnet build "Source\RimFirstPerson3D\RimFirstPerson3D.csproj"` 通过。不要部署。

## 不要做

- 不要把落点改成「接缝对面第一格」。没有贴墙碰撞时，那会把人送进墙里。
- 不要改 `UnloadStreamed` 的 `DeinitAndRemoveMap`。按距离只卸场景、保留世界上的定居点，是下一步，本任务不做。
- 不要改相机、准星、`BuildingLayer3D`。
- 不要把多张地图并成一张 `Map`。
