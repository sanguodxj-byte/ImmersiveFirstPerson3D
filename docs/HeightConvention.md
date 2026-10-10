# Y 轴高度约定（FirstPerson3D 唯一的魔法常数来源）

> 适用：FirstPerson3D（RimWorld 1.6）· 3D 自由视角
> 状态：**已定稿**。代码落地在 `Source/RimFirstPerson3D/Metrics3D.cs`。
> 验算图：`docs/_height_scale.png`（由 `scripts/height_check.py` 生成）

---

## 0. 结论先行

> ## 1 格 = 1.00 米，X / Y / Z 三轴同构，无任何缩放。
>
> 换算：`高度(格) = 现实尺寸(米) / 1.00`

原版 RimWorld **没有高度轴**（世界 Y 只用于绘制分层，`Altitudes` 40 层），所以"墙多高、
人多高、桌多高"在原版里全部无解。本 mod 要做 3D 自由视角，就必须自己约定一整套高度——
而约定需要锚点，否则所有数值都是拍脑袋的魔数。

---

## 1. 为什么唯一标尺是「角色贴图的世界长宽」

老板定的规则：

- **原版 def 数据一律不作参考**（`size` / `drawSize` / `fillage` 全部不看）。
- 唯一参考 = **角色贴图在世界里占的长宽** + 现实家具尺寸 + 现实人类身高。

理由：原版贴图与def 之间存在多套互不兼容的缩放约定（床 `drawSize=(2,2)` 却只占 1x2 格；
扶手椅贴图 256px 但只占 1 格；桌 `drawSize` 是非整数值 `2.05`），**def 无法反推贴图**。
但反过来成立——**贴图在世界里占几格是确定的**（这是美术规范，不是 def 可改的）。

### 1.1 实测：贴图有效内容

脚本 `scripts/measure_tex.py`，判据为 alpha ≥ 16 的像素包围盒：

| 样本 | 贴图 | 有效宽 | 有效高 | 占宽 | 占高 |
|---|---|---|---|---|---|
| 身体 男/南 | 128×128 | 38..89 (52px) | 43..107 (65px) | 40.6% | 50.8% |
| 身体 女/南 | 128×128 | 40..87 (48px) | 38..112 (75px) | 37.5% | 58.6% |
| 身体 瘦/南 | 128×128 | 50..76 (27px) | 43..108 (66px) | 21.1% | 51.6% |
| 身体 壮/南 | 128×128 | 25..101 (77px) | 32..126 (95px) | 60.2% | 74.2% |
| 身体 胖/南 | 128×128 | 23..102 (80px) | 38..112 (75px) | 62.5% | 58.6% |
| 头 男/南 | 128×128 | 41..86 (46px) | 39..88 (50px) | 35.9% | 39.1% |

### 1.2 关键：头与身是分离图层，必须叠加

**这是测量人形时最容易踩的坑**：只测身体贴图得到的是"无头躯干"，会低估身高约 1/3。

叠加所需的两个偏移量来自 `Core/Defs/Misc/BodyTypeDefs/BodyTypes.xml`（仅用于**定位**，
不用于反推尺寸）：

- `headOffset = (0.04, 0.34)` — 头中心沿朝向前移 0.34 格（五个体型一致）
- `bodyGraphicScale = (1, 0.9)` — 身体深度方向压缩 0.9（各体型不同）

叠加后（`scripts/measure_pawn.py`）：

| 体型 | 投影宽（格） | 投影长（格） | 现实肩宽 | 反推「1格=?米」 |
|---|---|---|---|---|
| Male | 0.406 | 0.683 | 0.47 m | 1.16 m |
| Female | 0.375 | 0.738 | 0.42 m | 1.12 m |
| Thin | 0.211 | 0.715 | 0.38 m | 1.80 m ⚠ |
| Hulk | 0.602 | 0.928 | 0.58 m | 0.96 m |
| Fat | 0.625 | 0.746 | 0.60 m | 0.96 m |

