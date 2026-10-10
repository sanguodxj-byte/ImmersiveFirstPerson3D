---
feature_ids: [F007]
related_features: []
topics: [rendering, furniture]
doc_kind: spec
created: 2026-10-10
---

# F007: 家具与生产建筑目录

> Status: spec | Owner: TBD

## Why

运行时 `Furniture3D.ArchOf` 只认床、凳、床头柜、柜、架、扶手椅。桌子、餐椅、灯、沙发、柱、王座、工作台仍走立板。残骸只有 7 个样板。终态要求殖民地里的大型物件都以原版贴图为材质的网格出现。管线已经有了：`docs/furniture/ThreeViewMethod.md`。

## What

按三视图规范扩展目录，一件一类，每类独立可验收。优先殖民地起居里抬头就能看见的：

1. 餐桌（1×2、2×2、2×4、3×3 共用一个桌面原型，占格来自 def，高度用 `TableHeight`）
2. 餐椅
3. 灯（落地灯、壁灯）
4. 生产台（灶台、研究台、制作台这一批，先做占格矩形的台面 + 立面，不要求内部机械结构）
5. 残骸目录补到「地图上常见的古代家具」；每增加一个样板就往 `Wreckage3D.Types` 注册

每类都要：取样区进 `furn_atlas.py` 或 `wreck_atlas.py`、跑 UV 校验、`Handles` 返回 true 后立板路径不再画它。

## Acceptance Criteria

- [ ] AC-1: 餐桌从南面和东面看，立面像素来自对应朝向的原版图，顶面来自俯视段。高度为 `TableHeight`（0.75 m）。
- [ ] AC-2: 餐椅、落地灯在第一人称下不再是一张竖着的原版精灵。
- [ ] AC-3: 至少三台不同的生产建筑使用台体原型，侧面材质取样自各自原版贴图。
- [ ] AC-4: `scripts/furn_uv_check.py` 通过。未登记的建筑仍显示立板，不出现空气。

## Dependencies

无。不要为了本项改墙和山的分支。

## Risk

生产建筑贴图很多没有完整三视图。缺朝向时按三视图规范的 south → north → east 回退。回退后仍然是基本体，不退回立板。

## Open Questions

无。床卷保持地面薄垫，它已经在高度约定里单独规定，不进床架原型。
