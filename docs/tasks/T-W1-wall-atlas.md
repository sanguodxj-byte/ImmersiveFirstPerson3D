---
topics: [rendering, walls]
doc_kind: task
created: 2026-10-10
feature: F005
status: coded
---

# T-W1: 把墙帽和立面贴到现有墙盒上

派发对象：快速模型。取样已经定稿，本任务不许改矩形。

已由本线直接落地（2026-10-10）：`BakeWall` / `BakeDoorHeader` 进 `BuildingLayer3D.cs`，产物三件齐。验收前三项已过，门洞与图集缺失回退两项待实机。

## 读这些

- `docs/decisions/0003-wall-atlas.md`
- `docs/walls/WallAtlasSpec.md`（矩形的唯一来源）
- `docs/walls/_sample_preview.png`（只用于核对，不要当贴图打包）
- `scripts/furn_atlas.py`（打包、描边剔除、生成 C# 的写法照这个抄）
- `Source/RimFirstPerson3D/BuildingLayer3D.cs` 的 `BakeBuilding`、`AppendSolidCell`
- `Source/RimFirstPerson3D/Util3D.cs` 的 `CutoutMatFor`、`CloneForBox`
- `Source/RimFirstPerson3D/Metrics3D.cs`、`Fps3DState.WallHeight`

## 要做的事

### 1. 打包脚本

新建 `scripts/wall_atlas.py`。矩形从规格表原样抄入，键名只有这六个：`bricks.top`、`bricks.side`、`planks.top`、`planks.side`、`smooth.top`、`smooth.side`。

沿用家具脚本的口径：`PAD = 2`，`STROKE_LUMA = 40`，黑像素用邻域补掉，透明补实，alpha 写成 255，边缘复制而不是留透明。产物：

- `1.6/Textures/FPS3D/WallAtlas.png`
- `Source/RimFirstPerson3D/WallUVGen.cs`（生成，文件头写明勿手改）
- `docs/walls/uv/<键名>.png`，放大图上画出取样框

`WallUVGen` 的 UV 与 `FurnitureUVGen` 同一口径：`Vector4(u0, v0, u1, v1)`，`v0 < v1`，Unity 的 v=0 在图的底边。另给每个键一个源像素尺寸。`TexPath = "FPS3D/WallAtlas"`。

脚本里检查每块取样：亮度低于 40 的像素占比超过 2% 就非零退出。不要为了通过检查去改矩形。

### 2. 只有建造墙走新图集

`BakeBuilding` 里 `Fillage.Full` 的分支拆开：

- `t.def == ThingDefOf.Wall`：走下面的墙盒。
- 其他满格（天然岩、平滑岩、模组墙）：继续现在的 `AppendSolidCell` 和 `CloneForBox`。不要改 `AppendSolidCell` 的 UV。

图集纹理用 `ContentFinder<Texture2D>.Get(WallUVGen.TexPath, false)`。加载失败时，建造墙也退回现在的 `CloneForBox` 路径。

材质：`new Material(ShaderDatabase.Cutout)`，`mainTexture` 是图集，`mainTextureScale = (1,1)`，`mainTextureOffset = (0,0)`，`filterMode = FilterMode.Point`，`wrapMode = TextureWrapMode.Clamp`。颜色用这块墙 `MatAt` 材质上的 `color`。按（外观键, 颜色）缓存。不要对建造墙调用 `CloneForBox`，那个函数会把 UV 锁死在一个连接子格里。

外观键按规格表：`Bricks` → `bricks`，`Planks` → `planks`，其余含 `Smooth` 和 `Metal` → `smooth`。

### 3. UV

新写墙用的发射函数，不要改岩石仍在用的 `AppendSolidCell`。

立面（含下面的门洞切块）对一个顶点：

- `u` 是该顶点在这条完整 1 m 棱上的位置，0 到 1。切成门洞之后不要把剩下的窄条重新归一化成 0..1。
- `v = (y - baseY) / Fps3DState.WallHeight`，地面为 0，墙顶为 1。
- 落到矩形：`u' = u0 + u * (u1 - u0)`，`v' = v0 + v * (v1 - v0)`。

