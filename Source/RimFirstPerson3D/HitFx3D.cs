using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 受击反馈（F014）—— Minecraft 口径
    // ============================================================
    //
    // 生物：受伤后 0.5 s 内把同一份几何再用红色画一遍（MC 的 hurtTime 就是这么做的）。
    //      本模组没有自定义着色器，只有「贴图 × 材质色 × 顶点色」这条乘子链，
    //      所以走 Transparent 叠层而不是改乘子：乘子只能压暗，压不出「发红的亮」。
    // 玩家：屏幕四边压出红色暗角，0.9 s 带脉冲退掉；残血时常驻一层薄红边
    //      （MC 剩一两颗心时屏幕边缘一直泛红，与瞬时受击是两回事，分开算再取大）。
    //
    // 触发只有一个真值：Pawn.PostApplyDamage 的 Harmony 后置。不轮询血量、不自算伤害，
    // 也不看 hitPoints 变化 —— 那会把「回血」「处决」「出生即伤」都当成受击。
    // 曲线与常数在 HitFxMath.cs（纯函数，离线台 tools/LightSim 链接同一份跑判据）。
    // 规范：docs/features/F014-hit-feedback.md
    public static class HitFx3D
    {
        /// <summary>红闪浓度分档：克隆材质按档缓存，一屏同时挂彩的 pawn 不会很多。</summary>
        private const int BucketN = 8;

        private static readonly Color HurtTint = new Color(1f, 0.12f, 0.12f, 1f);
        private static readonly Color VignetteTint = new Color(1f, 0.08f, 0.08f, 1f);

        // thingIDNumber -> (上次受伤时刻, 峰值)。时刻用 Time.realtimeSinceStartup：
        // 墙钟，暂停也照退，不会像 TicksGame 那样在暂停时把红闪冻住。
        private static readonly Dictionary<int, Hit> hits = new Dictionary<int, Hit>();
        private static readonly List<int> stale = new List<int>();
        private static readonly Dictionary<long, Material> overlays = new Dictionary<long, Material>();

        private static Texture2D vignette;
        private static int vigW, vigH;
        private static float lastPrune = -1f;
        private static float playerAt = -1f;
        private static float playerPeak;

        private struct Hit { public float at; public float peak; }

        public static bool On
        {
            get
            {
                var s = FirstPerson3DMod.settings;
                return s == null || s.hitFx;
            }
        }

        // ---- 触发 ----

        public static void Patch(Harmony harmony)
        {
            var m = AccessTools.Method(typeof(Pawn), "PostApplyDamage");
            if (m == null)
            {
                Log.Warning(ModEntry.LogPrefix + "找不到 Pawn.PostApplyDamage，受击反馈不生效");
                return;
            }
            harmony.Patch(m, postfix: new HarmonyMethod(typeof(HitFx3D), nameof(After)));
        }

        private static void After(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (__instance == null || !On) return;
            float dmg = Mathf.Max(totalDamageDealt, dinfo.Amount);
            if (dmg <= 0f) return;
            NoteHit(__instance.thingIDNumber, HitFxMath.PeakOf(dmg));
        }

        private static void NoteHit(int id, float peak)
        {
            float now = Time.realtimeSinceStartup;
            hits[id] = new Hit { at = now, peak = Mathf.Max(peak, 0.55f) };
            var me = CameraRig3D.Possessed;
            if (me != null && me.thingIDNumber == id)
            {
                // 同一窗口里连挨两下：取更狠的那次的峰值，时刻刷新
                playerPeak = Mathf.Max(playerPeak, peak);
                playerAt = now;
            }
        }

        /// <summary>调试动作：不经过伤害管线，直接点亮某只 pawn（附身者）的红闪 / 暗角。</summary>
        public static void Force(Pawn p, float peak)
        {
            if (p == null) return;
            NoteHit(p.thingIDNumber, Mathf.Clamp01(peak));
        }

        // ---- 生物红闪 ----

        /// <summary>该 pawn 当前的红闪强度（0 = 不罩红）。</summary>
        public static float Strength(Pawn p)
        {
            if (p == null || !On) return 0f;
            if (!hits.TryGetValue(p.thingIDNumber, out var h)) return 0f;
            return Mathf.Clamp01(HitFxMath.Hurt(Time.realtimeSinceStartup - h.at, h.peak) * Scale());
        }

        /// <summary>浓度倍率：只缩放结果，不改曲线 —— 曲线本身在 HitFxMath，离线台验的就是它。</summary>
        private static float Scale()
        {
            var s = FirstPerson3DMod.settings;
            return s == null ? 1f : Mathf.Clamp(s.hitFxScale, 0f, 2f);
        }

        /// <summary>
        /// 把红闪叠在刚画完的同一份几何上：同 mesh、同 matrix、同贴图，只换材质。
        /// 调用点画完正常一层再画这一层，所以叠出来是「原来的样子 + 一层红纱」，
        /// 描边壳（hull）不参与 —— 给它罩红只会把轮廓糊掉。
        /// </summary>
        public static void DrawOverlay(Mesh mesh, Matrix4x4 matrix, Material src, float k)
        {
            if (k <= 0f || mesh == null || src == null) return;
            var m = OverlayMat(src, k);
            if (m == null) return;
            Graphics.DrawMesh(mesh, matrix, m, 0);
            PerfCounters.HitFxDraws++;
        }

        private static Material OverlayMat(Material src, float k)
        {
            var tex = src.mainTexture;
            if (tex == null) return null;
            int b = Mathf.Clamp(Mathf.CeilToInt(k * BucketN), 1, BucketN);
            long key = ((long)tex.GetInstanceID() << 4) | (uint)b;
            if (overlays.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(ShaderDatabase.Transparent) { name = "Fps3D_HurtFlash" };
            m.mainTexture = tex;
            m.color = new Color(HurtTint.r, HurtTint.g, HurtTint.b, b / (float)BucketN);
            overlays[key] = m;
            return m;
        }

        // ---- 玩家暗角 ----

        /// <summary>全屏红雾：受击脉冲与低血压常驻取大，画在准星与 HUD 之前。</summary>
        public static void DrawVignette()
        {
            if (!On) return;
            var me = CameraRig3D.Possessed;
            if (me == null) return;
            float now = Time.realtimeSinceStartup;
            float flash = playerAt < 0f ? 0f : HitFxMath.Vignette(now - playerAt, playerPeak);
            float low = 0f;
            try
            {
                if (me.health != null && me.health.summaryHealth != null)
                    low = HitFxMath.LowHealth(me.health.summaryHealth.SummaryHealthPercent);
            }
            catch { }
            float a = Mathf.Clamp01(HitFxMath.Combine(flash, low) * Scale());
            if (a <= 0.004f) return;
            EnsureVignette();
            if (vignette == null) return;
            var prev = GUI.color;
            GUI.color = new Color(VignetteTint.r, VignetteTint.g, VignetteTint.b, a * 0.85f);
            GUI.DrawTexture(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), vignette,
                ScaleMode.StretchToFill, true);
            GUI.color = prev;
        }

        // 1/4 屏分辨率足够软（双线性放大），按当前 UI 比例生成；比例变了才重建。
        private static void EnsureVignette()
        {
            int w = Mathf.Max(16, Mathf.RoundToInt(UI.screenWidth * 0.25f));
            int h = Mathf.Max(16, Mathf.RoundToInt(UI.screenHeight * 0.25f));
            if (vignette != null && Mathf.Abs(w - vigW) <= 2 && Mathf.Abs(h - vigH) <= 2) return;
            if (vignette != null) Object.Destroy(vignette);
            vigW = w;
            vigH = h;
            vignette = ProcTex3D.Vignette(w, h, 0.30f, 1.6f);
        }

        // ---- 维护 ----

        /// <summary>过期项定期清，表不会随一晚上几百只 pawn 一直长。</summary>
        public static void Tick(float dt)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastPrune < 2f) return;
            lastPrune = now;
            if (hits.Count > 0)
            {
                stale.Clear();
                foreach (var kv in hits)
                    if (now - kv.Value.at > HitFxMath.HurtDur) stale.Add(kv.Key);
                for (int i = 0; i < stale.Count; i++) hits.Remove(stale[i]);
            }
            if (playerAt >= 0f && now - playerAt > HitFxMath.VignDur)
            {
                playerAt = -1f;
                playerPeak = 0f;
            }
        }

        /// <summary>退出三维：把残留的闪与暗角清掉，别让下一局开局就红一片。</summary>
        public static void Clear()
        {
            hits.Clear();
            playerAt = -1f;
            playerPeak = 0f;
        }

        /// <summary>导出用读数：当前还在闪的个体数与玩家暗角浓度。</summary>
        public static void Snapshot(System.Text.StringBuilder sb)
        {
            float now = Time.realtimeSinceStartup;
            int live = 0;
            foreach (var kv in hits)
                if (HitFxMath.Hurt(now - kv.Value.at, kv.Value.peak) > 0f) live++;
            float pv = playerAt < 0f ? 0f : HitFxMath.Vignette(now - playerAt, playerPeak);
            sb.Append("\"hitfx\":{\"on\":").Append(On ? 1 : 0)
              .Append(",\"flashing\":").Append(live)
              .Append(",\"player\":").Append(pv.ToString("0.###",
                  System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"overlays\":").Append(overlays.Count).Append('}');
        }
    }
}
