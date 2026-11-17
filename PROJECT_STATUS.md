# MacDock 状态档案 (v1.9.9)

## 1. 版本与演进基线
- **当前版本**：`1.9.9`（GitHub：`bieshuowola/MacDock` `v1.9.9` ID `447725438`），产物 `release\product-1.9.9-candidate-2098bcce\`；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **关键特性与状态机**：
  - **右键零转圈**：`PreviewMouseRightButtonDown` 抢先双替换 `OCR_APPSTARTING(32650)`+`OCR_WAIT(32514)`，`ShellMenu` 钩子拦截 `WM_SETCURSOR/WM_INITMENUPOPUP/WM_ENTERIDLE`，关闭后 1500ms 防抖延迟还原（退出时 `force` 同步还原）。
  - **单击多选与叠牌归档**：`Native.IsControlDown/IsShiftDown`（`Keyboard.Modifiers`+`GetKeyState`+`GetAsyncKeyState`）配合 `selectionHandled` 防穿透；多选拖拽渲染前 3 项扑克牌叠牌+数字角标，`HitDropTarget` 命中文件夹/`.exe` 中心 65% 暂停换位并高亮归档，拖出边界转接原生 `FileDrop`；空白网格内支持橡皮筋框选。
  - **全局接收与图钉锁定**：外部 `FileDrop`/`Bitmap` 唤醒 Dock 即激活 `ExternalFileDragActive`（350ms 异常逃生），空白/通用区松手直达桌面（同盘 Move/跨盘 Copy/防回环/STA 异步 I/O，保留滚动条并高亮新文件）；`InternalDropProtected` 800~1200ms 保护锁防 `ShellOps` 失焦误收起。`DrawerPinned` 锁定（无蓝框纯 `#18000000` 柔和阴影+动态 ToolTip），启动应用/`×`/Esc/全屏自动解锁。
  - **渲染稳定性**：`ShowDrawerAnimated` 经 `CompositionTarget.Rendering` 首帧同步防透明闪烁；`Native.Icon` 后台 `DecodePixelWidth<=96` 异步降采样；动画期延后 `SyncDesktop` 防掉帧；`ConfigureSurfaceGeometry` 收敛圆角抗锯齿。
- **待办**：暂无阻塞，后续迭代基于 `1.9.9` 增量演进。

## 2. 核心源码与架构地图
- `Program.cs`：主控状态机、`AutoHideBlocked`/`InternalDropProtected` 锁、`DropFilesToDesktop`、`SaveDroppedBitmapToDesktop`、全屏解锁、首帧同步。
- `DockWindow.cs`/`DrawerWindow.cs`：Dock/抽屉渲染、`InCenterDropZone` 65% 投递分流、橡皮筋框选、图钉锁与 `ConfigureSurfaceGeometry`。
- `PointerSort.cs`/`Native.cs`/`ShellMenu.cs`/`ShellOps.cs`：叠牌跟随与外拖转接、双光标屏蔽、异步图片降采样、`IFileOperation` 静默/Undo。
- `ExperienceChecks.cs`：`--drag-e2e-test`、`--external-drop-pin-test`、`--drawer-stability-test`、`--product-preview`。

## 3. 稳定基线与红线
- **禁改红线**：严禁改动 `DockMotion.cs`、`WakeStrip.cs`、`Genie.cs` 动画参数及 `ProfileLocation.cs`/首启排序保护；`README` 预览保持无边框居中 `dock-hover.webp` 与 `drawer-cycle.webp`（`skipDesktop` 隐私隔离）。
- **环境与交付约束**：本机无 `git`/`gh` CLI，GitHub API 走 `http://127.0.0.1:7897` + `GITHUB_TOKEN`；严禁直接同步启动 GUI 或直接运行 `Build.ps1` 撞文件锁；所有修改须先跑通对应 E2E 断言（`ExitCode=0`）并留证，再经计划任务热重载与打包。

## 4. 构建、测试与发布路径
- **编译/热重载**：`dotnet build -c Release`；桌面热重载 `schtasks /IT` 调用 `Reload-Dev.ps1`（内调 `Build.ps1` 并重启 `release\MacDock\MacDock.exe`）。
- **E2E 测试**：`Test.ps1` 及 `-p:EnableMacDockExperienceChecks=true` 发布至 `.scratch\preview-bin` 后计划任务运行 `--drag-e2e-test` / `--external-drop-pin-test` / `--drawer-stability-test`。
- **打包发布**：`packaging\Build-Product.ps1 -Version 1.9.9`（配合 `MacDock.iss`）产出至 `release\product-1.9.9-*\`。
