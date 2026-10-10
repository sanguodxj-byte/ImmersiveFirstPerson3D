// AI生成
using System.Collections.Generic;
using UnityEngine;

namespace RimFirstPerson3D
{
    // ============================================================
    // 地图野生建筑（残骸）样板 —— 7 组各出 1 个
    // ============================================================
    //
    // 来源：原版**带俯角**贴图（非正交俯视）反推几何；材质完全取样自
    // 去黑边后的原版贴图（见 docs/wreckage/clean/ 与 WreckageUVGen.cs）。
    // 本 mod 的赛璐璐描边由 ModelBuilder 的反绕外壳生成 —— 贴图内**不含**
    // 原版黑色描边。
    //
    // 坐标：模型局部空间，XZ 以模型中心为原点，Y 以底面为 0（旋转体的 pos.y
    // 亦为底面），单位=米。尺度：1 格 = 1.00 m，128 px = 1 格。
    // 高度：俯视贴图无法直接给出，下列高度按物件类型估设，**属渲染迭代项**。
    //
    // v3 重建要点（可用辨识度，经无标签盲测迭代）：
    //   坦克=低车体+双履带+负重轮+桶形炮塔+粗炮管；卡车=发动机罩+驾驶室+高货厢+
    //   多轮；飞船=钝头船体+成排舷窗+尾喷口+断裂板；步行机=躯干+头+两条分节机械腿；
    //   发动机=缸体+一排气缸盖+大飞轮盘+皮带轮+排气歧管；巨轮=多台阶圆盘+辐条+
    //   轮毂轴+胎面块；路障=下宽上窄混凝土墩。
    //
    // 每个方法都可直接喂给 ModelBuilder：
    //     var mb = new ModelBuilder(WreckageSamples.Uv(name), false);
    //     mb.AddAll(WreckageSamples.Build(name));
    // ============================================================

    public static class WreckageSamples
    {
        // ---- 样板清单（与 WreckageUVGen.R 的键一一对应）----

