// AI生成
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 指挥面板（1.7 Waylith 适配）：
    // 查看期将终端屏幕射线交点映射为 Input.mousePosition，驱动 ImGuiDispatch 完整输入与命中检测；
    // Repaint 阶段将 RenderTexture.active 指向终端屏幕 RT，由 ImGuiGuiBackend 原生烘焙原版 UIRoot 与全部 Window，
    // 主画面不残留任何 2D UI。
    public static class CommandPanel3D
    {
        public static bool ReadingHardwareMouse;

        static bool pointerOnScreen;
        static Vector2 pointerUv;
        static int lastPointerFrame = -1;

        static bool redirectingRepaint;
        static bool releasingRedirectTarget;

        static FieldInfo pawnTableField;
        static MethodInfo pawnExtraBottom;
        static MethodInfo pawnExtraTop;
        static MethodInfo pawnMargin;

        public static bool PointerOnScreen => pointerOnScreen;
        public static Vector2 PointerUv => pointerUv;

        public static void Clear()
        {
            EndRedirectTarget();
            pointerOnScreen = false;
            lastPointerFrame = -1;
        }

        public static void Patch(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(UIRootPrefix)) { priority = Priority.First },
                postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(UIRootPostfix)) { priority = Priority.Last });

            var pendingWin = AccessTools.Method(typeof(GUI), "ProcessPendingWindows", Type.EmptyTypes);
            if (pendingWin != null)
            {
                harmony.Patch(pendingWin,
                    prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(PendingWindowsPrefix)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(PendingWindowsPostfix)) { priority = Priority.Last });
            }

            var setActiveRt = AccessTools.PropertySetter(typeof(RenderTexture), nameof(RenderTexture.active));
            if (setActiveRt != null)
            {
                harmony.Patch(setActiveRt,
                    prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(SetActiveRenderTexturePrefix)));
            }

            var mousePosGetter = AccessTools.PropertyGetter(typeof(Input), nameof(Input.mousePosition));
            if (mousePosGetter != null)
            {
                harmony.Patch(mousePosGetter,
                    postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MousePositionPostfix)));
            }

            var winStackAdd = AccessTools.Method(typeof(WindowStack), nameof(WindowStack.Add), new[] { typeof(Window) });
            if (winStackAdd != null)
                harmony.Patch(winStackAdd, postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(WindowStackAddPostfix)));

            var mainTabsLowPrio = AccessTools.Method(typeof(MainTabsRoot), nameof(MainTabsRoot.HandleLowPriorityShortcuts));
            if (mainTabsLowPrio != null)
                harmony.Patch(mainTabsLowPrio, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MainTabsLowPriorityPrefix)));

            harmony.Patch(AccessTools.Method(typeof(UI), nameof(UI.UIToMapPosition), new[] { typeof(Vector2) }),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MapPositionWhileViewing)));
            harmony.Patch(AccessTools.Method(typeof(UI), nameof(UI.MapToUIPosition)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(MapToUIWhileViewing)));

            // 地图上的名牌、血条与堆叠数字走 GenMapUI.LabelDrawPosFor，查看期改用俯视正交相机投影。
            var labelPosThing = AccessTools.Method(typeof(GenMapUI), nameof(GenMapUI.LabelDrawPosFor),
                new[] { typeof(Thing), typeof(float) });
            if (labelPosThing != null)
                harmony.Patch(labelPosThing, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(LabelDrawPosThingWhileViewing)));
            var labelPosCell = AccessTools.Method(typeof(GenMapUI), nameof(GenMapUI.LabelDrawPosFor),
                new[] { typeof(IntVec3) });
            if (labelPosCell != null)
                harmony.Patch(labelPosCell, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(LabelDrawPosCellWhileViewing)));

            var placeCost = AccessTools.Method(typeof(Designator_Build), "DrawPlaceMouseAttachments");
            if (placeCost != null)
                harmony.Patch(placeCost, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(CostIconsAfterStuffMenu)));

            harmony.Patch(AccessTools.Method(typeof(FloatMenu), nameof(FloatMenu.DoWindowContents)),
                prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(KeepFloatMenu)));

            var winPlace = AccessTools.Method(typeof(Window), "SetInitialSizeAndPosition");
            if (winPlace != null)
                harmony.Patch(winPlace, postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(CapWindowRect)));

            var tabPlace = AccessTools.Method(typeof(MainTabWindow), "SetInitialSizeAndPosition");
            if (tabPlace != null)
                harmony.Patch(tabPlace, postfix: new HarmonyMethod(typeof(CommandPanel3D), nameof(CapMainTab)));

            var onScreen = new HarmonyMethod(typeof(CommandPanel3D), nameof(MapToolsOnlyOnScreen));
            harmony.Patch(AccessTools.Method(typeof(DesignatorManager), nameof(DesignatorManager.ProcessInputEvents)),
                prefix: onScreen);
            harmony.Patch(AccessTools.Method(typeof(Targeter), nameof(Targeter.ProcessInputEvents)),
                prefix: onScreen);

            var selectorClicks = AccessTools.Method(typeof(Selector), "HandleMapClicks");
            if (selectorClicks != null)
                harmony.Patch(selectorClicks, prefix: new HarmonyMethod(typeof(CommandPanel3D), nameof(SelectorClicksWhileViewing)));
        }

        // 光标不在终端上时不响应地图点击；当按住 Alt 左键拖拽平移地图时跳过框选，把普通左键点选、框选和右键菜单完整留给原版。
        static bool SelectorClicksWhileViewing()
        {
            if (!MapScreen3D.Viewing) return true;
            if (!pointerOnScreen) return false;
            var e = Event.current;
            if (e != null && e.button == 0 && e.alt)
                return false;
            return true;
        }

        static bool MapToolsOnlyOnScreen()
        {
            if (!MapScreen3D.Viewing) return true;
            return pointerOnScreen;
        }

        public static void NotePointer(bool hit, Vector2 uv)
        {
            pointerOnScreen = hit;
            if (hit) pointerUv = uv;
            lastPointerFrame = Time.frameCount;
        }

        static void RefreshPointerIfNeeded()
        {
            int frame = Time.frameCount;
            if (lastPointerFrame == frame) return;
            lastPointerFrame = frame;
            var cam = CameraRig3D.Cam;
            pointerOnScreen = cam != null && HandTerminal3D.TryScreenUV(cam, 1f, out pointerUv);
        }

        static void MousePositionPostfix(ref Vector3 __result)
        {
            if (!MapScreen3D.Viewing || ReadingHardwareMouse) return;
            RefreshPointerIfNeeded();
            if (!pointerOnScreen)
            {
                __result = new Vector3(-10000f, -10000f, 0f);
                return;
            }
            float sw = Screen.width > 0 ? Screen.width : GodView3D.RtW;
            float sh = Screen.height > 0 ? Screen.height : GodView3D.RtH;
            __result = new Vector3(pointerUv.x * sw, pointerUv.y * sh, 0f);
        }

        static void BeginRedirectTarget()
        {
            var rt = GodView3D.RenderTarget;
            if (rt == null || !rt.IsCreated()) return;
            redirectingRepaint = true;
            RenderTexture.active = rt;
        }

        static void EndRedirectTarget()
        {
            if (!redirectingRepaint && RenderTexture.active != GodView3D.ScreenTexture) return;
            redirectingRepaint = false;
            releasingRedirectTarget = true;
            try
            {
                if (RenderTexture.active == GodView3D.ScreenTexture)
                    RenderTexture.active = null;
            }
            finally
            {
                releasingRedirectTarget = false;
            }
        }

        static void SetActiveRenderTexturePrefix(ref RenderTexture value)
        {
            if (redirectingRepaint && !releasingRedirectTarget && value == null)
            {
                var rt = GodView3D.RenderTarget;
                if (rt != null) value = rt;
            }
        }

        static void UIRootPrefix()
        {
            if (!MapScreen3D.Viewing) return;
            RefreshPointerIfNeeded();
            EnsureWindowsClamped();
            MapScreen3D.OnGuiGesture(pointerOnScreen, pointerUv);
            if (Event.current != null && Event.current.type == EventType.Repaint)
                BeginRedirectTarget();
        }

        static void UIRootPostfix()
        {
            if (redirectingRepaint)
                EndRedirectTarget();
        }

        static void PendingWindowsPrefix()
        {
            if (!MapScreen3D.Viewing) return;
            if (Event.current != null && Event.current.type == EventType.Repaint)
                BeginRedirectTarget();
        }

        static void PendingWindowsPostfix()
        {
            if (redirectingRepaint)
                EndRedirectTarget();
        }

        static bool MainTabsLowPriorityPrefix()
        {
            if (!MapScreen3D.Viewing) return true;
            if (!pointerOnScreen && Event.current != null && Event.current.type == EventType.MouseDown)
                return false;
            return true;
        }

        public static void HoldVirtualScreen()
        {
        }

        public static void CompositeOntoScreen()
        {
        }

        public static void VirtualSize(out int w, out int h)
        {
            if (UI.screenWidth > 0 && UI.screenHeight > 0)
            {
                w = UI.screenWidth;
                h = UI.screenHeight;
                return;
            }
            w = Screen.width > 0 ? Screen.width : GodView3D.RtW;
            h = Screen.height > 0 ? Screen.height : GodView3D.RtH;
        }

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

        static bool LabelDrawPosThingWhileViewing(Thing thing, float worldOffsetZ, ref Vector2 __result)
        {
            if (!MapScreen3D.Viewing || thing == null) return true;
            var cam = GodView3D.Camera;
            if (cam == null) return true;
            Vector3 drawPos = thing.DrawPos;
            drawPos.z += worldOffsetZ;
            Vector3 vp = cam.WorldToViewportPoint(drawPos);
            if (vp.z < 0f)
            {
                __result = new Vector2(-10000f, -10000f);
                return false;
            }
            VirtualSize(out int w, out int h);
            Vector2 res = new Vector2(vp.x * w, (1f - vp.y) * h);
            if (thing is Pawn pawn)
            {
                if (!pawn.RaceProps.Humanlike) res.y -= 4f;
                else if (pawn.DevelopmentalStage.Baby()) res.y -= 8f;
            }
            __result = res;
            return false;
        }

        static bool LabelDrawPosCellWhileViewing(IntVec3 center, ref Vector2 __result)
        {
            if (!MapScreen3D.Viewing) return true;
            var cam = GodView3D.Camera;
            if (cam == null) return true;
            Vector3 pos = center.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
            Vector3 vp = cam.WorldToViewportPoint(pos);
            if (vp.z < 0f)
            {
                __result = new Vector2(-10000f, -10000f);
                return false;
            }
            VirtualSize(out int w, out int h);
            __result = new Vector2(vp.x * w, (1f - vp.y) * h - 1f);
            return false;
        }

        static bool CostIconsAfterStuffMenu(Designator_Build __instance)
        {
            if (!MapScreen3D.Viewing || __instance == null) return true;
            if (Find.WindowStack == null || Find.WindowStack.FloatMenu == null) return true;
            var thing = __instance.PlacingDef as ThingDef;
            if (thing != null && thing.MadeFromStuff && __instance.StuffDefRaw == null)
                return false;
            return true;
        }

        static void KeepFloatMenu(FloatMenu __instance)
        {
            if (MapScreen3D.Viewing && __instance != null)
                __instance.vanishIfMouseDistant = false;
        }

        static void WindowStackAddPostfix(WindowStack __instance, Window window)
        {
            if (!MapScreen3D.Viewing || __instance == null || window == null) return;
            if (window is MainTabWindow tab && tab.RequestedTabSize.x <= 1f && tab.RequestedTabSize.y <= 1f)
            {
                __instance.TryRemove(window, false);
                return;
            }
            ClampWindowToTerminal(window);
        }

        static void EnsureWindowsClamped()
        {
            if (Find.WindowStack == null) return;
            var list = Find.WindowStack.Windows;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var w = list[i];
                if (w is MainTabWindow tab && tab.RequestedTabSize.x <= 1f && tab.RequestedTabSize.y <= 1f)
                {
                    Find.WindowStack.TryRemove(w, false);
                    continue;
                }
                ClampWindowToTerminal(w);
            }
        }

        static void CapWindowRect(Window __instance)
        {
            if (!MapScreen3D.Viewing || __instance == null) return;
            ClampWindowToTerminal(__instance);
        }

        static void CapMainTab(MainTabWindow __instance)
        {
            if (!MapScreen3D.Viewing || __instance == null) return;
            ClampWindowToTerminal(__instance);
        }

        static void ClampWindowToTerminal(Window window)
        {
            if (window == null || window is ImmediateWindow || window.ID < 0) return;
            VirtualSize(out int vw, out int vh);
            if (vw < 8 || vh < 8) return;
            Rect r = window.windowRect;
            if (r.width <= 1f || r.height <= 1f) return;

            if (window is MainTabWindow tab)
            {
                if (r.width <= 260f && r.x >= 0f && r.y >= 0f && r.xMax <= vw && r.yMax <= vh)
                    return;
                float maxTabW = vw;
                float maxTabH = Mathf.Max(240f, (vh - 35f) - 72f);
                if (r.width > maxTabW) r.width = maxTabW;
                if (r.height > maxTabH) r.height = maxTabH;
                r.x = tab.Anchor == MainTabWindowAnchor.Left ? 0f : vw - r.width;
                r.y = (vh - 35f) - r.height;
                window.windowRect = r;
                if (tab is MainTabWindow_PawnTable)
                    FitPawnTable(tab, r);
                return;
            }

            float maxW = vw;
            float maxY = window is FloatMenu ? (float)vh : Mathf.Max(240f, vh - 35f);
            float maxH = maxY;
            bool changed = false;
            if (r.width > maxW) { r.width = maxW; changed = true; }
            if (r.height > maxH) { r.height = maxH; changed = true; }
            if (r.x < 0f) { r.x = 0f; changed = true; }
            if (r.y < 0f) { r.y = 0f; changed = true; }
            if (r.xMax > maxW) { r.x = Mathf.Max(0f, maxW - r.width); changed = true; }
            if (r.yMax > maxY) { r.y = Mathf.Max(0f, maxY - r.height); changed = true; }
            if (changed)
                window.windowRect = r.Rounded();
        }

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
    }
}
