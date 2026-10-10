# 贴图 → 3D 建模转换方法（设计稿 v2，含 v1 修正记录）

> **状态（2026-10-10）：早期设计稿，不是当前实现。**
> 家具与残骸走 `docs/furniture/ThreeViewMethod.md`：三视图分区取样 + `Model3D` 基本体。
> 墙和满格岩走 `BuildingLayer3D` 的体素盒，材质是原版连接图集。
> 总架构、进度与后续任务见 `docs/Architecture.md` 与 `BACKLOG.md`。
> 下文保留推理过程。其中「按俯角把一张图拆成顶面和四壁」没有进入已落地的家具或残骸代码。

> 适用：FirstPerson3D（RimWorld 1.6）
> 目标：把原版 2D 精灵贴图系统性升维成 3D 网格，且在默认视角下与原版**逐像素一致**。
> 方案基调：**预建模原型库 + 原版贴图作为 UV 纹理**（采纳），但需修正一个前提与一个缺陷。

---

## 0.1 ⚠️ v2 修正（2026-10-10）：推翻 v1 的映射方向

**v1 把贴图当纯俯视图处理（顶面贴整张图、侧面取边缘带），方向是错的。**
离线第一人称渲染直接证伪：`docs/_uv_fpv_test.png` 第 3 列。

- 错在哪：v1 只在"俯视视角"下成立。本 mod 是第一人称（`EyeHeight = 1.5`），
  顶面几乎不可见，玩家看到的是**立面**。侧面给纯色 → 物件失去全部辨识度：
  电熔炉变成一块纯棕、墙变成纯灰块、床变成灰框。旧做法（六面贴整图）虽然"不正确"，
  但在第一人称下反而保住了图案与辨识度——v1 把可用的东西当成缺陷修掉了。
- 真正的修正（v2）：RimWorld 贴图**不是**纯俯视图，而是美术用**统一的斜视虚拟相机**
  绘制的伪 3D 图。图片的垂直方向**同时编码了世界深度与物件高度**。
  因此正确做法是**按分区反投影**：

  | 贴图区域 | 去向 | 说明 |
  |---|---|---|
  | 下部 `v ∈ [0, s]` | 模型**四壁** | 立面像素，第一人称看到的图案就靠它 |
  | 上部 `v ∈ [s, 1]` | 模型**顶面** | 俯视像素 |

  分界 `s = h·cosθ / (h·cosθ + d·sinθ)`，随物件高度/深度比自动适配
  （墙 1×1 → 0.40，熔炉 1.1/1.5 → 0.33，床 0.55/2 → 0.16）。

- θ（美术虚拟相机俯角）≈ **56°**，由墙贴图"立面约占下部 40%"反推：
  顶面投影 ∝ 深度·sinθ，立面投影 ∝ 高度·cosθ → 0.6/0.4 = tanθ → θ ≈ 56°。
- 效果见 `docs/_uv_fpv_test.png` 第 4 列：第一人称下四壁终于显示原版图案。

**与 1.1 节的关系（v1 的错误根源）**：`CameraDriver` 是**游戏渲染相机**，它确实垂直俯视；
而美术**绘制贴图时用的虚拟相机**是斜视的。这是两台不同的相机，v1 把二者混为一谈，
于是从"游戏相机垂直俯视"错误地推出"贴图内容也是俯视"。

---

## 0. 结论先行

> ⚠️ **下表是 v1 的原始结论。其中第 2、3 条已被 0.1 节推翻，请以 0.1 节为准。**
> 保留原文是为了记录推理过程与错误来源。

| # | 结论 | 依据 |
|---|---|---|
| 1 | 原版**游戏渲染相机** = 正交垂直俯视，没有俯角（此条成立）。 | `CameraDriver.cs:47-49,193-194,339-345` |
| 2 | 因此贴图能提供的**唯一完整信息是顶面**；高度（Y）在贴图里**完全不存在**，必须由规则/手工补。 | `Printer_Plane.cs:79-82` |
| 3 | 推荐方案的落点是 **轮廓挤出（silhouette extrusion）**：轮廓取自贴图 alpha，高度取自 def 规则，侧面取自贴图边缘外推。 | 由 1、2 推导 |
| 4 | 当前代码有一个 P0 缺陷：`AppendBox` 六个面都用整张图的 0..1 UV，导致**侧面出现被压缩的整张图**（截图里的重复砖墙即此因）。 | `BuildingLayer3D.cs:228-249`，`shots/r12_wall_v055.png` |

