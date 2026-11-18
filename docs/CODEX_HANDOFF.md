# MacDock 接续检查点 · 2026-11-18

项目目标是在 Windows 11 上维护轻量 Dock 与真实桌面抽屉，保留原生任务栏、Explorer 和现有布局。工程使用 .NET 8 / WPF，模块职责与构建入口见 [PROJECT_STATUS.md](../PROJECT_STATUS.md)。下方 Antigravity 章节为历史快照，其中的分支、文件数和待办以本节为准。

## 已复核

- 完整工作区在 `D:\Windows Dock\MacDock`。本次接管时 `main` 为 `9f1c17b`，工作树干净。正式文件 `release\MacDock\MacDock.exe` 的版本为 1.9.9，DLL SHA-256 与 `test-results/199-takeover-deployment.json` 一致。
- 上轮部署记录确认真实桌面单实例、快捷键打开抽屉、重启前后配置哈希一致及 9 个固定项。本次仅核对文件和记录。当前命令环境未枚举出进程，实时运行状态待下次交互桌面核实。上轮重启使用了进程控制，托盘退出仍需专项验证。
- 保存的报告逐项核对通过：自检 83 项、连续排序 86 项、拖后输入 28 项、原生菜单 38 项。正式布局回读和 165Hz 滚动证据见 [验收清单](DESKTOP_INTERACTION_ACCEPTANCE.md)。WPF 回调间隔、DXGI 录制时间戳与实际面板呈现须分别解读。
- 上轮同步回读记录为私有 `MacDock-source/main` 的 `e2c763a` 和公开 `MacDock/main` 的 `5335cc9`。公开仓仅同步已有公开组件及说明，v1.9.9 附件哈希一致。本次复核使用保存的同步记录。

## 下一轮入口与保护

- 等待用户指定开发任务。若优先补验，先查托盘退出与焦点交接，再按任务覆盖混合 DPI、UU 超级屏、实际默认文件关联变更及第三方拖放。需要进一步比较动画时，补充可靠的 Present 采样。
- 继承 Genie、玻璃、阴影、圆角、波浪曲线、8 列布局、系统拖拽 Z 序与更新验签。源码与文件任务用原生工具；真机操作优先用本地桌面工具的 UI Automation，坐标输入仅作补充，桌面操作串行。
- 验收使用隔离配置和专用文件，截图存入验收目录。部署前读回真实配置路径、固定项与排序，保留裸备份及 Git 历史。接管前回滚材料在 [备份目录](../backup/takeover-199-20261118-102955/)，入口为 `Rollback.ps1`。先用独立目录构建与验收，再更新正式程序。

## 上轮接管的实现

复核并保留反重力阶段的高刷节拍、波浪与 Genie 曲线、8 列居中、抽屉底边和系统拖拽缩略图顺序。桌面配置沿用原数据和路径。历史交接全文附后。

- DrawerWindow、DrawerScrollViewport、Ui 将滚轮合并到单一渲染订阅，高频滚动只平移既有磁贴并废止快照，停止后才预烘焙。开关和内容刷新按项目 ID 与行内偏移恢复，过滤清空恢复未过滤位置。滚动条与添加按钮对齐，移除右上角叉号。
- Program 与 ModalInputGate 使用可释放的临时保持状态保护 Shell 文件操作和框选，ShellOps 在 STA 完成删除、改名和复制移动，保留原生确认。外部 CF_HDROP 依数据与修饰键分流，默认安全复制，Shift 移动交给 Shell，冲突自动改名。Alt 打开与拖放层级沿用已有逻辑。
- Program 与 IconResources 在图标线程读取文件、快捷方式、关联指纹，按失效对象刷新已存在的图标槽。缓存写入做版本校验，滚动和转场期间延后非关键和解工作。
- 测试、基线数据和已知未覆盖场景见 [桌面交互覆盖与验收清单](DESKTOP_INTERACTION_ACCEPTANCE.md)。真实桌面 165Hz 测试与可控夹具分开记录。完整源码和内含环境信息的证据分别留在私有仓库及本地。

# MacDock 技术交接文档（Antigravity → Codex）

> **交接基准时间**：2026-11-18 08:45 (UTC+8)<br>
> **精简入口**：[PROJECT_STATUS.md](../PROJECT_STATUS.md)<br>
> **历史基线参考**：[HANDOFF.md](../HANDOFF.md)（v1.9.7 及更早历史背景）

---

## 1. 当前三态严格对照（源码 vs 本地构建 vs GitHub Release）

