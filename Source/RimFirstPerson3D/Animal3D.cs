// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 动物程序化建模
    // ============================================================
    //
    // 建模前先看过原版三视图（muffalo / cat / chicken / elephant / wolf / thrumbo /
    // tortoise / megaspider / cobra 的 east/south/north）。共同特征：
    //   · 极卡通：身体是一团圆润的"蛋"或"馒头"，没有肌肉结构
    //   · 头大：头与身体的比例远大于真实动物，且常常贴在身体前缘
    //   · 腿极短或不画：muffalo/thrumbo 用毛边遮腿，cat/wolf 的腿是一截短桩
    //   · 粗描边：轮廓线粗而连续
    // 因此模型 = 椭球身体 + 球形头 + 短圆柱腿 + 少量特征件（耳/尾/角/鼻/壳），
    // 全部圆润基本体，按 9 种原型（archetype）组合，再按 def 关键字归类。
    // 贴图用该动物自身 east 面贴图的中心区（取毛色/花纹），描边用平滑法线外扩壳。
    //
    // 尺寸：L = 原版 drawSize.x × 0.72（贴图四周留白约 28%），其余比例相对 L。
    public static class Animal3D
    {
        private enum Arch { Blob, LongNeck, Quad, Slender, Stocky, Rodent, Bird, TallBird, Pachyderm, Shell, Insect, Serpent, Primate, Reptile }

        private class Built
        {
            public Mesh body;
            public Mesh hull;
            public float bodyCenterY;   // 身体中心离地高（躺卧姿态用）
            public float bodyHalfW;
            public float L;
        }

        private static readonly Dictionary<long, Built> cache = new Dictionary<long, Built>();

        public static bool Enabled
        {
            get { var s = FirstPerson3DMod.settings; return s == null || s.animalModels; }
        }

        /// <summary>该 pawn 是否由本系统绘制（非人形、非机械族）。</summary>
        public static bool HandlesPawn(Pawn p)
        {
            if (!Enabled || p == null || p.RaceProps == null) return false;
            if (p.RaceProps.Humanlike) return false;
            if (p.RaceProps.IsMechanoid) return false;
            return true;
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

        // ---- 归类 ----

        private static Arch ArchOf(string n)
        {
            // 先匹配者胜，顺序有讲究：
            //  · "muskox" 含 "ox"，必须在 Stocky 之前被 Blob 接住（它是长毛馒头身）
            //  · "elephant"/"mastodon" 含 "ant"，Pachyderm 必须在 Insect 之前
            //  · "seaturtle" 含 "turtle"，Shell 必须在 Reptile 之前
            //  · Insect 不再用 "ant" 做键：原版没有蚂蚁，却会把 antelope 误判成蜘蛛
            // 覆盖面：Core 全部 + Odyssey 41 种 + Biotech（toxalope/wasterat）+ Ideology 树灵。
            if (Has(n, "thrumbo", "alpaca", "dromedary", "camel", "giraffe", "llama")) return Arch.LongNeck;
            if (Has(n, "muffalo", "bison", "yak", "megasloth", "sheep", "mammoth", "alope", "muskox", "seal", "walrus")) return Arch.Blob;
            if (Has(n, "elephant", "rhino", "hippo", "mastodon")) return Arch.Pachyderm;
            if (Has(n, "tortoise", "turtle", "armadillo", "crab", "snail", "slug")) return Arch.Shell;
            if (Has(n, "iguana", "lizard", "gecko", "skink", "monitor", "komodo", "alligator", "croc", "gator", "frog", "toad", "salamander", "newt")) return Arch.Reptile;
            if (Has(n, "spider", "scarab", "spelopede", "insect", "beetle", "megaworm")) return Arch.Insect;
            if (Has(n, "cobra", "snake", "serpent", "viper")) return Arch.Serpent;
            if (Has(n, "emu", "ostrich", "cassowary", "flamingo", "heron", "crane", "stork")) return Arch.TallBird;
            if (Has(n, "chicken", "duck", "goose", "turkey", "bird", "parrot", "pigeon", "toucan", "crow", "macaw", "peacock", "penguin", "quail", "sparrow", "swan", "vulture", "raven", "owl", "eagle", "hawk")) return Arch.Bird;
            if (Has(n, "monkey", "ape", "chimp", "gorilla", "dryad", "baboon")) return Arch.Primate;
            if (Has(n, "rat", "squirrel", "chinchilla", "guineapig", "hare", "rabbit", "boomrat", "capybara", "beaver", "raccoon", "hamster", "vole", "porcupine", "prairie")) return Arch.Rodent;
            if (Has(n, "horse", "donkey", "deer", "caribou", "elk", "gazelle", "ibex", "goat", "antelope", "moose")) return Arch.Slender;
            if (Has(n, "bear", "warg", "boar", "pig", "cow", "ox", "wildboar", "husky", "labrador", "badger", "panda", "wolverine")) return Arch.Stocky;
            return Arch.Quad;
        }

        private static bool Has(string s, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (s.IndexOf(keys[i], System.StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // ---- 建模 ----

        private static Model3D BuildModel(string n, Arch a, float L, out float bodyCenterY, out float bodyHalfW)
        {
            var m = new Model3D();
            bool bushyTail = Has(n, "fox", "wolf", "cat", "lynx", "cougar", "husky", "labrador", "terrier", "warg", "raccoon", "squirrel");
            bool tailUp = Has(n, "cat", "squirrel", "monkey", "terrier");
            bool antlers = Has(n, "deer", "elk", "caribou", "moose");
            bool horns = Has(n, "ibex", "goat", "bison", "yak", "cow", "ox", "thrumbo", "muffalo");
            bool tusks = Has(n, "elephant", "boar", "wildboar", "mastodon", "walrus");

            switch (a)
            {
                case Arch.Blob:
                case Arch.LongNeck:
                {
                    // 馒头身 + 底部毛边（更宽更扁的一圈）+ 大头贴前缘
                    float bl = 0.76f * L, bh = 0.60f * L, bw = 0.62f * L;
                    float legH = 0.10f * L;
                    Legs4(m, L, 0.26f, 0.22f, legH, 0.09f * L);
                    m.Add(Prim.Sphere, new Vector3(0f, legH, -0.02f * L), new Vector3(bw, bh, bl));
                    m.Add(Prim.Sphere, new Vector3(0f, legH - 0.02f * L, -0.02f * L), new Vector3(bw * 1.08f, bh * 0.42f, bl * 1.06f));
                    bodyCenterY = legH + bh * 0.5f; bodyHalfW = bw * 0.54f;
                    if (a == Arch.LongNeck)
                    {
                        float neckL = 0.42f * L;
                        m.Add(Prim.Cylinder, new Vector3(0f, legH + bh * 0.62f, bl * 0.30f), new Vector3(0.20f * L, neckL, 0.20f * L), pitch: 40f);
                        var hp = new Vector3(0f, legH + bh * 0.62f + neckL * 0.77f, bl * 0.30f + neckL * 0.64f);
                        float hr = 0.30f * L;
                        m.Add(Prim.Sphere, hp - new Vector3(0, hr * 0.5f, 0), new Vector3(hr * 0.9f, hr, hr * 1.15f));
                        m.Add(Prim.Sphere, hp + new Vector3(0, -hr * 0.45f, hr * 0.55f), new Vector3(hr * 0.55f, hr * 0.5f, hr * 0.6f));
                        if (Has(n, "thrumbo"))
                            m.Add(Prim.Cone, hp + new Vector3(0, hr * 0.25f, hr * 0.3f), new Vector3(0.10f * L, 0.42f * L, 0.10f * L), pitch: 25f);
                        Ears(m, hp + new Vector3(0, hr * 0.3f, 0), hr * 0.9f, 0.07f * L, 0.12f * L);
                    }
                    else
                    {
                        float hr = 0.34f * L;
                        var hp = new Vector3(0f, legH + bh * 0.18f, bl * 0.46f);
                        m.Add(Prim.Sphere, hp, new Vector3(hr * 0.85f, hr * 1.15f, hr * 0.95f));
                        if (horns)
                        {
                            m.Add(Prim.Cone, hp + new Vector3(-hr * 0.35f, hr * 1.0f, 0f), new Vector3(0.07f * L, 0.16f * L, 0.07f * L), pitch: -20f, yaw: -25f);
                            m.Add(Prim.Cone, hp + new Vector3(hr * 0.35f, hr * 1.0f, 0f), new Vector3(0.07f * L, 0.16f * L, 0.07f * L), pitch: -20f, yaw: 25f);
                        }
                    }
                    m.Add(Prim.Cone, new Vector3(0f, legH + bh * 0.5f, -bl * 0.46f), new Vector3(0.10f * L, 0.16f * L, 0.10f * L), pitch: -140f);
                    break;
                }
                case Arch.Pachyderm:
                {
                    float bl = 0.74f * L, bh = 0.58f * L, bw = 0.56f * L;
                    float legH = 0.22f * L;
                    Legs4(m, L, 0.24f, 0.24f, legH + 0.08f * L, 0.12f * L);
                    m.Add(Prim.Sphere, new Vector3(0f, legH, -0.04f * L), new Vector3(bw, bh, bl));
                    bodyCenterY = legH + bh * 0.5f; bodyHalfW = bw * 0.5f;
                    float hr = 0.36f * L;
                    var hp = new Vector3(0f, legH + bh * 0.30f, bl * 0.46f);
                    m.Add(Prim.Sphere, hp, new Vector3(hr * 0.95f, hr * 0.95f, hr));
                    if (Has(n, "elephant", "mastodon"))
                    {
                        m.Add(Prim.Cylinder, hp + new Vector3(0f, hr * 0.35f, hr * 0.42f), new Vector3(0.11f * L, 0.46f * L, 0.11f * L), pitch: 150f);
                        m.Add(Prim.Box, hp + new Vector3(-hr * 0.55f, hr * 0.25f, -hr * 0.1f), new Vector3(0.05f * L, 0.30f * L, 0.26f * L), yaw: 20f);
                        m.Add(Prim.Box, hp + new Vector3(hr * 0.55f, hr * 0.25f, -hr * 0.1f), new Vector3(0.05f * L, 0.30f * L, 0.26f * L), yaw: -20f);
                    }
                    if (Has(n, "rhino"))
                        m.Add(Prim.Cone, hp + new Vector3(0f, hr * 0.55f, hr * 0.45f), new Vector3(0.09f * L, 0.26f * L, 0.09f * L), pitch: 35f);
                    if (tusks)
                    {
                        m.Add(Prim.Cone, hp + new Vector3(-hr * 0.22f, hr * 0.15f, hr * 0.4f), new Vector3(0.05f * L, 0.22f * L, 0.05f * L), pitch: 70f);
                        m.Add(Prim.Cone, hp + new Vector3(hr * 0.22f, hr * 0.15f, hr * 0.4f), new Vector3(0.05f * L, 0.22f * L, 0.05f * L), pitch: 70f);
                    }
                    m.Add(Prim.Cone, new Vector3(0f, legH + bh * 0.55f, -bl * 0.46f), new Vector3(0.07f * L, 0.22f * L, 0.07f * L), pitch: -150f);
                    break;
                }
                case Arch.Shell:
                {
                    float sl = 0.72f * L, sh = 0.32f * L, sw = 0.62f * L;
                    float legH = 0.06f * L;
                    Legs4(m, L, 0.30f, 0.26f, legH + 0.05f * L, 0.08f * L);
                    m.Add(Prim.Sphere, new Vector3(0f, legH, 0f), new Vector3(sw, sh, sl));
                    m.Add(Prim.Sphere, new Vector3(0f, legH + sh * 0.5f, 0f), new Vector3(sw * 0.9f, sh * 0.9f, sl * 0.9f));
                    bodyCenterY = legH + sh * 0.5f; bodyHalfW = sw * 0.5f;
                    float hr = 0.16f * L;
                    m.Add(Prim.Sphere, new Vector3(0f, legH + 0.02f * L, sl * 0.46f), new Vector3(hr, hr * 0.9f, hr * 1.2f));
                    m.Add(Prim.Cone, new Vector3(0f, legH + 0.04f * L, -sl * 0.46f), new Vector3(0.06f * L, 0.12f * L, 0.06f * L), pitch: -110f);
                    break;
                }
                case Arch.Reptile:
                {
                    // 蜥蜴 / 鳄 / 蛙：贴地的扁长身 + 向两侧外撑的短腿 + 扁头。
                    // 蛙类（frog/toad）无尾、身体更圆、头更大；其余带背脊鬣鳞与三节递减的长尾。
                    bool frog = Has(n, "frog", "toad");
                    float bl = (frog ? 0.46f : 0.62f) * L, bh = (frog ? 0.30f : 0.22f) * L, bw = (frog ? 0.44f : 0.34f) * L;
                    float legH = 0.10f * L;
                    float lx = 0.13f * L, lz = (frog ? 0.14f : 0.20f) * L, ll = (frog ? 0.20f : 0.16f) * L, lr = 0.05f * L;
                    // Euler(pitch,yaw) 下 yaw=±90 把 X 俯仰转成左右外张；pitch=115 时腿尖指向 (±0.91, -0.42)，
                    // 基点抬到 0.14L 使脚尖落在地面附近而不是穿地。
                    m.Add(Prim.Cylinder, new Vector3(-lx, 0.14f * L, lz), new Vector3(lr, ll, lr), yaw: -90f, pitch: 115f);
                    m.Add(Prim.Cylinder, new Vector3(lx, 0.14f * L, lz), new Vector3(lr, ll, lr), yaw: 90f, pitch: 115f);
                    m.Add(Prim.Cylinder, new Vector3(-lx, 0.14f * L, -lz), new Vector3(lr, ll, lr), yaw: -90f, pitch: 115f);
                    m.Add(Prim.Cylinder, new Vector3(lx, 0.14f * L, -lz), new Vector3(lr, ll, lr), yaw: 90f, pitch: 115f);
                    m.Add(Prim.Sphere, new Vector3(0f, legH, -0.02f * L), new Vector3(bw, bh, bl));
                    bodyCenterY = legH + bh * 0.5f; bodyHalfW = bw * 0.5f;
                    float hr = (frog ? 0.26f : 0.20f) * L;
                    var hp = new Vector3(0f, legH + bh * (frog ? 0.45f : 0.35f), bl * 0.46f);
                    m.Add(Prim.Sphere, hp, new Vector3(hr * 0.9f, hr * 0.7f, hr * 1.1f));
                    m.Add(Prim.Sphere, hp + new Vector3(0f, -hr * 0.05f, hr * 0.55f), new Vector3(hr * 0.55f, hr * 0.4f, hr * 0.5f));
                    if (!frog)
                    {
                        for (int i = 0; i < 4; i++)
                            m.Add(Prim.Cone, new Vector3(0f, legH + bh * 0.85f, (-0.18f + i * 0.12f) * L),
                                new Vector3(0.05f * L, 0.10f * L, 0.05f * L), noOutline: true);
                        for (int i = 0; i < 3; i++)
                            m.Add(Prim.Sphere, new Vector3(0f, legH + bh * 0.4f, (-0.44f - i * 0.17f) * L),
                                new Vector3((0.18f - i * 0.045f) * L, (0.13f - i * 0.03f) * L, 0.22f * L), noOutline: true);
                    }
                    break;
                }
                case Arch.Insect:
                {
                    float ab = 0.46f * L, th = 0.30f * L;
                    float legH = 0.14f * L;
                    // 腹（后）+ 胸头（前）
                    m.Add(Prim.Sphere, new Vector3(0f, legH, -0.20f * L), new Vector3(ab * 0.9f, ab * 0.75f, ab));
                    m.Add(Prim.Sphere, new Vector3(0f, legH + 0.02f * L, 0.22f * L), new Vector3(th * 0.95f, th * 0.85f, th));
                    bodyCenterY = legH + ab * 0.37f; bodyHalfW = ab * 0.45f;
                    // 六条向外下方张开的腿：Euler(pitch, yaw) 下 yaw=±90 把 X 轴俯仰
                    // 转成左右外张，pitch=130 时腿尖指向 (±0.77, -0.64, 0)。
                    // 腿长 0.30L，基点抬高 0.30L×0.64 ≈ 0.19L 使脚尖恰好着地。
                    for (int i = 0; i < 3; i++)
                    {
                        float z = (0.20f - i * 0.18f) * L;
                        m.Add(Prim.Cylinder, new Vector3(-0.10f * L, 0.19f * L, z), new Vector3(0.035f * L, 0.30f * L, 0.035f * L), yaw: -90f, pitch: 130f);
                        m.Add(Prim.Cylinder, new Vector3(0.10f * L, 0.19f * L, z), new Vector3(0.035f * L, 0.30f * L, 0.035f * L), yaw: 90f, pitch: 130f);
                    }
                    // 颚
                    m.Add(Prim.Cone, new Vector3(-0.06f * L, legH + 0.05f * L, 0.36f * L), new Vector3(0.04f * L, 0.16f * L, 0.04f * L), pitch: 80f, yaw: 15f);
                    m.Add(Prim.Cone, new Vector3(0.06f * L, legH + 0.05f * L, 0.36f * L), new Vector3(0.04f * L, 0.16f * L, 0.04f * L), pitch: 80f, yaw: -15f);
                    break;
                }
                case Arch.Serpent:
                {
                    // 盘绕的身体（扁宽椭球）+ 竖起的颈 + 头
                    m.Add(Prim.Sphere, new Vector3(0f, 0f, 0f), new Vector3(0.62f * L, 0.18f * L, 0.60f * L));
                    m.Add(Prim.Sphere, new Vector3(0.06f * L, 0.10f * L, -0.04f * L), new Vector3(0.44f * L, 0.14f * L, 0.42f * L));
                    bodyCenterY = 0.09f * L; bodyHalfW = 0.31f * L;
                    float neckL = 0.42f * L;
                    m.Add(Prim.Cylinder, new Vector3(0f, 0.12f * L, 0.12f * L), new Vector3(0.11f * L, neckL, 0.11f * L), pitch: 18f);
                    var hp = new Vector3(0f, 0.12f * L + neckL * 0.95f, 0.12f * L + neckL * 0.31f);
                    m.Add(Prim.Sphere, hp - new Vector3(0, 0.06f * L, 0), new Vector3(0.14f * L, 0.12f * L, 0.20f * L));
                    m.Add(Prim.Box, hp - new Vector3(0, 0.10f * L, 0.02f * L), new Vector3(0.22f * L, 0.14f * L, 0.03f * L));
                    break;
                }
                case Arch.Bird:
                case Arch.TallBird:
                {
                    bool tall = a == Arch.TallBird;
                    float bl = (tall ? 0.52f : 0.46f) * L, bh = (tall ? 0.46f : 0.40f) * L, bw = 0.40f * L;
                    float legH = (tall ? 0.42f : 0.14f) * L;
                    float legR = (tall ? 0.045f : 0.03f) * L;
                    m.Add(Prim.Cylinder, new Vector3(-0.08f * L, 0f, -0.02f * L), new Vector3(legR, legH + 0.06f * L, legR));
                    m.Add(Prim.Cylinder, new Vector3(0.08f * L, 0f, -0.02f * L), new Vector3(legR, legH + 0.06f * L, legR));
                    m.Add(Prim.Sphere, new Vector3(0f, legH, 0f), new Vector3(bw, bh, bl));
                    bodyCenterY = legH + bh * 0.5f; bodyHalfW = bw * 0.5f;
                    float neckL = (tall ? 0.46f : 0.14f) * L;
                    m.Add(Prim.Cylinder, new Vector3(0f, legH + bh * 0.70f, bl * 0.28f), new Vector3((tall ? 0.09f : 0.12f) * L, neckL, (tall ? 0.09f : 0.12f) * L), pitch: tall ? 12f : 35f);
                    float hr = (tall ? 0.16f : 0.18f) * L;
                    var hp = new Vector3(0f, legH + bh * 0.70f + neckL * (tall ? 0.95f : 0.8f), bl * 0.28f + neckL * (tall ? 0.2f : 0.55f));
                    m.Add(Prim.Sphere, hp - new Vector3(0, hr * 0.4f, 0), new Vector3(hr, hr * 0.95f, hr * 1.05f));
                    m.Add(Prim.Cone, hp + new Vector3(0, -hr * 0.15f, hr * 0.5f), new Vector3(0.06f * L, 0.14f * L, 0.06f * L), pitch: 95f);
                    if (Has(n, "chicken", "turkey"))
                        m.Add(Prim.Box, hp + new Vector3(0, hr * 0.5f, 0), new Vector3(0.03f * L, 0.10f * L, 0.14f * L));
                    // 尾羽：后上方的扁扇
                    m.Add(Prim.Box, new Vector3(0f, legH + bh * 0.55f, -bl * 0.42f), new Vector3(0.22f * L, 0.06f * L, 0.26f * L), pitch: -35f);
                    break;
                }
                case Arch.Primate:
                {
                    float bl = 0.46f * L, bh = 0.44f * L, bw = 0.38f * L;
                    float legH = 0.18f * L;
                    Legs4(m, L, 0.22f, 0.18f, legH + 0.06f * L, 0.06f * L);
                    m.Add(Prim.Sphere, new Vector3(0f, legH, 0f), new Vector3(bw, bh, bl));
                    bodyCenterY = legH + bh * 0.5f; bodyHalfW = bw * 0.5f;
                    float hr = 0.30f * L;
                    var hp = new Vector3(0f, legH + bh * 0.62f, bl * 0.34f);
                    m.Add(Prim.Sphere, hp, new Vector3(hr, hr, hr));
                    Ears(m, hp + new Vector3(0, hr * 0.45f, 0), hr * 1.0f, 0.06f * L, 0.08f * L);
                    m.Add(Prim.Cylinder, new Vector3(0f, legH + bh * 0.5f, -bl * 0.44f), new Vector3(0.05f * L, 0.48f * L, 0.05f * L), pitch: -40f);
                    break;
                }
                default:
                {
                    // 四足：Quad / Slender / Stocky / Rodent
                    bool slender = a == Arch.Slender, stocky = a == Arch.Stocky, rodent = a == Arch.Rodent;
                    float bl = (rodent ? 0.56f : 0.64f) * L;
                    float bh = (stocky ? 0.42f : rodent ? 0.36f : 0.34f) * L;
                    float bw = (stocky ? 0.36f : 0.30f) * L;
                    float legH = (slender ? 0.32f : stocky ? 0.20f : rodent ? 0.10f : 0.22f) * L;
                    float legR = (slender ? 0.045f : stocky ? 0.08f : 0.05f) * L;
                    Legs4(m, L, 0.24f, 0.12f, legH + 0.08f * L, legR);
                    m.Add(Prim.Sphere, new Vector3(0f, legH, 0f), new Vector3(bw, bh, bl));
                    bodyCenterY = legH + bh * 0.5f; bodyHalfW = bw * 0.5f;
                    // 颈 + 头
                    float neckL = (slender ? 0.22f : 0.10f) * L;
                    float hr = (rodent ? 0.26f : 0.22f) * L;
                    var np = new Vector3(0f, legH + bh * 0.62f, bl * 0.36f);
                    m.Add(Prim.Cylinder, np, new Vector3(0.14f * L, neckL, 0.14f * L), pitch: slender ? 38f : 55f);
                    var hp = np + new Vector3(0f, neckL * (slender ? 0.78f : 0.57f), neckL * (slender ? 0.62f : 0.82f)) + new Vector3(0, -hr * 0.2f, 0);
                    m.Add(Prim.Sphere, hp, new Vector3(hr * 0.95f, hr, hr * 1.05f));
                    // 吻部
                    m.Add(Prim.Sphere, hp + new Vector3(0f, hr * 0.15f, hr * 0.55f), new Vector3(hr * 0.55f, hr * 0.48f, hr * 0.6f));
                    Ears(m, hp + new Vector3(0, hr * 0.85f, -hr * 0.1f), hr * 0.9f, (rodent ? 0.07f : 0.06f) * L, (rodent ? 0.14f : 0.11f) * L);
                    if (antlers)
                    {
                        m.Add(Prim.Cone, hp + new Vector3(-hr * 0.3f, hr * 0.95f, -hr * 0.1f), new Vector3(0.05f * L, 0.30f * L, 0.05f * L), pitch: -25f, yaw: -35f);
                        m.Add(Prim.Cone, hp + new Vector3(hr * 0.3f, hr * 0.95f, -hr * 0.1f), new Vector3(0.05f * L, 0.30f * L, 0.05f * L), pitch: -25f, yaw: 35f);
                    }
                    else if (horns)
                    {
                        m.Add(Prim.Cone, hp + new Vector3(-hr * 0.3f, hr * 0.9f, -hr * 0.1f), new Vector3(0.05f * L, 0.18f * L, 0.05f * L), pitch: -35f, yaw: -20f);
                        m.Add(Prim.Cone, hp + new Vector3(hr * 0.3f, hr * 0.9f, -hr * 0.1f), new Vector3(0.05f * L, 0.18f * L, 0.05f * L), pitch: -35f, yaw: 20f);
                    }
                    if (tusks)
                    {
                        m.Add(Prim.Cone, hp + new Vector3(-hr * 0.25f, hr * 0.05f, hr * 0.6f), new Vector3(0.04f * L, 0.12f * L, 0.04f * L), pitch: 60f);
                        m.Add(Prim.Cone, hp + new Vector3(hr * 0.25f, hr * 0.05f, hr * 0.6f), new Vector3(0.04f * L, 0.12f * L, 0.04f * L), pitch: 60f);
                    }
                    // 尾
                    var tp = new Vector3(0f, legH + bh * 0.55f, -bl * 0.46f);
                    if (bushyTail)
                        m.Add(Prim.Sphere, tp + new Vector3(0, tailUp ? 0.10f * L : -0.04f * L, -0.14f * L), new Vector3(0.14f * L, 0.14f * L, 0.34f * L), pitch: tailUp ? 50f : -20f);
                    else
                        m.Add(Prim.Cylinder, tp, new Vector3(0.04f * L, (rodent ? 0.30f : 0.22f) * L, 0.04f * L), pitch: tailUp ? -30f : -145f);
                    break;
                }
            }
            m.RecalcBounds();
            return m;
        }

        /// <summary>四条短腿，前后距 fz、左右距 fx（相对 L），脚底着地。</summary>
        private static void Legs4(Model3D m, float L, float fz, float fx, float h, float r)
        {
            float x = fx * L, z = fz * L;
            m.Add(Prim.Cylinder, new Vector3(-x, 0f, z), new Vector3(r, h, r));
            m.Add(Prim.Cylinder, new Vector3(x, 0f, z), new Vector3(r, h, r));
            m.Add(Prim.Cylinder, new Vector3(-x, 0f, -z), new Vector3(r, h, r));
            m.Add(Prim.Cylinder, new Vector3(x, 0f, -z), new Vector3(r, h, r));
        }

        private static void Ears(Model3D m, Vector3 top, float spread, float r, float h)
        {
            m.Add(Prim.Cone, top + new Vector3(-spread * 0.42f, -h * 0.3f, 0f), new Vector3(r * 2f, h, r * 1.4f), pitch: -15f, yaw: -20f);
            m.Add(Prim.Cone, top + new Vector3(spread * 0.42f, -h * 0.3f, 0f), new Vector3(r * 2f, h, r * 1.4f), pitch: -15f, yaw: 20f);
        }

        // ---- 网格缓存 ----

        private static Built Get(Pawn p, float L)
        {
            int bucket = Mathf.RoundToInt(L * 8f);
            long key = ((long)p.def.shortHash << 20) ^ bucket;
            if (cache.TryGetValue(key, out var b) && b != null && b.body != null) return b;
            string n = (p.def.defName ?? "").ToLowerInvariant();
            var arch = ArchOf(n);
            var model = BuildModel(n, arch, L, out float cy, out float hw);
            // 贴图：取 east 面贴图**正中一小块**（0.42..0.58），全部件共用。
            // 不能取得更大：矮身动物（猫 / 鸡 / 鼠）的 east 贴图在身体上下都是透明行，
            // 原先的 0.28..0.72 会把透明像素采进来，Cutout 着色器直接丢弃这些像素，
            // 模型会被打出一个个洞。正中一小块在所有物种上都落在身体内，只损失花纹细节，
            // 而原版动物本就是近乎单色的卡通体，损失可忽略。
            var uv = new UvSpec
            {
                top = new Vector4(0.42f, 0.42f, 0.58f, 0.58f),
                side = new Vector4(0.42f, 0.44f, 0.58f, 0.56f),
                sideTileH = 0f, sideTileW = 0f, modelXZ = new Vector2(1f, 1f)
            };
            var builder = new ModelBuilder(uv, false);
            builder.AddAll(model);
            var st = FirstPerson3DMod.settings;
            float ow = (st != null ? st.outlineWidth : Metrics3D.OutlineWidth) * Mathf.Clamp(L, 0.6f, 2.2f);
            b = new Built { body = builder.ToMesh(), hull = builder.ToHull(ow), bodyCenterY = cy, bodyHalfW = hw, L = L };
            cache[key] = b;
            return b;
        }

        // ---- 绘制 ----

        private static readonly Dictionary<int, float> smoothYaw = new Dictionary<int, float>();

        public static void Draw(Pawn pawn, Vector3 pos, Vector3 camPos)
        {
            Graphic g = null;
            try { g = pawn.Drawer.renderer.renderTree != null ? pawn.Drawer.renderer.renderTree.BodyGraphic : null; } catch { }
            if (g == null)
            {
                try { g = pawn.ageTracker.CurKindLifeStage.bodyGraphicData != null ? pawn.ageTracker.CurKindLifeStage.bodyGraphicData.Graphic : null; } catch { }
            }
            if (g == null) return;
            Material mat = null;
            try { mat = g.MatAt(Rot4.East, pawn); } catch { }
            if (mat == null || mat.mainTexture == null) { try { mat = g.MatSingle; } catch { } }
            if (mat == null) return;

            float L = Mathf.Clamp(Mathf.Max(g.drawSize.x, g.drawSize.y) * Metrics3D.AnimalTextureFill, 0.35f, 4.5f);
            var built = Get(pawn, L);
            if (built == null || built.body == null) return;

            // 朝向：原版四向；移动中朝向下一格，并平滑过渡
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
                // 侧卧：绕前向轴翻 80°，身体中心落到地面上方半个体宽
                matrix = Matrix4x4.TRS(pos + new Vector3(0f, built.bodyHalfW, 0f), yawRot * Quaternion.Euler(0f, 0f, 80f), Vector3.one)
                       * Matrix4x4.Translate(new Vector3(0f, -built.bodyCenterY, 0f));
            }
            else
            {
                matrix = Matrix4x4.TRS(pos + new Vector3(0f, bob, 0f), yawRot, Vector3.one);
            }
            Graphics.DrawMesh(built.body, matrix, mat, 0);
            PerfCounters.AnimalMeshes++;
            if (Fps3DState.DrawOutlines && built.hull != null)
            {
                Graphics.DrawMesh(built.hull, matrix, Util3D.OutlineMat, 0);
                PerfCounters.OutlineMeshesDrawn++;
            }
        }
    }

    /// <summary>按地图遍历动物并交给 Animal3D 绘制。</summary>
    public class AnimalLayer3D
    {
        private readonly Map map;
        private readonly float baseY;
        public Vector3 worldOffset = Vector3.zero;

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
                    Animal3D.Draw(p, new Vector3(dp.x + worldOffset.x, baseY, dp.z + worldOffset.z), camPos);
                }
                catch { }
            }
        }

        public void Dispose() { }
    }
}
