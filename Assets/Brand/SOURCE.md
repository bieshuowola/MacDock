# MacDock 正式应用图标

用户于本轮提供并明确选定的蓝色三层玻璃素材，直接集成，未重新设计、裁切或生成。

- 原素材包：`D:\edgedownload\MacDock_ADE58FBAE7A89BE687_93E8B2B8E5828EE783.zip`
- `MacDock.ico` 原样使用，包含 16、24、32、48、64、128、256 像素帧。
- ICO SHA-256：`B3331DA95F05D260EE11DC83BFA259B281580F2DF6D009BF31A411FA56E7166F`
- 256 / 1024 PNG 和包内原 README 一同保留，方便后续维护。源文件的 JPEG 边缘处理说明来自素材包，不代表外部开源许可声明。
- 使用授权来自用户对本项目的明确指示；本项目不声明图标适用于第三方再授权。

`ApplicationIcon` 将同一 ICO 写入 EXE；WPF 窗口使用其中最大帧；托盘按系统小图标尺寸选帧。Dock/抽屉是工具窗口，依原交互不强行出现在任务栏或 Alt+Tab；普通窗口/快捷方式的品牌资源仍一致。唯一抽屉按钮的 Windows 四窗格 `Assets/Drawer.svg` 保持原样，与本应用品牌无关。
