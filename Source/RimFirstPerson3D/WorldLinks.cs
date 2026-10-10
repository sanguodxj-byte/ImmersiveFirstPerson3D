using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 存档桥接：把 LevelStack3D / Streaming3D 的静态映射转成可序列化的 ID 数据，
    // 并在读档后按 uniqueID 解析回 Map 对象。
    // 组件数据先于 maps 读入（见 Verse.Game.LoadGame：ExposeSmallComponents 在地图之前），
    // 所以读档时先暂存纯数据，等 LoadedGame 阶段地图已在 Find.Maps 里再落地。
    public static class WorldLinks
    {
        private static bool hasPending;
        private static List<int> pendingStackIds;
        private static List<int> pendingGroupIds;
        private static List<int> pendingStreamedIds;
        private static List<float> pendingOffsetsX;
        private static List<float> pendingOffsetsZ;
        private static int pendingHomeId = -1;

        // ---- 保存：静态状态 -> 纯数据 ----

        public static void CaptureSaveData(
            List<int> stackIds, List<int> groupIds,
            List<int> streamedIds, List<float> offX, List<float> offZ, ref int homeId)
        {
            stackIds.Clear();
            groupIds.Clear();
            streamedIds.Clear();
            offX.Clear();
            offZ.Clear();

            LevelStack3D.CaptureSaveData(stackIds, groupIds);
            Streaming3D.CaptureSaveData(streamedIds, offX, offZ, ref homeId);
        }

        // ---- 读档：暂存数据，等地图就绪 ----

        public static void PendingRestore(
            List<int> stackIds, List<int> groupIds,
            List<int> streamedIds, List<float> offX, List<float> offZ, int homeId)
        {
            pendingStackIds = stackIds;
            pendingGroupIds = groupIds;
            pendingStreamedIds = streamedIds;
            pendingOffsetsX = offX;
            pendingOffsetsZ = offZ;
            pendingHomeId = homeId;
            hasPending = stackIds != null || streamedIds != null;
        }

        public static void ApplyRestore()
        {
            if (!hasPending) return;
            hasPending = false;
            // 读档后静态映射可能残留上一局的 Map 引用，先整体清空再按 ID 重建。
            LevelStack3D.ResetAll();
            Streaming3D.ResetAll();
            try
            {
                LevelStack3D.RestoreFromSaveData(pendingStackIds, pendingGroupIds, Find.Maps);
                Streaming3D.RestoreFromSaveData(
                    pendingStreamedIds, pendingOffsetsX, pendingOffsetsZ, pendingHomeId, Find.Maps);
            }
            catch (System.Exception e)
            {
                Log.Warning(ModEntry.LogPrefix + "world link restore failed: " + e);
            }
            pendingStackIds = null;
            pendingGroupIds = null;
            pendingStreamedIds = null;
            pendingOffsetsX = null;
            pendingOffsetsZ = null;
            pendingHomeId = -1;
        }

        // 新开局没有待恢复数据：清掉可能的上一局残留，避免引用已销毁地图。
        public static void ClearIfUnrestored()
        {
            if (hasPending) return;
            LevelStack3D.ResetAll();
            Streaming3D.ResetAll();
        }
    }
}
