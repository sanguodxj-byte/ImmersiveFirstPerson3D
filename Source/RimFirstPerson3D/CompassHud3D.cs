// AI生成
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimFirstPerson3D
{
    // 上古卷轴5（天际）风格顶部中央水平框式罗盘。
    // 显示当前视线偏航角（N / NE / E / SE / S / SW / W / NW 与刻度），
    // 并在视场内以实心菱形标记近距离敌对、同伴、倒地队友与猎物。
    // 所有标记严格遵循全项目实心填充铁律（Solid Filled）。
    [StaticConstructorOnStartup]
    public static class CompassHud3D
    {
        public const float Width = 380f;
        public const float Height = 32f;
        public const float FovDegrees = 180f;

        private static Texture2D solid;

        private static readonly (int deg, string label, bool major)[] Cardinals = new[]
        {
            (0, "N", true),
            (45, "NE", false),
            (90, "E", true),
            (135, "SE", false),
            (180, "S", true),
            (225, "SW", false),
            (270, "W", true),
            (315, "NW", false)
        };

        public static Rect Bounds => new Rect((UI.screenWidth - Width) * 0.5f, 10f, Width, Height);

        private static void EnsureSolid()
        {
            if (solid != null) return;
            solid = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solid.SetPixel(0, 0, Color.white);
            solid.Apply();
            solid.name = "Compass3D_Solid";
        }

        private static void Fill(Rect r, Color c)
        {
            EnsureSolid();
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, solid);
            GUI.color = prev;
        }

        private static void DrawSolidDiamond(float cx, float cy, float size, Color c)
        {
            EnsureSolid();
            float half = size * 0.5f;
            Color prev = GUI.color;
            GUI.color = c;
            Matrix4x4 prevMatrix = GUI.matrix;
            // 采用局部矩阵右乘复合，确保在任何 UIScale 下严格以 (cx, cy) 为中心原地旋转 45 度，绝不飘移
            Matrix4x4 localTransform = Matrix4x4.TRS(new Vector3(cx, cy, 0f), Quaternion.Euler(0f, 0f, 45f), Vector3.one)
                                     * Matrix4x4.TRS(new Vector3(-cx, -cy, 0f), Quaternion.identity, Vector3.one);
            GUI.matrix = prevMatrix * localTransform;
            GUI.DrawTexture(new Rect(cx - half, cy - half, size, size), solid);
            GUI.matrix = prevMatrix;
            GUI.color = prev;
        }

        private static void DrawCenterPip(float cx, float topY)
        {
            Color pipColor = new Color(0.92f, 0.88f, 0.76f, 0.95f);
            // 实心下三角指针：4层实心矩形叠成实心向下箭头
            Fill(new Rect(cx - 3.5f, topY, 7f, 1.5f), pipColor);
            Fill(new Rect(cx - 2.5f, topY + 1.5f, 5f, 1.5f), pipColor);
            Fill(new Rect(cx - 1.5f, topY + 3.0f, 3f, 1.5f), pipColor);
            Fill(new Rect(cx - 0.5f, topY + 4.5f, 1f, 1.5f), pipColor);
        }

        public static void Draw(Pawn possessed)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            Rect rect = Bounds;
            float yaw = CameraRig3D.CamYaw;

            // 1. 天际风格线框底板
            Color frameColor = new Color(0.72f, 0.69f, 0.60f, 0.85f);
            // 半透明底色
            Fill(rect, new Color(0.04f, 0.04f, 0.05f, 0.65f));
            // 顶线与底线
            Fill(new Rect(rect.x, rect.y, rect.width, 1.5f), frameColor);
            Fill(new Rect(rect.x, rect.yMax - 1.5f, rect.width, 1.5f), frameColor);
            // 左右立端
            Fill(new Rect(rect.x, rect.y, 2f, rect.height), frameColor);
            Fill(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), frameColor);
            // 左右小折翼装饰
            Fill(new Rect(rect.x - 3f, rect.y + 6f, 3f, rect.height - 12f), frameColor);
            Fill(new Rect(rect.xMax, rect.y + 6f, 3f, rect.height - 12f), frameColor);

            // 2. 正上方中心实心指示指针
            DrawCenterPip(rect.center.x, rect.y);

            // 3. 罗盘刻度条（方位文本与刻度线，左右两端渐变衰减）
            DrawCompassTape(rect, yaw);

            // 4. 近距离目标动态实心标记（敌对红菱形 / 盟友蓝菱形 / 倒地黄菱形）
            DrawEntityMarkers(rect, possessed, yaw);
        }

        private static void DrawCompassTape(Rect rect, float currentYaw)
        {
            float halfW = rect.width * 0.5f;
            float centerX = rect.center.x;

            // 方位文本（N, NE, E, SE, S, SW, W, NW）
            for (int i = 0; i < Cardinals.Length; i++)
            {
                var card = Cardinals[i];
                float delta = Mathf.DeltaAngle(currentYaw, card.deg);
                if (Mathf.Abs(delta) > FovDegrees * 0.5f) continue;

                float px = centerX + (delta / (FovDegrees * 0.5f)) * halfW;
                if (px < rect.x + 6f || px > rect.xMax - 6f) continue;

                float edgeDist = halfW - Mathf.Abs(px - centerX);
                float alpha = Mathf.Clamp01(edgeDist / 38f);
                if (alpha < 0.02f) continue;

                if (card.major)
                {
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(0.96f, 0.94f, 0.84f, alpha);
                    Widgets.Label(new Rect(px - 14f, rect.y + 5f, 28f, 20f), card.label);
                }
                else
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    GUI.color = new Color(0.78f, 0.76f, 0.70f, alpha * 0.85f);
                    Widgets.Label(new Rect(px - 14f, rect.y + 7f, 28f, 16f), card.label);
                }
            }

            // 15度细分刻度
            Color tickColor = new Color(0.70f, 0.68f, 0.62f);
            for (int deg = 0; deg < 360; deg += 15)
            {
                if (deg % 45 == 0) continue; // 方位文字位置跳过刻度线

                float delta = Mathf.DeltaAngle(currentYaw, deg);
                if (Mathf.Abs(delta) > FovDegrees * 0.5f) continue;

                float px = centerX + (delta / (FovDegrees * 0.5f)) * halfW;
                if (px < rect.x + 4f || px > rect.xMax - 4f) continue;

                float edgeDist = halfW - Mathf.Abs(px - centerX);
                float alpha = Mathf.Clamp01(edgeDist / 35f);
                if (alpha < 0.02f) continue;

                float tickH = (deg % 30 == 0) ? 6.5f : 4f;
                Fill(new Rect(px - 0.75f, rect.yMax - 2f - tickH, 1.5f, tickH),
                    new Color(tickColor.r, tickColor.g, tickColor.b, alpha * 0.65f));
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static void DrawEntityMarkers(Rect rect, Pawn possessed, float currentYaw)
        {
            Map map = possessed != null ? possessed.MapHeld : Find.CurrentMap;
            if (map == null || map.mapPawns == null) return;

            Vector3 eyePos = possessed != null ? possessed.DrawPos : CameraRig3D.LastCamPos;
            float halfW = rect.width * 0.5f;
            float centerX = rect.center.x;
            var pawns = map.mapPawns.AllPawnsSpawned;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == null || p == possessed || p.Dead) continue;

                Vector3 diff = p.DrawPos - eyePos;
                float distSqr = diff.x * diff.x + diff.z * diff.z;
                if (distSqr > 3600f || distSqr < 0.25f) continue; // 60格感知范围

                float targetYaw = Mathf.Atan2(diff.x, diff.z) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(currentYaw, targetYaw);
                if (Mathf.Abs(delta) > FovDegrees * 0.5f) continue;

                float px = centerX + (delta / (FovDegrees * 0.5f)) * halfW;
                if (px < rect.x + 8f || px > rect.xMax - 8f) continue;

                float edgeDist = halfW - Mathf.Abs(px - centerX);
                float alpha = Mathf.Clamp01(edgeDist / 30f);
                if (alpha < 0.05f) continue;

                float markerY = rect.center.y;
                if (p.HostileTo(Faction.OfPlayer))
                {
                    // 敌对目标：实心红色菱形
                    DrawSolidDiamond(px, markerY, 7f, new Color(0.96f, 0.20f, 0.15f, alpha));
                }
                else if (p.IsColonist)
                {
                    if (p.Downed)
                    {
                        // 倒地殖民者：实心琥珀橙菱形
                        DrawSolidDiamond(px, markerY, 7f, new Color(1.0f, 0.65f, 0.15f, alpha));
                    }
                    else
                    {
                        // 友方殖民者：实心青蓝菱形
                        DrawSolidDiamond(px, markerY, 6f, new Color(0.25f, 0.85f, 0.95f, alpha));
                    }
                }
                else if (p.Faction == Faction.OfPlayer)
                {
                    // 殖民地驯服动物：实心草绿菱形
                    DrawSolidDiamond(px, markerY, 5f, new Color(0.35f, 0.85f, 0.40f, alpha * 0.85f));
                }
            }
        }
    }
}
