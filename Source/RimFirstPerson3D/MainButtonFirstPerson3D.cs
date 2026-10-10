using RimWorld;
using Verse;

namespace RimFirstPerson3D
{
    // 右下角功能按钮区的「第一人称」按钮。
    // 走原版 MainButtonDef 体系（与 研究/任务/世界 同一排），不自绘、不自造图标；
    // 点击即在 第一人称 / 关闭 之间切换。
    public class MainButtonWorker_FirstPerson3D : MainButtonWorker
    {
        public override bool Disabled
        {
            get
            {
                // 无地图（如世界界面）时不可用，与其它地图相关按钮一致。
                if (Find.CurrentMap == null) return true;
                return base.Disabled;
            }
        }

        public override void Activate()
        {
            Fps3DState.Mode = Fps3DState.Active ? ViewMode.Off : ViewMode.FirstPerson;
            Log.Message(ModEntry.LogPrefix + "mode -> " + Fps3DState.Mode);
        }

        // 按钮处于激活态时点亮进度条，用于标识当前已在第一人称。
        public override float ButtonBarPercent => Fps3DState.Active ? 1f : 0f;
    }
}
