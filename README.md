<div align="center">

# MacDock

**不是又一个快捷方式启动器，而是 Windows 11 的真实桌面镜像与 macOS 原生毛玻璃 Dock**

[![Release](https://img.shields.io/badge/release-v1.9.8-0078D4?style=flat-square)](https://github.com/bieshuowola/MacDock/releases/latest)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20x64-lightgrey?style=flat-square)](https://github.com/bieshuowola/MacDock/releases/latest)

[下载最新版](https://github.com/bieshuowola/MacDock/releases/latest) · [使用指南](GUIDE.md) · [版本记录](CHANGELOG.md)

<br/>

![MacDock 的玻璃桌面抽屉与 Dock](drawer.png)

</div>

---

## 核心特性

### 1:1 真实桌面双向镜像
MacDock 抽屉直接映射你的 Windows 真实桌面目录，而不是维护一份割裂的静态快捷方式列表——你在抽屉中的任何操作都与 Windows 桌面实时双向同步：
- **Drop-to-Exe 带参运行**：直接将任意文件（例如游戏 MOD 源文件）拖拽到抽屉内的 `.exe` / `.bat` / `.cmd` / 快捷方式图标上松开，即可以该文件完整路径作为命令行参数直接启动处理工具。
- **双向拖拽移动与归档**：支持拖入抽屉文件夹移动（按住 `Ctrl` 复制）、从抽屉直接拖出到微信/浏览器/编辑器等外部应用窗口，或从外部拖入文件直接落盘桌面。
- **原生键鼠与外壳菜单**：完整支持 `F2` 重命名、`Delete` / `Shift+Delete` 删除、`Ctrl+C` / `Ctrl+X` / `Ctrl+V`、`Alt+双击` / `Alt+Enter` 打开属性页，以及右键零延迟秒开 Windows 原生扩展菜单（含压缩工具、系统功能与第三方外壳扩展）。

### 高仿 macOS Genie 神奇效果与波浪物理动效
- **Genie 神奇过渡**：点击四窗格抽屉按钮或按下 `Ctrl+Alt+Space`，抽屉沿流线型漏斗曲线从图标锚点平滑涌出与吸入，支持动画中途可逆反向。
- **Dock 悬停波浪放大**：指针移近底部触发柔和放大曲线与相邻图标联动推挤，支持“单击打开 / 双击打开”自由切换。
- **图标防白板快照保护**：引入 Stale-While-Revalidate 缓存与多尺寸/StockIcon 回退机制，桌面刷新或壳层重载期间不再出现空白图标闪烁。

### 智能场景感知与按键防误触屏蔽
- **桌面常驻 + 最大化/全屏自动避让**：空桌面或普通窗口状态下 Dock 自动常驻，出现最大化或全屏程序覆盖工作区时自动平滑收起，移动指针至底部唤醒条随时唤出。
- **自定义按住屏蔽键**：在设置中绑定任意键盘按键（如 `~`），按住期间立即收起 Dock 并拦截底部唤醒，松开自动恢复，游戏或全屏编辑不再误触。

### 轻量零侵入
- **秒级开机自启**：优先注册当前用户级 `AtLogOn` 登录计划任务，绕过 `HKCU\...\Run` 系统启动推迟。
- **原生共存**：不修改或替换系统核心文件，Windows 任务栏、跳转列表与缩略图预览照常工作。

---

## 动效展示

| Dock 悬停波浪放大 | Genie 展开瞬间 |
| :---: | :---: |
| ![Dock 悬停放大](dock.png) | ![抽屉从 Dock 四窗格按钮上方展开的实拍截帧](genie.png) |

---

## 下载与更新

| 版本 | 适用场景 |
| --- | --- |
| **Windows 安装版 (`Setup.exe`)** | 日常使用。当前账户目录安装，支持应用内签名清单校验升级并保留现有排列与配置。 |
| **Windows 便携包 (`Portable.zip`)** | 解压即用，更新时从发布页下载替换程序即可。 |

- 运行环境：Windows 11 x64（已内置 .NET 8 运行时，无需额外安装）。
- 请从 [GitHub Releases](https://github.com/bieshuowola/MacDock/releases/latest) 获取正式包并核对 `SHA256SUMS.txt`。
- 桌面文件、应用列表和配置全部留存本机。[隐私与兼容性说明](GUIDE.md) · [反馈问题](https://github.com/bieshuowola/MacDock/issues)

<details>
<summary>安全与签名说明（含历史 1.9.2 Defender 误报记录）</summary>

- 正式安装包与主程序尚无 Authenticode 代码签名，Windows 初次运行可能提示 SmartScreen 未知发布者警告。
- 历史 1.9.2 安装包曾在另一台 Windows 笔记本上触发 Microsoft Defender 的 `Trojan:Win32/Sabsik.TE.A!ml` 检测，该版本继续撤回（微软复核已提交，结果仍为 Pending）。后续版本发布前均在本地执行 Defender 定向扫描未检出，但不能保证所有设备或云端策略必然放行。请保持系统安全防护开启并核对 `SHA256SUMS.txt`。

</details>
