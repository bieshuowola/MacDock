# 1.9.5 接续检查点（未验收，不发布）

后续状态更新：已继续完成原生环境诊断和布局恢复。开发宿主的 MSIX AppData 重定向导致此前读取另一份配置；通过实际文件句柄确认，并已增加加载/写入保护。正常 Windows 环境下硬件渲染、快捷键及圆角修复已复核，当前结果见 DELIVERY-1.9.5.md。以下内容保留为最初中断时的历史记录，不作为当前发布状态。

2026-11-12。基线dd965c1，正式程序仍1.9.4，本次尚未生成/部署/公开发布1.9.5。GitHub最新仍v1.9.4。禁止把本检查点当作已完成版本。

## 已完成与实证

- 开始前真实检测DISPLAY1、2560×1440、165Hz；未检测到活动UU虚拟屏。备份位置见.scratch/backup195-path.txt（backup/pre-1.9.5-20261112-100051，含runtime-1.9.4及完整profile）。
- 快捷键缺口：DrawerWindow文件快捷键仅读取Selection；PaintTile的IsMouseOver高亮没有形成选择，因此仅悬停没有操作目标。显式选择后的文件操作原来正常，不是整个键盘系统损坏。
- 局部候选：显式选择（含多选）优先，没有选择时使用当前可见悬停tile。Delete/F2在按下时捕获目标，释放时执行，保持原Shell操作及确认，不模拟额外点击。搜索Editing提前返回，文本编辑不触发文件操作。没有修改单击打开和选择交互。
- 原基线ExperienceChecks真实键盘文件操作通过；22项共19通过，另3项唤醒/波浪可视状态失败，不能称全套通过。记录test-results/195-baseline-experience-test。
- 新增仅悬停用例，以真实鼠标位置及Native.TestKey的系统键盘输入执行文件操作。测试只使用MDQA前缀专用临时文件，finally清理；用户文件未修改。隔离测试使用软件渲染后25项全通过，包括悬停复制、剪切（文件仍在）、F2真实重命名、Shift+Delete系统确认/取消/确认删除、Delete回收快捷方式且保留目标、粘贴、搜索文本编辑、键盘多选及菜单。记录test-results/195-hover-software-experience-test。这不能替代硬件动画验收。

## 当前实机阻塞：独立WPF硬件渲染也为空白

本轮多个截图只有Windows Composition玻璃层，没有WPF图标/文字，连WPF测试背景也未呈现。已用完全独立的.scratch/RenderProbe195.ps1排除MacDock业务逻辑：默认硬件路径只有白色空窗口，设置仅该探针进程SoftwareOnly后黑底白字正常。

- test-results/195-render-False.png：独立WPF默认硬件路径空白。
- test-results/195-render-True.png：同一探针软件路径正常。
- test-results/195-real-full.png、195-baseline-visible-product-preview/drawer.png：正式/基线程序也只有原生玻璃层。
- 未发现当前MacDock进程注入GPP64.dll，未修改游戏加加或系统图形配置；近期System 4101查询未得到事件。尚不能断言是驱动、远程或特定系统组件的唯一根因。
- 移除测试Start-Process的Hidden选项并未解决，相关失败对照保留。不继续重复同类实验。

Program.cs仅MACDOCK_EXPERIENCE_CHECKS构建中新增MACDOCK_QA_SOFTWARE_RENDER诊断开关，且仅testing模式允许。正式构建没有该路径；未把正式程序改成软件渲染。软件诊断圆角图像在195-software-diagnostic-corner-test；不可用其证明硬件圆角或Genie性能达标。

旧CornerChecks背景Topmost会干扰分层对照，本轮将背景置于普通层、增加终态settle并使用黑底。最初硬件空白导致的corner计数不可信，不据此宣布修复。未对产品圆角半径、模糊、阴影或抗锯齿作无证据修改；1.9.4渲染实现保持原样。

## 继续顺序

1. 请用户保存其他工作后恢复Windows图形环境（已异步提出重启请求，不擅自重启）。恢复后先执行独立默认硬件探针确认文字实际显示，再核实实体屏。不要把系统硬件路径故障归为圆角根因。
2. 使用隔离配置实测圆角四角、纯黑/浅色/深色、原生截图分层，并与历史正常实现对比，定位真实视觉回归后最小修复。不得用软件渲染替代正式性能验收。
3. 对候选执行原25项快捷键实机回归，检查真实GUI确认及文件结果；使用实际硬件完成Genie阴影同步、反向和短冷态必要回归，不重开长性能追踪。
4. 两项均通过后再递增版本1.9.5、按现有Build-Product流程构建、扫描、安装升级、部署及公开发布。复用1.9.4签名密钥与现有公开资料分发仓库，私有源码不公开。微软既有Pending不阻塞，但真实安全检出必须停止。

运行/配置最终状态见test-results/195-checkpoint-state.json。保留现有正式程序、用户配置和历史备份。当前不具备发布完成结论。
