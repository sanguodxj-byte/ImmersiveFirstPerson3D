---
feature_ids: [F012]
related_features: [F006, F010]
topics: [performance]
doc_kind: spec
created: 2026-10-10
---

# F012: 性能门槛

> Status: spec | Owner: TBD

## Why

单图体素场景能靠「每帧重建 3 个 chunk」稳住。山体合并网格（F006）和多图同屏（F010）会把绘制和重建放大。建筑脏重建目前对每个 chunk 扫描全图 `AllThings`。家具、残骸、动物逐件 `DrawMesh`。没有按 chunk 的视锥剔除。

## What

- 建筑与岩体的脏标记带空间索引：一个物件变化只重建它覆盖的 chunk，重建时只遍历该 chunk 范围内的物件。
- 相机视锥外的 chunk 不提交绘制。
- 家具与残骸按材质合并后再画；合并范围不超过一个 chunk。
- 距离超过远裁剪一半的植物与人物降为单面片。
- 读数继续用 `PerfMon3D` 的 FPS、avgMs、p95Ms。不新增第二套性能日志。

## Acceptance Criteria

- [ ] AC-1: 250×250 单图、第一人称、墙和山都开启时，站立不动 p95 ≤ 16 ms（约 60 FPS 的一半预算留给模拟）。
- [ ] AC-2: 当前图加一张邻图同屏，p95 ≤ 22 ms。
- [ ] AC-3: 拆掉一块墙，该帧重建的 chunk 数 ≤ 该墙占格所跨的 chunk 数，不出现全图 `MarkAllDirty`。
- [ ] AC-4: 背对一片建筑时，该片 chunk 的绘制调用为零（视锥剔除）。

## Dependencies

F006 与 F010 完成后再做终验。空间索引可以更早做，终验数字以那两项落地后的场景为准。

## Risk

门槛是开发机上的目标，不是所有硬件的承诺。验收时在 F7 面板和 `perfSnapshot` 里记录分辨率与地图尺寸。

## Open Questions

无。
