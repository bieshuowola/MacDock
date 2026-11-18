# MacDock 1.9.2 正式验收

> 历史记录。2026 年 11 月 11 日收到 Defender 拦截报告后，1.9.2 已从公开下载渠道撤下，微软复核待提交。[安全核查记录](SECURITY-1.9.2-2026-11-11.md)

1.9.1 在收到安装程序本地化补充时已正式发布。1.9.2 承接双语安装器，包含 1.9.1 的程序改进和托盘提示。私有源码提交为 `75d39dd`。1.9.1 的原始资产与校验值可通过历史发布页取得。[1.9.2 正式发布](https://github.com/bieshuowola/MacDock/releases/tag/v1.9.2) · [1.9.1 正式验收](VALIDATION-1.9.1-PUBLIC.md)

## 安装本地化

安装程序继续使用 Inno Setup 6.7.4。英文使用编译器的 `Default.isl`，简体中文采用上游同版本的社区语言文件，其 Git blob 为 `d6a11c4490de07dad443ade668289fc954dfa1ed`。构建时核对中文资源覆盖英文文件的全部标准消息键。MacDock 的桌面快捷方式、登录启动和完成页启动选项使用自定义双语文字。[上游简体中文资源](https://github.com/jrsoftware/issrc/blob/is-6_7_4/Files/Languages/Unofficial/ChineseSimplified.isl) · [Inno Setup 语言选择](https://jrsoftware.org/ishelp/topic_languagessection.htm)

使用最终安装包在 Windows 图形界面完成 26 项检查。中文 Windows 自动预选简体中文，语言选择页可手动切换 English。两种语言分别完成首次安装、从 1.9.1 覆盖升级、安装完成页、卸载确认及完成。路径冲突测试触发了真实错误提示，取消后验证 1.9.1 仍注册且原程序哈希一致。两种语言的成功升级和卸载均保留用户配置。记录为 `test-results/product192-localization-14116b/installer-language.json`。

英文界面在同一台中文 Windows 上切换验证。Windows 原生消息框按钮和系统错误详情使用 Windows 自身的界面语言。在这台电脑选择英文后，可见英文安装器文案及个别系统提供的中文文字。独立英文 Windows 的自动选择未实测。[Inno Setup 原生消息框说明](https://jrsoftware.org/isfaq.php)

## 发布与本机

正式渠道从 1.9.1 发现 1.9.2，验证签名并匿名下载真实安装包。1.9.2 检查时不会再次提供同版本。匿名下载的便携包也通过哈希核对。证据为 `test-results/product192-online-stable.json`。覆盖安装和故障回滚使用真实安装器验证。1.9.0 从托盘确认并完成在线升级到 1.9.1 的证据在上一版验收记录。

本机正式运行路径为 `release/MacDock/MacDock.exe`。部署后核对仅有一个运行实例，配置文件字节一致，四个固定项目与顺序一致。Windows 报告托盘图标名称为 `MacDock`，独立的悬停提示气泡未在截图中捕获。托盘检查更新显示正式频道的最新版结果，Esc 可关闭窗口，真实 `Ctrl+Alt+Space` 按键可打开抽屉。设置与托盘状态同步通过前版专项，最终设置入口的界面自动化复测未能定位控件。证据为 `test-results/product192-deployed.json`、`test-results/product192-live-update-ui.json` 与 `test-results/product192-live-hotkey.json`。原 1.9.0 程序与配置保存在 `backup/pre-1.9.1-20261111-041020`。
