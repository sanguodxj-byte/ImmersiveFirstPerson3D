# 家具三视图重构规范（v1，2026-10-10）

适用范围：FirstPerson3D 家具部分。上游依据 = 原版每件家具的**三视图**
（`_south` / `_east` / `_north` 三张朝向图）。

## 0. 三视图到底是什么

**不是正俯视，是带俯角的斜俯视。** 每张图内：

```
 上部 = 俯视段   物件顶面（被俯角压扁过）
 下部 = 立面段   朝向镜头那一面的真实外观
```

三张图的**立面段各是不同的一面**，所以三视图能凑齐模型的四个真实立面：

| 模型面（世界法线） | 取样来源 |
|---|---|
| 顶面 +Y | 当前朝向那张图的俯视段 |
| +Z（南，朝镜头） | 当前朝向那张图的立面段 |
| -Z（北） | 相反朝向那张图的立面段 |
| +X / -X（东/西） | 东向图的立面段（西 = 东镜像） |

原版没画满三向的（如 stool 只有 east/north），按 south→north→east 顺序回退。

**远端约定**：图里上部那块是"背对镜头的一端"——床的枕头、椅的靠背都在这一端。运行时用
`Furniture3D.BackSign(rot)` 取它在模型长轴上的符号（南=+Z，北=-Z，东=-X，西=+X）。
2026-10-10 修正：南向/北向的床头板与床尾板此前放反了。

**不要**把整张贴图糊到六个面上（那是本 mod 之前的立板回退，第一人称下就是错的）。
**不要**反推"美术相机俯角"再套公式：贴图是画出来的，不是投影出来的，
不同图之间比例互不自洽（实测同一柜体的立面在南/东图里 29px vs 32px，
顶面深度比却不等于长宽比）。三视图只当**素材 + 形状参考**。

## 1. 硬约束（全部可机检，`scripts/furn_uv_check.py`）

1. **坐标** = 源图像素，左上原点，半开区间 `[x0,x1)×[y0,y1)`；切点必须落在像素边界。
2. **黑色笔画一律不进材质**：`不透明 且 luma<40` 的像素（剪影描边 + 内线）
   在打包时用邻域均值补绘替换。描边由反绕外壳网格负责，不靠贴图。
   判据 = 图集块内不得有"原封没动"的黑像素。
3. **不空取**：每张图的内容像素（去掉笔画及其外扩 1 格）必须至少落在一个取样区里。
   豁免只有两类，且都要求写进 SPEC：
   - 内容包围盒左右各 6 列（侧棱，归垂直方向那张图）；
   - `discard` 带 —— **必须本身笔画占比 ≥30% 才允许丢**，否则报 `abuse`（防止拿它藏红）。
4. **不重复取样**：同一张图内取样区不得互相重叠。
5. **取样区不得基本压在笔画上**：块内笔画占比 >55% 判 `stroke`，该改取干净内区。
6. **表 ↔ 几何双向对齐**：图集里有但没人引用 = `unused`；代码引用了但表里没有 = `dangling`。
7. **尺寸只来自 `Metrics3D`**（现实家具尺寸）。原版 def 只用于定位：占几格、朝向。
   占格一律**查 def XML**：`D:\steam\steamapps\common\RimWorld\Data\Core\Defs\ThingDefs_Buildings\*.xml`，
   块内没有 `<size>` 就是 1×1。已核实：Bed (1,2)、Shelf (2,1)、Dresser (2,1)、
   Stool / EndTable / DiningChair / **Armchair** 均 1×1。
   **不要从贴图内容宽度反推占格** —— 256px 画布可以只画一件 1×1 的家具（Armchair 曾被误当成 2×2）。
8. **每件必须有一块 `wood`**（干净木面区，从该件自己的实心木面里取，无笔画无洞）：
   三视图只画**水平面（俯视段）+ 朝向镜头的立面（立面段）**，圆柱的侧壁/顶盖（腿、立柱、
   座沿）和薄板四周一圈立边（台面/顶板 5cm 厚的边）**在图里根本没有对应像素**。
   这些面一律取 `wood`，不许拿别的朝向的立面段糊（`nowood` 报错）。
   `wood` 允许与 top/front 区重叠（它是同一块表面上再取一份干净样本），其它区之间仍不得重叠。
9. **线稿型（白面 + 黑框，如 armchair）**：这类图没有"俯视段/立面段"之分，整件是一块圆角
   白面。按行分带切 `top`/`back`/`front`/`arml`/`armr`，每带的 x 区间 = 该带各行最长不透明段
   的交集（带内天然无洞）；板件边棱与圆柱仍走 `wood`。运行时用 `UvNames`/`PanelUv` 点名配区，
   缺某朝向的某条带时按 south→north→east 回退。

## 2. 单一真源与产物

