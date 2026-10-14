using System;
using Verse;

namespace RimFirstPerson3D
{
    public enum ViewMode
    {
        Off,        // 原版俯视
        FirstPerson,
        Chase
    }

    // 全局状态与常量。运行时可变项以 FirstPerson3DSettings 为真源（远程可控）。
    public static class Fps3DState
    {
        // 世界尺度：1 格 = 1 单位 = 1 米（三轴同构，依据见 Metrics3D.cs）。
        // 此处只做引用，不放魔数。
        public const float StoryHeight   = Metrics3D.CeilingHeight + FloorThickness;
        public const float WallHeight    = Metrics3D.CeilingHeight;
        public const float FovDeg        = 72f;
        public const float NearClip      = 0.08f;
        public const float FarClip       = 1200f;
        public const float SceneCullDist = 140f;
        public const float EyeHeight     = Metrics3D.HumanEyeHeight;
        public const float PawnBillboardHeight = Metrics3D.PawnHeight;

        /// <summary>楼板厚度。层高 = 室内净高 + 楼板，= 3.00。</summary>
        private const float FloorThickness = 0.30f;

        public static int ChunkRebuildBudget = 3;
        /// <summary>
        /// 每帧重刷顶点色的时间预算（毫秒）。按时间而不是块数：块与块的顶点数差几十倍，
        /// 按块数给预算在密草地里等于没有闸门（实机单块 1.6 万顶点，8 块/帧 = 帧时间 100 ms、fps 6）。
        /// 没刷到的块保持 colorDirty，下一帧接着刷；屏幕外的直接跳过。
        /// </summary>
        public static float ChunkColorBudgetMs = 2.5f;

        private static ViewMode modeInt = ViewMode.Off;

        public static ViewMode Mode
        {
            get
            {
                var s = FirstPerson3DMod.settings;
                if (s != null)
                {
                    var m = ParseMode(s.mode);
                    if (m != modeInt) modeInt = m;
                }
                return modeInt;
            }
            set
            {
                modeInt = value;
                var s = FirstPerson3DMod.settings;
                if (s != null) s.mode = value.ToString();
            }
        }

        // 三维只在局内有意义：标题界面/世界图里 Mode 可能仍留着玩家上次的选择，
        // 但那时 Active 必须为假，否则只查 Active 的原版绘制前缀会在局外把画面吃掉。
        public static bool Active => Current.ProgramState == ProgramState.Playing && Mode != ViewMode.Off;

        /// <summary>
        /// 只把当前会话退回俯视，不改写持久设置。
        /// 标题界面 / 世界图每帧都会走 Leave3D(forceOff)，以前那行会把玩家选定的
        /// 「下次仍以第一人称开局」抹成 Off —— 重开一局就得再按一次 F8。
        /// </summary>
        public static void ResetInMemory()
        {
            modeInt = ViewMode.Off;
        }

        public static bool DrawTerrain  { get => Get(s => s.drawTerrain, true);  set => Set(s => s.drawTerrain = value); }
        public static bool DrawPlants   { get => Get(s => s.drawPlants, true);   set => Set(s => s.drawPlants = value); }
        public static bool DrawItems    { get => Get(s => s.drawItems, true);    set => Set(s => s.drawItems = value); }
        public static bool DrawBuildings{ get => Get(s => s.drawBuildings, true);set => Set(s => s.drawBuildings = value); }
        public static bool DrawPawns    { get => Get(s => s.drawPawns, true);    set => Set(s => s.drawPawns = value); }
        public static bool DrawOutlines { get => Get(s => s.drawOutlines, true); set => Set(s => s.drawOutlines = value); }
        public static bool ShowPerfHud  { get => Get(s => s.showPerfHud, true);  set => Set(s => s.showPerfHud = value); }

        private static T Get<T>(Func<FirstPerson3DSettings, T> f, T fallback)
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return fallback;
            try { return f(s); } catch { return fallback; }
        }

        private static void Set(Action<FirstPerson3DSettings> f)
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            try { f(s); } catch { }
        }

        public static ViewMode ParseMode(string s)
        {
            if (string.Equals(s, "FirstPerson", StringComparison.OrdinalIgnoreCase)) return ViewMode.FirstPerson;
            if (string.Equals(s, "Chase", StringComparison.OrdinalIgnoreCase)) return ViewMode.Chase;
            return ViewMode.Off;
        }

        public static string Version => "0.2.0";
    }
}
