// AI生成
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // 第一人称手持视图模型：
    //  空手 -> 由 Hand3D 画右下角"手"，左键向前挥拳；
    //  持械 -> 隐藏手，在镜头右下斜置武器贴图（取材武器 def 的原版图标），
    //          斜对着玩家摄像头摆放，左键挥砍/射击时前推并旋转。
    // 视图模型全部画在 GUI 覆盖层（逻辑坐标空间），挥击状态由本类统一驱动。
    public static class Viewmodel3D
    {
        private const float SwingDur = 0.26f; // 单次挥击时长（秒）
        private static float attackT = -1f;   // <0 = 静止

        // 附身者当前是否持械（装备栏有主武器）。
        public static bool Armed
        {
            get
            {
                var pawn = CameraRig3D.Possessed;
                return pawn != null && pawn.equipment != null && pawn.equipment.Primary != null;
            }
        }

        // 左键/攻击触发一次挥击动画（空手挥拳与持械挥砍/射击后座共用）。
        public static void TriggerAttack() { attackT = 0f; }

        public static void Tick(float dt)
        {
            if (attackT >= 0f)
            {
                attackT += dt;
                if (attackT > SwingDur) attackT = -1f;
            }
        }

        // 0=静止 1=挥到底。
        public static float SwingPhase => attackT < 0f ? 0f : Mathf.Clamp01(attackT / SwingDur);

        // 挥击平移量（屏幕像素，logical 空间）：先快速向左上"前推"再回落。
        public static Vector2 SwingOffset(float amp)
        {
            float p = SwingPhase;
            if (p <= 0f) return Vector2.zero;
            float k = Mathf.Sin(p * Mathf.PI);
            return new Vector2(-amp * k, -amp * 0.45f * k);
        }

        // 挥击附加旋转角（度）。
        public static float SwingAngle(float amp)
        {
            float p = SwingPhase;
            if (p <= 0f) return 0f;
            return -amp * Mathf.Sin(p * Mathf.PI);
        }

        // 持械与搬运物视图：武器握把/物品定位在手部位置（Hand3D.HandCenter），
        // 武器头部/枪口朝向准星（屏幕中心）。
        // 原版武器图标枪口/头部朝右，水平镜像后朝左且顶面依旧朝上，
        // 沿握把到枪口的轴向精准对准准星方向旋转，实现第一人称手持观感。
        private const float WeaponLenFrac = 0.48f;   // 武器长度占屏高的比例

        private struct WeaponAnchors
        {
            public Vector2 muzzleUv; // (u, v) 归一化枪口/武器头部位置（左侧）
            public Vector2 gripUv;   // (u, v) 归一化握把位置（右侧）
        }

        private static readonly Dictionary<Texture2D, WeaponAnchors> anchorCache = new Dictionary<Texture2D, WeaponAnchors>();

        private static WeaponAnchors GetAnchors(Texture2D tex)
        {
            if (tex == null) return new WeaponAnchors { muzzleUv = new Vector2(0.08f, 0.5f), gripUv = new Vector2(0.85f, 0.55f) };
            if (anchorCache.TryGetValue(tex, out var cached)) return cached;
            var res = new WeaponAnchors { muzzleUv = new Vector2(0.08f, 0.5f), gripUv = new Vector2(0.85f, 0.55f) };
            try
            {
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();
                int minX = w, maxX = -1;
                int minX_ySum = 0, minX_count = 0;
                int maxX_ySum = 0, maxX_count = 0;
                for (int y = 0; y < h; y++)
                {
                    int gy = h - 1 - y; // GUI 空间 y 向下
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        if (px[row + x].a > 30)
                        {
                            if (x < minX) { minX = x; minX_ySum = gy; minX_count = 1; }
                            else if (x == minX) { minX_ySum += gy; minX_count++; }

                            if (x > maxX) { maxX = x; maxX_ySum = gy; maxX_count = 1; }
                            else if (x == maxX) { maxX_ySum += gy; maxX_count++; }
                        }
                    }
                }
                if (maxX > minX)
                {
                    float my = minX_count > 0 ? (float)minX_ySum / minX_count : h * 0.5f;
                    float gy = maxX_count > 0 ? (float)maxX_ySum / maxX_count : h * 0.5f;
                    res.muzzleUv = new Vector2((float)minX / w, my / h);
                    float gx = Mathf.Lerp(minX, maxX, 0.82f);
                    res.gripUv = new Vector2(gx / w, gy / h);
                }
            }
            catch { }
            anchorCache[tex] = res;
            return res;
        }

        // 水平镜像贴图：使枪口朝左、握把朝右，且枪身顶面朝上保持不变
        private static readonly Dictionary<Texture2D, Texture2D> mirrorCache = new Dictionary<Texture2D, Texture2D>();

        private static Texture2D MirrorHorizontal(Texture2D src)
        {
            if (src == null) return null;
            if (mirrorCache.TryGetValue(src, out var cached) && cached != null) return cached;
            Texture2D readable = src;
            RenderTexture rt = null;
            try
            {
                if (!src.isReadable)
                {
                    rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(src, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                    readable.Apply(false, false);
                    RenderTexture.active = prev;
                }
                int w = readable.width, h = readable.height;
                var px = readable.GetPixels32();
                var outPx = new Color32[px.Length];
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x++)
                    {
                        outPx[row + x] = px[row + (w - 1 - x)];
                    }
                }
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
                dst.SetPixels32(outPx);
                dst.Apply(false, false);
                dst.filterMode = src.filterMode;
                dst.wrapMode = src.wrapMode;
                mirrorCache[src] = dst;
                return dst;
            }
            catch
            {
                return src;
            }
            finally
            {
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
            }
        }

        public static void DrawWeapon()
        {
            if (!Armed) return;
            var pawn = CameraRig3D.Possessed;
            var eq = pawn != null && pawn.equipment != null ? pawn.equipment.Primary : null;
            if (eq == null || eq.def == null) return;

            var prevMatrix = GUI.matrix;
            var prevColor = GUI.color;
            GUI.color = SampleHandLight(pawn);

            var raw = eq.def.uiIcon;
            var tex = MirrorHorizontal(raw);
            if (tex == null || tex.width <= 0)
            {
                float s0 = UI.screenHeight * 0.30f;
                Widgets.ThingIcon(new Rect(UI.screenWidth * 0.5f - s0 * 0.5f,
                    UI.screenHeight * 0.5f - s0 * 0.5f, s0, s0), eq);
                GUI.color = prevColor;
                GUI.matrix = prevMatrix;
                return;
            }

            float W = UI.screenHeight * WeaponLenFrac;
            float Hs = W * tex.height / (float)tex.width;
            var anchors = GetAnchors(tex);

            Vector2 handPos = Hand3D.HandCenter;
            Vector2 crosshair = new Vector2(UI.screenWidth * 0.5f, UI.screenHeight * 0.5f);
            Vector2 toCrosshair = crosshair - handPos;

            Vector2 gripLocal = new Vector2(anchors.gripUv.x * W, anchors.gripUv.y * Hs);
            Vector2 muzzleLocal = new Vector2(anchors.muzzleUv.x * W, anchors.muzzleUv.y * Hs);
            Vector2 axis = muzzleLocal - gripLocal;

            float localAngle = Mathf.Atan2(axis.y, axis.x) * Mathf.Rad2Deg;
            float targetAngle = Mathf.Atan2(toCrosshair.y, toCrosshair.x) * Mathf.Rad2Deg;
            float rotAngle = targetAngle - localAngle;

            float off = SwingPhase > 0f ? Mathf.Sin(SwingPhase * Mathf.PI) : 0f;
            float recoil = off * W * 0.08f;
            Vector2 swingOff = SwingOffset(UI.screenHeight * 0.03f);
            Vector2 gripScreen = handPos - toCrosshair.normalized * recoil + swingOff;
            float finalAngle = rotAngle + SwingAngle(8f);

            // 1. 先在握把位置绘制持械手部
            Hand3D.DrawHandAt(gripScreen + new Vector2(W * 0.02f, Hs * 0.03f),
                UI.screenHeight * Hand3D.SizeFrac * 0.85f, GUI.color);

            // 2. 以握把在屏幕上的位置为枢轴旋转武器，使枪口朝向准星
            Matrix4x4 m = Matrix4x4.TRS(gripScreen, Quaternion.Euler(0f, 0f, finalAngle), Vector3.one)
                        * Matrix4x4.TRS(-gripLocal, Quaternion.identity, Vector3.one);
            GUI.matrix = prevMatrix * m;
            GUI.DrawTexture(new Rect(0f, 0f, W, Hs), tex, ScaleMode.StretchToFill, true);
            GUI.matrix = prevMatrix;

            // 3. 枪口火光：直接使用变换后的枪口屏幕坐标
            float pf = ShotFx3D.PlayerFlash;
            if (pf > 0f)
            {
                var ft = ShotFx3D.GuiFlashTex;
                if (ft != null)
                {
                    Vector3 muzScreen3D = m.MultiplyPoint3x4(new Vector3(muzzleLocal.x, muzzleLocal.y, 0f));
                    Vector2 muzScreen = new Vector2(muzScreen3D.x, muzScreen3D.y);
                    float fs = UI.screenHeight * 0.17f * (0.55f + 0.45f * pf);
                    GUI.color = new Color(1f, 0.82f, 0.42f, pf);
                    GUI.DrawTexture(new Rect(muzScreen.x - fs * 0.5f, muzScreen.y - fs * 0.5f, fs, fs),
                        ft, ScaleMode.StretchToFill, true);
                    GUI.color = prevColor;
                }
            }

            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        // 第一人称搬运物视图：定位在手部位置，与手部动作共振
        public static void DrawCarried()
        {
            if (Fps3DState.Mode != ViewMode.FirstPerson) return;
            var pawn = CameraRig3D.Possessed;
            if (pawn == null || pawn.carryTracker == null) return;
            Thing carried = null;
            try { carried = pawn.carryTracker.CarriedThing; } catch { }
            if (carried == null || carried.Destroyed) return;

            var prevColor = GUI.color;
            var prevMatrix = GUI.matrix;
            GUI.color = SampleHandLight(pawn);

            Vector2 handPos = Hand3D.HandCenter;
            var off = SwingOffset(UI.screenHeight * 0.04f);
            float s = UI.screenHeight * 0.22f;
            Vector2 itemPos = handPos + off + new Vector2(-s * 0.12f, -s * 0.12f);

            // 绘制搬运托持手部
            Hand3D.DrawHandAt(handPos + off + new Vector2(s * 0.1f, s * 0.15f),
                UI.screenHeight * Hand3D.SizeFrac * 0.85f, GUI.color);

            var rect = new Rect(itemPos.x - s * 0.5f, itemPos.y - s * 0.5f, s, s);
            try { Widgets.ThingIcon(rect, carried); } catch { }

            GUI.color = prevColor;
            GUI.matrix = prevMatrix;
        }

        /// <summary>第一人称手部与武器的环境受光乘子：采样附身角色所在格的放置亮度，白昼还原接近 1，夜里与室内随环境变暗并吃灯具色相。</summary>
        public static Color SampleHandLight(Pawn pawn)
        {
            try
            {
                if (!Lighting3D.Enabled || pawn == null || !pawn.Spawned || pawn.Map == null) return Color.white;
                var map = pawn.Map;
                var sc = SceneRegistry.GetOrCreate(map);
                if (sc == null || sc.Light == null) return Color.white;
                Lighting3D.BeginMap(map);
                var dp = pawn.DrawPos;
                var c = sc.Light.Placement(new Vector3(dp.x, sc.baseY, dp.z));
                // Placement 在正午露天约为 0.54，归一化到 1.0 使白昼手部保持原色，夜里下探到 ~0.26
                float k = 1.65f;
                return new Color(
                    Mathf.Clamp(c.r * k, Lighting3D.LightFloor, 1f),
                    Mathf.Clamp(c.g * k, Lighting3D.LightFloor, 1f),
                    Mathf.Clamp(c.b * k, Lighting3D.LightFloor, 1f),
                    1f);
            }
            catch
            {
                return Color.white;
            }
        }
    }
}