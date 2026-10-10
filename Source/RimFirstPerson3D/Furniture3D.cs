// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 家具程序化建模（三视图重构）
    // ============================================================
    //
    // 【依据】每件家具的原版三视图 = _south / _east / _north 三张朝向图。
    // 三张都不是正俯视，而是带俯角的斜俯视：图里**上部是俯视段**（物件的顶面），
    // **下部是立面段**（朝向镜头那一面的真实外观）。所以：
    //   · 模型的水平面  <- 当前朝向那张图的俯视段
    //   · 模型的竖直面  <- 各朝向那张图的立面段（南/北/东各自取，西 = 东镜像）
    // 这样四个立面各有其真实像素，而不是把一张图糊到六面上。
    //
    // 【取样区】坐标在 scripts/furn_atlas.py 的 SPEC 里逐图像素级目视标定，
    // 打包成 1.6/Textures/FPS3D/FurnitureAtlas.png，UV 常量表由 FurnitureUVGen.cs
    // 自动生成。取样的像素**全部来自原版贴图**，只有黑色笔画被邻域补绘替换
    // （描边由反绕外壳网格负责，不进材质）。校验：scripts/furn_uv_check.py。
    //
    // 【尺寸】一律取 Metrics3D（现实家具尺寸），不从原版 def 反推；
    // def 只用于定位：占几格、朝向。
    public static class Furniture3D
    {
        private enum Arch { Bed, Stool, Nightstand, Cabinet, ShelfUnit, Armchair }

        private class Built
        {
            public Mesh body;
            public Mesh hull;
        }

        private static readonly Dictionary<int, Built> cache = new Dictionary<int, Built>();
        private static Material mat;
        private static Texture2D atlas;

        /// <summary>本系统的开关（独立于其它层，便于单点回退）。</summary>
        public static bool Enabled = true;

        // ---- 归类：只看 defName 关键字（def 不作尺寸依据）----

        private static bool ArchOf(ThingDef d, out Arch arch)
        {
            arch = Arch.Stool;
            if (d == null) return false;
            string n = (d.defName ?? "").ToLowerInvariant();
            if (Has(n, "bed") && !Has(n, "bedroll")) { arch = Arch.Bed; return true; }
            if (Has(n, "endtable", "nightstand")) { arch = Arch.Nightstand; return true; }
            if (Has(n, "dresser", "cabinet")) { arch = Arch.Cabinet; return true; }
            if (Has(n, "shelf", "bookcase")) { arch = Arch.ShelfUnit; return true; }
            if (Has(n, "armchair")) { arch = Arch.Armchair; return true; }
            if (Has(n, "stool")) { arch = Arch.Stool; return true; }
            return false;
        }

        private static bool Has(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (s.IndexOf(keys[i], System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>该建筑是否由本层绘制（BuildingLayer3D 据此跳过立板回退）。</summary>
        public static bool Handles(Thing t)
        {
            if (!Enabled || t == null || t.def == null) return false;
            if (t.def.category != ThingCategory.Building) return false;
            if (t.def.Fillage == FillCategory.Full) return false;
            if (t is Building_Door) return false;
            Arch a;
            return ArchOf(t.def, out a);
        }

        // ---- 取样区 ----

        /// <summary>
        /// 本层用到的全部取样区（与 scripts/furn_atlas.py 的 SPEC 一一对应）。
        /// 校验器按字面量比对，多一个/少一个都会报，防止表与几何脱节。
        /// </summary>
        internal static readonly string[] AllRegions =
        {
            "bed.south.top", "bed.south.front",
            "bed.north.top", "bed.north.front",
            "bed.east.top", "bed.east.front",
            "stool.north.top", "stool.north.front",
            "stool.east.top", "stool.east.front",
            "endtable.south.top", "endtable.south.front",
            "endtable.north.top", "endtable.north.front",
            "endtable.east.top",
            "dresser.south.top", "dresser.south.front",
            "dresser.north.top", "dresser.north.front",
            "dresser.east.top", "dresser.east.front",
            "shelf.south.top", "shelf.south.front",
            "shelf.north.top", "shelf.north.front",
            "shelf.east.top", "shelf.east.front",
            "bed.south.wood", "stool.north.wood", "endtable.south.wood",
            "dresser.south.wood", "shelf.south.wood",
            "armchair.south.top", "armchair.south.back", "armchair.south.front",
            "armchair.south.wood",
            "armchair.north.arml", "armchair.north.armr", "armchair.north.top",
            "armchair.north.back", "armchair.north.wood",
            "armchair.east.back", "armchair.east.arml", "armchair.east.top",
            "armchair.east.wood",
        };

        private static Vector4 Reg(string key)
        {
            Vector4 r;
            return FurnitureUVGen.R.TryGetValue(key, out r) ? r : Vector4.zero;
        }

        private static string ViewOf(Rot4 r)
        {
            if (r == Rot4.South) return "south";
            if (r == Rot4.North) return "north";
            return "east";
        }

        private static string Opp(string v)
        {
            return v == "south" ? "north" : v == "north" ? "south" : "west";
        }

        /// <summary>
        /// 按 "原型.朝向.区段" 取区；缺该朝向时按 south -&gt; north -&gt; east 回退
        /// （原版并非每件都画满三向）。
        /// </summary>
        private static Vector4 Region(string arch, string want, string region)
        {
            var r = Reg(arch + "." + want + "." + region);
            if (r.w > r.y) return r;
            string[] fb = { "south", "north", "east" };
            for (int i = 0; i < fb.Length; i++)
            {
                if (fb[i] == want) continue;
                r = Reg(arch + "." + fb[i] + "." + region);
                if (r.w > r.y) return r;
            }
            return Vector4.zero;
        }

        /// <summary>
        /// 一个部件的取样规格：顶面用当前朝向的俯视段，四个立面按朝向各自配
        /// 那张图的立面段。dir: 0北(-Z) 1南(+Z) 2东(+X) 3西(-X)。
        /// </summary>
        private static UvSpec FaceUv(string arch, Rot4 rot, Vector2 xz, string topRegion = "top")
        {
            string v = ViewOf(rot);
            // 东/西面：当物件本身朝东/西时，它的端面由 south/north 图给出；
            // 否则由 east 图给出。
            string cross = (v == "east") ? "south" : "east";
            var side4 = new Vector4[4];
            side4[0] = Region(arch, Opp(v), "front");
            side4[1] = Region(arch, v, "front");
            side4[2] = Region(arch, cross, "front");
            side4[3] = side4[2];
            return new UvSpec
            {
                top = Region(arch, v, topRegion),
                side = side4[1],
                side4 = side4,
                sideTileH = 0f,
                sideTileW = 0f,
                modelXZ = xz
            };
        }

        /// <summary>
        /// 干净木面区（SPEC 里每件声明一处 <c>wood</c>）：三视图没画到的面 ——
        /// 圆柱的侧壁与顶盖 —— 取这块无笔画的实心木面，而不是把某个立面段糊上去。
        /// </summary>
        private static Vector4 WoodRegion(string arch, Rot4 rot)
        {
            return Region(arch, ViewOf(rot), "wood");
        }

        /// <summary>
        /// 薄板件（台面/顶板）：顶面仍取当前朝向的俯视段，四周一圈立边取木面 ——
        /// 立面段画的是门板抽屉，糊到 5cm 的板边上就是错的图案。
        /// </summary>
        private static UvSpec SlabUv(string arch, Rot4 rot, Vector2 xz)
        {
            var u = FaceUv(arch, rot, xz);
            var w = WoodRegion(arch, rot);
            u.side = w;
            for (int i = 0; i < 4; i++) u.side4[i] = w;
            return u;
        }

        /// <summary>
        /// 端板（床头/床尾）：大面用立面段，左右那两条 8cm 窄边用木面 ——
        /// 窄边在图里没有对应像素，硬拿立面段会被拉成横条纹。
        /// longZ: 床的长轴沿 Z（朝南/北），此时窄边是 ±X。
        /// </summary>
        private static UvSpec EndPanelUv(string arch, Rot4 rot, Vector2 xz, bool longZ)
        {
            var u = FaceUv(arch, rot, xz);
            var w = WoodRegion(arch, rot);
            if (longZ) { u.side4[2] = w; u.side4[3] = w; }
            else { u.side4[0] = w; u.side4[1] = w; }
            return u;
        }

        private static UvSpec WoodUv(string arch, Rot4 rot, Vector2 xz)
        {
            var w = WoodRegion(arch, rot);
            return new UvSpec
            {
                top = w,
                side = w,
                sideTileH = 0f,
                sideTileW = 0f,
                modelXZ = xz
            };
        }

        /// <summary>
        /// 线稿型家具（白面 + 黑框）用：顶面与各立面各自点名要哪条白面带。
        /// 这类贴图没有"俯视段/立面段"之分，一件东西上能分出 3~5 条互不相邻的白面。
        /// </summary>
        private static UvSpec UvNames(string arch, Rot4 rot, Vector2 xz,
            string topRegion, string sideRegion)
        {
            string v = ViewOf(rot);
            var t = Region(arch, v, topRegion);
            var s = Region(arch, v, sideRegion);
            return new UvSpec
            {
                top = t,
                side = s,
                side4 = new[] { s, s, s, s },
                sideTileH = 0f,
                sideTileW = 0f,
                modelXZ = xz
            };
        }

        /// <summary>
        /// "背对镜头的那一端"在模型长轴上的符号：三视图的镜头在该件朝向的那一侧，
        /// 所以图里远端（枕头/椅背所在）= 反向。
        /// </summary>
        private static float BackSign(Rot4 rot)
        {
            if (rot == Rot4.South) return 1f;
            if (rot == Rot4.North) return -1f;
            return rot == Rot4.East ? -1f : 1f;
        }

        /// <summary>
        /// 软包板件（靠背/扶手）：大面用该件自己的白面带，两条边棱用木面 ——
        /// 三视图里板件的边棱同样没有对应像素。longZ 决定边棱落在哪个轴。
        /// </summary>
        private static UvSpec PanelUv(string arch, Rot4 rot, Vector2 xz, string face)
        {
            string v = ViewOf(rot);
            var t = Region(arch, v, "top");
            var s = Region(arch, v, face);
            var w = WoodRegion(arch, rot);
            var s4 = new[] { s, s, s, s };
            if (rot == Rot4.North || rot == Rot4.South) { s4[2] = w; s4[3] = w; }
            else { s4[0] = w; s4[1] = w; }
            return new UvSpec
            {
                top = t,
                side = s,
                side4 = s4,
                sideTileH = 0f,
                sideTileW = 0f,
                modelXZ = xz
            };
        }

        // ---- 建模 ----

        /// <summary>原型 -> 取样表里的键名（与 scripts/furn_atlas.py SPEC 同名）。</summary>
        private static string KeyOf(Arch a)
        {
            switch (a)
            {
                case Arch.Bed: return "bed";
                case Arch.Stool: return "stool";
                case Arch.Nightstand: return "endtable";
                case Arch.Cabinet: return "dresser";
                case Arch.Armchair: return "armchair";
                default: return "shelf";
            }
        }

        private static Built Build(Arch arch, Rot4 rot, float w, float d)
        {
            string key = KeyOf(arch);
            var m = new Model3D();
            var uvs = new List<UvSpec>();
            var xz = new Vector2(w, d);
            // 0 = 主体（顶面 + 四立面）
            uvs.Add(FaceUv(key, rot, xz));

            // 长轴方向：Bed 的长短随朝向换轴
            bool longZ = (rot == Rot4.North || rot == Rot4.South);
            float len = longZ ? d : w, thick = longZ ? w : d;

            switch (arch)
            {
                case Arch.Bed:
                {
                    // 床架（顶面埋进床垫，不发顶）+ 床垫 + 床头板 + 床尾板 + 四立柱
                    var frame = new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(w - 0.06f, Metrics3D.BedFrameHeight, d - 0.06f),
                        noTop = true,
                        uv = 4        // 床架侧板 = 木面（见下）
                    };
                    m.parts.Add(frame);
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, Metrics3D.BedFrameHeight, 0f),
                        size = new Vector3(w - 0.20f, Metrics3D.MattressThickness, d - 0.18f),
                        uv = 0
                    });
                    // 头/尾板：_south 图里枕头在远端（+Z），_north 图里在 -Z —— 见 BackSign
                    float headSign = BackSign(rot);
                    float hbH = Metrics3D.BedHeadboardHeight - Metrics3D.BedFrameHeight;
                    float fbH = Metrics3D.FootboardHeight - Metrics3D.BedFrameHeight;
                    uvs.Add(EndPanelUv(key, rot, xz, longZ));   // 1 = 端板
                    AddEndPanel(m, 1, longZ, thick, headSign, len, Metrics3D.BedFrameHeight, hbH);
                    uvs.Add(EndPanelUv(key, rot, xz, longZ));   // 2 = 尾板
                    AddEndPanel(m, 2, longZ, thick, -headSign, len, Metrics3D.BedFrameHeight, fbH);
                    uvs.Add(WoodUv(key, rot, xz));             // 3 = 立柱（圆柱，三视图没画它）
                    AddPosts(m, 3, w, d, rot, Metrics3D.BedHeadboardHeight, Metrics3D.FootboardHeight);
                    // 4 = 床架侧板：0.4m 高的床架侧立面在三视图里根本没有（立面段只有 15px 的床尾栏）
                    uvs.Add(WoodUv(key, rot, xz));
                    break;
                }
                case Arch.Stool:
                {
                    float sh = Metrics3D.ChairSeatHeight;
                    float sw = Mathf.Min(0.44f, w * 0.6f);
                    var seat = FaceUv(key, rot, new Vector2(sw, sw));
                    // 座沿那一圈是竖直面，但三视图的立面段画的是腿，不是座沿 —— 用木面。
                    // 圆柱的侧壁只认 side，顶盖仍走 top（座面本尊）。
                    seat.side = WoodRegion(key, rot);
                    uvs.Add(seat);                             // 1 = 座面
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, sh, 0f),
                        size = new Vector3(sw, Metrics3D.SeatCushionThickness, sw),
                        uv = 1
                    });
                    uvs.Add(WoodUv(key, rot, xz));             // 2 = 腿（圆柱，三视图没画它们）
                    Legs(m, 2, sw * 0.34f, sh, Metrics3D.LegThickness);
                    break;
                }
                case Arch.Armchair:
                {
                    // 线稿型软包椅：整件是"白面 + 黑框"，没有俯视段/立面段之分。
                    // 座面/靠背/两扶手各点一条白面带，板件的边棱与腿用木面。
                    float sh = Metrics3D.ArmchairSeatHeight;
                    float t = Metrics3D.UpholsteryThickness;
                    float bs = BackSign(rot);
                    uvs.Add(UvNames(key, rot, xz, "top", "front"));    // 1 = 箱体
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(w * 0.9f, sh, d * 0.86f),
                        uv = 1
                    });
                    uvs.Add(PanelUv(key, rot, xz, "back"));             // 2 = 靠背
                    m.parts.Add(longZ
                        ? new Part
                        {
                            kind = Prim.Box, uv = 2,
                            pos = new Vector3(0f, sh, bs * (d * 0.5f - t * 0.5f)),
                            size = new Vector3(w * 0.98f, Metrics3D.ArmchairBackHeight - sh, t)
                        }
                        : new Part
                        {
                            kind = Prim.Box, uv = 2,
                            pos = new Vector3(bs * (w * 0.5f - t * 0.5f), sh, 0f),
                            size = new Vector3(t, Metrics3D.ArmchairBackHeight - sh, d * 0.98f)
                        });
                    uvs.Add(PanelUv(key, rot, xz, "arml"));              // 3 = 扶手 A
                    uvs.Add(PanelUv(key, rot, xz, "armr"));              // 4 = 扶手 B
                    // 扶手往后伸进靠背（0.88 半长 0.88 > 靠背内面 0.84），否则两者之间留缝
                    for (int i = 0; i < 2; i++)
                    {
                        float sgn = i == 0 ? -1f : 1f;
                        m.parts.Add(longZ
                            ? new Part
                            {
                                kind = Prim.Box, uv = 3 + i,
                                pos = new Vector3(sgn * (w * 0.5f - t * 0.5f), 0f, 0f),
                                size = new Vector3(t, Metrics3D.ArmchairArmHeight, d * 0.88f)
                            }
                            : new Part
                            {
                                kind = Prim.Box, uv = 3 + i,
                                pos = new Vector3(0f, 0f, sgn * (d * 0.5f - t * 0.5f)),
                                size = new Vector3(w * 0.88f, Metrics3D.ArmchairArmHeight, t)
                            });
                    }
                    break;
                }
                case Arch.Nightstand:
                {
                    float h = Metrics3D.NightstandHeight - Metrics3D.SlabThickness;
                    uvs.Add(SlabUv(key, rot, xz));             // 1 = 台面
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, h, 0f),
                        size = new Vector3(w * 0.92f, Metrics3D.SlabThickness, d * 0.92f),
                        uv = 1
                    });
                    uvs.Add(FaceUv(key, rot, xz));            // 2 = 柜体
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, Metrics3D.BaseboardHeight, 0f),
                        size = new Vector3(w * 0.78f, h - Metrics3D.BaseboardHeight, d * 0.72f),
                        noTop = true, uv = 2
                    });
                    uvs.Add(WoodUv(key, rot, xz));             // 3 = 腿（圆柱，三视图没画它们）
                    Legs(m, 3, w * 0.36f, Metrics3D.BaseboardHeight + 0.02f, Metrics3D.LegThickness);
                    break;
                }
                case Arch.Cabinet:
                case Arch.ShelfUnit:
                {
                    bool shelf = arch == Arch.ShelfUnit;
                    float h = shelf ? 0.85f : Metrics3D.CounterHeight;
                    uvs.Add(SlabUv(key, rot, xz));             // 1 = 顶板
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, h, 0f),
                        size = new Vector3(w * 0.96f, Metrics3D.SlabThickness, d * 0.96f),
                        uv = 1
                    });
                    // 柜体的大面用立面段（那就是门板/抽屉）；书架的层板与侧板是 5cm 薄板，
                    // 它们四周的面在图里没有对应像素 —— 一律走木面，顶面仍取俯视段。
                    uvs.Add(shelf ? SlabUv(key, rot, xz) : FaceUv(key, rot, xz));   // 2 = 箱体 / 层板·侧板
                    if (shelf)
                    {
                        // 侧板 + 层板：俯视段给层板顶面，立边与侧板各面用木面。
                        // 层板比侧板内空再小一点（0.84/0.78 vs 侧板的 0.85/0.8）：
                        // 严格共面的两个面会互相闪面（z-fighting）。
                        for (int i = 0; i < 3; i++)
                        {
                            float y = h * (0.12f + i * 0.34f);
                            m.parts.Add(new Part
                            {
                                kind = Prim.Box,
                                pos = new Vector3(0f, y, 0f),
                                size = new Vector3(w * 0.84f, Metrics3D.SlabThickness, d * 0.78f),
                                uv = 2
                            });
                        }
                        // 侧板：Part.pos.y 是**底面**高度，写 h*0.5 会让整块板悬在半空
                        m.parts.Add(new Part
                        {
                            kind = Prim.Box,
                            pos = new Vector3(-w * 0.45f, 0f, 0f),
                            size = new Vector3(Metrics3D.SlabThickness, h, d * 0.8f),
                            uv = 2
                        });
                        m.parts.Add(new Part
                        {
                            kind = Prim.Box,
                            pos = new Vector3(w * 0.45f, 0f, 0f),
                            size = new Vector3(Metrics3D.SlabThickness, h, d * 0.8f),
                            uv = 2
                        });
                    }
                    else
                    {
                        m.parts.Add(new Part
                        {
                            kind = Prim.Box,
                            pos = new Vector3(0f, 0f, 0f),
                            size = new Vector3(w * 0.9f, h, d * 0.8f),
                            uv = 2
                        });
                    }
                    break;
                }
            }
            m.RecalcBounds();

            var builder = new ModelBuilder(uvs, false);
            builder.AddAll(m);
            var st = FirstPerson3DMod.settings;
            float ow = (st != null ? st.outlineWidth : Metrics3D.OutlineWidth);
            return new Built { body = builder.ToMesh(), hull = builder.ToHull(ow) };
        }

        /// <summary>端板（床头/床尾）：垂直于长轴的一块薄板。</summary>
        private static void AddEndPanel(Model3D m, int uvIdx, bool longZ, float thick,
            float sign, float len, float y0, float h)
        {
            if (h <= 0.01f) return;
            float t = Metrics3D.HeadboardThickness;
            var pos = longZ ? new Vector3(0f, y0, sign * (len * 0.5f - t * 0.5f))
                            : new Vector3(sign * (len * 0.5f - t * 0.5f), y0, 0f);
            var size = longZ ? new Vector3(thick * 0.96f, h, t)
                             : new Vector3(t, h, thick * 0.96f);
            m.parts.Add(new Part { kind = Prim.Box, pos = pos, size = size, uv = uvIdx });
        }

        /// <summary>四角立柱：床头那两端高（床头板高），床尾那两端矮（床尾板高）。</summary>
        private static void AddPosts(Model3D m, int uvIdx, float w, float d, Rot4 rot,
            float headH, float footH)
        {
            float r = Metrics3D.LegThickness * 1.1f;
            bool longZ = rot == Rot4.North || rot == Rot4.South;
            float bs = BackSign(rot);
            for (int sa = -1; sa <= 1; sa += 2)
            {
                float h = sa * bs > 0f ? headH : footH;
                for (int sl = -1; sl <= 1; sl += 2)
                {
                    var pos = longZ
                        ? new Vector3(sl * (w * 0.5f - r), 0f, sa * (d * 0.5f - r))
                        : new Vector3(sa * (w * 0.5f - r), 0f, sl * (d * 0.5f - r));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = pos,
                        size = new Vector3(r * 2f, h, r * 2f),
                        uv = uvIdx
                    });
                }
            }
        }

        /// <summary>四只腿，中心外偏 off，脚底在 y0。</summary>
        private static void Legs(Model3D m, int uvIdx, float off, float topY, float r)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(sx * off, 0f, sz * off),
                        size = new Vector3(r, topY, r),
                        uv = uvIdx
                    });
        }

        // ---- 材质与缓存 ----

        private static Material Mat
        {
            get
            {
                if (mat != null) return mat;
                try { atlas = ContentFinder<Texture2D>.Get(FurnitureUVGen.TexPath, reportFailure: false); }
                catch { atlas = null; }
                if (atlas == null) return null;
                atlas.filterMode = FilterMode.Bilinear;
                atlas.wrapMode = TextureWrapMode.Clamp;
                mat = Util3D.CutoutMatFor(atlas);
                return mat;
            }
        }

        private static Built Get(Arch arch, Rot4 rot, float w, float d)
        {
            // 缓存键：原型 + 朝向 + 占格（w*64+d 在 64x64 内唯一，不会互相撞）
            int ck = (((int)arch * 4 + rot.AsByte) * 64 + Mathf.RoundToInt(w * 10f)) * 64 + Mathf.RoundToInt(d * 10f);
            Built b;
            if (cache.TryGetValue(ck, out b)) return b;
            b = Build(arch, rot, w, d);
            cache[ck] = b;
            return b;
        }

        public static void Clear()
        {
            foreach (var kv in cache)
            {
                Util3D.SafeDestroy(kv.Value.body);
                Util3D.SafeDestroy(kv.Value.hull);
            }
            cache.Clear();
        }

        /// <summary>画一件家具；返回 false 表示本层没接管（调用方走原回退路径）。</summary>
        public static bool TryDraw(Thing t, Vector3 pos)
        {
            if (!Handles(t)) return false;
            var m = Mat;
            if (m == null) return false;
            Arch arch;
            if (!ArchOf(t.def, out arch)) return false;
            var occ = GenAdj.OccupiedRect(t);
            float w = Mathf.Max(0.3f, occ.Width), d = Mathf.Max(0.3f, occ.Height);
            var rot = t.Rotation;
            if (arch == Arch.Bed && rot == Rot4.West) rot = Rot4.East;   // 西向无独立图，按东向几何
            var b = Get(arch, rot, w, d);
            if (b == null || b.body == null) return false;
            var matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
            Graphics.DrawMesh(b.body, matrix, m, 0);
            PerfCounters.BuildingMeshesDrawn++;
            if (Fps3DState.DrawOutlines && b.hull != null)
            {
                Graphics.DrawMesh(b.hull, matrix, Util3D.OutlineMat, 0);
                PerfCounters.OutlineMeshesDrawn++;
            }
            return true;
        }
    }
}