| 维度 | 当前实际状态（已核实） | 说明与差异 |
|---|---|---|
| **本地工作区源码** | `D:\Windows Dock\MacDock\`，版本常量 `1.9.9`（[MacDock.csproj](../MacDock.csproj)） | **唯一完整私有源码所在地**。除交接文档外，`README.md` 本地与远端 `main` 存在未同步差异；其余已推送到 `main` 的 27 个文件与远端一致。 |
| **GitHub 远程仓库** | `bieshuowola/MacDock`，唯一分支 `main`，代码基准提交 `bbda3b4dd0c51701c6e96d2c6ea536bffd3e8ffe`（含本文档推送后为新 HEAD） | **注意**：公开仓库 `main` 仅追踪 **27 个文件**（文档、部分核心 `.cs`、`packaging/Build-Product.ps1`、演示媒体），`.csproj`/`Build.ps1`/`Icons.cs`/`Layout.cs` 等大多数工程文件仅存本地，未上传公开仓。 |
| **本地正式部署** | `D:\Windows Dock\MacDock\release\MacDock\MacDock.exe` | 版本 `1.9.9`（win-x64 self-contained ReadyToRun），构建于 `2026-11-18T08:34:24+08:00`（[BUILDINFO.json](../release/MacDock/BUILDINFO.json)），`MacDock.dll` SHA-256 `AA9EED47DBDD5A8778458D216D92CC78F31DEF5A5D1F274B469B186BF88D3764`，当前已启动运行。 |
| **GitHub Release** | Tag `v1.9.9`（Release ID `447725438`，非 Draft/Prerelease） | 安装包 `MacDock-1.9.9-win-x64-Setup.exe`（61,753,392 B）、便携包 `MacDock-1.9.9-win-x64-Portable.zip`（87,078,781 B）及 `SHA256SUMS.txt`（359 B）均于 `2026-11-18T00:37~00:38Z` 覆盖上传，**包含 `bbda3b4d` 最新构建产物**。 |

---

## 2. 近两天开发增量（2026-11-17 ~ 2026-11-18）与验收边界

### 2.1 提交增量清单（`main` 分支按时间顺序）
1. **Dock 圆角与阴影管线（`00089a27` ~ `a089313f`）**：引入逐像素 Alpha 贴片 `CoverageBorder.cs`，重构 `DockWindow`/`RestShadow` 渲染管线，新增 `--dock-stability-test`。
2. **Genie 动画 165Hz 与底边零白线（`c124d2ad` ~ `b7d52e11`）**：
   - `NativeContour.cs` / `HostGlass.cs` 同步 `HostGlass` 变形帧与 `SoftContour` 底边对齐，修复弹簧过冲时轮廓脱节。
   - `Genie.cs` / `DrawerWindow.cs`：通过 `shareShadow=false` 与 `stage.Clip` 消除 `Viewport2DVisual3D` 底边白线；第 0 帧预烘焙静态纹理（`BeginGenie` 复用、`EndGenie` 释放）；引入 Win32 `DeferWindowPos` 批量异步同步与动画期 `timeBeginPeriod(1)` + `SustainedLowLatency`。
3. **抽屉对称居中、原生拖拽预览与 Z 序修复（`abcd8cf3` ~ `2d35ee1f`）**：
   - 监听 `DpiChanged` 与 `DisplaySettingsChanged`，失效 `cachedDpiScale` 并重新居中 `DrawerWindow` 网格。
   - `Native.cs` 声明 `IDropTargetHelper`，`DockWindow`/`DrawerWindow` 转发 `DragEnter/Over/Leave/Drop` 保留原生缩略图。
   - 修复 `PointerSort` 预览窗与 `HostGlass`/`RestShadow` 的 Z 序竞态及拖拽松手误触“打开方式”问题。
4. **外部文件拖入抽屉与高刷动画同步（`9f8450d6`）**：
   - 外部 `FileDrop` 拖入 Dock 默认安全复制存入抽屉（保留源文件、`FOF_RENAMEONCOLLISION`、提示条置于系统预览图下并高亮抽屉入口）；仅按住 `Alt` 拖到应用图标中心时触发打开/移入。
   - `DockMotion.cs` 升级为 `CompositionTarget.Rendering` 帧同步（保留原弹簧阻尼/曲线），减少动画期堆分配并延后 `CheckFullscreen`。
5. **抽屉图标全场景同步（`bbda3b4d`）**：
   - `DrawerWindow.Render()` 复用磁贴时比对 `iconHost.Tag` `(e.Path, l.DrawerIcon)` 重绑定 `Icons.View`。
   - `IconResources.cs` 将文件 `Attributes`、`desktop.ini` 元数据、`.lnk` 目标 `FileStamp` 及 5 秒缓存的 `UserChoice`/HKCR 关联纳入 `Stamp(path)`。
   - `Program.cs` 增加 80ms 单路径防抖（动画及 `Ui.SmoothScrollViewer.IsScrolling` 期间延后）、`SHCNE_UPDATEIMAGE`/`WM_SETTINGCHANGE` 250ms 全局无闪烁重校验及 15s/60s 低频一致性 `ReconcileIcons()`。

### 2.2 已验证 vs 未充分验证矩阵
- **已通过自动化 E2E（2026-11-18 交互式桌面 `schtasks /IT` 运行，全绿 `ExitCode=0`）**：
  - `--icon-test`（DPI 图标与高对比度离屏自检）
  - `--drawer-paste-reuse-test`（抽屉粘贴复用磁贴与 `ImageSource` 缓存、生成截图）
  - `--drawer-stability-test`（`2560x1392@165Hz`，8 列，左右对称差 `asymPx=1.00`，底边零白线检测，开/关均 >154fps）
  - `--dock-stability-test`（`CompositionTarget` 高刷，往返打断 `reversalOk=True`）
  - `--external-drop-pin-test`（外部拖入默认入抽屉、Z 序采样、框选与固定）
  - `--icon-sync-test`（隔离桌面 `.txt -> .vbs`、`.lnk` 换标、`desktop.ini` 自定义文件夹图标、`SHCNE_ASSOCCHANGED` 无闪烁）
- **尚未完成实机直连或存在验证缺口**：
  - 所有上述 11/17~11/18 改动均在 **UU 远程桌面虚拟屏**下通过自动化脚本验证，**尚未经过物理显示器直连实机手感与视觉验收**。
  - 实体混合 DPI 多显示器、休眠/唤醒长稳、独占全屏游戏、跨机 Defender 误报复核均未重测。

---

## 3. 版本演进、隐性状态机与边界保护机制

1. **真实桌面单源镜像边界**：抽屉镜像真实用户桌面、公共桌面及启用的特殊 Shell 对象，并非独立虚拟副本；`解除 Dock 固定` 仅修改固定关系，不删磁盘文件。首次初始化读取 Explorer 桌面图标坐标按**列优先（列内自上而下、列间从左到右）**排布；此后尊重用户持久化顺序，Explorer 未就绪或屏幕返回零宽时禁止覆盖已有布局。
2. **双路径防护与配置保护**：配置目录 `%LOCALAPPDATA%\MacDock`；强制校验真实磁盘落点防开发宿主 AppData 重定向，保留布局历史与保存读回校验。
3. **Genie 与阴影状态机**：支持连续点击与中途反向打断；`HostGlass`、`RestShadow`/`NativeContour` 与 `Genie` 共享同一动态轮廓几何，不可退回独立静态矩形。
4. **Dock 唤醒与全屏避让**：底边白条（`WakeStrip.cs`）与 Windows 原生任务栏存在约 1px 边界邻接，通过微驻留与意图过滤避免吞掉任务栏跳转列表/右键；独占全屏时自动避让；动画运行中延后 `CheckFullscreen`。
5. **键盘与焦点层级**：`F6` 切换 Dock/抽屉键盘模式（Dock 模式 `Delete` 为取消固定，抽屉 `Delete`/`Shift+Delete` 为真实删除并保留原生确认）；无显式选中时回退悬停目标；搜索框聚焦时文本编辑优先；原生 Shell 右键菜单通过 `BeginNativeMenu/EndNativeMenu` 防止焦点抢占闪没。
6. **拖放状态机**：内部拖拽由 `PointerHook` + `PointerSort` 协调；外部 `FileDrop` 默认复制存入抽屉（支持 `FOF_RENAMEONCOLLISION` 与逐项降级），仅按住 `Alt` 悬停目标中心进入应用打开/移入模式。
7. **自更新验签机制**：通过 `ReleaseUpdates.cs` 拉取并校验 ECDSA P-256 签名清单 `macdock-update.json/.sig`（**注意**：非 Windows Authenticode 签名，安装器本身无数字签名）。

---

## 4. 核心源码与架构地图

### 4.1 关键源码职责
| 模块 | 核心文件 | 职责说明 |
|---|---|---|
| **主控与生命周期** | `Program.cs` | 单实例、托盘/菜单、热键、`Messages` 窗口消息分发、桌面监视与 `StartWatchers`/`RegisterShellChanges`、图标异步队列/防抖/和解、测试 CLI 路由 |
| **Dock 栏与交互** | `DockWindow.cs`, `WakeStrip.cs`, `DockMotion.cs` | Dock 磁贴构建、波浪放大物理曲线（`CompositionTarget.Rendering`）、底边唤醒条、外部拖放命中与“放入抽屉”提示 |
| **抽屉与布局** | `DrawerWindow.cs`, `Layout.cs`, `Ui.cs` | 抽屉分组/网格/列表渲染、磁贴增量复用、`SmoothScrollViewer` 滚动、`ApplyLayout` DPI/列宽居中、预烘焙纹理缓存 |
| **Genie 与合成特效** | `Genie.cs`, `HostGlass.cs`, `RestShadow.cs`, `NativeContour.cs`, `CoverageBorder.cs` | 3D 漏斗变形、`HostBackdropBrush` 实时玻璃、GPU 高斯轮廓阴影、局部 DPI 圆角 Alpha 贴片、`DeferWindowPos` 同步 |
| **桌面镜像与 Shell** | `Desktop.cs`, `DesktopView.cs`, `Models.cs`, `ShellChanges.cs`, `ShellMenu.cs`, `ShellOps.cs` | 桌面枚举与 Explorer 坐标读取、`Store` 数据模型与 `Repoint`、Shell 变更通知、原生右键菜单与 `IFileOperation` |
| **图标与底层互操作** | `IconResources.cs`, `Icons.cs`, `Native.cs` | 文件/快捷方式/`desktop.ini`/注册表关联指纹 `Stamp`、SHA-256 `Fingerprint` 零闪烁比对、Win32/COM（含 `IDropTargetHelper`）声明 |
| **输入与拖拽排序** | `PointerHook.cs`, `PointerSort.cs`, `ModalInputGate.cs` | 低级鼠标钩子、跨组件拖拽幽灵窗与 Z 序置顶、模态弹窗输入门控 |
| **设置、更新与测试** | `Dialogs.cs`, `ReleaseUpdates.cs`, `ExperienceChecks.cs`, `Tests.cs`, `UpdateChecks.cs` | 设置窗口、签名清单更新校验、全套自动化 E2E 与单元测试 |

### 4.2 核心事件与状态流转链路
- **输入唤醒链路**：`PointerHook` / `WakeStrip` → `Program` 命中与全屏检查 → `DockMotion` (`CompositionTarget.Rendering`) → `DockWindow` / `HostGlass` / `RestShadow` 批量提交（`DeferWindowPos`）。
- **抽屉开关链路**：`Ctrl+Alt+Space` 或 Dock 入口点击 → `DrawerWindow.PrepareForOpen()` → 预烘焙静态纹理 → `Genie` 帧驱动同步轮廓 → 结束释放烘焙缓存并切回实时交互树。
- **桌面与图标同步链路**：`FileSystemWatcher` / `ShellChanges` / `WM_SETTINGCHANGE` / `iconReconcileTimer` → `Store.Sync` / `Repoint` + `ScheduleIconPathRefresh` (80ms) / `ScheduleGlobalIconRevalidate` (250ms) → `iconDispatcher` 后台提取 → `IconEntry.Update` (`Fingerprint` 变更才触发 `Changed` 事件) → `DrawerWindow.Render()` 局部更新。

---

## 5. 尚未解决的问题、回归风险与待核实清单

> **Codex 接手后须优先排查以下已知未解决缺陷与核实项：**

1. **【未解决】抽屉内部滚动明显掉帧**
   - **现状**：`DrawerWindow.cs` 中图标分组直接挂在非虚拟化的 `WrapPanel` 下，配合 `Ui.SmoothScrollViewer` 滚动与 `HostGlass` 实时透明合成，桌面项目较多时滚动帧率明显下降。目前无针对滚动帧率的自动化 E2E 指标。
2. **【未解决】抽屉关闭再打开后无法恢复上次滚动位置**
   - **现状**：`DrawerWindow.cs:686-687` 的 `Prepare()` 与 `PrepareForOpen()` 每次打开抽屉都会硬编码调用 `scroll.ResetScroll()`，导致上次浏览滚动位置丢失。
3. **【待再次核实】文件扩展名、文件类型及快捷方式变化后抽屉图标同步可靠性**
   - **现状**：虽然 `bbda3b4d` 已合入修复并通过 `--icon-sync-test`，但在实际桌面复杂环境下（如特定软件修改默认打开方式、`desktop.ini` 延迟写入、第三方 Shell 图标叠加、快捷方式目标变更后图标缓存未即时刷新），**未必在所有用户真实场景下都能及时同步**，需 Codex 重点在真机环境排查边界。
4. **【验收缺口】近期改动的真实物理屏验收**
   - 外部文件拖放（资源管理器拖入各类文件/快捷方式/多选、`Alt` 二级打开）、144Hz/165Hz 动画顺滑度、图标同步均**缺乏物理显示器直连人工验收**（此前均为 UU 远控环境测试）。
5. **【环境与安全遗留风险】**
   - 未验证显示环境：实体混合 DPI 多屏、低分辨率笔记本、休眠/恢复长期驻留、独占全屏游戏（如反作弊冲突）。
   - 安全与签名：v1.9.2/v1.9.4 曾有笔记本 Defender 误报 `Trojan:Win32/Sabsik.TE.A!ml`；安装包与 exe 仍无 Authenticode 签名。

---

## 6. 稳定基线、红线约束与工具环境铁律

1. **工具与网络环境**：本机**未安装 `git` / `gh` CLI**；本地代理固定为 `http://127.0.0.1:7897`；仓库同步与 Release 操作必须通过 PowerShell + `$GITHUB_TOKEN` 调用 GitHub REST API。
2. **防阻塞与热重载铁律**：
   - **严禁**终端同步直接启动 `MacDock.exe`（会占用 stdout 导致挂起），必须通过 `schtasks /Create ... /IT /F` + `/Run` + `/Delete` 投射到交互式桌面会话（或 `Shell.Application` COM 兜底）。
   - 清理进程严禁阻塞 `taskkill`，必须用 `Stop-Process -Name "MacDock" -Force -ErrorAction SilentlyContinue`。
   - 部署本地版本严禁直接跑 `Build.ps1` 撞 DLL 锁，统一通过 `schtasks /IT` 调用 `Reload-Dev.ps1`。
