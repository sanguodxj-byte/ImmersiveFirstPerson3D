using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L4-B：真泛光（P0 第 1、2 项都过才开，Ultra 档）
    // ============================================================
    //
    // 一台跟随主相机的副相机只看 BloomLayer：发光件（灯头光晕、火舌、眩光）各自多提交一份到这层，
    // 墙/山 chunk 用纯黑材质也提交一份当遮挡体（只画合批 chunk，不画逐件对象，控制 draw 数）。
    // 1/4 分辨率渲完 → 连续 3 次半尺寸 Blit（P0 证实双线性 = 免着色器的盒式模糊）→ 最小那张拉满屏
    // 用 MoteGlow 加回主画面，再叠一份 1/4 的做内核。帧时间超 1.0 ms（平滑后）本会话关掉，退回 A 案。
    public static class Bloom3D
    {
        public const int BloomLayer = 29;
        private const float BudgetMs = 1.0f;

        private static Camera bloomCam;
        private static RenderTexture rtQuarter;
        private static readonly RenderTexture[] chain = new RenderTexture[3];
        private static Material blackMat, compositeMat, coreMat;
        private static bool failed;
        private static float smoothMs;
        private static Camera boundMain;
        private static int originalMask;

        public static bool Active => Shaderpack3D.Bloom && !failed && bloomCam != null;

        /// <summary>发光件多提交一份到泛光层（Ensure 过才有相机；没开就什么都不做）。</summary>
        public static void Emit(Mesh mesh, Matrix4x4 m, Material mat)
        {
            if (!Active || mesh == null || mat == null) return;
            Graphics.DrawMesh(mesh, m, mat, BloomLayer, bloomCam);
        }

        /// <summary>遮挡体（墙/山 chunk）黑色提交：被墙挡住的灯不往外溢光。</summary>
        public static void Occluder(Mesh mesh, Matrix4x4 m)
        {
            if (!Active || mesh == null || blackMat == null) return;
            Graphics.DrawMesh(mesh, m, blackMat, BloomLayer, bloomCam);
        }

        /// <summary>整帧层开头：把相机摆好（之后各层的 Emit 才有目标）。下一次 Render 用的是这一帧的提交。</summary>
        public static void Prepare(Camera main)
        {
            if (!Shaderpack3D.Bloom || failed || main == null) { Release(); return; }
            try
            {
                Ensure(main);
                if (boundMain != main)
                {
                    RestoreMask();
                    boundMain = main;
                    originalMask = main.cullingMask;
                }
                bloomCam.CopyFrom(main);
                bloomCam.enabled = false;
                bloomCam.cullingMask = 1 << BloomLayer;
                bloomCam.clearFlags = CameraClearFlags.SolidColor;
                bloomCam.backgroundColor = Color.black;
                bloomCam.targetTexture = rtQuarter;
                // 主相机永远不看泛光层
                main.cullingMask &= ~(1 << BloomLayer);
            }
            catch (System.Exception e) { Fail("prepare", e); }
        }

        /// <summary>整帧层末尾：渲染、模糊、合成。</summary>
        public static void RenderAndComposite(Camera main)
        {
            if (!Active || main == null) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Shaderpack3D.Begin();
            var previousActive = RenderTexture.active;
            try
            {
                bloomCam.Render();
                Graphics.Blit(rtQuarter, chain[0]);
                Graphics.Blit(chain[0], chain[1]);
                Graphics.Blit(chain[1], chain[2]);
                var st = FirstPerson3DMod.settings;
                float k = st != null ? Mathf.Clamp(st.bloomIntensity, 0f, 2f) : ShaderpackDefaults.BloomIntensity;
                compositeMat.mainTexture = chain[2];
                compositeMat.color = new Color(k, k, k, 1f);
                ScreenFx3D.DrawFullscreen(main, ScreenFx3D.ScreenQuad, compositeMat);
                coreMat.mainTexture = chain[0];
                coreMat.color = new Color(k * 0.45f, k * 0.45f, k * 0.45f, 1f);
                ScreenFx3D.DrawFullscreen(main, ScreenFx3D.ScreenQuad, coreMat);
            }
            catch (System.Exception e) { Fail("render", e); }
            finally { RenderTexture.active = previousActive; Shaderpack3D.End(); }
            sw.Stop();
            smoothMs = Mathf.Lerp(smoothMs, (float)sw.Elapsed.TotalMilliseconds, 0.05f);
            if (smoothMs > BudgetMs * 1.5f)
            {
                failed = true;
                Release();
                Log.Message(ModEntry.LogPrefix + "F016 bloom over budget (" + smoothMs.ToString("0.00") + " ms), falling back to sprite bloom");
            }
        }

        private static void Ensure(Camera main)
        {
            int w = Mathf.Max(64, Screen.width / 4), h = Mathf.Max(64, Screen.height / 4);
            if (rtQuarter == null || rtQuarter.width != w || rtQuarter.height != h)
            {
                Release();
                rtQuarter = NewRT(w, h, 16, "Fps3D_BloomQ");
                for (int i = 0; i < chain.Length; i++)
                {
                    w = Mathf.Max(8, w / 2); h = Mathf.Max(8, h / 2);
                    chain[i] = NewRT(w, h, 0, "Fps3D_Bloom" + i);
                }
            }
            if (bloomCam == null)
            {
                var go = new GameObject("Fps3D_BloomCam");
                Object.DontDestroyOnLoad(go);
                bloomCam = go.AddComponent<Camera>();
                bloomCam.enabled = false;
            }
            if (blackMat == null)
            {
                Shader op = null;
                try { op = ShaderDatabase.Cutout; } catch { }
                blackMat = new Material(op) { name = "Fps3D_BloomOccluder", mainTexture = Util3D.WhiteTex, color = Color.black };
            }
            if (compositeMat == null)
            {
                Shader add = null;
                try { add = ShaderDatabase.MoteGlow; } catch { }
                compositeMat = new Material(add) { name = "Fps3D_BloomComposite", renderQueue = 3980 };
                coreMat = new Material(add) { name = "Fps3D_BloomCore", renderQueue = 3981 };
            }
        }

        private static RenderTexture NewRT(int w, int h, int depth, string name)
        {
            var rt = new RenderTexture(w, h, depth, RenderTextureFormat.ARGB32) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            rt.Create();
            return rt;
        }

        private static void RestoreMask()
        {
            if (boundMain != null) boundMain.cullingMask = originalMask;
            boundMain = null;
        }

        public static void Release()
        {
            RestoreMask();
            if (bloomCam != null)
            {
                bloomCam.targetTexture = null;
                Object.Destroy(bloomCam.gameObject);
                bloomCam = null;
            }
            if (compositeMat != null) compositeMat.mainTexture = null;
            if (coreMat != null) coreMat.mainTexture = null;
            if (rtQuarter != null) { rtQuarter.Release(); Object.Destroy(rtQuarter); rtQuarter = null; }
            for (int i = 0; i < chain.Length; i++)
                if (chain[i] != null) { chain[i].Release(); Object.Destroy(chain[i]); chain[i] = null; }
        }

        private static void Fail(string where, System.Exception e)
        {
            failed = true;
            Release();
            Log.WarningOnce(ModEntry.LogPrefix + "F016 bloom " + where + " failed, falling back: " + e.Message, 0x1F0164);
        }

        public static void ResetSession()
        {
            Release();
            if (blackMat != null) Object.Destroy(blackMat);
            if (compositeMat != null) Object.Destroy(compositeMat);
            if (coreMat != null) Object.Destroy(coreMat);
            blackMat = compositeMat = coreMat = null;
            failed = false;
            smoothMs = 0f;
        }
    }
}
