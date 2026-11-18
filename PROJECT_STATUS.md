# MacDock 状态档案 (v1.9.9)
- **基线**：`1.9.9`（本地正式部署 `release\MacDock\MacDock.exe`）；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **最新落地修复**：
  - **抽屉图标全场景同步**：`DrawerWindow.Render()` 复用磁贴时比对 `iconHost.Tag` `(e.Path, l.DrawerIcon)` 重绑定 `Icons.View`；`IconResources` 将文件属性、`desktop.ini` 元数据、`.lnk` 目标及 5 秒缓存的 `UserChoice`/HKCR 关联纳入 `Stamp`，支持精准单路径 `Invalidate(path)`。`Program` 新增 80ms 路径防抖（动画/滚动期延后）、`SHCNE_UPDATEIMAGE`/`WM_SETTINGCHANGE` 无闪烁重校验（250ms 防抖，仅 Fingerprint 变化换图）及低频一致性和解（抽屉可见 15s / 后台 60s）。
  - **历史已落地**：外部 FileDrop 默认复制存入抽屉；`DockMotion` 144Hz/165Hz `CompositionTarget.Rendering` 同步；抽屉网格居中零白线。
- **核心脚本与红线**：构建 `packaging\Build-Product.ps1`，热重载 `Reload-Dev.ps1`；保持 `1.9.9` 版本号，严禁改动 `DockMotion.cs` / `WakeStrip.cs` / `Genie.cs` 动画曲线与抽屉底边零白线。
