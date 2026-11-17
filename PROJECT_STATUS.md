# MacDock 项目状态档案 (v1.9.9)

## 1. 版本与演进基线
- **版本**：`1.9.9`（GitHub `bieshuowola/MacDock` `v1.9.9` ID `447725438`，已覆盖封版）；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。当前无阻塞待办。
- **核心已落地特性与状态机/保护锁**：
  - 右键零转圈：按下抢先替换 `32650`+`32514`，`ShellMenu` 钩子拦 `0x20/0x117/0x121`，1500ms 防抖还原。
  - 多选与归档：`Native.IsControlDown/IsShiftDown`（三态合检）+`selectionHandled` 防穿透；多选拖拽渲染叠牌+角标，命中 65% 中心暂停换位高亮归档，拖出转 `FileDrop`；网格支持橡皮筋框选。
  - 全局接收与图钉锁：外部 `FileDrop`/`Bitmap` 唤醒锁 `ExternalFileDragActive`（350ms 逃生），Dock 空白直达桌面；`InternalDropProtected` 防 `ShellOps` 失焦收起；`DrawerPinned` 纯 `#18000000` 阴影无蓝框，启动/`×`/Esc/全屏自动解锁。
  - 2K 165Hz 与渲染修复：`Timeline.DesiredFrameRateProperty` 对齐最高刷新率；主 Dock 移除 `HostGlass` 静止态 `CreateRoundRectRgn`/`SetWindowRgn` 硬裁剪、`glassTint` 升级 16x `CoverageBorder` 并移入 `glassLayer`；`DrawerWindow` 启用第 0 帧 DPI 1:1 静态纹理烘焙（`RenderTargetBitmap` + 冻结 `ImageBrush`），`EndGenie()` 立即置空释放实现零内存残留（CPU ~0.26ms/帧，160fps），并保留 `shareShadow=false`、`stage.Clip`（`pose.Row(1).Y - 0.5`）及 `prevDeformPose`+`SoftContour` 贴缘消除底边预乘白线与夹缝；`RefreshDesktop`/Shell `0x8000` 保留图标缓存；测试态隔离 Mutex、禁弹窗并 `Environment.Exit`。

## 2. 核心源码与架构地图
- `Program.cs`：主控、高刷解锁、测试态 Mutex/异常隔离、`DropFilesToDesktop`、局部图标刷新。
- `DockWindow.cs`/`DrawerWindow.cs`：`CoverageBorder` 圆角、`glassLayer`、`SetGenie`→`BakeGenieSurface`→`EndGenie`/`ReleaseBakedGenieSurface`→`UseFlatSurface`、增量 Diff & Patch。
- `HostGlass.cs`/`CoverageBorder.cs`/`NativeContour.cs`/`RestShadow.cs`：Per-Pixel Alpha 圆角合成、底边 1:1 对齐、`SoftContour` 阴影。
- `PointerSort.cs`/`Native.cs`/`ShellMenu.cs`/`ShellOps.cs`：叠牌外拖、缓存键 `(Path,Ticks,Length,Px,Stamp)`、`IFileOperation`。
- `ExperienceChecks.cs`：`--icon-test`、`--drawer-paste-reuse-test`、`--drawer-stability-test`、`--dock-stability-test`、`--external-drop-pin-test`。

## 3. 不可破坏的稳定基线与红线
- 严禁修改 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs`、`ProfileLocation.cs`/首启排序及动画曲线参数。
- 本机无 `git`/`gh` CLI，外部请求走 `127.0.0.1:7897` + `GITHUB_TOKEN`；严禁同步阻塞跑 GUI 或终端直接 `Build.ps1`；测试须用 `Start-Process` + 15s 硬超时（截屏/交互走 `schtasks /IT`）；交付前必须跑通 E2E 断言与视觉留证。

## 4. 构建、测试与发布脚本路径
- 编译：`Build.ps1`（检查构建 `dotnet build -c Release -p:DefineConstants=MACDOCK_EXPERIENCE_CHECKS`）
- 热重载：`schtasks /IT` 调 `Reload-Dev.ps1` 部署至 `release\MacDock\MacDock.exe`
- E2E 自检：设 `MACDOCK_TEST_RESULTS` 后运行 `bin\Release\net8.0-windows10.0.19041.0\MacDock.exe`（5 项全绿）
- 打包发布：`packaging\Build-Product.ps1 -Version 1.9.9`（产物 `release\product-1.9.9-candidate-*\`）+ `.scratch\publish-release.ps1`
