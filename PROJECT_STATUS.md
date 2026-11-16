# MacDock 状态档案 (v1.9.8)
- 版本：`1.9.8`（GitHub：`bieshuowola/MacDock` `v1.9.8`），产物：`release\product-1.9.8-candidate-8773e7a5\`
- 回滚：`checkpoint-1.9.7-before-1.9.8`，脚本：`backup\pre-1.9.8-20261116-200932\Rollback.ps1`

## 已落地功能
- 图标保护：Stale-While-Revalidate、透明检测、StockIcon/CLSID 兜底与退避重试
- 桌面镜像：单双击切换（默认单击支持 Ctrl/Shift 多选、保留多选拖拽、空白区点击清选并释放焦点、Ctrl+A 全选及悬停下划线/手型光标）、拖放到 `.exe`/`.lnk` 带参运行、`Alt+点击` 原生属性
- 常驻屏蔽：空桌面常驻、最大化/全屏隐藏；按住 `SuppressWakeKey` 自动收起并拦截唤醒
- 自启菜单：`AtLogOn` 计划任务（降级 `Run`）；双系统光标（`32650`+`32514`）屏蔽与 1500ms 防抖还原消除右键转圈
- 首页预览：`distribution/` 已更新 50fps 高画质循环动图 `dock-hover.webp` 与 `drawer-cycle.webp`（`skipDesktop` 隐私隔离渲染）

## 核心源码
- `Program.cs`：状态机、重试、计划任务自启
- `DockWindow.cs`/`DrawerWindow.cs`：渲染、波浪、单双击、拖放
- `Native.cs`/`ShellMenu.cs`/`Ui.cs`：Win32、Drop-to-Exe、光标锁定
- `Icons.cs`/`IconResources.cs`/`Models.cs`/`Dialogs.cs`：缓存、配置、设置 UI

## 绝对红线
- 禁改 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs` 动画参数及 `ProfileLocation.cs`/首启排序保护
- 无 `git`/`gh` CLI；API 走 `http://127.0.0.1:7897` 与 `GITHUB_TOKEN`
- 热重载禁直接覆写运行中进程文件

## 脚本
- 构建 `Build.ps1` | 测试 `Test.ps1` | 打包 `packaging\Build-Product.ps1`、`MacDock.iss` | 热重载 `Reload-Dev.ps1`
