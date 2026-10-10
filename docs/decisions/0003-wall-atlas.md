---
topics: [rendering, walls, textures]
doc_kind: decision
created: 2026-10-10
status: accepted
---

# 建造墙的顶面和立面来自一张取样图集

## 背景

建造墙已经是高 `Fps3DState.WallHeight`（2.70 m）的体素盒子，邻面剔除。材质来自原版 `Graphic_Linked` 的当前连接子格：`CloneForBox` 保留那一格的 `mainTextureScale/Offset`，四个立面和顶面共用网格 UV 0..1。顶面因此是立面贴图被拉到水平面上，看不出墙帽。

原版墙图集是俯视连接图，不是现成的立方体贴图。一张材质上的 scale/offset 只能框住一个子格，没法让顶面和立面各取一段。

## 决定

- 盒子网格保持现在的体素墙。不换建模方式，不按面增加绘制。
- 从原版三张外观图（`wall_atlas_bricks`、`wall_atlas_planks`、`wall_atlas_smooth`）各取「墙帽」和「立面」两段，打进一张图集。运行时用网格 UV 把顶面贴到墙帽、四个立面贴到立面段。
- 一个外观加一种染色共用一个材质。chunk 仍按材质合并。图集缺失时，这一面墙退回现在的连接子格。
- 取样矩形由审美定稿，写在 `docs/walls/WallAtlasSpec.md`。打包脚本和网格接线是机械工作，见 `docs/tasks/T-W1-wall-atlas.md`。
- 天然岩不走这张图集。山体的取样和坡面仍是单独的审美，见 F006。

## 后果

建造墙不再依赖连接子格的 scale/offset 来选像素。连接形状（直墙、转角、丁字）继续只影响「哪几面被剔除」，不决定贴哪一段纹理。

金属材料没有独立墙图，原版会退回 Smooth。本决定同样把 Metal 映到 smooth 段，染色仍取 `MatAt` 的颜色。

意识形态建筑风格的墙图本决定不取样。那些墙在图集落地后先显示核心三套外观加染色。要分风格时另加矩形，不改网格。
