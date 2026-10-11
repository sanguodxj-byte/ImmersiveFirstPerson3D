---
feature_ids: [F013]
related_features: [F005, F007, F011, F012]
topics: [rendering, lighting]
doc_kind: spec
created: 2026-10-11
---

# F013: 三维光照系统

> Status: spec | Owner: 本代理

## Why

第一人称下所有几何都是平涂：除了手持终端，没有任何网格写顶点色，`Graphics.DrawMesh` 用的又是原版无光照着色器。于是正午和半夜一个亮度、向阳墙和背阴墙一个亮度、灯开了和没开一个亮度，灯具本身也只是块不发光的模型。F011 的「照明」那条要的是同一件事，本项把它独立出来做完。

## What

两条载体，不写新的光照着色器（取舍见 `docs/decisions/0004-vertex-color-lighting.md`）：

| 载体 | 用在哪 | 写法 |
|---|---|---|
| 顶点色 RGB | 合批几何：地形、墙/岩/门 chunk、山体、水体岸壁、植物/草/物品实例批、植物/家具/残骸原型网格的形体明暗与根部 AO | 烘进网格 `SetColors`，随几何与光场重建 |
| 实例材质乘子 | 逐件绘制、共享原型的对象：家具、残骸、动物、人物立牌、第一人称空手与手持武器 | 克隆材质或 GUI 染色，`color = 原色 × 该处光场`，按量化档缓存 |

### 数据真值（禁止第二套口径）

| 量 | 唯一来源 |
|---|---|
| 太阳方向、天空亮度、晨昏与天气衰减 | `SkyScenery` 当帧 `Env`（`sunDir` / `glow` / `twilight` / `cloudiness`）。本项把它暴露出来，光照与天空同源，不许另算太阳角 |
| 灯具彩色辉光 | `map.glowGrid.VisualGlowAt(index)`。含灯具颜色、暗光、断电、挡光板，全是原版数值 |
| 灯是否点亮、发光颜色与半径 | `CompGlower.ShouldBeLitNow` / `GlowColor` / `GlowRadius` |
| 屋顶遮蔽 | `map.roofGrid` + `RoofDef.isThickRoof` |
| 挡光体与高度 | `map.edificeGrid` + `ThingDef.blockLight` + `Metrics3D` 高度约定（墙 2.70 / 岩 2.92 / 层高 3.00） |

米制一律走 `Metrics3D`，亮度增益一律走下面表里的具名常数，不散落在调用点。

### 逐顶点着色公式

```
N   = 该面声明的外法线（发射时显式给，不依赖 RecalculateNormals 的符号）
L   = Env.sunDir（世界空间，指向太阳）
ndl = N · L

amb  = ambColor × ambGain × skyGlow × skyVis(N) × ao
       skyGlow  = Env.glow（0 夜 .. 1 正午，已含天气衰减）
       ambColor = 用 Env 天空三段色做半球光：按 0.5+0.5·N.y 在 horizon 色与天顶色间插值
       skyVis   = 露天 1.0 ｜ 薄屋顶 ThinRoofSkyThrough ｜ 厚屋顶 0.0
sun  = sunColor × sunShape(ndl) × shadow × ao × sunStrength
       sunShape = smoothstep(-SoftFoot, SoftTop, ndl)   // 风格层：SoftFoot/SoftTop 可调
       sunColor = 白 ×(1-twilight) + Env.sunGlowHorizon × twilight   // 晨昏转暖
       shadow   = 体素投影，按格×两高度带预计算，四邻双线性 → 软边
                  skyVis=0 的格直接置 0：厚屋顶既遮天光也遮直射
moon = moonColor × max(0, N·Moon) × moonStrength × (1-skyGlow) × skyVis × ao
       moonColor = 偏冷近白 (0.78, 0.85, 1.00)，刻意不取夜空色相（理由见常数表 MoonStrength 行）
lamp = glowGrid 彩色 × LampGain × lampStrength（双线性）
emi  = 应发光建筑自身几何的亮度下限 EmissiveFloor

C = clamp01(max(LightFloor, Exposure × (amb + sun + moon + lamp))) ；自发光件再 max(emi, ·)
顶点色 = (C.r, C.g, C.b, 该层原有 alpha 语义)
```

