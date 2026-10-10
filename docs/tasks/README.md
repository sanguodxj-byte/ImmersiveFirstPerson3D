---
topics: [backlog, tasks]
doc_kind: note
created: 2026-10-10
updated: 2026-10-10
---

# 可派发任务

功能规格仍在 `docs/features/`。这里只放已经写死、可以交给快速模型独立做完的实现任务。审美（取样矩形、轮廓、明暗手感）不在这些任务里。

派发时把对应任务文件整份给模型。不要两份任务同时改同一个源文件。

| 任务 | 对应功能 | 现在能否派发 | 会改的源文件 | 不要和谁同时派 |
|---|---|---|---|---|
| `T-W1-wall-atlas.md` | F005 的墙帽差分、门洞 | 已落地（本线），待实机验收 | `BuildingLayer3D.cs`，新建 `scripts/wall_atlas.py` | F006、任何再改 `BuildingLayer3D` 的工作 |
| `T-P1-free-move.md` | F003 的平地连续移动 | 代码验收通过 | `CameraRig3D.cs`、`PawnLayer3D.cs`、`Metrics3D.cs` | 已收。查看期早退不要改 |
| `T-P2-crosshair.md` | F004，接在现有 `RaycastCenter` 上 | 代码验收通过 | `CameraRig3D.cs` 的射线和 `HandleClick`，`Crosshair3D.cs` 只加名字 | 已收 |
| `T-P3-streaming.md` | F010 里「登记已有邻图」这一段 | 暂不收：`streamEast` 被主图登记挡住 | `Streaming3D.cs`，`Fps3DRoot.cs` 加一次调用 | 已收，待改早退冲突 |

## 留在本线，不派发

F002 已由本线写完，尚未实机验收。不要再派。`CameraRig3D` 里查看期的两处早退不要改语义。

这些项还缺审美结论。快速模型没有矩形或轮廓可用，派出去会自己发明一套。

| 功能 | 缺什么 | 谁来补 |
|---|---|---|
| F006 山体 | 岩贴图的顶/立面矩形，以及坡的轮廓。高度在楼板落地前只能夹在 2.70 m 和下一层 3.00 m 之间，这个夹法还没选定具体常数 | 本线先定取样和高度，再写机械任务。在那之前不要拆 `BuildingLayer3D` 的满格分支 |
| F007 家具目录 | 每件新家具的取样矩形。空的 SPEC 不能派 | 本线按 `docs/furniture/ThreeViewMethod.md` 逐件定矩形 |
| F008 屋顶与楼板 | 天花板用屋顶图的哪一块 | 本线选定后再派「按 `RoofGrid` 放在 `WallHeight`」的机械任务 |
| F009 上下楼 | 楼梯的形体 | 本线定轮廓后再派位移 |
| F011 探索闭环 | 室内有多暗、受击红边的强度 | 本线。格子亮度用 `GlowGrid` 这件事已经写在规格里，手感还没定 |

F012 先不派。chunk 级脏标记和视锥剔除已经在 `BuildingLayer3D`（`NotifyThingChanged`、`Draw` 里的 `Frustum3D.Visible`）。重建时虽然会扫 `AllThings`，但已经用占格和 chunk 矩形做了重叠判断。剩下的家具合批、远距离降面和帧时间门槛，要等山体和邻图落地后用 `PerfMon3D` 再量，不另开一套监控。
