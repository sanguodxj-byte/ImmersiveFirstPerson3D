---
topics: [architecture, world, rendering, input]
doc_kind: note
created: 2026-10-10
---

# FirstPerson3D 架构

> 环世界 1.6 模组 `sanguo.firstperson3d`。在原版模拟之上替换相机与绘制，把殖民地看成以格为米的三维空间。
> 高度常数只来自 `docs/HeightConvention.md` / `Metrics3D.cs`。家具取样见 `docs/furniture/ThreeViewMethod.md`。
> 进度与差距见本文第 2 节。未完成工作见仓库根目录 `BACKLOG.md`。

## 1. 终态

玩家以第一人称在殖民地里自由走动（鼠标环视，WASD 相对朝向，跳跃与贴墙滑移，手感接近 Minecraft），看见的墙、家具、山体和大型地形都是用原版贴图当材质的三维网格。楼层沿 Y 轴叠放，走楼梯就能上下。走出地图边缘时邻接地图按需加载、离开后卸载。原版上帝视角保留在已有的手持终端屏幕上，主画面不离开第一人称。

模拟仍是环世界：需求、工作、战斗结算、存档不另起一套。本模组替换的是「怎么看」和「人怎么走、怎么瞄准」。

## 2. 进度

版本号 0.2.0（`About.xml` 与 `Fps3DState.Version` 一致）。下表按代码实际行为。

| 终态块 | 现在 | 缺口 |
|---|---|---|
| 第一人称画面 | F8 在 关闭 / 第一人称 / 第三人称追尾 间循环。主按钮只切第一人称开关。透视相机、眼高 1.62 m、FOV 72°、鼠标锁定环视、准星、血条、E 背包都在。空手右下角肤色像素手，持械换成武器图标视图模型，左键挥击有动画；左键攻击取原版 verb，E 键交互（门 / 拾取与装备 / 挖掘 / 敌对） | 身体仍是立牌。室内没有天花板，抬头看见天空 |
| MC 式操作 | WASD 按偏航积分水平位移，撞墙退单轴滑移；空格跳跃、重力下落（`Metrics3D.Gravity` / `JumpApex`）。位移期间 `pather.StopDead`，跨格写回 `pawn.Position`；远控 `cmdWalkHold` / `cmdKey` 单步保留；挖掘 / 搬运 / 施法期间让原版作业驱动 | 跳跃只算平地 1 m，不探测家具网格。待实机验收 |
| 墙 | 建造墙走取样图集（决策 `docs/decisions/0003-wall-atlas.md`）：顶面贴墙帽段、可见立面贴墙身段（`WallAtlas`，Point 过滤、按外观 + 染色缓存材质），逐格面剔除，高 2.70 m。名单门切门洞（宽 `DoorWidth`、高 `DoorHeight`），门楣补到墙高，开门不发门扇；图集缺失退回连接子格 | 天然岩与模组墙仍走连接子格子盒（岩归 F006）。意识形态墙风格不取样。待实机验收 |
| 家具 | 床、凳、床头柜、柜、架、扶手椅六类走三视图基本体，材质来自 `FurnitureAtlas.png` | 桌、餐椅、灯、工作台、生产建筑等仍是立板 + 地面贴片 |
| 山体 / 大型地形 | 天然岩也是满格建筑，因此同样挤成 2.70 m 的盒子。地形是平铺四边形。天空远山是装饰视差，不读地图岩体 | 山没有高度和坡。地形没有起伏。屋顶网格不存在 |
| 残骸 | 7 个样板（坦克、飞船碎片、战争步行者壳、引擎、巨轮、混凝土障、锈卡车）已接到绘制 | 其余野生建筑仍走立板 |
| 植物 / 人物 / 动物 / 水 | 树是三片交叉面，小植物朝向相机。人物复用原版渲染树立牌。动物有程序化原型，可退回立牌。水有深度，草在相机周围 | 人物与多数小物件仍是面片 |
| 光照 | F013 两条载体：合批几何烘顶点色（地形/墙体/水体/实例批/原型形体明暗），逐件对象走实例材质乘子。逐格光场取原版 `glowGrid` 彩色辉光 + `roofGrid` 天空遮蔽 + 体素投影与接触阴影；太阳方向与天空亮度与 `SkyScenery` 同一份数 | 实机实测：正午向阳立面约为背阳面 1.8 倍、夜间贴到亮度下限、篝火格亮且带颜色。未了：逐件对象放置亮度偏低、发光片尺寸与黑边、水体没接投影、闪电不进烘焙 |
| Y 轴多层 | 同栈多张 `Map` 按层高 3.00 m 上下叠放，一起绘制。PageUp / PageDown 切换 `CurrentMap`。调试动作可生成上层口袋图。栈关系随存档 | 切层会丢掉当前附身者，在新图上另选一个 pawn。不能沿楼梯走到楼上 |
| 流式加载 | 调试动作向东生成一张 100×100 邻图，世界偏移接在主图东边。走到边缘会 `DeSpawn`/`Spawn` 传送。可卸载、可存档 | 只有东向、同时只挂一张邻图、加载是手动的。传送会在落点附近找空地，避免相机卡进墙 |
| 视角切换 | M 键抬起 `1.6/Assets/hand_terminal.obj`；`Viewing` 期间原版俯视由 `GodView3D` 当帧捕获进屏幕 RT，主画面留在第一人称。屏幕可拖拽平移、轻点选人附身；原版顶栏 / 底栏由 `CommandPanel3D` 合成进屏幕纹理 | 实机验收未跑（AC 未勾）。原版窗口 UI 不整体入屏 |
| 性能 | 地形与建筑按 16×16 chunk、每帧最多重建 3 块；建筑 chunk 记局部包围盒做视锥剔除（`Frustum3D`），家具 / 残骸逐件再补点剔除。F7 性能面板。单图异常不拖垮其他图。植物分近远场，草有半径 | 每个脏建筑 chunk 都扫描全图 `AllThings`。家具、残骸、动物逐件 `DrawMesh`。无远距 LOD。多图同时全量绘制 |

