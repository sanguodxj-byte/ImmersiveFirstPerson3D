// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 物块与资源类 3D 程序化模型库（ItemModels3D）
    // ============================================================
    //
    // 【定位】将地面上的岩石块（ChunkSandstone~ChunkVacstone）、钢渣块（ChunkSlagSteel）、
    //         切制石砖（BlocksSandstone~BlocksVacstone）、钢铁（Steel）、玻璃钢（Plasteel）、
    //         铀（Uranium）、白银（Silver）、黄金（Gold）、玉石（Jade）、黑曜石（Obsidian）、
    //         活铁（Bioferrite）、原木（WoodLog）、重力板（GravlitePanel）、织物卷（Cloth等）、
    //         工业/高级零部件（Component）、化合燃料（Chemfuel）、中性胺（Neutroamine）、
    //         医药（Medicine）与有毒废料包（Wastepack）从 2D 十字面片升级为带反绕描边壳的实体 3D 模型。
    // 【材质】完全取样自原版贴图合并而成的 1.6/Textures/FPS3D/ItemAtlas.png（由 scripts/item_atlas.py 生成），
    //         每个物块含 4 个原版取样区：0=top(受光顶面)、1=side(主立面)、2=shade(背光暗面)、3=detail(特征细节)。
    // 【性能】与 PlantModels3D 同口径：在 ItemLayer3D 中按 (物块键×形态变体) 分组，经 Util3D.BuildInstancedMesh
    //         烘焙为合并网格，全图仅 2 次 DrawMesh（本体 + 描边壳），零逐件 DrawCall 开销。
    [StaticConstructorOnStartup]
    public static class ItemModels3D
    {
        public const int UV_TOP = 0;
        public const int UV_SIDE = 1;
        public const int UV_SHADE = 2;
        public const int UV_DETAIL = 3;

        public enum Arch
        {
            ChunkRock,
            ChunkSlag,
            StoneBlocks,
            Steel,
            Plasteel,
            Uranium,
            NuggetHeap,
            Jade,
            Obsidian,
            Bioferrite,
            WoodLog,
            HexPanel,
            FabricRoll,
            CompInd,
            CompSpacer,
            Chemfuel,
            Bottles,
            MedBox,
            MedHerbal,
            Wastepack,
        }

        public class Built
        {
            public Mesh body;
            public Mesh hull;
        }

        private static readonly Dictionary<string, Built> cache = new Dictionary<string, Built>();
        private static readonly Dictionary<ThingDef, string> defKeyCache = new Dictionary<ThingDef, string>();
        private static Material atlasMat;
        private static Texture2D atlasTex;

        public static bool Enabled = true;

        private static readonly Dictionary<string, Arch> KeyToArch = new Dictionary<string, Arch>(40)
        {
            { "chunksandstone",      Arch.ChunkRock },
            { "chunkgranite",        Arch.ChunkRock },
            { "chunklimestone",      Arch.ChunkRock },
            { "chunkslate",          Arch.ChunkRock },
            { "chunkmarble",         Arch.ChunkRock },
            { "chunkvacstone",       Arch.ChunkRock },
            { "chunkslagsteel",      Arch.ChunkSlag },
            { "blockssandstone",     Arch.StoneBlocks },
            { "blocksgranite",       Arch.StoneBlocks },
            { "blockslimestone",     Arch.StoneBlocks },
            { "blocksslate",         Arch.StoneBlocks },
            { "blocksmarble",        Arch.StoneBlocks },
            { "blocksvacstone",      Arch.StoneBlocks },
            { "steel",               Arch.Steel },
            { "plasteel",            Arch.Plasteel },
            { "uranium",             Arch.Uranium },
            { "silver",              Arch.NuggetHeap },
            { "gold",                Arch.NuggetHeap },
            { "jade",                Arch.Jade },
            { "obsidian",            Arch.Obsidian },
            { "bioferrite",          Arch.Bioferrite },
            { "woodlog",             Arch.WoodLog },
            { "gravlitepanel",       Arch.HexPanel },
            { "cloth",               Arch.FabricRoll },
            { "synthread",           Arch.FabricRoll },
            { "devilstrandcloth",    Arch.FabricRoll },
            { "hyperweave",          Arch.FabricRoll },
            { "componentindustrial", Arch.CompInd },
            { "componentspacer",     Arch.CompSpacer },
            { "chemfuel",            Arch.Chemfuel },
            { "neutroamine",         Arch.Bottles },
            { "medicineindustrial",  Arch.MedBox },
            { "medicineultratech",   Arch.MedBox },
            { "medicineherbal",      Arch.MedHerbal },
            { "wastepack",           Arch.Wastepack },
        };

        public static Material AtlasMat
        {
            get
            {
                if (atlasMat != null) return atlasMat;
                try { atlasTex = ContentFinder<Texture2D>.Get(ItemUVGen.TexPath, reportFailure: false); }
                catch { atlasTex = null; }
                if (atlasTex == null) return null;
                atlasTex.filterMode = FilterMode.Point;
                atlasTex.wrapMode = TextureWrapMode.Clamp;
                atlasMat = Util3D.CutoutMatFor(atlasTex);
                return atlasMat;
            }
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

        public static string KeyOf(Thing t)
        {
            if (!Enabled || t == null || t.def == null) return null;
            if (defKeyCache.TryGetValue(t.def, out var k)) return k;
            string dn = (t.def.defName ?? "").ToLowerInvariant().Replace("_", "").Replace(" ", "");
            string resolved = null;
            if (KeyToArch.ContainsKey(dn))
            {
                resolved = dn;
            }
            else if (dn.Contains("chunkmechanoid") || dn.Contains("chunkslag"))
            {
                resolved = "chunkslagsteel";
            }
            else if (dn.StartsWith("chunk") && t.def.thingCategories != null)
            {
                for (int i = 0; i < t.def.thingCategories.Count; i++)
                {
                    var cat = t.def.thingCategories[i]?.defName ?? "";
                    if (cat == "StoneChunks") { resolved = "chunkgranite"; break; }
                    if (cat == "Chunks") { resolved = "chunkslagsteel"; break; }
                }
            }
            else if (dn.StartsWith("blocks") && t.def.thingCategories != null)
            {
                for (int i = 0; i < t.def.thingCategories.Count; i++)
                {
                    if ((t.def.thingCategories[i]?.defName ?? "") == "StoneBlocks")
                    {
                        resolved = "blocksgranite";
                        break;
                    }
                }
            }
            defKeyCache[t.def] = resolved;
            return resolved;
        }

        public static bool Handles(Thing t) => KeyOf(t) != null && AtlasMat != null;

        /// <summary>按物品堆叠数量或随机种子返回变体号（0 / 1 / 2）。</summary>
        public static int VariantOf(Thing t, string key)
        {
            if (t == null) return 2;
            if (!KeyToArch.TryGetValue(key, out var arch)) return 2;
            if (arch == Arch.ChunkRock || arch == Arch.ChunkSlag)
            {
                int h = t.thingIDNumber;
                return ((h * 7919) % 3 + 3) % 3;
            }
            int limit = t.def != null ? t.def.stackLimit : 1;
            if (limit <= 1) return 2;
            if (t.stackCount <= 1) return 0;
            if (t.stackCount <= Mathf.Max(2, limit / 4)) return 1;
            return 2;
        }

        public static Built Prototype(string key, int variant = 2)
        {
            if (string.IsNullOrEmpty(key) || !KeyToArch.TryGetValue(key, out var arch)) return null;
            variant = Mathf.Clamp(variant, 0, 2);
            string ck = key + "#" + variant;
            if (cache.TryGetValue(ck, out var b)) return b;

            var model = BuildModel(key, arch, variant);
            var uvs = BuildUvSpecs(key, model.bounds);
            var mb = new ModelBuilder(uvs, false);
            mb.AddAll(model);
            float ow = (arch == Arch.ChunkRock || arch == Arch.ChunkSlag) ? 0.022f : 0.016f;
            b = new Built
            {
                body = mb.ToMesh(),
                hull = mb.ToHull(ow),
            };
            cache[ck] = b;
            return b;
        }

        private static List<UvSpec> BuildUvSpecs(string key, Vector3 bounds)
        {
            var xz = new Vector2(Mathf.Max(0.2f, bounds.x), Mathf.Max(0.2f, bounds.z));
            Vector4 rTop = GetRect(key, "top");
            Vector4 rSide = GetRect(key, "side");
            Vector4 rShade = GetRect(key, "shade");
            Vector4 rDet = GetRect(key, "detail");

            // UV_TOP (0): 顶面亮色 rTop，北/东立面 rSide，南/西立面 rShade（自动形成等轴测三面明暗）
            var specTop = new UvSpec
            {
                top = rTop,
                side = rSide,
                modelXZ = xz,
                side4 = new[] { rSide, rShade, rSide, rShade },
            };
            // UV_SIDE (1): 顶面 rSide，侧面 rSide / rShade
            var specSide = new UvSpec
            {
                top = rSide,
                side = rSide,
                modelXZ = xz,
                side4 = new[] { rSide, rShade, rSide, rShade },
            };
            // UV_SHADE (2): 暗部折面/阴影层
            var specShade = new UvSpec
            {
                top = rShade,
                side = rShade,
                modelXZ = xz,
            };
            // UV_DETAIL (3): 标志性特征色（铁锈斑、木截面年轮、铜线圈、能量核、绿十字、药草叶、浅色卷芯）
            var specDet = new UvSpec
            {
                top = rDet,
                side = rDet,
                modelXZ = xz,
            };
            return new List<UvSpec>(4) { specTop, specSide, specShade, specDet };
        }

        private static Vector4 GetRect(string key, string region)
        {
            if (ItemUVGen.R.TryGetValue(key + "." + region, out var r)) return r;
            return new Vector4(0f, 0f, 1f, 1f);
        }

        // ============================================================
        // 20 类原版物块与资源 3D 几何构造（严格对齐原版 2D 贴图剪影与细节）
        // ============================================================

        public static Model3D BuildModel(string key, Arch arch, int variant = 2)
        {
            var m = new Model3D();
            switch (arch)
            {
                case Arch.ChunkRock:
                    BuildChunkRock(m, variant);
                    break;
                case Arch.ChunkSlag:
                    BuildChunkSlag(m, variant);
                    break;
                case Arch.StoneBlocks:
                    BuildStoneBlocks(m, variant);
                    break;
                case Arch.Steel:
                    BuildSteel(m, variant);
                    break;
                case Arch.Plasteel:
                    BuildPlasteel(m, variant);
                    break;
                case Arch.Uranium:
                    BuildUranium(m, variant);
                    break;
                case Arch.NuggetHeap:
                    BuildNuggetHeap(m, variant);
                    break;
                case Arch.Jade:
                    BuildJade(m, variant);
                    break;
                case Arch.Obsidian:
                    BuildObsidian(m, variant);
                    break;
                case Arch.Bioferrite:
                    BuildBioferrite(m, variant);
                    break;
                case Arch.WoodLog:
                    BuildWoodLog(m, variant);
                    break;
                case Arch.HexPanel:
                    BuildHexPanel(m, variant);
                    break;
                case Arch.FabricRoll:
                    BuildFabricRoll(m, variant);
                    break;
                case Arch.CompInd:
                    BuildCompIndustrial(m);
                    break;
                case Arch.CompSpacer:
                    BuildCompSpacer(m);
                    break;
                case Arch.Chemfuel:
                    BuildChemfuel(m);
                    break;
                case Arch.Bottles:
                    BuildBottles(m);
                    break;
                case Arch.MedBox:
                    BuildMedBox(m, key == "medicineultratech");
                    break;
                case Arch.MedHerbal:
                    BuildMedHerbal(m);
                    break;
                case Arch.Wastepack:
                    BuildWastepack(m);
                    break;
            }
            m.RecalcBounds();
            return m;
        }

        /// <summary>天然岩石块（ChunkRockBase）：还原原版 rocklowa(L形阶梯斜劈岩) / rocklowb(浑厚多面斜脊岩) / rocklowc(双峰交错劈裂岩)。</summary>
        private static void BuildChunkRock(Model3D m, int v)
        {
            if (v == 0)
            {
                // rocklowa：左后方高耸斜切岩峰 + 右前方低矮宽岩阶（L 形阶梯多面体巨石）
                m.Add(Prim.Box, new Vector3(-0.08f, 0.00f,  0.04f), new Vector3(0.42f, 0.28f, 0.38f), yaw: -14f, uv: UV_TOP);
                m.Add(Prim.Wedge, new Vector3(-0.08f, 0.278f, 0.04f), new Vector3(0.42f, 0.13f, 0.38f), yaw: 166f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3( 0.14f, 0.00f, -0.06f), new Vector3(0.38f, 0.19f, 0.32f), yaw: 12f, uv: UV_SIDE);
                m.Add(Prim.Wedge, new Vector3( 0.14f, 0.188f, -0.06f), new Vector3(0.38f, 0.09f, 0.32f), yaw: -168f, uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3(-0.19f, 0.00f, -0.07f), new Vector3(0.26f, 0.21f, 0.28f), yaw: 24f, uv: UV_SHADE);
            }
            else if (v == 1)
            {
                // rocklowb：中央偏右宽厚斜顶巨石 + 左前侧劈裂斜坡面
                m.Add(Prim.Box, new Vector3( 0.04f, 0.00f,  0.02f), new Vector3(0.46f, 0.28f, 0.40f), yaw: 18f, uv: UV_TOP);
                m.Add(Prim.Wedge, new Vector3( 0.04f, 0.278f, 0.02f), new Vector3(0.46f, 0.13f, 0.40f), yaw: -162f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(-0.15f, 0.00f, -0.05f), new Vector3(0.34f, 0.20f, 0.34f), yaw: -22f, uv: UV_SIDE);
                m.Add(Prim.Wedge, new Vector3(-0.15f, 0.198f, -0.05f), new Vector3(0.34f, 0.10f, 0.34f), yaw: 158f, uv: UV_SIDE);
            }
            else
            {
                // rocklowc：双峰交错的多棱劈裂矿石（全斜切岩棱，无圆台帽）
                m.Add(Prim.Box, new Vector3(-0.06f, 0.00f,  0.06f), new Vector3(0.40f, 0.31f, 0.36f), yaw: -22f, uv: UV_TOP);
                m.Add(Prim.Wedge, new Vector3(-0.06f, 0.308f, 0.06f), new Vector3(0.40f, 0.13f, 0.36f), yaw: 158f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3( 0.12f, 0.00f, -0.08f), new Vector3(0.36f, 0.22f, 0.32f), yaw: 26f, uv: UV_SIDE);
                m.Add(Prim.Wedge, new Vector3( 0.12f, 0.218f, -0.08f), new Vector3(0.36f, 0.10f, 0.32f), yaw: -154f, uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3(-0.15f, 0.00f, -0.09f), new Vector3(0.26f, 0.19f, 0.26f), yaw: 10f, uv: UV_SHADE);
            }
        }

        /// <summary>钢渣块（ChunkSlagSteel）：还原原版 metaldebrisa(交叠扭曲钢梁) / b(锯齿残甲) / c(V形撕裂双翼)。</summary>
        private static void BuildChunkSlag(Model3D m, int v)
        {
            if (v == 0)
            {
                // metaldebrisa：左下斜插钢板 + 右侧宽翼折板 + 中央斜跨扭曲工字钢梁
                m.Add(Prim.Box, new Vector3(-0.10f, 0.00f, -0.10f), new Vector3(0.42f, 0.12f, 0.26f), yaw: 24f, uv: UV_SHADE);
                m.Add(Prim.Box, new Vector3( 0.14f, 0.04f,  0.04f), new Vector3(0.34f, 0.22f, 0.44f), yaw: -18f, roll: -12f, uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3(-0.04f, 0.06f,  0.02f), new Vector3(0.22f, 0.26f, 0.58f), yaw: 34f, pitch: 12f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(-0.04f, 0.24f,  0.02f), new Vector3(0.28f, 0.05f, 0.52f), yaw: 34f, pitch: 12f, noOutline: true, uv: UV_DETAIL);
            }
            else if (v == 1)
            {
                // metaldebrisb：中央拱起厚装甲板 + 四周参差伸出的断裂铆接齿条
                m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.46f, 0.20f, 0.44f), yaw: -22f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(0.00f, 0.14f, 0.00f), new Vector3(0.34f, 0.10f, 0.32f), yaw: -22f, pitch: 8f, noOutline: true, uv: UV_DETAIL);
                m.Add(Prim.Box, new Vector3(-0.24f, 0.02f,  0.10f), new Vector3(0.18f, 0.09f, 0.10f), yaw: -22f, uv: UV_SHADE);
                m.Add(Prim.Box, new Vector3( 0.24f, 0.02f, -0.10f), new Vector3(0.18f, 0.09f, 0.10f), yaw: -22f, uv: UV_SHADE);
                m.Add(Prim.Box, new Vector3( 0.10f, 0.02f,  0.24f), new Vector3(0.10f, 0.09f, 0.18f), yaw: -22f, uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3(-0.10f, 0.02f, -0.24f), new Vector3(0.10f, 0.09f, 0.18f), yaw: -22f, uv: UV_SIDE);
            }
            else
            {
                // metaldebrisc：V 形撕裂的双翼钢梁残骸
                m.Add(Prim.Box, new Vector3( 0.00f, 0.00f, -0.06f), new Vector3(0.32f, 0.14f, 0.30f), yaw: 10f, uv: UV_SHADE);
                m.Add(Prim.Box, new Vector3(-0.11f, 0.06f,  0.04f), new Vector3(0.18f, 0.26f, 0.48f), yaw: -24f, pitch: 16f, roll: 14f, uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3( 0.12f, 0.06f,  0.02f), new Vector3(0.20f, 0.28f, 0.52f), yaw: 20f, pitch: 14f, roll: -12f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3( 0.00f, 0.12f, -0.02f), new Vector3(0.34f, 0.08f, 0.14f), yaw: 6f, noOutline: true, uv: UV_DETAIL);
            }
        }

        /// <summary>切制石砖（StoneBlocks）：还原原版 _a(单块长砖) / _b(并排双砖) / _c(下二上一品字形三砖堆)。</summary>
        private static void BuildStoneBlocks(Model3D m, int v)
        {
            const float yaw = -32f;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            float bw = 0.21f, bh = 0.15f, bl = 0.44f;

            if (v == 0)
            {
                m.Add(Prim.Box, Vector3.zero, new Vector3(bw, bh, bl), yaw: yaw, uv: UV_TOP);
            }
            else if (v == 1)
            {
                m.Add(Prim.Box, -right * 0.125f, new Vector3(bw, bh, bl), yaw: yaw, uv: UV_TOP);
                m.Add(Prim.Box,  right * 0.125f, new Vector3(bw, bh, bl), yaw: yaw, uv: UV_TOP);
                // 底部隐形连接垫保证两砖连通为单一整体
                m.Add(Prim.Box, new Vector3(0f, 0.01f, 0f), new Vector3(0.28f, 0.04f, 0.24f), yaw: yaw, noOutline: true, uv: UV_SHADE);
            }
            else
            {
                m.Add(Prim.Box, -right * 0.125f, new Vector3(bw, bh, bl), yaw: yaw, uv: UV_TOP);
                m.Add(Prim.Box,  right * 0.125f, new Vector3(bw, bh, bl), yaw: yaw, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(0f, bh - 0.015f, 0f), new Vector3(bw, bh, bl * 0.96f), yaw: yaw, uv: UV_TOP);
            }
        }

        /// <summary>钢铁（Steel）：还原原版 steel_a/b/c 中带铁锈橙斑的五边形斜劈精炼钢块。</summary>
        private static void BuildSteel(Model3D m, int v)
        {
            if (v == 0)
            {
                m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.34f, 0.19f, 0.28f), yaw: -14f, uv: UV_TOP);
                m.Add(Prim.Wedge, new Vector3(0.00f, 0.188f, 0.00f), new Vector3(0.34f, 0.09f, 0.28f), yaw: 166f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(0.08f, 0.02f, 0.09f), new Vector3(0.18f, 0.15f, 0.14f), yaw: 8f, noOutline: true, uv: UV_DETAIL);
            }
            else if (v == 1)
            {
                m.Add(Prim.Box, new Vector3(-0.06f, 0.00f,  0.02f), new Vector3(0.36f, 0.21f, 0.30f), yaw: -16f, uv: UV_TOP);
                m.Add(Prim.Wedge, new Vector3(-0.06f, 0.208f, 0.02f), new Vector3(0.36f, 0.09f, 0.30f), yaw: 164f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3( 0.12f, 0.00f, -0.04f), new Vector3(0.28f, 0.18f, 0.26f), yaw: 22f, uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3(-0.02f, 0.03f,  0.11f), new Vector3(0.18f, 0.16f, 0.14f), yaw: 6f, noOutline: true, uv: UV_DETAIL);
            }
            else
            {
                // steel_c：后左高块 + 前右主块斜靠叠放，顶部为五边形斜切脊，侧前镶嵌铁锈斑
                m.Add(Prim.Box, new Vector3(-0.10f, 0.00f, -0.06f), new Vector3(0.36f, 0.23f, 0.28f), yaw: -18f, uv: UV_SIDE);
                m.Add(Prim.Wedge, new Vector3(-0.10f, 0.228f, -0.06f), new Vector3(0.36f, 0.10f, 0.28f), yaw: 162f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(-0.18f, 0.02f,  0.02f), new Vector3(0.18f, 0.18f, 0.16f), yaw: 6f, noOutline: true, uv: UV_DETAIL);

                m.Add(Prim.Box, new Vector3( 0.08f, 0.00f,  0.07f), new Vector3(0.38f, 0.20f, 0.30f), yaw: 14f, uv: UV_TOP);
                m.Add(Prim.Wedge, new Vector3( 0.08f, 0.198f,  0.07f), new Vector3(0.38f, 0.10f, 0.30f), yaw: -166f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3( 0.17f, 0.02f,  0.14f), new Vector3(0.18f, 0.16f, 0.15f), yaw: -8f, noOutline: true, uv: UV_DETAIL);
            }
        }

        /// <summary>玻璃钢（Plasteel）：还原原版 plasteel_a/b/c 的蓝青色斜切 N/H 形镂空槽口高科技复合装甲板。</summary>
        private static void BuildPlasteel(Model3D m, int v)
        {
            const float yaw = -24f;
            if (v == 0)
            {
                m.Add(Prim.Box, Vector3.zero, new Vector3(0.20f, 0.14f, 0.38f), yaw: yaw, uv: UV_TOP);
            }
            else if (v == 1)
            {
                m.Add(Prim.Box, Vector3.zero, new Vector3(0.24f, 0.15f, 0.50f), yaw: yaw, uv: UV_TOP);
            }
            else
            {
                // plasteel_c：无整底板遮挡，直接由左翼斜板 + 右翼斜板 + 中央斜梁咬合成清晰的 N/H 形开槽装甲板
                var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                m.Add(Prim.Box, -right * 0.135f - fwd * 0.03f, new Vector3(0.16f, 0.16f, 0.50f), yaw: yaw, uv: UV_TOP);
                m.Add(Prim.Box,  right * 0.135f + fwd * 0.03f, new Vector3(0.16f, 0.16f, 0.50f), yaw: yaw, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(0f, 0.005f, 0f), new Vector3(0.18f, 0.15f, 0.28f), yaw: yaw + 40f, noOutline: true, uv: UV_TOP);
            }
        }

        /// <summary>铀矿块（Uranium）：还原原版 uranium_a/b/c 的高耸截顶四棱台重金属矿柱。</summary>
        private static void BuildUranium(Model3D m, int v)
        {
            if (v == 0)
            {
                m.Add(Prim.Box, Vector3.zero, new Vector3(0.26f, 0.24f, 0.26f), yaw: 18f, uv: UV_TOP);
            }
            else if (v == 1)
            {
                m.Add(Prim.Box, Vector3.zero, new Vector3(0.36f, 0.30f, 0.34f), yaw: -16f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(0.02f, 0.22f, 0.00f), new Vector3(0.26f, 0.14f, 0.24f), yaw: 12f, noOutline: true, uv: UV_TOP);
            }
            else
            {
                // uranium_c：底宽上窄的高耸截顶棱锥状重矿块
                m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.42f, 0.24f, 0.38f), yaw: -14f, uv: UV_SIDE);
                m.Add(Prim.Box, new Vector3(-0.02f, 0.16f, 0.01f), new Vector3(0.32f, 0.22f, 0.28f), yaw: -10f, pitch: 6f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(-0.03f, 0.30f, 0.02f), new Vector3(0.22f, 0.12f, 0.20f), yaw: -6f, noOutline: true, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(0.14f, 0.00f, -0.06f), new Vector3(0.24f, 0.20f, 0.26f), yaw: 24f, noOutline: true, uv: UV_SHADE);
            }
        }

        /// <summary>白银 / 黄金（Silver / Gold）：还原原版圆润贵金属矿粒簇拥堆。</summary>
        private static void BuildNuggetHeap(Model3D m, int v)
        {
            if (v == 0)
            {
                m.Add(Prim.Sphere, Vector3.zero, new Vector3(0.26f, 0.18f, 0.22f), yaw: 15f, uv: UV_TOP);
                return;
            }
            // 底层托盘球 + 四周圆润矿粒 + 顶层亮泽金银颗粒
            m.Add(Prim.Sphere, new Vector3( 0.00f, 0.00f,  0.00f), new Vector3(0.46f, 0.18f, 0.40f), uv: UV_SIDE);
            m.Add(Prim.Sphere, new Vector3(-0.14f, 0.00f,  0.08f), new Vector3(0.24f, 0.17f, 0.22f), uv: UV_SIDE);
            m.Add(Prim.Sphere, new Vector3( 0.15f, 0.00f,  0.07f), new Vector3(0.24f, 0.17f, 0.22f), uv: UV_TOP);
            m.Add(Prim.Sphere, new Vector3(-0.12f, 0.00f, -0.10f), new Vector3(0.24f, 0.16f, 0.22f), uv: UV_SHADE);
            m.Add(Prim.Sphere, new Vector3( 0.13f, 0.00f, -0.09f), new Vector3(0.24f, 0.16f, 0.22f), uv: UV_SIDE);
            if (v >= 2)
            {
                m.Add(Prim.Sphere, new Vector3(-0.07f, 0.10f,  0.02f), new Vector3(0.24f, 0.18f, 0.22f), uv: UV_TOP);
                m.Add(Prim.Sphere, new Vector3( 0.08f, 0.10f,  0.01f), new Vector3(0.23f, 0.17f, 0.21f), uv: UV_TOP);
                m.Add(Prim.Sphere, new Vector3( 0.00f, 0.16f, -0.01f), new Vector3(0.22f, 0.16f, 0.20f), noOutline: true, uv: UV_TOP);
            }
        }

        /// <summary>玉石（Jade）：还原原版 jade_c 的温润圆丘状翠玉原石团块。</summary>
        private static void BuildJade(Model3D m, int v)
        {
            m.Add(Prim.Sphere, new Vector3(-0.08f, 0.00f,  0.05f), new Vector3(0.32f, 0.24f, 0.28f), uv: UV_SIDE);
            m.Add(Prim.Sphere, new Vector3( 0.10f, 0.00f,  0.04f), new Vector3(0.30f, 0.22f, 0.28f), uv: UV_TOP);
            if (v >= 1)
            {
                m.Add(Prim.Sphere, new Vector3( 0.00f, 0.00f, -0.08f), new Vector3(0.34f, 0.25f, 0.30f), uv: UV_SHADE);
                m.Add(Prim.Sphere, new Vector3( 0.00f, 0.11f,  0.00f), new Vector3(0.30f, 0.28f, 0.28f), uv: UV_TOP);
            }
            if (v >= 2)
            {
                m.Add(Prim.Sphere, new Vector3(-0.02f, 0.22f, 0.01f), new Vector3(0.22f, 0.18f, 0.20f), noOutline: true, uv: UV_DETAIL);
            }
        }

        /// <summary>黑曜石（Obsidian）：还原原版 obsidian_c 的斜刺火山玻璃锋利晶簇。</summary>
        private static void BuildObsidian(Model3D m, int v)
        {
            m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.42f, 0.18f, 0.38f), yaw: -18f, uv: UV_SHADE);
            m.Add(Prim.Cone, new Vector3(-0.04f, 0.08f,  0.02f), new Vector3(0.26f, 0.34f, 0.24f), pitch: -12f, roll: -10f, taper: 0.14f, uv: UV_TOP);
            if (v >= 1)
            {
                m.Add(Prim.Cone, new Vector3( 0.11f, 0.06f,  0.04f), new Vector3(0.22f, 0.28f, 0.20f), pitch: -16f, roll: -18f, taper: 0.16f, uv: UV_SIDE);
                m.Add(Prim.Cone, new Vector3(-0.12f, 0.05f, -0.06f), new Vector3(0.20f, 0.24f, 0.20f), pitch: 10f, roll: 14f, taper: 0.18f, uv: UV_SIDE);
            }
            if (v >= 2)
            {
                m.Add(Prim.Cone, new Vector3( 0.04f, 0.10f, -0.08f), new Vector3(0.22f, 0.30f, 0.20f), pitch: -18f, roll: 8f, taper: 0.15f, uv: UV_DETAIL);
            }
        }

        /// <summary>活铁（Bioferrite）：还原原版 bioferrite_c 的暗红褐锯齿刺状异变金属簇。</summary>
        private static void BuildBioferrite(Model3D m, int v)
        {
            m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.44f, 0.20f, 0.40f), yaw: 22f, uv: UV_SIDE);
            m.Add(Prim.Box, new Vector3(-0.06f, 0.08f, 0.04f), new Vector3(0.24f, 0.26f, 0.32f), yaw: -26f, pitch: 18f, roll: 14f, uv: UV_TOP);
            if (v >= 1)
            {
                m.Add(Prim.Box, new Vector3( 0.10f, 0.06f, -0.04f), new Vector3(0.22f, 0.24f, 0.28f), yaw: 32f, pitch: -16f, roll: -14f, uv: UV_DETAIL);
                m.Add(Prim.Cone, new Vector3(-0.12f, 0.10f, -0.06f), new Vector3(0.18f, 0.24f, 0.18f), pitch: -22f, roll: 18f, taper: 0.18f, noOutline: true, uv: UV_DETAIL);
            }
            if (v >= 2)
            {
                m.Add(Prim.Cone, new Vector3(0.06f, 0.12f, 0.08f), new Vector3(0.18f, 0.24f, 0.18f), pitch: 20f, roll: -16f, taper: 0.18f, noOutline: true, uv: UV_TOP);
            }
        }

        /// <summary>原木（WoodLog）：还原原版 woodlog_a/b/c 的品字形圆木堆与前端浅黄年轮截面。</summary>
        private static void BuildWoodLog(Model3D m, int v)
        {
            const float yaw = -32f;
            float d = 0.18f, len = 0.56f;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

            if (v == 0)
            {
                AddSingleLog(m, Vector3.zero, d, len, yaw);
            }
            else if (v == 1)
            {
                AddSingleLog(m, -right * 0.105f, d, len, yaw);
                AddSingleLog(m,  right * 0.105f, d, len, yaw);
                m.Add(Prim.Box, new Vector3(0f, 0.02f, 0f), new Vector3(0.24f, 0.06f, 0.28f), yaw: yaw, noOutline: true, uv: UV_SHADE);
            }
            else
            {
                AddSingleLog(m, -right * 0.105f, d, len, yaw);
                AddSingleLog(m,  right * 0.105f, d, len, yaw);
                AddSingleLog(m, new Vector3(0f, d * 0.78f, 0f), d * 0.96f, len * 0.96f, yaw);
                m.Add(Prim.Box, new Vector3(0f, 0.02f, 0f), new Vector3(0.24f, 0.08f, 0.28f), yaw: yaw, noOutline: true, uv: UV_SHADE);
            }
        }

        private static void AddSingleLog(Model3D m, Vector3 baseCenter, float d, float len, float yaw)
        {
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float r = d * 0.5f;
            var p0 = baseCenter + new Vector3(0f, r, 0f) - fwd * (len * 0.5f);
            // 外层树皮圆筒（带描边）
            m.Add(Prim.Cylinder, p0, new Vector3(d, len, d), yaw: yaw, pitch: 90f, uv: UV_SIDE);
            // 前后两端浅米黄年轮截面盘（noOutline=true，消除端盖内部黑环并还原原版浅黄木芯）
            float capD = d * 0.82f;
            m.Add(Prim.Cylinder, p0 - fwd * 0.004f, new Vector3(capD, 0.016f, capD), yaw: yaw, pitch: 90f, noOutline: true, uv: UV_DETAIL);
            m.Add(Prim.Cylinder, p0 + fwd * (len - 0.012f), new Vector3(capD, 0.016f, capD), yaw: yaw, pitch: 90f, noOutline: true, uv: UV_DETAIL);
        }

        /// <summary>重力板（GravlitePanel）：还原原版三层错位叠放的折角六边形浅青银飞船装甲板。</summary>
        private static void BuildHexPanel(Model3D m, int v)
        {
            int layers = v == 0 ? 1 : (v == 1 ? 2 : 3);
            for (int i = 0; i < layers; i++)
            {
                float ox = (i - (layers - 1) * 0.5f) * 0.050f;
                float oz = (i - (layers - 1) * 0.5f) * 0.038f;
                float y = i * 0.075f;
                m.Add(Prim.Box, new Vector3(ox, y, oz), new Vector3(0.38f, 0.080f, 0.38f), yaw: -28f, uv: UV_TOP);
                m.Add(Prim.Box, new Vector3(ox, y + 0.002f, oz), new Vector3(0.38f, 0.076f, 0.38f), yaw: 17f, noOutline: true, uv: UV_TOP);
            }
        }

        /// <summary>布料 / 合成纤维 / 魔菇布 / 超织物（FabricRoll）：还原原版斜置圆柱布卷 + 展开布摆 + 浅色卷芯端面。</summary>
        private static void BuildFabricRoll(Model3D m, int v)
        {
            const float yaw = -30f;
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

            // 1. 从布卷底侧向外平铺展开的布匹尾摆
            m.Add(Prim.Box, right * 0.08f, new Vector3(0.34f, 0.045f, 0.48f), yaw: yaw, uv: UV_TOP);

            // 2. 斜置饱满圆柱形布卷主筒
            float d = v == 0 ? 0.18f : (v == 1 ? 0.22f : 0.25f);
            float len = 0.54f;
            var p0 = -right * 0.04f + new Vector3(0f, d * 0.48f, 0f) - fwd * (len * 0.5f);
            m.Add(Prim.Cylinder, p0, new Vector3(d, len, d), yaw: yaw, pitch: 90f, uv: UV_TOP);

            // 3. 前后端面浅色螺旋布芯（UV_DETAIL）与中心小卷孔（UV_SHADE）
            m.Add(Prim.Cylinder, p0 - fwd * 0.005f, new Vector3(d * 0.76f, 0.018f, d * 0.76f), yaw: yaw, pitch: 90f, noOutline: true, uv: UV_DETAIL);
            m.Add(Prim.Cylinder, p0 - fwd * 0.008f, new Vector3(d * 0.30f, 0.020f, d * 0.30f), yaw: yaw, pitch: 90f, noOutline: true, uv: UV_SHADE);
            m.Add(Prim.Cylinder, p0 + fwd * (len - 0.013f), new Vector3(d * 0.76f, 0.018f, d * 0.76f), yaw: yaw, pitch: 90f, noOutline: true, uv: UV_DETAIL);
        }

        /// <summary>工业零部件（ComponentIndustrial）：还原原版机械底盘 + 交叉金属连杆 + 右下角黄铜线圈筒。</summary>
        private static void BuildCompIndustrial(Model3D m)
        {
            // 1. 底部多边形机械托架与左后暗色齿轮箱
            m.Add(Prim.Box, new Vector3( 0.00f, 0.00f,  0.00f), new Vector3(0.38f, 0.14f, 0.36f), yaw: -16f, uv: UV_SIDE);
            m.Add(Prim.Box, new Vector3(-0.08f, 0.10f, -0.06f), new Vector3(0.24f, 0.16f, 0.22f), yaw: 24f, uv: UV_SHADE);
            // 2. 斜向贯穿的亮钢活塞连杆
            m.Add(Prim.Box, new Vector3(-0.02f, 0.12f,  0.02f), new Vector3(0.14f, 0.14f, 0.48f), yaw: -38f, uv: UV_TOP);
            // 3. 右前侧标志性暖黄铜线圈电容筒 (UV_DETAIL)
            m.Add(Prim.Cylinder, new Vector3(0.11f, 0.04f, 0.09f), new Vector3(0.18f, 0.22f, 0.18f), uv: UV_DETAIL);
            // 4. 左上侧圆形法兰盘
            m.Add(Prim.Cylinder, new Vector3(-0.11f, 0.06f, 0.08f), new Vector3(0.18f, 0.16f, 0.18f), uv: UV_TOP);
        }

        /// <summary>高级零部件（ComponentSpacer）：还原原版精密矩形机匣 + 右上角亮银圆形能量核心环。</summary>
        private static void BuildCompSpacer(Model3D m)
        {
            // 1. 精密矩形装甲机匣底座与中层壳体
            m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.44f, 0.16f, 0.40f), uv: UV_SIDE);
            m.Add(Prim.Box, new Vector3(-0.03f, 0.12f, -0.02f), new Vector3(0.36f, 0.12f, 0.32f), uv: UV_TOP);
            // 2. 右上角凸起的亮银色圆形能量核心筒与内芯 (UV_DETAIL)
            m.Add(Prim.Cylinder, new Vector3(0.10f, 0.14f, -0.06f), new Vector3(0.22f, 0.15f, 0.22f), uv: UV_TOP);
            m.Add(Prim.Cylinder, new Vector3(0.10f, 0.27f, -0.06f), new Vector3(0.14f, 0.03f, 0.14f), noOutline: true, uv: UV_DETAIL);
            // 3. 前侧散热凹槽护板
            m.Add(Prim.Box, new Vector3(-0.08f, 0.04f, 0.16f), new Vector3(0.22f, 0.12f, 0.12f), noOutline: true, uv: UV_SHADE);
        }

        /// <summary>化合燃料（Chemfuel）：还原原版鲜红色立式便携油桶（Jerrycan）+ 顶部提手 + 右上角斜向黑桶盖。</summary>
        private static void BuildChemfuel(Model3D m)
        {
            // 1. 立式红色便携油桶主箱体 + 顶部收肩
            m.Add(Prim.Box, new Vector3(0.00f, 0.00f, 0.00f), new Vector3(0.36f, 0.36f, 0.24f), uv: UV_TOP);
            m.Add(Prim.Box, new Vector3(-0.03f, 0.33f, 0.00f), new Vector3(0.24f, 0.08f, 0.18f), uv: UV_TOP);
            // 2. 左上方提手握梁
            m.Add(Prim.Box, new Vector3(-0.05f, 0.39f, 0.00f), new Vector3(0.18f, 0.05f, 0.10f), uv: UV_SIDE);
            // 3. 右上角向外斜伸的黑色旋拧油桶盖 (UV_DETAIL)
            m.Add(Prim.Cylinder, new Vector3(0.11f, 0.33f, 0.00f), new Vector3(0.11f, 0.10f, 0.11f), roll: -32f, uv: UV_DETAIL);
            // 4. 桶身侧面十字浮雕压筋 (noOutline=true)
            m.Add(Prim.Box, new Vector3(0.00f, 0.07f, 0.00f), new Vector3(0.24f, 0.22f, 0.26f), noOutline: true, uv: UV_SIDE);
        }

        /// <summary>中性胺（Neutroamine）：还原原版三只品字形簇拥站立的白色医用试剂瓶 + 蓝青色瓶腹标签。</summary>
        private static void BuildBottles(Model3D m)
        {
            // 隐于三瓶交界中心底部的内连桥（外部不可见，确保 3D 连通性）
            m.Add(Prim.Box, new Vector3(0.00f, 0.01f, -0.01f), new Vector3(0.18f, 0.06f, 0.14f), noOutline: true, uv: UV_TOP);

            Vector3[] centers =
            {
                new Vector3(-0.11f, 0.00f,  0.04f),
                new Vector3( 0.11f, 0.00f,  0.04f),
                new Vector3( 0.00f, 0.00f, -0.08f),
            };
            for (int i = 0; i < centers.Length; i++)
            {
                var c = centers[i];
                // 白色医用方圆瓶身
                m.Add(Prim.Cylinder, c, new Vector3(0.18f, 0.25f, 0.18f), uv: UV_TOP);
                // 瓶腹一圈青蓝色药液标签带 (UV_DETAIL, 外扩 8mm 杜绝 Z-fighting)
                m.Add(Prim.Cylinder, c + new Vector3(0f, 0.055f, 0f), new Vector3(0.194f, 0.11f, 0.194f), noOutline: true, uv: UV_DETAIL);
                // 细瓶颈与深色瓶盖 (UV_SHADE)
                m.Add(Prim.Cylinder, c + new Vector3(0f, 0.23f, 0f), new Vector3(0.09f, 0.07f, 0.09f), noOutline: true, uv: UV_TOP);
                m.Add(Prim.Cylinder, c + new Vector3(0f, 0.28f, 0f), new Vector3(0.10f, 0.05f, 0.10f), uv: UV_SHADE);
            }
        }

        /// <summary>医药 / 闪耀世界医药（MedBox）：还原原版三层叠放的扁平急救箱 + 顶面米白内框与翠绿十字标。</summary>
        private static void BuildMedBox(Model3D m, bool ultratech)
        {
            for (int i = 0; i < 3; i++)
            {
                float y = i * 0.075f;
                m.Add(Prim.Box, new Vector3(0f, y, 0f), new Vector3(0.44f, 0.082f, 0.38f), uv: UV_TOP);
            }
            float topY = 0.232f;
            // 顶面浅色内框底板
            m.Add(Prim.Box, new Vector3(0f, topY, 0f), new Vector3(0.30f, 0.016f, 0.26f), noOutline: true, uv: ultratech ? UV_TOP : UV_SIDE);
            // 顶面中央醒目的翠绿十字标志 (UV_DETAIL)
            m.Add(Prim.Box, new Vector3(0f, topY + 0.010f, 0f), new Vector3(0.22f, 0.020f, 0.076f), noOutline: true, uv: UV_DETAIL);
            m.Add(Prim.Box, new Vector3(0f, topY + 0.010f, 0f), new Vector3(0.076f, 0.020f, 0.22f), noOutline: true, uv: UV_DETAIL);
        }

        /// <summary>草药（MedicineHerbal）：还原原版三层麻绳捆扎的棕色草药方包 + 顶面翠绿药草叶簇。</summary>
        private static void BuildMedHerbal(Model3D m)
        {
            for (int i = 0; i < 3; i++)
            {
                float y = i * 0.072f;
                m.Add(Prim.Box, new Vector3(0f, y, 0f), new Vector3(0.42f, 0.080f, 0.36f), uv: UV_TOP);
            }
            // 纵向与横向深棕麻绳捆带 (UV_SHADE, 充足外扩消除与盒面 Z-fighting)
            m.Add(Prim.Box, new Vector3(0f, 0.005f, 0f), new Vector3(0.442f, 0.226f, 0.072f), noOutline: true, uv: UV_SHADE);
            m.Add(Prim.Box, new Vector3(0f, 0.005f, 0f), new Vector3(0.072f, 0.226f, 0.382f), noOutline: true, uv: UV_SHADE);
            // 顶面翠绿药草叶簇 (UV_DETAIL)
            m.Add(Prim.Sphere, new Vector3( 0.00f, 0.205f,  0.00f), new Vector3(0.26f, 0.08f, 0.22f), noOutline: true, uv: UV_DETAIL);
            m.Add(Prim.Sphere, new Vector3(-0.06f, 0.215f,  0.03f), new Vector3(0.18f, 0.08f, 0.15f), noOutline: true, uv: UV_DETAIL);
            m.Add(Prim.Sphere, new Vector3( 0.06f, 0.215f, -0.02f), new Vector3(0.18f, 0.08f, 0.15f), noOutline: true, uv: UV_DETAIL);
        }

        /// <summary>有毒废料包（Wastepack）：还原原版三只品字形堆叠的灰绿色鼓胀防化垃圾袋与扎口结。</summary>
        private static void BuildWastepack(Model3D m)
        {
            Vector3[] bags =
            {
                new Vector3(-0.11f, 0.00f,  0.05f),
                new Vector3( 0.12f, 0.00f,  0.04f),
                new Vector3( 0.00f, 0.06f, -0.07f),
            };
            for (int i = 0; i < bags.Length; i++)
            {
                var b = bags[i];
                // 鼓胀圆润的防化袋肚
                m.Add(Prim.Sphere, b, new Vector3(0.32f, 0.26f, 0.28f), uv: i == 2 ? UV_TOP : UV_SIDE);
                // 袋顶扎紧的深色束口环 (UV_SHADE)
                m.Add(Prim.Cylinder, b + new Vector3(0f, 0.22f, 0f), new Vector3(0.10f, 0.04f, 0.10f), noOutline: true, uv: UV_SHADE);
                // 扎口上方分叉翘起的塑料袋结头褶皱
                m.Add(Prim.Cone, b + new Vector3(-0.02f, 0.24f, 0f), new Vector3(0.09f, 0.09f, 0.08f), roll: 18f, taper: 0.25f, uv: UV_TOP);
                m.Add(Prim.Cone, b + new Vector3( 0.02f, 0.24f, 0f), new Vector3(0.08f, 0.08f, 0.08f), roll: -18f, taper: 0.25f, noOutline: true, uv: UV_DETAIL);
            }
        }
    }
}
