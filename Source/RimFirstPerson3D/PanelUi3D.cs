// AI生成
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimFirstPerson3D
{
    // 终端上的指挥界面。按屏幕纹理的像素排版，不再把桌面 IMGUI 裁切拉进屏幕。
    // 图标、文案、优先级、设计器和技能指令都走原版数据与原版方法。
    public static class PanelUi3D
    {
        const float Margin = 8f;
        const float ButtonH = 34f;
        const float Gizmo = 64f;
        const float LabelH = 22f;

        sealed class Hit
        {
            public float x, y, w, h;
            public bool menu;
            public Action<int> act;
            public string tip;
            public bool Contains(float px, float py)
            {
                return px >= x && py >= y && px < x + w && py < y + h;
            }
        }

        static readonly List<Hit> hits = new List<Hit>(128);
        static readonly List<MainButtonDef> buttons = new List<MainButtonDef>(16);
        static readonly List<DesignationCategoryDef> cats = new List<DesignationCategoryDef>(12);
        static readonly List<WorkTypeDef> works = new List<WorkTypeDef>(24);
        static readonly List<TimeAssignmentDef> hours = new List<TimeAssignmentDef>(8);
        static readonly List<Gizmo> gizmos = new List<Gizmo>(48);
        static readonly List<Gizmo> grouped = new List<Gizmo>(32);
        static readonly List<ResearchProjectDef> projects = new List<ResearchProjectDef>(32);
        static readonly List<FloatMenuOption> menu = new List<FloatMenuOption>(16);
        static readonly List<Window> winBuf = new List<Window>(8);
        static readonly List<ThingDef> resources = new List<ThingDef>(16);

        static FieldInfo menuField, actField, paramField, validField;
        static bool feed;
        static Vector3 fed;
        static Vector2 pointer;
        static float colonistBottom = 68f;
        static float chromeTop = 900f;
        static Rect sheetRect;
        static float contentH;
        static float scroll;
        static bool sheetOpen;
        static string openDef;
        static int catIndex;
        static int workScroll;
        static bool ateDown;
        static bool hasPending;
        static Vector3 pendingFocus;
        static Pawn pendingColonist;
        static string hover;
        static string armed;
        static bool logged;

        static readonly Color Fill = new Color(21f / 255f, 25f / 255f, 29f / 255f, 0.94f);
        static readonly Color Border = new Color(97f / 255f, 108f / 255f, 122f / 255f, 1f);
        static readonly Color HoverFill = new Color(29f / 255f, 45f / 255f, 50f / 255f, 0.96f);
        static readonly Color SelectFill = new Color(0.32f, 0.28f, 0.21f, 0.96f);
        static readonly Color Ink = new Color(0.93f, 0.93f, 0.9f, 1f);
        static readonly Color Dim = new Color(0.62f, 0.62f, 0.62f, 1f);

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(UI), nameof(UI.MouseMapPosition)),
                prefix: new HarmonyMethod(typeof(PanelUi3D), nameof(MouseMapPrefix)));
        }

        public static void Reset()
        {
            hits.Clear();
            menu.Clear();
            sheetOpen = false;
            openDef = null;
            scroll = 0f;
            feed = false;
            ateDown = false;
            hasPending = false;
            pendingColonist = null;
            armed = null;
        }

        // 设计器拖拽和 MouseCell 读的是主相机指针。查看期改成终端俯视打中的那一格。
        static bool MouseMapPrefix(ref Vector3 __result)
        {
            if (!feed || !MapScreen3D.Viewing) return true;
            __result = fed;
            return false;
        }

        public static bool Handle(Map map, bool onScreen, Vector2 uv, ref Vector3 focus)
        {
            try
            {
                return HandleInner(map, onScreen, uv, ref focus);
            }
            catch (Exception e)
            {
                if (!logged)
                {
                    logged = true;
                    Log.Error(ModEntry.LogPrefix + "panel input " + e);
                }
                return false;
            }
        }

        static bool HandleInner(Map map, bool onScreen, Vector2 uv, ref Vector3 focus)
        {
            var tex = GodView3D.ScreenTexture as RenderTexture;
            float w = tex != null ? tex.width : 1536f;
            float h = tex != null ? tex.height : 1086f;
            feed = false;
            if (Input.GetKeyDown(KeyCode.Escape)) BackOut();

            pointer.x = onScreen ? uv.x * w : -1f;
            pointer.y = onScreen ? (1f - uv.y) * h : -1f;

            if (onScreen && sheetOpen && sheetRect.height > 8f && sheetRect.Contains(pointer))
            {
                float wheel = Input.mouseScrollDelta.y;
                if (Mathf.Abs(wheel) > 0.01f)
                    scroll = Mathf.Clamp(scroll - wheel * 40f, 0f, Mathf.Max(0f, contentH - (sheetRect.height - 36f)));
            }

            bool left = Input.GetMouseButtonDown(0);
            bool right = Input.GetMouseButtonDown(1);
            bool up = Input.GetMouseButtonUp(0);
            bool onChrome = onScreen && (pointer.y <= colonistBottom || pointer.y >= chromeTop);
            bool onSheet = onScreen && sheetOpen && sheetRect.height > 8f && sheetRect.Contains(pointer);

            if (onScreen && map != null && MapScreen3D.TryMapPoint(uv, out var point))
            {
                point.y = 0f;
                fed = point;
                feed = true;
            }

            if (onScreen && (left || right))
            {
                Hit hit = TopHit();
                if (menu.Count > 0 && (hit == null || !hit.menu))
                {
                    menu.Clear();
                    ateDown = true;
                    return true;
                }
                if (hit != null)
                {
                    ateDown = true;
                    hit.act?.Invoke(left ? 0 : 1);
                    ApplyPending(ref focus);
                    return true;
                }
                if (onSheet || onChrome)
                {
                    ateDown = true;
                    return true;
                }
            }

            if (up && ateDown)
            {
                ateDown = false;
                return true;
            }

            bool tool = Find.DesignatorManager.SelectedDesignator != null || Find.Targeter.IsTargeting;
            if (tool && feed)
            {
                if (right)
                {
                    SoundDefOf.CancelMode.PlayOneShotOnCamera();
                    Find.DesignatorManager.Deselect();
                    armed = null;
                    if (Find.Targeter.IsTargeting) Find.Targeter.StopTargeting();
                    return true;
                }
                if (left && Find.Targeter.IsTargeting)
                {
                    ConfirmTarget(map, fed.ToIntVec3());
                    return true;
                }
                if (Find.DesignatorManager.SelectedDesignator != null && (left || up))
                    PumpDesignator(left ? EventType.MouseDown : EventType.MouseUp);
                return true;
            }

            if (onChrome || onSheet) return true;
            return false;
        }

        public static void Draw(RenderTexture target)
        {
            if (target == null || !target.IsCreated()) return;
            try
            {
                hits.Clear();
                hover = null;
                PanelDraw3D.Begin(target);
                DrawColonists();
                DrawBottom();
                DrawSheet();
                DrawMenu();
                DrawHover();
                PanelDraw3D.End();
            }
            catch (Exception e)
            {
                try { PanelDraw3D.End(); } catch { /* 矩阵已经弹出 */ }
                if (!logged)
                {
                    logged = true;
                    Log.Error(ModEntry.LogPrefix + "panel draw " + e);
                }
            }
        }

        static void DrawColonists()
        {
            float w = PanelDraw3D.Width;
            float rowY = 4f;
            float rowH = 62f;
            colonistBottom = rowY + rowH + 2f;
            var bar = Find.ColonistBar;
            var entries = bar != null ? bar.Entries : null;
            int n = 0;
            float extra = 0f;
            int seenGroup = int.MinValue;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].pawn == null) continue;
                    if (seenGroup != int.MinValue && entries[i].group != seenGroup) extra += 12f;
                    seenGroup = entries[i].group;
                    n++;
                }
            }
            const float slot = 46f;
            const float gap = 4f;
            float rowW = n > 0 ? 4f + n * slot + (n - 1) * gap + extra + 4f : 0f;
            if (rowW > 4f)
                PanelDraw3D.Fill(new Rect(0f, 0f, Mathf.Min(w, rowW), colonistBottom), new Color(Fill.r, Fill.g, Fill.b, 0.88f));
            if (n == 0) return;
            float x = 4f;
            int prevGroup = int.MinValue;
            var map = Find.CurrentMap;
            for (int i = 0; i < entries.Count; i++)
            {
                var pawn = entries[i].pawn;
                if (pawn == null) continue;
                if (prevGroup != int.MinValue && entries[i].group != prevGroup) x += 12f;
                prevGroup = entries[i].group;
                if (x + slot > w - Margin) break;
                var rect = new Rect(x, rowY, slot, rowH - 8f);
                bool onMap = pawn.Map == map;
                var bg = onMap ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                if (ColonistBar.BGTex != null)
                    PanelDraw3D.Tex(rect, ColonistBar.BGTex, new Rect(0f, 0f, 1f, 1f), bg);
                Texture portrait = null;
                try
                {
                    portrait = PortraitsCache.Get(pawn, ColonistBarColonistDrawer.PawnTextureSize, Rot4.South,
                        ColonistBarColonistDrawer.PawnTextureCameraOffset, 1.28205f);
                }
                catch { /* 个别种族肖像取不到时留空框 */ }
                if (portrait != null)
                    PanelDraw3D.Tex(rect.ContractedBy(2f), portrait, new Rect(0f, 0f, 1f, 1f), bg);
                var mood = pawn.needs?.mood;
                if (mood != null)
                {
                    float mh = rect.height * Mathf.Clamp01(mood.CurLevelPercentage);
                    var mc = mood.CurLevelPercentage < 0.25f
                        ? new Color(0.75f, 0.22f, 0.18f, 0.9f)
                        : mood.CurLevelPercentage < 0.5f
                            ? new Color(0.85f, 0.72f, 0.22f, 0.9f)
                            : new Color(0.28f, 0.72f, 0.32f, 0.9f);
                    PanelDraw3D.Fill(new Rect(rect.x + 2f, rect.yMax - mh - 2f, 3f, mh), mc);
                }
                if (Find.Selector.IsSelected(pawn))
                    PanelDraw3D.Box(rect, new Color(0f, 0f, 0f, 0f), Color.white);
                var name = pawn.LabelShort;
                PanelDraw3D.Label(new Rect(rect.x, rect.yMax - 14f, rect.width, 14f), name, GameFont.Tiny, TextAnchor.MiddleCenter, Ink, false);
                var captured = pawn;
                Add(rect, button =>
                {
                    if (button != 0 || captured.Destroyed) return;
                    Find.Selector.Select(captured, false, true);
                    SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    if (captured.Spawned && captured.Map != null)
                    {
                        hasPending = true;
                        pendingFocus = new Vector3(captured.Position.x + 0.5f, 0f, captured.Position.z + 0.5f);
                        if (captured.IsColonist) pendingColonist = captured;
                    }
                }, name);
                x += slot + gap;
            }
        }

        static void DrawBottom()
        {
            float w = PanelDraw3D.Width;
            float h = PanelDraw3D.Height;
            CollectGizmos();
            CollectButtons();
            int cols = Mathf.Max(1, (int)((w - Margin * 2f) / (Gizmo + 4f)));
            int rows = grouped.Count == 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(grouped.Count / (float)cols), 1, 3);
            float gizmoBlock = rows > 0 ? rows * (Gizmo + 4f) : 0f;
            float buttonY = h - Margin - ButtonH;
            float gizmoY = buttonY - gizmoBlock;
            float labelY = (rows > 0 ? gizmoY : buttonY) - 4f - LabelH;
            chromeTop = labelY;
            PanelDraw3D.Fill(new Rect(0f, labelY - 2f, w, h - labelY + 2f), Fill);

            string title = BottomTitle();
            PanelDraw3D.Label(new Rect(Margin, labelY, w * 0.46f, LabelH), title, GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
            DrawResources(new Rect(w * 0.46f, labelY, w * 0.54f - Margin, LabelH));

            for (int i = 0; i < grouped.Count && i < cols * rows; i++)
            {
                int col = i % cols;
                int row = i / cols;
                var rect = new Rect(Margin + col * (Gizmo + 4f), gizmoY + row * (Gizmo + 4f), Gizmo, Gizmo);
                DrawGizmo(rect, grouped[i]);
            }

            if (buttons.Count == 0) return;
            float weight = 0f;
            for (int i = 0; i < buttons.Count; i++)
                weight += buttons[i].minimized ? 0.5f : 1f;
            float unit = weight > 0f ? (w - Margin * 2f) / weight : 0f;
            float x = Margin;
            for (int i = 0; i < buttons.Count; i++)
            {
                var def = buttons[i];
                float bw = unit * (def.minimized ? 0.5f : 1f);
                if (i == buttons.Count - 1) bw = w - Margin - x;
                var rect = new Rect(x, buttonY, Mathf.Max(8f, bw - 2f), ButtonH);
                bool open = sheetOpen && openDef == def.defName;
                bool over = rect.Contains(pointer);
                if (Widgets.ButtonSubtleAtlas != null)
                    PanelDraw3D.Atlas(rect, Widgets.ButtonSubtleAtlas);
                if (open) PanelDraw3D.Fill(rect.ContractedBy(2f), SelectFill);
                else if (over) PanelDraw3D.Fill(rect.ContractedBy(2f), HoverFill);
                if (def.Worker.Disabled) PanelDraw3D.Fill(rect, new Color(0f, 0f, 0f, 0.35f));
                string caption = def.LabelCap;
                if (def.Icon != null)
                    PanelDraw3D.Tex(new Rect(rect.center.x - 14f, rect.center.y - 14f, 28f, 28f), def.Icon, new Rect(0f, 0f, 1f, 1f), Color.white);
                else
                    PanelDraw3D.Label(rect, caption, GameFont.Tiny, TextAnchor.MiddleCenter, def.Worker.Disabled ? Dim : Ink, false);
                var captured = def;
                Add(rect, button =>
                {
                    if (button != 0) return;
                    if (captured.Worker.Disabled)
                    {
                        Messages.Message(captured.LabelCap, MessageTypeDefOf.RejectInput, false);
                        return;
                    }
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    Toggle(captured);
                }, caption);
                x += bw;
            }
        }

        static void DrawGizmo(Rect rect, Gizmo g)
        {
            var cmd = g as Command;
            bool over = rect.Contains(pointer);
            if (cmd != null && cmd.BGTexture != null)
                PanelDraw3D.Tex(rect, cmd.BGTexture, new Rect(0f, 0f, 1f, 1f), over ? HoverFill : Color.white);
            else
                PanelDraw3D.Box(rect, over ? HoverFill : Fill, Border);
            var tint = cmd != null ? cmd.IconDrawColor : Color.white;
            if (g.Disabled) tint = new Color(tint.r, tint.g, tint.b, 0.35f);
            var uv = cmd != null && cmd.iconTexCoords.width > 0.01f ? cmd.iconTexCoords : new Rect(0f, 0f, 1f, 1f);
            if (cmd != null && cmd.icon != null)
                PanelDraw3D.Tex(rect.ContractedBy(8f), cmd.icon, uv, tint);
            string caption = cmd != null ? cmd.LabelCap : "";
            if (cmd == null || cmd.icon == null)
                PanelDraw3D.Label(rect.ContractedBy(3f), caption, GameFont.Tiny, TextAnchor.MiddleCenter, g.Disabled ? Dim : Ink, true);
            else if (!caption.NullOrEmpty())
                PanelDraw3D.Label(new Rect(rect.x, rect.yMax - 16f, rect.width, 16f), caption, GameFont.Tiny, TextAnchor.MiddleCenter, Ink, false);
            if (g is Command_Toggle toggle && toggle.isActive != null && toggle.isActive() && Widgets.CheckboxOnTex != null)
                PanelDraw3D.Tex(new Rect(rect.xMax - 18f, rect.y + 2f, 16f, 16f), Widgets.CheckboxOnTex, new Rect(0f, 0f, 1f, 1f), Color.white);
            if (Find.DesignatorManager.SelectedDesignator == g)
                PanelDraw3D.Box(rect, new Color(0f, 0f, 0f, 0f), Color.white);
            var captured = g;
            Add(rect, button =>
            {
                if (button != 0) return;
                Press(captured);
            }, caption);
        }

        static void DrawResources(Rect row)
        {
            var map = Find.CurrentMap;
            if (map?.resourceCounter == null) return;
            resources.Clear();
            foreach (var kv in map.resourceCounter.AllCountedAmounts)
            {
                if (kv.Value <= 0 || kv.Key?.uiIcon == null) continue;
                int at = 0;
                while (at < resources.Count && string.CompareOrdinal(resources[at].defName, kv.Key.defName) < 0) at++;
                resources.Insert(at, kv.Key);
            }
            float x = row.xMax;
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                var def = resources[i];
                int count = map.resourceCounter.GetCount(def);
                string num = count.ToString();
                float tw = 36f;
                x -= tw + 20f;
                if (x < row.x) break;
                PanelDraw3D.Tex(new Rect(x, row.y + 1f, 18f, 18f), def.uiIcon, new Rect(0f, 0f, 1f, 1f), Color.white);
                PanelDraw3D.Label(new Rect(x + 20f, row.y, tw, row.height), num, GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
            }
        }

        static void DrawSheet()
        {
            contentH = 0f;
            if (!sheetOpen || openDef.NullOrEmpty()) return;
            float w = PanelDraw3D.Width;
            float top = colonistBottom + 4f;
            float height = chromeTop - top - 6f;
            if (height < 48f) return;
            sheetRect = new Rect(Margin, top, w - Margin * 2f, height);
            PanelDraw3D.Box(sheetRect, Fill, Border);
            var def = DefByName(openDef);
            string title = def != null ? def.LabelCap.ToString() : openDef;
            PanelDraw3D.Label(new Rect(sheetRect.x + 8f, sheetRect.y + 4f, sheetRect.width - 16f, 24f), title, GameFont.Small, TextAnchor.MiddleLeft, Ink, false);
            var view = new Rect(sheetRect.x + 6f, sheetRect.y + 32f, sheetRect.width - 12f, sheetRect.height - 38f);
            float y = view.y - scroll;
            float end = y;
            if (openDef == "Architect") end = DrawArchitect(view, y);
            else if (openDef == "Work") end = DrawWork(view, y);
            else if (openDef == "Schedule") end = DrawSchedule(view, y);
            else if (openDef == "Research") end = DrawResearch(view, y);
            else if (openDef == "Assign") end = DrawAssign(view, y);
            else if (openDef == "Animals" || openDef == "Wildlife") end = DrawPawns(view, y, openDef == "Animals");
            else end = DrawInfo(view, y, def);
            contentH = Mathf.Max(0f, end - (view.y - scroll));
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, contentH - view.height));
        }

        static float DrawArchitect(Rect view, float y)
        {
            CollectCats();
            if (cats.Count == 0) return y + 24f;
            if (catIndex >= cats.Count) catIndex = 0;
            float colW = 150f;
            float catY = view.y;
            for (int i = 0; i < cats.Count; i++)
            {
                var rect = new Rect(view.x, catY, colW, 26f);
                if (RowVisible(view, rect))
                {
                    bool on = i == catIndex;
                    PanelDraw3D.Box(rect, on ? SelectFill : (rect.Contains(pointer) ? HoverFill : Fill), Border);
                    PanelDraw3D.Label(rect, cats[i].LabelCap, GameFont.Tiny, TextAnchor.MiddleCenter, Ink, false);
                    int idx = i;
                    Add(rect, button =>
                    {
                        if (button != 0) return;
                        catIndex = idx;
                        scroll = 0f;
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }, cats[i].LabelCap);
                }
                catY += 28f;
            }
            float gridY = view.y - scroll;
            float gridX = view.x + colW + 8f;
            float gridW = view.xMax - gridX;
            int col = 0;
            int cols = Mathf.Max(1, (int)(gridW / (Gizmo + 4f)));
            foreach (var des in cats[catIndex].ResolvedAllowedDesignators)
            {
                if (des == null || !des.Visible) continue;
                var rect = new Rect(gridX + col * (Gizmo + 4f), gridY, Gizmo, Gizmo);
                if (RowVisible(view, rect)) DrawGizmo(rect, des);
                col++;
                if (col >= cols)
                {
                    col = 0;
                    gridY += Gizmo + 4f;
                }
            }
            if (col > 0) gridY += Gizmo + 4f;
            return gridY;
        }

        static float DrawWork(Rect view, float y)
        {
            CollectWorks();
            var map = Find.CurrentMap;
            if (map == null) return y;
            float nameW = 92f;
            int vis = Mathf.Max(1, (int)((view.width - nameW) / 40f));
            workScroll = Mathf.Clamp(workScroll, 0, Mathf.Max(0, works.Count - vis));
            float x0 = view.x + nameW;
            var head = new Rect(view.x, y, view.width, 22f);
            if (RowVisible(view, head))
            {
                Add(new Rect(view.x, y, 22f, 22f), b => { if (b == 0) workScroll = Mathf.Max(0, workScroll - vis); }, null);
                PanelDraw3D.Label(new Rect(view.x, y, 22f, 22f), "<", GameFont.Tiny, TextAnchor.MiddleCenter, Ink, false);
                Add(new Rect(view.xMax - 22f, y, 22f, 22f), b =>
                {
                    if (b == 0) workScroll = Mathf.Min(Mathf.Max(0, works.Count - vis), workScroll + vis);
                }, null);
                PanelDraw3D.Label(new Rect(view.xMax - 22f, y, 22f, 22f), ">", GameFont.Tiny, TextAnchor.MiddleCenter, Ink, false);
            }
            for (int i = 0; i < vis && workScroll + i < works.Count; i++)
            {
                var wt = works[workScroll + i];
                var rect = new Rect(x0 + i * 40f, y, 38f, 22f);
                if (RowVisible(view, rect))
                    PanelDraw3D.Label(rect, wt.labelShort, GameFont.Tiny, TextAnchor.MiddleCenter, Dim, false);
            }
            y += 24f;
            var pawns = map.mapPawns.FreeColonistsSpawned;
            for (int p = 0; p < pawns.Count; p++)
            {
                var pawn = pawns[p];
                if (pawn?.workSettings == null || !pawn.workSettings.EverWork) continue;
                var nameR = new Rect(view.x, y, nameW - 4f, 26f);
                if (RowVisible(view, nameR))
                    PanelDraw3D.Label(nameR, pawn.LabelShort, GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
                for (int i = 0; i < vis && workScroll + i < works.Count; i++)
                {
                    var wt = works[workScroll + i];
                    var rect = new Rect(x0 + i * 40f, y, 36f, 26f);
                    if (!RowVisible(view, rect)) continue;
                    bool off = pawn.WorkTypeIsDisabled(wt);
                    int pri = pawn.workSettings.GetPriority(wt);
                    var bg = off ? new Color(0.25f, 0.12f, 0.12f, 0.9f) : Fill;
                    PanelDraw3D.Box(rect, rect.Contains(pointer) ? HoverFill : bg, Border);
                    if (!off && pri > 0)
                    {
                        string mark = Find.PlaySettings.useWorkPriorities ? pri.ToString() : "✓";
                        PanelDraw3D.Label(rect, mark, GameFont.Tiny, TextAnchor.MiddleCenter, WidgetsWork.ColorOfPriority(pri), false);
                    }
                    var captured = pawn;
                    var work = wt;
                    Add(rect, button => CycleWork(captured, work, button), wt.labelShort);
                }
                y += 28f;
            }
            return y;
        }

        static float DrawSchedule(Rect view, float y)
        {
            EnsureHours();
            var map = Find.CurrentMap;
            if (map == null || hours.Count == 0) return y;
            float nameW = 92f;
            float cell = Mathf.Max(18f, (view.width - nameW) / 24f);
            var pawns = map.mapPawns.FreeColonistsSpawned;
            for (int p = 0; p < pawns.Count; p++)
            {
                var pawn = pawns[p];
                if (pawn?.timetable == null) continue;
                var nameR = new Rect(view.x, y, nameW - 4f, 24f);
                if (RowVisible(view, nameR))
                    PanelDraw3D.Label(nameR, pawn.LabelShort, GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
                for (int h = 0; h < 24; h++)
                {
                    var rect = new Rect(view.x + nameW + h * cell, y, cell - 1f, 24f);
                    if (!RowVisible(view, rect)) continue;
                    var ta = pawn.timetable.GetAssignment(h);
                    var bg = ta != null ? ta.color : Fill;
                    bg.a = 0.9f;
                    PanelDraw3D.Fill(rect, rect.Contains(pointer) ? Color.Lerp(bg, Color.white, 0.25f) : bg);
                    if (ta != null)
                        PanelDraw3D.Label(rect, ta.label, GameFont.Tiny, TextAnchor.MiddleCenter, Color.black, false);
                    int hour = h;
                    var captured = pawn;
                    Add(rect, button =>
                    {
                        if (button != 0 || captured.timetable == null) return;
                        var cur = captured.timetable.GetAssignment(hour);
                        int idx = 0;
                        for (int i = 0; i < hours.Count; i++) if (hours[i] == cur) idx = i;
                        captured.timetable.SetAssignment(hour, hours[(idx + 1) % hours.Count]);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }, (h).ToString());
                }
                y += 26f;
            }
            return y;
        }

        static float DrawResearch(Rect view, float y)
        {
            CollectProjects();
            var current = Find.ResearchManager?.GetProject();
            for (int i = 0; i < projects.Count; i++)
            {
                var proj = projects[i];
                var rect = new Rect(view.x, y, view.width, 26f);
                if (RowVisible(view, rect))
                {
                    bool on = proj == current;
                    PanelDraw3D.Box(rect, on ? SelectFill : (rect.Contains(pointer) ? HoverFill : Fill), Border);
                    int pct = Mathf.RoundToInt(proj.ProgressPercent * 100f);
                    PanelDraw3D.Label(rect.ContractedBy(4f), proj.LabelCap + "  " + pct + "%", GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
                    var captured = proj;
                    Add(rect, button =>
                    {
                        if (button != 0 || captured.IsFinished) return;
                        Find.ResearchManager.SetCurrentProject(captured);
                        SoundDefOf.Click.PlayOneShotOnCamera();
                    }, proj.LabelCap);
                }
                y += 28f;
            }
            return y;
        }

        static float DrawAssign(Rect view, float y)
        {
            var map = Find.CurrentMap;
            if (map == null) return y;
            var pawns = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                var pawn = pawns[i];
                if (pawn?.playerSettings == null) continue;
                var nameR = new Rect(view.x, y, 100f, 26f);
                if (RowVisible(view, nameR))
                    PanelDraw3D.Label(nameR, pawn.LabelShort, GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
                var area = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
                string areaLabel = area != null ? area.Label : "NoAreaAllowed".Translate();
                var areaR = new Rect(view.x + 104f, y, 180f, 26f);
                if (RowVisible(view, areaR))
                {
                    PanelDraw3D.Box(areaR, areaR.Contains(pointer) ? HoverFill : Fill, Border);
                    PanelDraw3D.Label(areaR.ContractedBy(3f), areaLabel, GameFont.Tiny, TextAnchor.MiddleCenter, Ink, false);
                    var captured = pawn;
                    Add(areaR, button => CycleArea(captured, button), areaLabel);
                }
                var outfit = pawn.outfits?.CurrentApparelPolicy;
                string outfitLabel = outfit != null ? outfit.label : "";
                var outR = new Rect(view.x + 290f, y, 180f, 26f);
                if (RowVisible(view, outR) && pawn.outfits != null)
                {
                    PanelDraw3D.Box(outR, outR.Contains(pointer) ? HoverFill : Fill, Border);
                    PanelDraw3D.Label(outR.ContractedBy(3f), outfitLabel, GameFont.Tiny, TextAnchor.MiddleCenter, Ink, false);
                    var captured = pawn;
                    Add(outR, button => CycleOutfit(captured, button), outfitLabel);
                }
                y += 28f;
            }
            return y;
        }

        static float DrawPawns(Rect view, float y, bool colony)
        {
            var map = Find.CurrentMap;
            if (map == null) return y;
            var all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                var pawn = all[i];
                if (pawn == null || !pawn.RaceProps.Animal) continue;
                bool mine = pawn.Faction == Faction.OfPlayer;
                if (colony != mine) continue;
                var rect = new Rect(view.x, y, view.width, 26f);
                if (RowVisible(view, rect))
                {
                    PanelDraw3D.Box(rect, rect.Contains(pointer) ? HoverFill : Fill, Border);
                    PanelDraw3D.Label(rect.ContractedBy(4f), pawn.LabelCap, GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
                    var captured = pawn;
                    Add(rect, button =>
                    {
                        if (button != 0 || captured.Destroyed) return;
                        Find.Selector.Select(captured, false, true);
                        SoundDefOf.Tick_High.PlayOneShotOnCamera();
                        if (captured.Spawned)
                        {
                            hasPending = true;
                            pendingFocus = new Vector3(captured.Position.x + 0.5f, 0f, captured.Position.z + 0.5f);
                        }
                    }, pawn.LabelShort);
                }
                y += 28f;
            }
            return y;
        }

        static float DrawInfo(Rect view, float y, MainButtonDef def)
        {
            string body = def != null ? def.description : "";
            var rect = new Rect(view.x, y, view.width, 180f);
            if (RowVisible(view, rect))
                PanelDraw3D.Label(rect, body, GameFont.Small, TextAnchor.UpperLeft, Ink, true);
            return y + 180f;
        }

        static void DrawMenu()
        {
            if (menu.Count == 0) return;
            float w = 280f;
            float row = 26f;
            float x = Mathf.Clamp(pointer.x, Margin, PanelDraw3D.Width - w - Margin);
            float y = Mathf.Clamp(pointer.y, colonistBottom, PanelDraw3D.Height - row * menu.Count - Margin);
            for (int i = 0; i < menu.Count; i++)
            {
                var opt = menu[i];
                var rect = new Rect(x, y + i * row, w, row - 1f);
                PanelDraw3D.Box(rect, opt.Disabled ? new Color(0.15f, 0.15f, 0.15f, 0.96f) : (rect.Contains(pointer) ? HoverFill : Fill), Border);
                PanelDraw3D.Label(rect.ContractedBy(4f), opt.Label, GameFont.Tiny, TextAnchor.MiddleLeft, opt.Disabled ? Dim : Ink, false);
                var captured = opt;
                Add(rect, button =>
                {
                    if (button != 0 || captured.Disabled || captured.action == null) return;
                    menu.Clear();
                    captured.action();
                    SoundDefOf.Click.PlayOneShotOnCamera();
                    StealMenus();
                }, opt.Label, true);
            }
        }

        static void DrawHover()
        {
            if (hover.NullOrEmpty()) return;
            var rect = new Rect(pointer.x + 14f, pointer.y + 16f, 240f, 22f);
            if (rect.xMax > PanelDraw3D.Width) rect.x = PanelDraw3D.Width - rect.width;
            if (rect.yMax > PanelDraw3D.Height) rect.y = pointer.y - 24f;
            PanelDraw3D.Box(rect, Fill, Border);
            PanelDraw3D.Label(rect.ContractedBy(3f), hover, GameFont.Tiny, TextAnchor.MiddleLeft, Ink, false);
        }

        static void Toggle(MainButtonDef def)
        {
            menu.Clear();
            if (sheetOpen && openDef == def.defName)
            {
                sheetOpen = false;
                openDef = null;
                if (def.defName == "Architect") Find.DesignatorManager.Deselect();
                return;
            }
            sheetOpen = true;
            openDef = def.defName;
            scroll = 0f;
            if (def.defName != "Architect") Find.DesignatorManager.Deselect();
        }

        static void Press(Gizmo g)
        {
            if (g == null) return;
            if (g.Disabled)
            {
                if (!g.disabledReason.NullOrEmpty())
                    Messages.Message(g.disabledReason, MessageTypeDefOf.RejectInput, false);
                return;
            }
            var prev = Event.current;
            Event.current = new Event { type = EventType.MouseDown, button = 0 };
            try { g.ProcessInput(Event.current); }
            finally { Event.current = prev; }
            if (Find.Targeter.IsTargeting && g is Command cmd) armed = cmd.LabelCap;
            StealMenus();
        }

        static void PumpDesignator(EventType type)
        {
            var prev = Event.current;
            Event.current = new Event { type = type, button = 0 };
            try { Find.DesignatorManager.ProcessInputEvents(); }
            finally { Event.current = prev; }
        }

        static void ConfirmTarget(Map map, IntVec3 cell)
        {
            EnsureTargetFields();
            var targeter = Find.Targeter;
            if (!targeter.IsTargeting || !cell.InBounds(map)) return;
            var target = PickTarget(map, cell);
            if (!target.IsValid)
            {
                SoundDefOf.Designate_Failed.PlayOneShotOnCamera();
                return;
            }
            var src = targeter.targetingSource;
            if (src != null)
            {
                if (!src.ValidateTarget(target)) return;
                src.OrderForceTarget(target);
            }
            else
            {
                var validator = validField?.GetValue(targeter) as Func<LocalTargetInfo, bool>;
                if (validator != null && !validator(target)) return;
                var action = actField?.GetValue(targeter) as Action<LocalTargetInfo>;
                action?.Invoke(target);
            }
            SoundDefOf.Tick_High.PlayOneShotOnCamera();
            armed = null;
            targeter.StopTargeting();
        }

        static LocalTargetInfo PickTarget(Map map, IntVec3 cell)
        {
            TargetingParameters p = null;
            var src = Find.Targeter.targetingSource;
            if (src != null) p = src.targetParams;
            else if (paramField != null) p = paramField.GetValue(Find.Targeter) as TargetingParameters;
            var list = map.thingGrid.ThingsListAtFast(cell);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var t = list[i];
                if (t == null || t.Destroyed) continue;
                if (p == null || p.CanTarget(new TargetInfo(t))) return t;
            }
            if (p == null || p.canTargetLocations) return new LocalTargetInfo(cell);
            return LocalTargetInfo.Invalid;
        }

        static void CycleWork(Pawn pawn, WorkTypeDef wt, int button)
        {
            if (pawn?.workSettings == null || !pawn.workSettings.EverWork || pawn.WorkTypeIsDisabled(wt)) return;
            if (!Find.PlaySettings.useWorkPriorities)
            {
                if (button != 0) return;
                int cur = pawn.workSettings.GetPriority(wt);
                pawn.workSettings.SetPriority(wt, cur > 0 ? 0 : 3);
                (cur > 0 ? SoundDefOf.Checkbox_TurnedOff : SoundDefOf.Checkbox_TurnedOn).PlayOneShotOnCamera();
                return;
            }
            int pri = pawn.workSettings.GetPriority(wt);
            if (button == 0)
            {
                pri--;
                if (pri < 0) pri = 4;
            }
            else
            {
                pri++;
                if (pri > 4) pri = 0;
            }
            pawn.workSettings.SetPriority(wt, pri);
            SoundDefOf.DragSlider.PlayOneShotOnCamera();
        }

        static void CycleArea(Pawn pawn, int button)
        {
            var map = pawn.MapHeld;
            if (map == null || pawn.playerSettings == null) return;
            var areas = map.areaManager.AllAreas;
            var cur = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
            int idx = 0;
            if (cur != null)
            {
                int found = areas.IndexOf(cur);
                idx = found >= 0 ? found + 1 : 0;
            }
            int n = areas.Count + 1;
            idx = button == 0 ? (idx + 1) % n : (idx - 1 + n) % n;
            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = idx == 0 ? null : areas[idx - 1];
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        static void CycleOutfit(Pawn pawn, int button)
        {
            if (pawn.outfits == null || Current.Game?.outfitDatabase == null) return;
            var all = Current.Game.outfitDatabase.AllOutfits;
            int n = 0;
            int cur = -1;
            ApparelPolicy current = pawn.outfits.CurrentApparelPolicy;
            foreach (var o in all)
            {
                if (o == current) cur = n;
                n++;
            }
            if (n == 0) return;
            int idx = cur < 0 ? 0 : (button == 0 ? (cur + 1) % n : (cur - 1 + n) % n);
            int i = 0;
            foreach (var o in all)
            {
                if (i == idx)
                {
                    pawn.outfits.CurrentApparelPolicy = o;
                    break;
                }
                i++;
            }
            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        static void StealMenus()
        {
            if (menuField == null) menuField = AccessTools.Field(typeof(FloatMenu), "options");
            if (Find.WindowStack == null || menuField == null) return;
            winBuf.Clear();
            var wins = Find.WindowStack.Windows;
            for (int i = 0; i < wins.Count; i++) winBuf.Add(wins[i]);
            for (int i = 0; i < winBuf.Count; i++)
            {
                if (!(winBuf[i] is FloatMenu fm)) continue;
                if (menuField.GetValue(fm) is List<FloatMenuOption> list)
                {
                    menu.Clear();
                    for (int k = 0; k < list.Count; k++) menu.Add(list[k]);
                }
                Find.WindowStack.TryRemove(fm, false);
            }
        }

        static void BackOut()
        {
            if (menu.Count > 0) { menu.Clear(); return; }
            if (Find.Targeter.IsTargeting) { armed = null; Find.Targeter.StopTargeting(); return; }
            if (Find.DesignatorManager.SelectedDesignator != null) { Find.DesignatorManager.Deselect(); return; }
            if (sheetOpen) { sheetOpen = false; openDef = null; }
        }

        static void ApplyPending(ref Vector3 focus)
        {
            if (!hasPending) return;
            focus = pendingFocus;
            hasPending = false;
            if (pendingColonist != null)
            {
                MapScreen3D.NotePawn(pendingColonist);
                pendingColonist = null;
            }
        }

        static void CollectGizmos()
        {
            gizmos.Clear();
            grouped.Clear();
            var sel = Find.Selector.SelectedObjects;
            if (sel == null) return;
            for (int i = 0; i < sel.Count; i++)
            {
                if (sel[i] is ISelectable selectable)
                {
                    foreach (var g in selectable.GetGizmos())
                        if (g != null) gizmos.Add(g);
                }
                if (sel[i] is Thing thing && Find.ReverseDesignatorDatabase != null)
                {
                    var all = Find.ReverseDesignatorDatabase.AllDesignators;
                    for (int k = 0; k < all.Count; k++)
                    {
                        var g = all[k].CreateReverseDesignationGizmo(thing);
                        if (g != null) gizmos.Add(g);
                    }
                }
            }
            for (int i = 0; i < gizmos.Count; i++)
            {
                var g = gizmos[i];
                if (!g.Visible) continue;
                bool have = false;
                for (int j = 0; j < grouped.Count; j++)
                {
                    if (grouped[j].GroupsWith(g)) { have = true; break; }
                }
                if (have) continue;
                int at = 0;
                while (at < grouped.Count && grouped[at].Order <= g.Order) at++;
                grouped.Insert(at, g);
            }
        }

        static void CollectButtons()
        {
            buttons.Clear();
            foreach (var d in DefDatabase<MainButtonDef>.AllDefs)
            {
                if (d?.Worker == null || !d.Worker.Visible) continue;
                int at = 0;
                while (at < buttons.Count && buttons[at].order <= d.order) at++;
                buttons.Insert(at, d);
            }
        }

        static void CollectCats()
        {
            cats.Clear();
            foreach (var d in DefDatabase<DesignationCategoryDef>.AllDefs)
            {
                if (d == null || !d.Visible) continue;
                int at = 0;
                while (at < cats.Count && cats[at].order <= d.order) at++;
                cats.Insert(at, d);
            }
        }

        static void CollectWorks()
        {
            works.Clear();
            foreach (var d in DefDatabase<WorkTypeDef>.AllDefs)
            {
                if (d == null || !d.VisibleCurrently) continue;
                int at = 0;
                while (at < works.Count && works[at].naturalPriority >= d.naturalPriority) at++;
                works.Insert(at, d);
            }
        }

        static void EnsureHours()
        {
            if (hours.Count > 0) return;
            foreach (var d in DefDatabase<TimeAssignmentDef>.AllDefs)
                if (d != null) hours.Add(d);
        }

        static void CollectProjects()
        {
            projects.Clear();
            var current = Find.ResearchManager?.GetProject();
            foreach (var d in DefDatabase<ResearchProjectDef>.AllDefs)
            {
                if (d == null || d.IsHidden) continue;
                if (!d.CanStartNow && d != current && !d.IsFinished) continue;
                if (d.IsFinished && d != current) continue;
                int at = 0;
                while (at < projects.Count && string.CompareOrdinal(projects[at].label, d.label) < 0) at++;
                projects.Insert(at, d);
            }
        }

        static MainButtonDef DefByName(string name)
        {
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].defName == name) return buttons[i];
            return DefDatabase<MainButtonDef>.GetNamedSilentFail(name);
        }

        static string BottomTitle()
        {
            var des = Find.DesignatorManager.SelectedDesignator;
            if (des != null) return des.LabelCap;
            if (Find.Targeter.IsTargeting && !armed.NullOrEmpty()) return armed;
            var sel = Find.Selector.SelectedObjects;
            if (sel == null || sel.Count == 0) return "";
            string name = sel[0] is Thing t ? t.LabelCap : sel[0].ToString();
            if (sel.Count > 1) name += " ×" + sel.Count;
            return name;
        }

        static bool RowVisible(Rect view, Rect row)
        {
            return row.yMax > view.y && row.y < view.yMax;
        }

        static Hit TopHit()
        {
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Contains(pointer.x, pointer.y)) return hits[i];
            return null;
        }

        static void Add(Rect rect, Action<int> act, string tip, bool menuHit = false)
        {
            if (tip != null && rect.Contains(pointer)) hover = tip;
            hits.Add(new Hit { x = rect.x, y = rect.y, w = rect.width, h = rect.height, act = act, tip = tip, menu = menuHit });
        }

        static void EnsureTargetFields()
        {
            if (actField != null) return;
            actField = AccessTools.Field(typeof(Targeter), "action");
            paramField = AccessTools.Field(typeof(Targeter), "targetParams");
            validField = AccessTools.Field(typeof(Targeter), "targetValidator");
        }
    }
}
