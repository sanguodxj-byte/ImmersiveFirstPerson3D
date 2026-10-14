// AI生成
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;

namespace RimFirstPerson3D
{
    // 第一人称常驻 HUD：
    // 1. 左下角角色状态条
    // 2. 中间上方天际风格框式罗盘（CompassHud3D）
    // 3. 右上角战术雷达迷你地图（MinimapHud3D）
    // 4. 右下角日期时间、天气、温度、速度等信息直接透传（GlobalControlsOnGUI）
    // 5. 背包界面（B / Tab 开关）
    [StaticConstructorOnStartup]
    public static class FpsHud3D
    {
        public static bool inventoryOpen;

        private static Texture2D solid;
        private static Vector2 scroll;

        // 背包开着时移动输入让位（环视保持）。
        public static bool BlockMovement => inventoryOpen;

        public static bool HasInteractiveUI =>
            Fps3DState.Active && Fps3DState.Mode == ViewMode.FirstPerson;

        public static Rect StatusRect => new Rect(10f, UI.screenHeight - 56f, 216f, 46f);
        public static Rect GlobalControlsRect
        {
            get
            {
                float top = (Find.LetterStack != null && Find.LetterStack.LettersListForReading != null && Find.LetterStack.LettersListForReading.Count > 0)
                    ? Mathf.Min(Find.LetterStack.LastTopY, UI.screenHeight - 480f)
                    : (UI.screenHeight - 480f);
                return new Rect(UI.screenWidth - 280f, Mathf.Max(0f, top - 20f), 280f, UI.screenHeight);
            }
        }

        public static Rect InventoryRect
        {
            get
            {
                float w = 372f;
                var items = Backpack3D.Items();
                float h = Mathf.Min(UI.screenHeight * 0.82f, 140f + items.Count * 30f);
                return new Rect(UI.screenWidth * 0.5f - w * 0.5f, (UI.screenHeight - h) * 0.5f, w, h);
            }
        }

        // 光标是否落在第一人称 HUD 任何控件几何范围内
        public static bool OverHud(Vector2 mousePos)
        {
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return false;
            if (inventoryOpen && InventoryRect.Contains(mousePos)) return true;
            if (StatusRect.Contains(mousePos)) return true;
            if (GlobalControlsRect.Contains(mousePos)) return true;
            if (MinimapHud3D.Bounds.Contains(mousePos)) return true;
            if (CompassHud3D.Bounds.Contains(mousePos)) return true;
            return false;
        }

        public static void ToggleInventory()
        {
            if (MapScreen3D.BlocksGameplay) return;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return;
            inventoryOpen = !inventoryOpen && CameraRig3D.Possessed != null;
            scroll = Vector2.zero;
        }

        public static void CloseInventory() { inventoryOpen = false; }

        public static void Draw()
        {
            // 仅第一人称显示背包、罗盘、小地图与状态；其它视角或终端打开期间一律收起来。
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson || MapScreen3D.BlocksGameplay)
            {
                inventoryOpen = false;
                return;
            }
            EnsureSolid();
            var pawn = CameraRig3D.Possessed;

            // 1. 左下角状态条（有附身角色时显示）
            if (pawn != null)
            {
                try { DrawStatus(pawn); } catch { }
            }

            // 2. 中间上方上古卷轴5框式罗盘
            try { CompassHud3D.Draw(pawn); } catch { }

            // 3. 右上角迷你地图
            try { MinimapHud3D.Draw(pawn); } catch { }

            // 4. 右下角日期时间等信息直接透传
            try { DrawGlobalControls(); } catch { }

            // 5. 背包界面（若开启）
            if (inventoryOpen && pawn != null)
            {
                try { DrawInventory(); } catch { }
            }