Thin 体型是 `bodyGraphicScale.x = 0.6` 横向压扁的美术特例，**不参与锚定**。
其余四型给出 0.96 ~ 1.16 m，均值约 1.09 m。

---

## 2. 两个独立锚点必须自洽

单一锚点不可靠（美术画风会偏）。用两个互相独立的锚点交叉验证：

| 锚点 | 内容 | 约束 |
|---|---|---|
| **A（人）** | 1 格宽要容纳肩宽 0.45 m 的人 | 1 格 ≥ 0.45 m |
| **B（床）** | 床在游戏里是 1×2 格，对应现实 1.0 m × 2.0 m | 1 格 = 1.00 m |

两者相容，且锚点 B 落在锚点 A 推导出的 1.09 m 附近。

### 2.1为什么取 1.00 而不是 1.09

锚点 A 的 1.09 m 来自"美术画的肩宽像素" ↔ "现实肩宽"，但美术画的是**俯视投影宽**
（肩宽被投影压缩，且含手臂），不是真实肩宽。偏差方向不确定。

而 1.00 m 让整个系统最简洁：**X / Y / Z 用同一个尺度，代码里不需要任何换算系数**。
定1.00 的代价是墙厚 1 m偏厚（现实 0.24 m），但这是网格游戏的固有约束
（原版墙就占满1 格），换取的是三轴同构 simplicity，值得。

---

## 3. 高度总表

以下米数即格数，直接引用`Metrics3D.cs`。

### 3.1 人体

只保留**站姿**。原版 RimWorld 没有"坐"这个概念——Pawn 要么站着，要么躺在床上睡觉，
不存在坐姿动画与坐姿贴图，因此不设坐姿常量。

| 项目 | 现实（米） | Y 轴（格） | 代码常量 |
|---|---|---|---|
| 成年身高 | 1.75 | 1.75 | `HumanHeight` / `PawnHeight` |
| 站立眼高 | 1.62 | 1.62 | `HumanEyeHeight` / `EyeHeight` |
| 肩宽 | 0.45 | 0.45 | `HumanShoulderWidth` |

### 3.2 座椅

座面高度是**家具自身的尺寸**（现实中椅子确实有这个高度），不代表角色能坐。

| 项目 | 米/格 | 代码常量 |
|---|---|---|
| 餐椅座高 | 0.45 | `ChairSeatHeight` |
| 餐椅靠背顶 | 0.90 | `ChairBackHeight` |
| 扶手椅座高 | 0.42 | `ArmchairSeatHeight` |
| 扶手椅靠背顶 | 0.85 | `ArmchairBackHeight` |
| 吧台面 | 1.05 | `BarHeight` |

### 3.3 桌与柜

| 项目 | 米/格 | 代码常量 |
|---|---|---|
| 餐桌/工作台面 | 0.75 | `TableHeight` |
| 书桌 | 0.73 | `DeskHeight` |
| 茶几 | 0.45 | `CoffeeTableHeight` |
| 床头柜 | 0.55 | `NightstandHeight` |

### 3.4 床（层级可叠加）

```
床架上沿 0.40  (BedFrameHeight)
  + 床垫厚 0.15  (MattressThickness)
= 床垫面 0.55(MattressSurface，代码里就是前两者相加)
  + 被褥厚 0.08  (PawnLyingSurface 里加的)
= 睡者底边 0.63  (PawnLyingSurface)
```

| 项目 | 米/格 | 代码常量 |
|---|---|---|
| 床架总高 | 0.40 | `BedFrameHeight` |
| 床垫厚 | 0.15 | `MattressThickness` |
| 床垫面 | 0.55 | `MattressSurface` |
| **睡者底边** | **0.63** | `PawnLyingSurface` |
| 床卷上的睡者底边 | 0.08 | `BedrollLyingSurface` |
| 床头板顶 | 1.05 | `BedHeadboardHeight` |

#### 为什么原版没有"睡者高度"

原版 Pawn 躺床上时，sprite 是**直接画在地面 altitude 层上**的。查`PawnRenderer.cs:414`：

