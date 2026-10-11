---
feature_ids: [F014]
related_features: [F003, F004, F013]
topics: [rendering, gameplay-feedback]
doc_kind: spec
created: 2026-10-12
---

# F014: 第一人称受击反馈（Minecraft 口径）

> Status: spec | Owner: 本代理

## Why

打中了没有反馈：pawn 中弹后画面毫无变化，玩家自己挨打也不知道 —— 原版靠的是头顶血条、飘字与红闪，而第一人称把相机拉近距离、立牌与模型都是无光照着色器，原版那套反馈有一半看不见。没有反馈的战斗里，玩家只能靠"敌人有没有倒下"倒推自己打没打中。

## What

两条通路，都模仿 Minecraft：

| 对象 | 效果 | Minecraft 对应 |
|---|---|---|
| 任意 pawn（殖民者 / 动物 / 敌人，人形与非人形都算） | 受伤后 0.5 s 内，同一份几何再叠一层红色，浓度线性退到 0 | `hurtTime` 10 tick 的红色叠层（模型用红色再画一遍） |
| 附身玩家 | 屏幕四边压出红色暗角，0.9 s 带余弦脉冲退掉；残血时另有一层常驻薄红边 | 伤害暗角（damage vignette，脉冲）+ 低血压常驻红雾 |

**为什么是叠层而不是改乘子**：本模组没有自定义着色器，只有「贴图 × 材质色 × 顶点色」这条乘子链（见 `docs/decisions/0004-vertex-color-lighting.md`）。乘子只能把颜色压暗，压不出"发红的亮" —— 深色护甲乘上红色只会变黑。所以走 `ShaderDatabase.Transparent` 的红色叠层：同 mesh、同 matrix、同贴图，只换材质，画在正常那一层之后。描边壳不叠（叠了只会糊掉轮廓）。

### 触发真值（禁止第二套口径）

| 量 | 唯一来源 |
|---|---|
| 谁受伤了、什么时候 | Harmony 后置 `Pawn.PostApplyDamage(DamageInfo, float)`。不轮询血量、不比对 hitPoints —— 那会把回血、处决、出生即伤都当成受击 |
| 闪多狠 | `max(totalDamageDealt, dinfo.Amount)` → `HitFxMath.PeakOf` |
| 残血程度 | `pawn.health.summaryHealth.SummaryHealthPercent`，与原版血条同一个数 |
| 退的时间 | `Time.realtimeSinceStartup`（墙钟）。暂停时红闪照旧退完，不会像 `TicksGame` 那样冻住 |

### 常数与曲线

真源在 `HitFxMath.cs`（纯函数，无 Verse、无贴图 —— 离线台链接的就是这一份）：

| 名 | 默认 | 含义 |
|---|---|---|
| `HurtDur` | 0.5 s | 生物红闪时长（MC 的 10 tick） |
| `VignDur` | 0.9 s | 玩家暗角余韵 |
| `PulseHz` | 2.2 | 暗角脉冲频率：只改变下降形状，不允许中途回弹到峰值之上 |
| `LowHealthFrom` | 0.40 | 血量比例低于此才开始常驻红边 |
| `LowHealthMax` | 0.35 | 见底时的红边浓度 |
| `BucketN` | 8 | 红闪浓度分档（克隆材质按档缓存，键含贴图实例号） |

两路暗角**取大不相加**：相加会让"残血再挨一发"直接顶满，整屏红到看不见敌人 —— 反馈读不出来就没有意义。

`hitFxScale`（设置项，0..2）只缩放结果，不改曲线形状。

### 验收通道

- 曲线：`dotnet run --project tools/LightSim` 里的 22 条 F014 判据（窗口边界、单调不回升、峰值夹住、包络不越界、脉冲确实存在、低血起点与上限、合成取大、伤害→强度单调），每条带反向对照。
- 贴图场与合成观感：`python scripts/hitfx_check.py` 出 `shots/hitfx_vignette.png` 与 `shots/hitfx_flash.png`，并断言红边带厚**按像素**横竖一致。反向对照用「按归一化半径」的写法（实测横 56 px / 竖 32 px，正是被禁的做法）。
- 实机：`hitfx: toggle` 做开关反证；`hitfx: hurt` 不走伤害管线直接点亮附身者的红闪与暗角（对照不该要求先找个人打架）。`dump lighting` 的 JSON 里带 `"hitfx":{on,flashing,player,overlays}`。

## Acceptance Criteria

- [x] AC-1: 曲线性质齐 —— 离线台 43 条判据（21 条光照 + 22 条受击）全过。
- [x] AC-2: 暗角与红闪的观感成立 —— 离屏合成图核对过：中心完全透明（不糊准星）、角上 0.985、带厚横竖都是 19 px；红闪 k=0.85 时形状保留、整块发亮红。
- [ ] AC-3: 实机红闪。需要真伤害：让附身者挨一发或打一只动物，看 pawn 是否罩红 0.5 s。**未测**。
- [ ] AC-4: 实机玩家暗角 + 开关反证（`hitfx: toggle` 前后画面必须只差这层红）。**未测**。
- [ ] AC-5: 帧时间。一屏同时挂红的 pawn 数与克隆材质数上限（`hitfx.overlays`）要有实测数；叠层是逐只一次 `DrawMesh`，只在 0.5 s 窗口内发生。**未测**。

## 已知未了

1. 死亡反馈没做：MC 还有白闪 + 倒下旋转，本项只有红闪；`Kill` 路径不经过红闪窗口。
2. 没有血液/星形粒子与受击音（MC 的 crit 星、原版飘字都不在本项范围）。
3. 红闪按「整只 pawn 一个浓度」，不区分被打的部位；MC 也是整体闪，所以先照做。
4. 附身者自己的红闪看不到（第一人称看不见自己身体），只有暗角 —— 与 MC 一致。
5. 叠层材质以贴图实例号为键：图集页换掉后旧材质会被 `!= null` 判定淘汰重建，未实测是否会残留。

## Dependencies

无阻塞依赖。与 F013 光照共用材质克隆缓存的路子（`Lighting3D.MatFor`），但两套缓存互不复用 —— 光照乘子与受击叠层是两种材质语义。

## Risk

`Pawn.PostApplyDamage` 是原版虚方法，别的模组（伤害类）也可能补丁它；本项只做后置且不改参数，冲突面小。红闪叠层走 `ShaderDatabase.Transparent`，其 alpha 由 `material.color.a` 驱动 —— 这条已由 `PlantLayer3D` 的半透明阴影在本模组里实机证实过，不是新假设。

## Open Questions

无。
