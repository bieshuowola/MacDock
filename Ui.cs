using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace MacDock;

// Short, interruptible window animations. DWM materials cannot fade with Window.Opacity,
// so real HWND movement plus content opacity is animated instead (the material follows the window).
internal static class Anim
{
    static readonly CubicEase shiftEase=CreateShiftEase();
    static CubicEase CreateShiftEase(){var ease=new CubicEase{EasingMode=EasingMode.EaseOut};ease.Freeze();return ease;}
    internal static void Retarget(Animatable visual,DependencyProperty property,double target,bool reduced)
    {
        double current=(double)visual.GetValue(property);
        visual.SetValue(property,target);
        if(reduced||!Allowed||Math.Abs(target-current)<.01){visual.BeginAnimation(property,null);return;}
        visual.BeginAnimation(property,new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(110))
            {EasingFunction=shiftEase,FillBehavior=FillBehavior.Stop});
    }
    public static int ActiveTimers;
    public static bool Allowed => SystemParameters.ClientAreaAnimation;
    public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseIn(double t) => t * t * t;
    public static double EaseInOut(double t) => t < .5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    // A critically bounded spring: one small overshoot, then settle. Opacity uses its own
    // monotonic curve; spring values never feed alpha or a continuously running physics loop.
    public static double DrawerSpring(double t)
    {
        double Raw(double x)=>1-Math.Exp(-7.5*x)*(Math.Cos(8*x)+7.5/8*Math.Sin(8*x));
        return t>=1?1:Raw(t)/Raw(1);
    }
    public static void Run(int ms, Action<double> step, Action? done = null, Func<bool>? cancelled = null, Func<double, double>? ease = null)
    {
        if (ms <= 0 || !Allowed) { if (cancelled?.Invoke() != true) { step(1); done?.Invoke(); } return; }
        ease ??= EaseOut;
        // Start at the first UI frame, after initial layout/resource work. Starting before
        // that work consumed almost the whole transition on the first cold drawer opening.
        var watch = new Stopwatch();
        double elapsed=0,last=0;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        ActiveTimers++;
        timer.Tick += (_, _) =>
        {
            if (cancelled?.Invoke() == true) { timer.Stop(); ActiveTimers--; return; }
            if(!watch.IsRunning)watch.Start();
            var now=watch.Elapsed.TotalMilliseconds;elapsed+=Math.Min(40,now-last);last=now;
            var raw = Math.Clamp(elapsed / ms, 0, 1);
            step(ease(raw));
            if (raw >= 1) { timer.Stop(); ActiveTimers--; done?.Invoke(); }
        };
        timer.Start();
    }
}

