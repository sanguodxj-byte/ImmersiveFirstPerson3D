using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L9：屏幕层（全屏片贴在相机前，TransparentPostLight / MoteGlow）
    // ============================================================
    public static partial class ScreenFx3D
    {
        private const float PlaneDist = 0.35f;
        private static Mesh screenQuad;

        public static Mesh ScreenQuad
        {
            get
            {
                if (screenQuad != null) return screenQuad;
                var m = new Mesh { name = "Fps3D_ScreenQuad" };
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
                m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
                m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
                m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
                m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
                screenQuad = m;
                return m;
            }
        }

        /// <summary>把一片全屏面片贴在相机近平面前（略大于视口，盖满四角）。</summary>
        public static void DrawFullscreen(Camera cam, Mesh quad, Material mat)
        {
            if (cam == null || quad == null || mat == null) return;
            float h = 2f * PlaneDist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
            float w = h * Mathf.Max(0.1f, cam.aspect);
            var t = cam.transform;
            Graphics.DrawMesh(quad, Matrix4x4.TRS(t.position + t.forward * PlaneDist, t.rotation, new Vector3(w, h, 1f)), mat, 0);
        }
    }
}

namespace RimFirstPerson3D
{
    using UnityEngine;
    using Verse;

    public static partial class ScreenFx3D
    {
        private static float prevVis = -1f;
        private static float changeAt = -99f;
        private static float changeSign;
        private static bool underwater;
        private static Texture2D vignette;
        private static int vigW, vigH;

        /// <summary>每帧（整帧层）更新状态：室内外跳变、水下。</summary>
        public static void Tick(Camera cam)
        {
            underwater = false;
            Atmosphere3D.DensityMul = 1f;
            if (!Shaderpack3D.ScreenFx || cam == null) { prevVis = -1f; return; }
            var map = Find.CurrentMap;
            var sc = map != null ? SceneRegistry.Find(map) : null;
            if (sc == null || sc.Light == null) return;
            var local = cam.transform.position - sc.worldOffset;
            float vis = sc.Light.SkyVisAt(local.x, local.z);
            float now = Time.realtimeSinceStartup;
            if (prevVis >= 0f)
            {
                float j = ScreenFxMath.Jump(prevVis, vis);
                if (j != 0f) { changeAt = now; changeSign = j; }
            }
            if (prevVis < 0f || Mathf.Abs(vis - prevVis) >= 0.5f) prevVis = vis;
            else prevVis = Mathf.Lerp(prevVis, vis, 0.05f);

            var cell = new IntVec3(Mathf.FloorToInt(local.x), 0, Mathf.FloorToInt(local.z));
            if (cell.InBounds(map) && WaterLayer3D.SinkAt(map, cell) > 0f
                && cam.transform.position.y < sc.baseY - Metrics3D.WaterSurfaceDrop)
            {
                underwater = true;
                Atmosphere3D.DensityMul = 4f;
            }
        }

        /// <summary>OnGUI 里、受击暗角与准星之前画：适应闪、常驻暗角、水下色调。</summary>
        public static void DrawGUI()
        {
            if (!Shaderpack3D.ScreenFx || Event.current == null || Event.current.type != EventType.Repaint) return;
            var full = new Rect(0f, 0f, UI.screenWidth, UI.screenHeight);
            var prev = GUI.color;
            try
            {
                float now = Time.realtimeSinceStartup;
                float dur = changeSign > 0f ? ShaderpackDefaults.AdaptBrightSec : ShaderpackDefaults.AdaptDarkSec;
                float a = ScreenFxMath.Adapt(now - changeAt, dur, changeSign);
                if (a > 0.004f)
                {
                    GUI.color = new Color(1f, 0.98f, 0.93f, a * 0.55f);
                    GUI.DrawTexture(full, BaseContent.WhiteTex);
                }
                else if (a < -0.004f)
                {
                    GUI.color = new Color(0f, 0f, 0f, -a * 0.65f);
                    GUI.DrawTexture(full, BaseContent.WhiteTex);
                }

                if (underwater)
                {
                    GUI.color = new Color(0.10f, 0.32f, 0.36f, 0.42f);
                    GUI.DrawTexture(full, BaseContent.WhiteTex);
                }

                float va = ScreenFxMath.BaseVignette(ShaderpackDefaults.VignetteBase, HitFx3D.LastVignette);
                if (va > 0.004f)
                {
                    EnsureVignette();
                    if (vignette != null)
                    {
                        GUI.color = new Color(0f, 0f, 0f, va);
                        GUI.DrawTexture(full, vignette, ScaleMode.StretchToFill, true);
                    }
                }
            }
            finally { GUI.color = prev; }
        }

        private static void EnsureVignette()
        {
            int w = Mathf.Max(32, UI.screenWidth / 4), h = Mathf.Max(32, UI.screenHeight / 4);
            if (vignette != null && vigW == w && vigH == h) return;
            if (vignette != null) Object.Destroy(vignette);
            vignette = ProcTex3D.Vignette(w, h, 0.55f, 1.6f);
            vigW = w; vigH = h;
        }
    }
}
