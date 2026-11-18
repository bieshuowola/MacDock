using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace MacDock;

internal sealed class DrawerWindow : GlassWindow
{
    readonly Controller owner;
    readonly StackPanel groups = new();
    readonly TextBox search;
    readonly TextBlock count = Theme.Text("", 12, true);
    readonly TextBlock placeholder;
    readonly Dictionary<string, RadioButton> modes = new();
    readonly Dictionary<string, System.Windows.Shapes.Path> modeGlyphs = new();
    readonly Button pinBtn, addFileBtn;
    readonly StackPanel actionsPanel;
    internal Button PinButton => pinBtn;
    internal Button AddFileButton => addFileBtn;
    internal StackPanel FooterActions => actionsPanel;
    bool? lastPinVisualLocked;
    bool? lastPinVisualDark;
    internal void UpdatePinVisual()
    {
        bool locked = owner.DrawerPinned;
        bool dark = Theme.Dark;
        if (lastPinVisualLocked == locked && lastPinVisualDark == dark) return;
        lastPinVisualLocked = locked;
        lastPinVisualDark = dark;
        var pinInk = locked
            ? new SolidColorBrush(dark ? Colors.White : Color.FromRgb(17, 24, 39))
            : Theme.Ink;
        pinBtn.Content = Theme.Glyph("M8,3 H16 L15,10 L19,14 H5 L9,10 Z M12,14 V22", 17, pinInk);
        if (locked)
        {
            pinBtn.Background = new SolidColorBrush(Theme.Dark ? Color.FromArgb(44, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0));
            pinBtn.BorderBrush = Brushes.Transparent;
            pinBtn.BorderThickness = new Thickness(0);
            pinBtn.Opacity = 1;
            pinBtn.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1.5, Opacity = 0.35, Color = Colors.Black };
            Theme.Tip(pinBtn, "取消锁定抽屉");
        }
        else
        {
            pinBtn.Background = Brushes.Transparent;
            pinBtn.BorderBrush = Brushes.Transparent;
            pinBtn.BorderThickness = new Thickness(0);
            pinBtn.Opacity = .72;
            pinBtn.Effect = null;
            Theme.Tip(pinBtn, "锁定抽屉（防止点击外部自动收起）");
        }
        InvalidatePrebakedSurface();
    }
    readonly SmoothScrollViewer scroll;
    internal SmoothScrollViewer Scroller=>scroll;
    readonly DispatcherTimer scrollBakeIdle=new(DispatcherPriority.ApplicationIdle){Interval=TimeSpan.FromMilliseconds(280)};
    sealed record ScrollAnchor(string[] Order,int Index,double Inset,double Offset);
    ScrollAnchor? unfilteredAnchor,pendingAnchor;
    string renderedQuery="";
    bool anchorQueued;
    readonly Grid main = new();
    readonly ScaleTransform entrance=new(1,1);
    internal double EntranceScale=>entrance.ScaleX;
    internal void SetEntranceScale(double value){entrance.ScaleX=value;entrance.ScaleY=value;}
    GenieVisual? genie;
    Border? bakedGenieSurface;
    readonly RectangleGeometry stageClipGeometry = new();
    RenderTargetBitmap? prebakedBitmap;
    bool prebakedDirty = true, prebakeQueued, windowClosed;
    double prebakedWidth, prebakedHeight, prebakedScale;
    bool deforming,meshAttached;double genieWidth,genieHeight,genieFrame;
    GeniePose? prevDeformPose;
    Layout? appliedLayout;
    double appliedScale;
    readonly Grid stage=new(){ClipToBounds=true};
    protected override UIElement? ForegroundOpacityTarget=>stage;
    readonly SoftContour contour=new();
    readonly bool shareShadow=false;
    readonly GatewayVisual gatewayRibbon=new();
    internal override double MaterialInset=>SoftContour.Pad;
    internal double PanelWidth=>Math.Max(0,Width-2*MaterialInset);
    internal Native.Rect BodyBounds{get{Native.GetWindowRect(Handle,out var r);int p=(int)Math.Round(MaterialInset*owner.Scale);r.Left+=p;r.Top+=p;r.Right-=p;r.Bottom=r.Top+(int)Math.Round((CurrentPose?.FrameHeight??PanelHeight)*owner.Scale);return r;}}
    public override bool Contains(Native.Point p){var r=BodyBounds;return IsVisible&&r.Contains(p)&&(CurrentPose==null||CurrentPose.Contains((p.X-r.Left)/owner.Scale,(p.Y-r.Top)/owner.Scale));}
    // Viewport2DVisual3D owns a Visual, not a WPF logical child. Keep the live source
    // in this window's logical tree so Loaded/Unloaded, icon subscriptions, resources,
    // focus scopes and scrolling templates retain their normal lifetimes.
    protected override System.Collections.IEnumerator LogicalChildren
    {
        get{var children=new List<object>();var basis=base.LogicalChildren;while(basis.MoveNext())children.Add(basis.Current);if(meshAttached&&Surface!=null)children.Add(Surface);return children.GetEnumerator();}
    }
    internal GeniePose? CurrentPose {get;private set;}
    internal double PanelHeight=>Math.Round(DesiredHeight()*owner.Scale)/owner.Scale;
    // Reserve the full animation extent before showing. Resizing a layered HWND
    // on the first close frame can present the previous surface at the new size.
    double FrameHeight=>PanelHeight+owner.Layout.DockHeight+46;
    protected override double MaterialHeight=>PanelHeight;
    internal bool IsDeforming=>deforming;
#if MACDOCK_EXPERIENCE_CHECKS
    internal string DiagnosticRendering {get;set;}="full";
