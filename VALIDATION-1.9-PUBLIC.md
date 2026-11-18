# MacDock 1.9.0 正式发布验收

2026 年 11 月 11 日，正式稳定版在 `bieshuowola/MacDock` 发布。应用源码提交为 `a779207`。公开仓库具有独立历史，仓库树只包含 README、使用指南和版本日志。Release 资产是 Windows x64 安装包、便携包、SHA-256 清单、更新清单与其签名。私有源码、开发历史、用户配置和诊断文件留在本地。

## 任务栏交互

旧构建上，Steam 跳转列表和运行窗口缩略图都能覆盖唤醒区域。指针停在真实的 Shell 弹层上时，旧判定仍满足唤醒坐标条件并打开 Dock。修复后，唤醒及初次按键路由检查指针下窗口的原生任务栏归属，并在可见任务栏弹层存在期间让出唤醒。触发区域及原有确认时间保持原值。已由 MacDock 接收的成对按键继续沿原路径完成。

- Windows 桌面实测 Steam 图标右键跳转列表、悬停缩略图、任务栏空白处右键以及弹层打开时移向唤醒带。Dock 保持隐藏。按 Esc 关闭系统菜单后，同一位置可以重新唤醒。证据为 `test-results/taskbar-wake-origin-taskbar-wake.json` 与 `test-results/taskbar-wake-after-taskbar-wake.json`。
- 同一正式构建验证了 Windows 任务栏按钮最小化和恢复测试窗口，以及真实 `XamlExplorerHostIslandWindow` 缩略图点击后恢复窗口、焦点回到对应窗口。通用测试脚本按旧窗口类型查找右键和悬停弹层，两项分类检查未命中，弹层归属与 Dock 隐藏以 Steam 专项记录为准。证据为 `test-results/product19-final-taskbar-click.json`。
- 最终频道构建 GUI 回归通过 160 项。无边框全屏代理、任务栏露出与 Alt+Tab 33 项，跨组件交互 66 项，抽屉按钮 31 项，过渡 20 项，显示变化 8 项，菜单 2 项。证据为 `test-results/product19-channel-gui-summary.json`。

## GitHub、安装和更新

- 本机 `192.168.1.189` 为网卡地址。GitHub API 直连可达，OAuth 换取令牌的 POST 直连超时，经现有 `127.0.0.1:7897` 代理可达。仅对授权及后续发布进程设置代理。设备授权完成后，GitHub CLI 读取 Windows 凭据库中的登录。
- 先通过公开预发布测试匿名下载与 ECDSA 签名校验，并用同一安装包验证真实安装、从 1.8.9 升级、路径冲突回滚、同版本修复、启动、卸载和保留配置重装，共 8 项。证据为 `test-results/product19-online-pilot.json` 与 `test-results/product19-install-4707fa51/install-checks.json`。
- 晋升正式稳定版后，生产更新模块从真实 `latest` 频道识别新版、验证 P-256 清单及安装包哈希。匿名下载的安装包和便携包与本地构建哈希相同。实际 1.9.0 窗口从托盘点「检查更新」，确认同版本不会再次提示。证据为 `test-results/product19-online-stable.json` 与 `test-results/product19-stable-app-check.json`。发现新版与界面同步还用受控签名响应测试过。发现新版、校验下载和安装重启分段验证，未在旧版界面中把全部步骤串成一次升级。
- 更新默认开启，启动后约 3 分钟检查，距上次成功满 24 小时才再进行自动检查。正常部署的 1.9.0 在启动后 183.34 秒自动检查成功，未重复提示同版本，见 `test-results/product19-formal-auto-check.json`。失败退避，托盘与设置使用同一状态，用户确认后下载及安装。便携版可检测新版，更新时按指南替换。跨天和退避按测试时钟验证。安装包没有 Authenticode 证书，Windows 可能显示 SmartScreen 提示。程序内下载采用独立的已嵌入公钥与签名清单验证。
- 真实首次启动与覆盖升级使用当前 Windows 用户的隔离 MacDock 配置。未在独立 Windows 用户重验桌面空间顺序。也未实测系统任务栏自动隐藏、独占全屏游戏、实体混合 DPI 多屏、网易 UU、休眠、Explorer 重启及多小时常驻。

## 本机与回滚

正式程序部署在 `release/MacDock/MacDock.exe`。启动后仅有一个正常 1.9.0 进程，既有配置文件字节一致，四个固定项目与顺序一致。见 `test-results/product19-formal-deployment.json`。

回滚时退出 1.9.0，从 `backup/product19-20261110-205642/runtime-1.8.9-before-1.9.0` 启动 1.8.9，或将完整目录恢复至 `release/MacDock`。备份的配置在同目录下 `profile-1.8.9`，通常无需恢复配置。后续构建、更新签名、审核和发布步骤见 `packaging/RELEASING.md`。
