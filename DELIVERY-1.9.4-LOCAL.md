# MacDock 1.9.4 本地交付

更新（2026-11-12）：用户已另行明确授权正式公开发布。v1.9.4 已发布且完成匿名下载、附件哈希及签名更新验证，详见 PUBLIC-RELEASE-1.9.4.md。以下正文保留本地部署时的历史状态；其中“暂不公开”的限制已由本次授权解除，1.9.2 继续撤回，微软复核仍 Pending。

2026-11-12。已部署并启动唯一正式实例：`D:\Windows Dock\MacDock\release\MacDock\MacDock.exe`。

修复：Genie动画轮廓被当作空心路径展平后丢失，导致阴影沿用静态矩形；现随形变更新，并与前景共用GPU场景，保留高斯投影、MSAA、玻璃及原动画。正常速度录制、逐段剪帧和必要点击/拖拽回归通过。短冷态未发现明确可重复退步；一次5分钟约50ms系统呈现等待归因未定，未声称消除全部偶发长帧，按用户指示停止追加长测。

交付文件：

- 可执行程序：`release/MacDock/MacDock.exe`。
- 便携包及本地安装候选：`release/product-1.9.4-candidate-791e5200`。对应SHA256SUMS.txt、local-build.json保留。安装器暂不公开分发。
- 完整私有源码：`release/MacDock-1.9.4-private-source.zip`；Git历史仍在项目`.git`，未发布源码。
- 动画验收：`VALIDATION-GENIE-SHADOW-SYNC.md`，原始视频/剪帧在`test-results/shadow-sync-final-visual`。
- 安全调查及范围：`SECURITY-1.9.4-LOCAL.md`，扫描、重建与部署记录在`test-results/security194`，安装记录`test-results/security194-install-7fba20f3`。
- 部署版本及哈希：`release/MacDock-1.9.4-local-delivery.json`。

Defender实时保护开启；开发机新旧安装器扫描完成未检出，独立ClamAV477文件未检出。没有证据证明原笔记本的Sabsik告警已经根治，微软复核编号5dd49c78-02dd-425c-8e6e-57516adbe806仍Pending。当前不恢复公开发布；新版本未在GitHub发布，不能声称真实Edge下载及其他机器已经通过。程序/安装器未作Authenticode签名，更新清单签名不替代代码签名。

回滚：`backup/pre-1.9.4-deploy-20261112-064343/Rollback.ps1`。在PowerShell中运行该本地脚本，只恢复1.9.3程序并保存回滚前程序，不回写旧用户配置。原始`backup/pre-shadow-sync-20261112-044356`、历史稳定性备份和桌面安全备份均保留。

源码修复检查点3f7326c，安全检查点eb884a4；最终交付记录提交见Git HEAD。当前真实配置未改变；没有移动、修改或删除真实桌面文件、固定项或抽屉排序。