internal static class Theme
{
    sealed class Swatch:DependencyObject
    {
        internal static readonly DependencyProperty ValueProperty=DependencyProperty.Register("Value",typeof(Color),typeof(Swatch));
        public Color Value{get=>(Color)GetValue(ValueProperty);set=>SetValue(ValueProperty,value);}
    }
    static readonly Dictionary<SolidColorBrush,Swatch> swatches=new();
    public static FontFamily Ui { get; } = new("Segoe UI Variable Text, Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
    public static SolidColorBrush Ink { get; } = Make(Color.FromRgb(245, 247, 251));
    public static SolidColorBrush Muted { get; } = Make(Color.FromRgb(174, 184, 201));
    public static SolidColorBrush Faint { get; } = Make(Color.FromRgb(139, 149, 168));
    public static SolidColorBrush Hairline { get; } = Make(Color.FromArgb(38, 255, 255, 255));
    public static SolidColorBrush FillSoft { get; } = Make(Color.FromArgb(20, 255, 255, 255));
    public static SolidColorBrush FillHover { get; } = Make(Color.FromArgb(36, 255, 255, 255));
    public static SolidColorBrush FillActive { get; } = Make(Color.FromArgb(46, 255, 255, 255));
    public static SolidColorBrush SelectionEdge { get; } = Make(Color.FromArgb(64, 255, 255, 255));
    public static SolidColorBrush FocusEdge { get; } = Make(Color.FromArgb(90,255,255,255));

    public static bool Dark {get;private set;}=true;
    internal static event Action? Changed;
    static SolidColorBrush Make(Color c){var source=new Swatch{Value=c};var brush=new SolidColorBrush();System.Windows.Data.BindingOperations.SetBinding(brush,SolidColorBrush.ColorProperty,new System.Windows.Data.Binding("Value"){Source=source});swatches[brush]=source;return brush;}
    static void Set(SolidColorBrush brush,Color color)=>swatches[brush].Value=color;
    internal static bool SystemDark()
    {
        try{using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");return key?.GetValue("AppsUseLightTheme") is int value&&value==0;}catch{return false;}
    }
    internal static void Apply(string mode)
    {
        Dark=mode=="dark"||mode!="light"&&SystemDark();
        Color R(byte r,byte g,byte b)=>Color.FromRgb(r,g,b);
        Color A(byte a,byte r,byte g,byte b)=>Color.FromArgb(a,r,g,b);
        Set(Ink,Dark?R(245,247,251):R(28,35,47));Set(Muted,Dark?R(188,197,211):R(74,84,99));Set(Faint,Dark?R(151,163,182):R(96,108,125));
        Set(Hairline,Dark?A(30,255,255,255):A(24,27,40,58));Set(FillSoft,Dark?A(20,255,255,255):A(12,30,47,70));
        Set(FillHover,Dark?A(36,255,255,255):A(22,30,47,70));Set(FillActive,Dark?A(46,255,255,255):A(32,30,47,70));Set(SelectionEdge,Dark?A(64,255,255,255):A(65,66,117,190));
        Set(FocusEdge,Dark?A(100,240,244,250):A(100,55,67,82));
        if(SystemParameters.HighContrast){Set(Ink,SystemColors.WindowTextColor);Set(Muted,SystemColors.WindowTextColor);Set(Faint,SystemColors.WindowTextColor);}
        var resources=System.Windows.Application.Current.Resources;
        foreach(var pair in new[]{("Ink",Ink),("Muted",Muted),("Faint",Faint),("Hairline",Hairline),("FillSoft",FillSoft),("FillHover",FillHover),("FillActive",FillActive)})resources[pair.Item1]=pair.Item2;
        resources["SelectionEdge"]=SelectionEdge;
        resources["DialogBack"]=new SolidColorBrush(Dark?A(248,29,36,49):A(248,244,247,252));
        resources["ScrollThumb"]=new SolidColorBrush(Dark?A(115,235,241,250):A(105,38,49,67));
        resources["GlassSurface"]=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(0,1),GradientStops=new GradientStopCollection
        {
            new(Dark?A((byte)(Native.HostMaterial?195:32),25,32,45):A((byte)(Native.HostMaterial?155:50),255,255,255),0),
            new(Dark?A((byte)(Native.HostMaterial?200:16),25,32,45):A((byte)(Native.HostMaterial?145:16),248,251,255),.42),
            new(Dark?A((byte)(Native.HostMaterial?205:38),17,25,38):A((byte)(Native.HostMaterial?145:32),227,234,243),1)
        }};
        Changed?.Invoke();
    }
    public static Style Style(string key) => (Style)System.Windows.Application.Current.FindResource(key);

    public static TextBlock Text(string text, double size = 13, bool muted = false, FontWeight? weight = null) => new()
    {
        Text = text, FontSize = size, FontFamily = Ui, Foreground = muted ? Muted : Ink,
        FontWeight = weight ?? FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center
    };

    // One material for every surface. The edge is a very restrained top-to-bottom highlight
    // instead of a hard 1 px white ring, so the glass reads as depth rather than a plastic frame.
    public static Border Surface(UIElement child, double radius)
    {
        var surface=new Border{Child=child,CornerRadius=new CornerRadius(radius),BorderThickness=new Thickness(0),SnapsToDevicePixels=true};
        surface.SetResourceReference(Border.BackgroundProperty,"GlassSurface");return surface;
    }

    // Transparent hairline frame that sits above dialog content without covering it.
    public static Border Frame(double radius=Native.CornerRadius) => new()
    {
        CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1),
        BorderBrush = new LinearGradientBrush(Color.FromArgb(26, 255, 255, 255), Color.FromArgb(6, 255, 255, 255), 90),
        Background = Brushes.Transparent, IsHitTestVisible = false
    };