#endif
    internal string LastPointerInput="";
    internal string LastWheelInput="";
    void ConfigureSurfaceGeometry(double sampleScale, bool clipToStage)
    {
        if (Surface is CoverageBorder coverage)
        {
            coverage.CornerRadius = new CornerRadius(MaterialRadius);
            coverage.SampleScale(sampleScale);
        }
        else if (Surface is Border border)
        {
            border.CornerRadius = new CornerRadius(MaterialRadius);
        }
        if (Surface != null)
        {
            Surface.Clip = null;
            Surface.Width = PanelWidth;
            Surface.Height = PanelHeight;
        }
        stage.ClipToBounds = clipToStage;
    }

    internal void SetGenie(double phase,double anchorX,double anchorY)
    {
        double frame=FrameHeight;
        if(!deforming)owner.DrawerLayerFrame?.Invoke("begin-before-resize",MaterialSnapshot);
        // Arm the curved clip and suppress the native rectangular projection BEFORE a
        // resize can synchronously refresh the compositor with a full-sized round rect.
        // Give the panes the first part of the motion. The existing Genie silhouette
        // catches up smoothly once the glyph has extended across the Dock edge.
        double sheetPhase = phase * GeniePose.Smooth(phase / .36);
        var pose=new GeniePose(PanelWidth,PanelHeight,frame,anchorX,anchorY,sheetPhase);
        // Coverage stays with the material's four corner patches, also in the
        // cached live texture. A second geometry clip would quantize it again.
        var bounds=new Rect(0,0,PanelWidth,PanelHeight);
        if(!deforming)ConfigureSurfaceGeometry(1, clipToStage: !shareShadow);
        var hostPose = !deforming ? pose : (prevDeformPose ?? pose);
        prevDeformPose = pose;
        deforming=true;CurrentPose=pose;SetMaterialGenie(hostPose);
        stageClipGeometry.Rect=new Rect(-MaterialInset,0,PanelWidth+2*MaterialInset,Math.Max(0,pose.Row(1).Y-0.5));
        if(!ReferenceEquals(stage.Clip,stageClipGeometry))stage.Clip=stageClipGeometry;
        owner.DrawerLayerFrame?.Invoke("begin-after-resize",MaterialSnapshot);
        if(!meshAttached&&Surface!=null)
        {
            if(genie==null||genieWidth!=PanelWidth||genieHeight!=PanelHeight||genieFrame!=frame)
            {
                genieWidth=PanelWidth;genieHeight=PanelHeight;genieFrame=frame;
                genie=new GenieVisual(PanelWidth,frame,shareShadow);
            }
            var targetSurface=BakeGenieSurface();
            stage.Children.Clear();AddLogicalChild(Surface);meshAttached=true;
            genie.Attach(targetSurface);genie.Set(pose);stage.Children.Add(genie.View);
            if(IsVisible)stage.UpdateLayout();
        }
        genie!.Set(pose);
        // The Surface retains its rounded alpha; the shared scene's soft exterior
        // projection needs the existing padding outside this stage's sheet rectangle.
        if(shareShadow)genie.AttachShadow(contour);
#if MACDOCK_EXPERIENCE_CHECKS
        genie.View.Opacity=DiagnosticRendering=="foreground"?0:1;
        RenderOptions.SetEdgeMode(genie.View,DiagnosticRendering=="msaa"?EdgeMode.Aliased:EdgeMode.Unspecified);
        contour.Visibility=DiagnosticRendering=="shadow"?Visibility.Hidden:Visibility.Visible;
        if(shareShadow&&DiagnosticRendering=="shadow")genie.DetachShadow();
#endif
        double opacity = GeniePose.Smooth(phase / .24);
#if MACDOCK_EXPERIENCE_CHECKS
        // Hiding a visual alone does not exclude retained resource updates.
        if(DiagnosticRendering!="shadow")
#endif
        // Shared projection already receives exactly this fade through stage.
        contour.Shape(PanelWidth, frame, MaterialRadius, bounds, pose, shareShadow?1:opacity);
        SetSurfaceOpacity(opacity);

        var gateway = GatewayPose.At(phase, 29 * owner.Layout.Unit);
        owner.Dock.PaintGateway(gateway);
        double seam = Math.Clamp(anchorY - owner.Dock.GatewayCenterFromTop, 0, frame);
        gatewayRibbon.Paint(gateway, new Point(anchorX, anchorY + owner.Dock.GatewayLift), new Rect(0, 0, PanelWidth, seam));
    }
    internal void EndGenie(bool retainHiddenMesh=false)
    {
        gatewayRibbon.Clear();
        if(genie==null||!deforming)return;
        owner.DrawerLayerFrame?.Invoke("end-before-resize",MaterialSnapshot);
        deforming=false;CurrentPose=null;prevDeformPose=null;
        contour.Shape(PanelWidth,FrameHeight,MaterialRadius,new Rect(0,0,PanelWidth,PanelHeight),null,1);
        owner.DrawerLayerFrame?.Invoke("end-after-height",MaterialSnapshot);
        SetMaterialGenie(null);
        owner.DrawerLayerFrame?.Invoke("end-normal-backdrop",MaterialSnapshot);
        SetSurfaceOpacity(1);
        UseFlatSurface();
        if(!retainHiddenMesh||IsVisible)UpdateLayout();
        owner.DrawerLayerFrame?.Invoke("end-after-layout",MaterialSnapshot);
    }

    internal void InvalidatePrebakedSurface(bool scheduleIdleWarm = true)
    {
        prebakedDirty = true;
        prebakedBitmap = null;
        if (scheduleIdleWarm && !scrollBakeIdle.IsEnabled && !deforming && !windowClosed && !prebakeQueued)
        {
            prebakeQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(WarmPrebakedSurface));
        }
    }

    internal void WarmPrebakedSurface()=>WarmPrebakedSurface(false);

    void WarmPrebakedSurface(bool attachingMesh)
    {
        prebakeQueued = false;
        if(windowClosed||Surface==null||
            (!attachingMesh&&(deforming||scrollBakeIdle.IsEnabled||scroll.IsScrolling)))return;
        double w = Math.Max(1, PanelWidth);
        double h = Math.Max(1, PanelHeight);
        double scale = Math.Max(1, owner.Scale);
        if (!prebakedDirty && prebakedBitmap != null &&
            Math.Abs(prebakedWidth - w) < 0.1 &&
            Math.Abs(prebakedHeight - h) < 0.1 &&
            Math.Abs(prebakedScale - scale) < 0.001)
            return;

        var live = Surface;
        if (!live.IsArrangeValid || Math.Abs(live.RenderSize.Width - w) > 0.5 || Math.Abs(live.RenderSize.Height - h) > 0.5)
        {
            var size = new Size(w, h);
            live.Measure(size);
            live.Arrange(new Rect(size));
            live.UpdateLayout();
        }
        int pxW = Math.Max(1, (int)Math.Round(w * scale));
        int pxH = Math.Max(1, (int)Math.Round(h * scale));
        double dpi = 96.0 * scale;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var vb = new VisualBrush(live)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                Viewbox = new Rect(0, 0, w, h),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            dc.DrawRectangle(vb, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(pxW, pxH, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        prebakedBitmap = rtb;
        prebakedWidth = w;
        prebakedHeight = h;
        prebakedScale = scale;
        prebakedDirty = false;
    }

    FrameworkElement BakeGenieSurface()
    {
        double w=Math.Max(1,PanelWidth);
        double h=Math.Max(1,PanelHeight);
        double scale=Math.Max(1,owner.Scale);
        if(prebakedBitmap==null||prebakedDirty||
            Math.Abs(prebakedWidth-w)>=0.1||Math.Abs(prebakedHeight-h)>=0.1||
            Math.Abs(prebakedScale-scale)>=0.001)
        {
            prebakedDirty=true;
            WarmPrebakedSurface(attachingMesh:true);
        }
        var brush=new ImageBrush(prebakedBitmap!)
        {
            Stretch=Stretch.Fill,
            TileMode=TileMode.None,
            AlignmentX=AlignmentX.Left,
            AlignmentY=AlignmentY.Top
        };
        RenderOptions.SetBitmapScalingMode(brush,BitmapScalingMode.Linear);
        brush.Freeze();
        bakedGenieSurface=new Border
        {
            Width=w,
            Height=h,
            Background=brush,
            IsHitTestVisible=false
        };
        return bakedGenieSurface;
    }

    void ReleaseBakedGenieSurface()
    {
        if(bakedGenieSurface!=null)
        {
            bakedGenieSurface.Background=null;
            bakedGenieSurface=null;
        }
        prebakedBitmap=null;
        prebakedDirty=true;
        if(!windowClosed)InvalidatePrebakedSurface(scheduleIdleWarm:true);
    }
    readonly Border segment;
    readonly DockPanel toolbar,footer;
    readonly Border gap1,gap2,gap3;
    readonly Grid searchHost;
    readonly Border searchChrome;
    readonly HashSet<string> selected = new(StringComparer.Ordinal);
    readonly Dictionary<string, Border> tiles = new(StringComparer.Ordinal);
    readonly List<Entry> visibleOrder = new();
    bool renderedListView;
    double renderedUnit, renderedScale;
    Entry? anchor;
    bool updating;

    internal double VisibleGridRows => scroll.ViewportHeight / (owner.Layout.RowHeight);
    internal string GridDiagnostics => $"window={ActualWidth:F1}x{ActualHeight:F1}; surface={Surface?.ActualWidth:F1}x{Surface?.ActualHeight:F1}; main={main.ActualWidth:F1}; scroll={scroll.ActualWidth:F1}; viewport={scroll.ViewportWidth:F1}x{scroll.ViewportHeight:F1}; tile={owner.Layout.DrawerTile:F1}; gap={owner.Layout.GapX:F1}; unit={owner.Layout.Unit:F2}";

    internal int MeasuredColumns()
    {
        var wrap = groups.Children.OfType<WrapPanel>().FirstOrDefault(); if (wrap == null) return 0;
        var positions = wrap.Children.OfType<FrameworkElement>().Select(e => e.TransformToAncestor(wrap).Transform(new Point()).Y).ToArray();
        return positions.Length == 0 ? 0 : positions.Count(y => Math.Abs(y - positions.Min()) < 1);
    }

    public DrawerWindow(Controller c) : base(interactive: true)
    {
        owner = c; Title = "MacDock 应用抽屉";
        // A pressed control must not move away between its down and up. Sorting settles
        // only when its threshold is reached; a simple click uses the current live mesh.
        PreviewMouseDown+=(_,a)=>{var hit=a.OriginalSource as DependencyObject;var path=new List<string>();while(hit!=null&&path.Count<8){path.Add(hit.GetType().Name+(hit is FrameworkElement f?":"+System.Windows.Automation.AutomationProperties.GetName(f):""));hit=VisualTreeHelper.GetParent(hit);}LastPointerInput=string.Join("/",path)+" @ "+a.GetPosition(Surface).ToString()+" Surface="+Surface?.RenderSize.ToString();owner.PauseDrawerMotion();};
        AddHandler(Mouse.PreviewMouseUpEvent,new MouseButtonEventHandler((_,_)=>Dispatcher.BeginInvoke(()=>owner.ResumeDrawerMotion())),true);
        DragEnter+=(_,_)=>owner.SettleDrawerMotion();
        main.RenderTransformOrigin=new Point(.5,1);main.RenderTransform=entrance;

        var l = c.Layout;
        for (var i = 0; i < 7; i++) main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions[4].Height = new GridLength(1, GridUnitType.Star);
        main.Margin = new Thickness(l.Chrome, l.DrawerPadTop, l.Chrome, l.DrawerPadBottom);

        // Row 0: sort and search controls.
        toolbar = new DockPanel { Height = l.ToolbarHeight };

        var switcher = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (name, data, tip) in new[] { ("自定义", Theme.GList, "自定义顺序 · 可拖拽排序"), ("按类型", Theme.GType, "按类型分组"), ("首字母", Theme.GAlpha, "按首字母分组") })
        {
            var glyph = Theme.Glyph(data, 16, Theme.Muted, 1.6);
            var radio = new RadioButton { Style = Theme.Style("Segment"), GroupName = "MacDockMode", Content = glyph, Tag = name };
            Theme.Tip(radio, tip);
            System.Windows.Automation.AutomationProperties.SetName(radio, tip);
            radio.Checked += (_, _) => { if (updating) return; c.Store.Data.Mode = name; c.Save(); Render(); c.Position(); UpdateModeVisuals(); };
            radio.MouseEnter += (_, _) => glyph.Stroke = Theme.Ink;
            radio.MouseLeave += (_, _) => { if (radio.IsChecked != true) glyph.Stroke = Theme.Muted; };
            modes[name] = radio; modeGlyphs[name] = glyph; switcher.Children.Add(radio);
        }
        segment = new Border
        {
            CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            Padding = new Thickness(1), Child = switcher, HorizontalAlignment = HorizontalAlignment.Left,
            BorderBrush = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255)), BorderThickness = new Thickness(1)
        };
        DockPanel.SetDock(segment,Dock.Left);toolbar.Children.Add(segment);
        Grid.SetRow(toolbar, 0); main.Children.Add(toolbar);

        gap1 = new Border { Height = 10 * l.Unit };
        Grid.SetRow(gap1, 1); main.Children.Add(gap1);

        // Row 2: search field with an overlaid magnifier and placeholder.
        search = new TextBox { Style = Theme.Style("SearchEditor"), Padding = new Thickness(0, 0, 12, 0), Height = l.SearchHeight, FontSize = 13, BorderThickness = new Thickness(0) };
        search.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "搜索应用和文件");
        var magnifier = Theme.Glyph(Theme.GSearch, 14, Theme.Faint, 1.6);
        var magnifierHost = new Border { Width = 38, Child = magnifier, IsHitTestVisible = false };
        placeholder = Theme.Text("", 13, true);
        placeholder.Margin = new Thickness(2, 0, 0, 0); placeholder.IsHitTestVisible = false; placeholder.Opacity = .85;
        var fieldGrid = new Grid();
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        fieldGrid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(magnifierHost, 0); fieldGrid.Children.Add(magnifierHost);
        Grid.SetColumn(search, 1); fieldGrid.Children.Add(search);
        Grid.SetColumn(placeholder, 1); fieldGrid.Children.Add(placeholder);
        searchHost = new Grid { Height = l.SearchHeight,Width=250,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(12,0,0,0) };
        searchChrome=new Border{CornerRadius=new CornerRadius(9),Background=Theme.FillSoft,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(1)};
        searchHost.Children.Add(searchChrome);
        void SearchState(){searchChrome.Background=search.IsKeyboardFocusWithin||searchHost.IsMouseOver?Theme.FillHover:Theme.FillSoft;searchChrome.BorderBrush=search.IsKeyboardFocusWithin?Theme.FocusEdge:Brushes.Transparent;}
        search.IsKeyboardFocusWithinChanged+=(_,_)=>SearchState();searchHost.MouseEnter+=(_,_)=>SearchState();searchHost.MouseLeave+=(_,_)=>SearchState();
        search.GotKeyboardFocus+=(_,_)=>owner.SettleDrawerMotion();
        searchHost.Children.Add(fieldGrid);
        Theme.Tip(search, "搜索名称或路径");
        search.TextChanged += (_, _) => { placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; Render(); };
        var clear=new Button{Style=Theme.Style("IconButton"),Width=26,Height=26,Content=Theme.Glyph(Theme.GClose,10,Theme.Muted),HorizontalAlignment=HorizontalAlignment.Right,Visibility=Visibility.Collapsed};
        clear.Click+=(_,_)=>search.Clear();search.TextChanged+=(_,_)=>clear.Visibility=search.Text.Length>0?Visibility.Visible:Visibility.Collapsed;Theme.Tip(clear,"清除搜索");searchHost.Children.Add(clear);search.Padding=new Thickness(0,0,30,0);
        var view=new Button{Style=Theme.Style("IconButton"),Content=Theme.Glyph(Theme.GList,15,Theme.Ink),Margin=new Thickness(8,0,0,0)};
        System.Windows.Automation.AutomationProperties.SetName(view,"切换列表和网格");Theme.Tip(view,"列表 / 网格");view.Click+=(_,_)=>{c.Store.Data.ListView=!c.Store.Data.ListView;c.Save();Render();};DockPanel.SetDock(view,Dock.Left);toolbar.Children.Add(view);toolbar.Children.Add(searchHost);
        gap2 = new Border { Height = 16 * l.Unit };
        Grid.SetRow(gap2, 3); main.Children.Add(gap2);

        // Row 4: scrolling grid of entries.
        scroll = new SmoothScrollViewer
        {
            Style = Theme.Style("Overlay"),
            Content = new DrawerScrollViewport(groups), CanContentScroll = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true, FocusVisualStyle = null,
            Padding = new Thickness(0)
        };
        Grid.SetRow(scroll, 4); main.Children.Add(scroll);
        SourceInitialized+=(_,_)=>HwndSource.FromHwnd(Handle).AddHook((nint h,int message,nint wp,nint lp,ref bool handled)=>{
            if(message==0x84&&!AllowsTransparency){var screenPoint=new Native.Point(unchecked((short)lp.ToInt64()),unchecked((short)(lp.ToInt64()>>16)));if(!Contains(screenPoint)||owner.Dock.Contains(screenPoint)){handled=true;return -1;}}
            if(message!=0x20A||owner.InternalDragging||owner.MenuOpen||Mouse.Captured!=null||!IsActive||Surface==null)return 0;
            // Viewport2DVisual3D can drop the routed wheel event after its live
            // source tree is rebuilt, even though WM_MOUSEWHEEL reached this HWND.
            // Use the real message and flat source-tree viewport, independent of
            // the stale 3D hit-test cache. No global hook, focus change or fake click.
            Native.GetCursorPos(out var p);if(!Native.GetWindowRect(Handle,out var r))return 0;
            var bounds=scroll.TransformToAncestor(Surface).TransformBounds(new Rect(scroll.RenderSize));
            var local=new Point((p.X-r.Left)/owner.Scale-MaterialInset,(p.Y-r.Top)/owner.Scale-MaterialInset);
            if(CurrentPose is { } pose){
                if(!pose.Contains(local.X,local.Y))return 0;
                var top=pose.Row(0);var bottom=pose.Row(1);double t=(local.Y-top.Y)/Math.Max(.5,bottom.Y-top.Y);var row=pose.Row(t);
                local=new Point(pose.Width*(local.X-row.Left)/Math.Max(.5,row.Right-row.Left),pose.Height*t);
            }
            if(bounds.Contains(local)&&scroll.ApplyWheel(unchecked((short)(wp.ToInt64()>>16)))){LastWheelInput="native viewport wheel";handled=true;}
            return 0;
        });
        gap3 = new Border { Height = 10 * l.Unit };
        Grid.SetRow(gap3, 5); main.Children.Add(gap3);

        // Row 6: selection/status (left) + icon actions (right)
        footer = new DockPanel { Height = l.FooterHeight, LastChildFill = true };
        actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        addFileBtn = new Button { Style = Theme.Style("IconButton"), Width = 30, Height = 30, Content = Theme.Glyph(Theme.GPlus, 17, Theme.Ink, 1.6), Margin = new Thickness(3, 0, 0, 0), Opacity = .72, FocusVisualStyle = null };
        addFileBtn.MouseEnter += (_, _) => addFileBtn.Opacity = 1;
        addFileBtn.MouseLeave += (_, _) => addFileBtn.Opacity = .72;
        addFileBtn.Click += (_, _) => c.PickFiles(false);
        Theme.Tip(addFileBtn, "添加文件或程序入口");
        System.Windows.Automation.AutomationProperties.SetName(addFileBtn, "添加入口");
        pinBtn = new Button { Style = Theme.Style("IconButton"), Width = 30, Height = 30, FocusVisualStyle = null, Content = Theme.Glyph("M8,3 H16 L15,10 L19,14 H5 L9,10 Z M12,14 V22", 17, Theme.Ink) };
        pinBtn.MouseEnter += (_, _) => { if (!c.DrawerPinned) pinBtn.Opacity = 1; };
        pinBtn.MouseLeave += (_, _) => { if (!c.DrawerPinned) pinBtn.Opacity = .72; };
        System.Windows.Automation.AutomationProperties.SetName(pinBtn, "锁定抽屉");
        pinBtn.Click += (_, _) => { c.DrawerPinned = !c.DrawerPinned; UpdatePinVisual(); c.UpdateModalRegions(); };
        UpdatePinVisual();
        actionsPanel.Children.Add(pinBtn); actionsPanel.Children.Add(addFileBtn);
        DockPanel.SetDock(actionsPanel, Dock.Right); footer.Children.Add(actionsPanel);
        footer.Children.Add(count);
        Grid.SetRow(footer, 6); main.Children.Add(footer);

        Surface = new CoverageBorder{Child=main,CornerRadius=new CornerRadius(MaterialRadius),BorderThickness=new Thickness(0)};
        Surface.SetResourceReference(Border.BackgroundProperty,"GlassSurface");
        Surface.SnapsToDevicePixels=false;
        var marqueeBox = new Border
        {
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(38, 0, 120, 212)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false
        };
        var marqueeCanvas = new Canvas { IsHitTestVisible = false };
        marqueeCanvas.Children.Add(marqueeBox);
        stage.Children.Add(Surface);
        stage.Children.Add(marqueeCanvas);
        stage.Margin = gatewayRibbon.Margin = new Thickness(MaterialInset);
        var frameRoot = new Grid();
        frameRoot.Children.Add(contour);
        frameRoot.Children.Add(stage);
        frameRoot.Children.Add(gatewayRibbon);
        Content = frameRoot;
        AllowDrop = true;
        ShellDrop? desktopDrop=null;
        void EndDesktopDrop(){desktopDrop?.Dispose();desktopDrop=null;if((Native.GetAsyncKeyState(1)&0x8000)==0)c.SetExternalFileDrag(false);UpdateFooter();}
        void DesktopDrag(DragEventArgs a,bool enter){
            if(a.Handled||a.Data.GetDataPresent("MacDock.Entry")&&((uint)a.KeyStates&12)==0)return;
            if(!a.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if(a.Data.GetDataPresent(DataFormats.Bitmap))
                {
                    c.SetExternalFileDrag(true);
                    a.Effects=DragDropEffects.Copy;
                    ShowFileStatus("松手保存图片到桌面");
                    a.Handled=true;
                }
                return;
            }
            if(!a.Data.GetDataPresent("MacDock.Entry"))
            {
                c.SetExternalFileDrag(true);
                a.Effects=Controller.ImportEffect(a.KeyStates,a.AllowedEffects);
                if(a.Effects==DragDropEffects.None){UpdateFooter();a.Handled=true;return;}
                bool copy=a.Effects==DragDropEffects.Copy;
                a.Effects=copy?DragDropEffects.Copy:DragDropEffects.Move;
                ShowFileStatus(copy?"松手复制到桌面":"松手移动到桌面");
                a.Handled=true;
                return;
            }
            var dest=c.OverrideDesktopFolder??Desktop.UserFolder;
            if(desktopDrop==null){desktopDrop=new ShellDrop(dest,(string[])a.Data.GetData(DataFormats.FileDrop));enter=true;}
            Native.GetCursorPos(out var p);a.Effects=(DragDropEffects)(enter?desktopDrop.Enter((uint)a.KeyStates,p,(uint)a.AllowedEffects):desktopDrop.Over((uint)a.KeyStates,p,(uint)a.AllowedEffects));a.Handled=true;
        }
        DragEnter+=(_,a)=>DesktopDrag(a,true);DragOver+=(_,a)=>DesktopDrag(a,false);DragLeave+=(_,_)=>EndDesktopDrop();Unloaded+=(_,_)=>EndDesktopDrop();
        Drop += (_, a) =>
        {
            if(a.Handled)return;
            if(a.Data.GetDataPresent("MacDock.Entry")&&((uint)a.KeyStates&12)==0)
            {
                if(a.Data.GetData("MacDock.Origin") as string=="dock")
                {var entry=c.Store.Data.Entries.FirstOrDefault(e=>e.Id==(string)a.Data.GetData("MacDock.Entry"));if(entry?.IsAlias==true){entry.Source="manual";entry.Origin="user-added";c.Changed();a.Effects=DragDropEffects.Link;}a.Handled=true;}
                return;
            }
            if(a.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if(!a.Data.GetDataPresent("MacDock.Entry"))
                {
                    a.Effects=c.CommitExternalFileDrop(a.Data,Controller.ImportEffect(a.KeyStates,a.AllowedEffects));
                    a.Handled=true;
                    EndDesktopDrop();
                    return;
                }
                var wasBusy=c.Busy;
                c.BeginInternalDropProtection();
                try{var targetDir=c.OverrideDesktopFolder??Desktop.UserFolder;if(desktopDrop==null){desktopDrop=new ShellDrop(targetDir,(string[])a.Data.GetData(DataFormats.FileDrop));Native.GetCursorPos(out var enterPoint);desktopDrop.Enter((uint)a.KeyStates,enterPoint,(uint)a.AllowedEffects);}Native.GetCursorPos(out var p);c.Busy=true;a.Effects=(DragDropEffects)desktopDrop.Drop((uint)a.KeyStates,p,(uint)a.AllowedEffects);a.Handled=true;c.SyncDesktopNow();}
                finally{EndDesktopDrop();c.SetExternalFileDrag(false);c.EndInternalDropProtection(true);c.Busy=wasBusy;}
                return;
            }
            if(a.Data.GetDataPresent(DataFormats.Bitmap))
            {
                bool ok=c.SaveDroppedBitmapToDesktop(a.Data);
                a.Effects=ok?DragDropEffects.Copy:DragDropEffects.None;
                a.Handled=true;
                EndDesktopDrop();
            }
        };
        PreviewKeyDown += OnKey;
        PreviewKeyUp+=OnGridKeyUp;
        PreviewMouseRightButtonDown += (_, _) => Native.SuppressAppStartingCursor();
        PreviewMouseRightButtonUp += (_, _) => Native.RestoreSystemCursors();
        scroll.IsKeyboardFocusedChanged+=(_,_)=>{if(!scroll.IsKeyboardFocused)pendingFileKey=Key.None;ApplySelectionVisuals();};
        bool marqueeActive=false;Point marqueeStart=default;IDisposable? marqueeInteraction=null;
        static bool IsScrollBarHit(DependencyObject? d){while(d!=null){if(d is System.Windows.Controls.Primitives.ScrollBar)return true;d=d is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);}return false;}
        void EndMarquee()
        {
            if(!marqueeActive)return;
            marqueeActive=false;
            marqueeInteraction?.Dispose();marqueeInteraction=null;
            marqueeBox.Visibility=Visibility.Collapsed;
            if(scroll.IsMouseCaptured)scroll.ReleaseMouseCapture();
            if(selected.Count>0)FocusGrid();
        }
        scroll.PreviewMouseLeftButtonDown += (_, a) =>
        {
            var posInScroll = a.GetPosition(scroll);
            bool insideGrid = posInScroll.X >= 0 && posInScroll.Y >= 0 && posInScroll.X <= scroll.ViewportWidth && posInScroll.Y <= scroll.ViewportHeight;
            if (insideGrid && FindTile(a.OriginalSource as DependencyObject) == null && !IsScrollBarHit(a.OriginalSource as DependencyObject))
            {
                ClearSelection(); Keyboard.ClearFocus();
                marqueeStart=a.GetPosition(stage);
                marqueeActive=scroll.CaptureMouse();
                if(marqueeActive)marqueeInteraction=c.HoldDrawerInteraction();
            }
        };
        scroll.PreviewMouseMove += (_, a) =>
        {
            if(!marqueeActive || a.LeftButton!=MouseButtonState.Pressed)return;
            var cur=a.GetPosition(stage);
            double x=Math.Min(marqueeStart.X,cur.X),y=Math.Min(marqueeStart.Y,cur.Y);
            double w=Math.Abs(cur.X-marqueeStart.X),h=Math.Abs(cur.Y-marqueeStart.Y);
            if(w<5&&h<5)return;
            marqueeBox.Width=w;marqueeBox.Height=h;
            Canvas.SetLeft(marqueeBox,x);Canvas.SetTop(marqueeBox,y);
            marqueeBox.Visibility=Visibility.Visible;
            var rect=new Rect(x,y,w,h);
            selected.Clear();
            foreach(var entry in visibleOrder)
            {
                if(!tiles.TryGetValue(entry.Id,out var tile)||tile.ActualWidth<=0)continue;
                var tl=tile.TransformToAncestor(stage).Transform(new Point(0,0));
                if(new Rect(tl,new Size(tile.ActualWidth,tile.ActualHeight)).IntersectsWith(rect))
                {
                    selected.Add(entry.Id);
                    anchor??=entry;
                    cursorId=entry.Id;
                }
            }
            ApplySelectionVisuals();
        };
        scroll.PreviewMouseLeftButtonUp += (_, _) => EndMarquee();
        scroll.LostMouseCapture += (_, _) => EndMarquee();
        IsVisibleChanged+=(_,_)=>{if(!IsVisible)EndMarquee();};
        var background = new ContextMenu();
        background.Opened += (_, _) =>
        {
            owner.MenuOpen = true; background.Items.Clear();
            void Add(string label, string? hint, Action action, bool enabled = true)
            {
                var item = new MenuItem { Header = MenuHeader(label, hint), IsEnabled = enabled };
                item.Click += (_, _) => action();
                background.Items.Add(item);
            }
            Add("粘贴到桌面", "Ctrl+V", c.PasteToDesktop, c.CanPaste);
            Add("全选", "Ctrl+A", SelectAll, visibleOrder.Count > 0);
            background.Items.Add(new Separator());
            Add("刷新", "F5", () => c.RefreshDesktop());
            Add("添加文件夹…", null, () => c.PickFolder(false));
            Add("添加文件或程序…", null, () => c.PickFiles(false));
            background.Items.Add(new Separator());
            Add("设置…", null, c.Settings);
        };
        background.Closed += (_, _) => owner.MenuOpen = false;
        ContextMenu = background;
        ContextMenuOpening+=(_,a)=>
        {
            if(a.Handled)return;
            a.Handled=true;
            if(a.CursorLeft<0&&a.CursorTop<0&&TryKeyboardMenu())return;
            c.MenuOpen=true;Dispatcher.BeginInvoke(c.ShowDesktopBackground);
        };
        SourceInitialized+=(_,_)=>HwndSource.FromHwnd(Handle).AddHook((nint hwnd,int message,nint wp,nint lp,ref bool handled)=>
        {
            if(message==0x007B&&lp==-1&&TryKeyboardMenu())handled=true;
            return 0;
        });
        Deactivated += (_, _) => c.RequestDrawerFocusCheck();
        DpiChanged += (_, _) => { InvalidateDpiCache(); InvalidatePrebakedSurface(); ApplyLayout(); Render(); if (IsVisible && !IsDeforming) RefreshMaterial(); };
        Native.DropPreviewHelper.AttachWindow(this, () => EndDesktopDrop());
        scroll.ScrollChanged += (_, e) =>
        {
            if(Math.Abs(e.VerticalChange)<=.01)return;
            InvalidatePrebakedSurface(scheduleIdleWarm:false);
            scrollBakeIdle.Stop();
            if(IsVisible&&!deforming)scrollBakeIdle.Start();
        };
        scrollBakeIdle.Tick+=(_,_)=>{scrollBakeIdle.Stop();c.ResumeDeferredDesktopSync();WarmPrebakedSurface();};
        Closed+=(_,_)=>scrollBakeIdle.Stop();
        void OnThemeChanged() => InvalidatePrebakedSurface();
        Theme.Changed += OnThemeChanged;
        Closed += (_, _) => { windowClosed = true; Theme.Changed -= OnThemeChanged; };
        ApplyLayout();
    }

    static Grid MenuHeader(string label, string? hint)
    {
        var grid = new Grid { Width = 190, Background = Brushes.Transparent };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(Theme.Text(label, 12.5));
        if (hint != null) { var right = Theme.Text(hint, 11, true); right.Opacity = .62; right.Margin = new Thickness(16, 0, 0, 0); Grid.SetColumn(right, 1); grid.Children.Add(right); }
        return grid;
    }

    (double TileW, double GapX, double GridW, double Chrome) GridMetrics(Layout l)
    {
        double scale = Math.Max(1, owner.Scale);
        double chrome = Math.Round(l.Chrome * scale) / scale;
        double panelW = Math.Round(l.DrawerWidth * scale) / scale;
        double innerW = Math.Max(0, panelW - 2 * chrome);
        int cols = Math.Max(1, l.Columns);
        double tileW = Math.Floor(l.DrawerTile * scale) / scale;
        double rawGap = cols > 1 ? Math.Floor(((innerW - l.ScrollReserve - cols * tileW) / (cols - 1)) * scale) / scale : 0;
        double gapX = Math.Max(0, rawGap);
        double gridW = cols * tileW + Math.Max(0, cols - 1) * gapX;
        return (tileW, gapX, gridW, chrome);
    }

    public void ApplyLayout()
    {
        var l = owner.Layout;
        if(l==appliedLayout&&owner.Scale==appliedScale)return;
        if(IsDeforming)owner.SettleDrawerMotion();
        pendingAnchor??=CaptureScrollAnchor();
        InvalidateDpiCache();
        appliedLayout=l;appliedScale=owner.Scale;
        Width = Math.Round(l.DrawerWidth*owner.Scale)/owner.Scale+2*MaterialInset; Height = FrameHeight+2*MaterialInset;
        UseFlatSurface();
        var (_, _, gridW, chrome) = GridMetrics(l);
        scroll.Height = l.GridHeight;
        main.Margin = new Thickness(chrome, l.DrawerPadTop, chrome, l.DrawerPadBottom);
        search.Height = l.SearchHeight;
        search.FontSize=placeholder.FontSize=Math.Clamp(13*l.Unit,13,15);count.FontSize=Math.Clamp(12*l.Unit,12,14);
        toolbar.Height=l.ToolbarHeight;searchHost.Height=l.SearchHeight;footer.Height=l.FooterHeight;
        gap1.Height=0;gap2.Height=16*l.Unit;gap3.Height=10*l.Unit;
        searchHost.Width=Math.Clamp(PanelWidth-chrome*2-210,0,280);
        groups.Width = owner.Store.Data.ListView ? double.NaN : gridW;
        groups.HorizontalAlignment = owner.Store.Data.ListView ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        PlaceScrollChrome(gridW,chrome);
        QueueAnchorRestore();
        InvalidatePrebakedSurface();
    }

    void PlaceScrollChrome(double gridWidth,double chrome)
    {
        double inner=PanelWidth-2*chrome;
        double contentRight=owner.Store.Data.ListView?PanelWidth-chrome-12:chrome+(inner+gridWidth)/2;
        double axis=Math.Round((PanelWidth+contentRight)*.5*owner.Scale)/owner.Scale;
        scroll.PlaceOverlayBar(PanelWidth-chrome-axis);
        actionsPanel.Margin=new Thickness(0,0,PanelWidth-chrome-axis-addFileBtn.Width/2,0);
    }

    public void Prepare() { ApplyLayout(); Render(); }
    internal void PrepareForOpen()
    {
        ApplyLayout();
        RestoreScrollAnchor();
        scrollBakeIdle.Stop();
        WarmPrebakedSurface();
    }
    internal void FinishScrollForClose()
    {
        scroll.StopWheel();
        scrollBakeIdle.Stop();
    }

    ScrollAnchor? CaptureScrollAnchor()
    {
        if(visibleOrder.Count==0||!groups.IsArrangeValid)return null;
        var order=visibleOrder.Select(e=>e.Id).ToArray();
        double offset=scroll.VerticalOffset;
        double closest=double.PositiveInfinity;
        int index=0;
        double inset=0;
        for(int i=0;i<order.Length;i++)
        {
            if(!TryTileY(order[i],out double y)||y+tiles[order[i]].ActualHeight<=0)continue;
            if(y>=closest-.01)continue;
            closest=y;index=i;inset=y;
        }
        return new ScrollAnchor(order,index,inset,offset);
    }

    bool TryTileY(string id,out double y)
    {
        y=0;
        if(!tiles.TryGetValue(id,out var tile)||tile.ActualHeight<=0||!scroll.IsAncestorOf(tile))return false;
        y=tile.TransformToAncestor(scroll).Transform(new Point()).Y;
        return true;
    }

    void QueueAnchorRestore()
    {
        if(anchorQueued||pendingAnchor==null)return;
        anchorQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            anchorQueued=false;
            RestoreScrollAnchor();
        }));
    }

    void RestoreScrollAnchor()
    {
        if(pendingAnchor is not {} anchor||windowClosed)return;
        pendingAnchor=null;
        UpdateLayout();
        double offset=anchor.Offset;
        for(int distance=0;distance<anchor.Order.Length;distance++)
        {
            int next=anchor.Index+distance,prior=anchor.Index-distance;
            if((next<anchor.Order.Length&&TryTileY(anchor.Order[next],out double y))||
                (prior>=0&&TryTileY(anchor.Order[prior],out y)))
            {
                offset=scroll.VerticalOffset+y-anchor.Inset;
                break;
            }
        }
        scroll.StopWheel();
        scroll.ScrollToVerticalOffset(Math.Clamp(offset,0,scroll.ScrollableHeight));
        UpdateLayout();
    }
    void UseFlatSurface()
    {
        stage.Clip=null;
        ConfigureSurfaceGeometry(1, clipToStage: true);
        genie?.DetachShadow();
        if(meshAttached)
        {
            stage.Children.Clear();genie!.Detach();meshAttached=false;
            RemoveLogicalChild(Surface);stage.Children.Add(Surface!);
        }
        ReleaseBakedGenieSurface();
        Surface!.Width=PanelWidth;Surface.Height=PanelHeight;
        Surface.HorizontalAlignment=HorizontalAlignment.Left;Surface.VerticalAlignment=VerticalAlignment.Top;
    }
    internal bool ChromeWithinSurface=>new FrameworkElement[]{toolbar,searchHost,footer}.All(e=>
    {var r=e.TransformToAncestor(Surface!).TransformBounds(new Rect(e.RenderSize));return r.Left>=0&&r.Top>=0&&r.Right<=Surface!.ActualWidth+.5&&r.Bottom<=Surface.ActualHeight+.5;});

    void UpdateModeVisuals()
    {
        updating = true;
        foreach (var (name, radio) in modes)
        {
            var active = name == owner.Store.Data.Mode;
            radio.IsChecked = active;
            if (modeGlyphs.TryGetValue(name, out var glyph)) glyph.Stroke = active ? Theme.Ink : Theme.Muted;
        }
        updating = false;
    }

    // ---------------------------------------------------------------- selection
    internal int SelectedCount => selected.Count;
    internal IReadOnlyList<Entry> Selection => visibleOrder.Where(e => selected.Contains(e.Id)).ToList();
    internal bool IsSelected(Entry e) => selected.Contains(e.Id);

    internal void FocusGrid() => scroll.Focus();
    bool Editing => search.IsKeyboardFocusWithin;
    internal bool TextInputActive=>Editing;
    string? cursorId;

    bool TryKeyboardMenu()
    {
        if(!scroll.IsKeyboardFocused)return false;
        if(!owner.MenuOpen&&(cursorId??Selection.FirstOrDefault()?.Id) is string id&&tiles.TryGetValue(id,out var tile))OpenItemMenu(tile,true);
        return true;
    }

    internal void OpenItemMenu(Border tile,bool keyboard)
    {
        if(tile.Tag is not Entry entry)return;
        var selection=Selection;
        if(selection.Count==0)selection=new[]{entry};
        bool sameParent=selection.Count==1||selection.All(e=>!e.IsVirtual)&&selection.Select(e=>Path.GetDirectoryName(e.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count()==1;
        owner.MenuOpen=true;
        Dispatcher.BeginInvoke(()=>
        {
            if(!IsVisible||!tile.IsLoaded){owner.MenuOpen=false;return;}
            if(!selection.Any(e=>e.IsAlias)&&sameParent)
            {
                Native.Point? position=null;
                if(keyboard)
                {
                    owner.SettleDrawerMotion();
                    var p=tile.PointToScreen(new Point(tile.ActualWidth*.5,tile.ActualHeight));
                    position=new Native.Point((int)p.X,(int)p.Y);
                }
                owner.ShowNativeMenu(selection,position);
                if(IsActive)FocusGrid();
            }
            else if(tile.ContextMenu is ContextMenu menu)
            {
                menu.PlacementTarget=tile;
                menu.Placement=keyboard?System.Windows.Controls.Primitives.PlacementMode.Bottom:System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen=true;
            }
            else owner.MenuOpen=false;
        });
    }

    internal void ClickSelect(Entry e, bool ctrl, bool shift)
    {
        cursorId=e.Id;
        if (shift && anchor != null)
        {
            var a = visibleOrder.FindIndex(x => x.Id == anchor.Id);
            var b = visibleOrder.FindIndex(x => x.Id == e.Id);
            if (a >= 0 && b >= 0)
            {
                if(!ctrl)selected.Clear();
                for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) selected.Add(visibleOrder[i].Id);
                ApplySelectionVisuals(); return;
            }
        }
        if (ctrl)
        {
            if (!selected.Remove(e.Id)) selected.Add(e.Id);
        }
        else { selected.Clear(); selected.Add(e.Id); }
        anchor = e;
        ApplySelectionVisuals();
    }

    internal void SelectOnly(Entry e) { selected.Clear(); selected.Add(e.Id); anchor = e; cursorId=e.Id; ApplySelectionVisuals(); }
    internal void SelectPaths(IReadOnlyList<string> paths,bool ensureVisible=true)
    {
        var set=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
        selected.Clear();anchor=null;cursorId=null;
        foreach(var e in visibleOrder)
        {
            if(!e.IsVirtual&&set.Contains(e.Path)){selected.Add(e.Id);anchor??=e;cursorId=e.Id;}
        }
        ApplySelectionVisuals();
        if(ensureVisible&&cursorId!=null)EnsureVisible(cursorId);
    }
    internal void SelectAll() { selected.Clear(); foreach (var e in visibleOrder) selected.Add(e.Id); cursorId??=visibleOrder.FirstOrDefault()?.Id; ApplySelectionVisuals(); }
    internal void ClearSelection()
    {
        bool had = selected.Count > 0 || anchor != null || cursorId != null;
        selected.Clear(); anchor = null; cursorId = null;
        if (had) ApplySelectionVisuals();
        else UpdateFooter();
    }

    internal Entry? DropTargetEntry { get; private set; }
    Border? dropTargetTile;
    static void ScaleTileIcon(Border tile, double scale, bool reduced)
    {
        if (tile.Child is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is Grid icon && icon.RenderTransform is ScaleTransform st)
        {
            Anim.Retarget(st, ScaleTransform.ScaleXProperty, scale, reduced);
            Anim.Retarget(st, ScaleTransform.ScaleYProperty, scale, reduced);
        }
    }
    internal void SetDropTarget(Entry? target)
    {
        if (DropTargetEntry?.Id == target?.Id && (target == null || dropTargetTile != null)) return;
        var previous = dropTargetTile;
        dropTargetTile = null;
        DropTargetEntry = target;
        if (previous != null)
        {
            previous.BorderThickness = new Thickness(1);
            ScaleTileIcon(previous, 1.0, owner.Store.Data.ReducedMotion);
            PaintTile(previous);
        }
        if (target != null && tiles.TryGetValue(target.Id, out var next))
        {
            dropTargetTile = next;
            next.Background = Theme.FillActive;
            next.BorderBrush = new SolidColorBrush(Theme.Dark ? Color.FromRgb(59, 158, 255) : Color.FromRgb(0, 120, 212));
            next.BorderThickness = new Thickness(2);
            ScaleTileIcon(next, 1.14, owner.Store.Data.ReducedMotion);
        }
    }
    internal Entry? HitDropTarget(Point surfacePoint, IReadOnlyCollection<string> draggedIds)
    {
        if (Surface == null) return null;
        foreach (var entry in visibleOrder)
        {
            if (draggedIds.Contains(entry.Id) || !DockWindow.AcceptsFileDropTarget(entry)) continue;
            if (!tiles.TryGetValue(entry.Id, out var tile) || tile.ActualWidth <= 0 || tile.ActualHeight <= 0) continue;
            var topLeft = tile.TransformToAncestor(Surface).Transform(new Point(0, 0));
            var bounds = new Rect(topLeft, new Size(tile.ActualWidth, tile.ActualHeight));
            double cx = bounds.Left + bounds.Width * 0.5, cy = bounds.Top + bounds.Height * 0.5;
            if (Math.Abs(surfacePoint.X - cx) <= bounds.Width * 0.325 && Math.Abs(surfacePoint.Y - cy) <= bounds.Height * 0.325)
                return entry;
        }
        return null;
    }

    internal void PaintTile(Border tile)
    {
        if(tile==dropTargetTile)return;
        if(tile.Tag is not Entry entry)return;
        bool singleClick=!owner.Store.Data.DoubleClickToOpen;
        bool selectedTile=selected.Contains(entry.Id),focusedTile=scroll.IsKeyboardFocused&&cursorId==entry.Id,hovered=tile.IsMouseOver;
        tile.Cursor=singleClick?Cursors.Hand:Cursors.Arrow;
        tile.Background=selectedTile?Theme.FillActive:hovered?(singleClick?Theme.FillHover:Theme.FillSoft):Brushes.Transparent;
        tile.BorderBrush=focusedTile?Theme.FocusEdge:selectedTile?Theme.SelectionEdge:Brushes.Transparent;
        System.Windows.Automation.AutomationProperties.SetItemStatus(tile,selectedTile?"已选择":"");
        if(tile.Child is StackPanel panel)
        {
            if(panel.Children[0] is Grid icon)icon.Opacity=owner.IsCut(entry)?.45:1;
            foreach(var label in panel.Children.OfType<TextBlock>())label.TextDecorations=singleClick&&hovered?TextDecorations.Underline:null;
        }
    }

    internal void ApplySelectionVisuals()
    {
        foreach(var tile in tiles.Values)PaintTile(tile);
        UpdateFooter();
        InvalidatePrebakedSurface();
    }

    void UpdateFooter()
    {
        var total = owner.Store.Data.Entries.Count(e=>e.InDrawer);
        var q = search.Text.Trim();
        count.Text = selected.Count > 0
            ? $"已选择 {selected.Count} 项 · Ctrl+C 复制 · Delete 删除"
            : q.Length > 0 ? $"{visibleOrder.Count} / {total} 个项目"
            : owner.Store.Data.Entries.Any(e => e.Pinned) ? $"{total} 个项目"
            : $"{total} 个项目 · 选中后可在右下角固定到 Dock";
        count.Opacity = .85;
    }

    internal void ShowFileStatus(string message)=>count.Text=message;

    // ---------------------------------------------------------------- keyboard
    Key pendingFileKey;
    bool pendingPermanentDelete;
    IReadOnlyList<Entry> pendingFileEntries=Array.Empty<Entry>();

    // Explicit selection (including multi-selection) wins. Hover is a fallback,
    // never a new persistent selection, and is resolved from the live tile hit test.
    IReadOnlyList<Entry> FileCommandTargets()
    {
        var entries=Selection;
        if(entries.Count>0)return entries;
        var tile=tiles.Values.FirstOrDefault(t=>t.IsMouseOver&&t.IsVisible);
        return tile?.Tag is Entry entry?new[]{entry}:Array.Empty<Entry>();
    }

    void OnGridKeyUp(object sender,KeyEventArgs e)
    {
        if(pendingFileKey==Key.None||e.Key!=pendingFileKey)return;
        var command=pendingFileKey;
        pendingFileKey=Key.None;
        if(!scroll.IsKeyboardFocused||owner.MenuOpen||owner.Busy)return;
        e.Handled=true;
        // Start the Shell dialog after the physical release, so it cannot swallow the
        // release and make the next independent press look like a held-down repeat.
        var entries=pendingFileEntries;
        pendingFileEntries=Array.Empty<Entry>();
        if(command==Key.Delete)owner.DeleteEntries(entries,pendingPermanentDelete);
        else if(entries.Count==1)owner.RenameEntry(entries[0]);
    }

    void Navigate(Key key,bool ctrl,bool shift)
    {
        if(visibleOrder.Count==0)return;
        int index=visibleOrder.FindIndex(e=>e.Id==cursorId);
        int next=key switch {Key.Home=>0,Key.End=>visibleOrder.Count-1,Key.Left=>index-1,Key.Right=>index+1,_=>index};
        if(index>=0&&key is Key.Up or Key.Down)
        {
            var tile=tiles[visibleOrder[index].Id];
            Point Center(Border view)=>view.TranslatePoint(new Point(view.ActualWidth/2,view.ActualHeight/2),groups);
            var origin=Center(tile);
            int sign=key==Key.Up?-1:1;
            var rows=visibleOrder.Select((e,i)=>(Index:i,Point:Center(tiles[e.Id]))).Where(x=>sign*(x.Point.Y-origin.Y)>tile.ActualHeight*.45).ToArray();
            if(rows.Length>0)
            {
                var row=rows.Min(x=>sign*(x.Point.Y-origin.Y));
                next=rows.Where(x=>sign*(x.Point.Y-origin.Y)<=row+tile.ActualHeight*.4).MinBy(x=>Math.Abs(x.Point.X-origin.X)).Index;
            }
        }
        var entry=visibleOrder[Math.Clamp(next,0,visibleOrder.Count-1)];
        if(shift){anchor??=index>=0?visibleOrder[index]:entry;ClickSelect(entry,ctrl,true);}
        else if(ctrl){cursorId=entry.Id;ApplySelectionVisuals();}
        else SelectOnly(entry);
        EnsureVisible(entry.Id);
    }

    void OnKey(object sender, KeyEventArgs a)
    {
        var key=a.Key==Key.System?a.SystemKey:a.Key;
        if(a.Key==Key.Escape&&PointerSort.Active is {} sort){sort.End(false);a.Handled=true;return;}
        if(owner.MenuOpen||owner.Busy||owner.DrawerInteractionActive)return;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (a.Key == Key.Escape)
        {
            pendingFileKey=Key.None;
            if(Editing)FocusGrid();
            else if(selected.Count>0||cursorId!=null)ClearSelection();
            else owner.CloseDrawer(keepDock:true,force:true);
            a.Handled = true; return;
        }
        if(a.Key==Key.F&&ctrl){FocusSearch();a.Handled=true;return;}
        if(a.Key==Key.F6&&!Editing){owner.FocusDockKeyboard();a.Handled=true;return;}
        if (Editing)
        {
            if (a.Key == Key.Down && search.Text.Length == 0) { FocusGrid(); if (visibleOrder.Count > 0) Navigate(Key.Home,false,false); a.Handled = true; }
            return;
        }
        if (a.Key == Key.A && ctrl && !Editing) { FocusGrid(); SelectAll(); a.Handled = true; return; }
        bool fileCommand=key is Key.Delete or Key.F2||ctrl&&key is Key.C or Key.X or Key.V;
        if(!scroll.IsKeyboardFocused&&fileCommand&&IsActive&&FileCommandTargets().Count>0)FocusGrid();
        if(!scroll.IsKeyboardFocused)return;
        switch (key)
        {
            case Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End: Navigate(key,ctrl,shift);a.Handled=true;break;
            case Key.Space when !a.IsRepeat && cursorId!=null && tiles.TryGetValue(cursorId,out var focus): ClickSelect((Entry)focus.Tag,true,false);a.Handled=true;break;
            case Key.A when ctrl: SelectAll(); a.Handled = true; break;
            case Key.C when ctrl: owner.CopyEntries(FileCommandTargets(), false); a.Handled = true; break;
            case Key.X when ctrl: owner.CopyEntries(FileCommandTargets(), true); a.Handled = true; break;
            case Key.V when ctrl: owner.PasteToDesktop(); a.Handled = true; break;
            case Key.Delete or Key.F2:
                if(pendingFileKey==Key.None){pendingFileKey=key;pendingPermanentDelete=shift;pendingFileEntries=FileCommandTargets();}
                a.Handled=true;break;
            case Key.F10 when shift && !a.IsRepeat:
            case Key.Apps when !a.IsRepeat:
                a.Handled=TryKeyboardMenu();break;
            case Key.F5: owner.RefreshDesktop(); a.Handled = true; break;
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Alt): owner.ShowProperties(Selection);a.Handled=true;break;
            case Key.Enter when !a.IsRepeat: if (Selection.Count > 0) owner.OpenEntries(Selection); a.Handled = true; break;
        }
    }

    // ---------------------------------------------------------------- rendering
    internal IReadOnlyList<FrameworkElement> SortTiles(bool manual)=>visibleOrder.Where(e=>(e.Source=="manual")==manual).Where(e=>tiles.ContainsKey(e.Id)).Select(e=>(FrameworkElement)tiles[e.Id]).ToArray();
    internal void ApplyCustomOrder(){Render();groups.BeginAnimation(OpacityProperty,null);groups.Opacity=1;}
    public void Render()
    {
        var current= pendingAnchor??CaptureScrollAnchor();
        UpdateModeVisuals();
        if (Surface is Border card) card.CornerRadius = new CornerRadius(MaterialRadius);
        dropTargetTile = null; DropTargetEntry = null;
        var l = owner.Layout;
        var q = search.Text.Trim();
        if(!string.Equals(q,renderedQuery,StringComparison.Ordinal))
        {
            if(renderedQuery.Length==0)unfilteredAnchor=current;
            pendingAnchor=q.Length==0?unfilteredAnchor:new ScrollAnchor(Array.Empty<string>(),0,0,0);
            renderedQuery=q;
        }
        else pendingAnchor=current;
        bool listView = owner.Store.Data.ListView;
        var entries = owner.Store.Data.Entries.Where(e => e.InDrawer && (q.Length == 0 || e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) || e.Path.Contains(q, StringComparison.CurrentCultureIgnoreCase))).ToList();
        var organized = Alphabet.Groups(entries.Where(e => e.Source != "manual"), owner.Store.Data.Mode).Select(g => (Key: "auto:" + g.Key, Title: g.Key, Items: g.ToArray()))
            .Concat(Alphabet.Groups(entries.Where(e => e.Source == "manual"), owner.Store.Data.Mode).Select(g => (Key: "manual:" + g.Key, Title: "手动添加 · 快捷入口" + (g.Key.Length > 0 ? " · " + g.Key : ""), Items: g.ToArray())))
            .ToList();

        var (tileW, gapX, gridW, _) = GridMetrics(l);
        if (renderedListView != listView || Math.Abs(renderedUnit - l.Unit) > 0.001 || Math.Abs(renderedScale - owner.Scale) > 0.001)
        {
            groups.Children.Clear();
            tiles.Clear();
            renderedListView = listView;
            renderedUnit = l.Unit;
            renderedScale = owner.Scale;
        }
        groups.Width = listView ? double.NaN : gridW;
        groups.HorizontalAlignment = listView ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;

        // Build lookup of existing tile controls by file path (primary) and entry ID (fallback)
        var oldByPath = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        var oldById = new Dictionary<string, Border>(StringComparer.Ordinal);
        foreach (var kv in tiles)
        {
            oldById[kv.Key] = kv.Value;
            if (kv.Value.Tag is Entry oldEntry && !string.IsNullOrEmpty(oldEntry.Path))
                oldByPath.TryAdd(oldEntry.Path, kv.Value);
        }

        var oldTitles = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        var oldWraps = new Dictionary<string, WrapPanel>(StringComparer.Ordinal);
        foreach (UIElement child in groups.Children)
        {
            if (child is TextBlock tb && tb.Tag is string tk) oldTitles.TryAdd(tk, tb);
            else if (child is WrapPanel wp && wp.Tag is string wk) oldWraps.TryAdd(wk, wp);
        }

        Border? FindReusableTile(Entry e)
        {
            if (!string.IsNullOrEmpty(e.Path) && oldByPath.TryGetValue(e.Path, out var byPath)) return byPath;
            return oldById.TryGetValue(e.Id, out var byId) ? byId : null;
        }

        var reusedTiles = new HashSet<Border>();
        var plannedTiles = new Dictionary<Entry, Border>();
        foreach (var group in organized)
        {
            foreach (var e in group.Items)
            {
                var reused = FindReusableTile(e);
                if (reused != null && reusedTiles.Add(reused))
                    plannedTiles[e] = reused;
            }
        }

        // Remove only deleted or filtered-out tiles from their parent WrapPanels
        foreach (var oldTile in oldById.Values)
        {
            if (!reusedTiles.Contains(oldTile) && oldTile.Parent is Panel parent)
                parent.Children.Remove(oldTile);
        }

        tiles.Clear();
        visibleOrder.Clear();
        var desiredGroupChildren = new List<UIElement>();

        foreach (var group in organized)
        {
            if (group.Title != "")
            {
                if (!oldTitles.TryGetValue(group.Key, out var title))
                {
                    title = Theme.Text(group.Title, 12, true, FontWeights.SemiBold);
                    title.Tag = group.Key;
                }
                else
                {
                    if (title.Text != group.Title) title.Text = group.Title;
                    title.Foreground = Theme.Ink;
                }
                title.Margin = new Thickness(l.GapX / 2, 14 * l.Unit, 0, 5 * l.Unit);
                title.Opacity = .9;
                desiredGroupChildren.Add(title);
            }

            if (!oldWraps.TryGetValue(group.Key, out var wrap))
            {
                wrap = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, Tag = group.Key };
            }
            wrap.Margin = new Thickness(0, 0, -gapX, 0);

            for (int i = 0; i < group.Items.Length; i++)
            {
                var e = group.Items[i];
                visibleOrder.Add(e);
                if (plannedTiles.TryGetValue(e, out var tile))
                {
                    tile.Tag = e;
                    System.Windows.Automation.AutomationProperties.SetName(tile, e.Name);
                    System.Windows.Automation.AutomationProperties.SetHelpText(tile, e.Name);
                    if (tile.Child is StackPanel panel)
                    {
                        var iconHost = panel.Children.OfType<Grid>().FirstOrDefault();
                        if (iconHost != null && (iconHost.Tag is not ValueTuple<string, double> bound ||
                            !string.Equals(bound.Item1, e.Path, StringComparison.OrdinalIgnoreCase) ||
                            Math.Abs(bound.Item2 - l.DrawerIcon) > 0.1))
                        {
                            iconHost.Children.Clear();
                            iconHost.Children.Add(Icons.View(owner.Icon(e.Path, l.DrawerIcon), l.DrawerIcon));
                            iconHost.Tag = (e.Path, l.DrawerIcon);
                            InvalidatePrebakedSurface();
                        }
                        foreach (var label in panel.Children.OfType<TextBlock>())
                            if (label.Text != e.Name) label.Text = e.Name;
                    }
                    if (listView)
                        ConfigureListViewTile(tile, l);
                    else
                    {
                        tile.Width = tileW;
                        tile.Height = l.DrawerTileHeight;
                        tile.Margin = new Thickness(0, 0, gapX, l.RowHeight - l.DrawerTileHeight);
                    }
                    if (tile.Parent == wrap)
                    {
                        int cur = wrap.Children.IndexOf(tile);
                        if (cur != i)
                        {
                            wrap.Children.RemoveAt(cur);
                            wrap.Children.Insert(i, tile);
                        }
                    }
                    else
                    {
                        if (tile.Parent is Panel oldParent) oldParent.Children.Remove(tile);
                        wrap.Children.Insert(i, tile);
                    }
                }
                else
                {
                    tile = (Border)owner.Dock.Tile(e, true);
                    if (listView) ConfigureListViewTile(tile, l);
                    else
                    {
                        tile.Width = tileW;
                        tile.Height = l.DrawerTileHeight;
                        tile.Margin = new Thickness(0, 0, gapX, l.RowHeight - l.DrawerTileHeight);
                    }
                    wrap.Children.Insert(i, tile);
                }
                tiles[e.Id] = tile;
            }
            while (wrap.Children.Count > group.Items.Length)
                wrap.Children.RemoveAt(wrap.Children.Count - 1);

            desiredGroupChildren.Add(wrap);
        }

        selected.RemoveWhere(id => !tiles.ContainsKey(id));
        if(cursorId!=null&&!tiles.ContainsKey(cursorId))cursorId=null;

        if (entries.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(0, 46 * l.Unit, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            empty.Children.Add(new Border
            {
                Width = 46 * l.Unit, Height = 46 * l.Unit, CornerRadius = new CornerRadius(13 * l.Unit),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                Child = Theme.Glyph(Theme.GSearch, 20 * l.Unit, Theme.Faint, 1.6), HorizontalAlignment = HorizontalAlignment.Center
            });
            var label = Theme.Text(q.Length == 0 ? "桌面上还没有可收纳的项目" : "没有匹配的项目", 13, true);
            label.TextAlignment = TextAlignment.Center; label.Margin = new Thickness(0, 14, 0, 0); label.HorizontalAlignment = HorizontalAlignment.Center;
            var hint = Theme.Text("把文件、文件夹或快捷方式拖到这里", 11.5, true);
            hint.TextAlignment = TextAlignment.Center; hint.Margin = new Thickness(0, 5, 0, 0); hint.HorizontalAlignment = HorizontalAlignment.Center; hint.Opacity = .75;
            empty.Children.Add(label); empty.Children.Add(hint);
            desiredGroupChildren.Add(empty);
        }

        // Incrementally patch groups.Children without destroying existing containers
        var desiredSet = new HashSet<UIElement>(desiredGroupChildren);
        for (int i = groups.Children.Count - 1; i >= 0; i--)
        {
            if (!desiredSet.Contains(groups.Children[i]))
                groups.Children.RemoveAt(i);
        }
        for (int i = 0; i < desiredGroupChildren.Count; i++)
        {
            var target = desiredGroupChildren[i];
            if (i < groups.Children.Count && ReferenceEquals(groups.Children[i], target))
                continue;
            int existingIdx = groups.Children.IndexOf(target);
            if (existingIdx >= 0) groups.Children.RemoveAt(existingIdx);
            groups.Children.Insert(i, target);
        }

        ApplySelectionVisuals();
        PlaceScrollChrome(gridW,GridMetrics(l).Chrome);
        QueueAnchorRestore();
        if (!IsVisible && meshAttached) Surface?.InvalidateVisual();
        groups.BeginAnimation(OpacityProperty, null);
        groups.Opacity = 1;
        InvalidatePrebakedSurface();
    }

    void ConfigureListViewTile(Border tile, Layout l)
    {
        tile.Width=Math.Max(200,PanelWidth-l.Chrome*2-12);tile.Height=54*l.Unit;tile.Margin=new Thickness(0,0,0,3);
        if(tile.Child is StackPanel row){row.Orientation=Orientation.Horizontal;row.HorizontalAlignment=HorizontalAlignment.Left;row.Margin=new Thickness(12,0,0,0);if(row.Children[0] is FrameworkElement icon){icon.Width=36;icon.Height=36;}foreach(var label in row.Children.OfType<TextBlock>()){label.Margin=new Thickness(18,0,0,0);label.MaxWidth=tile.Width-100;label.TextAlignment=TextAlignment.Left;label.VerticalAlignment=VerticalAlignment.Center;}}
    }

    static Border? FindTile(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Border border && border.Tag is Entry) return border;
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    internal void EnsureVisible(string id)
    {
        if(!tiles.TryGetValue(id,out var tile)) return;
        RestoreScrollAnchor();
        scroll.StopWheel();
        tile.BringIntoView();
        scroll.UpdateLayout();
    }

    internal Native.Point TileCenter(string id)
    {
        if(!tiles.TryGetValue(id,out var tile)) return default;
        var p=tile.PointToScreen(new Point(tile.ActualWidth/2,tile.ActualHeight/2));
        return new Native.Point((int)p.X,(int)p.Y);
    }

    internal double DesiredHeight()
    {
        var l = owner.Layout;
        return l.DrawerHeight(0,0);
    }

    // Drop the tile visual tree while hidden; it is rebuilt from the icon cache on open.
    public void ResetSelection() => ClearSelection();
    internal int RenderedGroups => groups.Children.Count;
    internal double SurfaceOpacity => ForegroundOpacity;
    internal double ScrollOffset => scroll.VerticalOffset;
    internal Border? TileControlForPath(string path) => tiles.Values.FirstOrDefault(t => t.Tag is Entry e && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    public void FocusSearch() => search.Focus();
}
