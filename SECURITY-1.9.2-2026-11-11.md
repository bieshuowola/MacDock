# MacDock 1.9.2 安装包安全核查

2026 年 11 月 11 日，用户报告另一台 Windows 笔记本从 GitHub Release 下载时，Edge 拦截文件，Defender 将 `.crdownload` 判为 `Trojan:Win32/Sabsik.TE.A!ml` 并删除。本机是独立的开发和核验环境。原笔记本的临时文件及当时的 Defender 定义未取得，因此无法直接验证该临时文件的完整字节或检测原因。

## 处置与来源

- 公开 1.9.2 已转为草稿，保留原附件供核验。匿名原安装包链接返回 HTTP 404，匿名稳定频道列出的版本为 1.9.1。公开 README 提醒暂停使用已下载的 1.9.2 安装包。证据在 `test-results/product192-withdrawal-status.json`。[公开说明](https://github.com/bieshuowola/MacDock)
- GitHub 资产摘要、发布 `SHA256SUMS.txt`、已保存的匿名下载副本和本地构建一致。安装包 SHA-256 为 `40d0aa834b4ba918611031525a5ab37a84e324606626ab0d84482c7e0a01fa19`。这确认核验对象与公开资产的字节一致。证据在 `test-results/product192-security-remote-assets.json` 和 `test-results/product192-online-stable.json`。
- 本机 Inno Setup 6.7.4 安装器与供应商 GitHub 资产 SHA-256 一致，Authenticode 发布者 Pyrsys B.V. 的签名有效。公开安装包外层 PE 代码节与供应商 `SetupLdr.e32` 一致，差异在原生资源节，包括图标、版本和安装清单。重新下载的 Windows SDK 与三个 .NET 运行时 NuGet 包均通过签名校验。抽查 CoreCLR、HostFXR 和 WPF DLL 的 Microsoft 签名有效。证据在 `test-results/product192-inno-loader-comparison.json`。[Inno Setup 6.7.4 官方发布](https://github.com/jrsoftware/issrc/releases/tag/is-6_7_4)
- 从源码提交 `75d39dd` 隔离重建。473 个载荷文件中 469 个逐字节一致。两份文档的换行规范化后内容一致。两份应用二进制的原生资源差异限于应用清单，规范化后 XML 一致。托管程序集的 4,009 个方法条目、嵌入资源和除 `#GUID` 外的元数据流一致。完整 Inno 封装的 SHA-256 不同，尚未实现逐字节可复现安装包。证据在 `test-results/product192-rebuild-comparison.json`、`test-results/product192-pe-comparison.json` 和 `test-results/product192-nuget-signatures.json`。
- 安装脚本写入当前用户目录和版本子目录。快捷方式、卸载记录及可选登录启动使用标准 Inno 和 Windows 机制。更新器在用户确认后从正式 GitHub 渠道获取清单和安装包，用嵌入公钥校验清单并核对安装包哈希。原 1.9.2 清单签名校验通过，安装包的 Authenticode 状态是 `NotSigned`。证据在 `test-results/product192-signature-audit.json`。

## 检测与剩余验证

2026 年 11 月 11 日用户确认已向微软提交原始 1.9.2 安装包的加密 ZIP。Submission ID 为 `5dd49c78-02dd-425c-8e6e-57516adbe806`，用户提供的当前状态为 Submitted / Pending。尚未收到分析结论。后续查询复用该编号，公开 1.9.2 继续保持草稿状态。

- 开发机 Defender 实时保护开启。安全定义更新到 `1.463.233.0` 后，原安装包、应用载荷、1.9.1 安装包和 Inno 来源安装器的定向扫描均未检出威胁。添加互联网来源标记的隔离副本也未复现检测。该测试没有运行安装包，无法复现另一台设备上完整的 Edge 下载和云端判定路径。证据在 `test-results/product192-defender-custom-scan.json` 和 `test-results/product192-motw-result.json`。
- 独立 ClamAV 1.5.4 使用官方渠道更新的病毒库，在开发机扫描原安装包和展开的应用载荷，共 474 个文件，检出 0 个。扫描的引擎和环境不同于原检测，无法代替微软对该次报告的结论。证据在 `test-results/product192-clamav.txt`。
- 当前核查未发现恶意行为或供应链污染的证据。原笔记本的下载检测尚无复核结论。用户已完成提交，编号及待分析状态见上文。原提交材料仍保留在 `.scratch/SECURITY-SUBMISSION-192.txt` 和本轮备份目录。[微软文件复核入口](https://www.microsoft.com/en-us/wdsi/filesubmission)
- 构建脚本已有 Authenticode 签名入口。更新清单的 P-256 签名仅验证更新来源和内容。Azure Artifact Signing Basic 标价约 9.99 美元/月，公开信任签名受地区及身份验证条件限制。本次未取得代码签名证书。[签名方案](https://learn.microsoft.com/windows/apps/package-and-deploy/code-signing-options) · [官方定价](https://azure.microsoft.com/en-us/products/artifact-signing)