    // The scroll viewer builds its bar inside its own template, so the slim bar is attached
    // to the viewer's resources instead of relying on application-level implicit styles.
    public static void SlickBar(ScrollViewer viewer)
    {
        void Apply()
        {
            if (viewer.Template?.FindName("PART_VerticalScrollBar", viewer) is System.Windows.Controls.Primitives.ScrollBar bar)
            {
                bar.Style = Style("Bar");
                bar.Width = 6;
            }
        }
        viewer.ApplyTemplate();
        Apply();
        viewer.Loaded += (_, _) => Apply();
    }

    public static void Tip(FrameworkElement element, string text)
    {
        element.ToolTip = new ToolTip { Content = text };
        ToolTipService.SetInitialShowDelay(element, 480);
        ToolTipService.SetBetweenShowDelay(element, 120);
        ToolTipService.SetShowDuration(element, 8000);
    }

    // 24x24 line glyphs, scaled uniformly. Round caps keep small sizes from looking chunky.
    public static Path Glyph(string data, double size, Brush brush, double thickness = 1.7)
    {
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = false
        };
        RenderOptions.SetEdgeMode(path, EdgeMode.Unspecified);
        return path;
    }

    public const string GSearch = "M3.6,10.6 A7,7 0 1 0 17.6,10.6 A7,7 0 1 0 3.6,10.6 Z M15.6,15.6 L20.6,20.6";
    public const string GClose = "M6.6,6.6 L17.4,17.4 M17.4,6.6 L6.6,17.4";
    public const string GPlus = "M12,5.4 L12,18.6 M5.4,12 L18.6,12";
    public const string GFolderPlus = "M3,7.2 A1.7,1.7 0 0 1 4.7,5.5 H9.3 L11.3,7.7 H19.3 A1.7,1.7 0 0 1 21,9.4 V17.2 A1.7,1.7 0 0 1 19.3,18.9 H4.7 A1.7,1.7 0 0 1 3,17.2 Z M12,11.2 L12,15.8 M9.7,13.5 L14.3,13.5";
    public const string GList = "M5,7.5 L5.1,7.5 M5,12 L5.1,12 M5,16.5 L5.1,16.5 M8.6,7.5 L19,7.5 M8.6,12 L19,12 M8.6,16.5 L15.2,16.5";
    public const string GType = "M3.8,4.4 H9.6 V10.2 H3.8 Z M14.4,4.4 H20.2 V10.2 H14.4 Z M3.8,13.8 H9.6 V19.6 H3.8 Z M14.4,13.8 H20.2 V19.6 H14.4 Z";
    public const string GAlpha = "M6.4,19 L12,5 L17.6,19 M8.8,14.2 L15.2,14.2";

    public static FrameworkElement GridGlyph(double box, Brush brush, double gap = 2.2)
    {
        var cell = (box - gap * 2) / 3;
        var canvas = new Canvas { Width = box, Height = box, ClipToBounds = false, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
            {
                var square = new System.Windows.Shapes.Rectangle
                {
                    Width = cell, Height = cell, RadiusX = cell * .3, RadiusY = cell * .3, Fill = brush
                };
                Canvas.SetLeft(square, col * (cell + gap));
                Canvas.SetTop(square, row * (cell + gap));
                canvas.Children.Add(square);
            }
        return canvas;
    }
}

