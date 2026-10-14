using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L6：水面（B 天色反射 + 菲涅尔 + 太阳亮带；A 平面镜面反射，P0 门控）
    // ============================================================
    //
    // B 案：相机 30 m 内的水面 chunk，每 0.1 s 把颜色整份写进顶点色（WaterMath.Shade），
    //       换一份「材质色为白」的克隆材质画，这样顶点色能比水体原色更亮（反射天空）。
    //       出了 30 m 把烘好的光照色写回去，换回原材质。远处保持 F013 原样。
    // A 案：Ultra 档且 P0 证实主相机能进 RT + 斜裁剪生效时，反射相机按水面镜像主相机，
    //       半分辨率渲染进 RT；近处水面另画一层贴这张 RT（UV = 顶点在主相机的视口坐标），
    //       alpha 随菲涅尔。斜裁剪去掉水下几何。绕序镜像，渲染时 GL.invertCulling。
    public static class WaterFx3D
    {
        public const float NearRange = 30f;
        private static readonly Dictionary<int, Material> whiteMats = new Dictionary<int, Material>();
        private static Material glintMat;
        private static Mesh glintQuad;

        public static Material WhiteOf(Material src)
        {
            if (src == null) return null;
            int id = src.GetInstanceID();
            if (whiteMats.TryGetValue(id, out var m) && m != null)
            {
                m.mainTextureOffset = src.mainTextureOffset;
                return m;
            }
            m = new Material(src) { name = src.name + "_F016white" };
            m.color = Color.white;
            whiteMats[id] = m;
            return m;
        }

        public static bool NearCamera(Bounds worldBounds)
        {
            var cam = CameraRig3D.Cam;
            if (cam == null) return false;
            float d2 = worldBounds.SqrDistance(cam.transform.position);
            return d2 <= NearRange * NearRange;
        }

        /// <summary>把一片水面的顶点色按当前视角重写（verts 是图内局部坐标）。</summary>
        public static void ShadeSurface(Mesh mesh, Vector3[] verts, Color32[] lit, Color32[] scratch,
            Vector3 worldOffset, Color waterTint, LightField3D field)
        {
            var cam = CameraRig3D.Cam;
            if (cam == null || mesh == null) return;
            var e = Lighting3D.Current;
            var cp = cam.transform.position;
            var sunCol = ToneMap.SunColorFor(e.sunDir) * Mathf.Clamp01(e.glow);
            for (int i = 0; i < verts.Length; i++)
            {
                var w = verts[i] + worldOffset;
                var v = (w - cp);
                float len = v.magnitude;
                if (len < 1e-3f) { scratch[i] = lit[i]; continue; }
                v /= len;
                float cos = Mathf.Max(0f, -v.y);
                float F = WaterMath.Fresnel(cos, ShaderpackDefaults.WaterF0);
                var r = WaterMath.Reflect(v, Vector3.up);
                var sky = WaterMath.SkyAlong(r, e.sky, e.horizon, e.glow);
                float g = WaterMath.Glint(r, e.sunDir, 220f) * 1.6f;
                float sl = field != null ? field.SunLitAt(verts[i], Vector3.up) : 1f;
                var c = WaterMath.Shade(waterTint, lit[i], sky, F, sunCol, g, sl);
                scratch[i] = c;
            }
            mesh.colors32 = scratch;
        }

        /// <summary>太阳（夜里月亮）在水面上的拉长亮带。h 为水面世界高度。</summary>
        public static void DrawGlintStreak(Map map, Vector3 worldOffset, float h, System.Func<IntVec3, bool> isWater)
        {
            var cam = CameraRig3D.Cam;
            if (cam == null) return;
            var e = Lighting3D.Current;
            bool day = e.sunDir.y > 0.02f && e.glow > 0.05f;
            var dir = day ? e.sunDir : e.moonDir;
            if (!WaterMath.GlintPoint(cam.transform.position, h + 0.02f, dir, out var p)) return;
            var local = p - worldOffset;
            var cell = new IntVec3(Mathf.FloorToInt(local.x), 0, Mathf.FloorToInt(local.z));
            if (!cell.InBounds(map) || !isWater(cell)) return;
            if (!EnsureGlint()) return;
            float dist = Vector3.Distance(cam.transform.position, p);
            if (dist > 120f) return;
            var toCam = cam.transform.position - p; toCam.y = 0f;
            if (toCam.sqrMagnitude < 1e-4f) toCam = Vector3.forward;
            var rot = Quaternion.LookRotation(toCam.normalized, Vector3.up);
            // 越贴地平线越长（掠射角）；宽度随距离放开
            float lenK = Mathf.Clamp(1f / Mathf.Max(0.08f, dir.y), 1f, 9f);
            var scale = new Vector3(0.6f + dist * 0.02f, 1f, (0.6f + dist * 0.02f) * lenK);
            var tint = (day ? ToneMap.SunColorFor(e.sunDir) : LightShade.MoonColor()) * ((day ? 0.55f : 0.18f) * (1f - e.cloudiness * 0.8f));
            tint.a = 1f;
            Graphics.DrawMesh(glintQuad, Matrix4x4.TRS(p, rot, scale), Lighting3D.MatFor(glintMat, tint), 0);
        }

        private static bool EnsureGlint()
        {
            if (glintMat != null && glintQuad != null) return true;
            Shader sh = null;
            try { sh = ShaderDatabase.MoteGlow; } catch { }
            if (sh == null) return false;
            var tex = ProcTex3D.AdditiveRadial(64);
            if (tex == null) return false;
            glintMat = new Material(sh) { name = "Fps3D_WaterGlint", mainTexture = tex, renderQueue = 2920 };
            glintQuad = Util3D.GroundQuad;
            return true;
        }

        // ------------------------------------------------------------
        // A 案：平面镜面反射
        // ------------------------------------------------------------
        private static Camera reflCam;
        private static RenderTexture reflRT;
        private static RenderTexture spareRT;
        private static Matrix4x4 sampledProjection;
        private static float sampledHeight = float.NaN;
        private static Material mirrorMat;
        private static float mirrorH = float.NaN;
        public static bool MirrorReady { get; private set; }

        /// <summary>本帧水面高度（近处第一块水面的局部 y + 偏移），WaterLayer3D 报上来。</summary>
        public static void ReportWaterHeight(float worldY)
        {
            if (float.IsNaN(mirrorH)) mirrorH = worldY;
        }

        /// <summary>整帧层调用：所有地图画完后渲染反射。</summary>
        public static void RenderMirror(Camera main)
        {
            float h = mirrorH;
            mirrorH = float.NaN;
            if (!Shaderpack3D.WaterMirror || main == null) { Release(); return; }
            if (!float.IsFinite(h) || main.transform.position.y <= h + 0.05f) { MirrorReady = false; return; }
            Shaderpack3D.Begin();
            var previousActive = RenderTexture.active;
            try
            {
                int w = Mathf.Max(64, Screen.width / 2), hh = Mathf.Max(64, Screen.height / 2);
                if (reflRT != null && (reflRT.width != w || reflRT.height != hh)) Release();
                if (reflCam == null)
                {
                    var go = new GameObject("Fps3D_WaterReflCam");
                    Object.DontDestroyOnLoad(go);
                    reflCam = go.AddComponent<Camera>();
                    reflCam.enabled = false;
                }
                if (spareRT == null)
                {
                    spareRT = new RenderTexture(w, hh, 16, RenderTextureFormat.ARGB32) { name = "Fps3D_WaterReflRT" };
                    spareRT.Create();
                }
                reflCam.CopyFrom(main);
                reflCam.enabled = false;
                reflCam.targetTexture = spareRT;
                reflCam.cullingMask = main.cullingMask & ~(1 << 31);
                reflCam.transform.position = WaterMath.MirrorY(h).MultiplyPoint(main.transform.position);
                var view = main.worldToCameraMatrix * WaterMath.MirrorY(h);
                reflCam.worldToCameraMatrix = view;
                var cPos = view.MultiplyPoint(new Vector3(0f, h, 0f));
                var cNormal = view.MultiplyVector(Vector3.up).normalized;
                var plane = new Vector4(cNormal.x, cNormal.y, cNormal.z, -Vector3.Dot(cPos, cNormal));
                if (!WaterMath.TryOblique(main.projectionMatrix, plane, out var projection))
                {
                    MirrorReady = false;
                    return;
                }
                projection = WaterMath.FlipProjectionX(projection);
                reflCam.projectionMatrix = projection;
                reflCam.Render();
                var old = reflRT;
                reflRT = spareRT;
                spareRT = old;
                sampledProjection = projection * view;
                sampledHeight = h;
                MirrorReady = true;
            }
            catch (System.Exception ex)
            {
                Release();
                Log.WarningOnce(ModEntry.LogPrefix + "F016 water mirror failed, disabling: " + ex.Message, 0x1F0166);
                var s = FirstPerson3DMod.settings;
                if (s != null) s.capsOblique = 0;
            }
            finally
            {
                if (reflCam != null) reflCam.targetTexture = null;
                RenderTexture.active = previousActive;
                Shaderpack3D.End();
            }
        }

        public static void Release()
        {
            MirrorReady = false;
            mirrorH = sampledHeight = float.NaN;
            if (reflCam != null)
            {
                reflCam.targetTexture = null;
                Object.Destroy(reflCam.gameObject);
                reflCam = null;
            }
            ReleaseTexture(ref reflRT);
            ReleaseTexture(ref spareRT);
            if (mirrorMat != null) Object.Destroy(mirrorMat);
            mirrorMat = null;
        }

        private static void ReleaseTexture(ref RenderTexture rt)
        {
            if (rt == null) return;
            if (RenderTexture.active == rt) RenderTexture.active = null;
            rt.Release();
            Object.Destroy(rt);
            rt = null;
        }

        public static void Reset()
        {
            Release();
            foreach (var m in whiteMats.Values) if (m != null) Object.Destroy(m);
            whiteMats.Clear();
            if (glintMat != null) Object.Destroy(glintMat);
            glintMat = null;
        }

        /// <summary>
        /// 近处水面再叠一层镜面：用上一帧的反射 RT（本帧的要等所有地图画完才渲），
        /// UV = 顶点在主相机里的视口坐标，alpha = 菲涅尔 × 0.85。
        /// </summary>
        public static void DrawMirrorOverlay(Mesh surf, Vector3[] verts, Vector3 worldOffset, ref Mesh overlay)
        {
            var cam = CameraRig3D.Cam;
            if (!Shaderpack3D.WaterMirror || !MirrorReady || reflRT == null || cam == null || surf == null) return;
            if (verts.Length == 0 || Mathf.Abs(verts[0].y + worldOffset.y - sampledHeight) > 0.01f) return;
            if (mirrorMat == null)
            {
                Shader sh = null;
                try { sh = ShaderDatabase.TransparentPostLight; } catch { }
                if (sh == null) return;
                mirrorMat = new Material(sh) { name = "Fps3D_WaterMirror", color = Color.white, renderQueue = 2914 };
            }
            mirrorMat.mainTexture = reflRT;
            if (overlay == null)
            {
                overlay = new Mesh { name = "Fps3D_WaterMirrorOverlay" };
                overlay.vertices = verts;
                overlay.triangles = surf.triangles;
            }
            var uv = new Vector2[verts.Length];
            var col = new Color32[verts.Length];
            var cp = cam.transform.position;
            for (int i = 0; i < verts.Length; i++)
            {
                var w = verts[i] + worldOffset;
                bool visible = WaterMath.TryViewport(sampledProjection, w, out uv[i]);
                var v = (w - cp).normalized;
                float F = WaterMath.Fresnel(Mathf.Max(0f, -v.y), ShaderpackDefaults.WaterF0);
                bool inside = visible && uv[i].x >= 0f && uv[i].x <= 1f && uv[i].y >= 0f && uv[i].y <= 1f;
                col[i] = new Color(1f, 1f, 1f, inside ? Mathf.Clamp01(F * 0.85f) : 0f);
            }
            overlay.uv = uv;
            overlay.colors32 = col;
            overlay.RecalculateBounds();
            Graphics.DrawMesh(overlay, Matrix4x4.Translate(worldOffset + new Vector3(0f, 0.005f, 0f)), mirrorMat, 31);
        }
    }
}