        /// <summary>@tex ruinedtank  @footprint 2.50 1.50</summary>
        public static Model3D BuildRuinedTank()
        {
            var m = new Model3D();
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, -0.560f), new Vector3(2.000f, 0.400f, 0.240f));   // 左履带
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, 0.560f), new Vector3(2.000f, 0.400f, 0.240f));    // 右履带
            m.Add(Prim.Box, new Vector3(0.000f, 0.390f, -0.640f), new Vector3(2.000f, 0.100f, 0.360f));    // 左履带裙板(挡泥板,俯视成连续履带带)
            m.Add(Prim.Box, new Vector3(0.000f, 0.390f, 0.640f), new Vector3(2.000f, 0.100f, 0.360f));     // 右履带裙板(挡泥板)
            m.Add(Prim.Box, new Vector3(0.000f, 0.230f, 0.000f), new Vector3(1.900f, 0.320f, 0.780f));    // 车体
            m.Add(Prim.Cylinder, new Vector3(-0.750f, 0.130f, -0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f); // 负重轮
            m.Add(Prim.Cylinder, new Vector3(-0.250f, 0.130f, -0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f);  // 负重轮
            m.Add(Prim.Cylinder, new Vector3(0.250f, 0.130f, -0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f);   // 负重轮
            m.Add(Prim.Cylinder, new Vector3(0.750f, 0.130f, -0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f);   // 负重轮
            m.Add(Prim.Cylinder, new Vector3(-0.750f, 0.130f, 0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f);   // 负重轮
            m.Add(Prim.Cylinder, new Vector3(-0.250f, 0.130f, 0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f);   // 负重轮
            m.Add(Prim.Cylinder, new Vector3(0.250f, 0.130f, 0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f);    // 负重轮
            m.Add(Prim.Cylinder, new Vector3(0.750f, 0.130f, 0.640f), new Vector3(0.300f, 0.260f, 0.300f), 0f, 90f);    // 负重轮
            m.Add(Prim.Cylinder, new Vector3(-0.100f, 0.550f, 0.000f), new Vector3(0.720f, 0.320f, 0.720f)); // 炮塔(桶形)
            m.Add(Prim.Cylinder, new Vector3(-0.100f, 0.870f, 0.000f), new Vector3(0.380f, 0.070f, 0.380f)); // 顶盖
            m.Add(Prim.Box, new Vector3(0.520f, 0.620f, 0.000f), new Vector3(0.900f, 0.140f, 0.140f));    // 炮管
            m.Add(Prim.Box, new Vector3(1.000f, 0.600f, 0.000f), new Vector3(0.160f, 0.180f, 0.180f));    // 破损炮口
            m.RecalcBounds();
            m.bounds = new Vector3(2.500f, 0.940f, 1.500f);
            return m;
        }

        /// <summary>@tex crashedshippart  @footprint 8.00 4.00</summary>
        public static Model3D BuildCrashedShipPart()
        {
            var m = new Model3D();
            m.Add(Prim.Cylinder, new Vector3(-1.200f, 1.200f, 0.000f), new Vector3(1.200f, 2.600f, 1.200f), 90f, 90f); // 机身主筒
            m.Add(Prim.Cone, new Vector3(-1.200f, 1.200f, 0.000f), new Vector3(1.200f, 1.500f, 1.200f), -90f, 90f);     // 船首锥
            m.Add(Prim.Cylinder, new Vector3(1.400f, 1.200f, 0.000f), new Vector3(1.350f, 0.500f, 1.350f), 90f, 90f);   // 尾部机匣环
            m.Add(Prim.Cylinder, new Vector3(-1.100f, 1.780f, 0.000f), new Vector3(0.760f, 0.240f, 0.760f));            // 驾驶舱穹顶(低矮圆穹,非喷气座舱盖)
            m.Add(Prim.Cylinder, new Vector3(-1.100f, 2.020f, 0.000f), new Vector3(0.400f, 0.140f, 0.400f));            // 穹顶顶塔
            m.Add(Prim.Box, new Vector3(0.250f, 1.620f, 0.000f), new Vector3(1.150f, 1.000f, 0.080f));                 // 竖直尾翼(背鳍)
            m.Add(Prim.Box, new Vector3(1.150f, 1.690f, 0.540f), new Vector3(0.900f, 0.075f, 0.800f));                 // 水平尾翼+Z
            m.Add(Prim.Box, new Vector3(1.150f, 1.690f, -0.540f), new Vector3(0.900f, 0.075f, 0.800f));                // 水平尾翼-Z
            m.Add(Prim.Box, new Vector3(1.200f, 0.100f, 0.000f), new Vector3(0.800f, 0.500f, 0.080f));                 // 腹鳍
            m.Add(Prim.Cylinder, new Vector3(-0.600f, 1.200f, 0.580f), new Vector3(0.400f, 0.200f, 0.400f), 0f, 90f);   // 舷窗1
            m.Add(Prim.Cylinder, new Vector3(0.100f, 1.200f, 0.580f), new Vector3(0.400f, 0.200f, 0.400f), 0f, 90f);    // 舷窗2
            m.Add(Prim.Cylinder, new Vector3(0.800f, 1.200f, 0.580f), new Vector3(0.400f, 0.200f, 0.400f), 0f, 90f);    // 舷窗3
            m.Add(Prim.Cylinder, new Vector3(-0.600f, 1.200f, -0.580f), new Vector3(0.400f, 0.200f, 0.400f), 180f, 90f); // 舷窗4
            m.Add(Prim.Cylinder, new Vector3(0.100f, 1.200f, -0.580f), new Vector3(0.400f, 0.200f, 0.400f), 180f, 90f);  // 舷窗5
            m.Add(Prim.Cylinder, new Vector3(0.800f, 1.200f, -0.580f), new Vector3(0.400f, 0.200f, 0.400f), 180f, 90f);  // 舷窗6
            m.Add(Prim.Cone, new Vector3(1.900f, 1.600f, -0.400f), new Vector3(0.500f, 0.600f, 0.500f), 90f, 90f);       // 喷口1
            m.Add(Prim.Cone, new Vector3(1.900f, 1.600f, 0.400f), new Vector3(0.500f, 0.600f, 0.500f), 90f, 90f);        // 喷口2
            m.Add(Prim.Cone, new Vector3(1.900f, 0.800f, -0.400f), new Vector3(0.500f, 0.600f, 0.500f), 90f, 90f);       // 喷口3
            m.Add(Prim.Cone, new Vector3(1.900f, 0.800f, 0.400f), new Vector3(0.500f, 0.600f, 0.500f), 90f, 90f);        // 喷口4
            m.Add(Prim.Box, new Vector3(0.700f, 0.000f, 0.700f), new Vector3(0.150f, 0.600f, 0.150f));  // 着陆支柱
            m.Add(Prim.Box, new Vector3(0.700f, 0.000f, -0.700f), new Vector3(0.150f, 0.600f, 0.150f)); // 着陆支柱
            m.Add(Prim.Box, new Vector3(-0.500f, 0.000f, 0.700f), new Vector3(0.150f, 0.600f, 0.150f)); // 着陆支柱
            m.Add(Prim.Box, new Vector3(-0.500f, 0.000f, -0.700f), new Vector3(0.150f, 0.600f, 0.150f)); // 着陆支柱
            m.Add(Prim.Box, new Vector3(0.700f, 0.000f, 0.700f), new Vector3(0.360f, 0.080f, 0.360f));  // 脚垫
            m.Add(Prim.Box, new Vector3(0.700f, 0.000f, -0.700f), new Vector3(0.360f, 0.080f, 0.360f)); // 脚垫
            m.Add(Prim.Box, new Vector3(-0.500f, 0.000f, 0.700f), new Vector3(0.360f, 0.080f, 0.360f)); // 脚垫
            m.Add(Prim.Box, new Vector3(-0.500f, 0.000f, -0.700f), new Vector3(0.360f, 0.080f, 0.360f)); // 脚垫
            m.Add(Prim.Box, new Vector3(0.300f, 0.000f, 0.950f), new Vector3(1.400f, 0.300f, 0.600f));  // 断裂破损板
            m.Add(Prim.Box, new Vector3(1.200f, 0.000f, -0.950f), new Vector3(1.100f, 0.280f, 0.600f)); // 碎片
            m.RecalcBounds();
            m.bounds = new Vector3(6.000f, 2.400f, 4.000f);
            return m;
        }

        /// <summary>@tex ancientwarwalkershell  @footprint 2.50 1.50</summary>
        public static Model3D BuildAncientWarwalkerShell()
        {
            var m = new Model3D();
            m.Add(Prim.Box, new Vector3(0.000f, 0.800f, 0.000f), new Vector3(1.100f, 0.700f, 0.800f));       // 躯干
            m.Add(Prim.Box, new Vector3(0.000f, 1.480f, -0.050f), new Vector3(1.250f, 0.200f, 0.860f), 0f, -6f); // 肩甲
            m.Add(Prim.Box, new Vector3(0.300f, 1.660f, -0.150f), new Vector3(0.520f, 0.400f, 0.560f));       // 驾驶舱/头
            m.Add(Prim.Box, new Vector3(-0.330f, 0.420f, -0.080f), new Vector3(0.300f, 0.440f, 0.300f), 0f, -14f); // 左大腿
            m.Add(Prim.Cylinder, new Vector3(-0.430f, 0.160f, -0.060f), new Vector3(0.340f, 0.360f, 0.340f), 0f, 90f); // 左膝
            m.Add(Prim.Box, new Vector3(-0.500f, 0.000f, 0.030f), new Vector3(0.280f, 0.440f, 0.280f), 0f, 8f);  // 左小腿
            m.Add(Prim.Box, new Vector3(-0.500f, 0.000f, 0.120f), new Vector3(0.420f, 0.160f, 0.620f));         // 左脚
            m.Add(Prim.Box, new Vector3(0.360f, 0.440f, -0.080f), new Vector3(0.300f, 0.440f, 0.300f), 0f, 12f); // 右大腿
            m.Add(Prim.Cylinder, new Vector3(0.470f, 0.180f, -0.070f), new Vector3(0.340f, 0.360f, 0.340f), 0f, 90f); // 右膝
            m.Add(Prim.Box, new Vector3(0.560f, 0.000f, -0.180f), new Vector3(0.280f, 0.460f, 0.280f), 0f, -22f); // 右小腿(折)
            m.Add(Prim.Box, new Vector3(0.740f, 0.000f, -0.430f), new Vector3(0.420f, 0.160f, 0.560f), 0f, -40f); // 右脚
            m.Add(Prim.Box, new Vector3(-0.120f, 1.320f, 0.250f), new Vector3(1.300f, 0.090f, 0.090f), 18f, 10f); // 外露液压梁
            m.Add(Prim.Box, new Vector3(-0.760f, 1.050f, 0.220f), new Vector3(0.260f, 0.900f, 0.220f));        // 断臂
            m.RecalcBounds();
            m.bounds = new Vector3(2.500f, 2.100f, 1.500f);
            return m;
        }

        /// <summary>@tex largeengineblock  @footprint 1.00 0.50</summary>
        public static Model3D BuildLargeEngineBlock()
        {
            var m = new Model3D();
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, 0.000f), new Vector3(0.820f, 0.120f, 0.420f));   // 油底壳
            m.Add(Prim.Box, new Vector3(0.000f, 0.120f, 0.000f), new Vector3(0.900f, 0.340f, 0.440f));   // 缸体
            m.Add(Prim.Cylinder, new Vector3(-0.300f, 0.460f, 0.000f), new Vector3(0.220f, 0.260f, 0.220f)); // 气缸盖1
            m.Add(Prim.Cylinder, new Vector3(-0.100f, 0.460f, 0.000f), new Vector3(0.220f, 0.260f, 0.220f)); // 气缸盖2
            m.Add(Prim.Cylinder, new Vector3(0.100f, 0.460f, 0.000f), new Vector3(0.220f, 0.260f, 0.220f));  // 气缸盖3
            m.Add(Prim.Cylinder, new Vector3(0.300f, 0.460f, 0.000f), new Vector3(0.220f, 0.260f, 0.220f));  // 气缸盖4
            m.Add(Prim.Cylinder, new Vector3(-0.500f, 0.240f, 0.000f), new Vector3(0.460f, 0.100f, 0.460f), 0f, 90f); // 大飞轮盘
            m.Add(Prim.Cylinder, new Vector3(-0.500f, 0.240f, 0.000f), new Vector3(0.180f, 0.180f, 0.180f), 0f, 90f); // 飞轮轴毂
            m.Add(Prim.Cylinder, new Vector3(0.470f, 0.300f, 0.000f), new Vector3(0.300f, 0.100f, 0.300f), 0f, 90f);  // 前端皮带轮
            m.Add(Prim.Box, new Vector3(0.100f, 0.340f, 0.260f), new Vector3(0.700f, 0.090f, 0.090f));   // 排气歧管
            m.Add(Prim.Box, new Vector3(0.430f, 0.000f, 0.260f), new Vector3(0.090f, 0.430f, 0.090f));   // 排气管
            m.RecalcBounds();
            m.bounds = new Vector3(1.300f, 0.720f, 0.500f);
            return m;
        }

        /// <summary>@tex giantwheela  @footprint 1.00 1.00</summary>
        public static Model3D BuildGiantWheelA()
        {
            var m = new Model3D();
            // 平放巨型车轮:轴沿 Y(正面朝上,与 vanilla 贴图朝向一致) —— 便于轮辐/胎面块绕轴用 yaw 布置且正面可见
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.000f, 0.000f), new Vector3(0.980f, 0.180f, 0.980f));  // 外胎(厚环体)
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.180f, 0.000f), new Vector3(0.860f, 0.060f, 0.860f));  // 胎面顶环(内收台阶)
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.000f, 0.000f), new Vector3(0.220f, 0.340f, 0.220f));  // 中心轮毂(凸出)
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.340f, 0.000f), new Vector3(0.100f, 0.080f, 0.100f));  // 轴头
            m.Add(Prim.Box, new Vector3(0.000f, 0.190f, 0.000f), new Vector3(0.070f, 0.050f, 0.920f), 0f);   // 轮辐0
            m.Add(Prim.Box, new Vector3(0.000f, 0.190f, 0.000f), new Vector3(0.070f, 0.050f, 0.920f), 90f);  // 轮辐90
            m.Add(Prim.Box, new Vector3(0.000f, 0.190f, 0.000f), new Vector3(0.070f, 0.050f, 0.920f), 45f);  // 轮辐45
            m.Add(Prim.Box, new Vector3(0.000f, 0.190f, 0.000f), new Vector3(0.070f, 0.050f, 0.920f), 135f); // 轮辐135
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, 0.520f), new Vector3(0.180f, 0.240f, 0.180f), 0f);   // 胎面凸块0
            m.Add(Prim.Box, new Vector3(0.520f, 0.000f, 0.000f), new Vector3(0.180f, 0.240f, 0.180f), 90f);  // 胎面凸块90
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, -0.520f), new Vector3(0.180f, 0.240f, 0.180f), 0f);  // 胎面凸块180
            m.Add(Prim.Box, new Vector3(-0.520f, 0.000f, 0.000f), new Vector3(0.180f, 0.240f, 0.180f), 90f); // 胎面凸块270
            m.Add(Prim.Box, new Vector3(0.370f, 0.000f, 0.370f), new Vector3(0.180f, 0.240f, 0.180f), 45f);  // 胎面凸块45
            m.Add(Prim.Box, new Vector3(-0.370f, 0.000f, 0.370f), new Vector3(0.180f, 0.240f, 0.180f), 135f); // 胎面凸块135
            m.Add(Prim.Box, new Vector3(0.370f, 0.000f, -0.370f), new Vector3(0.180f, 0.240f, 0.180f), 45f); // 胎面凸块225
            m.Add(Prim.Box, new Vector3(-0.370f, 0.000f, -0.370f), new Vector3(0.180f, 0.240f, 0.180f), 135f); // 胎面凸块315
            m.RecalcBounds();
            m.bounds = new Vector3(1.000f, 1.000f, 1.000f);
            return m;
        }

        /// <summary>@tex concretebarrier  @footprint 0.50 0.50</summary>
        public static Model3D BuildConcreteBarrier()
        {
            var m = new Model3D();
            m.Add(Prim.Box, new Vector3(-0.400f, 0.000f, 0.000f), new Vector3(0.160f, 0.060f, 0.440f)); // 左脚(横向脚座)
            m.Add(Prim.Box, new Vector3(0.400f, 0.000f, 0.000f), new Vector3(0.160f, 0.060f, 0.440f));  // 右脚
            m.Add(Prim.Box, new Vector3(-0.400f, 0.000f, 0.000f), new Vector3(0.120f, 0.740f, 0.120f)); // 左立柱
            m.Add(Prim.Box, new Vector3(0.400f, 0.000f, 0.000f), new Vector3(0.120f, 0.740f, 0.120f));  // 右立柱
            m.Add(Prim.Box, new Vector3(0.000f, 0.130f, 0.000f), new Vector3(0.940f, 0.080f, 0.100f));  // 下部横杆
            m.Add(Prim.Box, new Vector3(0.000f, 0.300f, 0.000f), new Vector3(0.940f, 0.360f, 0.100f));  // 横挡板(中)
            m.Add(Prim.Box, new Vector3(0.000f, 0.680f, 0.000f), new Vector3(1.000f, 0.100f, 0.130f));  // 顶部横栏
            m.RecalcBounds();
            m.bounds = new Vector3(1.000f, 0.780f, 0.440f);
            return m;
        }

        /// <summary>@tex rustedindustrialtruck  @footprint 4.00 4.00</summary>
        public static Model3D BuildRustedIndustrialTruck()
        {
            var m = new Model3D();
            m.Add(Prim.Box, new Vector3(0.000f, 0.280f, 0.000f), new Vector3(3.700f, 0.200f, 1.700f));   // 底盘大梁
            m.Add(Prim.Box, new Vector3(1.550f, 0.380f, 0.000f), new Vector3(0.900f, 0.450f, 1.500f));  // 发动机罩
            m.Add(Prim.Box, new Vector3(0.600f, 0.480f, 0.000f), new Vector3(0.900f, 0.850f, 1.600f));  // 驾驶室
            m.Add(Prim.Box, new Vector3(0.600f, 1.330f, 0.000f), new Vector3(0.880f, 0.100f, 1.500f));  // 驾驶室顶
            m.Add(Prim.Box, new Vector3(-0.800f, 0.480f, 0.000f), new Vector3(2.000f, 0.600f, 1.600f)); // 货厢
            m.Add(Prim.Box, new Vector3(-0.800f, 1.080f, 0.820f), new Vector3(2.000f, 0.500f, 0.080f)); // 货厢侧板
            m.Add(Prim.Box, new Vector3(-0.800f, 1.080f, -0.820f), new Vector3(2.000f, 0.500f, 0.080f)); // 货厢侧板
            m.Add(Prim.Cylinder, new Vector3(1.400f, 0.300f, -0.850f), new Vector3(0.600f, 0.300f, 0.600f), 0f, 90f); // 前轮
            m.Add(Prim.Cylinder, new Vector3(1.400f, 0.300f, 0.850f), new Vector3(0.600f, 0.300f, 0.600f), 0f, 90f);  // 前轮
            m.Add(Prim.Cylinder, new Vector3(-0.400f, 0.320f, -0.850f), new Vector3(0.640f, 0.340f, 0.640f), 0f, 90f); // 后轮
            m.Add(Prim.Cylinder, new Vector3(-0.400f, 0.320f, 0.850f), new Vector3(0.640f, 0.340f, 0.640f), 0f, 90f);  // 后轮
            m.Add(Prim.Cylinder, new Vector3(-1.200f, 0.320f, -0.850f), new Vector3(0.640f, 0.340f, 0.640f), 0f, 90f); // 后轮2
            m.Add(Prim.Cylinder, new Vector3(-1.200f, 0.320f, 0.850f), new Vector3(0.640f, 0.340f, 0.640f), 0f, 90f);  // 后轮2
            m.Add(Prim.Box, new Vector3(0.300f, 0.480f, -0.850f), new Vector3(0.100f, 0.900f, 0.100f)); // 排气管
            m.RecalcBounds();
            m.bounds = new Vector3(4.000f, 1.480f, 4.000f);
            return m;
        }

        // ---- 统一入口 ----

        /// <summary>全部样板的构造器，按样例名索引。</summary>
        public static readonly Dictionary<string, System.Func<Model3D>> Builders =
            new Dictionary<string, System.Func<Model3D>>(7)
        {
            { "ruinedtank", BuildRuinedTank },
            { "crashedshippart", BuildCrashedShipPart },
            { "ancientwarwalkershell", BuildAncientWarwalkerShell },
            { "largeengineblock", BuildLargeEngineBlock },
            { "giantwheela", BuildGiantWheelA },
            { "concretebarrier", BuildConcreteBarrier },
            { "rustedindustrialtruck", BuildRustedIndustrialTruck },
        };

        /// <summary>按样例名构造；未知名称返回 null。</summary>
        public static Model3D Build(string name)
        {
            return Builders.TryGetValue(name, out var f) ? f() : null;
        }

        /// <summary>按样例名取贴图取样规格（顶面=该残骸清理贴图，侧面取中段横带）。</summary>
        public static UvSpec Uv(string name)
        {
            // 卡车等带朝向的多视图，裸名缺键时回退南向视图区域。
            var key = name;
            if (!WreckageUVGen.R.ContainsKey(key) && WreckageUVGen.R.ContainsKey(name + ".south"))
                key = name + ".south";
            var sz = WreckageUVGen.R.TryGetValue(key, out var r) ? r : WreckageUVGen.R["ruinedtank"];
            var box = Build(name)?.bounds ?? Vector3.one;
            var s = new UvSpec
            {
                top = sz,
                side = new Vector4(sz.x, Mathf.Lerp(sz.y, sz.w, 0.45f),
                                    sz.z, Mathf.Lerp(sz.y, sz.w, 0.62f)),
                sideTileH = 0f,
                sideTileW = 0f,
                modelXZ = new Vector2(box.x, box.z)
            };
            return s;
        }
    }
}