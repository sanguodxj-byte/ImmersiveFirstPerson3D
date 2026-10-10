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
    public static class HandTerminal3D
    {
        // 抬起动画两端位姿（相机局部空间）：画外下方沉没 -> 眼前。
        // 到位后略放大、略抬高、更正对相机，屏幕占视野更大，边框不再压住画面中心。
        private static readonly Vector3 StartPos = new Vector3(0f, -0.50f, 0.46f);
        private static readonly Vector3 EndPos = new Vector3(0f, -0.06f, 0.36f);
        private const float StartPitch = 30f;
        private const float EndPitch = 6f;
        public const float PanelScale = 1.32f;

        private static bool loadTried;
        // 按材质组拆分的独立网格：Graphics.DrawMesh(mesh, mat) 只渲染 submesh 0，
        // 多 submesh 单材质逐个提交 = 全部重复画 submesh 0（实机踩坑：只见机身不见屏幕）。
        private static readonly List<Mesh> groupMeshes = new List<Mesh>();
        private static readonly List<string> groups = new List<string>();
        private static readonly List<Material> mats = new List<Material>();
        private static Material bodyMat, darkMat, metalMat, screenMat, scanMat, maskMat;
        private static Texture screenTex;
        private static Mesh screenQuad, scanQuad, maskQuad;

        // 调试冻结位姿（GABS 验收通道）：>=0 时按该进度持续绘制终端、不推进状态机。
        public static float DebugHold = -1f;

        public static bool Ready { get { EnsureLoaded(); return groupMeshes.Count > 0; } }

        public static void EnsureLoaded()
        {
            if (loadTried) return;
            loadTried = true;
            try
            {
                Mesh whole = null;
                var wholeGroups = new List<string>();
                var mod = ModLister.GetActiveModWithIdentifier(ModEntry.HarmonyId);
                if (mod != null)
                {
                    string path = System.IO.Path.Combine(mod.RootDir.FullName, "1.6", "Assets", "hand_terminal.obj");
                    var imported = ObjImporter3D.Load(path);
                    if (imported != null)
                    {
                        whole = imported.mesh;
                        wholeGroups.AddRange(imported.groups);
                        BakeVertexShading(whole, wholeGroups);
                    }
                }
                if (whole == null)
                {
                    whole = BuildFallbackMesh();
                    wholeGroups.Add("body");
                    wholeGroups.Add("screen");
                    Log.Warning(ModEntry.LogPrefix + "terminal OBJ missing, fallback box in use");
                }
                SplitByGroup(whole, wholeGroups);
                BuildMaterials();
            }
            catch (System.Exception e)
            {
                Log.Error(ModEntry.LogPrefix + "terminal load failed: " + e);
            }
        }

        // 屏幕正在显示俯视 RT（指挥面板），而不是自绘启动图。
        public static bool ShowingMap { get; private set; }

        // 查看期把屏幕换成俯视 RT。传 null 恢复自绘 UI。
        // Cutoff 降到 0：RT 里透明纹素的 alpha 不能把屏幕四边形抠穿。
        public static void SetScreenTexture(Texture tex)
        {
            EnsureLoaded();
            ShowingMap = tex != null;
            if (screenMat == null) return;
            screenMat.mainTexture = tex != null ? tex : screenTex;
            screenMat.SetFloat("_Cutoff", tex != null ? 0f : 0.5f);
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

        // 屏幕四边形在相机局部 z=0.0184，半宽 0.145、半高 0.1025。UV 左下为 (0,0)。
        // 半幅是模型坐标，不乘 PanelScale：逆矩阵已经除掉缩放。
        public static bool TryScreenUV(Camera cam, float raise, out Vector2 uv)
        {
            uv = default;
            if (cam == null) return false;
            Matrix4x4 world = CameraPose(cam, raise, out _);
            Matrix4x4 inv = world.inverse;
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            Vector3 o = inv.MultiplyPoint(ray.origin);
            Vector3 d = inv.MultiplyVector(ray.direction);
            const float z = 0.0184f;
            const float halfW = 0.145f;
            const float halfH = 0.1025f;
            if (Mathf.Abs(d.z) < 1e-6f) return false;
            float dist = (z - o.z) / d.z;
            if (dist < 0f) return false;
            Vector3 hit = o + d * dist;
            if (Mathf.Abs(hit.x) > halfW || Mathf.Abs(hit.y) > halfH) return false;
            uv = new Vector2(hit.x / (halfW * 2f) + 0.5f, hit.y / (halfH * 2f) + 0.5f);
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

            // 屏幕三层均由程序 quad 承载（OBJ 不含屏幕面）：
            // 主屏幕（终端 UI 纹理）-> 扫描亮带滚动 -> 启动渐亮遮罩
            if (screenQuad != null && glow > 0f)
            {
                if (screenMat != null) Graphics.DrawMesh(screenQuad, world, screenMat, 0);
                if (scanQuad != null && scanMat != null)
                {
                    scanMat.mainTextureOffset = new Vector2(0f, (Time.time * 0.26f) % 1f);
                    Color c = scanMat.color;
                    // 地图和原版界面叠在屏幕上时，扫描带只留一层薄光，避免罩住文字。
                    c.a = (ShowingMap ? 0.12f : 0.55f) * glow;
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

        // ---- 顶点明暗烘焙：固定光向的 diffuse+ambient 写进顶点色，
        //      RimWorld 无光照 shader 下以此保住体积感；screen 组恒白。----
        private static void BakeVertexShading(Mesh src, List<string> grp)
        {
            var lightDir = new Vector3(0.25f, 0.7f, 0.65f).normalized;
            var colors = new Color[src.vertexCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            var claimed = new int[src.vertexCount];
            var normals = src.normals;

            for (int s = 0; s < src.subMeshCount; s++)
            {
                bool isScreen = s < grp.Count && grp[s] == "screen";
                var tris = src.GetTriangles(s);
                foreach (int vi in tris)
                {
                    if (claimed[vi] == 1) continue;
                    claimed[vi] = 1;
                    if (isScreen) { colors[vi] = Color.white; continue; }
                    float n = Mathf.Max(0f, Vector3.Dot(normals[vi], lightDir));
                    float b = 0.52f + 0.48f * n;
                    colors[vi] = new Color(b, b, b, 1f);
                }
            }
            src.colors = colors;
        }

        // 多 submesh 整网按材质组拆独立 Mesh（顶点重映射，仅保留组内使用的顶点）。
        private static void SplitByGroup(Mesh src, List<string> grp)
        {
            var v = src.vertices;
            var uv = src.uv;
            var nrm = src.normals;
            var col = src.colors;
            groupMeshes.Clear();
            groups.Clear();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var tris = src.GetTriangles(s);
                if (tris.Length == 0) continue;
                var map = new Dictionary<int, int>(tris.Length);
                var nv = new List<Vector3>(tris.Length);
                var nuv = new List<Vector2>(tris.Length);
                var nn = new List<Vector3>(tris.Length);
                var nc = new List<Color>(tris.Length);
                var nt = new int[tris.Length];
                for (int i = 0; i < tris.Length; i++)
                {
                    int old = tris[i];
                    if (!map.TryGetValue(old, out int ni))
                    {
                        ni = nv.Count;
                        map[old] = ni;
                        nv.Add(v[old]);
                        nuv.Add(uv.Length > old ? uv[old] : Vector2.zero);
                        nn.Add(nrm.Length > old ? nrm[old] : Vector3.up);
                        nc.Add(col.Length > old ? col[old] : Color.white);
                    }
                    nt[i] = ni;
                }
                var m = new Mesh { name = "Fps3D_Term_" + (s < grp.Count ? grp[s] : s.ToString()) };
                m.SetVertices(nv);
                m.SetUVs(0, nuv);
                m.SetNormals(nn);
                m.SetColors(nc);
                m.triangles = nt;
                m.RecalculateBounds();
                groupMeshes.Add(m);
                groups.Add(s < grp.Count ? grp[s] : "");
            }
        }

        private static void BuildMaterials()
        {
            bodyMat = MakePartMat(BuildNoiseTex(new Color32(148, 126, 100, 255), 8, "Fps3D_TermBody"));
            darkMat = MakePartMat(BuildNoiseTex(new Color32(28, 30, 36, 255), 5, "Fps3D_TermDark"));
            metalMat = MakePartMat(BuildBrushedTex(new Color32(184, 174, 140, 255), "Fps3D_TermMetal"));
            screenMat = MakePartMat(BuildScreenTex());
            screenTex = screenMat.mainTexture;

            mats.Clear();
            foreach (var g in groups)
                mats.Add(g == "body" ? bodyMat : g == "metal" ? metalMat
                     : g == "screen" ? screenMat : darkMat);

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
            scanMat = new Material(ShaderDatabase.Transparent) { name = "Fps3D_TermScanMat" };
            scanMat.mainTexture = scanTex;

            // 启动渐亮遮罩：全黑面片按 1-glow 淡出
            var blackTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Fps3D_TermBlack" };
            blackTex.SetPixel(0, 0, Color.black);
            blackTex.Apply(false, false);
            maskMat = new Material(ShaderDatabase.Transparent) { name = "Fps3D_TermMaskMat" };
            maskMat.mainTexture = blackTex;

            // 屏幕三层 quad（贴边框内口，z 递增 0.6mm 防层间穿插）：
            // 主屏幕 0.0184 / 扫描带 0.0190 / 渐亮遮罩 0.0196
            screenQuad = MakeQuad(0.29f, 0.205f, 0.0184f);
            scanQuad = MakeQuad(0.288f, 0.202f, 0.0190f);
            maskQuad = MakeQuad(0.294f, 0.209f, 0.0196f);
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
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 }; // 法线 +Z 朝相机
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

        // ---- 回退盒体：OBJ 不可用时保底可用（机身盒 + 屏幕面） ----
        private static Mesh BuildFallbackMesh()
        {
            var b = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();
            var uvsList = new List<Vector2>();

            void Quad(Vector3 a, Vector3 b2, Vector3 c, Vector3 d)
            {
                int i0 = b.Count;
                b.AddRange(new[] { a, b2, c, d });
                var normal = Vector3.Cross(b2 - a, d - a).normalized;
                for (int k = 0; k < 4; k++) n.Add(normal);
                uvsList.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f) });
                tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0 + 1, i0 + 2, i0 + 3 });
            }
            void Box(Vector3 min, Vector3 max)
            {
                Quad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                     new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z)); // -Z
                Quad(new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                     new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z)); // +Z
                Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z),
                     new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z)); // -X
                Quad(new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
                     new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z)); // +X
                Quad(new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z),
                     new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)); // +Y
                Quad(new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                     new Vector3(max.x, min.y, min.z), new Vector3(min.x, min.y, min.z)); // -Y
            }

            Box(new Vector3(-0.18f, -0.135f, -0.016f), new Vector3(0.18f, 0.135f, 0.016f));
            int bodyCount = tris.Count;
            // 屏幕面（法线 +Z）
            int s0 = b.Count;
            b.AddRange(new[]
            {
                new Vector3(-0.145f, -0.1025f, 0.0165f), new Vector3(0.145f, -0.1025f, 0.0165f),
                new Vector3(-0.145f, 0.1025f, 0.0165f), new Vector3(0.145f, 0.1025f, 0.0165f)
            });
            for (int k = 0; k < 4; k++) n.Add(Vector3.forward);
            uvsList.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f) });
            tris.AddRange(new[] { s0, s0 + 2, s0 + 1, s0 + 1, s0 + 2, s0 + 3 });

            var m = new Mesh { name = "Fps3D_TermFallback" };
            m.SetVertices(b);
            m.SetNormals(n);
            m.SetUVs(0, uvsList);
            m.subMeshCount = 2;
            var bodyArr = new int[bodyCount]; tris.CopyTo(0, bodyArr, 0, bodyCount);
            m.SetTriangles(bodyArr, 0);
            m.SetTriangles(new[] { s0, s0 + 2, s0 + 1, s0 + 1, s0 + 2, s0 + 3 }, 1);
            m.RecalculateBounds();
            // 顶点色明暗（屏幕组恒白）
            var lightDir = new Vector3(0.25f, 0.7f, 0.65f).normalized;
            var colors = new Color[b.Count];
            for (int i = 0; i < b.Count; i++)
            {
                if (i >= s0) { colors[i] = Color.white; continue; }
                float d = Mathf.Max(0f, Vector3.Dot(n[i], lightDir));
                float br = 0.52f + 0.48f * d;
                colors[i] = new Color(br, br, br, 1f);
            }
            m.colors = colors;
            return m;
        }
    }
}
