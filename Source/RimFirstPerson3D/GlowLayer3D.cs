using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 灯具可见发光（F013）
    // ============================================================
    //
    // 「有光源」要看得见：光把地面和墙照亮之外，灯那一处本身要有一团亮。
    // 画两片：贴地的光池（光落在地上）+ 立着的三向星片（看见灯头那团）。
    //
    // 尺寸与颜色一律取原版 CompGlower，不另定半径表：
    //   GlowRadius -> 直径；GlowColor -> 颜色；Glows -> 断灯就整团消失。
    // 材质走 ShaderDatabase.MoteGlow（原版加法发光），取不到时退回 Transparent。
    [StaticConstructorOnStartup]
    public class GlowLayer3D
    {
        private const int MaxGlowers = 512;
        private const float RescanSec = 0.5f;
        // 星片中心高度系数：半径越大抬得越高，但不超过净层高的七成。
        private const float HaloHeightFactor = 0.6f;

        private readonly Map map;
        private readonly float baseY;
        public Vector3 worldOffset = Vector3.zero;

        private struct Src
        {
            public float x, z;
            public float r;
            public float h;
            public Color col;
        }

        private readonly List<Src> srcs = new List<Src>(64);
        private float poll = RescanSec;

        private static Material glowMat;
        private static Texture2D glowTex;
        private static Mesh poolMesh;
        private static Mesh haloMesh;

        public GlowLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void Update()
        {
            if (!Lighting3D.GlowQuadsOn) return;
            poll += Time.deltaTime;
            if (poll < RescanSec) return;
            poll = 0f;
            Rescan();
        }

        private void Rescan()
        {
            srcs.Clear();
            List<Thing> list = null;
            try { list = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial); } catch { }
            if (list == null) return;
            for (int i = 0; i < list.Count && srcs.Count < MaxGlowers; i++)
            {
                var t = list[i];
                if (t == null || !t.Spawned || t.def == null) continue;
                CompGlower c;
                try { c = t.TryGetComp<CompGlower>(); } catch { continue; }
                if (c == null || !c.Glows) continue;
                Color col;
                float r;
                try
                {
                    col = c.GlowColor.ToColor;
                    r = c.GlowRadius;
                }
                catch { continue; }
                if (r <= 0.05f) continue;
                // 颜色归一化到单位亮度：亮暗由强度承担，色相由灯具决定。
                float m = Mathf.Max(0.05f, Mathf.Max(col.r, Mathf.Max(col.g, col.b)));
                col = new Color(col.r / m, col.g / m, col.b / m, 1f);
                var dp = t.DrawPos;
                float h = 1.0f;
                try
                {
                    string dn = t.def.defName != null ? t.def.defName.ToLowerInvariant() : "";
                    if (dn.Contains("campfire") || dn.Contains("glowpod")) h = 0.48f;
                    else if (dn.Contains("brazier") || dn.Contains("darktorch")) h = 0.85f;
                    else if (dn.Contains("torch")) h = 1.15f;
                    else if (dn.Contains("standing") || dn.Contains("flood") || dn.Contains("sunlamp")) h = 1.38f;
                    else if (t.Graphic != null) h = Mathf.Clamp(t.Graphic.drawSize.y * 0.72f, 0.50f, 1.45f);
                }
                catch { }
                srcs.Add(new Src { x = dp.x, z = dp.z, r = r, h = h, col = col });
            }
        }

        public void Draw()
        {
            if (!Lighting3D.GlowQuadsOn || srcs.Count == 0) return;
            if (!EnsureAssets()) return;
            float lampGain = Mathf.Clamp01(Lighting3D.LampStrength);
            if (lampGain <= 0.001f) return;
            // 白昼下发光光晕柔和收敛，夜里饱满清晰
            float dayDim = Mathf.Lerp(1.0f, 0.55f, Mathf.Clamp01(Lighting3D.Current.glow));
            for (int i = 0; i < srcs.Count; i++)
            {
                var s = srcs[i];
                var wpos = new Vector3(s.x + worldOffset.x, baseY, s.z + worldOffset.z);
                if (!Frustum3D.VisiblePoint(wpos, s.r + 2f)) continue;
                // 断电/未点燃的灯不会进 srcs（Rescan 里已按 Glows 过滤），这里只画灯自身的色相。
                var poolTint = s.col * (0.28f * dayDim * lampGain);
                poolTint.a = 1f;
                var haloTint = Color.Lerp(s.col, Color.white, 0.28f) * (0.78f * dayDim * lampGain);
                haloTint.a = 1f;

                float d = Mathf.Clamp(s.r * 0.55f, 1.4f, 5.2f);
                // 光池：贴地，比地面略抬一点避免与共面的地表格打架。
                var pm = Matrix4x4.TRS(new Vector3(wpos.x, baseY + 0.03f, wpos.z),
                    Quaternion.identity, new Vector3(d, 1f, d));
                Graphics.DrawMesh(poolMesh, pm, Lighting3D.MatFor(glowMat, poolTint), 0);

                // 灯头那团：锚在灯具本体灯头高度（火盆/篝火偏低、立灯偏高），尺寸收敛在灯芯光晕尺度，绝不占满视野。
                float hy = baseY + Mathf.Clamp(s.h * 0.78f, 0.42f, Fps3DState.WallHeight * 0.75f);
                float hs = Mathf.Clamp(0.45f + s.r * 0.06f, 0.55f, 1.15f);
                var hm = Matrix4x4.TRS(new Vector3(wpos.x, hy, wpos.z), Quaternion.identity,
                    new Vector3(hs, hs * 0.85f, hs));
                Graphics.DrawMesh(haloMesh, hm, Lighting3D.MatFor(glowMat, haloTint), 0);
                PerfCounters.GlowDraws++;
            }
        }

        public LightField3D light;

        public void Dispose() { }

        private static bool EnsureAssets()
        {
            if (glowTex == null)
            {
                // 与射击火光共用同一份预乘加法光斑（ProcTex3D），不留第二套剖面
                glowTex = ProcTex3D.AdditiveRadial(64);
                if (glowTex == null) return false;
            }
            if (glowMat == null)
            {
                Shader sh = null;
                try { sh = ShaderDatabase.WorldOverlayAdditive; } catch { }
                if (sh == null) try { sh = Shader.Find("Custom/Unlit additive"); } catch { }
                if (sh == null) try { sh = ShaderDatabase.MoteGlow; } catch { }
                if (sh == null) try { sh = ShaderDatabase.Transparent; } catch { }
                if (sh == null) sh = Shader.Find("Unlit/Texture");
                if (sh == null) return false;
                glowMat = new Material(sh) { name = "Fps3D_GlowMat", renderQueue = 2950 };
                glowMat.mainTexture = glowTex;
                glowMat.color = Color.white;
            }
            if (poolMesh == null) poolMesh = Util3D.GroundQuad;
            if (haloMesh == null) haloMesh = Util3D.StarMesh;
            return true;
        }
    }
}
