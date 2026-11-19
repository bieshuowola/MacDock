# MacDock 状态快照

## 1. 当前基线状态
- **版本与部署**：`v1.9.9.2`（本地构建 `Seq 639306903267803041`，指纹 `92b42b84c6194b618fe202ab3eaa5b69`，**未发远端**）；`release\MacDock\MacDock.exe` 已热重载运行中，用户真实配置与桌面文件未改动。
- **最近修改文件**：`Genie.cs`、`Ui.cs`、`Icons.cs`、`DockWindow.cs`、`Program.cs`、`DrawerWindow.cs`、`DrawerScrollChecks.cs`（前序同期改动：`NativeContour.cs`、`HostGlass.cs`、`RestShadow.cs`、`ModalInputGate.cs`、`Theme.xaml`、`Dialogs.cs`、`SettingsReleaseChecks.cs`、`GenieBenchmarkChecks.cs`）。
- **已验证成果（实测事实）**：
  - 默认 `ps_2_0` Genie Shader 引擎：滚动位置闪跳已修复，顶/中/底/快速连续/隐藏 `Render()` 后重开第 1 帧偏移与纹理指纹 100% 一致，未暂停循环 `maxFrameMs` 为 `7.92–19.00 ms`（0 帧 >25ms）。
  - 抽屉图标偶发空白与休眠恢复空白已修复：引入 `IconViewHost`（构造即订阅 `icon.Changed`/`Theme.Changed`）、`IconEntry.StagePending`/`ApplyPending`/`SeedFrom`、`EnsureIconLoadedNow`、`EnsureSurfaceLayout` 强制重排及 `OnSystemResume` 重建 GPU 纹理；实测 `--corner-test`（`MACDOCK_DRAWER_SCROLL_CHECK=1`、`MACDOCK_SCROLL_INTERACTION_CHECK=1`）全项通过：冷启动、连续 5x、长闲置、随机 4x 及 5x 休眠恢复均 `72/72` 就绪、`blankIcons=0`、`prebakedBlankTiles=0`、`unarrangedImages=0`；并修复 `Image.MeasureOverride` 冒泡 `DpiChanged` 触发 `PropagateResumeLayout` `FailFast`。
  - 设置页对齐验收：`--settings-release-test` `41/41` 全过，`458`/`400` 宽度右边缘 0.00 DIP 对齐，说明文字无重叠挤压。
- **待验证事项**：完整 `--genie-benchmark-test` 尚未在最新图标/滚动修复后重跑；远端发版/同步暂缓，待用户决定。

## 2. 隐性约束与排错黑名单
- **废案黑名单**：
  - 禁 `ps_3_0` 着色器（黄屏）；`mac-gpu` 闲置 33.86ms 勿设默认；`Surface` 保持 `Visibility.Hidden` 防重烘焙。
  - 禁在 `DpiChanged` 处理器中未校验 `ReferenceEquals(e.OriginalSource, this)` 直接同步 `Render()`（会因 `Image.MeasureOverride` 冒泡触发 WPF `FailFast`）。
  - 禁仅靠 `Surface.IsArrangeValid` 判断是否 `UpdateLayout()`（固定宽高 `IconViewHost` 会截断冒泡导致子 `Image` `RenderSize=0x0`）。
  - 禁仅靠 `Loaded` 订阅 `icon.Changed`（未 `Show()` 窗口 `IsLoaded=false` 会漏订）。
- **操作红线**：禁触真实桌面与用户配置；GUI 启动/测试/热重载必须走 `schtasks /IT`（热重载脚本 `Reload-Dev.ps1`）；回滚入口 `backup\1992-genie-redesign-baseline\Rollback-1992.ps1`。

## 3. 遗留问题与焦点坐标
- **遗留问题**：`wpfgfx` 首 2 帧 10–21ms（架构级需 DX11/DComp）；`--appearance-test` 滑块项未过。
- **下一步任务**：按用户指令决定是否重跑 `--genie-benchmark-test`、推进远端双仓同步与 Release 签发，或继续处理遗留项。
- **核心焦点文件**：`DrawerWindow.cs`、`Icons.cs`、`Genie.cs`、`Program.cs`、`Dialogs.cs`、`DrawerScrollChecks.cs`。