已能在游戏里走通的闭环：进入第一人称、附身殖民者、环视、走到墙边、看见体素墙和六类家具、打开终端、调试生成上层或东邻图并走过去。连续移动、墙图集与终端屏幕俯视是本轮新落地的路径，待实机复核。

## 3. 运行时

`ModEntry` 在启动时打 Harmony，并创建不销毁的 `FirstPerson3D_Root`。

每帧 `Fps3DRoot.Update`：

1. 非对局或无地图：退出三维，还原相机。
2. 处理按键与 `settings.cmdAction`（GABS 远程通道和调试菜单共用）。
3. `MapScreen3D.Tick` 推进终端抬起 / 放下。
4. 终端处于 `Viewing`：主相机继续画第一人称，原版俯视由 `GodView3D` 捕获进屏幕 RT，环视与准星点击让位。
5. 否则压制原版绘制与地图相机，驱动 `CameraRig3D`。
6. 附身者走到图边时尝试 `Streaming3D.TryCrossBoundary`。
7. `Streaming3D.RenderMaps` 给出要画的地图：当前图、同一垂直栈、已登记世界偏移的邻图。每张图一个 `Scene3D`，异常按图吞掉。
8. `OnGUI` 画性能面板、准星、名牌、手、血条与背包。终端动画期间手和 HUD 让位。

原版这些绘制在三维模式且终端未全屏打开时被前缀跳过：`MapDrawer.DrawMapMesh`、`DynamicDrawManager.DrawDynamicThings`、`DrawBatch.Flush`、重力船地形帘、天气绘制、`CameraDriver` 更新、接管期内的 `UIRoot_Play.UIRootOnGUI`、`ThingOverlays`。模拟（tick、寻路、需求）继续跑。

## 4. 世界模型

环世界一张 `Map` 没有可玩的高度轴。本模组不把高度塞进同一张图的格子里，而是两套拼接：

```
世界坐标 = 图内格子坐标 + 水平偏移 (Streaming3D) + (0, 层号 × 3.00, 0)
层号     = 该图在 LevelStack3D 栈内的顺序
1 格     = 1 米，三轴同尺度
```

