---
feature_ids: [F015]
related_features: [F003, F004, F013, F014]
topics: [rendering, gameplay-feedback]
doc_kind: spec
created: 2026-10-12
---

# F015: 武器射击效果

> Status: spec | Owner: 本代理

## Why

第一人称压掉了原版绘制（`VanillaSuppressor` 跳过 `DynamicDrawManager.DrawDynamicThings`），后果之一是**弹丸在 3D 里根本不存在**：枪响了，什么也看不见 —— 没有子弹、没有火光、没有落点。所以本项第一件事不是加特效，是把原版已经有的东西画出来，再补上开火与命中的可见反馈。

## What

四件东西，两个真值触发：

| 元素 | 怎么画 | 位置来源 |
|---|---|---|
| 弹丸本体 | 原版弹丸贴图（图集子块在 `mainTextureScale/Offset` 上，单位面片 UV 0..1 正好采到），朝向相机 | `Projectile.ExactPosition`（抛物线弹的覆写一并白拿，不自算弹道） |
| 拖焰 | 沿弹道的相机朝向缎带，加法混合，长度 = 弹速 × `TracerHold` 夹进 [min,max] | 方向取 `Projectile.ExactRotation·forward`（原版算好的水平弹道） |
| 枪口火光 | 发射点沿弹道水平前移 0.32 m 的一团加法光斑（不挪会埋在发射者躯干里） | `Projectile.Launch` 的 `origin` 参数 |
| 落点烟尘 | 一朵边淡边长的加法爆点；打在 pawn 上偏红，打在墙上偏灰 | `Projectile.Impact` 时的 `DrawPos` |
| 视图模型火光 | 附身者开火时，在**枪口锚点**上亮一下 | 锚点就是 F013 视图模型那套变换把枪口钉住的那个屏幕点，不另算位置 |

**触发只有两个真值**：`Projectile.Launch` 与 `Projectile.Impact` 的 Harmony 后置。不自算命中、不自算弹道、不做「假开火」的调试动作 —— 那会绕开这两个真值，测了等于没测。

**加法混合必须用预乘光斑**：`ProcTex3D.AdditiveRadial`（RGB 已乘 alpha）与 `ProcTex3D.Radial`（白 RGB + alpha 衰减，给 GUI 普通混合用）是两回事，混用会糊出一块亮方片。灯具发光片（F013）原来自带一份私有剖面，已改吃这同一份，不留第二套。

### 常数

真源在 `ShotFxMath.cs`（纯函数，离线台链接的就是这一份）：

| 名 | 默认 | 含义 |
|---|---|---|
| `FlashDur` | 0.07 s | 枪口火光时长（比烟尘短命：火光没了烟尘还在，读得出弹道） |
| `PuffDur` | 0.26 s | 落点烟尘时长 |
| `TracerHold` | 0.05 s | 拖焰长度 = 弹速 × 这个保持时间。按速度而不是「全程比例」：同一把枪打近打远拖焰一样长才像回事 |
| `TracerMin` / `TracerMax` | 0.30 / 2.60 m | 拖焰上下限 |
| `TracerThick` | 0.055 m | 拖焰厚度（太细在 4K 下会糊成一条不存在的线） |
| `PuffGrow` | 2.1 | 烟尘在窗口内长到几倍底尺寸 |
| `MuzzleForward` | 0.32 m | 枪口火光前移量 |

设置项：`shotFx`（总开关）、`shotFxScale`（0..2 倍率，只缩放结果不改曲线）。

弹丸高度口径：原版 `origin.y` 是绘制层海拔（子弹层十几米），本模组的 y 是「离地几米」，两者不能混用 —— 枪口高按 `PawnHeight × 0.62 × 发射者 baseBodySize` 给，动物低、机甲高。

## 验收通道

- 曲线：`dotnet run --project tools/LightSim` 的 F015 段（衰减窗口、非法时长、拖焰单调与夹限、尾端方向、烟尘边淡边长、透明度上限、枪口水平前移、退化方向不炸 NaN、火光比烟尘短命），每条带反向对照。
- 贴图与合成：`python scripts/shotfx_check.py` 出 `shots/shotfx_preview.png`，常数从 C# 源码正则读出（改名就报错），断言预乘光斑逐像素 RGB==A、中心满、四角零、沿半径单调；反向对照把光斑换成非预乘写法（4084 个像素不符）必须被绊倒。
- 演示：`dotnet run --project tools/LightSim -- --demo` 出帧表，`python scripts/shotfx_demo.py` 渲染成胶片与 GIF（`shots/shotfx_demo_strip.png` / `.gif`）。C# 只出数、python 只出图：弹丸位置、拖焰长度与厚度、火光与烟尘的尺寸与透明度、目标红闪、玩家暗角，全部来自 `ShotFxMath` / `HitFxMath` / `LightShade`，连地面与人物底色都是 F013 公式的输出。四条判据量在像素上（火光把枪口画亮、红闪让目标偏红且随强度单调、暗角让边比中心红且中心仍清晰、弹丸投影沿弹道单调推进），对照一律取「同一帧去掉这一个字段」。
- 实机：`shotfx: toggle` 做开关反证；真开火走 GABS 的 `cmdClick`（原版射击作业 → 真 `Launch`/`Impact`）。`dump lighting` 的 JSON 里带 `"shotfx":{on,projectiles,tracers,flashes,puffs,queued,mats}`。

## Acceptance Criteria

- [x] AC-1: 曲线与贴图性质 —— 离线台 57 条判据（21 光照 + 22 受击 + 14 射击）全过；`shotfx_check.py` 全过。
- [ ] AC-2: 实机看得见弹丸。附身者持枪打一枪，`shotfx.projectiles` 与 `tracers` 必须大于 0。**未测**。
- [ ] AC-3: 实机枪口火光与落点烟尘（`flashes` / `puffs` 大于 0），且 `shotfx: toggle` 前后同机位截图只差这几层。**未测**。
- [ ] AC-4: 缎带朝向。拖焰是「长轴沿弹道、法线垂直视线」的 ribbon：正对相机时按 0.995 阈值主动不画（退化成一条视线）。若实机发现背面剔除导致单侧不可见，翻 `col2` 即可。**未测**。
- [ ] AC-5: 帧时间与上限。事件表 96 条封顶、加法材质按 8 档浓度缓存；一场大战的 `queued` / `mats` 实测数。**未测**。

## 已知未了

1. 没有弹孔贴花（decal）与抛壳 —— 本项只做"这一枪打得出去"的可见性。
2. 爆炸类弹丸（`Projectile_Explosive`）的 `Impact` 会照常出一朵烟尘，真正的爆炸闪光/火球归原版 F011 的战斗入口，不在本项。
3. 抛射类（迫击炮）弹道靠原版 `ExactPosition` 覆写，本项只在水平方向上拉缎带 —— 大仰角时缎带会显得平。
4. 枪口高度按体型估算，不是原版 `shotOrigin` 的真实枪管位置（那个值原版也没公开出来）。

## Dependencies

无阻塞依赖。与 F014 受击反馈共用「事件表 + 浓度分档材质缓存」的写法，但两套状态各自独立（一个是"谁在闪"，一个是"哪里刚响过"）。

## Risk

`Projectile.Launch` / `Impact` 是原版虚方法，伤害类模组常补丁它们；本项只做后置、不改参数，冲突面小。缎带的可见面方向（winding / 背面剔除）是唯一没法离线验的点，已列为 AC-4。

## Open Questions

无。
