// AI生成
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 第一人称"手"：当前暂停绘制。
    // 老板 22:44 否定"自造美术"（我自己用代码画的像素手被评"鬼图"），
    // 故撤下自造贴图，不再往画面上贴自画的手。
    // 后续方向待确认：①取样原版素材作手贴图；②或保持不显示手，仅保留挥击/持械的动作反馈。
    public static class Hand3D
    {
        public static void Draw()
        {
            // 暂停：不自造美术，不绘制手贴图。
        }
    }
}
