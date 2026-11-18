# MacDock 状态档案 (v1.9.9 · Codex 交接入口)
完整技术交接见 [docs/CODEX_HANDOFF.md](docs/CODEX_HANDOFF.md)（含三态对照、近两日增量、源码全图与待核实清单）。

## 1. 版本与演进基线
- **版本三态**：本地完整源码 `1.9.9`（`D:\Windows Dock\MacDock\`，`README.md` 有未同步差异）；远端 `main` 仅追踪 29 个文件（含本交接文档）；本地运行 `release\MacDock\MacDock.exe`；GitHub Release `v1.9.9`（ID `447725438`）已含 `bbda3b4d` 构建包。
- **近期已落地**：`CoverageBorder` 圆角、`Genie` 预烘焙与底边零白线、跨 DPI 居中、`IDropTargetHelper` 预览、外部拖入默认入抽屉（`Alt` 打开）、165Hz `DockMotion`、图标同步防抖/和解（E2E 通过）。
- **边界保护**：抽屉镜像真实桌面；AppData 真实落点校验；`Genie` 共享动态轮廓阴影；`F6` 键盘模式与原生菜单焦点保护。
- **未解决/待核实**：抽屉滚动掉帧；关闭再打开无法恢复滚动位置（`Prepare` 调用 `ResetScroll`）；扩展名/类型/快捷方式换标需真机核实；缺物理屏直连、混合 DPI 多屏与 Defender 实测。

## 2. 核心源码地图
- `Program.cs`：单实例、热键/托盘、桌面监听、图标防抖/和解。
- `DockWindow.cs`/`WakeStrip.cs`/`DockMotion.cs`：Dock 磁贴、唤醒条、165Hz 波浪、外部拖放。
- `DrawerWindow.cs`/`Layout.cs`/`Ui.cs`：抽屉网格、磁贴复用、滚动与跨 DPI 居中。
- `Genie.cs`/`HostGlass.cs`/`RestShadow.cs`/`NativeContour.cs`/`CoverageBorder.cs`：3D 变形、玻璃、轮廓阴影、圆角。
- `IconResources.cs`/`Icons.cs`/`Native.cs`/`PointerSort.cs`/`ShellOps.cs`/`ReleaseUpdates.cs`：图标指纹、Win32/COM、Z 序拖拽、更新验签。

## 3. 红线与环境约束
- 无 `git`/`gh`；走 `http://127.0.0.1:7897` + `$GITHUB_TOKEN` 调 GitHub API。
- 严禁同步起 GUI，须 `schtasks /IT`；停进程用 `Stop-Process`；热重载走 `Reload-Dev.ps1`。
- 保持 `1.9.9`；严禁改 `DockMotion`/`WakeStrip`/`Genie` 曲线与抽屉底边零白线；交付须编译 0 错 + 静默 E2E + 视觉留证，未实机验证不得报完成。

## 4. 构建、测试与发布
- 构建/热重载：`Build.ps1`、`Reload-Dev.ps1`（经 `schtasks /IT`）。
- 测试/回滚：`Test.ps1`；E2E `-p:EnableMacDockExperienceChecks=true`（`.scratch\run-all-e2e.ps1`）；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- 打包/发布：`packaging\Build-Product.ps1`（需 `ISCC.exe` + 公钥）；`.scratch\publish-release.ps1`。
