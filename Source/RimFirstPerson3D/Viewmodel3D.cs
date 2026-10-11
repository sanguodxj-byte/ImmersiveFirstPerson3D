// AI生成
using System.Collections.Generic;
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

        // 持械视图：枪口钉在准星（屏幕中心 = 透视灭点 = RaycastCenter 那条线）上，
        // 枪身沿「右下 -> 中心」这条轴斜出去。原版武器图标是枪口朝右的侧视 sprite，
        // 直接把它旋到朝左上会连「枪顶朝上」一起翻掉（弹匣朝天），所以走一次镜像：
        // 线性部分是反射而不是旋转，这一点是这套摆放的核心。
        private const float WeaponLenFrac = 0.62f;   // 图标宽度占屏高的比例
        private const float BarrelTiltDeg = 30f;     // 枪管轴与水平线的夹角

        // 图标里量出来的枪口位置与枪管轴：(u, v, ex, ey)，归一化到图标、y 轴向下（GUI 口径）。
        private static readonly Dictionary<Texture2D, Vector4> muzzleCache = new Dictionary<Texture2D, Vector4>();

        /// <summary>
        /// 按 alpha 扫出枪口：不透明外框的最右列、该列实像素的纵向中心；
        /// 轴取「外框中心 -> 枪口」，所以图标里枪管斜着画也不会摆歪。
        /// 贴图不可读时退回「右侧居中、水平枪管」——原版武器图标就是这个形状。
        /// </summary>
        private static Vector4 MuzzleOf(Texture2D tex)
        {
            if (tex == null) return new Vector4(1f, 0.5f, 1f, 0f);
            if (muzzleCache.TryGetValue(tex, out var cached)) return cached;
            var res = new Vector4(1f, 0.5f, 1f, 0f);
            try
            {
                if (tex.isReadable)
                {
                    int w = tex.width, h = tex.height;
                    var px = tex.GetPixels32();
                    int x0 = w, x1 = -1, y0 = h, y1 = -1;
                    int rx0 = w, rx1 = -1;      // 最右列的纵向范围（GUI y 向下）
                    for (int r = 0; r < h; r++)
                    {
                        int gy = h - 1 - r;
                        int row = r * w;
                        for (int c = 0; c < w; c++)
                        {
                            if (px[row + c].a <= 24) continue;
                            if (c < x0) x0 = c;
                            if (c > x1) x1 = c;
                            if (gy < y0) y0 = gy;
                            if (gy > y1) y1 = gy;
                            if (c == x1) { if (gy < rx0) rx0 = gy; if (gy > rx1) rx1 = gy; }
                        }
                    }
                    if (x1 > x0 && y1 > y0)
                    {
                        if (rx1 < rx0) { rx0 = y0; rx1 = y1; }     // 最右列只剩一像素时的兜底
                        float mx = x1 + 0.5f, my = (rx0 + rx1) * 0.5f + 0.5f;
                        float ax = mx - (x0 + x1 + 1) * 0.5f, ay = my - (y0 + y1) * 0.5f;
                        float el = Mathf.Sqrt(ax * ax + ay * ay);
                        if (el < 0.0001f) { ax = 1f; ay = 0f; el = 1f; }
                        res = new Vector4(Mathf.Clamp01(mx / w), Mathf.Clamp01(my / h), ax / el, ay / el);
                    }
                }
            }
            catch { res = new Vector4(1f, 0.5f, 1f, 0f); }
            muzzleCache[tex] = res;
            return res;
        }

        public static void DrawWeapon()
        {
            if (!Armed) return;
            var pawn = CameraRig3D.Possessed;
            var eq = pawn != null && pawn.equipment != null ? pawn.equipment.Primary : null;
            if (eq == null || eq.def == null) return;

            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            GUI.color = SampleHandLight(pawn);

            var tex = eq.def.uiIcon;
            if (tex == null || tex.width <= 0)
            {
                float s0 = UI.screenHeight * 0.30f;
                Widgets.ThingIcon(new Rect(UI.screenWidth * 0.5f - s0 * 0.5f,
                    UI.screenHeight * 0.5f - s0 * 0.5f, s0, s0), eq);
                GUI.color = prevColor;
                GUI.matrix = prevMatrix;
                return;
            }

            float W = UI.screenHeight * WeaponLenFrac;
            float Hs = W * tex.height / (float)tex.width;
            var muz = MuzzleOf(tex);
            float off = SwingPhase > 0f ? Mathf.Sin(SwingPhase * Mathf.PI) : 0f;

            // 期望轴：枪口在准星，枪尾在右下 —— butt->muzzle 的方向是指向左上 30°。
            // GUI 是 y 轴向下的屏幕系，所以「左上」= 210°，不是 150°（150° 是左下，摆出来枪会翘到准星上方）。
            float a = (180f + BarrelTiltDeg + SwingAngle(8f)) * Mathf.Deg2Rad;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            var nrm = new Vector2(d.y, -d.x);        // 正交且屏幕 y 分量为正：图标「向下」的那一侧
            // 后坐沿轴往回抽，枪口只留一点点位移，视觉上仍然瞄着准星。
            var anchor = new Vector2(UI.screenWidth * 0.5f, UI.screenHeight * 0.5f) - d * (off * W * 0.08f);

            // 图标自己的轴 (ex,ey) 映到 d：T = [d nrm] · E⁻¹，E 是图标系的正交基。
            float ex = muz.z, ey = muz.w;
            float t00 = d.x * ex - nrm.x * ey, t01 = d.x * ey + nrm.x * ex;
            float t10 = d.y * ex - nrm.y * ey, t11 = d.y * ey + nrm.y * ex;
            var m = new Vector2(muz.x * W, muz.y * Hs);
            GUI.matrix = new Matrix4x4(
                new Vector4(t00, t10, 0f, 0f),
                new Vector4(t01, t11, 0f, 0f),
                new Vector4(0f, 0f, 1f, 0f),
                new Vector4(anchor.x - (t00 * m.x + t01 * m.y),
                            anchor.y - (t10 * m.x + t11 * m.y), 0f, 1f));
            GUI.DrawTexture(new Rect(0f, 0f, W, Hs), tex, ScaleMode.StretchToFill, true);

            // 枪口火光（F015）：锚点就是枪口 —— 视图模型那套变换本来就把枪口钉在准星上，
            // 所以火光不需要另算位置，画在同一个点上必然对得齐。
            float pf = ShotFx3D.PlayerFlash;
            if (pf > 0f)
            {
                GUI.matrix = prevMatrix;
                var ft = ShotFx3D.GuiFlashTex;
                if (ft != null)
                {
                    float fs = UI.screenHeight * 0.17f * (0.55f + 0.45f * pf);
                    GUI.color = new Color(1f, 0.82f, 0.42f, pf);
                    GUI.DrawTexture(new Rect(anchor.x - fs * 0.5f, anchor.y - fs * 0.5f, fs, fs),
                        ft, ScaleMode.StretchToFill, true);
                    GUI.color = prevColor;
                }
            }

            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        /// <summary>第一人称手部与武器的环境受光乘子：采样附身角色所在格的放置亮度，白昼还原接近 1，夜里与室内随环境变暗并吃灯具色相。</summary>
        public static Color SampleHandLight(Pawn pawn)
        {
            if (!Lighting3D.Enabled || pawn == null || pawn.Map == null) return Color.white;
            var sc = SceneRegistry.GetOrCreate(pawn.Map);
            if (sc == null || sc.Light == null) return Color.white;
            Lighting3D.BeginMap(pawn.Map);
            var dp = pawn.DrawPos;
            var c = sc.Light.Placement(new Vector3(dp.x, sc.baseY, dp.z));
            // Placement 在正午露天约为 0.54，归一化到 1.0 使白昼手部保持原色，夜里下探到 ~0.26
            float k = 1.65f;
            return new Color(
                Mathf.Clamp(c.r * k, Lighting3D.LightFloor, 1f),
                Mathf.Clamp(c.g * k, Lighting3D.LightFloor, 1f),
                Mathf.Clamp(c.b * k, Lighting3D.LightFloor, 1f),
                1f);
        }
    }
}