3. **动画、视觉与版本号红线**：
   - 未经用户明确指令，**严禁改动版本号 `1.9.9`**，严禁破坏 `DockMotion.cs`/`WakeStrip.cs`/`Genie.cs` 核心动画曲线，严禁破坏抽屉底边零白线（`shareShadow=false` + `stage.Clip`）与 `CoverageBorder` 局部圆角。
   - **严禁静默弹窗**：自动化测试中涉及 `IFileOperation` 必须保持静默标志（如 `FOF_NOCONFIRMATION`）。
   - **严禁虚报**：未在真实环境验证过的功能不得写成已完成；交付前必须编译 0 错 + E2E 断言通过 + 视觉截图留证。

---

## 7. 构建、测试、热重载与发布脚本清单

| 用途 | 完整路径 / 命令 | 说明 |
|---|---|---|
| **本地构建部署** | `D:\Windows Dock\MacDock\Build.ps1` | 发布 `win-x64` ReadyToRun 到 `release\MacDock\` 并生成 `BUILDINFO.json`（运行中禁止直接调用） |
| **本地热重载** | `D:\Windows Dock\MacDock\Reload-Dev.ps1` | 停旧进程 → 等 DLL 解锁 → 调 `Build.ps1` → 异步拉起新版（必须经 `schtasks /IT` 调用） |
| **基础自检脚本** | `D:\Windows Dock\MacDock\Test.ps1` | 运行 `--self-test`（加 `-Desktop` 运行 `--ui-test`） |
| **E2E 测试构建** | `dotnet build "D:\Windows Dock\MacDock\MacDock.csproj" -c Release -p:EnableMacDockExperienceChecks=true -p:UseSharedCompilation=false --disable-build-servers` | 启用 `MACDOCK_EXPERIENCE_CHECKS` 编译体验/稳定性 E2E 入口至 `bin\Release\net8.0-windows10.0.19041.0\MacDock.exe` |
| **最近 E2E 套件** | `D:\Windows Dock\MacDock\.scratch\run-all-e2e.ps1` | 串联 `--icon-test`、`--drawer-paste-reuse-test`、`--drawer-stability-test`、`--dock-stability-test`、`--external-drop-pin-test`、`--icon-sync-test` |
| **产品打包脚本** | `D:\Windows Dock\MacDock\packaging\Build-Product.ps1` | 需传入 `-Version "1.9.9" -ReleaseRepository "bieshuowola/MacDock" -UpdatePublicKey "<ECDSA公钥>"`，依赖 `Inno Setup 6\ISCC.exe`，产出 Setup/Portable/SHA256SUMS |
| **发布参考脚本** | `D:\Windows Dock\MacDock\.scratch\publish-release.ps1` | 通过 GitHub REST API（`127.0.0.1:7897` + `GITHUB_TOKEN`）提交 blob/tree/commit 到 `main` 并覆盖 Release ID `447725438` |
| **紧急回滚脚本** | `D:\Windows Dock\MacDock\backup\pre-1.9.8-20261116-200932\Rollback.ps1` | 历史版本本地回滚入口 |