| 轴 | 机制 | 代码 |
|---|---|---|
| Y 楼层 | 多张 Map 注册进同一个栈。未注册时，同一世界格上的地图按 `uniqueID` 排序临时成组。口袋图 Tile 无效，只算自己，避免每帧越界查询 | `LevelStack3D` |
| XZ 邻图 | 每张图一个世界偏移。主图为原点。渲染集合是垂直栈与已登记邻图的并集 | `Streaming3D` |
| 存档 | `WorldLinkComponent` 只存地图 `uniqueID`、栈号、偏移。地图本体仍由原版存。读档先暂存，等 `Find.Maps` 就绪再解析 | `WorldLinks` |

上层图必须带有效世界格。`CreateUpperMap` 用 `PocketMapParent`，`Tile` 抄底层图，并放进 `Find.World.pocketMaps`，否则原版每帧读日期和温度会报 invalid tile -1。

流式邻图用 `FP3D_StreamNode`（纯 `MapParent`）。营地类世界对象会因无人而被原版拆掉，所以不用。

新流式图延迟 0.6 s 再造场景。生成当帧的物件列表还不完整，立刻烘网格会留下永久黑块。

## 5. 场景与绘制

`SceneRegistry` 按 `Map` 持有 `Scene3D`。一张场景的层：

| 层 | 几何 | 更新 |
|---|---|---|
| `TerrainLayer3D` | 按材质合并的地面四边形。满格建筑下的格不画 | 16×16 chunk，脏重建，预算 3 |
| `WaterLayer3D` | 有深度的水体 | 可关 |
| `GrassLayer3D` | 相机周围地被。有屋顶的格跳过 | 半径默认 28 格 |
| `BuildingLayer3D` | 满格体素盒、门板、蒸汽喷泉地面片。家具与残骸在这里短路 | 同上 chunk。家具和残骸在 `Draw` 里逐件画 |
| `PlantLayer3D` | 树：三片 60° 交叉面。小株：广告牌。近场摆动 | 生长轮询 |
| `ItemLayer3D` | 物品广告牌，尸体平铺 | 脏列表 |
| `PawnLayer3D` | 原版 `renderTree` 的绘制请求立起来。躺睡抬到 `PawnLyingSurface`（0.63 m） | 每帧 |
| `AnimalLayer3D` | `Animal3D` 基本体，或退回立牌 | 每帧 |
| `SkyScenery` | 全局，不按图。穹顶、太阳、云、星、视差远山、地裙 | 锚在相机与渲染集包围盒 |
| `LightField3D` | 按图。逐格天空遮蔽 / 灯具彩色 / 实心高度场 / 向阳投影，供上面各层烘顶点色 | 每帧推进一条扫描带（默认 6 行），变化格标脏 chunk |
| `GlowLayer3D` | 按图。灯具发光：贴地光池 + 灯头星片，尺寸与颜色取 `CompGlower` | 0.5 s 重扫发光建筑；随光照开关 |

建筑分派顺序（`BuildingLayer3D.BakeBuilding`）：

1. 门：厚 `DoorPanelThickness`、高 `DoorHeight` 2.05 m 的盒子。名单门（`Door`、`Autodoor`、`OrnateDoor`、`SecurityDoor`、`VacBarrier`）在图集可用时另发门楣盒（`BakeDoorHeader`），开门时不发门扇；名单外的门保持原样。
2. `Fillage.Full` 分两路：建造墙（`ThingDefOf.Wall`）在图集可用时走 `BakeWall`——顶面贴墙帽段、可见立面贴墙身段；天然岩与其余满格、以及图集缺失时的建造墙，仍走连接子格子盒，高度一律 `WallHeight` 2.70 m。
3. 蒸汽喷泉：地面片。立起来会变成黑幕。
4. `Furniture3D.Handles`：运行时不进 chunk。
5. `Wreckage3D.Handles`：同上。
6. 其余：沿朝向的双面立板 + 地面贴片。