```
scripts/furn_atlas.py     SPEC（逐图目视标定的取样表）+ 打包 + UV 覆盖可视化
   ↓                      → 1.6/Textures/FPS3D/FurnitureAtlas.png
   ↓                      → Source/.../FurnitureUVGen.cs（生成，勿手改）
   ↓                      → docs/furniture/uv/<item>_<view>.png（绿框=取样，红框=丢弃带）
scripts/furn_uv_check.py  双向校验器（自带合成夹具，夹具不过则拒绝给存量背书）
scripts/furn_color_check.py 颜色保真：材质像素必须与原版逐字节相同（补绘只许发生在笔画/洞上），
                          并汇总每件色感，回答"太白/发灰是美术还是管线"
scripts/furn_shape_check.py 单格形状探针（差分遮罩 + 按行分段），判"部件悬空/被剔除"
Source/.../Furniture3D.cs 运行时：def→原型归类 + 基本体拼装 + 按朝向配立面
```

判读用的放大对照表：`scripts/furn_views.py`（三视图并排 + 像素网格 + 行列刻度）。
线稿型（圆角大白块）的分带坐标：`scripts/_furn_fit.py pack <件>` —— 按行/列求"无洞最长
不透明段的交集带"，机器给候选、人判语义，别拿它代替看图。

## 3. 建模内核用法（`Model3D` / `ModelBuilder`）

- 一个模型 = 若干 `Part`（Box/Cylinder/Cone/Wedge/Sphere），`Part.uv` 指向
  `List<UvSpec>` 的下标 → **每个部件用自己的取样区**，否则各部件重复同一块图案。
- `UvSpec.side4[4]` = 北/南/东/西各自的立面区，退化的元素回退 `side`。
- 顶面 UV 按部件在**模型空间 XZ 包围盒**中的占比归一化（`TopRectFor`），
  不是按部件自身尺寸。
- 部件配区：`FaceUv` = 顶面俯视段 + 四立面各朝向立面段；`SlabUv` = 顶面俯视段 +
  四周一圈立边取 `wood`（台面/顶板/书架的层板与侧板）；`EndPanelUv` = 端板大面用立面段、
  两条 8cm 窄边取 `wood`；`WoodUv` = 整件取 `wood`（腿、立柱等圆柱）。
  圆柱的侧壁只认 `UvSpec.side`，顶盖认 `top`，所以座面用 `FaceUv` 再把 `side` 换成
  `wood` 即可只改座沿、不动座面。
- **检查页的 `cyl()` 第 7 个参数 = 直径**，与 `Part.size.x`（Cylinder 用直径）同口径。
  之前按半径传，凳面/凳腿/床柱在审核表里全比游戏内粗一倍 —— 检查页与运行时任何一处
  口径不一致，审核表就白看。
- 相邻部件要么**互相穿进一点**（扶手伸进靠背、层板比侧板内空再小 1cm），要么留出缝隙；
  **不得严格共面**（书架层板 0.84/0.78 vs 侧板内空 0.85/0.8），共面会 z-fighting。

## 4. 本批已落地 / 待办

已落地（`furn_uv_check` 0 缺陷、`furn_color_check` 材质像素零改写、240 格审核无剪影断裂、
编译 0 错 0 警）：`bed` `stool` `endtable` `dresser` `shelf`（含 `wood` 干净木面区 +
圆柱/薄边配区）、`armchair`（线稿型分带首例）、`table1x2` `table2x2` `table2x4` `table3x3`
（台面 top / 台面边 edge / 画出来的腿条 front / 腿的圆盖 wood）、
`bookcase` `bookcasesmall`（共用 `Arch.Cabinet`：正面就是厚框 + 亮内腔，读作一块大面板）、
`shelfsmall`（共用 `Arch.ShelfUnit`）。这三件只声明 south/east —— north 是素背板，
交给 south→north→east 回退；试算时落在纯透明行上的带一律缩回，不靠 discard 藏（abuse 会响）。

原型归类改成**配方表**：`Recipes[]` 一行 = defName 关键字 → (几何配方, 取样件名)。
一个配方可服务多件（四张桌共用 `Arch.Table`），所以件名与配方必须分开；取样件在图集里
没有区 = 不接管（走原立板回退），杜绝"整族接管却借用别件的像素"（bookcase 曾借 shelf 的区）。
`AllRegions` 由 `scripts/furn_regions.py` 从 SPEC 机械生成，不手抄。

待办，按优先级：
1. **couch**：横带 ~96%，纵带 99.4%（`_furn_fit.py pack` 已给坐标），但扶手在两侧形成
   独立小块，要与 armchair 的扶手配区一起定。
2. **床的变体** doublebed / royalbed / hospitalbed / bedroll / bedrolldouble /
   sleepspot / doublesleepspot：分带 90~97%，枕头、床尾架、输液杆要单独配区。
3. **diningchair**：圈背 + 椭圆坐垫 + 四条腿，白面被黑框切成互不相邻的小块，
   横带/纵带都只能覆盖 ~97%（分带器对纯透明行直接判废），需要按连通块切区的工具。
4. royalbed、hospitalbed、bedroll、sleepspot、doublebed 等床的变体（分带 90~97%，
   枕头/床尾结构要单独配区）；lampstanding、plantpot、column、walllamp、floodlight、
   stele、Royalty throne/drape。
5. 家具模型并入 chunk 合批（当前逐件 `DrawMesh`，与 Animal3D 同路）。
6. 实机验证（GABS）：第一人称近看 + 三向旋转，确认四立面各取其图；游戏内颜色比对。