**一句话**：不是"手工给每个物件建模再贴图"，而是"**把贴图当俯视轮廓挤出高度的同时，把贴图原样铺回顶面**"。只有挤出搞不定的少数物件才需要手工精模。

---

## 1. 实证基础

### 1.1 相机是正交垂直俯视 —— 铁证

```csharp
// D:/rim mod/DecompiledRefs/RimWorld/Assembly-CSharp/Verse/CameraDriver.cs
47:  public const float MinAltitude = 15f;
48:  private const float NearClipPlane = 0.5f;
49:  private const float MaxAltitude = 65f;
193:    this.MyCamera.nearClipPlane = 0.5f;
194:    this.MyCamera.farClipPlane = 65.5f;          // ← 关键
339:  private void ApplyPositionToGameObject()
343:    this.rootPos.y = 15.0 + (RootSize - min)/(max - min) * 50.0;   // 相机高度 15~65
344:    this.MyCamera.orthographicSize = this.RootSize;
345:    this.MyCamera.transform.position = this.rootPos + this.shaker.ShakeOffset;
```

推理链：
1. `farClipPlane = 65.5` **恰好**等于 `MaxAltitude(65) + nearClipPlane(0.5)`。只有视线垂直向下时，地面到相机的距离才恒等于相机高度；若有俯角，远处地面会超出 far 被裁掉。
2. `ApplyPositionToGameObject` **只写 position，从不写 rotation** → 相机姿态来自场景 prefab，固定不变。
3. `orthographicSize = RootSize` → 正交投影（无透视）。
4. 视口反算同样成立：`CameraDriver.cs:140-152` 中 `minZ = z - RootSize - 1`、`maxZ = z + RootSize`，z 方向半深 = `RootSize`（1:1 无压缩）；x 方向半宽 = `RootSize * (screenWidth/screenHeight)`（等比）。这正是垂直俯视的投影特征。

### 1.2 精灵是躺平的 quad，UV 直接对齐世界 XZ

```csharp
// D:/rim mod/DecompiledRefs/RimWorld/Assembly-CSharp/Verse/Printer_Plane.cs:79-82
subMesh.verts.Add(new Vector3(-0.5f*size.x, 0f,                  -0.5f*size.y)); // uv (0,0)
subMesh.verts.Add(new Vector3(-0.5f*size.x, topVerticesAltitudeBias, 0.5f*size.y)); // uv (0,1)
subMesh.verts.Add(new Vector3( 0.5f*size.x, topVerticesAltitudeBias, 0.5f*size.y)); // uv (1,1)
subMesh.verts.Add(new Vector3( 0.5f*size.x, 0f,                  -0.5f*size.y)); // uv (1,0)
```

由顶点顺序与 `DefaultUvs` 的配对可得：

- **u 轴 ∝ 世界 +X**（u=0 在西边 x=-w/2，u=1 在东边 x=+w/2）
- **v 轴 ∝ 世界 +Z**（v=0 在 z=-d/2，v=1 在 z=+d/2）
- 精灵 Y 方向只有 `topVerticesAltitudeBias = 0.01f` 的抬升 —— 这是防 z-fighting 的，不是"立起来"。**精灵是躺平的。**

### 1.3 世界 Z 才是"视觉上下"，世界 Y 只是绘制层

- `Altitudes.cs:19-38`：40 层，层距 `0.365853667`。Pawn 层 = 23 → `23 × 0.365853667 = 8.414`，与 `PawnLayer3D.cs:62` 注释的 "y≈8.4 悬空" 完全吻合。→ **世界 Y ≠ 视觉高度**。
- codewiki `条目/Pawn动画-PUABodyAnimation-关键帧图层坐标变换.md`（步骤 4）已固化该约定：*"腾空偏移必须加在世界 Z（屏幕垂直视觉高度），Y 轴只补 Altitudes.AltIncVect 量级的层级提升"*。

