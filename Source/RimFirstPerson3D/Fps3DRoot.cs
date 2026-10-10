// AI生成
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 常驻 MonoBehaviour：模式切换、场景生命周期、每帧驱动与 HUD。
    public class Fps3DRoot : MonoBehaviour
    {
        private Map lastMap;
        private Scene3D scene;
        private bool suppressed;
        private float pruneTimer;
        private float snapshotTimer;
        private float lastWallV = float.MinValue, lastWallO = float.MinValue, lastPlantS = float.MinValue;

        private void Update()
        {
            if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null)
            {
                Leave3D(true, true, true);
                return;
            }

            HandleKeys();
            // 终端状态机。捕获在 Tick 内完成并清掉标志，后面的场景 DrawMesh 仍走 layer 0。
            MapScreen3D.Tick(Time.deltaTime);

            if (!Fps3DState.Active)
            {
                Leave3D(false, true, true);
                return;
            }

            if (!suppressed)
            {
                suppressed = true;
                CameraRig3D.OnModeChanged();
            }
            // 每帧幂等重申接管：对抗地图加载收尾期原版对相机参数的回写
            VanillaSuppressor.ApplyCameraSuppression();

            // 地图切换：换场景（同栈全部楼层一起渲染）
            if (lastMap != Find.CurrentMap)
            {
                lastMap = Find.CurrentMap;
                CameraRig3D.OnModeChanged();
            }
            if (scene == null || scene.map.Disposed || scene.map != Find.CurrentMap)
            {
                scene = SceneRegistry.GetOrCreate(Find.CurrentMap);
                lastMap = Find.CurrentMap;
            }
            if (scene == null) return;

            // 重建类视觉参数变化 -> 同栈全层标脏
            var stv = FirstPerson3DMod.settings;
            bool paramChanged = stv != null
                && (stv.wallVScale != lastWallV || stv.wallOutline != lastWallO || stv.plantScale != lastPlantS);
            if (paramChanged)
            {
                lastWallV = stv.wallVScale;
                lastWallO = stv.wallOutline;
                lastPlantS = stv.plantScale;
            }

            CameraRig3D.UpdateCamera(Time.deltaTime);
            Viewmodel3D.Tick(Time.deltaTime);
            // 调试冻结位姿（cmdAction termPose <p>）：常驻绘制终端、不进状态机
            if (HandTerminal3D.DebugHold >= 0f && !MapScreen3D.Open)
                HandTerminal3D.Draw(CameraRig3D.Cam, Mathf.Clamp01(HandTerminal3D.DebugHold), 1f);
            // M4：附身 pawn 走到边界自动跨图
            if (CameraRig3D.Possessed != null)
                Streaming3D.TryCrossBoundary(CameraRig3D.Possessed);
            // M4：把世界上已生成的邻接地图登记进偏移表，走到边缘前即可寻到目标
            Streaming3D.EnsureNeighbors(Find.CurrentMap);
            var camPos = CameraRig3D.Cam != null ? CameraRig3D.Cam.transform.position : Vector3.zero;
            // 刷新本帧相机视锥（供各层 chunk 与逐件绘制的屏幕外/超远剔除）
            Frustum3D.Update();
            SkyScenery.DrawDistantRing(camPos);
            foreach (var m in Streaming3D.RenderMaps(Find.CurrentMap))
            {
                if (m == null || m.Disposed) continue;
                var sc = SceneRegistry.GetOrCreate(m);
                if (sc == null) continue;
                if (paramChanged) sc.MarkAllDirty();
                // 按图隔离：单图场景异常只丢该图该帧，不拖垮其它图与整帧
                try
                {
                    sc.Update();
                    if (CameraRig3D.Cam != null)
                    {
                        PerfCounters.ResetFrame();
                        sc.Draw(camPos);
                    }
                }
                catch (System.Exception e)
                {
                    var stE = FirstPerson3DMod.settings;
                    if (stE != null)
                        stE.diagCross = "sceneErr m=" + m.uniqueID + " " + e.GetType().Name
                            + ": " + e.Message + " @" + e.StackTrace;
                    Log.Warning(ModEntry.LogPrefix + "scene " + m.uniqueID + " error: " + e);
                }
            }
            PerfMon3D.Update();

            pruneTimer += Time.deltaTime;
            if (pruneTimer > 2f)
            {
                pruneTimer = 0f;
                SceneRegistry.PruneDisposed();
            }

            snapshotTimer += Time.deltaTime;
            if (snapshotTimer > 1f)
            {
                snapshotTimer = 0f;
                var s = FirstPerson3DMod.settings;
                if (s != null) s.perfSnapshot = PerfMon3D.SnapshotJson();
            }
        }

        private void Leave3D(bool forceOff, bool releaseRig, bool forceCloseScreen)
        {
            suppressed = false;
            VanillaSuppressor.RestoreCameras();
            RenderSettings.fog = false;
            SkyScenery.Restore();
            if (forceOff)
                Fps3DState.Mode = ViewMode.Off;
            if (forceCloseScreen)
                MapScreen3D.ForceClose();
            if (releaseRig)
                CameraRig3D.Release();
        }

        private void OnGUI()
        {
            if (!Fps3DState.Active || Current.ProgramState != ProgramState.Playing) return;
            var prevColor = GUI.color;
            var prevMatrix = GUI.matrix;
            var prevAnchor = Text.Anchor;
            try
            {
                // 覆盖层按 UIScale 缩放绘制，与 UI.screenWidth/Height（逻辑坐标）一致。
                // 否则在 4K/高 DPI 下（UIScale>1）整个覆盖层会被按 1/UIScale 缩小、偏向
                // 屏幕左上：准星离开正中、名牌错位飘移。与 RimWorld 原生 UI 同一套缩放。
                GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                    new Vector3(Prefs.UIScale, Prefs.UIScale, 1f));
                if (Fps3DState.ShowPerfHud) PerfMon3D.DrawHud();
                // 终端占据视野时准星、名牌、手和背包让位。性能条留下，方便对照开关前后的帧时间。
                if (!MapScreen3D.Animating && !MapScreen3D.Viewing)
                {
                    Crosshair3D.Draw();
                    NameLabels3D.Draw();
                    Hand3D.Draw();
                    Viewmodel3D.DrawWeapon();
                    FpsHud3D.Draw();
                }
            }
            finally
            {
                GUI.color = prevColor;
                GUI.matrix = prevMatrix;
                Text.Anchor = prevAnchor;
            }
        }

        private void HandleKeys()
        {
            // 远程动作通道（GABS）：与调试菜单同一入口
            var s = FirstPerson3DMod.settings;
            if (s != null && !string.IsNullOrEmpty(s.cmdAction))
            {
                string a = s.cmdAction.Trim();
                s.cmdAction = "";
                if (a == "createUpper") DebugActions3D.M3CreateUpper();
                else if (a == "levelUp") DebugActions3D.LevelUp();
                else if (a == "levelDown") DebugActions3D.LevelDown();
                else if (a == "streamEast") Streaming3D.StreamEast();
                else if (a == "unloadStreamed") Streaming3D.UnloadStreamed();
                else if (a == "mapScreen") MapScreen3D.Toggle();
                else if (a == "camdiag")
                {
                    if (s != null) s.diagCross = VanillaSuppressor.CameraDiagnose();
                }
                else if (a == "camheal")
                {
                    VanillaSuppressor.HealMainCamera();
                    if (s != null) s.diagCross = "healed -> " + VanillaSuppressor.CameraDiagnose();
                }
                else if (a.StartsWith("termPose"))
                {
                    float.TryParse(a.Substring(8).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float p);
                    HandTerminal3D.DebugHold = p;
                }
                else Log.Warning(ModEntry.LogPrefix + "unknown cmdAction '" + a + "'");
            }
            if (Input.GetKeyDown(KeyCode.F8))
            {
                Fps3DState.Mode = Fps3DState.Mode switch
                {
                    ViewMode.Off => ViewMode.FirstPerson,
                    ViewMode.FirstPerson => ViewMode.Chase,
                    _ => ViewMode.Off
                };
                if (Fps3DState.Mode == ViewMode.Off)
                    Leave3D(false, true, true);
                Log.Message(ModEntry.LogPrefix + "mode -> " + Fps3DState.Mode);
            }
            if (Input.GetKeyDown(KeyCode.F7))
            {
                Fps3DState.ShowPerfHud = !Fps3DState.ShowPerfHud;
            }
            if (Input.GetKeyDown(KeyCode.F10))
            {
                SceneRegistry.DisposeAll();
                scene = null;
                lastMap = null;
                Log.Message(ModEntry.LogPrefix + "scenes rebuilt");
            }
            if (Fps3DState.Active)
            {
                // M：指挥面板。终端抬起，地图和原版操作界面都在屏幕上，主画面保持第一人称。
                if (Input.GetKeyDown(KeyCode.M)) MapScreen3D.Toggle();
                // B：第一人称背包面板（屏幕活动期不开）
                if (!MapScreen3D.BlocksGameplay && Input.GetKeyDown(KeyCode.B)) FpsHud3D.ToggleInventory();
                // E：直接交互（门开关 / 拾取 / 装备 / 挖掘）；背包开着时先收背包
                if (!MapScreen3D.BlocksGameplay && Input.GetKeyDown(KeyCode.E))
                {
                    if (FpsHud3D.inventoryOpen) FpsHud3D.CloseInventory();
                    else CameraRig3D.Interact();
                }
                // 接管期原版 UI 被跳过（空格暂停的原版通道一并被接管），
                // 这里补上同一功能，游戏节奏控制不丢失。
                if (CameraRig3D.VanillaInputTakenOver
                    && Input.GetKeyDown(KeyCode.Space)
                    && Find.TickManager != null)
                    Find.TickManager.TogglePaused();
                if (Input.GetKeyDown(KeyCode.PageUp))
                {
                    if (LevelStack3D.SwitchLevel(1))
                        Log.Message(ModEntry.LogPrefix + "level up -> " + Find.CurrentMap.uniqueID);
                }
                if (Input.GetKeyDown(KeyCode.PageDown))
                {
                    if (LevelStack3D.SwitchLevel(-1))
                        Log.Message(ModEntry.LogPrefix + "level down -> " + Find.CurrentMap.uniqueID);
                }
            }
        }
    }
}
