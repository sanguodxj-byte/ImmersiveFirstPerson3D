// AI生成
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 指挥面板（M 键）：终端抬到眼前。俯视地图画进屏幕，原版操作界面叠在同一张屏幕上。
    // 主相机始终留在第一人称。屏幕下沿和上沿点原版指令，中间拖动平移、点击选人。
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
            HandTerminal3D.SetScreenTexture(null);
        }

        public static void Tick(float dt)
        {
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
                    EnsureFocus();
                    var map = Find.CurrentMap;
                    if (map == null || map.Disposed)
                    {
                        Close();
                        break;
                    }
                    // 先摆相机再读指针，点击打在已经画过的那一帧位姿上。
                    GodView3D.Pose(focus);
                    UpdatePointer(map);
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

        static void UpdatePointer(Map map)
        {
            var cam = CameraRig3D.Cam;
            bool hit = HandTerminal3D.TryScreenUV(cam, 1f, out Vector2 uv);
            // 下沿指令、上沿殖民者栏交给原版 UI。中间才拖地图、点选殖民者。
            bool chrome = hit && CommandPanel3D.ChromeBand(uv);
            if (!chrome && Input.GetMouseButtonDown(0) && hit && GUIUtility.hotControl == 0)
            {
                dragging = true;
                dragMoved = false;
                dragUv = uv;
                dragFocus = focus;
            }
            if (dragging && Input.GetMouseButton(0) && hit && !chrome)
            {
                float du = uv.x - dragUv.x;
                float dv = uv.y - dragUv.y;
                if (du * du + dv * dv > DragSlop * DragSlop) dragMoved = true;
                float size = GodView3D.OrthoSize;
                float aspect = GodView3D.Aspect;
                focus.x = dragFocus.x - du * 2f * size * aspect;
                focus.z = dragFocus.z - dv * 2f * size;
                ClampFocus(map);
            }
            if (dragging && Input.GetMouseButtonUp(0))
            {
                if (!dragMoved && hit && !chrome) TryPickPawn(map, uv);
                dragging = false;
            }
        }

        static void ClampFocus(Map map)
        {
            focus.x = Mathf.Clamp(focus.x, 0f, map.Size.x);
            focus.z = Mathf.Clamp(focus.z, 0f, map.Size.z);
        }

        static void TryPickPawn(Map map, Vector2 uv)
        {
            var cam = GodView3D.Camera;
            if (cam == null) return;
            Ray ray = cam.ViewportPointToRay(new Vector3(uv.x, uv.y, 0f));
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return;
            float dist = -ray.origin.y / ray.direction.y;
            if (dist < 0f) return;
            IntVec3 cell = (ray.origin + ray.direction * dist).ToIntVec3();
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
