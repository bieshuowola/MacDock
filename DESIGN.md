# 1.8.1 圆角交接专项

已实际读取用户 ZIP 中异常/正常原图、并排对比和上下角放大图。异常是主面板大圆角之外的亮色小圆角角块，不能当成正常 Genie 形变。静止两层及逐层隔离未重现照片级大块，近终态动态采样另行保留，避免把照片证据与本机是否复现混为一谈。

查到的具体代码顺序缺口：HostGlass 从曲面返回普通层时，先显示普通 Sprite、挂接复用的 Clip，最后才更新该 Clip 的大小/圆角；同时前景直接移除原生区域。现在完整的新 CompositionRoundedRectangleGeometry/Clip 在离树状态准备完毕，再一次挂接、显示普通层、隐藏曲面条带，最后恢复原生投影。前景不再解除圆角约束，而保留跟随实际物理尺寸/DPI 的圆角 HWND 区域（预留一像素抗锯齿）；只作用于使用 Genie 的抽屉，不修改 Dock 或全局系统设置。

这是有限的交接与裁剪修复，不改漏斗曲线、图标/控件、玻璃采样来源和动画节奏。原 Scene 的几何与 Clip 在替换后释放，静置不加新计时器或帧循环。229 个可采样近终态绘制帧、16 轮分层开关中未出现连片白角；原始报告保留两帧各一个边界亮像素，不冒称所有输出像素完全一致。仍不能把未稳定复现的照片级原故障宣称已经穷尽触发条件。

---
# MacDock 1.8：正式品牌、曲面抽屉与前台唤醒策略

第一阶段检查点 cbaf728（源码 de569ff，原运行 DLL 8CE9D619…）及 `backup/phase1-complete-20261109-002856` 已保存。DockMotion.cs 保持字节不变；桌面来源、图标提取、Shell 文件引擎、配置版本 3 及独立 DockOrder 继续沿用。

`AppBrand`、ApplicationIcon 和项目资源使用用户指定 ICO 原字节。窗口选最大 256 帧、托盘选系统小图标帧；保留 PNG 与来源哈希。工具窗口仍不占据 Alt+Tab 和任务栏，品牌的普通窗口场景用同构建临时窗口实测，不为验收强行改变 Dock 的工具窗口属性。

`DrawerMotion` 是独立有限解析临界阻尼运动，打开/关闭采用不同响应率，反向继承位置与速度，revision 防止重入后旧完成回调关闭新目标。`GeniePose` 为行网格、玻璃与输入提供同一不可变轮廓：上部先展开，下缘延迟释放并锚定实际 GatewayCenter。WPF `Viewport2DVisual3D` 使用 48×16 网格映射原有活控件，非静态屏幕截图。16 列避免一列三角形插值导致图标出现横向锯齿折变。静止时网格精确平直、物理像素对齐并缓存，无持续帧循环；保留缓存避免每次交换整棵视觉树的空白帧。Surface 保留 Window 逻辑父级，确保 Loaded、图标异步订阅、模板、字体资源与焦点的原有生命周期。初轮实拍发现冷加载图标无法更新，逻辑父级修复后实际冷开图标恢复，不能只靠测试预热掩盖。

玻璃仍由独立、无输入且不激活的 Windows Composition HostBackdropBrush HWND 实时采样外部内容。变形期间 48 个共享 HostBackdropBrush 的投影条带与前景网格使用同一曲线，未引入连续截图、屏幕软件模糊、Mica 替代或新 UI 框架。原生 HWND 区域和 ModalInputGate 的精确曲面判定分别约束绘制与点击；只有有限窗口边界，不存在全屏拦截层。菜单开始前完成变形并冻结位置，保留 1.7 原生菜单 Z-order 修复。

实拍的提前矩形投影来自 Windows 11 对辅助 HWND 的自动圆角/投影，不是 WPF 曲面自身。只裁剪 Composition 根节点或关闭非客户区渲染不足以移除它。变形期间，对本程序对应 HWND 使用 DWMWCP_DONOTROUND、暂停原生框架延展与非客户区绘制，使用 WPF GPU DropShadowEffect 跟随当前曲面。接近终态时淡出此临时阴影，完成后恢复原有系统框架、圆角和投影；静止无额外阴影动画。未修改任何系统全局阴影选项。实际取样在三个打开中段和关闭中段确认最终矩形边缘无残影，同时曲面边缘有局部柔影，终态系统投影仍存在。

