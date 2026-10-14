// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 动物程序化 3D 建模（基于原版三视图贴图取样与解剖分件重构）
    // ============================================================
    //
    // 【上游依据】
    //   1. 材质与分色：由 scripts/animal_atlas.py 解析原版（Core / Biotech /
    //      Ideology / Odyssey）全部动物的三视图贴图（_east / _south / _north）
    //      及 CutoutComplex 遮罩与 XML 染色，提取 6 个真实纹理区打包进
    //      Textures\FPS3D\AnimalAtlas.png（UV 表见 AnimalUVGen.cs）：
    //        · UV_COAT   (0) : 背部 / 躯干主毛皮、羽翼、鳞甲或背壳
    //        · UV_BELLY  (1) : 胸腹 / 下摆长毛 / 折叠翼 / 腹甲
    //        · UV_HEAD   (2) : 头部 / 面盘 / 吻部 / 头胸甲
    //        · UV_LIMB   (3) : 四肢 / 蹄爪 / 耳廓 / 深色肩腿带
    //        · UV_DETAIL (4) : 物种标志性特征（爆炸羊黄囊、毒气羊绿囊、敲击兽弯角、
    //                          鸡冠肉垂、象牙、鹿角、鸭喙、企鹅橙喙、熊猫黑斑、狐白尾尖等）
    //        · UV_EYE    (5) : 眼瞳 / 鼻头深色区
    //   2. 几何造型：延续圆润基本体 + 逐件平滑法线反绕描边壳的赛璐璐画风，
    //      严格对照原版三视图剪影（前胸垂瓣 + 腹下内收弧 + 后臀垂瓣、多段曲颈、
    //      吻颌鼻眼耳、弯刀角/盘角/分叉鹿角/掌状角、背部化学囊泡、开屏尾羽等）
    //      拼装 22~48 个精细部件，彻底取代早期的"单球身体+四根直柱腿"。
    [StaticConstructorOnStartup]
    public static class Animal3D
    {
        public enum Arch
        {
            Thrumbo,
            WoollyUngulate,
            SacUngulate,
            Camelid,
            Pachyderm,
            CervidEquine,
            UrsineSloth,
            Canid,
            Felid,
            Porcine,
            RodentSmall,
            PrimateDryad,
            BirdFowl,
            TallBird,
            Penguin,
            ShellCreature,
            ReptileAmphibian,
            Serpent,
            Insectoid
        }

        private const int UV_COAT = 0;
        private const int UV_BELLY = 1;
        private const int UV_HEAD = 2;
        private const int UV_LIMB = 3;
        private const int UV_DETAIL = 4;
        private const int UV_EYE = 5;

        private static readonly string[] RegionNames =
        {
            "coat", "belly", "head", "limb", "detail", "eye"
        };

        private class Built
        {
            public Mesh body;
            public Mesh hull;
            public float bodyCenterY;
            public float bodyHalfW;
            public float L;
            public bool usesAtlas;
        }

        private static readonly Dictionary<string, Built> cache = new Dictionary<string, Built>();
        private static Texture2D atlasTex;
        private static Material atlasMat;

        public static bool Enabled
        {
            get { var s = FirstPerson3DMod.settings; return s == null || s.animalModels; }
        }

        /// <summary>该 pawn 是否由本系统绘制（非人形、非机械族且在 3D 动物图集中）。</summary>
        public static bool HandlesPawn(Pawn p)
        {
            if (!Enabled || p == null || p.RaceProps == null) return false;
            if (p.RaceProps.Humanlike) return false;
            if (p.RaceProps.IsMechanoid) return false;
            string key = ResolveSpeciesKey(p);
            return AnimalUVGen.R.ContainsKey(key + ".coat");
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

        private static Material AtlasMat
        {
            get
            {
                if (atlasMat != null) return atlasMat;
                try { atlasTex = ContentFinder<Texture2D>.Get(AnimalUVGen.TexPath, reportFailure: false); }
                catch { atlasTex = null; }
                if (atlasTex == null) return null;
                atlasTex.filterMode = FilterMode.Bilinear;
                atlasTex.wrapMode = TextureWrapMode.Clamp;
                atlasMat = Util3D.CutoutMatFor(atlasTex);
                return atlasMat;
            }
        }

        // ---- 物种键与原型归类 ----

        private static bool Has(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (s.IndexOf(keys[i], System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>解析该 Pawn 在 AnimalUVGen 中的物种键（优先匹配 kindDef 以区分北极熊/灰熊、黑豹/美洲狮、雪兔/野兔）。</summary>
        private static string ResolveSpeciesKey(Pawn p)
        {
            if (p == null) return "muffalo";
            string kd = p.kindDef != null && p.kindDef.defName != null ? p.kindDef.defName.ToLowerInvariant() : "";
            string dd = p.def != null && p.def.defName != null ? p.def.defName.ToLowerInvariant() : "";
            if (kd.Length > 0 && AnimalUVGen.R.ContainsKey(kd + ".coat")) return kd;
            if (dd.Length > 0 && AnimalUVGen.R.ContainsKey(dd + ".coat")) return dd;
            string n = kd.Length > 0 ? kd : dd;
            if (Has(n, "polar")) return "bear_polar";
            if (Has(n, "grizzly", "bear")) return "bear_grizzly";
            if (Has(n, "panther")) return "panther";
            if (Has(n, "snowhare")) return "snowhare";
            if (Has(n, "alphathrumbo")) return "alphathrumbo";
            if (Has(n, "thrumbo")) return "thrumbo";
            if (Has(n, "toxalope")) return "toxalope";
            if (Has(n, "boomalope")) return "boomalope";
            if (Has(n, "boomrat")) return "boomrat";
            if (Has(n, "wasterat")) return "wasterat";
            if (Has(n, "greatwolf")) return "wolf_great";
            if (Has(n, "wolf_arctic")) return "wolf_arctic";
            if (Has(n, "wolf")) return "wolf_timber";
            if (Has(n, "fox_arctic")) return "fox_arctic";
            if (Has(n, "fox_fennec")) return "fox_fennec";
            if (Has(n, "fox")) return "fox_red";
            if (Has(n, "rhino")) return "rhinoceros";
            if (Has(n, "labrador")) return "labradorretriever";
            if (Has(n, "terrier", "yorkshire")) return "yorkshireterrier";
            if (Has(n, "scimitar")) return "cat_scimitar";
            if (Has(n, "dryad")) return "dryad_basic";
            return n;
        }

        public static Arch ArchOf(string n)
        {
            if (Has(n, "thrumbo")) return Arch.Thrumbo;
            if (Has(n, "boomalope", "toxalope")) return Arch.SacUngulate;
            if (Has(n, "dromedary", "camel", "alpaca", "llama")) return Arch.Camelid;
            if (Has(n, "muffalo", "bison", "yak", "muskox", "sheep")) return Arch.WoollyUngulate;
            if (Has(n, "elephant", "mastodon", "rhino", "hippo")) return Arch.Pachyderm;
            if (Has(n, "penguin")) return Arch.Penguin;
            if (Has(n, "ostrich", "emu", "cassowary", "flamingo", "heron", "crane", "stork")) return Arch.TallBird;
            if (Has(n, "chicken", "duck", "goose", "swan", "turkey", "peacock", "quail", "sparrow", "crow", "bluebird", "macaw", "parrot", "pigeon", "toucan", "vulture", "raven", "owl", "eagle", "hawk", "bird")) return Arch.BirdFowl;
            if (Has(n, "tortoise", "seaturtle", "turtle", "armadillo", "hermitcrab", "stonecrab", "crab", "lavasnail", "snail", "slug")) return Arch.ShellCreature;
            if (Has(n, "cobra", "snake", "serpent", "viper")) return Arch.Serpent;
            if (Has(n, "megaspider", "spelopede", "megascarab", "spider", "scarab", "insect", "beetle", "megaworm")) return Arch.Insectoid;
            if (Has(n, "iguana", "monitor", "lizard", "gecko", "skink", "komodo", "alligator", "croc", "gator", "bullfrog", "colossustoad", "frog", "toad", "salamander", "newt", "walrus", "seal", "sealion")) return Arch.ReptileAmphibian;
            if (Has(n, "monkey", "gorilla", "ape", "chimp", "baboon", "dryad")) return Arch.PrimateDryad;
            if (Has(n, "bear", "panda", "megasloth", "badger", "wolverine")) return Arch.UrsineSloth;
            if (Has(n, "hare", "snowhare", "rabbit", "squirrel", "beaver", "raccoon", "capybara", "chinchilla", "guineapig", "rat", "boomrat", "wasterat", "porcupine", "otter", "mink", "prairie", "vole", "hamster")) return Arch.RodentSmall;
            if (Has(n, "pig", "wildboar", "boar")) return Arch.Porcine;
            if (Has(n, "fox", "wolf", "warg", "husky", "labrador", "terrier", "hound", "dog")) return Arch.Canid;
            if (Has(n, "horse", "donkey", "deer", "elk", "caribou", "moose", "ibex", "goat", "gazelle", "antelope", "cow", "bull")) return Arch.CervidEquine;
            if (Has(n, "cat", "cougar", "panther", "lynx", "tiger")) return Arch.Felid;
            return Arch.Canid;
        }

        /// <summary>按原版 _south 与 _east 实测剪影宽高比校准正视宽度，避免修长物种在正视图显胖。</summary>
        private static float CalibrateWidth(string n, float bl, float defaultBw)
        {
            Vector3 prop;
            if (AnimalUVGen.Proportions.TryGetValue(n, out prop) && prop.x > 0.05f)
            {
                float ratio = Mathf.Clamp(prop.z / prop.x, 0.36f, 0.78f);
                float measuredBw = bl * ratio * 0.92f;
                return Mathf.Lerp(defaultBw, measuredBw, 0.65f);
            }
            return defaultBw;
        }

        // ---- 几何辅助：对称件、多段弯曲链、解剖躯干、精细头部 ----

        private static void AddSymX(Model3D m, Prim kind, float x, float y, float z, Vector3 size,
            float yaw = 0f, float pitch = 0f, float splay = 0f, float taper = 0f, int uv = UV_COAT, bool noOutline = false)
        {
            m.Add(kind, new Vector3(-x, y, z), size, -yaw, pitch, noOutline, uv, splay, taper);
            m.Add(kind, new Vector3(x, y, z), size, yaw, pitch, noOutline, uv, -splay, taper);
        }

        private static Vector3 StepVec(float len, float pitch, float yaw = 0f, float roll = 0f)
        {
            return Quaternion.Euler(pitch, yaw, roll) * new Vector3(0f, len, 0f);
        }

        /// <summary>从起点 p0 推进一节锥柱（内部球节 noOutline=true 消除折角黑环），返回该节末端坐标。</summary>
        private static Vector3 AddSeg(Model3D m, Vector3 p0, float len, float d0, float d1,
            float pitch, float yaw = 0f, float roll = 0f, int uv = UV_COAT, bool joint = true, bool noOutline = false)
        {
            if (joint)
                m.Add(Prim.Sphere, p0 - new Vector3(0f, d0 * 0.44f, 0f),
                    new Vector3(d0 * 0.88f, d0 * 0.82f, d0 * 0.88f), noOutline: true, uv: uv);
            float taper = d0 > 0.0001f ? Mathf.Clamp(d1 / d0, 0.04f, 1.8f) : 1f;
            m.Add(Prim.Cylinder, p0, new Vector3(d0, len, d0), yaw: yaw, pitch: pitch, noOutline: noOutline, uv: uv, roll: roll, taper: taper);
            return p0 + StepVec(len * 0.88f, pitch, yaw, roll);
        }

        /// <summary>左右对称推进一节锥柱（splay > 0 向外侧张开，splay < 0 向内侧收拢）。</summary>
        private static void AddSymSeg(Model3D m, ref Vector3 pL, ref Vector3 pR, float len, float d0, float d1,
            float pitch, float splay, float yaw = 0f, int uv = UV_DETAIL, bool joint = true, bool noOutline = false)
        {
            pL = AddSeg(m, pL, len, d0, d1, pitch, -yaw, splay, uv, joint, noOutline);
            pR = AddSeg(m, pR, len, d0, d1, pitch, yaw, -splay, uv, joint, noOutline);
        }

        /// <summary>
        /// 原版风格流线型平滑躯干：
        ///   · 浑然一体的主躯干贯通前胸至后臀，背部脊线平顺连续无马鞍深坑与分段圆球肉瘤；
        ///   · 两侧肩臀肌肉过渡层贴合侧壁，消除突兀向外鼓出的圆球包；
        ///   · 表面胸腹分色片与肩臀肌肉过渡层全部设为 noOutline=true，彻底杜绝毛虫分段黑线与胸口靶心黑圈。
        /// </summary>
        private static void BuildQuadBody(Model3D m, float L, float bl, float bh, float bw,
            float legH, float legD, float chestLift, float rumpScale,
            out Vector3 chestTop, out Vector3 tailBase,
            int frontLegUv = UV_LIMB, int rearLegUv = UV_LIMB, int bellyUv = UV_BELLY,
            float waistDip = 0f, float tuckUp = 0f)
        {
            float fz = bl * 0.18f;
            float rz = -bl * 0.20f;
            float sx = bw * 0.14f;

            int chestUv = (frontLegUv == UV_BELLY || bellyUv == UV_COAT) ? frontLegUv : UV_COAT;
            float midZ = (fz + rz) * 0.5f;

            float frontH = bh + chestLift, rearH = bh * 0.95f * rumpScale;
            float midH = Mathf.Max(bh * 0.60f, (frontH + rearH) * 0.48f - waistDip);
            float midY = Mathf.Max(0f, bh * 0.02f + tuckUp * 0.5f);

            // 1. 唯一流线型主躯干（全身唯一的带外轮廓椭球，从颈根到臀尖全线圆顺包裹，彻底消除多节块状拼接与分段感）
            float bodyH = Mathf.Max(frontH, rearH) * 0.98f - waistDip * 0.30f;
            float bodyY = Mathf.Max(0f, tuckUp * 0.20f);
            m.Add(Prim.Sphere, new Vector3(0f, bodyY, midZ),
                new Vector3(bw * 0.98f, bodyH, bl * 0.96f), uv: UV_COAT);

            // 2. 内部深胸与后臀体块（全部 noOutline: true，只充填肌肉体积，绝不在外表产生阶梯隆起与分节黑环）
            m.Add(Prim.Sphere, new Vector3(0f, 0f, fz),
                new Vector3(bw * 0.92f, frontH * 0.92f, bl * 0.46f), uv: chestUv, noOutline: true);
            m.Add(Prim.Sphere, new Vector3(0f, Mathf.Max(0f, tuckUp * 0.25f), rz),
                new Vector3(bw * 0.90f * rumpScale, rearH * 0.92f, bl * 0.46f), uv: UV_COAT, noOutline: true);

            // 3. 内部背腹平滑顺延层（高度严格收敛在主躯干内部，消除侧视中段突兀隆起）
            float bridgeH = Mathf.Min(bodyH * 0.95f, (frontH + rearH) * 0.48f - waistDip);
            m.Add(Prim.Sphere, new Vector3(0f, Mathf.Max(0f, tuckUp * 0.25f), midZ),
                new Vector3(bw * 0.88f, bridgeH, bl * 0.82f), uv: UV_COAT, noOutline: true);

            // 4. 侧身肋部贴合
            AddSymX(m, Prim.Sphere, sx, 0f, fz * 0.85f,
                new Vector3(bw * 0.46f, frontH * 0.85f, bl * 0.48f), uv: chestUv, noOutline: true);
            AddSymX(m, Prim.Sphere, sx, Mathf.Max(0f, tuckUp * 0.35f), rz * 0.85f,
                new Vector3(bw * 0.44f * rumpScale, rearH * 0.83f, bl * 0.48f), uv: UV_COAT, noOutline: true);

            chestTop = new Vector3(0f, chestLift + bh * 0.64f, bl * 0.32f);
            tailBase = new Vector3(0f, bh * 0.52f * rumpScale, -bl * 0.44f);
        }

        /// <summary>食肉目/啮齿目/灵长目头部：同心双层圆颅 + 同心双层倾角长椭球楔形吻部（零端盖接缝环、正视V形收尖、侧视上下颌自然分色）。</summary>
        private static void BuildHead(Model3D m, Vector3 headCenter, float hr,
            float snoutLen, float snoutW, float snoutH,
            int headUv = UV_HEAD, int snoutUv = UV_HEAD, int jawUv = UV_BELLY, bool hasNose = true)
        {
            const float pitch = 112f;
            const float tilt = pitch - 90f;

            // 1. 颅部：上提窄核（noOutline=false，使底弧高于眼下交界处被100%遮挡）+ 饱满颅皮（noOutline=true）
            m.Add(Prim.Sphere, headCenter + new Vector3(0f, -hr * 0.25f, -hr * 0.02f),
                new Vector3(hr * 0.84f, hr * 0.68f, hr * 0.90f), uv: headUv);
            m.Add(Prim.Sphere, headCenter - new Vector3(0f, hr * 0.44f, 0f),
                new Vector3(hr * 0.90f, hr * 0.88f, hr * 0.96f), noOutline: true, uv: headUv);

            // 2. 楔形长椭球吻部（球心锚定于眼下颅前，仅前半椭球伸出颅外，单调收尖无球泡、无圆柱端盖接缝）
            var cSnout = headCenter + new Vector3(0f, -hr * 0.10f, hr * 0.12f) + StepVec(snoutLen * 0.22f, pitch);
            float sx = Mathf.Max(snoutW * 1.32f, hr * 0.74f);
            float sy = Mathf.Max(snoutH * 1.22f, hr * 0.58f);
            float sz = snoutLen * 1.54f;
            float sxCore = Mathf.Max(0.02f, sx - 0.022f);
            float syCore = Mathf.Max(0.02f, sy - 0.022f);
            float szCore = Mathf.Max(0.02f, sz - 0.022f);
            m.Add(Prim.Sphere, cSnout - StepVec(syCore * 0.5f, tilt),
                new Vector3(sxCore, syCore, szCore), pitch: tilt, uv: snoutUv);
            m.Add(Prim.Sphere, cSnout - StepVec(sy * 0.5f, tilt),
                new Vector3(sx, sy, sz), pitch: tilt, noOutline: true, uv: snoutUv);

            // 3. 下颌与腮部连续浅色过渡层（一体贴合吻部下沿，消除孤立腮部圆斑）
            if (jawUv != snoutUv)
            {
                var cJaw = cSnout + new Vector3(0f, -sy * 0.14f, -hr * 0.04f);
                float hJaw = sy * 0.64f;
                m.Add(Prim.Sphere, cJaw - StepVec(hJaw * 0.5f, tilt),
                    new Vector3(sx * 0.95f, hJaw, sz * 0.90f), pitch: tilt, noOutline: true, uv: jawUv);
            }

            // 4. 眼下颧骨与鼻根无描边消隐桥（100% 遮挡颅核与吻核内部交界线）
            var cBridge = headCenter + new Vector3(0f, -hr * 0.05f, hr * 0.18f);
            float hBridge = hr * 0.74f;
            m.Add(Prim.Sphere, cBridge - StepVec(hBridge * 0.5f, tilt * 0.6f),
                new Vector3(hr * 0.86f, hBridge, hr * 0.78f), pitch: tilt * 0.6f, noOutline: true, uv: headUv);

            // 5. 鼻尖与双眼（鼻尖锚定于楔形吻端前上尖，正视位于V形吻端底部）
            if (hasNose)
            {
                var noseCenter = cSnout + StepVec(sz * 0.48f, pitch) + new Vector3(0f, sy * 0.06f, 0f);
                float nh = sy * 0.28f;
                m.Add(Prim.Sphere, noseCenter - new Vector3(0f, nh * 0.5f, 0f),
                    new Vector3(sx * 0.30f, nh, sx * 0.28f), noOutline: true, uv: UV_EYE);
            }
            AddSymX(m, Prim.Sphere, hr * 0.36f, headCenter.y - hr * 0.02f, headCenter.z + hr * 0.28f,
                new Vector3(hr * 0.19f, hr * 0.21f, hr * 0.21f), uv: UV_EYE, noOutline: true);
        }

        /// <summary>有蹄类草食动物专属长脸头部（马/鹿/驼鹿/牛/羊/骆驼）：长楔形主轮廓 + 顺滑一体式长脸（彻底消除面颊与吻部的任何断层折角）。</summary>
        private static void BuildUngulateHead(Model3D m, Vector3 headCenter, float hr,
            float muzzleLen, float muzzleW, float muzzleH,
            int headUv = UV_HEAD, int muzzleUv = UV_HEAD, int jawUv = UV_BELLY, float pitch = 118f)
        {
            float tilt = pitch - 90f;
            // 1. 贯穿整个头脸的长楔形主轮廓（头部全线仅此一个外轮廓描边，从脑后到吻端自然一体，彻底消除面颊与吻部的任何断层折角）
            var cFullHead = headCenter + StepVec(muzzleLen * 0.16f, pitch);
            float fullLen = hr * 0.95f + muzzleLen * 1.35f;
            float fullW = Mathf.Max(muzzleW * 1.35f, hr * 0.82f);
            float fullH = Mathf.Max(muzzleH * 1.25f, hr * 0.68f);
            m.Add(Prim.Sphere, cFullHead - StepVec(fullH * 0.45f, tilt),
                new Vector3(fullW, fullH, fullLen), pitch: tilt, uv: headUv);

            // 2. 内部饱满后颅（noOutline: true）
            m.Add(Prim.Sphere, headCenter - new Vector3(0f, hr * 0.42f, 0f),
                new Vector3(hr * 0.88f, hr * 0.86f, hr * 0.94f), noOutline: true, uv: headUv);

            // 3. 内部顺滑口吻与鼻端（noOutline: true）
            var cMuzzle = headCenter + new Vector3(0f, -hr * 0.08f, hr * 0.12f) + StepVec(muzzleLen * 0.32f, pitch);
            float sx = Mathf.Max(muzzleW * 1.22f, hr * 0.70f);
            float sy = Mathf.Max(muzzleH * 1.15f, hr * 0.58f);
            float sz = muzzleLen * 1.45f;
            m.Add(Prim.Sphere, cMuzzle - StepVec(sy * 0.5f, tilt),
                new Vector3(sx, sy, sz), pitch: tilt, noOutline: true, uv: headUv);

            if (muzzleUv != headUv)
            {
                var cTip = cMuzzle + StepVec(sz * 0.20f, pitch);
                float hTip = sy * 0.90f;
                m.Add(Prim.Sphere, cTip - StepVec(hTip * 0.5f, tilt),
                    new Vector3(sx * 0.92f, hTip, sz * 0.65f), pitch: tilt, noOutline: true, uv: muzzleUv);
            }

            // 4. 温和侧眼（位于额骨两侧、耳朵前方）
            AddSymX(m, Prim.Sphere, hr * 0.36f, headCenter.y + hr * 0.04f, headCenter.z + hr * 0.04f,
                new Vector3(hr * 0.16f, hr * 0.16f, hr * 0.18f), uv: UV_EYE, noOutline: true);
        }

        /// <summary>立耳 / 长耳 / 圆耳 / 垂耳。</summary>
        private static void BuildEars(Model3D m, Vector3 headCenter, float hr, float earW, float earH,
            bool floppy = false, bool roundEar = false, bool tufted = false, int earUv = UV_LIMB)
        {
            if (floppy)
            {
                AddSymX(m, Prim.Sphere, hr * 0.46f, headCenter.y - hr * 0.40f, headCenter.z - hr * 0.04f,
                    new Vector3(earW * 0.9f, earH * 1.15f, earW * 0.65f), yaw: 12f, splay: 18f, uv: earUv);
            }
            else if (roundEar)
            {
                AddSymX(m, Prim.Sphere, hr * 0.35f, headCenter.y + hr * 0.34f, headCenter.z - hr * 0.08f,
                    new Vector3(earW * 1.05f, earH * 0.88f, earW * 0.42f), yaw: 12f, splay: 22f, uv: earUv, noOutline: true);
            }
            else
            {
                bool longEar = earH > 0.20f * L_ear_ref(hr);
                float sp = longEar ? 18f : 24f;
                float pt = longEar ? -32f : -12f;
                var eL = headCenter + new Vector3(-hr * 0.28f, hr * 0.08f, -hr * 0.06f);
                var eR = headCenter + new Vector3(hr * 0.28f, hr * 0.08f, -hr * 0.06f);
                AddSymSeg(m, ref eL, ref eR, earH * 1.22f, earW, earW * 0.24f, pt, sp, 0f, earUv, joint: false);
                if (tufted)
                    AddSymSeg(m, ref eL, ref eR, earH * 0.52f, earW * 0.32f, earW * 0.08f, pt + 10f, sp * 1.8f, 0f, UV_EYE, joint: false, noOutline: true);
            }
        }

        private static float L_ear_ref(float hr) => hr / 0.22f;

        // ---- 19 类精细解剖建模配方 ----

        public static Model3D BuildModel(string n, Arch a, float L, out float bodyCenterY, out float bodyHalfW)
        {
            var m = new Model3D();
            bodyCenterY = 0.25f * L;
            bodyHalfW = 0.25f * L;
            switch (a)
            {
                case Arch.Thrumbo:
                {
                    bool alpha = Has(n, "alpha");
                    float bl = 0.72f * L, bh = 0.46f * L;
                    float bw = CalibrateWidth(n, bl, 0.36f * L);
                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.10f * L, 0.08f * L, 0.86f,
                        out var chestTop, out var tailBase, UV_COAT, UV_COAT, UV_COAT);
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.52f;

                    // 雪白通体神兽躯干，顺滑覆盖肩背与双胁的雪白厚毛披肩
                    AddSymX(m, Prim.Sphere, bw * 0.20f, bh * 0.18f, 0.02f * L,
                        new Vector3(bw * 0.54f, bh * 0.74f, bl * 0.72f), uv: UV_COAT, noOutline: true);
                    // 原版胸前标志性锯齿白披肩与前胸毛绺（带独立清晰轮廓）
                    m.Add(Prim.Sphere, new Vector3(0f, bh * 0.12f, bl * 0.24f),
                        new Vector3(bw * 0.58f, bh * 0.76f, bl * 0.38f), uv: UV_COAT);
                    AddSymX(m, Prim.Cone, bw * 0.22f, bh * 0.12f, bl * 0.22f,
                        new Vector3(0.065f * L, 0.16f * L, 0.075f * L), pitch: 18f, splay: 28f, taper: 0.18f, uv: UV_COAT);

                    // 腹底标志性白毛锯齿毛帘（4簇沿腹底边缘排布的下垂毛锥）
                    for (int i = 0; i < 4; i++)
                    {
                        float fz = (0.16f - i * 0.11f) * L;
                        m.Add(Prim.Cone, new Vector3(0f, 0.065f * L, fz),
                            new Vector3(0.055f * L, 0.046f * L, 0.065f * L), pitch: -168f, taper: 0.18f, uv: UV_COAT, noOutline: true);
                    }

                    // 自然平顺的微 S 形长颈（前倾角从36°渐变至18°，神兽姿态自然昂首前倾）
                    var np = chestTop + new Vector3(0f, -0.08f * L, 0.02f * L);
                    np = AddSeg(m, np, 0.18f * L, 0.23f * L, 0.19f * L, 36f, uv: UV_COAT, noOutline: true);
                    np = AddSeg(m, np, 0.16f * L, 0.19f * L, 0.16f * L, 26f, uv: UV_COAT, noOutline: true);
                    var hp = AddSeg(m, np, 0.14f * L, 0.16f * L, 0.14f * L, 16f, uv: UV_COAT, noOutline: true);
                    m.Add(Prim.Cylinder, chestTop + new Vector3(0f, -0.06f * L, 0.04f * L),
                        new Vector3(0.12f * L, 0.32f * L, 0.10f * L), pitch: 30f, noOutline: true, uv: UV_COAT, taper: 0.68f);

                    // 贴颈流线雪白披肩毛冠（平贴颈背，消除乱刺穿模）
                    m.Add(Prim.Cone, hp + new Vector3(0f, 0.03f * L, -0.05f * L),
                        new Vector3(0.075f * L, 0.18f * L, 0.080f * L), pitch: -18f, noOutline: true, uv: UV_COAT);
                    AddSymX(m, Prim.Cone, 0.04f * L, hp.y + 0.01f * L, hp.z - 0.04f * L,
                        new Vector3(0.065f * L, 0.16f * L, 0.070f * L), pitch: -22f, splay: 18f, uv: UV_COAT, noOutline: true);

                    // 紧凑异兽头（通体雪白神兽头）
                    float hr = 0.20f * L;
                    var headC = hp + new Vector3(0f, 0.01f * L, 0.03f * L);
                    BuildUngulateHead(m, headC, hr, 0.15f * L, 0.105f * L, 0.105f * L, UV_COAT, UV_COAT, UV_COAT, pitch: 122f);
                    BuildEars(m, headC, hr, 0.048f * L, 0.13f * L, earUv: UV_COAT);

                    // 原版标志性向前上方挺拔耸立、尖端微反勾的白玉弯刀大角
                    float hd0 = (alpha ? 0.142f : 0.128f) * L;
                    var hornP = headC + new Vector3(0f, hr * 0.24f, hr * 0.12f);
                    hornP = AddSeg(m, hornP, 0.18f * L, hd0, hd0 * 0.86f, 22f, uv: UV_DETAIL);
                    hornP = AddSeg(m, hornP, 0.17f * L, hd0 * 0.86f, hd0 * 0.60f, 10f, uv: UV_DETAIL);
                    hornP = AddSeg(m, hornP, 0.16f * L, hd0 * 0.60f, hd0 * 0.32f, -6f, uv: UV_DETAIL);
                    AddSeg(m, hornP, 0.15f * L, hd0 * 0.32f, hd0 * 0.06f, -26f, uv: UV_DETAIL, joint: false);
                    if (alpha)
                        AddSymX(m, Prim.Cone, hr * 0.30f, headC.y + hr * 0.16f, headC.z + hr * 0.08f,
                            new Vector3(0.055f * L, 0.13f * L, 0.055f * L), pitch: -20f, splay: 26f, uv: UV_DETAIL);

                    // 扬起短尾簇
                    m.Add(Prim.Cone, tailBase + new Vector3(0f, 0.04f * L, 0.02f * L),
                        new Vector3(0.085f * L, 0.16f * L, 0.085f * L), pitch: -68f, uv: UV_COAT);
                    break;
                }

                case Arch.WoollyUngulate:
                {
                    bool sheep = Has(n, "sheep");
                    bool bison = Has(n, "bison");
                    bool muskox = Has(n, "muskox");
                    bool yak = Has(n, "yak");
                    bool muffalo = !sheep && !yak && !bison && !muskox;

                    float bl = (sheep ? 0.68f : 0.80f) * L;
                    float bh = (sheep ? 0.48f : 0.56f) * L;
                    float bw = CalibrateWidth(n, bl, (sheep ? 0.46f : 0.54f) * L);

                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.10f * L, (sheep ? 0.02f : 0.08f) * L, bison ? 0.84f : 0.90f,
                        out var chestTop, out var tailBase,
                        bison ? UV_COAT : UV_LIMB,
                        bison ? UV_COAT : UV_LIMB,
                        sheep ? UV_COAT : UV_BELLY);
                    bodyCenterY = bh * 0.52f;
                    bodyHalfW = bw * 0.54f;

                    if (bison)
                    {
                        // 野牛高耸肩峰
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.18f, bl * 0.14f),
                            new Vector3(bw * 1.02f, bh * 0.90f, bl * 0.56f), noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.01f * L),
                            new Vector3(bw * 1.03f, bh * 0.40f, bl * 0.94f), noOutline: true, uv: UV_BELLY);
                    }
                    else if (!sheep && !muffalo)
                    {
                        // 野牦牛 / 牦牛 / 麝牛：圆拱高耸背峰 + 贴地深色底毛裙边(noOutline=true)
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.12f, bl * 0.10f),
                            new Vector3(bw * 1.04f, bh * 0.92f, bl * 0.62f), noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.01f * L),
                            new Vector3(bw * 1.04f, bh * 0.42f, bl * 0.94f), noOutline: true, uv: UV_BELLY);
                    }
                    else if (sheep)
                    {
                        // 绵羊团簇状蓬松绒球
                        AddSymX(m, Prim.Sphere, bw * 0.20f, bh * 0.12f, 0.03f * L,
                            new Vector3(bw * 0.68f, bh * 0.78f, bl * 0.58f), uv: UV_COAT, noOutline: true);
                    }

                    float hr = (sheep ? 0.24f : 0.29f) * L;
                    var headC = new Vector3(0f, bh * (sheep ? 0.48f : bison ? 0.36f : 0.42f), bl * 0.44f);
                    if (muffalo)
                    {
                        // Muffalo 浑然天成的羊毛巨丘：厚重前倾毛肩峰 + 前胸一体化厚毛斗篷领（通体雪青毛色，绝无杂色穿插）
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.16f, bl * 0.14f),
                            new Vector3(bw * 0.96f, bh * 0.82f, bl * 0.56f), noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.10f, bl * 0.34f),
                            new Vector3(bw * 0.86f, bh * 0.76f, bl * 0.34f), noOutline: true, uv: UV_COAT);
                        // 正面居中经典宽阔水滴形灰蓝面盘（面盘边缘平顺嵌于毛领中，额宽颊圆）
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.10f, bl * 0.40f),
                            new Vector3(hr * 0.88f, bh * 0.74f, hr * 0.68f), pitch: 12f, uv: UV_HEAD);
                        // 居中横缝双眼
                        AddSymX(m, Prim.Sphere, hr * 0.24f, bh * 0.38f, bl * 0.515f,
                            new Vector3(0.055f * L, 0.016f * L, 0.045f * L), yaw: 18f, uv: UV_EYE, noOutline: true);
                        // 额顶厚实羊毛发冠与两侧下垂微翘小毛耳
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.26f, bl * 0.36f),
                            new Vector3(hr * 0.76f, 0.09f * L, hr * 0.52f), noOutline: true, uv: UV_COAT);
                        AddSymX(m, Prim.Cone, hr * 0.44f, bh * 0.32f, bl * 0.38f,
                            new Vector3(0.040f * L, 0.085f * L, 0.045f * L), pitch: -22f, splay: 45f, uv: UV_HEAD, noOutline: true);
                        // 臀部下垂毛尾
                        m.Add(Prim.Cone, tailBase + new Vector3(0f, 0.02f * L, 0.01f * L),
                            new Vector3(0.065f * L, 0.14f * L, 0.065f * L), pitch: -72f, uv: UV_COAT);
                    }
                    else if (bison)
                    {
                        // 美洲野牛：前倾圆润牛头(无长管吻) + 下颌微收 + 两侧低位黑眼 + 外展上弯的橄榄米黄牛角(UV_DETAIL)
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.05f, bl * 0.43f),
                            new Vector3(hr * 0.88f, bh * 0.66f, hr * 0.84f), pitch: 18f, uv: UV_HEAD);
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.02f, bl * 0.49f),
                            new Vector3(hr * 0.46f, bh * 0.34f, hr * 0.52f), pitch: 18f, noOutline: true, uv: UV_BELLY);
                        AddSymX(m, Prim.Sphere, hr * 0.38f, bh * 0.27f, bl * 0.535f,
                            new Vector3(0.066f * L, 0.068f * L, 0.066f * L), uv: UV_EYE, noOutline: true);
                        var hL = new Vector3(-hr * 0.32f, bh * 0.42f, bl * 0.45f);
                        var hR = new Vector3( hr * 0.32f, bh * 0.42f, bl * 0.45f);
                        AddSymSeg(m, ref hL, ref hR, 0.105f * L, 0.068f * L, 0.048f * L, 34f, 68f, 0f, UV_DETAIL);
                        AddSymSeg(m, ref hL, ref hR, 0.095f * L, 0.048f * L, 0.020f * L, -16f, -14f, 0f, UV_DETAIL);
                    }
                    else
                    {
                        BuildUngulateHead(m, headC, hr, hr * 0.70f, hr * 0.56f, hr * 0.50f, UV_HEAD, UV_LIMB, UV_LIMB, pitch: 120f);
                        BuildEars(m, headC, hr, 0.048f * L, 0.10f * L, floppy: sheep, earUv: UV_LIMB);
                        if (muskox)
                            m.Add(Prim.Sphere, headC + new Vector3(0f, hr * 0.08f, -hr * 0.02f),
                                new Vector3(hr * 1.12f, hr * 0.76f, hr * 0.96f), noOutline: true, uv: UV_DETAIL);

                        // 物种弯角
                        var hL = headC + new Vector3(-hr * 0.32f, hr * 0.24f, 0f);
                        var hR = headC + new Vector3(hr * 0.32f, hr * 0.24f, 0f);
                        if (sheep)
                        {
                            AddSymSeg(m, ref hL, ref hR, 0.10f * L, 0.070f * L, 0.055f * L, -20f, 65f, 0f, UV_DETAIL);
                            AddSymSeg(m, ref hL, ref hR, 0.10f * L, 0.055f * L, 0.030f * L, 55f, 35f, 0f, UV_DETAIL);
                        }
                        else if (muskox)
                        {
                            AddSymSeg(m, ref hL, ref hR, 0.13f * L, 0.090f * L, 0.065f * L, 25f, 115f, 0f, UV_DETAIL);
                            AddSymSeg(m, ref hL, ref hR, 0.12f * L, 0.065f * L, 0.025f * L, 35f, 65f, 0f, UV_DETAIL);
                        }
                        else
                        {
                            float hl = (yak ? 0.15f : 0.11f) * L;
                            AddSymSeg(m, ref hL, ref hR, hl, 0.070f * L, 0.050f * L, 12f, 66f, 0f, UV_DETAIL);
                            AddSymSeg(m, ref hL, ref hR, hl, 0.050f * L, 0.018f * L, 6f, 20f, 0f, UV_DETAIL);
                        }
                    }
                    break;
                }

                case Arch.SacUngulate:
                {
                    // 爆炸羊 (boomalope) / 毒气羊 (toxalope)：贴地无腿躯干 + 彼此分明带独立描边的发光化学囊泡群
                    float bl = 0.72f * L, bh = 0.40f * L;
                    float bw = CalibrateWidth(n, bl, 0.38f * L);
                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.09f * L, 0.02f * L, 0.92f,
                        out var chestTop, out var tailBase, UV_COAT, UV_COAT, UV_BELLY);
                    bodyCenterY = bh * 0.55f;
                    bodyHalfW = bw * 0.60f;

                    // 分离排布且带独立黑描边的化学囊泡：1) 背顶主大囊 2) 尾端后凸囊 3) 两侧后腰大囊 4) 两侧前肩小囊
                    m.Add(Prim.Sphere, new Vector3(0f, bh * 0.56f, 0.02f * L),
                        new Vector3(0.33f * L, 0.33f * L, 0.37f * L), uv: UV_DETAIL);
                    m.Add(Prim.Sphere, new Vector3(0f, bh * 0.30f, -0.35f * L),
                        new Vector3(0.24f * L, 0.26f * L, 0.25f * L), uv: UV_DETAIL);
                    AddSymX(m, Prim.Sphere, bw * 0.38f, bh * 0.22f, -0.11f * L,
                        new Vector3(0.21f * L, 0.25f * L, 0.26f * L), uv: UV_DETAIL);
                    AddSymX(m, Prim.Sphere, bw * 0.36f, bh * 0.20f, 0.16f * L,
                        new Vector3(0.15f * L, 0.17f * L, 0.17f * L), uv: UV_DETAIL);

                    // 前伸微昂颈部(noOutline=true 消除颈根黑环) + 紧凑下倾楔形头(pitch=126° 垂吻)
                    var np = AddSeg(m, chestTop + new Vector3(0f, -0.06f * L, 0.02f * L),
                        0.18f * L, 0.13f * L, 0.10f * L, 44f, uv: UV_COAT, noOutline: true);
                    float hr = 0.165f * L;
                    var headC = np + new Vector3(0f, 0.01f * L, 0.02f * L);
                    BuildUngulateHead(m, headC, hr, 0.14f * L, 0.085f * L, 0.085f * L, UV_HEAD, UV_HEAD, UV_BELLY, pitch: 126f);
                    BuildEars(m, headC, hr, 0.040f * L, 0.095f * L, earUv: UV_LIMB);
                    break;
                }

                case Arch.Camelid:
                {
                    bool camel = Has(n, "dromedary", "camel");
                    float bl = 0.72f * L, bh = (camel ? 0.42f : 0.38f) * L;
                    float bw = CalibrateWidth(n, bl, (camel ? 0.35f : 0.31f) * L);
                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.08f * L, 0.02f * L, 0.92f,
                        out var chestTop, out var tailBase, camel ? UV_LIMB : UV_COAT, camel ? UV_LIMB : UV_COAT, UV_BELLY);
                    bodyCenterY = bh * 0.55f;
                    bodyHalfW = bw * 0.5f;

                    if (camel)
                    {
                        // 单峰驼：圆润中背驼峰(_south 投影高于头顶、_north 露出头顶双耳) + 无黑圈 U 形曲颈 + 温和垂吻骆驼头(pitch=122°) + 贴臀垂尾
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.42f, -0.05f * L),
                            new Vector3(bw * 0.80f, 0.32f * L, 0.38f * L), uv: UV_COAT);
                        var np = chestTop + new Vector3(0f, -0.12f * L, 0.02f * L);
                        np = AddSeg(m, np, 0.16f * L, 0.16f * L, 0.14f * L, 66f, uv: UV_COAT, noOutline: true);
                        var hp = AddSeg(m, np, 0.19f * L, 0.14f * L, 0.12f * L, 14f, uv: UV_COAT, noOutline: true);
                        float hr = 0.185f * L;
                        var headC = hp + new Vector3(0f, 0.01f * L, 0.02f * L);
                        BuildUngulateHead(m, headC, hr, 0.15f * L, 0.095f * L, 0.095f * L, UV_COAT, UV_HEAD, UV_BELLY, pitch: 122f);
                        AddSymX(m, Prim.Sphere, hr * 0.44f, headC.y + hr * 0.16f, headC.z - hr * 0.06f,
                            new Vector3(0.050f * L, 0.078f * L, 0.042f * L), splay: 26f, uv: UV_COAT);
                        var tp = AddSeg(m, tailBase + new Vector3(0f, -0.02f * L, 0.02f * L),
                            0.14f * L, 0.036f * L, 0.024f * L, -168f, uv: UV_LIMB, joint: false);
                        m.Add(Prim.Sphere, tp - new Vector3(0f, 0.03f * L, 0f),
                            new Vector3(0.048f * L, 0.065f * L, 0.048f * L), uv: UV_COAT);
                    }
                    else
                    {
                        // 羊驼：深植前胸的一体式修长竖颈(无横切黑圈) + 浅驼色温和垂吻(pitch=120°) + 微外展立耳 + 贴臀下垂短尾
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.46f, chestTop.z + 0.02f * L),
                            new Vector3(0.155f * L, 0.39f * L, 0.165f * L), pitch: 10f, uv: UV_COAT);
                        float hr = 0.19f * L;
                        var headC = new Vector3(0f, bh * 0.46f + 0.36f * L, chestTop.z + 0.08f * L);
                        BuildUngulateHead(m, headC, hr, 0.125f * L, 0.086f * L, 0.086f * L, UV_COAT, UV_HEAD, UV_BELLY, pitch: 120f);
                        m.Add(Prim.Sphere, headC + new Vector3(0f, hr * 0.08f, -0.01f * L),
                            new Vector3(hr * 0.94f, hr * 0.56f, hr * 0.90f), noOutline: true, uv: UV_COAT);
                        AddSymX(m, Prim.Cone, hr * 0.34f, headC.y + hr * 0.22f, headC.z - hr * 0.04f,
                            new Vector3(0.046f * L, 0.115f * L, 0.042f * L), pitch: -6f, splay: 16f, taper: 0.22f, uv: UV_COAT);
                        AddSeg(m, tailBase + new Vector3(0f, -0.02f * L, 0.03f * L),
                            0.13f * L, 0.050f * L, 0.024f * L, -162f, uv: UV_COAT, joint: false);
                    }
                    break;
                }

                case Arch.Pachyderm:
                {
                    bool ele = Has(n, "elephant", "mastodon");
                    bool mastodon = Has(n, "mastodon");
                    bool rhino = Has(n, "rhino");
                    float bl = 0.78f * L, bh = 0.52f * L;
                    float bw = CalibrateWidth(n, bl, (rhino ? 0.56f : 0.50f) * L);
                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.14f * L, (rhino ? 0.03f : 0.01f) * L, 0.94f,
                        out var chestTop, out var tailBase, UV_LIMB, UV_LIMB, UV_BELLY);
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.52f;

                    float hr = (rhino ? 0.25f : 0.30f) * L;
                    var headC = new Vector3(0f, bh * (rhino ? 0.50f : 0.62f), bl * (ele ? 0.46f : 0.43f));

                    if (ele)
                    {
                        m.Add(Prim.Sphere, headC - new Vector3(0f, hr * 0.44f, 0f),
                            new Vector3(hr * 0.96f, hr * 1.05f, hr * 0.98f), pitch: 8f, uv: UV_HEAD);
                        if (mastodon)
                            m.Add(Prim.Sphere, headC + new Vector3(0f, hr * 0.14f, 0f),
                                new Vector3(hr * 1.04f, hr * 0.64f, hr * 0.94f), noOutline: true, uv: UV_COAT);

                        // 原版特征性的下垂贴颊宽薄大扇耳 (薄如扇叶，向下垂挂贴颊，上沿紧贴头顶，彻底消除朝天竖起兔耳翅膀感)
                        AddSymX(m, Prim.Sphere, bw * 0.42f, headC.y - hr * 0.28f, headC.z - hr * 0.08f,
                            new Vector3(0.030f * L, 0.24f * L, 0.28f * L), yaw: 16f, pitch: -4f, splay: -2f, uv: UV_HEAD);
                        AddSymX(m, Prim.Sphere, hr * 0.31f, headC.y - hr * 0.02f, headC.z + hr * 0.32f,
                            new Vector3(0.05f * L, 0.055f * L, 0.055f * L), uv: UV_EYE, noOutline: true);

                        // 自然向下垂挂并在末端向前向上强烈回卷勾起的长象鼻 (Curled Hook)
                        var trk = headC + new Vector3(0f, -hr * 0.06f, hr * 0.38f);
                        trk = AddSeg(m, trk, 0.14f * L, 0.130f * L, 0.105f * L, 130f, uv: UV_HEAD, joint: true);
                        trk = AddSeg(m, trk, 0.13f * L, 0.105f * L, 0.082f * L, 165f, uv: UV_HEAD, joint: true);
                        trk = AddSeg(m, trk, 0.11f * L, 0.082f * L, 0.060f * L, 82f, uv: UV_HEAD, joint: true);
                        AddSeg(m, trk, 0.09f * L, 0.060f * L, 0.038f * L, 18f, uv: UV_HEAD, joint: true);

                        // 两根向前下方自然伸出微翘的洁白巨型象牙
                        var tL = headC + new Vector3(-hr * 0.26f, -hr * 0.22f, hr * 0.32f);
                        var tR = headC + new Vector3( hr * 0.26f, -hr * 0.22f, hr * 0.32f);
                        float tl = (mastodon ? 0.22f : 0.17f) * L;
                        AddSymSeg(m, ref tL, ref tR, tl, 0.054f * L, 0.036f * L, 108f, 14f, 0f, UV_DETAIL, joint: true);
                        AddSymSeg(m, ref tL, ref tR, tl * 0.85f, 0.036f * L, 0.012f * L, 68f, 8f, 0f, UV_DETAIL, joint: false);
                    }
                    else if (rhino)
                    {
                        // 犀牛：宽阔敦实的双侧重甲肩胁 + 紧凑低垂锥头(pitch=112°) + 整根无接缝鼻端大独角(UV_DETAIL) + 外展圆耳
                        AddSymX(m, Prim.Sphere, bw * 0.24f, bh * 0.04f, 0.02f * L,
                            new Vector3(bw * 0.68f, bh * 0.86f, bl * 0.78f), uv: UV_COAT, noOutline: true);
                        BuildUngulateHead(m, headC, hr, 0.13f * L, 0.14f * L, 0.13f * L, UV_HEAD, UV_HEAD, UV_LIMB, pitch: 112f);
                        AddSymX(m, Prim.Sphere, hr * 0.44f, headC.y + hr * 0.22f, headC.z - hr * 0.06f,
                            new Vector3(0.075f * L, 0.095f * L, 0.055f * L), yaw: 18f, splay: 24f, uv: UV_HEAD);
                        var h1 = headC + new Vector3(0f, -hr * 0.28f, hr * 0.48f);
                        AddSeg(m, h1, 0.24f * L, 0.115f * L, 0.016f * L, 16f, uv: UV_DETAIL, joint: false);
                    }
                    else
                    {
                        // 河马：宽阔低矮躯干 + 超大方形宽吻 + 凸起鼻孔与眼座
                        BuildHead(m, headC - new Vector3(0f, 0.04f * L, 0f), hr, hr * 1.05f, hr * 0.96f, hr * 0.74f, UV_HEAD, UV_BELLY, UV_BELLY, hasNose: false);
                        AddSymX(m, Prim.Sphere, hr * 0.24f, headC.y - hr * 0.04f, headC.z + hr * 0.68f,
                            new Vector3(0.07f * L, 0.06f * L, 0.08f * L), uv: UV_EYE, noOutline: true);
                        BuildEars(m, headC, hr, 0.045f * L, 0.08f * L, roundEar: true, earUv: UV_LIMB);
                    }

                    AddSeg(m, tailBase, 0.15f * L, 0.042f * L, 0.016f * L, -162f, uv: UV_LIMB, joint: false);
                    break;
                }

                case Arch.CervidEquine:
                {
                    bool equine = Has(n, "horse", "donkey");
                    bool donkey = Has(n, "donkey");
                    bool moose = Has(n, "moose");
                    bool antler = Has(n, "deer", "elk", "caribou", "moose");
                    bool ibex = Has(n, "ibex");
                    bool gazelle = Has(n, "gazelle", "antelope");
                    bool goat = Has(n, "goat");
                    bool bovine = Has(n, "cow", "bull");

                    if (equine)
                    {
                        // 马 (Horse) 专属高保真解剖建模：1:1 严格对照原版 2D 贴图像素工程蓝图 (东侧视/南正视/北后视)
                        // 彻底根除审查官全部视觉硬伤：
                        // 1. 紧凑修长躯干与真马鞍背流线：克制躯干宽度(bw=0.200L，彻底根除圆球巨臀)，基准流线主躯干贯穿前后，前驱深胸鬐甲(顶0.380L/底0.008L贴地)与后驱高臀(顶0.355L/底0.070L高收起)在马鞍处平滑相交于自然下凹脊线(顶0.330L下凹/腹底单向向上收起)，彻底消除毛毛虫多节突起与多余垂肉
                        // 2. 45°斜挺挺拔长颈与胸颈顺滑肌肉过渡：双段斜长颈(48°->40°)长0.34L向前上方挺拔伸展，肩胸处肌肉平顺包裹，彻底消除圆柱直插生硬折角
                        // 马 (Horse) 专属高保真解剖建模：1:1 严格对照原版 2D 贴图像素工程蓝图 (东侧视/南正视/北后视)
                        // 彻底根除全部历史审查硬伤：
                        // 1. 基准流线主躯干：采用 buildQuadBody 黄金解剖，鬐甲顶高0.48L > 臀峰0.41L > 鞍部0.36L，平缓优美微凹弧线，深胸饱满接地0.00L，完全消除肉瘤驼峰
                        // 2. 强壮挺拔厚实马颈：粗壮斜长马颈(胸端0.22L, 头端0.16L)，自鬐甲与前胸向上前方38°挺拔直贯颅顶，胸颈扇形肌肉无缝平滑衔接
                        // 3. 高还原原版Q版马首：适度长面(长吻0.12L，头长0.18L，拒绝食蚁兽管吻)，饱满颅骨与外展下颌腮骨，圆钝吻端平齐马鞍0.36L，黑眼与挺拔俏耳(高0.095L)
                        // 4. 沿颈背高耸连贯深色马鬃：采用 UV_DETAIL 深栗色，与马颈同角度平行紧贴颈脊，自鬐甲直达后脑枕骨与额前刘海，两端完全贴合零悬空
                        // 5. 贴臀自然下垂长马尾：采用 UV_DETAIL，根部锚定于后臀外缘(-0.31L, 0.28L)，三段自然垂直垂落至离地0.02L，在北后视中形成居中悬垂的饱满立体长羽

                        float bw = 0.32f * L;

                        // 1. 基准流线马身：全长流线基底主椭球 (长0.66L，充填中段马鞍最低点0.36L，腹底收腰0.04L，保证单一流畅收腹弧线)
                        m.Add(Prim.Sphere, new Vector3(0f, 0.02f * L, -0.10f * L), new Vector3(bw * 0.94f, 0.34f * L, 0.66f * L), uv: UV_COAT, noOutline: false);

                        // 前驱深胸与挺拔鬐甲 (顶高0.46L，胸底0.00L 接地，长0.38L，向前顺滑承托马颈脊线)
                        m.Add(Prim.Sphere, new Vector3(0f, 0.00f * L, 0.07f * L), new Vector3(bw * 0.98f, 0.46f * L, 0.38f * L), pitch: 10f, uv: UV_COAT, noOutline: false);

                        // 后驱饱满圆臀 (顶高0.415L 呈现自然微翘臀峰，底高0.01L，长0.40L，后弧达-0.42L，与尾根无缝咬合)
                        m.Add(Prim.Sphere, new Vector3(0f, 0.01f * L, -0.24f * L), new Vector3(bw * 0.96f, 0.405f * L, 0.40f * L), pitch: -8f, uv: UV_COAT, noOutline: false);

                        // 正视/后视丰满肩胛轮廓 (充盈体量，noOutline避免侧身产生多余轮廓环)
                        AddSymX(m, Prim.Sphere, bw * 0.22f, 0.04f * L, 0.06f * L, new Vector3(bw * 0.44f, 0.38f * L, 0.28f * L), pitch: 10f, uv: UV_COAT, noOutline: true);
                        // 后臀两侧丰满后腿肌肉
                        AddSymX(m, Prim.Sphere, bw * 0.20f, 0.03f * L, -0.24f * L, new Vector3(bw * 0.42f, 0.36f * L, 0.28f * L), pitch: -8f, uv: UV_COAT, noOutline: true);

                        // 2. 粗壮斜长厚实马颈 (自鬐甲脊顶 0.46L 与前胸斜向前上方 48° 挺拔舒展直达颅顶，单调平滑递增，彻底根除驼峰断层)
                        var neckBase = new Vector3(0f, 0.26f * L, 0.08f * L);
                        float neckLen = 0.20f * L;
                        float neckPitch = 48f;
                        m.Add(Prim.Cylinder, neckBase, new Vector3(0.18f * L, neckLen, 0.22f * L), pitch: neckPitch, taper: 0.74f, uv: UV_COAT, noOutline: false);
                        // 前胸与颈下肌肉充盈过渡
                        m.Add(Prim.Sphere, new Vector3(0f, 0.16f * L, 0.12f * L), new Vector3(bw * 0.86f, 0.28f * L, 0.24f * L), pitch: 28f, uv: UV_COAT, noOutline: false);

                        // 3. 高保真原版Q版马首 (比例严格控制在头长0.18L，后脑+腮骨+斜垂长吻124°，吻端高0.35L平齐马鞍)
                        var neckTop = neckBase + StepVec(neckLen * 0.88f, neckPitch);
                        var headC = neckTop + new Vector3(0f, 0.00f * L, 0.01f * L);

                        // 饱满后脑与颅骨 (Braincase & Poll，高0.165L，宽0.125L)
                        m.Add(Prim.Sphere, new Vector3(headC.x, headC.y - 0.015f * L, headC.z), new Vector3(0.125f * L, 0.165f * L, 0.135f * L), pitch: 16f, uv: UV_HEAD, noOutline: false);

                        // 宽阔外展下颌腮骨 (Mandible Cheek Flares)
                        AddSymX(m, Prim.Sphere, 0.045f * L, headC.y - 0.015f * L, headC.z - 0.01f * L, new Vector3(0.035f * L, 0.080f * L, 0.070f * L), pitch: 16f, uv: UV_HEAD, noOutline: true);

                        // 顺滑斜垂长吻桥 (Nasal Bridge: 前倾斜垂 124°，长0.120L)
                        var bridgeBase = new Vector3(headC.x, headC.y + 0.005f * L, headC.z + 0.02f * L);
                        m.Add(Prim.Cylinder, bridgeBase, new Vector3(0.082f * L, 0.120f * L, 0.092f * L), pitch: 124f, taper: 0.74f, uv: UV_HEAD, noOutline: false);

                        // 圆钝方圆吻端与鼻孔区 (Muzzle Tip: 吻端高 0.35L 平齐马鞍)
                        var noseC = bridgeBase + StepVec(0.110f * L, 124f);
                        m.Add(Prim.Sphere, new Vector3(noseC.x, noseC.y - 0.020f * L, noseC.z), new Vector3(0.075f * L, 0.070f * L, 0.075f * L), pitch: 18f, uv: UV_HEAD, noOutline: false);
                        // 下唇/分色下巴 (Chin，高 0.28L)
                        m.Add(Prim.Sphere, new Vector3(noseC.x, noseC.y - 0.040f * L, noseC.z - 0.01f * L), new Vector3(0.066f * L, 0.042f * L, 0.066f * L), pitch: 18f, uv: UV_BELLY, noOutline: true);

                        // 灵动黑眼睛 (侧面外凸，位于额骨与腮骨上方)
                        AddSymX(m, Prim.Sphere, 0.050f * L, headC.y + 0.015f * L, headC.z + 0.028f * L, new Vector3(0.024f * L, 0.024f * L, 0.024f * L), pitch: 16f, uv: UV_EYE, noOutline: false);

                        // 显著挺拔秀美马耳 (高0.095L，向后上方挺立俏立)
                        AddSymX(m, Prim.Cone, 0.036f * L, headC.y + 0.075f * L, headC.z - 0.015f * L,
                            new Vector3(0.026f * L, (donkey ? 0.15f : 0.095f) * L, 0.024f * L),
                            pitch: -16f, splay: donkey ? 22f : 12f, taper: 0.18f, uv: UV_HEAD, noOutline: false);

                        // 4. 沿颈背高耸连贯深色马鬃 (紧贴颈脊脊线，自鬐甲直达颅顶后枕骨与额前刘海，两端完全贴合零悬空)
                        var mane0 = new Vector3(0f, 0.44f * L, 0.07f * L);
                        var maneTop = new Vector3(0f, headC.y + 0.080f * L, headC.z - 0.012f * L);
                        float my = maneTop.y - mane0.y, mz = maneTop.z - mane0.z;
                        float mlen = Mathf.Sqrt(my * my + mz * mz);
                        float mpitch = Mathf.Atan2(mz, my) * 180f / Mathf.PI;

                        m.Add(Prim.Cylinder, mane0, new Vector3(0.052f * L, mlen, 0.072f * L), pitch: mpitch, taper: 0.88f, uv: UV_DETAIL, noOutline: false);
                        m.Add(Prim.Sphere, mane0, new Vector3(0.052f * L, 0.052f * L, 0.072f * L), uv: UV_DETAIL, noOutline: false);
                        m.Add(Prim.Sphere, maneTop, new Vector3(0.046f * L, 0.046f * L, 0.058f * L), uv: UV_DETAIL, noOutline: false);

                        // 额顶刘海 (Forelock)
                        m.Add(Prim.Sphere, new Vector3(0f, headC.y + 0.070f * L, headC.z + 0.022f * L), new Vector3(0.040f * L, 0.038f * L, 0.046f * L), pitch: 16f, uv: UV_DETAIL, noOutline: false);

                        // 5. 贴臀自然垂直下垂长马尾 (锚定于后臀后弧外缘-0.42L，垂直下垂至离地0.08L，在北后视中清晰居中悬垂)
                        var tailRoot = new Vector3(0f, 0.30f * L, -0.42f * L);
                        var t1 = AddSeg(m, tailRoot, 0.09f * L, 0.065f * L, 0.052f * L, -165f, uv: UV_DETAIL, joint: false, noOutline: false);
                        var t2 = AddSeg(m, t1, 0.08f * L, 0.052f * L, 0.032f * L, -175f, uv: UV_DETAIL, joint: false, noOutline: false);
                        AddSeg(m, t2, 0.05f * L, 0.032f * L, 0.014f * L, -180f, uv: UV_EYE, joint: false, noOutline: false);
                        break;
                    }
                    else
                    {
                        float bl = (bovine ? 0.76f : 0.70f) * L;
                        float bh = (bovine ? 0.42f : moose ? 0.38f : 0.38f) * L;
                        float bw = CalibrateWidth(n, bl, (bovine ? 0.42f : 0.31f) * L);

                        BuildQuadBody(m, L, bl, bh, bw, 0f,
                            (bovine ? 0.09f : 0.07f) * L,
                            (bovine ? 0.01f : 0.03f) * L,
                            bovine ? 0.96f : moose ? 0.93f : 0.95f,
                            out var chestTop, out var tailBase, UV_LIMB, bovine ? UV_COAT : UV_LIMB, ibex ? UV_LIMB : UV_BELLY);
                        bodyCenterY = bh * 0.5f;
                        bodyHalfW = bw * 0.5f;

                        // 颈部前倾角与挺拔昂首
                        float neckPitch = bovine ? 46f : moose ? 52f : 36f;
                        float nd0 = (bovine ? 0.19f : 0.155f) * L;
                        float nd1 = (bovine ? 0.165f : 0.135f) * L;
                        var np = chestTop + new Vector3(0f, -0.02f * L, 0.01f * L);
                        np = AddSeg(m, np, (bovine ? 0.13f : 0.16f) * L, nd0, nd1, neckPitch, uv: UV_COAT, noOutline: true);
                        var hp = AddSeg(m, np, (bovine ? 0.10f : 0.14f) * L, nd1, nd1 * 0.85f, neckPitch - 6f, uv: UV_COAT, noOutline: true);

                        // 有蹄类专属长脸头
                        float hr = (bovine ? 0.22f : 0.195f) * L;
                        var headC = hp + new Vector3(0f, 0.01f * L, 0.02f * L);
                        BuildUngulateHead(m, headC, hr,
                            (moose ? 0.18f : bovine ? 0.16f : 0.16f) * L,
                            (bovine ? 0.115f : moose ? 0.115f : 0.095f) * L,
                            (moose ? 0.12f : bovine ? 0.11f : 0.10f) * L,
                            UV_HEAD, bovine ? UV_LIMB : UV_HEAD, UV_BELLY, pitch: moose ? 116f : bovine ? 122f : 118f);

                        if (bovine)
                        {
                            // 家牛：位于牛角后下方的水平侧展牛耳（与前弯牛角清晰错开）
                        AddSymX(m, Prim.Cone, hr * 0.40f, headC.y + hr * 0.04f, headC.z - hr * 0.10f,
                            new Vector3(0.046f * L, 0.105f * L, 0.040f * L), pitch: -28f, splay: 74f, taper: 0.25f, uv: UV_HEAD);
                    }
                    else if (antler && !moose)
                    {
                        m.Add(Prim.Sphere, hp + new Vector3(0f, -0.08f * L, 0.045f * L),
                            new Vector3(0.085f * L, 0.11f * L, 0.075f * L), pitch: 28f, noOutline: true, uv: UV_BELLY);
                        AddSymX(m, Prim.Cone, hr * 0.38f, headC.y + hr * 0.08f, headC.z - hr * 0.06f,
                            new Vector3(0.044f * L, 0.115f * L, 0.038f * L), pitch: -14f, splay: 58f, taper: 0.22f, uv: UV_LIMB);
                    }
                    else if (ibex || goat)
                    {
                        AddSymX(m, Prim.Cone, hr * 0.38f, headC.y + hr * 0.06f, headC.z - hr * 0.04f,
                            new Vector3(0.044f * L, 0.11f * L, 0.038f * L), pitch: -14f, splay: 58f, taper: 0.22f, uv: UV_LIMB);
                    }
                    else
                    {
                        BuildEars(m, headC, hr, 0.046f * L, (donkey ? 0.18f : bovine ? 0.10f : 0.11f) * L, floppy: false, earUv: UV_LIMB);
                    }

                    if (goat || ibex)
                        m.Add(Prim.Cone, headC + new Vector3(0f, -hr * 0.36f, hr * 0.42f),
                            new Vector3(0.048f * L, 0.12f * L, 0.052f * L), pitch: 172f, taper: 0.18f, uv: UV_LIMB);

                    // 鹿角 / 弯刀角 / 牛角（全部采用浅米白/角质色 UV_DETAIL）
                    var hL = headC + new Vector3(-hr * 0.28f, hr * 0.24f, -hr * 0.04f);
                    var hR = headC + new Vector3(hr * 0.28f, hr * 0.24f, -hr * 0.04f);
                    if (moose)
                    {
                        // 驼鹿：后掠外展掌状主梁 + 无内部黑线掌蹼(noOutline=true) + 扇形三枚圆润掌叉
                        var p0L = hL; var p0R = hR;
                        AddSymSeg(m, ref hL, ref hR, 0.13f * L, 0.052f * L, 0.064f * L, -34f, 56f, 0f, UV_DETAIL, joint: false);
                        var browL = Vector3.Lerp(p0L, hL, 0.65f);
                        var browR = Vector3.Lerp(p0R, hR, 0.65f);
                        AddSymSeg(m, ref browL, ref browR, 0.09f * L, 0.040f * L, 0.020f * L, 38f, 14f, 0f, UV_DETAIL, joint: false);

                        var p1L = hL; var p1R = hR;
                        AddSymSeg(m, ref hL, ref hR, 0.17f * L, 0.070f * L, 0.052f * L, -48f, 24f, 0f, UV_DETAIL, joint: true);
                        for (int t = 0; t < 3; t++)
                        {
                            float frac = 0.18f + t * 0.40f;
                            var tL = Vector3.Lerp(p1L, hL, frac);
                            var tR = Vector3.Lerp(p1R, hR, frac);
                            AddSymSeg(m, ref tL, ref tR, (0.12f - t * 0.01f) * L, 0.052f * L, 0.024f * L,
                                -10f - t * 13f, 8f + t * 12f, 0f, UV_DETAIL, joint: false);
                        }
                    }
                    else if (antler)
                    {
                        // 弧形琴状分叉鹿角：第一段向两侧外展后掠，第二段向上向内弯拢，配前眉叉与内上顶叉
                        var bL = hL; var bR = hR;
                        AddSymSeg(m, ref hL, ref hR, 0.14f * L, 0.044f * L, 0.036f * L, -28f, 58f, 0f, UV_DETAIL);
                        var fL = Vector3.Lerp(bL, hL, 0.60f);
                        var fR = Vector3.Lerp(bR, hR, 0.60f);
                        AddSymSeg(m, ref fL, ref fR, 0.095f * L, 0.032f * L, 0.014f * L, 34f, 12f, 0f, UV_DETAIL, joint: false);
                        var mL = hL; var mR = hR;
                        AddSymSeg(m, ref hL, ref hR, 0.15f * L, 0.036f * L, 0.016f * L, -12f, -14f, 0f, UV_DETAIL);
                        AddSymSeg(m, ref mL, ref mR, 0.11f * L, 0.030f * L, 0.014f * L, 16f, -34f, 0f, UV_DETAIL, joint: false);
                    }
                    else if (ibex)
                    {
                        // 羱羊标志性五段大弧度半圆弯刀角（_east 呈巨型 C 形圆拱，_south/_north 向两侧外展再向下弯垂）
                        hL = headC + new Vector3(-hr * 0.22f, hr * 0.26f, hr * 0.04f);
                        hR = headC + new Vector3( hr * 0.22f, hr * 0.26f, hr * 0.04f);
                        AddSymSeg(m, ref hL, ref hR, 0.15f * L, 0.068f * L, 0.060f * L, 10f, 20f, 0f, UV_DETAIL);
                        AddSymSeg(m, ref hL, ref hR, 0.17f * L, 0.060f * L, 0.052f * L, -42f, 24f, 0f, UV_DETAIL);
                        AddSymSeg(m, ref hL, ref hR, 0.17f * L, 0.052f * L, 0.042f * L, -96f, 16f, 0f, UV_DETAIL);
                        AddSymSeg(m, ref hL, ref hR, 0.16f * L, 0.042f * L, 0.028f * L, -148f, 4f, 0f, UV_DETAIL);
                        AddSymSeg(m, ref hL, ref hR, 0.12f * L, 0.028f * L, 0.014f * L, -192f, -8f, 0f, UV_DETAIL);
                    }
                    else if (gazelle || goat || bovine)
                    {
                        if (bovine)
                        {
                            hL = headC + new Vector3(-hr * 0.30f, hr * 0.22f, hr * 0.08f);
                            hR = headC + new Vector3( hr * 0.30f, hr * 0.22f, hr * 0.08f);
                            AddSymSeg(m, ref hL, ref hR, 0.095f * L, 0.046f * L, 0.032f * L, 28f, 64f, 0f, UV_DETAIL);
                            AddSymSeg(m, ref hL, ref hR, 0.085f * L, 0.032f * L, 0.014f * L, 8f, 16f, 0f, UV_DETAIL);
                        }
                        else
                        {
                            float hl = (gazelle ? 0.16f : 0.11f) * L;
                            AddSymSeg(m, ref hL, ref hR, hl, 0.042f * L, 0.028f * L, -36f, 22f, 0f, UV_DETAIL);
                            AddSymSeg(m, ref hL, ref hR, hl * 0.85f, 0.028f * L, 0.012f * L, -52f, 9f, 0f, UV_DETAIL);
                        }
                    }

                    // 尾部（鹿白尾簇 / 驼鹿与牛贴臀垂尾）
                    if (bovine)
                    {
                        AddSeg(m, tailBase + new Vector3(0f, 0.01f * L, 0.02f * L), 0.17f * L, 0.044f * L, 0.020f * L, -160f, uv: UV_COAT, joint: false);
                    }
                    else if (moose)
                    {
                        AddSeg(m, tailBase + new Vector3(0f, -0.02f * L, 0.04f * L), 0.11f * L, 0.048f * L, 0.026f * L, -172f, uv: UV_COAT, joint: false);
                    }
                    else
                    {
                        m.Add(Prim.Cone, tailBase + new Vector3(0f, -0.01f * L, 0.02f * L), new Vector3(0.070f * L, 0.14f * L, 0.065f * L),
                            pitch: goat ? -35f : -164f, uv: antler ? UV_BELLY : UV_COAT);
                    }
                    break;
                    }
                }

                case Arch.UrsineSloth:
                {
                    bool panda = Has(n, "panda");
                    bool sloth = Has(n, "megasloth", "sloth");
                    bool badger = Has(n, "badger", "wolverine");
                    bool bear = !panda && !sloth && !badger;

                    float bl = (badger ? 0.68f : 0.76f) * L;
                    float bh = (badger ? 0.36f : sloth ? 0.50f : 0.42f) * L;
                    float bw = CalibrateWidth(n, bl, (badger ? 0.38f : 0.48f) * L);

                    int frontRegion = panda ? UV_LIMB : sloth ? UV_BELLY : UV_LIMB;
                    int rearRegion = panda ? UV_COAT : UV_LIMB;
                    int bellyRegion = panda ? UV_COAT : sloth ? UV_LIMB : UV_BELLY;
                    // 灰熊：浑厚强健滚木体魄，前肩耸起顺滑融入背脊，后臀低收 (rumpScale: 0.82)
                    BuildQuadBody(m, L, bl, bh, bw, 0f, (badger ? 0.09f : 0.12f) * L,
                        (sloth ? 0.04f : bear ? 0.08f : 0.03f) * L, sloth ? 0.90f : bear ? 0.82f : 0.98f, out var chestTop, out var tailBase, frontRegion, rearRegion, bellyRegion);
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.52f;

                    if (bear)
                    {
                        // 沉甸甸的低垂厚腹 (Sagging Belly)，自然融入圆润滚木体躯
                        m.Add(Prim.Sphere, new Vector3(0f, 0.005f * L, -0.02f * L),
                            new Vector3(bw * 0.96f, bh * 0.55f, bl * 0.72f), uv: UV_BELLY, noOutline: true);
                    }
                    else if (panda)
                    {
                        // 熊猫：后背纯白拱顶(_south 露出白背顶) + 后臀底部黑色下摆(_east/_north 底部黑腿块)
                        m.Add(Prim.Sphere, new Vector3(0f, 0.02f * L, -bl * 0.14f),
                            new Vector3(bw * 1.00f, bh * 1.06f, bl * 0.62f), noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -bl * 0.25f),
                            new Vector3(bw * 1.06f, bh * 0.46f, bl * 0.46f), noOutline: true, uv: UV_LIMB);
                    }
                    else if (sloth)
                    {
                        // 大地懒：前肩高耸蓬松米白毛领(UV_BELLY) + 肩峰后掠毛尖(noOutline=true) + 前胸底端深褐裙边(UV_LIMB)
                        m.Add(Prim.Sphere, new Vector3(0f, 0.02f * L, -bl * 0.12f),
                            new Vector3(bw * 1.01f, bh * 0.94f, bl * 0.74f), noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.08f * L, bl * 0.20f),
                            new Vector3(bw * 1.06f, bh * 0.96f, bl * 0.52f), noOutline: true, uv: UV_BELLY);
                        AddSymX(m, Prim.Sphere, bw * 0.16f, bh * 0.34f, bl * 0.16f,
                            new Vector3(bw * 0.82f, bh * 0.64f, bl * 0.44f), noOutline: true, uv: UV_BELLY);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, bl * 0.20f),
                            new Vector3(bw * 1.06f, bh * 0.36f, bl * 0.50f), noOutline: true, uv: UV_LIMB);
                        m.Add(Prim.Cone, new Vector3(0f, bh * 0.86f, bl * 0.12f),
                            new Vector3(0.14f * L, 0.15f * L, 0.14f * L), pitch: -48f, taper: 0.22f, noOutline: true, uv: UV_BELLY);
                    }

                    // 灰熊宽厚四方短吻与平视雄姿（吻部平视前探，宽阔大脸盘）
                    float hr = (badger ? 0.21f : sloth ? 0.23f : bear ? 0.21f : 0.25f) * L;
                    var headC = new Vector3(0f, bh * (sloth ? 0.48f : bear ? 0.46f : 0.62f), bl * (sloth ? 0.46f : bear ? 0.38f : 0.42f));
                    BuildHead(m, headC, hr, (badger ? 0.14f : bear ? 0.12f : 0.15f) * L, (bear ? 0.13f : 0.10f) * L, (bear ? 0.12f : 0.10f) * L,
                        panda ? UV_HEAD : sloth ? UV_BELLY : UV_COAT,
                        badger ? UV_DETAIL : sloth ? UV_LIMB : UV_HEAD,
                        panda ? UV_HEAD : UV_BELLY);
                    if (panda || sloth)
                    {
                        // 熊猫八字黑眼圈 / 大地懒深色眼罩（贴合眼周表面，不越出侧脸轮廓，noOutline=true）
                        AddSymX(m, Prim.Sphere, hr * 0.26f, headC.y - hr * 0.08f, headC.z + hr * 0.24f,
                            new Vector3(hr * 0.32f, hr * 0.28f, hr * 0.28f), yaw: 18f, splay: -14f, uv: UV_LIMB, noOutline: true);
                    }
                    if (!sloth)
                        BuildEars(m, headC, hr, bear ? 0.046f * L : 0.054f * L, bear ? 0.052f * L : 0.076f * L, roundEar: true, earUv: UV_LIMB);

                    if (sloth)
                    {
                        AddSeg(m, tailBase + new Vector3(0f, 0.01f * L, 0.04f * L), 0.25f * L, 0.15f * L, 0.042f * L, -126f, uv: UV_COAT, joint: true);
                    }
                    else if (badger)
                        AddSeg(m, tailBase, 0.17f * L, 0.12f * L, 0.055f * L, -115f, uv: UV_COAT);
                    else
                        AddSeg(m, tailBase + new Vector3(0f, 0.01f * L, 0.02f * L),
                            bear ? 0.06f * L : 0.10f * L, 0.055f * L, 0.030f * L, -165f, uv: panda ? UV_COAT : UV_LIMB, joint: false);
                    break;
                }

                case Arch.Canid:
                {
                    bool warg = Has(n, "warg");
                    bool fox = Has(n, "fox");
                    bool fennec = Has(n, "fennec");
                    bool husky = Has(n, "husky");
                    bool floppy = Has(n, "labrador", "boghound");
                    bool terrier = Has(n, "terrier", "yorkshire");

                    float bl = (fox ? 0.66f : 0.70f) * L;
                    float bh = (warg ? 0.40f : fox ? 0.32f : 0.34f) * L;
                    float bw = CalibrateWidth(n, bl, (warg ? 0.36f : fox ? 0.29f : 0.29f) * L);
                    int bellyUv = fox ? UV_DETAIL : UV_BELLY;

                    // 犬科极具辨识度的深胸与急剧收腰流线（前胸深沉贴地，腹部高高收起，背线平顺微弓消除驼峰）
                    float tuckUp = (warg ? 0.058f : fox ? 0.044f : 0.062f) * L;
                    float waistDip = 0.015f * L;
                    BuildQuadBody(m, L, bl, bh, bw, 0f,
                        (warg ? 0.10f : 0.06f) * L,
                        (warg ? 0.05f : 0.03f) * L,
                        warg ? 0.76f : 0.78f,
                        out var chestTop, out var tailBase, UV_LIMB, UV_LIMB, bellyUv,
                        waistDip: waistDip, tuckUp: tuckUp);
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.5f;

                    if (terrier)
                    {
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0f),
                            new Vector3(bw * 1.06f, bh * 0.52f, bl * 0.94f), noOutline: true, uv: UV_BELLY);
                    }

                    // 颈部挺立前探：狼颈有力挺起（pitch=42°），狼头平视前方，头顶高昂警惕
                    float canidNeckPitch = warg ? 44f : 42f;
                    var np = AddSeg(m, chestTop + new Vector3(0f, -0.04f * L, 0.02f * L),
                        0.15f * L, 0.18f * L, 0.14f * L, canidNeckPitch, uv: UV_COAT, noOutline: true);
                    m.Add(Prim.Sphere, chestTop + new Vector3(0f, -0.02f * L, 0.06f * L),
                        new Vector3(bw * 0.72f, bh * 0.65f, bl * 0.32f), pitch: 24f, noOutline: true, uv: UV_COAT);
                    if (husky)
                    {
                        m.Add(Prim.Cylinder, chestTop + new Vector3(0f, -0.04f * L, 0.04f * L),
                            new Vector3(0.11f * L, 0.14f * L, 0.09f * L), pitch: canidNeckPitch, noOutline: true, uv: UV_BELLY, taper: 0.82f);
                    }
                    float hr = (fox ? 0.155f : 0.165f) * L;
                    var headC = np + new Vector3(0f, 0.02f * L, 0.03f * L);
                    BuildHead(m, headC, hr, (warg ? 0.20f : fox ? 0.14f : 0.135f) * L, 0.076f * L, (warg ? 0.11f : 0.082f) * L,
                        UV_HEAD, UV_HEAD, bellyUv);

                    // 正视与背视极具辨识度的外展倒三角形颊毛 (Cheek Ruff，形成经典的六角/倒三角狼脸剪影)
                    AddSymX(m, Prim.Cone, hr * 0.44f, headC.y - hr * 0.10f, headC.z - hr * 0.02f,
                        new Vector3(0.052f * L, 0.110f * L, 0.072f * L), pitch: -14f, splay: 48f, yaw: -18f, taper: 0.24f, uv: UV_HEAD, noOutline: true);
                    AddSymX(m, Prim.Cone, hr * 0.42f, headC.y - hr * 0.16f, headC.z + hr * 0.04f,
                        new Vector3(0.042f * L, 0.090f * L, 0.062f * L), pitch: -10f, splay: 54f, yaw: -12f, taper: 0.20f, uv: UV_HEAD, noOutline: true);
                    // 适中挺立的 V 形尖狼耳（直立坚毅，朝前上方）
                    BuildEars(m, headC, hr, (fennec ? 0.09f : 0.044f) * L, (fennec ? 0.22f : warg ? 0.15f : fox ? 0.16f : 0.10f) * L,
                        floppy: floppy, earUv: UV_HEAD);

                    if (!warg && !floppy && !terrier)
                    {
                        // 灰狼 / 哈士奇 / 赤狐：前胸下方浅白毛色过渡（下颌浅色已由 BuildHead 一体化生成，不再加孤立腮球）
                        m.Add(Prim.Sphere, new Vector3(0f, 0.01f * L, bl * 0.22f),
                            new Vector3(bw * 0.96f, bh * 0.46f, bl * 0.46f), noOutline: true, uv: bellyUv);
                    }

                    if (warg)
                    {
                        // 座狼颈背与前肩顶峰的深黑刺状鬣毛簇（还原 _east 肩背黑刺、_south 额后黑鬃与 _north 脑后倒三角黑鬃）
                        for (int i = 0; i < 4; i++)
                        {
                            float t = i / 3f;
                            var spPos = Vector3.Lerp(headC + new Vector3(0f, hr * 0.28f, -hr * 0.22f),
                                new Vector3(0f, bh * 0.92f, 0.02f * L), t);
                            m.Add(Prim.Cone, spPos,
                                new Vector3(0.065f * L, (0.16f - i * 0.015f) * L, 0.085f * L),
                                pitch: -46f - i * 6f, taper: 0.16f, uv: UV_EYE);
                        }
                        AddSymX(m, Prim.Cone, 0.045f * L, headC.y + hr * 0.14f, headC.z - hr * 0.28f,
                            new Vector3(0.055f * L, 0.14f * L, 0.070f * L), pitch: -44f, splay: 22f, taper: 0.18f, uv: UV_EYE);
                    }

                    // 标志性犬科尾（狐狸无黑环超大白尖蓬松尾 / 哈士奇上卷镰刀白尾 / 座狼细鞭尾 / 狼一体式蓬松垂尾）
                    if (fox)
                    {
                        var tStart = tailBase + new Vector3(0f, 0.02f * L, 0.02f * L);
                        AddSeg(m, tStart, 0.36f * L, 0.14f * L, 0.036f * L, -114f, uv: UV_COAT, joint: true);
                        var tMid = tStart + StepVec(0.11f * L, -114f, 0f, 0f);
                        m.Add(Prim.Sphere, tMid + new Vector3(0f, -0.040f * L, 0f),
                            new Vector3(0.165f * L, 0.13f * L, 0.20f * L), pitch: -22f, noOutline: true, uv: UV_COAT);
                        var tWhite = tStart + StepVec(0.18f * L, -114f, 0f, 0f);
                        AddSeg(m, tWhite, 0.18f * L, 0.125f * L, 0.036f * L, -114f, uv: UV_DETAIL, joint: true, noOutline: true);
                    }
                    else if (husky || terrier)
                    {
                        var tp = AddSeg(m, tailBase + new Vector3(0f, 0.02f * L, 0.02f * L), 0.13f * L, 0.10f * L, 0.125f * L, -42f, uv: UV_COAT, joint: true);
                        tp = AddSeg(m, tp, 0.13f * L, 0.125f * L, 0.115f * L, 18f, uv: UV_BELLY, joint: true);
                        AddSeg(m, tp, 0.12f * L, 0.115f * L, 0.048f * L, 78f, uv: UV_BELLY, joint: true);
                    }
                    else if (warg)
                    {
                        var tp = AddSeg(m, tailBase + new Vector3(0f, 0.03f * L, 0.02f * L), 0.14f * L, 0.058f * L, 0.038f * L, -118f, uv: UV_LIMB, joint: true);
                        AddSeg(m, tp, 0.13f * L, 0.038f * L, 0.014f * L, -142f, uv: UV_EYE, joint: false);
                    }
                    else
                    {
                        // 饱满粗壮下垂的大扫帚狼尾（尾根斜向后下垂，尾梢贴近脚跟，还原原版平顺低垂姿态）
                        var tailP = tailBase + new Vector3(0f, 0.01f * L, 0.01f * L);
                        var tp = AddSeg(m, tailP, 0.095f * L, 0.082f * L, 0.086f * L, -140f, uv: UV_COAT, joint: true, noOutline: true);
                        AddSeg(m, tp, 0.090f * L, 0.086f * L, 0.036f * L, -155f, uv: UV_COAT, joint: true);
                    }
                    break;
                }

                case Arch.Felid:
                {
                    bool lynx = Has(n, "lynx");
                    bool scimitar = Has(n, "scimitar");
                    bool tiger = Has(n, "tiger");
                    bool smallCat = Has(n, "cat") && !scimitar;

                    float bl = 0.68f * L, bh = (smallCat ? 0.34f : 0.36f) * L;
                    float bw = CalibrateWidth(n, bl, (smallCat ? 0.29f : 0.31f) * L);
                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.07f * L, 0.02f * L, 0.96f,
                        out var chestTop, out var tailBase, UV_COAT, UV_COAT, lynx ? UV_COAT : UV_BELLY);
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.5f;

                    if (tiger)
                    {
                        // 虎背深色横纹带（noOutline=true）
                        for (int i = 0; i < 3; i++)
                            m.Add(Prim.Sphere, new Vector3(0f, bh * 0.10f, (0.14f - i * 0.16f) * L),
                                new Vector3(bw * 1.02f, bh * 0.88f, 0.07f * L), noOutline: true, uv: UV_DETAIL);
                    }

                    var np = AddSeg(m, chestTop + new Vector3(0f, -0.05f * L, 0.01f * L),
                        (smallCat ? 0.14f : 0.12f) * L, 0.14f * L, 0.12f * L, smallCat ? 26f : 40f, uv: UV_COAT, noOutline: true);
                    float hr = (smallCat ? 0.24f : 0.22f) * L;
                    var headC = np + new Vector3(0f, 0.01f * L, 0.02f * L);
                    BuildHead(m, headC, hr, hr * (smallCat ? 0.42f : 0.56f), hr * (smallCat ? 0.52f : 0.56f), hr * 0.42f,
                        UV_COAT, lynx ? UV_BELLY : UV_COAT, UV_BELLY);
                    if (lynx)
                    {
                        // 猞猁前胸白毛领 + 耳背白斑（下颌白毛已由 BuildHead 一体化生成，noOutline=true）
                        m.Add(Prim.Sphere, new Vector3(0f, 0.03f * L, bl * 0.28f),
                            new Vector3(bw * 0.82f, bh * 0.48f, bl * 0.34f), noOutline: true, uv: UV_BELLY);
                        AddSymX(m, Prim.Sphere, hr * 0.44f, headC.y + hr * 0.38f, headC.z - hr * 0.10f,
                            new Vector3(0.042f * L, 0.052f * L, 0.038f * L), uv: UV_BELLY, noOutline: true);
                    }
                    BuildEars(m, headC, hr, (smallCat ? 0.064f : 0.052f) * L, (lynx || smallCat ? 0.105f : 0.082f) * L,
                        roundEar: false, tufted: lynx, earUv: smallCat ? UV_COAT : lynx ? UV_LIMB : UV_HEAD);

                    if (scimitar)
                    {
                        AddSymX(m, Prim.Cone, hr * 0.20f, headC.y - hr * 0.38f, headC.z + hr * 0.46f,
                            new Vector3(0.042f * L, 0.14f * L, 0.042f * L), pitch: 168f, uv: UV_DETAIL);
                    }

                    // 猫科尾（家猫高举问号弯钩尾 / 猞猁黑尖短尾 / 美洲狮修长垂尾）
                    if (lynx || scimitar)
                    {
                        var tp = AddSeg(m, tailBase + new Vector3(0f, 0.01f * L, 0.02f * L), 0.10f * L, 0.068f * L, 0.054f * L, -124f, uv: UV_COAT, joint: true);
                        AddSeg(m, tp, 0.06f * L, 0.054f * L, 0.032f * L, -124f, uv: UV_EYE, joint: true, noOutline: true);
                    }
                    else if (smallCat)
                    {
                        var tp = AddSeg(m, tailBase + new Vector3(0f, 0.02f * L, 0.02f * L), 0.16f * L, 0.058f * L, 0.050f * L, -16f, uv: UV_COAT, joint: true);
                        tp = AddSeg(m, tp, 0.15f * L, 0.048f * L, 0.042f * L, 10f, roll: -10f, uv: UV_COAT, joint: true);
                        AddSeg(m, tp, 0.10f * L, 0.040f * L, 0.028f * L, 68f, roll: -28f, uv: UV_COAT, joint: true);
                    }
                    else
                    {
                        var tp = AddSeg(m, tailBase + new Vector3(0f, 0.03f * L, 0.03f * L), 0.20f * L, 0.054f * L, 0.038f * L, -134f, uv: UV_COAT, joint: true);
                        AddSeg(m, tp, 0.11f * L, 0.038f * L, 0.024f * L, -146f, uv: UV_DETAIL, joint: true, noOutline: true);
                    }
                    break;
                }

                case Arch.Porcine:
                {
                    bool boar = Has(n, "boar");
                    float bl = 0.66f * L, bh = 0.36f * L;
                    float bw = CalibrateWidth(n, bl, 0.38f * L);
                    // 野猪前高后低敦实楔形体魄（前肩强壮高耸，后臀收拢）
                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.08f * L, boar ? 0.04f * L : 0f, boar ? 0.80f : 0.92f,
                        out var chestTop, out var tailBase, UV_COAT, UV_COAT, UV_BELLY);
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.5f;

                    // 楔形猪头与扁平猪鼻盘（向前下方倾斜）
                    float hr = (boar ? 0.18f : 0.22f) * L;
                    var headC = new Vector3(0f, bh * (boar ? 0.38f : 0.54f), bl * (boar ? 0.38f : 0.38f));
                    BuildHead(m, headC, hr, hr * (boar ? 0.68f : 0.58f), hr * (boar ? 0.56f : 0.62f), hr * (boar ? 0.46f : 0.52f),
                        boar ? UV_COAT : UV_HEAD, boar ? UV_COAT : UV_HEAD, UV_BELLY, hasNose: false);
                    var snoutTip = headC + new Vector3(0f, -hr * 0.18f, hr * 0.56f);
                    m.Add(Prim.Sphere, snoutTip - new Vector3(0f, 0.034f * L, 0f),
                        new Vector3(0.082f * L, 0.068f * L, 0.058f * L), noOutline: true, uv: UV_HEAD);

                    // 向斜后上方倾斜的小尖猪耳
                    AddSymX(m, Prim.Cone, hr * 0.28f, headC.y + hr * 0.10f, headC.z - hr * 0.06f,
                        new Vector3(0.036f * L, 0.070f * L, 0.030f * L), pitch: -22f, splay: 26f, taper: 0.24f, uv: boar ? UV_COAT : UV_HEAD);

                    if (boar)
                    {
                        // 额中前额毛
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.04f, hr * 0.10f),
                            new Vector3(0.055f * L, hr * 0.46f, hr * 0.60f), pitch: 20f, noOutline: true, uv: UV_LIMB);
                        // 野猪标志性贴脊硬毛鬃带（平顺连续的铁锈橙背脊硬毛带，紧密贴伏背脊从额顶延伸至后背，杜绝圆柱穿刺）
                        for (int i = 0; i < 7; i++)
                        {
                            float t = i / 6f;
                            var spPos = Vector3.Lerp(
                                new Vector3(0f, headC.y + hr * 0.12f, headC.z - 0.02f * L),
                                new Vector3(0f, bh * 0.46f, -bl * 0.20f), t);
                            m.Add(Prim.Cone, spPos,
                                new Vector3(0.038f * L, 0.024f * L, 0.045f * L),
                                pitch: -10f - i * 3f, taper: 0.35f, uv: UV_LIMB, noOutline: true);
                        }

                        // 下颌嘴角向上弯翘的一对白色小獠牙（向斜前上方弯翘立起）
                        var tL = headC + new Vector3(-hr * 0.22f, -hr * 0.20f, hr * 0.36f);
                        var tR = headC + new Vector3( hr * 0.22f, -hr * 0.20f, hr * 0.36f);
                        AddSymSeg(m, ref tL, ref tR, 0.065f * L, 0.022f * L, 0.008f * L, 28f, 16f, 0f, UV_DETAIL, joint: false);
                    }

                    // 贴臀下垂短尾
                    var tp = AddSeg(m, tailBase + new Vector3(0f, 0.01f * L, 0.02f * L),
                        0.08f * L, 0.034f * L, 0.022f * L, -145f, uv: UV_COAT, joint: false);
                    if (boar)
                        AddSeg(m, tp, 0.05f * L, 0.022f * L, 0.012f * L, -155f, uv: UV_LIMB, joint: false, noOutline: true);
                    break;
                }

                case Arch.RodentSmall:
                {
                    bool hare = Has(n, "hare", "rabbit");
                    bool squirrel = Has(n, "squirrel");
                    bool beaver = Has(n, "beaver");
                    bool raccoon = Has(n, "raccoon");
                    bool boomrat = Has(n, "boomrat");
                    bool porcupine = Has(n, "porcupine");
                    bool cavy = Has(n, "capybara", "guineapig", "prairie", "vole");
                    bool chinchilla = Has(n, "chinchilla");

                    float bl = (cavy ? 0.64f : hare ? 0.54f : squirrel ? 0.48f : boomrat ? 0.60f : 0.58f) * L;
                    float bh = (cavy ? 0.40f : hare ? 0.36f : squirrel ? 0.30f : boomrat ? 0.24f : 0.35f) * L;
                    float bw = CalibrateWidth(n, bl, (cavy ? 0.36f : hare ? 0.32f : squirrel ? 0.26f : boomrat ? 0.28f : 0.30f) * L);

                    // 啮齿类与兔类：前胸低伏、后臀高拱饱满的团身蹲姿（爆炸鼠躯干低伏以凸显背上两大独立黄囊）
                    BuildQuadBody(m, L, bl, bh, bw, 0f, 0.065f * L, -0.02f * L, boomrat ? 0.86f : 1.12f,
                        out var chestTop, out var tailBase, UV_COAT, UV_COAT, UV_BELLY);
                    if (hare || squirrel)
                    {
                        // 野兔 / 松鼠圆拱团身背脊（noOutline=true 平滑过渡）
                        m.Add(Prim.Sphere, new Vector3(0f, 0.02f * L, -0.04f * L),
                            new Vector3(bw * 1.00f, bh * 1.02f, bl * 0.78f), noOutline: true, uv: UV_COAT);
                    }
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.5f;

                    if (boomrat)
                    {
                        // 爆炸鼠标志性的前后两大亮黄化学囊泡（补偿后倾位移使两囊坐落背脊正中，_south 与 _north 均呈现三层叠透视）
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.50f, 0.11f * L),
                            new Vector3(0.20f * L, 0.19f * L, 0.21f * L), pitch: -24f, uv: UV_DETAIL);
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.34f, -0.09f * L),
                            new Vector3(0.22f * L, 0.20f * L, 0.23f * L), pitch: -26f, uv: UV_DETAIL);
                    }
                    else if (porcupine)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            float fz = (0.14f - i * 0.12f) * L;
                            m.Add(Prim.Cone, new Vector3(0f, bh * 0.72f, fz),
                                new Vector3(0.055f * L, 0.17f * L, 0.055f * L), pitch: -58f, uv: UV_DETAIL);
                            AddSymX(m, Prim.Cone, bw * 0.26f, bh * 0.58f, fz,
                                new Vector3(0.050f * L, 0.15f * L, 0.050f * L), pitch: -55f, splay: 24f, uv: UV_DETAIL);
                        }
                    }

                    float hr = (cavy ? 0.25f : hare ? 0.23f : squirrel ? 0.20f : boomrat ? 0.19f : 0.22f) * L;
                    var headC = new Vector3(0f, bh * (hare ? 0.54f : squirrel ? 0.64f : boomrat ? 0.78f : 0.42f), bl * (hare || squirrel ? 0.36f : boomrat ? 0.42f : 0.40f));
                    BuildHead(m, headC, hr, hr * (cavy ? 0.82f : hare ? 0.34f : squirrel ? 0.38f : boomrat ? 0.62f : 0.74f), hr * (cavy ? 0.68f : 0.54f),
                        hr * (cavy ? 0.64f : 0.48f), UV_HEAD, UV_HEAD, UV_BELLY, hasNose: !(hare || squirrel || boomrat));

                    if (raccoon)
                        AddSymX(m, Prim.Sphere, hr * 0.28f, headC.y - hr * 0.12f, headC.z + hr * 0.24f,
                            new Vector3(hr * 0.34f, hr * 0.26f, hr * 0.26f), uv: UV_LIMB, noOutline: true);
                    if (beaver)
                        m.Add(Prim.Box, headC + new Vector3(0f, -hr * 0.42f, hr * 0.48f),
                            new Vector3(0.042f * L, 0.048f * L, 0.026f * L), noOutline: true, uv: UV_DETAIL);

                    if (hare || squirrel || boomrat)
                    {
                        // 兔类 / 松鼠 / 爆炸鼠：深植头顶两侧的圆尖椭球耳（消除耳根独立黑圈）
                        AddSymX(m, Prim.Sphere, hr * (hare ? 0.17f : boomrat ? 0.32f : 0.22f), headC.y + hr * (boomrat ? 0.02f : 0.10f), headC.z - hr * 0.06f,
                            new Vector3((hare ? 0.068f : 0.052f) * L, (hare ? 0.27f : boomrat ? 0.10f : 0.12f) * L, (hare ? 0.052f : 0.046f) * L),
                            pitch: hare ? -26f : -16f, splay: hare ? 11f : boomrat ? 38f : 18f, uv: UV_COAT);
                    }
                    else
                    {
                        BuildEars(m, headC, hr, (chinchilla ? 0.075f : 0.052f) * L,
                            0.095f * L, roundEar: true, earUv: UV_LIMB);
                    }

                    // 啮齿类特征尾（爆炸鼠尾端被巨型后黄囊完全覆盖，无外露鼠尾）
                    if (boomrat)
                    {
                        // 无外露鼠尾
                    }
                    else if (hare || cavy)
                    {
                        // 臀部底端贴地小绒尾（消除 _north 后背靶心黑圈）
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.08f, -bl * 0.43f),
                            new Vector3(0.11f * L, 0.11f * L, 0.11f * L), uv: UV_BELLY);
                    }
                    else if (squirrel || chinchilla)
                    {
                        // 松鼠标志性的高扬 S 形蓬松大尾（尾根深植臀内 + 过渡绒毛球贴合覆盖端盖描边唇）
                        var t0 = tailBase + new Vector3(0f, -0.04f * L, 0.06f * L);
                        var t1 = AddSeg(m, t0, 0.18f * L, 0.105f * L, 0.110f * L, -60f, roll: 14f, uv: UV_COAT, joint: false);
                        var j1 = t0 + StepVec(0.168f * L, -60f, 0f, 14f);
                        m.Add(Prim.Sphere, j1 - new Vector3(0f, 0.060f * L, 0f),
                            new Vector3(0.120f * L, 0.120f * L, 0.120f * L), noOutline: true, uv: UV_COAT);
                        var t2 = AddSeg(m, t1, 0.16f * L, 0.108f * L, 0.098f * L, -24f, roll: -18f, uv: UV_COAT, joint: false);
                        var j2 = t1 + StepVec(0.150f * L, -24f, 0f, -18f);
                        m.Add(Prim.Sphere, j2 - new Vector3(0f, 0.054f * L, 0f),
                            new Vector3(0.108f * L, 0.108f * L, 0.108f * L), noOutline: true, uv: UV_COAT);
                        AddSeg(m, t2, 0.13f * L, 0.094f * L, 0.036f * L, 20f, roll: 12f, uv: UV_COAT, joint: false);
                    }
                    else if (beaver)
                    {
                        m.Add(Prim.Sphere, tailBase + new Vector3(0f, -0.06f * L, -0.14f * L),
                            new Vector3(0.18f * L, 0.06f * L, 0.32f * L), pitch: -12f, uv: UV_LIMB);
                    }
                    else if (raccoon)
                    {
                        var tp = tailBase;
                        for (int i = 0; i < 4; i++)
                            tp = AddSeg(m, tp, 0.09f * L, (0.12f - i * 0.014f) * L, (0.11f - i * 0.014f) * L,
                                -102f + i * 8f, uv: (i % 2 == 0) ? UV_COAT : UV_LIMB);
                    }
                    else
                    {
                        var tp = AddSeg(m, tailBase, 0.16f * L, 0.050f * L, 0.036f * L, -102f, uv: UV_LIMB);
                        AddSeg(m, tp, 0.17f * L, 0.036f * L, 0.014f * L, -90f, uv: UV_LIMB);
                    }
                    break;
                }

                case Arch.PrimateDryad:
                {
                    bool gorilla = Has(n, "gorilla", "ape");
                    bool dryad = Has(n, "dryad");
                    bool clawer = Has(n, "clawer");

                    float bl = (gorilla ? 0.60f : 0.56f) * L;
                    float bh = (gorilla ? 0.54f : 0.42f) * L;
                    float bw = CalibrateWidth(n, bl, (gorilla ? 0.46f : 0.35f) * L);

                    BuildQuadBody(m, L, bl, bh, bw, 0f, (gorilla ? 0.13f : 0.08f) * L,
                        (gorilla ? 0.10f : 0.04f) * L, gorilla ? 0.84f : 0.96f,
                        out var chestTop, out var tailBase, UV_COAT, UV_COAT, UV_BELLY);
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.5f;

                    if (clawer)
                    {
                        var cL = new Vector3(-bw * 0.44f, 0.12f * L, bl * 0.28f);
                        var cR = new Vector3(bw * 0.44f, 0.12f * L, bl * 0.28f);
                        AddSymSeg(m, ref cL, ref cR, 0.16f * L, 0.08f * L, 0.025f * L, 78f, 18f, 0f, UV_DETAIL);
                    }

                    float hr = (gorilla ? 0.27f : 0.24f) * L;
                    var headC = chestTop + new Vector3(0f, -0.02f * L, 0.06f * L);
                    BuildHead(m, headC, hr, hr * 0.66f, hr * 0.66f, hr * 0.54f, UV_COAT, UV_HEAD, UV_HEAD);

                    if (dryad)
                    {
                        BuildEars(m, headC, hr, 0.065f * L, 0.17f * L, earUv: UV_DETAIL);
                    }
                    else
                    {
                        BuildEars(m, headC, hr, 0.055f * L, 0.075f * L, roundEar: true, earUv: UV_HEAD);
                        if (!gorilla)
                        {
                            var tp = AddSeg(m, tailBase, 0.17f * L, 0.052f * L, 0.045f * L, -18f, uv: UV_COAT);
                            tp = AddSeg(m, tp, 0.15f * L, 0.045f * L, 0.038f * L, 12f, uv: UV_COAT);
                            AddSeg(m, tp, 0.10f * L, 0.038f * L, 0.028f * L, 68f, uv: UV_COAT);
                        }
                    }
                    break;
                }

                case Arch.BirdFowl:
                case Arch.TallBird:
                {
                    bool tall = a == Arch.TallBird;
                    bool chicken = Has(n, "chicken");
                    bool turkey = Has(n, "turkey");
                    bool peacock = Has(n, "peacock");
                    bool duck = Has(n, "duck");
                    bool cassowary = Has(n, "cassowary");
                    bool macaw = Has(n, "macaw", "parrot");
                    bool waterfowl = Has(n, "duck", "goose", "swan");
                    bool longNeck = tall || Has(n, "goose", "swan", "peacock");

                    float bl = (tall ? 0.56f : (turkey || peacock || chicken) ? 0.48f : 0.54f) * L;
                    float bh = (tall ? 0.36f : (turkey || peacock || duck || chicken) ? 0.31f : 0.36f) * L;
                    float bw = CalibrateWidth(n, bl, (tall ? 0.38f : (turkey || peacock || duck || chicken) ? 0.35f : 0.36f) * L);

                    // 船形饱满胸腹直接贴地 (y=0)：
                    // 孔雀的身体/前胸是宝蓝色 (UV_HEAD)，绿头公鸭的主体是米白配栗棕前胸 (UV_BELLY + UV_LIMB)，鸵鸟主翼是黑羽配白翼尖
                    int bodyMainUv = peacock ? UV_HEAD : duck ? UV_BELLY : UV_COAT;
                    int breastUv = peacock ? UV_HEAD : duck ? UV_LIMB : UV_COAT;
                    int wingUv = tall ? UV_COAT : (turkey || chicken) ? UV_COAT : UV_BELLY;

                    m.Add(Prim.Sphere, new Vector3(0f, 0f, duck ? -0.02f * L : 0f), new Vector3(bw, bh, bl), uv: bodyMainUv);
                    m.Add(Prim.Sphere, new Vector3(0f, bh * (duck ? 0.04f : 0.06f), bl * (duck ? 0.22f : 0.18f)),
                        new Vector3(bw * 0.94f, bh * (duck ? 0.88f : 0.86f), bl * (duck ? 0.52f : 0.56f)), noOutline: true, uv: breastUv);
                    if (duck)
                    {
                        // 绿头公鸭后背斜坡中央的褐羽鞍带（平行贴合后背斜坡，还原 _north 背部中线褐色羽斑且侧视不越过白背脊）
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.42f, -0.11f * L),
                            new Vector3(bw * 0.42f, bh * 0.54f, bl * 0.60f), pitch: -28f, noOutline: true, uv: UV_LIMB);
                    }
                    if (tall)
                    {
                        // 鸵鸟/高鸟：两侧顺滑贴合的深色主翼(noOutline=true 消除体侧大黑圈) + 翼下缘圆润雪白羽翎簇(y>=0.01*L)
                        AddSymX(m, Prim.Sphere, bw * 0.38f, bh * 0.08f, -0.02f * L,
                            new Vector3(bw * 0.38f, bh * 0.68f, bl * 0.68f), yaw: 6f, pitch: -10f, splay: 12f, uv: UV_COAT, noOutline: true);
                        AddSymX(m, Prim.Sphere, bw * 0.48f, 0.01f * L, 0.02f * L,
                            new Vector3(0.13f * L, 0.13f * L, 0.20f * L), yaw: 8f, splay: 16f, uv: UV_BELLY);
                        AddSymX(m, Prim.Sphere, bw * 0.45f, 0.02f * L, -0.10f * L,
                            new Vector3(0.11f * L, 0.12f * L, 0.17f * L), yaw: 6f, splay: 14f, uv: UV_BELLY, noOutline: true);
                    }
                    else
                    {
                        AddSymX(m, Prim.Sphere, bw * 0.34f, bh * 0.12f, -0.02f * L,
                            new Vector3(bw * 0.40f, bh * 0.70f, bl * 0.68f), yaw: 5f, pitch: -8f, uv: wingUv, noOutline: true);
                        if (turkey)
                        {
                            // 火鸡体侧斜收折翼与翼尖棕黄飞羽边（还原 _east 体侧后下方斜收翼羽，不突入正面底部）
                            AddSymX(m, Prim.Sphere, bw * 0.36f, 0.03f * L, -0.04f * L,
                                new Vector3(0.08f * L, 0.16f * L, 0.12f * L), yaw: 8f, pitch: -36f, splay: 10f, uv: UV_BELLY, noOutline: true);
                        }
                        else if (peacock)
                        {
                            // 孔雀体侧灰白折翼下缘的暖棕翼斑（还原 _east 左下角棕翼斑且不向后突出）
                            AddSymX(m, Prim.Sphere, bw * 0.36f, 0.03f * L, -0.05f * L,
                                new Vector3(0.085f * L, 0.13f * L, 0.15f * L), yaw: 6f, pitch: -18f, splay: 8f, uv: UV_LIMB, noOutline: true);
                        }
                        else if (duck)
                        {
                            // 绿头公鸭侧翼中后部的褐羽斜带（还原 _east 与 _north 侧翼褐羽条）
                            AddSymX(m, Prim.Sphere, bw * 0.39f, bh * 0.28f, -0.08f * L,
                                new Vector3(0.090f * L, 0.082f * L, 0.24f * L), yaw: 6f, pitch: -16f, uv: UV_LIMB, noOutline: true);
                        }
                        else
                        {
                            // 家鸡等陆禽侧翼与腹下阴影：主翼保持金黄 UV_COAT，仅在腹底/翼底缘加窄条 UV_LIMB 暖棕暗部
                            AddSymX(m, Prim.Sphere, bw * 0.34f, 0.02f * L, -0.02f * L,
                                new Vector3(bw * 0.24f, bh * 0.34f, bl * 0.42f), yaw: 4f, pitch: -8f, uv: UV_LIMB, noOutline: true);
                        }
                    }
                    bodyCenterY = bh * 0.5f;
                    bodyHalfW = bw * 0.52f;

                    // S 形曲颈与鸟头（走禽/火鸡/孔雀/水禽/陆禽用两端微收曲颈 + 颈身/咽喉平滑套，彻底消除颈根、颈节与下颌黑弧）
                    var np = new Vector3(0f, bh * (tall ? 0.42f : (turkey || peacock) ? 0.56f : (duck || chicken) ? 0.68f : 0.56f),
                        bl * (tall ? 0.20f : (turkey || peacock) ? 0.32f : (duck || chicken) ? 0.28f : 0.25f));
                    int neckUv = (tall || duck || peacock) ? UV_HEAD : turkey ? UV_DETAIL : UV_COAT;
                    float hr = (tall ? 0.145f : (turkey || peacock) ? 0.13f : (duck || chicken) ? 0.145f : 0.17f) * L;
                    Vector3 headC;
                    if (turkey)
                    {
                        // 火鸡高昂鲜红S曲颈（UV_DETAIL）高出背脊，前胸垂挂三瓣红肉垂
                        var n0 = np;
                        np = AddSeg(m, n0, 0.27f * L, 0.072f * L, 0.060f * L, -8f, uv: UV_DETAIL, joint: false);
                        m.Add(Prim.Sphere, n0 + new Vector3(0f, 0.01f * L, -0.005f * L),
                            new Vector3(0.084f * L, 0.26f * L, 0.092f * L), pitch: -8f, noOutline: true, uv: UV_DETAIL);
                        m.Add(Prim.Sphere, np + new Vector3(0f, -0.065f * L, 0.014f * L),
                            new Vector3(0.092f * L, 0.105f * L, 0.112f * L), pitch: 10f, noOutline: true, uv: UV_DETAIL);
                    }
                    else if (peacock)
                    {
                        // 孔雀修长宝蓝高昂曲颈（一体单柱内芯 + 胸颈/咽喉无描边过渡桥，彻底消除颈部黑圈并使头冠高出尾屏上沿）
                        var n0 = new Vector3(0f, bh * 0.50f, bl * 0.30f);
                        np = AddSeg(m, n0, 0.34f * L, 0.068f * L, 0.056f * L, -6f, uv: UV_HEAD, joint: false);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.11f * L, 0.14f * L),
                            new Vector3(0.112f * L, 0.24f * L, 0.125f * L), pitch: -6f, noOutline: true, uv: UV_HEAD);
                        m.Add(Prim.Sphere, n0 + new Vector3(0f, 0.04f * L, -0.008f * L),
                            new Vector3(0.088f * L, 0.29f * L, 0.098f * L), pitch: -6f, noOutline: true, uv: UV_HEAD);
                        m.Add(Prim.Sphere, np + new Vector3(0f, -0.072f * L, 0.012f * L),
                            new Vector3(0.112f * L, 0.115f * L, 0.122f * L), pitch: 8f, noOutline: true, uv: UV_HEAD);
                    }
                    else if (duck)
                    {
                        // 绿头公鸭标志性的雪白颈环（UV_BELLY 与绿颈齐平无外凸）与挺立翠绿鸭颈（UV_HEAD）
                        var n0 = np;
                        np = AddSeg(m, n0, 0.18f * L, 0.092f * L, 0.072f * L, -4f, uv: UV_HEAD, joint: false);
                        m.Add(Prim.Sphere, n0 + new Vector3(0f, 0.030f * L, -0.006f * L),
                            new Vector3(0.122f * L, 0.048f * L, 0.128f * L), pitch: 4f, noOutline: true, uv: UV_BELLY);
                        m.Add(Prim.Sphere, n0 + new Vector3(0f, 0.056f * L, -0.002f * L),
                            new Vector3(0.115f * L, 0.14f * L, 0.122f * L), pitch: -4f, noOutline: true, uv: UV_HEAD);
                        m.Add(Prim.Sphere, np + new Vector3(0f, -0.072f * L, 0.014f * L),
                            new Vector3(0.128f * L, 0.120f * L, 0.142f * L), pitch: 8f, noOutline: true, uv: UV_HEAD);
                    }
                    else if (longNeck)
                    {
                        float nl = (tall ? 0.24f : 0.13f) * L;
                        var n0 = np;
                        // 下段颈底端与顶端均微收，使上下端盖反绕壳完全内嵌，配合无描边颈套彻底消除颈根与颈节黑环
                        np = AddSeg(m, n0, nl, (tall ? 0.076f : 0.104f) * L, 0.074f * L, -10f, uv: neckUv, joint: false);
                        if (tall)
                        {
                            m.Add(Prim.Sphere, n0 + new Vector3(0f, 0.01f * L, -0.01f * L),
                                new Vector3(0.102f * L, 0.23f * L, 0.108f * L), pitch: -10f, noOutline: true, uv: neckUv);
                        }
                        var nj = n0 + StepVec(nl * 0.92f, -10f);
                        m.Add(Prim.Sphere, nj - new Vector3(0f, 0.055f * L, 0f),
                            new Vector3(0.106f * L, 0.112f * L, 0.108f * L), noOutline: true, uv: neckUv);
                        var n1 = n0 + StepVec(nl * 0.86f, -10f);
                        np = AddSeg(m, n1, nl * (tall ? 0.86f : 0.96f), 0.094f * L, 0.068f * L, 16f, uv: neckUv, joint: false);
                        // 咽喉前侧(+Z)平滑套：贴合上颈与头底，遮蔽头球底缘反绕壳且不外凸双下巴
                        m.Add(Prim.Sphere, np + new Vector3(0f, -0.075f * L, 0.006f * L),
                            new Vector3(0.104f * L, 0.13f * L, 0.108f * L), pitch: 14f, noOutline: true, uv: neckUv);
                    }
                    else
                    {
                        // 家鸡等陆禽昂首挺胸的饱满短颈（过渡套宽度严格收敛于颈柱内，配背颈无描边桥消除 _north 背顶横切颈部黑线）
                        var n0 = np;
                        np = AddSeg(m, n0, 0.16f * L, 0.116f * L, 0.104f * L, -2f, uv: neckUv, joint: false);
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.56f, 0.06f * L),
                            new Vector3(0.130f * L, bh * 0.50f, 0.24f * L), pitch: -14f, noOutline: true, uv: neckUv);
                        m.Add(Prim.Sphere, n0 + new Vector3(0f, -0.015f * L, -0.002f * L),
                            new Vector3(0.128f * L, 0.15f * L, 0.145f * L), pitch: -2f, noOutline: true, uv: neckUv);
                        m.Add(Prim.Sphere, np + new Vector3(0f, -0.068f * L, 0.006f * L),
                            new Vector3(0.112f * L, 0.115f * L, 0.138f * L), pitch: 6f, noOutline: true, uv: neckUv);
                    }
                    headC = np + new Vector3(0f, hr * 0.08f, ((turkey || peacock || duck || chicken) ? 0.012f : -0.008f) * L);
                    m.Add(Prim.Sphere, headC - new Vector3(0f, hr * 0.48f, 0f),
                        new Vector3(hr * (tall ? 0.90f : 0.92f), hr * 0.84f, hr * (tall ? 0.86f : (turkey || peacock || duck || chicken) ? 0.94f : 1.02f)), pitch: 8f, uv: UV_HEAD);
                    if (turkey)
                    {
                        // 火鸡后枕红冠包覆（还原 _east 后脑红冠与前脸天蓝面盘分界）
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.42f, -0.024f * L),
                            new Vector3(hr * 0.88f, hr * 0.82f, hr * 0.72f), pitch: 6f, noOutline: true, uv: UV_DETAIL);
                    }
                    AddSymX(m, Prim.Sphere, hr * 0.22f, headC.y - hr * 0.36f, headC.z + hr * 0.04f,
                        new Vector3(hr * 0.44f, hr * 0.44f, hr * 0.46f), pitch: 12f, uv: macaw ? UV_DETAIL : UV_HEAD, noOutline: true);
                    AddSymX(m, Prim.Sphere, hr * 0.36f, headC.y - hr * 0.04f, headC.z + hr * 0.20f,
                        new Vector3(hr * 0.20f, hr * 0.20f, hr * 0.20f), uv: UV_EYE, noOutline: true);

                    // 鸟喙（水禽为内嵌窄核带描边 + 外层无描边扁鸭喙，陆禽/走禽为带完整外描边的微倾锥喙 + 无描边宽喙基）
                    int beakUv = (waterfowl || tall || macaw) ? UV_DETAIL : (turkey || peacock) ? UV_BELLY : UV_LIMB;
                    if (waterfowl)
                    {
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.28f, hr * 0.34f),
                            new Vector3(hr * 0.52f, hr * 0.34f, hr * 0.52f), pitch: 14f, noOutline: true, uv: beakUv);
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.25f, hr * 0.48f),
                            new Vector3(hr * 0.22f, hr * 0.18f, hr * 0.62f), pitch: 14f, uv: beakUv);
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.27f, hr * 0.48f),
                            new Vector3(hr * 0.44f, hr * 0.22f, hr * 0.66f), pitch: 14f, noOutline: true, uv: beakUv);
                    }
                    else
                    {
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.26f, hr * 0.26f),
                            new Vector3(hr * 0.54f, hr * 0.36f, hr * 0.52f), pitch: 12f, noOutline: true, uv: beakUv);
                        AddSeg(m, headC + new Vector3(0f, -hr * 0.08f, hr * 0.20f),
                            (tall ? 0.125f : (turkey || peacock || chicken) ? 0.098f : 0.125f) * L, (tall ? 0.048f : (turkey || peacock || chicken) ? 0.036f : 0.048f) * L, 0.014f * L,
                            tall ? 98f : 106f, uv: beakUv, joint: false, noOutline: false);
                    }

                    // 鸡冠肉垂 / 火鸡长肉垂 / 鹤鸵盔冠 / 孔雀羽冠
                    if (chicken)
                    {
                        for (int i = 0; i < 3; i++)
                            m.Add(Prim.Sphere, headC + new Vector3(0f, hr * 0.22f, (0.036f - i * 0.036f) * L),
                                new Vector3(0.038f * L, (i == 1 ? 0.086f : 0.070f) * L, 0.040f * L), uv: UV_DETAIL);
                        // 鸡冠低位无描边内衬（遮蔽三峰鸡冠底部的内部黑线，同时保留顶部清晰三峰锯齿）
                        m.Add(Prim.Sphere, headC + new Vector3(0f, hr * 0.16f, 0f),
                            new Vector3(0.044f * L, 0.052f * L, 0.104f * L), noOutline: true, uv: UV_DETAIL);
                        // 颌下圆润鲜红肉垂（还原 _east 与 _south 喙下红肉垂）
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.52f, hr * 0.44f),
                            new Vector3(0.044f * L, 0.062f * L, 0.052f * L), noOutline: true, uv: UV_DETAIL);
                    }
                    else if (turkey || cassowary)
                    {
                        // 火鸡标志性从喙端垂挂而下的鲜红长肉垂（snood）与胸前突出三瓣红肉垂（还原 _east 喙前悬垂红条与 _south 胸前连体红三瓣）
                        m.Add(Prim.Sphere, headC + new Vector3(0.008f * L, -0.16f * L, 0.088f * L),
                            new Vector3(0.030f * L, 0.155f * L, 0.034f * L), pitch: -4f, noOutline: true, uv: UV_DETAIL);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.13f * L, 0.215f * L),
                            new Vector3(0.068f * L, 0.22f * L, 0.078f * L), pitch: -6f, noOutline: true, uv: UV_DETAIL);
                        AddSymX(m, Prim.Sphere, 0.030f * L, 0.145f * L, 0.218f * L,
                            new Vector3(0.052f * L, 0.072f * L, 0.068f * L), noOutline: true, uv: UV_DETAIL);
                        if (cassowary)
                            m.Add(Prim.Cone, headC + new Vector3(0f, hr * 0.32f, -0.01f * L),
                                new Vector3(0.06f * L, 0.15f * L, 0.11f * L), pitch: -10f, uv: UV_LIMB);
                    }
                    else if (peacock)
                    {
                        // 孔雀标志性的上宽下窄白柄蓝顶扇形羽冠（还原 _east / _south / _north 头顶倒梯形白簇蓝冠）
                        var cBase = headC + new Vector3(0f, hr * 0.28f, -0.015f * L);
                        var cTop = AddSeg(m, cBase, 0.072f * L, 0.030f * L, 0.054f * L, -12f, uv: UV_BELLY, joint: false);
                        m.Add(Prim.Sphere, cTop - new Vector3(0f, 0.018f * L, 0f),
                            new Vector3(0.062f * L, 0.034f * L, 0.048f * L), pitch: -12f, noOutline: true, uv: UV_DETAIL);
                    }

                    // 尾羽（火鸡棕黄平底半圆扇尾 / 孔雀翠绿开屏高耸巨型圆扇尾 + 双圈眼斑 / 鸵鸟圆润白尾簇 / 普通鸟类上翘尾羽）
                    if (turkey || peacock)
                    {
                        float fanW = (peacock ? 0.68f : 0.66f) * L;
                        float fanH = (peacock ? 0.33f : 0.44f) * L;
                        float fanR = (peacock ? 0.32f : 0.35f) * L;
                        int fanMainUv = peacock ? UV_COAT : UV_BELLY;
                        float fanY = bh * (peacock ? 0.44f : 0.54f);
                        float fanZ = -bl * (peacock ? 0.26f : 0.26f);
                        float fanPitch = peacock ? -8f : -12f;
                        m.Add(Prim.Sphere, new Vector3(0f, fanY + 0.02f * L, fanZ),
                            new Vector3(fanW, fanH, 0.13f * L), pitch: fanPitch, uv: fanMainUv);
                        float[] fanAngles = { -68f, -36f, 36f, 68f };
                        for (int i = 0; i < fanAngles.Length; i++)
                        {
                            float ang = fanAngles[i];
                            m.Add(Prim.Sphere, new Vector3(0f, fanY + 0.02f * L, fanZ),
                                new Vector3(0.22f * L, fanR, 0.125f * L),
                                pitch: fanPitch, roll: ang, uv: fanMainUv);
                            m.Add(Prim.Sphere, new Vector3(0f, fanY + 0.02f * L, fanZ),
                                new Vector3(0.23f * L, fanR * 0.96f, 0.142f * L),
                                pitch: fanPitch, roll: ang, noOutline: true, uv: fanMainUv);
                        }
                        m.Add(Prim.Sphere, new Vector3(0f, fanY + 0.02f * L, fanZ),
                            new Vector3(fanW * 0.97f, fanH * 0.97f, 0.145f * L), pitch: fanPitch, noOutline: true, uv: fanMainUv);

                        if (peacock)
                        {
                            // 孔雀尾屏内外两圈放射眼斑（按椭球径向曲率收敛厚度，彻底消除侧视凸瘤且前后双面清晰呈现浅青外环与深蓝眼心）
                            float[] spotAngles = { -64f, -42f, -21f, 0f, 21f, 42f, 64f };
                            float[] spotRadii = { 0.14f * L, 0.23f * L };
                            for (int ir = 0; ir < spotRadii.Length; ir++)
                            {
                                float sr = spotRadii[ir];
                                float szOuter = (ir == 0 ? 0.148f : 0.126f) * L;
                                float szInner = (ir == 0 ? 0.152f : 0.130f) * L;
                                for (int ia = 0; ia < spotAngles.Length; ia++)
                                {
                                    if (ir == 0 && (ia == 0 || ia == spotAngles.Length - 1)) continue;
                                    var sp = new Vector3(0f, fanY + 0.02f * L, fanZ) + StepVec(sr, fanPitch, 0f, spotAngles[ia]);
                                    m.Add(Prim.Sphere, sp - new Vector3(0f, 0.028f * L, 0f),
                                        new Vector3(0.050f * L, 0.054f * L, szOuter), pitch: fanPitch, noOutline: true, uv: UV_BELLY);
                                    m.Add(Prim.Sphere, sp - new Vector3(0f, 0.020f * L, 0f),
                                        new Vector3(0.032f * L, 0.036f * L, szInner), pitch: fanPitch, noOutline: true, uv: UV_DETAIL);
                                }
                            }
                        }
                        else
                        {
                            m.Add(Prim.Sphere, new Vector3(0f, fanY, fanZ),
                                new Vector3(fanW * 0.66f, fanH * 0.68f, 0.175f * L), pitch: fanPitch, noOutline: true, uv: UV_LIMB);
                            for (int i = 0; i < fanAngles.Length; i++)
                            {
                                float ang = fanAngles[i];
                                m.Add(Prim.Sphere, new Vector3(0f, fanY, fanZ),
                                    new Vector3(0.18f * L, fanR * 0.68f, 0.175f * L),
                                    pitch: fanPitch, roll: ang, noOutline: true, uv: UV_LIMB);
                            }
                        }
                    }
                    else if (tall)
                    {
                        // 鸵鸟标志性后臀圆润雪白尾羽团（y>=0.02*L 严守接地底线，还原 _east 后臀白尾与 _north 底部三瓣白尾）
                        m.Add(Prim.Sphere, new Vector3(0f, 0.02f * L, -bl * 0.44f),
                            new Vector3(0.16f * L, 0.20f * L, 0.18f * L), pitch: -24f, uv: UV_BELLY);
                        AddSymX(m, Prim.Sphere, 0.08f * L, 0.05f * L, -bl * 0.40f,
                            new Vector3(0.14f * L, 0.16f * L, 0.16f * L), pitch: -20f, splay: 18f, uv: UV_BELLY, noOutline: true);
                    }
                    else
                    {
                        m.Add(Prim.Sphere, new Vector3(0f, bh * 0.24f, -bl * 0.36f),
                            new Vector3(0.18f * L, 0.16f * L, 0.20f * L), pitch: -24f, noOutline: true, uv: wingUv);
                        m.Add(Prim.Cone, new Vector3(0f, bh * 0.38f, -bl * 0.32f),
                            new Vector3(0.14f * L, 0.23f * L, 0.11f * L), pitch: -66f, uv: wingUv);
                    }
                    break;
                }

                case Arch.Penguin:
                {
                    // 帝企鹅修长梨形直立体态：宽圆下腹(y=0) + 向上平滑收窄的锥柱上躯干 + 高挑雪白肚皮自然衔接颌下金黄胸斑 + 头侧金黄耳斑 + 顺肩下垂流线鳍翅
                    float bw = 0.35f * L, bh = 0.50f * L, bl = 0.32f * L;
                    m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.01f * L),
                        new Vector3(bw, bh, bl), pitch: 8f, uv: UV_COAT);
                    // 向上平滑收窄的梨形上躯干（底径 0.26*L 完全内嵌于下腹与无描边背壳内，消除 _north 下背横线）
                    var torsoBase = new Vector3(0f, 0.20f * L, 0.005f * L);
                    AddSeg(m, torsoBase, 0.38f * L, 0.26f * L, 0.182f * L, 8f, uv: UV_COAT, joint: false);
                    m.Add(Prim.Sphere, new Vector3(0f, 0.10f * L, -0.004f * L),
                        new Vector3(0.33f * L, 0.42f * L, 0.31f * L), pitch: 8f, noOutline: true, uv: UV_COAT);

                    // 高挑饱满雪白肚皮 + 直抵颌下的金黄上胸斑（白腹顶弧自然压住黄斑下缘，上端直触下颌无黑隙）
                    m.Add(Prim.Sphere, new Vector3(0f, 0.35f * L, 0.036f * L),
                        new Vector3(0.182f * L, 0.22f * L, 0.235f * L), pitch: 14f, noOutline: true, uv: UV_HEAD);
                    m.Add(Prim.Sphere, new Vector3(0f, 0.015f * L, 0.048f * L),
                        new Vector3(0.268f * L, 0.45f * L, 0.285f * L), pitch: 8f, noOutline: true, uv: UV_BELLY);
                    m.Add(Prim.Sphere, new Vector3(0f, 0.16f * L, 0.062f * L),
                        new Vector3(0.225f * L, 0.27f * L, 0.255f * L), pitch: 9f, noOutline: true, uv: UV_BELLY);

                    // 顺肩贴侧下垂的流线型单节鳍翅（底端锚定于外侧翅尖 x=±0.235*L，splay=-24° 使上端向内收入肩部）
                    AddSymX(m, Prim.Sphere, 0.235f * L, 0.13f * L, 0.005f * L,
                        new Vector3(0.082f * L, 0.34f * L, 0.112f * L), pitch: 12f, splay: -24f, uv: UV_COAT);
                    // 背部底端内收小尾尖（完全收纳于侧视轮廓内，仅在 _north 底部显露小尾尖）
                    m.Add(Prim.Sphere, new Vector3(0f, 0.01f * L, -0.085f * L),
                        new Vector3(0.052f * L, 0.08f * L, 0.065f * L), pitch: -18f, uv: UV_COAT);
                    bodyCenterY = 0.35f * L;
                    bodyHalfW = bw * 0.5f;

                    float hr = 0.195f * L;
                    var headC = new Vector3(0f, 0.62f * L, 0.034f * L);
                    m.Add(Prim.Sphere, headC - new Vector3(0f, hr * 0.48f, 0f),
                        new Vector3(hr * 0.96f, hr * 0.92f, hr * 1.00f), pitch: 6f, uv: UV_COAT);
                    // 后颈中央窄幅无描边过渡桥（遮蔽头底中线反绕壳，同时不遮挡两侧金黄耳斑）
                    m.Add(Prim.Sphere, new Vector3(0f, 0.44f * L, 0.008f * L),
                        new Vector3(0.125f * L, 0.20f * L, 0.195f * L), pitch: 6f, noOutline: true, uv: UV_COAT);
                    // 头后侧左右金黄耳颈斑（还原 _east 颊后金黄弯斑与 _north 头部两侧金黄竖斑）
                    AddSymX(m, Prim.Sphere, 0.064f * L, 0.495f * L, 0.002f * L,
                        new Vector3(0.068f * L, 0.125f * L, 0.112f * L), pitch: 8f, splay: -6f, uv: UV_HEAD, noOutline: true);
                    AddSymX(m, Prim.Sphere, hr * 0.34f, headC.y - hr * 0.06f, headC.z + hr * 0.24f,
                        new Vector3(0.042f * L, 0.042f * L, 0.042f * L), uv: UV_EYE, noOutline: true);
                    // 近水平修长橘粉鸟喙（保留外描边 + 无描边宽喙基遮蔽根部反绕壳）
                    m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.18f, hr * 0.34f),
                        new Vector3(0.086f * L, 0.066f * L, 0.088f * L), pitch: 8f, noOutline: true, uv: UV_DETAIL);
                    AddSeg(m, headC + new Vector3(0f, -hr * 0.06f, hr * 0.38f),
                        0.145f * L, 0.048f * L, 0.014f * L, 94f, uv: UV_DETAIL, joint: false);
                    break;
                }

                case Arch.ShellCreature:
                {
                    bool sea = Has(n, "seaturtle");
                    bool armadillo = Has(n, "armadillo");
                    bool crab = Has(n, "crab");
                    bool snail = Has(n, "snail", "slug");

                    float sl = 0.68f * L, sh = (sea ? 0.32f : 0.40f) * L, sw = CalibrateWidth(n, sl, 0.52f * L);
                    bodyCenterY = sh * 0.5f;
                    bodyHalfW = sw * 0.5f;

                    if (snail)
                    {
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.04f * L),
                            new Vector3(0.34f * L, 0.16f * L, 0.78f * L), uv: UV_BELLY);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.02f * L, 0.26f * L),
                            new Vector3(0.28f * L, 0.20f * L, 0.36f * L), noOutline: true, uv: UV_HEAD);
                        m.Add(Prim.Cone, new Vector3(0f, 0.06f * L, -0.30f * L),
                            new Vector3(0.22f * L, 0.22f * L, 0.14f * L), pitch: -96f, uv: UV_BELLY);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.10f * L, -0.06f * L),
                            new Vector3(0.46f * L, 0.46f * L, 0.50f * L), uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.28f * L, -0.08f * L),
                            new Vector3(0.32f * L, 0.32f * L, 0.36f * L), noOutline: true, uv: UV_DETAIL);
                        AddSymX(m, Prim.Sphere, 0.16f * L, 0.16f * L, -0.06f * L,
                            new Vector3(0.22f * L, 0.30f * L, 0.32f * L), uv: UV_DETAIL, noOutline: true);
                        var eL = new Vector3(-0.07f * L, 0.16f * L, 0.36f * L);
                        var eR = new Vector3(0.07f * L, 0.16f * L, 0.36f * L);
                        AddSymSeg(m, ref eL, ref eR, 0.15f * L, 0.035f * L, 0.025f * L, 28f, 18f, 0f, UV_HEAD);
                        m.Add(Prim.Sphere, eL, new Vector3(0.055f * L, 0.055f * L, 0.055f * L), uv: UV_EYE, noOutline: true);
                        m.Add(Prim.Sphere, eR, new Vector3(0.055f * L, 0.055f * L, 0.055f * L), uv: UV_EYE, noOutline: true);
                        var fL = new Vector3(-0.06f * L, 0.08f * L, 0.40f * L);
                        var fR = new Vector3(0.06f * L, 0.08f * L, 0.40f * L);
                        AddSymSeg(m, ref fL, ref fR, 0.09f * L, 0.028f * L, 0.012f * L, 72f, 24f, 0f, UV_HEAD, joint: false);
                    }
                    else if (crab)
                    {
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.04f * L),
                            new Vector3(sw, sh * 1.05f, sl * 0.92f), uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, sh * 0.22f, -0.06f * L),
                            new Vector3(sw * 0.78f, sh * 0.78f, sl * 0.72f), noOutline: true, uv: UV_COAT);
                        AddSymX(m, Prim.Sphere, sw * 0.36f, 0.02f * L, -0.02f * L,
                            new Vector3(0.20f * L, sh * 0.68f, sl * 0.70f), uv: UV_LIMB, noOutline: true);
                        AddSymX(m, Prim.Sphere, 0.10f * L, sh * 0.42f, 0.22f * L,
                            new Vector3(0.055f * L, 0.055f * L, 0.055f * L), uv: UV_EYE, noOutline: true);
                        var cL = new Vector3(-0.18f * L, 0.08f * L, 0.20f * L);
                        var cR = new Vector3(0.18f * L, 0.08f * L, 0.20f * L);
                        AddSymSeg(m, ref cL, ref cR, 0.18f * L, 0.10f * L, 0.13f * L, 82f, 22f, 0f, UV_DETAIL);
                        AddSymSeg(m, ref cL, ref cR, 0.14f * L, 0.13f * L, 0.04f * L, 90f, -24f, 0f, UV_DETAIL);
                    }
                    else
                    {
                        // 陆龟 / 海龟 / 犰狳
                        float shellL = (sea ? 0.62f : armadillo ? 0.66f : 0.56f) * L;
                        float shellH = (sea ? 0.32f : armadillo ? 0.38f : 0.37f) * L;
                        float shellW = CalibrateWidth(n, shellL, (sea ? 0.50f : 0.47f) * L);
                        float shellPitch = sea ? -8f : 0f;
                        int shellMainUv = sea ? UV_DETAIL : UV_COAT;
                        bodyCenterY = shellH * 0.5f;
                        bodyHalfW = shellW * 0.5f;

                        if (sea)
                        {
                            // 海龟前胸下沿向后斜掠的流线扁鳍（与底甲缘顺滑融为一体，_east 展现后掠鳍尖，_south 不鼓出独立圆球，肩前无描边桥消除颈后竖黑线）
                            AddSymX(m, Prim.Sphere, shellW * 0.32f, 0f, 0.05f * L,
                                new Vector3(0.085f * L, 0.075f * L, 0.21f * L), yaw: 14f, pitch: -16f, splay: -14f, uv: UV_LIMB);
                            AddSymX(m, Prim.Sphere, shellW * 0.30f, 0.015f * L, 0.12f * L,
                                new Vector3(0.11f * L, 0.095f * L, 0.18f * L), yaw: 12f, pitch: -10f, uv: UV_LIMB, noOutline: true);
                        }

                        // 浅米黄贴地薄甲缘 (UV_BELLY) + 单一完整水平基座圆拱背甲（主壳 pitch=0 根除 _north 底甲横黑线，海龟前高后低斜坡由无描边背拱表达）
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.01f * L),
                            new Vector3(shellW * (sea ? 0.88f : 0.82f), 0.095f * L, shellL * 0.82f), uv: UV_BELLY);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.015f * L, -0.01f * L),
                            new Vector3(shellW, shellH, shellL), uv: shellMainUv);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.038f * L, -0.01f * L),
                            new Vector3(shellW * 0.85f, 0.15f * L, shellL * 0.85f), noOutline: true, uv: shellMainUv);
                        if (sea)
                        {
                            // 海龟前高后低的背甲流线拱与背脊浅暖棕盾片高光带（还原 _east / _south / _north 背甲斜度与明暗层次）
                            m.Add(Prim.Sphere, new Vector3(0f, 0.032f * L, 0.015f * L),
                                new Vector3(shellW * 0.96f, shellH * 1.00f, shellL * 0.92f), pitch: -6f, noOutline: true, uv: shellMainUv);
                            m.Add(Prim.Sphere, new Vector3(0f, shellH * 0.26f, 0.01f * L),
                                new Vector3(shellW * 0.72f, shellH * 0.76f, shellL * 0.76f), pitch: -6f, noOutline: true, uv: UV_COAT);
                        }

                        float hr = (armadillo ? 0.19f : sea ? 0.150f : 0.155f) * L;
                        var headC = new Vector3(0f, (sea ? 0.155f : 0.175f) * L, shellL * (sea ? 0.54f : 0.56f));
                        if (armadillo)
                        {
                            BuildHead(m, headC, hr, hr * 0.95f, hr * 0.64f, hr * 0.54f, UV_HEAD, UV_HEAD, UV_BELLY);
                            BuildEars(m, headC, hr, 0.045f * L, 0.10f * L, earUv: UV_HEAD);
                        }
                        else
                        {
                            // 龟颈与平视微倾龟头（内嵌描边核 + 同心无描边表皮，使描边壳外沿 Z 深度全程大于背甲前表，彻底根除 _south 黑圈内棕缝）
                            var neckBase = new Vector3(0f, (sea ? 0.085f : 0.095f) * L, shellL * 0.30f);
                            AddSeg(m, neckBase, 0.17f * L, 0.126f * L, 0.128f * L, sea ? 68f : 64f, uv: UV_HEAD, joint: false, noOutline: true);
                            m.Add(Prim.Sphere, headC - new Vector3(0f, hr * 0.38f, 0f),
                                new Vector3(hr * 0.72f, hr * 0.72f, hr * 0.94f), pitch: 4f, uv: UV_HEAD);
                            m.Add(Prim.Sphere, headC - new Vector3(0f, hr * 0.45f, 0f),
                                new Vector3(hr * 0.88f, hr * 0.86f, hr * 1.08f), pitch: 4f, noOutline: true, uv: UV_HEAD);
                            if (sea)
                            {
                                // 海龟头顶与颊侧的深棕鳞斑（还原 _east / _south 头部红棕斑块）
                                m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.18f, hr * 0.04f),
                                    new Vector3(hr * 0.68f, hr * 0.62f, hr * 0.82f), pitch: 6f, noOutline: true, uv: UV_DETAIL);
                            }
                            AddSymX(m, Prim.Sphere, hr * 0.34f, headC.y - hr * 0.02f, headC.z + hr * 0.22f,
                                new Vector3(hr * 0.20f, hr * 0.18f, hr * 0.20f), uv: UV_EYE, noOutline: true);
                        }
                        // 尾尖（仅陆龟与犰狳有后伸尾尖，海龟原版 _north / _east 尾部完全内收无外伸尾刺）
                        if (!sea)
                        {
                            AddSymX(m, Prim.Sphere, shellW * 0.22f, 0.055f * L, 0.05f * L,
                                new Vector3(shellW * 0.42f, shellH * 0.68f, shellL * 0.42f), noOutline: true, uv: shellMainUv);
                            AddSymX(m, Prim.Sphere, shellW * 0.20f, 0.050f * L, -0.07f * L,
                                new Vector3(shellW * 0.40f, shellH * 0.64f, shellL * 0.40f), noOutline: true, uv: shellMainUv);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.020f * L, -shellL * 0.37f),
                                new Vector3(0.065f * L, 0.058f * L, 0.070f * L), noOutline: true, uv: UV_BELLY);
                            AddSeg(m, new Vector3(0f, 0.035f * L, -shellL * 0.37f),
                                (armadillo ? 0.22f : 0.095f) * L, 0.040f * L, 0.014f * L, armadillo ? -98f : -120f, uv: UV_HEAD, joint: false);
                        }
                    }
                    break;
                }

                case Arch.ReptileAmphibian:
                {
                    bool frog = Has(n, "frog", "toad");
                    bool pinniped = Has(n, "walrus", "seal", "sealion");
                    bool gator = Has(n, "alligator", "croc", "gator");
                    bool iguana = Has(n, "iguana");

                    if (frog)
                    {
                        float bl = 0.54f * L, bh = 0.36f * L, bw = 0.48f * L;
                        bodyCenterY = 0.18f * L;
                        bodyHalfW = bw * 0.52f;
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.02f * L),
                            new Vector3(bw, bh, bl), pitch: -10f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.01f * L, 0.10f * L),
                            new Vector3(bw * 0.86f, bh * 0.76f, bl * 0.68f), noOutline: true, uv: UV_BELLY);
                        AddSymX(m, Prim.Sphere, bw * 0.38f, 0f, -0.06f * L,
                            new Vector3(0.18f * L, 0.22f * L, 0.30f * L), yaw: 12f, uv: UV_COAT, noOutline: true);
                        AddSymX(m, Prim.Sphere, bw * 0.26f, 0.18f * L, -0.02f * L,
                            new Vector3(0.10f * L, 0.12f * L, 0.28f * L), pitch: -10f, uv: UV_DETAIL, noOutline: true);

                        float hr = 0.28f * L;
                        var headC = new Vector3(0f, 0.22f * L, 0.20f * L);
                        m.Add(Prim.Sphere, headC - new Vector3(0f, hr * 0.45f, 0f),
                            new Vector3(hr * 1.15f, hr * 0.72f, hr * 0.95f), pitch: 14f, uv: UV_HEAD);
                        AddSymX(m, Prim.Sphere, hr * 0.32f, headC.y - hr * 0.40f, headC.z + hr * 0.06f,
                            new Vector3(hr * 0.52f, hr * 0.48f, hr * 0.56f), pitch: 14f, uv: UV_HEAD, noOutline: true);
                        m.Add(Prim.Sphere, headC + new Vector3(0f, -hr * 0.58f, hr * 0.12f),
                            new Vector3(hr * 0.88f, hr * 0.56f, hr * 0.76f), pitch: 14f, noOutline: true, uv: UV_BELLY);
                        AddSymX(m, Prim.Sphere, hr * 0.34f, headC.y + 0.02f * L, headC.z + 0.04f * L,
                            new Vector3(0.10f * L, 0.10f * L, 0.10f * L), uv: UV_DETAIL);
                        AddSymX(m, Prim.Sphere, hr * 0.36f, headC.y + 0.04f * L, headC.z + 0.07f * L,
                            new Vector3(0.06f * L, 0.06f * L, 0.06f * L), uv: UV_EYE, noOutline: true);
                    }
                    else if (pinniped)
                    {
                        bool walrus = Has(n, "walrus");
                        float bl = 0.68f * L, bh = 0.38f * L, bw = 0.42f * L;
                        bodyCenterY = 0.20f * L;
                        bodyHalfW = bw * 0.5f;
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0f), new Vector3(bw, bh, bl), uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.02f * L, 0.14f * L),
                            new Vector3(bw * 0.92f, bh * 1.04f, bl * 0.56f), pitch: -8f, noOutline: true, uv: UV_COAT);
                        AddSymX(m, Prim.Sphere, bw * 0.48f, 0f, 0.12f * L,
                            new Vector3(0.24f * L, 0.06f * L, 0.16f * L), yaw: -24f, uv: UV_LIMB);
                        AddSymX(m, Prim.Sphere, 0.10f * L, 0f, -bl * 0.52f,
                            new Vector3(0.14f * L, 0.06f * L, 0.22f * L), yaw: 22f, uv: UV_LIMB);

                        float hr = 0.25f * L;
                        var headC = new Vector3(0f, 0.28f * L, 0.28f * L);
                        BuildHead(m, headC, hr, hr * 0.68f, hr * 0.76f, hr * 0.58f, UV_HEAD, UV_BELLY, UV_BELLY);
                        if (walrus)
                            AddSymX(m, Prim.Cone, hr * 0.26f, headC.y - hr * 0.42f, headC.z + hr * 0.44f,
                                new Vector3(0.055f * L, 0.20f * L, 0.055f * L), pitch: 172f, uv: UV_DETAIL);
                    }
                    else
                    {
                        // 鬣蜥 / 巨蜥 / 鳄鱼
                        if (!gator)
                        {
                            // 鬣蜥：前胸贴地 + 肩背至尾尖直线斜坡锥躯 + 低矮贴地腹球 + 楔形蜥蜴头 + 颌下垂皮(dewlap) + 全程均匀显露的11枚锯齿绿背棘
                            float bh = 0.182f * L;
                            float bw = CalibrateWidth(n, 0.56f * L, 0.21f * L);
                            bodyCenterY = bh * 0.5f;
                            bodyHalfW = bw * 0.5f;

                            // 前肩胸球(z=+0.06*L, 前缘仅至+0.20*L) + 低矮后腹贴地球 + 肩背至尾尖直线渐细长锥
                            m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.06f * L),
                                new Vector3(bw, bh, 0.28f * L), uv: UV_COAT);
                            m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.05f * L),
                                new Vector3(bw * 0.90f, 0.132f * L, 0.32f * L), uv: UV_COAT);
                            AddSeg(m, new Vector3(0f, 0.098f * L, 0.10f * L),
                                0.54f * L, bw * 0.88f, 0.022f * L, -96.2f, uv: UV_COAT, joint: false);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.004f * L, 0.02f * L),
                                new Vector3(bw * 0.96f, 0.155f * L, 0.32f * L), noOutline: true, uv: UV_COAT);

                            // 咽下垂皮 (dewlap，前缘收在头心之后以免切割 _south 下颌描边) + 严格同心(pitch=0)的蜥蜴头
                            var headC = new Vector3(0f, 0.168f * L, 0.275f * L);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.036f * L, 0.205f * L),
                                new Vector3(0.076f * L, 0.118f * L, 0.115f * L), noOutline: true, uv: UV_COAT);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.095f * L, 0.20f * L),
                                new Vector3(0.125f * L, 0.115f * L, 0.14f * L), noOutline: true, uv: UV_HEAD);
                            m.Add(Prim.Sphere, headC - new Vector3(0f, 0.046f * L, 0f),
                                new Vector3(0.116f * L, 0.092f * L, 0.172f * L), uv: UV_HEAD);
                            m.Add(Prim.Sphere, headC - new Vector3(0f, 0.057f * L, 0f),
                                new Vector3(0.140f * L, 0.114f * L, 0.184f * L), noOutline: true, uv: UV_HEAD);
                            AddSymX(m, Prim.Sphere, 0.050f * L, headC.y - 0.006f * L, headC.z + 0.036f * L,
                                new Vector3(0.030f * L, 0.028f * L, 0.038f * L), uv: UV_EYE, noOutline: true);

                            // 沿颈背至尾背直线斜坡连续排布的 11 枚紧密锯齿背棘
                            for (int i = 0; i < 11; i++)
                            {
                                float t = i / 10f;
                                float sz = Mathf.Lerp(0.16f, -0.35f, t) * L;
                                float surfY = (Mathf.Lerp(0.180f, 0.068f, t) + Mathf.Sin(t * Mathf.PI) * 0.012f) * L;
                                float spH = Mathf.Lerp(0.068f, 0.045f, t) * L;
                                m.Add(Prim.Cone, new Vector3(0f, surfY - 0.016f * L, sz),
                                    new Vector3(0.044f * L, spH, 0.046f * L), pitch: -18f, noOutline: true, uv: UV_COAT);
                            }
                        }
                        else
                        {
                            // 短吻鳄：低矮修长贴地躯干 + 暖米黄(UV_DETAIL)咽喉与腹底 + 前胸贴地短足 + 无内黑环凸起眼座 + 修长扁平鳄吻 + 一体平滑渐细长尾
                            float bh = 0.172f * L;
                            float bw = CalibrateWidth(n, 0.56f * L, 0.24f * L);
                            bodyCenterY = bh * 0.5f;
                            bodyHalfW = bw * 0.5f;

                            // 前肩胸主球(z=+0.05*L) + 低矮贴地后腹球 + 从肩背(z=+0.06*L)顶面齐平连续渐细至尾尖的一体长尾锥（彻底消除侧视臀部台阶）
                            m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.05f * L),
                                new Vector3(bw, bh, 0.30f * L), uv: UV_COAT);
                            m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.04f * L),
                                new Vector3(bw * 0.90f, 0.122f * L, 0.34f * L), uv: UV_COAT);
                            AddSeg(m, new Vector3(0f, 0.092f * L, 0.06f * L),
                                0.51f * L, 0.160f * L, 0.022f * L, -97.8f, uv: UV_COAT, joint: false);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.005f * L, 0.01f * L),
                                new Vector3(bw * 0.96f, 0.148f * L, 0.34f * L), noOutline: true, uv: UV_COAT);

                            // 暖米黄(UV_DETAIL)腹底 + 前胸腹底短足凸起（还原 _east 咽腹之间蓝灰前足分隔，不越出 _south 侧轮廓）
                            m.Add(Prim.Sphere, new Vector3(0f, 0.002f * L, -0.02f * L),
                                new Vector3(bw * 0.75f, 0.055f * L, 0.32f * L), noOutline: true, uv: UV_DETAIL);
                            m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.12f * L),
                                new Vector3(bw * 0.76f, 0.052f * L, 0.088f * L), noOutline: true, uv: UV_COAT);

                            // 扁平头颅与宽阔厚实的经典 U 形长鳄吻 (顺滑渐变无阶梯断层，正视宽厚有力再现短吻鳄标志性强壮吻部)
                            m.Add(Prim.Sphere, new Vector3(0f, 0.040f * L, 0.185f * L),
                                new Vector3(0.155f * L, 0.068f * L, 0.18f * L), uv: UV_COAT);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.036f * L, 0.185f * L),
                                new Vector3(0.165f * L, 0.076f * L, 0.19f * L), noOutline: true, uv: UV_COAT);
                            // 突出凸起的立体眼眶座与冷酷侧眼
                            AddSymX(m, Prim.Sphere, 0.042f * L, 0.096f * L, 0.208f * L,
                                new Vector3(0.038f * L, 0.036f * L, 0.055f * L), uv: UV_COAT);
                            AddSymX(m, Prim.Sphere, 0.046f * L, 0.104f * L, 0.215f * L,
                                new Vector3(0.022f * L, 0.022f * L, 0.030f * L), uv: UV_EYE, noOutline: true);

                            // 宽阔厚实的中前吻部体块（由后至前平滑收敛：宽 0.142L -> 0.120L，高 0.054L -> 0.044L，顺滑无阶梯）
                            m.Add(Prim.Sphere, new Vector3(0f, 0.036f * L, 0.260f * L),
                                new Vector3(0.142f * L, 0.054f * L, 0.18f * L), uv: UV_COAT, noOutline: true);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.033f * L, 0.335f * L),
                                new Vector3(0.120f * L, 0.044f * L, 0.15f * L), uv: UV_COAT, noOutline: true);
                            // 吻端圆滑厚实 U 形收口（宽 0.104L，厚 0.038L，正视饱满厚实，完全消除单薄平铲感）
                            m.Add(Prim.Sphere, new Vector3(0f, 0.030f * L, 0.385f * L),
                                new Vector3(0.104f * L, 0.038f * L, 0.072f * L), uv: UV_COAT);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.030f * L, 0.385f * L),
                                new Vector3(0.110f * L, 0.042f * L, 0.078f * L), noOutline: true, uv: UV_COAT);

                            // 鼻梁平滑纵向连接体与米黄下颌平滑底板
                            m.Add(Prim.Sphere, new Vector3(0f, 0.038f * L, 0.275f * L),
                                new Vector3(0.130f * L, 0.048f * L, 0.24f * L), pitch: 1f, uv: UV_COAT);
                            m.Add(Prim.Sphere, new Vector3(0f, 0.020f * L, 0.265f * L),
                                new Vector3(0.096f * L, 0.026f * L, 0.20f * L), pitch: 1f, noOutline: true, uv: UV_DETAIL);
                        }
                    }
                    break;
                }

                case Arch.Serpent:
                {
                    // 眼镜蛇：同心双层无节缝 S 形盘蛇管 + 自然弧线后仰昂立曲颈 + 立体宽展盾型兜帽 + 冠顶顺滑衔接蛇头
                    bodyCenterY = 0.16f * L;
                    bodyHalfW = 0.18f * L;

                    // 1. 底盘阿基米德平滑流线螺旋（几何连续无折角，从尾尖平滑盘绕至昂起曲颈）
                    int numSpiral = 18;
                    float turns = 1.30f;
                    var pts = new List<Vector3>(numSpiral + 4);
                    var diams = new List<float>(numSpiral + 4);

                    for (int i = 0; i < numSpiral; i++)
                    {
                        float t = (float)i / (numSpiral - 1);
                        float angle = -1.1f + t * Mathf.PI * 2f * turns;
                        float r = (0.24f - t * 0.13f) * L;
                        float x = Mathf.Cos(angle) * r;
                        float z = Mathf.Sin(angle) * r - 0.04f * L;
                        float y = (0.015f + t * 0.110f) * L;
                        pts.Add(new Vector3(x, y, z));
                        diams.Add((0.032f + t * 0.052f) * L);
                    }

                    // 昂起曲颈段（平滑连接底盘，先向前探出、再优雅向后仰、头部向前探视的立体S形微拱）
                    var neckNodes = new (Vector3 p, float d)[]
                    {
                        (new Vector3( 0.010f * L, 0.145f * L,  0.085f * L), 0.068f * L),
                        (new Vector3( 0.000f * L, 0.210f * L,  0.045f * L), 0.062f * L),
                        (new Vector3( 0.000f * L, 0.275f * L, -0.005f * L), 0.056f * L),
                        (new Vector3( 0.000f * L, 0.335f * L,  0.025f * L), 0.050f * L),
                    };
                    for (int i = 0; i < neckNodes.Length; i++)
                    {
                        pts.Add(neckNodes[i].p);
                        diams.Add(neckNodes[i].d);
                    }

                    for (int i = 0; i < pts.Count; i++)
                    {
                        float ds = diams[i];
                        m.Add(Prim.Sphere, pts[i] - new Vector3(0f, ds * 0.44f, 0f),
                            new Vector3(ds * 0.98f, ds * 0.98f, ds * 0.98f), noOutline: true, uv: UV_COAT);
                        if (i < pts.Count - 1)
                        {
                            Vector3 d = pts[i + 1] - pts[i];
                            float segLen = d.magnitude;
                            float segPitch = Mathf.Acos(Mathf.Clamp(d.y / segLen, -1f, 1f)) * Mathf.Rad2Deg;
                            float segYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                            float dc = Mathf.Max(0.014f * L, ds - 0.026f * L);
                            float dcNext = Mathf.Max(0.014f * L, diams[i + 1] - 0.026f * L);
                            AddSeg(m, pts[i], segLen, dc, dcNext, segPitch, segYaw, 0f, UV_COAT, false);
                            m.Add(Prim.Cylinder, pts[i], new Vector3(ds, segLen, ds),
                                yaw: segYaw, pitch: segPitch, taper: diams[i + 1] / ds, noOutline: true, uv: UV_COAT);
                        }
                    }

                    // 2. 原版 2D 贴图完全无宽展兜帽，颈部平顺挺立与躯干等粗，颈前浅腹鳞带自然顺延
                    m.Add(Prim.Sphere, new Vector3(0f, 0.085f * L, 0.072f * L),
                        new Vector3(0.048f * L, 0.22f * L, 0.048f * L), pitch: -14f, noOutline: true, uv: UV_HEAD);

                    // 3. 顺滑衔接的精炼卵圆形小蛇头（完全还原原版细长平顺小蛇头，无横展翼片）
                    var headC = new Vector3(0f, 0.335f * L, 0.042f * L);
                    m.Add(Prim.Sphere, headC - new Vector3(0f, 0.018f * L, 0f),
                        new Vector3(0.052f * L, 0.042f * L, 0.076f * L), uv: UV_COAT);
                    m.Add(Prim.Sphere, headC - new Vector3(0f, 0.022f * L, 0f),
                        new Vector3(0.058f * L, 0.046f * L, 0.082f * L), noOutline: true, uv: UV_COAT);
                    m.Add(Prim.Sphere, headC + new Vector3(0f, -0.020f * L, 0.006f * L),
                        new Vector3(0.048f * L, 0.034f * L, 0.068f * L), noOutline: true, uv: UV_HEAD);
                    AddSymX(m, Prim.Sphere, 0.024f * L, headC.y - 0.002f * L, headC.z + 0.016f * L,
                        new Vector3(0.016f * L, 0.016f * L, 0.018f * L), uv: UV_EYE, noOutline: true);
                    break;
                }

                case Arch.Insectoid:
                {
                    bool spelopede = Has(n, "spelopede");
                    bool scarab = Has(n, "scarab", "beetle");
                    bodyCenterY = 0.20f * L;
                    bodyHalfW = 0.25f * L;

                    if (scarab)
                    {
                        // 巨型圣甲虫 (megascarab)：严格还原原版紧凑平底两段式甲虫 —— 高拱陡尾后鞘翅 + 紧嵌前倾头胸盾 + 平整腹底板 + 外露单对黑圆眼 + 内拢上钩前螯
                        bodyCenterY = 0.10f * L;
                        bodyHalfW = 0.11f * L;

                        // 1. 贯穿前后段的贴地腹胸底板（还原 _east 平整贴地底缘与胸腹交界三角侧甲，消除哑铃状底凹）
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.01f * L),
                            new Vector3(0.155f * L, 0.088f * L, 0.275f * L), uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.01f * L),
                            new Vector3(0.172f * L, 0.102f * L, 0.292f * L), noOutline: true, uv: UV_COAT);

                        // 2. 后段：高拱圆润后鞘翅甲壳（中心移至 z=-0.055*L 与前胸紧密嵌合）
                        m.Add(Prim.Sphere, new Vector3(0f, 0.008f * L, -0.055f * L),
                            new Vector3(0.184f * L, 0.178f * L, 0.215f * L), pitch: -10f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.055f * L),
                            new Vector3(0.204f * L, 0.196f * L, 0.235f * L), pitch: -10f, noOutline: true, uv: UV_COAT);

                        // 3. 前段：紧嵌于后鞘翅前方的圆润前倾头胸盾（无多余颊刺）
                        m.Add(Prim.Sphere, new Vector3(0f, 0.006f * L, 0.075f * L),
                            new Vector3(0.144f * L, 0.135f * L, 0.175f * L), pitch: 16f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.075f * L),
                            new Vector3(0.164f * L, 0.154f * L, 0.192f * L), pitch: 16f, noOutline: true, uv: UV_COAT);

                        // 4. 外露单对黑圆复眼（位于前吻收缩面外侧、赤道轮廓内侧，在 _south 中不触碰头廓黑线）
                        AddSymX(m, Prim.Sphere, 0.044f * L, 0.065f * L, 0.156f * L,
                            new Vector3(0.036f * L, 0.036f * L, 0.036f * L), uv: UV_EYE, noOutline: true);

                        // 5. 单尖上钩镰刀前螯 + 正中口器小尖突（消除大颚双尖分叉）
                        AddSymX(m, Prim.Sphere, 0.050f * L, 0.024f * L, 0.148f * L,
                            new Vector3(0.044f * L, 0.044f * L, 0.085f * L), yaw: -12f, pitch: 16f, uv: UV_COAT, noOutline: true);
                        AddSymX(m, Prim.Cone, 0.048f * L, 0.036f * L, 0.162f * L,
                            new Vector3(0.018f * L, 0.105f * L, 0.038f * L), pitch: 84f, yaw: -20f, uv: UV_COAT);
                        AddSymX(m, Prim.Cone, 0.048f * L, 0.036f * L, 0.156f * L,
                            new Vector3(0.040f * L, 0.112f * L, 0.044f * L), pitch: 84f, yaw: -20f, uv: UV_COAT, noOutline: true);
                        m.Add(Prim.Cone, new Vector3(0f, 0.040f * L, 0.162f * L),
                            new Vector3(0.022f * L, 0.052f * L, 0.022f * L), pitch: 132f, noOutline: true, uv: UV_COAT);
                    }
                    else if (spelopede)
                    {
                        // 洞穴虫 (spelopede)：严格还原原版三节虫体 —— 单尖前钩水滴尾腹 + 紧叠中胸节 + 平底斜坡头甲与复眼簇 + 单尖上钩镰刀大颚
                        bodyCenterY = 0.14f * L;
                        bodyHalfW = 0.11f * L;

                        // 1. 后段：膨大基球 + 后仰圆台过渡 + 单一朝前上方钩尖尾锥（彻底消除尾尖分叉与 _north 双球断层）
                        m.Add(Prim.Sphere, new Vector3(0f, 0.028f * L, -0.155f * L),
                            new Vector3(0.168f * L, 0.148f * L, 0.175f * L), pitch: -18f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.018f * L, -0.155f * L),
                            new Vector3(0.188f * L, 0.168f * L, 0.195f * L), pitch: -18f, noOutline: true, uv: UV_COAT);
                        AddSeg(m, new Vector3(0f, 0.095f * L, -0.165f * L),
                            0.105f * L, 0.154f * L, 0.096f * L, -18f, 0f, 0f, UV_COAT, false);
                        m.Add(Prim.Cylinder, new Vector3(0f, 0.095f * L, -0.165f * L),
                            new Vector3(0.176f * L, 0.105f * L, 0.168f * L), pitch: -18f, taper: 0.64f, noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.135f * L, -0.195f * L),
                            new Vector3(0.118f * L, 0.112f * L, 0.118f * L), noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Cone, new Vector3(0f, 0.178f * L, -0.202f * L),
                            new Vector3(0.088f * L, 0.125f * L, 0.095f * L), pitch: 18f, uv: UV_COAT);
                        m.Add(Prim.Cone, new Vector3(0f, 0.170f * L, -0.202f * L),
                            new Vector3(0.110f * L, 0.132f * L, 0.114f * L), pitch: 18f, noOutline: true, uv: UV_COAT);

                        // 2. 中段：紧密斜叠的收腰胸节
                        m.Add(Prim.Sphere, new Vector3(0f, 0.008f * L, -0.015f * L),
                            new Vector3(0.140f * L, 0.142f * L, 0.190f * L), pitch: -24f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.015f * L),
                            new Vector3(0.160f * L, 0.160f * L, 0.208f * L), pitch: -24f, noOutline: true, uv: UV_COAT);

                        // 3. 前段：平底前倾头胸甲 + 颊侧尖角 + 正中暗色鼻脊 + 表面外露三眼簇 + 上前弯触角刺 + 单尖前伸上钩镰刀大颚
                        m.Add(Prim.Sphere, new Vector3(0f, 0.006f * L, 0.115f * L),
                            new Vector3(0.152f * L, 0.146f * L, 0.190f * L), pitch: 18f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.115f * L),
                            new Vector3(0.172f * L, 0.164f * L, 0.210f * L), pitch: 18f, noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, 0.155f * L),
                            new Vector3(0.138f * L, 0.088f * L, 0.175f * L), pitch: 6f, noOutline: true, uv: UV_COAT);
                        AddSymX(m, Prim.Cone, 0.065f * L, 0.060f * L, 0.130f * L,
                            new Vector3(0.044f * L, 0.056f * L, 0.054f * L), pitch: 12f, splay: 68f, uv: UV_COAT, noOutline: true);

                        // 面部正中深色纵脊(UV_DETAIL)与两侧醒目三眼簇
                        m.Add(Prim.Sphere, new Vector3(0f, 0.042f * L, 0.205f * L),
                            new Vector3(0.028f * L, 0.070f * L, 0.045f * L), pitch: 18f, noOutline: true, uv: UV_DETAIL);
                        AddSymX(m, Prim.Sphere, 0.048f * L, 0.075f * L, 0.192f * L,
                            new Vector3(0.038f * L, 0.038f * L, 0.038f * L), uv: UV_EYE, noOutline: true);
                        AddSymX(m, Prim.Sphere, 0.054f * L, 0.115f * L, 0.175f * L,
                            new Vector3(0.026f * L, 0.026f * L, 0.026f * L), uv: UV_EYE, noOutline: true);
                        AddSymX(m, Prim.Sphere, 0.072f * L, 0.090f * L, 0.165f * L,
                            new Vector3(0.024f * L, 0.024f * L, 0.024f * L), uv: UV_EYE, noOutline: true);

                        // 头顶前上方弯角刺
                        AddSymX(m, Prim.Cone, 0.038f * L, 0.118f * L, 0.178f * L,
                            new Vector3(0.036f * L, 0.110f * L, 0.040f * L), pitch: 68f, yaw: -6f, uv: UV_COAT, noOutline: true);

                        // 下方单尖镰刀大颚（基部椭球下倾过渡 + 单一前伸上翘尖锥，消除大颚双尖分叉）
                        AddSymX(m, Prim.Sphere, 0.058f * L, 0.034f * L, 0.205f * L,
                            new Vector3(0.046f * L, 0.048f * L, 0.095f * L), yaw: -10f, pitch: 18f, uv: UV_COAT, noOutline: true);
                        AddSymX(m, Prim.Cone, 0.054f * L, 0.046f * L, 0.220f * L,
                            new Vector3(0.020f * L, 0.125f * L, 0.042f * L), pitch: 84f, yaw: -20f, uv: UV_COAT);
                        AddSymX(m, Prim.Cone, 0.054f * L, 0.046f * L, 0.214f * L,
                            new Vector3(0.044f * L, 0.132f * L, 0.048f * L), pitch: 84f, yaw: -20f, uv: UV_COAT, noOutline: true);
                        AddSymX(m, Prim.Cone, 0.022f * L, 0.052f * L, 0.208f * L,
                            new Vector3(0.022f * L, 0.068f * L, 0.022f * L), pitch: 138f, noOutline: true, uv: UV_LIMB);
                    }
                    else
                    {
                        // 巨型蜘蛛 (megaspider)：严格还原原版三视图 —— 贴地胸甲 + 高拱下钩尾刺后腹 + 两侧外展上扬新月角冠头盾 + 额角与上钩巨螯 + 多眼群
                        bodyCenterY = 0.16f * L;
                        bodyHalfW = 0.19f * L;

                        // 1. 贴地中胸甲(y=0)与前胸下颌尖突(还原 _east 胸底右伸尖突)
                        m.Add(Prim.Sphere, new Vector3(0f, 0f, -0.02f * L),
                            new Vector3(0.185f * L, 0.130f * L, 0.25f * L), pitch: -6f, uv: UV_LIMB);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.008f * L, -0.01f * L),
                            new Vector3(0.200f * L, 0.140f * L, 0.26f * L), pitch: -6f, noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.012f * L, 0.07f * L),
                            new Vector3(0.180f * L, 0.138f * L, 0.18f * L), pitch: 18f, noOutline: true, uv: UV_COAT);
                        m.Add(Prim.Cone, new Vector3(0f, 0.032f * L, 0.11f * L),
                            new Vector3(0.052f * L, 0.085f * L, 0.052f * L), pitch: 108f, noOutline: true, uv: UV_COAT);

                        // 2. 贯穿整个后腹的一体化坚固高拱几丁质主腹甲 (Solid Chitinous Opisthosoma，彻底消除多球糖葫芦堆叠感)
                        m.Add(Prim.Sphere, new Vector3(0f, 0.125f * L, -0.175f * L),
                            new Vector3(0.205f * L, 0.170f * L, 0.265f * L), pitch: -26f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.120f * L, -0.175f * L),
                            new Vector3(0.220f * L, 0.180f * L, 0.280f * L), pitch: -26f, noOutline: true, uv: UV_COAT);

                        // 腹甲背侧横向覆瓦状硬质几丁质背板 (Tergite Plates，平贴背壳，消除突起破面)
                        m.Add(Prim.Sphere, new Vector3(0f, 0.170f * L, -0.115f * L),
                            new Vector3(0.185f * L, 0.024f * L, 0.075f * L), pitch: -22f, uv: UV_COAT, noOutline: true);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.205f * L, -0.175f * L),
                            new Vector3(0.180f * L, 0.024f * L, 0.075f * L), pitch: -26f, uv: UV_COAT, noOutline: true);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.185f * L, -0.235f * L),
                            new Vector3(0.160f * L, 0.024f * L, 0.070f * L), pitch: -30f, uv: UV_COAT, noOutline: true);

                        // 腹底平顺暗托与下钩尾刺
                        m.Add(Prim.Sphere, new Vector3(0f, 0.055f * L, -0.14f * L),
                            new Vector3(0.160f * L, 0.095f * L, 0.14f * L), pitch: -35f, noOutline: true, uv: UV_LIMB);
                        m.Add(Prim.Cone, new Vector3(0f, 0.145f * L, -0.295f * L),
                            new Vector3(0.044f * L, 0.065f * L, 0.048f * L), pitch: -148f, uv: UV_COAT);

                        // 头胸两侧霸气上挑的新月角冠头盾（向斜上方霸气挑起，真实再现原版昆虫新月头冠）
                        AddSymX(m, Prim.Cone, 0.080f * L, 0.165f * L, 0.105f * L,
                            new Vector3(0.046f * L, 0.180f * L, 0.052f * L), pitch: 22f, splay: 44f, uv: UV_COAT);
                        AddSymX(m, Prim.Cone, 0.070f * L, 0.155f * L, 0.112f * L,
                            new Vector3(0.072f * L, 0.190f * L, 0.078f * L), pitch: 22f, splay: 44f, uv: UV_COAT, noOutline: true);

                        // 4. 锥形面部、多点蛛眼群、前伸额角与前下内拢巨螯
                        m.Add(Prim.Sphere, new Vector3(0f, 0.082f * L, 0.155f * L),
                            new Vector3(0.136f * L, 0.096f * L, 0.14f * L), pitch: 12f, uv: UV_COAT);
                        m.Add(Prim.Sphere, new Vector3(0f, 0.072f * L, 0.155f * L),
                            new Vector3(0.160f * L, 0.118f * L, 0.16f * L), pitch: 12f, noOutline: true, uv: UV_COAT);
                        // 额前中央向上向前微勾的中央锐角
                        m.Add(Prim.Cone, new Vector3(0f, 0.155f * L, 0.195f * L),
                            new Vector3(0.018f * L, 0.110f * L, 0.032f * L), pitch: 65f, uv: UV_COAT);
                        m.Add(Prim.Cone, new Vector3(0f, 0.155f * L, 0.185f * L),
                            new Vector3(0.036f * L, 0.120f * L, 0.038f * L), pitch: 65f, noOutline: true, uv: UV_COAT);
                        // 蜘蛛多眼簇
                        AddSymX(m, Prim.Sphere, 0.046f * L, 0.145f * L, 0.195f * L,
                            new Vector3(0.025f * L, 0.025f * L, 0.025f * L), uv: UV_EYE, noOutline: true);
                        AddSymX(m, Prim.Sphere, 0.064f * L, 0.130f * L, 0.180f * L,
                            new Vector3(0.023f * L, 0.023f * L, 0.023f * L), uv: UV_EYE, noOutline: true);
                        AddSymX(m, Prim.Sphere, 0.042f * L, 0.115f * L, 0.205f * L,
                            new Vector3(0.021f * L, 0.021f * L, 0.021f * L), uv: UV_EYE, noOutline: true);
                        // 额前两侧向前下方内拢的双螯
                        AddSymX(m, Prim.Cone, 0.048f * L, 0.105f * L, 0.195f * L,
                            new Vector3(0.024f * L, 0.115f * L, 0.038f * L), pitch: 108f, yaw: -14f, uv: UV_COAT);
                        AddSymX(m, Prim.Cone, 0.048f * L, 0.105f * L, 0.188f * L,
                            new Vector3(0.048f * L, 0.122f * L, 0.048f * L), pitch: 108f, yaw: -14f, uv: UV_COAT, noOutline: true);
                        AddSymX(m, Prim.Cone, 0.018f * L, 0.088f * L, 0.210f * L,
                            new Vector3(0.024f * L, 0.068f * L, 0.024f * L), pitch: 138f, noOutline: true, uv: UV_LIMB);
                    }

                    break;
                }
            }

            m.RecalcBounds();
            return m;
        }

        // ---- 网格与 UV 构建缓存 ----

        private static List<UvSpec> BuildUvSpecs(string spKey, out bool usesAtlas)
        {
            var list = new List<UvSpec>(RegionNames.Length);
            usesAtlas = AnimalUVGen.R.ContainsKey(spKey + ".coat") && AtlasMat != null;
            for (int i = 0; i < RegionNames.Length; i++)
            {
                Vector4 r;
                if (!usesAtlas || !AnimalUVGen.R.TryGetValue(spKey + "." + RegionNames[i], out r))
                {
                    // 未进图集的第三方模组动物回退：采其自身贴图中心安全区
                    r = new Vector4(0.42f, 0.42f, 0.58f, 0.58f);
                }
                list.Add(new UvSpec
                {
                    top = r,
                    side = r,
                    sideTileH = 0f,
                    sideTileW = 0f,
                    modelXZ = Vector2.one
                });
            }
            return list;
        }

        private static Built Get(Pawn p, float L)
        {
            string spKey = ResolveSpeciesKey(p);
            int bucket = Mathf.RoundToInt(L * 8f);
            string cacheKey = spKey + "#" + bucket;
            if (cache.TryGetValue(cacheKey, out var b) && b != null && b.body != null) return b;

            var arch = ArchOf(spKey);
            var model = BuildModel(spKey, arch, L, out float cy, out float hw);
            var uvs = BuildUvSpecs(spKey, out bool usesAtlas);
            var builder = new ModelBuilder(uvs, false);
            builder.AddAll(model);
            var st = FirstPerson3DMod.settings;
            float ow = (st != null ? st.outlineWidth : Metrics3D.OutlineWidth) * Mathf.Clamp(L, 0.6f, 2.2f);
            b = new Built
            {
                body = builder.ToMesh(),
                hull = builder.ToHull(ow),
                bodyCenterY = cy,
                bodyHalfW = hw,
                L = L,
                usesAtlas = usesAtlas
            };
            cache[cacheKey] = b;
            return b;
        }

        // ---- 绘制 ----

        private static readonly Dictionary<int, float> smoothYaw = new Dictionary<int, float>();

        public static void Draw(Pawn pawn, Vector3 pos, Vector3 camPos, Color lit)
        {
            Graphic g = null;
            try { g = pawn.Drawer.renderer.renderTree != null ? pawn.Drawer.renderer.renderTree.BodyGraphic : null; } catch { }
            if (g == null)
            {
                try { g = pawn.ageTracker.CurKindLifeStage.bodyGraphicData != null ? pawn.ageTracker.CurKindLifeStage.bodyGraphicData.Graphic : null; } catch { }
            }
            if (g == null)
            {
                g = PawnLayer3D.ResolveGraphic(pawn, pawn.Rotation);
            }
            if (g == null) { WarnOnce("g-null:" + DefName(pawn), "no body graphic"); return; }
            Material fallbackMat = null;
            try { fallbackMat = g.MatAt(Rot4.East, pawn); } catch { }
            if (fallbackMat == null || fallbackMat.mainTexture == null || fallbackMat.NullOrBad()) { try { fallbackMat = g.MatSingle; } catch { } }
            if (fallbackMat == null || fallbackMat.NullOrBad()) { WarnOnce("mat-null:" + DefName(pawn), "no material"); return; }

            float L = Mathf.Clamp(Mathf.Max(g.drawSize.x, g.drawSize.y) * Metrics3D.AnimalTextureFill, 0.35f, 4.5f);
            Built built = null;
            try { built = Get(pawn, L); }
            catch (System.Exception e)
            {
                WarnOnce("buildfail:" + DefName(pawn), "model build failed: " + e.GetType().Name + ": " + e.Message);
                built = null;
            }
            if (built == null || built.body == null || !built.usesAtlas)
            {
                Billboard(pawn, pos, camPos, g, Lighting3D.MatFor(fallbackMat, lit));
                return;
            }

            Material drawMat = (built.usesAtlas && AtlasMat != null) ? AtlasMat : fallbackMat;
            drawMat = Lighting3D.MatFor(drawMat, lit);

            float targetYaw = pawn.Rotation.AsAngle;
            bool moving = false;
            try
            {
                if (pawn.pather != null && pawn.pather.Moving)
                {
                    moving = true;
                    var d = pawn.pather.nextCell - pawn.Position;
                    if (d.x != 0 || d.z != 0) targetYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                }
            }
            catch { }
            int id = pawn.thingIDNumber;
            if (!smoothYaw.TryGetValue(id, out float yaw)) yaw = targetYaw;
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-Time.deltaTime / 0.12f));
            smoothYaw[id] = yaw;

            float t = Time.realtimeSinceStartup;
            float bob = moving ? Mathf.Abs(Mathf.Sin(t * 9f + id)) * 0.045f * L : Mathf.Sin(t * 1.4f + id) * 0.006f * L;
            bool lying = false;
            try { lying = pawn.GetPosture() != PawnPosture.Standing || pawn.Downed; } catch { }

            Matrix4x4 matrix;
            var yawRot = Quaternion.AngleAxis(yaw, Vector3.up);
            if (lying)
            {
                matrix = Matrix4x4.TRS(pos + new Vector3(0f, built.bodyHalfW, 0f), yawRot * Quaternion.Euler(0f, 0f, 80f), Vector3.one)
                       * Matrix4x4.Translate(new Vector3(0f, -built.bodyCenterY, 0f));
            }
            else
            {
                matrix = Matrix4x4.TRS(pos + new Vector3(0f, bob, 0f), yawRot, Vector3.one);
            }
            Graphics.DrawMesh(built.body, matrix, drawMat, 0);
            if (!lying) Shadows3D.Cast(built.body, matrix, pos, null);   // F016 L3：闭合网格
            // 受击红闪（F014）：同一份网格同一份变换，再罩一层红；描边壳不罩
            HitFx3D.DrawOverlay(built.body, matrix, drawMat, HitFx3D.Strength(pawn));
            PerfCounters.AnimalMeshes++;
            if (Fps3DState.DrawOutlines && built.hull != null)
            {
                Graphics.DrawMesh(built.hull, matrix, Util3D.OutlineMat, 0);
                PerfCounters.OutlineMeshesDrawn++;
            }
        }

        // ---- 诊断与兜底 ----

        private static string DefName(Pawn p)
        {
            try { return p != null && p.def != null ? p.def.defName : "?"; }
            catch { return "?"; }
        }

        private static readonly HashSet<string> warned = new HashSet<string>();

        /// <summary>按 key 只报一次的日志（模型/材质/贴图问题按物种归类即可，不许每帧刷屏）。</summary>
        public static void WarnOnce(string key, string msg)
        {
            if (key == null || warned.Contains(key)) return;
            warned.Add(key);
            Log.Warning(ModEntry.LogPrefix + "animal " + key + " -> " + msg);
        }

        /// <summary>
        /// 模型不可用时的保底立牌：取原版该朝向的贴图，画一张朝向相机的竖向面片。
        /// </summary>
        private static void Billboard(Pawn pawn, Vector3 pos, Vector3 camPos, Graphic g, Material fallbackMat)
        {
            var quad = Util3D.BillboardQuad;
            if (quad == null || pawn == null || g == null) return;
            float yaw = Mathf.Atan2(camPos.x - pos.x, camPos.z - pos.z) * Mathf.Rad2Deg;
            float camDirAngle = Mathf.Atan2(camPos.x - pos.x, -(camPos.z - pos.z)) * Mathf.Rad2Deg;
            Rot4 viewRot = Rot4.FromAngleFlat(pawn.Rotation.AsAngle - camDirAngle);
            Material mat = fallbackMat;
            try
            {
                var m = g.MatAt(viewRot, pawn);
                if (m != null && m.mainTexture != null) mat = m;
            }
            catch { }
            if (mat == null) return;
            float w = Mathf.Clamp(g.drawSize.x, 0.3f, 4.5f);
            float h = Mathf.Clamp(g.drawSize.y, 0.3f, 4.5f);
            var rot = Quaternion.AngleAxis(yaw, Vector3.up);
            var matrix = Matrix4x4.TRS(pos, rot, new Vector3(w, h, 1f));
            Graphics.DrawMesh(quad, matrix, mat, 0);
            if (Shadows3D.AnyActive)   // F016 L3：回退立牌正对太阳投影
            {
                float sYaw = Shadows3D.SunYaw;
                Shadows3D.Cast(quad, Matrix4x4.TRS(pos, Quaternion.AngleAxis(sYaw, Vector3.up), new Vector3(w, h, 1f)), pos,
                    mat.mainTexture, true, Matrix4x4.TRS(pos, Quaternion.AngleAxis(sYaw + 180f, Vector3.up), new Vector3(w, h, 1f)));
            }
            HitFx3D.DrawOverlay(quad, matrix, mat, HitFx3D.Strength(pawn));
            PerfCounters.AnimalMeshes++;
        }
    }

    /// <summary>按地图遍历动物并交给 Animal3D 绘制。</summary>
    public class AnimalLayer3D
    {
        private readonly Map map;
        private readonly float baseY;
        public Vector3 worldOffset = Vector3.zero;
        public LightField3D light;

        public AnimalLayer3D(Map map, float baseY)
        {
            this.map = map;
            this.baseY = baseY;
        }

        public void Draw(Vector3 camPos)
        {
            if (!Animal3D.Enabled) return;
            var pawns = map.mapPawns.AllPawnsSpawned;
            if (pawns == null) return;
            for (int i = 0; i < pawns.Count; i++)
            {
                var p = pawns[i];
                try
                {
                    if (p == null || p.Destroyed || p.CarriedBy != null) continue;
                    if (!Animal3D.HandlesPawn(p)) continue;
                    if (p.Position.Fogged(map)) continue;
                    var dp = p.DrawPos;
                    var wpos = new Vector3(dp.x + worldOffset.x, baseY - WaterLayer3D.SinkAt(map, p.Position), dp.z + worldOffset.z);
                    if (!Frustum3D.VisiblePoint(wpos, 3f)) continue;
                    var lit = Lighting3D.InstanceLevel(p, light, new Vector3(dp.x, baseY, dp.z));
                    Animal3D.Draw(p, wpos, camPos, lit);
                }
                catch (System.Exception e)
                {
                    Animal3D.WarnOnce("draw:" + (p != null && p.def != null ? p.def.defName : "?")
                        + ":" + e.GetType().Name, e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                }
            }
        }

        public void Dispose() { }
    }
}
