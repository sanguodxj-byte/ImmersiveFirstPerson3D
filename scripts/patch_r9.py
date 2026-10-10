# -*- coding: utf-8 -*-
# r9: 视觉参数全部实时化（ModSettings），供 GABS 在线调优
import io

def rd(p): return io.open(p, encoding='utf-8').read()
def wr(p, s): io.open(p, 'w', encoding='utf-8', newline='\n').write(s)

base = r'D:\rim mod\FirstPerson3D\Source\RimFirstPerson3D'

# ---------- 1) 设置面板新增视觉字段 ----------
s = rd(base + r'\ModSettings3D.cs')
old = """        public bool freeCam = false;
        public string possessName = "";
        public string perfSnapshot = "";"""
new = """        public bool freeCam = false;
        public string possessName = "";
        public string perfSnapshot = "";

        // ---- 视觉调优参数（全部实时生效；带 * 的需触发对应层重建，由根驱动自动检测）----
        public float fov = 72f;             // 透视视场角
        public float pawnHeight = 1.8f;     // 人形立牌总高（米）
        public float pawnWidthScale = 0.72f;// 立牌宽 = 高 * 此值
        public float headScale = 0.34f;     // 头部面片尺寸 = 身高 * 此值
        public float headYFrac = 0.78f;     // 头部中心高度 = 身高 * 此值
        public float wallVScale = 0.55f;    // *墙体侧面贴图纵向密度
        public float wallOutline = 0.03f;   // *描边壳外扩量（米）
        public float skyR = 0.45f;          // 天空色
        public float skyG = 0.62f;
        public float skyB = 0.85f;
        public bool fogOn = false;          // 线性雾
        public float fogEnd = 95f;          // 雾完全距离（米）
        public float plantScale = 1.0f;     // *植物高度缩放"""
assert old in s, "settings anchor"
s = s.replace(old, new)
old = """            Scribe_Values.Look(ref freeCam, "freeCam", false);
            base.ExposeData();"""
new = """            Scribe_Values.Look(ref freeCam, "freeCam", false);
            Scribe_Values.Look(ref fov, "fov", 72f);
            Scribe_Values.Look(ref pawnHeight, "pawnHeight", 1.8f);
            Scribe_Values.Look(ref pawnWidthScale, "pawnWidthScale", 0.72f);
            Scribe_Values.Look(ref headScale, "headScale", 0.34f);
            Scribe_Values.Look(ref headYFrac, "headYFrac", 0.78f);
            Scribe_Values.Look(ref wallVScale, "wallVScale", 0.55f);
            Scribe_Values.Look(ref wallOutline, "wallOutline", 0.03f);
            Scribe_Values.Look(ref skyR, "skyR", 0.45f);
            Scribe_Values.Look(ref skyG, "skyG", 0.62f);
            Scribe_Values.Look(ref skyB, "skyB", 0.85f);
            Scribe_Values.Look(ref fogOn, "fogOn", false);
            Scribe_Values.Look(ref fogEnd, "fogEnd", 95f);
            Scribe_Values.Look(ref plantScale, "plantScale", 1.0f);
            base.ExposeData();"""
assert old in s, "scribe anchor"
s = s.replace(old, new)
wr(base + r'\ModSettings3D.cs', s)
print("settings ok")

# ---------- 2) 相机：每帧应用 fov/天空/雾 ----------
c = rd(base + r'\CameraRig3D.cs')
old = """        public static void UpdateCamera(float dt)
        {
            EnsureCreated();
            if (!Fps3DState.Active) return;"""
new = """        public static void UpdateCamera(float dt)
        {
            EnsureCreated();
            if (!Fps3DState.Active) return;

            ApplyLookSettings();"""
assert old in c, "updatecamera anchor"
c = c.replace(old, new)

old = """        private static void HandleLook()"""
new = """        // 每帧应用视觉参数：FOV / 天空色 / 线性雾（全为设置面板实时项）。
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

        private static void HandleLook()"""
assert old in c, "handlelook anchor"
c = c.replace(old, new)
wr(base + r'\CameraRig3D.cs', c)
print("camera ok")

# ---------- 3) 抑制器：初始 fov/天空也读设置 ----------
v = rd(base + r'\VanillaSuppressor.cs')
old = """                mapCamera.orthographic = false;
                mapCamera.fieldOfView = Fps3DState.FovDeg;
                mapCamera.nearClipPlane = Fps3DState.NearClip;
                mapCamera.farClipPlane = Fps3DState.FarClip;
                mapCamera.backgroundColor = new Color(0.09f, 0.11f, 0.16f);"""