鼠标按下由既有低级钩子只设置一个原子暂停标志，不阻塞钩子或跨线程操作 WPF。这样冻结发生在 WPF 排队命中前，避免 Render 优先级帧先移动控件；仅在 PreviewMouseDown 后暂停仍会漏点。释放、失焦、关闭、菜单、拖拽或退出正确结束暂停。PointerSort 的可选 prepare 仅服务抽屉，在达到阈值时完成入场并保留原捕获及抓取偏移，不改变 Dock 排序行为。

全屏策略不枚举游戏、不检查后台游戏图像。前台整屏窗口才可能抑制；使用 WindowFromPoint/真实 Shell 任务栏根窗口确认任务栏已暴露且可交互，而非仅用 IsWindowVisible（被游戏盖住的任务栏也可能返回 true）。自动隐藏激活细条不算已暴露任务栏。独立进程测试窗口与真实 Win+T / Alt+Tab 输入覆盖状态恢复，但不冒称已测试独占 DirectX 游戏或反作弊。

公开接口依据：[Viewport2DVisual3D](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.media3d.viewport2dvisual3d)、[Composition Visual](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.visual)、[DWM 非客户区策略](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmncrenderingpolicy)。这是 Windows 上的轻量曲面近似，不是 Apple 专有 Genie 实现或逐像素视觉等价承诺。

---
# MacDock 1.7.1：延迟提示与柔性开关

Dock 使用独立的 1000ms 单次名称计时器，按稳定基准槽位而非波浪缩放后的图像判定对象。切换对象必须完整重新等待，没有 ToolTipService 的 BetweenShowDelay 捷径；点击、拖放、菜单、收起取消提示。一个非激活 ToolTip 保持固定槽位锚点，长名称换行；抽屉没有关联鼠标 ToolTip，Automation Name/HelpText 保留。静置不轮询名称。

旧 Dock 动画只是整个 HWND 下移与透明度渐变，反向收起还从固定透明度起步，难以形成柔性连续感。新 DockMotion 用可重定向的有限解析阻尼弹簧，保留位置和速度；玻璃的 CompositionRoundedRectangleGeometry 从底部锚定舒展，同时改变宽度、厚度和圆角，WPF 前景色层使用同一几何函数。图标只轻微上浮/淡入，保持原始比例；根槽位、窗口坐标和拖动命中不跟随材质伸缩。玻璃仍由 HostBackdropBrush 实时合成，没有连续截图、软件模糊或新图形依赖。

