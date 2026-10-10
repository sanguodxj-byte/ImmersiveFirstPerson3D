// AI生成
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 第一人称准星：屏幕正中的实心十字。
    // 全局规范要求实心填充、禁止空心轮廓，因此用实心矩形块拼出十字，不画描边线。
    // 只绘制不接收输入（不调用任何 GUI 控件），不会阻断底层 UI。
    public static class Crosshair3D
    {
        private const float ArmLength = 7f;   // 单臂长度（像素）
        private const float ArmThick = 2f;    // 臂厚度（像素）
        private const float Gap = 2f;         // 中心到臂起点的空隙
        private const float CenterSize = 2f;  // 中心块边长

        private static Texture2D solid;
        private static Color lastColor = new Color(-1f, -1f, -1f, -1f);

        public static void Draw()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null || !s.showCrosshair) return;

            float k = Mathf.Max(0.2f, s.crosshairScale);
            float cx = UI.screenWidth * 0.5f;
            float cy = UI.screenHeight * 0.5f;
            var color = new Color(
                Mathf.Clamp01(s.crosshairR),
                Mathf.Clamp01(s.crosshairG),
                Mathf.Clamp01(s.crosshairB), 1f);

            if (solid == null)
            {
                solid = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solid.SetPixel(0, 0, Color.white);
                solid.Apply();
                solid.name = "Fps3D_CrosshairSolid";
            }

            // GUI.color 复制到材质，一次性设置可省掉逐块赋值。
            if (color != lastColor)
            {
                lastColor = color;
                GUI.color = color;
            }

            float a = ArmLength * k, t = Mathf.Max(1f, ArmThick * k);
            float g = Gap * k, c = CenterSize * k;

            // 中心实心块
            Fill(cx - c * 0.5f, cy - c * 0.5f, c, c);
            // 四向实心臂（实心矩形，非线条）
            Fill(cx - t * 0.5f, cy - g - a, t, a);   // 上
            Fill(cx - t * 0.5f, cy + g, t, a);       // 下
            Fill(cx - g - a, cy - t * 0.5f, a, t);   // 左
            Fill(cx + g, cy - t * 0.5f, a, t);       // 右

            GUI.color = Color.white;
            lastColor = new Color(-1f, -1f, -1f, -1f);

            // 命中物名称：十字旁边用 Widgets.Label 画 LabelCap，无命中不画。
            var hit = CameraRig3D.RaycastCenter();
            if (hit != null && hit.Value.thing != null)
            {
                string label = hit.Value.thing.LabelCap;
                Vector2 size = Text.CalcSize(label);
                Widgets.Label(new Rect(cx + g + a + 8f, cy - size.y * 0.5f, size.x + 4f, size.y), label);
            }
        }

        // 纯绘制调用：不经 GUI 控件系统，不消耗也不拦截任何输入事件。
        private static void Fill(float x, float y, float w, float h)
        {
            GUI.DrawTexture(new Rect(x, y, w, h), solid);
        }
    }
}
