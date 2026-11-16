# MacDock 状态档案 (v1.9.9)
- 版本：`1.9.9`（GitHub：`bieshuowola/MacDock` `v1.9.9`），产物：`release\product-1.9.9-*\`
- 回滚：`checkpoint-1.9.7-before-1.9.8`，脚本：`backup\pre-1.9.8-20261116-200932\Rollback.ps1`

## 已落地功能
- 图标保护：Stale-While-Revalidate、透明检测、StockIcon/CLSID 兜底与退避重试
- 桌面镜像：单双击切换（双重 Ctrl/Shift 多选、扑克牌叠牌跟手与角标、抽屉/Dock 65% 中心投递分流、拖出转接 `FileDrop`、空白处橡皮筋框选、原滚动位置保留与新文件高亮）
- 全局接收与图钉锁定：外部拖拽（含 `Bitmap` 图像流）唤醒主 Dock 即进入 `ExternalFileDragActive` 全局接收（同盘 Move/跨盘 Copy/防回环自杀/后台 STA I/O），空白/通用区松手直达桌面；抽屉右下角精简为“图钉防收起锁定（按压阴影+ToolTip 联动，全屏/启动/Esc/主动关闭自动解锁）”+“+”
- 常驻屏蔽与菜单：空桌面常驻、最大化/全屏隐藏；按住 `SuppressWakeKey` 屏蔽唤醒；`AtLogOn` 计划任务自启；双系统光标（`32650`+`32514`）屏蔽与 1500ms 防抖还原消除右键转圈
- 首页预览：`distribution/` 已更新 50fps 循环动图 `dock-hover.webp` 与 `drawer-cycle.webp`（`skipDesktop` 隐私隔离）

## 核心源码
- `Program.cs`：状态机、全局拖拽锁、`DropFilesToDesktop`、`SaveDroppedBitmapToDesktop`、全屏解锁
- `DockWindow.cs`/`DrawerWindow.cs`：渲染、波浪、框选、图钉锁、65% 中心投递区
- `PointerSort.cs`/`Native.cs`/`ShellMenu.cs`/`Ui.cs`：多选叠牌跟随、Win32、Drop-to-Exe、光标锁定
- `Icons.cs`/`IconResources.cs`/`Models.cs`/`Dialogs.cs`：缓存、配置、设置 UI

## 绝对红线
- 禁改 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs` 动画参数及 `ProfileLocation.cs`/首启排序保护
- 无 `git`/`gh` CLI；API 走 `http://127.0.0.1:7897` 与 `GITHUB_TOKEN`
- 热重载禁直接覆写运行中进程文件

## 脚本
- 构建 `Build.ps1` | 测试 `Test.ps1` | 打包 `packaging\Build-Product.ps1`、`MacDock.iss` | 热重载 `Reload-Dev.ps1`
