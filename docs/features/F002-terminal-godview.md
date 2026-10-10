---
feature_ids: [F002]
related_features: []
topics: [camera, terminal]
doc_kind: spec
created: 2026-10-10
---

# F002: 上帝视角接到终端屏幕

> Status: coded | Owner: 本线

代码在 `GodView3D.cs`、`MapScreen3D.cs`、`VanillaSuppressor.cs`、`HandTerminal3D.cs`。`CameraRig3D` 只在查看期让出环视和准星点击，`Fps3DRoot` 去掉全屏接管。下面的验收还没有实机跑过。

## Why

视角切换的道具已经在仓库里：`1.6/Assets/hand_terminal.obj`。现在 M 键只是用它做抬起动画，闪白之后主相机变回原版正交俯视，屏幕上的贴图仍是自绘 UI。终态要求原版上帝视角出现在这块屏幕上，主画面留在第一人称。决策：`docs/decisions/0002-godview-on-terminal.md`。

## What

- 终端抬到眼前时，主相机保持透视与第一人称位置。
- 另有一台正交相机，参数与原版地图相机一致（俯视、`orthographicSize` 跟随原来的缩放），渲染到 `RenderTexture`，赋给 `HandTerminal3D` 的屏幕材质。
- `VanillaSuppressor` 改为认相机：主相机继续跳过原版地图网格和动态物；这台正交相机放行，且只写屏幕 RT。
- 屏幕上的指针能平移这台正交相机，并能点选 pawn。选中后放下终端，第一人称附身到该 pawn。
- 放下终端时销毁或停用 RT 相机，三维场景不重建。
- 整屏闪白后独占主相机的路径删除。

## Acceptance Criteria

- [ ] AC-1: 按 M 后，视野主体仍是第一人称殖民地，终端屏幕里能看到实时的原版俯视地图（墙、地形、殖民者在动）。
- [ ] AC-2: 指针打在屏幕上时可以平移地图并点选另一名殖民者；放下终端后附身变为该殖民者，人在原地。
- [ ] AC-3: 开关各一次后，chunk 没有全量重建（场景对象仍是开关前的那批），F7 的 p95 不因终端常驻绘制而持续高于打开前的两倍。
- [ ] AC-4: OBJ 缺失时不打开转场，行为与现在 `HandTerminal3D.Ready == false` 一致。

## Dependencies

无。`HandTerminal3D` 的加载、分组绘制和抬起插值直接复用。

## Risk

原版 UI 是屏幕空间 IMGUI。第一段必须先让地图画面进 RT。把整个 `UIRoot` 画进网格是另一件事；AC-2 允许在屏幕命中时临时把原版 UI 限制在屏幕矩形内，或只实现相机平移加 `Selector` 点选，不要求设计器界面全套出现在网格上。

## Open Questions

无。放大到全屏再操作可以作为后续手感优化，不作为本项验收。
