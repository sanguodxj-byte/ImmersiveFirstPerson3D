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
        private enum Arch { Bed, Bedroll, Stool, Chair, Nightstand, Cabinet, ShelfUnit,
                          Armchair, Throne, GrandThrone, Drape, Stele,
                          Table, Lamp, Column, Pot, Marker, FloodLight }

        /// <summary>一条配方：defName 关键字 -> 几何配方 + 取样件名（与 scripts/furn_atlas.py SPEC 同名）。</summary>
        private struct Recipe
        {
            public string[] keys;
            public Arch arch;
            public string item;
            public Recipe(string[] keys, Arch arch, string item)
            {
                this.keys = keys; this.arch = arch; this.item = item;
            }
        }

        // 加新件 = SPEC 加坐标 + 这里加一行。取样件在图集里没有区 = 不接管（走原立板回退），
        // 免得"整族顺手接管了但用的是别件的像素"（bookcase 曾借 shelf 的区，就是这种错）。
        private static readonly Recipe[] Recipes =
        {
            new Recipe(new[] { "table1x2" }, Arch.Table, "table1x2"),
            new Recipe(new[] { "table2x2" }, Arch.Table, "table2x2"),
            new Recipe(new[] { "table2x4" }, Arch.Table, "table2x4"),
            new Recipe(new[] { "table3x3" }, Arch.Table, "table3x3"),
            new Recipe(new[] { "endtable", "nightstand" }, Arch.Nightstand, "endtable"),
            new Recipe(new[] { "armchair" }, Arch.Armchair, "armchair"),
            new Recipe(new[] { "couch" }, Arch.Armchair, "couch"),
            new Recipe(new[] { "diningchair" }, Arch.Chair, "diningchair"),
            // 床家族：关键字先长后短（doublebed 含 bed、bedrolldouble 含 bedroll）。
            // SleepingSpot / DoubleSleepingSpot 不接管：原版是"一圈轮廓 + 中间透明"的地面贴花，
            // 不是实体面，糊成一块实心垫子反而错。
            new Recipe(new[] { "bedrolldouble" }, Arch.Bedroll, "bedrolldouble"),
            new Recipe(new[] { "bedroll" }, Arch.Bedroll, "bedroll"),
            new Recipe(new[] { "doublebed" }, Arch.Bed, "doublebed"),
            new Recipe(new[] { "royalbed" }, Arch.Bed, "royalbed"),
            new Recipe(new[] { "hospitalbed" }, Arch.Bed, "hospitalbed"),
            new Recipe(new[] { "bed" }, Arch.Bed, "bed"),
            // 单图型（Graphic_Single：原版只画一张立面图，四向共用）：顶面取该件真正的水平面，
            // 竖面取图里那条带，图里没画的圆柱一律 wood。
            new Recipe(new[] { "standinglamp" }, Arch.Lamp, "standinglamp"),
            new Recipe(new[] { "floodlight" }, Arch.FloodLight, "floodlight"),
            new Recipe(new[] { "column" }, Arch.Column, "column"),
            new Recipe(new[] { "plantpot" }, Arch.Pot, "plantpot"),
            new Recipe(new[] { "penmarker" }, Arch.Marker, "penmarker"),
            // Royalty 大件：王座沿用扶手椅的几何（箱体 + 靠背 + 两扶手），只把靠背加高；
            // 大王座是"台座 + 金椅 + 尖顶"，帷幔是"横杆 + 布面 + 穗子"，石碑是"碑身 + 基座"。
            new Recipe(new[] { "grandthrone" }, Arch.GrandThrone, "grandthrone"),
            new Recipe(new[] { "throne" }, Arch.Throne, "throne"),
            new Recipe(new[] { "drape" }, Arch.Drape, "drape"),
            new Recipe(new[] { "stelegrand" }, Arch.Stele, "stelegrand"),
            new Recipe(new[] { "stelelarge" }, Arch.Stele, "stelelarge"),
            new Recipe(new[] { "dresser", "cabinet" }, Arch.Cabinet, "dresser"),
            // 关键字先长后短：shelfsmall 含 "shelf"、bookcasesmall 含 "bookcase"，
            // 顺序错了就会用小件的配方去套大件的取样名（或反过来）。
            new Recipe(new[] { "bookcasesmall" }, Arch.Cabinet, "bookcasesmall"),
            new Recipe(new[] { "bookcase" }, Arch.Cabinet, "bookcase"),
            new Recipe(new[] { "shelfsmall" }, Arch.ShelfUnit, "shelfsmall"),
            new Recipe(new[] { "shelf" }, Arch.ShelfUnit, "shelf"),
            new Recipe(new[] { "stool" }, Arch.Stool, "stool"),
        };

        private class Built
        {
            public Mesh body;
            public Mesh hull;
        }

        private static readonly Dictionary<int, Built> cache = new Dictionary<int, Built>();
        private static readonly Dictionary<string, int> keyIds = new Dictionary<string, int>();

        private static int KeyId(string k)
        {
            int id;
            if (keyIds.TryGetValue(k, out id)) return id;
            id = keyIds.Count;
            keyIds[k] = id;
            return id;
        }
        private static Material mat;
        private static Texture2D atlas;

        /// <summary>本系统的开关（独立于其它层，便于单点回退）。</summary>
        public static bool Enabled = true;

        // ---- 归类：只看 defName 关键字（def 不作尺寸依据）----

        private static bool RecipeOf(ThingDef d, out Arch arch, out string key)
        {
            arch = Arch.Stool; key = null;
            if (d == null) return false;
            string n = (d.defName ?? "").ToLowerInvariant();
            for (int i = 0; i < Recipes.Length; i++)
            {
                var r = Recipes[i];
                if (!Has(n, r.keys)) continue;
                if (!regionItems.Contains(r.item)) return false;   // 没有该件的区 -> 不接管
                arch = r.arch; key = r.item; return true;
            }
            return false;
        }

        /// <summary>图集表里出现过的取样件名（键的第一段）。</summary>
        private static readonly HashSet<string> regionItems = BuildRegionItems();

        private static HashSet<string> BuildRegionItems()
        {
            var set = new HashSet<string>();
            foreach (var k in FurnitureUVGen.R.Keys)
            {
                int dot = k.IndexOf('.');
                if (dot > 0) set.Add(k.Substring(0, dot));
            }
            return set;
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
            Arch a; string k;
            return RecipeOf(t.def, out a, out k);
        }

        // ---- 取样区 ----

        /// <summary>
        /// 本层用到的全部取样区（与 scripts/furn_atlas.py 的 SPEC 一一对应）。
        /// 校验器按字面量比对，多一个/少一个都会报，防止表与几何脱节。
        /// </summary>
        internal static readonly string[] AllRegions =
        {
            "armchair.east.arml",
            "armchair.east.back",
            "armchair.east.top",
            "armchair.east.wood",
            "armchair.north.arml",
            "armchair.north.armr",
            "armchair.north.back",
            "armchair.north.top",
            "armchair.north.wood",
            "armchair.south.back",
            "armchair.south.front",
            "armchair.south.top",
            "armchair.south.wood",
            "bed.east.front",
            "bed.east.top",
            "bed.north.front",
            "bed.north.top",
            "bed.south.front",
            "bed.south.top",
            "bed.south.wood",
            "bedroll.east.front",
            "bedroll.east.pillow",
            "bedroll.east.top",
            "bedroll.east.wood",
            "bedroll.south.front",
            "bedroll.south.pillow",
            "bedroll.south.top",
            "bedroll.south.wood",
            "bedrolldouble.east.front",
            "bedrolldouble.east.pillow",
            "bedrolldouble.east.top",
            "bedrolldouble.east.wood",
            "bedrolldouble.south.front",
            "bedrolldouble.south.pillow",
            "bedrolldouble.south.top",
            "bedrolldouble.south.wood",
            "bookcase.east.front",
            "bookcase.east.top",
            "bookcase.east.wood",
            "bookcase.south.front",
            "bookcase.south.top",
            "bookcase.south.wood",
            "bookcasesmall.east.front",
            "bookcasesmall.east.top",
            "bookcasesmall.east.wood",
            "bookcasesmall.south.front",
            "bookcasesmall.south.top",
            "bookcasesmall.south.wood",
            "column.south.front",
            "column.south.top",
            "column.south.wood",
            "couch.east.back",
            "couch.east.front",
            "couch.east.top",
            "couch.east.wood",
            "couch.north.arml",
            "couch.north.armr",
            "couch.north.back",
            "couch.north.front",
            "couch.north.wood",
            "couch.south.arml",
            "couch.south.armr",
            "couch.south.back",
            "couch.south.front",
            "couch.south.top",
            "couch.south.wood",
            "diningchair.east.top",
            "diningchair.east.wood",
            "diningchair.north.top",
            "diningchair.south.back",
            "diningchair.south.top",
            "diningchair.south.wood",
            "doublebed.east.front",
            "doublebed.east.pillow",
            "doublebed.east.top",
            "doublebed.east.wood",
            "doublebed.south.front",
            "doublebed.south.pillow",
            "doublebed.south.top",
            "doublebed.south.wood",
            "drape.south.front",
            "drape.south.mid",
            "drape.south.tassel",
            "drape.south.tassel2",
            "drape.south.tassel3",
            "drape.south.top",
            "drape.south.wood",
            "dresser.east.front",
            "dresser.east.top",
            "dresser.north.front",
            "dresser.north.top",
            "dresser.south.front",
            "dresser.south.top",
            "dresser.south.wood",
            "endtable.east.top",
            "endtable.north.front",
            "endtable.north.top",
            "endtable.south.front",
            "endtable.south.top",
            "endtable.south.wood",
            "floodlight.south.basel",
            "floodlight.south.baser",
            "floodlight.south.pole",
            "floodlight.south.top",
            "floodlight.south.wood",
            "grandthrone.south.front",
            "grandthrone.south.gold",
            "grandthrone.south.spire",
            "grandthrone.south.top",
            "grandthrone.south.wood",
            "hospitalbed.east.front",
            "hospitalbed.east.pillow",
            "hospitalbed.east.top",
            "hospitalbed.east.wood",
            "hospitalbed.south.front",
            "hospitalbed.south.pillow",
            "hospitalbed.south.top",
            "hospitalbed.south.wood",
            "penmarker.south.front",
            "penmarker.south.tip",
            "penmarker.south.top",
            "penmarker.south.wood",
            "plantpot.south.front",
            "plantpot.south.top",
            "plantpot.south.wood",
            "royalbed.east.front",
            "royalbed.east.pillow",
            "royalbed.east.top",
            "royalbed.east.wood",
            "royalbed.south.front",
            "royalbed.south.pillow",
            "royalbed.south.top",
            "royalbed.south.wood",
            "shelf.east.front",
            "shelf.east.top",
            "shelf.north.front",
            "shelf.north.top",
            "shelf.south.front",
            "shelf.south.top",
            "shelf.south.wood",
            "shelfsmall.east.front",
            "shelfsmall.east.top",
            "shelfsmall.east.wood",
            "shelfsmall.south.front",
            "shelfsmall.south.top",
            "shelfsmall.south.wood",
            "standinglamp.south.front",
            "standinglamp.south.neck",
            "standinglamp.south.top",
            "standinglamp.south.wood",
            "stelegrand.south.crest",
            "stelegrand.south.face",
            "stelegrand.south.front",
            "stelegrand.south.mid",
            "stelegrand.south.shoulder",
            "stelegrand.south.top",
            "stelegrand.south.wood",
            "stelelarge.south.crest",
            "stelelarge.south.face",
            "stelelarge.south.front",
            "stelelarge.south.mid",
            "stelelarge.south.shoulder",
            "stelelarge.south.top",
            "stelelarge.south.wood",
            "stool.east.front",
            "stool.east.top",
            "stool.north.front",
            "stool.north.top",
            "stool.north.wood",
            "table1x2.east.edge",
            "table1x2.east.front",
            "table1x2.east.top",
            "table1x2.east.wood",
            "table1x2.north.edge",
            "table1x2.north.front",
            "table1x2.north.top",
            "table1x2.north.wood",
            "table2x2.north.edge",
            "table2x2.north.front",
            "table2x2.north.top",
            "table2x2.north.wood",
            "table2x4.east.edge",
            "table2x4.east.front",
            "table2x4.east.top",
            "table2x4.east.wood",
            "table2x4.north.edge",
            "table2x4.north.front",
            "table2x4.north.top",
            "table2x4.north.wood",
            "table3x3.north.edge",
            "table3x3.north.front",
            "table3x3.north.top",
            "table3x3.north.wood",
            "throne.south.arml",
            "throne.south.armr",
            "throne.south.back",
            "throne.south.front",
            "throne.south.top",
            "throne.south.wood",
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
            if (!HasRegion(arch, "front"))
            {
                // 有的件整张图就没有立面段（diningchair 的"腿带"是纯线稿，几何也不拿它当面）：
                // 退到木面，而不是 Vector4.zero —— 那会去采图集原点。
                var wf = WoodRegion(arch, rot);
                for (int i = 0; i < 4; i++) side4[i] = wf;
            }
            else
            {
                side4[0] = Region(arch, Opp(v), "front");
                side4[1] = Region(arch, v, "front");
                side4[2] = Region(arch, cross, "front");
                side4[3] = side4[2];
            }
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

        // 本层新增件的尺寸写在这里，不进 Metrics3D（那是并行修改中的共享文件）
        private const float PillowThickness = 0.11f;
        private const float PillowDepth = 0.34f;        // 沿床长方向占多深
        private const float BedrollThickness = 0.08f;
        private const float ChairBackThickness = 0.06f;
        private const float ColumnHeight = 1.85f;   // 原版柱图 64x128：1 格宽、2 格高
        private const float ThroneSeatHeight = 0.48f;
        private const float ThroneBackHeight = 1.35f;
        private const float DrapeRodHeight = 1.42f;
        private const float ClothThickness = 0.06f;

        private static bool HasRegion(string key, string region)
        {
            string[] vs = { "south", "north", "east" };
            for (int i = 0; i < vs.Length; i++)
            {
                var r = Reg(key + "." + vs[i] + "." + region);
                if (r.w > r.y) return true;
            }
            return false;
        }

        private static Built Build(Arch arch, string key, Rot4 rot, float w, float d)
        {
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
                    // 5 = 枕头：图里枕头与床垫是两块分开的白面，模型上也得分开才认得出"床"
                    if (HasRegion(key, "pillow"))
                    {
                        int pi = uvs.Count;
                        uvs.Add(UvNames(key, rot, xz, "pillow", "pillow"));
                        AddPillows(m, pi, w, d, rot, Metrics3D.MattressSurface,
                                   PillowDepth, PillowThickness);
                    }
                    break;
                }
                case Arch.Bedroll:
                {
                    // 地铺：一条薄垫 + 枕头。床架、床头板、立柱一概没有 —— 那是"床"的特征；
                    // bedroll 占格与床相同（1x2），只能靠轮廓区分。
                    int mi = uvs.Count;
                    uvs.Add(FaceUv(key, rot, xz));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(w - 0.08f, BedrollThickness, d - 0.08f),
                        uv = mi
                    });
                    if (HasRegion(key, "pillow"))
                    {
                        int pi = uvs.Count;
                        uvs.Add(UvNames(key, rot, xz, "pillow", "pillow"));
                        AddPillows(m, pi, w, d, rot, BedrollThickness, PillowDepth, PillowThickness);
                    }
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
                case Arch.Chair:
                {
                    // 餐椅：座面是画出来的椭圆 -> 圆柱。靠背板必须落在座盘的**弦内**：
                    // 原来板宽 0.45、摆在 z=0.23（盘半径 0.26），板底两角整个悬在盘外，
                    // 人眼高度看就是"椅背浮着"。腿同理要张到座盘沿下。
                    float sh = Metrics3D.ChairSeatHeight;
                    float cush = Metrics3D.SeatCushionThickness;
                    float sd = Mathf.Min(0.44f, Mathf.Min(w, d) * 0.62f);   // 座盘直径
                    float t = ChairBackThickness;
                    float r = sd * 0.5f;
                    float bz = r - t * 1.2f;                                // 靠背中心（往座盘里收）
                    float bw = 1.84f * Mathf.Sqrt(Mathf.Max(0.0025f,
                                    r * r - (bz + t * 0.5f) * (bz + t * 0.5f)));
                    int si = uvs.Count;
                    var seat = FaceUv(key, rot, new Vector2(sd, sd));
                    seat.side = WoodRegion(key, rot);
                    uvs.Add(seat);
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, sh - cush, 0f),
                        size = new Vector3(sd, cush, sd),
                        uv = si
                    });
                    float bs = BackSign(rot);
                    int bi = uvs.Count;
                    uvs.Add(PanelUv(key, rot, xz, "back"));
                    float bh = Metrics3D.ChairBackHeight - (sh - cush);      // 板底插进座盘以下
                    m.parts.Add(longZ
                        ? new Part
                        {
                            kind = Prim.Box, uv = bi,
                            pos = new Vector3(0f, sh - cush - 0.06f, bs * bz),
                            size = new Vector3(bw, bh, t)
                        }
                        : new Part
                        {
                            kind = Prim.Box, uv = bi,
                            pos = new Vector3(bs * bz, sh - cush - 0.06f, 0f),
                            size = new Vector3(t, bh, bw)
                        });
                    int li = uvs.Count;
                    uvs.Add(WoodUv(key, rot, xz));
                    Legs(m, li, r * 0.72f, sh - cush + 0.03f, Metrics3D.LegThickness);
                    break;
                }
                case Arch.Armchair:
                case Arch.Throne:
                {
                    // 线稿型软包椅：整件是"白面 + 黑框"，没有俯视段/立面段之分。
                    // 座面/靠背/两扶手各点一条白面带，板件的边棱与腿用木面。
                    // 王座 = 同一条几何，只是座更高、靠背顶到 1.35m。
                    bool throne = arch == Arch.Throne;
                    float sh = throne ? ThroneSeatHeight : Metrics3D.ArmchairSeatHeight;
                    float bh = throne ? ThroneBackHeight : Metrics3D.ArmchairBackHeight;
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
                            size = new Vector3(w * 0.98f, bh - sh, t)
                        }
                        : new Part
                        {
                            kind = Prim.Box, uv = 2,
                            pos = new Vector3(bs * (w * 0.5f - t * 0.5f), sh, 0f),
                            size = new Vector3(t, bh - sh, d * 0.98f)
                        });
                    uvs.Add(PanelUv(key, rot, xz, "arml"));             // 3 = 扶手 A
                    uvs.Add(PanelUv(key, rot, xz, "armr"));             // 4 = 扶手 B
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
                case Arch.Table:
                {
                    // 台面（顶 = top 带，四周一圈 = edge 带）+ 四条腿（侧面用画出来的腿条，
                    // 圆盖用 wood：圆柱的端面在平面图里根本没有对应像素）
                    float th = Metrics3D.SlabThickness;
                    float h = Metrics3D.TableHeight - th;              // 台面上沿 = TableHeight
                    uvs.Add(UvNames(key, rot, xz, "top", "edge"));     // 1 = 台面
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, h, 0f),
                        size = new Vector3(w - 0.04f, th, d - 0.04f),
                        uv = 1
                    });
                    uvs.Add(UvNames(key, rot, xz, "wood", "front"));   // 2 = 腿
                    Legs(m, 2, Mathf.Min(w, d) * 0.5f - 0.14f, h, Metrics3D.LegThickness);
                    break;
                }
                case Arch.Lamp:
                {
                    // 立灯：底座 + 灯杆 + 灯罩。灯罩 = 图里那圈发光的白面（top）；
                    // 灯杆在图里只是 3px 的一条线，没有可取的面 —— 走木面。
                    float poleTop = Metrics3D.LampPoleHeight - Metrics3D.LampShadeHeight;
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "front", "front"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(0.36f, 0.05f, 0.36f),
                        uv = bi
                    });
                    int pi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "wood", "neck"));   // 杆身=图里那段颈，端面没画=木面
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0.05f, 0f),
                        size = new Vector3(0.05f, poleTop - 0.05f, 0.05f),
                        uv = pi
                    });
                    int si = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "top", "top"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, poleTop, 0f),
                        size = new Vector3(Metrics3D.LampShadeDiameter, Metrics3D.LampShadeHeight,
                                           Metrics3D.LampShadeDiameter),
                        uv = si
                    });
                    break;
                }
                case Arch.Column:
                {
                    int ci = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "front", "front"));   // 1 = 柱身（图里那条竖带）
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0.14f, 0f),
                        size = new Vector3(0.34f, ColumnHeight - 0.30f, 0.34f),
                        uv = ci
                    });
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "top", "front"));     // 2 = 底座/柱头：水平面另有带
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(0.56f, 0.14f, 0.56f),
                        uv = bi
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, ColumnHeight - 0.16f, 0f),
                        size = new Vector3(0.60f, 0.16f, 0.60f),
                        uv = bi
                    });
                    break;
                }
                case Arch.Pot:
                {
                    // 花盆：原版是一张俯视图 —— 中心那块棕色是盆土（顶面），外圈白环是盆壁。
                    int i0 = uvs.Count;
                    // 顶面 UV 按盆口尺寸铺，别按占格 1x1 铺（那会把盆土糊到 2 倍大）
                    uvs.Add(FaceUv(key, rot, new Vector2(Metrics3D.PlantPotDiameter,
                                                         Metrics3D.PlantPotDiameter)));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(Metrics3D.PlantPotDiameter, Metrics3D.PlantPotHeight,
                                           Metrics3D.PlantPotDiameter),
                        uv = i0
                    });
                    break;
                }
                case Arch.Marker:
                {
                    int pi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "front", "front"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(0.10f, 0.85f, 0.10f),
                        uv = pi
                    });
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "top", "top"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.60f, 0f),
                        size = new Vector3(0.62f, 0.20f, 0.08f),
                        uv = bi
                    });
                    int ti = uvs.Count;                       // 桩头露在牌子上面那一截
                    uvs.Add(UvNames(key, rot, xz, "tip", "tip"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.80f, 0f),
                        size = new Vector3(0.08f, 0.14f, 0.08f),   // 比桩身窄一圈，别与它共面
                        uv = ti
                    });
                    break;
                }
                case Arch.FloodLight:
                {
                    // 底座：图里是一条椭圆，左半 = 侧壁，右半 = 顶面（两条都不含洞）
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "baser", "basel"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(0.62f, 0.14f, 0.62f),
                        uv = bi
                    });
                    int pi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "pole", "pole"));     // 2 = 灯杆（图里杆与底座是一条竖带）
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0.14f, 0f),
                        size = new Vector3(0.16f, 0.62f, 0.16f),
                        uv = pi
                    });
                    int hi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "top", "top"));       // 3 = 横灯头（两端黄色灯面就在这条带里）
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.76f, 0f),
                        size = new Vector3(0.90f, 0.24f, 0.30f),
                        uv = hi
                    });
                    break;
                }
                case Arch.GrandThrone:
                {
                    // 大王座（3x2）：白台座 + 台阶 + 金椅 + 尖顶。图里金与白是两块分开的带，
                    // 所以几何也分两批板件，各点自己的取样带。
                    float bs = BackSign(rot);
                    int di = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "top", "front"));      // 1 = 台座（顶面/立面分开）
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(w * 0.96f, 0.16f, d * 0.94f),
                        uv = di
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.16f, -bs * d * 0.22f),
                        size = new Vector3(w * 0.78f, 0.14f, d * 0.50f),
                        uv = di
                    });
                    int gi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "gold", "gold"));      // 2 = 金椅 + 尖顶
                    float seatY = 0.30f;
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, seatY, 0f),
                        size = new Vector3(0.95f, 0.22f, 0.85f),
                        uv = gi
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, seatY + 0.22f, bs * 0.36f),
                        size = new Vector3(0.80f, 0.85f, 0.14f),
                        uv = gi
                    });
                    int qi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "spire", "spire"));    // 3 = 尖顶
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, seatY + 1.07f, bs * 0.30f),
                        size = new Vector3(0.34f, 0.42f, 0.28f),
                        uv = qi
                    });
                    int ai = gi;                                          // 4 = 两侧金扶手柱（同金带）
                    for (int sd = -1; sd <= 1; sd += 2)
                        m.parts.Add(new Part
                        {
                            kind = Prim.Box,
                            pos = new Vector3(sd * 0.54f, seatY - 0.06f, -0.04f),
                            size = new Vector3(0.22f, 0.62f, 0.70f),
                            uv = ai
                        });
                    break;
                }
                case Arch.Drape:
                {
                    // 帷幔（2x1）：横杆 + 垂下的布面 + 三个穗子。布面是竖直的，原版把它画成
                    // 三段横带 + 一道波浪裙摆，所以大面/薄边/顶面各取一段；穗子只有左右两块
                    // 是实心矩形（中间那块与它们之间是透的），左右各取一条、中间复用。
                    float bs = BackSign(rot);
                    string sv = ViewOf(rot);
                    int ti = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "tassel", "tassel"));   // 1 = 横杆 + 左穗
                    int tj = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "tassel2", "tassel2")); // 2 = 右穗
                    int tk = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "tassel3", "tassel3")); // 3 = 中穗
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, DrapeRodHeight, bs * 0.02f),
                        size = new Vector3(w * 0.86f, 0.09f, 0.09f),
                        uv = ti
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(-w * 0.36f, 0.34f, bs * 0.02f),
                        size = new Vector3(0.09f, 0.13f, 0.09f),
                        uv = ti
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.34f, bs * 0.02f),
                        size = new Vector3(0.09f, 0.13f, 0.09f),
                        uv = tk
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(w * 0.36f, 0.34f, bs * 0.02f),
                        size = new Vector3(0.09f, 0.13f, 0.09f),
                        uv = tj
                    });
                    int ci = uvs.Count;
                    uvs.Add(Bands(key, sv, "mid", "front", "top",           // 4 = 布面：大面取布身中段
                                  new Vector2(w * 0.82f, ClothThickness)));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.47f, bs * 0.02f),
                        size = new Vector3(w * 0.82f, 0.95f, ClothThickness),
                        uv = ci
                    });
                    break;
                }
                case Arch.Stele:
                {
                    // 石碑：一块竖碑 + 一个基座。碑身的大面 = 图里那片刻纹（top），
                    // 基座 = 图底部那条带（front）。占格 2x2 / 3x3 只改高度与宽度。
                    float h = 0.55f + 0.50f * Mathf.Max(w, d);
                    // 碑身是上窄下宽的梯形：拆成三段叠块，每段的大面/薄边各取图里
                    // 对应高度的那条横带。梯形逐带取样才铺得满 —— 一条矩形要么含洞、
                    // 要么丢掉两侧（原版这块碑的图就是逐层收窄的）。
                    string sv = ViewOf(rot);
                    float hLow = (h - 0.18f) * 0.40f, hMid = (h - 0.18f) * 0.32f;
                    int si = uvs.Count;
                    uvs.Add(Bands(key, sv, "face", "mid", "mid", new Vector2(w * 0.56f, 0.24f)));   // 1 = 下段
                    int ui = uvs.Count;
                    uvs.Add(Bands(key, sv, "mid", "shoulder", "mid", new Vector2(w * 0.48f, 0.22f))); // 2 = 中段
                    int vi = uvs.Count;
                    uvs.Add(Bands(key, sv, "top", "top", "crest", new Vector2(w * 0.38f, 0.20f)));   // 3 = 上段
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.18f, 0f),
                        size = new Vector3(w * 0.56f, hLow, 0.24f),
                        uv = si
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.18f + hLow, 0f),
                        size = new Vector3(w * 0.48f, hMid, 0.22f),
                        uv = ui
                    });
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.18f + hLow + hMid, 0f),
                        size = new Vector3(w * 0.38f, h - 0.18f - hLow - hMid, 0.20f),
                        uv = vi
                    });
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, xz, "front", "front"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(w * 0.72f, 0.18f, d * 0.42f),
                        uv = bi
                    });
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
                        // 层板要**压进**侧板（0.90 覆盖侧板内面 0.875），又不能与它共面：
                        // 0.84 会留 3.5cm 缝 -> 部件清单上看是三块悬空孤岛（furn_guard 报的）。
                        for (int i = 0; i < 3; i++)
                        {
                            float y = h * (0.12f + i * 0.34f);
                            m.parts.Add(new Part
                            {
                                kind = Prim.Box,
                                pos = new Vector3(0f, y, 0f),
                                size = new Vector3(w * 0.90f, Metrics3D.SlabThickness, d * 0.74f),
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

        /// <summary>一条带子铺满某个面：大面(±Z)取 face，薄边(±X)取 edge，顶面取 cap。
        /// 石碑这种"逐层收窄"的图靠它把每条横带派到对应的那一面。</summary>
        private static UvSpec Bands(string key, string view, string face, string edge,
            string cap, Vector2 xz)
        {
            var f = Region(key, view, face);
            var e = Region(key, view, edge);
            return new UvSpec
            {
                top = Region(key, view, cap),
                side = f,
                side4 = new[] { f, f, e, e },
                sideTileH = 0f,
                sideTileW = 0f,
                modelXZ = xz
            };
        }

        /// <summary>枕头：贴着床头那一端，沿床宽并排（2 格宽的床放两个）。pos.y 是底面，
        /// 所以 surfY 传"床垫上表面"，枕头就坐在床垫上而不是陷进去。</summary>
        private static void AddPillows(Model3D m, int uvIdx, float w, float d, Rot4 rot,
            float surfY, float depth, float thick)
        {
            bool longZ = rot == Rot4.North || rot == Rot4.South;
            float bs = BackSign(rot);
            float len = longZ ? d : w, wid = longZ ? w : d;
            int n = wid >= 1.5f ? 2 : 1;
            float pw = wid * 0.84f / n;
            for (int i = 0; i < n; i++)
            {
                float off = (i - (n - 1) * 0.5f) * pw * 1.08f;
                var pos = longZ
                    ? new Vector3(off, surfY, bs * (len * 0.5f - depth * 0.5f - 0.07f))
                    : new Vector3(bs * (len * 0.5f - depth * 0.5f - 0.07f), surfY, off);
                var size = longZ
                    ? new Vector3(pw * 0.94f, thick, depth)
                    : new Vector3(depth, thick, pw * 0.94f);
                m.parts.Add(new Part { kind = Prim.Box, pos = pos, size = size, uv = uvIdx });
            }
        }

        /// <summary>四角立柱：床头那两端高（床头板高），床尾那两端矮（床尾板高）。</summary>
        private static void AddPosts(Model3D m, int uvIdx, float w, float d, Rot4 rot,
            float headH, float footH)
        {
            float r = Metrics3D.LegThickness * 1.1f;
            bool longZ = rot == Rot4.North || rot == Rot4.South;
            float bs = BackSign(rot);
            // 立柱外圆不能与床板外圆严格共面（原来都在 w/2、d/2 上，871cm² 的面对面贴
            // 在一起必然闪面），也不能与床垫面/床板顶同高 —— 一律内缩 2cm、柱头错开 2cm。
            // 0.02 正好等于端板那 4% 的宽度损失（板边在 w*0.48），两者会贴成
            // 同一圆柱面 -> 390cm² 共面。内缩到 0.05 才真正错开。
            const float inset = 0.05f;
            for (int sa = -1; sa <= 1; sa += 2)
            {
                float h = (sa * bs > 0f ? headH : footH) + (sa * bs > 0f ? 0.02f : -0.02f);
                for (int sl = -1; sl <= 1; sl += 2)
                {
                    var pos = longZ
                        ? new Vector3(sl * (w * 0.5f - r - inset), 0f, sa * (d * 0.5f - r - inset))
                        : new Vector3(sa * (w * 0.5f - r - inset), 0f, sl * (d * 0.5f - r - inset));
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

        private static Built Get(Arch arch, string key, Rot4 rot, float w, float d)
        {
            // 缓存键：取样件 + 配方 + 朝向 + 占格（同一配方下不同件不能共用模型）
            int ck = (((KeyId(key) * 8 + (int)arch) * 4 + rot.AsByte) * 64
                      + Mathf.RoundToInt(w * 10f)) * 64 + Mathf.RoundToInt(d * 10f);
            Built b;
            if (cache.TryGetValue(ck, out b)) return b;
            b = Build(arch, key, rot, w, d);
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
            Arch arch; string key;
            if (!RecipeOf(t.def, out arch, out key)) return false;
            var occ = GenAdj.OccupiedRect(t);
            float w = Mathf.Max(0.3f, occ.Width), d = Mathf.Max(0.3f, occ.Height);
            var rot = t.Rotation;
            if (arch == Arch.Bed && rot == Rot4.West) rot = Rot4.East;   // 西向无独立图，按东向几何
            var b = Get(arch, key, rot, w, d);
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
