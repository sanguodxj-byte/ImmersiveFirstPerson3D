using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L4-A：太阳眩光与镜头鬼影（无 RT 的「精灵泛光」）
    // ============================================================
    //
    // 太阳在视野内且没被墙/山/屋顶挡住时：太阳处一圈大光晕 + 沿「太阳 → 屏幕中心」的 4 枚鬼影。
    // 全部是贴在相机前 1 m 的加法面片（MoteGlow，预乘贴图），不需要后处理。
    // 可见度：相机 → 太阳方向 5 条射线走光场实心高度场（中心 + 四角偏 0.6°），0.1 s 重测、0.15 s 渐变；
    // 再乘 (1 - 云量)。夜里同一套给月亮，强度 0.25。
    public static class Flares3D
    {
        private const float PlaneDist = 1.0f;
        private static Material haloMat, ghostMat;
        private static Mesh quad;
        private static float vis, visTarget, poll;

        public static float SunVisibility => vis;

        private static bool Ensure()
        {
            if (haloMat != null && ghostMat != null && quad != null) return true;
            Shader sh = null;
            try { sh = ShaderDatabase.MoteGlow; } catch { }
            if (sh == null) try { sh = ShaderDatabase.WorldOverlayAdditive; } catch { }
            if (sh == null) return false;
            var radial = ProcTex3D.AdditiveRadial(128);
            var ring = ProcTex3D.AdditiveRing(128, 0.78f, 0.18f);
            if (radial == null || ring == null) return false;
            haloMat = new Material(sh) { name = "Fps3D_SunHalo", mainTexture = radial, renderQueue = 3990 };
            ghostMat = new Material(sh) { name = "Fps3D_SunGhost", mainTexture = ring, renderQueue = 3991 };
            quad = BuildCenteredQuad();
            return true;
        }

        private static Mesh BuildCenteredQuad()
        {
            var m = new Mesh { name = "Fps3D_FlareQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            // 双面：相机朝向的符号不必操心
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        public static void Draw()
        {
            if (!Shaderpack3D.Sprites) return;
            var cam = CameraRig3D.Cam;
            var map = Find.CurrentMap;
            if (cam == null || map == null) return;
            var sc = SceneRegistry.Find(map);
            if (sc == null || sc.Light == null) return;
            Shaderpack3D.Begin();
            try
            {
                var e = Lighting3D.Current;
                bool day = e.sunDir.y > 0.02f && e.glow > 0.05f;
                Vector3 dir = day ? e.sunDir : e.moonDir;
                float gainBase = day ? 1f : 0.25f;
                if (dir.y <= 0.02f) { vis = 0f; return; }

                // ---- 可见度 ----
                poll += Time.deltaTime;
                if (poll >= 0.1f)
                {
                    poll = 0f;
                    visTarget = SunRayVisibility(sc, cam.transform.position, dir);
                }
                vis = FlareMath.Approach(vis, visTarget, Time.deltaTime, 0.05f);
                if (vis <= 0.01f) return;
                if (!Ensure()) return;

                var vp = cam.WorldToViewportPoint(cam.transform.position + dir * 100f);
                if (vp.z <= 0f) return;
                var sun = new Vector2(vp.x, vp.y);
                float gain = FlareMath.GlareGain(sun, vis, e.cloudiness, dir.y) * gainBase;
                if (gain <= 0.01f) return;

                float frustH = 2f * PlaneDist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                var rot = cam.transform.rotation;
                var tint = day ? ToneMap.SunColorFor(e.sunDir) : LightShade.MoonColor();

                // 太阳处大光晕
                var hp = cam.ViewportToWorldPoint(new Vector3(sun.x, sun.y, PlaneDist));
                float hs = frustH * 0.55f;
                var hc = tint * (0.55f * gain); hc.a = 1f;
                var hm = Matrix4x4.TRS(hp, rot, new Vector3(hs, hs, 1f));
                var hmat = Lighting3D.MatFor(haloMat, hc);
                Graphics.DrawMesh(quad, hm, hmat, 0);
                Bloom3D.Emit(quad, hm, hmat);

                if (!Shaderpack3D.SunGhosts) return;
                for (int i = 0; i < FlareMath.GhostT.Length; i++)
                {
                    var g = FlareMath.GhostPos(sun, i);
                    var gp = cam.ViewportToWorldPoint(new Vector3(g.x, g.y, PlaneDist));
                    float gs = frustH * FlareMath.GhostScale[i] * 2f;
                    // 鬼影带一点色散：交替偏暖 / 偏青
                    var gc = (i % 2 == 0 ? new Color(1f, 0.82f, 0.55f) : new Color(0.55f, 0.85f, 1f)) * (FlareMath.GhostGain[i] * gain);
                    gc.a = 1f;
                    Graphics.DrawMesh(quad, Matrix4x4.TRS(gp, rot, new Vector3(gs, gs, 1f)), Lighting3D.MatFor(ghostMat, gc), 0);
                }
            }
            finally { Shaderpack3D.End(); }
        }

        /// <summary>5 条射线的通畅比例（0..1）。相机所在格有屋顶则 0。</summary>
        private static float SunRayVisibility(Scene3D sc, Vector3 camWorld, Vector3 dir)
        {
            var f = sc.Light;
            var local = camWorld - sc.worldOffset;
            if (f.Roofed(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.z))) return 0f;
            var right = Vector3.Cross(Vector3.up, dir).normalized;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            var up = Vector3.Cross(dir, right).normalized;
            float off = Mathf.Tan(0.6f * Mathf.Deg2Rad);
            int open = 0;
            for (int i = 0; i < 5; i++)
            {
                var d = dir;
                if (i == 1) d = (dir + right * off).normalized;
                else if (i == 2) d = (dir - right * off).normalized;
                else if (i == 3) d = (dir + up * off).normalized;
                else if (i == 4) d = (dir - up * off).normalized;
                if (f.SegmentOpen(local, local + d * 60f, 0.2f)) open++;
            }
            return open / 5f;
        }
    }
}
