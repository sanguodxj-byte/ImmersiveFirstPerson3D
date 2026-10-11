# 植被与树木 3D 化规范（v1，2026-10-11）

适用范围：FirstPerson3D 植被部分。上游依据 = 原版植物贴图（侧视画意风，无硬黑描边）。

## 0. 口径

- 1 格 = 1 米；树总高 3.0m、灵树 3.5m（沿用 PlantLayer3D 世界高度约定），小株按现实尺寸。
- 冠幅按原型贴图剪影比例定，允许出占格（与原版立牌出格一致）。
- 材质 = 无光照 Cutout 平涂（同家具/残骸），审核页用 MeshBasicMaterial 对齐实机观感。

## 1. 管线（单一真源）

```
scripts/plant_build.py     SPEC（逐原型取样系数 + 几何配方 + 世界尺寸）+ 图集打包 + OBJ 导出
   ↓                        → 1.6/Textures/FPS3D/PlantAtlas.png（PAD=2 防串色；v 翻到 Unity 左下原点）
   ↓                        → 1.6/Assets/plants/<原型>.obj（Y 轴向上、米、底面中心原点、右手系 CCW ——
   ↓                          ObjImporter3D 反转绕序到 Unity 左手系；同步副本到 viewer/plants/）
   ↓                        → viewer/plant_uv.json（UV 表 + 原型表，审核页与运行时同源）
   ↓                        → docs/plants/sampling.png（取样区画框，人判语义）
viewer/plants.html         审核页：加载同一批 OBJ + 同一张图集（零第二份口径）；
                           绕序反转与 ObjImporter3D 同款，FrontSide = 游戏实况
scripts/plant_shots.py     无头 Chrome 批量出图 → docs/plants/audit/<原型>.png（4 机位拼版）
Source/.../PlantModels3D.cs  运行时：defName→原型映射 + OBJ 导入缓存 + 三档色调 + 哈希朝向
                             + 生长缩放 + 网格层描边壳（焊接/平均法线/外扩/反绕）
PlantLayer3D.cs            接线：Handles 的植物让给 PlantModels3D 逐件绘制，其余回退星形/十字面片
```

## 2. 硬约束（全部可机检，plant_build.py 自带守卫）

1. **取样区语义**：皮/叶/点缀按相对系数落在内容包围盒内，`docs/plants/sampling.png` 人判；
   原版植物贴图是画意风、**没有硬黑描边**（实测 oak 0% 暗像素），桦树黑斑、可可树暗部是
   特征 —— 禁止照搬家具管线的"描边补绘"，会抹掉特征。
2. **材质不透底**：取样块内半透明/透明像素邻域实心填充（`solidify`），保证球面/锥面不漏底。
3. **着地 + 归一化**：生成后统一落地（min_y=0）、y 缩放到约定总高 H、xz 缩放到冠幅 D；
   守卫查 `min_y≈0`、`max_y≤H+0.02`、UV∈[0,1]、面数≤2400，任一失败退出码非零。
4. **面朝外**：lathe/sphere 公式按右手系 CCW 生成，导入器反转后即 Unity 正面；
   改绕序公式前先看 ObjImporter3D 的反转注释（实机踩坑：不反转整模被背面剔除）。
5. **径向叶先 pitch 后 yaw**：rot_yaw_pitch 必须先绕 X（外倾角）再绕 Y（方位角），
   否则叶片倾斜方向不随方位转（agave/棕榈/灵树都会错）。
6. **审核页 = 游戏实况**：页面必须加载运行时同一批 OBJ 文件与同一张图集；
   页面另建一份几何口径是家具管线踩过的漂移坑（ThreeViewMethod.md 第 14 条）。

## 3. 运行时行为

- **接管的植物**（PlantModels3D.Handles）：24 原型 —— 14 树（oak maple poplar pine cypress
  birch palm drago teak willow bamboo cocoa cecropia anima）+ 10 小株（bush berry brambles
  agave alocasia shrublow pincushion saguaro healroot devilstrand），逐件 `Graphics.DrawMesh`
  绘制（同家具路线）；生长缩放 = lerp(0.3, 1, Growth)；朝向与色调按 thingIDNumber 哈希。
- **未接管**（庄稼/花/草/变异植物）：继续走 PlantLayer3D 星形/十字面片 + plantwindsway 摇摆。
- **缺文件回退**：OBJ 缺失的原型 `Handles` 返回 false，该株回退面片路径，不会两路同时消失。
- **摇摆**：3D 树木用 Cutout 不摇摆 —— plantwindsway 着色器的顶点动画按立牌 UV 布局假设位移，
  套在真实网格上会整树果冻抖；树干本就不该摆，冠层摆动留待自研 shader（AssetBundle 前置）。
- **季节**：落叶/积雪态未做（leafless 态仍画有叶模型），已知取舍，待原型加裸枝变体。
- **性能**：接管件逐件 DrawMesh（体 + 壳），未并入 chunk 合批 —— 与家具同批待办
  （BACKLOG 家具条目 1）。

## 4. 本批已落地

24 原型全量过审（plant_build 守卫全绿 + art-reviewer 逐件 4 机位审核 24/24 PASS，
willow 悬空 / saguaro 断臂两处修复后复审通过）。审核表在 `docs/plants/audit/`。