描边不用自定义着色器（本机没有 Unity Editor，着色器进不了 AssetBundle）。轮廓靠反绕外扩壳。`Model3D` 用盒子、圆柱、圆锥、楔形、球拼装，并给每个部件一套 UV。

### 光照（F013）

原版着色器不接 `Light`，所以光照只有两条乘子通路（`docs/decisions/0004-vertex-color-lighting.md`）：

- **顶点色 RGB**：位置已知的合批几何。`TerrainLayer3D`、`BuildingLayer3D.MeshBuilderX`、`Water3D.QuadBuilder`、`Util3D.BuildInstancedMesh`（植物/草/物品实例批）、`ModelBuilder`（家具与残骸原型的形体明暗）。
- **实例材质乘子**：共享原型网格的逐件对象（家具、残骸、动物、人物立牌）。克隆材质 `color = 原色 × 该处光场`，按量化亮度档缓存。

面法线由发射点按本项目绕序自己算（`Cross(v1-v0, v2-v0)`），不交给 `RecalculateNormals`：向阳项吃法线符号，符号必须自己声明。地形顶点的 `colors.a` 仍是软边遮罩，光照只写 RGB。

每图一份 `LightField3D`：逐格缓存天空遮蔽（`roofGrid`）、灯具彩色（`map.glowGrid.VisualGlowAt`）、实心高度场（满格建筑与屋顶，墙 2.70 / 岩 2.92 / 层高 3.00）、向阳投影（两条高度带各扫一次射线，四邻双线性出软边）。每帧只推进一条扫描带，值真变了才标脏对应 chunk，重建仍吃 `Fps3DState.ChunkRebuildBudget`。合成式、常数与默认值写在 `docs/features/F013-lighting.md`，真源在 `Lighting3D.cs`；可调项在 `FirstPerson3DSettings`，验收开关在 `DebugActions3D`（`dump lighting` / `sky: noon|dusk|night` / `lighting: lamp on|off` 等）。

合成式本体在 `LightShade.cs`：纯函数，只吃 `ILightField` + `LightEnv` + `LightTuning`，不碰 Map / Material / Quaternion（那些在 Unity 播放器外跑不动）。游戏里 `LightField3D` 实现该接口，离线台 `tools/LightSim` 用一座手写合成场景（墙 / 厚屋顶房 / 房内灯笼 / 手工投影 / 角接触阴影）实现同一接口，csproj 直接链接同一份 `LightShade.cs` —— **不是复制公式**，所以台子过等于游戏里过。进游戏前先跑 `dotnet run --project tools/LightSim`（21 条判据，每条带反向对照，<2 s），全绿才允许开一轮实机：AmbGain（正午天光+直射截断）与 MoonStrength（月光方向被亮度下限吃掉）两条缺陷都是在台子里 2 秒抓到的，实机各烧了一轮才看出症状。

灯具本身要看得见：`GlowLayer3D` 按 `CompGlower` 的 `GlowRadius`/`GlowColor` 画贴地光池与灯头星片，未通电不画；带发光件的逐件对象亮度下限抬到 `EmissiveFloor`。

闪电 `Env.flash` 不进烘焙：顶点色是按档重烘的量，容不下每帧翻转的瞬时项。

## 6. 输入与附身

`CameraRig3D` 复用场景里的原版相机物体，改成透视。进入第一人称即锁定光标并附身：优先当前选中的殖民者，否则任意殖民者。附身期间强制征召，退出时恢复原来的征召状态。

`Fps3DState.Active` 只在 `ProgramState.Playing` 下为真；标题界面与世界图每帧走的 `Leave3D(forceOff)` 只清会话状态（`ResetInMemory`），不再改写玩家持久化的 `mode` —— 以前那行会把「下次仍以第一人称开局」抹成 Off。

接管期（`VanillaInputTakenOver`）跳过原版 UI。让位条件：Alt、失焦、原版弹窗、暂停、没有附身对象。终端查看期间光标交给屏幕，原版 UI 仍然跳过。空格暂停由模组补上，因为原版快捷键在接管期进不来。

