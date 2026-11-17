# MacDock 状态档案 (v1.9.9)
## 1. 版本与基线
- **版本**：`1.9.9`（GitHub `bieshuowola/MacDock` `v1.9.9` ID `447725438`），产物 `release\product-1.9.9-candidate-2098bcce\`；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **关键状态机与防坑**：
  - 右键零转圈：按下抢先替换 `32650`+`32514`，`ShellMenu` 钩子拦 `0x20/0x117/0x121`，1500ms 防抖还原。
  - 单击多选归档：`Native.IsControlDown/IsShiftDown`（三态合检）+`selectionHandled` 防穿透；多选拖拽渲染扑克牌叠牌+角标，命中 65% 中心暂停换位高亮归档，拖出边界转原生 `FileDrop`；网格区支持橡皮筋框选。
  - 全局接收与图钉锁：外部 `FileDrop`/`Bitmap` 唤醒即锁 `ExternalFileDragActive`（350ms 逃生），Dock 空白直达桌面（同盘 Move/跨盘 Copy/防回环/STA 异步 I/O，保滚动并高亮新项）；`InternalDropProtected` 防 `ShellOps` 失焦收起；`DrawerPinned` 纯 `#18000000` 阴影无蓝框，启动/`×`/Esc/全屏自动解锁。
  - 渲染加固：`ShowDrawerAnimated` 经 `CompositionTarget.Rendering` 首帧防透明，图片后台 `<=96px` 降采样，动画期延后 `SyncDesktop`，`ConfigureSurfaceGeometry` 收敛圆角。
## 2. 源码地图
- `Program.cs`：主控、`AutoHideBlocked`、`DropFilesToDesktop`、`SaveDroppedBitmapToDesktop`、首帧同步。
- `DockWindow.cs`/`DrawerWindow.cs`：渲染、65% 投递区、框选、图钉锁、`ConfigureSurfaceGeometry`。
- `PointerSort.cs`/`Native.cs`/`ShellMenu.cs`/`ShellOps.cs`：叠牌外拖、双光标与降采样、`IFileOperation`。
- `ExperienceChecks.cs`：`--drag-e2e-test`、`--external-drop-pin-test`、`--drawer-stability-test`、`--product-preview`。
## 3. 红线约束
- 禁改 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs`、`ProfileLocation.cs`/首启排序。
- 无 `git`/`gh` CLI，走 `127.0.0.1:7897`+`GITHUB_TOKEN`；禁同步阻塞跑 GUI 或直接 `Build.ps1`；须先 E2E（`ExitCode=0`）+留证再热重载打包。
## 4. 脚本路径
- 构建 `Build.ps1` | 热重载 `schtasks` 调 `Reload-Dev.ps1` | E2E `-p:EnableMacDockExperienceChecks=true` 跑 `.scratch\preview-bin` | 打包 `packaging\Build-Product.ps1 -Version 1.9.9`