逐件对象与实例批（植被/草/物品/家具/动物/人物）不按「朝上面」取这个 `C`，而按漫反射凸体的有效受光积分取一个不带方向的放置亮度：`amb` 取半球权重 0.70、`sun` 取 0.62、`moon` 取 0.45，同样过曝光与下限，再乘进实例材质色或合并网格顶点色（实例批顶点色下限同样夹在 `LightFloor` 之上）；方向感与根部遮蔽由原型网格的形体明暗（`FormShape` 与植物垂直 AO，固定风格光方向）承担。厚屋顶下放置亮度的天光与月光项同样归零，只留灯具。

`Exposure` 是实测量出来的：不加它，正午的 `amb + sun` 普遍超过 1，向阳面与天光面全被截成纯白（一层 9252 个北立面顶点里 8020 个是纯白），画面看上去"任何时候都没有光照"。

常数与默认值（真源在 `Lighting3D.cs`，设置项可实时覆盖）：

| 名 | 默认 | 含义 |
|---|---|---|
| `LightFloor` | 0.16 | 无任何光时的可辨认下限。设置项 0..0.5 |
| `Exposure` | 0.62 | 合成后的总曝光。不加这一档，正午 `amb+sun` 超过 1 被截成纯白，看不出光照 |
| `AmbGain` | 0.45 | 天光（环境项）增益，在 `AmbientColor` 内乘，山体那条通路也走它。调色板按最大通道归一后天光能到 1.0，正午再叠法向直射 1.0 必然截断：实机量到 terrain 朝上均值 248/255、模拟台量到朝上面 b=1.000。晴天正午的漫射本就不到法向直射的一半，0.45 是这个比例，剩下的头部空间留给明暗差 |
| `MoonStrength` | 0.45 | 月夜弱直射。原先 0.20：乘上曝光只有 0.124，被 `LightFloor` 0.16 整个吃掉，模拟台量到朝月面与背月面 Δ=0（夜里没有月光方向）。月色也从「夜空色相归一」改成偏冷近白 —— 归一把很暗的夜空当满亮度重放，只剩蓝通道够量级，而蓝的亮度权重只有 0.0722 |
| `SunStrength` | 1.00 | 直射项增益 |
| `LampGain` | 3.6 | `glowGrid` 原色 → 亮度，与原版 `GlowGrid.GroundGameGlowFactor` 同值；`overlit` 哨兵直接取 1.0 |
| `LampStrength` | 1.00 | 灯具项总增益 |
| `AoStrength` | 0.45 | 接触阴影深度 |
| `SoftFoot` / `SoftTop` | -0.10 / 0.35 | 向阳项软化区间；两者相等即赛璐璐硬分档 |
| `ThinRoofSkyThrough` | 0.35 | 薄屋顶透过的天光比例 |
| `EmissiveFloor` | 0.85 | 应发光的灯具自身亮度下限 |
| `ShadowMaxSteps` | 15 | 投影射线步数，与原版 `GenCelestial.ShadowMaxLengthDay` 同值 |
| `ShadowSampleLow` | 0.40 m | 投影低带（地面受影）的射线起点高度 |
| 投影高带 | `WallHeight × 0.9` = 2.43 m | 竖立面上部用的第二条射线高度 |
| `AoMin` | 0.35 | 接触阴影的最深倍率下限，防止内凹角被压成纯黑 |

### 接触阴影（AO）

顶点处取该面平面上的四邻占用（实心格 / 屋顶 / 地面高于该点），按经典体素角 AO 加权 `AoStrength`。墙脚压暗地面、墙角压暗墙面、凹角最深。地面合批矩形被切到 `lightQuadStep`（默认 2 m）以内，保证角点采样有分辨率。

### alpha 语义不得改动

地形顶点的 `colors.a` 是软边遮罩（`TerrainFadeRough` 靠它切过渡），光照只写 RGB，A 原样保留。水体的深度分档继续走 `material.color`，本项不给 `Map/Transparent` 许诺顶点色支持。

### 刷新与失效

