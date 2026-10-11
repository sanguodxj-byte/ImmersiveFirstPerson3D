// AI生成
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimFirstPerson3D
{
    // ============================================================
    // 单图光照场（F013）
    // ============================================================
    //
    // 逐格缓存四张表：
    //   skyVis  天空遮蔽（露天 255 / 薄屋顶 / 厚屋顶 0）
    //   lamp    灯具彩色辉光（原版 map.glowGrid 的拷贝，含颜色与断电）
    //   topH    实心高度场（满格建筑与屋顶，供投影与接触阴影判定）
    //   sunLo/Hi 向阳投影（两条高度带各一张，0=被挡 255=直射）
    //
    // 真值口径写在 docs/features/F013-lighting.md。本文件不定义米制，只引用 Metrics3D。
    //
    // 【为什么扫而不是全量重算】
    // 一盏灯开关、一个屋顶变化、太阳走过 5° 都会改亮度。整图重算要几万格 × 十几步射线，
    // 放进一帧里就是可见的卡顿。这里每帧只推进一条带（默认 6 行），一轮扫完再回到顶，
    // 变化分摊到约 0.1~0.2 秒；只有值真的变了才把所在 chunk 标脏。
    public sealed class LightField3D : ILightField
    {
        private readonly MountainField3D mountain;

        /// <summary>低带采样高度（米）：地面受影。</summary>
        public const float ShadowSampleLow = 0.40f;

        /// <summary>射线步数上限，与原版 GenCelestial.ShadowMaxLengthDay 同值。</summary>
        public const int ShadowMaxSteps = 15;

        private readonly Map map;
        private readonly int sizeX;
        private readonly int sizeZ;
        private readonly float baseY;

        private readonly byte[] skyVis;
        private readonly float[] topH;
        private readonly float[] bottomH;
        private readonly float[] treeTopH;
        private readonly byte[] sunLo;
        private readonly byte[] sunHi;
        private readonly Color32[] lamp;

        // 本帧扫完后收集到的变化格，由 Scene3D 消费并标脏对应 chunk。
        private readonly List<IntVec3> changed = new List<IntVec3>();
        // 灯具/屋顶/实心高度变化格（区别于纯太阳投影移动），用于驱动植被/草丛/物品的局部重着色。
        private readonly List<IntVec3> lampChanged = new List<IntVec3>();

        // 太阳基向量（本帧）：水平单位步长 + 每前进 1 米升高多少
        private float hsx, hsz, slope;
        private bool sunActive;
        private float maxOccY;

        public LightField3D(Map map, float baseY, MountainField3D mountain = null)
        {
            this.mountain = mountain;
            this.map = map;
            this.baseY = baseY;
            sizeX = map.Size.x;
            sizeZ = map.Size.z;
            int n = sizeX * sizeZ;
            skyVis = new byte[n];
            topH = new float[n];
            bottomH = new float[n];
            treeTopH = new float[n];
            sunLo = new byte[n];
            sunHi = new byte[n];
            lamp = new Color32[n];
        }

        public int CellCount => skyVis.Length;
        public IReadOnlyList<IntVec3> Changed => changed;
        public IReadOnlyList<IntVec3> LampChanged => lampChanged;

        // ------------------------------------------------------------
        // 每帧推进
        // ------------------------------------------------------------

        /// <summary>刷新一条带。env 来自 SkyScenery，光照与天空共用同一份太阳数值。</summary>
        public void Sweep(SkyScenery.Env env)
        {
            changed.Clear();
            lampChanged.Clear();
            if (!Lighting3D.Enabled) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ComputeSunBasis(env);
            int rows = Mathf.Clamp(Mathf.RoundToInt(Lighting3D.SweepRows), 1, 32);
            int z0 = sweepZ;
            int z1 = Mathf.Min(sizeZ, z0 + rows);
            for (int z = z0; z < z1; z++)
                for (int x = 0; x < sizeX; x++)
                    UpdateCell(x, z, env);
            sweepZ = z1 >= sizeZ ? 0 : z1;
            if (sweepZ == 0) primed = true;
            PerfCounters.LastLightSweepMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        private bool primed;
        public bool Primed => primed;

        /// <summary>
        /// 一次铺满整图（进第一人称时用一次）。
        /// 不这么做的话：数组初值全是 0，扫描带每帧都在"改值"，于是整图 chunk 被连续标脏几百帧，
        /// 而且第一批烘出来的网格读到的还是没扫到的格子（暗）。250x250 图实测把桥顶到 30 s 超时。
        /// 一次铺满 = 一次性的几十毫秒，之后才允许烘颜色。
        /// </summary>
        public void Prime(SkyScenery.Env env)
        {
            changed.Clear();
            lampChanged.Clear();
            if (!Lighting3D.Enabled) { primed = true; return; }
            ComputeSunBasis(env);
            dirtyTrack = false;
            // 先把整图高度场 (topH/bottomH) 与天光/灯具铺满，再算射线投影，保证 RayLit 读到的高度表完整。
            for (int z = 0; z < sizeZ; z++)
                for (int x = 0; x < sizeX; x++)
                    UpdateCellStructAndLamp(x, z);
            for (int z = 0; z < sizeZ; z++)
                for (int x = 0; x < sizeX; x++)
                    UpdateCellShadow(x, z);
            dirtyTrack = true;
            sweepZ = 0;
            primed = true;
        }

        private int sweepZ;

        private void ComputeSunBasis(SkyScenery.Env env)
        {
            // 对太阳方向做 1/24 分档量化（约 2.4° 一档），避免连续浮点漂移导致远距投影边界每帧翻转标脏。
            var raw = env.sunDir;
            Vector3 d = new Vector3(
                Mathf.Round(raw.x * 24f) / 24f,
                Mathf.Round(raw.y * 24f) / 24f,
                Mathf.Round(raw.z * 24f) / 24f);
            if (d.sqrMagnitude > 1e-4f) d.Normalize();
            else d = raw;
            float hl = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            sunActive = d.y > 0.02f && hl > 0.02f && env.glow > 0.001f;
            if (!sunActive) return;
            hsx = d.x / hl;
            hsz = d.z / hl;
            slope = d.y / hl;
        }

        private bool UpdateCellStructAndLamp(int x, int z)
        {
            int i = z * sizeX + x;
            bool structOrLampDirty = false;

            // ---- 天空遮蔽 + 实心高度场 ----
            byte sv = 255;
            float th = 0f, bh = 0f;
            RoofDef rd = null;
            try { rd = map.roofGrid != null ? map.roofGrid.RoofAt(x, z) : null; } catch { }
            if (rd != null)
                sv = rd.isThickRoof ? (byte)0 : (byte)Mathf.RoundToInt(Lighting3D.ThinRoofSkyThrough * 255f);
            var c = new IntVec3(x, 0, z);
            Building ed = null;
            try { ed = c.GetEdifice(map); } catch { }
            if (mountain != null && mountain.TryInterval(x + 0.5f, z + 0.5f, out float low, out float high))
            {
                th = high;
                bh = low;
            }
            if (ed != null && ed.def != null && ed.def.Fillage == FillCategory.Full
                && !MountainField3D.IsRock(ed))
            {
                th = Mathf.Max(th, Fps3DState.WallHeight);
                bh = 0f;
            }
            else if (rd != null && th == 0f)
            {
                th = Fps3DState.StoryHeight;
                bh = Fps3DState.WallHeight;
            }
            float trH = 0f;
            if (th == 0f)
            {
                try
                {
                    var plant = c.GetPlant(map);
                    if (plant != null && plant.Spawned && plant.def != null && plant.def.plant != null)
                    {
                        if (plant.def.plant.IsTree)
                            trH = 2.8f * Mathf.Clamp(plant.Growth, 0.55f, 1.0f);
                        else if (plant.Growth > 0.25f)
                            trH = 0.85f * Mathf.Clamp(plant.Growth, 0.5f, 1.0f);
                    }
                    else if (ed != null && ed.def != null && ed.def.fillPercent >= 0.35f)
                    {
                        trH = 1.45f;
                    }
                    if (trH < 2.0f)
                    {
                        for (int d = 0; d < 8; d++)
                        {
                            var nc = c + GenAdj.AdjacentCells[d];
                            if (!nc.InBounds(map)) continue;
                            var np = nc.GetPlant(map);
                            if (np != null && np.Spawned && np.def != null && np.def.plant != null && np.def.plant.IsTree)
                            {
                                float ntr = 2.6f * Mathf.Clamp(np.Growth, 0.55f, 1.0f);
                                bool cardinal = (nc.x == x || nc.z == z);
                                float crownH = ntr * (cardinal ? 0.88f : 0.72f);
                                if (crownH > trH) trH = crownH;
                            }
                        }
                    }
                }
                catch { }
            }
            if (skyVis[i] != sv) { skyVis[i] = sv; structOrLampDirty = true; }
            if (topH[i] != th || bottomH[i] != bh || treeTopH[i] != trH)
            {
                topH[i] = th;
                bottomH[i] = bh;
                treeTopH[i] = trH;
                structOrLampDirty = true;
            }
            float absTop = baseY + Mathf.Max(th, trH);
            if (absTop > maxOccY) maxOccY = absTop;

            // ---- 灯具彩色辉光（原版数值，含断电与挡光板） ----
            var g = QuantGlow(SampleGlow(i));
            if (!g.Equals(lamp[i])) { lamp[i] = g; structOrLampDirty = true; }
            return structOrLampDirty;
        }

        private bool UpdateCellShadow(int x, int z)
        {
            int i = z * sizeX + x;
            byte sv = skyVis[i];
            byte lo = 255, hi = 255;
            if (Lighting3D.ShadowsOn && sv > 0)
            {
                if (sunActive)
                {
                    // 射线高度用世界绝对 Y：TopY 也是绝对的（baseY + 顶高），上层图的 baseY 不为 0，
                    // 拿「离地高度」去比「绝对 Y」会把整图判成全阴影。
                    lo = RayLitLow(x, z, baseY + ShadowSampleLow);
                    hi = RayLit(x, z, baseY + ShadowSampleHigh()) ? (byte)255 : (byte)0;
                }
            }
            else if (sv == 0) { lo = 0; hi = 0; }
            bool shadowDirty = false;
            if (sunLo[i] != lo) { sunLo[i] = lo; shadowDirty = true; }
            if (sunHi[i] != hi) { sunHi[i] = hi; shadowDirty = true; }
            return shadowDirty;
        }

        private void UpdateCell(int x, int z, SkyScenery.Env env)
        {
            bool slDirty = UpdateCellStructAndLamp(x, z);
            bool shDirty = UpdateCellShadow(x, z);
            if (slDirty || shDirty) AddChanged(x, z, slDirty);
        }

        private static float ShadowSampleHigh()
        {
            return Fps3DState.WallHeight * 0.9f;
        }

        // glowGrid 量化到 1/8，避免每帧亚像素级抖动把 chunk 反复标脏。
        private static Color32 QuantGlow(Color32 g)
        {
            if (g.a == 1) return new Color32(g.r, g.g, g.b, (byte)255);   // 原版 overlit 哨兵
            return new Color32(
                (byte)(g.r & 0xF8), (byte)(g.g & 0xF8), (byte)(g.b & 0xF8), g.a);
        }

        private Color32 SampleGlow(int index)
        {
            try
            {
                var gg = map.glowGrid;
                if (gg == null) return default(Color32);
                return gg.VisualGlowAt(index);
            }
            catch { return default(Color32); }
        }

        private void AddChanged(int x, int z, bool slDirty = false)
        {
            if (!dirtyTrack) return;          // 一次铺满时不记变化，避免把整图 chunk 全标脏
            var cell = new IntVec3(x, 0, z);
            if (changed.Count < 4096) changed.Add(cell);
            if (slDirty && lampChanged.Count < 4096) lampChanged.Add(cell);
        }

        // 铺满阶段关掉变化登记（Prime 里切）
        private bool dirtyTrack = true;

        // 从本格的 (x+0.5, y, z+0.5) 朝太阳方向走格子，撞到比 y 高的实心就判定为阴影。
        //
        // 起点必须抬到本格自身顶面之上：墙顶的顶点在 2.70 m，而带高按 0.9×墙高算只有 2.43 m，
        // 不抬的话旁边同样高的墙就把自己的墙顶挡成阴影（机检实测白天朝上面 58.5 < 朝下面 114.6）。
        private bool RayLit(int x, int z, float y)
        {
            float rx = x + 0.5f, rz = z + 0.5f;
            if (OccAt(rx, y, rz)) y = TopY(z * sizeX + x) + 0.05f;
            float ceilY = maxOccY > baseY ? maxOccY : baseY + Metrics3D.MountainMaxHeight + Fps3DState.StoryHeight;
            int maxSteps = mountain != null && mountain.Ready ? 24 : ShadowMaxSteps;
            for (int k = 1; k <= maxSteps; k++)
            {
                rx += hsx; rz += hsz; y += slope;
                int cx = Mathf.FloorToInt(rx), cz = Mathf.FloorToInt(rz);
                if (cx < 0 || cz < 0 || cx >= sizeX || cz >= sizeZ) return true;
                if (y > ceilY) return true;
                if (OccAt(rx, y, rz)) return false;
            }
            return true;
        }

        // 低带地面投影：实心墙/山体全遮挡(0)，树冠中心全遮挡(0)，树冠外缘/灌木投下深半影(24)。
        private byte RayLitLow(int x, int z, float y)
        {
            float rx = x + 0.5f, rz = z + 0.5f;
            int selfIdx = z * sizeX + x;
            if (OccAt(rx, y, rz)) y = TopY(selfIdx) + 0.05f;
            byte res = treeTopH[selfIdx] > 1.8f ? (byte)0 : (treeTopH[selfIdx] > 1.0f ? (byte)24 : (byte)255);
            float ceilY = maxOccY > baseY ? maxOccY : baseY + Metrics3D.MountainMaxHeight + Fps3DState.StoryHeight;
            int maxSteps = mountain != null && mountain.Ready ? 24 : ShadowMaxSteps;
            for (int k = 1; k <= maxSteps; k++)
            {
                rx += hsx; rz += hsz; y += slope;
                int cx = Mathf.FloorToInt(rx), cz = Mathf.FloorToInt(rz);
                if (cx < 0 || cz < 0 || cx >= sizeX || cz >= sizeZ) return res;
                if (y > ceilY) return res;
                int idx = cz * sizeX + cx;
                if (y >= baseY + bottomH[idx] && TopY(idx) > y) return 0;
                if (treeTopH[idx] > 0.1f && baseY + treeTopH[idx] > y)
                {
                    if (treeTopH[idx] > 1.8f) return 0;
                    if (res > 24) res = 24;
                }
            }
            return res;
        }

        private float TopY(int index) => baseY + topH[index];

        // ------------------------------------------------------------
        // 采样
        // ------------------------------------------------------------

        // 格心 (i+0.5) 双线性：顶点落在格角上时正好取四邻平均，灯下不会出现硬边。
        private void Bilinear(float x, float z, out int i00, out int i10, out int i01, out int i11,
            out float tx, out float tz)
        {
            float fx = Mathf.Clamp(x - 0.5f, 0f, sizeX - 1f);
            float fz = Mathf.Clamp(z - 0.5f, 0f, sizeZ - 1f);
            int bx = Mathf.Min(Mathf.FloorToInt(fx), sizeX - 2 > 0 ? sizeX - 2 : 0);
            int bz = Mathf.Min(Mathf.FloorToInt(fz), sizeZ - 2 > 0 ? sizeZ - 2 : 0);
            tx = Mathf.Clamp01(fx - bx);
            tz = Mathf.Clamp01(fz - bz);
            i00 = bz * sizeX + bx;
            i10 = i00 + 1;
            i01 = i00 + sizeX;
            i11 = i01 + 1;
        }

        private static float Lerp(byte a, byte b, float t)
        {
            return (a + (b - a) * t) / 255f;
        }

        public float SkyVisAt(float x, float z)
        {
            Bilinear(x, z, out int a, out int b, out int c, out int d, out float tx, out float tz);
            return Mathf.Lerp(Mathf.Lerp(Lerp(skyVis[a], skyVis[b], tx), Lerp(skyVis[c], skyVis[d], tx), tz), 1f, 0f);
        }


        public Color LampAt(float x, float z)
        {
            Bilinear(x, z, out int a, out int b, out int c, out int d, out float tx, out float tz);
            Color A = lamp[a], B = lamp[b], C = lamp[c], D = lamp[d];
            return Color.Lerp(Color.Lerp(A, B, tx), Color.Lerp(C, D, tx), tz);
        }

        public float SunLitAt(Vector3 p, Vector3 n)
        {
            var q = p + n * 0.5f;
            var arr = (q.y - baseY) > Fps3DState.WallHeight * 0.5f ? sunHi : sunLo;
            Bilinear(q.x, q.z, out int a, out int b, out int c, out int d, out float tx, out float tz);
            return Mathf.Lerp(Mathf.Lerp(Lerp(arr[a], arr[b], tx), Lerp(arr[c], arr[d], tx), tz), 1f, 0f);
        }

        // 点是否落在实心体里（直接查预计算的 bottomH/topH 表，O(1) 零分配）。
        private bool OccAt(float x, float y, float z)
        {
            int cx = Mathf.FloorToInt(x), cz = Mathf.FloorToInt(z);
            if (cx < 0 || cz < 0 || cx >= sizeX || cz >= sizeZ) return false;
            int i = cz * sizeX + cx;
            return y >= baseY + bottomH[i] && TopY(i) > y;
        }

        /// <summary>接触阴影：顶点所在角四周四格的占用比例（0=全空 1=全被围）。</summary>
        public float OccFraction(Vector3 p, Vector3 n)
        {
            // 面平面内的两根轴：水平面取 X/Z，竖直面取「沿面的水平轴」与 Y。
            Vector3 u, v;
            if (Mathf.Abs(n.y) > 0.5f) { u = Vector3.right; v = Vector3.forward; }
            else if (Mathf.Abs(n.x) > 0.5f) { u = Vector3.forward; v = Vector3.up; }
            else { u = Vector3.right; v = Vector3.up; }
            var q = p + n * 0.5f;
            float s = 0f;
            s += OccAt(q.x + u.x * 0.5f, q.y + u.y * 0.5f, q.z + u.z * 0.5f) ? 1f : 0f;
            s += OccAt(q.x + v.x * 0.5f, q.y + v.y * 0.5f, q.z + v.z * 0.5f) ? 1f : 0f;
            s += OccAt(q.x + (u + v).x * 0.5f, q.y + (u + v).y * 0.5f, q.z + (u + v).z * 0.5f) ? 1f : 0f;
            s += OccAt(q.x + (u - v).x * 0.5f, q.y + (u - v).y * 0.5f, q.z + (u - v).z * 0.5f) ? 1f : 0f;
            return s * 0.25f;
        }

        // ------------------------------------------------------------
        // 着色（规范里的合成式）
        // ------------------------------------------------------------

        public Color ShadeMountain(Vector3 p, Vector3 n)
        {
            if (!Lighting3D.Enabled) return Color.white;
            float localY = Mathf.Max(0f, p.y - baseY);

            // Sample sky visibility and lamp glow slightly out along the surface normal so vertical
            // cliff/cave walls read the adjacent open/roofed cell rather than the solid rock cell.
            Vector3 samplePos = p + n * 0.25f;
            float vis = (n.y > 0.05f || localY > Fps3DState.StoryHeight)
                ? 1f
                : SkyVisAt(samplePos.x, samplePos.z);

            float aoK = 1f;
            if (localY <= Metrics3D.MountainCaveCeiling + 0.5f && n.y <= 0.05f)
            {
                ref var tune = ref Lighting3D.TuneNow;
                if (tune.aoStrength > 0f)
                {
                    aoK = Mathf.Max(tune.aoMin, 1f - tune.aoStrength * OccFraction(p, n));
                }
            }

            var env = Lighting3D.Current;
            float hemi = 0.5f + 0.5f * n.y;
            // Subtle foot contact darkening + high-ridge relief + geological strata banding
            float footAo = Mathf.Lerp(0.84f, 1.0f, Mathf.Clamp01(localY / 1.4f));
            float elevLift = Mathf.Lerp(0.96f, 1.08f, Mathf.Clamp01(localY / Metrics3D.MountainMaxHeight));
            float strata = 1.0f + 0.045f * Mathf.Sin(localY * 2.4f + p.x * 0.18f + p.z * 0.14f);
            float rockMod = footAo * elevLift * strata * aoK;

            var color = Lighting3D.AmbientColor(hemi) * (env.glow * vis * rockMod);
            float direct = Lighting3D.SunShape(Vector3.Dot(n, env.sunDir));
            if (env.glow > 0.001f && direct > 0f && vis > 0.01f)
            {
                var q = p + n * 0.18f + Vector3.up * 0.12f;
                bool lit = true;
                if (Lighting3D.ShadowsOn && sunActive)
                {
                    float ceilY = maxOccY > baseY ? maxOccY : baseY + Metrics3D.MountainMaxHeight + Fps3DState.StoryHeight;
                    for (int k = 1; k <= 24; k++)
                    {
                        q.x += hsx; q.z += hsz; q.y += slope;
                        if (q.x < 0f || q.z < 0f || q.x >= sizeX || q.z >= sizeZ || q.y > ceilY) break;
                        if (OccAt(q.x, q.y, q.z)) { lit = false; break; }
                    }
                }
                if (lit) color += Lighting3D.SunColor() * (direct * env.glow * Lighting3D.SunStrength * vis * rockMod);
            }
            if (env.glow < 0.5f && vis > 0.01f)
                color += Lighting3D.MoonColor() * (Mathf.Max(0f, Vector3.Dot(n, env.moonDir))
                    * Lighting3D.MoonStrength * (1f - env.glow) * vis * rockMod);
            if (localY <= Metrics3D.MountainCaveCeiling + 0.5f)
            {
                float lampFade = Mathf.Clamp01((Metrics3D.MountainCaveCeiling + 0.5f - localY) / 0.5f);
                color += Lighting3D.LampColor(LampAt(samplePos.x, samplePos.z)) * lampFade;
            }
            return Lighting3D.ClampWithFloor(color);
        }

        /// <summary>位置 + 法线 -> 亮度乘子。p 用图内局部坐标（Y 含 baseY）。</summary>
        public Color Shade(Vector3 p, Vector3 n, bool ao)
        {
            // 公式本体在 LightShade（离线模拟场景台跑同一份），这里只喂当帧快照。
            return LightShade.Shade(this, p, n, ao, ref Lighting3D.EnvNow, ref Lighting3D.TuneNow);
        }

        /// <summary>
        /// 逐件对象 / 实例批的放置亮度（F013）：该处「平均有多亮」，不带方向。
        /// 按漫反射闭表面的平均入射算，不能按「朝上面」取样（正午会双双顶满，植被等于不受光）。
        /// 公式本体在 LightShade.Placement。方向感由原型网格的形体明暗负责。
        /// </summary>
        public Color Placement(Vector3 p)
        {
            if (!Lighting3D.Enabled) return Color.white;
            return LightShade.Placement(this, p, ref Lighting3D.EnvNow, ref Lighting3D.TuneNow);
        }
    }
}