internal class GlassWindow : Window
{
    internal static string LastRawWheel="";
    readonly bool materialEnabled;
    HostGlass? hostGlass;
    internal bool MenuTracking;
    double materialReveal=1;
    double cachedDpiScale=1;
    GeniePose? geniePose;
    bool usesGenie;
    bool hostFailed;
    public nint Handle => new WindowInteropHelper(this).Handle;
    public bool MaterialAvailable { get; private set; }
    public double MaterialRadius=>Native.HostMaterial?(ShowActivated?26:16):Native.CornerRadius;
    internal virtual double MaterialInset=>0;
    protected virtual double MaterialHeight=>ActualHeight-2*MaterialInset;
    internal object? MaterialSnapshot=>hostGlass?.Snapshot();
    public GlassWindow(bool interactive = false, bool material = true)
    {
        materialEnabled=material;
        Icon=AppBrand.Image;
        IsVisibleChanged+=(_,_)=>hostGlass?.Sync(IsVisible);
        Theme.Changed+=RefreshMaterial;Closed+=(_,_)=>{Theme.Changed-=RefreshMaterial;hostGlass?.Dispose();};SizeChanged+=(_,_)=>RefreshMaterial();
        // Foreground stays crisp in WPF; a separate input-free composition surface below it
        // samples real outside windows. Both share physical bounds, visibility and opacity.
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = !material||Native.HostMaterial||(!interactive&&!Native.DocumentedMaterial)||Environment.GetEnvironmentVariable("MACDOCK_MATERIAL_BACKEND")=="accent-layered";
        if(this is DrawerWindow or DockWindow or RestShadow&&Native.HostMaterial)AllowsTransparency=false;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = interactive; Topmost = true;
        if(!interactive)Focusable=false;
        FontFamily = Theme.Ui; UseLayoutRounding = true; SnapsToDevicePixels = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Auto);
        if(material && Environment.GetEnvironmentVariable("MACDOCK_MATERIAL_BACKEND")=="documented-chrome")
            System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(-1),UseAeroCaptionButtons=false});
        if(material)
        {
            void RefreshAfterLayout()=>Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{if(IsVisible)RefreshMaterial();});
            Loaded+=(_,_)=>RefreshAfterLayout();Activated+=(_,_)=>RefreshAfterLayout();DpiChanged+=(_,_)=>{UpdateCachedDpi();RefreshAfterLayout();};
        }
        SourceInitialized += (_, _) =>
        {
            UpdateCachedDpi();
            HwndSource.FromHwnd(Handle).AddHook((nint h,int m,nint w,nint l,ref bool handled)=>{if(m==0x20A)LastRawWheel=$"{Title}:{h}; fg={Native.GetForegroundWindow()}";return 0;});
            HwndSource.FromHwnd(Handle).AddHook((nint h,int m,nint w,nint l,ref bool handled)=>
            {
                if(m==0x0020&&(MenuTracking||ShellMenu.ActivePopup!=0)){Native.SetArrowCursor();handled=true;return 1;}
                if(m==0x47&&!MenuTracking)
                {
                    hostGlass?.Sync(IsVisible);
                    PointerSort.SurfaceRepositioned(this);
                }
                return 0;
            });
            var ex = Native.GetWindowLongPtr(Handle, -20).ToInt64() | 0x80;
            if (!interactive) ex |= 0x08000000;
            Native.SetWindowLongPtr(Handle, -20, (nint)ex);
            if(!interactive)HwndSource.FromHwnd(Handle).AddHook((nint hwnd,int message,nint wp,nint lp,ref bool handled)=>
            {
                if(message==0x21){handled=true;return 3;} // MA_NOACTIVATE: deliver the click without activating or eating it.
                return 0;
            });
            if (material)
            {
                HwndSource.FromHwnd(Handle).CompositionTarget.BackgroundColor = Colors.Transparent;
                RefreshMaterial();
            }
            if(this is DrawerWindow or DockWindow or RestShadow&&Native.HostMaterial){HwndSource.FromHwnd(Handle).CompositionTarget.BackgroundColor=Colors.Transparent;int policy=1;Native.DwmSetWindowAttribute(Handle,2,ref policy,4);var margins=new Native.Margins{Left=-1,Top=-1,Right=-1,Bottom=-1};Native.DwmExtendFrameIntoClientArea(Handle,ref margins);}
        };
    }
    void UpdateCachedDpi()
    {
        if(Handle==0)return;
        uint dpi=Native.GetDpiForWindow(Handle);
        if(dpi>0)cachedDpiScale=dpi/96.0;
    }
    public virtual void RefreshMaterial()
    {
        if (!materialEnabled) return;
        if(Handle==0)return;
        UpdateCachedDpi();
        if(geniePose!=null){if(MaterialInset==0)GeniePose.SetInputRegion(Handle,geniePose,cachedDpiScale);int noFrame=1;Native.DwmSetWindowAttribute(Handle,2,ref noFrame,4);}
        else if(usesGenie)GeniePose.SetRestRegion(Handle,MaterialRadius,cachedDpiScale);
        if(Native.HostMaterial){
            if(!hostFailed&&Native.TransparencyAllowed){try{hostGlass??=new HostGlass(Handle,MaterialInset);hostGlass.Resize(Math.Max(0,ActualWidth-2*MaterialInset),Math.Max(0,MaterialHeight),cachedDpiScale,MaterialRadius);hostGlass.Reveal(materialReveal);hostGlass.Deform(geniePose,cachedDpiScale);hostGlass.Opacity(MaterialOpacity);if(!MenuTracking)hostGlass.Sync(IsVisible);MaterialAvailable=true;Background=Brushes.Transparent;Surface?.SetResourceReference(Border.BackgroundProperty,"GlassSurface");return;}catch(Exception e){hostFailed=true;Program.Log(e);}}
            hostGlass?.Dispose();hostGlass=null;MaterialAvailable=false;
            Background=Brushes.Transparent;if(Surface is Border solid)solid.Background=SystemParameters.HighContrast?SystemColors.WindowBrush:new SolidColorBrush(Theme.Dark?Color.FromRgb(28,34,46):Color.FromRgb(240,244,250));return;
        }
        MaterialAvailable = Native.Acrylic(Handle, !ShowActivated);
        Background = MaterialAvailable ? Brushes.Transparent : SystemParameters.HighContrast?SystemColors.WindowBrush:new SolidColorBrush(Theme.Dark?Color.FromRgb(28,34,46):Color.FromRgb(240,244,250));
    }
    protected virtual UIElement? ForegroundOpacityTarget=>Surface;
    internal double MaterialOpacity {get;private set;}=1;
    internal double ForegroundOpacity=>ForegroundOpacityTarget?.Opacity??1;
    public void SetSurfaceOpacity(double value){MaterialOpacity=value;if(ForegroundOpacityTarget is {} visual)visual.Opacity=value;try{hostGlass?.Opacity(value);}catch(Exception e){hostFailed=true;Program.Log(e);RefreshMaterial();}}
    protected void SetMaterialReveal(double value){materialReveal=value;hostGlass?.Reveal(value);}
    protected void SetMaterialRevealAndOpacity(double revealValue,double opacityValue)
    {
        materialReveal=revealValue;
        MaterialOpacity=opacityValue;
        if(ForegroundOpacityTarget is {} visual)visual.Opacity=opacityValue;
        try{hostGlass?.RevealAndOpacity(revealValue,opacityValue);}
        catch(Exception e){hostFailed=true;Program.Log(e);RefreshMaterial();}
    }
    protected void SetMaterialGenie(GeniePose? pose)
    {
        bool change=(geniePose==null)!=(pose==null);geniePose=pose;usesGenie=true;double dpi=cachedDpiScale;
        if(pose!=null){if(change&&Handle!=0){int policy=1;Native.DwmSetWindowAttribute(Handle,2,ref policy,4);}if(MaterialInset==0)GeniePose.SetInputRegion(Handle,pose,dpi);hostGlass?.Deform(pose,dpi);}
        else{hostGlass?.Deform(null,dpi);GeniePose.SetRestRegion(Handle,MaterialRadius,dpi);}
    }
    internal void SetContentSurface(Border surface){Surface=surface;Content=surface;}
    public virtual bool Contains(Native.Point p) => IsVisible && Native.GetWindowRect(Handle, out var r) && r.Contains(p);
    public void Place(int x, int y) => Native.DeferWindowPosAsync(Handle, -1, x, y, 0, 0, 0x0001);
    public FrameworkElement? Surface { get; protected set; }
}

