# MacDock 状态档案 (v1.9.9)
- **基线**：`1.9.9`（Release `447725438`）；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1`。
- **最新修复**：
  - **Z序/拖放**：去同线程 `SWP_ASYNCWINDOWPOS`；`TopmostInsertAfter` 使 Dock/Drawer 置于 `ghost` 与 `SysDragImage` 下；`NeedsGhostRaise` 护航预览；`PointerSort`/`DockWindow` 校验 `downScreen`/`moved` 阈值，禁拖拽松手误触打开方式；`DropPreviewHelper.AttachWindow` 统一缩略图与离开清理。
  - **性能/DPI/更新**：预烘焙+零白线+`GridMetrics` 对称（`asymPx=0`）；`Ui`/`HostGlass`/`DockWindow` 同步 DPI；`ReleaseUpdates` 凭 `BUILDINFO.json` 与 `SHA256SUMS.txt` 校验 `MacDock.dll` 修订版。
- **红线**：禁改 `DockMotion`/`WakeStrip`/`Genie`/`ProfileLocation` 及 `1.9.9`。
- **脚本**：`Build.ps1` | 热重载 `Reload-Dev.ps1` | E2E `.scratch\run-e2e-suite.ps1` | 打包 `packaging\Build-Product.ps1`。
