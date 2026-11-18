# MacDock 1.9.5 正式发布记录

2026-11-12。已正式发布为latest、非草稿、非预发布：
https://github.com/bieshuowola/MacDock/releases/tag/v1.9.5

程序源码提交588e6d882efe76c858c4ff9500355f5b79d3e56d；公开分发标签指向892c2b83cf7bc6f80264a5a4aca7309c2d24f2a7，仅六项既有产品资料。没有公开私有源码、测试录屏、进程取证、用户布局或签名私钥。

五项附件全部上传并核对远端大小与SHA-256：MacDock-1.9.5-win-x64-Setup.exe、MacDock-1.9.5-win-x64-Portable.zip、SHA256SUMS.txt、macdock-update.json、macdock-update.sig。最终产物来自release/product-1.9.5-candidate-ca92f979。

匿名发布页HTTP200，最新稳定版API返回v1.9.5；五项文件均完整匿名下载并与本地核对一致。使用未经修改的生产ReleaseUpdates源码验证1.9.1、1.9.4发现1.9.5、签名验证和安装包下载校验通过；同版本不重复提示，不提供降级，没有自动执行安装。证据public195-published.json、public195-updater.json、public195-anonymous.json及下载目录。

Defender实时保护开启，定义1.463.256.0，最终候选未检出；ClamAV扫描477个文件未检出。历史1.9.2另一台笔记本的Sabsik告警仍无已知最终复核结论，微软提交5dd49c78-02dd-425c-8e6e-57516adbe806按已知Pending记录；旧1.9.2继续撤回。未关闭防护或添加排除，未宣称其他设备必然放行。安装器及程序仍无Authenticode签名，已在公开说明中告知。

本地已原子部署同一载荷，正常托盘退出重启后配置字节、全部条目顺序及五项固定顺序均保持；唯一1.9.5实例，EXE/DLL哈希与候选一致。真实数据目录为普通Windows用户的AppData配置，开发宿主的私有覆盖层已识别并受代码保护。五项入口和68项抽屉按照用户确认规则恢复，见DELIVERY-1.9.5.md；未声称完全重建未知历史排列。原快捷方式与Run入口仍指向既有D盘正式位置，一次性测试任务均已注销。

回滚：backup/pre-1.9.5-native-deploy-20261112-113934/Rollback.ps1，仅回退程序到1.9.4，不回写旧用户配置。历史备份均保留。本轮实体165Hz完整动态录制与混合DPI未复测，实际完整录制在UU虚拟屏144Hz完成，范围与原始证据详见DELIVERY-1.9.5.md。
