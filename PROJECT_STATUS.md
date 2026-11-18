# MacDock 1.9.9 项目状态

正式版本 `1.9.9` 已完成最终构建、签名、本地覆盖部署与 Release 发布准备。保留高刷运动曲线、8 列居中、Genie 底边无白线、实时玻璃及系统拖拽图层；Dock 固定图标始终单击启动、抽屉遵循用户设置的单击/双击模式；抽屉滚动条在右侧留白中轴居中，右下角 `+` 按钮与最右侧图标列右边缘对齐；自动更新支持版本递增与同版本修订检测，采用构建序号、构建指纹、SHA-256 与 P-256 签名验证及防降级拦截。

## 源码地图

- Program / Models 管理配置、单实例、桌面镜像、图标队列及窗口状态。
- DockWindow / DockMotion / WakeStrip 管理磁贴、波浪和唤醒。
- DrawerWindow / DrawerScrollViewport / Ui / Layout 管理抽屉、滚动和 DPI。
- Genie / HostGlass / RestShadow / NativeContour / CoverageBorder 管理变形、材质、阴影与圆角。
- IconResources / Icons / Native / PointerSort / ShellOps 管理图标、系统输入、拖放与文件操作。ReleaseUpdates 验证更新来源。

## 构建、发布与保护

构建脚本 `Build.ps1` / `packaging/Build-Product.ps1`，自检 `Test.ps1`，清单签名 `packaging/New-UpdateManifest.ps1`。正式部署使用同一份最终验收载荷覆盖 `release\MacDock\MacDock.exe`，保留 `%LOCALAPPDATA%\MacDock` 配置与工作区外备份。源码同步至私有 `bieshuowola/MacDock-source` 与公开 `bieshuowola/MacDock`（各自保留提交历史），配置、私钥、备份与环境证据仅留本地。
