using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 远程控制面板：字段经 RimBridge get/update_mod_settings 读写，
    // 是 GABS 实机调参与性能回读的正式通道。
    public class FirstPerson3DSettings : ModSettings
    {
        public string mode = "Off";
        public bool drawTerrain = true;
        public bool drawPlants = true;
        public bool drawItems = true;
        public bool drawBuildings = true;
        public bool drawPawns = true;
        public bool drawOutlines = true;
        public bool showPerfHud = true;
        public float camYaw = 90f;
        public float camPitch = 10f;
        public float camX = 0f;
        public float camY = 0f;
        public float camZ = 0f;
        public bool freeCam = false;
        public string possessName = "";
        public string perfSnapshot = "";

        // ---- 鼠标自由视角 ----
        // 开启后进入 3D 即锁定光标并直接用鼠标环视（无需按住右键）；
        // 任何原版窗口打开或按解除键时自动解锁，保证 UI 输入不被阻断。
        public bool mouseLook = true;
        // 光标被窗口/暂停等条件自动解锁后，需手动点回画面才恢复；此标志记录当前是否处于锁定态。
        public bool mouseLookLatched = false;
        // 环视灵敏度（度/像素）。
        public float lookSensitivity = 0.25f;

        // ---- 准星 ----
        // 第一人称准星：实心填充绘制（中心块 + 四向实心臂），不接收输入。
        public bool showCrosshair = true;
        public float crosshairR = 0.95f;
        public float crosshairG = 0.98f;
        public float crosshairB = 1.0f;
        // 准星整体缩放（1.0 为默认大小）。
        public float crosshairScale = 1.0f;

        // ---- 合成输入通道（GABS 远程测试操作逻辑用；消费后归零，不持久化）----
        public float cmdLookDx = 0f;        // 环视偏航增量（度）
        public float cmdLookDy = 0f;        // 环视俯仰增量（度）
        public string cmdKey = "";          // 单次移动指令："W"/"A"/"S"/"D"
        public string cmdClick = "";        // 非空=触发一次准星点击
        public string cmdAction = "";       // 动作指令："createUpper"/"levelUp"/"levelDown"/"streamEast"/"unloadStreamed"
        public string cmdTeleport = "";     // "x,z"：传送附身 pawn 到当前图该格（消费后归零）
        public float cmdWalkHold = 0f;      // 持续合成 W 按住的秒数（跨图行走测试用）
        public string diagCross = "";       // 跨图诊断实时状态（TryCrossBoundary 回写）

        // ---- 视觉调优参数（全部实时生效；带* 的需触发对应层重建，由根驱动自动检测）----
        // 注：物理高度类默认值一律引用 Metrics3D，本类不另立魔数。
        public float fov = 72f;             // 透视视场角
        public float pawnHeight = Metrics3D.PawnHeight;   // 人形立牌总高（米/格）
        public float pawnWidthScale = 0.72f;// 立牌宽 = 高 * 此值
        public float headScale = 0.34f;     // 头部面片尺寸 = 身高 * 此值
        public float headYFrac = 0.78f;     // 头部中心高度 = 身高 * 此值
        public float wallVScale = 1.0f;    // *墙体侧面贴图纵向密度
        public float wallOutline = 0.05f;   // *描边壳外扩量（米）
        public float skyR = 0.45f;          // 天空色
        public float skyG = 0.62f;
        public float skyB = 0.85f;
        public bool fogOn = false;          // 线性雾
        public float fogEnd = 95f;          // 雾完全距离（米）
        public float plantScale = 1.0f;     // *植物高度缩放
        // 描边壳外扩宽度（米）。参考 Metrics3D.OutlineWidth。
        public float outlineWidth = Metrics3D.OutlineWidth;
        // ---- 环境与生物（SkyScenery / Water3D / Grass3D / PlantLayer3D / Animal3D）----
        public bool drawWater = true;       // 真实深度水体（水底/岸壁/半透明水面）
        public bool drawGrass = true;       // 相机周围地被草丛
        public float grassRadius = 28f;     // 草丛散布半径（格）
        public float windStrength = 1.0f;   // 草/植物随风摇摆强度（0 关闭）
        public bool animalModels = true;    // 动物用程序化 3D 模型（关闭则退回立牌）

        // ---- 光照（F013，规范见 docs/features/F013-lighting.md）----
        // 关闭 lightOn 后顶点色不再写，画面应与光照落地前完全一致（AC-1 的反证）。
        public bool lightOn = true;         // 光照总开关
        public bool lightShadows = true;    // 体素向阳投影（射线）
        public bool lightAo = true;         // 顶点接触阴影
        public bool lightGlow = true;       // 灯具可见发光片
        public float lightFloor = 0.16f;    // 无任何光时的可辨认下限
        public float lightSun = 1.0f;       // 直射项增益
        public float lightLamp = 1.0f;      // 灯具项增益（数值本身来自原版 glowGrid）
        public float lightAoStrength = 0.45f; // 接触阴影深度
        public float lightSoftFoot = -0.10f;  // 向阳项软化区间下沿（与上沿相等=硬分档）
        public float lightSoftTop = 0.35f;    // 向阳项软化区间上沿
        public float lightAmbTint = 0.55f;  // 环境光按天空色相染色的强度
        public float lightSweepRows = 6f;   // 光照场每帧扫描的行数（分摊重建压力）
        public float lightQuadStep = 2f;    // 受光几何的最大面片边长（米）
        public string lightSnapshot = "";   // 光照导出 JSON，供 GABS 回读与机检
        public bool lightFileDump = false;  // 每 2 s 把光照导出追加进 persistentDataPath/FirstPerson3D_light.log
        public bool lightProbe = false;     // 载体探针：顶点色一律烘成常数灰，验着色器吃不吃顶点色

        // ---- 受击反馈（F014，规范见 docs/features/F014-hit-feedback.md）----
        // 关掉后既不给生物罩红闪、也不画玩家暗角，画面应与落地前完全一致（判据的反证）。
        public bool hitFx = true;           // 总开关
        public float hitFxScale = 1.0f;     // 浓度倍率（0.2..2），只缩放不改变曲线形状

        // ---- 武器射击效果（F015，规范见 docs/features/F015-shot-effects.md）----
        // 关掉后弹丸、拖焰、枪口火光、落点烟尘全不画（原版绘制本来就被压掉，等于子弹不可见）。
        public bool shotFx = true;          // 总开关
        public float shotFxScale = 1.0f;    // 浓度/长度倍率，只缩放不改曲线

        // ---- 光影包级画面（F016，规范见 docs/features/F016-shaderpack-look.md）----
        // shaderQuality: off / low / medium / high / ultra。各层开关由档位决定，下面的单项可覆盖。
        public string shaderQuality = "medium";
        public bool shaderAutoDowngrade = true;   // p95 超预算连续 5 s 自动逐层降级
        public string renderCaps = "";            // P0 引擎能力探针结果（JSON），probe 动作写入
        public int capsMainCamRT = -1;            // -1 未测 / 0 不可用 / 1 可用（持久化，档位门控用）
        public int capsBlitBilinear = -1;
        public int capsEngineFog = -1;
        public int capsOblique = -1;
        // L1 胶片色调
        public bool lightToneMap = true;
        public bool lightKelvinSun = true;
        public float lightExposure = ShaderpackDefaults.Exposure;
        public float lightWhitePoint = ShaderpackDefaults.WhitePoint;
        public float lightHighlightDesat = ShaderpackDefaults.HighlightDesat;
        public float lightSplitTone = ShaderpackDefaults.SplitTone;
        public float lightSaturation = ShaderpackDefaults.Saturation;
        public float lightNightDesat = ShaderpackDefaults.NightDesat;
        // L3 投影
        public float shadowAlpha = ShaderpackDefaults.ShadowAlpha;
        // L4 泛光
        public float bloomIntensity = ShaderpackDefaults.BloomIntensity;
        // L5 大气
        public float fogDensity = ShaderpackDefaults.FogDensity;
        public float shaftGain = ShaderpackDefaults.ShaftGain;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", "Off");
            Scribe_Values.Look(ref drawTerrain, "drawTerrain", true);
            Scribe_Values.Look(ref drawPlants, "drawPlants", true);
            Scribe_Values.Look(ref drawItems, "drawItems", true);
            Scribe_Values.Look(ref drawBuildings, "drawBuildings", true);
            Scribe_Values.Look(ref drawPawns, "drawPawns", true);
            Scribe_Values.Look(ref drawOutlines, "drawOutlines", true);
            Scribe_Values.Look(ref showPerfHud, "showPerfHud", true);
            Scribe_Values.Look(ref camYaw, "camYaw", 90f);
            Scribe_Values.Look(ref camPitch, "camPitch", 10f);
            Scribe_Values.Look(ref camX, "camX", 0f);
            Scribe_Values.Look(ref camY, "camY", 0f);
            Scribe_Values.Look(ref camZ, "camZ", 0f);
            Scribe_Values.Look(ref freeCam, "freeCam", false);
            Scribe_Values.Look(ref mouseLook, "mouseLook", true);
            Scribe_Values.Look(ref lookSensitivity, "lookSensitivity", 0.25f);
            Scribe_Values.Look(ref showCrosshair, "showCrosshair", true);
            Scribe_Values.Look(ref crosshairR, "crosshairR", 0.95f);
            Scribe_Values.Look(ref crosshairG, "crosshairG", 0.98f);
            Scribe_Values.Look(ref crosshairB, "crosshairB", 1.0f);
            Scribe_Values.Look(ref crosshairScale, "crosshairScale", 1.0f);
            // mouseLookLatched 是运行时状态，不持久化（读档后可安全重新判定）。
            Scribe_Values.Look(ref fov, "fov", 72f);
            Scribe_Values.Look(ref pawnHeight, "pawnHeight", Metrics3D.PawnHeight);
            Scribe_Values.Look(ref pawnWidthScale, "pawnWidthScale", 0.72f);
            Scribe_Values.Look(ref headScale, "headScale", 0.34f);
            Scribe_Values.Look(ref headYFrac, "headYFrac", 0.78f);
            Scribe_Values.Look(ref wallVScale, "wallVScale", 1.0f);
            Scribe_Values.Look(ref wallOutline, "wallOutline", 0.05f);
            Scribe_Values.Look(ref skyR, "skyR", 0.45f);
            Scribe_Values.Look(ref skyG, "skyG", 0.62f);
            Scribe_Values.Look(ref skyB, "skyB", 0.85f);
            Scribe_Values.Look(ref fogOn, "fogOn", false);
            Scribe_Values.Look(ref fogEnd, "fogEnd", 95f);
            Scribe_Values.Look(ref plantScale, "plantScale", 1.0f);
            Scribe_Values.Look(ref outlineWidth, "outlineWidth", Metrics3D.OutlineWidth);
            Scribe_Values.Look(ref drawWater, "drawWater", true);
            Scribe_Values.Look(ref drawGrass, "drawGrass", true);
            Scribe_Values.Look(ref grassRadius, "grassRadius", 28f);
            Scribe_Values.Look(ref windStrength, "windStrength", 1.0f);
            Scribe_Values.Look(ref animalModels, "animalModels", true);
            Scribe_Values.Look(ref lightOn, "lightOn", true);
            Scribe_Values.Look(ref lightShadows, "lightShadows", true);
            Scribe_Values.Look(ref lightAo, "lightAo", true);
            Scribe_Values.Look(ref lightGlow, "lightGlow", true);
            Scribe_Values.Look(ref lightFloor, "lightFloor", 0.16f);
            Scribe_Values.Look(ref lightSun, "lightSun", 1.0f);
            Scribe_Values.Look(ref lightLamp, "lightLamp", 1.0f);
            Scribe_Values.Look(ref lightAoStrength, "lightAoStrength", 0.45f);
            Scribe_Values.Look(ref lightSoftFoot, "lightSoftFoot", -0.10f);
            Scribe_Values.Look(ref lightSoftTop, "lightSoftTop", 0.35f);
            Scribe_Values.Look(ref lightAmbTint, "lightAmbTint", 0.55f);
            Scribe_Values.Look(ref lightSweepRows, "lightSweepRows", 6f);
            Scribe_Values.Look(ref lightQuadStep, "lightQuadStep", 2f);
            Scribe_Values.Look(ref lightFileDump, "lightFileDump", false);
            Scribe_Values.Look(ref lightProbe, "lightProbe", false);
            Scribe_Values.Look(ref hitFx, "hitFx", true);
            Scribe_Values.Look(ref hitFxScale, "hitFxScale", 1.0f);
            Scribe_Values.Look(ref shotFx, "shotFx", true);
            Scribe_Values.Look(ref shotFxScale, "shotFxScale", 1.0f);
            Scribe_Values.Look(ref shaderQuality, "shaderQuality", "medium");
            Scribe_Values.Look(ref shaderAutoDowngrade, "shaderAutoDowngrade", true);
            Scribe_Values.Look(ref capsMainCamRT, "capsMainCamRT", -1);
            Scribe_Values.Look(ref capsBlitBilinear, "capsBlitBilinear", -1);
            Scribe_Values.Look(ref capsEngineFog, "capsEngineFog", -1);
            Scribe_Values.Look(ref capsOblique, "capsOblique", -1);
            Scribe_Values.Look(ref lightToneMap, "lightToneMap", true);
            Scribe_Values.Look(ref lightKelvinSun, "lightKelvinSun", true);
            Scribe_Values.Look(ref lightExposure, "lightExposure", ShaderpackDefaults.Exposure);
            Scribe_Values.Look(ref lightWhitePoint, "lightWhitePoint", ShaderpackDefaults.WhitePoint);
            Scribe_Values.Look(ref lightHighlightDesat, "lightHighlightDesat", ShaderpackDefaults.HighlightDesat);
            Scribe_Values.Look(ref lightSplitTone, "lightSplitTone", ShaderpackDefaults.SplitTone);
            Scribe_Values.Look(ref lightSaturation, "lightSaturation", ShaderpackDefaults.Saturation);
            Scribe_Values.Look(ref lightNightDesat, "lightNightDesat", ShaderpackDefaults.NightDesat);
            Scribe_Values.Look(ref shadowAlpha, "shadowAlpha", ShaderpackDefaults.ShadowAlpha);
            Scribe_Values.Look(ref bloomIntensity, "bloomIntensity", ShaderpackDefaults.BloomIntensity);
            Scribe_Values.Look(ref fogDensity, "fogDensity", ShaderpackDefaults.FogDensity);
            Scribe_Values.Look(ref shaftGain, "shaftGain", ShaderpackDefaults.ShaftGain);
            FirstPerson3DMod.settings = this;
            base.ExposeData();
        }
    }

    public class FirstPerson3DMod : Mod
    {
        public static FirstPerson3DSettings settings;

        public FirstPerson3DMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<FirstPerson3DSettings>();
        }
    }
}
