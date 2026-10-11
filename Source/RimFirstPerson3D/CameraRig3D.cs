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
        private static readonly Dictionary<int, Vector2> lastMouseByButton = new Dictionary<int, Vector2>();

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
            // 原版视角屏：主相机已还原正交俯视，第一人称相机驱动暂停
            if (MapScreen3D.Open) return;

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
            cachedCensus = sb.ToString();
            return cachedCensus;
        }

        // 每帧应用视觉参数：FOV / 天空色 / 线性雾（全为设置面板实时项）。
        private static void ApplyLookSettings()
        {
            var st = FirstPerson3DMod.settings;
            if (st == null || cam == null) return;
            if (cam.fieldOfView != st.fov) cam.fieldOfView = st.fov;
            cam.backgroundColor = new Color(st.skyR, st.skyG, st.skyB);
            RenderSettings.fog = st.fogOn;
            if (st.fogOn)
            {
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = 6f;
                RenderSettings.fogEndDistance = Mathf.Max(20f, st.fogEnd);
            }
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

            // 与 HandleLook 的终端早退重复一道：避免以后单独调用时把光标藏起来并回中。
            if (MapScreen3D.BlocksGameplay)
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
            if (Find.TickManager != null && Find.TickManager.Paused) return true;
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

        // 光标是否悬停在原版 UI 上：命中则本次点击交给 UI，不重新锁定。
        private static bool OverUI()
        {
            var stack = Find.WindowStack;
            if (stack == null) return false;
            return stack.GetWindowAt(UI.MousePosUIInvertedUseEventIfCan) != null;
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

            // 合成指令挂起时重夺征召权：TSS 等共管 AI 会把附身 pawn 解除征召，
            // 令 ！Drafted 早退、远控通道静默失效（cmdWalkHold 停摆、传送不消费）。
            var st0 = FirstPerson3DMod.settings;
            bool synthPending = st0 != null && (st0.cmdWalkHold > 0f
                || !string.IsNullOrEmpty(st0.cmdTeleport) || !string.IsNullOrEmpty(st0.cmdKey));
            if (pawn != null && synthPending && pawn.drafter != null && !pawn.Drafted)
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

            // 原版 job 持有者（挖矿 / 搬运 / 施法）：这一帧不改位置，也不 StopDead。
            // 相机与立牌临时回退到 DrawPos，等 job 结束再重新接管。
            if (JobOwnsPawn(pawn))
            {
                DisableFoot();
                return;
            }

            // 暂停 / 窗口 / 焦点门控：水平与竖直速度都记为 0，不补移动（脚点冻结）
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

            // 竖直：着地空格起跳 + 重力积分（本任务地面 = 所在场景 baseY，不探测家具）
            IntegrateVertical(dt);

            // 跨格写回 pawn.Position：走原版格子变更，门 / 陷阱 / 区域继续生效
            var cell = new IntVec3(Mathf.FloorToInt(footPos.x), 0, Mathf.FloorToInt(footPos.y));
            if (cell.InBounds(map) && cell != pawn.Position)
                pawn.Position = cell;
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

        // 竖直：着地时按空格起跳；每帧 vy -= Gravity*dt，离地高度 += vy*dt；
        // 落地（高度降到 0）钳住并清零竖直速度。空中再按空格无效。
        private static void IntegrateVertical(float dt)
        {
            bool space = Input.GetKey(KeyCode.Space);
            bool grounded = footHeight <= 0.0001f;
            if (grounded && space && !spacePrev)
                vy = Mathf.Sqrt(2f * Metrics3D.Gravity * Metrics3D.JumpApex);
            spacePrev = space;

            vy -= Metrics3D.Gravity * dt;
            footHeight += vy * dt;
            if (footHeight <= 0f)
            {
                footHeight = 0f;
                vy = 0f;
            }
        }

        // 这些作业是玩家在第一人称里主动下发的：作业期间让原版驱动，本帧不接管位移，
        // 相机与立牌临时回退到 DrawPos，等作业结束（或被取消）再重新接管。
        private static readonly HashSet<JobDef> OwningJobs = new HashSet<JobDef>();
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
            Add(JobDefOf.Deconstruct);
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
            Log.Message(ModEntry.LogPrefix + "teleport -> " + cell);
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

                // 右键：按下记位置，抬起时若几乎没移动才算“点击”——与 mouseLook 关闭时的
                // 右键拖拽环视区分开（拖拽不弹菜单）。
                if (Input.GetMouseButtonDown(1)) rightDownPos = (Vector2)Input.mousePosition;
                if (Input.GetMouseButtonUp(1)
                    && ((Vector2)Input.mousePosition - rightDownPos).sqrMagnitude <= RightClickDragTol * RightClickDragTol
                    && GUIUtility.hotControl == 0)
                {
                    OpenAimedFloatMenu();
                    return;
                }
            }

            // 左键攻击：按下立即一次；按住则以 AttackInterval 连击。
            // 旧写法用 GetMouseButtonDown(0) 早退，把“按住连击”整条堵死，只出第一下。
            if (synthetic) { Attack(); attackCooldown = AttackInterval; return; }
            // 该次左键已被“重新锁定自由视角”消费，不再当作攻击。
            if (clickConsumedByLook) { clickConsumedByLook = false; return; }
            if (GUIUtility.hotControl != 0) return;
            if (Input.GetMouseButtonDown(0)) { Attack(); attackCooldown = AttackInterval; }
            else if (Input.GetMouseButton(0) && attackCooldown <= 0f) { Attack(); attackCooldown = AttackInterval; }
        }

        // 按住左键的连击节奏（秒），与挥击动画时长（0.26s）接近。
        private const float AttackInterval = 0.30f;
        private static float attackCooldown;
        // 右键“点击”与“拖拽”的区分阈值（像素）：抬起时位移不超过它才弹菜单。
        private const float RightClickDragTol = 6f;
        private static Vector2 rightDownPos;

        // F / 右键：对准命中格弹完整原版地图浮动菜单。优先走 FloatMenuMakerMap，
        // 让工作台配方、翻找储物、装卸、使用、治疗、逮捕、收割、移动等所有原版
        // FloatMenuOptionProvider 都参与生成；异常时才回退到目标 Thing 的旧入口。
        public static void OpenAimedFloatMenu()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return;
            if (Find.WindowStack == null || Find.WindowStack.FloatMenu != null) return;
            var pawn = Possessed;
            if (pawn == null) return;
            var hit = RaycastCenter();
            if (hit == null) return;

            var aimedCell = hit.Value.cell;
            var map = Find.CurrentMap;
            if (map == null || !aimedCell.IsValid || !aimedCell.InBounds(map)) return;
            if (!pawn.Spawned || pawn.Map != map) return;
            if (hit.Value.thing != null && hit.Value.thing.Map != map) return;

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
                string labelCap = context.IsMultiselect ? null : context.FirstSelectedPawn.LabelCap;
                var menu = new RightBottomFloatMenuMap(opts, labelCap, clickPos)
                {
                    givesColonistOrders = true,
                    vanishIfMouseDistant = false
                };
                // 菜单接管后立即释放第一人称光标，避免本帧/下一帧鼠标视角或攻击穿透。
                ReleaseMouseLook();
                Find.WindowStack.Add(menu);
                return;
            }

            // 兼容原版/第三方 provider 生成失败的情况，保留原有 Thing 菜单行为。
            var thing = hit.Value.thing;
            if (thing == null || thing.Destroyed) return;
            opts = new List<FloatMenuOption>();
            try
            {
                foreach (var o in thing.GetFloatMenuOptions(pawn))
                    if (o != null) opts.Add(o);
            }
            catch { }
            if (opts.Count == 0) return;
            ReleaseMouseLook();
            Find.WindowStack.Add(new RightBottomFloatMenu(opts));
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

        // 交互触及距离：门/拾取/装备/使用允许约 2 格半触及（贴门低头或斜看门框不漏）；挖掘/拆解/收割/救援同距。
        private const float ReachUse = 2.6f;
        private const float ReachWork = 2.8f;

        private static readonly System.Reflection.MethodInfo DoorTryCloseMethod =
            HarmonyLib.AccessTools.Method(typeof(Building_Door), "DoorTryClose");
        private static readonly System.Reflection.FieldInfo DoorHoldOpenField =
            HarmonyLib.AccessTools.Field(typeof(Building_Door), "holdOpenInt");

        // 左键动作：先起挥击动画（空手挥拳 / 持械挥砍或射击后座），
        // 若准星命中可攻击的 pawn 且在动词射程内，则下达一次攻击。
        private static void Attack()
        {
            Viewmodel3D.TriggerAttack();
            var pawn = Possessed;
            if (pawn == null) return;
            var hit = RaycastCenter();
            if (hit == null) return;
            var target = hit.Value.thing as Pawn;
            if (target == null || target == pawn || target.Dead) return;
            var verb = pawn.TryGetAttackVerb(target, true);
            if (verb == null || !verb.CanHitTarget(target)) return;
            verb.TryStartCastOn(target);
        }

        // E 键交互：准星命中项执行一次情境交互（门、拾取/装备、挖掘、收割、使用建筑、救援）。
        public static void Interact()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            if (!Fps3DState.Active || Fps3DState.Mode != ViewMode.FirstPerson) return;
            var hit = RaycastCenter();
            if (hit == null) return;
            var thing = hit.Value.thing;
            if (thing == null) return;
            if (hit.Value.dist > ReachWork) return;   // 够不着就不响应，避免隔墙远程操作
            Find.Selector.Select(thing);
            ApplyInteraction(thing, hit.Value.dist);
        }

        // F 键：快速拾取准星处的地面物品（不选中、不弹菜单，直接入背包）。
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

        // G 键：拆解准星处的建筑（危险动作单列，与 E 分开，避免误拆）。
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
            try
            {
                // 不作"可拆解"的本地判定：该标记在不同版本挂在 Thing/BuildingProperties 上，
                // 名字不稳。原版 JobDriver_Deconstruct 自己会校验，非法目标会立即中止，
                // 这里只排除天然岩（走挖掘而非拆解）。
                Give(pawn, JobMaker.MakeJob(JobDefOf.Deconstruct, b));
            }
            catch { }
        }

        // 数字键 1..n：对背包第 n 件物品执行主要动作（装备/穿戴/食用）。
        public static void UseHotbar(int slot)
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            Backpack3D.UseSlot(slot);
        }

        // 准星命中项的一次交互。dist 为相机到命中点的距离（米），用于触及判定。
        private static void ApplyInteraction(Thing thing, float dist)
        {
            var pawn = Possessed;
            if (pawn == null || thing == null || thing.Destroyed) return;
            try
            {
                // 1) 门（栅栏门、动物帘同为 Building_Door）：手动开/关，并立刻重建门扇网格。
                var door = thing as Building_Door;
                if (door != null)
                {
                    if (dist <= ReachUse)
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
                    return;
                }

                // 2) 地面物品：拾取（武器且手上空着时直接装备）。
                if (thing.def != null && thing.def.category == ThingCategory.Item)
                {
                    if (dist <= ReachUse) PickUp(pawn, thing);
                    return;
                }

                // 3) 可挖掘：天然岩 / Mineable。
                var building = thing as Building;
                bool rock = (building != null && building.def != null && building.def.building != null
                        && building.def.building.isNaturalRock)
                    || thing is Mineable;
                if (rock)
                {
                    if (dist <= ReachWork) Give(pawn, JobMaker.MakeJob(JobDefOf.Mine, thing));
                    return;
                }

                // 4) 植物：收割。
                if (thing is Plant)
                {
                    if (dist <= ReachWork) Give(pawn, JobMaker.MakeJob(JobDefOf.Harvest, thing));
                    return;
                }

                // 5) 建筑：只做"使用"，绝不在这里顺手拆掉（拆解走 G 键）。
                if (building != null)
                {
                    if (dist <= ReachUse) UseBuilding(pawn, building);
                    return;
                }

                // 6) 人：敌对->攻击；倒地->救援/俘虏；需要治疗->治疗。
                var target = thing as Pawn;
                if (target != null && target != pawn && !target.Dead)
                {
                    if (target.HostileTo(pawn)) { AttackThing(pawn, target); return; }
                    if (target.Downed)
                    {
                        bool friendly = target.Faction == null || !target.Faction.HostileTo(pawn.Faction);
                        Give(pawn, JobMaker.MakeJob(friendly ? JobDefOf.Rescue : JobDefOf.Capture, target));
                        return;
                    }
                    if (target.health != null && target.health.hediffSet != null
                        && target.health.hediffSet.HasTendableHediff())
                    {
                        Give(pawn, JobMaker.MakeJob(JobDefOf.TendPatient, target));
                    }
                }
            }
            catch { }
        }

        private static void Give(Pawn pawn, Job job)
        {
            try { if (pawn != null && pawn.jobs != null && job != null) pawn.jobs.TryTakeOrderedJob(job); }
            catch { }
        }

        private static void AttackThing(Pawn pawn, Thing t)
        {
            try
            {
                var verb = pawn.TryGetAttackVerb(t, true);
                if (verb == null || !verb.CanHitTarget(t)) return;
                verb.TryStartCastOn(t);
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

        // 建筑"使用"：床->躺下休息；可开关电器->切换开关；工作台/容器只保留、不误拆。
        private static bool UseBuilding(Pawn pawn, Building b)
        {
            try
            {
                if (b is Building_Bed bed)
                {
                    Give(pawn, JobMaker.MakeJob(JobDefOf.LayDown, bed));
                    return true;
                }
                var fl = b.GetComp<CompFlickable>();
                if (fl != null)
                {
                    fl.SwitchIsOn = !fl.SwitchIsOn;
                    return true;
                }
                if (b is Building_WorkTable) return true;
                if (b is Building_Storage) return true;
            }
            catch { }
            return false;
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

                // 地面平面（当前层高）
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
                            best = new RayHit { cell = cell, dist = t };
                            found = true;
                        }
                    }
                }

                // 满格建筑 / 门 / 地面物品：沿射线 0.25 格步进（从 0.05m 起，避免贴门 < 0.6m 时首步穿过门格），首次命中即最近命中
                for (float t = 0.05f; t < bestT; t += 0.25f)
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
                    // 门必须排在满格之前：门的 fillPercent=1 也是满格，
                    // 单独判门语义更清楚，也保证门一定作为命中物返回。
                    if (ed is Building_Door)
                    {
                        bestT = t;
                        best = new RayHit { cell = cell, thing = ed, dist = t };
                        found = true;
                        break;
                    }
                    // 满格实体墙：维持原命中并停止
                    if (ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full)
                    {
                        bestT = t;
                        best = new RayHit { cell = cell, thing = ed, dist = t };
                        found = true;
                        break;
                    }
                    // 地面物品：本步尚无更近的墙/门，且采样高度在基面到 1.2m 之间
                    if (p.y >= baseY && p.y <= baseY + 1.2f)
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
