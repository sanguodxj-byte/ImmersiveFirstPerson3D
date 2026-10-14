using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 P0：引擎能力探针
    // ============================================================
    //
    // Waylith（1.7）不是原装 Unity：GodView3D 只证实了「停用的正交副相机 → RT」。
    // 泛光（L4-B）与镜面反射（L6-A）要的更多，在实机上一次性量清楚，写进设置（持久化），
    // 由 Shaderpack3D.Caps 门控。没测过（-1）一律当不可用 —— 保底方案永远在。
    //
    // 五项：
    //   1 主相机 targetTexture=RT 后 Render()，RT 里有没有画面、主相机还原后是否正常
    //   2 Graphics.Blit 半尺寸缩放是否双线性（棋盘格缩一半应得到均匀灰）
    //   3 引擎雾 API 是否存在
    //   4 反向深度斜裁剪投影是否被遵守
    //   5 ShaderDatabase 全量：混合模式（不透明 / alpha / 加法）与是否乘顶点色
    //
    // 探针用 layer 30 与一台专用停用相机，只在 DevMode 动作或 cmdAction "probeCaps" 时跑，不进每帧。
    public static class RenderProbe3D
    {
        private const int ProbeLayer = 30;
        private const int Px = 32;

        private static Camera probeCam;
        private static RenderTexture probeRT;

        public static string Run()
        {
            var sb = new StringBuilder(2048);
            var s = FirstPerson3DMod.settings;
            var previousActive = RenderTexture.active;
            sb.Append('{');
            try
            {
                EnsureCam();
                int camRT = Safe(ProbeMainCamRT, out string d1);
                int blit = Safe(ProbeBlitBilinear, out string d2);
                int fog = Safe(ProbeFog, out string d3);
                int obl = Safe(ProbeOblique, out string d4);
                sb.Append("\"mainCamRT\":").Append(camRT).Append(",\"mainCamRTNote\":\"").Append(Esc(d1)).Append('"');
                sb.Append(",\"blitBilinear\":").Append(blit).Append(",\"blitNote\":\"").Append(Esc(d2)).Append('"');
                sb.Append(",\"engineFog\":").Append(fog).Append(",\"fogNote\":\"").Append(Esc(d3)).Append('"');
                sb.Append(",\"oblique\":").Append(obl).Append(",\"obliqueNote\":\"").Append(Esc(d4)).Append('"');
                sb.Append(",\"shaders\":[");
                ProbeShaders(sb);
                sb.Append(']');
                if (s != null)
                {
                    s.capsMainCamRT = camRT;
                    s.capsBlitBilinear = blit;
                    s.capsEngineFog = fog;
                    s.capsOblique = obl;
                }
            }
            catch (Exception e)
            {
                sb.Append("\"error\":\"").Append(Esc(e.GetType().Name + ": " + e.Message)).Append('"');
            }
            finally
            {
                Cleanup();
                RenderTexture.active = previousActive;
            }
            sb.Append('}');
            string j = sb.ToString();
            if (s != null)
            {
                s.renderCaps = j;
                try { s.Write(); } catch { }
            }
            Log.Message(ModEntry.LogPrefix + "RENDERCAPS " + j);
            return j;
        }

        private delegate int ProbeFn(out string note);

        private static int Safe(ProbeFn f, out string note)
        {
            try { return f(out note); }
            catch (Exception e) { note = "threw " + e.GetType().Name + ": " + e.Message; return 0; }
        }

        // ------------------------------------------------------------

        private static void EnsureCam()
        {
            if (probeRT == null)
            {
                probeRT = new RenderTexture(Px, Px, 16, RenderTextureFormat.ARGB32) { name = "Fps3D_ProbeRT" };
                probeRT.Create();
            }
            if (probeCam == null)
            {
                var go = new GameObject("Fps3D_ProbeCam");
                UnityEngine.Object.DontDestroyOnLoad(go);
                probeCam = go.AddComponent<Camera>();
                probeCam.enabled = false;
            }
            probeCam.orthographic = false;
            probeCam.fieldOfView = 40f;
            probeCam.nearClipPlane = 0.1f;
            probeCam.farClipPlane = 500f;
            probeCam.cullingMask = 1 << ProbeLayer;
            probeCam.clearFlags = CameraClearFlags.SolidColor;
            probeCam.backgroundColor = Color.black;
            probeCam.targetTexture = probeRT;
            probeCam.transform.position = new Vector3(-5000f, 50f, -5000f);
            probeCam.transform.rotation = Quaternion.identity;
            probeCam.ResetProjectionMatrix();
        }

        private static void Cleanup()
        {
            try
            {
                if (probeCam != null)
                {
                    probeCam.targetTexture = null;
                    UnityEngine.Object.Destroy(probeCam.gameObject);
                }
            }
            catch { }
            probeCam = null;
            try { if (probeRT != null) { probeRT.Release(); UnityEngine.Object.Destroy(probeRT); } } catch { }
            probeRT = null;
        }

        private static Color ReadCenter(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(rt.width / 2, rt.height / 2, 1, 1), 0, 0);
                tex.Apply();
                var c = tex.GetPixel(0, 0);
                UnityEngine.Object.Destroy(tex);
                return c;
            }
            finally { RenderTexture.active = prev; }
        }

        private static Matrix4x4 InFront(float dist, float size)
        {
            var p = probeCam.transform.position + probeCam.transform.forward * dist;
            // BillboardQuad 底边锚定在 0，竖直朝 -Z；朝向相机（相机看 +Z）
            return Matrix4x4.TRS(p - new Vector3(0f, size * 0.5f, 0f), Quaternion.Euler(0f, 180f, 0f), new Vector3(size, size, 1f));
        }

        private static Color RenderQuad(Material m, float dist, Color bg, Color32 vcol)
        {
            probeCam.backgroundColor = bg;
            var mesh = ColoredQuad(vcol);
            Graphics.DrawMesh(mesh, InFront(dist, dist * 2f), m, ProbeLayer, probeCam);
            probeCam.Render();
            UnityEngine.Object.Destroy(mesh);
            return ReadCenter(probeRT);
        }

        private static Mesh ColoredQuad(Color32 c)
        {
            var src = Util3D.BillboardQuad;
            var m = new Mesh { name = "Fps3D_ProbeQuad" };
            m.vertices = src.vertices;
            m.uv = src.uv;
            m.triangles = DoubleSided(src.triangles);
            var cols = new Color32[src.vertexCount];
            for (int i = 0; i < cols.Length; i++) cols[i] = c;
            m.colors32 = cols;
            m.RecalculateBounds();
            return m;
        }

        private static int[] DoubleSided(int[] t)
        {
            var r = new int[t.Length * 2];
            for (int i = 0; i < t.Length; i += 3)
            {
                r[i] = t[i]; r[i + 1] = t[i + 1]; r[i + 2] = t[i + 2];
                r[t.Length + i] = t[i]; r[t.Length + i + 1] = t[i + 2]; r[t.Length + i + 2] = t[i + 1];
            }
            return r;
        }

        // 1 ------------------------------------------------------------
        private static int ProbeMainCamRT(out string note)
        {
            var cam = CameraRig3D.Cam;
            if (cam == null) { note = "no main camera"; return 0; }
            var rt = RenderTexture.GetTemporary(64, 64, 16, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Color c;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                c = ReadCenter(rt);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.ReleaseTemporary(rt);
            }
            sw.Stop();
            bool restored = cam.targetTexture == prevTarget;
            float l = c.r + c.g + c.b;
            note = "center=" + c + " ms=" + sw.Elapsed.TotalMilliseconds.ToString("0.00") + " restored=" + restored;
            return (l > 0.02f && restored) ? 1 : 0;
        }

        // 2 ------------------------------------------------------------
        private static int ProbeBlitBilinear(out string note)
        {
            var src = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[64];
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    px[y * 8 + x] = ((x + y) & 1) == 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
            src.SetPixels32(px);
            src.Apply();
            var half = RenderTexture.GetTemporary(4, 4, 0, RenderTextureFormat.ARGB32);
            half.filterMode = FilterMode.Bilinear;
            Color c;
            try
            {
                Graphics.Blit(src, half);
                c = ReadCenter(half);
            }
            finally
            {
                RenderTexture.ReleaseTemporary(half);
                UnityEngine.Object.Destroy(src);
            }
            // 双线性：4x4 的采样点正好落在 2x2 块中心，四格平均 = 0.5 灰；点采样只会是 0 或 1
            note = "center=" + c;
            return (c.r > 0.3f && c.r < 0.7f) ? 1 : 0;
        }

        // 3 ------------------------------------------------------------
        private static int ProbeFog(out string note)
        {
            note = "unsupported (RenderSettings/FogMode absent)";
            return 0;
        }

        // 4 ------------------------------------------------------------
        private static int ProbeOblique(out string note)
        {
            Shader sh = null;
            try { sh = ShaderDatabase.Cutout; } catch { }
            if (sh == null) { note = "no Cutout shader"; return 0; }
            var m = new Material(sh) { mainTexture = Util3D.WhiteTex, color = Color.white };
            Color plain, clipped, retained;
            try
            {
                probeCam.ResetProjectionMatrix();
                plain = RenderQuad(m, 10f, Color.black, new Color32(255, 255, 255, 255));
                // 相机空间里 z = 20 的裁剪面（法线朝前）：10 m 处的片应被裁掉
                var plane = new Vector4(0f, 0f, -1f, -20f);
                if (!WaterMath.TryOblique(probeCam.projectionMatrix, plane, out var projection))
                {
                    note = "invalid reverse-depth projection";
                    return 0;
                }
                probeCam.projectionMatrix = projection;
                clipped = RenderQuad(m, 10f, Color.black, new Color32(255, 255, 255, 255));
                retained = RenderQuad(m, 30f, Color.black, new Color32(255, 255, 255, 255));
            }
            finally
            {
                probeCam.ResetProjectionMatrix();
                UnityEngine.Object.Destroy(m);
            }
            note = "plain=" + plain + " clipped=" + clipped + " retained=" + retained;
            return (plain.r > 0.5f && clipped.r < 0.1f && retained.r > 0.5f) ? 1 : 0;
        }

        // 5 ------------------------------------------------------------
        // 灰底 0.5 上画一片：贴图白、材质色 (1,1,1,0.5)、顶点色 (1,0,0)
        //   alpha 混合 + 乘顶点色 -> r 0.75 g 0.25；不乘 -> g 0.75
        //   加法 + 乘顶点色 -> r 1 g 0.5；不乘 -> g 1
        //   不透明 + 乘顶点色 -> r 1 g 0；不乘 -> g 1（与加法不乘同值，用 r=1,g=1 合并判为 opaque|additive）
        private static void ProbeShaders(StringBuilder sb)
        {
            bool first = true;
            var props = typeof(ShaderDatabase).GetProperties(BindingFlags.Public | BindingFlags.Static);
            var fields = typeof(ShaderDatabase).GetFields(BindingFlags.Public | BindingFlags.Static);
            void One(string name, Shader sh)
            {
                if (sh == null) return;
                string blend = "?";
                bool vcol = false;
                Color c = Color.clear;
                try
                {
                    var m = new Material(sh) { mainTexture = Util3D.WhiteTex, color = new Color(1f, 1f, 1f, 0.5f) };
                    c = RenderQuad(m, 10f, new Color(0.5f, 0.5f, 0.5f, 1f), new Color32(255, 0, 0, 255));
                    UnityEngine.Object.Destroy(m);
                    if (Near(c.r, 0.75f) && (Near(c.g, 0.25f) || Near(c.g, 0.75f))) { blend = "alpha"; vcol = Near(c.g, 0.25f); }
                    else if (c.r > 0.9f && Near(c.g, 0.5f)) { blend = "additive"; vcol = true; }
                    else if (c.r > 0.9f && c.g < 0.1f) { blend = "opaque"; vcol = true; }
                    else if (c.r > 0.9f && c.g > 0.9f) { blend = "opaque|additive"; vcol = false; }
                    else if (Near(c.r, 0.5f) && Near(c.g, 0.5f)) blend = "invisible";
                }
                catch (Exception e) { blend = "err:" + e.GetType().Name; }
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"n\":\"").Append(Esc(name)).Append("\",\"blend\":\"").Append(blend)
                  .Append("\",\"vcol\":").Append(vcol ? 1 : 0)
                  .Append(",\"rgb\":[").Append(F(c.r)).Append(',').Append(F(c.g)).Append(',').Append(F(c.b)).Append("]}");
            }
            foreach (var p in props)
            {
                if (p.PropertyType != typeof(Shader)) continue;
                Shader sh = null;
                try { sh = p.GetValue(null, null) as Shader; } catch { }
                One(p.Name, sh);
            }
            foreach (var f in fields)
            {
                if (f.FieldType != typeof(Shader)) continue;
                Shader sh = null;
                try { sh = f.GetValue(null) as Shader; } catch { }
                One(f.Name, sh);
            }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.08f;
        private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        private static string Esc(string s) => s == null ? "" : s.Replace("\\", "/").Replace("\"", "'");
    }
}
