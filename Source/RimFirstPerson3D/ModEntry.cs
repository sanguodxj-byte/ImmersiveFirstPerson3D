// AI生成
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 模组入口：装配 Harmony 补丁并创建常驻驱动对象。
    [StaticConstructorOnStartup]
    public static class ModEntry
    {
        public const string HarmonyId = "sanguo.firstperson3d";
        public const string LogPrefix = "[FirstPerson3D] ";

        static ModEntry()
        {
            var harmony = new Harmony(HarmonyId);
            VanillaSuppressor.Patch(harmony);
            CommandPanel3D.Patch(harmony);
            PanelUi3D.Patch(harmony);
            GodView3D.Patch(harmony);
            ChangeNotify.Patch(harmony);
            HitFx3D.Patch(harmony);
            ShotFx3D.Patch(harmony);

            var go = new GameObject("FirstPerson3D_Root");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Fps3DRoot>();

            Log.Message(LogPrefix + "initialized (r58)");
        }
    }
}
