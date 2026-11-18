using System.IO;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Brushes = System.Windows.Media.Brushes;

namespace MacDock;
internal static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        if(args.Contains("--game-fixture"))return GameWakeChecks.Fixture(args[Array.IndexOf(args,"--game-fixture")+1]);
        if(args.Contains("--external-fixture"))return ExternalGlassChecks.Fixture(args[Array.IndexOf(args,"--external-fixture")+1]);
        if(args.Contains("--modal-test"))return TransitionChecks.Logic();
        if(args.Contains("--icon-test")){var preview=new System.Windows.Application();preview.Resources=new ResourceDictionary{Source=new Uri("Theme.xaml",UriKind.Relative)};Theme.Apply("system");return IconTests.Run();}
        if(args.Contains("--icon-audit")){var audit=new System.Windows.Application();audit.Resources=new ResourceDictionary{Source=new Uri("Theme.xaml",UriKind.Relative)};return IconAudit.Run();}
        if(args.Contains("--data-test"))return Tests.DataBoundary();
        if(args.Contains("--update-test"))return UpdateChecks.Run().GetAwaiter().GetResult();
        if(args.Contains("--dpi-test")){var preview=new System.Windows.Application();preview.Resources=new ResourceDictionary{Source=new Uri("Theme.xaml",UriKind.Relative)};return Tests.DpiPreviews();}
        if(args.Contains("--source-audit"))
        {
            var folder=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")??Path.Combine(AppContext.BaseDirectory,"test-results");Directory.CreateDirectory(folder);
            var items=Desktop.Enumerate();File.WriteAllText(Path.Combine(folder,"source-audit.json"),System.Text.Json.JsonSerializer.Serialize(new{view=Desktop.ViewAvailable,error=DesktopView.LastError,desktopIconDip=DesktopView.IconSizeDip,items},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));return 0;
        }
        if(args.Contains("--menu-test")) return Tests.MenuOverflow();
        if(args.Contains("--self-test")) return Tests.Run();
        if(args.Contains("--fullscreen-fixture"))
        {
            var fixtureApp=new System.Windows.Application();
            var screen=Forms.Screen.PrimaryScreen!.Bounds;
            var w=new Window { Title="MacDock fullscreen test fixture",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=Brushes.Black,Content=new TextBlock { Text="MacDock 全屏共存自动测试 · 3 秒后关闭",Foreground=Brushes.White,FontSize=24,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center } };
            w.Loaded+=(_,_)=>Native.SetWindowPos(new WindowInteropHelper(w).Handle,0,screen.X,screen.Y,screen.Width,screen.Height,0x40);
            var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) };timer.Tick+=(_,_)=> { timer.Stop();w.Close(); };timer.Start();
            return fixtureApp.Run(w);
        }
        bool isTestRun=args.Any(a=>a.StartsWith("--",StringComparison.Ordinal)&&(a.EndsWith("-test",StringComparison.Ordinal)||a is "--product-preview" or "--genie-idle-check" or "--external-glass" or "--native-menu" or "--contrast" or "--probe" or "--idle"));
        using Mutex? mutex=isTestRun?null:new Mutex(true,"Local\\MacDock.Desktop.v1",out var first);
        if(!isTestRun && mutex!=null)
        {
            bool acquired=false;
            try{acquired=mutex.WaitOne(0,false);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired) { System.Windows.MessageBox.Show("MacDock 已在运行，请使用托盘图标或 Ctrl+Alt+空格。","MacDock");return 0; }
        }
        int maxRefreshHz=60;
        foreach(var scr in Forms.Screen.AllScreens)maxRefreshHz=Math.Max(maxRefreshHz,Native.GetDisplayRefreshRate(scr.DeviceName));
        if(maxRefreshHz>60)System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline),new FrameworkPropertyMetadata{DefaultValue=maxRefreshHz});
        var app=new System.Windows.Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Resources = new ResourceDictionary { Source = new Uri("Theme.xaml", UriKind.Relative) };
        EventManager.RegisterClassHandler(typeof(ContextMenu),ContextMenu.OpenedEvent,new RoutedEventHandler((sender,_)=>
        {
            var menu=(ContextMenu)sender;InteractionChecks.Observe(menu);
            menu.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                if (menu.IsOpen && !menu.IsKeyboardFocusWithin) menu.Focus();
            });
        }));
        Controller? c=null;
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>Native.RestoreSystemCursors(force:true);
        app.DispatcherUnhandledException+=(_,e)=> { Native.RestoreSystemCursors(force:true);Log(e.Exception);if(isTestRun){Environment.Exit(1);return;}System.Windows.MessageBox.Show("操作未完成，错误已记录。\n"+e.Exception.Message,"MacDock");e.Handled=true; };
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>{ Native.RestoreSystemCursors(force:true);Log(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject.ToString())); };
        app.Startup+=(_,_)=>
        {
            try
            {
                bool testing=args.Any(a=>a is "--proportion-test" or "--hover-drag-test" or "--display-check-test" or "--window-click-test" or "--gesture-test" or "--gateway-test" or "--interaction-test" or "--motion-perf-test" or "--contour-test" or "--render-input-test" or "--cross-pin-test" or "--stability-test" or "--corner-test" or "--wake-strip-test" or "--shadow-test" or "--brand-test" or "--game-wake-test" or "--genie-test" or "--dock-motion-test" or "--native17-test" or "--sorting-test" or "--menu-focus-test" or "--mirror-test" or "--external-glass" or "--resume-test" or "--gpu-host" or "--transition-test" or "--appearance-test" or "--ui-test" or "--visual-test" or "--file-test" or "--contrast" or "--native-menu" or "--quality-test" or "--material-test" or "--probe" or "--idle" or "--settings-shot" or "--automation-host" or "--responsive-test" or "--drag-test");
                testing|=args.Contains("--first-run-test")||args.Contains("--settings-release-test");
                bool productPreview = false;
                bool dragE2E = false;
                bool externalDropPin = false;
                bool drawerStability = false;
                bool drawerPasteReuse = false;
                bool dockStability = false;
#if MACDOCK_EXPERIENCE_CHECKS
                productPreview = args.Contains("--product-preview");
                dragE2E = args.Contains("--drag-e2e-test");
                externalDropPin = args.Contains("--external-drop-pin-test");
                drawerStability = args.Contains("--drawer-stability-test");
                dockStability = args.Contains("--dock-stability-test");
                testing|=args.Contains("--genie-idle-check");
                drawerPasteReuse = args.Contains("--drawer-paste-reuse-test");
                testing|=args.Contains("--experience-test")||productPreview||dragE2E||externalDropPin||drawerStability||dockStability||drawerPasteReuse||args.Contains("--update-ui-test")||args.Contains("--icon-sync-test");
#endif
#if MACDOCK_EXPERIENCE_CHECKS
                if(testing&&Environment.GetEnvironmentVariable("MACDOCK_QA_SOFTWARE_RENDER")=="1")
                    System.Windows.Media.RenderOptions.ProcessRenderMode=System.Windows.Interop.RenderMode.SoftwareOnly;
#endif
                string? testProfile=testing?System.IO.Path.Combine(Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")??AppContext.BaseDirectory,"profile"):null;
                if(testProfile!=null&&Directory.Exists(testProfile)&&!File.Exists(Path.Combine(testProfile,"config.json")))
                {
                    try{File.Delete(Path.Combine(testProfile,"last-session.json"));}catch{}
                }
                c=new Controller(testProfile,autoHide:testing,skipDesktop:productPreview||dragE2E||externalDropPin||drawerStability||dockStability||drawerPasteReuse);
                c.RecordSession(args,testing);
                if(args.Contains("--first-run-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=FirstRunChecks.Run(c);});
                if(args.Contains("--settings-release-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=SettingsReleaseChecks.Run(c);});
#if MACDOCK_EXPERIENCE_CHECKS
                if(args.Contains("--genie-idle-check"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=GenieIdleChecks.Run(c);});
                if(args.Contains("--experience-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.Run(c);});
                if(args.Contains("--product-preview"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.Capture(c);});
                if(args.Contains("--drag-e2e-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.DragE2E(c);});
                if(args.Contains("--external-drop-pin-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.ExternalDropAndPin(c);});
                if(args.Contains("--drawer-stability-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.DrawerStability(c);});
                if(args.Contains("--dock-stability-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.DockStability(c);});
                if(args.Contains("--drawer-paste-reuse-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.DrawerPasteReuse(c);});
                if(args.Contains("--icon-sync-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.IconSync(c);});
                if(args.Contains("--update-ui-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ExperienceChecks.Update(c);});
#endif
                if(args.Contains("--proportion-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ProportionChecks.Run(c);});
                if(args.Contains("--hover-drag-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=HoverDragChecks.Run(c);});
                if(args.Contains("--display-check-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=DisplayChecks.Run(c);});
                if(args.Contains("--window-click-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=WindowClickChecks.Run(c);});
                if(args.Contains("--gesture-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=GestureChecks.Run(c);});
                if(args.Contains("--gateway-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=GatewayChecks.Run(c);});
                if(args.Contains("--interaction-test")||args.Contains("--motion-perf-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=InteractionChecks.Run(c,args.Contains("--motion-perf-test"));});
                if(args.Contains("--contour-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=ContourChecks.Run(c);});
                if(args.Contains("--render-input-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=RenderInputChecks.Run(c,args.Contains("--baseline"));});
                if(args.Contains("--cross-pin-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=args.Contains("--verify-restart")?SortingChecks.VerifyRestart(c):CrossPinChecks.Run(c);});
                if(args.Contains("--stability-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=StabilityChecks.Run(c,args.Contains("--store-only"));});
                if(args.Contains("--corner-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=CornerChecks.Run(c);});
                if(args.Contains("--wake-strip-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=WakeStripChecks.Run(c);});
                if(args.Contains("--shadow-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=GenieChecks.Shadow(c);});
                if(args.Contains("--brand-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=BrandChecks.Run(c);});
                if(args.Contains("--game-wake-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=GameWakeChecks.Run(c);});
                if(args.Contains("--genie-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=GenieChecks.Run(c);});
                if(args.Contains("--dock-motion-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=DockMotionChecks.Run(c);});
                if(args.Contains("--native17-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=NativeMenuChecks.Run(c);});
                if(args.Contains("--sorting-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_ = args.Contains("--verify-restart")?SortingChecks.VerifyRestart(c):args.Contains("--edge-test")?SortingChecks.Edge(c):SortingChecks.Run(c);});
                if(args.Contains("--menu-focus-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=MenuFocusChecks.Run(c);});
                if(args.Contains("--mirror-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=MirrorChecks.Run(c);});
                if(args.Contains("--external-glass"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=ExternalGlassChecks.Run(c);});
                if(args.Contains("--resume-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=ResumeChecks.Run(c);});
                if(args.Contains("--gpu-host"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=TransitionChecks.GpuHost(c);});
                if(args.Contains("--transition-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.Normal,()=>{_=TransitionChecks.Run(c);});
                if(args.Contains("--appearance-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=AppearanceChecks.Run(c);});
                if(args.Contains("--drag-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=QualityChecks.DragOnly(c);});
                if(args.Contains("--responsive-test"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>Tests.Responsive(c));
                if(args.Contains("--automation-host"))app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{_=Tests.AutomationHost(c);});
                if(args.Contains("--quality-test")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=QualityChecks.Run(c,args.Contains("--baseline")); });
                if(args.Contains("--material-test")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=QualityChecks.MaterialOnly(c); });
                if(args.Contains("--contrast")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=Tests.Contrast(c); });
                if(args.Contains("--native-menu")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=Tests.NativeMenu(c); });
                if(args.Contains("--file-test")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=Tests.FileOps(c); });
                if(args.Contains("--ui-test")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=Tests.Ui(c); });
                if(args.Contains("--visual-test")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=Tests.VisualOnly(c); });
                if(args.Contains("--settings-shot")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>Tests.SettingsShot(c));
                if(args.Contains("--idle")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=Tests.Idle(c); });
                if(args.Contains("--probe")) app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=> { _=Tests.Probe(c); });
                if(args.Contains("--drawer")) c.ToggleDrawer();
                if(args.Contains("--dock")) c.Expand();
            }
            catch(Exception e) { Log(e);if(isTestRun){Environment.Exit(1);return;}System.Windows.MessageBox.Show("MacDock 无法启动："+e.Message,"MacDock");app.Shutdown(1); }
        };
        app.Exit+=(_,_)=>c?.Dispose();
        var result=app.Run();HostGlass.ShutdownQueue();DiagnosticsLog.Finish();return result;
    }
    public static void Log(Exception e)=>DiagnosticsLog.Error(e);
}

internal sealed class SenseBox
{
    readonly int left, top, right, bottom;
    public SenseBox(int l, int t, int r, int b) { left = l; top = t; right = r; bottom = b; }
    public bool Contains(int x, int y) => x >= left && x < right && y >= top && y < bottom;
    public override string ToString() => $"{left},{top},{right},{bottom}";
}

internal sealed class Controller : IDisposable
{
    public Store Store { get; }
    public DockWindow Dock { get; }
    public DrawerWindow? Drawer { get; private set; }
    public Forms.Screen Screen { get; private set; }
    public System.Drawing.Rectangle WorkArea {get;private set;}
    public double Scale { get; private set; }=1;
    internal void SetScreen(Forms.Screen screen) { Screen=screen; }
    System.Drawing.Rectangle? testArea;
    internal void TestLayoutArea(System.Drawing.Rectangle area){testArea=area;if(UsableArea(area,Scale))WorkArea=area;}
    static bool UsableArea(System.Drawing.Rectangle area,double scale)=>area.Width>=160*scale&&area.Height>=180*scale;
    public Layout Layout => new(WorkArea.Width/Scale,WorkArea.Height/Scale,Store.Data.DockCapacity,Store.Data.DrawerRows,Store.Data.DrawerColumns,DesktopView.IconSizeDip);
    internal bool AutoHideDock { get; set; }
    internal int SuppressWakeKey => Store.Data.SuppressWakeKey;
    internal bool WakeSuppressedByKey => SuppressWakeKey>0&&Native.KeyHeld(SuppressWakeKey);
    internal static bool ShouldSuppressDockByKey(bool wakeSuppressedByKey,bool menuOpen,bool busy,bool internalDragging,bool drawerVisible,bool textEditing)=>
        wakeSuppressedByKey&&!menuOpen&&!busy&&!internalDragging&&!drawerVisible&&!textEditing;
    bool SuppressDockActive => ShouldSuppressDockByKey(WakeSuppressedByKey,MenuOpen,Busy,InternalDragging,Drawer?.IsVisible==true,Native.ForegroundTextEditing());
    internal bool DesktopResident => !AutoHideDock&&!displayUnavailable&&!Native.Fullscreen(Screen)&&!Native.HasMaximizedWindow(Screen);
    bool menuOpen,internalDragging,busy,desktopDirty,mirrorVisualDirty,internalDropInProgress;
    long suppressAutoHideUntilTick;
    internal bool ExternalFileDragActive { get; private set; }
    internal bool DrawerPinned { get; set; }
    internal string? OverrideDesktopFolder { get; set; }
    readonly DispatcherTimer externalDragEscape=new(){Interval=TimeSpan.FromMilliseconds(200)};
    long externalDragReleasedTick;
    internal void SetExternalFileDrag(bool active)
    {
        if (ExternalFileDragActive == active) return;
        ExternalFileDragActive = active;
        externalDragReleasedTick = 0;
        if (active) externalDragEscape.Start(); else externalDragEscape.Stop();
        UpdateModalRegions();
    }
    internal static DragDropEffects ImportEffect(DragDropKeyStates keys,DragDropEffects allowed)
    {
        var requested=(keys&DragDropKeyStates.ShiftKey)!=0?DragDropEffects.Move:DragDropEffects.Copy;
        return (allowed&requested)!=0?requested:DragDropEffects.None;
    }
    internal bool AutoHideBlocked => InternalDropProtected || ExternalFileDragActive || DrawerInteractionActive || (DrawerPinned && Drawer?.IsVisible == true);
    int drawerInteractions;
    int drawerInteractionRevision;
    internal bool DrawerInteractionActive=>drawerInteractions>0;
    internal IDisposable HoldDrawerInteraction()
    {
        drawerInteractions++;
        drawerInteractionRevision++;
        InteractionChanged();
        return new DrawerInteraction(this);
    }
    sealed class DrawerInteraction(Controller owner):IDisposable
    {
        Controller? controller=owner;
        public void Dispose()
        {
            var c=controller;
            if(c==null)return;
            controller=null;
            c.drawerInteractions--;
            c.drawerInteractionRevision++;
            c.InteractionChanged();
        }
    }
    internal bool InternalDropProtected => internalDropInProgress || Environment.TickCount64 < suppressAutoHideUntilTick;
    internal bool DrawerClosing => drawerClosing;
    internal void BeginInternalDropProtection()
    {
        internalDropInProgress = true;
        suppressAutoHideUntilTick = Environment.TickCount64 + 1200;
        UpdateModalRegions();
    }
    internal void EndInternalDropProtection(bool restoreDrawerFocus)
    {
        if (restoreDrawerFocus)
        {
            suppressAutoHideUntilTick = Environment.TickCount64 + 800;
            if (Drawer is { IsVisible: true } drawer)
            {
                drawer.Activate();
                drawer.FocusGrid();
            }
            internalDropInProgress = false;
            UpdateModalRegions();
            _ = Task.Delay(850).ContinueWith(_ => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (!disposed) UpdateModalRegions(); }));
        }
        else
        {
            suppressAutoHideUntilTick = 0;
            internalDropInProgress = false;
            UpdateModalRegions();
        }
    }
    public bool MenuOpen { get=>menuOpen;set{menuOpen=value;InteractionChanged();} }
    public bool InternalDragging { get=>internalDragging;set{internalDragging=value;InteractionChanged();} }
    public bool Busy { get=>busy;set{busy=value;InteractionChanged();} }
    void InteractionChanged()
    {
        if(disposed)return;
        UpdateModalRegions();
        if(menuOpen||busy||internalDragging||DrawerInteractionActive) { Dock?.ObservePointer(default,false);return; }
        if(desktopDirty){syncDebounce.Stop();syncDebounce.Start();}
        QueueDockLeaveCheck();
    }
    readonly GlassWindow indicator;
    nint indicatorNative;
    readonly Forms.NotifyIcon tray;
    readonly DispatcherTimer updateCheck=new(){Interval=TimeSpan.FromMinutes(2)};
    readonly CancellationTokenSource updateLifetime=new();
    CancellationTokenSource? automaticRequest;
    UpdateStatus updateStatus=new(null,null,null);
    internal sealed record UpdateStatus(DateTimeOffset? Checked,string? Version,string? Notes,bool IsRevision=false);
    internal UpdateStatus Updates=>updateStatus;
    internal event Action? UpdateStatusChanged;
    internal string? UpdateProblem {get;private set;}
    int autoFailures;
    bool checkingUpdate;
    bool downloadingUpdate;
    Window? updateWindow;
    ReleaseUpdates.Manifest? availableUpdate;
    internal bool UpdateInProgress=>checkingUpdate||downloadingUpdate;
    internal string? UpdateProgress=>downloadingUpdate?"正在下载并验证安装包…":checkingUpdate?"正在检查更新…":null;
    internal bool CanInstallUpdate=>InstalledForUpdates();
    readonly System.Drawing.Icon trayIcon;
    readonly Intent intent=new();
    readonly uint taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
    readonly DispatcherTimer wake=new(),leave=new(),refresh=new(),syncDebounce=new(),safetySync=new(),suppressPoll=new();
    readonly DispatcherTimer iconRefreshDebounce=new(){Interval=TimeSpan.FromMilliseconds(80)};
    readonly DispatcherTimer iconGlobalDebounce=new(){Interval=TimeSpan.FromMilliseconds(250)};
    readonly DispatcherTimer iconReconcileTimer=new(){Interval=TimeSpan.FromSeconds(15)};
    readonly HashSet<string> pendingIconRefresh=new(StringComparer.OrdinalIgnoreCase);
    int iconReconcileTickCount;
    internal int IconCacheHits {get;private set;}
    internal int IconLoads {get;private set;}
    bool keySuppressingDock;
    readonly DispatcherTimer displayRetry=new(){Interval=TimeSpan.FromMilliseconds(350)};
    bool displayUnavailable;
    int displayRetries;
    readonly Native.EventProc eventProc;
    PointerHook? hook;
    readonly ModalInputGate modal;
    Native.Rect? retainedDockArea;
    readonly List<nint> eventHooks=new();
    readonly List<FileSystemWatcher> watchers=new();
    ShellChanges? shellChanges;
    readonly Dictionary<(string Path,int Px),IconEntry> icons=new();
    readonly Dictionary<(string Path,int Px),long> iconRetryAfter=new();
    readonly List<DispatcherTimer> iconRetryTimers=new();
    readonly object iconGate=new();
    readonly Thread iconThread;
    readonly Dispatcher iconDispatcher;
    Native.Rect sense;
    volatile SenseBox senseBox = new(0, 0, 0, 0);
    volatile bool dockShowing;
    Native.Point previous;
    bool disposed;
    int pointerNear;
    Layout? lastDisplayLayout;
    long latestPoint;
    int pendingPointer;
    Native.Rect dockRect;
    int dockY=int.MinValue;
    int dockToken,drawerToken,dockEpoch,drawerEpoch;
    bool dockHiding;
    bool indicatorSuppressed;
    readonly DockMotion dockMotion;
    readonly DrawerMotion drawerMotion;
    internal Action<double,double>? DockAnimationFrame;
    internal double DockProgress=>dockMotion.Value;
    internal double DockVelocity=>dockMotion.Velocity;
    internal bool DockMotionRunning=>dockMotion.Running;
    bool drawerClosing;
    bool drawerLostForeground;
    internal Action<double,int,double,double>? DrawerAnimationFrame;
    internal Action<string,object?>? DrawerLayerFrame;
    internal long PointerEvents;
    internal long HookCallbacks;
    internal int WinEventCount, PrefCount, PositionCount, PointerDispatch;
    readonly System.Text.StringBuilder trail=new();
    void T(string s){ lock(trail){ if(trail.Length>6000) trail.Remove(0,3000); trail.Append(Environment.TickCount64%100000).Append(':').Append(s).Append(' '); } }
    internal string Trail=>trail.ToString();
    internal string Diagnostics => $"box={senseBox}; showing={dockShowing}; events={PointerEvents}; callbacks={HookCallbacks}; hook={(hook?.Available==true)}; dpi={Scale}; sense={sense.Left},{sense.Top},{sense.Right},{sense.Bottom}; fullscreen={Native.Fullscreen(Screen)}; foreground={Native.Class(Native.GetForegroundWindow())}; pressed={Native.GetAsyncKeyState(1)}; since={intent.Since}";
    public bool HotkeyAvailable { get; private set; }
    public bool HookAvailable => hook?.Available==true;
    internal void RecordSession(string[] args,bool testing)
    {
        try
        {
            var session=new{time=DateTimeOffset.Now,pid=Environment.ProcessId,executable=Environment.ProcessPath,profile=Store.FilePath,testing,flags=args.Where(a=>a.StartsWith("--",StringComparison.Ordinal)).ToArray(),pins=Store.PinnedEntries().Select(e=>new{e.Id,e.Name,e.Path,e.Source}).ToArray()};
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Store.FilePath)!,"last-session.json"),System.Text.Json.JsonSerializer.Serialize(session,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception e){Program.Log(e);}
    }
    internal nint IndicatorHandle=>indicator.Handle;
    internal double IndicatorLength=>(indicator.Content as Border)?.Width??0;
    internal double IndicatorAlpha=>indicator.Opacity;
    internal Native.Rect WakeBounds=>sense;
    readonly bool skipDesktop;
    public Controller(string? profile=null,bool autoHide=false,bool skipDesktop=false)
    {
        this.skipDesktop=skipDesktop;
        AutoHideDock=autoHide;
        long startup=Stopwatch.GetTimestamp();
        void StartupStage(string stage)=>DiagnosticsLog.Event("startup.phase",new{stage,milliseconds=Stopwatch.GetElapsedTime(startup).TotalMilliseconds});
        Store=new Store(profile);
        StartupStage("profile-loaded");
        Theme.Apply(Store.Data.Appearance);
        Screen=Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==Store.Data.Monitor)
            ??Forms.Screen.PrimaryScreen??Forms.Screen.FromPoint(System.Drawing.Point.Empty);
        WorkArea=Native.MonitorArea(Screen,true);
        // The windows need valid construction metrics even when started during a
        // display handoff. Position() only publishes coordinates from a usable work area.
        if(!UsableArea(WorkArea,1))WorkArea=new System.Drawing.Rectangle(0,0,1280,720);
        Dispatcher? worker=null;using(var ready=new ManualResetEventSlim())
        {
            iconThread=new Thread(()=>{worker=Dispatcher.CurrentDispatcher;ready.Set();Dispatcher.Run();}){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name="MacDock icon resources"};
            iconThread.SetApartmentState(ApartmentState.STA);iconThread.Start();ready.Wait();iconDispatcher=worker!;
        }
        // Shell enumeration uses its own STA while WPF constructs the indicator.
        // Join before reconciliation/layout: an incomplete Explorer snapshot must
        // never be committed as an empty desktop or replace an existing order.
        bool firstRun=Store.IsFirstRun;
        var desktopScan=iconDispatcher.InvokeAsync(()=>
        {
            if(skipDesktop)return (Items:new List<DesktopItem>(),Positions:(IReadOnlyList<DesktopView.PositionedItem>)Array.Empty<DesktopView.PositionedItem>());
            var items=Desktop.Enumerate(firstRun,out var placed);
            return (Items:items,Positions:placed);
        });

        indicator=new GlassWindow(material:false) { Width=104,Height=18,IsHitTestVisible=false,Content=new Border { Width=86,Height=4.5,CornerRadius=new CornerRadius(3),Background=Brushes.White,Opacity=.82,Effect=new System.Windows.Media.Effects.DropShadowEffect { BlurRadius=5,ShadowDepth=1,Opacity=.35 } } };
        indicator.Opacity=0;indicator.Show();indicator.Hide();indicatorNative=indicator.Handle;
        Native.SetWindowLongPtr(indicator.Handle,-20,(nint)(Native.GetWindowLongPtr(indicator.Handle,-20).ToInt64()|0x20));
        indicator.Place(WorkArea.Left+WorkArea.Width/2,WorkArea.Top+WorkArea.Height/2);
        var initialDpi=Native.GetDpiForWindow(indicator.Handle);
        if(initialDpi>0)Scale=initialDpi/96.0;
        StartupStage("indicator-created");
        // Entries mirror the desktop only; never the Start Menu or system tools.
        var snapshot=desktopScan.Task.GetAwaiter().GetResult();
        var desktopItems=snapshot.Items;var positions=snapshot.Positions;
        StartupStage("desktop-enumerated");
        if(!skipDesktop)
        {
            if(Store.IsFirstRun)Store.Initialize(desktopItems,positions,Desktop.DefaultPins(),Layout.Columns,Layout.VisibleRows);
            else if(!Desktop.ViewAvailable)awaitingInitialDesktop=true;
            else if(Store.Sync(desktopItems,Desktop.ReadableFolders)||Store.Data.MigrationPending||Store.Warning!=null)Store.Save();
        }
        StartupStage("layout-reconciled");
        Dock=new DockWindow(this);
        dockMotion=new DockMotion(RenderDockPose,CompleteDockMotion);
        drawerMotion=new DrawerMotion(RenderDrawerPose,CompleteDrawerMotion);
        // Create the dock HWND once, without leaving an interactive hidden rectangle.
        Dock.Show();Dock.Hide();
        StartupStage("dock-created");
        HwndSource.FromHwnd(indicator.Handle).AddHook(Messages);
        HotkeyAvailable=Native.RegisterHotKey(indicator.Handle,1,0x4003,0x20);
        trayIcon=TrayIcon();
        tray=new Forms.NotifyIcon { Icon=trayIcon,Text="MacDock",Visible=true };
        tray.MouseUp+=(_,e)=> { if(e.Button==Forms.MouseButtons.Right)ShowTrayMenu(); };
        tray.DoubleClick+=(_,_)=>ToggleDrawer();
        if(profile==null&&ReleaseUpdates.Configured!=null)
        {
            ReadUpdateStatus();
            updateCheck.Tick+=async (_,_)=>
            {
                updateCheck.Stop();
                if(!Store.Data.AutoCheckUpdates)return;
                if(UpdateInProgress){ScheduleUpdateCheck(TimeSpan.FromMinutes(5));return;}
                var due=TimeUntilUpdateCheck(DateTimeOffset.UtcNow,updateStatus.Checked);
                if(due>TimeSpan.Zero){ScheduleUpdateCheck(due);return;}
                await CheckUpdates(false);
                ScheduleUpdateCheck(autoFailures>0?TimeSpan.FromMinutes(30*Math.Pow(2,Math.Min(autoFailures-1,3))):TimeSpan.FromHours(24));
            };
            ScheduleUpdateCheck(TimeSpan.FromMinutes(3));
        }
        wake.Tick+=(_,_)=> { wake.Stop();Native.GetCursorPos(out var p);T("wake");Pointer(p); };
        leave.Interval = TimeSpan.FromMilliseconds(200);
        leave.Tick += (_, _) => { leave.Stop(); CheckDockLeave(); };
        refresh.Interval=TimeSpan.FromMilliseconds(150);refresh.Tick+=(_,_)=> { refresh.Stop();if(disposed)return;if(dockMotion.Running||drawerMotion.Running){refresh.Start();return;}CheckFullscreen();if(Dock.IsVisible)Dock.UpdateDots(); };
        syncDebounce.Interval=TimeSpan.FromMilliseconds(450);syncDebounce.Tick+=(_,_)=> { syncDebounce.Stop();SyncDesktop(); };
        safetySync.Interval=TimeSpan.FromMinutes(5);safetySync.Tick+=(_,_)=> { if(!InternalDragging)SyncDesktop(); };
        iconRefreshDebounce.Tick+=(_,_)=>FlushPendingIconRefresh();
        iconGlobalDebounce.Tick+=(_,_)=>{iconGlobalDebounce.Stop();if(dockMotion.Running||drawerMotion.Running||Drawer?.Scroller.IsScrolling==true){iconGlobalDebounce.Start();return;}RevalidateIcons();};
        iconReconcileTimer.Tick+=(_,_)=>{
            if(disposed)return;
            iconReconcileTickCount++;
            if(Drawer?.IsVisible!=true&&(iconReconcileTickCount&3)!=0)return;
            if(dockMotion.Running||drawerMotion.Running||Drawer?.Scroller.IsScrolling==true||InternalDragging||MenuOpen||Busy)return;
            ReconcileIcons();
        };
        iconReconcileTimer.Start();
        suppressPoll.Interval=TimeSpan.FromMilliseconds(40);suppressPoll.Tick+=(_,_)=>PollSuppressWakeKey();
        if(SuppressWakeKey>0)suppressPoll.Start();
        externalDragEscape.Tick+=(_,_)=>
        {
            if(!ExternalFileDragActive){externalDragEscape.Stop();return;}
            bool btnDown=(Native.GetAsyncKeyState(1)&0x8000)!=0||(Native.GetAsyncKeyState(2)&0x8000)!=0;
            if(btnDown){externalDragReleasedTick=0;return;}
            if(externalDragReleasedTick==0)externalDragReleasedTick=Environment.TickCount64;
            else if(Environment.TickCount64-externalDragReleasedTick>=350)SetExternalFileDrag(false);
        };
        displayRetry.Tick+=(_,_)=>{displayRetry.Stop();RefreshDisplay();};
        modal=new ModalInputGate(()=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>{if(!AutoHideBlocked&&!Busy&&!InternalDragging)CloseDrawer(collapseDock:true);}),(int)Native.GetDoubleClickTime(),Native.DoubleClickWidth,Native.DoubleClickHeight,()=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>{if(AutoHideBlocked||Busy||InternalDragging)return;drawerLostForeground=true;CloseDrawer(collapseDock:true);UpdateModalRegions();}));
        hook=new PointerHook(OnPointerEvent,FilterPointer);
        eventProc=(_,evt,h,obj,child,thread,time)=>
        {
            if(disposed)return;
            WinEventCount++;
            // Only Explorer's actual desktop layout changes matter among child events.
            if(obj==-4&&(evt is 0x8004 or 0x800B)&&Native.Class(h)=="SysListView32"&&Native.Class(Native.GetParent(h))=="SHELLDLL_DefView"){ScheduleSync();return;}
            // Only whole windows matter. Child-element create/show events arrive by the thousand
            // per second and used to wake the refresh timer for nothing.
            if(obj!=0) return;
            if(evt==0x800B && h!=Native.GetForegroundWindow()) return;
            if(evt==3)RequestDrawerFocusCheck();
            if(!refresh.IsEnabled)refresh.Start();
        };
        // Foreground, minimize/restore and active window move/size transitions drive fullscreen and maximized detection.
        foreach(var range in new[]{(3u,3u),(0x0016u,0x0017u),(0x8004u,0x8004u),(0x800Bu,0x800Bu)}) { var h=Native.SetWinEventHook(range.Item1,range.Item2,0,eventProc,0,0,2);if(h!=0)eventHooks.Add(h); }
        SystemEvents.DisplaySettingsChanged+=DisplayChanged;SystemEvents.UserPreferenceChanged+=PreferenceChanged;SystemEvents.PowerModeChanged+=PowerChanged;
        Position();CheckFullscreen();
        StartupStage("input-ready");
        StartWatchers();RegisterShellChanges();safetySync.Start();WarmIcons();
        if(awaitingInitialDesktop)ScheduleSync();
        if(profile==null)_=Task.Run(EnsureStartupRegistration);
        DiagnosticsLog.Event("startup",new{milliseconds=Stopwatch.GetElapsedTime(startup).TotalMilliseconds,entries=Store.Data.Entries.Count,recovered=Store.Warning!=null});
        if(Store.Warning!=null)Notify(Store.Warning);
        if(!HookAvailable)Notify("鼠标唤醒不可用，请通过托盘打开抽屉。");
        if(!HotkeyAvailable)Notify("Ctrl+Alt+空格已被占用；可通过白条悬停或托盘打开。");
    }
    nint Messages(nint hwnd,int msg,nint wp,nint lp,ref bool handled)
    {
        if(msg==0x0312) { ToggleDrawer();handled=true; }
        if(taskbarCreated!=0&&msg==(int)taskbarCreated){RegisterShellChanges();StartWatchers();Position();CheckFullscreen();ScheduleSync();}
        if(msg is 0x02e0 or 0x001a or 0x007e) System.Windows.Application.Current.Dispatcher.BeginInvoke(Position);
        if(msg==0x001a)
        {
            string? area=lp!=0?Marshal.PtrToStringUni(lp):null;
            if(area==null||area.Contains("Explorer",StringComparison.OrdinalIgnoreCase)||area.Contains("Shell",StringComparison.OrdinalIgnoreCase)||area.Equals("Policy",StringComparison.OrdinalIgnoreCase))
                System.Windows.Application.Current.Dispatcher.BeginInvoke(ScheduleGlobalIconRevalidate);
        }
        return 0;
    }
    void DisplayChanged(object? s,EventArgs e)=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>{displayRetries=0;RefreshDisplay();});
    void RefreshDisplay()
    {
        if(disposed)return;
        Screen=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==Store.Data.Monitor)??Forms.Screen.PrimaryScreen??Screen;
        Position();CheckFullscreen();
    }
    void PreferenceChanged(object? s,UserPreferenceChangedEventArgs e)
    {
        PrefCount++;DisplayChanged(s,e);
        System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
        {
            bool wasDark=Theme.Dark;
            Theme.Apply(Store.Data.Appearance);
            if(wasDark!=Theme.Dark)InvalidateIcons();
            else if(e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General)ScheduleGlobalIconRevalidate();
            ScheduleSync();
        });
    }
    void PowerChanged(object? s,PowerModeChangedEventArgs e) { if(e.Mode==PowerModes.Resume)System.Windows.Application.Current.Dispatcher.BeginInvoke(()=> { hook?.Dispose();hook=new PointerHook(OnPointerEvent,FilterPointer);Position();CheckFullscreen();SyncDesktop(); }); }
    internal void TestResumeLifecycle()=>PowerChanged(null,new PowerModeChangedEventArgs(PowerModes.Resume));

    // ---------------------------------------------------------------- desktop mirror
    void StartWatchers()
    {
        var folders=Desktop.Folders().ToArray();
        if(watchers.Select(w=>w.Path).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).SequenceEqual(folders.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase))return;
        foreach(var w in watchers)w.Dispose();watchers.Clear();
        foreach(var dir in folders)
        {
            try
            {
                var watcher=new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories=false,
                    NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.Attributes|NotifyFilters.LastWrite|NotifyFilters.Size
                };
                watcher.Created+=(_,e)=>{ScheduleSync();ScheduleIconPathRefresh(e.FullPath);};
                watcher.Deleted+=(_,e)=>{ScheduleSync();ScheduleIconPathRefresh(e.FullPath);};
                watcher.Changed+=(_,e)=>{ScheduleSync();ScheduleIconPathRefresh(e.FullPath);};
                watcher.Renamed+=(_,e)=>System.Windows.Application.Current.Dispatcher.BeginInvoke(()=> { if(Store.Repoint(e.OldFullPath,e.FullPath,true)) { mirrorVisualDirty=true;Store.Save(); } ScheduleIconPathRefresh(e.OldFullPath);ScheduleIconPathRefresh(e.FullPath);ScheduleSync(); });
                watcher.Error+=(_,_)=>ScheduleSync();
                watcher.EnableRaisingEvents=true;
                watchers.Add(watcher);
            }
            catch(Exception e) { Program.Log(e); }
        }
    }
    void RegisterShellChanges(){shellChanges?.Dispose();shellChanges=new ShellChanges(indicator.Handle,(events,paths)=>{
        if(disposed)return;
        if((events&0x08008000)!=0)ScheduleGlobalIconRevalidate();
        if((events&0x20001)!=0&&paths.Length==2)
        {
            if(Store.Repoint(paths[0],paths[1],true)){mirrorVisualDirty=true;Store.Save();}
            ScheduleIconPathRefresh(paths[0]);
            ScheduleIconPathRefresh(paths[1]);
        }
        foreach(var path in paths)ScheduleIconPathRefresh(path);
        ScheduleSync();
    });}
    bool awaitingInitialDesktop;
    void ScheduleSync() { if(disposed)return;System.Windows.Application.Current.Dispatcher.BeginInvoke(()=> { syncDebounce.Stop();syncDebounce.Start(); }); }
    internal void SyncDesktop(bool force=false)
    {
        if(disposed||skipDesktop)return;
        if(!force && (InternalDragging||MenuOpen||Busy||DrawerInteractionActive||drawerMotion.Running||dockMotion.Running||Drawer?.Scroller.IsScrolling==true)) { desktopDirty=true;return; }
        desktopDirty=false;
        try
        {
            var before=DesktopView.IconSizeDip;StartWatchers();var items=Desktop.Enumerate();
            // Earlier parallel discovery may beat Explorer at login. Keep the loaded
            // arrangement until a real view is available; Shell events and the existing
            // safety sync retry without an always-running startup polling loop.
            if(awaitingInitialDesktop&&!Desktop.ViewAvailable)return;
            awaitingInitialDesktop=false;
            // Only an actually unreachable desktop may keep stale entries alive.
            if(items.Count==0 && !Desktop.Reachable()) return;
            var changed=Store.Sync(items,Desktop.ReadableFolders);
            changed|=mirrorVisualDirty;mirrorVisualDirty=false;
            WarmIcons();
            if(!changed){if(Math.Abs(before-DesktopView.IconSizeDip)>.01)Position();return;}
            Store.Save();
            Dock.Rebuild();Drawer?.Render();Position();
        }
        catch(Exception e) { Program.Log(e); }
    }
    internal void SyncDesktopNow()=>SyncDesktop(true);
    internal void ResumeDeferredDesktopSync()
    {
        if(desktopDirty&&!disposed){syncDebounce.Stop();syncDebounce.Start();}
    }
    internal void RefreshDesktop(){SyncDesktop(true);}
    internal bool SetAppearance(string value)
    {
        bool dark=Theme.Dark;
        if(!SaveSetting(()=>Store.Data.Appearance,v=>Store.Data.Appearance=v,value))return false;
        Theme.Apply(value);
        if(dark!=Theme.Dark)InvalidateIcons();
        return true;
    }

    internal bool SaveSetting<T>(Func<T> read,Action<T> apply,T value)
    {
        var old=read();var order=Store.Data.DockOrder;
        try{apply(value);Store.SaveSettings();return true;}
        catch(Exception error){apply(old);Store.Data.DockOrder=order;DiagnosticsLog.Error(error,"settings.save");Notify("设置未保存，原设置仍在使用。");return false;}
    }

    internal bool SetMonitorPreference(string value)
    {
        if(!SaveSetting(()=>Store.Data.Monitor,v=>Store.Data.Monitor=v,value))return false;
        RefreshDisplay();return true;
    }

    internal bool SetSuppressWakeKey(int vk)
    {
        vk=Math.Clamp(vk,0,254);
        if(!SaveSetting(()=>Store.Data.SuppressWakeKey,v=>Store.Data.SuppressWakeKey=v,vk))return false;
        if(WakeSuppressedByKey){wake.Stop();intent.Reset();}
        if(vk>0)suppressPoll.Start();else{suppressPoll.Stop();if(keySuppressingDock){keySuppressingDock=false;CheckFullscreen();}}
        return true;
    }

    internal bool SetLayoutPreference((int Capacity,int Rows,int Columns) value)
    {
        var data=Store.Data;
        if(!SaveSetting(()=>(data.DockCapacity,data.DrawerRows,data.DrawerColumns),
            v=>(data.DockCapacity,data.DrawerRows,data.DrawerColumns)=v,value))return false;
        Position();return true;
    }

    // ---------------------------------------------------------------- icons
    int Pixels(double dip)=>Math.Clamp((int)Math.Ceiling(dip*Scale*2),32,256);
    static bool IsValidIconTarget(string path)=>path.StartsWith("::",StringComparison.Ordinal)||File.Exists(path)||Directory.Exists(path);
    void EvictStaleIcon()
    {
        var active=new HashSet<string>(Store.Data.Entries.Where(e=>e.InDrawer||e.Pinned).Select(e=>e.Path),StringComparer.OrdinalIgnoreCase);
        (string Path,int Px)? victim=null;
        foreach(var kv in icons)if(!kv.Value.Loading&&kv.Value.Missing&&!active.Contains(kv.Key.Path)){victim=kv.Key;break;}
        if(victim==null)foreach(var kv in icons)if(!kv.Value.Loading&&!active.Contains(kv.Key.Path)){victim=kv.Key;break;}
        if(victim==null)foreach(var kv in icons)if(!kv.Value.Loading&&kv.Value.Missing){victim=kv.Key;break;}
        if(victim==null)foreach(var kv in icons)if(!kv.Value.Loading){victim=kv.Key;break;}
        victim??=icons.Keys.First();
        icons.Remove(victim.Value);
        iconRetryAfter.Remove(victim.Value);
    }
    void ScheduleIconRetry(string path,int px,(string Path,int Px) key,int delayMs)
    {
        if(disposed)return;
        lock(iconGate)iconRetryAfter[key]=Environment.TickCount64+delayMs;
        var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(delayMs)};
        iconRetryTimers.Add(timer);
        timer.Tick+=(_,_)=>
        {
            timer.Stop();
            iconRetryTimers.Remove(timer);
            if(disposed)return;
            lock(iconGate)iconRetryAfter.Remove(key);
            RequestIcon(path,px);
        };
        timer.Start();
    }
    public IconEntry Icon(string path,double dip)
    {
        var px=Pixels(dip);
        return RequestIcon(path,px);
    }
    IconEntry RequestIcon(string path,int px)
    {
        if(disposed)return IconEntry.Empty;
        var key=(path.ToUpperInvariant(),px);IconEntry slot;
        int revision;
        lock(iconGate)
        {
            if(!icons.TryGetValue(key,out slot!)){if(icons.Count>=2048)EvictStaleIcon();icons[key]=slot=new IconEntry();}
            // Watchers and the asynchronous reconciliation own stamp validation.
            // Creating/reusing a tile must not open its target or read the registry.
            if(slot.Loading||(slot.Stamp.Length!=0&&(!slot.Missing||slot.RetryCount>=3))){IconCacheHits++;return slot;}
            if(iconRetryAfter.TryGetValue(key,out var retryAt)&&Environment.TickCount64<retryAt)return slot;
            slot.Loading=true;revision=slot.Revision;
            IconLoads++;
        }
        GestureChecks.Trace?.Invoke("icon-request",new{path,px,oldStamp=slot.Stamp,slot=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slot)});
        iconDispatcher.BeginInvoke(DispatcherPriority.Background,()=>
        {
            if(disposed)return;
            IconEntry result;string version;bool changed=false,validTarget=false;
            try{IconResources.Describe(path);version=IconResources.Stamp(path);result=Icons.Measure(Native.Icon(path,px));if(result.Missing&&(validTarget=IsValidIconTarget(path)))DiagnosticsLog.Event("icon.unavailable",new{pixels=px,shell=path.StartsWith("::",StringComparison.Ordinal)});changed=version!=IconResources.Stamp(path);}
            catch(Exception e){DiagnosticsLog.Error(e,"icon.load");result=IconEntry.Empty;version=IconResources.Stamp(path);}
            System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>
            {
                if(disposed)return;
                if(slot.Revision!=revision||changed){slot.Loading=false;slot.Stamp="";lock(iconGate)iconRetryAfter.Remove(key);RequestIcon(path,px);return;}
                bool pixelsChanged=slot.Missing||slot.Fingerprint!=result.Fingerprint;
                slot.Update(result,version);
                if(!result.Missing){lock(iconGate)iconRetryAfter.Remove(key);if(pixelsChanged&&Drawer!=null&&!Drawer.IsDeforming)Drawer.InvalidatePrebakedSurface();return;}
                if(!validTarget)slot.RetryCount=3;
                if(slot.RetryCount<3)
                {
                    slot.RetryCount++;
                    int delayMs=slot.RetryCount==1?500:slot.RetryCount==2?1000:1500;
                    ScheduleIconRetry(path,px,key,delayMs);
                }
            });
        });
        return slot;
    }
    void WarmIcons()
    {
        // Complete the immediately usable Dock before lower-priority drawer icons.
        foreach(var entry in Store.PinnedEntries())RequestIcon(entry.Path,Pixels(Layout.Icon));
        var paths=Store.Data.Entries.Where(e=>e.InDrawer||e.Pinned).Select(e=>e.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var px=Pixels(Layout.DrawerIcon);
        foreach(var path in paths)RequestIcon(path,px);
    }
    void InvalidateIcons()
    {
        IconResources.Invalidate();
        Native.ClearIconCache();
        (string Path,int Px)[] keys;
        lock(iconGate)
        {
            iconRetryAfter.Clear();
            foreach(var slot in icons.Values){slot.Stamp="";slot.Revision++;slot.RetryCount=0;}
            keys=icons.Keys.ToArray();
        }
        foreach(var key in keys)RequestIcon(key.Path,key.Px);
    }
    void ScheduleGlobalIconRevalidate()
    {
        if(disposed)return;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>{iconGlobalDebounce.Stop();iconGlobalDebounce.Start();});
    }
    void RevalidateIcons()
    {
        ReconcileIcons();
    }
    internal void TriggerIconReconcileNow()=>ReconcileIcons();
    bool reconcilingIcons;
    bool reconcileAgain;
    void ReconcileIcons()
    {
        if(disposed)return;
        if(reconcilingIcons){reconcileAgain=true;return;}
        var active=Store.Data.Entries.Where(e=>e.InDrawer||e.Pinned).Select(e=>e.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        (string Path,string Stamp,IconEntry Slot,int Revision)[] snapshot;
        lock(iconGate)
        {
            snapshot=icons.Where(pair=>active.Contains(pair.Key.Path)&&!pair.Value.Loading)
                .Select(pair=>(pair.Key.Path,pair.Value.Stamp,pair.Value,pair.Value.Revision)).ToArray();
        }
        if(snapshot.Length==0)return;
        reconcilingIcons=true;
        var ui=System.Windows.Application.Current.Dispatcher;
        iconDispatcher.BeginInvoke(DispatcherPriority.Background,()=>{
            IconResources.InvalidateAssociations();
            var stale=snapshot.Where(item=>item.Stamp!=IconResources.Stamp(item.Path)).ToArray();
            ui.BeginInvoke(DispatcherPriority.Background,()=>{
                reconcilingIcons=false;
                if(disposed)return;
                foreach(var path in stale.Where(item=>item.Revision==item.Slot.Revision&&!item.Slot.Loading)
                    .Select(item=>item.Path).Distinct(StringComparer.OrdinalIgnoreCase))
                    ScheduleIconPathRefresh(path);
                if(reconcileAgain){reconcileAgain=false;ScheduleGlobalIconRevalidate();}
            });
        });
    }
    void ScheduleIconPathRefresh(string? path)
    {
        if(disposed||string.IsNullOrWhiteSpace(path))return;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
        {
            if(disposed)return;
            pendingIconRefresh.Add(path);
            if(Path.GetFileName(path).Equals("desktop.ini",StringComparison.OrdinalIgnoreCase))
            {
                if(Path.GetDirectoryName(path) is string parent&&!string.IsNullOrWhiteSpace(parent))
                    pendingIconRefresh.Add(parent);
            }
            iconRefreshDebounce.Stop();
            iconRefreshDebounce.Start();
        });
    }
    void FlushPendingIconRefresh()
    {
        iconRefreshDebounce.Stop();
        if(disposed||pendingIconRefresh.Count==0)return;
        if(dockMotion.Running||drawerMotion.Running||Drawer?.Scroller.IsScrolling==true)
        {
            iconRefreshDebounce.Start();
            return;
        }
        var batch=pendingIconRefresh.ToArray();
        pendingIconRefresh.Clear();
        foreach(var p in batch)RefreshIconPath(p);
    }
    void RefreshIconPath(string path)
    {
        if(disposed)return;
        IconResources.Invalidate(path);
        Native.ClearIconCache(path);
        (string Path,int Px)[] keys;
        lock(iconGate)
        {
            keys=icons.Keys.Where(k=>k.Path.Equals(path,StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach(var key in keys){iconRetryAfter.Remove(key);var slot=icons[key];slot.Stamp="";slot.Revision++;slot.RetryCount=0;}
        }
        foreach(var key in keys)RequestIcon(key.Path,key.Px);
    }

    void ReinstallPointerHook() { try { hook?.Dispose();hook=new PointerHook(OnPointerEvent,FilterPointer); } catch(Exception e) { Program.Log(e); } }
    internal void TestReinstallHook()=>ReinstallPointerHook();

    // ---------------------------------------------------------------- placement
    public void Position()
    {
        if(disposed)return;
        if(nativeMenuTracking){nativeMenuPositionPending=true;return;}
        PositionCount++;
        var area=testArea??Native.MonitorArea(Screen,true);
        if(!UsableArea(area,1))
        {
            if(!displayUnavailable)
            {
                displayUnavailable=true;
                DiagnosticsLog.Event("display.unavailable",new{area.X,area.Y,area.Width,area.Height,dpi=Scale*96});
                sense=default;senseBox=new SenseBox(0,0,0,0);
                PointerSort.Active?.End(false);
                retainedDockArea=null;
                CheckFullscreen();
            }
            if(displayRetries++<8)displayRetry.Start();
            return;
        }
        bool recovering=displayUnavailable;displayUnavailable=false;displayRetries=0;displayRetry.Stop();
        var previousArea=WorkArea;double previousScale=Scale;
        WorkArea=area;
        // Physical coordinates for Win32, DIPs for WPF sizes; acquire DPI after changing monitors.
        indicator.Place(area.Left+area.Width/2-(int)(indicator.Width*Scale/2),area.Bottom-(int)(22*Layout.DensityUnit*Scale));
        double next=Native.GetDpiForWindow(indicator.Handle)/96.0;
        bool dpiChanged=next>0&&Math.Abs(next-Scale)>.001;
        if(dpiChanged){Scale=next;InvalidateIcons();}
        if(recovering||area!=previousArea||dpiChanged)DiagnosticsLog.Event("display.changed",new{previous=new{previousArea.X,previousArea.Y,previousArea.Width,previousArea.Height,scale=previousScale},current=new{area.X,area.Y,area.Width,area.Height,scale=Scale},recovering});
        var layout=Layout;
        if(dpiChanged||layout!=lastDisplayLayout)
        {
            Dock.InvalidateDpiCache();
            Drawer?.InvalidateDpiCache();
            bool geometryChanged=dpiChanged||lastDisplayLayout==null||Math.Abs(lastDisplayLayout.Unit-layout.Unit)>.001;
            if(geometryChanged||lastDisplayLayout!.VisibleDockCount(Store.PinnedEntries().Count)!=layout.VisibleDockCount(Store.PinnedEntries().Count))Dock.Rebuild();
            if(Dock.IsVisible)Dock.RefreshMaterial();
            if(Drawer is {} drawer)
            {
                if(drawerClosing){drawerMotion.Cancel();CompleteDrawerMotion(false);}
                drawer.InvalidatePrebakedSurface();
                drawer.ApplyLayout();
                if(geometryChanged||lastDisplayLayout!.DrawerWidth!=layout.DrawerWidth)drawer.ApplyCustomOrder();
                if(drawer.IsVisible&&!drawer.IsDeforming)drawer.RefreshMaterial();
            }
            lastDisplayLayout=layout;
        }
        indicator.Width=104*layout.DensityUnit;indicator.Height=18*layout.DensityUnit;
        if(indicator.Content is Border line){line.Width=86*layout.DensityUnit;line.Height=4.5*layout.DensityUnit;}
        indicator.Place(area.Left+area.Width/2-(int)(indicator.Width*Scale/2),area.Bottom-(int)(22*Layout.DensityUnit*Scale));
        // The hit test is half-open. The work-area edge itself belongs to the taskbar.
        double wakeUnit=Layout.DensityUnit*Scale;
        int wakeHalfWidth=Math.Min(area.Width/2,(int)(240*wakeUnit));
        sense=new Native.Rect { Left=area.Left+area.Width/2-wakeHalfWidth,Right=area.Left+area.Width/2+wakeHalfWidth,Top=area.Bottom-(int)(34*wakeUnit),Bottom=area.Bottom };
        senseBox=new SenseBox(sense.Left,sense.Top,sense.Right,sense.Bottom);
        PlaceDock(area);
        if(Drawer is { IsVisible:true })
        {
            Drawer.ApplyLayout();
            var d=DrawerRect();
            if(drawerToken==0) Drawer.Place(d.Left,d.Top);
        }
        UpdateModalRegions();
        if(recovering)CheckFullscreen();
        RenderIndicator(Dock.IsVisible?dockMotion.Value:0);
    }
    void PlaceDock(System.Drawing.Rectangle area)
    {
        Dock.Width=Layout.DockWidth(Store.Data.Entries.Count(e=>e.Pinned)+(Dock.PreviewingPin?1:0));Dock.Height=Layout.DockHeight;
        var wPx=(int)Math.Round(Dock.Width*Scale);
        dockRect=new Native.Rect
        {
            Left=area.Left+(area.Width-wPx)/2,Right=area.Left+(area.Width-wPx)/2+wPx,
            Top=area.Bottom-(int)Math.Round((Layout.DockHeight+Layout.DockBottomGap)*Scale),
            Bottom=area.Bottom-(int)Math.Round(Layout.DockBottomGap*Scale)
        };
        if(Dock.IsVisible && dockToken==0) { dockY=dockRect.Top;Native.DeferWindowPosAsync(Dock.Handle,Native.TopmostInsertAfter(Dock.Handle),dockRect.Left,dockRect.Top,0,0,0x0001); }
        else if(!Dock.IsVisible) dockY=dockRect.Top;
    }
    internal void PositionDockPreview(){PlaceDock(WorkArea);UpdateModalRegions();}
    Native.Rect DrawerRect()
    {
        var area=WorkArea;
        var h=(int)Math.Round((Drawer!.PanelHeight+2*Drawer.MaterialInset)*Scale);
        var w=(int)Math.Round(Drawer.Width*Scale);
        var y=area.Bottom-(int)Math.Round((Drawer.PanelHeight+Drawer.MaterialInset+Layout.DockHeight+Layout.DockBottomGap+22)*Scale);
        return new Native.Rect { Left=area.Left+(area.Width-w)/2,Right=area.Left+(area.Width-w)/2+w,Top=y,Bottom=y+h };
    }
    void MoveDock(int y)
    {
        dockY=y;
        Native.DeferWindowPosAsync(Dock.Handle,Native.TopmostInsertAfter(Dock.Handle),dockRect.Left,y,0,0,0x0001);
    }
    internal void UpdateModalRegions()
    {
        if(modal==null)return;
        modal.Current=!drawerLostForeground&&Drawer is {IsVisible:true}&&Native.GetWindowRect(Drawer.Handle,out var rect)
            ?new ModalInputGate.Regions(Drawer.BodyBounds,dockRect,MenuOpen||Busy||InternalDragging||AutoHideBlocked,Drawer.Handle,Dock.Handle,Drawer.TextInputActive,drawerToken!=0&&!drawerClosing,Drawer.CurrentPose,Scale,!Drawer.AllowsTransparency):null;
        if(Drawer?.IsVisible!=true)modal.FinishDismissal();
    }
    internal void DrawerFocusChanged()
    {
        if(Drawer is not {IsVisible:true}||MenuOpen||Busy||InternalDragging||AutoHideBlocked)return;
        if((Native.GetAsyncKeyState(1)&0x8000)!=0)return; // external OLE source can own focus while the file is being dragged
        var foreground=Native.GetForegroundWindow();
        if(foreground==0||foreground==Drawer.Handle||foreground==Dock.Handle||Native.GetAncestor(foreground,3)==Drawer.Handle)return;
        drawerLostForeground=true;
        CloseDrawer(collapseDock:true);
        UpdateModalRegions();
    }
    internal void RequestDrawerFocusCheck()
    {
        if(DrawerInteractionActive)return;
        int revision=drawerInteractionRevision;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
        {
            if(revision==drawerInteractionRevision)DrawerFocusChanged();
        });
    }
    void SetDockOpacity(double value) { Dock.SetSurfaceOpacity(value); }

    // ---------------------------------------------------------------- input intent
    // Runs on the hook thread: no UI work, no shared mutable state without interlocked access.
    bool FilterPointer(Native.Point p,uint message,uint data)
    {
        // Freeze before WPF's queued hit test, not after PreviewMouseDown has already
        // picked a target. A render-priority tick may otherwise move the control between
        // the physical down and WPF input dispatch. This hook only sets an atomic flag.
        var region=modal.Current;
        if(message is 0x201 or 0x204 or 0x207 or 0x20B && !modal.OwnsGesture &&
            (region!=null||dockShowing||senseBox.Contains(p.X,p.Y))&&Native.TaskbarInputAt(p))
        {
            modal.FinishDismissal();
            System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input,(Action)(()=>
            {
                intent.Reset();wake.Stop();
                if(!MenuOpen&&!Busy&&!InternalDragging&&!AutoHideBlocked)CloseDrawer(collapseDock:true);
            }));
            return false;
        }
        if (message is 0x201 or 0x204 && region is { Suspended:false, Shape:not null } &&
            region.Drawer.Contains(p) &&
            region.Shape.Contains((p.X-region.Drawer.Left)/region.Scale, (p.Y-region.Drawer.Top)/region.Scale) &&
            Native.WindowFromPoint(p) == region.DrawerWindow)
        {
            drawerMotion.PauseFromHook();
            GestureChecks.Trace?.Invoke("hook-pause", new { p.X, p.Y });
        }
        if(message is 0x202 or 0x205&&drawerMotion.InputPaused)System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>ResumeDrawerMotion());
        if(message==0x201&&!dockShowing&&!WakeSuppressedByKey&&senseBox.Contains(p.X,p.Y)&&Native.IsWindowVisible(indicatorNative)&&Native.GetWindowRect(indicatorNative,out var bar)&&Math.Abs(p.X-(bar.Left+bar.Right)/2)<=48*Scale&&Math.Abs(p.Y-(bar.Top+bar.Bottom)/2)<=6*Scale&&!Native.TaskbarPopupVisible())
        {modal.CaptureWake();System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>{if(!WakeSuppressedByKey&&!Native.TaskbarPopupVisible(WorkArea))Expand();});return true;}
        bool consumed = modal.Filter(p,message,data);
        if (message is 0x202 or 0x205 && dockShowing)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, CheckDockLeave);
        if (message is 0x201 or 0x202)
            GestureChecks.Trace?.Invoke(message == 0x201 ? "hook-down" : "hook-up", new {
                p.X, p.Y, consumed, hit = (long)Native.WindowFromPoint(p),
                dock = (long)(region?.DockWindow ?? 0), drawer = (long)(region?.DrawerWindow ?? 0)
            });
        return consumed;
    }
    void OnPointerEvent(Native.Point p,bool button)
    {
        if(disposed)return;
        Interlocked.Increment(ref HookCallbacks);
        try
        {
            if(button)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(()=>
                {
                    intent.Reset();wake.Stop();
                    // Outside button ownership is decided synchronously by ModalInputGate.
                });
                return;
            }
            bool near=senseBox.Contains(p.X,p.Y);
            int wasNear=Interlocked.Exchange(ref pointerNear,near?1:0);
            // Deliver the exit edge as well as entry. Idle filtering must not swallow state transitions.
            if(!dockShowing && !near && wasNear==0) return;
            PointerEvents++;
            Interlocked.Exchange(ref latestPoint,((long)p.X<<32)|(uint)p.Y);
            if(Interlocked.Exchange(ref pendingPointer,1)!=0) return;
            try
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Input,(Action)(()=>
                {
                    Interlocked.Exchange(ref pendingPointer,0);
                    var packed=Interlocked.Read(ref latestPoint);
                    PointerDispatch++;Pointer(new Native.Point((int)(packed>>32),(int)((uint)packed&0xFFFFFFFF)),moved:true);
                }));
            }
            catch { Interlocked.Exchange(ref pendingPointer,0); }
        }
        catch(Exception e) { Program.Log(e); }
    }
    void Pointer(Native.Point p,bool moved=false)
    {
        if(displayUnavailable)return;
        if(modal.OwnsGesture){wake.Stop();intent.Reset();Dock.ObservePointer(default,false);return;}
        if(retainedDockArea is Native.Rect retained&&(Dock.Contains(p)||!retained.Contains(p)))retainedDockArea=null;
        if(moved)Dock.ObserveWakeMovement();
        Dock.ObservePointer(p, !Busy && !MenuOpen && !InternalDragging);
        if(Dock.IsVisible)
        {
            leave.Stop();
            if (Protected(p))
            {
                if (dockHiding) Expand();
            }
            else if (!dockHiding) Collapse();
            previous=p;return;
        }
        if(WakeSuppressedByKey)
        {
            wake.Stop();intent.Reset();previous=p;return;
        }
        bool inside=sense.Contains(p);
        bool pressed=(Native.GetAsyncKeyState(1)&0x8000)!=0 || (Native.GetAsyncKeyState(2)&0x8000)!=0;
        bool alt=(Native.GetAsyncKeyState(0x12)&0x8000)!=0;
        bool approaching=p.Y>previous.Y && Math.Abs(p.Y-previous.Y)>=Math.Abs(p.X-previous.X)/2;
        T($"P in={inside} pr={pressed} alt={alt} since={intent.Since} d={intent.Delay} wake={wake.IsEnabled}");
        if(intent.Feed(Environment.TickCount64,inside,pressed,pressed&&alt,approaching))
        {
            wake.Stop();intent.Reset();
            if(!WakeSuppressedByKey&&!Native.TaskbarInputAt(p)&&!Native.TaskbarPopupVisible(WorkArea))Expand();
        }
        else if(intent.Since>=0)
        {
            // Restart on every move: the required dwell changes when a drag or a direct approach
            // starts, and a stale timer would either fire early or add a whole extra delay.
            wake.Stop();
            wake.Interval=TimeSpan.FromMilliseconds(Math.Max(1,intent.Delay-(Environment.TickCount64-intent.Since)));
            wake.Start();
        }
        else if(!inside || pressed&&!alt)wake.Stop();
        previous=p;
    }
    // The bottom strip is part of the dock target: while the pointer rests there (typical when
    // dragging a file onto the dock) the panel must stay open instead of collapsing underneath it.
    bool Protected(Native.Point p)
    {
        if(SuppressDockActive)return false;
        return DesktopResident || MenuOpen || Busy || InternalDragging || AutoHideBlocked ||
            ((Dock.KeyboardActive || retainedDockArea != null || sense.Contains(p) || Dock.Contains(p) || Drawer?.Contains(p) == true)&&!Native.TaskbarInputAt(p)) ||
            (Mouse.Captured is DependencyObject captured && Window.GetWindow(captured) == Dock);
    }

    bool dockLeaveCheckPending;

    void QueueDockLeaveCheck()
    {
        if (disposed || dockLeaveCheckPending || Dock?.IsVisible != true) return;
        dockLeaveCheckPending = true;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            dockLeaveCheckPending = false;
            CheckDockLeave();
        });
    }

    void CheckDockLeave()
    {
        if (disposed || !Dock.IsVisible || dockHiding || modal.OwnsGesture) return;
        if (Native.GetCursorPos(out var point) && !Protected(point)) Collapse();
    }

    internal void FocusDockKeyboard()
    {
        if(!Dock.IsVisible)Expand();
        Dock.BeginKeyboard();
    }

    internal void FocusDrawerKeyboard()
    {
        if(Drawer?.IsVisible!=true||drawerClosing)ToggleDrawer();
        else {Drawer.Activate();Drawer.FocusGrid();}
    }

    internal void EndDockKeyboard()
    {
        if(Drawer is { IsVisible:true }&&!drawerClosing)FocusDrawerKeyboard();
        DockKeyboardEnded();
    }

    internal void DockKeyboardEnded()=>QueueDockLeaveCheck();

    // ---------------------------------------------------------------- show / hide
    public void Expand()
    {
        T("expand");
        if(displayUnavailable||Native.Fullscreen(Screen))return;
        ShowDockAnimated();
        // Programmatic reveal can happen without a pointer move. The one-shot fallback
        // covers that case only. A real exit starts the spring directly.
        leave.Start();
    }
    bool animBoostActive;
    GCLatencyMode savedGcLatency;
    ProcessPriorityClass savedProcPriority=ProcessPriorityClass.Normal;
    ThreadPriority savedThreadPriority=ThreadPriority.Normal;
    void BeginAnimationBoost()
    {
        if(animBoostActive||disposed)return;
        animBoostActive=true;
        try{Native.timeBeginPeriod(1);}catch{}
        try{savedGcLatency=GCSettings.LatencyMode;GCSettings.LatencyMode=GCLatencyMode.SustainedLowLatency;}catch{}
        try{using var p=Process.GetCurrentProcess();savedProcPriority=p.PriorityClass;if(savedProcPriority!=ProcessPriorityClass.High&&savedProcPriority!=ProcessPriorityClass.RealTime)p.PriorityClass=ProcessPriorityClass.High;}catch{}
        try{savedThreadPriority=Thread.CurrentThread.Priority;if(savedThreadPriority<ThreadPriority.Highest)Thread.CurrentThread.Priority=ThreadPriority.Highest;}catch{}
    }
    internal void SetWaveBoost(bool active)
    {
        if (active) BeginAnimationBoost();
        else EndAnimationBoost();
    }
    void EndAnimationBoost(bool force=false)
    {
        if(!animBoostActive)return;
        if(!force&&(dockMotion.Running||drawerMotion.Running||Dock.WaveRunning))return;
        animBoostActive=false;
        try{Thread.CurrentThread.Priority=savedThreadPriority;}catch{}
        try{using var p=Process.GetCurrentProcess();p.PriorityClass=savedProcPriority;}catch{}
        try{GCSettings.LatencyMode=savedGcLatency;}catch{}
        try{Native.timeEndPeriod(1);}catch{}
    }

    void ShowDockAnimated()
    {
        GestureChecks.Trace?.Invoke("dock-show", new { phase = dockMotion.Value, velocity = dockMotion.Velocity });
        foreach(var entry in Store.PinnedEntries().Take(Layout.VisibleDockCount(Store.Data.Entries.Count(e=>e.Pinned))))Icon(entry.Path,Layout.Icon);
        dockHiding=false;
        var reduced=Store.Data.ReducedMotion||!Anim.Allowed;
        if(!Dock.IsVisible)
        {
            Dock.BeginWake();dockMotion.Snap(reduced?1:0);MoveDock(dockRect.Top);Dock.Show();dockShowing=true;
            dockToken=++dockEpoch;
            if(reduced){dockMotion.Snap(1);CompleteDockMotion(true);return;}
            int token=dockToken;
            EventHandler? onFirstFrame=null;
            onFirstFrame=(_,_)=>
            {
                CompositionTarget.Rendering-=onFirstFrame;
                if(disposed||dockHiding||dockToken!=token||!Dock.IsVisible)return;
                BeginAnimationBoost();
                dockMotion.To(true);
            };
            CompositionTarget.Rendering+=onFirstFrame;
            return;
        }
        dockToken=++dockEpoch;if(reduced){dockMotion.Snap(1);CompleteDockMotion(true);}else{BeginAnimationBoost();dockMotion.To(true);}
    }
    void HideDockAnimated(bool instant=false)
    {
        if(!Dock.IsVisible)return;
        leave.Stop();
        var reduced=instant||Store.Data.ReducedMotion||!Anim.Allowed;
        GestureChecks.Trace?.Invoke("dock-hide", new { phase = dockMotion.Value, velocity = dockMotion.Velocity });
        Dock.DismissTip();dockHiding=true;dockToken=++dockEpoch;
        if(!reduced&&!indicatorSuppressed&&!indicator.IsVisible){indicator.Opacity=0;indicator.Show();}
        if(reduced){dockMotion.Snap(0);CompleteDockMotion(false);}else{BeginAnimationBoost();dockMotion.To(false);}
    }
    void RenderDockPose(double phase){Dock.SetRevealAndOpacity(phase,Math.Clamp(phase*1.4,0,1));RenderIndicator(phase);DockAnimationFrame?.Invoke(phase,dockMotion?.Velocity??0);}
    void CompleteDockMotion(bool open){dockToken=0;dockHiding=false;if(open){MoveDock(dockRect.Top);Dock.SetRevealAndOpacity(1,1);}else{Dock.Hide();dockShowing=false;Dock.SetRevealAndOpacity(1,1);dockY=dockRect.Top;CheckFullscreen();}EndAnimationBoost();}
    internal void SettleDockMotion(){if(!Dock.IsVisible)return;dockMotion.Snap(1);dockToken=0;dockHiding=false;MoveDock(dockRect.Top);EndAnimationBoost();}
    void PollSuppressWakeKey()
    {
        if(disposed)return;
        bool active=SuppressDockActive;
        if(active)
        {
            keySuppressingDock=true;
            wake.Stop();intent.Reset();
            if(Dock.IsVisible&&!dockHiding)HideDockAnimated();
        }
        else if(keySuppressingDock)
        {
            keySuppressingDock=false;
            CheckFullscreen();
        }
    }
    public void Collapse()
    {
        if((DesktopResident&&!SuppressDockActive)||MenuOpen||Busy||InternalDragging||AutoHideBlocked||Drawer?.IsVisible==true)return;
        T("collapse");
        HideDockAnimated();
        intent.Reset();CheckFullscreen();
    }
    internal void FinishDrag() { Dock.ObservePointer(default,false);QueueDockLeaveCheck(); }
    internal void TestIndicator(bool visible)=>ShowIndicator(visible,true);
    void ShowIndicator(bool show,bool instant=false)
    {
        if(show)RenderIndicator(Dock.IsVisible?dockMotion.Value:0);else if(indicator.IsVisible)indicator.Hide();
    }
    void RenderIndicator(double phase)
    {
        if(indicatorSuppressed){if(indicator.IsVisible)indicator.Hide();return;}
        var p=WakeStrip.Pose(phase);
        if(indicator.Content is Border line){line.Width=86*Layout.DensityUnit*p.Length;line.Height=4.5*Layout.DensityUnit*p.Height;}
        indicator.Opacity=p.Alpha;
        if(p.Alpha>.001){if(!indicator.IsVisible)indicator.Show();}
        else if(indicator.IsVisible&&!dockMotion.Running)indicator.Hide();
    }
    public void CheckFullscreen()
    {
        indicatorSuppressed=displayUnavailable||Native.Fullscreen(Screen);
        if(indicatorSuppressed)
        {
            DrawerPinned=false;Drawer?.UpdatePinVisual();
            if(Drawer!=null){drawerMotion.Cancel();drawerHideDeferred=false;drawerClosing=false;drawerToken=0;Drawer.Hide();Drawer.EndGenie();Drawer.ResetSelection();if(Drawer.Surface!=null)Drawer.Surface.Opacity=1;}
            Dock.SetGatewayRest(false);UpdateModalRegions();HideDockAnimated(true);ShowIndicator(false,true);intent.Reset();wake.Stop();
            EndAnimationBoost();
            return;
        }
        if(DesktopResident&&!SuppressDockActive)
        {
            if(!Dock.IsVisible||dockHiding)ShowDockAnimated();
        }
        else if(Dock.IsVisible&&!dockHiding&&!MenuOpen&&!Busy&&!InternalDragging&&Drawer?.IsVisible!=true&&(SuppressDockActive||!Native.GetCursorPos(out var pt)||!Protected(pt)))
        {
            HideDockAnimated();
            intent.Reset();
        }
        RenderIndicator(Dock.IsVisible?dockMotion.Value:0);
    }

    // ---------------------------------------------------------------- drawer
    bool drawerHideDeferred;

    public void ToggleDrawer()
    {
        MeasureDrawerStage("toggle");
        GestureChecks.Trace?.Invoke("toggle-enter",new{visible=Drawer?.IsVisible==true,closing=drawerClosing,phase=DrawerProgress});
        if(Drawer is { IsVisible:true } && !drawerClosing){CloseDrawer(keepDock:true,force:true);return;}
        if(displayUnavailable||Native.Fullscreen(Screen))return;
        drawerLostForeground=false;
        // Cancel the old close epoch before any icon/layout/COM work can pump messages.
        // Otherwise the old timer advances (or hides the HWND) during a reverse opening.
        if(drawerClosing){drawerHideDeferred=false;drawerClosing=false;drawerToken=++drawerEpoch;}
        Expand();
        MeasureDrawerStage("dock-ready");
        if(Drawer==null)
        {
            Drawer=new DrawerWindow(this);
            Drawer.Render();
        }
        drawerClosing=false;
        retainedDockArea=null;
        if(!Drawer.IsVisible)Drawer.PrepareForOpen();
        MeasureDrawerStage("drawer-prepared");
        ShowDrawerAnimated();
    }
    void ShowDrawerAnimated()
    {
        var d=Drawer!;var target=DrawerRect();bool fresh=!d.IsVisible;
        long started=Stopwatch.GetTimestamp();
        drawerFrameStamp=0;drawerMaxFrameMs=0;
        drawerClosing=false;drawerToken=++drawerEpoch;
        if(fresh){drawerMotion.Snap(0);var gateway=Dock.GatewayCenter;drawerAnchorX=(gateway.X-target.Left)/Scale-d.MaterialInset;drawerAnchorY=(gateway.Y-target.Top)/Scale-d.MaterialInset;new WindowInteropHelper(d).EnsureHandle();d.Place(target.Left,target.Top);d.SetSurfaceOpacity(0);d.SetGenie(0,drawerAnchorX,drawerAnchorY);MeasureDrawerStage("pose-ready");d.Show();MeasureDrawerStage("window-shown");d.Place(target.Left,target.Top);}
        else if(!d.IsDeforming){var gateway=Dock.GatewayCenter;drawerAnchorX=(gateway.X-target.Left)/Scale-d.MaterialInset;drawerAnchorY=(gateway.Y-target.Top)/Scale-d.MaterialInset;d.SetGenie(drawerMotion.Value,drawerAnchorX,drawerAnchorY);}
        d.Activate();if(fresh)d.FocusGrid();d.UpdateLayout();
        MeasureDrawerStage("layout-ready");
        // Only our two surfaces are reordered, once, before the transition. No changes
        // during native menu tracking; the extended curved neck cannot cover the button.
        MoveDock(dockRect.Top);UpdateModalRegions();
        double elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if(elapsed>35)DiagnosticsLog.Event("drawer.prepare.slow",new{elapsed,fresh,dpi=Scale*96});
        if(Store.Data.ReducedMotion||!Anim.Allowed){drawerMotion.Snap(1);CompleteDrawerMotion(true);}
        else if(fresh)
        {
            int token=drawerToken;
            EventHandler? onFirstFrame=null;
            onFirstFrame=(_,_)=>
            {
                CompositionTarget.Rendering-=onFirstFrame;
                if(disposed||drawerClosing||drawerToken!=token||Drawer!=d||!d.IsVisible)return;
                BeginAnimationBoost();
                drawerMotion.To(true);
            };
            CompositionTarget.Rendering+=onFirstFrame;
        }
        else{BeginAnimationBoost();drawerMotion.To(true);}
    }
    double drawerAnchorX,drawerAnchorY;
#if MACDOCK_EXPERIENCE_CHECKS
    internal Action<string>? DrawerTiming;
#endif
    [Conditional("MACDOCK_EXPERIENCE_CHECKS")]
    void MeasureDrawerStage(string stage)
    {
#if MACDOCK_EXPERIENCE_CHECKS
        DrawerTiming?.Invoke(stage);
#endif
    }
    long drawerFrameStamp;
    double drawerMaxFrameMs;
    internal double DrawerProgress=>drawerMotion.Value;
    internal double DrawerVelocity=>drawerMotion.Velocity;
    internal bool DrawerMotionRunning=>drawerMotion.Running;
    void RenderDrawerPose(double phase)
    {
        if(Drawer is not {IsVisible:true} d)return;
        long now=Stopwatch.GetTimestamp();
        if(drawerFrameStamp!=0)drawerMaxFrameMs=Math.Max(drawerMaxFrameMs,Stopwatch.GetElapsedTime(drawerFrameStamp,now).TotalMilliseconds);
        drawerFrameStamp=now;
        MeasureDrawerStage("frame-enter");
        d.SetGenie(phase,drawerAnchorX,drawerAnchorY);UpdateModalRegions();
        MeasureDrawerStage("frame-leave");
        if(DrawerAnimationFrame!=null){Native.GetWindowRect(d.Handle,out var r);DrawerAnimationFrame(phase,r.Top,1,d.ForegroundOpacity);}
    }
#if MACDOCK_EXPERIENCE_CHECKS
    internal void PreviewDrawerPhase(double phase)
    {
        if(Drawer is not {IsVisible:true} d)return;
        drawerMotion.Cancel();
        d.SetGenie(phase,drawerAnchorX,drawerAnchorY);
        UpdateModalRegions();
    }
#endif
    void CompleteDrawerMotion(bool open)
    {
        if(Drawer is not { } d)return;
        if(drawerMaxFrameMs>50)DiagnosticsLog.Event("drawer.frame.slow",new{open,maxIntervalMs=drawerMaxFrameMs,dpi=Scale*96});
        drawerFrameStamp=0;drawerMaxFrameMs=0;
        Dock.SetGatewayRest(open);
        if (!open && Dock.GatewayPressActive)
        {
            // The sheet has already folded to zero. Hiding its foreground HWND now
            // can activate the previous app and cancel the Dock's in-flight click.
            drawerHideDeferred = true;
            GestureChecks.Trace?.Invoke("drawer-hide-deferred", null);
            return;
        }
        drawerHideDeferred = false;
        drawerToken=0;drawerClosing=false;
        if(open){d.EndGenie();d.SetSurfaceOpacity(1);Position();if(desktopDirty){syncDebounce.Stop();syncDebounce.Start();}}
        else
        {
            GestureChecks.Trace?.Invoke("drawer-hide", null);
            d.Hide();
            if (retainedDockArea != null && Dock.IsVisible && !dockHiding && !MenuOpen)
                MoveDock(dockRect.Top);
            d.EndGenie(retainHiddenMesh:true);
            d.ResetSelection();
            UpdateModalRegions();
            QueueDockLeaveCheck();
        }
        MeasureDrawerStage(open?"open-complete":"close-complete");
        EndAnimationBoost();
    }

    internal void GatewayCaptureEnded(bool released = false)
    {
        QueueDockLeaveCheck();
        if (!drawerHideDeferred) return;
        if (released)
        {
            // The release has already been tested and any valid click has retargeted.
            // A cancelled click can finish teardown now, before another frame.
            if (!Dock.GatewayPressActive) CompleteDrawerMotion(false);
            return;
        }
        System.Windows.Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            if (drawerHideDeferred && !Dock.GatewayPressActive) CompleteDrawerMotion(false);
        });
    }
    internal void PauseDrawerMotion(){if(drawerMotion.Running){drawerMotion.Pause();drawerFrameStamp=0;}}
    internal void ResumeDrawerMotion(){if(!drawerMotion.Running)BeginAnimationBoost();drawerMotion.Resume();}
    internal void SettleDrawerMotion([System.Runtime.CompilerServices.CallerMemberName]string caller="")
    {
        if(Drawer is not {IsVisible:true} d)return;
        if(!d.IsDeforming&&!drawerMotion.Running)return;
        T("settle-drawer:"+caller+";p="+drawerMotion.Value.ToString("F3"));
        drawerMotion.Snap(1);drawerToken=0;drawerClosing=false;d.EndGenie();d.SetSurfaceOpacity(1);UpdateModalRegions();EndAnimationBoost();
    }
    public void CloseDrawer(bool keepDock=false,bool collapseDock=false,bool force=false)
    {
        if(InternalDropProtected||DrawerInteractionActive)return;
        if(!force && AutoHideBlocked)return;
        DrawerPinned=false;
        Drawer?.UpdatePinVisual();
        var d=Drawer;
        if(collapseDock){retainedDockArea=null;if(!DesktopResident)HideDockAnimated();}
        if(d==null||!d.IsVisible||drawerClosing)return;
        d.FinishScrollForClose();
        if(keepDock&&Native.GetWindowRect(d.Handle,out var previousDrawer))retainedDockArea=new Native.Rect{Left=Math.Min(previousDrawer.Left,dockRect.Left),Right=Math.Max(previousDrawer.Right,dockRect.Right),Top=previousDrawer.Top,Bottom=dockRect.Bottom};
        else retainedDockArea=null;
        drawerClosing=true;drawerToken=++drawerEpoch;
        if(Store.Data.ReducedMotion||!Anim.Allowed){drawerMotion.Cancel();CompleteDrawerMotion(false);return;}
        if(!d.IsDeforming){var r=DrawerRect();var gateway=Dock.GatewayCenter;drawerAnchorX=(gateway.X-r.Left)/Scale-d.MaterialInset;drawerAnchorY=(gateway.Y-r.Top)/Scale-d.MaterialInset;d.SetGenie(drawerMotion.Value,drawerAnchorX,drawerAnchorY);MoveDock(dockRect.Top);}
        BeginAnimationBoost();
        drawerMotion.To(false);
    }
    public void Launch(Entry e)=>Launch(e,false);
    internal void Launch(Entry e,bool dockClick)
    {
        LaunchCount++;
        Safe(()=>
        {
            string target=e.Target;
            var wins=Native.Windows(dockClick&&!Shell.CanMatchApplication(e)?string.Empty:target);
            var fg=Native.GetForegroundWindow();
            CloseDrawer(force:true);
            int active=wins.FindIndex(w=>w.Handle==fg);
            if(dockClick&&active>=0&&!Native.IsIconic(fg))
            {
                minimizedFromDock[target]=fg;
                Native.MinimizeWindow(fg);
            }
            else if(wins.Count>0)
            {
                nint window=wins[(active+1)%wins.Count].Handle;
                if(dockClick&&minimizedFromDock.Remove(target,out var previous)&&
                    wins.Any(w=>w.Handle==previous)&&Native.IsIconic(previous))window=previous;
                Native.ActivateWindow(window);
            }
            else if(!dockClick||!target.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)||
                    launchPendingTarget!=target||Environment.TickCount64>=launchPendingUntil)
            {
                Shell.Launch(e);
                if(dockClick){launchPendingTarget=target;launchPendingUntil=Environment.TickCount64+1000;}
            }
        });
    }
    string? launchPendingTarget;
    long launchPendingUntil;
    readonly Dictionary<string,nint> minimizedFromDock=new(StringComparer.OrdinalIgnoreCase);
    public void Changed() { Save();if(Store.Data.ReducedMotion&&Dock.IsVisible)SettleDockMotion();Dock.Rebuild();Drawer?.Render();Position(); }
    internal void CommitSort(IReadOnlyList<string> scope,string id,int index,bool dock){if(!PersistArrangement(()=>Store.SortScope(scope,id,index,dock)))return;if(dock)Dock.ApplyPinnedOrder();else Drawer?.ApplyCustomOrder(id);}
    internal bool PersistArrangement(Action change)
    {
        var entries=Store.Data.Entries.ToList();var pins=entries.Select(e=>e.Pinned).ToArray();var order=Store.Data.DockOrder.ToList();
        try{change();Store.Save();return true;}
        catch(Exception error){Store.Data.Entries=entries;for(int i=0;i<entries.Count;i++)entries[i].Pinned=pins[i];Store.Data.DockOrder=order;Program.Log(error);Notify("固定或排序未保存，已保留原排列："+error.Message);return false;}
    }
    internal int LaunchCount;
    public void Save()=>Safe(Store.Save);
    public void Safe(Action action) { try { action(); } catch(Exception e){Program.Log(e);Notify("操作未完成："+e.Message);} }
    public void AddFiles(string[] paths,bool pin) { Safe(()=> { foreach(var p in paths)Store.Add(p,pin,source:Desktop.IsDesktopPath(p)?"desktop":"manual");Changed(); }); }
    public void PickFiles(bool pin)
    {
        Busy=true;
        try { var d=new Microsoft.Win32.OpenFileDialog { Multiselect=true,Title="添加入口 · 原文件保持原位",Filter="所有文件|*.*" };if(d.ShowDialog()==true)AddFiles(d.FileNames,pin); }
        finally { Busy=false; }
    }
    public void PickFolder(bool pin)
    {
        Busy=true;
        try { var d=new Microsoft.Win32.OpenFolderDialog { Title="添加文件夹入口" };if(d.ShowDialog()==true)AddFiles(new[]{d.FolderName},pin); }
        finally { Busy=false; }
    }
    // ---------------------------------------------------------------- real file operations
    // Every command below runs through the Windows shell, so prompts, conflicts, recycle bin,
    // elevation and progress behave exactly as they do in Explorer.
    readonly HashSet<string> cut=new(StringComparer.OrdinalIgnoreCase);
    internal bool IsCut(Entry e) => !e.IsVirtual && cut.Contains(e.Path);
    nint DialogOwner => Drawer is { IsVisible:true } ? Drawer.Handle : Dock.Handle;
    public bool CanPaste => ShellOps.ReadFileClipboard().Paths.Length > 0;

    public string[] RealPaths(IEnumerable<Entry> entries) => entries
        .Where(e=>!e.IsVirtual && (File.Exists(e.Path)||Directory.Exists(e.Path)))
        .Select(e=>e.Path).ToArray();

    internal string[] DragPaths(Entry primary)
    {
        var source=Drawer is { IsVisible:true } ? Drawer.Selection : null;
        var list=source!=null && source.Any(x=>x.Id==primary.Id) ? source : new[]{primary};
        return RealPaths(list);
    }

    internal bool DropFilesToDesktop(string[] files, bool copy)
    {
        if (files.Length == 0 || fileImportRunning) return false;
        string destination = OverrideDesktopFolder ?? Desktop.UserFolder;
        var candidates = files.Where(Path.IsPathFullyQualified)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length == 0) return false;
        if (!copy && candidates.All(file => IsInDirectory(file, destination))) return false;
        ImportFiles(candidates, destination, copy);
        return true;
    }

    internal DragDropEffects CommitExternalFileDrop(IDataObject data, DragDropEffects requested)
    {
        if(requested==DragDropEffects.None)return DragDropEffects.None;
        try
        {
            bool copy=requested==DragDropEffects.Copy;
            if(data.GetData(DataFormats.FileDrop,false) is not string[] files||!DropFilesToDesktop(files,copy))
                return DragDropEffects.None;
            if(copy)return DragDropEffects.Copy;
            // The Shell worker owns the move. An OLE Move result would allow the
            // source to delete its files before that worker has completed.
            data.SetData("Performed DropEffect",new MemoryStream(new byte[sizeof(int)]),false);
        }
        catch(Exception error){DiagnosticsLog.Error(error,"drop.external");}
        return DragDropEffects.None;
    }

    static bool IsInDirectory(string path, string directory) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(path) ?? ""),
            Path.TrimEndingDirectorySeparator(directory), StringComparison.OrdinalIgnoreCase);

    bool fileImportRunning;
    async void ImportFiles(string[] paths, string destination, bool copy)
    {
        using var interaction = HoldDrawerInteraction();
        fileImportRunning = true;
        nint hwnd = DialogOwner;
        bool quiet = skipDesktop;
        try
        {
            var result = await ShellOps.RunSta(() =>
            {
                if (!Directory.Exists(destination)) return (Complete:false, Added:Array.Empty<string>());
                var before = Directory.GetFileSystemEntries(destination).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var valid = paths.Where(path => (copy || !IsInDirectory(path, destination)) &&
                    (File.Exists(path) || Directory.Exists(path))).ToArray();
                bool complete = valid.Length > 0 && (copy
                    ? ShellOps.Copy(valid, destination, hwnd, quiet)
                    : ShellOps.Move(valid, destination, hwnd, quiet, renameOnCollision:true));
                // Shell may partially complete or cancel a batch. Do not replay the
                // request or select an older destination with the same name.
                var added = Directory.GetFileSystemEntries(destination).Where(path => !before.Contains(path)).ToArray();
                return (Complete:complete && valid.Length == paths.Length, Added:added);
            });
            if (disposed) return;
            if (!result.Complete) Notify("部分或全部文件未存入抽屉，操作可能已取消。", false);
            if (OverrideDesktopFolder != null && result.Added.Length > 0)
            {
                foreach (var path in result.Added)
                    if (!Store.Data.Entries.Any(e => e.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))
                        Store.Data.Entries.Add(new Entry{Path=path,Name=Path.GetFileNameWithoutExtension(path),Source="desktop",AutoName=false});
                Store.Save();
                Drawer?.Render();
            }
            else SyncDesktopNow();
            if (result.Added.Length > 0) Drawer?.SelectPaths(result.Added, ensureVisible:false);
        }
        catch(Exception error)
        {
            Program.Log(error);
            if(!disposed){Notify("无法完成存入抽屉。", false);ScheduleSync();}
        }
        finally
        {
            fileImportRunning = false;
            if(!disposed)SetExternalFileDrag(false);
        }
    }

    internal bool SaveDroppedBitmapToDesktop(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.Bitmap)) return false;
        string destDir = OverrideDesktopFolder ?? Desktop.UserFolder;
        try
        {
            var raw = data.GetData(DataFormats.Bitmap);
            string fileName = $"MacDock_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            string destPath = Path.Combine(destDir, fileName);
            if (raw is System.Windows.Media.Imaging.BitmapSource bs)
            {
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bs));
                using var fs = File.Create(destPath);
                encoder.Save(fs);
            }
            else if (raw is System.Drawing.Bitmap bmp)
            {
                bmp.Save(destPath, System.Drawing.Imaging.ImageFormat.Png);
            }
            else return false;

            if (OverrideDesktopFolder != null)
            {
                Store.Data.Entries.Add(new Entry { Path = destPath, Name = Path.GetFileNameWithoutExtension(destPath), Source = "desktop", AutoName = false });
                Store.Save();
            }
            SyncDesktopNow();
            Dock.Rebuild();
            Drawer?.Render();
            Drawer?.SelectPaths(new[] { destPath });
            return true;
        }
        catch (Exception ex) { Program.Log(ex); Notify("保存拖入图片失败：" + ex.Message); return false; }
        finally { SetExternalFileDrag(false); }
    }

    static string? ResolveDirectoryTarget(Entry target)
    {
        if (target.IsVirtual) return null;
        if (Directory.Exists(target.Path)) return target.Path;
        if (target.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var linkTarget = IconResources.ReadLink(target.Path).Target;
            if (!string.IsNullOrWhiteSpace(linkTarget) && Directory.Exists(linkTarget)) return linkTarget;
        }
        return null;
    }

    internal bool DropEntriesToTarget(Entry target, IReadOnlyList<Entry> sources, bool copy)
    {
        var paths = RealPaths(sources).Where(p => !string.Equals(p, target.Path, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (paths.Length == 0) return false;
        var destDir = ResolveDirectoryTarget(target);
        bool exec = destDir == null && ShellDrop.IsExecutableTarget(target.Path);
        if (destDir != null || !exec) BeginInternalDropProtection();
        Busy = true;
        try
        {
            if (destDir != null)
            {
                bool ok = copy
                    ? ShellOps.Copy(paths, destDir, DialogOwner, quiet: skipDesktop)
                    : ShellOps.Move(paths, destDir, DialogOwner, quiet: skipDesktop);
                if (!ok)
                {
                    Notify("拖放未完成或已取消。", false);
                    return false;
                }
                if (!copy)
                {
                    var moved = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
                    Store.Data.Entries.RemoveAll(x => !x.IsVirtual && moved.Contains(x.Path) && !File.Exists(x.Path) && !Directory.Exists(x.Path));
                    Store.Save();
                }
                SyncDesktopNow();
                Dock.Rebuild();
                Drawer?.Render();
                Drawer?.ClearSelection();
                return true;
            }

            Native.GetCursorPos(out var pt);
            string dest = target.IsVirtual ? target.ShellVerb ?? "" : target.Path;
            using var drop = new ShellDrop(dest, paths);
            uint keys = exec ? 0u : (copy ? 8u : 4u);
            uint allowed = exec ? 1u : 3u;
            uint enterEffect = drop.Enter(keys, pt, allowed);
            uint effect = enterEffect != 0 ? drop.Drop(keys, pt, allowed) : 0u;
            if (effect == 0)
            {
                Notify("此目标未接受文件。");
                return false;
            }
            if (!exec)
            {
                if (!copy)
                {
                    var moved = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
                    Store.Data.Entries.RemoveAll(x => !x.IsVirtual && moved.Contains(x.Path) && !File.Exists(x.Path) && !Directory.Exists(x.Path));
                    Store.Save();
                }
                SyncDesktopNow();
                Dock.Rebuild();
                Drawer?.Render();
                Drawer?.ClearSelection();
            }
            else
            {
                EndInternalDropProtection(restoreDrawerFocus: false);
                CloseDrawer(force:true);
            }
            return true;
        }
        catch (Exception ex)
        {
            Program.Log(ex);
            Notify("拖放未完成：" + ex.Message);
            return false;
        }
        finally
        {
            if (!exec) EndInternalDropProtection(restoreDrawerFocus: true);
            Busy = false;
        }
    }

    internal void OpenEntries(IReadOnlyList<Entry> entries)
    {
        if(entries.Count==0) return;
        Safe(()=> { foreach(var e in entries) Shell.Launch(e); CloseDrawer(force:true); });
    }
    internal void OpenWithEntry(Entry e) => Safe(()=> { if(!ShellOps.OpenWith(e.Path)) Notify("没有可用于打开方式的项目。"); });
    internal void RevealEntry(Entry e) => Safe(()=>
    {
        if(e.IsVirtual) Process.Start(new ProcessStartInfo(e.ShellVerb??"shell:Desktop") { UseShellExecute=true });
        else Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{e.Path}\"") { UseShellExecute=true });
    });

    internal void CopyEntries(IReadOnlyList<Entry> entries,bool move)
    {
        if(move&&entries.Any(e=>e.IsAlias)){Drawer?.ShowFileStatus("手动入口不能剪切。可在资源管理器中移动原文件。");return;}
        var paths=RealPaths(entries);
        if(paths.Length==0) {if(entries.Count>0)Drawer?.ShowFileStatus("所选项目不支持复制或剪切。");return;}
        Busy=true;
        try
        {
            if(!ShellOps.SetFileClipboard(paths,move)) { Notify("无法写入剪贴板。"); return; }
            cut.Clear(); if(move) foreach(var p in paths) cut.Add(p);
            Drawer?.ApplySelectionVisuals();
            Notify(move?$"已剪切 {paths.Length} 个桌面项目，可粘贴到桌面或资源管理器":$"已复制 {paths.Length} 个桌面项目",false);
        }
        finally { Busy=false; }
    }

    internal async void PasteToDesktop()
    {
        var (paths,move)=ShellOps.ReadFileClipboard();
        if(paths.Length==0) {if(Drawer?.IsVisible==true)Drawer.ShowFileStatus("剪贴板中没有可粘贴的文件。");return;}
        var destination=OverrideDesktopFolder??Desktop.UserFolder;
        if(!Directory.Exists(destination)) { Notify("找不到桌面文件夹。"); return; }
        bool fromDrawer=Drawer?.IsActive==true;
        var beforePaths=Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var interaction=HoldDrawerInteraction();
        var hwnd=DialogOwner;
        try
        {
            var quiet=skipDesktop;
            var ok=await ShellOps.RunSta(()=>move?ShellOps.Move(paths,destination,hwnd,quiet):ShellOps.Copy(paths,destination,hwnd,quiet));
            if(disposed)return;
            cut.Clear();
            if(!ok) Notify("粘贴未完成或已取消。",false);
            if(OverrideDesktopFolder!=null)
            {
                foreach(var src in paths)
                {
                    var destPath=Path.Combine(destination,Path.GetFileName(src));
                    if((File.Exists(destPath)||Directory.Exists(destPath))&&!Store.Data.Entries.Any(x=>string.Equals(x.Path,destPath,StringComparison.OrdinalIgnoreCase)))
                        Store.Data.Entries.Add(new Entry{Path=destPath,Name=Path.GetFileNameWithoutExtension(destPath),Source="desktop",AutoName=false});
                }
                Drawer?.Render();
            }
            else SyncDesktopNow();
            var added=Store.Data.Entries.Where(e=>e.InDrawer&&!beforePaths.Contains(e.Path)).Select(e=>e.Path).ToArray();
            if(added.Length>0) Drawer?.SelectPaths(added, ensureVisible: false);
            else Drawer?.ApplySelectionVisuals();
        }
        catch(Exception error){Program.Log(error);Notify("粘贴未完成。");}
        finally { RestoreDrawerAfterShell(fromDrawer); }
    }

    void RestoreDrawerAfterShell(bool fromDrawer)
    {
        if(!fromDrawer||Drawer is not {IsVisible:true} drawer)return;
        var foreground=Native.GetForegroundWindow();
        // Explorer can leave its progress HWND active briefly after PerformOperations returns.
        // Keep the drawer's keyboard focus only when this operation still owns the foreground.
        if(Native.Class(foreground)=="OperationStatusWindow")drawer.Activate();
        if(drawer.IsActive)drawer.FocusGrid();
    }

    internal async void DeleteEntries(IReadOnlyList<Entry> entries,bool permanent)
    {
        var real=entries.Where(e=>!e.IsVirtual && e.Source=="desktop" && Desktop.IsDesktopPath(e.Path) && (File.Exists(e.Path)||Directory.Exists(e.Path))).ToList();
        var manual=entries.Where(e=>e.IsAlias).ToList();
        if(real.Count==0 && manual.Count==0) {if(entries.Count>0)Drawer?.ShowFileStatus("所选项目不支持删除。");return;}
        bool fromDrawer=Drawer?.IsActive==true;
        using var interaction=HoldDrawerInteraction();
        var hwnd=DialogOwner;
        try
        {
            var paths=real.Select(e=>e.Path).ToArray();
            if(paths.Length>0 && !await ShellOps.RunSta(()=>ShellOps.Delete(paths,permanent,hwnd)))
            { SyncDesktopNow();Drawer?.ShowFileStatus("删除已取消或未完成。");return; }
            if(disposed)return;
            if(manual.Count>0&&PersistArrangement(()=>{foreach(var e in manual)Store.Data.Entries.Remove(e);}))mirrorVisualDirty=true;
            cut.Clear();
            SyncDesktopNow();Drawer?.ApplySelectionVisuals();
        }
        catch(Exception error){Program.Log(error);Notify("删除未完成。");}
        finally { RestoreDrawerAfterShell(fromDrawer); }
    }

    internal async void RenameEntry(Entry e)
    {
        if(e.IsVirtual) {Drawer?.ShowFileStatus("此 Windows 系统入口不支持重命名。");return;}
        bool fromDrawer=Drawer?.IsActive==true;
        using var interaction=HoldDrawerInteraction();
        try
        {
            if(e.IsAlias) { Dialogs.RenameLabel(this,e);return; }
            var name=Dialogs.RenameFile(this,e);
            if(name==null) return;
            if(name is "." or ".."||name.EndsWith('.')||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0){Drawer?.ShowFileStatus("名称包含 Windows 不允许的字符。");return;}
            var source=e.Path;
            var hwnd=DialogOwner;
            if(!await ShellOps.RunSta(()=>ShellOps.Rename(source,name,hwnd))) { Notify("重命名未完成或已取消。",false); return; }
            if(disposed)return;
            var folder=Path.GetDirectoryName(source)!;
            var renamed=Path.Combine(folder,name);
            Store.Repoint(source,renamed);
            Store.Save();SyncDesktopNow();
        }
        catch(Exception error){Program.Log(error);Notify("重命名未完成。");}
        finally { RestoreDrawerAfterShell(fromDrawer); }
    }

    internal void ShowProperties(IReadOnlyList<Entry> entries)
    {
        var paths=RealPaths(entries);
        if(paths.Length==0) return;
        using var interaction=HoldDrawerInteraction();
        bool fromDrawer=Drawer?.IsActive==true;
        try { if(!ShellOps.Properties(DialogOwner,paths)) Notify("无法打开属性。",false); }
        finally { RestoreDrawerAfterShell(fromDrawer); }
    }

    internal void CompressEntries(IReadOnlyList<Entry> entries)
    {
        var paths=RealPaths(entries);
        if(paths.Length==0) return;
        Busy=true;
        var previous=Mouse.OverrideCursor;
        try
        {
            Mouse.OverrideCursor=Cursors.Wait;
            if(ShellOps.Compress(paths,out var archive,out var error)) { Notify("已创建 "+Path.GetFileName(archive),false);SyncDesktopNow(); }
            else Notify(error);
        }
        finally { Mouse.OverrideCursor=previous;Busy=false; }
    }

    internal void ExtractEntry(Entry e)
    {
        if(e.IsVirtual) return;
        Busy=true;
        var previous=Mouse.OverrideCursor;
        try
        {
            Mouse.OverrideCursor=Cursors.Wait;
            if(ShellOps.Extract(e.Path,out var folder,out var error)) { Notify("已解压到 "+Path.GetFileName(folder),false);SyncDesktopNow(); }
            else Notify(error);
        }
        finally { Mouse.OverrideCursor=previous;Busy=false; }
    }

    internal void ShowNativeMenu(IReadOnlyList<Entry> entries,Native.Point? position=null)
    {
        if(entries.Count==0) return;
        using var interaction=HoldDrawerInteraction();
        InteractionChecks.Record?.Invoke("shell-start",null);MenuOpen=true;BeginNativeMenu();
        try
        {
            var p=position.GetValueOrDefault();if(position==null)Native.GetCursorPos(out p);
            var real=RealPaths(entries);InteractionChecks.Record?.Invoke("shell-real-paths",null);
            bool pinned=entries.All(e=>e.Pinned);Action pin=()=>SetPinned(entries,!pinned);
            var ok=real.Length==1?ShellMenu.ShowItem(DialogOwner,real[0],p.X,p.Y,()=>RenameEntry(entries[0]),pin,pinned):real.Length>1?ShellMenu.Show(DialogOwner,real,p.X,p.Y,pin:pin,pinned:pinned)
                :entries.Count==1&&entries[0].IsVirtual?ShellMenu.ShowItem(DialogOwner,entries[0].Path,p.X,p.Y,pin:pin,pinned:pinned)
                :false;
            if(!ok) Notify("无法打开 Windows 菜单：所选项目跨多个文件夹或不可用。",false);
            if (ok && ShellMenu.LastCommand > 0) ScheduleSync();
        }
        finally { EndNativeMenu();MenuOpen=false; }
    }

    bool nativeMenuTracking,nativeMenuPositionPending;
    void BeginNativeMenu(){
        // Settle before TrackPopupMenu starts. Moving a topmost owner during its native
        // menu loop could raise the owner above the otherwise correctly placed menu.
        SettleDockMotion();
        SettleDrawerMotion();
        nativeMenuTracking=true;Dock.MenuTracking=true;if(Drawer!=null)Drawer.MenuTracking=true;
        Mouse.OverrideCursor=Cursors.Arrow;Native.SetArrowCursor();
    }
    void EndNativeMenu(){nativeMenuTracking=false;Dock.MenuTracking=false;if(Drawer!=null)Drawer.MenuTracking=false;Mouse.OverrideCursor=null;Dock.RefreshMaterial();Drawer?.RefreshMaterial();if(nativeMenuPositionPending){nativeMenuPositionPending=false;Position();}}
    internal void ShowDesktopBackground(){using var interaction=HoldDrawerInteraction();MenuOpen=true;BeginNativeMenu();try{Native.GetCursorPos(out var p);if(!ShellMenu.ShowBackground(DialogOwner,p.X,p.Y,PasteToDesktop,CanPaste,RefreshDesktop))Notify("Windows 桌面菜单暂不可用。");ScheduleSync();}finally{EndNativeMenu();MenuOpen=false;}}
    internal void SetPinned(IReadOnlyList<Entry> entries,bool pinned)
    {
        if(!PersistArrangement(()=>{foreach(var e in entries)e.Pinned=pinned;}))return;
        Dock.Rebuild();Drawer?.Render();Position();
    }
    internal void InsertPin(Entry entry,int index)
    {
        if(entry.Pinned)return;
        var order=Store.PinnedEntries().Select(e=>e.Id).ToList();
        order.Insert(Math.Clamp(index,0,order.Count),entry.Id);
        if(!PersistArrangement(()=>{entry.Pinned=true;Store.Data.DockOrder=order;}))return;
        Dock.Rebuild();Drawer?.Render();Position();
    }
    internal void RemoveEntries(IReadOnlyList<Entry> entries)
    {
        foreach(var e in entries.Where(e=>e.IsAlias)) Store.Data.Entries.Remove(e);
        Changed();
    }

    public void Rename(Entry e) => RenameEntry(e);
    public ContextMenu GeneralMenu()
    {
        var m=new ContextMenu();
        foreach(var (text,action) in new (string,Action)[]{("添加程序或文件…",()=>PickFiles(true)),("添加文件夹…",()=>PickFolder(true)),("设置…",Settings),("退出",()=>System.Windows.Application.Current.Shutdown())}) { var i=new MenuItem { Header=text };i.Click+=(_,_)=>action();m.Items.Add(i); }
        MenuItem? overflow=null;
        m.Opened+=(_,_)=>
        {
            if(overflow!=null)m.Items.Remove(overflow);
            var pinned=Store.PinnedEntries().ToArray();
            overflow=PinnedOverflow(pinned,Layout.VisibleDockCount(pinned.Length),Launch);
            if(overflow!=null)m.Items.Insert(0,overflow);
        };
        return m;
    }
    internal MenuItem? RecoveryMenu()
    {
        var legacy=Store.Data.Entries.Where(e=>e.Source is "legacy" or "retired-auto").ToArray();
        if(legacy.Length==0)return null;
        var menu=new MenuItem{Header="从旧版记录恢复手动入口…"};
        foreach(var entry in legacy)
        {
            var item=new MenuItem{Header=entry.Name.Replace("_","__"),ToolTip=entry.Origin+"\n"+entry.Path};
            item.Click+=(_,_)=>
            {
                if(MessageBox.Show($"将「{entry.Name}」恢复为手动入口？", "MacDock",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
                if(SaveSetting(()=>(entry.Source,entry.Origin),v=>(entry.Source,entry.Origin)=v,("manual","user-confirmed")))Drawer?.Render();
            };
            menu.Items.Add(item);
        }
        return menu;
    }
    internal static MenuItem? PinnedOverflow(IReadOnlyList<Entry> pinned,int visible,Action<Entry> launch)
    {
        var extra=pinned.Skip(visible).ToArray();if(extra.Length==0)return null;
        var menu=new MenuItem{Header="更多固定项目"};
        foreach(var entry in extra){var item=new MenuItem{Header=entry.Name.Replace("_","__")};item.Click+=(_,_)=>launch(entry);menu.Items.Add(item);}
        return menu;
    }
    ContextMenu? trayMenu;
    internal ContextMenu? TestTrayMenu=>trayMenu;
    internal void ShowTrayMenu()
    {
        if(trayMenu?.IsOpen==true){trayMenu.IsOpen=false;return;}
        var menu=new ContextMenu();
        var settings=new MenuItem{Header="设置"};settings.Click+=(_,_)=>Settings();
        var updates=new MenuItem{Header=UpdateMenuLabel};updates.Click+=async (_,_)=>await CheckUpdates(true);
        void UpdateMenu()=>updates.Header=UpdateMenuLabel;
        menu.Opened+=(_,_)=>UpdateStatusChanged+=UpdateMenu;
        menu.Closed+=(_,_)=>UpdateStatusChanged-=UpdateMenu;
        var exit=new MenuItem{Header="退出 MacDock"};exit.Click+=(_,_)=>System.Windows.Application.Current.Shutdown();
        menu.Items.Add(settings);menu.Items.Add(updates);menu.Items.Add(new Separator());menu.Items.Add(exit);
        menu.PlacementTarget=Dock;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
        Native.GetCursorPos(out var p);var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(Dock);
        menu.HorizontalOffset=p.X/dpi.DpiScaleX;menu.VerticalOffset=p.Y/dpi.DpiScaleY;
        menu.Opened+=(_,_)=> { MenuOpen=true;Dock.ObservePointer(p,false);if(PresentationSource.FromVisual(menu) is HwndSource source)Native.SetForegroundWindow(source.Handle);menu.Focus(); };
        menu.Closed+=(_,_)=> { MenuOpen=false; };
        trayMenu=menu;menu.IsOpen=true;
    }
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    internal string UpdateMenuLabel=>UpdateProgress??(updateStatus.Version is string version?(updateStatus.IsRevision?$"发现 v{version} 修订更新":$"发现新版本 · v{version}"):"检查更新");
    string UpdateStatusPath=>Path.Combine(Path.GetDirectoryName(Store.FilePath)!,"update-status.json");
    internal static TimeSpan TimeUntilUpdateCheck(DateTimeOffset now,DateTimeOffset? last)=>
        last is DateTimeOffset time&&time<=now&&now-time<TimeSpan.FromHours(24)?TimeSpan.FromHours(24)-(now-time):TimeSpan.Zero;
    void ReadUpdateStatus()
    {
        if(!File.Exists(UpdateStatusPath))return;
        try
        {
            var saved=System.Text.Json.JsonSerializer.Deserialize<UpdateStatus>(File.ReadAllText(UpdateStatusPath));
            if(saved==null)return;
            var current=Version.Parse(ReleaseUpdates.CurrentVersion);
            bool valid=Version.TryParse(saved.Version,out var version)&&(version>current||(version==current&&saved.IsRevision));
            updateStatus=new UpdateStatus(saved.Checked,valid?saved.Version:null,valid?saved.Notes:null,valid&&saved.IsRevision);
        }
        catch(Exception error){DiagnosticsLog.Error(error,"update.history");}
    }
    void SaveUpdateStatus(ReleaseUpdates.Manifest? next)
    {
        updateStatus=new UpdateStatus(DateTimeOffset.UtcNow,next?.Version,next?.Notes,next?.IsRevision??false);
        try
        {
            string temp=UpdateStatusPath+".tmp";
            File.WriteAllText(temp,System.Text.Json.JsonSerializer.Serialize(updateStatus));
            File.Move(temp,UpdateStatusPath,true);
        }
        catch(Exception error){DiagnosticsLog.Error(error,"update.history");}
        UpdateStatusChanged?.Invoke();
    }
    void ScheduleUpdateCheck(TimeSpan delay)
    {
        updateCheck.Stop();
        if(disposed||!Store.Data.AutoCheckUpdates||ReleaseUpdates.Configured==null)return;
        updateCheck.Interval=delay<TimeSpan.FromSeconds(1)?TimeSpan.FromSeconds(1):delay;
        updateCheck.Start();
    }
    internal bool SetAutoUpdates(bool value)
    {
        if(!SaveSetting(()=>Store.Data.AutoCheckUpdates,v=>Store.Data.AutoCheckUpdates=v,value))return false;
        if(!value)automaticRequest?.Cancel();
        ScheduleUpdateCheck(TimeSpan.FromMinutes(3));return true;
    }
    static bool InstalledForUpdates()
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MacDock.Desktop_is1");
        return key?.GetValue("InstallLocation") is string directory&&key.GetValue("DisplayVersion") is string version&&
            version==ReleaseUpdates.CurrentVersion&&string.Equals(Path.Combine(directory,"versions",version,"MacDock.exe"),Environment.ProcessPath,StringComparison.OrdinalIgnoreCase);
    }

    internal async Task CheckUpdates(bool manual,ReleaseUpdates? testFeed=null,Window? anchor=null)
    {
        if(disposed)return;
        if(UpdateInProgress){if(manual)ShowUpdateWindow(anchor);return;}
        checkingUpdate=true;
        UpdateProblem=null;
        UpdateStatusChanged?.Invoke();
        if(manual)ShowUpdateWindow(anchor);
        try
        {
            var updater=testFeed??ReleaseUpdates.Configured;
            if(updater==null){UpdateProblem="当前构建尚未接入公开更新渠道。";return;}
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
            if(!manual)automaticRequest=deadline;
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var current=Version.Parse(ReleaseUpdates.CurrentVersion);
            var next=await updater.Check(current,deadline.Token);
            if(disposed)return;
            availableUpdate=next;SaveUpdateStatus(next);autoFailures=0;
        }
        catch(OperationCanceledException) when(disposed||!manual&&!Store.Data.AutoCheckUpdates){}
        catch(Exception error)
        {
            if(!disposed){DiagnosticsLog.Error(error,"update.request");UpdateProblem=UpdateError(error);if(!manual)autoFailures++;}
        }
        finally{automaticRequest=null;checkingUpdate=false;if(!disposed)UpdateStatusChanged?.Invoke();}
    }

    static string UpdateError(Exception error)=>error switch
    {
        OperationCanceledException=>"请求超时，请重试。",
        System.Net.Http.HttpRequestException {StatusCode:System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.Forbidden}=>"GitHub 暂时限制了请求，请稍后重试。",
        System.Net.Http.HttpRequestException {StatusCode:null}=>"无法连接 GitHub，请检查网络后重试。",
        InvalidDataException=>"更新文件验证未通过，请稍后重试。",
        _=>"未能完成更新请求，请稍后重试。"
    };

    void ShowUpdateWindow(Window? anchor)
    {
        if(updateWindow is {IsVisible:true} open){open.Activate();return;}
        updateWindow=Dialogs.Update(this,anchor);
        updateWindow.Closed+=(_,_)=>updateWindow=null;
        updateWindow.Show();
        updateWindow.Activate();
    }

    internal async Task InstallUpdate()
    {
        if(disposed||UpdateInProgress||availableUpdate is not {} next||ReleaseUpdates.Configured is not {} updater)return;
        if(!CanInstallUpdate)
        {
            Safe(()=>Process.Start(new ProcessStartInfo(updater.ReleasePage(next).AbsoluteUri){UseShellExecute=true}));return;
        }
        downloadingUpdate=true;UpdateProblem=null;UpdateStatusChanged?.Invoke();
        try
        {
            using var downloadDeadline=CancellationTokenSource.CreateLinkedTokenSource(updateLifetime.Token);
            downloadDeadline.CancelAfter(TimeSpan.FromMinutes(5));
            var cache=Path.Combine(Path.GetDirectoryName(Store.FilePath)!,"update-cache");
            var installer=await updater.Download(next,cache,downloadDeadline.Token);
            if(disposed)return;
            if(Process.Start(new ProcessStartInfo(installer){UseShellExecute=true})==null)throw new InvalidOperationException("无法启动安装程序。");
            System.Windows.Application.Current.Shutdown();
        }
        catch(OperationCanceledException) when(disposed){}
        catch(Exception error)
        {
            if(!disposed){DiagnosticsLog.Error(error,"update.install");UpdateProblem=UpdateError(error);}
        }
        finally{downloadingUpdate=false;if(!disposed)UpdateStatusChanged?.Invoke();}
    }
    const string StartupTaskName="MacDock";
    static string CurrentUserAccount()=>string.IsNullOrWhiteSpace(Environment.UserDomainName)?Environment.UserName:$"{Environment.UserDomainName}\\{Environment.UserName}";
    static string? GetRunStartupPath()
    {
        try
        {
            using var k=Registry.CurrentUser.OpenSubKey(RunKey);
            if(k?.GetValue("MacDock") is not string raw||string.IsNullOrWhiteSpace(raw))return null;
            var trimmed=raw.Trim();
            if(trimmed.StartsWith('"')){int end=trimmed.IndexOf('"',1);return end>1?trimmed[1..end]:trimmed.Trim('"');}
            return trimmed;
        }
        catch{return null;}
    }
    static void ClearRunStartup()
    {
        try{using var k=Registry.CurrentUser.OpenSubKey(RunKey,true);k?.DeleteValue("MacDock",false);}
        catch(Exception e){DiagnosticsLog.Error(e,"startup.run.delete");}
    }
    static void SetRunStartup(string exePath)
    {
        using var k=Registry.CurrentUser.CreateSubKey(RunKey);
        k.SetValue("MacDock","\""+exePath+"\"");
    }
    static bool TryQueryStartupTask(out string? exePath)
    {
        exePath=null;
        object? svc=null,folder=null,task=null;
        try
        {
            var type=Type.GetTypeFromProgID("Schedule.Service");
            if(type==null)return false;
            svc=Activator.CreateInstance(type);
            if(svc==null)return false;
            ((dynamic)svc).Connect(null,null,null,null);
            folder=((dynamic)svc).GetFolder("\\");
            task=((dynamic)folder).GetTask(StartupTaskName);
            if(task!=null)
            {
                try
                {
                    foreach(dynamic act in ((dynamic)task).Definition.Actions)
                    {
                        exePath=act.Path as string;
                        break;
                    }
                }
                catch{}
                return true;
            }
        }
        catch(FileNotFoundException){return false;}
        catch{}
        finally
        {
            if(task!=null&&Marshal.IsComObject(task))Marshal.ReleaseComObject(task);
            if(folder!=null&&Marshal.IsComObject(folder))Marshal.ReleaseComObject(folder);
            if(svc!=null&&Marshal.IsComObject(svc))Marshal.ReleaseComObject(svc);
        }
        return false;
    }
    static bool TryRegisterStartupTaskCom(string exePath)
    {
        object? svc=null,folder=null,td=null;
        try
        {
            var type=Type.GetTypeFromProgID("Schedule.Service");
            if(type==null)return false;
            svc=Activator.CreateInstance(type);
            if(svc==null)return false;
            dynamic service=svc;
            service.Connect(null,null,null,null);
            folder=service.GetFolder("\\");
            td=service.NewTask(0);
            dynamic task=td;
            task.RegistrationInfo.Description="MacDock 开机登录快速启动";
            string userId=CurrentUserAccount();
            task.Principal.UserId=userId;
            task.Principal.LogonType=3;
            task.Principal.RunLevel=0;
            task.Settings.Enabled=true;
            task.Settings.DisallowStartIfOnBatteries=false;
            task.Settings.StopIfGoingOnBatteries=false;
            task.Settings.ExecutionTimeLimit="PT0S";
            task.Settings.AllowDemandStart=true;
            task.Settings.StartWhenAvailable=true;
            task.Settings.MultipleInstances=2;
            task.Settings.Compatibility=2;
            dynamic trigger=task.Triggers.Create(9);
            trigger.UserId=userId;
            trigger.Enabled=true;
            dynamic action=task.Actions.Create(0);
            action.Path=exePath;
            action.WorkingDirectory=Path.GetDirectoryName(exePath)??"";
            ((dynamic)folder).RegisterTaskDefinition(StartupTaskName,task,6,null,null,3,null);
            return true;
        }
        catch(Exception e){DiagnosticsLog.Error(e,"startup.task.com");return false;}
        finally
        {
            if(td!=null&&Marshal.IsComObject(td))Marshal.ReleaseComObject(td);
            if(folder!=null&&Marshal.IsComObject(folder))Marshal.ReleaseComObject(folder);
            if(svc!=null&&Marshal.IsComObject(svc))Marshal.ReleaseComObject(svc);
        }
    }
    static string BuildStartupTaskXml(string exePath,string userId)
    {
        string escapedUser=System.Security.SecurityElement.Escape(userId)??userId;
        string escapedExe=System.Security.SecurityElement.Escape(exePath)??exePath;
        string escapedDir=System.Security.SecurityElement.Escape(Path.GetDirectoryName(exePath)??"")??"";
        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>MacDock 开机登录快速启动</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{escapedUser}</UserId>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{escapedUser}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{escapedExe}</Command>
      <WorkingDirectory>{escapedDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";
    }
    static bool TryRegisterStartupTaskCli(string exePath)
    {
        string xmlFile=Path.Combine(Path.GetTempPath(),$"macdock-startup-{Environment.ProcessId}.xml");
        try
        {
            File.WriteAllText(xmlFile,BuildStartupTaskXml(exePath,CurrentUserAccount()),System.Text.Encoding.Unicode);
            using var proc=Process.Start(new ProcessStartInfo("schtasks.exe")
            {
                Arguments=$"/Create /F /TN \"{StartupTaskName}\" /XML \"{xmlFile}\"",
                UseShellExecute=false,
                CreateNoWindow=true,
                RedirectStandardOutput=true,
                RedirectStandardError=true
            });
            if(proc==null)return false;
            proc.WaitForExit(5000);
            return proc.ExitCode==0;
        }
        catch(Exception e){DiagnosticsLog.Error(e,"startup.task.cli");return false;}
        finally{try{if(File.Exists(xmlFile))File.Delete(xmlFile);}catch{}}
    }
    static bool TryDeleteStartupTask()
    {
        bool ok=false;
        object? svc=null,folder=null;
        try
        {
            var type=Type.GetTypeFromProgID("Schedule.Service");
            if(type!=null)
            {
                svc=Activator.CreateInstance(type);
                if(svc!=null)
                {
                    ((dynamic)svc).Connect(null,null,null,null);
                    folder=((dynamic)svc).GetFolder("\\");
                    ((dynamic)folder).DeleteTask(StartupTaskName,0);
                    return true;
                }
            }
        }
        catch(FileNotFoundException){ok=true;}
        catch{}
        finally
        {
            if(folder!=null&&Marshal.IsComObject(folder))Marshal.ReleaseComObject(folder);
            if(svc!=null&&Marshal.IsComObject(svc))Marshal.ReleaseComObject(svc);
        }
        if(ok)return true;
        try
        {
            using var proc=Process.Start(new ProcessStartInfo("schtasks.exe")
            {
                Arguments=$"/Delete /F /TN \"{StartupTaskName}\"",
                UseShellExecute=false,
                CreateNoWindow=true,
                RedirectStandardOutput=true,
                RedirectStandardError=true
            });
            if(proc==null)return false;
            proc.WaitForExit(5000);
            return proc.ExitCode==0;
        }
        catch{return false;}
    }
    static void EnsureStartupRegistration()
    {
        try
        {
            string? exe=Environment.ProcessPath;
            if(string.IsNullOrWhiteSpace(exe))return;
            bool hasTask=TryQueryStartupTask(out string? taskExe);
            string? runExe=GetRunStartupPath();
            if(hasTask)
            {
                if(!string.Equals(taskExe,exe,StringComparison.OrdinalIgnoreCase))SetStartup(true);
                else if(runExe!=null)ClearRunStartup();
            }
            else if(runExe!=null)
            {
                SetStartup(true);
            }
        }
        catch(Exception e){DiagnosticsLog.Error(e,"startup.ensure");}
    }
    public static bool StartupEnabled() => TryQueryStartupTask(out _)||GetRunStartupPath()!=null;
    internal static void SetStartup(bool enabled)
    {
        if(!enabled)
        {
            TryDeleteStartupTask();
            ClearRunStartup();
            return;
        }
        string exe=Environment.ProcessPath??throw new InvalidOperationException("无法确定 MacDock 可执行文件路径。");
        if(TryRegisterStartupTaskCom(exe)||TryRegisterStartupTaskCli(exe))
        {
            ClearRunStartup();
            return;
        }
        SetRunStartup(exe);
    }
    public void Settings()=>ShowSettings(null);
    internal void ShowSettings(Action<Window>? ready)
    {
        Busy=true;
        try { Dialogs.Settings(this,ready); }
        finally { Busy=false; }
    }
    public void Notify(string message,bool warning=true) { tray.BalloonTipTitle="MacDock";tray.BalloonTipText=message;tray.BalloonTipIcon=warning?Forms.ToolTipIcon.Warning:Forms.ToolTipIcon.Info;tray.ShowBalloonTip(5000); }

    static System.Drawing.Icon TrayIcon()=>AppBrand.Tray();
    public void Dispose()
    {
        void Trace(string value){if(Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS") is string folder)File.AppendAllText(Path.Combine(folder,"dispose-stages.txt"),value+Environment.NewLine);}
        Trace("begin");
        if(disposed)return;disposed=true;Native.RestoreSystemCursors(force:true);updateCheck.Stop();updateLifetime.Cancel();updateLifetime.Dispose();dockMotion.Cancel();drawerMotion.Cancel();EndAnimationBoost(force:true);PointerSort.Active?.End(false);if(trayMenu!=null)trayMenu.IsOpen=false;wake.Stop();leave.Stop();refresh.Stop();syncDebounce.Stop();safetySync.Stop();displayRetry.Stop();suppressPoll.Stop();iconRefreshDebounce.Stop();iconGlobalDebounce.Stop();iconReconcileTimer.Stop();foreach(var t in iconRetryTimers.ToArray())t.Stop();iconRetryTimers.Clear();
        foreach(var w in watchers) { try { w.EnableRaisingEvents=false;w.Dispose(); } catch { } }
        SystemEvents.DisplaySettingsChanged-=DisplayChanged;SystemEvents.UserPreferenceChanged-=PreferenceChanged;SystemEvents.PowerModeChanged-=PowerChanged;
        hook?.Dispose();shellChanges?.Dispose();foreach(var h in eventHooks)Native.UnhookWinEvent(h);
        Trace("input disposed");
        iconDispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        Native.UnregisterHotKey(indicator.Handle,1);tray.Visible=false;tray.Dispose();trayIcon.Dispose();Trace("tray disposed");Drawer?.Close();Trace("drawer closed");Dock.Close();Trace("dock closed");indicator.Close();Trace("end");
    }
}