            // 6. 统一绘制悬停提示
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                try { TooltipHandler.DoTooltipGUI(); } catch { }
            }
        }

        private static void EnsureSolid()
        {
            if (solid != null) return;
            solid = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solid.SetPixel(0, 0, Color.white);
            solid.Apply();
            solid.name = "Fps3D_HudSolid";
        }

        // 透传 RimWorld 原版右下角日期时间、天气、温度、速度控制器与信件
        private static void DrawGlobalControls()
        {
            if (Current.ProgramState != ProgramState.Playing) return;
            if (Find.CurrentMap == null) return;
            var ui = Find.MapUI;
            if (ui == null || ui.globalControls == null) return;

            try
            {
                ui.globalControls.GlobalControlsOnGUI();
            }
            catch
            {
                // 地图过渡期或切换期保护
            }
        }

        private static void DrawStatus(Pawn pawn)
        {
            var r = StatusRect;
            Fill(r, new Color(0f, 0f, 0f, 0.55f));

            string name = pawn.Name != null ? pawn.Name.ToStringFull : pawn.def.label;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.95f, 0.97f, 1f);
            Widgets.Label(new Rect(r.x + 10f, r.y + 4f, r.width - 20f, 20f), name);

            float frac = 1f;
            try
            {
                if (pawn.health != null && pawn.health.summaryHealth != null)
                    frac = Mathf.Clamp01(pawn.health.summaryHealth.SummaryHealthPercent);
            }
            catch { }
            var bar = new Rect(r.x + 10f, r.y + 28f, r.width - 52f, 9f);
            Fill(bar, new Color(0.12f, 0.10f, 0.10f, 0.9f));
            Fill(new Rect(bar.x, bar.y, bar.width * frac, bar.height),
                Color.Lerp(new Color(0.75f, 0.16f, 0.12f), new Color(0.22f, 0.72f, 0.28f), frac));
            GUI.color = new Color(0.95f, 0.97f, 1f);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(r.x + r.width - 44f, r.y + 22f, 36f, 18f),
                Mathf.RoundToInt(frac * 100f).ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // 背包界面：装备栏（只读）+ 背包物品列表（每行一个主要动作按钮 + 丢弃）。
        private static void DrawInventory()
        {
            var pawn = CameraRig3D.Possessed;
            if (pawn == null) { inventoryOpen = false; return; }

            var items = Backpack3D.Items();
            Thing primary = Backpack3D.PrimaryWeapon;

            var r = InventoryRect;
            float w = r.width;
            Fill(r, new Color(0f, 0f, 0f, 0.78f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.95f, 0.97f, 1f);

            float y = r.y + 8f;
            string head = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.label;
            Widgets.Label(new Rect(r.x + 12f, y, w - 90f, 22f), head);
            // 关闭按钮
            if (Widgets.ButtonText(new Rect(r.x + w - 74f, y - 1f, 62f, 24f), T("Close", "关闭")))
                inventoryOpen = false;
            y += 28f;

            // 装备中的主武器（只读，附带说明拿在手上）。
            if (primary != null)
            {
                Fill(new Rect(r.x + 8f, y - 2f, w - 16f, 24f), new Color(1f, 1f, 1f, 0.10f));
                Widgets.Label(new Rect(r.x + 16f, y, w - 32f, 22f),
                    primary.def.label + "  " + T("Equipped", "已装备"));
                y += 28f;
            }

            // 列表区域
            var outRect = new Rect(r.x + 8f, y, w - 16f, r.yMax - 12f - y);
            float rowH = 30f;
            var viewRect = new Rect(0f, 0f, outRect.width - 18f, Mathf.Max(outRect.height, items.Count * rowH));
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float ry = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                var th = items[i];
                int slot = i + 1;
                var row = new Rect(0f, ry, viewRect.width, rowH - 4f);
                Fill(row, new Color(1f, 1f, 1f, 0.06f));

                // 前 N 件标出快捷栏号（数字键直接使用）。
                if (slot <= Backpack3D.HotbarSlots)
                    Widgets.Label(new Rect(row.x + 6f, row.y + 3f, 16f, 20f), slot.ToString());

                string label = th.def.label + (th.stackCount > 1 ? " x" + th.stackCount : "");
                Widgets.Label(new Rect(row.x + 26f, row.y + 3f, row.width - 150f, 20f), label);

                string act = Backpack3D.PrimaryActionLabel(th);
                if (!string.IsNullOrEmpty(act))
                {
                    if (Widgets.ButtonText(new Rect(row.x + row.width - 118f, row.y + 1f, 58f, 22f), act))
                        Backpack3D.PrimaryAction(th);
                }
                if (Widgets.ButtonText(new Rect(row.x + row.width - 56f, row.y + 1f, 54f, 22f), T("DropThing", "丢弃")))
                    Backpack3D.Drop(th);

                ry += rowH;
            }
            if (items.Count == 0)
                Widgets.Label(new Rect(0f, 0f, viewRect.width, 22f), T("InventoryEmpty", "背包是空的"));
            Widgets.EndScrollView();

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static string T(string key, string fallback)
        {
            string s = key.Translate();
            return string.IsNullOrEmpty(s) || s == key ? fallback : s;
        }

        private static void Fill(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, solid);
            GUI.color = prev;
        }
    }
}