### 1.4 drawSize 语义

- `GraphicData.cs:27`：`public Vector2 drawSize = Vector2.one;`（缺省 1×1 格）
- Def 里可写单值 `<drawSize>1.2</drawSize>` 或二元组 `<drawSize>(1,2)</drawSize>`（`Core/Defs/ThingDefs_Buildings/Buildings_Furniture.xml` 实测两种都存在）
- **贴图内容 = 整个 drawSize 矩形**（含透明边距）。床：贴图 128×128、drawSize `(1,2)` → 贴图被非等比拉伸到 1 格宽 × 2 格深。

### 1.5 材质-UV 配对契约（atlas 风险）

RimWorld 有**两套** atlas 机制，配对方式不同，混用会采样错位：

| 路径 | 贴图来源 | UV 应由谁提供 |
|---|---|---|
| `Graphic_Linked`（墙/连接建筑） | `MatAt()` 返回 **atlas 子材质**，带 `mainTextureScale/Offset` | **UV 保持 0..1**，靠材质的 scale/offset 落到子图 |
| 普通 `Graphic` | 材质 = **原始贴图**（不进图集；1.6 的静态图集只在原版渲染路径用 `uvRect`） | **UV 直接用 0..1** |
| 静态图集 | `GlobalTextureAtlasManager` + `Graphic.cs:95-110` 的 `tile.uvRect` | 原版走顶点 uvRect；本 mod 不走此路径 |

→ **硬约束：本 mod 生成的所有 UV 必须落在 [0,1]² 内**，平铺一律交给材质 tiling 参数。已有代码 `Util3D.CloneForBox` 保留 scale/offset 的做法是对的，应继续遵守。

> 注：`Util3D.BuildBox` 的侧面用 `v1 = h * vScale`（v > 1）依赖 wrap=Repeat；Cutout 材质若为 Clamp 会被拉伸而非平铺。该函数目前无人调用（`BuildingLayer3D` 用自己的 `MeshBuilderX`），建议随本次改造一并清理。

---

## 2. 方法总纲：三源合成

一张原版贴图 + 一条 def 规则 = 一个 3D 原型。三个信息源各管一维：

```
             ┌─ 平面轮廓 (x,z)  ← 贴图 alpha 剪影（俯视 1:1，免费且精确）
3D 模型 = ───┼─ 高度 (y)        ← def 规则表 / 原型 json（贴图里没有，必须外补）
             └─ 侧面外观       ← 贴图边缘像素外推（贴图里没有，靠外推）
```

剖面示意（南←→北）：

```
        贴图顶部 v=1 ──┐
   ┌───────────────────┐  ← 顶面：整张贴图正交投影，与原版像素级一致
   │   顶 面（贴图）    │
   └──┬─────────────┬──┘  ← 南/北侧壁：取贴图边缘带沿高度铺开
      │  侧 壁（外推） │  H  ← 高度来自 def 规则，贴图不提供
      └─────────────┘
        贴图底部 v=0 ──┘
```

**"预建模"的正确定位**：不是逐个 def 手工捏模型，而是维护一个**原型库（prototype library）**——
- 简单物件（墙、门、床、桌椅、柜、灯…）→ 由**轮廓挤出自动生成**，零手工成本，覆盖 ~85% def
- 复杂物件（树、炮台、机械、工作台、多层结构）→ 手工预建模，挂同一套 UV 规则
- 兜底 → 退化为当前的程序化 box/billboard（保证永不空白）

---

## 3. UV 规范（核心）

### 3.1 顶面：正交俯视投影

因为相机垂直俯视，**顶面 UV 就是世界 XZ 的线性映射**：

```csharp
// 世界坐标 p → 贴图 UV。center = 精灵中心(Thing.DrawPos)，drawSize = graphic.drawSize
static Vector2 TopUV(Vector3 p, Vector3 center, Vector2 drawSize)
{
    float inset = 0.5f / texWidth;          // 防边缘漏色，见 3.3
    return new Vector2(
        Mathf.Clamp01(0.5f + (p.x - center.x) / drawSize.x),
        Mathf.Clamp01(0.5f + (p.z - center.z) / drawSize.y));
}
```

