using System;

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
        public const float FarClip       = 140f;
        public const float EyeHeight     = Metrics3D.HumanEyeHeight;
        public const float PawnBillboardHeight = Metrics3D.PawnHeight;

        /// <summary>楼板厚度。层高 = 室内净高 + 楼板，= 3.00。</summary>
        private const float FloorThickness = 0.30f;

        public static int ChunkRebuildBudget = 3;

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

        public static bool Active => Mode != ViewMode.Off;

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