准星左键攻击：命中敌对 pawn 且在 verb 射程内才 `TryStartCastOn`，同时触发视图模型挥击动画。E 键交互：选中命中物并执行门开关、拾取 / 装备、挖掘或对敌攻击（`CameraRig3D.Interact` / `ApplyInteraction`）。移动见第 2 节。

持械视图模型（`Viewmodel3D.DrawWeapon`）按「枪口钉在准星」摆：准星就是屏幕中心，也是 `CameraRig3D.RaycastCenter` 那条视线与透视灭点，所以枪口落在准星上等于枪管轴指向画面深处；枪身沿右下 30° 斜出（`BarrelTiltDeg`），整件仍在屏内。原版武器图标是枪口朝右的侧视 sprite，纯旋转会把「枪顶朝上」翻成弹匣朝天，因此线性部分取**反射**（行列式 -1，即水平镜像后再对齐轴）。枪口位置与枪管轴不写死，按图标 alpha 扫：不透明外框最右列的实像素纵向中心是枪口，轴取「外框中心 → 枪口」，所以图标里枪管斜着画（突击步枪轴自带 +0.35 下沉）也摆得正。这套摆放由 `shots/smh_ref/compose_weapon_preview.py` 用真实图标离线复刻同一变换检查（枪口落点 ±1 px、枪尾在右下、镜像生效、不出画，各带反向对照），改 C# 的摆放参数必须同步那个脚本，否则预览证明的是另一套数学。

武器射击效果（F015）补的是「枪响了却什么都看不见」：第一人称压掉原版绘制后弹丸本来就不存在，所以先画弹丸本体（原版贴图 + `Projectile.ExactPosition`，抛物线弹的覆写一并白拿），再沿弹道拉一条相机朝向的加法缎带，开火处前移 0.32 m 点一团火光，`Impact` 处开一朵边淡边长的烟尘；附身者自己开火则在视图模型的枪口锚点上亮一下 —— 那个锚点是 F013 变换算出来的，不另算位置就必然对得齐。加法混合要用**预乘**光斑（`ProcTex3D.AdditiveRadial`，灯具发光片也改吃这一份），白 RGB 配 alpha 的那种在加法下会糊成一块亮方片。

效果演示走「C# 出数、python 出图」的分工：`dotnet run --project tools/LightSim -- --demo` 用游戏里同一份 `HitFxMath` / `ShotFxMath` / `LightShade` 把 1.5 秒的射击与受击按 20 fps 算成帧表（`temp_light/fx_demo.json`），`scripts/shotfx_demo.py` 只做投影与合成，出胶片与 GIF。脚本里不许再算一遍衰减，暗角的带宽与浓度上限也是从 `HitFxMath.cs` 正则读出来的 —— 否则演示证明的是脚本而不是模组。这条通道当场否掉了 0.85 的暗角浓度（屏幕三边全红、准星糊住），并暴露出远景目标只有几个像素、红闪读不出来，于是渲染加了 2 倍放大的取景窗。

受击反馈（F014）照 Minecraft 的两条做：任意 pawn 受伤后 0.5 s 内把**同一份几何再叠一层红**（`HitFx3D.DrawOverlay`，同 mesh 同 matrix 只换 `Transparent` 材质），附身玩家则压屏幕四边的红色暗角（`HitFx3D.DrawVignette`，画在准星之前，所以再红也挡不住准星），残血另有一层常驻薄红边，两路**取大不相加**。触发只认 `Pawn.PostApplyDamage` 的 Harmony 后置，时刻取 `Time.realtimeSinceStartup`（墙钟，暂停也照样退完）。曲线在 `HitFxMath.cs`（纯函数，`tools/LightSim` 链接它跑 22 条判据），暗角贴图场与红闪合成由 `scripts/hitfx_check.py` 离屏出图核对。为什么用叠层而不是乘子：乘子只能把颜色压暗，深色护甲乘上红色只会变黑，而 MC 的受击闪是"发亮的红"。

设置真源是 `FirstPerson3DSettings`，经 RimBridge 读写，供 GABS 调参和读性能快照。合成输入（`cmdLookDx`、`cmdKey`、`cmdWalkHold`、`cmdClick`、`cmdTeleport`、`cmdAction`）消费后清零，不进存档。