验证（局部坐标、中心在原点）：z=+d/2 → v=1（贴图顶＝北＝屏幕上方）✓；x=+w/2 → u=1（东＝屏幕右）✓。

> 这一条是整个方案成立的关键：**只要顶面用这个 UV，玩家从默认视角看过去就与原版逐像素一致**，3D 化不损失任何原版观感。

### 3.2 侧面：边缘外推（贴图里没有侧面信息）

侧面 UV 的做法是"**横向沿轮廓走，纵向固定在贴图边缘**"。三种模式：

| 模式 | 做法 | 适用 |
|---|---|---|
| `EdgeLine` | v 固定为 `ε`（南）或 `1-ε`（北），u 沿边长 0..1 | 矮物件、物品 |
| `EdgeBand`（**默认**） | 取贴图下缘 `[0, band]`（band≈0.35）沿高度铺开。墙贴图的下缘本就是砖墙立面，铺出来天然像砖墙侧壁 | 墙、门、柱 |
| `EdgeSolid` | 取边缘若干行像素均值 → 纯色侧壁 | 金属/机械、需要干净的剪影 |

> ⚠️ **本段属于 v1 思路，已被 0.1 节的分区映射取代**（侧面不该是"南缘带"，而应是贴图下部的
> 立面像素）。以下仅保留为踩坑记录。

**实测修正（离线渲染验证得出，勿踩）**：**不要**拿"俯视轮廓所在位置的那一列像素"当侧面纹理源。
第一版渲染器就是这么做的，结果墙的侧壁出现大片纯黑——因为俯视轮廓的边缘像素画的是
**顶面描边与投影阴影**，不是立面外观。正确做法是侧壁的纹理源与轮廓位置解耦：

- 竖直方向：固定取贴图**南缘带** `v ∈ [0, band]`（美术只画了南立面，四个侧面都借它）
- 水平方向：把轮廓位置 `a ∈ [0,1]` 压缩到贴图中段 `u = 0.25 + 0.5·a`，避开左右缘描边

实测：墙侧壁的纹理复杂度从 0.26 降到 0.01（转为均匀砖色）；1×1 满格建筑挤出后的
俯视覆盖率 46.2% → 65.3%，与盒体的 65.8% 基本一致，说明挤出体已闭合完整。

另一处实测坑：轮廓挤出的条带若按 `step=2` 采样却只覆盖 1 像素跨度，相邻条带之间会留下
1 像素缝隙透出背景，从侧面看就是一排"梳齿"。条带必须**无缝覆盖** `[k, k+step)`。

以盒体南面为例（当前 `AppendBox` 的错误做法 vs 修正）：

```csharp
// 现状（错误）：整张图压到侧面 → 出现"枕头贴在墙侧面"
Quad(..., uvBL:(0,0), uvBR:(1,0), uvTR:(1,1), uvTL:(0,1), matrix);

// 修正 EdgeBand：横向 0..1，纵向只在贴图下缘 band 内
float b = sideBand;              // ≈0.35
Quad(..., uvBL:(0,0),   uvBR:(1,0),
          uvTR:(1,b),   uvTL:(0,b), matrix);

// 修正 EdgeSolid：纵向固定边缘值（等价于拉成纯色带）
Quad(..., uvBL:(0,e), uvBR:(1,e), uvTR:(1,e), uvTL:(0,e), matrix);
```

北面取贴图**上缘** `[1-band, 1]`；西/东面同理取左右缘列。这样四壁与顶面在棱边处颜色连续，视觉上是"一块有厚度的板"。

### 3.3 边缘内缩（防漏色）

Cutout 材质下，贴图 alpha 边缘会因双线性采样混入透明像素，导致模型棱边出现半透明/黑边。对策：**UV 向内缩 `0.5~1 像素`**（`inset = 0.5f / texWidth`），在 3.1 与 3.2 的取值上统一施加。

### 3.4 Rot4 处理

- 有 4 向贴图的 def（床/门/椅子/柜）→ **换贴图，不换公式**。UV 公式只依赖 drawSize，与朝向无关。
- 几何占位用 `GenAdj.OccupiedRect(t)`（已含旋转），drawSize 用于 UV 归一化。两者不要混。
- 无 4 向贴图的 def（墙 linked atlas）→ 材质自带 atlas 子图，UV 保持 0..1 即可。

