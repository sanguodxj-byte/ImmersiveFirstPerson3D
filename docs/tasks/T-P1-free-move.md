---
topics: [input, movement]
doc_kind: task
created: 2026-10-10
feature: F003
status: accepted
---

# T-P1: 平地上的连续移动

派发对象：快速模型。行为以 `docs/features/F003-free-move.md` 为准，本文件把还没写死的数和挂钩补上。家具顶面碰撞、游泳、下蹲不在本任务。

## 常数

只往 `Metrics3D` 加这两项，业务代码引用它们，不要再写字面量：

- `JumpApex = 1.00f`（米）。起跳能到达的高度。
- `Gravity = 20f`（米/秒²）。

起跳竖直速度是 `sqrt(2f * Gravity * JumpApex)`，最高点就是 1.00 m。墙高 2.70 m，跳不上去。没有二段跳。

水平速度用 `pawn.GetStatValue(StatDefOf.MoveSpeed)`，单位是格/秒，也就是米/秒。按下即到这个速度，松开即 0。不要加速度曲线，不要另一张手感表。

## 位移

改 `CameraRig3D.HandleMovementOrders`。WASD 仍按当前偏航换成世界 XZ。不要再为走路调用 `jobs.StartJob(JobDefOf.Goto)`。

在第一人称持有位移的期间，每帧对附身者调用 `pawn.pather.StopDead()`，避免寻路把人拉回格心。附身者当前 job 是 `JobDefOf.Mine`、`JobDefOf.TakeInventory`，或正在施放动词时，这一帧不要 `StopDead`，也不要改他的位置。这三件事是 `HandleClick` 会下的原版指令。

碰撞只看目标点所在格：格在图内、`Walkable`、没有 `Fillage.Full` 的 edifice。先试 XZ，不行再试单轴 X、单轴 Z。不要加皮肤半径，不要加 `MeshCollider`。人可以站到格子边缘，贴着墙面。

暂停（`TickManager.Paused`）时水平速度和竖直速度都记为 0，不补移动。环视代码不要动。

跨格时写 `pawn.Position`。不要为走路 `DeSpawn`。同格内的亚格位置存在控制器里。

在 `CameraRig3D` 上提供一个空值表示「未启用」的脚点（世界 XZ 加离地高度）。启用时：

- 第一人称和追尾相机的锚点 XZ 用这个脚点，不用 `DrawPos`。Y 仍是该 pawn 所在场景的 `baseY`，再加离地高度，眼高仍是 `Fps3DState.EyeHeight`。
- `PawnLayer3D` 只对这个附身者改绘制用的 XZ。其他 pawn 仍用 `DrawPos`。

跳跃：着地时按空格，竖直速度设为上面的起跳速度。每帧 `vy -= Gravity * dt`，离地高度加上 `vy * dt`。降到 0 时钳住并清零竖直速度。空中再按空格无效。本任务的地面就是所在场景的 `baseY`，不探测家具网格。

## 保持不动的行为

附身时强制征召、退出时恢复 `prevDrafted` 保持现状。远控的 `cmdWalkHold`、`cmdKey`、`cmdTeleport` 继续能走，只是走路不再变成 `Goto`。`cmdTeleport` 仍是瞬移。

`freeCam` 分支不要改。

## 验收

- [ ] 按住 W 沿视线连续走，松开立即停，日志和 job 里不出现这次行走的 `Goto`。
- [ ] 斜向顶墙时沿墙滑，相机不进满格建筑。
- [ ] 空格离地最高 1.00 m，然后回到 `baseY`。跳不上一面 2.70 m 的墙。
- [ ] 暂停期间脚点不动。
- [ ] `dotnet build "Source\RimFirstPerson3D\RimFirstPerson3D.csproj"` 通过。不要部署。

## 不要做

- 不要改 `VanillaSuppressor`、终端、墙体网格。
- 不要取消强制征召。
- 不要给家具加碰撞体来完成「跳上床」。那是后续任务。
