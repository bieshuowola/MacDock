# MacDock 状态档案 (v1.9.9 · Codex 交接入口)
> **完整技术交接文档（含三态明细、近两日增量、源码全图与待核实清单）**：[docs/CODEX_HANDOFF.md](docs/CODEX_HANDOFF.md)

## 1. 版本与演进基线
- **版本三态**：源码 `1.9.9`（完整源码仅在本地 `D:\Windows Dock\MacDock\`；远端 `main` 仅追踪 27 个文件，基准提交 `bbda3b4d`，本地 `README.md` 有未同步差异）；本地运行 `release\MacDock\MacDock.exe`（2026-11-18 构建）；GitHub Release `v1.9.9`（ID `447725438`）已含 `bbda3b4d` 安装包/便携包。
- **近期已实现**：`CoverageBorder` 圆角、`Genie` 第 0 帧预烘焙与底边零白线、抽屉跨 DPI 居中、`IDropTargetHelper` 原生拖放预览、外部 FileDrop 默认复制入抽屉（`Alt` 触发应用打开）、`DockMotion` 165Hz `CompositionTarget` 同步、抽屉图标同步防抖/和解（E2E 通过）。
- **边界保护**：抽屉镜像真实桌面（`解除固定`不删文件）；AppData 真实落点校验与布局历史保护；`Genie` 共享动态轮廓阴影；`F6` 键盘模式与原生 Shell 菜单焦点保护。
- **未解决与待核实**：① **抽屉内部滚动明显掉帧**；② **抽屉关闭再打开无法恢复上次滚动位置**（`Prepare()`/`PrepareForOpen()` 调用了 `scroll.ResetScroll()`）；③ **扩展名/类型/快捷方式变化后图标同步需真机再次核实**；④ **近期改动尚缺物理屏直连、混合 DPI 多屏、休眠与 Defender 误报实机验收**。

## 2. 核心源码与架构地图
- `Program.cs`：单实例、热键/托盘、桌面监听 `StartWatchers`/`RegisterShellChanges`、图标异步队列/防抖/和解。
- `DockWindow.cs`/`WakeStrip.cs`/`DockMotion.cs`：Dock 磁贴、唤醒条、165Hz 波浪曲线、外部拖放命中。
- `DrawerWindow.cs`/`Layout.cs`/`Ui.cs`：抽屉网格、磁贴复用、`SmoothScrollViewer`、跨 DPI 居中、预烘焙纹理。
- `Genie.cs`/`HostGlass.cs`/`RestShadow.cs`/`NativeContour.cs`/`CoverageBorder.cs`：3D 变形、实时玻璃、轮廓阴影、DPI 圆角。
- `IconResources.cs`/`Icons.cs`/`Native.cs`/`PointerSort.cs`/`ShellOps.cs`/`ReleaseUpdates.cs`：图标指纹与零闪烁更新、Win32/COM、Z 序拖拽、更新验签。

## 3. 稳定基线与不可破坏红线
- **环境**：无 `git`/`gh` CLI；走 `http://127.0.0.1:7897` + `$GITHUB_TOKEN` 调 GitHub REST API。
- **防阻塞**：严禁同步起 GUI，须 `schtasks /IT`；停进程用非阻塞 `Stop-Process`；热重载走 `Reload-Dev.ps1`。
- **交付与红线**：保持 `1.9.9`；严禁改 `DockMotion`/`WakeStrip`/`Genie` 曲线与抽屉底边零白线；交付须编译 0 错 + 静默 E2E 断言 + 视觉截图留证；未实机验证不得报完成。

## 4. 构建、测试与发布
- **构建/热重载**：`Build.ps1`（勿带锁直跑）、`Reload-Dev.ps1`（经 `schtasks /IT`）。
- **测试/回滚**：`Test.ps1`；E2E 构建 `-p:EnableMacDockExperienceChecks=true`，套件 `.scratch\run-all-e2e.ps1`；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **打包/发布**：`packaging\Build-Product.ps1`（需 `ISCC.exe` + 公钥参数）；`.scratch\publish-release.ps1`（REST API 推送 `main` 与 Release）。
