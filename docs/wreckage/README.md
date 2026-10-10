# 残骸样板重构（地图野生建筑）— 交付说明（盲测通过版 + 材质验收）

> 任务：通过原版（带俯角斜视）三视图反推几何 + 材质完全取样原版贴图作 UV 重构。
> 本 Agent 负责「地图野生建筑部分（各种残骸）」。
> 硬约束：① 侧面锁死；② 原版贴图只当素材、组合成新图集供 UV 粘贴（后续不用原版贴图）；
> ③ 不复刻原版黑色描边（`ModelBuilder.EmitHull` 的 Mod 赛璐璐描边保留，属另一回事）。
> 尺度：1 格 = 1.00 m；**128 px == 1 格**；高度口径以 `Metrics3D` 为准。

---

## 1. 交付清单

| 文件 | 说明 |
|------|------|
| `Source/RimFirstPerson3D/WreckageSamples.cs` | **核心交付**：7 个 `Model3D` 样板 + `Builders/Build()/Uv()` 统一入口 |
| `Source/RimFirstPerson3D/WreckageUVGen.cs` | 图集 UV 表（**生成物，勿手改**），9 键 |
| `1.6/Textures/FPS3D/WreckageAtlas.png` | 新图集（1024×1504 RGBA，**生成物**） |
| `docs/wreckage/samples/*.png` + `_all.png` | 7 样板三视图（45°/正视/俯视）预览对照表（**平面着色，带标签**） |
| `docs/wreckage/clean/*.png` + `clean_report.json` | 已程序去黑边的源贴图（图集素材） |
| `viewer/wreck.html` | **带纹理 3D 验收页**（three.js，读 `wreck_uv.json` + `wreck_atlas.png`） |
| `viewer/shots/*.png` + `_overview_7.png` | 7 样板**带纹理**五视角截图 + 总览图 |
| `scripts/wreck_atlas.py` | clean_report → 图集 + 生成 `WreckageUVGen.cs`（可重跑） |
| `scripts/wreck_render.py` | 解析 `WreckageSamples.cs` → 平面着色预览（可重跑） |
| `scripts/wreck_viewer_json.py` | C# → `viewer/wreck_uv.json`（可重跑） |
| `scripts/wreck_shots.py` | 8899 静态服务 + 无头 Chrome 批量截图（可重跑） |
| `scripts/wreck_overview.py` | 拼 7 样板带纹理总览图（可重跑） |
| `scripts/wreck_blind.py` | 无标签盲测三视图渲染器（R1–R11 门禁用） |

## 2. 7 个样板清单

| 样板 | 源贴图 | 足迹(格) | 高度(格) | UV 键 |
|------|--------|---------|---------|-------|
| RuinedTank | ruinedtank | 2.50 × 1.50 | 0.94 | `ruinedtank` |
| CrashedShipPart | crashedshippart | 6.00 × 4.00 | 2.40 | `crashedshippart` |
| AncientWarwalkerShell | ancientwarwalkershell | 2.50 × 1.50 | 2.10 | `ancientwarwalkershell` |
| LargeEngineBlock | largeengineblock | 1.30 × 0.50 | 0.72 | `largeengineblock` |
| GiantWheelA | giantwheela | 1.00 × 1.00 | 1.00 | `giantwheela` |
| ConcreteBarrier | concretebarrier | 1.00 × 0.44 | 0.78 | `concretebarrier` |
| RustedIndustrialTruck | rustedindustrialtruck_{south,east,north} | 4.00 × 4.00 | 1.48 | `rustedindustrialtruck.{south,east,north}` |

> 足迹/高度取自 `WreckageSamples.cs` 各方法 `m.bounds = (X, Y, Z)`（XZ 为足迹、Y 为高度）；
> **高度为估设**（俯视贴图无法直接给出高度），属渲染迭代项。
> 注：`GiantWheelA`/`ConcreteBarrier` 现为平放/护栏形态，bounds 已在代码中同步。

## 3. UV 取样区说明（键 → 图集块 → UV）

图集 `WreckageAtlas.png` 由 `clean/*.png` 按 `clean_report.json` 的 `bbox_after` 紧贴剪影裁切后
shelf-pack（W=1024，GAP=2，边缘 clamp 复制防双线性/mip 串色）而成。图集块位置（像素，y 自顶向下）：

| 键 | 块尺寸(px) | 块左上角(x,y) | 图集 UV (u0,v0,u1,v1) |
|----|-----------|--------------|----------------------|
| crashedshippart | 996×472 | (0, 0) | 0.00000, 0.68617, 0.97266, 1.00000 |
| rustedindustrialtruck.north | 406×431 | (0, 474) | 0.00000, 0.39827, 0.39648, 0.68484 |
| rustedindustrialtruck.east | 427×427 | (408, 474) | 0.39844, 0.40093, 0.81543, 0.68484 |
| rustedindustrialtruck.south | 419×416 | (0, 907) | 0.00000, 0.12035, 0.40918, 0.39694 |
| ruinedtank | 305×180 | (421, 907) | 0.41113, 0.27726, 0.70898, 0.39694 |
| ancientwarwalkershell | 308×179 | (0, 1325) | 0.00000, 0.00000, 0.30078, 0.11902 |
| giantwheela | 106×116 | (310, 1325) | 0.30273, 0.04189, 0.40625, 0.11902 |
| largeengineblock | 118×58 | (418, 1325) | 0.40820, 0.08045, 0.52344, 0.11902 |
| concretebarrier | 58×58 | (538, 1325) | 0.52539, 0.08045, 0.58203, 0.11902 |