new = """                var st = FirstPerson3DMod.settings;
                mapCamera.orthographic = false;
                mapCamera.fieldOfView = st != null ? st.fov : Fps3DState.FovDeg;
                mapCamera.nearClipPlane = Fps3DState.NearClip;
                mapCamera.farClipPlane = Fps3DState.FarClip;
                mapCamera.backgroundColor = new Color(
                    st != null ? st.skyR : 0.09f,
                    st != null ? st.skyG : 0.11f,
                    st != null ? st.skyB : 0.16f);"""
assert old in v, "suppressor anchor"
v = v.replace(old, new)
wr(base + r'\VanillaSuppressor.cs', v)
print("suppressor ok")

# ---------- 4) 建筑层：vScale/描边读设置 ----------
b = rd(base + r'\BuildingLayer3D.cs')
old = """            mat = Util3D.CloneForBox(mat);

            var center = t.DrawPos;
            var pos = new Vector3(center.x, baseY, center.z);
            float w = occ.Width, d = occ.Height;"""
new = """            mat = Util3D.CloneForBox(mat);

            var center = t.DrawPos;
            var pos = new Vector3(center.x, baseY, center.z);
            float w = occ.Width, d = occ.Height;
            var st = FirstPerson3DMod.settings;
            float vS = st != null ? st.wallVScale : 0.55f;
            float outline = st != null ? st.wallOutline : 0.03f;"""
assert old in b, "bake anchor"
b = b.replace(old, new)
b = b.replace("vScale: 0.55f,", "vScale: vS,")
b = b.replace("hull.AppendBoxHull(dw, Fps3DState.WallHeight * 0.82f, dd, 0.03f,",
              "hull.AppendBoxHull(dw, Fps3DState.WallHeight * 0.82f, dd, outline,")
b = b.replace("hull.AppendBoxHull(w, Fps3DState.WallHeight, d, 0.03f,",
              "hull.AppendBoxHull(w, Fps3DState.WallHeight, d, outline,")
assert "vScale: vS," in b and b.count("vScale: vS,") == 2, "vScale replace"
wr(base + r'\BuildingLayer3D.cs', b)
print("building ok")

# ---------- 5) Pawn 层：比例读设置 ----------
p = rd(base + r'\PawnLayer3D.cs')
old = """            float H, W;
            if (pawn.RaceProps.Humanlike)
            {
                H = Fps3DState.PawnBillboardHeight;
                W = H * 0.72f;
                DrawHumanlike(pawn, pos, yaw, viewRot, H, W, quad);
            }"""
new = """            float H, W;
            if (pawn.RaceProps.Humanlike)
            {
                var st = FirstPerson3DMod.settings;
                H = st != null ? st.pawnHeight : Fps3DState.PawnBillboardHeight;
                W = H * (st != null ? st.pawnWidthScale : 0.72f);
                DrawHumanlike(pawn, pos, yaw, viewRot, H, W, quad);
            }"""
assert old in p, "pawn size anchor"
p = p.replace(old, new)
old = """            float headY = H * 0.78f;
            float headS = H * 0.34f;"""
new = """            var st2 = FirstPerson3DMod.settings;
            float headY = H * (st2 != null ? st2.headYFrac : 0.78f);
            float headS = H * (st2 != null ? st2.headScale : 0.34f);"""
assert old in p, "head anchor"
p = p.replace(old, new)
wr(base + r'\PawnLayer3D.cs', p)
print("pawn ok")

# ---------- 6) 植物层：高度缩放读设置 ----------
pl = rd(base + r'\PlantLayer3D.cs')
old = """            foreach (var e in entries)
            {
                var size = e.lastVisualSize;
                if (size.x <= 0.01f) continue;
                var matrix = Matrix4x4.TRS(e.pos, Quaternion.identity,
                    new Vector3(size.x, Mathf.Max(0.1f, size.y), 1f));"""
new = """            var st = FirstPerson3DMod.settings;
            float hScale = st != null ? st.plantScale : 1f;
            foreach (var e in entries)
            {
                var size = e.lastVisualSize;
                if (size.x <= 0.01f) continue;
                var matrix = Matrix4x4.TRS(e.pos, Quaternion.identity,
                    new Vector3(size.x, Mathf.Max(0.1f, size.y * hScale), 1f));"""
assert old in pl, "plant anchor"
pl = pl.replace(old, new)
wr(base + r'\PlantLayer3D.cs', pl)
print("plant ok")

# ---------- 7) 根驱动：检测重建类参数变化 + 退出关雾 ----------
f = rd(base + r'\Fps3DRoot.cs')
old = """        private Map lastMap;
        private Scene3D scene;
        private bool suppressed;
        private float pruneTimer;
        private float snapshotTimer;"""
new = """        private Map lastMap;
        private Scene3D scene;
        private bool suppressed;
        private float pruneTimer;
        private float snapshotTimer;
        private float lastWallV = float.MinValue, lastWallO = float.MinValue, lastPlantS = float.MinValue;"""
