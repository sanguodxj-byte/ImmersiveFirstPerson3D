// AI生成
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 指挥面板：查看期让原版 UIRoot 按终端分辨率排版并处理点击，
    // 画到主画面的那一层记下来，主画面本身涂成全透明，下一帧贴回屏幕纹理。
    // 贴图按原样 1:1。字形按字体网格的四个角贴，不用 DrawTexture 的矩形 UV。
    public static class CommandPanel3D
    {
        const int StampCap = 12288;

        // 1 表示按纹理像素排版，头像、底栏、检查文字和现在屏幕上的格子一致。
        // 工作、日程这类默认 1010×684 的分页另在 CapMainTab 里限高限宽，避免再铺满终端。
        const float UiShrink = 1f;

        struct Stamp
        {
            public int order;
            public float nx, ny, nw, nh;
            public Texture tex;
            public Rect src;
            public Color tint;
            public int l, r, t, b;
        }

        // 四个角各自的 UV。DrawTexture 会把较小的 U 放在左边，字体图集经常是反的，整字就会镜像。
        struct GlyphDraw
        {
            public int order;
            public Texture tex;
            public Color tint;
            public float x0, y0, x1, y1, x2, y2, x3, y3;
            public float u0, v0, u1, v1, u2, v2, u3, v3;
        }

        static readonly List<Stamp> recording = new List<Stamp>(512);
        static readonly List<Stamp> committed = new List<Stamp>(512);
        static readonly List<GlyphDraw> glyphRecording = new List<GlyphDraw>(1024);
        static readonly List<GlyphDraw> glyphCommitted = new List<GlyphDraw>(1024);
        static readonly List<UIVertex> glyphs = new List<UIVertex>(256);
        static readonly Vector2[] glyphCorner = new Vector2[4];
        static readonly Vector2[] glyphCornerUv = new Vector2[4];
        static TextGenerator textGen;
        static int drawSeq;
        static Material glyphMat;
        static Shader glyphShader;
        static Font glyphFont;

        static bool redirecting;
        static bool pointerOnScreen;
        static Vector2 pointerUv;
        static float layoutScale = 1f;
        static int hideDepth;
        static Color savedGuiColor;
        static bool logged;
        static bool loggedCap;
        static MethodInfo visibleRectGetter;
        static MethodInfo unclipMethod;
        static bool visibleRectTried;
        static int clipFixWindow = int.MinValue;
        static int clipFixFrame = -1;
        static int clipFixType = -1;
        static Vector2 clipFixAdd;
        static FieldInfo pawnTableField;
        static MethodInfo pawnExtraBottom;
        static MethodInfo pawnExtraTop;
        static MethodInfo pawnMargin;

        public static void Clear()
        {
            recording.Clear();
            committed.Clear();
            glyphRecording.Clear();
            glyphCommitted.Clear();
            drawSeq = 0;
        }

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(UIRootPrefix)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(UIRootPostfix)));
            harmony.Patch(AccessTools.Method(typeof(UI), nameof(UI.ApplyUIScale)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(ApplyScaleWhileViewing)));
            var innerWindow = AccessTools.Method(typeof(Window), "InnerWindowOnGUI", new[] { typeof(int) });
            if (innerWindow != null)
                harmony.Patch(innerWindow, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(RememberWindowClip)));
            else
                Log.Warning(ModEntry.LogPrefix + "command panel missed window clip");
            harmony.Patch(AccessTools.Method(typeof(UI), nameof(UI.GUIToScreenPoint)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(GuiToScreenWhileViewing)));
            harmony.Patch(AccessTools.Method(typeof(UI), nameof(UI.UIToMapPosition), new[] { typeof(Vector2) }),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MapPositionWhileViewing)));
            harmony.Patch(AccessTools.Method(typeof(UI), nameof(UI.MapToUIPosition)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MapToUIWhileViewing)));
            var placeCost = AccessTools.Method(typeof(Designator_Build), "DrawPlaceMouseAttachments");
            if (placeCost != null)
                harmony.Patch(placeCost, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(CostIconsAfterStuffMenu)));
            else
                Log.Warning(ModEntry.LogPrefix + "command panel missed place-cost attachments");
            harmony.Patch(AccessTools.Method(typeof(FloatMenu), nameof(FloatMenu.DoWindowContents)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(KeepFloatMenu)));
            var tabPlace = AccessTools.Method(typeof(MainTabWindow), "SetInitialSizeAndPosition");
            if (tabPlace != null)
                harmony.Patch(tabPlace, postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(CapMainTab)));
            else
                Log.Warning(ModEntry.LogPrefix + "command panel missed main tab placement");
            var onScreen = new HarmonyMethod(typeof(CommandPanel3D), nameof(MapToolsOnlyOnScreen));
            harmony.Patch(AccessTools.Method(typeof(DesignatorManager), nameof(DesignatorManager.ProcessInputEvents)),
                prefix: onScreen);
            harmony.Patch(AccessTools.Method(typeof(Targeter), nameof(Targeter.ProcessInputEvents)),
                prefix: onScreen);
            harmony.Patch(AccessTools.PropertyGetter(typeof(UI), nameof(UI.MousePositionOnUI)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MouseOnUIPrefix)));
            // 左键留给终端拖地图。右键下令、取消选择仍走原版。
            var selectorClicks = AccessTools.Method(typeof(Selector), "HandleMapClicks");
            if (selectorClicks != null)
                harmony.Patch(selectorClicks, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(SelectorClicksWhileViewing)));
            else
                Log.Warning(ModEntry.LogPrefix + "command panel missed Selector.HandleMapClicks");
            var drawLocs = AccessTools.Method(typeof(ColonistBarDrawLocsFinder), "CalculateDrawLocs",
                new[] { typeof(List<Vector2>), typeof(float).MakeByRefType(), typeof(int) });
            if (drawLocs != null)
                harmony.Patch(drawLocs, postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(LeftAlignColonists)));
            else
                Log.Warning(ModEntry.LogPrefix + "command panel missed colonist draw locs");

            // 先记账再把颜色参数清掉，避免记下的是已经透明的那一笔。
            var hideColor = new HarmonyMethod(typeof(CommandPanel3D), nameof(ZeroDrawColor)) { priority = Priority.Last };
            var texPre = new HarmonyMethod(typeof(CommandPanel3D), nameof(TexPrefix)) { priority = Priority.First };
            var texPost = new HarmonyMethod(typeof(CommandPanel3D), nameof(TexPostfix));
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(GUI)))
            {
                if (m.Name != "DrawTexture" && m.Name != "DrawTextureWithTexCoords") continue;
                var ps = m.GetParameters();
                if (ps.Length < 2 || ps[0].ParameterType != typeof(Rect) || ps[1].ParameterType != typeof(Texture))
                    continue;
                try
                {
                    harmony.Patch(m, prefix: texPre, postfix: texPost);
                    if (HasColorArg(m)) harmony.Patch(m, prefix: hideColor);
                }
                catch (Exception e)
                {
                    Log.Warning(ModEntry.LogPrefix + "command panel skipped " + m + ": " + e.Message);
                }
            }

            var stylePre = new HarmonyMethod(typeof(CommandPanel3D), nameof(StylePrefix)) { priority = Priority.First };
            var stylePost = new HarmonyMethod(typeof(CommandPanel3D), nameof(TexPostfix));
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(GUIStyle)))
            {
                if (m.Name != "Draw") continue;
                var ps = m.GetParameters();
                if (ps.Length < 1 || ps[0].ParameterType != typeof(Rect)) continue;
                try { harmony.Patch(m, prefix: stylePre, postfix: stylePost); }
                catch (Exception e)
                {
                    Log.Warning(ModEntry.LogPrefix + "command panel skipped " + m + ": " + e.Message);
                }
            }
        }

        static bool HasColorArg(MethodBase m)
        {
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].ParameterType == typeof(Color) && ps[i].Name == "color") return true;
            return false;
        }

        // 原版把殖民者头像排在屏幕正中。终端上改从左边起排，点选矩形跟着同一份坐标走。
        static void LeftAlignColonists(List<Vector2> outDrawLocs)
        {
            if (!MapScreen3D.Viewing || outDrawLocs == null || outDrawLocs.Count == 0) return;
            float min = outDrawLocs[0].x;
            for (int i = 1; i < outDrawLocs.Count; i++)
                if (outDrawLocs[i].x < min) min = outDrawLocs[i].x;
            float dx = 8f - min;
            if (dx > -0.5f && dx < 0.5f) return;
            for (int i = 0; i < outDrawLocs.Count; i++)
            {
                var p = outDrawLocs[i];
                p.x += dx;
                outDrawLocs[i] = p;
            }
        }

        // 选择框会吃掉每一次没被按钮用掉的左键。查看期跳过左键，右键菜单保留。
        static bool SelectorClicksWhileViewing()
        {
            if (!MapScreen3D.Viewing) return true;
            if (!pointerOnScreen) return false;
            var e = Event.current;
            if (e == null || e.button != 0) return true;
            if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
                return false;
            return true;
        }

        // 光标不在终端上时，设计器和目标选取不该打到地图外的某一格。
        static bool MapToolsOnlyOnScreen()
        {
            if (!MapScreen3D.Viewing) return true;
            return pointerOnScreen;
        }

        // ---- 指针：整块屏幕都映射到原版 UI。没打中屏幕时把鼠标挪走。 ----

        public static void NotePointer(bool hit, Vector2 uv)
        {
            pointerOnScreen = hit;
            if (hit) pointerUv = uv;
        }

        static void UIRootPrefix()
        {
            redirecting = false;
            if (!MapScreen3D.Viewing) return;
            var rt = GodView3D.ScreenTexture as RenderTexture;
            if (rt == null || !rt.IsCreated()) return;
            redirecting = true;
            AimPointer();
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                recording.Clear();
                glyphRecording.Clear();
                drawSeq = 0;
            }
        }

        static void UIRootPostfix()
        {
            if (redirecting && Event.current != null && Event.current.type == EventType.Repaint)
                Commit();
            if (redirecting)
                MapScreen3D.OnGuiGesture(pointerOnScreen, pointerUv);
            redirecting = false;
            if (hideDepth != 0)
            {
                hideDepth = 0;
                GUI.color = savedGuiColor;
            }
        }

        // 查看期的排版尺寸。Update 和 OnGUI 都写同一对数字，避免窗口每帧被当成改了分辨率、材料菜单跟着鼠标重摆。
        public static void HoldVirtualScreen()
        {
            if (!MapScreen3D.Viewing) return;
            VirtualPixels(out int w, out int h);
            UI.screenWidth = w;
            UI.screenHeight = h;
        }

        // Root.OnGUI 会调 ApplyUIScale。查看期虚拟像素等于屏幕纹理，矩阵保持单位阵。
        // Prefs.UIScale 是把 4K 显示器收成逻辑分辨率用的。终端纹理已经是排版像素，
        // 再套这层缩放会把窗口画到左上角。大分页另由 CapMainTab 限尺寸。
        static bool ApplyScaleWhileViewing()
        {
            if (!MapScreen3D.Viewing)
            {
                layoutScale = 1f;
                return true;
            }
            HoldVirtualScreen();
            layoutScale = 1f;
            GUI.matrix = Matrix4x4.identity;
            return false;
        }

        static void VirtualPixels(out int w, out int h)
        {
            var rt = GodView3D.ScreenTexture as RenderTexture;
            int rw = rt != null && rt.width > 0 ? rt.width : 1536;
            int rh = rt != null && rt.height > 0 ? rt.height : 1086;
            float shrink = UiShrink > 0.2f && UiShrink <= 1f ? UiShrink : 1f;
            w = Mathf.Max(rw, Mathf.RoundToInt(rw / shrink));
            h = Mathf.Max(rh, Mathf.RoundToInt(rh / shrink));
        }

        // 窗口矩形在虚拟 UI 像素里。GUIToScreenPoint 会夹到物理屏幕，底栏和材料菜单就对不上点击。
        static bool GuiToScreenWhileViewing(Vector2 guiPoint, ref Vector2 __result)
        {
            if (!MapScreen3D.Viewing) return true;
            __result = GuiToVirtual(guiPoint);
            return false;
        }

        // MouseCell、设计器和目标选取都从这里把指针换成地图坐标。主相机是第一人称，不能拿来反投影。
        static bool MapPositionWhileViewing(Vector2 screenLoc, ref Vector3 __result)
        {
            if (!MapScreen3D.Viewing) return true;
            if (!pointerOnScreen)
            {
                __result = new Vector3(-1000f, 0f, -1000f);
                return false;
            }
            VirtualSize(out int w, out int h);
            if (w < 1 || h < 1) return true;
            if (!MapScreen3D.TryMapPoint(new Vector2(screenLoc.x / w, screenLoc.y / h), out var point))
                return true;
            point.y = 0f;
            __result = point;
            return false;
        }

        // 拖框和同类建筑的间距数字走 MapToUIPosition。主相机是第一人称，要按俯视相机折回虚拟像素。
        static bool MapToUIWhileViewing(Vector3 v, ref Vector2 __result)
        {
            if (!MapScreen3D.Viewing) return true;
            var cam = GodView3D.Camera;
            if (cam == null) return true;
            Vector3 vp = cam.WorldToViewportPoint(v);
            if (vp.z < 0f)
            {
                __result = new Vector2(-10000f, -10000f);
                return false;
            }
            VirtualSize(out int w, out int h);
            __result = new Vector2(vp.x * w, (1f - vp.y) * h);
            return false;
        }

        // 材料还没选定、菜单还开着时，造价图标画在鼠标上，盖住菜单又点不中。菜单关掉后再显示造价。
        static bool CostIconsAfterStuffMenu(Designator_Build __instance)
        {
            if (!MapScreen3D.Viewing || __instance == null) return true;
            if (Find.WindowStack == null || Find.WindowStack.FloatMenu == null) return true;
            var thing = __instance.PlacingDef as ThingDef;
            if (thing != null && thing.MadeFromStuff && __instance.StuffDefRaw == null)
                return false;
            return true;
        }

        // 菜单用窗口内鼠标到矩形的距离判断要不要自己关掉。坐标一对不上就会立刻消失。
        static void KeepFloatMenu(FloatMenu __instance)
        {
            if (MapScreen3D.Viewing && __instance != null)
                __instance.vanishIfMouseDistant = false;
        }

        // 建筑分类栏本来就窄，不动。工作、日程、研究这些默认铺大半块屏幕的分页，收成终端的一半左右。
        static void CapMainTab(MainTabWindow __instance)
        {
            if (!MapScreen3D.Viewing || __instance == null) return;
            if (UI.screenWidth < 8 || UI.screenHeight < 8) return;
            Rect r = __instance.windowRect;
            if (r.width <= 280f) return;
            float maxW = UI.screenWidth * 0.48f;
            float maxH = (UI.screenHeight - 35f) * 0.56f;
            bool changed = false;
            if (r.width > maxW) { r.width = maxW; changed = true; }
            if (r.height > maxH) { r.height = maxH; changed = true; }
            if (!changed) return;
            if (__instance.Anchor == MainTabWindowAnchor.Left) r.x = 0f;
            else r.x = UI.screenWidth - r.width;
            r.y = (UI.screenHeight - 35f) - r.height;
            __instance.windowRect = r;
            if (__instance is MainTabWindow_PawnTable)
                FitPawnTable(__instance, r);
        }

        // 工作表按创建时的整屏宽度排好列，窗口收小后右边列会被裁掉且不能横滑。按新窗口重算列宽。
        static void FitPawnTable(MainTabWindow tab, Rect window)
        {
            if (pawnTableField == null)
            {
                pawnTableField = AccessTools.Field(typeof(MainTabWindow_PawnTable), "table");
                pawnExtraBottom = AccessTools.PropertyGetter(typeof(MainTabWindow_PawnTable), "ExtraBottomSpace");
                pawnExtraTop = AccessTools.PropertyGetter(typeof(MainTabWindow_PawnTable), "ExtraTopSpace");
                pawnMargin = AccessTools.PropertyGetter(typeof(MainTabWindow_PawnTable), "Margin");
            }
            var table = pawnTableField?.GetValue(tab) as PawnTable;
            if (table == null) return;
            float margin = pawnMargin != null ? (float)pawnMargin.Invoke(tab, null) : 6f;
            float bottom = pawnExtraBottom != null ? (float)pawnExtraBottom.Invoke(tab, null) : 53f;
            float top = pawnExtraTop != null ? (float)pawnExtraTop.Invoke(tab, null) : 0f;
            int maxW = Mathf.Max(120, Mathf.FloorToInt(window.width - margin * 2f));
            int maxH = Mathf.Max(80, Mathf.FloorToInt(window.height - bottom - top - margin * 2f));
            table.SetMinMaxSize(120, maxW, 0, maxH);
        }

        static void AimPointer()
        {
            var cam = CameraRig3D.Cam;
            pointerOnScreen = cam != null && HandTerminal3D.TryScreenUV(cam, 1f, out pointerUv);
            if (Event.current == null) return;
            if (!pointerOnScreen)
            {
                Event.current.mousePosition = new Vector2(-10000f, -10000f);
                return;
            }
            VirtualSize(out int w, out int h);
            Event.current.mousePosition = new Vector2(pointerUv.x * w, (1f - pointerUv.y) * h);
        }

        static bool MouseOnUIPrefix(ref Vector2 __result)
        {
            if (!MapScreen3D.Viewing) return true;
            if (!pointerOnScreen)
            {
                __result = new Vector2(-10000f, -10000f);
                return false;
            }
            VirtualSize(out int w, out int h);
            __result = new Vector2(pointerUv.x * w, pointerUv.y * h);
            return false;
        }

        static void VirtualSize(out int w, out int h)
        {
            if (MapScreen3D.Viewing)
            {
                VirtualPixels(out w, out h);
                return;
            }
            if (UI.screenWidth > 0 && UI.screenHeight > 0)
            {
                w = UI.screenWidth;
                h = UI.screenHeight;
                return;
            }
            w = 1536;
            h = 1086;
        }

        // ---- 记账：Repaint 时把将要画到主画面的内容记下来，再把主画面这笔画成全透明。 ----

        static void TexPrefix(object[] __args)
        {
            if (!redirecting) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            try { RecordDrawTexture(__args); }
            catch (Exception e) { LogOnce(e); }
            PushHide();
        }

        static void StylePrefix(GUIStyle __instance, object[] __args, MethodBase __originalMethod)
        {
            if (!redirecting) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            try
            {
                if (__instance != null && __args != null && __args.Length > 0 && __args[0] is Rect position)
                    RecordStyle(__instance, position, __args, __originalMethod);
            }
            catch (Exception e) { LogOnce(e); }
            PushHide();
        }

        static void TexPostfix() => PopHide();

        static void ZeroDrawColor(ref Color color)
        {
            if (!redirecting) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            color.a = 0f;
        }

        static void PushHide()
        {
            if (hideDepth == 0)
            {
                savedGuiColor = GUI.color;
                var c = savedGuiColor;
                c.a = 0f;
                GUI.color = c;
            }
            hideDepth++;
        }

        static void PopHide()
        {
            if (!redirecting) return;
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (hideDepth <= 0) return;
            hideDepth--;
            if (hideDepth == 0) GUI.color = savedGuiColor;
        }

        static void RecordDrawTexture(object[] args)
        {
            if (args == null || args.Length < 2) return;
            if (!(args[0] is Rect pos) || !(args[1] is Texture tex) || tex == null) return;
            var src = new Rect(0f, 0f, 1f, 1f);
            Color tint = GUI.color;
            bool explicitColor = false;
            int l = 0, r = 0, t = 0, b = 0;
            for (int i = 2; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case Rect uv:
                        src = uv;
                        break;
                    case Color c:
                        tint = c;
                        explicitColor = true;
                        break;
                    case Vector4 borders:
                        l = Mathf.RoundToInt(borders.x);
                        r = Mathf.RoundToInt(borders.y);
                        t = Mathf.RoundToInt(borders.z);
                        b = Mathf.RoundToInt(borders.w);
                        break;
                }
            }
            if (!explicitColor) tint = GUI.color;
            ClampBorder(tex, ref l, ref r, ref t, ref b);
            Record(pos, tex, src, tint, l, r, t, b);
        }

        static void RecordStyle(GUIStyle style, Rect position, object[] args, MethodBase method)
        {
            string text = null;
            Texture image = null;
            bool hover = false, active = false, on = false;
            bool hoverSet = false, activeSet = false;
            int controlId = 0;
            bool hasId = false;
            if (method != null && args != null)
            {
                var ps = method.GetParameters();
                int n = Mathf.Min(ps.Length, args.Length);
                for (int i = 1; i < n; i++)
                {
                    object a = args[i];
                    switch (ps[i].Name)
                    {
                        case "text":
                            text = a as string;
                            break;
                        case "image":
                            image = a as Texture;
                            break;
                        case "content":
                            if (a is GUIContent gc)
                            {
                                text = gc.text;
                                image = gc.image;
                            }
                            break;
                        case "isHover":
                        case "hover":
                            hover = a is bool hb && hb;
                            hoverSet = true;
                            break;
                        case "isActive":
                            active = a is bool ab && ab;
                            activeSet = true;
                            break;
                        case "on":
                            on = a is bool ob && ob;
                            break;
                        case "controlID":
                            if (a is int id) { controlId = id; hasId = true; }
                            break;
                    }
                }
            }
            if (!hoverSet && Event.current != null)
                hover = position.Contains(Event.current.mousePosition);
            if (!activeSet && hasId)
                active = hover && GUIUtility.hotControl == controlId;

            GUIStyleState state = PickState(style, on, hover, active);
            Color gui = savedGuiColor.a > 0f && hideDepth > 0 ? savedGuiColor : GUI.color;
            if (state != null && state.background != null)
            {
                var bg = state.background;
                int l = style.border.left, r = style.border.right, t = style.border.top, b = style.border.bottom;
                ClampBorder(bg, ref l, ref r, ref t, ref b);
                Record(position, bg, new Rect(0f, 0f, 1f, 1f), gui * GUI.backgroundColor, l, r, t, b);
            }

            Rect inner = position;
            var pad = style.padding;
            if (pad != null)
            {
                inner.x += pad.left;
                inner.y += pad.top;
                inner.width -= pad.left + pad.right;
                inner.height -= pad.top + pad.bottom;
            }
            inner.x += style.contentOffset.x;
            inner.y += style.contentOffset.y;
            if (inner.width < 1f || inner.height < 1f) return;

            SplitContent(style, inner, image, out Rect imageRect, out Rect textRect, out bool drawImage, out bool drawText);
            if (drawImage && image != null)
                Record(imageRect, image, new Rect(0f, 0f, 1f, 1f), gui * GUI.contentColor, 0, 0, 0, 0);
            if (drawText && !string.IsNullOrEmpty(text))
            {
                Color textTint = gui * GUI.contentColor;
                if (state != null) textTint *= state.textColor;
                RecordText(style, textRect, text, textTint);
            }
        }

        static GUIStyleState PickState(GUIStyle style, bool on, bool hover, bool active)
        {
            if (on)
            {
                if (active) return style.onActive;
                if (hover) return style.onHover;
                return style.onNormal;
            }
            if (active) return style.active;
            if (hover) return style.hover;
            return style.normal;
        }

        static void SplitContent(GUIStyle style, Rect inner, Texture image, out Rect imageRect, out Rect textRect, out bool drawImage, out bool drawText)
        {
            imageRect = inner;
            textRect = inner;
            drawImage = image != null && style.imagePosition != ImagePosition.TextOnly;
            drawText = style.imagePosition != ImagePosition.ImageOnly;
            if (!drawImage) return;
            if (style.imagePosition == ImagePosition.ImageOnly) return;
            float side = Mathf.Min(inner.height, inner.width * 0.45f);
            if (style.imagePosition == ImagePosition.ImageLeft)
            {
                imageRect = new Rect(inner.x, inner.y, side, inner.height);
                textRect = new Rect(inner.x + side, inner.y, Mathf.Max(0f, inner.width - side), inner.height);
            }
            else
            {
                float ih = Mathf.Min(inner.height * 0.62f, inner.width);
                imageRect = new Rect(inner.x, inner.y, inner.width, ih);
                textRect = new Rect(inner.x, inner.y + ih, inner.width, Mathf.Max(0f, inner.height - ih));
            }
        }

        static void RecordText(GUIStyle style, Rect rect, string text, Color tint)
        {
            if (rect.width < 1f || rect.height < 1f) return;
            Font font = style.font != null ? style.font : GUI.skin.font;
            if (font == null) return;
            int size = style.fontSize > 0 ? style.fontSize : font.fontSize;
            if (size <= 0) size = 12;
            if (textGen == null) textGen = new TextGenerator(text.Length + 8);
            var settings = new TextGenerationSettings
            {
                font = font,
                fontSize = size,
                fontStyle = style.fontStyle,
                richText = style.richText,
                textAnchor = style.alignment,
                lineSpacing = 1f,
                scaleFactor = 1f,
                color = tint,
                generateOutOfBounds = false,
                updateBounds = false,
                horizontalOverflow = style.wordWrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow,
                verticalOverflow = style.clipping == TextClipping.Overflow ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate,
                generationExtents = new Vector2(rect.width, rect.height),
                pivot = Vector2.zero
            };
            if (!textGen.Populate(text, settings)) return;
            Texture atlas = font.material != null ? font.material.mainTexture : null;
            if (atlas == null) return;
            glyphs.Clear();
            textGen.GetVertices(glyphs);
            int quads = glyphs.Count / 4;
            if (quads > 512) quads = 512;
            glyphFont = font;
            for (int q = 0; q < quads; q++)
                RecordGlyph(rect, atlas, q * 4);
        }

        // 字体网格的左上角经常对到图集的右侧。按角记下 UV，贴的时候才不会整字反过来。
        static void RecordGlyph(Rect rect, Texture atlas, int i)
        {
            if (glyphRecording.Count >= StampCap) return;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            var g = glyphCorner;
            var uv = glyphCornerUv;
            for (int k = 0; k < 4; k++)
            {
                Vector3 p = glyphs[i + k].position;
                g[k] = new Vector2(rect.x + p.x, rect.yMax - p.y);
                uv[k] = new Vector2(glyphs[i + k].uv0.x, glyphs[i + k].uv0.y);
                if (g[k].x < minX) minX = g[k].x;
                if (g[k].x > maxX) maxX = g[k].x;
                if (g[k].y < minY) minY = g[k].y;
                if (g[k].y > maxY) maxY = g[k].y;
            }
            if (maxX - minX < 0.2f || maxY - minY < 0.2f) return;
            float x0 = minX, y0 = minY, x1 = maxX, y1 = maxY;
            if (TryVisible(out Rect vis) && !IsPhysicalRootClip(vis) && ClipInDrawSpace(vis, minX, minY))
            {
                x0 = Mathf.Max(x0, vis.xMin);
                y0 = Mathf.Max(y0, vis.yMin);
                x1 = Mathf.Min(x1, vis.xMax);
                y1 = Mathf.Min(y1, vis.yMax);
                if (x1 - x0 < 0.2f || y1 - y0 < 0.2f) return;
            }
            Vector2 c00 = CornerUv(g, uv, minX, minY);
            Vector2 c10 = CornerUv(g, uv, maxX, minY);
            Vector2 c01 = CornerUv(g, uv, minX, maxY);
            Vector2 c11 = CornerUv(g, uv, maxX, maxY);
            // 屏幕自下而上：左下、左上、右上、右下。GUI 的 y 向下，所以 maxY 是底边。
            if (!TryNormPoint(new Vector2(x0, y1), out Vector2 n0)) return;
            if (!TryNormPoint(new Vector2(x0, y0), out Vector2 n1)) return;
            if (!TryNormPoint(new Vector2(x1, y0), out Vector2 n2)) return;
            if (!TryNormPoint(new Vector2(x1, y1), out Vector2 n3)) return;
            Vector2 t0 = BilinearUv(c00, c10, c01, c11, minX, maxX, minY, maxY, x0, y1);
            Vector2 t1 = BilinearUv(c00, c10, c01, c11, minX, maxX, minY, maxY, x0, y0);
            Vector2 t2 = BilinearUv(c00, c10, c01, c11, minX, maxX, minY, maxY, x1, y0);
            Vector2 t3 = BilinearUv(c00, c10, c01, c11, minX, maxX, minY, maxY, x1, y1);
            Color vc = glyphs[i].color;
            if (vc.a <= 0.004f) return;
            glyphRecording.Add(new GlyphDraw
            {
                order = drawSeq++,
                tex = atlas,
                tint = vc,
                x0 = n0.x, y0 = n0.y, x1 = n1.x, y1 = n1.y, x2 = n2.x, y2 = n2.y, x3 = n3.x, y3 = n3.y,
                u0 = t0.x, v0 = t0.y, u1 = t1.x, v1 = t1.y, u2 = t2.x, v2 = t2.y, u3 = t3.x, v3 = t3.y
            });
        }

        static Vector2 CornerUv(Vector2[] g, Vector2[] uv, float x, float y)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int k = 0; k < 4; k++)
            {
                float dx = g[k].x - x;
                float dy = g[k].y - y;
                float d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = k; }
            }
            return uv[best];
        }

        static Vector2 BilinearUv(Vector2 c00, Vector2 c10, Vector2 c01, Vector2 c11,
            float minX, float maxX, float minY, float maxY, float x, float y)
        {
            float tx = maxX > minX + 1e-4f ? (x - minX) / (maxX - minX) : 0f;
            float ty = maxY > minY + 1e-4f ? (y - minY) / (maxY - minY) : 0f;
            return Vector2.Lerp(Vector2.Lerp(c00, c10, tx), Vector2.Lerp(c01, c11, tx), ty);
        }

        static bool TryNormPoint(Vector2 gui, out Vector2 n)
        {
            n = default;
            float sw = UI.screenWidth;
            float sh = UI.screenHeight;
            if (sw < 1f || sh < 1f) return false;
            Vector2 p = GuiToVirtual(gui);
            n = new Vector2(p.x / sw, p.y / sh);
            return true;
        }

        static void Record(Rect gui, Texture tex, Rect src, Color tint, int l, int r, int t, int b)
        {
            if (tex == null || tint.a <= 0.004f) return;
            if (recording.Count >= StampCap)
            {
                if (!loggedCap)
                {
                    loggedCap = true;
                    Log.Warning(ModEntry.LogPrefix + "command panel stamp cap");
                }
                return;
            }
            if (!TryScreenRect(ref gui, ref src, l, r, t, b, out Rect n)) return;
            recording.Add(new Stamp
            {
                order = drawSeq++,
                nx = n.x, ny = n.y, nw = n.width, nh = n.height,
                tex = tex, src = src, tint = tint,
                l = l, r = r, t = t, b = b
            });
        }

        static bool TryScreenRect(ref Rect gui, ref Rect src, int l, int r, int t, int b, out Rect normalized)
        {
            normalized = default;
            if (gui.width < 0.4f || gui.height < 0.4f) return false;
            if (TryVisible(out Rect vis) && !IsPhysicalRootClip(vis) && ClipInDrawSpace(vis, gui.xMin, gui.yMin))
            {
                float x0 = Mathf.Max(gui.xMin, vis.xMin);
                float y0 = Mathf.Max(gui.yMin, vis.yMin);
                float x1 = Mathf.Min(gui.xMax, vis.xMax);
                float y1 = Mathf.Min(gui.yMax, vis.yMax);
                if (x1 - x0 < 0.4f || y1 - y0 < 0.4f) return false;
                bool cropped = x0 > gui.xMin + 0.5f || y0 > gui.yMin + 0.5f
                    || x1 < gui.xMax - 0.5f || y1 < gui.yMax - 0.5f;
                if (cropped && l == 0 && r == 0 && t == 0 && b == 0 && gui.width > 0.01f && gui.height > 0.01f)
                {
                    float cl = (x0 - gui.xMin) / gui.width;
                    float cr = (gui.xMax - x1) / gui.width;
                    float ct = (y0 - gui.yMin) / gui.height;
                    float cb = (gui.yMax - y1) / gui.height;
                    src = new Rect(
                        src.x + src.width * cl,
                        src.y + src.height * cb,
                        src.width * (1f - cl - cr),
                        src.height * (1f - ct - cb));
                }
                gui = new Rect(x0, y0, x1 - x0, y1 - y0);
            }
            Vector2 p0 = GuiToVirtual(new Vector2(gui.xMin, gui.yMin));
            Vector2 p1 = GuiToVirtual(new Vector2(gui.xMax, gui.yMax));
            // 归一化分母是虚拟像素。贴回时乘纹理尺寸，控件就按 UiShrink 变小。
            float sw = UI.screenWidth;
            float sh = UI.screenHeight;
            if (sw < 1f || sh < 1f) return false;
            float nx = Mathf.Min(p0.x, p1.x) / sw;
            float ny = Mathf.Min(p0.y, p1.y) / sh;
            float nw = Mathf.Abs(p1.x - p0.x) / sw;
            float nh = Mathf.Abs(p1.y - p0.y) / sh;
            if (nw < 0.0008f || nh < 0.0008f) return false;
            normalized = new Rect(nx, ny, nw, nh);
            return true;
        }

        static bool TryVisible(out Rect vis)
        {
            vis = default;
            EnsureClip();
            if (visibleRectGetter == null) return false;
            try
            {
                vis = (Rect)visibleRectGetter.Invoke(null, null);
                return vis.width > 1f && vis.height > 1f;
            }
            catch { return false; }
        }

        // 裁剪矩形的左上角落到绘制矩形内部，说明它在另一套坐标里（窗口屏幕坐标对局部坐标）。
        // 这时相交会把弹窗切走或再偏一次，整块不裁。
        static bool ClipInDrawSpace(Rect vis, float x, float y)
        {
            return vis.xMin <= x + 2f && vis.yMin <= y + 2f;
        }

        // 根裁剪矩形是物理屏幕。虚拟像素比它大时，裁它会把底栏和右侧警报切掉。
        static bool IsPhysicalRootClip(Rect vis)
        {
            if (Screen.width < 2 || Screen.height < 2) return false;
            if (UI.screenWidth <= Screen.width + 2 && UI.screenHeight <= Screen.height + 2) return false;
            return vis.xMin <= 1f && vis.yMin <= 1f
                && Mathf.Abs(vis.width - Screen.width) <= 2f
                && Mathf.Abs(vis.height - Screen.height) <= 2f;
        }

        static void EnsureClip()
        {
            if (visibleRectTried) return;
            visibleRectTried = true;
            try
            {
                // UnityEngine.GUIClip 为 internal 类型，编译期不可直接引用，改为运行时按名解析。
                var clipType = AccessTools.TypeByName("UnityEngine.GUIClip");
                visibleRectGetter = clipType != null ? AccessTools.PropertyGetter(clipType, "visibleRect") : null;
                unclipMethod = clipType != null ? AccessTools.Method(clipType, "Unclip", new[] { typeof(Vector2) }) : null;
            }
            catch { visibleRectGetter = null; }
        }

        // GUI.Window 的回调里，绘制矩形是窗口局部坐标。Unclip 没把窗口原点加回去时，弹窗会贴在终端左上角。
        // 在窗口函数入口探测一次：Unclip(0,0) 已经靠近 windowRect 就不要再加，避免叠两次。
        static void RememberWindowClip(Window __instance)
        {
            Window window = __instance;
            clipFixAdd = Vector2.zero;
            clipFixWindow = int.MinValue;
            if (!MapScreen3D.Viewing || window == null || Event.current == null) return;
            clipFixWindow = window.ID;
            clipFixFrame = Time.frameCount;
            clipFixType = (int)Event.current.type;
            Rect wr = window.windowRect;
            Vector2 zero = RawUnclip(Vector2.zero);
            float dx = zero.x - wr.x;
            float dy = zero.y - wr.y;
            if (dx * dx + dy * dy <= 96f * 96f) return;
            if (zero.x > -48f && zero.x < 96f && zero.y > -48f && zero.y < 160f)
                clipFixAdd = new Vector2(wr.x, wr.y);
        }

        // GUIClip.Unclip 给出虚拟像素。矩阵在查看期是单位阵，结果和 UI.screenWidth 同一套。
        static Vector2 GuiToVirtual(Vector2 gui)
        {
            Vector2 p = RawUnclip(gui);
            if (clipFixAdd.sqrMagnitude > 0.01f && Event.current != null
                && Time.frameCount == clipFixFrame && (int)Event.current.type == clipFixType
                && Find.WindowStack != null && Find.WindowStack.currentlyDrawnWindow != null
                && Find.WindowStack.currentlyDrawnWindow.ID == clipFixWindow)
                p += clipFixAdd;
            return p;
        }

        static Vector2 RawUnclip(Vector2 gui)
        {
            EnsureClip();
            if (unclipMethod != null)
            {
                try { return (Vector2)unclipMethod.Invoke(null, new object[] { gui }); }
                catch { unclipMethod = null; }
            }
            Vector2 p = GUIUtility.GUIToScreenPoint(gui);
            if (layoutScale > 0.01f && layoutScale < 0.999f) p /= layoutScale;
            return p;
        }

        static void ClampBorder(Texture tex, ref int l, ref int r, ref int t, ref int b)
        {
            if (tex == null) { l = r = t = b = 0; return; }
            int mx = Mathf.Max(0, tex.width / 2);
            int my = Mathf.Max(0, tex.height / 2);
            l = Mathf.Clamp(l, 0, mx);
            r = Mathf.Clamp(r, 0, mx);
            t = Mathf.Clamp(t, 0, my);
            b = Mathf.Clamp(b, 0, my);
        }

        static void Commit()
        {
            committed.Clear();
            for (int i = 0; i < recording.Count; i++)
            {
                var s = recording[i];
                if (s.tex == null) continue;
                committed.Add(s);
            }
            recording.Clear();
            glyphCommitted.Clear();
            for (int i = 0; i < glyphRecording.Count; i++)
            {
                var g = glyphRecording[i];
                if (g.tex == null) continue;
                glyphCommitted.Add(g);
            }
            glyphRecording.Clear();
        }

        // 叠到上一帧记下来的界面。调用点在上帝相机 Render 之后、终端 DrawMesh 之前。
        public static void CompositeOntoScreen()
        {
            var rt = GodView3D.ScreenTexture as RenderTexture;
            if (rt == null || !rt.IsCreated()) return;
            if (committed.Count == 0 && glyphCommitted.Count == 0) return;
            RenderTexture prev = RenderTexture.active;
            bool pushed = false;
            bool glyphOpen = false;
            Texture glyphTex = null;
            try
            {
                RenderTexture.active = rt;
                GL.PushMatrix();
                pushed = true;
                GL.LoadPixelMatrix(0f, rt.width, 0f, rt.height);
                GL.Clear(true, false, Color.clear);
                int si = 0, gi = 0;
                while (si < committed.Count || gi < glyphCommitted.Count)
                {
                    bool useGlyph = gi < glyphCommitted.Count
                        && (si >= committed.Count || glyphCommitted[gi].order < committed[si].order);
                    if (!useGlyph)
                    {
                        if (glyphOpen) { GL.End(); glyphOpen = false; glyphTex = null; }
                        GL.LoadPixelMatrix(0f, rt.width, 0f, rt.height);
                        var s = committed[si++];
                        if (s.tex == null) continue;
                        float x = s.nx * rt.width;
                        float w = s.nw * rt.width;
                        float h = s.nh * rt.height;
                        float y = rt.height - s.ny * rt.height - h;
                        Graphics.DrawTexture(new Rect(x, y, w, h), s.tex, s.src, s.l, s.r, s.t, s.b, s.tint);
                        continue;
                    }
                    var gd = glyphCommitted[gi++];
                    if (gd.tex == null) continue;
                    if (!glyphOpen || glyphTex != gd.tex)
                    {
                        if (glyphOpen) GL.End();
                        if (!BeginGlyphPass(rt, gd.tex)) { glyphOpen = false; glyphTex = null; continue; }
                        GL.Begin(GL.QUADS);
                        glyphOpen = true;
                        glyphTex = gd.tex;
                    }
                    GlyphVert(rt, gd.x0, gd.y0, gd.u0, gd.v0, gd.tint);
                    GlyphVert(rt, gd.x1, gd.y1, gd.u1, gd.v1, gd.tint);
                    GlyphVert(rt, gd.x2, gd.y2, gd.u2, gd.v2, gd.tint);
                    GlyphVert(rt, gd.x3, gd.y3, gd.u3, gd.v3, gd.tint);
                }
                if (glyphOpen) { GL.End(); glyphOpen = false; }
            }
            catch (Exception e) { LogOnce(e); }
            finally
            {
                if (glyphOpen) GL.End();
                if (pushed) GL.PopMatrix();
                RenderTexture.active = prev;
            }
        }

        static bool BeginGlyphPass(RenderTexture rt, Texture tex)
        {
            if (glyphFont == null || glyphFont.material == null || tex == null) return false;
            Shader sh = glyphFont.material.shader;
            if (sh == null) return false;
            if (glyphMat == null || glyphShader != sh)
            {
                if (glyphMat != null) UnityEngine.Object.Destroy(glyphMat);
                glyphMat = new Material(glyphFont.material) { hideFlags = HideFlags.HideAndDontSave };
                glyphShader = sh;
                glyphMat.SetInt("_ZWrite", 0);
                glyphMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                glyphMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }
            glyphMat.mainTexture = tex;
            glyphMat.color = Color.white;
            if (!glyphMat.SetPass(0)) return false;
            GL.LoadPixelMatrix(0f, rt.width, 0f, rt.height);
            return true;
        }

        static void GlyphVert(RenderTexture rt, float nx, float ny, float u, float v, Color tint)
        {
            GL.Color(tint);
            GL.TexCoord2(u, v);
            GL.Vertex3(nx * rt.width, (1f - ny) * rt.height, 0f);
        }

        static void LogOnce(Exception e)
        {
            if (logged) return;
            logged = true;
            Log.Warning(ModEntry.LogPrefix + "command panel blit failed: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
