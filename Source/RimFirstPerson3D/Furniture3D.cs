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
    // 打包成 Textures\FPS3D\FurnitureAtlas.png，UV 常量表由 FurnitureUVGen.cs
    // 自动生成。取样的像素**全部来自原版贴图**，只有黑色笔画被邻域补绘替换
    // （描边由反绕外壳网格负责，不进材质）。校验：scripts/furn_uv_check.py。
    //
    // 【尺寸】一律取 Metrics3D（现实家具尺寸），不从原版 def 反推；
    // def 只用于定位：占几格、朝向。
    [StaticConstructorOnStartup]
    public static class Furniture3D
    {
        private enum Arch { Bed, Bedroll, Stool, Chair, Nightstand, Cabinet, ShelfUnit,
                           Armchair, Throne, GrandThrone, Drape, Stele, ProductionBench,
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
            // Production phase 1: exact vanilla defs only. Do not broaden these to a
            // table/smithy keyword: modded workbenches must retain the safe fallback.
            new Recipe(new[] { "tablebutcher" }, Arch.ProductionBench, "tablebutcher"),
            new Recipe(new[] { "tablesculpting" }, Arch.ProductionBench, "tablesculpting"),
            new Recipe(new[] { "fueledsmithy" }, Arch.ProductionBench, "tablesmithingfueled"),
            new Recipe(new[] { "electricsmithy" }, Arch.ProductionBench, "tablesmithingelectric"),
            new Recipe(new[] { "electricstove" }, Arch.ProductionBench, "tablestoveelectric"),
            new Recipe(new[] { "fueledstove" }, Arch.ProductionBench, "tablestovefueled"),
            new Recipe(new[] { "handtailoringbench" }, Arch.ProductionBench, "tabletailorhand"),
            new Recipe(new[] { "electrictailoringbench" }, Arch.ProductionBench, "tabletailorelectric"),
            new Recipe(new[] { "tablestonecutter" }, Arch.ProductionBench, "tablestonecutter"),
            new Recipe(new[] { "tablemachining" }, Arch.ProductionBench, "tablemachining"),
            new Recipe(new[] { "druglab" }, Arch.ProductionBench, "tabledruglab"),
            new Recipe(new[] { "brewery" }, Arch.ProductionBench, "tablebrewery"),
            new Recipe(new[] { "electricsmelter" }, Arch.ProductionBench, "electricsmelter"),
            new Recipe(new[] { "simpleresearchbench" }, Arch.ProductionBench, "researchbenchsimple"),
            new Recipe(new[] { "hitechresearchbench" }, Arch.ProductionBench, "researchbenchhitech"),
            new Recipe(new[] { "fabricationbench" }, Arch.ProductionBench, "fabricationbench"),
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

        private struct RecipeCache
        {
            public bool handled;
            public Arch arch;
            public string item;
        }

        private static readonly Dictionary<ThingDef, RecipeCache> recipeDefCache = new Dictionary<ThingDef, RecipeCache>();

        private static bool RecipeOf(ThingDef d, out Arch arch, out string key)
        {
            if (d == null) { arch = Arch.Stool; key = null; return false; }
            if (recipeDefCache.TryGetValue(d, out var rc))
            {
                arch = rc.arch;
                key = rc.item;
                return rc.handled;
            }
            arch = Arch.Stool; key = null;
            string n = (d.defName ?? "").ToLowerInvariant();
            bool handled = false;
            for (int i = 0; i < Recipes.Length; i++)
            {
                var r = Recipes[i];
                // Production recipes are deliberately exact; the legacy furniture
                // catalogue keeps its established family matching below.
                bool match = (r.arch == Arch.ProductionBench)
                    ? (n == r.keys[0]) : Has(n, r.keys);
                if (!match) continue;
                if (!regionItems.Contains(r.item)) break;   // 没有该件的区 -> 不接管
                arch = r.arch; key = r.item; handled = true; break;
            }
            recipeDefCache[d] = new RecipeCache { handled = handled, arch = arch, item = key };
            return handled;
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

        public static bool ResolveBuildDef(Thing t, out ThingDef buildDef, out bool isConstructible)
        {
            buildDef = null;
            isConstructible = false;
            if (t == null || t.def == null) return false;
            if (t is Blueprint bp)
            {
                buildDef = bp.EntityToBuild() as ThingDef;
                isConstructible = true;
                return buildDef != null;
            }
            if (t is Frame fr)
            {
                buildDef = fr.BuildDef;
                isConstructible = true;
                return buildDef != null;
            }
            buildDef = t.def;
            return true;
        }

        /// <summary>该建筑（或其蓝图/建造框架）是否由本层绘制（BuildingLayer3D 据此跳过立板回退）。</summary>
        public static bool Handles(Thing t)
        {
            if (!Enabled || !ResolveBuildDef(t, out var d, out _)) return false;
            if (d.category != ThingCategory.Building) return false;
            if (d.Fillage == FillCategory.Full) return false;
            if (t is Building_Door || (d.thingClass != null && typeof(Building_Door).IsAssignableFrom(d.thingClass))) return false;
            Arch a; string k;
            return RecipeOf(d, out a, out k);
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
            "armchair.east.front",
            "armchair.east.top",
            "armchair.east.wood",
            "armchair.north.arml",
            "armchair.north.armr",
            "armchair.north.back",
            "armchair.north.front",
            "armchair.north.top",
            "armchair.north.wood",
            "armchair.south.arml",
            "armchair.south.armr",
            "armchair.south.back",
            "armchair.south.front",
            "armchair.south.top",
            "armchair.south.wood",
            "bed.east.front",
            "bed.east.pillow",
            "bed.east.top",
            "bed.east.wood",
            "bed.north.front",
            "bed.north.pillow",
            "bed.north.top",
            "bed.south.front",
            "bed.south.pillow",
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
            "couch.east.arml",
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
            "electricsmelter.east.front",
            "electricsmelter.east.hood",
            "electricsmelter.east.hood_s",
            "electricsmelter.east.top",
            "electricsmelter.north.front",
            "electricsmelter.north.hood",
            "electricsmelter.north.hood_s",
            "electricsmelter.north.top",
            "electricsmelter.south.front",
            "electricsmelter.south.hood",
            "electricsmelter.south.hood_s",
            "electricsmelter.south.top",
            "endtable.east.front",
            "endtable.east.top",
            "endtable.north.front",
            "endtable.north.top",
            "endtable.south.front",
            "endtable.south.top",
            "endtable.south.wood",
            "fabricationbench.east.body",
            "fabricationbench.east.front",
            "fabricationbench.east.mach",
            "fabricationbench.east.top",
            "fabricationbench.north.body",
            "fabricationbench.north.front",
            "fabricationbench.north.mach",
            "fabricationbench.north.top",
            "fabricationbench.south.body",
            "fabricationbench.south.front",
            "fabricationbench.south.mach",
            "fabricationbench.south.top",
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
            "researchbenchhitech.east.flask",
            "researchbenchhitech.east.front",
            "researchbenchhitech.east.top",
            "researchbenchhitech.east.tray",
            "researchbenchhitech.north.flask",
            "researchbenchhitech.north.front",
            "researchbenchhitech.north.top",
            "researchbenchhitech.north.tray",
            "researchbenchhitech.south.flask",
            "researchbenchhitech.south.front",
            "researchbenchhitech.south.top",
            "researchbenchhitech.south.tray",
            "researchbenchsimple.east.front",
            "researchbenchsimple.east.top",
            "researchbenchsimple.north.front",
            "researchbenchsimple.north.top",
            "researchbenchsimple.south.front",
            "researchbenchsimple.south.top",
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
            "tablebrewery.east.front",
            "tablebrewery.east.top",
            "tablebrewery.east.vat",
            "tablebrewery.east.vat_s",
            "tablebrewery.north.front",
            "tablebrewery.north.top",
            "tablebrewery.north.vat",
            "tablebrewery.north.vat_s",
            "tablebrewery.south.front",
            "tablebrewery.south.top",
            "tablebrewery.south.vat",
            "tablebrewery.south.vat_s",
            "tablebutcher.east.front",
            "tablebutcher.east.top",
            "tablebutcher.north.front",
            "tablebutcher.north.top",
            "tablebutcher.south.front",
            "tablebutcher.south.top",
            "tabledruglab.east.blu",
            "tabledruglab.east.col",
            "tabledruglab.east.front",
            "tabledruglab.east.grn",
            "tabledruglab.east.pad",
            "tabledruglab.east.top",
            "tabledruglab.east.vat",
            "tabledruglab.north.blu",
            "tabledruglab.north.col",
            "tabledruglab.north.front",
            "tabledruglab.north.grn",
            "tabledruglab.north.pad",
            "tabledruglab.north.top",
            "tabledruglab.north.vat",
            "tabledruglab.south.blu",
            "tabledruglab.south.col",
            "tabledruglab.south.front",
            "tabledruglab.south.grn",
            "tabledruglab.south.pad",
            "tabledruglab.south.top",
            "tabledruglab.south.vat",
            "tablemachining.east.body",
            "tablemachining.east.front",
            "tablemachining.east.pad",
            "tablemachining.east.top",
            "tablemachining.north.body",
            "tablemachining.north.front",
            "tablemachining.north.pad",
            "tablemachining.north.top",
            "tablemachining.south.body",
            "tablemachining.south.front",
            "tablemachining.south.pad",
            "tablemachining.south.top",
            "tablesculpting.east.front",
            "tablesculpting.east.stone",
            "tablesculpting.east.stone_s",
            "tablesculpting.east.top",
            "tablesculpting.north.front",
            "tablesculpting.north.stone",
            "tablesculpting.north.stone_s",
            "tablesculpting.north.top",
            "tablesculpting.south.front",
            "tablesculpting.south.stone",
            "tablesculpting.south.stone_s",
            "tablesculpting.south.top",
            "tablesmithingelectric.east.box_front",
            "tablesmithingelectric.east.box_top",
            "tablesmithingelectric.east.front",
            "tablesmithingelectric.east.hood",
            "tablesmithingelectric.east.top",
            "tablesmithingelectric.north.box_front",
            "tablesmithingelectric.north.box_top",
            "tablesmithingelectric.north.front",
            "tablesmithingelectric.north.hood",
            "tablesmithingelectric.north.top",
            "tablesmithingelectric.south.box_front",
            "tablesmithingelectric.south.box_top",
            "tablesmithingelectric.south.front",
            "tablesmithingelectric.south.hood",
            "tablesmithingelectric.south.top",
            "tablesmithingfueled.east.box_front",
            "tablesmithingfueled.east.box_top",
            "tablesmithingfueled.east.front",
            "tablesmithingfueled.east.hood",
            "tablesmithingfueled.east.top",
            "tablesmithingfueled.north.box_front",
            "tablesmithingfueled.north.box_top",
            "tablesmithingfueled.north.front",
            "tablesmithingfueled.north.hood",
            "tablesmithingfueled.north.top",
            "tablesmithingfueled.south.box_front",
            "tablesmithingfueled.south.box_top",
            "tablesmithingfueled.south.front",
            "tablesmithingfueled.south.hood",
            "tablesmithingfueled.south.top",
            "tablestonecutter.east.disc",
            "tablestonecutter.east.front",
            "tablestonecutter.east.top",
            "tablestonecutter.north.disc",
            "tablestonecutter.north.front",
            "tablestonecutter.north.top",
            "tablestonecutter.south.disc",
            "tablestonecutter.south.front",
            "tablestonecutter.south.top",
            "tablestoveelectric.east.box_front",
            "tablestoveelectric.east.box_top",
            "tablestoveelectric.east.front",
            "tablestoveelectric.east.top",
            "tablestoveelectric.north.box_front",
            "tablestoveelectric.north.box_top",
            "tablestoveelectric.north.front",
            "tablestoveelectric.north.top",
            "tablestoveelectric.south.box_front",
            "tablestoveelectric.south.box_top",
            "tablestoveelectric.south.front",
            "tablestoveelectric.south.top",
            "tablestovefueled.east.box_front",
            "tablestovefueled.east.box_top",
            "tablestovefueled.east.front",
            "tablestovefueled.east.top",
            "tablestovefueled.north.box_front",
            "tablestovefueled.north.box_top",
            "tablestovefueled.north.front",
            "tablestovefueled.north.top",
            "tablestovefueled.south.box_front",
            "tablestovefueled.south.box_top",
            "tablestovefueled.south.front",
            "tablestovefueled.south.top",
            "tabletailorelectric.east.front",
            "tabletailorelectric.east.mach",
            "tabletailorelectric.east.mach_s",
            "tabletailorelectric.east.top",
            "tabletailorelectric.north.front",
            "tabletailorelectric.north.mach",
            "tabletailorelectric.north.mach_s",
            "tabletailorelectric.north.top",
            "tabletailorelectric.south.front",
            "tabletailorelectric.south.mach",
            "tabletailorelectric.south.mach_s",
            "tabletailorelectric.south.top",
            "tabletailorhand.east.front",
            "tabletailorhand.east.top",
            "tabletailorhand.north.front",
            "tabletailorhand.north.top",
            "tabletailorhand.south.front",
            "tabletailorhand.south.top",
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

        private static Vector4[] Side4ByRot(string arch, Rot4 rot, string sr)
        {
            if (rot == Rot4.South)
                return new[] { Region(arch, "south", sr), Region(arch, "north", sr), Region(arch, "east", sr), Region(arch, "east", sr) };
            if (rot == Rot4.North)
                return new[] { Region(arch, "north", sr), Region(arch, "south", sr), Region(arch, "east", sr), Region(arch, "east", sr) };
            if (rot == Rot4.East)
                return new[] { Region(arch, "east", sr), Region(arch, "east", sr), Region(arch, "south", sr), Region(arch, "north", sr) };
            return new[] { Region(arch, "east", sr), Region(arch, "east", sr), Region(arch, "north", sr), Region(arch, "south", sr) };
        }

        /// <summary>
        /// 一个部件的取样规格：顶面用当前朝向的俯视段，四个立面按朝向各自配
        /// 那张图的立面段。dir: 0南(-Z) 1北(+Z) 2东(+X) 3西(-X)。
        /// </summary>
        private static UvSpec FaceUv(string arch, Rot4 rot, Vector2 xz, string topRegion = "top")
        {
            string v = ViewOf(rot);
            Vector4[] side4;
            if (!HasRegion(arch, "front"))
            {
                // 有的件整张图就没有立面段（diningchair 的"腿带"是纯线稿，几何也不拿它当面）：
                // 退到木面，而不是 Vector4.zero —— 那会去采图集原点。
                var wf = WoodRegion(arch, rot);
                side4 = new[] { wf, wf, wf, wf };
            }
            else
            {
                side4 = Side4ByRot(arch, rot, "front");
            }
            var top = Region(arch, v, topRegion);
            if (rot == Rot4.North && Reg(arch + ".north." + topRegion).w <= Reg(arch + ".north." + topRegion).y)
                top = new Vector4(top.x, top.w, top.z, top.y);
            else if (rot == Rot4.West)
                top = new Vector4(top.z, top.y, top.x, top.w);
            return new UvSpec
            {
                top = top,
                side = side4[0],
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
        /// Production benches are not line-art furniture. Their source sprite
        /// already contains a complete 3x1 top projection and a separate
        /// lower apron strip for each view. Keep the four vertical faces on
        /// their matching source view instead of stretching the current view
        /// across every face.
        /// </summary>
        private static UvSpec ProductionUv(string key, Rot4 rot, Vector2 xz,
            string topRegion, string sideRegion)
        {
            string v = ViewOf(rot);
            var side4 = Side4ByRot(key, rot, sideRegion);
            return new UvSpec
            {
                top = Region(key, v, topRegion),
                side = side4[0],
                side4 = side4,
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
        private static UvSpec PanelUv(string arch, Rot4 rot, Vector2 xz, string face, bool wideAlongX)
        {
            string v = ViewOf(rot);
            var s = Region(arch, v, face);
            var w = WoodRegion(arch, rot);
            var s4 = new[] { s, s, s, s };
            if (wideAlongX) { s4[2] = w; s4[3] = w; }
            else { s4[0] = w; s4[1] = w; }
            return new UvSpec
            {
                top = w,
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
        // Production phase 1 dimensions are authored constants, independent of
        // ThingDef drawSize. These four vanilla benches are 3x1 in their Defs
        // and their source sprites use the same 3:1 top projection.
        private const float ProductionBenchLong = 3.00f;
        private const float ProductionBenchShort = 1.00f;
        private const float ProductionBenchBodyHeight = 0.72f;
        private const float ProductionBenchTopThickness = 0.08f;

        private static void ProductionSize(string key, Rot4 rot, out float w, out float d)
        {
            float longSpan = ProductionBenchLong;
            float shortSpan = ProductionBenchShort;
            if (key == "researchbenchsimple")
            {
                longSpan = 3.00f;
                shortSpan = 2.00f;
            }
            else if (key == "researchbenchhitech" || key == "fabricationbench")
            {
                longSpan = 5.00f;
                shortSpan = 2.00f;
            }
            bool longX = rot == Rot4.North || rot == Rot4.South;
            w = longX ? longSpan : shortSpan;
            d = longX ? shortSpan : longSpan;
        }

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
            if (arch == Arch.ProductionBench)
                ProductionSize(key, rot, out w, out d);
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
                    // 床架比床垫宽一圈，保留木面顶沿以防露出反绕黑壳 + 床垫 + 床头板 + 床尾板 + 四立柱
                    var frame = new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(w - 0.06f, Metrics3D.BedFrameHeight, d - 0.06f),
                        noTop = false,
                        uv = 4        // 床架侧板与外露床沿 = 木面（见下）
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
                        uvs.Add(UvNames(key, rot, Vector2.zero, "pillow", "pillow"));
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
                        uvs.Add(UvNames(key, rot, Vector2.zero, "pillow", "pillow"));
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
                    uvs.Add(PanelUv(key, rot, Vector2.zero, "back", longZ));
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
                    uvs.Add(FaceUv(key, rot, Vector2.zero));                 // 1 = 箱体（四立面各自取对应朝向 front）
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(w * 0.9f, sh, d * 0.86f),
                        uv = 1
                    });
                    uvs.Add(PanelUv(key, rot, Vector2.zero, "back", longZ)); // 2 = 靠背
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
                    uvs.Add(PanelUv(key, rot, Vector2.zero, "arml", !longZ)); // 3 = 扶手 A
                    uvs.Add(PanelUv(key, rot, Vector2.zero, "armr", !longZ)); // 4 = 扶手 B
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
                    LegsXY(m, 2, w * 0.5f - 0.14f, d * 0.5f - 0.14f, h, Metrics3D.LegThickness);
                    break;
                }
                case Arch.ProductionBench:
                {
                    BuildProductionBench(m, uvs, key, rot, w, d, xz);
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
                    uvs.Add(UvNames(key, rot, Vector2.zero, "front", "front"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(0.08f, 0.85f, 0.08f),
                        uv = pi
                    });
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "top", "top"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.60f, 0f),
                        size = longZ ? new Vector3(0.62f, 0.20f, 0.12f) : new Vector3(0.12f, 0.20f, 0.62f),
                        uv = bi
                    });
                    int ti = uvs.Count;                       // 桩头露在牌子上面那一截
                    uvs.Add(UvNames(key, rot, Vector2.zero, "tip", "tip"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.80f, 0f),
                        size = new Vector3(0.06f, 0.14f, 0.06f),   // 比桩身窄一圈，别与它共面
                        uv = ti
                    });
                    break;
                }
                case Arch.FloodLight:
                {
                    // 底座：图里是一条椭圆，左半 = 侧壁，右半 = 顶面（两条都不含洞）
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "baser", "basel"));
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0f, 0f),
                        size = new Vector3(0.62f, 0.14f, 0.62f),
                        uv = bi
                    });
                    int pi = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "pole", "pole"));     // 2 = 灯杆（图里杆与底座是一条竖带）
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(0f, 0.14f, 0f),
                        size = new Vector3(0.16f, 0.62f, 0.16f),
                        uv = pi
                    });
                    int hi = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "wood", "top"));      // 3 = 横灯头（顶面用素金属 wood，立面带两端黄灯面 top）
                    m.parts.Add(new Part
                    {
                        kind = Prim.Box,
                        pos = new Vector3(0f, 0.76f, 0f),
                        size = longZ ? new Vector3(0.90f, 0.24f, 0.30f) : new Vector3(0.30f, 0.24f, 0.90f),
                        uv = hi
                    });
                    break;
                }
                case Arch.GrandThrone:
                {
                    // 大王座（3x2）：白台座 + 台阶 + 金椅 + 尖顶。图里金与白是两块分开的带，
                    // 所以几何也分两批板件，各点自己的取样带。
                    float bs = BackSign(rot);
                    float spanW = longZ ? w : d, spanD = longZ ? d : w;
                    int di = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "top", "front"));      // 1 = 台座（顶面/立面分开）
                    OrientBox(m, longZ, di, 0f, 0f, 0f, spanW * 0.96f, 0.16f, spanD * 0.94f);
                    OrientBox(m, longZ, di, 0f, 0.16f, -bs * spanD * 0.22f, spanW * 0.78f, 0.14f, spanD * 0.50f);
                    int gi = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "gold", "gold"));      // 2 = 金椅 + 尖顶
                    float seatY = 0.30f;
                    OrientBox(m, longZ, gi, 0f, seatY, 0f, 0.95f, 0.22f, 0.85f);
                    OrientBox(m, longZ, gi, 0f, seatY + 0.22f, bs * 0.36f, 0.80f, 0.85f, 0.14f);
                    int qi = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "spire", "spire"));    // 3 = 尖顶
                    OrientBox(m, longZ, qi, 0f, seatY + 1.07f, bs * 0.30f, 0.34f, 0.42f, 0.28f);
                    int ai = gi;                                          // 4 = 两侧金扶手柱（同金带）
                    for (int sd = -1; sd <= 1; sd += 2)
                        OrientBox(m, longZ, ai, sd * 0.54f, seatY - 0.06f, -0.04f, 0.22f, 0.62f, 0.70f);
                    break;
                }
                case Arch.Drape:
                {
                    // 帷幔（2x1）：横杆 + 两侧立柱 + 垂下的布面 + 三个穗子。
                    float bs = BackSign(rot);
                    float spanW = longZ ? w : d;
                    string sv = ViewOf(rot);
                    int ti = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "tassel", "tassel"));   // 1 = 横杆 + 立柱 + 左穗
                    int tj = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "tassel2", "tassel2")); // 2 = 右穗
                    int tk = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "tassel3", "tassel3")); // 3 = 中穗
                    OrientBox(m, longZ, ti, 0f, DrapeRodHeight, bs * 0.02f, spanW * 0.86f, 0.09f, 0.09f);
                    for (int sd = -1; sd <= 1; sd += 2)
                        OrientBox(m, longZ, ti, sd * spanW * 0.40f, 0f, bs * 0.02f, 0.07f, DrapeRodHeight + 0.045f, 0.07f);
                    OrientBox(m, longZ, ti, -spanW * 0.36f, 0.34f, bs * 0.02f, 0.09f, 0.13f, 0.09f);
                    OrientBox(m, longZ, tk, 0f, 0.34f, bs * 0.02f, 0.09f, 0.13f, 0.09f);
                    OrientBox(m, longZ, tj, spanW * 0.36f, 0.34f, bs * 0.02f, 0.09f, 0.13f, 0.09f);
                    int ci = uvs.Count;
                    uvs.Add(Bands(key, sv, "mid", "front", "top", Vector2.zero, longZ));
                    OrientBox(m, longZ, ci, 0f, 0.47f, bs * 0.02f, spanW * 0.82f, 0.95f, ClothThickness);
                    break;
                }
                case Arch.Stele:
                {
                    // 石碑：一块竖碑 + 一个基座。碑身的大面 = 图里那片刻纹（top），
                    // 基座 = 图底部那条带（front）。占格 2x2 / 3x3 只改高度与宽度。
                    float spanW = longZ ? w : d, spanD = longZ ? d : w;
                    float h = 0.55f + 0.50f * Mathf.Max(w, d);
                    string sv = ViewOf(rot);
                    float hLow = (h - 0.18f) * 0.40f, hMid = (h - 0.18f) * 0.32f;
                    int si = uvs.Count;
                    uvs.Add(Bands(key, sv, "face", "mid", "mid", Vector2.zero, longZ));     // 1 = 下段
                    int ui = uvs.Count;
                    uvs.Add(Bands(key, sv, "mid", "shoulder", "mid", Vector2.zero, longZ)); // 2 = 中段
                    int vi = uvs.Count;
                    uvs.Add(Bands(key, sv, "top", "top", "crest", Vector2.zero, longZ));    // 3 = 上段
                    OrientBox(m, longZ, si, 0f, 0.18f, 0f, spanW * 0.56f, hLow, 0.24f);
                    OrientBox(m, longZ, ui, 0f, 0.18f + hLow, 0f, spanW * 0.48f, hMid, 0.22f);
                    OrientBox(m, longZ, vi, 0f, 0.18f + hLow + hMid, 0f, spanW * 0.38f, h - 0.18f - hLow - hMid, 0.20f);
                    int bi = uvs.Count;
                    uvs.Add(UvNames(key, rot, Vector2.zero, "front", "front"));
                    OrientBox(m, longZ, bi, 0f, 0f, 0f, spanW * 0.72f, 0.18f, spanD * 0.42f);
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

        /// <summary>一条带子铺满某个面：大面取 face，薄边取 edge，顶面取 cap。
        /// 石碑这种"逐层收窄"的图靠它把每条横带派到对应的那一面。</summary>
        private static UvSpec Bands(string key, string view, string face, string edge,
            string cap, Vector2 xz, bool longZ = true)
        {
            var f = Region(key, view, face);
            var e = Region(key, view, edge);
            return new UvSpec
            {
                top = Region(key, view, cap),
                side = f,
                side4 = longZ ? new[] { f, f, e, e } : new[] { e, e, f, f },
                sideTileH = 0f,
                sideTileW = 0f,
                modelXZ = xz
            };
        }

        private static void OrientBox(Model3D m, bool longZ, int uvIdx,
            float lx, float y0, float lz, float sx, float sy, float sz)
        {
            m.parts.Add(new Part
            {
                kind = Prim.Box,
                pos = longZ ? new Vector3(lx, y0, lz) : new Vector3(lz, y0, lx),
                size = longZ ? new Vector3(sx, sy, sz) : new Vector3(sz, sy, sx),
                uv = uvIdx
            });
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
            LegsXY(m, uvIdx, off, off, topY, r);
        }

        private static void LegsXY(Model3D m, int uvIdx, float offX, float offZ, float topY, float r)
        {
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    m.parts.Add(new Part
                    {
                        kind = Prim.Cylinder,
                        pos = new Vector3(sx * offX, 0f, sz * offZ),
                        size = new Vector3(r, topY, r),
                        uv = uvIdx
                    });
        }

        private static void BenchBox(Model3D m, Rot4 rot, int uvIdx,
            float lx, float y0, float lz, float sx, float sy, float sz, bool noTop = false)
        {
            Vector3 pos, size;
            if (rot == Rot4.South)
            {
                pos = new Vector3(lx, y0, lz);
                size = new Vector3(sx, sy, sz);
            }
            else if (rot == Rot4.North)
            {
                pos = new Vector3(-lx, y0, -lz);
                size = new Vector3(sx, sy, sz);
            }
            else
            {
                pos = new Vector3(-lz, y0, lx);
                size = new Vector3(sz, sy, sx);
            }
            m.parts.Add(new Part
            {
                kind = Prim.Box,
                pos = pos,
                size = size,
                noTop = noTop,
                uv = uvIdx
            });
        }

        private static void BenchCyl(Model3D m, Rot4 rot, int uvIdx,
            float lx, float y0, float lz, float dia, float sy)
        {
            Vector3 pos;
            if (rot == Rot4.South) pos = new Vector3(lx, y0, lz);
            else if (rot == Rot4.North) pos = new Vector3(-lx, y0, -lz);
            else pos = new Vector3(-lz, y0, lx);
            m.parts.Add(new Part
            {
                kind = Prim.Cylinder,
                pos = pos,
                size = new Vector3(dia, sy, dia),
                uv = uvIdx
            });
        }

        private static void BuildProductionBench(Model3D m, List<UvSpec> uvs,
            string key, Rot4 rot, float w, float d, Vector2 xz)
        {
            float topY = ProductionBenchBodyHeight;
            float topT = ProductionBenchTopThickness;
            float surfY = topY + topT;
            int uLocal = uvs.Count;
            uvs.Add(ProductionUv(key, rot, Vector2.zero, "top", "front"));
            int uShared = uvs.Count;
            uvs.Add(ProductionUv(key, rot, xz, "top", "front"));

            if (key == "tablesculpting")
            {
                int uStone = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "stone", "stone_s"));
                BenchBox(m, rot, uLocal, -0.50f, topY, 0f, 1.96f, topT, 0.96f);
                BenchBox(m, rot, uLocal, -0.50f, 0f, 0f, 1.84f, topY, 0.84f, true);
                BenchCyl(m, rot, uStone, 0.04f, surfY, 0f, 0.72f, 0.05f);
                BenchBox(m, rot, uStone, 0.68f, 0f, -0.10f, 0.48f, 0.70f, 0.46f);
                BenchBox(m, rot, uStone, 0.78f, 0f, 0.22f, 0.40f, 0.84f, 0.36f);
                BenchBox(m, rot, uStone, 1.14f, 0f, 0.04f, 0.50f, 0.78f, 0.50f);
            }
            else if (key == "tablesmithingfueled")
            {
                int uBox = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "box_top", "box_front"));
                int uHood = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "hood", "hood"));
                BenchBox(m, rot, uBox, -0.98f, 0f, 0f, 0.96f, 0.92f, 0.92f);
                BenchBox(m, rot, uLocal, 0.48f, topY, -0.10f, 1.96f, topT, 0.76f);
                BenchBox(m, rot, uLocal, 0.48f, 0f, -0.10f, 1.84f, topY, 0.68f, true);
                BenchBox(m, rot, uHood, -0.06f, 0f, 0.34f, 0.38f, 0.54f, 0.22f, true);
                BenchBox(m, rot, uHood, -0.06f, 0.54f, 0.34f, 0.62f, 0.22f, 0.28f);
            }
            else if (key == "tablesmithingelectric")
            {
                int uBox = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "box_top", "box_front"));
                int uHood = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "hood", "hood"));
                BenchBox(m, rot, uLocal, 0f, topY, -0.10f, 2.96f, topT, 0.76f);
                BenchBox(m, rot, uLocal, 0f, 0f, -0.10f, 2.84f, topY, 0.68f, true);
                BenchBox(m, rot, uBox, -0.84f, surfY, -0.08f, 0.56f, 0.26f, 0.62f);
                BenchBox(m, rot, uHood, -0.06f, 0f, 0.34f, 0.38f, 0.54f, 0.22f, true);
                BenchBox(m, rot, uHood, -0.06f, 0.54f, 0.34f, 0.62f, 0.22f, 0.28f);
            }
            else if (key == "tablestoveelectric")
            {
                int uBox = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "box_top", "box_front"));
                BenchBox(m, rot, uLocal, -0.48f, topY, 0f, 1.96f, topT, 0.96f);
                BenchBox(m, rot, uLocal, -0.48f, 0f, 0f, 1.84f, topY, 0.84f, true);
                BenchBox(m, rot, uBox, 0.98f, 0f, 0f, 0.96f, 0.90f, 0.92f);
            }
            else if (key == "tablestovefueled")
            {
                int uBox = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "box_top", "box_front"));
                BenchBox(m, rot, uBox, -0.98f, 0f, 0f, 0.96f, 0.90f, 0.92f);
                BenchBox(m, rot, uLocal, 0.48f, topY, 0f, 1.96f, topT, 0.96f);
                BenchBox(m, rot, uLocal, 0.48f, 0f, 0f, 1.84f, topY, 0.84f, true);
            }
            else if (key == "tabletailorhand" || key == "tabletailorelectric")
            {
                BenchBox(m, rot, uLocal, 0f, topY, 0f, 2.96f, topT, 0.96f);
                BenchBox(m, rot, uLocal, 0f, 0.16f, 0f, 2.84f, topY - 0.16f, 0.84f, true);
                float[] footXs = { -1.34f, -0.45f, 0.45f, 1.34f };
                for (int ix = 0; ix < footXs.Length; ix++)
                    for (int sz = -1; sz <= 1; sz += 2)
                        BenchCyl(m, rot, uLocal, footXs[ix], 0f, sz * 0.34f, 0.12f, 0.18f);
                if (key == "tabletailorelectric")
                {
                    int uMach = uvs.Count;
                    uvs.Add(ProductionUv(key, rot, Vector2.zero, "mach", "mach_s"));
                    int uMachS = uvs.Count;
                    uvs.Add(ProductionUv(key, rot, Vector2.zero, "mach_s", "mach_s"));
                    BenchBox(m, rot, uMachS, 0.02f, surfY, 0.08f, 0.86f, 0.06f, 0.52f);
                    BenchBox(m, rot, uMachS, 0.28f, surfY + 0.06f, 0.08f, 0.24f, 0.12f, 0.30f, true);
                    BenchBox(m, rot, uMach, 0.04f, surfY + 0.18f, 0.08f, 0.74f, 0.10f, 0.30f);
                    BenchBox(m, rot, uMachS, -0.20f, surfY + 0.11f, 0.08f, 0.16f, 0.07f, 0.22f, true);
                }
            }
            else if (key == "tablestonecutter")
            {
                int uDisc = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "disc", "front"));
                BenchBox(m, rot, uLocal, 0f, topY, -0.14f, 2.96f, topT, 0.62f);
                BenchBox(m, rot, uLocal, 0f, 0f, -0.14f, 2.84f, topY, 0.56f, true);
                BenchCyl(m, rot, uDisc, 0.02f, 0f, 0.08f, 0.92f, 0.78f);
                BenchCyl(m, rot, uLocal, 0.02f, 0.78f, 0.04f, 1.14f, 0.06f);
                BenchCyl(m, rot, uDisc, 0.02f, 0.84f, 0.04f, 0.94f, 0.02f);
                BenchBox(m, rot, uDisc, -0.88f, 0f, 0.24f, 0.40f, 0.26f, 0.26f);
                BenchBox(m, rot, uDisc, 0.60f, 0f, 0.26f, 0.24f, 0.22f, 0.22f);
            }
            else if (key == "tablemachining")
            {
                int uBody = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "top", "body"));
                int uPad = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "pad", "front"));
                BenchBox(m, rot, uBody, 0f, topY, 0f, 2.96f, topT, 0.96f);
                BenchBox(m, rot, uBody, 0f, 0.16f, 0f, 2.84f, topY - 0.16f, 0.84f, true);
                BenchBox(m, rot, uLocal, 0f, 0f, 0f, 2.88f, 0.16f, 0.88f, false);
                BenchBox(m, rot, uPad, -0.02f, surfY, 0.15f, 1.12f, 0.04f, 0.50f);
            }
            else if (key == "tabledruglab")
            {
                int uPad = uvs.Count; uvs.Add(ProductionUv(key, rot, Vector2.zero, "pad", "pad"));
                int uCol = uvs.Count; uvs.Add(ProductionUv(key, rot, Vector2.zero, "col", "col"));
                int uVat = uvs.Count; uvs.Add(ProductionUv(key, rot, Vector2.zero, "vat", "vat"));
                int uGrn = uvs.Count; uvs.Add(ProductionUv(key, rot, Vector2.zero, "grn", "grn"));
                int uBlu = uvs.Count; uvs.Add(ProductionUv(key, rot, Vector2.zero, "blu", "blu"));
                BenchBox(m, rot, uLocal, 0f, topY, 0f, 2.96f, topT, 0.96f);
                BenchBox(m, rot, uLocal, 0f, 0f, 0f, 2.84f, topY, 0.84f, true);
                BenchBox(m, rot, uPad, 1.08f, surfY, -0.04f, 0.44f, 0.08f, 0.42f);
                BenchCyl(m, rot, uCol, 1.08f, surfY + 0.08f, -0.04f, 0.16f, 0.54f);
                BenchCyl(m, rot, uVat, -0.06f, surfY, -0.06f, 0.44f, 0.24f);
                BenchCyl(m, rot, uCol, -0.06f, surfY + 0.24f, -0.06f, 0.16f, 0.12f);
                BenchBox(m, rot, uLocal, -0.60f, surfY, 0.22f, 0.36f, 0.06f, 0.34f);
                BenchCyl(m, rot, uGrn, -0.60f, surfY + 0.06f, 0.22f, 0.28f, 0.20f);
                BenchCyl(m, rot, uGrn, -0.60f, surfY + 0.26f, 0.22f, 0.12f, 0.10f);
                BenchCyl(m, rot, uVat, -1.20f, surfY, -0.18f, 0.22f, 0.20f);
                BenchCyl(m, rot, uCol, -1.20f, surfY + 0.20f, -0.18f, 0.10f, 0.10f);
                BenchCyl(m, rot, uBlu, -0.90f, surfY, -0.02f, 0.22f, 0.20f);
                BenchCyl(m, rot, uBlu, -0.90f, surfY + 0.20f, -0.02f, 0.10f, 0.10f);
                BenchCyl(m, rot, uGrn, -1.18f, surfY, 0.18f, 0.22f, 0.20f);
                BenchCyl(m, rot, uGrn, -1.18f, surfY + 0.20f, 0.18f, 0.10f, 0.10f);
            }
            else if (key == "tablebrewery")
            {
                int uVat = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "vat", "vat_s"));
                BenchBox(m, rot, uLocal, -0.50f, topY, 0f, 1.96f, topT, 0.94f);
                BenchBox(m, rot, uLocal, -0.50f, 0f, 0f, 1.84f, topY, 0.84f, true);
                BenchCyl(m, rot, uVat, -1.16f, surfY, -0.06f, 0.46f, 0.08f);
                BenchCyl(m, rot, uVat, -0.66f, surfY, -0.06f, 0.46f, 0.08f);
                BenchCyl(m, rot, uVat, 0.74f, 0f, -0.14f, 0.58f, 0.84f);
                BenchCyl(m, rot, uVat, 1.14f, 0f, 0.14f, 0.58f, 0.90f);
            }
            else if (key == "electricsmelter")
            {
                int uHood = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "hood_s", "hood_s"));
                int uGlow = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "hood", "hood"));
                BenchBox(m, rot, uLocal, 0f, topY, 0f, 2.96f, topT, 0.96f);
                BenchBox(m, rot, uLocal, 0f, 0f, 0f, 2.84f, topY, 0.84f, true);
                BenchBox(m, rot, uHood, 0f, surfY, -0.18f, 1.24f, 0.24f, 0.36f);
                BenchBox(m, rot, uHood, -0.58f, surfY, 0.13f, 0.44f, 0.20f, 0.26f);
                BenchBox(m, rot, uHood, 0.58f, surfY, 0.13f, 0.44f, 0.20f, 0.26f);
                BenchBox(m, rot, uGlow, 0f, surfY, 0.13f, 0.72f, 0.16f, 0.26f);
                BenchBox(m, rot, uHood, 0f, surfY, 0.35f, 0.92f, 0.10f, 0.18f);
            }
            else if (key == "researchbenchhitech")
            {
                int uTray = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "tray", "tray"));
                int uFlask = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "flask", "flask"));
                BenchBox(m, rot, uLocal, 0f, topY, 0f, 4.96f, topT, 1.96f);
                BenchBox(m, rot, uLocal, 0f, 0f, 0f, 4.84f, topY, 1.84f, true);
                BenchBox(m, rot, uTray, -1.80f, surfY, 0f, 0.64f, 0.06f, 1.66f);
                BenchBox(m, rot, uTray, 1.80f, surfY, 0f, 0.64f, 0.06f, 1.66f);
                BenchBox(m, rot, uTray, 0f, surfY, -0.32f, 0.92f, 0.12f, 0.50f);
                float[] fzs = { 0.26f, -0.26f };
                for (int i = 0; i < 2; i++)
                {
                    BenchCyl(m, rot, uFlask, -1.80f, surfY + 0.06f, fzs[i], 0.34f, 0.18f);
                    BenchCyl(m, rot, uFlask, -1.80f, surfY + 0.24f, fzs[i], 0.14f, 0.10f);
                }
            }
            else if (key == "fabricationbench")
            {
                int uBody = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "top", "body"));
                int uSteel = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "body", "body"));
                int uMach = uvs.Count;
                uvs.Add(ProductionUv(key, rot, Vector2.zero, "mach", "mach"));
                BenchBox(m, rot, uBody, 0f, topY, 0f, 4.96f, topT, 1.96f);
                BenchBox(m, rot, uBody, 0f, 0.16f, 0f, 4.84f, topY - 0.16f, 1.84f, true);
                BenchBox(m, rot, uLocal, 0f, 0f, 0f, 4.88f, 0.16f, 1.88f, false);
                BenchBox(m, rot, uMach, -1.76f, surfY, -0.22f, 0.68f, 0.16f, 0.72f);
                BenchBox(m, rot, uMach, -0.78f, surfY, -0.18f, 0.84f, 0.24f, 1.16f);
                BenchBox(m, rot, uMach, 0.80f, surfY, -0.18f, 0.84f, 0.24f, 1.16f);
                BenchBox(m, rot, uMach, 1.78f, surfY, -0.22f, 0.68f, 0.16f, 0.72f);
                BenchBox(m, rot, uSteel, -1.31f, surfY, -0.20f, 0.22f, 0.10f, 0.24f);
                BenchBox(m, rot, uSteel, 0.01f, surfY, -0.20f, 0.74f, 0.10f, 0.28f);
                BenchBox(m, rot, uSteel, 1.33f, surfY, -0.20f, 0.22f, 0.10f, 0.24f);
                BenchBox(m, rot, uSteel, -0.80f, surfY, 0.66f, 0.42f, 0.18f, 0.46f);
                BenchBox(m, rot, uSteel, 0.86f, surfY, 0.66f, 0.42f, 0.18f, 0.46f);
            }
            else
            {
                float longSpan = Mathf.Max(w, d);
                float shortSpan = Mathf.Min(w, d);
                BenchBox(m, rot, uLocal, 0f, topY, 0f, longSpan - 0.04f, topT, shortSpan - 0.04f);
                BenchBox(m, rot, uLocal, 0f, 0f, 0f, longSpan - 0.16f, topY, shortSpan - 0.16f, true);
            }
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

        // ---- 可站立支撑面 ----

        /// <summary>本件家具的可站立顶面高度（米，相对地面 baseY）；0 = 不作为支撑面。
        /// 座椅/床类取座面·床垫面（**不取靠背顶**，免得人站在椅背上）；其余取网格包围盒顶。</summary>
        public static float TopOf(Thing t)
        {
            if (t is Blueprint) return 0f;
            if (!Handles(t) || !ResolveBuildDef(t, out var def, out _)) return 0f;
            Arch arch; string key;
            if (!RecipeOf(def, out arch, out key)) return 0f;
            var occ = GenAdj.OccupiedRect(t);
            float w = Mathf.Max(0.3f, occ.Width), d = Mathf.Max(0.3f, occ.Height);
            var rot = t.Rotation;
            if (arch == Arch.Bed && rot == Rot4.West) rot = Rot4.East;   // 西向无独立图，按东向几何
            var b = Get(arch, key, rot, w, d);
            if (b == null) return 0f;
            switch (arch)
            {
                case Arch.Bed: return Metrics3D.MattressSurface;
                case Arch.Bedroll: return Metrics3D.BedrollLyingSurface;
                case Arch.Chair:
                case Arch.Stool: return Metrics3D.ChairSeatHeight + Metrics3D.SeatCushionThickness;
                case Arch.Armchair: return Metrics3D.ArmchairSeatHeight + Metrics3D.SeatCushionThickness;
                case Arch.Throne: return ThroneSeatHeight + Metrics3D.SeatCushionThickness;
                default: return b.body != null ? b.body.bounds.max.y : 0f;
            }
        }

        /// <summary>该格最高的可站立家具顶面（米）；格内无接管家具 = 0（站在地面）。</summary>
        public static float SupportAt(Map map, IntVec3 c)
        {
            if (map == null || !c.InBounds(map)) return 0f;
            float h = 0f;
            var list = map.thingGrid.ThingsListAtFast(c);
            for (int i = 0; i < list.Count; i++)
            {
                float y = TopOf(list[i]);
                if (y > h) h = y;
            }
            return h;
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

        /// <summary>画一件家具（或其蓝图/建造框架）；返回 false 表示本层没接管（调用方走原回退路径）。
        /// lit 是该件所在处的放置亮度（F013 实例乘子），与原型网格烘好的形体明暗相乘。</summary>
        public static bool TryDraw(Thing t, Vector3 pos, Color lit)
        {
            if (!Handles(t) || !ResolveBuildDef(t, out var def, out bool isConstructible)) return false;
            var m = Mat;
            if (m == null) return false;
            Arch arch; string key;
            if (!RecipeOf(def, out arch, out key)) return false;
            var occ = GenAdj.OccupiedRect(t);
            float w = Mathf.Max(0.3f, occ.Width), d = Mathf.Max(0.3f, occ.Height);
            var rot = t.Rotation;
            if (arch == Arch.Bed && rot == Rot4.West) rot = Rot4.East;   // 西向无独立图，按东向几何
            var b = Get(arch, key, rot, w, d);
            if (b == null || b.body == null) return false;
            var matrix = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);
            if (isConstructible)
            {
                var bpMat = Util3D.BlueprintMatFor(atlas, FilterMode.Bilinear);
                Graphics.DrawMesh(b.body, matrix, bpMat, 0);
                PerfCounters.BuildingMeshesDrawn++;
                if (Fps3DState.DrawOutlines && b.hull != null)
                {
                    Graphics.DrawMesh(b.hull, matrix, Util3D.BlueprintOutlineMat, 0);
                    PerfCounters.OutlineMeshesDrawn++;
                }
                return true;
            }
            Graphics.DrawMesh(b.body, matrix, Lighting3D.MatFor(m, lit), 0);
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
