// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    /// <summary>
    /// 墙体、矮墙掩体、围栏与穿墙/壁挂建筑 3D 程序化建模系统（WallModels3D）。
    ///
    /// - 贴图全部取自 scripts/wall_atlas.py 生成的 Textures\FPS3D\WallAtlas.png（WallUVGen.R），
    ///   保留原版像素颗粒（FilterMode.Point）并叠加原版 Stuff / GraphicData 颜色。
    /// - 满格墙体（建造墙、平滑岩墙、加固防爆墙、飞船气密船体墙、虫穴墙、冰墙、迷宫灰墙、
    ///   虚空金属刺墙、扭曲血肉墙、穿墙通风口、穿墙制冷机）按暴露面剔除（仅向可见外立面发射墙面与 3D 浮雕结构）。
    /// - 矮墙掩体与围栏（沙袋、路障、木/石栅栏、金属栅栏、远古混凝土围栏）根据四向正交邻居
    ///   （北/南/西/东）自动生成中心立柱与连接横梁/沙袋臂。
    /// - 壁挂灯具（壁灯 WallLamp、壁炬 TorchWallLamp）按朝向贴合在相邻墙体的可见外立面上（离地 1.65m）。
    /// </summary>
    [StaticConstructorOnStartup]
    public static class WallModels3D
    {
        public enum WallArch
        {
            None = 0,
            // 满格墙体（高 2.70m，天然冰 2.92m）
            WallBricks,
            WallPlanks,
            WallSmooth,
            RockSmooth,
            FortifiedWall,
            GravshipHull,
            BurrowWall,
            SmoothedIce,
            SolidIce,
            GrayWall,
            VoidmetalWall,
            FleshmassWall,
            // 穿墙 1x1x1 设备（高 1.00m，上方空隙由两侧相邻墙体填充）
            Vent,
            Cooler,
            // 四向连接矮墙掩体与围栏
            Sandbags,
            Barricade,
            FenceSmooth,
            FenceMetal,
            AncientFence,
            // 壁挂件（1x1 格内统一高度 Y = 2.00m）与火把木柄
            WallLamp,
            WallTorch,
            TorchPost,
        }

        public const int UV_TOP = 0;
        public const int UV_SIDE = 1;
        public const int UV_DETAIL = 2;
        public const int UV_EXTRA = 3;

        /// <summary>识别给定建筑（或其蓝图/框架）属于哪一类墙体/掩体/穿墙/壁挂原型。</summary>
        public static WallArch ArchOf(Thing t)
        {
            if (t == null || t.def == null) return WallArch.None;
            if (t is Blueprint bp && bp.EntityToBuild() is ThingDef bpDef)
                return ArchOfDef(bpDef, bp.EntityToBuildStuff());
            if (t is Frame fr && fr.BuildDef != null)
                return ArchOfDef(fr.BuildDef, fr.EntityToBuildStuff());
            return ArchOfDef(t.def, t.Stuff);
        }

        public static WallArch ArchOfDef(ThingDef def, ThingDef stuff = null)
        {
            if (def == null) return WallArch.None;
            string dn = def.defName ?? string.Empty;

            if (def == ThingDefOf.Wall)
            {
                string ap = StuffAppearance(stuff);
                if (ap == "bricks") return WallArch.WallBricks;
                if (ap == "planks") return WallArch.WallPlanks;
                return WallArch.WallSmooth;
            }

            switch (dn)
            {
                case "SmoothedSandstone":
                case "SmoothedGranite":
                case "SmoothedLimestone":
                case "SmoothedSlate":
                case "SmoothedMarble":
                case "SmoothedVacstone":
                    return WallArch.RockSmooth;

                case "AncientFortifiedWall":
                case "OrbitalAncientFortifiedWall":
                    return WallArch.FortifiedWall;

                case "GravshipHull":
                    return WallArch.GravshipHull;

                case "BurrowWall":
                    return WallArch.BurrowWall;

                case "SmoothedSolidIce":
                    return WallArch.SmoothedIce;

                case "SolidIce":
                    return WallArch.SolidIce;

                case "GrayWall":
                    return WallArch.GrayWall;

                case "VoidmetalWall":
                    return WallArch.VoidmetalWall;

                case "Fleshmass":
                case "Fleshmass_Active":
                    return WallArch.FleshmassWall;

                case "Vent":
                    return WallArch.Vent;

                case "Cooler":
                    return WallArch.Cooler;

                case "Sandbags":
                    return WallArch.Sandbags;

                case "Barricade":
                    return WallArch.Barricade;

                case "Fence":
                    return StuffAppearance(stuff) == "metal" ? WallArch.FenceMetal : WallArch.FenceSmooth;

                case "AncientFence":
                    return WallArch.AncientFence;

                case "WallLamp":
                case "AncientEmergencyLight_Red":
                case "AncientEmergencyLight_Blue":
                case "AncientEmergencyLight_Green":
                    return WallArch.WallLamp;

                case "TorchWallLamp":
                    return WallArch.WallTorch;

                case "TorchLamp":
                case "Darktorch":
                case "DarktorchFungus":
                    return WallArch.TorchPost;
            }

            if (def.building != null && def.building.isAttachment)
            {
                return WallArch.WallLamp;
            }

            // 兼容其他模组继承自 Wall 或 isWall 的满格墙
            if (def.Fillage == FillCategory.Full && def.building != null && def.building.isWall)
            {
                string ap = StuffAppearance(stuff);
                if (ap == "bricks") return WallArch.WallBricks;
                if (ap == "planks") return WallArch.WallPlanks;
                return WallArch.WallSmooth;
            }

            return WallArch.None;
        }

        public static bool IsFullWall(WallArch a)
        {
            return a >= WallArch.WallBricks && a <= WallArch.FleshmassWall;
        }

        public static bool IsInWallDevice(WallArch a)
        {
            return a == WallArch.Vent || a == WallArch.Cooler;
        }

        public static bool IsFullWallOrInWall(WallArch a)
        {
            return a >= WallArch.WallBricks && a <= WallArch.Cooler;
        }

        public static bool IsLowBarrierOrFence(WallArch a)
        {
            return a >= WallArch.Sandbags && a <= WallArch.AncientFence;
        }

        public static bool IsWallAttachment(WallArch a)
        {
            return a == WallArch.WallLamp || a == WallArch.WallTorch || a == WallArch.TorchPost;
        }

        public static bool IsLinkedBarrier(ThingDef def)
        {
            if (def == null) return false;
            string dn = def.defName;
            return dn == "Sandbags" || dn == "Barricade" || dn == "Fence" || dn == "AncientFence";
        }

        private static string StuffAppearance(ThingDef stuff)
        {
            var ap = stuff != null && stuff.stuffProps != null ? stuff.stuffProps.appearance : null;
            if (ap == null) return "smooth";
            if (ap.defName == "Bricks") return "bricks";
            if (ap.defName == "Planks") return "planks";
            if (ap.defName == "Metal") return "metal";
            return "smooth";
        }

        /// <summary>返回该墙体原型在 WallUVGen.R 中的贴图键前缀。</summary>
        public static string AtlasPrefix(WallArch arch)
        {
            switch (arch)
            {
                case WallArch.WallBricks: return "bricks";
                case WallArch.WallPlanks: return "planks";
                case WallArch.WallSmooth: return "smooth";
                case WallArch.RockSmooth: return "rocksmooth";
                case WallArch.FortifiedWall: return "fortified";
                case WallArch.GravshipHull: return "gravshiphull";
                case WallArch.BurrowWall: return "burrow";
                case WallArch.SmoothedIce: return "smoothedice";
                case WallArch.SolidIce: return "solidice";
                case WallArch.GrayWall: return "graywall";
                case WallArch.VoidmetalWall: return "voidmetal";
                case WallArch.FleshmassWall: return "fleshmass";
                case WallArch.Vent: return "vent";
                case WallArch.Cooler: return "cooler";
                case WallArch.Sandbags: return "sandbags";
                case WallArch.Barricade: return "barricade";
                case WallArch.FenceSmooth: return "fence_smooth";
                case WallArch.FenceMetal: return "fence_metal";
                case WallArch.AncientFence: return "ancientfence";
                case WallArch.WallLamp: return "walllamp";
                case WallArch.WallTorch: return "walltorch";
                case WallArch.TorchPost: return "walltorch";
                default: return "smooth";
            }
        }

        public static Vector4 GetUv(string prefix, string slot)
        {
            if (WallUVGen.R.TryGetValue(prefix + "." + slot, out var r)) return r;
            if (WallUVGen.R.TryGetValue(prefix + ".side", out r)) return r;
            if (WallUVGen.R.TryGetValue("smooth." + slot, out r)) return r;
            return WallUVGen.R["smooth.side"];
        }

        public static List<UvSpec> BuildUvSpecs(WallArch arch)
        {
            string p = AtlasPrefix(arch);
            Vector4 rTop = GetUv(p, "top");
            Vector4 rSide = GetUv(p, "side");
            Vector4 rDet = WallUVGen.R.TryGetValue(p + ".detail", out var d) ? d
                         : (WallUVGen.R.TryGetValue(p + ".cold", out d) ? d : rTop);
            Vector4 rExt = WallUVGen.R.TryGetValue(p + ".hot", out var e) ? e : rTop;
            Vector4 mainSide = arch == WallArch.Cooler ? rTop : rSide;

            var xz = new Vector2(1f, 1f);
            return new List<UvSpec>(4)
            {
                new UvSpec { top = rTop, side = mainSide, modelXZ = xz },
                new UvSpec { top = rSide, side = rSide, modelXZ = xz },
                new UvSpec { top = rDet, side = rDet, modelXZ = xz },
                new UvSpec { top = rExt, side = rExt, modelXZ = xz },
            };
        }

        /// <summary>
        /// 判定矮墙/围栏在指定方向 (d: 0=北-Z, 1=南+Z, 2=西-X, 3=东+X) 是否与邻格相连。
        /// </summary>
        public static bool BarrierLinksTo(Map map, IntVec3 cell, WallArch arch, IntVec3 neighbor)
        {
            if (map == null || !neighbor.InBounds(map)) return false;
            ThingDef ndef = null;
            var ed = neighbor.GetEdifice(map);
            if (ed != null && ed.def != null)
            {
                if (ed is Frame fr && fr.BuildDef != null) ndef = fr.BuildDef;
                else ndef = ed.def;
            }
            if (ndef == null)
            {
                var list = neighbor.GetThingList(map);
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is Blueprint bp && bp.EntityToBuild() is ThingDef bd)
                    {
                        ndef = bd;
                        break;
                    }
                }
            }
            if (ndef == null) return false;
            string dn = ndef.defName;
            if (arch == WallArch.Sandbags)
            {
                return dn == "Sandbags";
            }
            if (arch == WallArch.Barricade)
            {
                return dn == "Barricade";
            }
            if (arch == WallArch.FenceSmooth || arch == WallArch.FenceMetal || arch == WallArch.AncientFence)
            {
                if (dn == "Fence" || dn == "AncientFence" || dn == "FenceGate") return true;
                if (ndef.Fillage == FillCategory.Full) return true;
            }
            return false;
        }

        // ============================================================
        // 1. 满格墙体立面 3D 浮雕细节构造（附加在暴露立面 d: 0=北-Z, 1=南+Z, 2=西-X, 3=东+X）
        // ============================================================

        /// <summary>
        /// 向 Model3D 添加一面暴露满格墙立面（d: 0=北-Z, 1=南+Z, 2=西-X, 3=东+X）上的 3D 浮雕构件。
        /// 模型局部原点为该格中心地面 (0, 0, 0)，墙体占 [-0.5..+0.5, 0..wallH, -0.5..+0.5]。
        /// 所有贴面浮雕均设 noOutline=true，外轮廓由主墙体反绕黑边壳统一包裹，杜绝共面黑线。
        /// </summary>
        public static void AddExposedFaceRelief(Model3D m, WallArch arch, int d, float wallH, int cellHash = 0)
        {
            // d: 0=北(-Z, yaw=180), 1=南(+Z, yaw=0), 2=西(-X, yaw=90), 3=东(+X, yaw=-90)
            float yaw = d == 0 ? 180f : (d == 1 ? 0f : (d == 2 ? 90f : -90f));
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 FacePos(float uOffset, float y, float outDist)
            {
                // 局部朝南(+Z)基准：右向为 +X (uOffset)，外法线为 +Z (0.5 + outDist)
                return rot * new Vector3(uOffset, y, 0.5f + outDist);
            }

            switch (arch)
            {
                case WallArch.WallBricks:
                {
                    // 石砖墙：顶部压顶石檐 + 底部勒脚石基（使用 UV_EXTRA 即纯墙帽石面）
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.16f, 0.014f), new Vector3(0.98f, 0.16f, 0.032f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(0f, 0.00f, 0.010f), new Vector3(0.98f, 0.14f, 0.024f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    break;
                }
                case WallArch.WallPlanks:
                {
                    // 木板墙：上下横木梁 + 两侧木立柱筋
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.16f, 0.014f), new Vector3(0.98f, 0.16f, 0.032f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(0f, 0.00f, 0.010f), new Vector3(0.98f, 0.14f, 0.024f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(-0.43f, 0.14f, 0.010f), new Vector3(0.10f, wallH - 0.30f, 0.024f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos( 0.43f, 0.14f, 0.010f), new Vector3(0.10f, wallH - 0.30f, 0.024f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    break;
                }
                case WallArch.WallSmooth:
                {
                    // 光滑金属/宝石墙：利落的上下金属折边压条
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.14f, 0.012f), new Vector3(0.98f, 0.14f, 0.028f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(0f, 0.00f, 0.010f), new Vector3(0.98f, 0.14f, 0.024f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    break;
                }
                case WallArch.RockSmooth:
                {
                    // 平滑岩石墙：还原原版 rocksmooth_atlas 标志性的双层石雕顶冠线脚与底台
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.18f, 0.018f), new Vector3(0.98f, 0.18f, 0.040f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.36f, 0.011f), new Vector3(0.96f, 0.16f, 0.026f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Box, FacePos(0f, 0.00f, 0.014f), new Vector3(0.98f, 0.20f, 0.032f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    break;
                }
                case WallArch.FortifiedWall:
                {
                    // 远古加固防爆墙：重型防爆上挑檐 + 下部厚重阶梯式装甲护壁肋板 + 两侧混凝土加强柱
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.28f, 0.024f), new Vector3(0.98f, 0.28f, 0.052f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(0f, 0.00f, 0.028f), new Vector3(0.84f, 1.08f, 0.060f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Wedge, FacePos(0f, 1.07f, 0.028f), new Vector3(0.84f, 0.26f, 0.060f), yaw: yaw + 180f, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Box, FacePos(-0.41f, 0.00f, 0.018f), new Vector3(0.14f, wallH - 0.26f, 0.040f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    m.Add(Prim.Box, FacePos( 0.41f, 0.00f, 0.018f), new Vector3(0.14f, wallH - 0.26f, 0.040f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    break;
                }
                case WallArch.GravshipHull:
                {
                    // 飞船气密船体墙：还原原版中央竖向舱壁拼缝 + 上下双联凸起装甲板 + 顶部中央凹槽锁扣
                    m.Add(Prim.Box, FacePos(-0.26f, wallH - 0.26f, 0.020f), new Vector3(0.42f, 0.26f, 0.044f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos( 0.26f, wallH - 0.26f, 0.020f), new Vector3(0.42f, 0.26f, 0.044f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos( 0.00f, wallH - 0.24f, 0.010f), new Vector3(0.12f, 0.22f, 0.024f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    // 中部暗色舱壁横槽带
                    m.Add(Prim.Box, FacePos( 0.00f, 1.28f, 0.010f), new Vector3(0.96f, 0.16f, 0.024f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    // 上排左右装甲面板
                    m.Add(Prim.Box, FacePos(-0.25f, 1.46f, 0.018f), new Vector3(0.42f, 0.88f, 0.040f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    m.Add(Prim.Box, FacePos( 0.25f, 1.46f, 0.018f), new Vector3(0.42f, 0.88f, 0.040f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    // 下排左右装甲面板
                    m.Add(Prim.Box, FacePos(-0.25f, 0.14f, 0.020f), new Vector3(0.42f, 1.12f, 0.044f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    m.Add(Prim.Box, FacePos( 0.25f, 0.14f, 0.020f), new Vector3(0.42f, 1.12f, 0.044f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    break;
                }
                case WallArch.BurrowWall:
                {
                    // 虫穴分泌物岩土墙：浑然一体的天然有机巢穴墙体，整幅完整展现深黑褐泥冠、浅黄钙化波浪与暗橄榄夯土基底，严禁添加切断波浪的生硬横盒
                    break;
                }
                case WallArch.SmoothedIce:
                {
                    // 平滑冰墙：纯白冰雪顶冠 + 冰蓝折光腰线与冰基
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.22f, 0.016f), new Vector3(0.98f, 0.22f, 0.036f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.40f, 0.011f), new Vector3(0.96f, 0.18f, 0.026f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Box, FacePos(0f, 0.00f, 0.012f), new Vector3(0.98f, 0.18f, 0.028f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    break;
                }
                case WallArch.SolidIce:
                {
                    // 天然冰川墙：厚重冰雪冠顶 + 通高冰晶壁面凸脊
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.32f, 0.020f), new Vector3(0.96f, 0.32f, 0.044f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(-0.22f, 0.00f, 0.016f), new Vector3(0.46f, wallH - 0.30f, 0.036f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Box, FacePos( 0.22f, 0.00f, 0.012f), new Vector3(0.44f, wallH - 0.30f, 0.028f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    break;
                }
                case WallArch.GrayWall:
                {
                    // 异象迷宫灰墙：深灰巨石顶沿 + 浅灰高光倒角线 + 嵌入式石板框
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.20f, 0.016f), new Vector3(0.98f, 0.20f, 0.036f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.32f, 0.020f), new Vector3(0.98f, 0.12f, 0.044f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Box, FacePos(0f, 0.00f, 0.014f), new Vector3(0.98f, 0.20f, 0.032f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    break;
                }
                case WallArch.VoidmetalWall:
                {
                    // 虚空金属刺墙：还原原版贴图下部破壁而出的尖锐暗黑结晶刺簇
                    m.Add(Prim.Box, FacePos(0f, wallH - 0.22f, 0.016f), new Vector3(0.98f, 0.22f, 0.036f), yaw: yaw, noOutline: true, uv: UV_EXTRA);
                    m.Add(Prim.Cone, FacePos(-0.28f, 0.04f, 0.035f), new Vector3(0.22f, 0.78f, 0.12f), yaw: yaw, pitch: 12f, roll: -8f, taper: 0.06f, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Cone, FacePos( 0.02f, 0.02f, 0.040f), new Vector3(0.26f, 0.96f, 0.14f), yaw: yaw, pitch: 14f, roll: 4f, taper: 0.05f, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Cone, FacePos( 0.30f, 0.06f, 0.032f), new Vector3(0.20f, 0.68f, 0.11f), yaw: yaw, pitch: 10f, roll: 10f, taper: 0.06f, noOutline: true, uv: UV_DETAIL);
                    break;
                }
                case WallArch.FleshmassWall:
                {
                    // 扭曲血肉墙：还原原版贴图中鼓胀突出的球状血肉囊泡与肉瘤
                    m.Add(Prim.Sphere, FacePos(-0.22f, 1.52f, 0.025f), new Vector3(0.44f, 0.42f, 0.16f), yaw: yaw, noOutline: true, uv: UV_TOP);
                    m.Add(Prim.Sphere, FacePos( 0.20f, 1.10f, 0.028f), new Vector3(0.48f, 0.46f, 0.18f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
                    m.Add(Prim.Sphere, FacePos(-0.14f, 0.48f, 0.022f), new Vector3(0.40f, 0.38f, 0.15f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                    m.Add(Prim.Sphere, FacePos( 0.24f, 1.88f, 0.020f), new Vector3(0.32f, 0.30f, 0.13f), yaw: yaw, noOutline: true, uv: UV_TOP);
                    break;
                }
            }
        }

        // ============================================================
        // 2. 穿墙设备（Vent 通风口 / Cooler 制冷机）与壁挂灯具（WallLamp / WallTorch）
        // ============================================================

        /// <summary>向 1x1x1 (高 1.00m) 通风口或制冷机的前后立面添加 3D 百叶窗/散热格栅与冷热风口标志。</summary>
        public static void AddInWallDeviceRelief(Model3D m, WallArch arch, Rot4 rot, float devH,
            bool expN, bool expS, bool expW, bool expE)
        {
            bool[] exp = { expN, expS, expW, expE };
            // RimWorld Rot4: 0=North(+Z, 对应我们的 d=1 南/+Z), 1=East(+X, d=3), 2=South(-Z, d=0), 3=West(-X, d=2)
            int frontDir = rot == Rot4.North ? 1 : (rot == Rot4.South ? 0 : (rot == Rot4.East ? 3 : 2));
            int backDir = frontDir ^ 1;

            for (int d = 0; d < 4; d++)
            {
                if (!exp[d]) continue;
                float yaw = d == 0 ? 180f : (d == 1 ? 0f : (d == 2 ? 90f : -90f));
                var q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 FP(float u, float y, float outD) => q * new Vector3(u, y, 0.5f + outD);

                if (arch == WallArch.Vent)
                {
                    // 1x1x1 通风口：顶部压边 + 前后立面外框与 5 层水平百叶格栅
                    m.Add(Prim.Box, FP(0f, devH - 0.10f, 0.012f), new Vector3(0.96f, 0.10f, 0.028f), yaw: yaw, noOutline: true, uv: UV_TOP);
                    if (d == frontDir || d == backDir)
                    {
                        m.Add(Prim.Box, FP(0f, 0.10f, 0.016f), new Vector3(0.82f, 0.76f, 0.036f), yaw: yaw, noOutline: true, uv: UV_TOP);
                        for (int i = 0; i < 5; i++)
                        {
                            float ly = 0.16f + i * 0.135f;
                            m.Add(Prim.Box, FP(0f, ly, 0.032f), new Vector3(0.72f, 0.085f, 0.042f), yaw: yaw, pitch: 14f, noOutline: true, uv: UV_DETAIL);
                        }
                    }
                }
                else if (arch == WallArch.Cooler)
                {
                    // 1x1x1 制冷机：顶部压边 + 下半散热百叶格栅 + 上半冷/热风向指示面板
                    m.Add(Prim.Box, FP(0f, devH - 0.10f, 0.014f), new Vector3(0.96f, 0.10f, 0.032f), yaw: yaw, noOutline: true, uv: UV_TOP);
                    if (d == frontDir || d == backDir)
                    {
                        m.Add(Prim.Box, FP(0f, 0.08f, 0.020f), new Vector3(0.82f, 0.48f, 0.044f), yaw: yaw, noOutline: true, uv: UV_SIDE);
                        m.Add(Prim.Box, FP(0f, 0.60f, 0.024f), new Vector3(0.82f, 0.24f, 0.052f), yaw: yaw, noOutline: true, uv: UV_TOP);
                        int indicatorUv = (d == frontDir) ? UV_DETAIL : UV_EXTRA;
                        m.Add(Prim.Box, FP(0f, 0.64f, 0.042f), new Vector3(0.30f, 0.16f, 0.036f), yaw: yaw, noOutline: true, uv: indicatorUv);
                    }
                }
            }
        }

        // ============================================================
        // 3. 四向连接矮墙掩体与围栏、壁挂灯具完整 3D 模型生成
        // ============================================================

        /// <summary>
        /// 构造单格矮墙/围栏/壁挂灯具或满格墙预览样板的完整 Model3D（局部原点在格中心地面 (0,0,0)）。
        /// </summary>
        public static Model3D BuildCellModel(WallArch arch, bool linkN, bool linkS, bool linkW, bool linkE, Rot4 rot, int cellHash = 0)
        {
            var m = new Model3D();
            if (IsInWallDevice(arch))
            {
                // 通风管与制冷机本体统一为 1x1x1 模型（高 1.00m）
                const float devH = 1.00f;
                m.Add(Prim.Box, Vector3.zero, new Vector3(1.00f, devH, 1.00f), uv: UV_TOP);
                AddInWallDeviceRelief(m, arch, rot, devH, linkN, linkS, linkW, linkE);
                m.RecalcBounds();
                return m;
            }

            if (IsFullWall(arch))
            {
                float h = arch == WallArch.SolidIce ? Metrics3D.RockHeight : Fps3DState.WallHeight;
                m.Add(Prim.Box, Vector3.zero, new Vector3(1.00f, h, 1.00f), uv: UV_TOP);
                bool[] exp = { linkN, linkS, linkW, linkE };
                for (int d = 0; d < 4; d++)
                    if (exp[d]) AddExposedFaceRelief(m, arch, d, h, cellHash);
                m.RecalcBounds();
                return m;
            }

            switch (arch)
            {
                case WallArch.Sandbags:
                    BuildSandbags(m, linkN, linkS, linkW, linkE);
                    break;
                case WallArch.Barricade:
                    BuildBarricade(m, linkN, linkS, linkW, linkE);
                    break;
                case WallArch.FenceSmooth:
                    BuildFence(m, linkN, linkS, linkW, linkE, isMetal: false);
                    break;
                case WallArch.FenceMetal:
                    BuildFence(m, linkN, linkS, linkW, linkE, isMetal: true);
                    break;
                case WallArch.AncientFence:
                    BuildAncientFence(m, linkN, linkS, linkW, linkE);
                    break;
                case WallArch.WallLamp:
                    BuildWallLamp(m, rot);
                    break;
                case WallArch.WallTorch:
                    BuildWallTorch(m, rot);
                    break;
                case WallArch.TorchPost:
                    BuildTorchPost(m);
                    break;
            }
            m.RecalcBounds();
            return m;
        }

        /// <summary>沙袋掩体（Sandbags）：三层错缝堆叠的饱满扎口沙袋，自动向四向邻格延伸。</summary>
        private static void BuildSandbags(Model3D m, bool n, bool s, bool w, bool e)
        {
            if (!n && !s && !w && !e) { w = true; e = true; }

            // 中心三层叠袋（底层用 UV_DETAIL，中层用 UV_SIDE，顶层用 UV_EXTRA 即单层袋顶面）
            m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.48f, 0.20f, 0.46f), uv: UV_DETAIL);
            m.Add(Prim.Box, new Vector3(0.00f, 0.18f, 0.00f), new Vector3(0.46f, 0.20f, 0.48f), yaw: 8f, uv: UV_SIDE);
            m.Add(Prim.Box, new Vector3(0.00f, 0.36f, 0.00f), new Vector3(0.44f, 0.19f, 0.44f), yaw: -6f, uv: UV_EXTRA);

            void AddArm(float dirYaw)
            {
                var q = Quaternion.Euler(0f, dirYaw, 0f);
                // 沿臂方向延伸的三层饱满沙袋（中层错缝偏移）
                m.Add(Prim.Box, q * new Vector3(0f, 0.00f, 0.31f), new Vector3(0.46f, 0.20f, 0.38f), yaw: dirYaw, uv: UV_DETAIL);
                m.Add(Prim.Box, q * new Vector3(0f, 0.18f, 0.28f), new Vector3(0.44f, 0.20f, 0.38f), yaw: dirYaw + 4f, uv: UV_SIDE);
                m.Add(Prim.Box, q * new Vector3(0f, 0.36f, 0.30f), new Vector3(0.42f, 0.19f, 0.38f), yaw: dirYaw - 3f, uv: UV_EXTRA);
            }

            if (s) AddArm(0f);
            if (n) AddArm(180f);
            if (e) AddArm(90f);
            if (w) AddArm(-90f);
        }

        /// <summary>路障掩体（Barricade）：中央八角装甲墩柱 + 四向连接厚重掩体横梁。</summary>
        private static void BuildBarricade(Model3D m, bool n, bool s, bool w, bool e)
        {
            if (!n && !s && !w && !e) { w = true; e = true; }

            // 中央八角/倒角重型连接墩 + 中腰凹槽箍带 (UV_DETAIL)
            m.Add(Prim.Box, new Vector3(0f, 0.00f, 0f), new Vector3(0.44f, 0.60f, 0.44f), uv: UV_TOP);
            m.Add(Prim.Box, new Vector3(0f, 0.18f, 0f), new Vector3(0.46f, 0.22f, 0.46f), noOutline: true, uv: UV_DETAIL);

            void AddBeam(float dirYaw)
            {
                var q = Quaternion.Euler(0f, dirYaw, 0f);
                m.Add(Prim.Box, q * new Vector3(0f, 0.00f, 0.32f), new Vector3(0.38f, 0.36f, 0.36f), yaw: dirYaw, uv: UV_SIDE);
                m.Add(Prim.Box, q * new Vector3(0f, 0.34f, 0.32f), new Vector3(0.40f, 0.24f, 0.36f), yaw: dirYaw, uv: UV_TOP);
            }

            if (s) AddBeam(0f);
            if (n) AddBeam(180f);
            if (e) AddBeam(90f);
            if (w) AddBeam(-90f);
        }

        /// <summary>木/石/金属栅栏（Fence）：还原原版方立柱 + 柱帽 + 横档（木/石双横档，金属三横档）。</summary>
        private static void BuildFence(Model3D m, bool n, bool s, bool w, bool e, bool isMetal = false)
        {
            bool isolated = !n && !s && !w && !e;
            if (isolated)
            {
                // 独立单格栅栏：还原原版菜单与单格的双立柱 + 横档造型
                m.Add(Prim.Box, new Vector3(-0.30f, 0.00f, 0f), new Vector3(0.16f, 0.96f, 0.16f), uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3(-0.30f, 0.94f, 0f), new Vector3(0.19f, 0.06f, 0.19f), noOutline: true, uv: UV_DETAIL);
                m.Add(Prim.Box, new Vector3( 0.30f, 0.00f, 0f), new Vector3(0.16f, 0.96f, 0.16f), uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3( 0.30f, 0.94f, 0f), new Vector3(0.19f, 0.06f, 0.19f), noOutline: true, uv: UV_DETAIL);
                if (isMetal)
                {
                    m.Add(Prim.Box, new Vector3(0.00f, 0.68f, 0f), new Vector3(0.88f, 0.12f, 0.10f), uv: UV_TOP);
                    m.Add(Prim.Box, new Vector3(0.00f, 0.44f, 0f), new Vector3(0.88f, 0.12f, 0.10f), uv: UV_SIDE);
                    m.Add(Prim.Box, new Vector3(0.00f, 0.20f, 0f), new Vector3(0.88f, 0.12f, 0.10f), uv: UV_SIDE);
                }
                else
                {
                    m.Add(Prim.Box, new Vector3(0.00f, 0.64f, 0f), new Vector3(0.88f, 0.16f, 0.10f), uv: UV_TOP);
                    m.Add(Prim.Box, new Vector3(0.00f, 0.26f, 0f), new Vector3(0.88f, 0.16f, 0.10f), uv: UV_SIDE);
                }
                m.Add(Prim.Box, new Vector3( 0.00f, 0.00f, 0f), new Vector3(0.64f, 0.04f, 0.08f), noOutline: true, uv: UV_SIDE);
                return;
            }

            // 中心立柱 + 柱帽
            m.Add(Prim.Box, new Vector3(0f, 0.00f, 0f), new Vector3(0.18f, 0.96f, 0.18f), uv: UV_SIDE);
            m.Add(Prim.Box, new Vector3(0f, 0.94f, 0f), new Vector3(0.21f, 0.06f, 0.21f), noOutline: true, uv: UV_DETAIL);

            void AddRail(float dirYaw)
            {
                var q = Quaternion.Euler(0f, dirYaw, 0f);
                if (isMetal)
                {
                    m.Add(Prim.Box, q * new Vector3(0f, 0.68f, 0.27f), new Vector3(0.10f, 0.12f, 0.46f), yaw: dirYaw, uv: UV_TOP);
                    m.Add(Prim.Box, q * new Vector3(0f, 0.44f, 0.27f), new Vector3(0.10f, 0.12f, 0.46f), yaw: dirYaw, uv: UV_SIDE);
                    m.Add(Prim.Box, q * new Vector3(0f, 0.20f, 0.27f), new Vector3(0.10f, 0.12f, 0.46f), yaw: dirYaw, uv: UV_SIDE);
                }
                else
                {
                    m.Add(Prim.Box, q * new Vector3(0f, 0.64f, 0.27f), new Vector3(0.10f, 0.16f, 0.46f), yaw: dirYaw, uv: UV_TOP);
                    m.Add(Prim.Box, q * new Vector3(0f, 0.26f, 0.27f), new Vector3(0.10f, 0.16f, 0.46f), yaw: dirYaw, uv: UV_SIDE);
                }
            }

            if (s) AddRail(0f);
            if (n) AddRail(180f);
            if (e) AddRail(90f);
            if (w) AddRail(-90f);
        }

        /// <summary>远古混凝土围栏（AncientFence）：还原原版深灰钢筋混凝土立柱 + 嵌入式混凝土挡板。</summary>
        private static void BuildAncientFence(Model3D m, bool n, bool s, bool w, bool e)
        {
            bool isolated = !n && !s && !w && !e;
            if (isolated)
            {
                m.Add(Prim.Box, new Vector3(-0.32f, 0.00f, 0f), new Vector3(0.22f, 0.98f, 0.22f), uv: UV_DETAIL);
                m.Add(Prim.Box, new Vector3(-0.32f, 0.95f, 0f), new Vector3(0.25f, 0.06f, 0.25f), noOutline: true, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3( 0.32f, 0.00f, 0f), new Vector3(0.22f, 0.98f, 0.22f), uv: UV_DETAIL);
                m.Add(Prim.Box, new Vector3( 0.32f, 0.95f, 0f), new Vector3(0.25f, 0.06f, 0.25f), noOutline: true, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3( 0.00f, 0.00f, 0f), new Vector3(0.66f, 0.64f, 0.14f), uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3( 0.00f, 0.62f, 0f), new Vector3(0.68f, 0.08f, 0.17f), noOutline: true, uv: UV_TOP);
                return;
            }

            m.Add(Prim.Box, new Vector3(0f, 0.00f, 0f), new Vector3(0.24f, 0.98f, 0.24f), uv: UV_DETAIL);
            m.Add(Prim.Box, new Vector3(0f, 0.95f, 0f), new Vector3(0.27f, 0.06f, 0.27f), noOutline: true, uv: UV_TOP);

            void AddPanel(float dirYaw)
            {
                var q = Quaternion.Euler(0f, dirYaw, 0f);
                m.Add(Prim.Box, q * new Vector3(0f, 0.00f, 0.28f), new Vector3(0.14f, 0.64f, 0.44f), yaw: dirYaw, uv: UV_SIDE);
                m.Add(Prim.Box, q * new Vector3(0f, 0.62f, 0.28f), new Vector3(0.17f, 0.08f, 0.44f), yaw: dirYaw, noOutline: true, uv: UV_TOP);
            }

            if (s) AddPanel(0f);
            if (n) AddPanel(180f);
            if (e) AddPanel(90f);
            if (w) AddPanel(-90f);
        }

        /// <summary>壁灯等壁挂件（WallLamp）：1x1 格内离地高度统一为 Y = 2.00m，贴靠所依附墙面。</summary>
        private static void BuildWallLamp(Model3D m, Rot4 rot)
        {
            float y0 = Metrics3D.WallAttachmentHeight;
            float yaw = rot.AsAngle;
            var q = Quaternion.Euler(0f, yaw, 0f);
            // rot=North 时所附墙体在 +Z (z=+0.44f)，灯管朝室内 (-Z) 突出
            m.Add(Prim.Box, q * new Vector3(0f, y0 + 0.00f, 0.44f), new Vector3(0.46f, 0.16f, 0.10f), yaw: yaw, uv: UV_TOP);
            m.Add(Prim.Box, q * new Vector3(0f, y0 + 0.02f, 0.38f), new Vector3(0.40f, 0.12f, 0.10f), yaw: yaw, uv: UV_SIDE);
            m.Add(Prim.Box, q * new Vector3(0f, y0 + 0.03f, 0.35f), new Vector3(0.36f, 0.09f, 0.06f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
        }

        /// <summary>壁炬（TorchWallLamp）：1x1 格内离地高度统一为 Y = 2.00m，仅构建托架与木柄（火焰由 Shader 渲染）。</summary>
        private static void BuildWallTorch(Model3D m, Rot4 rot)
        {
            float y0 = Metrics3D.WallAttachmentHeight;
            float yaw = rot.AsAngle;
            var q = Quaternion.Euler(0f, yaw, 0f);
            // 壁挂托架底座（贴靠 +Z 墙面 z=0.44f）
            m.Add(Prim.Box, q * new Vector3(0f, y0 + 0.00f, 0.44f), new Vector3(0.16f, 0.18f, 0.10f), yaw: yaw, uv: UV_DETAIL);
            m.Add(Prim.Box, q * new Vector3(0f, y0 + 0.05f, 0.36f), new Vector3(0.12f, 0.08f, 0.16f), yaw: yaw, noOutline: true, uv: UV_DETAIL);
            // 斜向伸出的火把木柄与顶杯（无实体火焰球/锥，火焰交由 Shader 渲染）
            m.Add(Prim.Cylinder, q * new Vector3(0f, y0 + 0.02f, 0.34f), new Vector3(0.11f, 0.30f, 0.11f), yaw: yaw, pitch: -24f, taper: 1.25f, uv: UV_SIDE);
            m.Add(Prim.Cylinder, q * new Vector3(0f, y0 + 0.24f, 0.24f), new Vector3(0.14f, 0.08f, 0.14f), yaw: yaw, pitch: -24f, taper: 1.12f, noOutline: true, uv: UV_TOP);
        }

        /// <summary>落地火把（TorchLamp / Darktorch）：仅构建直立木柄与顶杯，火焰部分由 Shader 渲染。</summary>
        private static void BuildTorchPost(Model3D m)
        {
            m.Add(Prim.Cylinder, new Vector3(0f, 0.00f, 0f), new Vector3(0.14f, 0.78f, 0.14f), taper: 1.15f, uv: UV_SIDE);
            m.Add(Prim.Cylinder, new Vector3(0f, 0.74f, 0f), new Vector3(0.18f, 0.14f, 0.18f), taper: 1.18f, noOutline: true, uv: UV_TOP);
        }
    }
}
