# RimFirstPerson3D 3D 几何建模覆盖现状与缺失清单

> 本文档记录 RimWorld Core 及扩展中建筑、家具、物块、动物与残骸的 3D 建模覆盖状态与待建模清单。

---

## 一、当前已建立 3D 几何模型的范围

当前已在 `Furniture3D`、`WallModels3D`、`BuildingLayer3D`、`Wreckage3D`、`ItemModels3D` 和 `Animal3D` 中建立真实三维几何体（而非立板平面贴片）的类别如下：

### 1. 核心家具与生产台（`Furniture3D`，46 种专用配方）
* **生产与科研工作台（Batch A1，16 件）**：
  * 屠宰台（`TableButcher`）
  * 雕塑台（`TableSculpting`）
  * 燃料锻造台（`FueledSmithy`）
  * 电力锻造台（`ElectricSmithy`）
  * 电灶台（`ElectricStove`）
  * 燃料灶台（`FueledStove`）
  * 手工裁缝台（`HandTailoringBench`）
  * 电动裁缝台（`ElectricTailoringBench`）
  * 切石台（`TableStonecutter`）
  * 机械加工台（`TableMachining`）
  * 制药台（`DrugLab`）
  * 酿酒台（`Brewery`）
  * 电力熔炼炉（`ElectricSmelter`）
  * 简易研究台（`SimpleResearchBench`）
  * 高级研究台（`HiTechResearchBench`）
  * 精密装配台（`FabricationBench`）
* **基础家具与寝具（14 件）**：
  * 单人床（`Bed`）、双人床（`DoubleBed`）、皇室床（`RoyalBed`）、医疗床（`HospitalBed`）
  * 睡袋（`Bedroll`）、双人睡袋（`BedrollDouble`）
  * 动物床（`AnimalBed`）、远古床（`AncientBed`）
  * 桌子（`Table1x2c`、`Table2x2c`、`Table2x4c`、`Table3x3c`）
  * 床头柜（`EndTable`）、梳妆台/工具柜（`Dresser` / `ToolCabinet`）
* **座椅与收纳（7 件）**：
  * 凳子（`Stool`）、餐椅（`DiningChair`）、扶手椅（`Armchair`）、沙发（`Couch`）
  * 置物架（`Shelf`）、小型置物架（`ShelfSmall`）、书架（`Bookcase`）、小型书架（`BookcaseSmall`）
* **照明、装饰与仪式（9 件）**：
  * 落地灯（`StandingLamp`）、泛光灯（`FloodLight`）
  * 石柱（`Column`）、盆栽（`PlantPot`）、动物标牌（`PenMarker`）
  * 宝座（`Throne`）、宏伟宝座（`GrandThrone`）、垂幔（`Drape`）、石碑（`SteleLarge`、`SteleGrand`）

### 2. 墙体、门、通道设备与掩体（`WallModels3D` / `BuildingLayer3D`）
* **满格墙体（高 2.70m，全封闭三维盒体 + 描边壳）**：砖石墙、木板墙、打磨石墙、天然冰墙、强化墙、重力舰船体壳等 12 种墙体。
* **门与自动门**：`Door`、`Autodoor`（含动态门楣与开门位移）。
* **穿墙 1x1x1 设备（高 1.00m，上方与两侧墙体平齐缝合）**：通风孔（`Vent`）、空调（`Cooler`）。
* **四向连接掩体与低矮隔断**：沙袋（`Sandbags`）、路障（`Barricade`）、各类围栏（`FenceSmooth`、`FenceMetal`、`AncientFence`）。
* **壁挂件（1x1 占格，高度 Y=2.00m）与火把**：壁灯（`WallLamp`）、壁挂火把（`TorchWallLamp`）、落地火把木柄（`TorchLamp`，火光走独立着色器）。
* **地面管线**：电缆（`PowerConduit`、`WaterproofConduit`）。

### 3. 自然物块、矿石与残骸（`ItemModels3D` / `Wreckage3D` / `MountainLayer3D`）
* **碎石块与金属废渣**：砂岩/花岗岩/石灰岩/板岩/大理石碎石块（`ChunkRock`、`ChunkSandstone` 等）及钢铁废渣（`ChunkSlagSteel`）。
* **远古机械残骸**：战行者外壳（`ancientwarwalkershell`）、巨轮（`giantwheela`）、坠毁飞船残片（`crashedshippart`）、废弃坦克（`ruinedtank`）、大型引擎组（`largeengineblock`）、混凝土防撞栏（`concretebarrier`）、锈蚀工业卡车（`rustedindustrialtruck`）。
* **山体与天然岩石**：自然岩石开采面（`MountainLayer3D` 三维方块网格）。

