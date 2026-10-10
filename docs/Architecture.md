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

建筑分派顺序（`BuildingLayer3D.BakeBuilding`）：

1. 门：厚 `DoorPanelThickness`、高 `DoorHeight` 2.05 m 的盒子。名单门（`Door`、`Autodoor`、`OrnateDoor`、`SecurityDoor`、`VacBarrier`）在图集可用时另发门楣盒（`BakeDoorHeader`），开门时不发门扇；名单外的门保持原样。
2. `Fillage.Full` 分两路：建造墙（`ThingDefOf.Wall`）在图集可用时走 `BakeWall`——顶面贴墙帽段、可见立面贴墙身段；天然岩与其余满格、以及图集缺失时的建造墙，仍走连接子格子盒，高度一律 `WallHeight` 2.70 m。
3. 蒸汽喷泉：地面片。立起来会变成黑幕。
4. `Furniture3D.Handles`：运行时不进 chunk。
5. `Wreckage3D.Handles`：同上。
6. 其余：沿朝向的双面立板 + 地面贴片。

描边不用自定义着色器（本机没有 Unity Editor，着色器进不了 AssetBundle）。体积靠顶点色近似光照，轮廓靠反绕外扩壳。`Model3D` 用盒子、圆柱、圆锥、楔形、球拼装，并给每个部件一套 UV。

## 6. 输入与附身

`CameraRig3D` 复用场景里的原版相机物体，改成透视。进入第一人称即锁定光标并附身：优先当前选中的殖民者，否则任意殖民者。附身期间强制征召，退出时恢复原来的征召状态。

接管期（`VanillaInputTakenOver`）跳过原版 UI。让位条件：Alt、失焦、原版弹窗、暂停、没有附身对象。终端查看期间光标交给屏幕，原版 UI 仍然跳过。空格暂停由模组补上，因为原版快捷键在接管期进不来。

准星左键攻击：命中敌对 pawn 且在 verb 射程内才 `TryStartCastOn`，同时触发视图模型挥击动画。E 键交互：选中命中物并执行门开关、拾取 / 装备、挖掘或对敌攻击（`CameraRig3D.Interact` / `ApplyInteraction`）。移动见第 2 节。

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
| `PerfMon3D.cs` | 性能读数 |
| `DebugActions3D.cs` / `MainButtonFirstPerson3D.cs` | DevMode / GABS 动作、第一人称主按钮 |

构建：`Source/RimFirstPerson3D/RimFirstPerson3D.csproj` 输出到 `1.6/Assemblies/`。部署：`scripts/deploy.ps1`（游戏进程会锁 DLL，须先关游戏）。