### 3.5 多格物件

- `drawSize` 可能是不规则值（实测 `2.05`、`1.2`、`(5,5)`）→ UV 归一化一律用 `drawSize`，**几何尺寸用 occ**，二者独立。
- 非等比缩放会让法线失真 → 合并进 chunk 后统一 `RecalculateNormals()`（现有 `MeshBuilderX.ToMesh()` 已做 ✓）。

---

## 4. 高度规则表

贴图不提供高度，按下表推定；全部可通过 ModSettings / 原型 json 覆盖。

| 类别 | 判定条件 | 默认高度（格） | 轮廓来源 | 侧面模式 |
|---|---|---|---|---|
| 满格建筑 | `def.Fillage == FillCategory.Full` | 1.0（墙，可配） | occ 矩形 | EdgeBand |
| 门 | `t is Building_Door` | 0.82 | occ（薄边） | EdgeBand |
| 家具 | Building 且非 Full | 0.45 + 0.15×drawSize.y（0.4~0.8） | 贴图 alpha 轮廓 | EdgeBand |
| 植物 | `ThingCategory.Plant` | `plant.def.plant.visualSizeRange`（或 `plantScale`） | alpha 轮廓 | EdgeLine |
| 物品 | `ThingCategory.Item` | 0.12~0.2 按 drawSize | alpha 轮廓 | EdgeSolid |
| 尸体 | `t is Corpse` | 0.15，平铺 | alpha | EdgeSolid |
| Pawn | `pawn.RaceProps.Humanlike` | 保持现有 billboard / 图集 blit 路线（不动） | — | — |

> Pawn 不在本次改造范围内：`PawnLayer3D` 的图集 blit 路线（`PawnLayer3D.cs:86-108`）已经是当前最优解，硬换成挤出模型会丢失服装/头部图层。

---

## 5. 原型库（手工预建模的边界）

需要手工建模的，是"轮廓挤出表达不了"的形体：

| 原型 | 为什么需要手工 |
|---|---|
| 树（trunk + canopy） | 需要干/冠分离的自遮挡结构 |
| 炮台 / 机械 | 有可旋转部件与炮管伸出轮廓 |
| 工作台 / 炉 | 台面 + 台腿，侧面不该是边缘色 |
| 多层结构（平台/上层） | 与 `LevelStack3D` 联动，需要楼板 |

**资产格式**：`OBJ + 同名 .proto.json`（不建议 AssetBundle —— 需匹配 RimWorld 内部 Unity 版本，跨版本易碎）。

```json
{
  "id": "worktable_unit",
  "anchor": [0.5, 0, 0.5],
  "rotateOnlyYaw": true,
  "sides": { "mode": "edgeBand", "band": 0.35 },
  "slots": {
    "0": "body",
    "1": "top"
  },
  "paramAxes": ["x", "z"],
  "defaultHeight": 0.7
}
```

`slots` 语义：`body` = 原版 `MatAt()` 主材质；`top` = 顶面专用槽（缺省复用 body）；后续可扩 `accent`（金属/发光）。

---

## 6. 落地改动清单

### P0 —— 修 `AppendBox` 的侧面 UV（改动最小、收益最大）

文件：`Source/RimFirstPerson3D/BuildingLayer3D.cs:228-249`

把北/南/西/东四面的 UV 从"整图 0..1"改为"横向 0..1 + 纵向边缘带"，顶面改为正交投影方向（现顶面已接近正确，只需核对方位：v 必须 +Z 递增）。

收益：消除侧面整图重复；墙面从"重复砖墙"变成"顶面 + 砖色侧壁"；第一人称贴近时不再穿帮。
风险：低（单函数内改动，可被 ModSettings 一键回退）。

### P1 —— 抽出 UV 生成器

新增 `Source/RimFirstPerson3D/PrototypeUV.cs`：
- `TopUV(...)` / `SideUV(mode, band, edge)` / `ApplyInset(...)`
- `ModSettings3D` 增加：`sideMode`（枚举）、`sideBand`（0~0.5）、`edgeInsetPx`（0~2）
- `DebugActions3D` 增加：打印相机参数（`Camera.main.transform` 实测值，用于回归验证 1.1 的结论）、UV 覆盖可视化

