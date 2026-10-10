// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimFirstPerson3D
{
    // 透视相机控制：第一人称（附身 pawn）与第三人称追尾，含输入与指令下达。
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
        // 已接管且未临时让位（Alt 唤鼠 / 原版弹窗 / 暂停 / 失焦 / 原版视角屏 / 无附身对象）。
        public static bool VanillaInputTakenOver =>
            Fps3DState.Active && Fps3DState.Mode == ViewMode.FirstPerson
            && inputEngaged && Possessed != null
            && !MapScreen3D.Open
            && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt)
            && Application.isFocused
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
                anchor = new Vector3(foot.Value.x, pawnBaseY + foot.Value.y, foot.Value.z);
            else
                anchor = new Vector3(dp.x + pawnOffset.x, pawnBaseY, dp.z + pawnOffset.z);

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

        // 相机普查：枚举场景中所有 Camera 的关键状态。
        public static string CameraCensus()
        {
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
            return sb.ToString();
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

            // 终端查看：光标留给屏幕射线。这里直接返回，右键拖拽和远程环视也不会带动终端。
            if (MapScreen3D.Viewing)
            {
                UnlockCursor();
                MouseLookDelta.Reset();
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
            if (!Application.isFocused) return true;
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

            // 与 HandleLook 的查看期早退重复一道：避免以后单独调用时把光标藏起来并回中。
            if (MapScreen3D.Viewing)
            {
                UnlockCursor();
                return false;
            }

            bool uiBusy = VanillaUiBusy();
            bool paused = Find.TickManager != null && Find.TickManager.Paused;
            // 按住 Alt：临时唤出鼠标做 UI 操作，松开自动回到接管态。
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            // mouseLook 从关到开（含远控面板中途开启）：首个无阻挡帧立即接管并锁定。
            if (mouseLookLatchPending && !altHeld && !uiBusy && !paused && Application.isFocused)
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

            // 临时让位：只解锁光标，接管保持
            if (altHeld || uiBusy || paused || !Application.isFocused)
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
            return stack != null
                && (stack.AnyWindowAbsorbingAllInput
                    || stack.NonImmediateDialogWindowOpen
                    || stack.WindowsForcePause);
        }

        private static void SetCursorState(CursorLockMode lockMode, bool visible)
        {
            if (Cursor.lockState != lockMode) Cursor.lockState = lockMode;
            if (Cursor.visible != visible) Cursor.visible = visible;
        }

        private static void UnlockCursor()
        {
            SetCursorState(CursorLockMode.None, true);
            mousePositionReady = false;
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
                    || Find.WindowStack.WindowsForcePause;
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
        private static bool StandableAt(Map map, Vector2 local)
        {
            var c = new IntVec3(Mathf.FloorToInt(local.x), 0, Mathf.FloorToInt(local.y));
            if (!c.InBounds(map)) return false;
            if (!c.Walkable(map)) return false;
            var ed = c.GetEdifice(map);
            if (ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full) return false;
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

        // 附身者当前 job 是挖矿 / 搬运，或正在施放动词时：让原版驱动，本帧不接管。
        // 这三件事是 HandleClick 会下的原版指令。
        private static bool JobOwnsPawn(Pawn pawn)
        {
            try
            {
                var job = pawn.CurJob;
                if (job != null && job.def != null)
                {
                    if (job.def == JobDefOf.Mine) return true;
                    if (job.def == JobDefOf.TakeInventory) return true;
                }
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
            // 查看期的左键是屏幕平移/点选，不走准星射线。
            if (MapScreen3D.Viewing)
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
            if (!synthetic && (!Input.GetMouseButtonDown(0) || GUIUtility.hotControl != 0)) return;
            // 该次左键已被重新锁定自由视角消费，不再下单。
            if (!synthetic && clickConsumedByLook)
            {
                clickConsumedByLook = false;
                return;
            }
            // 背包面板打开时不攻击（面板内的点击交给 GUI 按钮）。
            if (FpsHud3D.inventoryOpen) return;
            Attack();
        }

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

        // E 键交互：准星命中项执行一次交互（门开关、拾取、装备、挖掘）。
        public static void Interact()
        {
            if (MapScreen3D.Viewing || FpsHud3D.inventoryOpen) return;
            var hit = RaycastCenter();
            if (hit == null) return;
            var thing = hit.Value.thing;
            if (thing == null) return;
            Find.Selector.Select(thing);
            ApplyInteraction(thing);
        }

        // 选中之后的左键动作：仅当有附身者时生效。
        private static void ApplyInteraction(Thing thing)
        {
            var pawn = Possessed;
            if (pawn == null || thing == null) return;

            // 门（栅栏门、动物帘同为 Building_Door）：手动开/关
            var door = thing as Building_Door;
            if (door != null)
            {
                if (!door.Open) door.StartManualOpenBy(pawn);
                else door.StartManualCloseBy(pawn);
                return;
            }

            // 地面物品：武器直接装备（原版 Equip 作业），其余拾入背包暂存。
            if (thing.def != null && thing.def.category == ThingCategory.Item)
            {
                thing.SetForbidden(false, false);
                if (thing.def.IsWeapon)
                {
                    pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Equip, thing));
                    return;
                }
                Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, thing);
                job.count = 1;
                job.checkEncumbrance = true;
                job.takeInventoryDelay = 120;
                pawn.jobs.TryTakeOrderedJob(job);
                return;
            }

            // 天然岩 / 可挖掘：挖掘作业，距离交给作业本身，不再卡 1 格
            var building = thing as Building;
            if (building != null
                && (building.def != null && building.def.building != null && building.def.building.isNaturalRock
                    || thing is Mineable))
            {
                Job job = JobMaker.MakeJob(JobDefOf.Mine, thing);
                pawn.jobs.TryTakeOrderedJob(job);
                return;
            }

            // 敌对 pawn：能打就打（不重算伤害、不打友方），否则只保留选中
            var target = thing as Pawn;
            if (target != null && target.HostileTo(pawn))
            {
                var verb = pawn.TryGetAttackVerb(thing, true);
                if (verb == null || !verb.CanHitTarget(thing)) return;
                verb.TryStartCastOn(thing);
            }
        }

        public struct RayHit
        {
            public IntVec3 cell;
            public Thing thing;
        }

        // 屏幕中心射线 → 真实命中：对全部渲染图依次测地面平面、满格建筑体素盒、
        // 人形立牌盒，取最近者——准星指什么就命中什么，不再穿墙落到墙后地面格。
        public static RayHit? RaycastCenter()
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
                            best = new RayHit { cell = cell };
                            found = true;
                        }
                    }
                }

                // 满格建筑 / 门 / 地面物品：沿射线 0.25 格步进，首次命中即最近命中
                for (float t = 0.6f; t < bestT; t += 0.25f)
                {
                    var p = ray.origin + ray.direction * t;
                    if (p.y < baseY - 0.1f || p.y > baseY + Fps3DState.WallHeight + 0.05f) continue;
                    var cell = new IntVec3(
                        Mathf.FloorToInt(p.x - off.x), 0, Mathf.FloorToInt(p.z - off.z));
                    if (!cell.InBounds(map)) continue;
                    // 满格实体墙：维持原命中并停止
                    var ed = cell.GetEdifice(map);
                    if (ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full)
                    {
                        bestT = t;
                        best = new RayHit { cell = cell, thing = ed };
                        found = true;
                        break;
                    }
                    // 门（不是满格）：同样命中该门并停止，不再穿过门打到门后的地面/墙
                    if (ed is Building_Door)
                    {
                        bestT = t;
                        best = new RayHit { cell = cell, thing = ed };
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
                            best = new RayHit { cell = cell, thing = item };
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
                        best = new RayHit { cell = p.Position, thing = p };
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
