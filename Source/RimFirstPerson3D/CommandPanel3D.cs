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
    // 指挥面板：查看期不把原版 UI 画在主画面上，改记到终端屏幕纹理里。
    // 上帝相机在 Update 里会清掉 RT，所以这里只在 OnGUI 记账，下一帧 Capture 之后再叠上去。
    // 只留下顶栏和底栏，并在终端上纵向拉开，中间地图不再叠窗口。
    // 文字图集按原样采样会左右颠倒，叠的时候单独把字形源矩形横向翻过来。
    public static class CommandPanel3D
    {
        // 原版 UI 纵坐标向下：顶栏、底栏（指令行 + 主按钮）。
        const float SrcTop = 0.08f;
        const float SrcBottom = 0.76f;
        // 终端 UV 向上。顶栏贴上沿，底栏拉开，中间留给地图拖拽。
        public const float DevTop = 0.90f;
        public const float DevBottom = 0.42f;

        const int StampCap = 4096;

        struct Stamp
        {
            public float nx, ny, nw, nh;
            public Texture tex;
            public Rect src;
            public Color tint;
            public int l, r, t, b;
            public bool glyph;
        }

        static readonly List<Stamp> recording = new List<Stamp>(512);
        static readonly List<Stamp> committed = new List<Stamp>(512);
        static readonly List<UIVertex> glyphs = new List<UIVertex>(256);
        static TextGenerator textGen;

        static bool redirecting;
        static bool pointerOnChrome;
        static Vector2 pointerUv;
        static float vanillaNx, vanillaNy;
        static int hideDepth;
        static Color savedGuiColor;
        static bool logged;
        static bool loggedCap;
        static MethodInfo visibleRectGetter;
        static bool visibleRectTried;

        public static bool ChromeBand(Vector2 uv) => TryVanillaNorm(uv, out _, out _);

        public static void Clear()
        {
            recording.Clear();
            committed.Clear();
        }

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(UIRootPrefix)),
                postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(UIRootPostfix)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(UI), nameof(UI.MousePositionOnUI)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MouseOnUIPrefix)));
            harmony.Patch(AccessTools.Method(typeof(MapInterface), nameof(MapInterface.HandleMapClicks)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(PassUnlessViewing)));
            // 有窗口 absorbInputAroundWindow 时，这一步会在指令按钮之前把点击吃掉。
            harmony.Patch(AccessTools.Method(typeof(WindowStack), nameof(WindowStack.HandleEventsHighPriority)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(WindowEventsWhileViewing)));

            var hideColor = new HarmonyMethod(typeof(CommandPanel3D), nameof(ZeroDrawColor));
            var texPre = new HarmonyMethod(typeof(CommandPanel3D), nameof(TexPrefix));
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

            var stylePre = new HarmonyMethod(typeof(CommandPanel3D), nameof(StylePrefix));
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

            PatchDesignators(harmony, new HarmonyMethod(typeof(CommandPanel3D), nameof(PassUnlessViewing)));
        }

        // 查看期不让设计器 / 地图点击落到真实地图上。面板中部的拖拽只移动俯视焦点。
        static bool PassUnlessViewing() => !MapScreen3D.Viewing;

        // 查看期仍把 Esc / 确认交给窗口，但不让“点在窗口外”把鼠标点击吃掉。
        static bool WindowEventsWhileViewing(WindowStack __instance)
        {
            if (!MapScreen3D.Viewing) return true;
            if (Event.current == null) return false;
            if (KeyBindingDefOf.Cancel.KeyDownEvent)
                __instance.Notify_PressedCancel();
            if (KeyBindingDefOf.Accept.KeyDownEvent)
                __instance.Notify_PressedAccept();
            return false;
        }

        static bool HasColorArg(MethodBase m)
        {
            var ps = m.GetParameters();
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].ParameterType == typeof(Color) && ps[i].Name == "color") return true;
            return false;
        }

        static void PatchDesignators(Harmony harmony, HarmonyMethod prefix)
        {
            Assembly asm = typeof(Designator).Assembly;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            if (types == null) return;
            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null || !typeof(Designator).IsAssignableFrom(t)) continue;
                MethodInfo m;
                try
                {
                    m = t.GetMethod(nameof(Designator.SelectedProcessInput),
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
                        null, new[] { typeof(Event) }, null);
                }
                catch { continue; }
                if (m == null) continue;
                try { harmony.Patch(m, prefix: prefix); }
                catch (Exception e)
                {
                    Log.Warning(ModEntry.LogPrefix + "command panel skipped " + t.Name + ": " + e.Message);
                }
            }
        }

        // ---- 指针：屏幕 UV 对到原版 UI。中部和未打中屏幕时把鼠标挪走，空白窗口吃不到点击。 ----

        static void UIRootPrefix()
        {
            redirecting = false;
            pointerOnChrome = false;
            if (!MapScreen3D.Viewing) return;
            var rt = GodView3D.ScreenTexture as RenderTexture;
            if (rt == null || !rt.IsCreated()) return;
            redirecting = true;
            AimPointer();
            if (Event.current != null && Event.current.type == EventType.Repaint)
                recording.Clear();
        }

        static void UIRootPostfix()
        {
            if (redirecting && Event.current != null && Event.current.type == EventType.Repaint)
                Commit();
            redirecting = false;
            if (hideDepth != 0)
            {
                hideDepth = 0;
                GUI.color = savedGuiColor;
            }
        }

        static void AimPointer()
        {
            var cam = CameraRig3D.Cam;
            bool hit = cam != null && HandTerminal3D.TryScreenUV(cam, 1f, out pointerUv);
            pointerOnChrome = hit && TryVanillaNorm(pointerUv, out vanillaNx, out vanillaNy);
            if (Event.current == null) return;
            if (!pointerOnChrome)
            {
                Event.current.mousePosition = new Vector2(-10000f, -10000f);
                return;
            }
            float w = UI.screenWidth > 0 ? UI.screenWidth : Screen.width;
            float h = UI.screenHeight > 0 ? UI.screenHeight : Screen.height;
            // 控件矩形在虚拟分辨率里，y 向下。和 UI.screenWidth 同一套，不走 ScreenToGUIPoint。
            Event.current.mousePosition = new Vector2(vanillaNx * w, vanillaNy * h);
        }

        static bool MouseOnUIPrefix(ref Vector2 __result)
        {
            if (!redirecting) return true;
            if (!pointerOnChrome)
            {
                __result = new Vector2(-10000f, -10000f);
                return false;
            }
            float w = UI.screenWidth > 0 ? UI.screenWidth : Screen.width;
            float h = UI.screenHeight > 0 ? UI.screenHeight : Screen.height;
            __result = new Vector2(vanillaNx * w, (1f - vanillaNy) * h);
            return false;
        }

        // 终端 UV（y 向上）对回原版归一化坐标（y 向下）。中间地带返回 false。
        static bool TryVanillaNorm(Vector2 uv, out float nx, out float ny)
        {
            nx = uv.x;
            float devDown = 1f - uv.y;
            if (uv.y >= DevTop)
            {
                float span = 1f - DevTop;
                float t = span > 1e-4f ? devDown / span : 0f;
                ny = Mathf.Lerp(0f, SrcTop, t);
                return true;
            }
            if (uv.y <= DevBottom)
            {
                float dev0 = 1f - DevBottom;
                float t = DevBottom > 1e-4f ? (devDown - dev0) / DevBottom : 0f;
                ny = Mathf.Lerp(SrcBottom, 1f, t);
                return true;
            }
            ny = 0f;
            return false;
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
            for (int q = 0; q < quads; q++)
            {
                int i = q * 4;
                float minX = glyphs[i].position.x, maxX = minX;
                float minY = glyphs[i].position.y, maxY = minY;
                int bl = 0;
                float blScore = glyphs[i].position.x + glyphs[i].position.y;
                for (int k = 1; k < 4; k++)
                {
                    Vector3 p = glyphs[i + k].position;
                    if (p.x < minX) minX = p.x;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.y > maxY) maxY = p.y;
                    float score = p.x + p.y;
                    if (score < blScore) { blScore = score; bl = k; }
                }
                float gw = maxX - minX;
                float gh = maxY - minY;
                if (gw < 0.2f || gh < 0.2f) continue;
                Vector4 uvBL = glyphs[i + bl].uv0;
                Vector4 uvTR = glyphs[i].uv0;
                float trScore = glyphs[i].position.x + glyphs[i].position.y;
                for (int k = 1; k < 4; k++)
                {
                    float score = glyphs[i + k].position.x + glyphs[i + k].position.y;
                    if (score > trScore) { trScore = score; uvTR = glyphs[i + k].uv0; }
                }
                var src = new Rect(uvBL.x, uvBL.y, uvTR.x - uvBL.x, uvTR.y - uvBL.y);
                if (Mathf.Abs(src.width) < 1e-6f || Mathf.Abs(src.height) < 1e-6f) continue;
                var glyph = new Rect(rect.x + minX, rect.yMax - maxY, gw, gh);
                Color vc = glyphs[i].color;
                Record(glyph, atlas, src, vc, 0, 0, 0, 0, true);
            }
        }

        static void Record(Rect gui, Texture tex, Rect src, Color tint, int l, int r, int t, int b, bool glyph = false)
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
                nx = n.x, ny = n.y, nw = n.width, nh = n.height,
                tex = tex, src = src, tint = tint,
                l = l, r = r, t = t, b = b,
                glyph = glyph
            });
        }

        static bool TryScreenRect(ref Rect gui, ref Rect src, int l, int r, int t, int b, out Rect normalized)
        {
            normalized = default;
            if (gui.width < 0.4f || gui.height < 0.4f) return false;
            if (TryVisible(out Rect vis))
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
            Vector2 p0 = GUIUtility.GUIToScreenPoint(new Vector2(gui.xMin, gui.yMin));
            Vector2 p1 = GUIUtility.GUIToScreenPoint(new Vector2(gui.xMax, gui.yMax));
            float sw = Screen.width;
            float sh = Screen.height;
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
            if (!visibleRectTried)
            {
                visibleRectTried = true;
                try
                {
                    // UnityEngine.GUIClip 为 internal 类型，编译期不可直接引用，改为运行时按名解析。
                    var clipType = AccessTools.TypeByName("UnityEngine.GUIClip");
                    visibleRectGetter = clipType != null ? AccessTools.PropertyGetter(clipType, "visibleRect") : null;
                }
                catch { visibleRectGetter = null; }
            }
            if (visibleRectGetter == null) return false;
            try
            {
                vis = (Rect)visibleRectGetter.Invoke(null, null);
                return vis.width > 1f && vis.height > 1f;
            }
            catch { return false; }
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
                if (!PlaceChrome(ref s)) continue;
                committed.Add(s);
            }
            recording.Clear();
        }

        // 中心不在顶栏或底栏的丢掉（中间那块窗口、肖像黑块）。留下来的裁进源带，再拉到终端上下沿。
        static bool PlaceChrome(ref Stamp s)
        {
            float y0 = s.ny;
            float y1 = s.ny + s.nh;
            if (s.nh < 1e-4f) return false;
            float cy = (y0 + y1) * 0.5f;
            float band0, band1, dev0, dev1;
            if (cy <= SrcTop)
            {
                band0 = 0f;
                band1 = SrcTop;
                dev0 = 0f;
                dev1 = 1f - DevTop;
            }
            else if (cy >= SrcBottom)
            {
                band0 = SrcBottom;
                band1 = 1f;
                dev0 = 1f - DevBottom;
                dev1 = 1f;
            }
            else return false;

            float c0 = Mathf.Max(y0, band0);
            float c1 = Mathf.Min(y1, band1);
            if (c1 - c0 < 0.0008f) return false;
            float topClip = (c0 - y0) / s.nh;
            float botClip = (y1 - c1) / s.nh;
            float vB = s.src.y;
            float vT = s.src.y + s.src.height;
            s.src.y = Mathf.Lerp(vB, vT, botClip);
            s.src.height = Mathf.Lerp(vB, vT, 1f - topClip) - s.src.y;

            float span = band1 - band0;
            float scale = span > 1e-4f ? (dev1 - dev0) / span : 1f;
            float mid = Mathf.Lerp(dev0, dev1, ((c0 + c1) * 0.5f - band0) / span);
            float nh = (c1 - c0) * scale;
            s.ny = mid - nh * 0.5f;
            s.nh = nh;
            return s.nh > 0.0008f && s.nw > 0.0008f;
        }

        // 叠到上一帧记下来的界面。调用点在上帝相机 Render 之后、终端 DrawMesh 之前。
        public static void CompositeOntoScreen()
        {
            var rt = GodView3D.ScreenTexture as RenderTexture;
            if (rt == null || !rt.IsCreated() || committed.Count == 0) return;
            RenderTexture prev = RenderTexture.active;
            bool pushed = false;
            try
            {
                RenderTexture.active = rt;
                GL.PushMatrix();
                pushed = true;
                GL.LoadPixelMatrix(0f, rt.width, 0f, rt.height);
                GL.Clear(true, false, Color.clear);
                for (int i = 0; i < committed.Count; i++)
                {
                    var s = committed[i];
                    if (s.tex == null) continue;
                    float x = s.nx * rt.width;
                    float w = s.nw * rt.width;
                    float h = s.nh * rt.height;
                    float y = rt.height - s.ny * rt.height - h;
                    Rect src = s.src;
                    // 字形用顶点 UV 重建后仍是反的；图标方向是对的，只翻文字。
                    if (s.glyph)
                    {
                        src.x += src.width;
                        src.width = -src.width;
                    }
                    Graphics.DrawTexture(new Rect(x, y, w, h), s.tex, src, s.l, s.r, s.t, s.b, s.tint);
                }
            }
            catch (Exception e) { LogOnce(e); }
            finally
            {
                if (pushed) GL.PopMatrix();
                RenderTexture.active = prev;
            }
        }

        static void LogOnce(Exception e)
        {
            if (logged) return;
            logged = true;
            Log.Warning(ModEntry.LogPrefix + "command panel blit failed: " + e.GetType().Name + ": " + e.Message);
        }
    }
}
