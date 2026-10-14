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
            // 战车残骸：车身左倾沉陷泥中，左侧履带被炸断散落，炮塔卡死偏斜，炮管严重低垂耷拉
            m.Add(Prim.Box, new Vector3(0.000f, 0.120f, 0.000f), new Vector3(1.900f, 0.280f, 0.780f), 0f, 2f, -8f);   // 倾斜车体
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, 0.540f), new Vector3(1.950f, 0.360f, 0.260f), 0f, 2f, -8f);    // 右履带(着地)
            m.Add(Prim.Box, new Vector3(0.000f, 0.320f, 0.600f), new Vector3(1.950f, 0.080f, 0.340f), 0f, 2f, -8f);    // 右裙板
            // 左履带炸毁断裂：断裂前段微翘，后段瘫卧在泥地
            m.Add(Prim.Box, new Vector3(0.400f, 0.020f, -0.560f), new Vector3(1.100f, 0.220f, 0.260f), -6f, 5f, 4f);  // 断裂前段履带
            m.Add(Prim.Box, new Vector3(-0.600f, 0.000f, -0.560f), new Vector3(0.900f, 0.160f, 0.280f), 8f, -2f, -6f); // 塌陷后段履带
            m.Add(Prim.Box, new Vector3(0.100f, 0.240f, -0.620f), new Vector3(1.400f, 0.060f, 0.300f), -4f, 8f, 12f); // 撕裂翘起裙板
            // 负重轮
            m.Add(Prim.Cylinder, new Vector3(-0.650f, 0.100f, 0.600f), new Vector3(0.280f, 0.240f, 0.280f), 0f, 90f);
            m.Add(Prim.Cylinder, new Vector3(0.100f, 0.100f, 0.600f), new Vector3(0.280f, 0.240f, 0.280f), 0f, 90f);
            m.Add(Prim.Cylinder, new Vector3(0.650f, 0.100f, 0.600f), new Vector3(0.280f, 0.240f, 0.280f), 0f, 90f);
            m.Add(Prim.Cylinder, new Vector3(-0.450f, 0.080f, -0.580f), new Vector3(0.280f, 0.220f, 0.280f), 10f, 85f, -10f); // 脱位外露负重轮
            m.Add(Prim.Cylinder, new Vector3(0.350f, 0.080f, -0.580f), new Vector3(0.280f, 0.220f, 0.280f), -8f, 95f, 15f);  // 脱位负重轮2
            // 歪斜卡死炮塔与耷拉下垂炮管
            m.Add(Prim.Cylinder, new Vector3(-0.060f, 0.400f, 0.000f), new Vector3(0.700f, 0.260f, 0.700f), 18f, 6f, -6f); // 卡死歪斜炮塔
            m.Add(Prim.Cylinder, new Vector3(-0.060f, 0.640f, 0.000f), new Vector3(0.360f, 0.060f, 0.360f), 18f, 6f, -6f); // 破损顶盖
            m.Add(Prim.Box, new Vector3(0.480f, 0.380f, 0.140f), new Vector3(0.880f, 0.130f, 0.130f), 18f, -16f, 0f);    // 耷拉下垂炮管
            m.Add(Prim.Box, new Vector3(0.920f, 0.250f, 0.280f), new Vector3(0.150f, 0.160f, 0.160f), 18f, -18f, 0f);    // 破损炸裂炮口
            m.RecalcBounds();
            m.bounds = new Vector3(2.500f, 0.750f, 1.500f);
            return m;
        }

        /// <summary>@tex crashedshippart  @footprint 8.00 4.00</summary>
        public static Model3D BuildCrashedShipPart()
        {
            var m = new Model3D();
            // 坠毁飞船残骸：无任何立柱与支架！巨大圆柱舱体直接砸入地面，带倾侧与坠毁角度，两端撕裂暴露管道，蒙皮侧向剥落贴地
            m.Add(Prim.Cylinder, new Vector3(-0.200f, 0.280f, 0.000f), new Vector3(1.300f, 3.600f, 1.300f), 90f, 85f, -10f); // 横卧砸地主舱段
            m.Add(Prim.Cone, new Vector3(-1.950f, 0.120f, -0.150f), new Vector3(1.250f, 1.100f, 1.250f), -90f, 75f, 0f);    // 栽入泥土的破损首锥
            m.Add(Prim.Cylinder, new Vector3(1.550f, 0.450f, 0.120f), new Vector3(1.360f, 0.400f, 1.360f), 90f, 85f, -10f);  // 尾部断裂机匣
            // 剥落耷拉向地面的巨大外壳装甲板 (Wedge与Box)
            m.Add(Prim.Box, new Vector3(0.100f, 0.020f, 0.780f), new Vector3(2.600f, 0.120f, 0.700f), 8f, 12f, -12f);    // 撕脱贴地装甲板
            m.Add(Prim.Wedge, new Vector3(-0.800f, 0.020f, 0.850f), new Vector3(1.200f, 0.350f, 0.600f), 14f, 6f, 0f);   // 卷曲外翘蒙皮
            m.Add(Prim.Box, new Vector3(0.500f, 0.000f, -0.800f), new Vector3(2.200f, 0.100f, 0.650f), -6f, -10f, 8f);   // 另一侧剥落侧板
            // 舱体凹陷穹顶与侧面舷窗残痕
            m.Add(Prim.Cylinder, new Vector3(-0.600f, 0.850f, 0.100f), new Vector3(0.700f, 0.180f, 0.700f), 15f, 8f, -10f); // 凹陷观察穹顶
            m.Add(Prim.Cylinder, new Vector3(-0.300f, 0.450f, 0.650f), new Vector3(0.360f, 0.160f, 0.360f), 0f, 80f, -10f); // 舷窗1
            m.Add(Prim.Cylinder, new Vector3(0.350f, 0.520f, 0.650f), new Vector3(0.360f, 0.160f, 0.360f), 0f, 80f, -10f);  // 舷窗2
            m.Add(Prim.Cylinder, new Vector3(1.000f, 0.600f, 0.650f), new Vector3(0.360f, 0.160f, 0.360f), 0f, 80f, -10f);  // 舷窗3
            // 尾部断裂喷口管（歪斜半埋）
            m.Add(Prim.Cone, new Vector3(1.900f, 0.650f, -0.250f), new Vector3(0.480f, 0.550f, 0.480f), 85f, 75f, -15f);  // 扭曲喷口1
            m.Add(Prim.Cone, new Vector3(1.950f, 0.350f, 0.300f), new Vector3(0.460f, 0.500f, 0.460f), 95f, 95f, -5f);   // 扭曲喷口2
            // 散落贴地碎片
            m.Add(Prim.Box, new Vector3(-1.400f, 0.000f, 0.800f), new Vector3(0.800f, 0.140f, 0.500f), -20f, 0f, 10f);   // 坠地碎片1
            m.Add(Prim.Box, new Vector3(1.600f, 0.000f, -0.750f), new Vector3(0.900f, 0.120f, 0.550f), 25f, 0f, -8f);    // 坠地碎片2
            m.RecalcBounds();
            m.bounds = new Vector3(5.600f, 1.250f, 2.500f);
            return m;
        }

        /// <summary>@tex ancientwarwalkershell  @footprint 2.50 1.50</summary>
        public static Model3D BuildAncientWarwalkerShell()
        {
            var m = new Model3D();
            // 远古双足机甲外壳：完全倒塌坍塌的机甲残躯外壳，绝非站立！主胸舱倾倒贴地，内部断梁外露，断裂机械腿横卧废墟边缘
            // 1. 倒塌倾覆的主胸甲舱段 (长1.35m, 宽0.85m, 高0.38m, 侧倒倾角 yaw=-12f, pitch=6f, roll=15f)
            m.Add(Prim.Box, new Vector3(0.050f, 0.060f, 0.000f), new Vector3(1.300f, 0.400f, 0.820f), -12f, 6f, 15f);
            // 2. 垮塌凹陷的副装甲外壳板 (斜压在主舱一侧)
            m.Add(Prim.Box, new Vector3(-0.480f, 0.020f, -0.120f), new Vector3(0.850f, 0.280f, 0.720f), 10f, 10f, -12f);
            // 3. 歪倒在泥地中的驾驶舱/头部罩 (向下向外偏斜)
            m.Add(Prim.Box, new Vector3(0.620f, 0.020f, -0.180f), new Vector3(0.520f, 0.320f, 0.540f), -24f, 8f, 18f);
            // 4. 外露的折断内部骨架梁 (两根斜横穿废墟空隙的生锈工字梁)
            m.Add(Prim.Box, new Vector3(0.000f, 0.260f, 0.080f), new Vector3(1.250f, 0.080f, 0.080f), 16f, -8f, 14f);
            m.Add(Prim.Box, new Vector3(0.200f, 0.220f, -0.200f), new Vector3(0.800f, 0.070f, 0.070f), -35f, 14f, 5f);
            // 5. 撕裂脱开的机械腿残段 (完全横卧在地面，断口朝外，绝不竖立)
            m.Add(Prim.Cylinder, new Vector3(-0.800f, 0.040f, 0.220f), new Vector3(0.360f, 0.280f, 0.360f), 15f, 85f, 20f); // 翻倒的膝关节
            m.Add(Prim.Box, new Vector3(-0.950f, 0.000f, -0.100f), new Vector3(0.650f, 0.160f, 0.280f), -20f, 4f, -10f);     // 横躺小腿板
            m.Add(Prim.Box, new Vector3(-1.050f, 0.000f, -0.280f), new Vector3(0.420f, 0.120f, 0.500f), -40f, 0f, 8f);       // 脱落脚掌板
            // 6. 另一侧破裂残片
            m.Add(Prim.Box, new Vector3(0.750f, 0.000f, 0.280f), new Vector3(0.550f, 0.140f, 0.400f), 25f, -6f, 12f);
            m.RecalcBounds();
            m.bounds = new Vector3(2.450f, 0.580f, 1.450f);
            return m;
        }

        /// <summary>@tex largeengineblock  @footprint 1.00 0.50</summary>
        public static Model3D BuildLargeEngineBlock()
        {
            var m = new Model3D();
            // 巨型发动机缸体残骸：整体严重倾侧半陷于泥中，顶端气缸高低破损不齐，断裂排气管耷拉触地，歪斜卡死飞轮
            m.Add(Prim.Box, new Vector3(0.000f, 0.040f, 0.000f), new Vector3(0.850f, 0.260f, 0.420f), 0f, 5f, 15f);    // 倾侧半埋缸体
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, -0.060f), new Vector3(0.780f, 0.100f, 0.380f), 0f, 5f, 15f);   // 陷入泥中的油底壳
            // 错落破损的气缸盖（有的破损凹陷，有的微露）
            m.Add(Prim.Cylinder, new Vector3(-0.280f, 0.280f, -0.040f), new Vector3(0.200f, 0.160f, 0.200f), 0f, 5f, 15f); // 气缸1
            m.Add(Prim.Cylinder, new Vector3(-0.090f, 0.240f, -0.030f), new Vector3(0.190f, 0.100f, 0.190f), 0f, 5f, 15f); // 破损气缸2
            m.Add(Prim.Cylinder, new Vector3(0.090f, 0.270f, -0.020f), new Vector3(0.200f, 0.150f, 0.200f), 0f, 5f, 15f);  // 气缸3
            m.Add(Prim.Cylinder, new Vector3(0.280f, 0.230f, -0.010f), new Vector3(0.190f, 0.080f, 0.190f), 0f, 5f, 15f);  // 破损气缸4
            // 侧面飞轮卡死在地面断层
            m.Add(Prim.Cylinder, new Vector3(-0.460f, 0.120f, 0.000f), new Vector3(0.420f, 0.080f, 0.420f), 8f, 85f, 25f); // 倾斜卡死大飞轮
            m.Add(Prim.Cylinder, new Vector3(0.440f, 0.160f, 0.000f), new Vector3(0.260f, 0.080f, 0.260f), -10f, 85f, 5f);  // 前端破损皮带轮
            // 断裂下垂耷拉在泥土中的排气管
            m.Add(Prim.Box, new Vector3(0.100f, 0.180f, 0.240f), new Vector3(0.650f, 0.080f, 0.080f), 0f, 5f, 15f);
            m.Add(Prim.Box, new Vector3(0.380f, 0.000f, 0.240f), new Vector3(0.080f, 0.280f, 0.080f), 0f, 0f, -18f); // 垂地断管
            m.RecalcBounds();
            m.bounds = new Vector3(1.200f, 0.460f, 0.550f);
            return m;
        }

        /// <summary>@tex giantwheela  @footprint 1.00 1.00</summary>
        public static Model3D BuildGiantWheelA()
        {
            var m = new Model3D();
            // 平放巨型车轮残骸：平卧倒在泥中，整体微倾(pitch=4f, roll=-3f)，厚胎环与轮辐紧贴地面，齿块内收在轮廓内无采样黑斑
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.000f, 0.000f), new Vector3(0.980f, 0.160f, 0.980f), 0f, 4f, -3f);  // 外胎厚环体
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.150f, 0.000f), new Vector3(0.860f, 0.050f, 0.860f), 0f, 4f, -3f);  // 胎面顶环
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.000f, 0.000f), new Vector3(0.240f, 0.280f, 0.240f), 0f, 4f, -3f);  // 中心轮毂
            m.Add(Prim.Cylinder, new Vector3(0.000f, 0.280f, 0.000f), new Vector3(0.120f, 0.060f, 0.120f), 0f, 4f, -3f);  // 轴头
            // 4条轮辐
            m.Add(Prim.Box, new Vector3(0.000f, 0.160f, 0.000f), new Vector3(0.070f, 0.040f, 0.880f), 0f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(0.000f, 0.160f, 0.000f), new Vector3(0.070f, 0.040f, 0.880f), 90f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(0.000f, 0.160f, 0.000f), new Vector3(0.070f, 0.040f, 0.880f), 45f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(0.000f, 0.160f, 0.000f), new Vector3(0.070f, 0.040f, 0.880f), 135f, 4f, -3f);
            // 8处胎面咬合块（收拢在 0.44m 半径以内）
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, 0.440f), new Vector3(0.140f, 0.200f, 0.140f), 0f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(0.440f, 0.000f, 0.000f), new Vector3(0.140f, 0.200f, 0.140f), 90f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(0.000f, 0.000f, -0.440f), new Vector3(0.140f, 0.200f, 0.140f), 0f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(-0.440f, 0.000f, 0.000f), new Vector3(0.140f, 0.200f, 0.140f), 90f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(0.311f, 0.000f, 0.311f), new Vector3(0.140f, 0.200f, 0.140f), 45f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(-0.311f, 0.000f, 0.311f), new Vector3(0.140f, 0.200f, 0.140f), 135f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(0.311f, 0.000f, -0.311f), new Vector3(0.140f, 0.200f, 0.140f), 45f, 4f, -3f);
            m.Add(Prim.Box, new Vector3(-0.311f, 0.000f, -0.311f), new Vector3(0.140f, 0.200f, 0.140f), 135f, 4f, -3f);
            m.RecalcBounds();
            m.bounds = new Vector3(1.020f, 0.360f, 1.020f);
            return m;
        }

        /// <summary>@tex concretebarrier  @footprint 0.50 0.50</summary>
        public static Model3D BuildConcreteBarrier()
        {
            var m = new Model3D();
            // 混凝土路障残骸：彻底删除两脚崭新门框！还原原版梯形水泥防撞墩被暴力撞断、倾倒倒伏在地的真实残骸姿态
            // 1. 梯形混凝土主墩段 (长0.62m, 宽0.38m, 高0.38m, 向一侧严重倾倒倒伏 roll=28f, pitch=8f, yaw=-10f)
            m.Add(Prim.Box, new Vector3(-0.100f, 0.040f, 0.000f), new Vector3(0.600f, 0.360f, 0.360f), -10f, 8f, 28f);
            // 顶端窄斜台阶 (八字梯形收顶)
            m.Add(Prim.Box, new Vector3(-0.160f, 0.160f, 0.000f), new Vector3(0.580f, 0.140f, 0.220f), -10f, 8f, 28f);
            // 2. 撞断脱开的副水泥墩块 (断口朝向主块，歪倒在旁边)
            m.Add(Prim.Box, new Vector3(0.340f, 0.000f, -0.080f), new Vector3(0.320f, 0.240f, 0.320f), 15f, -6f, -16f);
            m.Add(Prim.Box, new Vector3(0.320f, 0.120f, -0.080f), new Vector3(0.300f, 0.100f, 0.180f), 15f, -6f, -16f);
            // 3. 散落贴地的碎裂水泥角块
            m.Add(Prim.Box, new Vector3(0.120f, 0.000f, 0.180f), new Vector3(0.240f, 0.080f, 0.180f), 30f, 0f, 10f);
            m.Add(Prim.Box, new Vector3(-0.350f, 0.000f, -0.150f), new Vector3(0.200f, 0.060f, 0.160f), -15f, 0f, -8f);
            m.RecalcBounds();
            m.bounds = new Vector3(1.050f, 0.440f, 0.650f);
            return m;
        }

        /// <summary>@tex rustedindustrialtruck  @footprint 4.00 4.00</summary>
        public static Model3D BuildRustedIndustrialTruck()
        {
            var m = new Model3D();
            // 锈蚀工业卡车残骸：绝非停车位平稳完好卡车！车头向前下栽(pitch=6f)，整车左倾下陷(roll=-10f，左侧轮胎爆毁蹭地)，驾驶室压瘪，货厢外倾塌陷
            // 1. 倾斜蹭地的大梁底盘
            m.Add(Prim.Box, new Vector3(0.000f, 0.140f, 0.000f), new Vector3(3.600f, 0.180f, 1.650f), 0f, 6f, -10f);
            // 2. 瘪塌向下的发动机机罩
            m.Add(Prim.Box, new Vector3(1.450f, 0.220f, 0.000f), new Vector3(0.900f, 0.380f, 1.450f), 0f, 12f, -10f);
            // 3. 压瘪歪斜的驾驶室
            m.Add(Prim.Box, new Vector3(0.550f, 0.300f, 0.000f), new Vector3(0.900f, 0.720f, 1.550f), -3f, 5f, -12f);
            m.Add(Prim.Box, new Vector3(0.550f, 1.000f, 0.000f), new Vector3(0.880f, 0.080f, 1.450f), -3f, 5f, -12f);
            // 4. 塌陷外倾的货厢
            m.Add(Prim.Box, new Vector3(-0.800f, 0.300f, 0.000f), new Vector3(2.000f, 0.520f, 1.550f), 0f, 5f, -10f);
            m.Add(Prim.Box, new Vector3(-0.800f, 0.820f, 0.780f), new Vector3(1.950f, 0.400f, 0.080f), 0f, 5f, -6f);  // 右侧板
            m.Add(Prim.Box, new Vector3(-0.800f, 0.720f, -0.850f), new Vector3(1.950f, 0.400f, 0.080f), 0f, 5f, -22f); // 左侧板(向外塌倒)
            // 5. 残缺车轮：左侧轮胎爆毁脱落，右侧车轮歪斜
            m.Add(Prim.Cylinder, new Vector3(1.350f, 0.200f, 0.850f), new Vector3(0.580f, 0.280f, 0.580f), 0f, 95f, -10f); // 右前轮
            m.Add(Prim.Cylinder, new Vector3(-0.400f, 0.220f, 0.850f), new Vector3(0.600f, 0.300f, 0.600f), 0f, 95f, -10f); // 右后轮1
            m.Add(Prim.Cylinder, new Vector3(-1.180f, 0.220f, 0.850f), new Vector3(0.600f, 0.300f, 0.600f), 0f, 95f, -10f); // 右后轮2
            // 左侧：轮子脱落歪倒在底盘旁边
            m.Add(Prim.Cylinder, new Vector3(1.100f, 0.000f, -0.920f), new Vector3(0.560f, 0.220f, 0.560f), 15f, 25f, 75f); // 倒在地上的左轮
            m.Add(Prim.Cylinder, new Vector3(-0.700f, 0.020f, -0.920f), new Vector3(0.580f, 0.260f, 0.580f), -20f, 30f, 65f); // 倒在地上的左后轮
            // 扭曲下垂的排气管
            m.Add(Prim.Box, new Vector3(0.280f, 0.320f, -0.850f), new Vector3(0.090f, 0.750f, 0.090f), 12f, -8f, -24f);
            m.RecalcBounds();
            m.bounds = new Vector3(3.900f, 1.150f, 2.300f);
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