assert old in f, "root fields anchor"
f = f.replace(old, new)

old = """            CameraRig3D.UpdateCamera(Time.deltaTime);
            scene.Update();"""
new = """            // 重建类视觉参数变化 -> 全层标脏
            var stv = FirstPerson3DMod.settings;
            if (stv != null && (stv.wallVScale != lastWallV || stv.wallOutline != lastWallO || stv.plantScale != lastPlantS))
            {
                lastWallV = stv.wallVScale;
                lastWallO = stv.wallOutline;
                lastPlantS = stv.plantScale;
                scene.MarkAllDirty();
            }

            CameraRig3D.UpdateCamera(Time.deltaTime);
            scene.Update();"""
assert old in f, "root update anchor"
f = f.replace(old, new)

# 退出 3D 模式的两处恢复路径补关雾
f = f.replace("""                    VanillaSuppressor.RestoreCameras();
                    CameraRig3D.SetEnabled(false);
                    Fps3DState.Mode = ViewMode.Off;""",
"""                    VanillaSuppressor.RestoreCameras();
                    CameraRig3D.SetEnabled(false);
                    RenderSettings.fog = false;
                    Fps3DState.Mode = ViewMode.Off;""")
f = f.replace("""                    suppressed = false;
                    VanillaSuppressor.RestoreCameras();
                    CameraRig3D.SetEnabled(false);
                }
                return;""",
"""                    suppressed = false;
                    VanillaSuppressor.RestoreCameras();
                    CameraRig3D.SetEnabled(false);
                    RenderSettings.fog = false;
                }
                return;""", 1)
f = f.replace("""                    if (suppressed)
                    {
                        suppressed = false;
                        VanillaSuppressor.RestoreCameras();
                        CameraRig3D.SetEnabled(false);
                    }
                }
                Log.Message""",
"""                    if (suppressed)
                    {
                        suppressed = false;
                        VanillaSuppressor.RestoreCameras();
                        CameraRig3D.SetEnabled(false);
                        RenderSettings.fog = false;
                    }
                }
                Log.Message""")
wr(base + r'\Fps3DRoot.cs', f)
print("root ok")

# ---------- 8) 快照格式串重写（修 F2 字面量 + 带新参数） ----------
m = rd(base + r'\PerfMon3D.cs')
start = m.index("        public static string SnapshotJson()")
end = m.index("        private static void AppendFileLine()")
new_method = '''        public static string SnapshotJson()
        {
            var c = PerfCounters;
            var st = FirstPerson3DMod.settings;
            string census = CameraRig3D.CameraCensus().Replace(',', ';').Replace('|', '.');
            return string.Format(
                "{{\\"mode\\":\\"{0}\\",\\"fps\\":{1:F1},\\"avgMs\\":{2:F2},\\"p95Ms\\":{3:F2},\\"maxMs\\":{4:F2}," +
                "\\"gc\\":{5:F2},\\"terr\\":{6},\\"bld\\":{7},\\"hull\\":{8},\\"plant\\":{9}/{10},\\"item\\":{11}/{12},\\"pawnQ\\":{13}," +
                "\\"tb\\":{14:F2},\\"bb\\":{15:F2},\\"pb\\":{16:F2},\\"cam\\":\\"{17:F1};{18:F1};{19:F1}\\",\\"who\\":\\"{20}\\"," +
                "\\"fov\\":{21:F0},\\"fog\\":\\"{22};{23:F0}\\",\\"census\\":\\"{24}\\"}}",
                Fps3DState.Mode.ToString(), fpsResult, avgMs, p95Ms, maxMs, gcMbPerSec,
                c.TerrainMeshesDrawn, c.BuildingMeshesDrawn, c.OutlineMeshesDrawn,
                c.PlantInstances, c.PlantBatches, c.ItemInstances, c.ItemBatches, c.PawnQuads,
                c.LastTerrainBuildMs, c.LastBuildingBuildMs, c.LastPlantBuildMs,
                CameraRig3D.LastCamPos.x, CameraRig3D.LastCamPos.y, CameraRig3D.LastCamPos.z,
                CameraRig3D.PossessedInfo.Replace(',', ' '),
                st == null ? 72f : st.fov,
                st != null && st.fogOn ? "on" : "off",
                st == null ? 0f : st.fogEnd,
                census);
        }

'''
m = m[:start] + new_method + m[end:]
wr(base + r'\PerfMon3D.cs', m)
print("perf ok")

# ---------- 9) 版本号 ----------
e = rd(base + r'\ModEntry.cs')
e = e.replace('initialized (r3)', 'initialized (r9)')
wr(base + r'\ModEntry.cs', e)
print("entry ok")
