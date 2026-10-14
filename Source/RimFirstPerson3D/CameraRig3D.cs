// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFirstPerson3D
{
    // 透视相机控制：第一人称（附身 pawn）与第三人称追尾，含输入与指令下达。
    [StaticConstructorOnStartup]
    public static class CameraRig3D
    {
        private static GameObject camGO;
        private static Camera cam;
        private static readonly Vector3 CamOffsetChase = new Vector3(0f, 2.1f, -4.6f);

        public static Camera Cam => cam;
        public static Vector3 LastCamPos { get; private set; }
        public static string PossessedInfo =>
            (Possessed == null ? "none" : (Possessed.Name == null ? Possessed.def.defName : Possessed.Name.ToString()))
            + "@" + (Possessed == null ? "-" : Possessed.Position.ToString());
        public static Pawn Possessed { get; private set; }
        private static bool prevDrafted;

        private static float Yaw
        {
            get { var s = FirstPerson3DMod.settings; return s != null ? s.camYaw : 90f; }
            set { var s = FirstPerson3DMod.settings; if (s != null) s.camYaw = value; }
        }

        private static float Pitch
        {
            get { var s = FirstPerson3DMod.settings; return s != null ? s.camPitch : -8f; }
            set { var s = FirstPerson3DMod.settings; if (s != null) s.camPitch = value; }
        }

        public static float CamYaw => Yaw;
        public static float CamPitch => Pitch;

        private static float syncTimer;

        // ---- 连续位移控制器（F003 / T-P1）----
        // 第一人称持有位移期间直接对附身者积分位置：水平按 WASD+偏航，竖直按跳跃/重力。
        // 脚点（图内局部 XZ + 离地高度）是相机锚点与立牌绘制的唯一位置来源；
        // footActive=false 表示未启用（未接管 / 无附身 / 暂停门控 / 原版 job 持有者让位）。
        private static bool footActive;
        private static Map footMap;
        private static Vector2 footPos;      // 图内局部格坐标（含小数）；格心 = (c.x+0.5, c.z+0.5)
        private static float footHeight;     // 离地高度（米）
        private static float waterSink;      // 身处水体的视觉下沉（米；平滑趋近水面落差+水深）
        private static float vy;             // 竖直速度（米/秒）
        private static bool spacePrev;       // 空格边沿检测（着地才起跳）
        private static Vector2 stepDir;      // cmdKey 单步方向（世界 XZ，已归一）
        private static float stepRemaining;  // cmdKey 单步剩余格数
        private static Vector2 lastMousePosition;
        private static bool mousePositionReady;
        private static ViewMode lastLookMode = ViewMode.Off;
        private static bool prevMouseLook;
        // mouseLook 中途从关到开时置位：等到首个无阻挡帧直接进入锁定环视，
        // 不要求玩家再点一次画面补锁。
        private static bool mouseLookLatchPending;
        // 第一人称“接管期”：进入第一人称即置位，Esc 释放、点击画面重新接管。
        // 接管期间原版整套 UI 的输入与绘制被跳过（见 VanillaSuppressor），
        // 鼠标/键盘全部由本模组消费，绝不穿透到原界面。
        private static bool inputEngaged;

        // 接管期判定（供 VanillaSuppressor 决定是否跳过原版 UIRootOnGUI）：
        // 已接管且未临时让位（Alt 唤鼠 / 原版弹窗 / 暂停 / 失焦 / 后台 / 原版视角屏 / 无附身对象）。
        public static bool VanillaInputTakenOver =>
            Fps3DState.Active && Fps3DState.Mode == ViewMode.FirstPerson
            && inputEngaged && Possessed != null
            && !MapScreen3D.Open
            && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)
            && Application.isFocused
            && MouseLookDelta.IsGameWindowActive()
            && !VanillaUiBusy();
        // 本帧的这次左键已被"重新锁定光标"消费：不再当作准星点击选择/下令。
        private static bool clickConsumedByLook;

        // 脚点（世界 XZ + 离地高度）；未启用为 null。相机锚点 XZ 用它替代 DrawPos。
        public static Vector3? FootPoint
        {
            get
            {
                if (!footActive || footMap == null || footMap.Disposed
                    || Possessed == null || Possessed.MapHeld != footMap) return null;
                var off = Streaming3D.OffsetOf(footMap);
                return new Vector3(footPos.x + off.x, footHeight, footPos.y + off.z);
            }
        }

        // 附身者在立牌层里的绘制 XZ 覆盖（世界坐标）；非附身者返回 null（仍用 DrawPos）。
        public static Vector2? PawnDrawXZ(Pawn pawn)
        {
            if (!footActive || footMap == null || pawn == null
                || pawn != Possessed || pawn.MapHeld != footMap) return null;
            var off = Streaming3D.OffsetOf(footMap);
            return new Vector2(footPos.x + off.x, footPos.y + off.z);
        }

        public static void EnsureCreated()
        {
            if (camGO != null) return;
            var camGo = GameObject.Find("Camera");
            if (camGo == null) return;
            camGO = camGo;
            cam = camGo.GetComponent<Camera>();
            DontMoveVanilla = false;
        }

        public static bool DontMoveVanilla;

        public static void SetEnabled(bool on)
        {
            // 原版相机始终由游戏驱动开关；此处仅同步状态。
        }

        // 退出 3D 模式：停用状态并释放附身，恢复附身前记录的征召状态与光标。
        public static void Release()
        {
            SetEnabled(false);
            StopMovementControl(Possessed);
            ReleaseMouseLook();
            inputEngaged = false;
            lastLookMode = ViewMode.Off;
            mouseLookLatchPending = false;
            mousePositionReady = false;
            Unpossess();
        }

        public static void OnModeChanged()
        {
            if (Fps3DState.Active)
            {
                EnsureCreated();
                SetEnabled(true);
                var settings = FirstPerson3DMod.settings;
                if (settings != null)
                    settings.mouseLookLatched = settings.mouseLook && Fps3DState.Mode == ViewMode.FirstPerson;
                mousePositionReady = false;
                StopMovementControl(Possessed);
                ResetMovementControl();
                Possess(null);
            }
            else
            {
                Release();
            }
        }

        private static void Possess(Pawn forced)
        {
            Unpossess();
            var pawn = forced ?? AutoPickPawn();
            if (pawn == null) return;
            Possessed = pawn;
            if (pawn.drafter != null)
            {
                prevDrafted = pawn.drafter.Drafted;
                pawn.drafter.Drafted = true;
            }
        }

        private static void Unpossess()
        {
            StopMovementControl(Possessed);
            if (Possessed != null && Possessed.drafter != null && !Possessed.Destroyed)
            {
                try { Possessed.drafter.Drafted = prevDrafted; } catch { }
            }
            Possessed = null;
        }

        private static Pawn AutoPickPawn()
        {
            var map = Find.CurrentMap;
            if (map == null) return null;
            var sel = Find.Selector.SingleSelectedThing as Pawn;
            if (sel != null && sel.Spawned && sel.Map == map) return sel;
            Pawn fallback = null;
            foreach (var p in map.mapPawns.FreeColonistsAndPrisonersSpawned)
            {
                if (p == null || p.Dead || !p.Spawned) continue;
                return p;
            }
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || p.Dead) continue;
                fallback = p;
                break;
            }
            return fallback;
        }

        public static void UpdateCamera(float dt)
        {
            EnsureCreated();
            if (!Fps3DState.Active) return;

            ApplyLookSettings();

            TryPossessByName();
            if (Possessed == null || Possessed.Destroyed || !Possessed.Spawned
                || Possessed.MapHeld != Find.CurrentMap)
            {
                Possess(null);
            }
            // 无附身对象即无第一人称相机：接管必须解除，否则画面冻结还断掉全部输入。
            if (Possessed == null)
            {
                inputEngaged = false;
                return;
            }

            HandleLook();
            HandleMovementOrders(dt);
            HandleClick();
            HandleInteractionKeys();
            // 消费标记只在产生它的这一帧有效，避免残留到后续帧吞掉正常点击。
            clickConsumedByLook = false;

            var st = FirstPerson3DMod.settings;
            float yaw = Yaw, pitch = Pitch;
            Vector3 anchor, eye;
            if (st != null && st.freeCam)
            {
                anchor = new Vector3(st.camX, 0f, st.camZ);
                eye = new Vector3(st.camX, st.camY, st.camZ);
                camGO.transform.position = eye;
                camGO.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                LastCamPos = eye;
                SyncFollow(eye);
                return;
            }
            var dp = Possessed.DrawPos;
            // 附身 pawn 所在地图的楼层基准与水平流式偏移（上层/邻图锚点正确落位）
            float pawnBaseY = 0f;
            Vector3 pawnOffset = Vector3.zero;
            var pscene = SceneRegistry.Find(Possessed.MapHeld);
            if (pscene != null)
            {
                pawnBaseY = pscene.baseY;
                pawnOffset = pscene.worldOffset;
            }
            // 脚点启用时锚点 XZ 取脚点（连续位置），Y = 楼层基准 + 离地高度；否则回退 DrawPos。
            var foot = FootPoint;
            if (foot.HasValue)
            {
                // 身处水体则整具身体下沉（水面落差 + 当前水深），脚踩河床而不浮在水面上方。
                var footCell = new IntVec3(Mathf.FloorToInt(footPos.x), 0, Mathf.FloorToInt(footPos.y));
                float sinkTarget = WaterLayer3D.SinkAt(Possessed.MapHeld, footCell);
                waterSink = Mathf.MoveTowards(waterSink, sinkTarget, Metrics3D.WaterSinkSpeed * dt);
                anchor = new Vector3(foot.Value.x, pawnBaseY + foot.Value.y - waterSink, foot.Value.z);
            }
            else
            {
                waterSink = 0f;
                anchor = new Vector3(dp.x + pawnOffset.x, pawnBaseY, dp.z + pawnOffset.z);
            }

            if (Fps3DState.Mode == ViewMode.FirstPerson)
            {
                eye = anchor + new Vector3(0f, Fps3DState.EyeHeight, 0f);
                camGO.transform.position = eye;
                camGO.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }
            else
            {
                var rot = Quaternion.Euler(pitch, yaw, 0f);
                eye = anchor + rot * new Vector3(0f, 0f, -CamOffsetChase.z) + new Vector3(0f, CamOffsetChase.y, 0f);
                camGO.transform.position = eye;
                camGO.transform.LookAt(anchor + new Vector3(0f, 1.2f, 0f));
            }
            LastCamPos = eye;

            SyncFollow(eye);
        }

        private static void SyncFollow(Vector3 eye)
        {
            syncTimer += Time.deltaTime;
            if (syncTimer > 0.5f)
            {
                syncTimer = 0f;
                // 相机本体即原版相机，无需搬运 AudioListener。
            }
        }

        private static float lastCensusTime;
        private static string cachedCensus = "";

        // 相机普查：枚举场景中所有 Camera 的关键状态（节流缓存，避免每秒遍历全场景 GameObject）。
        public static string CameraCensus()
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastCensusTime < 5f && !string.IsNullOrEmpty(cachedCensus))
                return cachedCensus;
            lastCensusTime = now;
            var sb = new System.Text.StringBuilder();
            var cams = Object.FindObjectsOfType<Camera>();
            int i = 0;
            foreach (var cam in cams)
            {
                if (i++ > 0) sb.Append('|');
                sb.Append(cam.name).Append('/')
                  .Append(cam.enabled ? "on" : "off").Append('/')
                  .Append(cam.gameObject.activeInHierarchy ? "act" : "inact").Append('/')
                  .Append("d").Append(cam.depth.ToString("F0")).Append('/')
                  .Append(cam.orthographic ? "ortho" : "persp").Append('/')
                  .Append(cam.clearFlags.ToString()).Append('/')
                  .Append(((int)(cam.backgroundColor.r * 255)).ToString() + ","
                      + ((int)(cam.backgroundColor.g * 255)).ToString() + ","
                      + ((int)(cam.backgroundColor.b * 255)).ToString());
            }
            sb.Append("|camYaw=").Append(Yaw.ToString("F0")).Append(",pitch=").Append(Pitch.ToString("F0"));
            cachedCensus = sb.ToString();
            return cachedCensus;
        }

        // 每帧应用视觉参数：FOV / 天空色（禁用 Unity 全局线性雾，避免把 540m~678m 的天穹与云顶网格染成地平线断层色带）。
        private static void ApplyLookSettings()
        {
            var st = FirstPerson3DMod.settings;
            if (st == null || cam == null) return;
            if (cam.fieldOfView != st.fov) cam.fieldOfView = st.fov;
            cam.backgroundColor = SkyScenery.CurrentHorizonColor;
        }

        private static void HandleLook()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null) return;

            // 终端抬起期间光标留给屏幕射线。右键拖拽和远程环视也不带动视角。
            if (MapScreen3D.BlocksGameplay)
            {
                YieldCursor();
                return;
            }

            bool freeLook = UpdateMouseLook(s);
            if (!freeLook) MouseLookDelta.Reset();
            if (!freeLook && Input.GetMouseButton(1) && !IsLookInputBlocked())
            {
                Vector2 delta = ReadMouseDelta();
                Yaw += delta.x * 3.2f;
                Pitch = Mathf.Clamp(Pitch - delta.y * 3.2f, -80f, 80f);
            }
            // 合成输入通道：GABS 远程驱动同一环视逻辑
            if (s.cmdLookDx != 0f || s.cmdLookDy != 0f)
            {
                Yaw += s.cmdLookDx;
                Pitch = Mathf.Clamp(Pitch + s.cmdLookDy, -80f, 80f);
                s.cmdLookDx = 0f;
                s.cmdLookDy = 0f;
            }
        }

        private static bool IsLookInputBlocked()
        {
            if (!Application.isFocused || !MouseLookDelta.IsGameWindowActive()) return true;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return true;
            if (Find.TickManager != null && Find.TickManager.Paused) return true;
            var stack = Find.WindowStack;
            return stack != null
                && (stack.AnyWindowAbsorbingAllInput
                    || stack.NonImmediateDialogWindowOpen
                    || stack.WindowsForcePause);
        }

        // 右键拖拽环视的增量：光标可见且未锁定，直接用位置差。
        // 注意：RimWorld 的 InputManager 未定义 "Mouse X"/"Mouse Y" 轴，
        // Input.GetAxis 恒返回 0，这里绝不能依赖它。
        private static Vector2 ReadMouseDelta()
        {
            Vector2 position = Input.mousePosition;
            if (!mousePositionReady)
            {
                lastMousePosition = position;
                mousePositionReady = true;
                return Vector2.zero;
            }

            Vector2 positionDelta = position - lastMousePosition;
            lastMousePosition = position;
            // 单帧增量上限：滤掉远控注入/系统光标跳变产生的巨幅尖峰。
            float px = Mathf.Clamp(positionDelta.x, -40f, 40f);
            float py = Mathf.Clamp(positionDelta.y, -40f, 40f);
            return new Vector2(px * 0.05f, py * 0.05f);
        }

        // 鼠标自由视角 + 第一人称输入接管状态机。
        // 接管（inputEngaged）与光标锁定（mouseLookLatched）解耦：
        // mouseLook 关闭时同样接管输入（原版 UI 照样被跳过），只是光标不锁定。
        // 临时让位（Alt/弹窗/暂停/失焦）只解锁光标，不释放接管；
        // Esc 完全释放，点击非 UI 画面区重新接管。
        private static bool UpdateMouseLook(FirstPerson3DSettings s)
        {
            ViewMode mode = Fps3DState.Mode;
            if (s.mouseLook != prevMouseLook)
            {
                mouseLookLatchPending = s.mouseLook;
                prevMouseLook = s.mouseLook;
            }
            if (mode != lastLookMode)
            {
                mousePositionReady = false;
                if (mode == ViewMode.FirstPerson && Fps3DState.Active)
                {
                    inputEngaged = true;
                    if (s.mouseLook) s.mouseLookLatched = true;
                }
                else
                {
                    inputEngaged = false;
                    s.mouseLookLatched = false;
                }
                lastLookMode = mode;
            }

            if (!Fps3DState.Active || mode != ViewMode.FirstPerson)
            {
                inputEngaged = false;
                ReleaseMouseLook(s);
                return false;
            }

            // 与 HandleLook 的终端/背包早退重复一道：避免以后单独调用时把光标藏起来并回中。
            if (MapScreen3D.BlocksGameplay || FpsHud3D.inventoryOpen)
            {
                YieldCursor();
                return false;
            }

            bool uiBusy = VanillaUiBusy();
            bool paused = Find.TickManager != null && Find.TickManager.Paused;
            // 按住 Alt：临时唤出鼠标做 UI 操作，松开自动回到接管态。
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            // mouseLook 从关到开（含远控面板中途开启）：首个无阻挡帧立即接管并锁定。
            if (mouseLookLatchPending && !altHeld && !uiBusy && !paused && Application.isFocused && MouseLookDelta.IsGameWindowActive())
            {
                mouseLookLatchPending = false;
                inputEngaged = true;
                s.mouseLookLatched = true;
                mousePositionReady = false;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                inputEngaged = false;
                s.mouseLookLatched = false;
                mousePositionReady = false;
            }

            // 临时让位：切后台 / Alt 呼出 / 弹窗 / 暂停，立即释放光标
            if (altHeld || uiBusy || paused || !Application.isFocused || !MouseLookDelta.IsGameWindowActive())
            {
                UnlockCursor();
                return false;
            }

            // 空闲态（Esc 释放后）：点击非 UI 画面区即重新接管
            if (!inputEngaged)
            {
                UnlockCursor();
                if (Input.GetMouseButtonDown(0) && !OverUI())
                {
                    inputEngaged = true;
                    mousePositionReady = false;
                    clickConsumedByLook = true;
                    if (s.mouseLook) s.mouseLookLatched = true;
                }
                return false;
            }

            // 接管中但鼠标锁定关闭：视角走右键拖拽路径，光标保持可见
            if (!s.mouseLook)
            {
                UnlockCursor();
                return false;
            }

            // 不锁定光标：锁定会让 Input.mousePosition 冻结在屏幕中心，而本游戏
            // 又无 "Mouse X/Y" 轴可读增量。改为隐藏光标 + 每帧回中，用位置差
            // 得到连续、无边界限制的环视增量（详见 MouseLookDelta）。
            SetCursorState(CursorLockMode.None, false);
            float sens = Mathf.Max(0.01f, s.lookSensitivity);
            Vector2 delta = MouseLookDelta.Read();
            Yaw += delta.x * sens;
            Pitch = Mathf.Clamp(Pitch - delta.y * sens, -80f, 80f);
            return true;
        }

        private static bool VanillaUiBusy()
        {
            var stack = Find.WindowStack;
            if (stack == null) return false;
            // 右键情境浮动菜单开着时让位：菜单是 Super 层、不吸输入，靠这一条才能让
            // 原版 UIRoot 把它画出来并接收点击（否则接管期整套窗口栈被跳过，菜单画不出也点不动）。
            if (stack.FloatMenu != null) return true;
            return stack.AnyWindowAbsorbingAllInput
                || stack.NonImmediateDialogWindowOpen
                || stack.WindowsForcePause;
        }

        private static void SetCursorState(CursorLockMode lockMode, bool visible)
        {
            if (Cursor.lockState != lockMode) Cursor.lockState = lockMode;
            if (Cursor.visible != visible) Cursor.visible = visible;
        }

        private static void UnlockCursor()
        {
            SetCursorState(CursorLockMode.None, true);
            MouseLookDelta.Reset();
            mousePositionReady = false;
        }

        // 终端占用视野时交出系统光标，并停掉每帧回中，否则点不到屏幕上的按钮。
        public static void YieldCursor()
        {
            UnlockCursor();
            MouseLookDelta.Reset();
        }

        // 光标是否悬停在原版 UI 或第一人称 HUD 上：命中则本次点击交给 UI，不重新锁定。
        private static bool OverUI()
        {
            var stack = Find.WindowStack;
            if (stack != null && stack.GetWindowAt(UI.MousePosUIInvertedUseEventIfCan) != null)
                return true;
            return FpsHud3D.OverHud(UI.MousePosUIInvertedUseEventIfCan);
        }

        public static void ReleaseMouseLook(FirstPerson3DSettings s)
        {
            if (s != null) s.mouseLookLatched = false;
            UnlockCursor();
        }

        // 退出 3D / 离开游戏时统一调用：解除附身并还原光标。
        public static void ReleaseMouseLook()
        {
            ReleaseMouseLook(FirstPerson3DMod.settings);
        }

        // ---- 原版视角屏开关的接管联动 ----

        // 开屏（MapScreen3D.Show）：释放输入接管并解锁光标，交还原版。
        public static void NotifyScreenShown()
        {
            inputEngaged = false;
            ReleaseMouseLook();
        }

        // 关屏（MapScreen3D.Close）：第一人称直接回到接管态，无需再点一次画面。
        public static void ReengageAfterScreen()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null || !Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return;
            inputEngaged = true;
            mousePositionReady = false;
            if (s.mouseLook) s.mouseLookLatched = true;
        }

        // 远程指名附身：possessName 非空则匹配当前地图 pawn（包含匹配，一次有效）。
        private static void TryPossessByName()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null || string.IsNullOrEmpty(s.possessName)) return;
            string want = s.possessName.Trim();
            s.possessName = "";
            var map = Find.CurrentMap;
            if (map == null) return;
            foreach (var p in map.mapPawns.AllPawnsSpawned)
            {
                if (p == null || p.Name == null) continue;
                if (p.Name.ToString().IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || p.def.defName.IndexOf(want, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Possess(p);
                    Log.Message(ModEntry.LogPrefix + "possess -> " + p.Name);
                    return;
                }
            }
            Log.Message(ModEntry.LogPrefix + "possess: no pawn matching '" + want + "'");
        }

        // ---- 连续位移（F003 / T-P1）----
        // 不再为行走下 JobDefOf.Goto：第一人称持有位移期间直接积分附身者位置。
        private static void HandleMovementOrders(float dt)
        {
            var pawn = Possessed;

            // 地图屏幕活动期（抬屏/已开/放屏）/ 背包面板：位移让位（环视保持，脚点冻结）
            if (MapScreen3D.BlocksGameplay || FpsHud3D.BlockMovement)
                return;

            // 附身期间锁死征召：TSS 等共管 AI、事件与超时会把附身者解除征召。
            // 以前只在「有远控指令挂着」时才抢回来，于是键盘玩家一旦被解召就一直动不了 ——
            // 相机钉在原地、被附身者还在几个殖民者之间来回换（实机：cam 恒为 112,2.2,112，
            // 三人全部 drafted=False / job=GotoWander）。现在每帧强制，
            // 退出附身由 Unpossess 还原进图前的征召状态。
            if (pawn != null && pawn.drafter != null && !pawn.Drafted)
                pawn.drafter.Drafted = true;
            if (pawn == null || pawn.drafter == null || !pawn.Drafted)
            {
                DisableFoot();
                return;
            }

            var map = pawn.MapHeld;
            if (map == null || map.Disposed)
            {
                DisableFoot();
                return;
            }

            // 脚点就位：首次启用 / 换图后从 DrawPos 落定（避免跳变）
            EnsureFoot(pawn);

            var st = FirstPerson3DMod.settings;

            // 合成传送：仍是瞬移（DeSpawn/Spawn），落定后脚点重挂
            if (st != null && !string.IsNullOrEmpty(st.cmdTeleport))
            {
                HandleTeleport(pawn, st);
                SnapFootToPawn(pawn);
            }

            // ---- 采集方向输入（WASD + cmdWalkHold + cmdKey 单步）----
            Vector2 dir = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) dir.y += 1f;
            if (Input.GetKey(KeyCode.S)) dir.y -= 1f;
            if (Input.GetKey(KeyCode.A)) dir.x -= 1f;
            if (Input.GetKey(KeyCode.D)) dir.x += 1f;

            if (st != null)
            {
                if (st.cmdWalkHold > 0f)
                {
                    st.cmdWalkHold -= dt;
                    dir.y += 1f;
                }
                if (!string.IsNullOrEmpty(st.cmdKey))
                {
                    HandleSyntheticStep(st.cmdKey);
                    st.cmdKey = "";
                }
            }

            // cmdKey 单步：注入世界方向直到走满剩余格数
            Vector2 stepInput = stepRemaining > 0f ? stepDir : Vector2.zero;

            // 焦点/Alt 门控只针对物理键盘按住（防切窗误触）；
            // 合成通道（cmdKey/cmdWalkHold）是远控指令，后台无焦点时必须照常下发。
            bool physicalHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A)
                || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D);
            bool focusBlocked = physicalHeld
                && (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)
                    || !Application.isFocused);

            bool paused = Find.TickManager != null && Find.TickManager.Paused;
            bool windowBlocked = false;
            if (Find.WindowStack != null)
            {
                windowBlocked = Find.WindowStack.AnyWindowAbsorbingAllInput
                    || Find.WindowStack.NonImmediateDialogWindowOpen
                    || Find.WindowStack.WindowsForcePause
                    // 右键浮动菜单开着：光标已交给菜单，脚点冻结，避免选项还没点人就走了。
                    || Find.WindowStack.FloatMenu != null;
            }

            // 蓝图供料完成后若转为 Frame 且材料已齐，自动衔接 FinishFrame 建造工序
            TryContinuePendingConstruct(pawn);

            // 原版 job 持有者（挖矿 / 搬运 / 建造 / 工作台 / 施法）：
            // 若玩家主动按 WASD 移动（且距离下令已超过 0.25s），则中断当前作业交还移动控制；
            // 否则这一帧让原版驱动，相机与立牌临时回退到 DrawPos。
            if (JobOwnsPawn(pawn))
            {
                if (physicalHeld && !focusBlocked && !paused && !windowBlocked
                    && Time.realtimeSinceStartup - lastOrderedJobTime > 0.25f)
                {
                    pendingConstructCell = IntVec3.Invalid;
                    try { pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced); } catch { }
                    SnapFootToPawn(pawn);
                }
                else
                {
                    DisableFoot();
                    return;
                }
            }

            // 暂停 / 窗口 / 焦点门控：水平与竖直速度都记为 0，不补移动（脚点冻结）
            if (paused || windowBlocked || focusBlocked)
            {
                vy = 0f;
                spacePrev = Input.GetKey(KeyCode.Space);
                return;
            }

            // ---- 本帧持有位移：停掉 pather，避免寻路把人拉回格心 ----
            if (pawn.pather != null) pawn.pather.StopDead();

            // 水平：按当前偏航把输入换成世界 XZ；叠加 cmdKey 单步注入
            Vector2 moveWorld = Vector2.zero;
            if (dir.sqrMagnitude > 0.0001f)
            {
                dir.Normalize();
                float yaw = Yaw;
                var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                var worldDir = fwd * dir.y + right * dir.x;
                moveWorld = new Vector2(worldDir.x, worldDir.z);
            }
            if (stepInput.sqrMagnitude > 0.0001f)
                moveWorld += stepInput;
            if (moveWorld.sqrMagnitude > 0.0001f)
            {
                moveWorld.Normalize();
                float speed = pawn.GetStatValue(StatDefOf.MoveSpeed);
                float moved = IntegrateHorizontal(map, moveWorld * (speed * dt));
                if (stepRemaining > 0f)
                {
                    stepRemaining -= moved;
                    if (stepRemaining <= 0f) stepDir = Vector2.zero;
                }
            }

            // 竖直：着地空格起跳 + 重力积分（地面 = 脚下格支撑面：地面或家具顶面）
            IntegrateVertical(map, dt);

            // 跨格写回 pawn.Position：走原版格子变更，门 / 陷阱 / 区域继续生效
            var cell = new IntVec3(Mathf.FloorToInt(footPos.x), 0, Mathf.FloorToInt(footPos.y));
            if (cell.InBounds(map) && cell != pawn.Position)
            {
                pawn.Position = cell;
                pawn.pather?.ResetToCurrentPosition();
            }
        }

        // 只测目标点所在格：先试 XZ，不行再试单轴 X、单轴 Z。返回实际位移距离（格）。
        // 不加皮肤半径、不加 MeshCollider：人可站到格子边缘、贴着墙面。
        private static float IntegrateHorizontal(Map map, Vector2 delta)
        {
            if (delta.sqrMagnitude <= 0f) return 0f;
            Vector2 desired = footPos + delta;
            if (StandableAt(map, desired)) { footPos = desired; return delta.magnitude; }
            var onlyX = new Vector2(desired.x, footPos.y);
            if (StandableAt(map, onlyX)) { footPos = onlyX; return Mathf.Abs(delta.x); }
            var onlyZ = new Vector2(footPos.x, desired.y);
            if (StandableAt(map, onlyZ)) { footPos = onlyZ; return Mathf.Abs(delta.y); }
            return 0f;
        }

        // 目标点所在格可站：格在图内、Walkable、且没有 Fillage.Full 的 edifice。
        // 门仅在已开启（door.Open）或玩家已站在该门格内时允许穿行；关闭的门阻挡直穿，需按 E 打开。
        private static bool StandableAt(Map map, Vector2 local)
        {
            var c = new IntVec3(Mathf.FloorToInt(local.x), 0, Mathf.FloorToInt(local.y));
            if (!c.InBounds(map)) return false;
            if (!c.Walkable(map)) return false;
            // 家具顶面高出脚底太多：挡在侧面，必须跳上去（不自动抬升）。
            if (Furniture3D.SupportAt(map, c) > footHeight + Metrics3D.StepTolerance) return false;
            var ed = c.GetEdifice(map);
            if (ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full)
            {
                if (ed is Building_Door door)
                {
                    var curCell = new IntVec3(Mathf.FloorToInt(footPos.x), 0, Mathf.FloorToInt(footPos.y));
                    if (door.Open || curCell == c || door.OccupiedRect().Contains(curCell))
                        return true;
                }
                return false;
            }
            return true;
        }

        // 竖直：地面 = 脚下格的支撑面（地面 0，或家具顶面）；着地时按空格起跳；
        // 每帧 vy -= Gravity*dt，离地高度 += vy*dt；下落穿过支撑面时钳住并清零竖直速度。
        private static void IntegrateVertical(Map map, float dt)
        {
            float support = SupportUnderFoot(map);
            bool space = Input.GetKey(KeyCode.Space);
            bool grounded = footHeight <= support + 0.05f;
            if (grounded && space && !spacePrev)
            {
                vy = Mathf.Sqrt(2f * Metrics3D.Gravity * Metrics3D.JumpApex);
            }
            spacePrev = space;

            vy -= Metrics3D.Gravity * dt;
            footHeight += vy * dt;
            if (vy <= 0f && footHeight <= support)
            {
                footHeight = support;
                vy = 0f;
            }
        }

        /// <summary>脚下格的支撑面高度（米，相对 baseY）：地面 0，或家具顶面。</summary>
        private static float SupportUnderFoot(Map map)
        {
            var c = new IntVec3(Mathf.FloorToInt(footPos.x), 0, Mathf.FloorToInt(footPos.y));
            return Furniture3D.SupportAt(map, c);
        }

        // 这些作业是玩家在第一人称里主动下发的：作业期间让原版驱动，本帧不接管位移，
        // 相机与立牌临时回退到 DrawPos，等作业结束（或被取消）再重新接管。
        private static readonly HashSet<JobDef> OwningJobs = new HashSet<JobDef>();
        private static float lastOrderedJobTime;
        private static IntVec3 pendingConstructCell = IntVec3.Invalid;

        private static void EnsureOwningJobs()
        {
            if (OwningJobs.Count > 0) return;
            void Add(JobDef d) { if (d != null) OwningJobs.Add(d); }
            Add(JobDefOf.Mine);
            Add(JobDefOf.TakeInventory);
            Add(JobDefOf.Equip);
            Add(JobDefOf.Wear);
            Add(JobDefOf.Ingest);
            Add(JobDefOf.Harvest);
            Add(JobDefOf.CutPlant);
            Add(JobDefOf.Deconstruct);
            Add(JobDefOf.Uninstall);
            Add(JobDefOf.FinishFrame);
            Add(JobDefOf.HaulToContainer);
            Add(JobDefOf.HaulToCell);
            Add(JobDefOf.PlaceNoCostFrame);
            Add(JobDefOf.DoBill);
            Add(JobDefOf.Research);
            Add(JobDefOf.Repair);
            Add(JobDefOf.Refuel);
            Add(JobDefOf.SmoothFloor);
            Add(JobDefOf.SmoothWall);
            Add(JobDefOf.Open);
            Add(JobDefOf.Flick);
            Add(JobDefOf.Rescue);
            Add(JobDefOf.Capture);
            Add(JobDefOf.TendPatient);
            Add(JobDefOf.LayDown);
        }

        // 附身者当前 job 是玩家交互下发的作业，或正在施放动词时：让原版驱动。
        private static bool JobOwnsPawn(Pawn pawn)
        {
            try
            {
                EnsureOwningJobs();
                var job = pawn.CurJob;
                // 原版菜单还会下发 Goto、Haul 等不在固定列表中的任务。
                // 玩家强制任务执行期间必须交还寻路，不能每帧 StopDead。
                if (job != null && job.playerForced) return true;
                if (job != null && job.def != null && OwningJobs.Contains(job.def)) return true;
                if (pawn.stances != null && pawn.stances.curStance is Stance_Warmup) return true;
            }
            catch { }
            return false;
        }

        // cmdKey 单步：按当前偏航解析 W/A/S/D 为世界方向，走满一格后自动停。
        // 语义与旧的 Goto 单格一致，只是不再走寻路。
        private static void HandleSyntheticStep(string cmd)
        {
            string k = cmd.Trim().ToUpperInvariant();
            Vector2 fwd = YawForward(), right = YawRight();
            if (k == "W") stepDir = fwd;
            else if (k == "S") stepDir = -fwd;
            else if (k == "A") stepDir = -right;
            else if (k == "D") stepDir = right;
            else { stepDir = Vector2.zero; stepRemaining = 0f; return; }
            stepRemaining = 1f;
        }

        // cmdTeleport("x,z")：仍是瞬移——DeSpawn + GenSpawn，保留征召；调用方随后重挂脚点。
        private static void HandleTeleport(Pawn pawn, FirstPerson3DSettings st)
        {
            var parts = st.cmdTeleport.Split(',');
            st.cmdTeleport = "";
            if (parts.Length != 2
                || !int.TryParse(parts[0].Trim(), out var tx)
                || !int.TryParse(parts[1].Trim(), out var tz)) return;
            var cell = new IntVec3(tx, 0, tz);
            var homeMap = pawn.MapHeld;
            if (homeMap == null || !cell.InBounds(homeMap) || !cell.Walkable(homeMap)) return;
            bool wasDrafted = pawn.Drafted;
            pawn.DeSpawn();
            GenSpawn.Spawn(pawn, cell, homeMap);
            if (wasDrafted && pawn.drafter != null) pawn.drafter.Drafted = true;
            if (camGO != null && Fps3DState.Mode == ViewMode.FirstPerson)
            {
                var dp = pawn.DrawPos;
                camGO.transform.position = new Vector3(dp.x, Fps3DState.EyeHeight, dp.z);
                camGO.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
                LastCamPos = camGO.transform.position;
            }
            lastRaycastFrame = -1;
            Log.Message(ModEntry.LogPrefix + "teleport -> " + cell);
        }

        public static void TeleportAndAim(IntVec3 cell, float yaw, float pitch)
        {
            EnsureCreated();
            if (Possessed == null || Possessed.Destroyed || !Possessed.Spawned || Possessed.MapHeld != Find.CurrentMap)
                Possess(null);
            var pawn = Possessed;
            var st = FirstPerson3DMod.settings;
            if (st != null)
            {
                st.cmdTeleport = "";
                st.camYaw = yaw;
                st.camPitch = pitch;
            }
            var homeMap = pawn?.MapHeld ?? Find.CurrentMap;
            if (pawn != null && homeMap != null && cell.InBounds(homeMap))
            {
                bool wasDrafted = pawn.Drafted;
                if (pawn.Spawned) pawn.DeSpawn();
                GenSpawn.Spawn(pawn, cell, homeMap);
                if (wasDrafted && pawn.drafter != null) pawn.drafter.Drafted = true;
                SnapFootToPawn(pawn);
                if (camGO != null)
                {
                    var dp = pawn.DrawPos;
                    camGO.transform.position = new Vector3(dp.x, Fps3DState.EyeHeight, dp.z);
                    camGO.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                    LastCamPos = camGO.transform.position;
                }
            }
            lastRaycastFrame = -1;
        }

        private static Vector2 YawForward()
        {
            var f = Quaternion.Euler(0f, Yaw, 0f) * Vector3.forward;
            return new Vector2(f.x, f.z).normalized;
        }

        private static Vector2 YawRight()
        {
            var r = Quaternion.Euler(0f, Yaw, 0f) * Vector3.right;
            return new Vector2(r.x, r.z).normalized;
        }

        // 脚点：首次启用 / 换图后从 DrawPos 落定（保证不跳变）
        private static void EnsureFoot(Pawn pawn)
        {
            if (footActive && footMap != null && !footMap.Disposed && footMap == pawn.MapHeld) return;
            SnapFootToPawn(pawn);
        }

        private static void SnapFootToPawn(Pawn pawn)
        {
            var dp = pawn.DrawPos;
            footMap = pawn.MapHeld;
            footPos = new Vector2(dp.x, dp.z);
            footHeight = 0f;
            waterSink = 0f;
            vy = 0f;
            spacePrev = false;
            stepDir = Vector2.zero;
            stepRemaining = 0f;
            footActive = true;
        }

        private static void DisableFoot()
        {
            footActive = false;
            footMap = null;
            footHeight = 0f;
            waterSink = 0f;
            vy = 0f;
            spacePrev = false;
            stepDir = Vector2.zero;
            stepRemaining = 0f;
        }

        private static void StopMovementControl(Pawn pawn)
        {
            DisableFoot();
        }

        private static void ResetMovementControl()
        {
            DisableFoot();
        }

        private static void HandleClick()
        {
            if (attackCooldown > 0f) attackCooldown -= Time.deltaTime;
            // 终端抬起期间的鼠标是屏幕平移/点选，不走准星射线。
            if (MapScreen3D.BlocksGameplay)
            {
                var pending = FirstPerson3DMod.settings;
                if (pending != null) pending.cmdClick = "";
                return;
            }
            var st = FirstPerson3DMod.settings;
            bool synthetic = false;
            if (st != null && !string.IsNullOrEmpty(st.cmdClick))
            {
                st.cmdClick = "";
                synthetic = true;
            }

            if (!synthetic)
            {
                // 浮动菜单开着：鼠标交给菜单（点选项 / 点外部关闭），本帧不下准星动作。
                if (Find.WindowStack != null && Find.WindowStack.FloatMenu != null) return;
                // 背包面板打开时不攻击/交互（面板内的点击交给 GUI 按钮）。
                if (FpsHud3D.inventoryOpen) return;
                // 光标在 UI 上时不执行 3D 动作，右键与左键完整留给原版 UI。
                if (OverUI()) return;
            }

            // 左键攻击：按下立即一次；按住则以 AttackInterval 连击。
            // 旧写法用 GetMouseButtonDown(0) 早退，把“按住连击”整条堵死，只出第一下。
            bool isRanged = Possessed != null && Possessed.equipment != null
                && Possessed.equipment.Primary != null && Possessed.equipment.Primary.def.IsRangedWeapon;
            float cdInterval = isRanged ? 0.05f : AttackInterval;
            if (synthetic) { Attack(); attackCooldown = cdInterval; return; }
            // 该次左键已被“重新锁定自由视角”消费，不再当作攻击。
            if (clickConsumedByLook) { clickConsumedByLook = false; return; }
            if (GUIUtility.hotControl != 0 || OverUI()) return;
            if (Input.GetMouseButtonDown(0)) { Attack(); attackCooldown = cdInterval; }
            else if (Input.GetMouseButton(0) && attackCooldown <= 0f) { Attack(); attackCooldown = cdInterval; }
        }

        // 键盘交互热键与快捷操作：E 键情境交互、F 键/右键原版菜单、Tab/B 键背包物品栏
        private static void HandleInteractionKeys()
        {
            if (MapScreen3D.BlocksGameplay) return;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return;
            if (!Application.isFocused || !MouseLookDelta.IsGameWindowActive()) return;

            // 背包打开时按 Escape、Tab 或 B 关闭背包
            if (FpsHud3D.inventoryOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.B))
                {
                    FpsHud3D.CloseInventory();
                }
                return;
            }

            // 弹窗、暂停或光标在 UI 上时不响应 3D 快捷热键
            if (VanillaUiBusy() || OverUI()) return;

            // E 键：全部交互与浮动菜单（原右键全部功能迁移至此）。门等直接开关，工作台/角色/物品/地块等呼出原版浮动指令菜单
            if (Input.GetKeyDown(KeyCode.E))
            {
                Interact();
                return;
            }

            // F 键：搬运（准星处可搬运地面物端在身前随身携带；手上已端着则就地放下）
            if (Input.GetKeyDown(KeyCode.F))
            {
                CarryAimed();
                return;
            }

            // G 键：拆解准星处建筑
            if (Input.GetKeyDown(KeyCode.G))
            {
                DemolishAimed();
                return;
            }

            // Tab 或 B 键：第一人称物品栏与背包开关
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.B))
            {
                FpsHud3D.ToggleInventory();
                return;
            }

            // 数字键 1..6：使用背包第 n 件物品（装备 / 穿戴 / 食用）
            for (int i = 1; i <= Backpack3D.HotbarSlots; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha0 + i))
                {
                    UseHotbar(i);
                    return;
                }
            }
        }

        // 按住左键的连击节奏（秒），与挥击动画时长（0.26s）接近。
        private const float AttackInterval = 0.30f;
        private static float attackCooldown;

        // E 键 / 浮动菜单：对准命中实体目标呼出完整原版地图浮动菜单。优先走 FloatMenuMakerMap，
        // 让工作台配方、翻找储物、装卸、使用、治疗、逮捕、收割等所有原版
        // FloatMenuOptionProvider 都参与生成；严禁空地响应或弹出“走到那里”。
        public static bool OpenAimedFloatMenu()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return false;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return false;
            if (Find.WindowStack == null || Find.WindowStack.FloatMenu != null) return false;
            var pawn = Possessed;
            if (pawn == null) return false;
            var hit = RaycastCenter();
            if (hit == null) return false;

            // 严格要求命中实体目标且在触及范围内，空地坚决不弹菜单、不寻路
            var thing = hit.Value.thing;
            if (thing == null || thing.Destroyed || hit.Value.dist > ReachWork) return false;

            var aimedCell = hit.Value.cell;
            var map = Find.CurrentMap;
            if (map == null || !aimedCell.IsValid || !aimedCell.InBounds(map)) return false;
            if (!pawn.Spawned || pawn.Map != map) return false;
            if (thing.Map != map) return false;

            // FloatMenuMap 的重验证依赖 Find.Selector.SelectedPawns；让附身 pawn 成为
            // 原版菜单的唯一操作者，而不是把准星目标 Thing 错当成选择对象。
            Find.Selector.ClearSelection();
            Find.Selector.Select(pawn);
            Vector3 clickPos = aimedCell.ToVector3Shifted();
            var opts = new List<FloatMenuOption>();
            FloatMenuContext context = null;
            bool vanillaMenuBuilt = false;
            try
            {
                opts = FloatMenuMakerMap.GetOptions(new List<Pawn> { pawn }, clickPos, out context);
                vanillaMenuBuilt = context != null && opts != null && opts.Count > 0;
            }
            catch { }

            if (vanillaMenuBuilt)
            {
                // 彻底过滤掉任何带有“走到这里/走到那里”的移动指令选项，第一人称移动由 WASD 控制
                opts.RemoveAll(o => o == null || o.isGoto || o.Priority == MenuOptionPriority.GoHere
                    || (o.Label != null && o.Label.IndexOf("GoHere".Translate(), System.StringComparison.OrdinalIgnoreCase) >= 0));

                if (opts.Count > 0)
                {
                    string labelCap = context.IsMultiselect ? null : context.FirstSelectedPawn.LabelCap;
                    var menu = new RightBottomFloatMenuMap(opts, labelCap, clickPos)
                    {
                        givesColonistOrders = true,
                        vanishIfMouseDistant = false
                    };
                    // 菜单接管后立即释放第一人称光标，避免本帧/下一帧鼠标视角或攻击穿透。
                    ReleaseMouseLook();
                    Find.WindowStack.Add(menu);
                    return true;
                }
            }

            // 兼容原版/第三方 provider 生成失败的情况，保留原有 Thing 菜单行为。
            opts = new List<FloatMenuOption>();
            try
            {
                foreach (var o in thing.GetFloatMenuOptions(pawn))
                    if (o != null) opts.Add(o);
            }
            catch { }
            opts.RemoveAll(o => o == null || o.isGoto || o.Priority == MenuOptionPriority.GoHere
                || (o.Label != null && o.Label.IndexOf("GoHere".Translate(), System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (opts.Count == 0) return false;
            ReleaseMouseLook();
            Find.WindowStack.Add(new RightBottomFloatMenu(opts));
            return true;
        }

        // F/右键菜单固定贴在第一人称自绘准星的右下方，不跟随原版 UI 或当前鼠标位置漂移。
        // 在初始化钩子里设置，避免 WindowStack 首帧重新初始化时覆盖 windowRect。
        private sealed class RightBottomFloatMenu : FloatMenu
        {
            private const float Edge = 8f;

            public RightBottomFloatMenu(List<FloatMenuOption> options) : base(options) { }

            protected override void SetInitialSizeAndPosition()
            {
                base.SetInitialSizeAndPosition();
                PlaceNextToCrosshair(ref windowRect);
            }
        }

        private sealed class RightBottomFloatMenuMap : FloatMenuMap
        {
            private const float Edge = 8f;

            public RightBottomFloatMenuMap(List<FloatMenuOption> options, string title, Vector3 clickPos)
                : base(options, title, clickPos) { }

            protected override void SetInitialSizeAndPosition()
            {
                base.SetInitialSizeAndPosition();
                PlaceNextToCrosshair(ref windowRect);
            }
        }

        private static void PlaceNextToCrosshair(ref Rect rect)
        {
            var settings = FirstPerson3DMod.settings;
            float scale = settings != null ? Mathf.Max(0.2f, settings.crosshairScale) : 1f;
            float arm = 7f * scale;
            const float Gap = 10f;
            float cx = UI.screenWidth * 0.5f;
            float cy = UI.screenHeight * 0.5f;

            // GUI 坐标原点在左上角，因此“右下”就是 x/y 同时增大。
            // 先贴准星，再在屏幕边缘处做最小夹紧，保证整张菜单仍可点击。
            rect.x = cx + arm + Gap;
            rect.y = cy + arm + Gap;
            rect.x = Mathf.Min(rect.x, UI.screenWidth - rect.width - 8f);
            rect.y = Mathf.Min(rect.y, UI.screenHeight - rect.height - 8f);
            rect.x = Mathf.Max(8f, rect.x);
            rect.y = Mathf.Max(8f, rect.y);
        }

        // 交互触及距离：门/拾取/装备/使用允许约 3 格触及；挖掘/拆解/砍伐/收割/建造/工作台同距。
        private const float ReachUse = 3.2f;
        private const float ReachWork = 3.4f;

        private static readonly System.Reflection.MethodInfo DoorTryCloseMethod =
            HarmonyLib.AccessTools.Method(typeof(Building_Door), "DoorTryClose");
        private static readonly System.Reflection.FieldInfo DoorHoldOpenField =
            HarmonyLib.AccessTools.Field(typeof(Building_Door), "holdOpenInt");
        private static readonly System.Reflection.FieldInfo RemoveBuildingWorkLeftField =
            HarmonyLib.AccessTools.Field(typeof(JobDriver_RemoveBuilding), "workLeft");
        private static readonly System.Reflection.FieldInfo RemoveBuildingTotalWorkField =
            HarmonyLib.AccessTools.Field(typeof(JobDriver_RemoveBuilding), "totalNeededWork");
        private static readonly System.Reflection.FieldInfo PlantWorkDoneField =
            HarmonyLib.AccessTools.Field(typeof(JobDriver_PlantWork), "workDone");
        private static readonly System.Reflection.FieldInfo MineTicksToPickHitField =
            HarmonyLib.AccessTools.Field(typeof(JobDriver_Mine), "ticksToPickHit");
        private static readonly System.Reflection.FieldInfo VerbCurrentTargetField =
            HarmonyLib.AccessTools.Field(typeof(Verb), "currentTarget");
        private static readonly System.Reflection.FieldInfo VerbCurrentDestinationField =
            HarmonyLib.AccessTools.Field(typeof(Verb), "currentDestination");

        // 枪械射击：向准星方向发射子弹并触发枪口火光与后座
        private static bool ShootRangedWeapon(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null) return false;
            var eq = pawn.equipment?.Primary;
            if (eq == null || !eq.def.IsRangedWeapon) return false;
            var verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (verb == null) return false;

            // 正在冷却中或正在连射中时等待
            if (pawn.stances != null && pawn.stances.curStance is Stance_Cooldown cd && cd.ticksLeft > 0)
                return false;
            if (verb.state == VerbState.Bursting)
                return false;

            // 获取准星对准的目标
            var hit = RaycastCenter();
            LocalTargetInfo target;
            if (hit != null && hit.Value.thing != null && !hit.Value.thing.Destroyed && hit.Value.thing != pawn)
            {
                target = new LocalTargetInfo(hit.Value.thing);
            }
            else if (hit != null && hit.Value.cell.IsValid && hit.Value.cell.InBounds(pawn.Map))
            {
                target = new LocalTargetInfo(hit.Value.cell);
            }
            else
            {
                Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
                float range = verb.verbProps.range;
                Vector3 targetWorld = pawn.DrawPos + fwd * range;
                IntVec3 cell = new IntVec3(
                    Mathf.Clamp(Mathf.FloorToInt(targetWorld.x), 0, pawn.Map.Size.x - 1),
                    0,
                    Mathf.Clamp(Mathf.FloorToInt(targetWorld.z), 0, pawn.Map.Size.z - 1));
                target = new LocalTargetInfo(cell);
            }

            // 避免目标落在脚下本格
            if (target.Cell == pawn.Position)
            {
                Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
                IntVec3 offset = new IntVec3(Mathf.RoundToInt(fwd.x), 0, Mathf.RoundToInt(fwd.z));
                if (offset == IntVec3.Zero) offset = IntVec3.North;
                target = new LocalTargetInfo(pawn.Position + offset);
            }

            // 射程钳制
            float maxRange = verb.verbProps.range;
            float dist = (target.Cell - pawn.Position).LengthHorizontal;
            if (dist > maxRange)
            {
                Vector3 d = (target.Cell - pawn.Position).ToVector3();
                d.y = 0f;
                if (d.sqrMagnitude > 0.001f)
                {
                    Vector3 clamped = pawn.Position.ToVector3Shifted() + d.normalized * Mathf.Max(1f, maxRange - 0.5f);
                    IntVec3 c = clamped.ToIntVec3();
                    if (c.InBounds(pawn.Map)) target = new LocalTargetInfo(c);
                }
            }

            Viewmodel3D.TriggerAttack();
            if (verb.TryStartCastOn(target))
            {
                if (verb.WarmupTime > 0f)
                    verb.WarmupComplete();
            }
            else
            {
                VerbCurrentTargetField?.SetValue(verb, target);
                VerbCurrentDestinationField?.SetValue(verb, target);
                verb.WarmupComplete();
            }
            return true;
        }

        // 左键动作：对 Pawn 执行攻击或射击；对物体/建筑/植物/岩石执行破坏性交互（拆除、砍伐、挖矿、收获等）。
        public static void Attack()
        {
            var pawn = Possessed;
            if (pawn == null || pawn.Dead || !pawn.Spawned) return;

            // 持械枪械射击：对准星方向开火射击并实际发射子弹
            if (pawn.equipment != null && pawn.equipment.Primary != null && pawn.equipment.Primary.def.IsRangedWeapon)
            {
                ShootRangedWeapon(pawn);
                return;
            }

            Viewmodel3D.TriggerAttack();
            var hit = RaycastCenter();
            if (hit == null) return;
            var thing = hit.Value.thing;
            if (thing == null || thing.Destroyed) return;

            var target = thing as Pawn;
            if (target != null)
            {
                if (target == pawn || target.Dead) return;
                AttackThing(pawn, target);
                return;
            }

            if (hit.Value.dist <= ReachWork)
            {
                ApplyDestructiveInteraction(pawn, thing);
            }
        }

        // E 键交互与菜单统合：门等即时交互直接执行；其余准星目标呼出原版浮动指令菜单（包含配方选择、救援/俘虏、装备穿戴、优先工作等全部操作）。
        // 准星指向空地或超出触及范围时坚决不响应，严禁出现“走到那里/走到这里”的移动行为。
        public static void Interact()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return;
            var pawn = Possessed;
            if (pawn == null) return;
            var hit = RaycastCenter();
            if (hit == null) return;

            // 准星指向空地（无实体）或超出触及距离：坚决忽略，不响应亦不寻路
            var thing = hit.Value.thing;
            if (thing == null || thing.Destroyed || hit.Value.dist > ReachWork) return;

            // 1) 门：最高频操作，贴身按 E 瞬间开关门
            if (thing is Building_Door door && hit.Value.dist <= ReachUse)
            {
                ToggleDoor(pawn, door);
                return;
            }

            // 2) 呼出原版浮动指令菜单（全部配方、救援/俘虏、装备穿戴、使用等，已过滤 GoHere 寻路）
            if (OpenAimedFloatMenu())
            {
                return;
            }

            // 3) 兜底：若未生成浮动菜单，对命中物体执行单一情境动作
            Find.Selector.Select(thing);
            if (TryResolveNonDestructiveInteraction(pawn, thing, hit.Value.dist, out _, out var perform) && perform != null)
            {
                perform();
            }
        }

        // 准星下方 [E] 非破坏性交互提示文案（如 "[E] 收获"）；无可用交互或超出触及距离时返回 null。
        public static string GetAimedInteractPrompt()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return null;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return null;
            var pawn = Possessed;
            if (pawn == null) return null;
            var hit = RaycastCenter();
            if (hit == null || hit.Value.thing == null || hit.Value.thing.Destroyed) return null;
            if (hit.Value.dist > ReachWork) return null;
            if (TryResolveNonDestructiveInteraction(pawn, hit.Value.thing, hit.Value.dist, out string label, out _)
                && !string.IsNullOrEmpty(label))
            {
                return "[E] " + label;
            }
            return null;
        }

        // 查询附身者当前正在执行的作业进度（0..1），供准星下方绘制原版样式的进度条；无进度作业时返回 null。
        public static float? GetActiveWorkProgress()
        {
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return null;
            var pawn = Possessed;
            if (pawn == null || !pawn.Spawned || pawn.jobs == null) return null;
            var job = pawn.CurJob;
            var driver = pawn.jobs.curDriver;
            if (job == null || job.def == null || driver == null) return null;
            if (pawn.pather != null && pawn.pather.Moving) return null;

            try
            {
                // 1) 拆除 / 卸载（JobDriver_RemoveBuilding）
                if (driver is JobDriver_RemoveBuilding)
                {
                    float workLeft = RemoveBuildingWorkLeftField != null ? (float)RemoveBuildingWorkLeftField.GetValue(driver) : 0f;
                    float total = RemoveBuildingTotalWorkField != null ? (float)RemoveBuildingTotalWorkField.GetValue(driver) : 0f;
                    if (total > 0.01f)
                        return Mathf.Clamp01(1f - workLeft / total);
                }

                // 2) 砍伐 / 收获 / 割除植物（JobDriver_PlantWork）
                if (driver is JobDriver_PlantWork)
                {
                    var plant = job.targetA.Thing as Plant;
                    if (plant != null && plant.def != null && plant.def.plant != null)
                    {
                        float total = Mathf.Max(1f, plant.def.plant.harvestWork);
                        float done = PlantWorkDoneField != null ? (float)PlantWorkDoneField.GetValue(driver) : 0f;
                        return Mathf.Clamp01(done / total);
                    }
                }

                // 3) 挖矿（JobDriver_Mine）：按原版岩石耐久 + 当前镐击周期平滑插值
                if (driver is JobDriver_Mine)
                {
                    var rock = job.targetA.Thing;
                    if (rock != null && !rock.Destroyed && rock.MaxHitPoints > 0)
                    {
                        float speed = Mathf.Max(0.05f, pawn.GetStatValue(StatDefOf.MiningSpeed));
                        float swingTicks = Mathf.Max(1f, 100f / speed);
                        int ticksLeft = MineTicksToPickHitField != null ? (int)MineTicksToPickHitField.GetValue(driver) : (int)swingTicks;
                        float swingFrac = Mathf.Clamp01(1f - ticksLeft / swingTicks);
                        float dmgPerSwing = (rock.def.building != null && rock.def.building.isNaturalRock && !rock.def.building.isResourceRock) ? 80f : 40f;
                        float effectiveHp = Mathf.Max(0f, rock.HitPoints - swingFrac * dmgPerSwing);
                        return Mathf.Clamp01(1f - effectiveHp / Mathf.Max(1f, (float)rock.MaxHitPoints));
                    }
                }

                // 4) 建造框架或蓝图供料（JobDriver_ConstructFinishFrame / JobDriver_HaulToContainer）
                if (driver is JobDriver_ConstructFinishFrame)
                {
                    var frame = job.targetA.Thing as Frame;
                    if (frame != null && !frame.Destroyed)
                        return Mathf.Clamp01(frame.PercentComplete);
                }
                if (driver is JobDriver_HaulToContainer && driver.ticksLeftThisToil > 0)
                {
                    var dest = job.GetTarget(TargetIndex.B).Thing;
                    if (dest is Blueprint || dest is Frame)
                    {
                        float dur = (dest.def?.building != null && dest.def.building.haulToContainerDuration > 0)
                            ? dest.def.building.haulToContainerDuration
                            : 90f;
                        return Mathf.Clamp01(1f - driver.ticksLeftThisToil / dur);
                    }
                }

                // 5) 工作台账单制作（JobDriver_DoBill）
                if (driver is JobDriver_DoBill doBill && job.bill != null && job.bill.recipe != null)
                {
                    if (doBill.ticksSpentDoingRecipeWork > 0 || doBill.workLeft > 0f)
                    {
                        Thing uft = job.GetTarget(TargetIndex.B).Thing;
                        float total = (job.bill is Bill_Mech bm && bm.State == FormingState.Formed)
                            ? 300f
                            : job.bill.recipe.WorkAmountTotal(uft);
                        if (total > 0.01f && doBill.workLeft > 0f)
                            return Mathf.Clamp01(1f - doBill.workLeft / total);
                    }
                }

                // 6) 研究（JobDriver_Research）
                if (driver is JobDriver_Research)
                {
                    var proj = Find.ResearchManager != null ? Find.ResearchManager.GetProject() : null;
                    if (proj != null)
                        return Mathf.Clamp01(proj.ProgressPercent);
                }

                // 7) 修理建筑（JobDriver_Repair）
                if (driver is JobDriver_Repair)
                {
                    var b = job.targetA.Thing;
                    if (b != null && !b.Destroyed && b.MaxHitPoints > 0 && b.HitPoints < b.MaxHitPoints)
                        return Mathf.Clamp01(b.HitPoints / (float)b.MaxHitPoints);
                }
            }
            catch { }
            return null;
        }

        // 快速拾取准星处的地面物品（不选中、不弹菜单，直接入背包）。保留为背包入口；F 键已改走 CarryAimed。
        public static void QuickPickup()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            var pawn = Possessed;
            if (pawn == null) return;
            var hit = RaycastCenter();
            if (hit == null || hit.Value.thing == null) return;
            if (hit.Value.dist > ReachUse) return;
            PickUp(pawn, hit.Value.thing);
        }

        // F 键：搬运。准星处原版可搬运的地面物用 CarryTracker 端在身前（离地随身，走原版携带系统，
        // 与 hauling 作业同一套，故其他 pawn 搬运时也能被 PawnLayer3D 画出来）；手上已端着则就地放下。
        // 携带物/武器的可视：世界侧 PawnLayer3D.DrawHeld，第一人称 Viewmodel3D.DrawCarried。
        public static void CarryAimed()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return;
            var pawn = Possessed;
            if (pawn == null || pawn.Dead || pawn.Downed || pawn.carryTracker == null) return;

            // 手上已端着：再按 F 放下到准星指向的地图格（最多 3 格以内），若准星无有效目标或超距则落在身前脚下。
            Thing held = null;
            try { held = pawn.carryTracker.CarriedThing; } catch { }
            if (held != null)
            {
                try
                {
                    IntVec3 targetCell = pawn.Position;
                    var dropHit = RaycastCenter();
                    if (dropHit != null && dropHit.Value.cell.IsValid && dropHit.Value.cell.InBounds(pawn.Map))
                    {
                        float d = (dropHit.Value.cell - pawn.Position).LengthHorizontal;
                        if (d <= 3.5f)
                        {
                            targetCell = dropHit.Value.cell;
                        }
                        else
                        {
                            // 钳制在朝向准星最多 3 格处
                            Vector3 dir = (dropHit.Value.cell - pawn.Position).ToVector3().normalized;
                            IntVec3 clamped = (pawn.Position.ToVector3Shifted() + dir * 3f).ToIntVec3();
                            if (clamped.InBounds(pawn.Map))
                                targetCell = clamped;
                        }
                    }

                    Thing dropped;
                    if (pawn.carryTracker.TryDropCarriedThing(targetCell, ThingPlaceMode.Near, out dropped)
                        && dropped != null)
                    {
                        dropped.SetForbidden(false, false);
                    }
                }
                catch { }
                return;
            }

            var hit = RaycastCenter();
            if (hit == null) return;
            var thing = hit.Value.thing;
            if (thing == null || thing.Destroyed || !thing.Spawned) return;
            if (hit.Value.dist > ReachUse) return;                          // 搬运要贴身
            if (thing.def == null || thing.def.category != ThingCategory.Item) return;
            if (!thing.def.EverHaulable && !thing.def.Minifiable) return;   // 原版可搬运
            try
            {
                thing.SetForbidden(false, false);
                // 走 count 重载：受 CarryTracker 负重上限约束，装不下的部分留在地上。
                int n = pawn.carryTracker.TryStartCarry(thing, thing.stackCount, false);
                if (n > 0) Find.Selector.Select(pawn.carryTracker.CarriedThing);
            }
            catch { }
        }

        // G 键：拆解准星处的建筑（与左键拆除共用同一套原版 Deconstruct 下发逻辑）。
        public static void DemolishAimed()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            var pawn = Possessed;
            if (pawn == null) return;
            var hit = RaycastCenter();
            if (hit == null || hit.Value.thing == null) return;
            if (hit.Value.dist > ReachWork) return;
            var b = hit.Value.thing as Building;
            if (b == null || b.def == null || b.def.building == null) return;
            if (b.def.building.isNaturalRock) return;
            StartDeconstructBuilding(pawn, b);
        }

        // 数字键 1..n：对背包第 n 件物品执行主要动作（装备/穿戴/食用）。
        public static void UseHotbar(int slot)
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            Backpack3D.UseSlot(slot);
        }

        // 左键破坏性交互：挖矿、砍伐树木、收获/割除植物、拆除建筑、取消蓝图/框架。
        private static void ApplyDestructiveInteraction(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null || thing.Destroyed) return;
            try
            {
                // 1) 天然岩石 / 矿脉 -> 挖矿（设置 ignoreDesignations = true 使其无需提前划定开采区）
                var building = thing as Building;
                bool rock = (building != null && building.def != null && building.def.building != null
                        && building.def.building.isNaturalRock)
                    || thing is Mineable;
                if (rock)
                {
                    if (IsDoingJobOn(pawn, JobDefOf.Mine, thing)) return;
                    var job = JobMaker.MakeJob(JobDefOf.Mine, thing, 20000, checkOverrideOnExpiry: true);
                    job.ignoreDesignations = true;
                    Give(pawn, job);
                    return;
                }

                // 2) 植物 -> 树木或未成熟植物走砍伐/割除（CutPlant），已成熟非树木作物走收获（Harvest）
                if (thing is Plant plant)
                {
                    bool isTree = plant.def != null && plant.def.plant != null && plant.def.plant.IsTree;
                    JobDef jd = (!isTree && plant.HarvestableNow) ? JobDefOf.Harvest : JobDefOf.CutPlant;
                    if (IsDoingJobOn(pawn, jd, plant)) return;
                    Give(pawn, JobMaker.MakeJob(jd, plant));
                    return;
                }

                // 3) 蓝图 -> 取消蓝图；框架 -> 取消/拆解退还材料
                if (thing is Blueprint || thing is Frame)
                {
                    thing.Destroy(DestroyMode.Cancel);
                    return;
                }

                // 4) 建筑 -> 拆除（先确保认领无主遗迹并挂上 Deconstruct Designation，满足 JobDriver_Deconstruct 的校验）
                if (building != null)
                {
                    StartDeconstructBuilding(pawn, building);
                }
            }
            catch { }
        }

        private static void StartDeconstructBuilding(Pawn pawn, Building b)
        {
            if (pawn == null || b == null || b.Destroyed || b.Map == null) return;
            if (IsDoingJobOn(pawn, JobDefOf.Deconstruct, b)) return;
            try
            {
                if (b.Faction != pawn.Faction && b.ClaimableBy(pawn.Faction))
                    b.SetFaction(pawn.Faction, pawn);
                if (!b.DeconstructibleBy(pawn.Faction)) return;
                if (b.Map.designationManager.DesignationOn(b, DesignationDefOf.Deconstruct) == null)
                    b.Map.designationManager.AddDesignation(new Designation(b, DesignationDefOf.Deconstruct));
                Give(pawn, JobMaker.MakeJob(JobDefOf.Deconstruct, b));
            }
            catch { }
        }

        private static bool IsDoingJobOn(Pawn pawn, JobDef def, Thing target)
        {
            var cur = pawn != null ? pawn.CurJob : null;
            return cur != null && cur.def == def && cur.targetA.Thing == target;
        }

        // 解析 E 键非破坏性交互及其原版提示词。
        private static bool TryResolveNonDestructiveInteraction(
            Pawn pawn, Thing thing, float dist, out string label, out System.Action perform)
        {
            label = null;
            perform = null;
            if (pawn == null || thing == null || thing.Destroyed || thing.def == null) return false;

            try
            {
                // 1) 门：开门 / 关门
                if (thing is Building_Door door)
                {
                    if (dist > ReachUse) return false;
                    label = door.Open ? T("Close", "关闭") : T("DesignatorOpen", "打开");
                    perform = () => ToggleDoor(pawn, door);
                    return true;
                }

                // 2) 地面物品：装备武器或拾取入包
                if (thing.def.category == ThingCategory.Item)
                {
                    if (dist > ReachUse) return false;
                    if (thing.def.IsWeapon && (pawn.equipment == null || pawn.equipment.Primary == null))
                        label = T("Equip", "装备");
                    else
                        label = thing.stackCount > 1
                            ? "PickUpAll".Translate(thing.Label, thing).ToString()
                            : "PickUpOne".Translate(thing.LabelNoCount, thing).ToString();
                    perform = () => PickUp(pawn, thing);
                    return true;
                }

                // 3) 建筑蓝图与建造框架：搬运材料并建造
                if (thing is Blueprint || thing is Frame)
                {
                    if (dist > ReachWork) return false;
                    label = WorkTypeDefOf.Construction != null && !string.IsNullOrEmpty(WorkTypeDefOf.Construction.labelShort)
                        ? WorkTypeDefOf.Construction.labelShort.CapitalizeFirst()
                        : "Construct";
                    perform = () => StartConstructThing(pawn, thing);
                    return true;
                }

                // 4) 成熟作物/可收获非树木植物：非破坏性收获（如浆果灌木、仙珍果、成熟农作物）
                if (thing is Plant plant)
                {
                    bool isTree = plant.def.plant != null && plant.def.plant.IsTree;
                    if (!isTree && plant.HarvestableNow && dist <= ReachWork)
                    {
                        label = T("DesignatorHarvest", "收获");
                        perform = () => Give(pawn, JobMaker.MakeJob(JobDefOf.Harvest, plant));
                        return true;
                    }
                    return false;
                }

                // 5) 建筑：研究台、工作台（IBillGiver）、床铺、可开关设施、受损建筑修理
                if (thing is Building b)
                {
                    if (b.def.building != null && b.def.building.isNaturalRock) return false;

                    // 5a) 受损的我方建筑优先提供修理（若该建筑不是门且生命值未满）
                    if (b.Faction == pawn.Faction && b.def.useHitPoints && b.HitPoints < b.MaxHitPoints
                        && !(b is IBillGiver) && !(b is Building_ResearchBench) && !(b is Building_Bed))
                    {
                        label = WorkTypeDefOf.Construction != null && !string.IsNullOrEmpty(WorkTypeDefOf.Construction.labelShort)
                            ? WorkTypeDefOf.Construction.labelShort.CapitalizeFirst()
                            : "Repair";
                        perform = () => Give(pawn, JobMaker.MakeJob(JobDefOf.Repair, b));
                        return true;
                    }

                    // 5b) 研究台
                    if (b is Building_ResearchBench bench)
                    {
                        label = WorkTypeDefOf.Research != null && !string.IsNullOrEmpty(WorkTypeDefOf.Research.labelShort)
                            ? WorkTypeDefOf.Research.labelShort.CapitalizeFirst()
                            : "Research";
                        perform = () => StartResearchAtBench(pawn, bench);
                        return true;
                    }

                    // 5c) 各类生产工作台（IBillGiver / Building_WorkTable）
                    if (b is IBillGiver billGiver)
                    {
                        label = GetBillGiverActionLabel(b);
                        perform = () => StartWorkAtBillGiver(pawn, b, billGiver);
                        return true;
                    }

                    // 5d) 床铺 -> 躺下休息
                    if (b is Building_Bed bed)
                    {
                        label = NeedDefOf.Rest != null ? NeedDefOf.Rest.LabelCap.ToString() : "Rest";
                        perform = () => Give(pawn, JobMaker.MakeJob(JobDefOf.LayDown, bed));
                        return true;
                    }

                    // 5e) 电源/开关切换
                    var fl = b.GetComp<CompFlickable>();
                    if (fl != null)
                    {
                        label = T("CommandDesignateTogglePower", "开关");
                        perform = () => { fl.SwitchIsOn = !fl.SwitchIsOn; };
                        return true;
                    }

                    // 5f) 受损的工作台/床铺兜底修理
                    if (b.Faction == pawn.Faction && b.def.useHitPoints && b.HitPoints < b.MaxHitPoints)
                    {
                        label = WorkTypeDefOf.Construction != null && !string.IsNullOrEmpty(WorkTypeDefOf.Construction.labelShort)
                            ? WorkTypeDefOf.Construction.labelShort.CapitalizeFirst()
                            : "Repair";
                        perform = () => Give(pawn, JobMaker.MakeJob(JobDefOf.Repair, b));
                        return true;
                    }
                }

                // 6) Pawn：倒地友方 -> 救援；倒地敌方 -> 俘虏；受伤友方 -> 治疗
                if (thing is Pawn target && target != pawn && !target.Dead)
                {
                    if (target.Downed)
                    {
                        bool friendly = target.Faction == null || !target.Faction.HostileTo(pawn.Faction);
                        label = friendly ? T("DesignatorRescue", JobDefOf.Rescue.label) : T("DesignatorCapture", JobDefOf.Capture.label);
                        perform = () => Give(pawn, JobMaker.MakeJob(friendly ? JobDefOf.Rescue : JobDefOf.Capture, target));
                        return true;
                    }
                    if (!target.HostileTo(pawn) && target.health != null && target.health.hediffSet != null
                        && target.health.hediffSet.HasTendableHediff())
                    {
                        label = WorkTypeDefOf.Doctor != null && !string.IsNullOrEmpty(WorkTypeDefOf.Doctor.labelShort)
                            ? WorkTypeDefOf.Doctor.labelShort.CapitalizeFirst()
                            : JobDefOf.TendPatient.label;
                        perform = () => Give(pawn, JobMaker.MakeJob(JobDefOf.TendPatient, target));
                        return true;
                    }
                    return false;
                }

                // 7) 通用回退：检查该物体自带的 FloatMenuOptions（如交互终端、乐器、容器等）
                foreach (var opt in thing.GetFloatMenuOptions(pawn))
                {
                    if (opt != null && !opt.Disabled && opt.action != null)
                    {
                        label = opt.Label;
                        var act = opt.action;
                        perform = () => { lastOrderedJobTime = Time.realtimeSinceStartup; act(); };
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static string T(string key, string fallback)
        {
            if (LanguageDatabase.activeLanguage != null && key.CanTranslate())
                return key.Translate().ToString();
            return fallback;
        }

        private static void ToggleDoor(Pawn pawn, Building_Door door)
        {
            if (!door.Open)
            {
                if (door is Building_JammedDoor jammed && jammed.Jammed)
                    jammed.UnlockAndOpenDoor();
                else
                    door.StartManualOpenBy(pawn);
            }
            else
            {
                if (door.HoldOpen && DoorHoldOpenField != null)
                    DoorHoldOpenField.SetValue(door, false);
                bool closedNow = false;
                if (DoorTryCloseMethod != null)
                {
                    try { closedNow = (bool)DoorTryCloseMethod.Invoke(door, null); }
                    catch { }
                }
                if (!closedNow)
                    door.StartManualCloseBy(pawn);
            }
            ChangeNotify.NotifyDoorToggled(door);
        }

        private static string GetBillGiverActionLabel(Building b)
        {
            var allWg = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < allWg.Count; i++)
            {
                var wg = allWg[i];
                if (wg.fixedBillGiverDefs != null && wg.fixedBillGiverDefs.Contains(b.def))
                {
                    if (!string.IsNullOrEmpty(wg.verb)) return wg.verb.CapitalizeFirst();
                    if (wg.workType != null && !string.IsNullOrEmpty(wg.workType.labelShort))
                        return wg.workType.labelShort.CapitalizeFirst();
                }
            }
            return WorkTypeDefOf.Crafting != null && !string.IsNullOrEmpty(WorkTypeDefOf.Crafting.labelShort)
                ? WorkTypeDefOf.Crafting.labelShort.CapitalizeFirst()
                : "Work";
        }

        private static void StartWorkAtBillGiver(Pawn pawn, Building b, IBillGiver billGiver)
        {
            if (pawn == null || b == null || billGiver == null || b.Destroyed) return;
            Pawn prevMakingFor = FloatMenuMakerMap.makingFor;
            try
            {
                FloatMenuMakerMap.makingFor = pawn;
                WorkGiver_DoBill worker = FindDoBillWorkerFor(b.def);
                if (worker == null) return;

                // 若工作台已有账单，直接尝试启动
                if (billGiver.BillStack != null && billGiver.BillStack.Count > 0)
                {
                    for (int i = 0; i < billGiver.BillStack.Count; i++)
                        billGiver.BillStack[i].nextTickToSearchForIngredients = 0;
                    Job job = worker.JobOnThing(pawn, b, forced: true);
                    if (job != null)
                    {
                        Give(pawn, job);
                        return;
                    }
                }

                // 若工作台尚无账单（或现有账单缺料），遍历该工作台可用配方，自动创建一条有原料可做的账单并启动
                if (b.def.AllRecipes != null && billGiver.BillStack != null)
                {
                    for (int i = 0; i < b.def.AllRecipes.Count; i++)
                    {
                        var r = b.def.AllRecipes[i];
                        if (r == null || !r.AvailableNow || !r.AvailableOnNow(b)) continue;
                        Bill candidate = r.MakeNewBill();
                        billGiver.BillStack.AddBill(candidate);
                        Job job = worker.JobOnThing(pawn, b, forced: true);
                        if (job != null)
                        {
                            Give(pawn, job);
                            return;
                        }
                        billGiver.BillStack.Delete(candidate);
                    }

                    // 若地图上暂时没有任何配方原料，但工作台还是空的，默认挂上第一条可用配方并提示原因
                    if (billGiver.BillStack.Count == 0)
                    {
                        for (int i = 0; i < b.def.AllRecipes.Count; i++)
                        {
                            var r = b.def.AllRecipes[i];
                            if (r != null && r.AvailableNow && r.AvailableOnNow(b))
                            {
                                billGiver.BillStack.AddBill(r.MakeNewBill());
                                break;
                            }
                        }
                    }
                }

                if (JobFailReason.HaveReason)
                    Messages.Message(JobFailReason.Reason, b, MessageTypeDefOf.RejectInput, historical: false);
            }
            catch { }
            finally
            {
                FloatMenuMakerMap.makingFor = prevMakingFor;
            }
        }

        private static WorkGiver_DoBill FindDoBillWorkerFor(ThingDef benchDef)
        {
            var allWg = DefDatabase<WorkGiverDef>.AllDefsListForReading;
            for (int i = 0; i < allWg.Count; i++)
            {
                var wg = allWg[i];
                if (wg.fixedBillGiverDefs != null && wg.fixedBillGiverDefs.Contains(benchDef)
                    && wg.Worker is WorkGiver_DoBill db)
                    return db;
            }
            for (int i = 0; i < allWg.Count; i++)
            {
                if (allWg[i].Worker is WorkGiver_DoBill db) return db;
            }
            return null;
        }

        private static void StartResearchAtBench(Pawn pawn, Building_ResearchBench bench)
        {
            if (pawn == null || bench == null || bench.Destroyed) return;
            try
            {
                if (Find.ResearchManager != null)
                {
                    var cur = Find.ResearchManager.GetProject();
                    if (cur == null || !cur.CanBeResearchedAt(bench, ignoreResearchBenchPowerStatus: false))
                    {
                        var all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
                        for (int i = 0; i < all.Count; i++)
                        {
                            var p = all[i];
                            if (p != null && p.CanStartNow && p.CanBeResearchedAt(bench, ignoreResearchBenchPowerStatus: false))
                            {
                                Find.ResearchManager.SetCurrentProject(p);
                                break;
                            }
                        }
                    }
                }
                Give(pawn, JobMaker.MakeJob(JobDefOf.Research, bench));
            }
            catch { }
        }

        private static void StartConstructThing(Pawn pawn, Thing thing)
        {
            if (pawn == null || thing == null || thing.Destroyed || pawn.Map == null) return;
            Pawn prevMakingFor = FloatMenuMakerMap.makingFor;
            try
            {
                FloatMenuMakerMap.makingFor = pawn;
                thing.SetForbidden(false, false);

                // 若背包内持有该蓝图/框架所需的建材，先释放到脚下以便原版建造驱动直接取用
                if (thing is IConstructible ic && pawn.inventory != null && pawn.inventory.innerContainer != null)
                {
                    var cost = ic.TotalMaterialCost();
                    if (cost != null)
                    {
                        for (int i = 0; i < cost.Count; i++)
                        {
                            var need = cost[i];
                            if (need == null || need.thingDef == null) continue;
                            for (int j = pawn.inventory.innerContainer.Count - 1; j >= 0; j--)
                            {
                                var inv = pawn.inventory.innerContainer[j];
                                if (inv != null && inv.def == need.thingDef)
                                    Backpack3D.Drop(inv);
                            }
                        }
                    }
                }

                if (thing is Blueprint bp)
                {
                    pendingConstructCell = bp.Position;
                    var deliverWg = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToBlueprints")?.Worker as WorkGiver_Scanner;
                    Job job = deliverWg != null ? deliverWg.JobOnThing(pawn, bp, forced: true) : null;
                    if (job != null)
                    {
                        Give(pawn, job);
                        return;
                    }
                    if (JobFailReason.HaveReason)
                        Messages.Message(JobFailReason.Reason, bp, MessageTypeDefOf.RejectInput, historical: false);
                    return;
                }

                if (thing is Frame frame)
                {
                    pendingConstructCell = frame.Position;
                    if (!frame.IsCompleted())
                    {
                        var deliverFrameWg = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructDeliverResourcesToFrames")?.Worker as WorkGiver_Scanner;
                        Job deliverJob = deliverFrameWg != null ? deliverFrameWg.JobOnThing(pawn, frame, forced: true) : null;
                        if (deliverJob != null)
                        {
                            Give(pawn, deliverJob);
                            return;
                        }
                    }
                    if (frame.IsCompleted())
                    {
                        var finishWg = DefDatabase<WorkGiverDef>.GetNamedSilentFail("ConstructFinishFrames")?.Worker as WorkGiver_Scanner;
                        Job finishJob = finishWg != null ? finishWg.JobOnThing(pawn, frame, forced: true) : null;
                        if (finishJob == null)
                            finishJob = JobMaker.MakeJob(JobDefOf.FinishFrame, frame);
                        Give(pawn, finishJob);
                        return;
                    }
                    if (JobFailReason.HaveReason)
                        Messages.Message(JobFailReason.Reason, frame, MessageTypeDefOf.RejectInput, historical: false);
                }
            }
            catch { }
            finally
            {
                FloatMenuMakerMap.makingFor = prevMakingFor;
            }
        }

        private static void TryContinuePendingConstruct(Pawn pawn)
        {
            if (!pendingConstructCell.IsValid || pawn == null || !pawn.Spawned || pawn.Map == null) return;
            var cur = pawn.CurJob;
            if (cur != null && (cur.def == JobDefOf.HaulToContainer || cur.def == JobDefOf.PlaceNoCostFrame || cur.def == JobDefOf.FinishFrame))
                return;
            if (!pendingConstructCell.InBounds(pawn.Map))
            {
                pendingConstructCell = IntVec3.Invalid;
                return;
            }
            var frame = pendingConstructCell.GetEdifice(pawn.Map) as Frame;
            if (frame == null)
            {
                var list = pendingConstructCell.GetThingList(pawn.Map);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is Frame f) { frame = f; break; }
                }
            }
            if (frame != null && !frame.Destroyed)
            {
                if (frame.IsCompleted())
                {
                    pendingConstructCell = IntVec3.Invalid;
                    Give(pawn, JobMaker.MakeJob(JobDefOf.FinishFrame, frame));
                }
                else
                {
                    pendingConstructCell = IntVec3.Invalid;
                    StartConstructThing(pawn, frame);
                }
            }
            else
            {
                pendingConstructCell = IntVec3.Invalid;
            }
        }

        private static void Give(Pawn pawn, Job job)
        {
            try
            {
                if (pawn != null && pawn.jobs != null && job != null)
                {
                    if (pawn.pather != null && !pawn.pather.Moving && pawn.pather.nextCell != pawn.Position)
                        pawn.pather.ResetToCurrentPosition();
                    lastOrderedJobTime = Time.realtimeSinceStartup;
                    pawn.jobs.TryTakeOrderedJob(job);
                }
            }
            catch { }
        }

        private static void AttackThing(Pawn pawn, Thing t)
        {
            try
            {
                var verb = pawn.TryGetAttackVerb(t, true);
                if (verb == null) return;
                if (verb.CanHitTarget(t))
                {
                    verb.TryStartCastOn(t);
                    return;
                }
                if (verb.IsMeleeAttack)
                {
                    Give(pawn, JobMaker.MakeJob(JobDefOf.AttackMelee, t));
                }
                else
                {
                    Give(pawn, JobMaker.MakeJob(JobDefOf.AttackStatic, t));
                }
            }
            catch { }
        }

        // 地面物品 -> 背包。武器且手上空着时直接装备，其余收进背包（原版 TakeInventory 作业）。
        public static void PickUp(Pawn pawn, Thing item)
        {
            if (pawn == null || item == null || item.Destroyed) return;
            if (item.def == null || item.def.category != ThingCategory.Item) return;
            try
            {
                item.SetForbidden(false, false);
                if (item.def.IsWeapon && (pawn.equipment == null || pawn.equipment.Primary == null))
                {
                    Give(pawn, JobMaker.MakeJob(JobDefOf.Equip, item));
                    return;
                }
                var job = JobMaker.MakeJob(JobDefOf.TakeInventory, item);
                job.count = item.stackCount;
                job.checkEncumbrance = true;
                job.takeInventoryDelay = 60;
                Give(pawn, job);
            }
            catch { }
        }

        public struct RayHit
        {
            public IntVec3 cell;
            public Thing thing;
            public float dist;   // 相机到命中点的距离（米）
        }

        private static int lastRaycastFrame = -1;
        private static RayHit? cachedRayHit;

        // 屏幕中心射线 → 真实命中：同帧内（OnGUI Layout/Repaint/输入调用）复用结果，避免单帧多重求交。
        public static RayHit? RaycastCenter()
        {
            int frame = Time.frameCount;
            if (lastRaycastFrame == frame) return cachedRayHit;
            lastRaycastFrame = frame;
            cachedRayHit = DoRaycastCenter();
            return cachedRayHit;
        }

        private static RayHit? DoRaycastCenter()
        {
            if (cam == null) return null;
            var ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var st = FirstPerson3DMod.settings;
            float pawnH = st != null ? st.pawnHeight : 1.7f;
            float bestT = 80f;
            var best = new RayHit();
            bool found = false;

            foreach (var map in Streaming3D.RenderMaps(Find.CurrentMap))
            {
                if (map == null || map.Disposed) continue;
                var off = Streaming3D.OffsetOf(map);
                float baseY = LevelStack3D.LevelBaseY(map);

                // 地面平面（当前层高）：若落点格内有可交互物（工作台/物品/蓝图/框架/植物），直接绑定该 Thing
                float denom = ray.direction.y;
                if (Mathf.Abs(denom) > 1e-4f)
                {
                    float t = (baseY + 0.05f - ray.origin.y) / denom;
                    if (t > 0f && t < bestT)
                    {
                        var point = ray.origin + ray.direction * t;
                        var cell = new IntVec3(
                            Mathf.FloorToInt(point.x - off.x), 0, Mathf.FloorToInt(point.z - off.z));
                        if (cell.InBounds(map))
                        {
                            bestT = t;
                            best = new RayHit { cell = cell, thing = ResolveGroundCellThing(map, cell), dist = t };
                            found = true;
                        }
                    }
                }

                // 沿射线步进检测：山体、满格建筑、门、蓝图/框架、工作台与家具、树木与可交互植物、地面物品
                for (float t = 0.05f; t < bestT; t += 0.15f)
                {
                    var p = ray.origin + ray.direction * t;
                    var cell = new IntVec3(
                        Mathf.FloorToInt(p.x - off.x), 0, Mathf.FloorToInt(p.z - off.z));
                    if (!cell.InBounds(map)) continue;
                    var ed = cell.GetEdifice(map);
                    var mountain = SceneRegistry.Find(map)?.Mountain;
                    if (mountain != null && mountain.Contains(p.x - off.x, p.y - baseY, p.z - off.z))
                    {
                        bestT = t;
                        best = new RayHit { cell = cell, thing = MountainField3D.IsRock(ed) ? ed : null, dist = t };
                        found = true;
                        break;
                    }
                    if (MountainField3D.IsRock(ed)) continue;
                    float top = baseY + Fps3DState.WallHeight + 0.05f;
                    if (p.y < baseY - 0.1f || p.y > top) continue;

                    // 1) 门：站在已打开的门格内向外看时不拦截近端射线
                    if (ed is Building_Door door)
                    {
                        if (Possessed != null && cell == Possessed.Position && door.Open && t < 0.45f)
                            continue;
                        bestT = t;
                        best = new RayHit { cell = cell, thing = ed, dist = t };
                        found = true;
                        break;
                    }

                    // 2) 满格实体墙或满格建造框架
                    if (ed != null && ed.def != null && GetThingRayFillFull(ed))
                    {
                        bestT = t;
                        best = new RayHit { cell = cell, thing = ed, dist = t };
                        found = true;
                        break;
                    }

                    // 3) 非满格 Edifice（工作台、家具、矮墙、建造框架等）
                    if (ed != null && p.y <= baseY + GetThingRayHeight(map, cell, ed))
                    {
                        if (Possessed == null || cell != Possessed.Position || t > 0.45f)
                        {
                            bestT = t;
                            best = new RayHit { cell = cell, thing = ed, dist = t };
                            found = true;
                            break;
                        }
                    }

                    // 4) 格内其它建筑、建筑蓝图（Blueprint）或框架（Frame）
                    var thingList = cell.GetThingList(map);
                    Thing hitConstructOrBld = null;
                    for (int i = 0; i < thingList.Count; i++)
                    {
                        var th = thingList[i];
                        if (th == null || th == ed || th == Possessed) continue;
                        if (th is Blueprint || th is Frame || (th.def != null && th.def.category == ThingCategory.Building))
                        {
                            if (p.y <= baseY + GetThingRayHeight(map, cell, th))
                            {
                                hitConstructOrBld = th;
                                break;
                            }
                        }
                    }
                    if (hitConstructOrBld != null)
                    {
                        bestT = t;
                        best = new RayHit { cell = cell, thing = hitConstructOrBld, dist = t };
                        found = true;
                        break;
                    }

                    // 5) 地面物品
                    if (p.y >= baseY && p.y <= baseY + 0.90f)
                    {
                        var item = cell.GetFirstItem(map);
                        if (item != null)
                        {
                            bestT = t;
                            best = new RayHit { cell = cell, thing = item, dist = t };
                            found = true;
                            break;
                        }
                    }

                    // 6) 树木与可收获/可交互植物（排除普通装饰野草以免遮挡地面与物品）
                    var plant = cell.GetPlant(map);
                    if (plant != null && IsRayInteractivePlant(map, plant))
                    {
                        float plantH = (plant.def.plant != null && plant.def.plant.IsTree) ? 2.25f : 0.85f;
                        if (p.y >= baseY && p.y <= baseY + plantH)
                        {
                            bestT = t;
                            best = new RayHit { cell = cell, thing = plant, dist = t };
                            found = true;
                            break;
                        }
                    }
                }

                // 人形立牌盒：精确 slab 求交，可与墙/地面比远近
                foreach (var p in map.mapPawns.AllPawnsSpawned)
                {
                    if (p == null || p == Possessed) continue;
                    var dp = p.DrawPos;
                    var min = new Vector3(dp.x - 0.45f + off.x, baseY, dp.z - 0.45f + off.z);
                    var max = new Vector3(dp.x + 0.45f + off.x, baseY + pawnH + 0.2f, dp.z + 0.45f + off.z);
                    if (RayBox(ray.origin, ray.direction, min, max, out float tp) && tp < bestT)
                    {
                        bestT = tp;
                        best = new RayHit { cell = p.Position, thing = p, dist = tp };
                        found = true;
                    }
                }
            }
            return found ? best : (RayHit?)null;
        }

        private static bool GetThingRayFillFull(Thing t)
        {
            if (t == null || t.def == null) return false;
            if (t.def.Fillage == FillCategory.Full) return true;
            if (t is Frame fr && fr.BuildDef != null && fr.BuildDef.Fillage == FillCategory.Full) return true;
            if (t is Blueprint bp && bp.EntityToBuild() is ThingDef bd && bd.Fillage == FillCategory.Full) return true;
            return false;
        }

        private static float GetThingRayHeight(Map map, IntVec3 cell, Thing t)
        {
            if (t == null || t.def == null) return 0.9f;
            ThingDef d = t.def;
            if (t is Frame fr && fr.BuildDef != null) d = fr.BuildDef;
            else if (t is Blueprint bp)
            {
                if (bp.EntityToBuild() is ThingDef bd) d = bd;
                else return 0.25f;
            }
            if (d.Fillage == FillCategory.Full) return Fps3DState.WallHeight;
            if (d.building != null && (d.building.canPlaceOverWall || d.building.isAttachment)) return Fps3DState.WallHeight;
            if (d.defName.IndexOf("Column", System.StringComparison.OrdinalIgnoreCase) >= 0
                || d.defName.IndexOf("Pillar", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return Furniture3D.ColumnHeight;
            float support = Furniture3D.SupportAt(map, cell);
            return Mathf.Max(0.95f, support + 0.25f, d.fillPercent * 1.6f);
        }

        private static bool IsRayInteractivePlant(Map map, Plant plant)
        {
            if (plant == null || plant.Destroyed || plant.def == null || plant.def.plant == null) return false;
            if (plant.def.plant.IsTree) return true;
            if (plant.HarvestableNow) return true;
            if (plant.def.plant.Sowable || plant.def.plant.harvestedThingDef != null) return true;
            if (map != null && map.designationManager != null && map.designationManager.HasMapDesignationOn(plant))
                return true;
            return false;
        }

        private static Thing ResolveGroundCellThing(Map map, IntVec3 cell)
        {
            var item = cell.GetFirstItem(map);
            if (item != null) return item;
            var ed = cell.GetEdifice(map);
            if (ed != null && !MountainField3D.IsRock(ed)) return ed;
            var list = cell.GetThingList(map);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (t == null || t == Possessed) continue;
                if (t is Blueprint || t is Frame || (t.def != null && t.def.category == ThingCategory.Building && !MountainField3D.IsRock(t)))
                    return t;
            }
            var plant = cell.GetPlant(map);
            if (plant != null && IsRayInteractivePlant(map, plant)) return plant;
            return null;
        }

        // 射线-AABB slab 求交；相机自身所在盒（附身者）由调用方排除。
        private static bool RayBox(Vector3 ro, Vector3 rd, Vector3 min, Vector3 max, out float tHit)
        {
            tHit = 0f;
            float tmin = 0f, tmax = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float o = i == 0 ? ro.x : (i == 1 ? ro.y : ro.z);
                float d = i == 0 ? rd.x : (i == 1 ? rd.y : rd.z);
                float lo = i == 0 ? min.x : (i == 1 ? min.y : min.z);
                float hi = i == 0 ? max.x : (i == 1 ? max.y : max.z);
                if (Mathf.Abs(d) < 1e-6f)
                {
                    if (o < lo || o > hi) return false;
                    continue;
                }
                float t1 = (lo - o) / d, t2 = (hi - o) / d;
                if (t1 > t2) { var tmp = t1; t1 = t2; t2 = tmp; }
                tmin = Mathf.Max(tmin, t1);
                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) return false;
            }
            if (tmax < 0.1f) return false;
            tHit = Mathf.Max(tmin, 0.1f);
            return true;
        }
    }
}
