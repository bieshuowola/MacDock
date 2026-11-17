# MacDock 状态档案 (v1.9.9)
## 1. 版本与基线
- **版本**：`1.9.9`（GitHub `bieshuowola/MacDock` `v1.9.9` ID `447725438`），产物 `release\product-1.9.9-candidate-*\`；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **关键状态机与防坑**：
  - 右键零转圈：按下抢先替换 `32650`+`32514`，`ShellMenu` 钩子拦 `0x20/0x117/0x121`，1500ms 防抖还原。
  - 单击多选归档：`Native.IsControlDown/IsShiftDown`（三态合检）+`selectionHandled` 防穿透；多选拖拽渲染扑克牌叠牌+角标，命中 65% 中心暂停换位高亮归档，拖出边界转原生 `FileDrop`；网格区支持橡皮筋框选。
  - 全局接收与图钉锁：外部 `FileDrop`/`Bitmap` 唤醒即锁 `ExternalFileDragActive`（350ms 逃生），Dock 空白直达桌面；`InternalDropProtected` 防 `ShellOps` 失焦收起；`DrawerPinned` 纯 `#18000000` 阴影无蓝框，启动/`×`/Esc/全屏自动解锁。
  - 渲染与抗锯齿加固：`Timeline.DesiredFrameRateProperty` 匹配物理屏最高刷新率（抽屉 2K 165Hz 满帧）；主 Dock 移除 `HostGlass` 静止态 `CreateRoundRectRgn`/`SetWindowRgn` 硬裁剪、`glassTint` 升级为 16x 超采样 `CoverageBorder` 并移入 `glassLayer` 防重排；`DrawerWindow` 变形期置 `shareShadow=false` 且以 `stage.Clip` 精确裁切 `pose.Row(1).Y - 0.5` 消除 `Viewport2DVisual3D` D3D9 纹理底边预乘白线，`prevDeformPose` 与 `SoftContour` 贴缘（`Pad`）消除底边夹缝与 `open-28` vs `open-29` 突变；`RefreshDesktop`/Shell `0x8000` 保留未变更图标缓存；测试态隔离 Mutex、禁弹窗并显式 `Environment.Exit`。
## 2. 源码地图
- `Program.cs`：主控、高刷帧率解锁、测试态 Mutex/异常隔离、局部图标刷新、`DropFilesToDesktop`。
- `DockWindow.cs`/`DrawerWindow.cs`：`CoverageBorder` 抗锯齿圆角、`glassLayer` 隔离重排、`stage.Clip` + `prevDeformPose`、增量 Diff & Patch。
- `HostGlass.cs`/`CoverageBorder.cs`/`NativeContour.cs`/`RestShadow.cs`：全 Per-Pixel Alpha 圆角合成、底边 1:1 对齐、贴缘 `SoftContour` 阴影。
- `PointerSort.cs`/`Native.cs`/`ShellMenu.cs`/`ShellOps.cs`：叠牌外拖、缓存键 `(Path,Ticks,Length,Px,Stamp)`、`IFileOperation`。
- `ExperienceChecks.cs`：`--icon-test`、`--drawer-paste-reuse-test`、`--drawer-stability-test`、`--dock-stability-test`、`--external-drop-pin-test`。
## 3. 红线约束
- 禁改 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs`、`ProfileLocation.cs`/首启排序。
- 无 `git`/`gh` CLI，走 `127.0.0.1:7897`+`GITHUB_TOKEN`；禁同步阻塞跑 GUI 或直接 `Build.ps1`；跑测试须用 `Start-Process` + 15s 硬超时（GUI 抓图走 `schtasks /IT`）。
## 4. 脚本路径
- 构建 `Build.ps1` | 热重载 `schtasks /IT` 调 `Reload-Dev.ps1` | E2E `-p:EnableMacDockExperienceChecks=true` 跑 `.scratch\preview-bin` | 打包 `packaging\Build-Product.ps1 -Version 1.9.9`