using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // F016 L5：大气 —— 距离/高度雾、室内光束、低太阳放射光条
    // ============================================================
    //
    // 室内（相机格 skyVis）密度 × 0.2；天气（雾/雨/雪/沙）改密度。水下由 ScreenFx3D 再乘 4。
    //
    // 光束：相机 24 m 内的屋顶开洞格（FogMath.IsRoofHole），太阳照得进时（洞口沿太阳方向通畅），
    // 从层高处沿 -sunDir 拉一束交叉加法片到地面；雾越浓越显。门洞不单列（门所在格通常有屋顶，
    // 光束要等 F008 屋顶几何落地再从洞口下沿起算，见 F016 Dependencies）。
    public static class Atmosphere3D
    {
        // 水下等屏幕层乘进来的额外密度倍率（ScreenFx3D 写）
        public static float DensityMul = 1f;

        public static float CurrentDensity { get; private set; }

        private static Material beamMat;
        private static Mesh beamMesh;
        private static Mesh moteQuad;
        private static readonly List<Vector3> holes = new List<Vector3>(64);
        private static float holePoll = 99f;
        private static int holeMapId = -1;

        public static void Draw(Camera cam)
        {
            var map = Find.CurrentMap;
            if (cam == null || map == null)
            {
                ReleaseEngineFog();
                return;
            }
            var sc = SceneRegistry.Find(map);
            if (sc == null || sc.Light == null)
            {
                ReleaseEngineFog();
                return;
            }
            Shaderpack3D.Begin();
            try
            {
                var env = Lighting3D.Current;
                var local = cam.transform.position - sc.worldOffset;
                float vis = sc.Light.SkyVisAt(local.x, local.z);
                var st = FirstPerson3DMod.settings;
                float baseD = st != null ? Mathf.Max(0f, st.fogDensity) : ShaderpackDefaults.FogDensity;
                float dens = FogMath.Density(baseD, env.fog, env.rain, env.snow, env.sand)
                             * FogMath.IndoorK(vis, ShaderpackDefaults.FogIndoorK) * DensityMul;
                CurrentDensity = dens;

                if (Shaderpack3D.Fog) DrawFog(cam, env, dens, sc.baseY);
                else ReleaseEngineFog();

                if (Shaderpack3D.IndoorShafts) DrawIndoorShafts(cam, map, sc, env, dens, baseD);
                if (Shaderpack3D.OutdoorShafts) DrawOutdoorRays(cam, env);
            }
            finally { Shaderpack3D.End(); }
        }

        // ------------------------------------------------------------
        // 雾
        // ------------------------------------------------------------

        private static void DrawFog(Camera cam, SkyScenery.Env env, float dens, float groundY)
        {
            if (dens <= 1e-4f)
            {
                ReleaseEngineFog();
                return;
            }
            DepthFog3D.Update(cam, env, dens, groundY);
        }

        public static void ReleaseEngineFog()
        {
            DepthFog3D.Disable();
            CurrentDensity = 0f;
        }

        public static void Restore()
        {
            ReleaseEngineFog();
            DensityMul = 1f;
        }

        // ------------------------------------------------------------
        // 室内光束
        // ------------------------------------------------------------

        private static bool EnsureBeams()
        {
            if (beamMat != null && beamMesh != null) return true;
            Shader sh = null;
            try { sh = ShaderDatabase.MoteGlow; } catch { }
            if (sh == null) try { sh = ShaderDatabase.WorldOverlayAdditive; } catch { }
            if (sh == null) return false;
            var tex = ProcTex3D.AdditiveBeam(32, 64);
            if (tex == null) return false;
            beamMat = new Material(sh) { name = "Fps3D_LightShaft", mainTexture = tex, renderQueue = 2960 };
            beamMesh = BuildBeam();
            moteQuad = Util3D.BillboardQuad;
            return true;
        }

        // 两片交叉竖直面：沿 -Y 从 0 到 -1，宽 1；v=1 在顶（洞口）、v=0 在底
        private static Mesh BuildBeam()
        {
            var m = new Mesh { name = "Fps3D_ShaftBeam" };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var c = new List<Color32>();
            for (int k = 0; k < 2; k++)
            {
                var ax = k == 0 ? new Vector3(0.5f, 0f, 0f) : new Vector3(0f, 0f, 0.5f);
                int b = v.Count;
                v.Add(-ax); v.Add(-ax + Vector3.down); v.Add(ax + Vector3.down); v.Add(ax);
                uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f));
                for (int i = 0; i < 4; i++) c.Add(new Color32(255, 255, 255, 255));
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
                t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2);
            }
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(c); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        private static void ScanHoles(Map map, Scene3D sc, Vector3 camLocal)
        {
            holes.Clear();
            var f = sc.Light;
            int cx = Mathf.FloorToInt(camLocal.x), cz = Mathf.FloorToInt(camLocal.z);
            const int R = 24;
            for (int z = cz - R; z <= cz + R; z++)
                for (int x = cx - R; x <= cx + R; x++)
                {
                    if (x < 0 || z < 0 || x >= f.SizeX || z >= f.SizeZ) continue;
                    int n = (f.Roofed(x + 1, z) ? 1 : 0) + (f.Roofed(x - 1, z) ? 1 : 0)
                          + (f.Roofed(x, z + 1) ? 1 : 0) + (f.Roofed(x, z - 1) ? 1 : 0);
                    if (!FogMath.IsRoofHole(f.Roofed(x, z), f.Solid(x, z), n)) continue;
                    holes.Add(new Vector3(x + 0.5f, sc.baseY + Fps3DState.StoryHeight, z + 0.5f));
                    if (holes.Count >= 48) return;
                }
        }

        private static void DrawIndoorShafts(Camera cam, Map map, Scene3D sc, SkyScenery.Env env, float dens, float baseD)
        {
            if (env.glow <= 0.08f || env.sunDir.y <= 0.08f) return;
            var camLocal = cam.transform.position - sc.worldOffset;
            holePoll += Time.deltaTime;
            if (holePoll >= 1f || holeMapId != map.uniqueID)
            {
                holePoll = 0f;
                holeMapId = map.uniqueID;
                ScanHoles(map, sc, camLocal);
            }
            if (holes.Count == 0 || !EnsureBeams()) return;
            var st = FirstPerson3DMod.settings;
            float gain = st != null ? st.shaftGain : ShaderpackDefaults.ShaftGain;
            float k = FogMath.ShaftIntensity(gain, env.glow, dens, baseD) * 0.22f;
            if (k <= 0.005f) return;
            var sun = env.sunDir;
            var down = -sun;
            float len = Fps3DState.StoryHeight / Mathf.Max(0.12f, sun.y);
            var rot = Quaternion.FromToRotation(Vector3.down, down);
            var tint = ToneMap.SunColorFor(sun) * k; tint.a = 1f;
            var mat = Lighting3D.MatFor(beamMat, tint);
            for (int i = 0; i < holes.Count; i++)
            {
                var top = holes[i];
                // 太阳照得进：洞口往太阳方向 30 m 通畅
                if (!sc.Light.SegmentOpen(top, top + sun * 30f, 0.2f)) continue;
                // 光束要落在室内（终点那格有顶）才有意义
                var end = top + down * len;
                if (!sc.Light.Roofed(Mathf.FloorToInt(end.x), Mathf.FloorToInt(end.z))) continue;
                var w = top + sc.worldOffset;
                if (!Frustum3D.VisiblePoint(w + down * (len * 0.5f), len)) continue;
                Graphics.DrawMesh(beamMesh, Matrix4x4.TRS(w, rot, new Vector3(1f, len, 1f)), mat, 0);
                // 光束里飘几粒尘：沿光束缓慢下落、左右漂
                var mote = tint * 0.8f; mote.a = 1f;
                var mm = Lighting3D.MatFor(beamMat, mote);
                for (int d = 0; d < 4; d++)
                {
                    float ph = Mathf.Repeat(Time.time * 0.05f + d * 0.25f + i * 0.13f, 1f);
                    var p = w + down * (len * (0.15f + 0.75f * ph))
                            + new Vector3(Mathf.Sin(Time.time * 0.7f + d * 2.1f + i) * 0.3f, 0f, Mathf.Cos(Time.time * 0.6f + d * 1.7f + i) * 0.3f);
                    Graphics.DrawMesh(moteQuad, Matrix4x4.TRS(p, cam.transform.rotation, new Vector3(0.03f, 0.03f, 1f)), mm, 0);
                }
            }
        }

        // ------------------------------------------------------------
        // 低太阳放射光条（室外）
        // ------------------------------------------------------------

        private static void DrawOutdoorRays(Camera cam, SkyScenery.Env env)
        {
            float elev = Mathf.Asin(Mathf.Clamp(env.sunDir.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (elev <= 0.5f || elev >= 25f || env.glow <= 0.05f) return;
            float v = Flares3D.SunVisibility;
            // 只有「部分被挡」时才有光条：全露是眩光，全挡是阴影
            float part = Mathf.Clamp01(1f - Mathf.Abs(v - 0.5f) / 0.35f);
            if (part <= 0.01f || !EnsureBeams()) return;
            var vp = cam.WorldToViewportPoint(cam.transform.position + env.sunDir * 100f);
            if (vp.z <= 0f || vp.x < -0.3f || vp.x > 1.3f || vp.y < -0.3f || vp.y > 1.3f) return;
            var st = FirstPerson3DMod.settings;
            float gain = (st != null ? st.shaftGain : ShaderpackDefaults.ShaftGain) * 0.10f * part * (1f - env.cloudiness)
                         * Mathf.Clamp01((25f - elev) / 10f);
            if (gain <= 0.005f) return;
            const float dist = 1.0f;
            float frustH = 2f * dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var center = cam.ViewportToWorldPoint(new Vector3(vp.x, vp.y, dist));
            var camRot = cam.transform.rotation;
            var tint = ToneMap.SunColorFor(env.sunDir) * gain; tint.a = 1f;
            var mat = Lighting3D.MatFor(beamMat, tint);
            for (int i = 0; i < 12; i++)
            {
                float ang = i * 30f + Mathf.Sin(Time.time * 0.15f + i) * 6f;
                float l = frustH * (0.55f + 0.25f * Mathf.Sin(i * 1.7f + Time.time * 0.2f));
                // 光条网格沿局部 -Y 伸出：在相机平面里绕视线转角度（交叉的另一片侧对相机，看不见也无妨）
                var r = camRot * Quaternion.AngleAxis(ang, Vector3.forward);
                Graphics.DrawMesh(beamMesh, Matrix4x4.TRS(center, r, new Vector3(frustH * 0.05f, l, frustH * 0.05f)), mat, 0);
            }
        }
    }
}