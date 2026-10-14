---
feature_ids: [F004]
related_features: [F003, F011]
topics: [input, interaction]
doc_kind: spec
created: 2026-10-10
---

# F004: 准星交互

> Status: coded | Owner: 实现见 `docs/tasks/T-P2-crosshair.md`（已收，待实机验收）。落地键位：左键只攻击（按住以 0.30 s 连击），门 / 拾取与装备 / 挖掘等快速情境动作在 E 键（`CameraRig3D.Interact`），右键对准目标弹**原版浮动菜单**（`CameraRig3D.OpenAimedFloatMenu` → `Thing.GetFloatMenuOptions`，一次覆盖工作台配方 / 翻找储物 / 装卸 / 使用 / 治疗 / 逮捕 / 收割等全部原版动作）。**F 键 = 搬运**（`CameraRig3D.CarryAimed`）：准星处原版可搬运（`EverHaulable`/`Minifiable`）的地面物走 `CarryTracker.TryStartCarry` 端在身前，再按 F 就地放下。空格纯为跳跃（F003）；接管期暂停与倍速改走玩家自己的原版键位（`KeyBindingDefOf.TogglePause` 等），不再占用空格。
>
> 携带物 / 手持武器的可视：世界侧 `PawnLayer3D.DrawHeld` 给**所有 pawn**（含正在 hauling 的其他殖民者）补画右手武器与身前搬运物（肚腹高度、低于头部，面向相机的小面片）；第一人称附身者的搬运物由 `Viewmodel3D.DrawCarried` 画在屏幕下半部，武器仍走 `Viewmodel3D.DrawWeapon`。原版 blit/立牌只烘服装+头，不含装备与携带物，故必须补画。

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
