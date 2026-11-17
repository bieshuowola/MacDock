# MacDock 状态档案 (v1.9.9)
- **基线**：`1.9.9`（本地正式部署 `release\MacDock\MacDock.exe`）；回滚 `backup\pre-1.9.8-20261116-200932\Rollback.ps1` / Git `2d35ee1f`。
- **本轮核心修复**：
  - **外部 FileDrop 存入抽屉**：普通拖入 Dock（空白/间隙/图标/抽屉按钮）统一默认安全复制存入抽屉（保留源文件、`FOF_RENAMEONCOLLISION`、单/多文件部分成功逐项降级）；显示“松开放入抽屉”提示（置于 `SysDragImage` 下）并高亮抽屉入口；仅按住 `Alt` 拖到目标中心触发应用打开/移入。
  - **144Hz/165Hz 动画同步**：`DockMotion` 升级为 `CompositionTarget.Rendering`（保留原弹簧阻尼/曲线）；`OnFrame` 去堆分配、`SetWaveBoost`、`DrawerWindow` 复用 `stageClipGeometry`、`Genie` 复用顶点缓冲、动画期延后 `CheckFullscreen`。
- **红线**：保持 `1.9.9` 版本号，物理屏实际呈现待本地直连验收。
