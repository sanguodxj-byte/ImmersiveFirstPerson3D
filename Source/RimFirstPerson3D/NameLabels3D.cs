using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 3D 模式角色名牌：原版 GenMapUI.LabelDrawPosFor 按俯视相机投影
    //（锚在 DrawPos 脚底、z 向北偏 0.6 格），第一人称下全部悬空错位，
    // 已随 ThingOverlays 一并屏蔽；这里按各图水平偏移 + 楼层基准 + 头顶高度
    // 把名字投到对应角色头顶。文字与底框沿用原版 GenMapUI.DrawPawnLabel。
    public static class NameLabels3D
    {
        private const float MaxDist = 70f;   // 名牌可见距离（米）

        public static void Draw()
        {
            var cam = CameraRig3D.Cam;
            var st = FirstPerson3DMod.settings;
            if (cam == null || st == null || Find.CurrentMap == null) return;
            float headTop = Mathf.Max(0.5f, st.pawnHeight);
            var camPos = cam.transform.position;
            var maps = Streaming3D.RenderMaps(Find.CurrentMap);
            foreach (var map in maps)
            {
                if (map == null || map.Disposed) continue;
                var offset = Streaming3D.OffsetOf(map);
                float baseY = LevelStack3D.LevelBaseY(map);
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p == null || p.Dead || !p.RaceProps.Humanlike) continue;
                    if (map.fogGrid.IsFogged(p.Position)) continue;
                    var head = new Vector3(
                        p.DrawPos.x + offset.x,
                        baseY + headTop + 0.25f,
                        p.DrawPos.z + offset.z);
                    if (Vector3.Distance(camPos, head) > MaxDist) continue;
                    Vector3 sp = cam.WorldToScreenPoint(head);
                    if (sp.z <= 0.1f) continue;   // 背后
                    var ui = new Vector2(sp.x / Prefs.UIScale, UI.screenHeight - sp.y / Prefs.UIScale);
                    GenMapUI.DrawPawnLabel(p, ui);
                }
            }
        }
    }
}
