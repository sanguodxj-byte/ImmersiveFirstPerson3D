// AI生成
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 手持地图终端（viewmodel）：Blender 建模 OBJ 经 ObjImporter3D 导入，
    // 每帧以第一人称相机空间矩阵提交绘制。抬起动画 = 局部位姿从画外下方
    // 插值到眼前；屏幕面为独立 submesh 贴程序生成的终端 UI 纹理，
    // 附扫描亮带滚动与启动渐亮遮罩。OBJ 缺失时回退程序化盒体。
    [StaticConstructorOnStartup]
    public static class HandTerminal3D
    {
        // 抬起动画两端位姿（相机局部空间）：画外下方沉没 -> 眼前。
        // 到位后正对视线且贴近相机，让终端外壳完整落入画面（占高约 96%），屏幕内口清晰锐利且四周留出一圈第一人称背景。
        private static readonly Vector3 StartPos = new Vector3(0f, -0.50f, 0.46f);
        private static readonly Vector3 EndPos = new Vector3(0f, -0.008f, 0.305f);
        private const float StartPitch = 30f;
        private const float EndPitch = 1.5f;
        public const float PanelScale = 1.44f;

        private static bool loadTried;
        // 按材质组拆分的独立网格：Graphics.DrawMesh(mesh, mat) 只渲染 submesh 0，
        // 多 submesh 单材质逐个提交 = 全部重复画 submesh 0（实机踩坑：只见机身不见屏幕）。
        private static readonly List<Mesh> groupMeshes = new List<Mesh>();
        private static readonly List<string> groups = new List<string>();
        private static readonly List<Material> mats = new List<Material>();
        // 数据域副本：拆组之后仍要按 uv 把 screen 面重定位到边框内口、并取它的外包与朝向。
        // 一律吃这份数据，不再从 Mesh 读回（见 BuildGroupMeshes 注释）。
        private static readonly List<List<Vector3>> groupVerts = new List<List<Vector3>>();
        private static readonly List<List<Vector2>> groupUvs = new List<List<Vector2>>();
        private static readonly List<List<Vector3>> groupNormals = new List<List<Vector3>>();

        // 一份终端几何数据：顶点/UV/法线 + 每个材质组的三角索引（索引指向上面的顶点表）。
        private class TermData
        {
            public List<Vector3> verts = new List<Vector3>();
            public List<Vector2> uvs = new List<Vector2>();
            public List<Vector3> normals = new List<Vector3>();
            public List<string> names = new List<string>();
            public List<List<int>> tris = new List<List<int>>();
        }
        private static Material bodyMat, darkMat, metalMat, screenMat, scanMat, maskMat;
        private static Texture screenTex;
        private static Mesh scanQuad, maskQuad;
        // 边框内口。Z 取反后机身正面在 z=-0.0160，黑边框外表面在 z=-0.0180；
        // 把屏幕嵌在 z=-0.0172（在机身正面之前、黑边框之内，朝向位于 z=0 的相机）。
        const float OpeningZ = -0.0172f;
        const float OpeningHalfW = 0.145f;
        const float OpeningHalfH = 0.1025f;
        static bool screenPlaneReady;
        static float planeZ, planeMinX, planeMaxX, planeMinY, planeMaxY;
        static bool planeUGrowsX = true, planeVGrowsY = true;

        // 调试冻结位姿（GABS 验收通道）：>=0 时按该进度持续绘制终端、不推进状态机。
        public static float DebugHold = -1f;

        public static bool Ready { get { EnsureLoaded(); return groupMeshes.Count > 0; } }

        public static void EnsureLoaded()
        {
            if (loadTried) return;
            loadTried = true;
            try
            {
                TermData data = null;
                var mod = ModLister.GetActiveModWithIdentifier(ModEntry.HarmonyId);
                if (mod != null)
                {
                    string path = System.IO.Path.Combine(mod.RootDir.FullName, "1.7", "Assets", "hand_terminal.obj");
                    var imported = ObjImporter3D.Load(path);
                    if (imported != null && imported.verts != null && imported.groupTris.Count > 0)
                        data = new TermData
                        {
                            verts = imported.verts,
                            uvs = imported.uvs,
                            normals = imported.normals,
                            names = imported.groups,
                            tris = imported.groupTris,
                        };
                }
                if (data == null)
                {
                    data = BuildFallbackData();
                    Log.Warning(ModEntry.LogPrefix + "terminal OBJ missing, fallback box in use");
                }
                BuildGroupMeshes(data);
                FitScreenToOpening();
                CaptureScreenPlane();
                BuildMaterials();
                Log.Message(ModEntry.LogPrefix + "terminal built: " + groupMeshes.Count + "grp/"
                    + data.verts.Count + "v, screenPlane=" + (screenPlaneReady ? "ok" : "none"));
            }
            catch (System.Exception e)
            {
                Log.Error(ModEntry.LogPrefix + "terminal load failed: " + e);
            }
        }

        // 屏幕正在显示俯视 RT（指挥面板），而不是自绘启动图。
        public static bool ShowingMap { get; private set; }

        // 查看期把屏幕换成俯视 RT。传 null 恢复自绘 UI。
        public static void SetScreenTexture(Texture tex)
        {
            EnsureLoaded();
            ShowingMap = tex != null;
            if (screenMat == null) return;
            screenMat.mainTexture = tex != null ? tex : screenTex;
        }

        // 与 Draw 共用的相机空间矩阵。缩放写在矩阵里，逆变换回到未缩放的模型坐标。
        static Matrix4x4 CameraPose(Camera cam, float raise, out float eased)
        {
            float t = Mathf.Clamp01(raise);
            eased = 1f - Mathf.Pow(1f - t, 3f);
            var pos = Vector3.Lerp(StartPos, EndPos, eased);
            float sway = (1f - eased) * Mathf.Sin(t * Mathf.PI) * 4f;
            var rot = Quaternion.Euler(Mathf.Lerp(StartPitch, EndPitch, eased) + sway, 0f, 0f);
            var local = Matrix4x4.TRS(pos, rot, Vector3.one * PanelScale);
            return cam.transform.localToWorldMatrix * local;
        }

        // 屏幕面在模型坐标里。UV 左下为 (0,0)，和贴图、PanelUi3D 的像素同一套。
        // 半幅不乘 PanelScale：逆矩阵已经除掉缩放。
        public static bool TryScreenUV(Camera cam, float raise, out Vector2 uv)
        {
            uv = default;
            if (cam == null) return false;
            Matrix4x4 world = CameraPose(cam, raise, out _);
            Matrix4x4 inv = world.inverse;
            Ray ray;
            CommandPanel3D.ReadingHardwareMouse = true;
            try
            {
                ray = cam.ScreenPointToRay(Input.mousePosition);
            }
            finally
            {
                CommandPanel3D.ReadingHardwareMouse = false;
            }
            Vector3 o = inv.MultiplyPoint(ray.origin);
            Vector3 d = inv.MultiplyVector(ray.direction);
            float z, x0, x1, y0, y1;
            bool uGrows, vGrows;
            if (screenPlaneReady)
            {
                z = planeZ;
                x0 = planeMinX;
                x1 = planeMaxX;
                y0 = planeMinY;
                y1 = planeMaxY;
                uGrows = planeUGrowsX;
                vGrows = planeVGrowsY;
            }
            else
            {
                z = OpeningZ;
                x0 = -OpeningHalfW;
                x1 = OpeningHalfW;
                y0 = -OpeningHalfH;
                y1 = OpeningHalfH;
                uGrows = true;
                vGrows = true;
            }
            if (Mathf.Abs(d.z) < 1e-6f) return false;
            float dist = (z - o.z) / d.z;
            if (dist < 0f) return false;
            Vector3 hit = o + d * dist;
            const float pad = 0.002f;
            if (hit.x < x0 - pad || hit.x > x1 + pad || hit.y < y0 - pad || hit.y > y1 + pad) return false;
            float u = Mathf.InverseLerp(x0, x1, hit.x);
            float v = Mathf.InverseLerp(y0, y1, hit.y);
            if (!uGrows) u = 1f - u;
            if (!vGrows) v = 1f - v;
            uv = new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
            return true;
        }

        // 抬起到位时屏幕面四角的世界坐标。给俯视 RT 定分辨率用（见 GodView3D.EnsureTargetFor）：
        // 屏幕面固定在相机空间，量它在画面上占多少像素，RT 就开多大，贴上去才 1:1。
        public static bool TryScreenCornersWorld(Camera cam, Vector3[] dst)
        {
            EnsureLoaded();
            if (cam == null || dst == null || dst.Length < 4 || groupMeshes.Count == 0) return false;
            Matrix4x4 world = CameraPose(cam, 1f, out _);
            float z, x0, x1, y0, y1;
            if (screenPlaneReady)
            {
                z = planeZ;
                x0 = planeMinX;
                x1 = planeMaxX;
                y0 = planeMinY;
                y1 = planeMaxY;
            }
            else
            {
                z = OpeningZ;
                x0 = -OpeningHalfW;
                x1 = OpeningHalfW;
                y0 = -OpeningHalfH;
                y1 = OpeningHalfH;
            }
            dst[0] = world.MultiplyPoint3x4(new Vector3(x0, y0, z));
            dst[1] = world.MultiplyPoint3x4(new Vector3(x1, y0, z));
            dst[2] = world.MultiplyPoint3x4(new Vector3(x1, y1, z));
            dst[3] = world.MultiplyPoint3x4(new Vector3(x0, y1, z));
            return true;
        }

        // raise: 0=画外下方 1=眼前；glow: 0=熄屏 1=全亮。
        public static void Draw(Camera cam, float raise, float glow)
        {
            EnsureLoaded();
            if (cam == null || groupMeshes.Count == 0) return;

            var world = CameraPose(cam, raise, out _);
            for (int i = 0; i < groupMeshes.Count; i++)
                if (mats[i] != null) Graphics.DrawMesh(groupMeshes[i], world, mats[i], 0);

            // 主屏幕是贴到边框内口上的 screen 组，已经在上面的循环里画过。
            // 再画一块程序四边形会在同一深度上叠出第二张界面。
            if (glow > 0f)
            {
                if (scanQuad != null && scanMat != null)
                {
                    scanMat.mainTextureOffset = new Vector2(0f, (Time.time * 0.26f) % 1f);
                    Color c = scanMat.color;
                    // 地图和原版界面叠在屏幕上时，只保留极微弱的屏幕光泽，不遮挡原版文字与色彩。
                    c.a = (ShowingMap ? 0.03f : 0.55f) * glow;
                    scanMat.color = c;
                    Graphics.DrawMesh(scanQuad, world, scanMat, 0);
                }
                if (maskQuad != null && maskMat != null && glow < 0.999f)
                {
                    maskMat.color = new Color(0f, 0f, 0f, 1f - glow);
                    Graphics.DrawMesh(maskQuad, world, maskMat, 0);
                }
            }
        }

        // 由导入数据直接按材质组拆独立 Mesh：每个 Mesh 只有 1 个 submesh，三角用 triangles 属性写入。
        // 这里刻意不做任何 Mesh 回读（subMeshCount / GetTriangles(List,int) / vertices / uv / normals / colors）：
        // 1.7（Waylith/UnityStubs）下多 submesh 网格的读写不可靠，曾导致 M 键指挥面板整块静默失效。
        // 顶点色明暗沿用原实现：固定光向 diffuse+ambient，screen 组恒白，按组序先到先得。
        private static void BuildGroupMeshes(TermData d)
        {
            groupMeshes.Clear();
            groups.Clear();
            groupVerts.Clear();
            groupUvs.Clear();
            groupNormals.Clear();

            var lightDir = new Vector3(0.25f, 0.7f, -0.65f).normalized;
            var colors = new Color[d.verts.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            var claimed = new byte[d.verts.Count];

            for (int s = 0; s < d.tris.Count; s++)
            {
                var tris = d.tris[s];
                if (tris == null || tris.Count == 0) continue;
                bool sane = true;
                for (int i = 0; i < tris.Count; i++)
                    if (tris[i] < 0 || tris[i] >= d.verts.Count) { sane = false; break; }
                if (!sane) continue;

                bool isScreen = s < d.names.Count && d.names[s] == "screen";
                var map = new Dictionary<int, int>(tris.Count);
                var nv = new List<Vector3>(tris.Count);
                var nuv = new List<Vector2>(tris.Count);
                var nn = new List<Vector3>(tris.Count);
                var nc = new List<Color>(tris.Count);
                var nt = new int[tris.Count];
                for (int i = 0; i < tris.Count; i++)
                {
                    int old = tris[i];
                    if (!map.TryGetValue(old, out int ni))
                    {
                        ni = nv.Count;
                        map[old] = ni;
                        var n = d.normals != null && old < d.normals.Count ? d.normals[old] : Vector3.up;
                        nv.Add(d.verts[old]);
                        nuv.Add(d.uvs != null && old < d.uvs.Count ? d.uvs[old] : Vector2.zero);
                        nn.Add(n);
                        if (claimed[old] == 0)
                        {
                            claimed[old] = 1;
                            if (!isScreen)
                            {
                                float k = Mathf.Max(0f, Vector3.Dot(n, lightDir));
                                float b = 0.52f + 0.48f * k;
                                colors[old] = new Color(b, b, b, 1f);
                            }
                        }
                        nc.Add(colors[old]);
                    }
                    nt[i] = ni;
                }

                var m = new Mesh { name = "Fps3D_Term_" + (s < d.names.Count ? d.names[s] : s.ToString()) };
                m.SetVertices(nv);
                m.SetUVs(0, nuv);
                m.SetNormals(nn);
                m.SetColors(nc);
                m.triangles = nt;
                m.RecalculateBounds();
                groupMeshes.Add(m);
                groups.Add(s < d.names.Count ? d.names[s] : "");
                groupVerts.Add(nv);
                groupUvs.Add(nuv);
                groupNormals.Add(nn);
            }
        }

        // OBJ 的 screen 与边框内口不是同一块矩形：内口 x±0.145、y±0.1025、z=0.0184，
        // 网格却在 z=0、y[-0.1203, 0.0847]。它更靠近相机，会盖住内口里的界面，点击也打在偏掉的那块上。
        static void FitScreenToOpening()
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != "screen") continue;
                var v = groupVerts[i];
                var uv = groupUvs[i];
                if (uv == null || v == null || uv.Count != v.Count || v.Count == 0) return;
                for (int k = 0; k < v.Count; k++)
                {
                    v[k] = new Vector3(
                        Mathf.Lerp(-OpeningHalfW, OpeningHalfW, uv[k].x),
                        Mathf.Lerp(-OpeningHalfH, OpeningHalfH, uv[k].y),
                        OpeningZ);
                }
                var mesh = groupMeshes[i];
                mesh.SetVertices(v);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                Log.Message(ModEntry.LogPrefix + "terminal screen fit to bezel opening");
                return;
            }
        }

        // 取 screen 组的实际外包和 UV 朝向。FitScreenToOpening 已经把它对齐到边框内口。
        static void CaptureScreenPlane()
        {
            screenPlaneReady = false;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i] != "screen") continue;
                var v = groupVerts[i];
                var uv = groupUvs[i];
                if (v == null || v.Count == 0) return;
                planeMinX = planeMaxX = v[0].x;
                planeMinY = planeMaxY = v[0].y;
                planeZ = v[0].z;
                float uAtMinX = 0f, uAtMaxX = 1f, vAtMinY = 0f, vAtMaxY = 1f;
                bool haveUv = uv != null && uv.Count == v.Count;
                if (haveUv)
                {
                    uAtMinX = uAtMaxX = uv[0].x;
                    vAtMinY = vAtMaxY = uv[0].y;
                }
                for (int k = 1; k < v.Count; k++)
                {
                    planeZ += v[k].z;
                    if (v[k].x < planeMinX)
                    {
                        planeMinX = v[k].x;
                        if (haveUv) uAtMinX = uv[k].x;
                    }
                    if (v[k].x > planeMaxX)
                    {
                        planeMaxX = v[k].x;
                        if (haveUv) uAtMaxX = uv[k].x;
                    }
                    if (v[k].y < planeMinY)
                    {
                        planeMinY = v[k].y;
                        if (haveUv) vAtMinY = uv[k].y;
                    }
                    if (v[k].y > planeMaxY)
                    {
                        planeMaxY = v[k].y;
                        if (haveUv) vAtMaxY = uv[k].y;
                    }
                }
                planeZ /= v.Count;
                planeUGrowsX = uAtMaxX >= uAtMinX;
                planeVGrowsY = vAtMaxY >= vAtMinY;
                screenPlaneReady = planeMaxX - planeMinX > 0.01f && planeMaxY - planeMinY > 0.01f;
                return;
            }
        }

        private static void BuildMaterials()
        {
            bodyMat = MakePartMat(BuildNoiseTex(new Color32(148, 126, 100, 255), 8, "Fps3D_TermBody"));
            darkMat = MakePartMat(BuildNoiseTex(new Color32(28, 30, 36, 255), 5, "Fps3D_TermDark"));
            metalMat = MakePartMat(BuildBrushedTex(new Color32(184, 174, 140, 255), "Fps3D_TermMetal"));
            screenMat = new Material(ShaderDatabase.Transparent)
            {
                name = "Fps3D_TermScreen",
                mainTexture = BuildScreenTex(),
                renderQueue = 2905
            };
            screenTex = screenMat.mainTexture;

            mats.Clear();
            foreach (var g in groups)
                mats.Add(g == "body" ? bodyMat : g == "metal" ? metalMat
                     : g == "screen" ? screenMat : darkMat);

            Shader overlayShader = ShaderDatabase.TransparentPostLight ?? ShaderDatabase.Transparent;

            // 扫描亮带：竖向渐变亮条，Repeat 纵向滚动
            var scanTex = new Texture2D(4, 128, TextureFormat.RGBA32, false)
            { name = "Fps3D_TermScan", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 128; y++)
            {
                float k = Mathf.Clamp01(1f - Mathf.Abs(y - 96) / 32f);
                k = k * k;
                var c = new Color(0.65f, 0.95f, 1f, 0.55f * k);
                for (int x = 0; x < 4; x++) scanTex.SetPixel(x, y, c);
            }
            scanTex.Apply(false, false);
            scanMat = new Material(overlayShader) { name = "Fps3D_TermScanMat", renderQueue = 3110 };
            scanMat.mainTexture = scanTex;

            // 启动渐亮遮罩：全黑面片按 1-glow 淡出
            var blackTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Fps3D_TermBlack" };
            blackTex.SetPixel(0, 0, Color.black);
            blackTex.Apply(false, false);
            maskMat = new Material(overlayShader) { name = "Fps3D_TermMaskMat", renderQueue = 3115 };
            maskMat.mainTexture = blackTex;

            // 主屏幕是 screen 组（已贴到内口 z=-0.0172）。扫描带和遮罩贴在它朝向相机的一侧（z 更负）。
            scanQuad = MakeQuad(0.288f, 0.202f, -0.0176f);
            maskQuad = MakeQuad(0.294f, 0.209f, -0.0182f);
        }

        private static Material MakePartMat(Texture2D tex)
        {
            var m = new Material(ShaderDatabase.Cutout) { name = "Fps3D_Term_" + tex.name };
            m.mainTexture = tex;
            return m;
        }

        private static Mesh MakeQuad(float w, float h, float z)
        {
            var m = new Mesh { name = "Fps3D_TermOverlayQuad" };
            float hw = w * 0.5f, hh = h * 0.5f;
            m.vertices = new[]
            {
                new Vector3(-hw, -hh, z), new Vector3(hw, -hh, z),
                new Vector3(-hw, hh, z), new Vector3(hw, hh, z)
            };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 }; // 顺时针绕序，法线 -Z 朝向相机
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Texture2D BuildNoiseTex(Color32 baseCol, int amplitude, string name)
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(20261010);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    int n = rng.Next(-amplitude, amplitude + 1);
                    t.SetPixel(x, y, new Color32(
                        (byte)Mathf.Clamp(baseCol.r + n, 0, 255),
                        (byte)Mathf.Clamp(baseCol.g + n, 0, 255),
                        (byte)Mathf.Clamp(baseCol.b + n, 0, 255), 255));
                }
            t.Apply(false, false);
            return t;
        }

        private static Texture2D BuildBrushedTex(Color32 baseCol, string name)
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false)
            { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(7712);
            for (int y = 0; y < S; y++)
            {
                int n = rng.Next(-10, 11);
                for (int x = 0; x < S; x++)
                    t.SetPixel(x, y, new Color32(
                        (byte)Mathf.Clamp(baseCol.r + n, 0, 255),
                        (byte)Mathf.Clamp(baseCol.g + n, 0, 255),
                        (byte)Mathf.Clamp(baseCol.b + n, 0, 255), 255));
            }
            t.Apply(false, false);
            return t;
        }

        // 终端启动界面：深蓝黑底 + 双层网格 + 同心带环 + 十字线 + 四角实心角标。
        // 全几何实心元素，无文字（全局规范：玩家可见文字仅限 Core 定义原文）。
        private static Texture2D BuildScreenTex()
        {
            const int W = 512, H = 384;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false)
            { name = "Fps3D_TermScreen", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var deep = new Color(0.015f, 0.045f, 0.07f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++) t.SetPixel(x, y, deep);

            var gridA = new Color(0.05f, 0.16f, 0.22f);
            var gridB = new Color(0.025f, 0.09f, 0.13f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (x % 64 == 0 || y % 64 == 0) t.SetPixel(x, y, gridA);
                    else if (x % 16 == 0 || y % 16 == 0) t.SetPixel(x, y, gridB);
                }

            int cx = W / 2, cy = H / 2;
            // 同心带环（实心带状，非细线轮廓）
            Band(t, cx, cy, 118, 128, new Color(0.10f, 0.42f, 0.52f));
            Band(t, cx, cy, 74, 79, new Color(0.06f, 0.28f, 0.36f));
            Band(t, cx, cy, 34, 38, new Color(0.12f, 0.50f, 0.60f));
            SolidDisc(t, cx, cy, 7, new Color(0.55f, 0.95f, 1f));
            // 径向刻度短线（每 45 度）
            for (int a = 0; a < 8; a++)
            {
                float rad = a * Mathf.PI / 4f;
                for (int r = 84; r <= 104; r++)
                {
                    int x = cx + Mathf.RoundToInt(Mathf.Cos(rad) * r);
                    int y = cy + Mathf.RoundToInt(Mathf.Sin(rad) * r);
                    t.SetPixel(x, y, new Color(0.10f, 0.40f, 0.50f));
                }
            }
            // 中央十字准线
            FillRect(t, 0, cy - 1, W, 2, new Color(0.08f, 0.30f, 0.38f));
            FillRect(t, cx - 1, 0, 2, H, new Color(0.08f, 0.30f, 0.38f));
            // 四角实心 L 角标
            var corner = new Color(0.35f, 0.80f, 0.90f);
            L(t, 18, 18, 40, 11, corner, false, false);
            L(t, W - 18, 18, 40, 11, corner, true, false);
            L(t, 18, H - 18, 40, 11, corner, false, true);
            L(t, W - 18, H - 18, 40, 11, corner, true, true);
            // 顶/底状态条（实心色块）
            FillRect(t, 26, 8, 60, 5, corner);
            FillRect(t, W - 86, 8, 60, 5, new Color(0.10f, 0.30f, 0.38f));
            FillRect(t, 26, H - 13, 90, 5, new Color(0.10f, 0.30f, 0.38f));
            // 边缘 vignette 渐变
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int d = Mathf.Min(Mathf.Min(x, W - 1 - x), Mathf.Min(y, H - 1 - y));
                    if (d < 22)
                    {
                        float k = d / 22f;
                        Color c = t.GetPixel(x, y) * Mathf.Lerp(0.35f, 1f, k);
                        t.SetPixel(x, y, c);
                    }
                }
            t.Apply(false, false);
            return t;
        }

        private static void Band(Texture2D t, int cx, int cy, int r0, int r1, Color c)
        {
            for (int y = cy - r1; y <= cy + r1; y++)
                for (int x = cx - r1; x <= cx + r1; x++)
                {
                    if (x < 0 || y < 0 || x >= t.width || y >= t.height) continue;
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (d >= r0 && d <= r1) t.SetPixel(x, y, c);
                }
        }

        private static void SolidDisc(Texture2D t, int cx, int cy, int r, Color c)
        {
            for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) t.SetPixel(x, y, c);
                }
        }

        private static void FillRect(Texture2D t, int x, int y, int w, int h, Color c)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                {
                    if (xx < 0 || yy < 0 || xx >= t.width || yy >= t.height) continue;
                    t.SetPixel(xx, yy, c);
                }
        }

        // 实心 L 形角标（两段实心矩形）
        private static void L(Texture2D t, int x, int y, int len, int thick, Color c, bool flipX, bool flipY)
        {
            int ox = flipX ? x - len : x;
            int oy = flipY ? y - thick : y;
            FillRect(t, ox, oy, len, thick, c);
            int vx = flipX ? x - thick : x;
            int vy = flipY ? y - len : y;
            FillRect(t, vx, vy, thick, len, c);
        }

        // ---- 回退盒体：OBJ 不可用时保底可用（机身盒 + 屏幕面）。
        //      产出与导入数据同构的 TermData，后续走同一条 BuildGroupMeshes 路径。 ----
        private static TermData BuildFallbackData()
        {
            var d = new TermData();

            int V(Vector3 p, Vector3 normal, Vector2 uv)
            {
                d.verts.Add(p);
                d.normals.Add(normal);
                d.uvs.Add(uv);
                return d.verts.Count - 1;
            }
            var bodyTris = new List<int>();
            void Quad(Vector3 a, Vector3 b2, Vector3 c, Vector3 e)
            {
                var normal = Vector3.Cross(b2 - a, c - a).normalized;
                int i0 = V(a, normal, new Vector2(0f, 0f));
                V(b2, normal, new Vector2(1f, 0f));
                V(c, normal, new Vector2(1f, 1f));
                V(e, normal, new Vector2(0f, 1f));
                bodyTris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            void Box(Vector3 min, Vector3 max)
            {
                Quad(new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z),
                     new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z)); // -Z
                Quad(new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                     new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)); // +Z
                Quad(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                     new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z)); // -X
                Quad(new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z),
                     new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z)); // +X
                Quad(new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z),
                     new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z)); // +Y
                Quad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                     new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z)); // -Y
            }

            Box(new Vector3(-0.18f, -0.135f, -0.016f), new Vector3(0.18f, 0.135f, 0.016f));
            d.names.Add("body");
            d.tris.Add(bodyTris);

            // 屏幕面（法线 -Z 朝向相机）
            var screenTris = new List<int>();
            int s0 = V(new Vector3(-0.145f, -0.1025f, -0.0165f), Vector3.back, new Vector2(0f, 0f));
            V(new Vector3(0.145f, -0.1025f, -0.0165f), Vector3.back, new Vector2(1f, 0f));
            V(new Vector3(-0.145f, 0.1025f, -0.0165f), Vector3.back, new Vector2(0f, 1f));
            V(new Vector3(0.145f, 0.1025f, -0.0165f), Vector3.back, new Vector2(1f, 1f));
            screenTris.AddRange(new[] { s0, s0 + 2, s0 + 1, s0 + 1, s0 + 2, s0 + 3 });
            d.names.Add("screen");
            d.tris.Add(screenTris);
            return d;
        }
    }
}
