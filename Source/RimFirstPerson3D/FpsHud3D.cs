// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 第一人称常驻 HUD：左下角角色状态（名字 + 实心血条）与背包面板（E 开关）。
    // 全部实心填充；文字只用游戏数据本身（角色名/物品标签/数字），不自造文案。
    public static class FpsHud3D
    {
        public static bool inventoryOpen;

        private static Texture2D solid;

        // 背包开着时移动输入让位（环视保持）。
        public static bool BlockMovement => inventoryOpen;

        public static void ToggleInventory()
        {
            if (MapScreen3D.BlocksGameplay) return;
            inventoryOpen = !inventoryOpen && CameraRig3D.Possessed != null;
        }

        public static void CloseInventory() { inventoryOpen = false; }

        public static void Draw()
        {
            var pawn = CameraRig3D.Possessed;
            if (pawn == null) { inventoryOpen = false; return; }
            if (solid == null)
            {
                solid = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                solid.SetPixel(0, 0, Color.white);
                solid.Apply();
                solid.name = "Fps3D_HudSolid";
            }

            DrawStatus(pawn);
            if (inventoryOpen) DrawInventory(pawn);
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

        private static void DrawInventory(Pawn pawn)
        {
            float w = 330f;
            Thing primary = null;
            var rows = new List<Thing>();
            try
            {
                if (pawn.equipment != null) primary = pawn.equipment.Primary;
                if (pawn.inventory != null && pawn.inventory.innerContainer != null)
                    foreach (var th in pawn.inventory.innerContainer)
                        if (th != null && th.def != null) rows.Add(th);
            }
            catch { }

            float h = 32f + (primary != null ? 26f : 0f) + rows.Count * 28f + 10f;
            var r = new Rect(UI.screenWidth * 0.5f - w * 0.5f, (UI.screenHeight - h) * 0.5f, w, h);
            Fill(r, new Color(0f, 0f, 0f, 0.72f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.95f, 0.97f, 1f);
            float y = r.y + 8f;
            // 标题只用游戏数据（角色名），不自造文案。
            string head = pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.label;
            Widgets.Label(new Rect(r.x + 12f, y, w - 24f, 22f), head);
            y += 24f;

            // 装备中的主武器（只读）。
            if (primary != null)
            {
                Fill(new Rect(r.x + 8f, y - 2f, w - 16f, 24f), new Color(1f, 1f, 1f, 0.08f));
                Widgets.Label(new Rect(r.x + 16f, y, w - 32f, 22f), primary.def.label);
                y += 26f;
            }

            // 背包内物品：点一行即取出，丢到附身者脚下。
            foreach (var th in rows)
            {
                string label = th.def.label + (th.stackCount > 1 ? " x" + th.stackCount : "");
                if (Widgets.ButtonText(new Rect(r.x + 10f, y - 2f, w - 20f, 24f), label))
                    Drop(pawn, th);
                y += 28f;
            }
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // 从背包把一件物品丢到附身者脚下（取出）。
        private static void Drop(Pawn pawn, Thing th)
        {
            try
            {
                if (th == null || th.Destroyed) return;
                var map = pawn.MapHeld;
                if (map == null || pawn.inventory == null || pawn.inventory.innerContainer == null) return;
                if (!pawn.inventory.innerContainer.Contains(th)) return;
                Thing dropped;
                if (pawn.inventory.innerContainer.TryDrop(th, pawn.PositionHeld, map, ThingPlaceMode.Near, out dropped)
                    && dropped != null)
                    dropped.SetForbidden(false, false);
            }
            catch { }
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