internal sealed class SmoothScrollViewer : ScrollViewer
{
    internal void ResetScroll(){CancelWheel();ScrollToTop();}
    static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register("AnimatedOffset", typeof(double), typeof(SmoothScrollViewer), new PropertyMetadata(0.0, (o, e) => ((SmoothScrollViewer)o).ScrollToVerticalOffset((double)e.NewValue)));
    double target;
    bool wheeling,dragging;
    int wheelRevision;
    internal string WheelDiagnostics=>$"requests={wheelRevision}; animated={GetValue(AnimatedOffsetProperty)}; target={target}; actual={VerticalOffset}; extent={ExtentHeight}; viewport={ViewportHeight}; measure={IsMeasureValid}; arrange={IsArrangeValid}";
    int fadeToken;
    System.Windows.Controls.Primitives.ScrollBar? bar;
    readonly DispatcherTimer hide=new(){Interval=TimeSpan.FromSeconds(1)};
    internal System.Windows.Controls.Primitives.ScrollBar? OverlayBar=>bar;
    public SmoothScrollViewer()
    {
        Loaded+=(_,_)=>
        {
            ApplyTemplate();var next=Template?.FindName("PART_VerticalScrollBar",this) as System.Windows.Controls.Primitives.ScrollBar;
            if(next==null||ReferenceEquals(next,bar))return;bar=next;bar.Opacity=0;bar.IsHitTestVisible=false;
            System.Windows.Automation.AutomationProperties.SetName(bar,"内容滚动条");
            bar.MouseEnter+=(_,_)=>Reveal();bar.MouseLeave+=(_,_)=>Arm();
            bar.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,new System.Windows.Controls.Primitives.DragStartedEventHandler((_,_)=>{dragging=true;CancelWheel();Reveal();}));
            bar.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,new System.Windows.Controls.Primitives.DragCompletedEventHandler((_,_)=>{dragging=false;target=VerticalOffset;Arm();}));
        };
        hide.Tick+=(_,_)=>
        {
            hide.Stop();if(bar==null||bar.IsMouseOver||dragging)return;
            var token=++fadeToken;var animation=new DoubleAnimation(0,TimeSpan.FromMilliseconds(Anim.Allowed?220:0));
            animation.Completed+=(_,_)=>{if(token==fadeToken&&bar!=null)bar.IsHitTestVisible=false;};bar.BeginAnimation(OpacityProperty,animation);
        };
        PreviewMouseMove+=(_,e)=>{if(ScrollableHeight>0&&e.GetPosition(this).X>=ActualWidth-12)Reveal();};
        ScrollChanged+=(_,e)=>{if(Math.Abs(e.VerticalChange)>.01)Reveal();};
        Unloaded+=(_,_)=>{hide.Stop();CancelWheel();fadeToken++;if(bar!=null){bar.BeginAnimation(OpacityProperty,null);bar.Opacity=0;bar.IsHitTestVisible=false;}};
    }
    void Arm(){hide.Stop();if(!dragging&&bar?.IsMouseOver!=true)hide.Start();}
    void Reveal()
    {
        if(bar==null||ScrollableHeight<=0)return;
        fadeToken++;bar.IsHitTestVisible=true;bar.BeginAnimation(OpacityProperty,new DoubleAnimation(bar.IsMouseOver||dragging?.92:.66,TimeSpan.FromMilliseconds(Anim.Allowed?110:0)));Arm();
    }
    void CancelWheel(){wheelRevision++;var offset=VerticalOffset;SetValue(AnimatedOffsetProperty,offset);BeginAnimation(AnimatedOffsetProperty,null);wheeling=false;target=offset;}
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if(!ApplyWheel(e.Delta)){base.OnMouseWheel(e);return;}e.Handled=true;
    }
    internal bool ApplyWheel(int delta)
    {
        if(ScrollableHeight<=0)return false;
        int request=++wheelRevision;
        if(!wheeling||Math.Abs(target-VerticalOffset)>300)target=VerticalOffset;
        target = Math.Clamp(target - delta * .85, 0, ScrollableHeight);
        var animation=new DoubleAnimation(VerticalOffset,target,TimeSpan.FromMilliseconds(Anim.Allowed?170:0)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
        animation.Completed+=(_,_)=>{if(request!=wheelRevision)return;wheeling=false;SetValue(AnimatedOffsetProperty,VerticalOffset);BeginAnimation(AnimatedOffsetProperty,null);};wheeling=true;BeginAnimation(AnimatedOffsetProperty,animation);Reveal();
        return true;
    }
}
