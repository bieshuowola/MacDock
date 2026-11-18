# MacDock 1.9.1 正式验收

正式源码提交为 `7d70db4`，本地与私有远程均保留 `v1.9.1` 源码标签。公开仓库独立保存产品介绍、指南、变更记录和三张以演示项目拍摄的实机图。正式 Release 资产为 Windows x64 安装包、便携包、SHA-256 清单、更新清单及其签名。[公开 Release](https://github.com/bieshuowola/MacDock/releases/tag/v1.9.1)

## 桌面体验

- 专项实机记录通过 22 项。静止指针在唤醒带时，Dock 以统一尺寸展开。1 像素位移保持静止视觉，明显移动可在展开动画结束前激活波浪放大。证据为 `test-results/product191-experience-test-fb5018/experience.json`。
- 同次专项验证了原全局快捷键、抽屉导航与多选、搜索文本编辑、Windows 菜单键、复制剪切粘贴、F2、回收快捷方式、永久删除确认与取消，以及 Dock 的 F6 焦点切换和只取消固定。测试操作仅使用临时文件。游戏加加会在本机接管 Shift+F10，已通过 Windows 菜单键和键盘上下文消息实测同一菜单入口。
- 最终打包构建下，Steam 缩略图、跳转列表、任务栏右键菜单可正常使用。菜单打开期间 Dock 保持隐藏，关闭后恢复唤醒。证据为 `test-results/product191-final-taskbar-wake.json`。
- 候选回归通过设置、跨组件固定、抽屉按钮、动画过渡和菜单等 134 项。无边框全屏代理窗口的任务栏切换后唤醒、以及工作区最下边缘像素唤醒，在同一环境下用 1.9.0 与 1.9.1 对照均出现失败。未将这两项计为通过。证据为 `test-results/product191-channel-summary.json` 与 `test-results/product191-baseline190-summary.json`。

## 更新与发布

- 手动更新窗口通过 10 项专项，覆盖检查中状态、重复请求合并、最新版、Esc、离线、超时、限流、新版详情和当前屏幕居中。离线、超时和限流用受控响应复现。真实 GitHub 稳定频道通过实际更新器匿名下载，安装包和便携包 SHA-256 与本地发布产物相同。证据为 `test-results/product191-update-ui-test-572b8f/update-ui.json` 与 `test-results/product191-online-stable.json`。
- Windows 托盘图标可访问名称核对为 `MacDock`。原快捷键仍为 `Ctrl+Alt+Space`，托盘右键的真实检查入口可打开更新结果。托盘初始化设置提示文字，后续更新提醒使用独立字段。
- 最终安装包通过 8 项安装验收，覆盖首次安装、升级失败回滚、从 1.9.0 升级、同版本修复、启动、卸载和保留配置重装。安装包未做 Authenticode 签名。应用内更新使用已嵌入的 P-256 公钥验证清单，并验证安装包 SHA-256。证据为 `test-results/product191-install-02c2d119/install-checks.json`。
- 用安装版 1.9.0 从托盘发起真实在线更新，确认后验证下载、打开升级向导、完成安装及重新启动。旧版文件与配置均校验一致，6 项通过。证据为 `test-results/product191-live-upgrade-50fc1c/online-upgrade.json`。

## 回滚

`backup/pre-1.9.1-20261111-041020` 保存经校验的 1.9.0 程序、配置与 Git bundle。需要回滚本机运行版本时，退出 1.9.1，使用备份中的 `runtime-1.9.0/MacDock.exe`，或将该完整目录恢复至 `release/MacDock`。原配置通常可直接使用。
