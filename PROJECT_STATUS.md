# MacDock 状态快照

## 1. 当前版本与核心能力
- **版本**：`v1.9.9.4`（抽屉自动收起状态机与关联上下文窗口优化）。
- **核心改进**：统一管理抽屉关联上下文（Dock、设置页、`Dialogs.Prompt`、`EntryMenu`/`ShellMenu` 及初始化期 `#32770`/`OperationStatusWindow`/`OpenWith` 与 IME 候选窗）；协调关联弹窗 Z-Order 并自动还原；修复 `#32770` 关闭回落误收起、减少动画双击防穿透、`Ctrl+F` 焦点同步及状态残留；通过 11 项全量自动化测试、真实桌面截图留证及独立交叉审计。

## 2. 关键文件与脚本
- **焦点源码**：`ModalInputGate.cs`、`Program.cs`、`Native.cs`、`ShellMenu.cs`、`ShellOps.cs`、`Dialogs.cs`、`DockWindow.cs`、`DrawerWindow.cs`、`TransitionChecks.cs`、`DrawerDismissChecks.cs`。
- **构建与发布**：`packaging\Build-Product.ps1`、`Reload-Dev.ps1`、`release\MacDock\MacDock.exe`。

## 3. 技术红线
- 禁改 Genie/弹簧参数、禁 `ps_3_0`、禁删 `Surface` `Visibility.Hidden`、`DpiChanged` 须校验 `OriginalSource`。
- 禁触真实桌面与用户配置；桌面测试/热重载必须经 `schtasks /IT`；回滚备份位于 `backup\1994-prepublish\`。
