using System.Collections.Generic;
using Verse;

namespace RimFirstPerson3D
{
    // 多层栈与流式世界偏移的存档持久化。
    // 静态映射无法随存档保存，这里只持久化地图 uniqueID（由 Map.ExposeData 保留）
    // 与偏移量，读档后在 LoadedGame 里按 ID 解析回真实的 Map 对象。
    public class WorldLinkComponent : GameComponent
    {
        private List<int> stackIds = new List<int>();
        private List<int> groupIds = new List<int>();
        private List<int> streamedIds = new List<int>();
        private List<float> streamedOffsetsX = new List<float>();
        private List<float> streamedOffsetsZ = new List<float>();
        private int homeId = -1;

        public WorldLinkComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // 保存前把当前静态状态抽成纯数据，避免直接序列化 Map/Vector3。
                WorldLinks.CaptureSaveData(
                    stackIds, groupIds, streamedIds, streamedOffsetsX, streamedOffsetsZ, ref homeId);
            }
            Scribe_Collections.Look(ref stackIds, "fp3dStackIds", LookMode.Value);
            Scribe_Collections.Look(ref groupIds, "fp3dGroupIds", LookMode.Value);
            Scribe_Collections.Look(ref streamedIds, "fp3dStreamedIds", LookMode.Value);
            Scribe_Collections.Look(ref streamedOffsetsX, "fp3dStreamedOffsetsX", LookMode.Value);
            Scribe_Collections.Look(ref streamedOffsetsZ, "fp3dStreamedOffsetsZ", LookMode.Value);
            Scribe_Values.Look(ref homeId, "fp3dHomeId", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                WorldLinks.PendingRestore(
                    stackIds, groupIds, streamedIds, streamedOffsetsX, streamedOffsetsZ, homeId);
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            WorldLinks.ApplyRestore();
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // 新开局没有待恢复数据，这里只清理静态残留，避免上一局的地图引用穿场。
            WorldLinks.ClearIfUnrestored();
        }
    }
}
