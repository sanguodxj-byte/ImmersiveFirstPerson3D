// AI生成
namespace RimFirstPerson3D
{
    // ============================================================
    // 世界尺度约定（本项目唯一的魔法常数来源）
    // ============================================================
    //
    // 【为什么需要这个文件】
    // RimWorld 原版是纯 2D 俯视，世界里根本没有高度轴（Y 只用于绘制分层）。
    // 所以「墙有多高」「人有多高」「桌子多高」在原版里全部无解。
    // 本 mod 要做 3D 自由视角，就必须自己约定一整套高度。
    //
    // 【唯一标尺：角色贴图的世界长宽】
    // 推导只用两样东西，且都不看原版 def：
    //   1. 角色贴图在世界里占的长宽（RimWorld 中 Pawn 恒占 1x1 格，贴图 128px铺满该格）
    //   2. 现实家具尺寸 + 现实人类身高
    //
    //   两个独立锚点必须给出同一答案：
    //     锚点 A（人）：1 格宽要容纳肩宽 0.45 m 的人 -> 1 格>= 0.45 m
    //     锚点 B（床）：床在游戏里是 1x2 格，对应现实 1.0 m x 2.0 m -> 1 格 = 1.00 m
    //   两者相容，故取整为：
    //
    //   >>> 1 格 = 1.00 米，X / Y / Z 三轴同构，无任何缩放<<<
    //
    // 【用法】
    //   所有高度一律写成「现实米数」，直接当格数用；需要换算时过Meters()。
    //   不要在业务代码里出现无出处的魔数（如 0.82、1.37），一律引用本文件的常量。
    //
    // 【验算图】docs/_height_scale.png（由 scripts/height_check.py 生成）
    // ============================================================

    public static class Metrics3D
    {
        // ---- 唯一常数 ----

        /// <summary>1 格（= 1 世界单位）等于多少米。X / Y / Z 三轴同构。</summary>
        public const float MetersPerCell = 1.00f;

        /// <summary>现实米 -> 格。世界坐标里就是直接相等，此函数仅作语义标注。</summary>
        public static float Meters(float m) => m / MetersPerCell;

        /// <summary>格 -> 现实米。仅用于显示/调试。</summary>
        public static float Cells(float cells) => cells * MetersPerCell;

        // ---- 人体（RimWorld 无数据，取现实人体工程学尺寸）----
        //
        // 只保留站姿。原版 RimWorld 没有"坐"这个概念——Pawn 要么站着，要么躺在
        // 床上睡觉，不存在坐姿动画与坐姿贴图。所以不设坐姿常量，避免凭空发明。

        public const float HumanHeight = 1.75f;      // 成年身高
        public const float HumanEyeHeight = 1.62f;   // 站立眼高（第一人称相机基准）
        public const float HumanShoulderWidth = 0.45f;

        public const float PawnHeight = 1.75f;       // Pawn 立牌高度（3D 化后）

        // ---- 座椅 ----
        //
        // 座面高度是家具自身的尺寸（现实中椅子确实有这个高度），不是"角色能坐"的意思。

        public const float ChairSeatHeight = 0.45f;      // 餐椅/扶手椅座面
        public const float ChairBackHeight = 0.90f;       // 靠背顶
        public const float ArmchairSeatHeight = 0.42f;
        public const float ArmchairBackHeight = 0.85f;
        public const float ArmchairArmHeight = 0.62f;    // 软包扶手顶
        public const float UpholsteryThickness = 0.16f;  // 软包板件（靠背/扶手）厚
        public const float SeatCushionThickness = 0.09f; // 坐垫/凳面厚（5cm 侧看像纸片）
        public const float BarHeight = 1.05f;            // 吧台面

        // ---- 桌 ----

        public const float TableHeight = 0.75f;          // 餐桌/工作台面
        public const float DeskHeight = 0.73f;
        public const float CoffeeTableHeight = 0.45f;    // 茶几
        public const float NightstandHeight = 0.55f;     // 床头柜

        // ---- 床（层级可叠加：床架 + 床垫 = 床垫面）----

        public const float BedFrameHeight = 0.40f;       // 床架上沿
        public const float MattressThickness = 0.15f;// 床垫厚
        public const float MattressSurface = BedFrameHeight + MattressThickness; // 0.55
        public const float BedHeadboardHeight = 1.05f;   // 床头板顶

