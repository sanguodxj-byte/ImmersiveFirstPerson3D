---
topics: [input, interaction]
doc_kind: task
created: 2026-10-10
updated: 2026-10-10
feature: F004
status: accepted
---

# T-P2: 在现有准星射线上补交互

派发对象：快速模型。准星、射线、点击已经有原型。本任务只补命中类型和左键动作。不要另写一条射线。

不依赖连续移动。相机已经从屏幕中心发射线。不要和 T-P1 同时改 `CameraRig3D.cs`。

## 已经有的，保持

`CameraRig3D.RaycastCenter`（约 745 行）：

- `cam.ViewportPointToRay(0.5, 0.5)`。
- 对 `Streaming3D.RenderMaps` 里的每一张图做，用 `OffsetOf` 和 `LevelStack3D.LevelBaseY`。
- 先打该层地面平面。
- 再以 0.25 m 步进，只接受 `Fillage.Full` 的 edifice，高度在 `baseY` 到 `WallHeight` 之间。
- 再对其他 pawn 做 AABB（排除 `Possessed`），半宽 0.45，高为 `pawnHeight`。
- 取 `t` 最小的命中，上限 80 m。

`HandleClick`（约 701 行）：吃掉 `cmdClick` 和左键；`clickConsumedByLook` 或 `GUIUtility.hotControl != 0` 时不下命令。命中有 `thing` 就 `Find.Selector.Select`。只有格子时，给其他已选中的 pawn 下 `JobDefOf.Goto`。这两条都留下。

`Crosshair3D` 只画实心十字。`NameLabels3D` 把殖民者名字画在头顶，用的是 `GenMapUI.DrawPawnLabel`。不要改名牌，不要改十字的形状。

## 要补的命中

仍在 `RaycastCenter` 的那次步进里，`t` 从小到大：

- edifice 是 `Fillage.Full`：维持现在的命中并停止。门不是满格，现在会穿过门打到后面的地面或墙。edifice 是 `Building_Door` 时同样命中该门并停止。
- 这一步还没有更近的墙或门，且采样高度在 `baseY` 到 `baseY + 1.2f`，格上有 `ThingCategory.Item`：命中该物品并停止。
- 地面和 pawn 盒子的比较方式不变。墙比物品近时仍然是墙。

不要把步长改成 0.20。不要改成只查附身者脚下那一张图。不要加切比雪夫距离 1 的上限，80 m 和遮挡就是现有的距离。

## 要补的左键

仍在 `HandleClick`，`Select` 之后，仅当 `Possessed` 不为空：

- `Building_Door`：`Open` 为假则 `StartManualOpenBy(Possessed)`，为真则 `StartManualCloseBy(Possessed)`。栅栏门、动物帘也是 `Building_Door`，一并走这两个方法，不要用墙体任务里的门楣名单。
- `ThingCategory.Item`：照 `FloatMenuOptionProvider_PickUpItem` 的单件路径。`SetForbidden(false, false)`，`JobMaker.MakeJob(JobDefOf.TakeInventory, thing)`，`count = 1`，`checkEncumbrance = true`，`takeInventoryDelay = 120`，然后 `Possessed.jobs.TryTakeOrderedJob`。不要 `innerContainer.TryAdd`，不要用空格点击那条 `StartJob(Goto)`。
- `building.isNaturalRock` 或 `Mineable`：`JobMaker.MakeJob(JobDefOf.Mine, thing)`，同样 `TryTakeOrderedJob`。不要自己改地形。原版挖掘会让殖民者走过去，这是作业自己的距离，不要再卡 1 格。
- 其他 pawn，且 `thing.HostileTo(Possessed)`：`var verb = Possessed.TryGetAttackVerb(thing, true)`。`verb` 为空，或 `!verb.CanHitTarget(thing)`，则只保留 `Select`，不要攻击。否则 `verb.TryStartCastOn(thing)`。不要重算伤害，不要打友方。

空格子上给其他殖民者下 `Goto` 的分支不要删。

准星旁的名字：`Crosshair3D.Draw` 调用一次 `RaycastCenter`。命中有 `thing` 时，在十字旁边用 `Widgets.Label` 画 `thing.LabelCap`。没有命中就不画。不要新建一套说明文字，不要关掉 `NameLabels3D`。

## 验收

- [ ] 对着关闭的门左键，门打开；再按一次关上。射线不会再穿过门打到门后的地面。
- [ ] 对着地上物品左键，附身者收到 `TakeInventory`，而不是把物品直接塞进背包容器。
- [ ] 对着天然岩左键，附身者收到 `JobDefOf.Mine`。
- [ ] 对着敌对 pawn 左键，走 `TryStartCastOn`；对着自己人只是选中。
- [ ] 点空地时，其他已选殖民者仍会 `Goto`。多图、楼层偏移下，墙仍然挡住墙后的人和物品。
- [ ] `dotnet build "Source\RimFirstPerson3D\RimFirstPerson3D.csproj"` 通过。不要部署。

## 不要做

- 不要重写 `RaycastCenter` 的地图循环、地面平面、pawn 盒子和 80 m 上限。
- 不要改 `Streaming3D`、墙网格、移动积分。
- 不要为了「让路」去关一个还不存在的脚点。有作业时，现有的 `TryTakeOrderedJob` 会换掉走路的 `Goto`。
