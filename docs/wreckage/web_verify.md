# 残骸样板 · 浏览器截图视觉验证（round 3）

- 服务：`http://127.0.0.1:8899/`（root=`viewer/`，PID 41616）
- 页面：`viewer/wreck.html?item=<名>&view=<视角>&v=1791581401`
- 截图：无头 Chrome `--headless=new --use-angle=swiftshader`，1280×900
- 样本：7 组各 1 样板；视角：45° / 俯视 / 主视·南 / 侧视·东 / 低角度
- 产物：`viewer/shots/{item}__v0..v4.png`（35 张）+ `viewer/shots/{item}__sheet.png`（7 张对照条）
- 判读：`tools/look.py`（视觉模型，结论仅作参考，关键项已单图/几何交叉复核）

## 总体结论

7 个样板全部正常渲染：**无 WebGL 报错（无 ERR 红字）**、无缺贴图（无品红/纯白）、
贴图颜色均正常，几何完整、无实质破面/穿模。可作为首版视觉交付。

## 逐样板

| 样板 | 渲染 | 贴图 | 几何 | 备注 |
|---|---|---|---|---|
| ruinedtank（废弃坦克） | ✅ | 迷彩绿，正常 | ✅ | 视觉模型提"履带/炮塔接缝悬空"，经几何核对：履带顶 0.30 与车体底 0.20 已重叠、炮管嵌入炮塔，判为视觉误报 |
| crashedshippart（坠毁飞船残骸） | ✅ | 灰绿/卡其，正常 | ✅ | 躺倒主筒侧视可见中空断口，合理 |
| ancientwarwalkershell（远古步行机残壳） | ✅ | 灰米石质+褐锈，正常 | ✅ | 石堆咬合自然；锐边缺口为造型设计 |
| largeengineblock（大型发动机缸体） | ✅ | 铁锈红/暗褐，正常 | ✅ | 盒体+4 圆柱缸，边缘干净 |
| giantwheela（巨型轮 A） | ✅ | 灰/褐金属，正常 | ✅ | **已澄清**：侧视"黑块"经单图复核仅暗部凹槽，占 1–2 成，非纯黑覆盖、非法线/UV 异常 |
| concretebarrier（混凝土路障） | ✅ | 混凝土灰，正常 | ✅ | **已澄清**：俯视"文字"为界面标签与右下角水印，非几何缺陷 |
| rustedindustrialtruck（锈蚀卡车） | ✅ | 锈蚀色，正常 | ✅ | **存疑解除**：侧视见完整轮盘圆面、主视见窄边 → 车轮盘面朝向为**左右两侧**，符合卡车预期，无需修改 |

## 已解除的存疑项

- **卡车 4 轮轴向**：`Cylinder(...,90f,90f)` 经 `R=Ry(yaw)*Rx(pitch)` 推算轴沿 X（盘面为 YZ 平面），
  侧视·东（+X）应见圆、主视·南（−Z）应见窄边 —— 截图实测与之一致，**几何正确，不改 `WreckageSamples.cs`**。
- **giantwheela 黑块 / concretebarrier 文字**：均为视觉模型夸大/叠加元素，非渲染缺陷。

## 仍待用户评审（历史遗留，未擅动）

1. `WreckageSamples.Uv("rustedindustrialtruck")` 在 `WreckageUVGen.R` 无裸键（仅 `.south/.north/.east`），
   运行时回退到 `ruinedtank` 区域 —— **真实缺口**，需补卡车裸键或改 `Uv` 取值逻辑。
2. 各样板高度为**估设**（俯视贴图无法直接给高），待渲染迭代。
3. `concretebarrier`(64px)/`giantwheela`(128px) 依 128px=1 格得 0.5~1 格，若原版 drawSize 不同需逐项缩放。
4. 侧面 UV 为近似条带，未经美术确认。
5. 样板尚未接入 `BuildingLayer3D`。