- **v 口径**：Unity 纹理 v=0 在图像**底边**，故 `v = 1 - y/H`（`WreckageUVGen.cs` 已按此生成）。
- 顶面 UV = 该键的 `R` 整块；侧面 UV = 顶面 v 区间内竖直插值近似条带（**未经美术验证，待迭代**）。
- `SrcSize`（源像素宽高）用于侧面平铺周期；宽度/平铺周期即 1 格 = 128 px 换算。

## 4. 图集口径说明（重要）

本版**选定「组装新图集 WreckageAtlas.png + 生成 WreckageUVGen.cs」**（沿用 `FurnitureUVGen` 范式），
而非逐图直接引用去黑边贴图。理由：满足「组合成新图集供 UV 粘贴、后续禁用原版贴图」的要求，
且与项目既有图集管线一致。**若希望改为逐图引用，请告知，会切换实现。**

## 5. 已知存疑 / 待迭代项

1. **高度**：全部为估设，待渲染迭代 + 评审确认。
2. **缩放**：`concretebarrier`(64px)、`giantwheela`(128px) 依「128px=1 格」得 0.5~1 格；
   若原版实际占用不同，可能需逐项缩放因子（**未擅用 def 反推**，待评审）。
3. **侧面 UV**：approximation 条带，观感待美术确认（带纹理截图已可复现此现象）。
4. **几何精度**：样板几何来自斜视三视图反推，非逐像素还原；`WreckageSamples.cs` 头部已注明
   「坐标为模型局部空间、XZ 居中、Y 底面为 0、单位米」。
5. **图集 alpha**：在 Cutout 着色器下的边缘表现待游戏内确认。
6. **运行时接入**：尚未把样板接进 `BuildingLayer3D`，待本版评审通过后再做。

## 6. 材质 / 带纹理验收（本轮新增）

把取样自原版的贴图按 UV 贴到几何上、再从目标视角渲染，用于验收「材质是否确取自原版」：

- **渲染器**：`viewer/wreck.html`（three.js）。加载 `wreck_atlas.png`，按 `viewer/wreck_uv.json`
  逐面贴图（`MeshLambertMaterial`），UV 口径已翻正 `v' = 1 - v`；支持 `?item=&view=` 指定样板/视角。
- **截图**：`scripts/wreck_shots.py` 经本机 8899 静态服务 + 无头 Chrome，导出
  7 样板 × 5 视角（45° / 俯视 / 主视·南 / 侧视·东 / 低角度）→ `viewer/shots/<item>__v<0..4>.png`，
  并拼每样板对照条 `<item>__sheet.png`；`scripts/wreck_overview.py` 拼总览 `viewer/shots/_overview_7.png`。
- **判读**（`tools/look.py` 视觉读图，逐样板抽样）：
  - 坦克 / 飞船 / 步行机 / 发动机 / 路障 / 卡车：**贴图正常**，无明显错位、串色；纹样与轮廓贴合。
  - **巨轮**：侧面/顶面接缝处可见明显拉伸与错位观感 —— 即 §5.3 的 side UV 近似条带问题
    （平放圆盘的顶面圆贴图正常，问题集中在侧面条带的衔接）。
- **结论**：材质**确系完全取样自原版贴图**（经新图集 `WreckageAtlas.png`），几何 + UV 在目标视角下可读；
  侧面 UV 仍为近似条带、待美术精修。**下一步需用户评审是否进入运行时接入。**

## 7. 盲测验收（独立子代理盲判）

判定协议（用户定）：几何→渲染**无标签**三视图拼图（文件名随机、映射文件移出目录）→
交**独立子代理**只读图盲判 → **多数票**认出任物方算通过。经 R1–R11 迭代后 R11 通过（4 份样本）：

| 样板 | 子代理读到的语义 | 结论 |
|------|----------------|------|
| RuinedTank | 坦克 / 主战坦克 | ✅ 4/4 |
| RustedIndustrialTruck | 卡车 / 载货货车 | ✅ 4/4 |
| GiantWheelA | 齿轮 / 手轮 / 转盘 | ✅ 4/4 |
| LargeEngineBlock | 四缸发动机（一份误读插头） | ✅ 3/4 |
| CrashedShipPart | 潜艇 / 科幻飞船 / 战机 / 航天飞机 | ✅ 4/4（船体-飞行器家族） |
| AncientWarwalkerShell | 机器人 / 人形机甲 / 方块人 | ✅ 3/4 |
| ConcreteBarrier | 路障/护栏、栏架（一份误读长凳） | ✅ 多数票 |

关键修复：飞船去喷气座舱、加尾翼/腹鳍/穹顶→稳定读作飞船；巨轮由立放改**平放**（轴沿 Y，
轮辐/胎面块绕轴 yaw 布置）→归入轮/齿轮家族；路障由实心块（误读阶梯/工字钢/导轨）改
**立柱+横挡板+脚座护栏结构**→读作路障/护栏。盲图目录 `%TEMP%\idk_*`、渲染器 `scripts/wreck_blind.py`。

## 8. 复现方式

```bat
:: 生成图集 + UVGen（读 docs/wreckage/clean/clean_report.json）
C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_atlas.py
:: 生成平面着色预览图（解析 WreckageSamples.cs）
C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_render.py
:: 生成带纹理验收页数据（C# -> viewer/wreck_uv.json），并确保 viewer/wreck_atlas.png 为最新图集
C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_viewer_json.py
:: 带纹理批量截图（需本机 8899 已把 viewer/ 作为静态根目录）
C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_shots.py
C:/Users/Administrator/AppData/Local/Programs/Python/Python311/python.exe scripts/wreck_overview.py
```