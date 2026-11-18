# MacDock 1.9.4 正式公开发布记录

2026-11-12。依据用户本轮明确授权发布，未重新构建或改动已验收的程序，未重新开展性能长测。

- 正式 Release：https://github.com/bieshuowola/MacDock/releases/tag/v1.9.4 。非草稿、非预发布，已设为 latest。
- 程序源码基线：395c501ffc0f06aeff7838a2e33d591a97a2d153。后续提交仅更新分发文档与发布记录，二进制不变。
- 公开分发标签指向独立文档提交 e6ed6b4a9959698509909ee471fa82c27f33645e，仅包含 README.md、GUIDE.md、CHANGELOG.md 和三张既有产品演示图。自动生成的源码归档不含私有项目。
- 五项附件：MacDock-1.9.4-win-x64-Setup.exe、MacDock-1.9.4-win-x64-Portable.zip、SHA256SUMS.txt、macdock-update.json、macdock-update.sig。未上传 private 源码包、内部验收资料、local-build.json、配置或密钥。

## 产物与线上验证

安装包 SHA-256：2E768C20571EEA6AE720293F5FF290F0D966FFC31D2905BF04239D9905435B45。

便携包 SHA-256：3076F6EA90877D7F7DB305ACA17DF7985D24A34C81D551B52CBACE35501F3058。

发布前核对便携包473个文件均与此前扫描载荷匹配，无配置、源码、密钥及调试文件；安装包版本及哈希与已验收候选一致。GitHub五个附件的大小及服务端摘要均匹配。更新清单沿用既有P-256受保护密钥，公钥与程序嵌入值一致。

匿名访问正式发布页HTTP200、latest返回v1.9.4，完整下载全部五个附件并核对内容；使用未经修改的生产ReleaseUpdates源码验证1.9.1和1.9.3均发现1.9.4、签名验证和完整安装包下载校验成功，同版本不重复提醒，不提供降级。没有再次运行安装器或改动真实配置；安装、升级、保留配置及卸载沿用本轮此前已通过的受控测试。本次网络验证不等同于另一台笔记本Edge下载验收。

证据：test-results/public194-artifact-audit.json、public194-published.json、public194-updater.json、public194-anonymous.json、public194-public-tree.json；下载原件保存在public194-downloads和public194-anonymous目录。

## 安全与限制

真实下载的安装包及其他附件再次接受正常启用Defender的定向检查，两次扫描均完成，检出事件0；防护和排除列表未改动。此前开发机对新旧安装包Defender检查未检出，ClamAV扫描477文件未检出。相关记录public194-download-defender.json和SECURITY-1.9.4-LOCAL.md保留。

历史1.9.2在另一台Windows笔记本触发Trojan:Win32/Sabsik.TE.A!ml，仍保持草稿撤回状态；微软复核5dd49c78-02dd-425c-8e6e-57516adbe806仍Pending。未获得另一台设备或微软云端放行结论，不能宣称历史告警已经根治。程序和安装器仍无Authenticode代码签名，清单签名不替代代码签名。已在公开主页、指南和Release说明中如实告知，未指导关闭或绕过防护。

Genie修复与性能边界沿用VALIDATION-GENIE-SHADOW-SYNC.md：阴影随形变同步；短冷态未发现明确可重复性能退步，单次5分钟约50ms呈现等待归因未定，没有宣称所有长帧均被消除。

## 本地状态与回滚

发布结束核对唯一实例仍为release/MacDock/MacDock.exe、版本1.9.4，EXE哈希与候选一致。配置SHA-256仍为35880C66399478C9C77D38E7E7F5AEECC63B89948F9FE82D9A3A4028B7C0558C；固定项、抽屉排序及真实桌面文件未改动。证据public194-local-state.json。

本机1.9.3回滚入口backup/pre-1.9.4-deploy-20261112-064343/Rollback.ps1保留，恢复程序时不回写旧配置。既有备份、历史Release和私有源码归档均未覆盖或删除。