### 4. 动物（`Animal3D`）
* 原版全部陆生动物已完成 3D 几何建模导入与三视图角度适配（共 171 个动物种类/变体）。

---

## 二、尚未建立 3D 模型的建筑与设施清单（目前处于立板 Slab 状态）

以下建筑在游戏中目前处于立板（Billboard / Slab）回退或平面贴图状态，需按计划开展 3D 几何建模：

### 1. 娱乐设施（Joy / Recreation，共 8 件）
* 台球桌（`BilliardsTable`）
* 国际象棋桌（`ChessTable`）
* 扑克桌（`PokerTable`）
* 乌尔王棋盘（`GameOfUrBoard`）
* 蹄铁桩（`HorseshoesPin`）
* 环石圈（`HoopstoneRing`）
* 显像管电视（`TubeTelevision`）
* 平板电视（`FlatscreenTelevision`）

### 2. 电力与能源设施（Power，共 8 件）
* 蓄电池（`Battery`）
* 太阳能发电机（`SolarGenerator`）
* 风力发电机（`WindTurbine`）
* 水力发电机（`WatermillGenerator`）
* 火力发电机（`WoodFiredGenerator`）
* 化合燃料发电机（`ChemfuelPoweredGenerator`）
* 地热发电机（`GeothermalGenerator`）
* 电源开关（`PowerSwitch`）

### 3. 辅助生产、重型工业与加工设施（Production，共 9 件）
* 生物燃料提炼器（`BiofuelRefinery`）
* 水培无土栽培槽（`HydroponicsBasin`）
* 营养膏合成机（`NutrientPasteDispenser`）
* 漏斗（`Hopper`）
* 发酵桶（`FermentingBarrel`）
* 电火葬炉（`ElectricCrematorium`）
* 深钻机（`DeepDrill`）
* 屠宰点（`ButcherSpot`）、手工制作点（`CraftingSpot`）

### 4. 防卫与炮塔设施（Security，共 4 件）
* 迷你自动机枪塔（`Turret_MiniTurret`）
* 泡沫迫击炮/灭火炮塔（`Turret_FoamTurret`）
* 火箭巢发射器（`Turret_RocketswarmLauncher`）
* 尖刺陷阱（`TrapSpike`）

### 5. 飞船结构与终局设施（Ship，共 6 件）
* 飞船骨架横梁（`Ship_Beam`）
* 飞船电脑核心（`Ship_ComputerCore`）
* 飞船休眠舱（`Ship_CryptosleepCasket`）
* 飞船引擎（`Ship_Engine`）
* 飞船反应堆（`Ship_Reactor`）
* 飞船传感器阵列（`Ship_SensorCluster`）

### 6. 医疗、科研、温控与杂项设施（Misc & Temperature，共 18 件）
* **科研与勘探**：多元分析仪（`MultiAnalyzer`）、地质扫描仪（`GroundPenetratingScanner`）、远程矿物扫描仪（`LongRangeMineralScanner`）
* **通讯与轨道贸易**：通讯台（`CommsConsole`）、轨道贸易信标（`OrbitalTradeBeacon`）
* **医疗与生命体征**：生命体征监测仪（`VitalsMonitor`）、低温休眠舱（`CryptosleepCasket` / `AncientCryptosleepCasket`）
* **运输与物流**：空投舱发射台（`PodLauncher`）、运输舱（`TransportPod`）
* **安防与消防**：灭火泡发生器（`FirefoamPopper`）、抽水泵（`MoisturePump`）
* **墓葬与寝宿**：石棺（`Sarcophagus`）、坟墓（`Grave`）、动物睡眠箱（`AnimalSleepingBox`）、地面铺位（`SleepingSpot` / `DoubleSleepingSpot` / `AnimalSleepingSpot`）
* **温控**：营火（`Campfire`）、电加热器（`Heater`）、蒸发冷却器（`PassiveCooler`）、天然间歇泉口（`SteamGeyser`）
* **特种照明**：太阳灯（`SunLamp`）