- 每图一份光场缓存（`LightField3D`）：逐格灯具彩色、天空遮蔽项、投影项、占用项。
- 量化失效：`Env.glow` 取 1/16 步、太阳方位取 5°、灯具彩色取 1/8 步；屋顶/建筑/地形变化由扫描带自然带出。
- 两条防抖：整层重烘最短间隔 1.5 s；原型网格缓存（家具/残骸/动物/植物模型）全局冷却 8 s。实机踩过没有冷却的后果 —— 渲染集合里每张图各判各的量化档，交替把同一份全局缓存清成反复重建，7 个植物 OBJ 被反复重导入。
- 闪电 `Env.flash` 不进烘焙通路：顶点色是一次烘好、按档重烘的量，容不下每帧翻转的瞬时项。雷暴增亮留给以后的非烘焙载体。
- 残骸的朝向烘进网格缓存（按 样板名+角度 分键）：网格留在模型空间的话，物体一转、被照亮的那一面也跟着转。动物因为朝向是连续平滑的，仍按模型空间烘，形状跟着身子转 —— 已知取舍。
- 光场变化只把受影响的 chunk 标脏，重建继续吃 `Fps3DState.ChunkRebuildBudget`，不新建第二条重建通路。
- 逐件对象的实例材质按量化亮度档缓存，键含源材质实例号；上限 `LitMatCacheCap`，超限整表清空。绝不改原版池化材质。

### 光源可见化

带 `CompGlower` 且当前应发光的建筑，画贴地光池（`Util3D.GroundQuad`）与灯头光晕（三片 60° 交叉面 `Util3D.StarMesh`），材质走 `ShaderDatabase.WorldOverlayAdditive` 纯加法发光（零预乘黑边），光池直径按 `GlowRadius` 收敛，灯头光晕锚定在灯具本体灯芯高度并控制在灯头尺度，颜色 = `GlowColor × 亮度 × LampStrength`，未通电/未点燃不画。家具灯具的灯泡部件在原型网格里标自发光，不吃暗部。

### 验收通道

三条，都不产生第二套光照数值：

- **离线模拟场景台 `tools/LightSim`（进游戏前的闸门）**：`dotnet run --project tools/LightSim`，非 0 退出即未通过。合成场景 24×24（一堵墙顶高 2.70、一间厚屋顶房、房内一盏红灯笼、手工投影块、墙脚与房角的接触阴影），实现 `ILightField` 把逐格数值喂进**同一份** `LightShade.cs`（csproj 直接链接该文件，不是复制公式），所以台子过 == 游戏里过。21 条判据每条带反向对照（关 AO / 关投影 / 曝光调高 / 法线取反），全绿用时 <2 s —— 一轮实机要 10 分钟，光照观感的性质不该靠实机试。
- `lightSnapshot` / `dump lighting`：一次性抓取，供 GABS 回读与日志取用。实机菜单路径解析不稳（`Actions/<分组>` 取不到），所以它只能算补充。
- `lightFileDump`（默认关）：每 2 s 把同一份 JSON 追加进 `persistentDataPath/FirstPerson3D_light.log`，与 `PerfMon3D` 的 perf 日志同一手法。昼夜与灯具变化按自然时间推进，看的是一串 dump 的趋势，不该靠一次性抓取。`scripts/light_check.py` 吃这些 dump。

## Acceptance Criteria

实机数据来自 `FirstPerson3D_light.log`（quick test 图 250×250、第一人称、自然推进时间），判据由 `scripts/light_check.py sweep` 跑，白天与夜间两组判据各 FAIL 0。

下面 AC-1..AC-3 是 **AmbGain 0.45 / MoonStrength 0.45 之后**那一轮（188 份 dump；正午档 glow=1.00、sunY=+0.29，深夜档 glow=0.00、sunY=-0.47；0..255 刻度）。改常数之前的旧数字只在括号里留作对照，别混着引。