```csharp
float num = BaseHeadOffsetAt(Rot4.South).z + bodyType.bedOffset + bed.def.building.bed_pawnDrawOffset;
Vector3 vector3 = intVec3.FacingCell.ToVector3() * num;   // ← 只作用于 X/Z
bodyPos = shiftedWithAltitude - vector3;
```

偏移量全部作用在 **XZ 平面**，y 分量只来自 `ToVector3ShiftedWithAltitude(AltLayer)`，
即绘制海拔层（美术分层用，恒为 2D 屏幕深度，**不是真实高度**）。

所以**原版根本不存在"睡者高度"这个概念** —— 躺睡和站立画在同一套 2D 地面上。
本 mod 要 3D 化，就必须自己定。

#### 取值依据

睡者躺在床垫上，被褥 + 身体有厚度，**不能与床垫面完全共面**（会 z-fighting）。
取现实被褥厚度 0.08 m，故睡者底边 = 0.55 + 0.08 = **0.63**。

床卷（`bedroll`）是铺在地上的，没有床架，Pawn 直接贴地 + 被褥 = **0.08**。
（另外实测 `bed_showSleeperBody = false`，原版压根不画床上的睡者身体。）

代码落地在 `PawnLayer3D.LyingYFor()`：站立走 `baseY`，躺睡按床种类加偏移。

### 3.5 建筑

| 项目 | 米/格 | 代码常量 |
|---|---|---|
| 室内净层高 | 2.70 | `CeilingHeight` / `WallHeight` |
| 层高（含楼板 0.30） | 3.00 | `StoryHeight` |
| 门高 / 门宽 | 2.05 / 0.95 | `DoorHeight` / `DoorWidth` |
| 窗台高 | 0.90 | `WindowSillHeight` |
| 窗高 | 1.20 | `WindowHeight` |
| 踢脚 | 0.10 | `BaseboardHeight` |
| 栏杆/扶手 | 1.05 | `RailingHeight` |
| 墙体厚（X/Z） | 1.00 | `WallThickness` |

### 3.6 零散物件

| 项目 | 米/格 | 代码常量 |
|---|---|---|
| 桌面小件 | 0.25 | `TabletopItemHeight` |
| 地面物件 | 0.30 | `FloorItemHeight` |
| 挂墙件 | 0.35 | `WallItemHeight` |

---

## 4. 自洽性检查（人眼可验）

见 `docs/_height_scale.png`。五条必须成立的直觉关系：

1. **肩宽 0.45 格 / 格宽 1 格 = 45%** → 人站在格中央，两侧留白充足，符合俯视网格观感
2. **桌面 0.75 格 ≈ 人胯部** → 站着写字高度自然
3. **床垫面 0.55 格 ≈ 人腰腹** → 躺在床上只露出上半身
4. **睡者底边 0.63 > 床垫面 0.55** → 被褥层 0.08，躺睡不与床垫共面（避免 z-fighting）
5. **室内净高 2.70 / 人高 1.75 = 1.54** → 举手可及天花，标准室内比例

第 4 条在图上有6 倍局部放大框，床垫面 / 被褥 / 睡者底边三层分明。

---

## 5. 约定纪律（写给后续所有改动）

### 5.1 禁止事项

- ❌ **禁止在业务代码里写无出处的魔数**（如 `0.82`、`1.37`、`2.6f`）。
  一律引用 `Metrics3D.cs` 的常量。
- ❌ **禁止用原版 def 反推尺寸**。`size` / `drawSize` / `fillage` 仅用于**定位**
  （占几格、朝向、碰撞），绝不用于推导米数。
- ❌ **禁止为新物件随手取整**。"看起来差不多"不是依据。取值必须能在现实家具里找到对应物。

### 5.2 新物件高度怎么定

1. 在现实世界里找同类物品，查其人体工程学尺寸（标准家具尺寸是有据可查的）
2. 把米数填进 `Metrics3D.cs`，加注释写明出处
3. 跑 `scripts/height_check.py` 重新生成验算图，肉眼确认比例不违和
4. 若属于新类别（植物、容器、机械…），在 3.x 节补一节