绘制期间 COM/消息处理可能重入新的开关请求；每次重定向带 revision，绘制前后和最终完成通知均核对同一请求，防止旧完成回调错误隐藏新动画。专用重入回归在修改前复现了“进度仍为 1、计时器在运行却通知关闭”，修复后只在当前目标收敛时通知一次。名称提示首次正式实机验收还发现 WPF 默认 Popup 关闭淡出会保留原生 HWND 150ms；通过此 ToolTip 的公开 Parent/PopupAnimation 属性取消关闭尾帧，不改其他提示或系统设置。原生 HWND 的关闭另设 80ms 实测断言。[WPF Popup 源码](https://github.com/dotnet/wpf/blob/main/src/Microsoft.DotNet.Wpf/src/PresentationFramework/System/Windows/Controls/Primitives/Popup.cs) 解释了淡出期间保留窗口的行为。

打开回弹约 1.1%，关闭使用临界阻尼；不可感知的尾段及时结束，避免机械等待。每次只存在一个 Render 优先级 15ms 计时器，稳定/隐藏/退出后释放，系统关闭动画或用户选择减少动画时立即到位。鼠标按下和原生菜单打开前完成当前入场，避免移动中的图像快照、命中或菜单层级冲突。没有逐像素丝带扭曲、Apple 专有 Genie 或 GPU 网格变形的复刻承诺；这是在现有 WPF/Composition 架构中采用的轻量轮廓形变。

## 追加的交接与输入修复

唤醒横条的长度、厚度和透明度直接由原有 DockMotion 进度计算，同一条连续函数用于两个方向，不添加横条计时器，不改 DockMotion.cs、原白条位置、原尺寸或 sense 区域。可见线收缩，原生小窗口仍固定且 WS_EX_TRANSPARENT / no-activate；相对恒定的柔影随长度缩短自然软化，不增加光效。

120fps 录制及阶段诊断发现扩展 HWND 高度时，有若干次合成背景仍处于普通完整圆角矩形模式；现在在任何扩窗前先设置 GeniePose 和曲面裁剪，再允许同步的尺寸通知刷新合成。隐藏后不重启原生投影；可见终态在前后 HWND 同步后才恢复系统阴影。增加实时不变量：扩展窗口时曲面已就绪、可见普通背景交接的前景/背景/合成尺寸一致。未稳定捕获用户描述的每一种偶发四角闪现，修复的是实际记录到的时序缺口，调整后的录制与逐帧检查未见同类残影。

另修复两处与实际曲面宿主有关的输入边界。Viewport2DVisual3D 的 TranslatePoint 会对视口外的行返回 (0,0)；排序改用共同二维祖先的 TransformToAncestor，保留不可见行的真实布局几何，避免滚动目标错位。独立进程验证保存顺序与实际网格一致。外部首次点击关闭仍由模态门消费，但主动 Alt+Tab/前台切换必须立即解除模态；钩子在前台已变为其他应用时放行当前点击，UI 完成被动关闭，禁止动画残留吞掉新应用的第一次点击。真正全屏隐藏也立即清除区域。
以下为本轮已经保留并回归的菜单/排序修复。

---

# MacDock 1.7：菜单与排序修复

基于本机 `fbf54f3` / 1.6，修改前完整备份至 `backup/sort-menu-20261108-215551`。保留 WPF、Composition、Shell、图标链路和桌面数据来源，无框架迁移。

原菜单并非缺少“置顶”标志：实际失败夹具在 TrackPopupMenuEx 期间触发 Position/RefreshMaterial，宿主的入场动画和再次定位把面板抬到了菜单上面。进入系统菜单前完成当前入场几何，递增动画 epoch；菜单持有期间冻结宿主定位和背景层的 Z-order 同步，退出后才处理延迟定位。菜单仍由实际抽屉 HWND 所有，保留 SetForegroundWindow、IContextMenu2/3 转发及系统子菜单。不另给菜单全局置顶，不改系统设置。实验性的背景/前景所有权链不是必要修复，最终不采用。

向本进程的 HMENU 插入固定/取消固定命令，QueryContextMenu 的 ID 上界为 0x7FFD，MacDock 使用独立的 0x7FFE；Shell 原命令偏移不变。此操作不注册 Explorer 扩展，不改文件。多选固定状态即时重新读取，调用原来的配置保存与视图更新路径。

两处排序原先共用 Entries 列表；抽屉拖动既影响固定顺序，又触发整个 Dock 重建。新 DockItem 尚未测量的 Left=0 被用于旧动画坐标，同数量重建时没有重新初始化，导致所有图标平移到同一点。现增加独立可选 DockOrder，固定项以此排序；抽屉只修改当前分区的 Entries 顺序，其他分区和固定项不动。Dock 重新测量后总是从真实布局初始化动画坐标；本地排序只更新所属视图，Dock 调整已有磁贴位置，抽屉不重建 Dock。

PointerSort 为一次手势持有鼠标捕获，使用系统拖动阈值，稳定槽位几何与当前滚动偏移计算插入索引；邻近图标用可中断的 110ms 位移动画让位。原图标仅在拖动时隐藏，一次渲染的单个预览随指针移动；这不是背景截图或连续截屏。仅拖动期间运行 35ms 边缘滚动计时器。Esc 临时热键、丢失捕获、卸载或结束时释放预览、捕获、钩子与计时器。Ctrl/Shift 和拖出窗口转给既有 OLE/Shell 路径；内部排序不创建 ShellDrop、不启动悬停激活计时器。减少动画设置同时作用于插入反馈。

官方接口依据：[TrackPopupMenuEx 宿主与菜单消息](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenuex)、[QueryContextMenu 命令 ID 范围](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-querycontextmenu)。实测范围及局限见 VALIDATION.md。

以下为保留的 1.6 设计记录。

---

# MacDock 1.6 技术与设计决策

本轮从本地 db22703（1.5）继续，保留 WPF/Win32/Shell 架构、图标资源链路、配置版本 3 和已有交互；没有修改 PasswordBook 或游戏加加。原发布、完整配置和源码另存 backup/glass-mirror-20261108-094758。

## 背景透视的诊断与实际方案

1.5 默认使用 Accent Acrylic 兼容路径，并非 Mica 或静态截图。其亮度混合加上前景覆盖使黑白背景响应很弱：部分区域只有约 20 个亮度级差。此前“颜色会变”只能证明有合成，不能证明背景形态和层次足够。普通文档化 Desktop Acrylic 在非激活/透明 WPF 宿主上也不能直接获得 PasswordBook 的相同表现。

只读检查 PasswordBook/WindowMaterial.cs：透明 HwndTarget、DwmExtendFrameIntoClientArea(-1)、DWMWA_SYSTEMBACKDROP_TYPE=TRANSIENTWINDOW（3，Desktop Acrylic）。其普通交互窗口与 Dock 的非激活悬浮窗口不同。本轮没有复制其代码或修改其项目；未把 Mica 的壁纸着色当作后方窗口透视。

最终默认采用公开 Windows.UI.Composition.Compositor.CreateHostBackdropBrush + DesktopWindowTarget。直接把 Composition root 接入同一个 WPF HWND 会替换 WPF 内容，已实测排除。局部增加独立合成 HWND：在 WPF 前景正下方，仅覆盖各自面板大小，设置 NOACTIVATE/TOOLWINDOW/TRANSPARENT/NOREDIRECTIONBITMAP，不进入任务栏或 Alt+Tab；使用 DWMWA_USE_HOSTBACKDROPBRUSH。它实时采样外部窗口，由系统完成模糊；WPF 单独绘制清晰图标、文字及薄色彩覆盖。没有屏幕采集、位图背景缓存或 CPU 模糊。

合成层和前景同步物理位置、DPI 尺寸、显隐与透明度。几何 clip 为抗锯齿圆角，配合 HWND region 排除空角输入；Dock 16 DIP、抽屉 26 DIP，提示/设置内容保持同一圆角体系。重复尺寸/透明度写入和无变化的 SetWindowPos 被跳过。关闭时释放 target、brush、geometry、clip 和 compositor，并关闭自建 DispatcherQueue。禁用系统透明、高对比度或合成创建失败时使用协调实色，不把降级标为实时玻璃。

浅色覆盖保证文字对比，深色保留色彩渗透；系统 blur 半径由 Windows 决定，没有专有折射、Apple 连续曲率或逐像素等价承诺。图标中原有品牌边框保留，不能为统一造型裁掉图形。

验收用独立 GDI 进程绘制黑/白、红蓝大块和窄条纹，确认绘制状态后采样。早期同进程 WPF 夹具未可靠建立背景，已拒绝其失败/通过推断；新报告同时记录实际外部源、通道响应、条纹衰减和局部文字对比。截图只作证据，不参与程序渲染。

## 桌面镜像与 Shell 菜单

自动来源继续严格限定用户/公共桌面直接子项及实际桌面视图确认的特殊入口，手动项目独立分区。文件系统非递归监听 + SHChangeNotifyRegister/NewDelivery 补充 Shell 图像、关联和虚拟项目通知；TaskbarCreated 后重建监听，五分钟安全校准保留。变动在交互完成后防抖重排，重命名先维护身份，避免菜单/拖放中销毁磁贴。路径重定向后重新绑定 watcher。

桌面对象默认用 SHBindToParent → IShellFolder.GetUIObjectOf → IContextMenu/2/3；所有命令作用于原对象，快捷方式操作针对 .lnk 本身。转发原生菜单的绘制、度量、初始化和 MENUCHAR 消息，并返回 IContextMenu3 的 LRESULT。Shell rename canonical verb 交给本视图的名称输入与既有 IFileOperation；其余命令由 Shell 执行，取消菜单视为正常结束。支持同父目录多选；跨用户/公共目录多选和手动别名保留安全的批量管理菜单，不假装跨目录扩展都可用。固定按钮独立于原生文件菜单。

空白菜单在本进程通过 SHGetDesktopFolder/CreateViewObject 创建，保留系统“新建”及扩展；添加宿主的粘贴/刷新命令，仍走 IFileOperation。直接取得 Explorer 已有视图的跨进程菜单曾出现度量/绘制异常，已弃用。未注入 Explorer、使用其私有新式菜单接口或修改全局注册表菜单。

抽屉空白的外部 OLE 投放作用于真实用户桌面，保留 Shell 的 Ctrl/Shift、冲突与取消。普通内部拖动仍可排序；显式复制/移动修饰键进入真实文件语义。Dock 空白仍只固定引用。桌面对象被移出时不再伪装成自动桌面项：已明确固定的项目可跟随为手动引用，非固定项退出镜像。手动引用删除和取消固定不删除真实目标。

## 排版、焦点与动画

以 DIP 工作区为基础，使用 IFolderView2.GetViewModeAndIconSize 读取真实 Explorer 图标像素尺寸，再以所属 HWND DPI 转换，仅提供有界的偏好因子；不是重复应用 DPI。手动行列/容量优先，窗口始终受工作区约束。原项目没有独立手动图标尺寸开关，因此未引入额外配置中心。

Segoe UI Variable Text 配合 Microsoft YaHei UI/YaHei 回退；网格双行标签使用统一固定行高、居中基线和完整名称提示，不为长名称改变图标中心。搜索框采用单层中性聚焦样式，打开抽屉不自动编辑；Ctrl+F/点击正常进入编辑，保留原生 TextBox/IME。图标和文本在静止时不保留非整数 ScaleTransform；入场轻微缩放结束精确回到 1。

打开/关闭使用有限、可打断动画，反向时在图标、布局和 COM 工作之前作废旧 epoch，避免旧关闭计时器继续推进。原生菜单占有键盘时抽屉不截获其 Esc/快捷键；延迟菜单焦点初始化不覆盖已经获得焦点的菜单项。新合成背景可能先于首帧 WPF 输入位图可见，首帧尚未建立时只吸收面板内的点击对，不触发后台或错误收起；已绘制的按钮在动画期间正常交互。外部关闭仍成对消费，无全屏接收层。

## 官方接口参考

- [HostBackdropBrush 的外部背景采样](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositor.createhostbackdropbrush)
- [DWM Mica / Desktop Acrylic 区分](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- [DWM host backdrop 开关](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)
- [真实视图图标大小](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-getviewmodeandiconsize)
- [Shell 变更通知](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shchangenotifyregister)
- [文件夹背景菜单](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellfolder-createviewobject)
- [合成队列关闭](https://learn.microsoft.com/en-us/uwp/api/windows.system.dispatcherqueuecontroller.shutdownqueueasync)

以下保留较早版本的设计记录，以本节和本轮验收为准。

---

# MacDock 1.4 技术与设计决策

延续 WPF / Win32 / Windows Shell 架构，局部修复现有项目。保留任务栏、Explorer、桌面、文件路径与用户配置，不实现替代 Shell，也未复制 AirDeck 的品牌图片。压缩包内说明作为参考资料读取，不当作额外执行指令；实际问题由本机运行确认。

## 桌面来源的根因与纠正

1.3 遍历 Desktop/NameSpace 注册项，将“已注册”误当作“实际显示”。注册表包含库、搜索、内部提供程序等，不能直接作为抽屉来源。普通 Shell.Application.NameSpace(0).Items() 也比当前桌面视图宽，本机对照同样列出了并未显示的系统目录，不能以这个替换注册表后就宣称修复。

1.4 经 ShellWindows.FindWindowSW(SWC_DESKTOP) → IServiceProvider → IShellBrowser.QueryActiveShellView → IFolderView 读取 Explorer 实际视图的直接子项，仅据此确认特殊入口。用户文件特殊入口的解析名是用户目录路径，单独映射回 Shell 标识。实际文件仍只读用户/公共桌面文件夹的直接子项，保持隐藏桌面绘制后可用，不扫描开始菜单、不递归、不开启隐藏/系统文件导入。

视图不可用时，仅保留已确认的扩展入口和显式启用的已知入口，不把未知注册项当作可见项。两个物理目录分别记录可读状态，暂不可达不当作文件已删除。Watcher 防抖与交互结束补同步保留；TaskbarCreated 后重定位并重新读取桌面视图，五分钟安全同步没有持续高频轮询。

## 历史数据与文件安全

配置版本 3。1/2 版本升级前保存 config.json.before-v3，全部旧记录仍可追溯。旧版曾把自动导入都改为 manual，不能据此推断用户主动添加；这些记录标为 legacy，默认不混入内容，但原固定状态保留。未出现在真实视图的自动 Shell 项目标为 retired-auto；被固定的则转为可保留引用。新主动添加标记 user-added，并在“手动添加 · 快捷入口”分区显示。用户可通过恢复菜单主动确认旧记录，恢复菜单每次打开重新读取当前状态。

真实桌面项目的复制、移动、重命名、回收与属性由 Shell 执行。手动与未确认旧版项目是引用：Delete 仅移除引用，F2 只改显示名称，Ctrl+X 不移动原目标。引用拖出只提供 Copy/Link，不提供 Move，避免移动原程序。真实文件保留原生修饰键、冲突与取消处理；不假设所有应用支持文件输入。

内部 OLE 数据增加 dock/drawer 来源标记，区分 Dock 排序与抽屉文件投放。抽屉拖到 Dock 文件夹/兼容应用走 Shell，拖到空白只固定引用；Dock 内排序不触发真实文件操作。抽屉自定义模式直接拖动排序，显式 Ctrl/Shift 可进行文件投放。引用的内部排序使用允许的 Link 效果返回，因此不必为了排序给外部目标移动原文件的权限。

## 固定锚点的波浪

1.3 算法按当前指针重新给整排加水平 offset，且悬停条件覆盖整个 Dock，导致整排左右摆动和入口附近仍有波浪。1.4 保持固定基准槽位，高斯曲线只改变附近应用的图标尺度及最多约数 DIP 的局部位移。窗口、分隔线、入口不参与移动，远处应用回到固定位置。没有更改隐藏窗口的宽度来追随指针。

有效命中同时要求实际 HWND、屏幕矩形和应用槽位范围；入口、过渡区、抽屉打开、菜单、拖放及失去窗口所有权时不触发应用波浪。收敛后取消 Rendering，恢复后不保持错误放大。上轮修复的离开边沿、单调动画 epoch、白条恢复、独立钩子线程保留，不加延迟掩盖状态错误，不通过看门狗反复重装。

## 桌面比例、清晰度与材质

参考 AirDeck 的透视层次与留白，以约 70% DIP 工作区宽度推导自动网格，提高图标到 56×Unit、标签到 12.25 DIP，控制可见高度并滚动。排序仍左上角，搜索有独立层级，不添加日期/时间。入口为独立绘制的四窗格矢量，不放大位图，也不使用 AirDeck 标志。

工作区/DPI 改变后重建图标网格，并更新工具栏、搜索区、间隔和页脚尺寸，保留搜索与数据顺序。小工作区保留控件最小高度，避免裁切；列数计算消除接近整数时浮点向下取整的误差。Dock 去除额外横向 slack，按实际槽位计算宽度；居中缩放让 72×Unit 高度仍容纳 1.55 倍图形，不再靠大面积空白和下沉基线腾出动画空间。

### 图标机制

实际桌面 65 项的原始对照发现，部分 IShellItemImageFactory 大图已含细方框和中央小主体，继续按大图画布缩放会放大缺陷。改为优先遵循 .lnk 的 IconLocation（含索引/负资源 ID）、缺省时解析目标、.url 的 IconFile/IconIndex，以及直接 ICO/PE 资源。PE 只以 DATAFILE/IMAGE_RESOURCE 映射，不执行目标代码。枚举帧尺寸后选最近的较大原帧，没有大帧时保留原生小帧；避免预先将 32/48 像素插值成 128 再缩小。无可用静态资源时回退 Shell 图标；对 Shell 特有的“中间小图加中性框”使用通用结构识别，绝不删改原始资源里的品牌边框。

缓存按路径、物理尺寸及源/图标资源的修改时间和长度区分；快捷方式解析也按版本缓存，资源替换、桌面变更、显示比例变化、系统偏好和手动 F5 均可刷新。不存在特定应用名称分支。一个有消息循环的 STA 工作线程负责提取，冻结位图后回到 UI 线程更新槽位，失败落回占位而非永久重试；400 项位图、512 项资源元数据有界。无持续提取或图标轮询。打开抽屉、唤醒 Dock 与五分钟安全同步时复核版本，外部资源更新无需重启。

按 alpha 测量主体，去掉透明画布边距后在固定盒中等比例居中。旧方法让超大透明画布带负偏移参与 WPF 布局，在非整数 DPI 下会偏移或裁切；新的透明边距裁除不剪掉可见主体，也不统一切圆角。纯白透明图形依据颜色/覆盖率自动加浅色模式柔和轮廓阴影，避免白底失去辨识度；合法边框照常保留。

材质使用已有的系统 Accent Acrylic 兼容路径、透明 HwndTarget 与薄渐变层。Dock 采用逐像素宿主，抽屉采用普通 DWM 宿主；共享浅色白透/深色石墨调色体系，依据 AppsUseLightTheme 及 UserPreferenceChanged 自动更新，也可手动选择。两种 HWND 的合成亮度仍有客观差别。共享画刷通过绑定保持可更新，防止 WPF 模板冻结后切换失败。首轮浅色遮蔽过强，经实际黑白/条纹测量减少覆盖后保留更明确的背景响应。参考 PasswordBook 得到的生命周期处理保留：布局、激活、DPI 改变后重施材质。没有持续截屏、CPU 高斯模糊或静态背景替身。

抽屉滚动条在同一网格层叠加，5 DIP 胶囊、10 DIP 拖动区域，没有轨道或占位列；实际滚动或靠近右缘时淡入，鼠标/拖动保持可见，结束约一秒后渐隐并关闭隐藏命中。仅使用短时动画和单次延时，未订阅永久帧循环；滚轮反向、直接拖动会正确接管平滑偏移。

效果受 Windows 版本和显卡/合成策略控制。Accent 接口没有公开稳定性保证，模糊半径和材质响应不能按 Apple/AirDeck 参数精确设定；DWM 圆角与阴影也不是任意半径的 Apple 实现。API 失败尝试文档化背景，关闭系统透明/高对比度/合成不可用时实色降级。没有 Apple 专有折射，不宣称逐像素复刻。菜单保留轻量 WPF 材质风格，补齐真正的子菜单 Popup、滚动和键盘；原生 Shell 菜单继续保留兼容性。

## 版本与实测约束

所有诊断控制器使用隔离配置，包括原先遗漏的 probe/idle/settings-shot。last-session.json 记录实际程序、配置路径及测试模式，交付核对进程、DLL 哈希和便携包一致。

真实测试包含鼠标、OLE、Explorer 源、桌面隐藏、系统文件操作与窗口合成；离屏 DPI/重排明确不等同物理多屏。一次被 SearchHost 覆盖的波浪测试保留为未接受记录，另有测试驱动线程亲和与布局浮点问题的修正记录，不把它们混成通过结果。详见 VALIDATION.md。

## 主要官方参考

- [实际桌面视图的取得与遍历](https://devblogs.microsoft.com/oldnewthing/20130318-00/?p=4933/)
- [IShellBrowser.QueryActiveShellView](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellbrowser-queryactiveshellview)
- [低级鼠标钩子线程与超时](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc)
- [DWM 系统背景类型](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
- [DWM 圆角适用与限制](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners)
- [Shell 文件操作](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-ifileoperation)
- [Shell 图像提取与后台线程](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage)
- [SHDefExtractIcon 资源与索引](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shdefextracticonw)
- [PE 只读资源加载](https://learn.microsoft.com/en-us/windows/win32/intl/loading-a-win32-pe-resource-module)
- [Apple Materials HIG](https://developer.apple.com/design/human-interface-guidelines/materials)
