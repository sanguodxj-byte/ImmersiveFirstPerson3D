---
feature_ids: [F009]
related_features: [F003, F008]
topics: [movement, levels]
doc_kind: spec
created: 2026-10-10
---

# F009: 走上楼与走下楼

> Status: spec | Owner: TBD

## Why

`LevelStack3D.SwitchLevel` 只改 `CurrentMap`。`CameraRig3D` 发现附身者的图与当前图不一致时会重新 `Possess`，所以上下楼等于换了一个人。栈和层高已经能把两张图画在正确的 Y 上。缺的是同一个人沿垂直方向走到另一张图。

## What

- 指定楼梯建筑（先覆盖原版楼梯若存在；没有则用本模组的一个楼梯 Thing，占格与高度写进 `Metrics3D`）。
- 附身者走上升段时，连续高度从本层 `baseY` 变到上层 `baseY`，结束时 `DeSpawn`/`Spawn` 到上层对应格，征召与背包保留，附身对象不变。
- 下楼对称。
- PageUp / PageDown 保留为调试切换，不再是玩家上下楼的方式。
- 没有楼梯的地方不能靠跳跃进入上层（跳跃高度见 F003）。

## Acceptance Criteria

- [ ] AC-1: 从底层沿楼梯走到顶，附身的仍是同一个 pawn（名字与 thingID 不变），所在图变为上层。
- [ ] AC-2: 上楼过程中相机 Y 连续变化，没有整段传送。
- [ ] AC-3: 上层地板遮住下层天花板，人在楼上看不见楼下房间内部。
- [ ] AC-4: 存档读档后，栈关系还在，人留在上楼后的那张图。

## Dependencies

F003 的连续位移。F008 的楼板，否则上楼后没有地面。

## Risk

上层是口袋图时，原版温度和天气读的是同一个世界格。这是决策 0001 已接受的结果。本项不新开世界格。

## Open Questions

无。
