// AI生成
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;
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
                float y = Mathf.Max(0f, top - 20f);
                return new Rect(UI.screenWidth - 300f, y, 300f, UI.screenHeight - y);
            }
        }

        public static Rect InventoryRect
        {
            get
            {
                float w = 480f;
                float h = Mathf.Clamp(UI.screenHeight * 0.72f, 420f, 620f);
                return new Rect((UI.screenWidth - w) * 0.5f, (UI.screenHeight - h) * 0.5f, w, h);
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
            // 仅在原版 UIRootOnGUI 被完全跳过（VanillaInputTakenOver 为真）时，由模组负责补偿绘制 GlobalControls；
            // 若原版处于让位态（按 Alt、开弹窗、Esc 等），原版 UIRoot 已经在绘制，此处不再重复绘制。
            if (!CameraRig3D.VanillaInputTakenOver) return;
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

        // 背包界面：原版质感窗口（装备栏 + 负重条 + 货物列表，集成图标/丢弃/食用/穿戴与标准翻译键）。
        private static void DrawInventory()
        {
            var pawn = CameraRig3D.Possessed;
            if (pawn == null) { inventoryOpen = false; return; }

            var items = Backpack3D.Items();
            Thing primary = Backpack3D.PrimaryWeapon;

            var r = InventoryRect;
            float w = r.width;

            // 原版质感窗口底框与边框
            Widgets.DrawWindowBackground(r);

            // 1. 标题与头部
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            string title = (pawn.Name != null ? pawn.Name.ToStringShort : pawn.def.label) + " (" + T("TabGear", "装备") + ")";
            Widgets.Label(new Rect(r.x + 16f, r.y + 12f, w - 60f, 28f), title);

            // 原版右上角标准关闭按钮
            if (Widgets.CloseButtonFor(r))
            {
                SoundDefOf.Click.PlayOneShotOnCamera();
                inventoryOpen = false;
            }

            // 2. 负重信息与进度条
            float curMass = MassUtility.GearAndInventoryMass(pawn);
            float maxMass = MassUtility.Capacity(pawn);
            bool overloaded = curMass > maxMass;
            Text.Font = GameFont.Small;
            GUI.color = overloaded ? ColorLibrary.RedReadable : new Color(0.9f, 0.9f, 0.9f, 1f);
            Widgets.Label(new Rect(r.x + 16f, r.y + 40f, w - 32f, 22f),
                "MassCarried".Translate(curMass.ToString("0.##"), maxMass.ToString("0.##")));
            GUI.color = Color.white;

            // 细长实心负重填充条
            var barRect = new Rect(r.x + 16f, r.y + 63f, w - 32f, 4f);
            Fill(barRect, new Color(0.12f, 0.12f, 0.12f, 0.8f));
            float frac = Mathf.Clamp01(curMass / Mathf.Max(0.1f, maxMass));
            Color barColor = overloaded ? new Color(0.85f, 0.2f, 0.2f) : new Color(0.35f, 0.75f, 0.35f);
            Fill(new Rect(barRect.x, barRect.y, barRect.width * frac, barRect.height), barColor);

            // 3. 滚动列表区域
            float listY = r.y + 74f;
            var outRect = new Rect(r.x + 12f, listY, w - 24f, r.yMax - 14f - listY);

            // 预估滚动高度：装备项 + 货物项 + 2个分段标题
            float contentHeight = 32f + (primary != null ? 32f : 24f) + 32f + Mathf.Max(26f, items.Count * 32f) + 20f;
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(outRect.height, contentHeight));

            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float curY = 0f;

            // ---- 装备分段 ----
            Widgets.ListSeparator(ref curY, viewRect.width, T("Equipment", "装备"));
            if (primary != null)
            {
                var row = new Rect(0f, curY, viewRect.width, 28f);
                if (Mouse.IsOver(row))
                {
                    GUI.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                    GUI.DrawTexture(row, TexUI.HighlightTex);
                    GUI.color = Color.white;
                }

                // 武器真实图标
                if (primary.def.DrawMatSingle != null && primary.def.DrawMatSingle.mainTexture != null)
                {
                    Widgets.ThingIcon(new Rect(4f, curY, 28f, 28f), primary);
                }

                // 武器名称与品质
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Small;
                GUI.color = new Color(0.9f, 0.9f, 0.9f, 1f);
                Rect labelRect = new Rect(36f, curY, row.width - 36f - 56f, 28f);
                Widgets.Label(labelRect, primary.LabelCap.Truncate(labelRect.width));
                if (Mouse.IsOver(row))
                {
                    TooltipHandler.TipRegion(row, primary.GetTooltip());
                }

                // 右侧小操作按钮：信息卡与丢下
                Widgets.InfoCardButton(row.width - 24f, curY + 2f, primary);

                Rect dropR = new Rect(row.width - 50f, curY + 2f, 24f, 24f);
                TooltipHandler.TipRegion(dropR, "DropThing".Translate());
                if (Widgets.ButtonImage(dropR, TexButton.Drop))
                {
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    Backpack3D.Drop(primary);
                }

                curY += 32f;
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.grey;
                Widgets.Label(new Rect(10f, curY, viewRect.width, 24f), "(" + T("EquippedNothing", "（无）") + ")");
                GUI.color = Color.white;
                curY += 26f;
            }

            // ---- 背包货物分段 ----
            Widgets.ListSeparator(ref curY, viewRect.width, T("Inventory", "货物"));
            if (items.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = Color.grey;
                Widgets.Label(new Rect(10f, curY, viewRect.width, 24f), "(" + T("EquippedNothing", "（无）") + ")");
                GUI.color = Color.white;
                curY += 26f;
            }
            else
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var th = items[i];
                    int slot = i + 1;
                    var row = new Rect(0f, curY, viewRect.width, 28f);
                    if (Mouse.IsOver(row))
                    {
                        GUI.color = new Color(0.5f, 0.5f, 0.5f, 1f);
                        GUI.DrawTexture(row, TexUI.HighlightTex);
                        GUI.color = Color.white;
                    }

                    // 前 N 件快捷栏数字提示方块
                    float xOffset = 4f;
                    if (slot <= Backpack3D.HotbarSlots)
                    {
                        var slotRect = new Rect(xOffset, curY + 4f, 18f, 20f);
                        Fill(slotRect, new Color(1f, 1f, 1f, 0.12f));
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Text.Font = GameFont.Tiny;
                        GUI.color = new Color(0.8f, 0.9f, 1f);
                        Widgets.Label(slotRect, slot.ToString());
                        xOffset += 22f;
                    }

                    // 物品真实图标
                    if (th.def.DrawMatSingle != null && th.def.DrawMatSingle.mainTexture != null)
                    {
                        Widgets.ThingIcon(new Rect(xOffset, curY, 28f, 28f), th);
                        xOffset += 32f;
                    }

                    // 物品名称与堆叠
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Text.Font = GameFont.Small;
                    GUI.color = new Color(0.9f, 0.9f, 0.9f, 1f);
                    float rightBtnsWidth = 135f;
                    Rect labelRect = new Rect(xOffset, curY, row.width - xOffset - rightBtnsWidth, 28f);
                    Widgets.Label(labelRect, th.LabelCap.Truncate(labelRect.width));
                    if (Mouse.IsOver(row))
                    {
                        TooltipHandler.TipRegion(row, th.GetTooltip());
                    }

                    // 右侧操作按钮组
                    // 1) 信息按钮
                    Widgets.InfoCardButton(row.width - 24f, curY + 2f, th);

                    // 2) 丢弃图标按钮
                    Rect dropR = new Rect(row.width - 50f, curY + 2f, 24f, 24f);
                    TooltipHandler.TipRegion(dropR, "DropThing".Translate());
                    if (Widgets.ButtonImage(dropR, TexButton.Drop))
                    {
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        Backpack3D.Drop(th);
                    }

                    // 3) 主要动作按钮（食用图标 或 装备/穿戴文字按钮）
                    if (th.def.ingestible != null)
                    {
                        Rect ingR = new Rect(row.width - 76f, curY + 2f, 24f, 24f);
                        TooltipHandler.TipRegion(ingR, "ConsumeThing".CanTranslate() ? "ConsumeThing".Translate(th.LabelNoCount, th) : "ConsumeThing".Translate());
                        if (Widgets.ButtonImage(ingR, TexButton.Ingest))
                        {
                            SoundDefOf.Tick_High.PlayOneShotOnCamera();
                            Backpack3D.PrimaryAction(th);
                        }
                    }
                    else
                    {
                        string act = Backpack3D.PrimaryActionLabel(th);
                        if (!string.IsNullOrEmpty(act))
                        {
                            Rect actR = new Rect(row.width - 128f, curY + 2f, 74f, 24f);
                            if (Widgets.ButtonText(actR, act))
                            {
                                SoundDefOf.Tick_High.PlayOneShotOnCamera();
                                Backpack3D.PrimaryAction(th);
                            }
                        }
                    }

                    curY += 32f;
                }
            }

            Widgets.EndScrollView();
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private static string T(string key, string fallback)
        {
            if (LanguageDatabase.activeLanguage != null && key.CanTranslate())
                return key.Translate().ToString();
            return fallback;
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
