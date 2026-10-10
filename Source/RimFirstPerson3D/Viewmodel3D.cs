// AI生成
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 第一人称手持视图模型：
    //  空手 -> 由 Hand3D 画右下角"手"，左键向前挥拳；
    //  持械 -> 隐藏手，在镜头右下斜置武器贴图（取材武器 def 的原版图标），
    //          斜对着玩家摄像头摆放，左键挥砍/射击时前推并旋转。
    // 视图模型全部画在 GUI 覆盖层（逻辑坐标空间），挥击状态由本类统一驱动。
    public static class Viewmodel3D
    {
        private const float SwingDur = 0.26f; // 单次挥击时长（秒）
        private static float attackT = -1f;   // <0 = 静止

        // 附身者当前是否持械（装备栏有主武器）。
        public static bool Armed
        {
            get
            {
                var pawn = CameraRig3D.Possessed;
                return pawn != null && pawn.equipment != null && pawn.equipment.Primary != null;
            }
        }

        // 左键/攻击触发一次挥击动画（空手挥拳与持械挥砍/射击后座共用）。
        public static void TriggerAttack() { attackT = 0f; }

        public static void Tick(float dt)
        {
            if (attackT >= 0f)
            {
                attackT += dt;
                if (attackT > SwingDur) attackT = -1f;
            }
        }

        // 0=静止 1=挥到底。
        public static float SwingPhase => attackT < 0f ? 0f : Mathf.Clamp01(attackT / SwingDur);

        // 挥击平移量（屏幕像素，logical 空间）：先快速向左上"前推"再回落。
        public static Vector2 SwingOffset(float amp)
        {
            float p = SwingPhase;
            if (p <= 0f) return Vector2.zero;
            float k = Mathf.Sin(p * Mathf.PI);
            return new Vector2(-amp * k, -amp * 0.45f * k);
        }

        // 挥击附加旋转角（度）。
        public static float SwingAngle(float amp)
        {
            float p = SwingPhase;
            if (p <= 0f) return 0f;
            return -amp * Mathf.Sin(p * Mathf.PI);
        }

        // 持械视图：镜头右下角斜置武器图标，随挥击前推 + 旋转。
        public static void DrawWeapon()
        {
            if (!Armed) return;
            var pawn = CameraRig3D.Possessed;
            var eq = pawn != null && pawn.equipment != null ? pawn.equipment.Primary : null;
            if (eq == null || eq.def == null) return;

            float size = UI.screenHeight * 0.40f;
            float off = SwingPhase > 0f ? Mathf.Sin(SwingPhase * Mathf.PI) : 0f;
            float x = UI.screenWidth - size * 0.74f - off * size * 0.20f;
            float y = UI.screenHeight - size * 0.76f - off * size * 0.12f;
            var r = new Rect(x, y, size, size);

            var tex = eq.def.uiIcon;
            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            // 斜对着相机：默认 -34°，挥击时再转一点，像端枪/劈砍。
            float ang = -34f + SwingAngle(18f);
            GUIUtility.RotateAroundPivot(ang, new Vector2(r.center.x, r.yMax));
            GUI.color = Color.white;
            if (tex != null)
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
            else
                Widgets.ThingIcon(r, eq);
            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }
    }
}