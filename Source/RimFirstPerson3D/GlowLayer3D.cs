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
            public bool isFlame;
            public float flameScale;
        }

        private readonly List<Src> srcs = new List<Src>(64);
        private float poll = RescanSec;

        private static Material glowMat;
        private static Material flameMat;
        private static Texture2D glowTex;
        private static Texture2D flameTex;
        private static Texture2D flameDistortTex;
        private static Mesh poolMesh;
        private static Mesh haloMesh;
        private static Mesh flameMesh;

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
                float sx = dp.x, sz = dp.z;
                float h = 1.0f;
                bool isFlame = false;
                float flameScale = 0.36f;
                try
                {
                    string dn = t.def.defName != null ? t.def.defName.ToLowerInvariant() : "";
                    bool isAttach = t.def.building != null && t.def.building.isAttachment;
                    if (isAttach)
                    {
                        // 壁挂件统一在 1x1 格内离地 Y = 2.00m，剔除原版 2D 俯视 +0.9m 的穿墙偏移
                        var cPos = t.Position;
                        if (dn == "torchwalllamp" || dn.Contains("torch"))
                        {
                            var localTip = Quaternion.Euler(0f, t.Rotation.AsAngle, 0f) * new Vector3(0f, 0f, 0.22f);
                            sx = cPos.x + 0.5f + localTip.x;
                            sz = cPos.z + 0.5f + localTip.z;
                            h = Metrics3D.WallAttachmentHeight + 0.28f;
                            isFlame = true;
                            flameScale = 0.34f;
                        }
                        else
                        {
                            var localLamp = Quaternion.Euler(0f, t.Rotation.AsAngle, 0f) * new Vector3(0f, 0f, 0.34f);
                            sx = cPos.x + 0.5f + localLamp.x;
                            sz = cPos.z + 0.5f + localLamp.z;
                            h = Metrics3D.WallAttachmentHeight + 0.08f;
                        }
                    }
                    else if (dn.Contains("campfire"))
                    {
                        h = 0.32f;
                        isFlame = true;
                        flameScale = 0.50f;
                    }
                    else if (dn.Contains("brazier"))
                    {
                        h = 0.72f;
                        isFlame = true;
                        flameScale = 0.44f;
                    }
                    else if (dn.Contains("torch"))
                    {
                        h = 0.86f;
                        isFlame = true;
                        flameScale = 0.38f;
                    }
                    else if (dn.Contains("glowpod")) h = 0.48f;
                    else if (dn.Contains("standing") || dn.Contains("flood") || dn.Contains("sunlamp")) h = 1.38f;
                    else if (t.Graphic != null) h = Mathf.Clamp(t.Graphic.drawSize.y * 0.72f, 0.50f, 1.45f);
                }
                catch { }
                srcs.Add(new Src { x = sx, z = sz, r = r, h = h, col = col, isFlame = isFlame, flameScale = flameScale });
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

                float phase = Time.time * 8.5f + (s.x * 3.7f + s.z * 5.3f);
                float flicker = s.isFlame
                    ? (1.0f + 0.07f * Mathf.Sin(phase * 1.35f) + 0.04f * Mathf.Cos(phase * 2.75f))
                    : 1.0f;

                // 断电/未点燃的灯不会进 srcs（Rescan 里已按 Glows 过滤），这里只画灯自身的色相。
                var poolTint = s.col * (0.30f * dayDim * lampGain * flicker);
                poolTint.a = 1f;
                var haloTint = Color.Lerp(s.col, Color.white, 0.25f) * (0.76f * dayDim * lampGain * flicker);
                haloTint.a = 1f;

                float d = Mathf.Clamp(s.r * 0.58f, 1.45f, 5.4f) * (s.isFlame ? (0.97f + 0.03f * flicker) : 1f);
                // 光池：贴地，比地面略抬一点避免与共面的地表格打架。
                var pm = Matrix4x4.TRS(new Vector3(wpos.x, baseY + 0.03f, wpos.z),
                    Quaternion.identity, new Vector3(d, 1f, d));
                Graphics.DrawMesh(poolMesh, pm, Lighting3D.MatFor(glowMat, poolTint), 0);

                // 灯头位置：壁挂件（h >= 2.0m）与火把直接取其真实锚点高度，其余普通落地灯具按比例限幅
                float hy = (s.isFlame || s.h >= Metrics3D.WallAttachmentHeight)
                    ? (baseY + s.h)
                    : (baseY + Mathf.Clamp(s.h * 0.78f, 0.42f, Fps3DState.WallHeight * 0.75f));

                if (s.isFlame && flameMesh != null && flameMat != null)
                {
                    // 火把/壁炬/火盆：木柄顶端由 GPU 火焰 Shader 动态绘制双层摇曳立体火舌
                    float swayX = 5.5f * Mathf.Sin(phase);
                    float swayZ = 5.5f * Mathf.Cos(phase * 0.83f);
                    float stretch = 1.0f + 0.14f * Mathf.Sin(phase * 1.47f);
                    float fw = s.flameScale;
                    float fh = s.flameScale * 1.45f * stretch;

                    var fPos = new Vector3(wpos.x, hy, wpos.z);
                    var outerRot = Quaternion.Euler(swayX, (phase * 12f) % 360f, swayZ);
                    var outerCol = Color.Lerp(s.col, new Color(1f, 0.58f, 0.14f, 1f), 0.45f) * (1.15f * lampGain);
                    outerCol.a = 1f;
                    Graphics.DrawMesh(flameMesh, Matrix4x4.TRS(fPos, outerRot, new Vector3(fw, fh, fw)),
                        Lighting3D.MatFor(flameMat, outerCol), 0);

                    var innerRot = Quaternion.Euler(-swayZ * 0.6f, ((phase * 18f) + 30f) % 360f, swayX * 0.6f);
                    var innerCol = Color.Lerp(s.col, Color.white, 0.65f) * (1.25f * lampGain);
                    innerCol.a = 1f;
                    Graphics.DrawMesh(flameMesh, Matrix4x4.TRS(fPos + new Vector3(0f, 0.02f, 0f), innerRot,
                        new Vector3(fw * 0.62f, fh * 0.72f, fw * 0.62f)),
                        Lighting3D.MatFor(flameMat, innerCol), 0);
                    PerfCounters.GlowDraws += 2;
                }

                float hs = s.isFlame
                    ? Mathf.Clamp(s.flameScale * 1.42f, 0.44f, 0.88f)
                    : Mathf.Clamp(0.48f + s.r * 0.06f, 0.58f, 1.18f);
                float haloCenterY = hy + (s.isFlame ? s.flameScale * 0.48f : 0f);
                var hm = Matrix4x4.TRS(new Vector3(wpos.x, haloCenterY, wpos.z),
                    Quaternion.identity, new Vector3(hs, hs * 0.90f, hs));
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
                try { sh = ShaderDatabase.MoteGlow; } catch { }
                if (sh == null) try { sh = ShaderDatabase.WorldOverlayAdditive; } catch { }
                if (sh == null) try { sh = ShaderDatabase.TransparentPostLight; } catch { }
                if (sh == null) try { sh = ShaderDatabase.Transparent; } catch { }
                if (sh == null) return false;
                glowMat = new Material(sh) { name = "Fps3D_GlowMat", renderQueue = 2950 };
                glowMat.mainTexture = glowTex;
                glowMat.color = Color.white;
            }
            if (flameTex == null) flameTex = ProcTex3D.FlameTongue(64, 96);
            if (flameDistortTex == null) flameDistortTex = ProcTex3D.Ripple(64);
            if (flameMat == null && flameTex != null)
            {
                Shader fsh = null;
                try { fsh = ShaderDatabase.MoteGlowDistorted; } catch { }
                if (fsh == null) try { fsh = ShaderDatabase.MoteGlow; } catch { }
                if (fsh == null) try { fsh = ShaderDatabase.WorldOverlayAdditiveTwoSided; } catch { }
                if (fsh == null) try { fsh = ShaderDatabase.WorldOverlayAdditive; } catch { }
                if (fsh == null) fsh = glowMat.shader;
                flameMat = new Material(fsh) { name = "Fps3D_FlameShaderMat", renderQueue = 2955 };
                flameMat.mainTexture = flameTex;
                flameMat.color = Color.white;
                if (flameDistortTex != null && flameMat.HasProperty("_DistortionTex"))
                    flameMat.SetTexture("_DistortionTex", flameDistortTex);
                if (flameMat.HasProperty("_distortionIntensity"))
                    flameMat.SetFloat("_distortionIntensity", 0.028f);
                if (flameMat.HasProperty("_pulseSpeed"))
                    flameMat.SetFloat("_pulseSpeed", 2.4f);
            }
            if (poolMesh == null) poolMesh = Util3D.GroundQuad;
            if (haloMesh == null) haloMesh = BuildCenteredHaloMesh();
            if (flameMesh == null) flameMesh = BuildFlameMesh();
            return true;
        }

        /// <summary>
        /// 构造几何中心在 (0,0,0)、全纯白顶点色的 3D 光晕星片（3 竖直交叉面 + 1 水平赤道面），
        /// 避免复用树木 StarMesh 导致下半截带深灰根部 AO 顶点色且中心上偏 +0.5m。
        /// </summary>
        private static Mesh BuildCenteredHaloMesh()
        {
            var m = new Mesh { name = "Fps3D_CenteredHaloMesh" };
            var verts = new List<Vector3>(16);
            var uvs = new List<Vector2>(16);
            var cols = new List<Color32>(16);
            var tris = new List<int>(48);
            var white = new Color32(255, 255, 255, 255);

            void AddDoubleQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
            {
                int b = verts.Count;
                verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(1f, 0f));
                cols.Add(white); cols.Add(white); cols.Add(white); cols.Add(white);
                tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
            }

            float[] angles = { 0f, 60f, 120f };
            for (int i = 0; i < angles.Length; i++)
            {
                float rad = angles[i] * Mathf.Deg2Rad;
                float cx = Mathf.Cos(rad) * 0.5f;
                float cz = Mathf.Sin(rad) * 0.5f;
                AddDoubleQuad(
                    new Vector3(-cx, -0.5f, -cz),
                    new Vector3(-cx,  0.5f, -cz),
                    new Vector3( cx,  0.5f,  cz),
                    new Vector3( cx, -0.5f,  cz));
            }
            // 水平赤道面：从高仰角或低仰角观察灯头时同样饱满圆润
            AddDoubleQuad(
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3( 0.5f, 0f, -0.5f));

            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>构造三向交叉双面竖直火舌网格（底边锚定在 y=0，顶边在 y=1），供火焰 Shader 绘制。</summary>
        private static Mesh BuildFlameMesh()
        {
            var m = new Mesh { name = "Fps3D_FlameMesh" };
            var verts = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);
            float[] angles = { 0f, 60f, 120f };
            for (int i = 0; i < angles.Length; i++)
            {
                float rad = angles[i] * Mathf.Deg2Rad;
                float cx = Mathf.Cos(rad) * 0.5f;
                float cz = Mathf.Sin(rad) * 0.5f;
                int b = verts.Count;
                verts.Add(new Vector3(-cx, 0f, -cz));
                verts.Add(new Vector3(-cx, 1f, -cz));
                verts.Add(new Vector3( cx, 1f,  cz));
                verts.Add(new Vector3( cx, 0f,  cz));
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                // 正反双面，保证任意第一人称视角下火舌均可见
                tris.Add(b + 0); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 2);
            }
            m.SetVertices(verts);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
