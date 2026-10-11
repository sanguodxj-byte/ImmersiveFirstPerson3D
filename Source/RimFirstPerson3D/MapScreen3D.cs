// AI生成
using System;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 指挥面板（M 键）：终端抬到眼前。俯视地图画进屏幕，原版 UI 再贴到这块纹理上。
    // 主相机始终留在第一人称。原版没用掉的左键拖拽平移焦点，点一下沿用原版点选。
    // Open 恒为 false，避免旧的「全屏接管 → Leave3D」短路再次生效。
    public static class MapScreen3D
    {
        enum Phase { Closed, Rising, Viewing, Lowering }

        static Phase phase = Phase.Closed;
        static float animT;
        static Vector3 focus;
        static bool focusReady;
        static bool dragging;
        static bool dragMoved;
        static Vector2 dragUv;
        static Vector3 dragFocus;
        static Pawn pickedPawn;
        static MethodInfo selectUnderMouse;
        static MethodInfo selectAllMatching;
        static bool selectLookup;
        static bool loggedSelect;

        const float RiseDur = 0.55f;
        const float FallDur = 0.45f;
        const float DragSlop = 0.02f;

        public static bool Open => false;
        public static bool Viewing => phase == Phase.Viewing;
        public static bool Animating => phase == Phase.Rising || phase == Phase.Lowering;
        // 抬起、查看、放下期间第一人称移动让位。环视在查看期另由 CameraRig 停掉。
        public static bool BlocksGameplay => phase != Phase.Closed;

        public static void Toggle()
        {
            if (Animating) return;
            if (phase == Phase.Viewing) Close();
            else if (phase == Phase.Closed) Show();
        }

        static void Show()
        {
            if (!Fps3DState.Active || Find.CurrentMap == null) return;
            if (!HandTerminal3D.Ready) return;
            phase = Phase.Rising;
            animT = 0f;
            focusReady = false;
            dragging = false;
            dragMoved = false;
            pickedPawn = null;
            CommandPanel3D.Clear();
            PanelUi3D.Reset();
            HandTerminal3D.SetScreenTexture(null);
        }

        static void Close()
        {
            phase = Phase.Lowering;
            animT = 0f;
            dragging = false;
            RememberPickedPawn();
            pickedPawn = null;
        }

        public static void ForceClose()
        {
            phase = Phase.Closed;
            animT = 0f;
            focusReady = false;
            dragging = false;
            pickedPawn = null;
            CommandPanel3D.Clear();
            PanelUi3D.Reset();
            HandTerminal3D.SetScreenTexture(null);
        }

        public static void Tick(float dt)
        {
            if (phase != Phase.Closed) CameraRig3D.YieldCursor();
            switch (phase)
            {
                case Phase.Rising:
                {
                    animT += dt;
                    float rt = Mathf.Clamp01(animT / RiseDur);
                    HandTerminal3D.Draw(CameraRig3D.Cam, rt, Mathf.Clamp01(animT / (RiseDur * 0.8f)));
                    if (animT >= RiseDur)
                    {
                        phase = Phase.Viewing;
                        animT = 0f;
                    }
                    break;
                }
                case Phase.Viewing:
                {
                    CommandPanel3D.HoldVirtualScreen();
                    EnsureFocus();
                    var map = Find.CurrentMap;
                    if (map == null || map.Disposed)
                    {
                        Close();
                        break;
                    }
                    // 先摆相机再读指针，点击打在已经画过的那一帧位姿上。
                    GodView3D.Pose(focus);
                    UpdatePointer();
                    GodView3D.Capture(map, focus);
                    CommandPanel3D.CompositeOntoScreen();
                    HandTerminal3D.SetScreenTexture(GodView3D.ScreenTexture);
                    HandTerminal3D.Draw(CameraRig3D.Cam, 1f, 1f);
                    break;
                }
                case Phase.Lowering:
                {
                    animT += dt;
                    float lt = Mathf.Clamp01(animT / FallDur);
                    HandTerminal3D.Draw(CameraRig3D.Cam, 1f - lt, 1f - lt * 0.8f);
                    if (animT >= FallDur)
                    {
                        phase = Phase.Closed;
                        animT = 0f;
                        CommandPanel3D.Clear();
                        PanelUi3D.Reset();
                        HandTerminal3D.SetScreenTexture(null);
                    }
                    break;
                }
            }
        }

        static void EnsureFocus()
        {
            if (focusReady) return;
            focus = DefaultFocus();
            focusReady = true;
        }

        // 俯视用地图本地坐标。脚点是世界坐标，要减掉邻图偏移。
        static Vector3 DefaultFocus()
        {
            var pawn = CameraRig3D.Possessed;
            if (pawn == null || !pawn.Spawned) return Vector3.zero;
            var fp = CameraRig3D.FootPoint;
            if (fp.HasValue && pawn.MapHeld != null)
            {
                var off = Streaming3D.OffsetOf(pawn.MapHeld);
                return new Vector3(fp.Value.x - off.x, 0f, fp.Value.z - off.z);
            }
            var dp = pawn.DrawPos;
            return new Vector3(dp.x, 0f, dp.z);
        }

        static void UpdatePointer()
        {
            var cam = CameraRig3D.Cam;
            Vector2 uv = Vector2.zero;
            bool hit = cam != null && HandTerminal3D.TryScreenUV(cam, 1f, out uv);
            // 点击交给本帧稍后的原版 OnGUI。这里只记住打中屏幕的 UV。
            CommandPanel3D.NotePointer(hit, uv);
        }

        // 原版按钮、窗口、设计器没吃掉的左键才平移。判断用 Event.type，被 Use 过的不再算。
        public static void OnGuiGesture(bool onScreen, Vector2 uv)
        {
            if (!Viewing) return;
            var e = Event.current;
            if (e == null) return;
            // 设计器或目标选取武装时，左键留给铺设。这里再选人会把设计器取消掉。
            if (MapToolArmed())
            {
                if (e.rawType == EventType.MouseUp && e.button == 0) dragging = false;
                return;
            }
            var map = Find.CurrentMap;

            if (e.type == EventType.MouseDown && e.button == 0 && onScreen && GUIUtility.hotControl == 0)
            {
                if (e.clickCount >= 2)
                {
                    dragging = false;
                    SelectUnderPointer(true);
                    return;
                }
                dragging = true;
                dragMoved = false;
                dragUv = uv;
                dragFocus = focus;
                return;
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && dragging)
            {
                if (GUIUtility.hotControl != 0)
                {
                    dragging = false;
                    return;
                }
                if (!onScreen || map == null) return;
                float du = uv.x - dragUv.x;
                float dv = uv.y - dragUv.y;
                if (du * du + dv * dv > DragSlop * DragSlop) dragMoved = true;
                float size = GodView3D.OrthoSize;
                float aspect = GodView3D.Aspect;
                focus.x = dragFocus.x - du * 2f * size * aspect;
                focus.z = dragFocus.z - dv * 2f * size;
                ClampFocus(map);
                return;
            }
            if (e.rawType == EventType.MouseUp && e.button == 0)
            {
                if (dragging && e.type == EventType.MouseUp && !dragMoved && onScreen)
                    SelectUnderPointer(false);
                dragging = false;
            }
        }

        static bool MapToolArmed()
        {
            if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null)
                return true;
            return Find.Targeter != null && Find.Targeter.IsTargeting;
        }

        static void ClampFocus(Map map)
        {
            focus.x = Mathf.Clamp(focus.x, 0f, map.Size.x);
            focus.z = Mathf.Clamp(focus.z, 0f, map.Size.z);
        }

        public static bool TryMapPoint(Vector2 uv, out Vector3 point)
        {
            point = Vector3.zero;
            var cam = GodView3D.Camera;
            if (cam == null) return false;
            Ray ray = cam.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float dist = -ray.origin.y / ray.direction.y;
            if (dist < 0f) return false;
            point = ray.origin + ray.direction * dist;
            point.y = 0f;
            return true;
        }

        public static void NotePawn(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || !pawn.Spawned || !pawn.IsColonist) return;
            pickedPawn = pawn;
        }

        static void SelectUnderPointer(bool all)
        {
            if (!selectLookup)
            {
                selectLookup = true;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                selectUnderMouse = typeof(Selector).GetMethod("SelectUnderMouse", flags);
                selectAllMatching = typeof(Selector).GetMethod("SelectAllMatchingObjectUnderMouseOnScreen", flags);
            }
            var selector = Find.Selector;
            if (selector == null) return;
            var method = all ? selectAllMatching : selectUnderMouse;
            if (method == null)
            {
                var map = Find.CurrentMap;
                if (!all && map != null) TryPickPawn(map, pointerFallback());
                return;
            }
            try
            {
                method.Invoke(selector, null);
            }
            catch (Exception e)
            {
                if (!loggedSelect)
                {
                    loggedSelect = true;
                    Log.Warning(ModEntry.LogPrefix + "terminal select failed: " + e.GetType().Name + ": " + e.Message);
                }
                return;
            }
            var selected = selector.SelectedObjects;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] is Pawn pawn)
                {
                    NotePawn(pawn);
                    break;
                }
            }
        }

        static Vector2 pointerFallback()
        {
            var cam = CameraRig3D.Cam;
            if (cam != null && HandTerminal3D.TryScreenUV(cam, 1f, out var uv)) return uv;
            return Vector2.zero;
        }

        static void TryPickPawn(Map map, Vector2 uv)
        {
            if (!TryMapPoint(uv, out var point)) return;
            IntVec3 cell = point.ToIntVec3();
            if (!cell.InBounds(map)) return;

            var list = map.thingGrid.ThingsListAtFast(cell);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var pawn = list[i] as Pawn;
                if (pawn == null || !pawn.Spawned || pawn.Dead) continue;
                Find.Selector.Select(pawn, false, true);
                pickedPawn = pawn;
                return;
            }
        }

        // 放下后由 CameraRig3D.TryPossessByName 消费。同一人不再走一遍解除再附身。
        static void RememberPickedPawn()
        {
            var pawn = pickedPawn;
            if (pawn == null || pawn.Destroyed || !pawn.Spawned) return;
            if (pawn == CameraRig3D.Possessed) return;
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            s.possessName = pawn.Name != null ? pawn.Name.ToString() : pawn.def.defName;
        }
    }
}
