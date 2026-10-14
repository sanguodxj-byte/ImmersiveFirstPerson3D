// AI生成
using RimWorld;
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
            HitFx3D.Tick(Time.deltaTime);
            ShotFx3D.Tick(Time.deltaTime);
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
            SkyScenery.DrawDistantRing();
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
                LightDumpToFile();
            }
        }

        // 光照导出落盘（F013 验收通道）：与 PerfMon 的 perf 日志同一手法、同一目录。
        // 走文件而不是桥调调试动作，是因为实机菜单路径解析不稳（Actions/<分组> 取不到），
        // 而连续时间的昼夜与灯具变化本来就该由一串 dump 看趋势，不该靠一次性抓取。
        private float lightDumpTimer;
        private void LightDumpToFile()
        {
            var s = FirstPerson3DMod.settings;
            if (s == null || !s.lightFileDump) return;
            // 本块本身就是 1 Hz 才进来一次，所以按秒累加；
            // 用 Time.deltaTime 累加等于每 6 分钟才导出一行（实机踩过）。
            lightDumpTimer += 1f;
            if (lightDumpTimer < 6f) return;
            lightDumpTimer = 0f;
            try
            {
                string path = System.IO.Path.Combine(Application.persistentDataPath,
                    "FirstPerson3D_light.log");
                System.IO.File.AppendAllText(path, Lighting3D.DumpJson() + "\n");
            }
            catch (System.Exception e)
            {
                // 静默吞过一次就再也查不到为什么没文件，这里点名一次
                Log.WarningOnce(ModEntry.LogPrefix + "light dump failed: " + e.GetType().Name
                    + ": " + e.Message + "\n" + e.StackTrace, 12001);
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                CameraRig3D.ReleaseMouseLook();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                CameraRig3D.ReleaseMouseLook();
            }
        }

        private void Leave3D(bool forceOff, bool releaseRig, bool forceCloseScreen)
        {
            suppressed = false;
            HitFx3D.Clear();
            ShotFx3D.Clear();
            VanillaSuppressor.RestoreCameras();
            SkyScenery.Restore();
            if (forceOff)
                Fps3DState.ResetInMemory();
            if (forceCloseScreen)
                MapScreen3D.ForceClose();
            if (releaseRig)
                CameraRig3D.Release();
        }

        private void OnGUI()
        {
            if (!Fps3DState.Active || Current.ProgramState != ProgramState.Playing) return;
            bool isRepaint = Event.current != null && Event.current.type == EventType.Repaint;
            if (!isRepaint && !FpsHud3D.HasInteractiveUI) return;

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
                if (isRepaint && Fps3DState.ShowPerfHud) PerfMon3D.DrawHud();
                // 终端占据视野时准星、名牌、手和背包让位。性能条留下，方便对照开关前后的帧时间。
                if (!MapScreen3D.Animating && !MapScreen3D.Viewing)
                {
                    if (isRepaint)
                    {
                        // 受击红雾压在场景之上、准星之下：准星和 HUD 必须一直读得出来
                        HitFx3D.DrawVignette();
                        Crosshair3D.Draw();
                        NameLabels3D.Draw();
                        Hand3D.Draw();
                        Viewmodel3D.DrawWeapon();
                        Viewmodel3D.DrawCarried();
                    }
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
                else if (a == "modeFirstPerson") Fps3DState.Mode = ViewMode.FirstPerson;
                else if (a == "levelUp") DebugActions3D.LevelUp();
                else if (a == "levelDown") DebugActions3D.LevelDown();
                else if (a == "streamEast") Streaming3D.StreamEast();
                else if (a == "unloadStreamed") Streaming3D.UnloadStreamed();
                else if (a == "mapScreen") MapScreen3D.Toggle();
                else if (a == "interact") CameraRig3D.Interact();
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
                else if (a.StartsWith("skyPreset"))
                {
                    string preset = a.Length > 9 ? a.Substring(9).Trim() : "";
                    SkyScenery.DebugPreset = (preset == "off" || preset == "none") ? "" : preset;
                    var curMap = Find.CurrentMap;
                    if (curMap != null)
                    {
                        try
                        {
                            var plants = curMap.listerThings.ThingsInGroup(ThingRequestGroup.Plant);
                            for (int i = 0; i < plants.Count; i++)
                            {
                                if (plants[i] is Plant pl && pl.def != null && pl.def.plant != null && pl.def.plant.IsTree && pl.Growth < 0.85f)
                                    pl.Growth = 1.0f;
                            }
                        }
                        catch { }
                        var sc = SceneRegistry.GetOrCreate(curMap);
                        if (sc != null)
                        {
                            sc.ForcePrimeLighting();
                        }
                    }
                }
                else if (a == "lightDump")
                {
                    // 光照导出（F013）：结果落进 settings.lightSnapshot，GABS 直接回读
                    DebugActions3D.DumpLighting();
                }
                else if (a.StartsWith("testScenario"))
                {
                    string scName = a.Length > 12 ? a.Substring(12).Trim() : "";
                    DebugActions3D.RunTestScenario(scName);
                }
                else Log.Warning(ModEntry.LogPrefix + "unknown cmdAction '" + a + "'");
            }
            if (!Application.isFocused || !MouseLookDelta.IsGameWindowActive()) return;
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
            // F9：顶点色载体探针（F013 验证用）。开著时所有烘焙顶点色变成常数灰，
            // 画面不跟着整片变暗就说明着色器不吃顶点色，光照得换载体。
            if (Input.GetKeyDown(KeyCode.F9))
            {
                var sp = FirstPerson3DMod.settings;
                if (sp != null)
                {
                    sp.lightProbe = !sp.lightProbe;
                    Log.Message(ModEntry.LogPrefix + "carrier probe -> " + sp.lightProbe);
                }
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

                // 原版暂停与时间控制热键放行：空格键（暂停）与时间速度按键
                if (Find.TickManager != null)
                {
                    if (KeyBindingDefOf.TogglePause.KeyDownEvent || Input.GetKeyDown(KeyCode.Space))
                        Find.TickManager.TogglePaused();
                    else if (KeyBindingDefOf.TimeSpeed_Normal.KeyDownEvent)
                        Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    else if (KeyBindingDefOf.TimeSpeed_Fast.KeyDownEvent)
                        Find.TickManager.CurTimeSpeed = TimeSpeed.Fast;
                    else if (KeyBindingDefOf.TimeSpeed_Superfast.KeyDownEvent)
                        Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                }
            }
        }
    }
}