### 5.3 分类回退

无专属常量时按此顺序回退（见 `Metrics3D` 的 `Fallback*`）：

```
建筑 → FallbackBuildingHeight  (2.70)
家具 → FallbackFurnitureHeight (0.75)   # 1 格家具
植物 → FallbackPlantHeight     (1.00)
物品 → FallbackItemHeight      (0.25)
Pawn → FallbackPawnHeight      (1.75)
```

---

## 6. 相关文件

| 文件 | 作用 |
|---|---|
| `Source/RimFirstPerson3D/Metrics3D.cs` | 常数唯一真源 |
| `Source/RimFirstPerson3D/Fps3DState.cs` | 引用 `Metrics3D`，不含魔数 |
| `Source/RimFirstPerson3D/PawnLayer3D.cs` | `LyingYFor()` —— 站立/躺睡的高度分流 |
| `scripts/measure_tex.py` | 实测贴图有效内容像素范围 |
| `scripts/measure_pawn.py` | 头身叠加，测人形投影尺寸 |
| `scripts/height_convention.py` | 换算表 + 锚点自洽性检查 |
| `scripts/height_check.py` | 生成 `docs/_height_scale.png` |

---

## 7. 踩坑记录

### 7.1 头身分离，只测身体会低估身高约 1/3

身体贴图 65px高（128px 贴图），头贴图另计。两者靠 `headOffset` 定位。
只测身体 → 把人算成 0.51 格高，比真实 1.75 格矮了一大截。

### 7.2 Thin 体型的`bodyGraphicScale.x = 0.6`

横向压扁是美术特例，用它反推尺度会得到 1.80 m 的荒谬结果。**锚定只取 Male / Female /
Hulk / Fat 四型**。

### 7.3 用家具贴图有效宽反推尺度，平均误差 38%

早期尝试用家具像素宽交叉验证，得到 38% 平均误差——因为美术在格子里留了很宽的边距，
贴图内容并不等于物件占地。**贴图有效内容 ≠ 物件世界尺寸**，只有"占几格"才是可靠的。

### 7.4 def 的 `drawSize` 与 `size` 不一致

床`size=(1,2)` 但 `drawSize=(2,2)`；桌 `drawSize=2.05`（非整数）；扶手椅 `drawSize=(3,3)`
但只占 1 格。**这就是"原版数据不能作参考"的实证**。

### 7.5 别给不存在的游戏状态发明常量

曾加过 `HumanSeatedEyeHeight = 1.20`（坐姿眼高）和 `PawnSittingHeight = 1.30`
（坐姿 Pawn 顶高）。**原版 RimWorld 里没有"坐"这个概念**——Pawn 要么站着，
要么躺在床上睡觉，既没有坐姿动画也没有坐姿贴图。两个常量纯属凭现实经验编造，已删。

判断依据只能是**原版实际有无此状态**，不能是"现实里有没有这个东西"。

区分：座椅的 `ChairSeatHeight` 0.45 要保留，因为它是**家具自身的尺寸**
（现实中椅子确实有这个高度），与"角色能不能坐"无关。

### 7.6 躺睡高度：原版根本没有这个概念

查`PawnRenderer.cs:414` 发现，原版把 Pawn 摆到床上时偏移量全作用在 XZ 平面，
y 分量只来自绘制海拔层（美术分层用，不是真实高度）。
**所以"睡者该离地多高"这个问题在原版里不存在**，3D 化时必须自己定。

另外实测 `bed_showSleeperBody = false`（bedroll/bedrollDouble），
说明原版连床上的睡者身体都不画，进一步印证原版没处理这个问题。

---

*所有像素数据来自 `D:/rim mod/RimWorld_Vanilla_Textures_1.6/Core/textures/` 实测；
头身偏移来自 `Data/Core/Defs/Misc/BodyTypeDefs/BodyTypes.xml`（仅用于定位）。*
