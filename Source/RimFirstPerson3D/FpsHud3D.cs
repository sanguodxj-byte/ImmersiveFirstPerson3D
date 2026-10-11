// AI生成
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 第一人称常驻 HUD：左下角角色状态条 + 背包界面（B / Tab 开关）。
    // 背包本身只是一个视图：物品的枚举与动作全部交给独立组件 Backpack3D，
    // 本类不直接碰 pawn.inventory —— 这样"存什么、怎么用"只有一处实现。
    // 整个背包仅在第一人称可用（Chase/Off 下不显示、也不响应开关）。
    [StaticConstructorOnStartup]
    public static class FpsHud3D
    {
        public static bool inventoryOpen;

        private static Texture2D solid;
        private static Vector2 scroll;

        // 背包开着时移动输入让位（环视保持）。
        public static bool BlockMovement => inventoryOpen;

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
            // 仅第一人称显示背包与状态；其它视角一律收起来。
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson)
            {
                inventoryOpen = false;
                return;
            }
            var pawn = CameraRig3D.Possessed;
            if (pawn == null) { inventoryOpen = false; return; }
            EnsureSolid();

            DrawStatus(pawn);
            if (inventoryOpen) DrawInventory();
        }

        private static void EnsureSolid()
        {
            if (solid != null) return;
            solid = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            solid.SetPixel(0, 0, Color.white);
            solid.Apply();
            solid.name = "Fps3D_HudSolid";
        }

        private static void DrawStatus(Pawn pawn)
        {
            float w = 216f, h = 46f;
            var r = new Rect(10f, UI.screenHeight - h - 10f, w, h);
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

            float w = 372f;
            float h = Mathf.Min(UI.screenHeight * 0.82f, 140f + items.Count * 30f);
            var r = new Rect(UI.screenWidth * 0.5f - w * 0.5f, (UI.screenHeight - h) * 0.5f, w, h);
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
