---
topics: [rendering, walls, textures]
doc_kind: spec
created: 2026-10-10
status: accepted
---

# 建造墙取样规格

审美定稿。实现任务 `docs/tasks/T-W1-wall-atlas.md` 只能照抄这些矩形，不能挪边、不能换格子、不能改成平铺。

对照图：`docs/walls/_sample_preview.png`。左列是东西向直墙那一格，绿框是墙帽，黄框是立面。中列是墙帽取样，右列是立面取样。

## 源图

三张图都是 320×320，4×4 连接格。原版 `MaterialAtlasPool` 每格 80×80，内容区从格内 (10,10) 起 60×60（scale 3/16，padding 1/32）。

取样只用东西向直墙（`LinkDirections` = Left|Right = 10）。这一格的南侧立面是一整条材质，墙帽是它上方的平顶。南北向直墙主要是窄顶，不拿来做立面。

内容区在源图上的像素原点（左上角）是 (170, 90)。

| 键 | 文件（相对 `D:\rim mod\RimWorld_Vanilla_Textures_1.6`） | 区域 | 矩形 (x0, y0, x1, y1) |
|---|---|---|---|
| `bricks.top` | `Core/textures/things/building/linked/wall/wall_atlas_bricks.png` | 墙帽 | (170, 97, 230, 121) |
| `bricks.side` | 同上 | 立面 | (170, 126, 230, 146) |
| `planks.top` | `Core/textures/things/building/linked/wall/wall_atlas_planks.png` | 墙帽 | (170, 97, 230, 121) |
| `planks.side` | 同上 | 立面 | (170, 126, 230, 146) |
| `smooth.top` | `Core/textures/things/building/linked/wall/wall_atlas_smooth.png` | 墙帽 | (170, 97, 230, 121) |
| `smooth.side` | 同上 | 立面 | (170, 126, 230, 146) |

坐标是源图像素，原点在左上，半开区间 `[x0,x1)×[y0,y1)`，与 `scripts/furn_atlas.py` 相同。三张图布局对齐，所以矩形数字相同，像素内容不同。

## 为什么是这两段

东西向直墙内容区高 60 行（第 0 行在 y=90）：

- 第 0–1 行、第 57–59 行是黑边。描边由反绕壳负责，不进材质。
- 第 7–30 行是平顶。砖和木板这里是暗灰墙帽；光滑墙这里是中灰墙帽。上下各让开 1 行高光，避免把俯视光照边带进顶面。
- 第 36–55 行是朝南的立面。砖是砖缝，木板是横板，光滑是亮灰墙身。不含顶面倒角，不含底部发黑的收边。

立面这段上下接不齐（砖的最下一皮被原图裁短），不是可重复的瓷砖。因此一面墙的立面把这段贴一次，铺满整面 `WallHeight`，不做垂直重复。水平方向一面墙（1 m）也贴一次。

过滤用 `FilterMode.Point`。立面只有 20 像素高，拉到 2.70 m 后双线性会把砖缝抹平。家具图集继续双线性，不要跟着改。

## 外观怎么对应

| `StuffAppearanceDef.defName` | 图集键前缀 |
|---|---|
| `Bricks` | `bricks` |
| `Planks` | `planks` |
| `Smooth` | `smooth` |
| `Metal` | `smooth` |
| 其他或没有材料 | `smooth` |

金属没有 `wall_atlas_metal`。原版 `Graphic_Appearances` 缺图时退回 Smooth，这里同样退回 smooth。石块是 Bricks，木头是 Planks，钢、玻璃钢、铀是 Metal，所以石墙和钢墙的立面像素不同，染色再乘上各自的材料色。

染色取这块墙 `Graphic.MatAt` 返回的 `material.color`，不另读一张颜色表。
