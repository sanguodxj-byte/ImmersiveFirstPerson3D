using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 三维光照（F013）：全局状态、着色分量与实例材质乘子
    // ============================================================
    //
    // 规范与出处：docs/features/F013-lighting.md。载体取舍：docs/decisions/0004-vertex-color-lighting.md。
    //
    // 本模组没有自定义着色器可用（本机无 Unity Editor），光照只有两条乘子通路：
    //   1) 顶点色 RGB —— 位置已知的合批几何（地形 / 墙体 chunk / 实例批 / 原型网格形体明暗）
    //   2) 实例材质 color —— 共享原型网格的逐件对象（家具 / 残骸 / 动物 / 人物立牌）
    // 两者都是「贴图 × 材质色 × 顶点色」链上的乘子，绝不改 alpha 语义（地形软边靠它）。
    //
    // 数值真值：太阳与天空亮度来自 SkyScenery.Env（与看见的天空同一份），灯具来自 map.glowGrid。
    public static class Lighting3D
    {
        // ---- 规范常数（可调项走 FirstPerson3DSettings，属性里覆盖）----

        /// <summary>glowGrid 原色到亮度的增益，与原版 GlowGrid.GroundGameGlowFactor 同值。</summary>
        public const float LampGain = 3.6f;

        /// <summary>原版「Lit」判定阈值（GlowGrid.PsychGlowAtGlow 同值）。</summary>
        public const float GameGlowLitThreshold = 0.3f;

        /// <summary>薄屋顶透过的天光比例。</summary>
        public const float ThinRoofSkyThrough = 0.35f;

        /// <summary>接触阴影最深倍率，防止内凹角被压成纯黑。</summary>
        public const float AoMin = 0.35f;

        /// <summary>
        /// 月夜弱直射强度。0.20 时乘上曝光只有 0.124，被亮度下限（0.16）整个吃掉 ——
        /// 模拟台量到朝月面与背月面完全同亮，等于夜里没有月光方向。0.45 让朝月面落在 0.24 左右。
        /// </summary>
        public const float MoonStrength = 0.45f;

        /// <summary>
        /// 天光（环境项）增益。调色板归一后天光能到 1.0，正午再叠法向直射 1.0 必然截断成纯白
        /// （实机：terrain 朝上均值 248/255；模拟台：朝上面 b=1.000）。
        /// 晴天正午的漫射入射本就不到法向直射的一半，0.45 就是这个比例，
        /// 留出头部空间让向阳/背阴的差看得见（朝上向阳 ≈0.83，朝上露天阴影 ≈0.20）。
        /// </summary>
        public const float AmbGain = 0.45f;

        /// <summary>逐件对象的方向形状权重：直射与半球环境各自相对「朝上参考」的偏移量。</summary>
        public const float FormSunWeight = 0.55f;
        public const float FormAmbWeight = 0.30f;
        public const float FormMin = 0.30f;
        public const float FormMax = 1.60f;

        /// <summary>
        /// 原型网格（家具/残骸/动物/植物模型）形体明暗用的固定风格光方向，与手持终端同一口径。
        /// 刻意不跟太阳走：原型网格是按图共享的缓存，跟太阳就得每过一档清缓存重烘 ——
        /// 实机后果是 7 个植物 OBJ 反复重导入、上百万顶点的实例批反复重建，帧时间直接崩。
        /// 世界里的方向性明暗由合批几何（墙/地/水）承担，逐件对象只随所在处亮度整体升降。
        /// </summary>
        public static readonly Vector3 FormLightDir = new Vector3(0.25f, 0.7f, 0.65f).normalized;

        /// <summary>实例材质缓存上限，超限整表清空（下一帧按需重建）。</summary>
        public const int LitMatCacheCap = 4096;

        /// <summary>自发光件（当前应发光的灯具）自身亮度下限。</summary>
        public const float EmissiveFloor = 0.85f;

        /// <summary>
        /// 影响烘出来的顶点色的可调项指纹。根驱动每帧比对，变了就整层标脏（这些参数标 * ）。
        /// 不含它的话，滑杆推到底画面也不动 —— 几何没变，chunk 不会自己重烘。
        /// </summary>
        public static long ParamKey()
        {
            var s = S;
            if (s == null) return 0L;
            long h = 17;
            h = h * 31 + (s.lightOn ? 1 : 0);
            h = h * 31 + (s.lightShadows ? 1 : 0);
            h = h * 31 + (s.lightAo ? 1 : 0);
            h = h * 31 + Q(s.lightFloor);
            h = h * 31 + Q(s.lightSun);
            h = h * 31 + Q(s.lightLamp);
            h = h * 31 + Q(s.lightAoStrength);
            h = h * 31 + Q(s.lightSoftFoot);
            h = h * 31 + Q(s.lightSoftTop);
            h = h * 31 + Q(s.lightAmbTint);
            h = h * 31 + Q(s.lightQuadStep);
            h = h * 31 + (CarrierProbe ? 1 : 0);
            // F016：色调与画质档改了同样要重烘
            h = h * 31 + (s.lightToneMap ? 1 : 0);
            h = h * 31 + (s.lightKelvinSun ? 1 : 0);
            h = h * 31 + Q(s.lightExposure);
            h = h * 31 + Q(s.lightWhitePoint);
            h = h * 31 + Q(s.lightHighlightDesat);
            h = h * 31 + Q(s.lightSplitTone);
            h = h * 31 + Q(s.lightSaturation);
            h = h * 31 + Q(s.lightNightDesat);
            h = h * 31 + Shaderpack3D.BakeKey();
            h = h * 31 + (SkyScenery.DebugPreset != null ? SkyScenery.DebugPreset.GetHashCode() : 0);
            return h;
        }

        // 指纹用的粗量化（1/64）：滑杆微调要能触发重烘，但不必逐位都触发。
        private static long Q(float v)
        {
            return (long)(Mathf.Clamp(v, -4f, 32f) * 64f);
        }

        private static FirstPerson3DSettings S => FirstPerson3DMod.settings;

        public static bool Enabled => S == null ? true : S.lightOn;
        public static bool ShadowsOn => Enabled && (S == null || S.lightShadows);
        public static bool GlowQuadsOn => Enabled && (S == null || S.lightGlow);
        public static float LightFloor => S != null ? Mathf.Clamp01(S.lightFloor) : 0.16f;
        public static float SunStrength => S != null ? S.lightSun : 1f;
        public static float LampStrength => S != null ? S.lightLamp : 1f;
        public static float AoStrength => (S == null ? true : S.lightAo) ? (S != null ? S.lightAoStrength : 0.45f) : 0f;
        public static float SoftFoot => S != null ? S.lightSoftFoot : -0.10f;
        public static float SoftTop => S != null ? S.lightSoftTop : 0.35f;
        public static float AmbTint => S != null ? Mathf.Clamp01(S.lightAmbTint) : 0.55f;
        public static float SweepRows => S != null ? Mathf.Max(1f, S.lightSweepRows) : 6f;

        /// <summary>受光几何的最大面片边长（米）。顶点色逐顶点，格子比这粗就糊掉灯下细节。</summary>
        public static float QuadStep => S != null ? Mathf.Clamp(S.lightQuadStep, 1f, 16f) : 2f;

        // ---- 当帧环境与参数（由 Scene3D 按图设置，光照与天空同一份数值）----

        private static SkyScenery.Env current = SkyScenery.Sample(null);

        public static SkyScenery.Env Current => current;

        /// <summary>
        /// 纯函数层（LightShade）要的环境与参数快照。BeginMap 每帧每图刷一次，
        /// 逐顶点只读结构体，不在最热的路径里去碰设置对象和原版属性。
        /// </summary>
        internal static LightEnv EnvNow;
        internal static LightTuning TuneNow;

        /// <summary>切到某张图：取该图当帧环境，并刷新着色用的两份快照。</summary>
        public static void BeginMap(Map map)
        {
            current = SkyScenery.SampleCached(map);
            EnvNow = new LightEnv
            {
                sunDir = current.sunDir,
                moonDir = current.moonDir,
                glow = current.glow,
                twilight = current.twilight,
                skyZenith = current.sky,
                skyHorizon = current.horizon,
                sunGlowHorizon = current.sunGlowHorizon,
                kelvinSun = KelvinSunOn,
            };
            TuneNow = new LightTuning
            {
                floor = LightFloor,
                exposure = Exposure,
                ambGain = AmbGain,
                sunStrength = SunStrength,
                lampStrength = LampStrength,
                lampGain = LampGain,
                aoStrength = AoStrength,
                aoMin = AoMin,
                softFoot = SoftFoot,
                softTop = SoftTop,
                ambTint = AmbTint,
                moonStrength = MoonStrength,
                // F016 L1 胶片色调
                toneMap = ToneMapOn,
                whitePoint = S != null ? S.lightWhitePoint : ShaderpackDefaults.WhitePoint,
                highlightDesat = S != null ? S.lightHighlightDesat : ShaderpackDefaults.HighlightDesat,
                splitTone = S != null ? S.lightSplitTone : ShaderpackDefaults.SplitTone,
                shadowTint = ShaderpackDefaults.ShadowTint,
                highlightTint = ShaderpackDefaults.HighlightTint,
                saturation = S != null ? S.lightSaturation : ShaderpackDefaults.Saturation,
                nightDesat = S != null ? S.lightNightDesat : ShaderpackDefaults.NightDesat,
            };
            Shaderpack3D.ApplyWeather(ref EnvNow, ref TuneNow, current);
        }

        // ---- 公式本体在 LightShade.cs（离线模拟场景台与游戏共用），这里只留调用点的薄封装 ----

        /// <summary>半球环境光颜色（按 N.y 插值）。公式在 LightShade，这里只是取当帧快照。</summary>
        public static Color AmbientColor(float hemi)
        {
            return LightShade.AmbientColor(ref EnvNow, ref TuneNow, hemi);
        }

        /// <summary>直射光颜色：正午近白，晨昏按霞光色转暖。</summary>
        public static Color SunColor()
        {
            return LightShade.SunColor(ref EnvNow);
        }

        /// <summary>月夜色：偏冷近白（不取夜空色相，理由见 LightShade.MoonColor）。</summary>
        public static Color MoonColor()
        {
            return LightShade.MoonColor();
        }

        /// <summary>向阳项：smoothstep 区间由 SoftFoot/SoftTop 决定，两者相等即硬分档。</summary>
        public static float SunShape(float ndl)
        {
            return LightShade.SunShape(ndl, ref TuneNow);
        }

        /// <summary>灯具项：原版逐格彩色辉光 → 加性颜色。</summary>
        public static Color LampColor(Color glowRaw)
        {
            return LightShade.LampColor(glowRaw, ref TuneNow);
        }

        /// <summary>合成下限与截断。</summary>
        public static Color ClampWithFloor(Color c)
        {
            // 山体那条通路也走这里：夜视权重按天色给（山体自己的灯具项已经加在 c 里）
            return LightShade.ClampWithFloor(c, ref TuneNow, Mathf.Clamp01(1f - EnvNow.glow * 2f));
        }

        /// <summary>
        /// 合成前的总曝光：环境项 + 直射 + 灯具在正午能加到 1.5 以上，不压一档就全被截成纯白，
        /// 正午看上去和没光照一样（实机量测：terrain 朝上桶均值 248/255，9.4 万顶点是纯白）。
        /// </summary>
        public const float ExposureLegacy = 0.62f;

        /// <summary>
        /// F016 L1：胶片色调开时曝光默认 1.0（曲线自己留头部空间），关时回到 F013 的 0.62。
        /// </summary>
        public static float Exposure => ToneMapOn
            ? (S != null ? Mathf.Clamp(S.lightExposure, 0.2f, 4f) : ShaderpackDefaults.Exposure)
            : ExposureLegacy;

        public static bool ToneMapOn => S == null || S.lightToneMap;
        public static bool KelvinSunOn => S == null || S.lightKelvinSun;

        /// <summary>
        /// 原型网格的形体明暗（固定风格光方向，见 FormLightDir）。
        /// 绝对亮度由实例材质乘子承担，两者相乘不重复计太阳。
        /// </summary>
        public static Color FormShape(Vector3 n)
        {
            if (!Enabled) return Color.white;
            return LightShade.FormShape(n, ref EnvNow, ref TuneNow, FormLightDir,
                FormSunWeight, FormAmbWeight, FormMin, FormMax);
        }

        /// <summary>自发光件的顶点色：不吃暗部，按自身下限抬亮。</summary>
        public static Color Emissive()
        {
            float f = EmissiveFloor;
            return new Color(f, f, f, 1f);
        }

        /// <summary>
        /// 载体探针（F9 或设置 lightProbe 切换，只用于验证）：把所有烘进网格的顶点色强制成一个常数灰。
        /// 顶点色若真被着色器乘上，画面必须整片变暗；纹丝不动就说明 Map/Cutout 与地形着色器
        /// 不吃顶点色 RGB，光照必须换载体（材质色分档）。手持终端那句注释不算证据。
        /// 做成设置项是因为合成按键在这台机器上不一定落得进窗口，而设置文件离线就能改。
        /// </summary>
        public static bool CarrierProbe => S != null && S.lightProbe;

        /// <summary>0..1 亮度 -> 顶点色。alpha 固定 255：需要保留遮罩语义的层自己写 A。</summary>
        public static Color32 To32(Color c)
        {
            if (CarrierProbe)
                return new Color32(64, 64, 64, 255);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
                (byte)255);
        }

        // ---- 实例材质乘子（逐件绘制的对象）----

        /// <summary>
        /// 逐件对象的放置亮度（F013）。按所在格地面取样：ambient/灯具与合批几何同一份数，
        /// 方向细节由原型网格的顶点色（FormShape）承担，两者相乘不重复计太阳。
        /// 当前应发光的灯具自身抬到 EmissiveFloor，灯头不会跟着房间一起黑掉。
        /// </summary>
        public static Color InstanceLevel(Thing t, LightField3D field, Vector3 localAtFeet)
        {
            if (!Enabled || field == null) return Color.white;
            var c = field.Placement(localAtFeet);
            if (Glows(t))
            {
                float f = EmissiveFloor;
                c.r = Mathf.Max(c.r, f); c.g = Mathf.Max(c.g, f); c.b = Mathf.Max(c.b, f);
            }
            return c;
        }

        /// <summary>该物件当前是否真的在发光（CompGlower.Glows，含断电与点燃状态）。</summary>
        public static bool Glows(Thing t)
        {
            if (t == null) return false;
            try
            {
                var c = t.TryGetComp<CompGlower>();
                return c != null && c.Glows;
            }
            catch { return false; }
        }

        private static readonly Dictionary<long, Material> litMats = new Dictionary<long, Material>();
        public static int LitMatHits;
        public static int LitMatMisses;

        /// <summary>
        /// 按放置亮度取克隆材质。源材质的 color 与主贴图语义保持不变，光照只乘进去。
        /// 亮度量化到每通道 5 bit，控制缓存规模；超限整表清空。
        /// 遮罩门控着色器（CutoutComplex 系：非原版服饰常用，见 GatedColorShaders）
        /// 的 _Color 只作用于 _MaskTex 红通道标记的区域，材质色乘子压不暗 ——
        /// 这些材质直接返回原样，由调用方追加 DrawDarken 叠加压暗。
        /// </summary>
        public static Material MatFor(Material src, Color level)
        {
            if (src == null) return null;
            if (!Enabled) return src;
            if (!DarkensByColor(src)) return src;
            int qr = Quant(level.r), qg = Quant(level.g), qb = Quant(level.b);
            if (qr == 31 && qg == 31 && qb == 31) return src;   // 满亮，不用克隆
            long key = ((long)src.GetInstanceID() << 15) | (uint)((qr << 10) | (qg << 5) | qb);
            if (litMats.TryGetValue(key, out var m) && m != null)
            {
                LitMatHits++;
                return m;
            }
            LitMatMisses++;
            m = new Material(src);
            m.color = src.color * Denorm(qr, qg, qb);
            if (litMats.Count >= LitMatCacheCap) ClearLitMats();
            litMats[key] = m;
            return m;
        }

        /// <summary>
        /// 着色器是否吃材质色乘子。CutoutComplex 系（原版「队伍色/双色」着色器，
        /// 种族扩展与社区服饰大量使用）把 _Color 乘在 _MaskTex 红通道门控的区域里：
        /// 遮罩为黑时材质色完全不起作用，MatFor 克隆等于空转，必须走叠加压暗回退。
        /// 出自 CutoutComplex.frag.glsl：colMultiplier = colPower.r * (_Color * vColor) + (1 - colPower.r)。
        /// </summary>
        private static readonly System.Collections.Generic.HashSet<string> gatedColorShaders
            = new System.Collections.Generic.HashSet<string>
        {
            "CutoutComplex", "CutoutComplexBlend", "CutoutComplexUI",
            "CutoutSkinOverlay", "CutoutWithOverlay"
        };

        public static bool DarkensByColor(Material src)
        {
            if (src == null || src.shader == null) return true;
            return !gatedColorShaders.Contains(src.shader.name);
        }

        /// <summary>
        /// 遮罩门控材质的受光回退：同一网格、同一贴图、黑色、alpha = 1 - 亮度，
        /// 朝黑做 alpha 混合 = 均匀压暗，不改色相、按贴图 alpha 自动贴住轮廓。
        /// 与 HitFx3D 叠加同一套机制（TransparentPostLight + 高 renderQueue 后画）。
        /// 全亮或吃材质色乘子的材质直接跳过。
        /// </summary>
        private static readonly Dictionary<long, Material> darkenMats = new Dictionary<long, Material>();
        private const int DarkenBucketN = 16;

        public static void DrawDarken(Mesh mesh, Matrix4x4 matrix, Material src, Color level)
        {
            if (!Enabled || mesh == null || src == null) return;
            if (DarkensByColor(src)) return;   // 已被材质色乘子压暗，无需叠加
            float g = (level.r + level.g + level.b) / 3f;
            int q = Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp01(g)) * DarkenBucketN), 0, DarkenBucketN);
            if (q <= 0) return;
            var tex = src.mainTexture;
            if (tex == null) return;   // 无贴图就没有轮廓可压
            long key = ((long)tex.GetInstanceID() << 5) | (uint)q;
            if (!darkenMats.TryGetValue(key, out var m) || m == null)
            {
                if (darkenMats.Count >= LitMatCacheCap) ClearDarkenMats();
                m = new Material(ShaderDatabase.TransparentPostLight ?? ShaderDatabase.Transparent)
                {
                    name = "Fps3D_Darken",
                    renderQueue = 2917
                };
                m.mainTexture = tex;
                darkenMats[key] = m;
            }
            m.color = new Color(0f, 0f, 0f, q / (float)DarkenBucketN);
            Graphics.DrawMesh(mesh, matrix, m, 0);
            PerfCounters.PawnDarkenQuads++;
        }

        public static void ClearDarkenMats()
        {
            foreach (var kv in darkenMats)
                if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            darkenMats.Clear();
        }

        private static int Quant(float v)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(v) * 31f), 0, 31);
        }

        private static Color Denorm(int qr, int qg, int qb)
        {
            return new Color(qr / 31f, qg / 31f, qb / 31f, 1f);
        }

        public static void ClearLitMats()
        {
            foreach (var kv in litMats)
                if (kv.Value != null) Object.Destroy(kv.Value);
            litMats.Clear();
            ClearDarkenMats();
        }

        // ---- 采样探针与导出（scripts/light_check.py 的输入）----

        /// <summary>
        /// 网格颜色通道统计：按法线分六桶记平均亮度、未受光（全白）顶点数与遮罩顶点数。
        /// 只在按需导出时读一次网格，不参与每帧路径。
        /// </summary>
        public sealed class Probe
        {
            public readonly int[] count = new int[6];
            public readonly int[] litN = new int[6];
            public readonly double[] luma = new double[6];
            public readonly int[] unlit = new int[6];
            public readonly int[] masked = new int[6];
            public double min = 255.0;
            public double max = 0.0;
            public int meshes;
            public int verts;
            public int meshesNoColor;
            // 一次导出的顶点读取预算：整场景顶点色+法线一次读出来是几十 MB 的临时分配，
            // 实机量测把 GC 顶到 52~72 MB/s、fps 掉到 7~22。超出预算的网格只做廉价的
            // 「有没有颜色通道」判定，D1 的存量检查看得到每一个网格，极值与均值按已读样本算。
            public int budget = 150000;

            // 桶序：0上 1下 2北(-Z) 3南(+Z) 4西(-X) 5东(+X)
            public static int Bucket(Vector3 n)
            {
                float ay = Mathf.Abs(n.y), ax = Mathf.Abs(n.x), az = Mathf.Abs(n.z);
                if (ay >= ax && ay >= az) return n.y >= 0f ? 0 : 1;
                if (az >= ax) return n.z <= 0f ? 2 : 3;
                return n.x <= 0f ? 4 : 5;
            }

            /// <summary>不读顶点的廉价检查：只数网格数与「有没有颜色通道」。</summary>
            public void AddMeshHeader(Mesh m)
            {
                meshes++;
                if (m == null) return;
                bool hasColor = true;
                try
                {
                    hasColor = m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color);
                }
                catch { hasColor = true; }   // 查不动时不算缺通道，宁可漏报不误报
                verts += m.vertexCount;
                if (!hasColor) meshesNoColor++;
            }

            public void Add(Mesh m)
            {
                if (m == null) return;
                if (budget <= 0) { AddMeshHeader(m); return; }
                meshes++;
                var cols = m.colors32;
                if (cols == null || cols.Length == 0) { meshesNoColor++; return; }
                budget -= cols.Length;
                var nors = m.normals;
                verts += cols.Length;
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i];
                    int b = (nors != null && nors.Length == cols.Length) ? Bucket(nors[i]) : 0;
                    count[b]++;
                    if (c.a < 255) masked[b]++;
                    // alpha 为 0 的顶点根本不出像素（地形软边遮罩），拿它算亮度极值会假报"低于下限"
                    if (c.a == 0) continue;
                    litN[b]++;
                    float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    luma[b] += l;
                    if (c.r == 255 && c.g == 255 && c.b == 255) unlit[b]++;
                    if (l < min) min = l;
                    if (l > max) max = l;
                }
            }

            public void Merge(Probe o)
            {
                for (int i = 0; i < 6; i++)
                {
                    count[i] += o.count[i];
                    litN[i] += o.litN[i];
                    luma[i] += o.luma[i];
                    unlit[i] += o.unlit[i];
                    masked[i] += o.masked[i];
                }
                meshes += o.meshes;
                verts += o.verts;
                meshesNoColor += o.meshesNoColor;
                if (o.min < min) min = o.min;
                if (o.max > max) max = o.max;
            }

            public void Write(StringBuilder sb, string name)
            {
                sb.Append('{').Append("\"name\":\"").Append(name).Append('"');
                sb.Append(",\"meshes\":").Append(meshes);
                sb.Append(",\"meshesNoColor\":").Append(meshesNoColor);
                sb.Append(",\"verts\":").Append(verts);
                sb.Append(",\"min\":").Append(F(min)).Append(",\"max\":").Append(F(max));
                sb.Append(",\"buckets\":[");
                for (int i = 0; i < 6; i++)
                {
                    if (i > 0) sb.Append(',');
                    // 平均亮度只对可见顶点（alpha>0）求，遮罩顶点不参与
                    double mean = litN[i] > 0 ? luma[i] / litN[i] : 0.0;
                    sb.Append("{\"n\":").Append(count[i])
                      .Append(",\"lit\":").Append(litN[i])
                      .Append(",\"mean\":").Append(F(mean))
                      .Append(",\"unlit\":").Append(unlit[i])
                      .Append(",\"masked\":").Append(masked[i]).Append('}');
                }
                sb.Append("]}");
            }

            private static string F(double v)
            {
                return v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>把当帧光照状态与各层网格的颜色统计导出成 JSON，供 GABS 回读与机检。</summary>
        public static string DumpJson()
        {
            var sb = new StringBuilder(1024);
            var e = current;
            sb.Append('{');
            sb.Append("\"on\":").Append(Enabled ? 1 : 0);
            sb.Append(",\"probe\":").Append(CarrierProbe ? 1 : 0);
            sb.Append(",\"floor\":").Append(F(LightFloor));
            sb.Append(",\"amb\":").Append(F(AmbGain));
            sb.Append(",\"moon\":").Append(F(MoonStrength));
            sb.Append(",\"sun\":").Append(F(SunStrength));
            sb.Append(",\"lampStrength\":").Append(F(LampStrength));
            sb.Append(",\"ao\":").Append(F(AoStrength));
            sb.Append(",\"shadows\":").Append(ShadowsOn ? 1 : 0);
            sb.Append(",\"soft\":[").Append(F(SoftFoot)).Append(',').Append(F(SoftTop)).Append(']');
            sb.Append(",\"glow\":").Append(F(e.glow));
            sb.Append(",\"celGlow\":").Append(F(e.celGlow));
            sb.Append(",\"twilight\":").Append(F(e.twilight));
            sb.Append(",\"flash\":").Append(F(e.flash));
            sb.Append(",\"sunDir\":").Append(V(e.sunDir));
            sb.Append(",\"moonDir\":").Append(V(e.moonDir));
            sb.Append(",\"litMat\":{\"hits\":").Append(LitMatHits)
              .Append(",\"misses\":").Append(LitMatMisses).Append('}');
            sb.Append(",\"sweepMs\":").Append(PerfCounters.LastLightSweepMs.ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(',');
            HitFx3D.Snapshot(sb);      // F014：受击反馈的当帧读数，跟着同一份 dump 出来
            sb.Append(',');
            ShotFx3D.Snapshot(sb);     // F015：射击效果的当帧读数
            sb.Append(',');
            Shaderpack3D.Snapshot(sb); // F016：画质档、预算与引擎能力
            sb.Append(",\"maps\":[");
            bool any = false;
            foreach (var sc in SceneRegistry.All)
            {
                if (sc == null || sc.map == null) continue;
                if (any) sb.Append(',');
                any = true;
                sc.WriteLightJson(sb);
            }
            sb.Append("]}");
            return sb.ToString();

            static string F(double v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            static string V(Vector3 p) =>
                "[" + F(p.x) + "," + F(p.y) + "," + F(p.z) + "]";
        }
    }
}
