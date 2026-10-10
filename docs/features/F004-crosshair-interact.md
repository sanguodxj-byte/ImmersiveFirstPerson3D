---
feature_ids: [F004]
related_features: [F003, F011]
topics: [input, interaction]
doc_kind: spec
created: 2026-10-10
---

# F004: 准星交互

> Status: coded | Owner: 实现见 `docs/tasks/T-P2-crosshair.md`（已收，待实机验收）。落地键位：左键只攻击，门 / 拾取与装备 / 挖掘在 E 键（`CameraRig3D.Attack` / `Interact`）

## Why

屏幕中心已经有射线：`CameraRig3D.RaycastCenter` 对全部渲染图打地面、满格建筑和别人的立牌，左键会选中命中物；打在空格子上时，给其他已选殖民者下 `Goto`。门不是满格，射线会穿过去。物品没有参与求交。第一人称探索还要在这条射线上开门、拾取、挖掘和攻击，结算仍走原版。

## What

- 从相机中心发一条射线，先打本模组的墙、门、家具、物品碰撞，再落到格子。
- 左键按命中类型调用原版能力：门开关、物品拾取进殖民者背包、对矿脉下挖掘、对敌对目标使用当前远程或近战 verb。
- 准星旁用游戏已有的 label 显示命中物名称，不自造说明文字。
- 距离上限用原版该动作的 range。超出则不发动作。

## Acceptance Criteria

- [ ] AC-1: 准星对着关闭的门按左键，门打开；再按一次关上。人不必先走到门上。
- [ ] AC-2: 准星对着地上物品按左键，物品进入该殖民者背包。
- [ ] AC-3: 准星对着天然岩按住左键，殖民者对其执行挖掘，岩格最终变为原版挖开后的地形。
- [ ] AC-4: 准星对着敌对 pawn 按左键，产生一次原版攻击结算（命中或未命中都算，伤害公式不改）。

## Dependencies

射线和点击已经在用当前相机，不需要等连续移动。和 T-P1 不要同时改 `CameraRig3D.cs`。T-P1 若先落地，它每帧 `StopDead` 时必须放过 `Mine`、`TakeInventory` 和动词施放。

## Risk

挖掘和拾取在原版里是 job，用 `TryTakeOrderedJob` 下发。现在的走路也是 `Goto`，这份作业会把它换掉。连续移动落地之后，持位移的那一帧不能再把这两个 job 清掉。

## Open Questions

无。