## 7. 手持终端与上帝视角

资产：`1.6/Assets/hand_terminal.obj`，由 `ObjImporter3D` 读入。屏幕面不在 OBJ 里，`HandTerminal3D` 用程序四边形。抬起过程贴自绘 UI；升到位后贴 `GodView3D` 的 `RenderTexture`。

`MapScreen3D` 相位：`Closed → Rising → Viewing → Lowering`。全程不把主相机交还原版，不闪白，开关终端都不 `MarkAllDirty`。`Open` 恒为 false，`Fps3DRoot` 不会因此 `Leave3D`。OBJ 没载入时 `HandTerminal3D.Ready == false`，`Show` 直接返回。

`Viewing` 期间 `GodView3D.Capture` 只调用当前地图的 `DrawMapMesh` 和 `DrawDynamicThings`。捕获窗里的 `Graphics.DrawMesh` / `DrawMeshInstanced` 被前缀改到 layer 31，带相机参数的重载改投到一台停用的正交相机。该相机 `cullingMask` 只有 layer 31，画面写进屏幕 RT，靠 `Camera.Render()` 当帧画完。主相机每帧去掉 layer 31。没有相机参数的两个短重载在捕获窗内改调带相机的重载。`CameraDriver.CurrentViewRect` 在捕获窗内改成这台相机的焦点和 `RootSize`，不挪主相机。俯视倾斜在第一次 `ApplyCameraSuppression`、第一人称欧拉写上之前记下来。

屏幕上拖拽平移焦点，轻点选中该格的 pawn，放下时写入 `possessName`，由已有的 `TryPossessByName` 附身。查看期间 `HandleLook` 和 `HandleClick` 让位。天气、fleck、指示层不进 RT。

顶栏与底栏由 `CommandPanel3D` 在查看期记账原版 UI 的绘制，下一帧合成到屏幕纹理（文字图集的字形单独横向翻转）；指针落在屏幕边框带时交给原版 UI。

决策：`docs/decisions/0002-godview-on-terminal.md`。

## 8. 贴图到网格

两套已经在用的方法，加一条回退：

| 对象 | 方法 | 材质 |
|---|---|---|
| 建造墙、门楣 | `BakeWall` / `BakeDoorHeader` 取样图集 | `1.6/Textures/FPS3D/WallAtlas.png`（墙帽 / 立面两段，`FilterMode.Point`）乘墙自身 `MatAt` 染色。`WallUVGen.cs` 由 `scripts/wall_atlas.py` 生成，不手改 |
| 门、满格岩 | 运行时体素盒 | 原版 `MatAt` 连接图集，UV 0..1。岩体不进墙图集 |
| 家具六类 | 三视图分区取样 + `Model3D` 基本体。规格在 `scripts/furn_atlas.py`，校验 `scripts/furn_uv_check.py` | `1.6/Textures/FPS3D/FurnitureAtlas.png`。`FurnitureUVGen.cs` 生成，不手改 |
| 残骸七类 | 同上，样板在 `WreckageSamples.cs` | `WreckageAtlas.png`。`WreckageUVGen.cs` 生成，不手改 |
| 其余建筑 | 立板 | 原版整张贴图 |

`docs/TextureTo3D_Pipeline.md` 是早期设计稿。其中「按俯角公式把一张图拆成顶面和四壁」没有成为家具和残骸的实现。新物件沿三视图规范或体素盒，不沿那份公式。

尺寸纪律：`Metrics3D` 是唯一米制来源。Def 的 `size` / `drawSize` / `fillage` 只用于占格、朝向、碰撞和「满格与否」，不用于反推米数。

## 9. 性能路径

现在的预算只盖住「单图、格子几何」：chunk 合并、每帧重建上限、建筑 chunk 视锥剔除、植物近远场、草半径、按图隔离异常。

变贵的来源已经在代码里：

