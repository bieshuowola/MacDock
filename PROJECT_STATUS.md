# MacDock 状态快照

## 1. 当前基线状态
- **版本与部署**：`v1.9.9.3`（本机正式部署已更新，未发布 GitHub Release；构建 `Seq 639309497063631859`，指纹 `b969ac1e953c44d9b660d75722f2473f`）；本机 `release\MacDock\MacDock.exe` 已覆盖部署并单实例运行中，用户配置未改动（`entries=106, pinned=11, manual=1`）。
- **最近修改文件**：`DrawerWindow.cs`、`DockWindow.cs`、`Native.cs`、`RunningApplications.cs`、`EntryMenu.cs`、`Program.cs`、`DrawerScrollChecks.cs`、`DrawerAndCloseChecks.cs`、`MacDock.csproj`。
- **已验证成果（实测事实）**：
  - **取消抽屉「手动添加 · 快捷入口」独立分组**：`DrawerWindow.Render()` 取消按 `Source == "manual"` 拆分，所有手动添加项目直接融入正常图标网格并参与「自定义 / 按类型 / 首字母」统一分组与拖拽排序（`SortTiles()`）；保留 `Source == "manual"` / `IsAlias` 数据语义以防桌面同步误删或删除原文件。
  - **Dock / 抽屉右键菜单「关闭」**：新增 `Native.ApplicationWindows()`、`Native.PostCloseWindow`（`PostMessage(WM_CLOSE)`，兼容 UWP `ApplicationFrameWindow` 与隐藏 owner 窗口）、`RunningApplications.ClosableWindows()`、`Controller.CanCloseEntry` / `CloseEntry` 与 `DockWindow.RefreshRunningSoon()`；仅对存在可关闭窗口的运行中单个应用显示「关闭」，正常发送 `WM_CLOSE`，支持多窗口、已关闭竞态、固定/未固定项，不取消固定、不删文件、不误关其他程序。
  - **实测通过**：`--self-test`（89/89）、`--drawer-close-test`（22/22 + 统一网格与右键菜单截图留证）、`--corner-test`（`MACDOCK_DRAWER_SCROLL_CHECK=1`，5/5 零回归）。

## 2. 隐性约束与排错黑名单
- **废案黑名单**：
  - 禁 `ps_3_0` 着色器（黄屏）；`mac-gpu` 闲置 33.86ms 勿设默认；`Surface` 保持 `Visibility.Hidden` 防重烘焙。
  - 禁在 `DpiChanged` 处理器中未校验 `ReferenceEquals(e.OriginalSource, this)` 直接同步 `Render()`。
  - 禁仅靠 `Surface.IsArrangeValid` 判断是否 `UpdateLayout()`；禁仅靠 `Loaded` 订阅 `icon.Changed`。
- **操作红线**：禁触真实桌面与用户配置；GUI 启动/测试/热重载必须走 `schtasks /IT`（热重载脚本 `Reload-Dev.ps1`）；回滚入口 `backup\1993-predeploy-1992\`（`v1.9.9.2` 本地载荷，SHA256 已核验）及 `backup\1992-rev2-prepublish\`。

## 3. 遗留问题与焦点坐标
- **遗留问题**：`wpfgfx` 首 2 帧 10–21ms（架构级需 DX11/DComp）；`--appearance-test` 滑块项未过；远端双仓与 `v1.9.9.3` Release 待用户确认后签发。
- **核心焦点文件**：`DrawerWindow.cs`、`DockWindow.cs`、`EntryMenu.cs`、`RunningApplications.cs`、`Native.cs`、`Program.cs`、`DrawerAndCloseChecks.cs`。