### P2 —— 轮廓挤出器

新增 `Source/RimFirstPerson3D/SilhouetteExtruder.cs`：
1. 读 `Graphic.MatAt()` 的 `mainTexture` → `GetPixels32()`（按 texPath 缓存，只做一次）
2. 沿 alpha 阈值扫出外轮廓（marching squares 或逐行 run-length，后者更简单且足够）
3. 轮廓沿 Y 挤出 → 顶点列表（原语用四边形条带）
4. UV 按 §3 规则写入
5. 用 `MeshBuilderX` 追加进现有 chunk 合并流程 → **不增加 draw call**

### P3 —— 原型库与绑定解析

1. `PrototypeLibrary`：扫描 `1.6/Meshes/*.obj` + `.proto.json`，解析成 `Mesh` + 元数据
2. `PrototypeResolver`：`ThingDef → 原型` 三层解析
   - 精确：`Defs/PrototypeBindings/*.xml` 显式指定
   - 启发式：按 §4 表分类
   - 兜底：程序化 box / billboard
   - 结果缓存进 `Dictionary<ThingDef, Resolved>`，O(1)
3. 绑定失败/解析异常一律静默回退，不产生红字（与现有 `BakeBuilding` 的 try-catch 风格一致）

---

## 7. 验收标准（可量化）

| 项 | 方法 | 通过线 |
|---|---|---|
| 顶面保真 | 原版俯视截图 vs 3D 化后同机位截图，逐像素 diff | 结构差异 < 2%（仅边缘/AA 区） |
| 轮廓匹配 | 贴图 alpha 剪影 vs 顶面渲染剪影，算 IoU | > 0.95 |
| 侧面回归 | 侧视截图检查贴图重复 | 无整图重复（当前 bug 的回归测试） |
| 性能 | `PerfMon3D`：draw call 与 `chunk rebuild ms` | 相对现状增幅 < 10% |
| 兼容 | 载入 5 个建筑类 mod 的 def | 零红字、零空白 |

---

## 8. 风险表

| 级别 | 风险 | 对策 |
|---|---|---|
| **P0** | 侧面 UV 修正后，某些贴图边缘是透明 → 侧壁整体消失 | `EdgeSolid` 兜底：边缘带全透明时退化为纯色（取贴图非透明区均值） |
| **P0** | 材质-UV 配对错（把 atlas 子材质当原始贴图用） | 严守 §1.5 契约；对 `Graphic_Linked` 只改 UV 不改材质 |
| **P1** | 挤出网格顶点数暴涨（复杂剪影） | 轮廓简化（Douglas-Peucker，ε≈0.02 格）；chunk 预算 `ChunkRebuildBudget` 下调 |
| **P1** | 非等比缩放导致法线失真 | 合并后统一 `RecalculateNormals()` |
| **P1** | 相机姿态假设不成立（未证实 rotation 来自场景） | P1 的 debug 动作实测打印；若已倾斜，则顶面 UV 加 `sinθ` 压缩项即可，公式结构不变 |
| **P2** | OBJ 解析器被恶意/畸形文件打挂 | 解析加 try-catch + 顶点数上限 |

---

## 9. 分期与验收节奏

| 阶段 | 内容 | 验收 |
|---|---|---|
| P0 | `AppendBox` 侧面 UV 修正 | 侧视不再重复；顶视 diff < 2% |
| P1 | `PrototypeUV` + settings + debug | 参数可视化，可现场调 band |
| P2 | 轮廓挤出器（先覆盖满格建筑与家具） | 灰盒建筑外观达标 |
| P3 | 原型库（树/炮台/工作台 3 个起步） | 手工原型接入零红字 |
| P4 | 对外扩展接口（外部 mod 只放 obj+json 即可接入） | 第三方接入测试 |

---

*文档依据：`D:/rim mod/DecompiledRefs/RimWorld/Assembly-CSharp/`（1.6.9438.37837）、`D:/rim mod/RimWorld_Vanilla_Textures_1.6/Core/textures/`、`D:/rim mod/_CodeWiki/条目/`。所有行号均已实际读取核对。*