- 建筑脏重建对每个 chunk 扫描 `listerThings.AllThings`。
- 家具、残骸、动物、人物每件一次绘制调用。
- `RenderMaps` 对栈内和邻图全量 `Update` + `Draw`，远裁剪 140 m，图与图之间没有剔除。
- 流式和多层一旦从调试按钮变成随走动加载，绘制集合会成倍增加。

验收数字走已有的 `PerfMon3D`（FPS、平均毫秒、p95、GC）。门槛写在 `docs/features/F012-perf-budget.md`，不在业务代码里另造日志格式。

## 10. 模块索引

| 文件 | 职责 |
|---|---|
| `ModEntry.cs` | Harmony、常驻根物体 |
| `Fps3DRoot.cs` | 帧循环、按键、HUD |
| `Fps3DState.cs` | 模式与绘制开关 |
| `ModSettings3D.cs` | 远程设置 |
| `VanillaSuppressor.cs` | 原版绘制与相机的压制 / 还原 |
| `CameraRig3D.cs` | 透视相机、附身、环视、连续移动、准星攻击与交互 |
| `MouseLookDelta.cs` / `Crosshair3D.cs` / `NameLabels3D.cs` | 鼠标增量、准星、名牌 |
| `Viewmodel3D.cs` / `Hand3D.cs` | 手部与武器视图模型、挥击动画 |
| `FpsHud3D.cs` | 血条与背包 HUD |
| `Util3D.cs` / `Frustum3D.cs` | 材质与网格工具、视锥剔除 |
| `Scene3D.cs` / `ChangeNotify.cs` | 单图场景聚合；地形与物件变更转发 |
| `LevelStack3D.cs` | 垂直栈 |
| `Streaming3D.cs` | 水平偏移、跨边传送、东向调试加载 |
| `WorldLinks.cs` / `WorldLinkPersistence.cs` | 栈与偏移的存档 |
| `Metrics3D.cs` | 米制常数 |
| `Model3D.cs` | 基本体与描边壳 |
| `BuildingLayer3D.cs` | 墙、门、回退立板 |
| `Furniture3D.cs` / `Wreckage3D.cs` | 目录内的真实网格 |
| `FurnitureUVGen.cs` / `WallUVGen.cs` / `WreckageUVGen.cs` / `WreckageSamples.cs` | 生成的 UV 表与残骸样板，不手改 |
| `HandTerminal3D.cs` / `MapScreen3D.cs` / `ObjImporter3D.cs` | 终端与视角切换 |
| `GodView3D.cs` / `CommandPanel3D.cs` | 终端屏幕的俯视捕获与原版顶 / 底栏合成 |
| `SkyScenery.cs` `Water3D.cs` `Grass3D.cs` `PlantLayer3D.cs` `PawnLayer3D.cs` `Animal3D.cs` `ItemLayer3D.cs` `TerrainLayer3D.cs` | 环境与生物 |
| `Lighting3D.cs` / `LightShade.cs` / `LightField3D.cs` / `GlowLayer3D.cs` | 光照：状态与当帧快照、纯函数合成式（离线台与游戏共用）、按图逐格光场、灯具可见发光（F013） |
| `HitFx3D.cs` / `HitFxMath.cs` | 受击反馈：受伤触发与红闪叠层 + 玩家暗角（F014）；曲线是纯函数，离线台链接同一份跑判据 |
| `ShotFx3D.cs` / `ShotFxMath.cs` | 射击效果：弹丸本体与拖焰、枪口火光、落点烟尘（F015）；触发只认 `Projectile.Launch` / `Impact` |
| `tools/LightSim/` | 光照离线模拟场景台：合成场景实现 `ILightField`，链接 `LightShade.cs` 跑 21 条双向判据。`dotnet run --project tools/LightSim`，非 0 即未通过 —— 实机一轮的前置闸门 |
| `PerfMon3D.cs` | 性能读数 |
| `DebugActions3D.cs` / `MainButtonFirstPerson3D.cs` | DevMode / GABS 动作、第一人称主按钮 |

构建：`Source/RimFirstPerson3D/RimFirstPerson3D.csproj` 输出到 `1.6/Assemblies/`。部署：`scripts/deploy.ps1`（游戏进程会锁 DLL，须先关游戏）。
