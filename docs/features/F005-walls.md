---
feature_ids: [F005]
related_features: [F006, F008]
topics: [rendering, buildings]
doc_kind: spec
created: 2026-10-10
---

# F005: 墙体几何与原版材质

> Status: coded | Owner: T-W1 已由本线落地（`BakeWall` / `BakeDoorHeader` + `WallAtlas`），待实机验收

## Why

玩家建造的墙已经是高 2.70 m 的体素盒子，邻面剔除，材质来自原版连接图集。四个立面和顶面共用这一格的 0..1 UV，顶上看起来仍是立面。门是一块 0.18 m 厚的板，门洞上方是空的。终态里墙要在第一人称下能认出原版的砖、木、光滑墙，并且顶面是墙帽。

## What

做法已记在 `docs/decisions/0003-wall-atlas.md`。取样矩形在 `docs/walls/WallAtlasSpec.md`。

- 只处理 `ThingDefOf.Wall` 和名单内的门。天然岩继续现在的盒子，交给 F006。
- 从原版三张墙图各取墙帽和立面，打进一张图集，UV 贴回现有盒子。一个外观加一种染色共用一个材质。立面贴一次，铺满墙高，不垂直重复。
- 门洞：门格朝房间的那一面让出宽 `DoorWidth`、高 `DoorHeight` 的口，门楣留到 `WallHeight`；门洞侧壁发整面（墙有 1 m 厚，侧壁中间开口会在隧道两侧留透光的槽）。打开的门不发门扇。
- 外轮廓仍用反绕壳，不把原版黑边画进材质。

## Acceptance Criteria

- [ ] AC-1: 一段石墙、一段钢墙并排时，第一人称能从侧面纹理区分两者，纹理来自原版贴图。
- [ ] AC-2: 两面墙的内角不再出现多余的朝内面（邻面剔除保持）。
- [ ] AC-3: 关着的门挡住通过；打开后能看见门后的房间，门板几何让开洞口。
- [ ] AC-4: 改一块墙的材质后，只有所在 chunk 重建，同图其他 chunk 的网格对象不变。

## Dependencies

无。F008 的楼板搭在墙顶高度上，依赖本项把「建造墙的顶」固定在 `WallHeight`。

## Risk

连接子格的 scale/offset 只能框住一段像素。顶面和立面要不同，就必须换成网格 UV 指向自己的图集，不能继续 `CloneForBox`。图集加载失败时退回现在的子格。

金属外观没有独立墙图，使用 smooth 段再染色。意识形态建筑风格的墙图本次不取样。

## Open Questions

无。矩形和过滤方式已在取样规格里定死。
