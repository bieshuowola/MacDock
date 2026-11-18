# MacDock 私有构建与公开分发流程

## 仓库边界

源代码默认同步到私有源码仓库与公开分发仓库。公开分发仓库保留自己的 Git 历史，以其现有 `main` 为父提交同步完整可构建源码与发布说明。配置、凭据、签名私钥、内部测试截图、环境专属日志及回滚载荷留在本地。

日常更新沿用 [项目开发约定](../AGENTS.md)。覆盖本机正式程序和现有 Release 的安装包、便携包与 SHA256 校验文件，保留当前版本号、标签及 Release。验证远端附件 SHA256 后记录部署与回滚信息。用户指定其他发布方式时按该轮要求执行。

## 构建

在 Windows x64 安装 .NET 8 SDK 和有权使用的 Inno Setup 6.7.4。使用 PowerShell 7.5 或更新版。

1. 准备长期保管的 P-256 更新签名密钥。私钥留在工作树之外的受控存储。准备 Base64 编码的 SPKI 公钥及已审查的公开仓库 `owner/name`。
2. 运行 `packaging/Build-Product.ps1 -Compiler <ISCC.exe> -Version 1.9.0 -ReleaseRepository <owner/name> -UpdatePublicKey <base64>`。发布目录按构建分开，不覆盖本机正式运行目录。安装包和便携包包含同一份自带 .NET 运行时的 x64 程序。
3. 若已有合法 Authenticode 证书，可传入本地 `-Signer <script.ps1>`。该脚本接收一个待签文件路径。构建会在打包前签署主程序，在计算哈希前签署安装包，并检查签名有效。没有证书的构建要标明未签名以及 SmartScreen 的实际行为。
4. 对最终安装包运行 `packaging/New-UpdateManifest.ps1 -Setup <setup.exe> -ProtectedKey <current-user-dpapi-file> -Notes <release-notes.md>`。也可以用 `-PrivateKeyPem` 读取存放在受控密钥库中的 PEM。正式 MacDock 渠道的 P-256 私钥以 Windows 当前用户 DPAPI 加密保存在工作树外。保留该账户及其受保护文件的恢复能力，且勿上传。核对脚本输出的公钥与构建中嵌入的公钥一致。签名后不可改动安装包。
5. 复核 `SHA256SUMS.txt` 对应实际将上传的最终文件。确认 ZIP 不含源码、PDB、实际用户配置、真实桌面截图或调试日志。
6. 默认在现有 Release 内替换三项附件。上传前备份旧附件并记录 Release ID 和标签；上传后逐项比对远端摘要与本地 SHA256。如果渠道现有签名清单也绑定了安装包，使用该渠道的受控密钥同步生成并校验匹配的清单。

无渠道参数的构建适合本地验证。它的「检查更新」会提示尚未接入，避免候选版连接错误仓库。公开版本的首次安装是一次人工下载安装，随后安装版会低频检查 GitHub。更新器只接受嵌入公钥签名的正式清单和匹配的 SHA-256 安装包。升级向导由用户确认。便携版手动替换。

## 安装与回滚

默认路径为 `%LOCALAPPDATA%\Programs\MacDock`。安装按版本分别写入 `versions` 子目录，旧版本保持可启动，完成后再更新本软件创建的快捷方式。Windows 注册卸载入口，卸载清理安装记录中的程序文件，并仅删除仍指向受管 MacDock 程序的快捷方式和启动项。软件保留 `%LOCALAPPDATA%\MacDock` 下的配置。向导中断时，从开始菜单打开旧版，或在已知安装目录运行先前版本。

建议首次授权后再接入 CI。公共仓库的工作流无须读取私有仓库或持有签名私钥。私有构建可上传内部测试产物，首次分发仍应人工复核。每次公共发布前重验干净 Windows 账户、安装升级、更新和关键桌面交互。
