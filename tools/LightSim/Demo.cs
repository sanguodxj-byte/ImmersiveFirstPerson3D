using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RimFirstPerson3D.LightSim
{
    // ============================================================
    // 效果演示的帧表导出（F014 受击 + F015 射击）
    // ============================================================
    //
    // 这里只出「数」，不出「图」：每一帧的弹丸位置、拖焰长度与浓度、火光与烟尘的尺寸与
    // 透明度、目标红闪强度、玩家暗角浓度、视图模型枪口火光浓度，全部由游戏里跑的同一份
    // ShotFxMath / HitFxMath / LightShade 算出来写成 JSON。
    // 渲染交给 scripts/shotfx_demo.py —— 它只负责把这份 JSON 画成胶片与 GIF，
    // 不许自己再算一遍衰减，否则演示证明的是脚本而不是模组。
    //
    // 场景坐标是合成的（跟 SyntheticField 的手工墙/灯一个性质）；公式是真的。
    internal static class FxDemo
    {
        private const float Fps = 20f;
        private const int Frames = 30;              // 1.5 秒
        private const float BulletSpeed = 15f;      // 步枪弹速（米/秒）

        // 合成场景：相机在东侧朝西看；NPC 射手在东南，目标在西北，一枪斜穿画面
        private static readonly Vector3 Cam = new Vector3(16f, 1.62f, 9f);   // 站在弹道侧面偏后：一枪从左前斜穿到右远
        private static readonly Vector3 Shooter = new Vector3(12.5f, 0f, 11.5f);
        private static readonly Vector3 Target = new Vector3(3.5f, 0f, 6.5f);
        private static readonly float MuzzleH = Metrics3D.PawnHeight * 0.62f;
        private static readonly float TargetH = 0.95f;

        public static int Run()
        {
            var field = new SyntheticField();
            var t = Program.Tuning();
            var day = Program.Day();

            var muzzle0 = ShotFxMath.MuzzleAt(new Vector3(Shooter.x, MuzzleH, Shooter.z),
                Target - Shooter, 0.32f);
            var impact = new Vector3(Target.x, TargetH, Target.z);
            float path = Vector3.Distance(muzzle0, impact);
            float flight = path / BulletSpeed;
            float playerHitAt = 0.30f;      // 玩家挨一发（暗角）
            float playerFireAt = 1.00f;     // 玩家开火（视图模型枪口火光）

            var sb = new StringBuilder(8192);
            sb.Append("{\"fps\":").Append(Fps.ToString("0.####", CultureInfo.InvariantCulture))
              .Append(",\"flight\":").Append(F(flight))
              .Append(",\"cam\":").Append(V(Cam))
              .Append(",\"target\":").Append(V(Target))
              .Append(",\"shooter\":").Append(V(Shooter))
              .Append(",\"muzzle0\":").Append(V(muzzle0))
              .Append(",\"targetH\":").Append(F(TargetH))
              .Append(",\"tracerThick\":").Append(F(ShotFxMath.TracerThick))
              .Append(",\"hurtDur\":").Append(F(HitFxMath.HurtDur))
              .Append(",\"frames\":[");

            for (int i = 0; i < Frames; i++)
            {
                float now = i / Fps;
                if (i > 0) sb.Append(',');
                sb.Append('{');
                sb.Append("\"t\":").Append(F(now));

                // 场景底色（F013 真公式）：证明效果是叠在受光几何上，不是叠在平涂灰上
                float ground = Luma(LightShade.Shade(field, new Vector3(9f, 0f, 9f),
                    Vector3.up, true, ref day, ref t));
                float body = Luma(LightShade.Placement(field, new Vector3(Target.x, 0f, Target.z),
                    ref day, ref t));
                sb.Append(",\"ground\":").Append(F(ground)).Append(",\"body\":").Append(F(body));

                // 弹丸 + 拖焰
                if (now >= 0f && now <= flight)
                {
                    float frac = Mathf.Clamp01(now / flight);
                    var p = Vector3.Lerp(muzzle0, impact, frac);
                    float len = ShotFxMath.TracerLen(BulletSpeed);
                    var dir = (impact - muzzle0).normalized;
                    var tail = p - dir * len;
                    if (now > ShotFxMath.FlashDur * 0.5f && p.z > impact.z + 0.05f)
                    {
                        sb.Append(",\"bullet\":{\"p\":").Append(V(p))
                          .Append(",\"tail\":").Append(V(tail))
                          .Append(",\"len\":").Append(F(len)).Append('}');
                    }
                }

                // 枪口火光（3D，NPC 那一枪）
                float mk = ShotFxMath.Decay(now, ShotFxMath.FlashDur);
                if (mk > 0f)
                {
                    sb.Append(",\"muzzle\":{\"p\":").Append(V(muzzle0))
                      .Append(",\"alpha\":").Append(F(mk))
                      .Append(",\"size\":").Append(F(0.42f * (0.6f + 0.4f * mk))).Append('}');
                }

                // 落点烟尘
                float tp = now - flight;
                float pa = ShotFxMath.PuffAlpha(tp);
                if (pa > 0f)
                {
                    sb.Append(",\"puff\":{\"p\":").Append(V(impact))
                      .Append(",\"alpha\":").Append(F(pa))
                      .Append(",\"size\":").Append(F(ShotFxMath.PuffSize(tp, 0.30f)))
                      .Append(",\"tint\":[0.78,0.76,0.72]}");
                }

                // 目标红闪（F014）
                float hk = HitFxMath.Hurt(tp, 1f);
                if (hk > 0f)
                    sb.Append(",\"hurt\":{\"p\":").Append(V(impact))
                      .Append(",\"k\":").Append(F(hk)).Append('}');

                // 玩家暗角（F014）：受击脉冲与残血取大
                float vign = HitFxMath.Combine(HitFxMath.Vignette(now - playerHitAt, 1f),
                    HitFxMath.LowHealth(0.30f));
                if (vign > 0f) sb.Append(",\"vignette\":").Append(F(vign));

                // 视图模型枪口火光（F015）：附身者自己开火，浓度走同一条衰减
                float gf = ShotFxMath.Decay(now - playerFireAt, ShotFxMath.FlashDur);
                if (gf > 0f) sb.Append(",\"gunflash\":").Append(F(gf));

                sb.Append('}');
            }
            sb.Append("]}");

            string outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "..", "temp_light"));
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "fx_demo.json");
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine("帧表 -> " + outPath + "（" + Frames + " 帧 @ " + Fps + " fps，"
                + "弹道 " + F(path) + " m / 飞行 " + F(flight) + " s）");
            return 0;
        }

        private static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string V(Vector3 p)
        {
            return "[" + F(p.x) + "," + F(p.y) + "," + F(p.z) + "]";
        }

        private static float Luma(Color c)
        {
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }
    }
}