        /// <summary>
        /// 躺睡的 Pawn 立牌底边Y 坐标。
        ///
        /// 【为什么原版没有这个值】原版 Pawn 躺床上时，sprite 是直接画在地面
        /// altitude 层上的（PawnRenderer.cs:414 的偏移全在 XZ 平面，不含 y），
        /// 所以原版**不存在"睡者高度"这个概念**。本 mod 要 3D 化，必须自己定。
        ///
        /// 【取值依据】睡者躺在床垫上，被褥/身体有厚度，不能与床垫面完全共面，
        /// 否则会z-fighting。所以比床垫面高一个"被褥厚度"。
        /// 现实里床垫上的被褥约 0.08m。定PawnLyingSurface = 0.63。
        /// </summary>
        public const float PawnLyingSurface = MattressSurface + 0.08f;   // 0.63

        /// <summary>
        /// 床卷（bedroll）是铺在地上的，没有床架，Pawn 直接躺地面。
        /// bed_showSleeperBody = false（实测，见 Buildings_Furniture.xml），
        /// 原版不画睡者身体，所以无���躺睡高度需要约定。
        /// </summary>
        public const float BedrollLyingSurface = 0.08f;

        // ---- 建筑 ----

        public const float CeilingHeight = 2.70f;        // 室内净层高
        public const float DoorHeight = 2.05f;
        public const float DoorWidth = 0.95f;
        /// <summary>门扇厚度（薄于墙，视觉上像门板嵌在墙洞里）。</summary>
        public const float DoorPanelThickness = 0.18f;
        /// <summary>门框立柱截面边长。</summary>
        public const float DoorPostWidth = 0.10f;
        public const float WindowSillHeight = 0.90f;     // 窗台
        public const float WindowHeight = 1.20f;
        public const float BaseboardHeight = 0.10f;      // 踢脚
        public const float RailingHeight = 1.05f;        // 栏杆/扶手
        public const float WallThickness = 1.00f;        // 墙体X/Z 厚度（满格）

        // ---- 移动与跳跃（F003 连续移动）----
        //
        // 第一人称连续位移的两个物理量的唯一来源。起跳竖直速度由二者反解：
        //   v0 = sqrt(2 * Gravity * JumpApex) = sqrt(2*20*1.0) ≈ 6.32 m/s，顶点恰为 JumpApex。
        // 墙高 2.70 m，跳不上去；没有二段跳。

        /// <summary>起跳能到达的离地最高点（米）。</summary>
        public const float JumpApex = 1.00f;
        /// <summary>重力加速度（米/秒²）。</summary>
        public const float Gravity = 20f;

        // ---- 零散物件 ----

        public const float TabletopItemHeight = 0.25f;   // 桌面小件
        public const float FloorItemHeight = 0.30f;      // 地面物件（堆叠）
        public const float WallItemHeight = 0.35f;       // 挂墙件

        // ---- 分类回退高度（无专属常量时用）----
        //
        // 归类顺序：建筑 > 家具 > 植物 > 物品。家具按 def.Size 的较大边插值，
        // 因为 1 格家具（桌）和 2 格家具（长桌、床）的高度并不线性相关。

        public const float FallbackBuildingHeight = CeilingHeight;
        public const float FallbackFurnitureHeight = 0.75f;
        public const float FallbackPlantHeight = 1.00f;
        public const float FallbackItemHeight = 0.25f;
        public const float FallbackPawnHeight = HumanHeight;

        // ---- 程序化模型尺寸（3D 建模用）----
        //
        // 这些值全部对应现实家具/设备的实际尺寸，用于 ModelLibrary3D 拼装基本体。
        // 纪律同前：不随手取整，每个值都要能指出对应的现实物件。

        /// <summary>通用板厚（桌面/搁板/箱体壁）。</summary>
        public const float SlabThickness = 0.05f;
        /// <summary>通用腿/柱截面边长（桌腿、椅腿、栏杆柱）。</summary>
        public const float LegThickness = 0.07f;

        /// <summary>工作台/操作台面高（厨房台面、手术台、屠宰台）。</summary>
        public const float CounterHeight = 0.95f;
        /// <summary>炉灶高度（含灶台面）。</summary>
        public const float StoveHeight = 0.95f;

        /// <summary>置物架总高与进深（货架类）。</summary>
        public const float ShelfHeight = 1.80f;
        public const float ShelfDepth = 0.45f;
        /// <summary>置物架层板数（含底板）。</summary>
        public const int ShelfTiers = 3;

