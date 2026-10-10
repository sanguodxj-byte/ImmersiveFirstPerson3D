---
topics: [sop, workflow]
doc_kind: note
created: 2026-10-09
updated: 2026-10-10
---

# 开发流程

本仓库是环世界 1.6 的 C# Harmony 模组，目标框架 net48。没有前端工具链。

## 改代码

1. 对照 `docs/Architecture.md` 和对应的 `docs/features/Fxxx-*.md`。高度只用 `Metrics3D`，不在业务代码里写新的米制魔数。
2. 生成物不手改：`FurnitureUVGen.cs`、`WreckageUVGen.cs`、`WallUVGen.cs` 和 `1.6/Textures/FPS3D/*Atlas.png`。改取样表后重跑 `scripts/furn_atlas.py`、`scripts/wreck_atlas.py` 或 `scripts/wall_atlas.py`，再用 `scripts/furn_uv_check.py` 校验家具。墙的矩形以 `docs/walls/WallAtlasSpec.md` 为准，不在脚本里另改。
3. 构建：

```powershell
dotnet build "Source\RimFirstPerson3D\RimFirstPerson3D.csproj"
```

输出 DLL 在 `1.6/Assemblies/`。

4. 部署到游戏模组目录：`scripts/deploy.ps1`。环世界进程开着会锁住 DLL，脚本会直接失败。先关游戏。
5. 实机验收走 GABS：调试菜单分组 `FirstPerson3D`，或写 `FirstPerson3DSettings.cmdAction`（`createUpper`、`levelUp`、`levelDown`、`streamEast`、`unloadStreamed`、`mapScreen`、`termPose`）。性能读 `perfSnapshot` / F7。

## 改文档

- 行为或模块边界变了：更新 `docs/Architecture.md`。
- 新的取舍：在 `docs/decisions/` 加一篇短文，并在架构文档里链过去。
- 新工作：在 `BACKLOG.md` 加一行，并按 `docs/features/TEMPLATE.md` 写规格。完成后从 backlog 移除。

## 不要沿用的旧模板

本文件早期从别的仓库抄来过 Node / Biome / worktree 流程。那些命令在本仓库不存在。以本节为准。
