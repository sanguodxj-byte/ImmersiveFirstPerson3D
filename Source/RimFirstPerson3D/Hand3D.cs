// AI生成
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 第一人称"手"：空手时在屏幕右下角绘制一只握拳的手。
    // 贴图为程序化生成的灰度"肤色遮罩"白模，剖面严格拟合 Show Me Your Hands 的 Hand.png：
    //   最外一圈粗黑描边(d>=0.745)、往里 244*(1-u)^0.35 的亮面过渡、中心 244 平顶亮盘。
    // 运行时整体乘以附身者肤色 -> 亮盘=肤色、黑边保持黑。持械时由 Viewmodel3D 画武器，本手隐藏。
    [StaticConstructorOnStartup]
    public static class Hand3D
    {
        private static Texture2D _tex;

        // 摆放按「掌心离屏幕右/下边缘各留多少屏高」定，不再用「框左上角 = 屏幕边 - 系数×size」：
        // 手是一张几乎占满方框的圆饼（不透明半径 ≈0.47×边长），那套系数会把圆饼两侧切到屏外，
        // 看着就是贴在角落里。离线预览脚本读的就是这三个常数（改这里它会跟着变，读不到直接报错）。
        public const float SizeFrac = 0.30f;          // 方框边长占屏高
        public const float MarginRightFrac = 0.24f;   // 掌心离右边缘，占屏高
        public const float MarginBottomFrac = 0.26f;  // 掌心离下边缘，占屏高

        public static Vector2 HandCenter =>
            new Vector2(UI.screenWidth - UI.screenHeight * MarginRightFrac,
                        UI.screenHeight - UI.screenHeight * MarginBottomFrac);

        public static void DrawHandAt(Vector2 center, float size, Color light)
        {
            var pawn = CameraRig3D.Possessed;
            if (pawn == null) return;
            EnsureTex();
            var r = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);
            var prev = GUI.color;
            GUI.color = SkinOf(pawn) * light;
            GUI.DrawTexture(r, _tex, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        public static void Draw()
        {
            if (Viewmodel3D.Armed) return;              // 持械：隐藏手
            var pawn = CameraRig3D.Possessed;
            if (pawn == null) return;

            EnsureTex();

            float size = UI.screenHeight * SizeFrac;
            var off = Viewmodel3D.SwingOffset(size * 0.30f);   // 左键挥拳时前推
            float cx = UI.screenWidth - UI.screenHeight * MarginRightFrac;
            float cy = UI.screenHeight - UI.screenHeight * MarginBottomFrac;
            var r = new Rect(cx - size * 0.5f + off.x, cy - size * 0.5f + off.y, size, size);

            var prev = GUI.color;
            GUI.color = SkinOf(pawn) * Viewmodel3D.SampleHandLight(pawn);
            GUI.DrawTexture(r, _tex, ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        // 附身者肤色（非人形/取不到时给中性肤色兜底）。
        private static Color SkinOf(Pawn pawn)
        {
            if (pawn.story != null)
                return pawn.story.SkinColor;
            return new Color(0.87f, 0.69f, 0.57f);
        }

        // 程序化生成 128x128 灰度手贴图，仅生成一次。
        private static void EnsureTex()
        {
            if (_tex != null) return;
            const int S = 128;
            const float R = 60f;
            const float C = (S - 1) * 0.5f;   // 63.5
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            t.filterMode = FilterMode.Bilinear;
            t.wrapMode = TextureWrapMode.Clamp;
            var cols = new Color[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x - C, dy = y - C;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / R;
                    if (d >= 1f)
                    {
                        cols[y * S + x] = new Color(0f, 0f, 0f, 0f);
                        continue;
                    }
                    float L;
                    if (d < 0.458f) L = 244f;                              // 平顶亮盘
                    else if (d < 0.745f)
                    {
                        float u = (d - 0.458f) / 0.287f;
                        L = 244f * Mathf.Pow(1f - u, 0.35f);               // 亮面过渡
                    }
                    else L = 0f;                                           // 粗黑描边
                    float a = 1f;
                    if (d > 0.97f) a = Mathf.Clamp01(1f - (d - 0.97f) / 0.03f);
                    float v = L / 255f;
                    cols[y * S + x] = new Color(v, v, v, a);
                }
            }
            t.SetPixels(cols);
            t.Apply(false, false);
            _tex = t;
        }
    }
}