- [x] AC-1: 正午室外有方向性。建筑按朝向分桶均值：东 167.6、北 163.1 对 西 52.0、南 56.3（向阳面约 3.1 倍），上 125.6 对 下 54.2。整串 188 份里 71 份算得出立面差：中位 137.7，正午档 115.6（旧一轮：北 198.3 / 南 107.4，1.8 倍）。关掉 `lightOn` 后各层不再写顶点色。
- [x] AC-2: 夜间断电处贴到下限。深夜地形朝上均值 41.0（= `LightFloor` 0.16×255）、建筑 42.0，同帧建筑 max 254 出在篝火那一格 → 灯具项生效且色相来自 `glowGrid`。**同一帧植被 64.2 反而高于地面 —— 见「已知未了」第 1 条。**
- [x] AC-3: 接触阴影与投影。正午地面朝上均值 154.6、最低 41（墙脚与阴影侧）、最高 214 —— 不再截成纯白（旧一轮：均值 248、一层 9.4 万顶点是纯白）。投影射线起点修正后「朝上面亮于朝下面」才成立 —— 修之前实测 up 58.5 < down 114.6，是机检抓出来的（射线从 0.9×墙高起算，低于墙顶，相邻墙把自己的墙顶挡成阴影）。
- [ ] AC-4: 帧时间。光场扫描实测 0.25 ms（夜）到 9.18 ms（正午整图换档那帧，`Prime`/整层重烘的尖峰），单次 chunk 重建 0.6~1.2 ms。整场帧时间仍缺一组 `lightOn` 开/关的 A/B：这一轮游戏主线程在 15:04 后停摆（另一个 mod 在主线程同步等 LLM HTTP，403 反复重试），GABP 与 PowerShell 全部超时，A/B 没做成。
- [ ] AC-5: 逐件对象受光。正午植被朝上均值 62.7 对地面 154.6 = 0.41，与离线台预测的 0.47（放置 0.374 / 朝上 0.804）同量级 —— 放置亮度走「漫反射平均入射」而不是朝上面，实机与模拟台对上了。绝对观感（植被该比向阳地面暗多少）仍要肉眼定，本条不算了结。
- [x] AC-6: 两套尺子都双向自证。`python scripts/light_check.py selftest` 33 条夹具全过（六条检测器 + 三组配对判据 + 四条读取层 + 五条立面取证 + 三条截图遮罩取证）；`dotnet run --project tools/LightSim` 21 条合成场景判据全过（管公式本体，条数不同，别混为一谈）。截图那条是这一轮补的：`shots` 曾把盖着 Debug log 窗口的两张图判成「昼夜差足够，通过」—— 量的是窗口不是场景。现在会认出面板、避开再量，避不开就拒绝下结论。
- [ ] AC-7: 灯具可见发光。发光片画出来了但观感不对：篝火那团在屏幕上占掉约四分之一并带一圈黑边，半径映射与混合要重定。这一轮没拿到干净截图（原因见 AC-4）。

## 已知未了

1. **夜里灯具照到了植被却没照到地面**：同一帧植被朝上均值 64.2、地面 41.0（正好贴下限）。两条通路都读 `LampAt`，差别在顶点色要等 chunk 重烘、实例乘子每帧现算，怀疑是重烘队列滞后（正午那档看不出来）。下一轮先做一件事：点灯后等 30 s 再 dump，地面仍贴下限就是真缺陷，不是采样口径。
2. 逐件对象与实例批的放置亮度偏低（AC-5）：要拿同一处「地面顶点色 vs 该处植被实例乘子」的对照才能定是 `AmbientColor` 权重还是 `FormShape` 乘子的问题。
3. 发光片尺寸与黑边（AC-7）。
4. 水体顶点色已被实测证实可用，但水体仍按材质色分档走，没接进光场的投影与 AO（这一轮 water 层顶点数 0，图里没有可见水体，等于没测到）。
5. 闪电不进烘焙通路，雷暴瞬时增亮还没有载体。

## Dependencies

无阻塞依赖。几何来自已落地的 F005（墙）与 F007（家具）。F008（屋顶网格）未落地，本项的屋顶项只读 `roofGrid` 数值，不画屋顶几何；F008 完成后无需改光照公式。

## Risk

`Map/Cutout` 乘顶点色 RGB 已由手持终端证实；`materials/map/plantwindsway` 与 `Map/Transparent` 未证实。故每层各带 `按顶点色` / `按材质色` 的通路开关，实机 AC-1 之前先用一组对照截图定层，不许拿注释当结论。

逐件对象用一个标量乘子，方向性靠原型网格的形体明暗，近距离转动物体时向阳面不跟着转。已知取舍，写在这里，不做逐顶点重烘。

## Open Questions

无。
