// AI生成
using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 指挥面板（M 键）：终端抬到眼前。俯视地图画进屏幕，原版 UI 再贴到这块纹理上。
    // 主相机始终留在第一人称。左键点选/框选、右键下令均走原版 Selector；
    // WASD/方向键、鼠标中键拖拽（或 Alt+左键拖拽）平移地图，滚轮缩放视野，点击头像或信件自动跳转焦点。
    public static class MapScreen3D
    {
        enum Phase { Closed, Rising, Viewing, Lowering }

        static Phase phase = Phase.Closed;
        static float animT;
        static Vector3 focus;
        static bool focusReady;
        static bool dragging;
        static int dragButton = -1;
        static Vector2 dragUv;
        static Vector3 dragFocus;
        static Pawn pickedPawn;
        static Pawn initialPawn;

        const float RiseDur = 0.55f;
        const float FallDur = 0.45f;

        public static bool Open => phase != Phase.Closed;
        public static bool Viewing => phase == Phase.Viewing;
        public static bool Animating => phase == Phase.Rising || phase == Phase.Lowering;
        // 抬起、查看、放下期间第一人称移动让位。环视在查看期另由 CameraRig 停掉。
        public static bool BlocksGameplay => phase != Phase.Closed;

        public static void Patch(Harmony harmony)
        {
            var jumpVec = AccessTools.Method(typeof(CameraDriver), nameof(CameraDriver.JumpToCurrentMapLoc), new[] { typeof(Vector3) });
            if (jumpVec != null)
                harmony.Patch(jumpVec, postfix: new HarmonyMethod(typeof(MapScreen3D), nameof(OnCameraJumpVec)));

            var panCell = AccessTools.Method(typeof(CameraDriver), nameof(CameraDriver.PanToMapLoc), new[] { typeof(IntVec3) });
            if (panCell != null)
                harmony.Patch(panCell, postfix: new HarmonyMethod(typeof(MapScreen3D), nameof(OnCameraPanCell)));

            var panSize = AccessTools.Method(typeof(CameraDriver), nameof(CameraDriver.PanToMapLocAndSize));
            if (panSize != null)
                harmony.Patch(panSize, postfix: new HarmonyMethod(typeof(MapScreen3D), nameof(OnCameraPanSize)));
        }

        static void OnCameraJumpVec(Vector3 loc)
        {
            if (!Viewing) return;
            focus = new Vector3(loc.x, 0f, loc.z);
            focusReady = true;
            if (Find.CurrentMap != null) ClampFocus(Find.CurrentMap);
        }

        static void OnCameraPanCell(IntVec3 cell)
        {
            if (!Viewing) return;
            Vector3 loc = cell.ToVector3Shifted();
            focus = new Vector3(loc.x, 0f, loc.z);
            focusReady = true;
            if (Find.CurrentMap != null) ClampFocus(Find.CurrentMap);
        }

        static void OnCameraPanSize(Vector3 loc, float size)
        {
            if (!Viewing) return;
            focus = new Vector3(loc.x, 0f, loc.z);
            focusReady = true;
            GodView3D.SetOrthoSize(size);
            if (Find.CurrentMap != null) ClampFocus(Find.CurrentMap);
        }

        public static void Toggle()
        {
            if (Animating) return;
            if (phase == Phase.Viewing) Close();
            else if (phase == Phase.Closed) Show();
        }

        static void Show()
        {
            if (!Fps3DState.Active || Find.CurrentMap == null) return;
            if (!HandTerminal3D.Ready)
            {
                Log.Warning(ModEntry.LogPrefix + "map screen suppressed: terminal mesh not ready");
                return;
            }
            // 先量出这块屏在画面上占多少像素，把俯视 RT 开到同样大：1:1 贴上去，界面文字才不糊。
            GodView3D.EnsureTargetFor(CameraRig3D.Cam);
            phase = Phase.Rising;
            animT = 0f;
            focusReady = false;
            dragging = false;
            dragButton = -1;
            initialPawn = CameraRig3D.Possessed;
            pickedPawn = null;
            CommandPanel3D.Clear();
            HandTerminal3D.SetScreenTexture(null);
            CameraRig3D.NotifyScreenShown();
        }

        static void Close()
        {
            phase = Phase.Lowering;
            animT = 0f;
            dragging = false;
            dragButton = -1;
            SyncSelectedColonist();
            RememberPickedPawn();
            pickedPawn = null;
            CameraRig3D.ReengageAfterScreen();
        }

        public static void ForceClose()
        {
            phase = Phase.Closed;
            animT = 0f;
            focusReady = false;
            dragging = false;
            dragButton = -1;
            pickedPawn = null;
            CommandPanel3D.Clear();
            HandTerminal3D.SetScreenTexture(null);
            CameraRig3D.ReengageAfterScreen();
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
                    if (Input.GetKeyDown(KeyCode.Escape))
                    {
                        var stack = Find.WindowStack;
                        if (stack == null || (!stack.NonImmediateDialogWindowOpen && stack.FloatMenu == null))
                        {
                            Close();
                            break;
                        }
                    }
                    CommandPanel3D.HoldVirtualScreen();
                    EnsureFocus();
                    var map = Find.CurrentMap;
                    if (map == null || map.Disposed)
                    {
                        Close();
                        break;
                    }
                    UpdateKeyboardNavigation(dt, map);
                    SyncSelectedColonist();
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
                        HandTerminal3D.SetScreenTexture(null);
                    }
                    break;
                }
            }
        }

        static void UpdateKeyboardNavigation(float dt, Map map)
        {
            if (GUIUtility.keyboardControl != 0) return;
            float dx = 0f, dz = 0f;
            if (KeyBindingDefOf.MapDolly_Left != null && KeyBindingDefOf.MapDolly_Left.IsDown) dx -= 1f;
            if (KeyBindingDefOf.MapDolly_Right != null && KeyBindingDefOf.MapDolly_Right.IsDown) dx += 1f;
            if (KeyBindingDefOf.MapDolly_Up != null && KeyBindingDefOf.MapDolly_Up.IsDown) dz += 1f;
            if (KeyBindingDefOf.MapDolly_Down != null && KeyBindingDefOf.MapDolly_Down.IsDown) dz -= 1f;
            if (dx != 0f || dz != 0f)
            {
                float speed = Mathf.Max(14f, GodView3D.OrthoSize * 1.45f);
                focus.x += dx * speed * dt;
                focus.z += dz * speed * dt;
                ClampFocus(map);
            }
        }

        static void SyncSelectedColonist()
        {
            var sel = Find.Selector?.SelectedObjects;
            if (sel == null) return;
            for (int i = 0; i < sel.Count; i++)
            {
                if (sel[i] is Pawn p && !p.Destroyed && p.Spawned && p.IsColonist && !p.Dead)
                {
                    pickedPawn = p;
                    return;
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
            CommandPanel3D.NotePointer(hit, uv);
        }

        // 处理终端地图手势：中键拖拽或 Alt+左键拖拽平移焦点，滚轮与缩放热键调整视野。
        // 普通左键点击、双击、拖框多选与右键指令全部走原版 Selector。
        public static void OnGuiGesture(bool onScreen, Vector2 uv)
        {
            if (!Viewing) return;
            var e = Event.current;
            if (e == null) return;
            var map = Find.CurrentMap;

            if (KeyBindingDefOf.MapZoom_In != null && KeyBindingDefOf.MapZoom_In.KeyDownEvent)
            {
                GodView3D.AdjustZoom(4f);
                e.Use();
                return;
            }
            if (KeyBindingDefOf.MapZoom_Out != null && KeyBindingDefOf.MapZoom_Out.KeyDownEvent)
            {
                GodView3D.AdjustZoom(-4f);
                e.Use();
                return;
            }

            if (e.type == EventType.ScrollWheel && onScreen)
            {
                bool overWindow = Find.WindowStack != null && Find.WindowStack.MouseObscuredNow;
                if (!overWindow)
                {
                    GodView3D.AdjustZoom(-e.delta.y * 0.35f);
                    e.Use();
                    return;
                }
            }

            bool isPanButton = e.button == 2 || (e.button == 0 && e.alt);
            if (e.type == EventType.MouseDown && isPanButton && onScreen && GUIUtility.hotControl == 0)
            {
                dragging = true;
                dragButton = e.button;
                dragUv = uv;
                dragFocus = focus;
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDrag && dragging && e.button == dragButton)
            {
                if (!onScreen || map == null) return;
                float du = uv.x - dragUv.x;
                float dv = uv.y - dragUv.y;
                float size = GodView3D.OrthoSize;
                float aspect = GodView3D.Aspect;
                focus.x = dragFocus.x - du * 2f * size * aspect;
                focus.z = dragFocus.z - dv * 2f * size;
                ClampFocus(map);
                e.Use();
                return;
            }
            if (e.rawType == EventType.MouseUp && dragging && e.button == dragButton)
            {
                dragging = false;
                dragButton = -1;
                if (e.type == EventType.MouseUp) e.Use();
            }
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

        // 放下后由 CameraRig3D.TryPossessByName 消费。同一人不再走一遍解除再附身。
        static void RememberPickedPawn()
        {
            var pawn = pickedPawn;
            if (pawn == null || pawn.Destroyed || !pawn.Spawned) return;
            if (pawn == initialPawn || pawn == CameraRig3D.Possessed) return;
            var s = FirstPerson3DMod.settings;
            if (s == null) return;
            s.possessName = pawn.Name != null ? pawn.Name.ToString() : pawn.def.defName;
        }
    }
}