顶面：`u` 沿格子的 +X，`v` 沿格子的 +Z，各 0..1，落入 `*.top`。整面贴一次，不重复。

立面落入 `*.side`。一整面墙（高 `WallHeight`）把立面矩形贴一次。不要按高度重复，这段像素垂直方向对不齐。

风序保持现在朝外的绕序。描边壳只包实际发出的那些面，门洞里不要留下全高的壳。描边材质仍是 `Util3D.OutlineMat`。`settings.wallVScale` 继续不要参与墙的 UV 或高度。

### 4. 门洞

只对这五个 defName 切洞：`Door`、`Autodoor`、`OrnateDoor`、`SecurityDoor`、`VacBarrier`。栅栏门和动物帘不切。缺 DLC 时对不上名字就当普通邻居。

切口的面是**门格朝房间的那一面**（与相邻墙的立面共面那一个平面），不是墙格朝门的门洞侧壁：墙是 1 m 厚的整格盒子，侧壁的 1 m 棱沿墙厚方向，在它中间开 0.95×2.05 的口会在隧道两侧留下透光的槽。所以侧壁发整面，口开在门格那一面。沿这条 1 m 棱：

- `inset = (1f - Metrics3D.DoorWidth) * 0.5f`
- 左条：棱坐标 `[0, inset]`，高度 `[0, DoorHeight]`
- 右条：`[1 - inset, 1]`，高度 `[0, DoorHeight]`
- 门楣条：整条棱，高度 `[DoorHeight, WallHeight]`
- 中间开口不发几何

门所在的每一格再发一块门楣盒：占满该格，底在 `baseY + DoorHeight`，顶在 `baseY + WallHeight`。朝向满格邻居的立面剔除。顶面用 `*.top`。底面（门洞上沿）法线朝下，UV 用 `*.top`。朝向房间的立面用第 3 节的高度比例，这样砖缝和旁边的墙对齐。上面那块门楣条就是这块门楣盒朝房间的那一面，不要重复发两遍。

门扇保持现在的盒子（厚 `Metrics3D.DoorPanelThickness`，高 `Metrics3D.DoorHeight`，朝向逻辑不动）。`Building_Door.Open` 为真时不发门扇。门楣在开关时都在。门扇材质仍用门自己的 `MatAt`，不塞进墙图集。

不要用 `DoorPostWidth` 再做一圈门框。开口宽度就是 `DoorWidth`。

## 验收

- [x] `python scripts/wall_atlas.py` 退出码 0，三个产物都在，六个键的描边占比检查通过。
- [x] `dotnet build "Source\RimFirstPerson3D\RimFirstPerson3D.csproj"` 通过。不要部署，游戏开着会锁 DLL。
- [x] 建造墙的顶面 UV 来自 `*.top`，立面来自 `*.side`。天然岩的调用仍进原来的 `AppendSolidCell`。
- [ ] 关着的允许名单门挡住通道；`Open` 时没有门扇，洞口上方仍有门楣。洞口宽度正好 `DoorWidth`（两侧各 0.025 的窄条在门格朝房间的那一面），门洞侧壁发整面、不透光。
- [ ] 图集文件缺失时，建造墙仍用现在的连接子格画出来，不抛异常。

## 不要做

- 不要改规格里的矩形，不要给意识形态墙风格加新矩形。
- 不要改 `FurnitureUVGen.cs`、`WreckageUVGen.cs`、已有的家具和残骸图集，不要改 `furn_atlas.py`。
- 不要改 `AppendSolidCell` 的 0..1 UV，岩石还在用。
- 不要动相机、移动、流式加载。
- 不要把 `settings.wallVScale` 用进墙高。高度只有 `Fps3DState.WallHeight` 和 `Metrics3D` 里已有的门常数。
- 不要为了门洞去改 chunk 脏标记。`NotifyThingChanged` 已经按占格标脏。
