---
topics: [sop, workflow]
doc_kind: note
created: 2026-10-09
updated: 2026-10-11
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

## 出图（无头 Chrome）

所有启动无头 Chrome 的脚本只走一条口径：`scripts/_tmpsweep.py`。
`scratch()` 给每次启动一个 D 盘沙盒（`temp_shots/`，已 gitignore），`env_for(rd)` 把子进程的
TEMP/TMP/TMPDIR 按到沙盒，`--user-data-dir` 用 `profile(rd)`，出完图 `drop(rd)` 整目录删。

为什么不让它写 C 盘：Chrome 每次启动都在 `%TEMP%` 自建一个 `scoped_dir<PID>_<rand>`
（走真出图通路实测 23.0~26.2 MB/次，里面是 crashpad 加现装一遍的 autoclaw-chrome / xl_ext 扩展），
**显式指定 `--user-data-dir` 也拦不住**。720 格审核 = 16.5 GB 全砸 C 盘；
2026-10-11 就是这么把 C 盘写满的，连带 GABS 写不进运行时状态
（`write C:\Users\Administrator\.gabs\...: not enough space on the disk`）。
旧口径"运行期起线程扫 C 盘"是在和写入速度抢时间：静置阈值 90~120 s，
2 个 worker 约 42 MB/s，峰值先堆到 5~6 GB，而且进程一死（Ctrl-C / 崩 / 超时被杀）就再没人扫。

实测（本机，走 `furn_audit.shoot` 真出图通路）：726 格一批跑完 C 盘新增 0 个特征目录、
可用空间不动；D 盘沙盒稳态 190~270 MB（≈ worker 数 × 26 MB，用完即删）。
`drop()` 有约 2%（726 格里 15 次）被 crashpad 攥着句柄删不掉，重试窗口放到 8 s 也没消尽 ——
靠 `sweep_mine()` 在收尾时按**本进程 pid** 立刻收回（不动静置阈值、也不碰并发脚本的那份），
跑完残留归 0。

三道机检（都带合成夹具，夹具不过就拒绝给存量背书）：

| 检查 | 命令 | 抓什么 |
|------|------|--------|
| 通路卫生闸门 | `python scripts/shot_hygiene_check.py` | AST 扫全仓库启 Chrome 的脚本：没接 `_tmpsweep`、启动没传 `env=`、`tempfile.mkdtemp` 没给 `dir=`（写 C 盘）、没有 `drop()`、`sweep(0)`（会删并发脚本在用的沙盒） |
| C 盘写入量尺 | `python scripts/shot_temp_measure.py [次数]`<br>`python scripts/shot_temp_measure.py --batch 30` | 真出图 N 次（或 3 worker 并发批量），断言 C 盘 Chrome 特征目录新增为 0、收尾沙盒残留为 0；把 `env_for` 摘掉必须判 FAIL |
| 占用快照 | `python scripts/shot_temp_snap.py` | 长批量跑到中途时看一眼 C/D 盘 |

本仓库的脚本已不需要后台清扫器；`scripts/_tmpwatch.ps1` 是**可选**的机内网，
只为清仓库外那些无头 Chrome（浏览器自动化、别的代理的工具）留在 C 盘 `%TEMP%` 的 `scoped_dir*`。
不起它，本仓库也不会漏。

审核台的逐格缓存另有一条硬规矩：`raw/*.png` 命中缓存就不重渲，而空场景参考每轮必重渲，
两边不同源会拿"旧模型 vs 新场景"做差分 —— 2026-10-11 那 4 格 `floodlight/贴脸` 假断裂就是这么来的
（模型格是 04:17 的旧图，参考是 05:53 新渲的）。现在由 `furn_audit.invalidate_cache()`
按输入指纹（取样表 / 检查页 / `Furniture3D.cs` / 图集 / 脚本本体）作废，
夹具在 `selftest()` 里双向验：输入没变必须 reuse，变了或缺文件必须 reset 并删掉旧格。

游戏内截图不走这条路：原版 F12 与 GABS 落在
`C:\Users\Administrator\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Screenshots`
（2026-10-11 实测 328 张 / 1.83 GB，只进不出，没有自动轮换 —— 要清得人工确认，那是玩家自己的截图），
`scripts/make_shot_jpgs.py` 从那里读；`scripts/shot.ps1` 抓窗口默认写 D 盘 `shots/`。

## 改文档

- 行为或模块边界变了：更新 `docs/Architecture.md`。
- 新的取舍：在 `docs/decisions/` 加一篇短文，并在架构文档里链过去。
- 新工作：在 `BACKLOG.md` 加一行，并按 `docs/features/TEMPLATE.md` 写规格。完成后从 backlog 移除。

## 不要沿用的旧模板

本文件早期从别的仓库抄来过 Node / Biome / worktree 流程。那些命令在本仓库不存在。以本节为准。
