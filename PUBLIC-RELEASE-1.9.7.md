# MacDock 1.9.7 正式发布记录

2026-11-13 08:49（北京时间）已公开稳定Release：
https://github.com/bieshuowola/MacDock/releases/tag/v1.9.7

运行代码1353dcb21a85ab8bc35901c7f469b0a3d2636d0c。公开文档标签07195427a009bc5e03fff90849b7b0c2b1209d7f，自动源码归档仅含六个产品资料文件，无私有源码、用户配置、验收照片或密钥。

五个附件完整公开：MacDock-1.9.7-win-x64-Setup.exe、MacDock-1.9.7-win-x64-Portable.zip、SHA256SUMS.txt、macdock-update.json、macdock-update.sig。公开页面匿名GET返回200，便携包及全部更新文件实际匿名下载并匹配本地哈希。

原生产更新器源码未改：1.9.1和1.9.6均实际发现1.9.7，验证P256签名并下载61,677,267字节安装包；同版本不提示、未来版本不降级。下载包SHA256与已扫描、完成升级验收的载荷一致。测试进程仅为GitHub API使用正常直连，附件复用既有代理，未改系统网络设置或给客户端添加凭据。

原正式目录release/MacDock/MacDock.exe已部署最终候选f419daa5。发布后PID4460、版本1.9.7，单实例，EXE与DLL哈希匹配。配置字节、全部条目与五个Dock固定项顺序保持；自启动开关实点验证通过，恢复原Run状态，值仍指向该正式路径。短时5秒闲置CPU累计0秒，私有内存约118MiB，不冒称长期泄漏测试。

1.9.6稳定回滚：backup/pre-1.9.7-native-deploy-20261113-083848/runtime-1.9.6，Rollback.ps1仅回退程序、保留当前用户配置。Git历史及开发前完整备份保留。私有源码包仅本地交付，不上传公开仓库。

标准ReadyToRun使便携包约79.5MB增加至90.2MB、安装包约55.3MB增加至61.7MB，换取经验证的较少初始化/JIT工作；未混淆、加壳、关闭防护或减少动画效果。

验收边界：详细启动三组交替数据见DELIVERY-1.9.7.md。应用自身完整就绪中位数800→561ms，不代表Windows登录/Run项调度同等提速；本轮未做整机重启、Explorer真实冷登录、UU切换、多设备及长期泄漏验收。历史Defender复核仍Pending，本机扫描未检出不等于所有设备/云端必然放行；应用和安装器仍无Authenticode签名。
