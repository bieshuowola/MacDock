# MacDock 状态档案 (v1.9.9)
## 1. 版本与基线
- **版本**：`1.9.9`（Release `v1.9.9` ID `447725438`，无阻塞待办）；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **关键状态机/锁/修复**：
  - 右键：`32650`+`32514` 抢先替换，`ShellMenu` 钩子拦 `0x20/0x117/0x121`，1500ms 还原。
  - 多选拖拽：`Native.IsControlDown/IsShiftDown`（三态）+`selectionHandled`；叠牌角标，65% 中心暂停换位高亮归档，出界转 `FileDrop`；网格框选。
  - 接收与图钉：`ExternalFileDragActive`（350ms 逃生），Dock 空白直达桌面；`InternalDropProtected` 防失焦；`DrawerPinned` 无蓝框，启动/`×`/Esc/全屏解锁。
  - 渲染/165Hz 稳帧：`DesiredFrameRateProperty` 对齐高刷；抽屉空闲/变更预烘焙冻结 `RenderTargetBitmap`（`WarmPrebakedSurface`/`InvalidatePrebakedSurface`），第 0 帧零阻塞复用、`EndGenie` 释放重排（avg=5.91~6.02ms，166fps+）；`shareShadow=false`+`stage.Clip`（`Row(1).Y-0.5`）+`prevDeformPose`+`SoftContour`（零白线）；`BeginDeferWindowPos`/`DeferWindowPos` 批量异步窗口同步（`SWP_ASYNCWINDOWPOS|SWP_NOACTIVATE`）+`HostGlass.RevealAndOpacity` 合并刷新+动画期 `timeBeginPeriod(1)`/`SustainedLowLatency`/高优先级。
## 2. 源码地图
- `Program.cs`：主控、高刷、动画期优先级/GC 锁、测试隔离、`DropFilesToDesktop`。
- `DockWindow.cs`/`DrawerWindow.cs`：`CoverageBorder`、`glassLayer`、`WarmPrebakedSurface`→`SetGenie`→`EndGenie`/`ReleaseBakedGenieSurface`→`UseFlatSurface`、增量 Diff。
- `HostGlass.cs`/`RestShadow.cs`/`NativeContour.cs`/`Ui.cs`：Per-Pixel Alpha、批量异步定位、单位圆预计算、DPI 缓存、`SoftContour`。
- `PointerSort.cs`/`Native.cs`/`ShellMenu.cs`/`ShellOps.cs`：叠牌外拖、`DeferWindowPosPair`、缓存键、`IFileOperation`。
- `ExperienceChecks.cs`：`--icon-test`、`--drawer-paste-reuse-test`、`--drawer-stability-test`、`--dock-stability-test`、`--external-drop-pin-test`。
## 3. 红线约束
- 禁改 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs`、`ProfileLocation.cs`/首启排序/动画参数。
- 无 `git`/`gh` CLI，走 `127.0.0.1:7897`+`GITHUB_TOKEN`；禁同步跑 GUI 或直接 `Build.ps1`；测试 `Start-Process`+15s 超时（交互抓图走 `schtasks /IT`）；交付前必跑 E2E 断言与视觉留证。
## 4. 脚本路径
- 编译 `Build.ps1` | 热重载 `schtasks /IT`→`Reload-Dev.ps1`（`release\MacDock\MacDock.exe`）| E2E 设 `MACDOCK_TEST_RESULTS` 跑 `bin\Release\net8.0-windows10.0.19041.0\MacDock.exe` | 打包 `packaging\Build-Product.ps1 -Version 1.9.9` + `.scratch\publish-release.ps1`。