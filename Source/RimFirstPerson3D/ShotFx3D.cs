using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 武器射击效果（F015）
    // ============================================================
    //
    // 第一人称压掉了原版绘制（VanillaSuppressor 跳 DynamicDrawManager），弹丸在 3D 里
    // 本来就看不见 —— 所以「射击效果」的第一件事不是加特效，是把原版已有的东西画出来：
    //
    //   弹丸本体   原版贴图，位置取 Projectile.ExactPosition（抛物线弹的覆写也一并白拿）
    //   拖焰       沿弹道方向的相机朝向缎带，长度 = 弹速 × TracerHold，加法混合
    //   枪口火光   发射点沿弹道前移一段（不挪会埋在发射者躯干里），加法面片
    //   落点烟尘   Impact 处一朵边淡边长的爆点；打在 pawn 上偏红，打在墙上偏灰
    //   视图模型   附身者开火时，在枪口锚点（F013 视图模型那套变换算出来的）亮一下
    //
    // 触发只有两个真值：Projectile.Launch 与 Projectile.Impact 的 Harmony 后置。
    // 不自算弹道、不自算命中 —— 那两个原版都有，抄一份就是第二套规范。
    // 曲线与长度在 ShotFxMath.cs（纯函数，离线台链接同一份）。
    // 规范：docs/features/F015-shot-effects.md
    public static class ShotFx3D
    {
        private const int MaxEvents = 96;         // 事件上限：一场大战也别把列表撑爆
        private const int BucketN = 8;            // 加法材质按浓度分档缓存
        private const float MuzzleForward = 0.32f; // 枪口前移（米）

        private static readonly Color FlashTint = new Color(1.0f, 0.78f, 0.34f);
        private static readonly Color TracerTint = new Color(1.0f, 0.86f, 0.55f);
        private static readonly Color DustTint = new Color(0.78f, 0.76f, 0.72f);
        private static readonly Color BloodTint = new Color(0.85f, 0.16f, 0.14f);

        private struct Flash { public int map; public Vector3 at; public float t; }
        private struct Puff { public int map; public Vector3 at; public float t; public float size; public Color tint; }

        private static readonly List<Flash> flashes = new List<Flash>();
        private static readonly List<Puff> puffs = new List<Puff>();
        private static readonly Dictionary<long, Material> addMats = new Dictionary<long, Material>();
        private static Texture2D radial;
        private static Mesh quad;

        private static float playerFlashAt = -1f;
        private static float lastPrune = -1f;
        private static int tracerDrawn, flashDrawn, puffDrawn, projDrawn;

        public static bool On
        {
            get
            {
                var s = FirstPerson3DMod.settings;
                return s == null || s.shotFx;
            }
        }

        private static float Scale()
        {
            var s = FirstPerson3DMod.settings;
            return s == null ? 1f : Mathf.Clamp(s.shotFxScale, 0f, 2f);
        }

        // ---- 触发 ----

        public static void Patch(Harmony harmony)
        {
            // RimWorld 1.6 同时有“目标版”和“带 origin 的实际发射版”两个
            // Launch 重载。不能只按名称查找，否则 Harmony 会抛
            // AmbiguousMatchException，进而阻断整个 ModEntry 静态初始化。
            var launch = AccessTools.Method(typeof(Projectile), "Launch", new[]
            {
                typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo),
                typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool),
                typeof(Thing), typeof(ThingDef)
            });
            if (launch != null)
                harmony.Patch(launch, postfix: new HarmonyMethod(typeof(ShotFx3D), nameof(AfterLaunch)));
            else
                Log.Warning(ModEntry.LogPrefix + "找不到 Projectile.Launch，枪口火光与视图模型火光不生效");

            var impact = AccessTools.Method(typeof(Projectile), "Impact");
            if (impact != null)
                harmony.Patch(impact, postfix: new HarmonyMethod(typeof(ShotFx3D), nameof(AfterImpact)));
            else
                Log.Warning(ModEntry.LogPrefix + "找不到 Projectile.Impact，落点烟尘不生效");
        }

        private static void AfterLaunch(Projectile __instance, Thing launcher, Vector3 origin)
        {
            if (!On || __instance == null || launcher == null) return;
            var map = launcher.Map;
            if (map == null) return;
            // 高度：原版 origin.y 是绘制层海拔（子弹层 ≈ 十几米），本模组的 y 是「离地几米」，
            // 两者不能混用 —— 按发射者的体型给一个枪口高，动物低、机甲高。
            float body = 1f;
            try { var p = launcher as Pawn; if (p != null) body = Mathf.Max(0.35f, p.RaceProps.baseBodySize); }
            catch { }
            var at = new Vector3(origin.x, Metrics3D.PawnHeight * 0.62f * body, origin.z);
            var dir = SafeDir(__instance);
            AddFlash(map.uniqueID, ShotFxMath.MuzzleAt(at, dir, MuzzleForward));
            var me = CameraRig3D.Possessed;
            if (me != null && (object)me == (object)launcher) playerFlashAt = Time.realtimeSinceStartup;
        }

        private static void AfterImpact(Projectile __instance, Thing hitThing)
        {
            if (!On || __instance == null) return;
            var map = __instance.Map;
            if (map == null) return;
            var dp = __instance.DrawPos;
            var at = new Vector3(dp.x, Mathf.Max(0.05f, dp.y), dp.z);
            float dmg = 1f;
            try { dmg = __instance.DamageAmount; } catch { }
            float size = Mathf.Clamp(0.26f + dmg * 0.012f, 0.26f, 0.95f);
            bool onPawn = hitThing is Pawn;
            AddPuff(map.uniqueID, at, size, onPawn ? BloodTint : DustTint);
        }

        private static Vector3 SafeDir(Projectile p)
        {
            try { return p.ExactRotation * Vector3.forward; } catch { return Vector3.zero; }
        }

        private static void AddFlash(int mapId, Vector3 at)
        {
            if (flashes.Count >= MaxEvents) flashes.RemoveAt(0);
            flashes.Add(new Flash { map = mapId, at = at, t = Time.realtimeSinceStartup });
        }

        private static void AddPuff(int mapId, Vector3 at, float size, Color tint)
        {
            if (puffs.Count >= MaxEvents) puffs.RemoveAt(0);
            puffs.Add(new Puff { map = mapId, at = at, t = Time.realtimeSinceStartup, size = size, tint = tint });
        }

        /// <summary>视图模型枪口火光浓度（0 = 没在响）。由 Viewmodel3D 在枪口锚点上画。</summary>
        public static float PlayerFlash
        {
            get
            {
                if (!On || playerFlashAt < 0f) return 0f;
                return Mathf.Clamp01(ShotFxMath.Decay(Time.realtimeSinceStartup - playerFlashAt,
                    ShotFxMath.FlashDur) * Scale());
            }
        }

        // ---- 绘制 ----

        /// <summary>每张图各调一次：画本图的弹丸、火光与烟尘。坐标口径与 pawn 层一致。</summary>
        public static void DrawMap(Map map, float baseY, Vector3 worldOffset)
        {
            tracerDrawn = flashDrawn = puffDrawn = projDrawn = 0;
            if (!On || map == null) return;
            if (!EnsureAssets()) return;
            var camPos = CamPos;
            int id = map.uniqueID;

            DrawProjectiles(map, baseY, worldOffset, camPos);
            DrawFlashes(id, baseY, worldOffset, camPos);
            DrawPuffs(id, baseY, worldOffset, camPos);
        }

        private static void DrawProjectiles(Map map, float baseY, Vector3 wo, Vector3 camPos)
        {
            List<Thing> list = null;
            try { list = map.listerThings.ThingsInGroup(ThingRequestGroup.Projectile); } catch { }
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i] as Projectile;
                if (p == null || p.Destroyed) continue;
                var dp = p.DrawPos;
                var wp = new Vector3(dp.x + wo.x, baseY + Mathf.Max(0f, dp.y), dp.z + wo.z);
                if (!Frustum3D.VisiblePoint(wp, 1.5f)) continue;
                var dir = SafeDir(p);

                // 本体：弹丸自己的贴图材质（图集子块在 mainTextureScale/Offset 上，单位面片正好采到）
                Material pm = null;
                try { pm = p.DrawMat; } catch { }
                if (pm != null && pm.mainTexture != null)
                {
                    float s = 0.3f;
                    try
                    {
                        var gd = p.def != null ? p.def.graphicData : null;
                        if (gd != null) s = Mathf.Clamp(gd.drawSize.x, 0.12f, 1.2f);
                    }
                    catch { }
                    var rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(wp - camPos) : Quaternion.identity;
                    Graphics.DrawMesh(quad, Matrix4x4.TRS(wp - Vector3.up * (s * 0.5f), rot,
                        new Vector3(s, s, 1f)), pm, 0);
                    projDrawn++;
                }

                // 拖焰：沿弹道的相机朝向缎带
                float speed = 0f;
                try { if (p.def != null && p.def.projectile != null) speed = p.def.projectile.speed; } catch { }
                float len = ShotFxMath.TracerLen(speed) * Scale();
                if (len < 0.02f || dir.sqrMagnitude < 1e-6f) continue;
                var vhat = dir.normalized;
                var away = (wp - camPos);
                if (away.sqrMagnitude < 1e-4f) continue;
                away.Normalize();
                if (Mathf.Abs(Vector3.Dot(vhat, away)) > 0.995f) continue;   // 正对相机：缎带退化成一条视线，交给本体
                var right = Vector3.Cross(vhat, away).normalized;
                var tail = wp - vhat * len;
                var m = new Matrix4x4(
                    new Vector4(right.x * ShotFxMath.TracerThick, right.y * ShotFxMath.TracerThick, right.z * ShotFxMath.TracerThick, 0f),
                    new Vector4(vhat.x * len, vhat.y * len, vhat.z * len, 0f),
                    new Vector4(away.x, away.y, away.z, 0f),
                    new Vector4(tail.x, tail.y, tail.z, 1f));
                Graphics.DrawMesh(quad, m, AddMat(TracerTint, 1f), 0);
                tracerDrawn++;
            }
        }

        private static void DrawFlashes(int mapId, float baseY, Vector3 wo, Vector3 camPos)
        {
            for (int i = 0; i < flashes.Count; i++)
            {
                var f = flashes[i];
                if (f.map != mapId) continue;
                float k = ShotFxMath.Decay(Time.realtimeSinceStartup - f.t, ShotFxMath.FlashDur) * Scale();
                if (k <= 0f) continue;
                var wp = new Vector3(f.at.x + wo.x, baseY + f.at.y, f.at.z + wo.z);
                if (!Frustum3D.VisiblePoint(wp, 2f)) continue;
                float s = 0.42f * (0.6f + 0.4f * k);
                DrawBillboardAdditive(wp, s, FlashTint, k);
                flashDrawn++;
            }
        }

        private static void DrawPuffs(int mapId, float baseY, Vector3 wo, Vector3 camPos)
        {
            float now = Time.realtimeSinceStartup;
            for (int i = 0; i < puffs.Count; i++)
            {
                var q = puffs[i];
                if (q.map != mapId) continue;
                float t = now - q.t;
                float a = ShotFxMath.PuffAlpha(t) * Scale();
                if (a <= 0f) continue;
                float s = ShotFxMath.PuffSize(t, q.size);
                var wp = new Vector3(q.at.x + wo.x, baseY + q.at.y, q.at.z + wo.z);
                if (!Frustum3D.VisiblePoint(wp, 2f)) continue;
                DrawBillboardAdditive(wp, s, q.tint, a);
                puffDrawn++;
            }
        }

        private static void DrawBillboardAdditive(Vector3 wp, float size, Color tint, float k)
        {
            var look = wp - CamPos;
            if (look.sqrMagnitude < 1e-6f) return;
            var rot = Quaternion.LookRotation(look);
            Graphics.DrawMesh(quad, Matrix4x4.TRS(wp - Vector3.up * (size * 0.5f), rot,
                new Vector3(size, size, 1f)), AddMat(tint, k), 0);
        }

        /// <summary>相机位置：优先当场取，取不到用本帧记下的（DrawMap 在 UpdateCamera 之后）。</summary>
        private static Vector3 CamPos
        {
            get
            {
                var c = CameraRig3D.Cam;
                if (c != null) return c.transform.position;
                return CameraRig3D.LastCamPos;
            }
        }

        // ---- 资源与材质 ----

        /// <summary>视图模型枪口火光用的 GUI 贴图：白心透明边（非预乘，IMGUI 是普通 alpha 混合）。</summary>
        private static Texture2D guiTex;
        public static Texture2D GuiFlashTex
        {
            get
            {
                if (guiTex == null) guiTex = ProcTex3D.Radial(64, 0.06f, 1.5f);
                return guiTex;
            }
        }

        private static bool EnsureAssets()
        {
            if (quad == null) quad = Util3D.BillboardQuad;
            if (radial == null) radial = ProcTex3D.AdditiveRadial(64);
            return radial != null && quad != null;
        }

        /// <summary>加法材质按「色调 × 浓度」分档缓存：加法混合里颜色本身就是亮度。</summary>
        private static Material AddMat(Color tint, float k)
        {
            float q = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(k) * BucketN) / (float)BucketN, 1f / BucketN, 1f);
            int qr = Quant(tint.r * q), qg = Quant(tint.g * q), qb = Quant(tint.b * q);
            long key = ((long)qr << 20) | ((long)qg << 10) | (uint)qb;
            if (addMats.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(AdditiveShader()) { name = "Fps3D_ShotFx", renderQueue = 2960 };
            m.mainTexture = radial;
            m.color = new Color(qr / 255f, qg / 255f, qb / 255f, 1f);
            addMats[key] = m;
            return m;
        }

        private static int Quant(float v) { return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * 255f), 0, 255); }

        private static Shader addShader;
        private static Shader AdditiveShader()
        {
            if (addShader != null) return addShader;
            try { addShader = ShaderDatabase.WorldOverlayAdditive; } catch { }
            if (addShader == null) try { addShader = ShaderDatabase.MoteGlow; } catch { }
            if (addShader == null) try { addShader = ShaderDatabase.Transparent; } catch { }
            return addShader;
        }

        // ---- 维护 ----

        public static void Tick(float dt)
        {
            float now = Time.realtimeSinceStartup;
            if (now - lastPrune < 1f) return;
            lastPrune = now;
            for (int i = flashes.Count - 1; i >= 0; i--)
                if (ShotFxMath.Decay(now - flashes[i].t, ShotFxMath.FlashDur) <= 0f) flashes.RemoveAt(i);
            for (int i = puffs.Count - 1; i >= 0; i--)
                if (ShotFxMath.PuffAlpha(now - puffs[i].t) <= 0f) puffs.RemoveAt(i);
        }

        public static void Clear()
        {
            flashes.Clear();
            puffs.Clear();
            playerFlashAt = -1f;
        }

        /// <summary>导出用读数：当帧画了多少、还压着多少事件、材质缓存多大。</summary>
        public static void Snapshot(System.Text.StringBuilder sb)
        {
            sb.Append("\"shotfx\":{\"on\":").Append(On ? 1 : 0)
              .Append(",\"projectiles\":").Append(projDrawn)
              .Append(",\"tracers\":").Append(tracerDrawn)
              .Append(",\"flashes\":").Append(flashDrawn)
              .Append(",\"puffs\":").Append(puffDrawn)
              .Append(",\"queued\":").Append(flashes.Count + puffs.Count)
              .Append(",\"mats\":").Append(addMats.Count).Append('}');
        }
    }
}
