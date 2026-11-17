# MacDock 状态档案 (v1.9.9)
## 1. 版本与基线
- **版本**：`1.9.9`（Release `v1.9.9` ID `447725438`，无阻塞待办）；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **关键状态机/锁/修复**：
  - 右键：`32650`+`32514` 抢先替换，`ShellMenu` 钩子拦 `0x20/0x117/0x121`，1500ms 还原。
  - 多选与原生拖放：`Native.IsControlDown/IsShiftDown`+`selectionHandled`；`IDropTargetHelper`（`DropPreviewHelper`+`Show(true)`）透传系统拖放缩略图；叠牌外拖/归档/框选。
  - 接收与图钉：`ExternalFileDragActive`（350ms 逃生），Dock 空白直达桌面；`DrawerPinned` 无蓝框，启动/`×`/Esc/全屏解锁。
  - 渲染/高刷/居中：`DesiredFrameRateProperty` 对齐高刷；抽屉预烘焙冻结 `RenderTargetBitmap`+空闲重热；`shareShadow=false`+`stage.Clip`+`prevDeformPose`+`SoftContour` 零白线；`DeferWindowPosPair` 批量异步同步+`timeBeginPeriod(1)`/`SustainedLowLatency`/高优先级；`DrawerWindow.GridMetrics` 物理像素对齐 `tileW`/`gapX`/`chrome` 与 `groups` 居中（`asymPx=0.00`）。
  - 更新检测：`ReleaseUpdates` 支持同版本号 `v1.9.9` 资产时间与 `SHA256SUMS.txt` 修订比对。
## 2. 源码地图
- `Program.cs`：主控、高刷、动画期优先级/GC 锁、测试隔离、`DropFilesToDesktop`。
- `DockWindow.cs`/`DrawerWindow.cs`：`CoverageBorder`、`glassLayer`、`WarmPrebakedSurface`→`SetGenie`→`EndGenie`→`UseFlatSurface`、`GridMetrics`。
- `HostGlass.cs`/`RestShadow.cs`/`NativeContour.cs`/`Ui.cs`：Per-Pixel Alpha、批量异步定位、`UnitArc`、DPI 缓存失效。
- `Native.cs`/`ReleaseUpdates.cs`/`Dialogs.cs`：`DeferWindowPosPair`、`IDropTargetHelper`、同版本修订更新。
- `ExperienceChecks.cs`/`UpdateChecks.cs`：5 项 E2E（含左右边距对称断言 `<=2px`）与 `--update-test`。
## 3. 红线约束
- 禁改 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs`、`ProfileLocation.cs`/首启排序/动画参数。
- 无 `git`/`gh` CLI，走 `127.0.0.1:7897`+`GITHUB_TOKEN`；禁同步跑 GUI 或直接 `Build.ps1`；测试 `Start-Process`+15s 超时（交互抓图走 `schtasks /IT`）。
## 4. 脚本路径
- 编译 `Build.ps1` | 热重载 `schtasks /IT`→`Reload-Dev.ps1` | E2E `bin\Release\net8.0-windows10.0.19041.0\MacDock.exe` | 打包 `packaging\Build-Product.ps1 -Version 1.9.9` + `.scratch\publish-release.ps1`。