        /// <summary>床头板厚度。</summary>
        public const float HeadboardThickness = 0.08f;
        /// <summary>床尾板高度。</summary>
        public const float FootboardHeight = 0.55f;

        /// <summary>炮塔：底座高 / 转塔体高 / 炮管长。</summary>
        public const float TurretBaseHeight = 0.55f;
        public const float TurretBodyHeight = 0.45f;
        public const float TurretBarrelLength = 0.90f;

        /// <summary>落地灯：灯杆高 / 灯罩高 / 灯罩直径。</summary>
        public const float LampPoleHeight = 1.55f;
        public const float LampShadeHeight = 0.26f;
        public const float LampShadeDiameter = 0.38f;
        /// <summary>火把/壁灯灯头离地高度。</summary>
        public const float TorchHeadHeight = 1.70f;

        /// <summary>木桶：高 / 直径（发酵桶、啤酒桶）。</summary>
        public const float BarrelHeight = 0.90f;
        public const float BarrelDiameter = 0.62f;
        /// <summary>料斗（hopper）高度。</summary>
        public const float HopperHeight = 1.00f;

        /// <summary>蓄电池高度（工业电池组外形）。</summary>
        public const float BatteryHeight = 0.90f;
        /// <summary>太阳能板倾角（度）与板厚。</summary>
        public const float SolarPanelTiltDeg = 35f;
        public const float SolarPanelThickness = 0.12f;
        /// <summary>太阳能板支架高（板最低点离地）。</summary>
        public const float SolarPanelLegHeight = 0.35f;

        /// <summary>栅栏总高 / 立柱截面 / 横档高度。</summary>
        public const float FenceHeight = 1.00f;
        public const float FencePostSize = 0.12f;
        /// <summary>沙袋掩体高度。</summary>
        public const float SandbagHeight = 0.55f;
        /// <summary>路障高度。</summary>
        public const float BarricadeHeight = 0.60f;

        /// <summary>坟墓：土堆高 / 墓碑高 / 墓碑厚。</summary>
        public const float GraveMoundHeight = 0.30f;
        public const float HeadstoneHeight = 0.70f;
        public const float HeadstoneThickness = 0.10f;

        /// <summary>花盆：盆高 / 盆口直径。</summary>
        public const float PlantPotHeight = 0.40f;
        public const float PlantPotDiameter = 0.50f;
        /// <summary>雕塑底座高。</summary>
        public const float SculptureBaseHeight = 0.60f;

        /// <summary>通讯台（含天线）总高。</summary>
        public const float CommsConsoleHeight = 1.20f;
        /// <summary>烟囱高 / 直径。</summary>
        public const float ChimneyHeight = 0.60f;
        public const float ChimneyDiameter = 0.25f;

        // ---- 垂直交通（Y 轴作为真实高度轴）----

        /// <summary>楼梯：单级踏步高度（踢面）。</summary>
        public const float StairRiser = 0.18f;
        /// <summary>楼梯：单级踏步进深（踏面）。</summary>
        public const float StairTread = 0.28f;
        /// <summary>楼梯宽度。</summary>
        public const float StairWidth = 1.00f;
        /// <summary>坡道坡度（度），用于地形高差处的可行走斜面。</summary>
        public const float RampSlopeDeg = 30f;

        // ---- 描边 ----

        /// <summary>描边壳外扩宽度（米）。</summary>
        public const float OutlineWidth = 0.035f;

        // ---- 水体（3D 化后水有真实深度；取现实涉水深度）----

        /// <summary>浅岸：与陆地相邻的一圈浅水，脚踝深。</summary>
        public const float WaterDepthShore = 0.20f;
        /// <summary>浅水：原版 WaterShallow，可涉水，约齐膝。</summary>
        public const float WaterDepthShallow = 0.35f;
        /// <summary>齐胸深水：原版 WaterMovingChestDeep。</summary>
        public const float WaterDepthChest = 1.10f;
        /// <summary>深水：原版 WaterDeep / WaterOceanDeep，不可通行。</summary>
        public const float WaterDepthDeep = 1.60f;
        /// <summary>水面高出地面基准的量（避免与岸边地面 z-fighting）。</summary>
        public const float WaterSurfaceLift = 0.02f;

        // ---- 动物 ----

        /// <summary>原版动物贴图四周留白约 28%，有效体长 = drawSize × 此系数。</summary>
        public const float AnimalTextureFill = 0.72f;
    }
}
