using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace MacDock;

internal sealed class DockWindow : GlassWindow
{
    readonly Controller owner;
    readonly StackPanel items = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Dictionary<string, Border> dots = new();
    readonly List<DockItem> layout = new();
    Border? drawerButton;
    GatewayVisual? drawerGlyph;
    double gatewayPhase;
    readonly CoverageBorder glassTint=new(){IsHitTestVisible=false,BorderThickness=new Thickness(0)};
    readonly Canvas glassLayer=new(){IsHitTestVisible=false};
    readonly Border contentHost;
    readonly TranslateTransform sheetLift=new();
    double reveal=1;
    internal double RevealPhase=>reveal;

    sealed class DockItem
    {
        public FrameworkElement Element = null!;
        public Entry? Entry;
        public ScaleTransform? Icon;
        public TranslateTransform? Shift;
        public TranslateTransform? Landing;
        public double Left, Width, GapAfter;
    }

    // Magnification state. Only running while the pointer is over the bar or the wave is
    // still settling, so a resting dock costs nothing.
    double[] currentScale = Array.Empty<double>();
    double[] currentLeft = Array.Empty<double>();
    bool pointerInside;
    double pointerX;
    bool running;
    Native.Point? wakePointer;
    internal long RenderFrames;
    long lastFrame;
    const double Peak = 1.63;
    static double WaveProfile(double x)=>.65*Math.Exp(-3.9*x)+.35*Math.Exp(-.59*x);
    static readonly double waveEdge=WaveProfile(5.0625);

    public DockWindow(Controller c) : base(interactive: false)
    {
        owner = c; AllowDrop = true;
        contentHost = new Border { Child = items, Padding = new Thickness(c.Layout.DockPad, 0, c.Layout.DockPad, 0) };
        glassTint.SetResourceReference(Border.BackgroundProperty,"GlassSurface");
        glassLayer.Children.Add(glassTint);
        var layers=new Grid();layers.Children.Add(glassLayer);layers.Children.Add(contentHost);
        Surface = new Border{Child=layers,Background=Brushes.Transparent};
        Content = Surface;
        Height = c.Layout.DockHeight;
        DragEnter += BlankOver; DragOver += BlankOver; Drop += BlankDrop;
        Native.DropPreviewHelper.AttachWindow(this, () => { mouseInhibited = false; if (owner.Drawer?.IsVisible != true) owner.SetExternalFileDrag(false); });
        PreviewDragEnter+=(_,a)=> { if(a.Data.GetDataPresent(DataFormats.FileDrop)&&!a.Data.GetDataPresent("MacDock.Entry"))owner.SetExternalFileDrag(true); if(!mouseInhibited){mouseInhibited=true;ResetMagnification();} };
        PreviewDragLeave+=(_,_)=> {Native.GetCursorPos(out var p);if(!Contains(p)&&!owner.InternalDragging)mouseInhibited=false;if(((Native.GetAsyncKeyState(1)|Native.GetAsyncKeyState(2))&0x8000)==0&&owner.Drawer?.IsVisible!=true)owner.SetExternalFileDrag(false);};
        PreviewDrop+=(_,_)=>{mouseInhibited=false;owner.SetExternalFileDrag(false);};
        ContextMenu = c.GeneralMenu();
        TrackDockMenu(ContextMenu);
        ContextMenuOpening += PrepareMenuOwner;
        MouseMove += (_, _) => { if(Native.GetCursorPos(out var p)) ObservePointer(p); };
        MouseLeave += (_, _) => { if(Native.GetCursorPos(out var p)) ObservePointer(p); };
        items.SizeChanged += (_, _) => { RefreshGeometry(); EnsureWave(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) {wakePointer=null;ResetMagnification();CancelTip();EndKeyboard();ClearDockSelection();ClearDoubleClickState();} };
        Focusable=true;
        PreviewKeyDown+=OnDockKey;
        Deactivated+=(_,_)=>{if(KeyboardActive){EndKeyboard();owner.DockKeyboardEnded();}};
        SizeChanged+=(_,_)=>SetReveal(reveal);
        DpiChanged+=(_,_)=>{if(!IsLoaded)return;InvalidateDpiCache();RefreshMaterial();owner.Position();};
        PreviewMouseDown+=(_,_)=>{CancelTip(true);owner.SettleDockMotion();};
        PreviewMouseRightButtonDown+=(_,_)=>Native.SuppressAppStartingCursor();
        PreviewMouseRightButtonUp+=(_,_)=>Native.RestoreSystemCursors();
        PreviewDragEnter+=(_,_)=>{CancelTip(true);owner.SettleDockMotion();};
        tipTimer.Tick+=(_,_)=>ShowTip();
        // WPF's system tooltip fade keeps its HWND alive for another 150ms on close.
        // Configure only this tooltip's public Popup host; never change system settings.
        labelTip.Opened+=(_,_)=>{if(labelTip.Parent is System.Windows.Controls.Primitives.Popup popup)popup.PopupAnimation=System.Windows.Controls.Primitives.PopupAnimation.None;};
        Rebuild();
    }
    public override void RefreshMaterial(){base.RefreshMaterial();if(Surface is not Border surface)return;if(MaterialAvailable)glassTint.SetResourceReference(Border.BackgroundProperty,"GlassSurface");else glassTint.Background=surface.Background;surface.Background=Brushes.Transparent;SetReveal(reveal);}
    internal void SetReveal(double value){reveal=value;SetMaterialReveal(value);UpdateRevealGeometry(value);}
    internal void SetRevealAndOpacity(double value,double opacity){reveal=value;SetMaterialRevealAndOpacity(value,opacity);UpdateRevealGeometry(value);}
    void UpdateRevealGeometry(double value){double w=Math.Max(0,ActualWidth),h=Math.Max(0,ActualHeight);var (r,c)=DockMotion.Shape(w,h,MaterialRadius,value);glassTint.Width=r.Width;glassTint.Height=r.Height;glassTint.CornerRadius=new CornerRadius(c);Canvas.SetLeft(glassTint,r.X);Canvas.SetTop(glassTint,r.Y);sheetLift.Y=(1-value)*9*owner.Layout.Unit;double t=Math.Clamp(value,0,1);items.Opacity=Math.Clamp((t-.08)/.74,0,1);}

    internal string GlyphDiag
    {
        get
        {
            if (drawerButton?.Child is not FrameworkElement g || Surface == null) return "none";
            var gb = g.TransformToAncestor(Surface).TransformBounds(new Rect(0, 0, g.RenderSize.Width, g.RenderSize.Height));
            var tb = drawerButton.TransformToAncestor(Surface).TransformBounds(new Rect(0, 0, drawerButton.ActualWidth, drawerButton.ActualHeight));
            var kids = g is System.Windows.Controls.Canvas cv ? string.Join(" ", cv.Children.OfType<FrameworkElement>().Take(3).Select(k => $"{System.Windows.Controls.Canvas.GetLeft(k):F1}/{k.ActualWidth:F1}")) + $" n={cv.Children.Count} cw={cv.ActualWidth:F1}" : "";
            return $"glyph={gb.X:F1},{gb.Y:F1},{gb.Width:F1}x{gb.Height:F1}; tile={tb.X:F1},{tb.Y:F1},{tb.Width:F1}x{tb.Height:F1}; items={items.ActualWidth:F1}x{items.ActualHeight:F1}; surface={Surface.ActualWidth:F1}x{Surface.ActualHeight:F1}; {kids}";
        }
    }

    ContextMenu? activeMenu;
    long menuEpoch;
    nint returnFocus;
    Border? keyboardTile;
    Border? selectedDockTile;
    string? lastDockClickId,lastDrawerClickId;
    long lastDockClickTick,lastDrawerClickTick;
    Native.Point lastDockClickPoint,lastDrawerClickPoint;
    internal bool KeyboardActive=>keyboardTile!=null;

    void ClearDoubleClickState(){lastDockClickId=null;lastDrawerClickId=null;lastDockClickTick=0;lastDrawerClickTick=0;}
    void ClearDockSelection()
    {
        if(selectedDockTile!=null&&selectedDockTile!=keyboardTile)selectedDockTile.BorderBrush=Brushes.Transparent;
        selectedDockTile=null;
    }
    void SelectDockTile(Border root)
    {
        ClearDockSelection();
        selectedDockTile=root;
        root.BorderBrush=Theme.FocusEdge;
    }
    internal void ActivateTile(Entry e,Border root,bool inDrawer)
    {
        if((Keyboard.Modifiers&ModifierKeys.Alt)!=0&&!e.IsVirtual&&!string.IsNullOrWhiteSpace(e.Path))
        {
            ClearDoubleClickState();
            owner.ShowProperties(new[]{e});
            return;
        }
        if(!owner.Store.Data.DoubleClickToOpen)
        {
            if(inDrawer)
            {
                bool ctrl=Native.IsControlDown(),shift=Native.IsShiftDown();
                if(ctrl||shift){ClearDoubleClickState();owner.Drawer?.FocusGrid();owner.Drawer?.ClickSelect(e,ctrl,shift);return;}
                owner.Drawer?.SelectOnly(e);
            }
            ClearDoubleClickState();
            owner.Launch(e,dockClick:!inDrawer);
            return;
        }
        long now=Environment.TickCount64;
        Native.GetCursorPos(out var pt);
        uint doubleTime=Native.GetDoubleClickTime();
        int maxDx=Math.Max(4,Native.DoubleClickWidth*2),maxDy=Math.Max(4,Native.DoubleClickHeight*2);
        if(inDrawer)
        {
            owner.Drawer?.FocusGrid();
            owner.Drawer?.SelectOnly(e);
            bool second=lastDrawerClickId==e.Id&&now-lastDrawerClickTick<=doubleTime&&
                Math.Abs(pt.X-lastDrawerClickPoint.X)<=maxDx&&Math.Abs(pt.Y-lastDrawerClickPoint.Y)<=maxDy;
            if(second)
            {
                lastDrawerClickId=null;lastDrawerClickTick=0;
                owner.Launch(e,dockClick:false);
            }
            else
            {
                lastDrawerClickId=e.Id;lastDrawerClickTick=now;lastDrawerClickPoint=pt;
            }
        }
        else
        {
            SelectDockTile(root);
            bool second=lastDockClickId==e.Id&&now-lastDockClickTick<=doubleTime&&
                Math.Abs(pt.X-lastDockClickPoint.X)<=maxDx&&Math.Abs(pt.Y-lastDockClickPoint.Y)<=maxDy;
            if(second)
            {
                lastDockClickId=null;lastDockClickTick=0;
                ClearDockSelection();
                owner.Launch(e,dockClick:true);
            }
            else
            {
                lastDockClickId=e.Id;lastDockClickTick=now;lastDockClickPoint=pt;
            }
        }
    }

    internal void BeginKeyboard()
    {
        if(!IsVisible||!Native.SetForegroundWindow(Handle))return;
        Focus();
        SetKeyboardTile(0);
    }

    void EndKeyboard()
    {
        ClearDockSelection();
        if(keyboardTile==null)return;
        keyboardTile.BorderBrush=Brushes.Transparent;
        keyboardTile=null;
    }

    IEnumerable<Border> KeyboardTiles()=>layout.Where(n=>n.Entry!=null||n.Element==drawerButton).Select(n=>(Border)n.Element);

    void SetKeyboardTile(int index)
    {
        var available=KeyboardTiles().ToArray();
        if(available.Length==0)return;
        EndKeyboard();
        keyboardTile=available[Math.Clamp(index,0,available.Length-1)];
        keyboardTile.BorderBrush=Theme.FocusEdge;
        keyboardTile.Focus();
    }

    void OnDockKey(object sender,KeyEventArgs e)
    {
        if(!KeyboardActive||owner.MenuOpen||owner.Busy||owner.InternalDragging)return;
        var available=KeyboardTiles().ToArray();
        int index=Array.IndexOf(available,keyboardTile);
        switch(e.Key==Key.System?e.SystemKey:e.Key)
        {
            case Key.Left: SetKeyboardTile(index-1);break;
            case Key.Right: SetKeyboardTile(index+1);break;
            case Key.Home: SetKeyboardTile(0);break;
            case Key.End: SetKeyboardTile(available.Length-1);break;
            case Key.F6:
                EndKeyboard();owner.FocusDrawerKeyboard();break;
            case Key.Escape:
                EndKeyboard();owner.EndDockKeyboard();break;
            case Key.Enter when !e.IsRepeat:
                if(keyboardTile?.Tag is Entry launch){EndKeyboard();owner.Launch(launch,dockClick:true);owner.EndDockKeyboard();}
                else {EndKeyboard();owner.FocusDrawerKeyboard();}
                break;
            case Key.Delete when !e.IsRepeat && keyboardTile?.Tag is Entry pin:
                owner.SetPinned(new[]{pin},false);
                SetKeyboardTile(index);break;
            case Key.F10 when Keyboard.Modifiers==ModifierKeys.Shift && !e.IsRepeat && keyboardTile?.ContextMenu is ContextMenu menu:
                menu.PlacementTarget=keyboardTile;
                menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen=true;break;
            default: return;
        }
        e.Handled=true;
    }

    void TrackDockMenu(ContextMenu menu)
    {
        menu.Opened += (_, _) => { activeMenu = menu; owner.MenuOpen = true; };
        menu.Closed += (_, _) =>
        {
            if (activeMenu != menu || menu.IsOpen) return;
            activeMenu = null;
            owner.MenuOpen = false;
            Native.RestoreSystemCursors();
        };
    }

    void PrepareMenuOwner(object sender, ContextMenuEventArgs e)
    {
        var target = e.OriginalSource as DependencyObject;
        ContextMenu? menu = null;
        while (target != null)
        {
            if (target is FrameworkElement { ContextMenu: { } found })
            {
                menu = found;
                break;
            }
            target = target is Visual ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
        }
        if (menu == null) return;

        // Activate the owner before WPF opens the popup and captures the pointer.
        // Promoting the popup after capture sends activation/focus messages through
        // WPF's menu mode and can dismiss the very menu that just opened.
        var previous = Native.GetForegroundWindow();
        if (previous != Handle)
        {
            if (!Native.SetForegroundWindow(Handle)) return;
            returnFocus = previous;
            InteractionChecks.Record?.Invoke("owner-activated-before-open", (long)Handle);
        }
        long epoch = ++menuEpoch;
        RoutedEventHandler? closed = null;
        void Restore()
        {
            menu.Closed -= closed;
            if (epoch != menuEpoch) return;
            var destination = returnFocus;
            returnFocus = 0;
            if (Native.GetForegroundWindow() == Handle && Native.IsWindowVisible(destination))
                Native.SetForegroundWindow(destination);
        }
        closed = (_, _) => Restore();
        menu.Closed += closed;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!menu.IsOpen) Restore();
        });
    }

    public void Rebuild()
    {
        var l = owner.Layout;
        bool restoreKeyboard=KeyboardActive;
        var keyboardId=(keyboardTile?.Tag as Entry)?.Id;
        EndKeyboard();
        CancelTip();contentHost.Padding = new Thickness(l.DockPad, 0, l.DockPad, 0);
        items.Children.Clear(); dots.Clear(); drawerButton = null; layout.Clear();
        StopWave();var pinned = owner.Store.PinnedEntries();
        foreach (var e in pinned.Take(l.VisibleDockCount(pinned.Count)))
        {
            var element = DockTile(e, out var icon, out var shift);
            var landing=new TranslateTransform();
            var transforms=new TransformGroup();transforms.Children.Add(shift);transforms.Children.Add(landing);
            element.RenderTransform=transforms;
            items.Children.Add(element);
            layout.Add(new DockItem { Element = element, Entry = e, Icon = icon, Shift = shift, Landing=landing });
        }
        if (pinned.Count > 0)
        {
            var divider = new Border { Width = 1, Height = 26 * l.Unit, Background = Theme.Hairline, Margin = new Thickness(6 * l.Unit, 0, 6 * l.Unit, 0) };
            items.Children.Add(divider);
            layout.Add(new DockItem { Element = divider });
        }
        var button = DrawerButton();
        items.Children.Add(button);
        layout.Add(new DockItem { Element = button });
        Width = l.DockWidth(pinned.Count);
        Height = l.DockHeight;
        ResetMagnification();
        if(restoreKeyboard&&IsActive)
        {
            var candidates=KeyboardTiles().ToList();
            SetKeyboardTile(candidates.FindIndex(b=>(b.Tag as Entry)?.Id==keyboardId));
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RefreshGeometry));
    }

    FrameworkElement DrawerButton()
    {
        var l = owner.Layout;
        var glyph = new GatewayVisual { Width = 29 * l.Unit, Height = 29 * l.Unit,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        drawerGlyph = glyph;
        PaintGateway(GatewayPose.At(gatewayPhase, glyph.Width));
        var root = new Border
        {
            Width = l.DrawerButton, Height = l.DrawerButton, CornerRadius = new CornerRadius(11 * l.Unit),
            Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = glyph, Focusable=true,FocusVisualStyle=null,
            BorderThickness=new Thickness(1),BorderBrush=Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(owner.Store.Data.Entries.Any(e => e.Pinned) ? l.DockGap / 2 : 0, 0, 0, 0)
        };
        root.Tag="应用抽屉 · Ctrl+Alt+空格";
        glyph.RenderTransform=sheetLift;
        System.Windows.Automation.AutomationProperties.SetName(root, "打开应用抽屉");
        root.MouseEnter += (_, _) => {root.Background = Theme.FillHover;ResetMagnification();};
        root.MouseLeave += (_, _) => root.Background = Brushes.Transparent;
        bool pressed=false;
        root.PreviewMouseLeftButtonDown+=(_,e)=>{
            pressed = root.CaptureMouse();
            GestureChecks.Trace?.Invoke("gateway-down", new { clicks=e.ClickCount,captured=pressed,over=root.IsMouseOver,phase=owner.DrawerProgress });
        };
        root.LostMouseCapture += (_, _) =>
        {
            GestureChecks.Trace?.Invoke("gateway-lost-capture", pressed);
            bool interrupted = pressed;
            pressed = false;
            if (interrupted) owner.GatewayCaptureEnded();
        };
        root.MouseLeftButtonUp += (_, a) =>
        {
            // Capture routes MouseUp to the tile even after the pointer leaves it.
            // Test the fixed hit slot, independent of the drawn pane or IsMouseOver.
            var point = a.GetPosition(root);
            bool inside = new Rect(root.RenderSize).Contains(point);
            bool invoke = pressed && inside;
            GestureChecks.Trace?.Invoke("gateway-up", new { pressed, inside, point.X, point.Y, invoke, phase=owner.DrawerProgress });
            pressed = false;
            a.Handled = true;
            root.ReleaseMouseCapture();
            if (invoke)
            {
                InteractionChecks.Record?.Invoke("gateway-click", null);
                owner.ToggleDrawer();
            }
            owner.GatewayCaptureEnded(released: true);
        };
        root.AllowDrop=true;var hover=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(550)};
        hover.Tick+=(_,_)=>{hover.Stop();if(owner.Drawer?.IsVisible!=true)owner.ToggleDrawer();};
        root.DragEnter+=(_,a)=>{if(a.Data.GetDataPresent(DataFormats.FileDrop)){hover.Start();if(!a.Data.GetDataPresent("MacDock.Entry")){var files=(string[])a.Data.GetData(DataFormats.FileDrop);var destDir=owner.OverrideDesktopFolder??Desktop.UserFolder;bool copy=Controller.ShouldCopyToDirectory(files,destDir,a.KeyStates);a.Effects=copy?DragDropEffects.Copy:DragDropEffects.Move;}else a.Effects=DragDropEffects.None;a.Handled=true;}};
        root.DragOver+=(_,a)=>{if(a.Data.GetDataPresent(DataFormats.FileDrop)&&!a.Data.GetDataPresent("MacDock.Entry")){var files=(string[])a.Data.GetData(DataFormats.FileDrop);var destDir=owner.OverrideDesktopFolder??Desktop.UserFolder;bool copy=Controller.ShouldCopyToDirectory(files,destDir,a.KeyStates);a.Effects=copy?DragDropEffects.Copy:DragDropEffects.Move;}else a.Effects=DragDropEffects.None;a.Handled=true;};
        root.DragLeave+=(_,_)=>hover.Stop();root.Drop+=(_,a)=>{hover.Stop();if(a.Data.GetDataPresent(DataFormats.FileDrop)&&!a.Data.GetDataPresent("MacDock.Entry")){var files=(string[])a.Data.GetData(DataFormats.FileDrop);var destDir=owner.OverrideDesktopFolder??Desktop.UserFolder;bool copy=Controller.ShouldCopyToDirectory(files,destDir,a.KeyStates);owner.DropFilesToDesktop(files,copy);a.Effects=copy?DragDropEffects.Copy:DragDropEffects.Move;}else a.Effects=DragDropEffects.None;a.Handled=true;};root.Unloaded+=(_,_)=>hover.Stop();
        drawerButton = root;
        return root;
    }

    // Dock tile: icon bottom-aligned so magnification grows upward while the running dot stays put.
    FrameworkElement DockTile(Entry e, out ScaleTransform iconScale, out TranslateTransform shift)
    {
        var l = owner.Layout;
        var root = new Border
        {
            Width = l.DockTile, Height = l.DockTileHeight, Background = Brushes.Transparent,
            Cursor = Cursors.Hand, Tag = e, CornerRadius = new CornerRadius(10),Focusable=true,FocusVisualStyle=null,
            BorderThickness=new Thickness(1),BorderBrush=Brushes.Transparent
        };
        System.Windows.Automation.AutomationProperties.SetName(root, e.Name);
        shift = new TranslateTransform(0, 0);
        root.RenderTransform = shift;

        var canvas = new Grid{RenderTransform=sheetLift,Margin=new Thickness(-1)};
        iconScale = new ScaleTransform(1, 1);
        var iconHost = new Grid
        {
            Width = l.Icon, Height = l.Icon, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 12 * l.Unit), RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = iconScale
        };
        iconHost.Children.Add(Icons.View(owner.Icon(e.Path, l.Icon), l.Icon));
        canvas.Children.Add(iconHost);

        var dot = new Border
        {
            Width = 4.2, Height = 4.2, CornerRadius = new CornerRadius(2.1), Background = Theme.Ink,
            Opacity = Native.Windows(e.Target).Count > 0 ? .95 : 0,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 4 * l.Unit)
        };
        canvas.Children.Add(dot); dots[e.Id] = dot;
        root.Child = canvas;

        // Reordering and file drops. Dragging also carries the real file list so the icon can be
        // dragged out to Explorer or another application.
        Point start = default; Native.Point startScreen = default; bool dragged = false, pressedHere = false;
        PointerSort.Attach(root,this,()=>true,()=>layout.Where(n=>n.Entry!=null).Select(n=>n.Element).ToArray(),null,(ids,id,index)=>owner.CommitSort(ids,id,index,true),active=>{if(active)ClearDoubleClickState();owner.InternalDragging=active;mouseInhibited=active;ResetMagnification();if(!active)owner.FinishDrag();},()=>ActivateTile(e,root,false),external:()=>{ClearDoubleClickState();owner.InternalDragging=true;try{var data=ShellOps.DragData(e.Id,owner.DragPaths(e),false,"dock",e.IsAlias);System.Windows.DragDrop.DoDragDrop(root,data,DragDropEffects.Copy|DragDropEffects.Link|(e.IsAlias?DragDropEffects.None:DragDropEffects.Move));}finally{owner.InternalDragging=false;mouseInhibited=false;owner.FinishDrag();}},reduced:()=>owner.Store.Data.ReducedMotion);
        root.PreviewMouseLeftButtonDown += (_, a) => { start = a.GetPosition(root); Native.GetCursorPos(out startScreen); dragged = false; pressedHere = true; root.CaptureMouse(); };
        root.PreviewMouseMove += (_, a) =>
        {
            if (a.LeftButton != MouseButtonState.Pressed || dragged || !pressedHere) return;
            if (!MovedPastThreshold(start, startScreen, a.GetPosition(root), owner.Scale)) return;
            dragged = true; pressedHere = false; ClearDoubleClickState(); root.ReleaseMouseCapture(); owner.InternalDragging = true;
            mouseInhibited = true; ResetMagnification();
            try
            {
                var data = ShellOps.DragData(e.Id, owner.DragPaths(e), !e.IsAlias&&Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),"dock",e.IsAlias);
                System.Windows.DragDrop.DoDragDrop(root, data, DragDropEffects.Copy | DragDropEffects.Link | (e.IsAlias?DragDropEffects.None:DragDropEffects.Move));
            }
            finally { owner.InternalDragging = false; mouseInhibited = false; owner.FinishDrag(); }
        };
        root.LostMouseCapture += (_, _) => { pressedHere = false; };
        root.MouseLeftButtonUp += (_, a) => { bool moved = dragged || MovedPastThreshold(start, startScreen, a.GetPosition(root), owner.Scale); bool invoke = pressedHere && !moved && !owner.InternalDragging && PointerSort.Active == null && root.IsMouseOver; pressedHere = false; if (root.IsMouseCaptured) root.ReleaseMouseCapture(); if (invoke) ActivateTile(e, root, false); a.Handled = true; };

        var menu = new ContextMenu();
        EntryMenu.Fill(menu, owner, new[] { e });
        root.ContextMenuOpening += (_, _) => EntryMenu.Fill(menu, owner, new[] { e });
        TrackDockMenu(menu);
        root.ContextMenu = menu;

        ShellDrop? bridge = null; string[]? files = null; bool entered = false;
        var hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        hover.Tick += (_, _) =>
        {
            hover.Stop(); var w = Native.Windows(e.Target);
            if (w.Count > 0) { Native.ActivateWindow(w[0].Handle); owner.Notify("窗口已激活，继续拖入目标窗口；松手前请确认目标。", false); }
        };
        void Reset() { hover.Stop(); bridge?.Dispose(); bridge = null; files = null; entered = false; }
        root.DragEnter += (_, a) =>
        {
            if(ReorderIntent(a,false)){a.Effects=ReorderEffect(a);a.Handled=true;return;}
            if (a.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if(!AcceptsFileDropTarget(e)||!InCenterDropZone(root,a)){Reset();return;}
                Reset(); files = (string[])a.Data.GetData(DataFormats.FileDrop); bridge = new ShellDrop(e.IsVirtual ? e.ShellVerb ?? "" : e.Path, files); Native.GetCursorPos(out var p);
                a.Effects = (DragDropEffects)bridge.Enter((uint)a.KeyStates, p, (uint)a.AllowedEffects); entered = true; hover.Start(); a.Handled = true;
            }
        };
        root.DragOver += (_, a) =>
        {
            if (ReorderIntent(a,false))
            {
                a.Effects = ReorderEffect(a); a.Handled = true; return;
            }
            if (files != null && bridge != null) { Native.GetCursorPos(out var p); a.Effects = (DragDropEffects)bridge.Over((uint)a.KeyStates, p, (uint)a.AllowedEffects); a.Handled = true; }
        };
        root.DragLeave += (_, _) => { Reset(); };
        root.Drop += (_, a) =>
        {
            if (ReorderIntent(a,false))
            {
                var scope=owner.Store.PinnedEntries().Select(x=>x.Id).ToArray();owner.CommitSort(scope,(string)a.Data.GetData("MacDock.Entry"),Array.IndexOf(scope,e.Id),true);a.Effects = ReorderEffect(a); a.Handled = true; return;
            }
            if (a.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if(!AcceptsFileDropTarget(e)||!InCenterDropZone(root,a)){Reset();return;}
                bool keepDrawer = false && !ShellDrop.IsExecutableTarget(e.Path);
                if (keepDrawer) owner.BeginInternalDropProtection();
                owner.Busy = true;
                try
                {
                    Native.GetCursorPos(out var p);
                    if (!entered) { files = (string[])a.Data.GetData(DataFormats.FileDrop); bridge = new ShellDrop(e.IsVirtual ? e.ShellVerb ?? "" : e.Path, files); bridge.Enter((uint)a.KeyStates, p, (uint)a.AllowedEffects); }
                    var effect = bridge?.Drop((uint)a.KeyStates, p, (uint)a.AllowedEffects) ?? 0;
                    a.Effects = (DragDropEffects)effect;
                    if (effect == 0) owner.Notify("此目标未接受文件。可悬停激活运行窗口，再继续拖入窗口。", false);
                }
                catch (Exception ex) { owner.Notify("拖放未完成：" + ex.Message); a.Effects = DragDropEffects.None; }
                finally { owner.SetExternalFileDrag(false); Reset(); if (keepDrawer) owner.EndInternalDropProtection(true); owner.Busy = false; }
                a.Handled = true;
            }
        };
        root.Unloaded += (_, _) => Reset();
        return root;
    }

    // ---------------------------------------------------------------- magnification
    internal void BeginWake()
    {
        ResetMagnification();
        wakePointer=Native.GetCursorPos(out var point)?point:null;
    }

    internal void ObserveWakeMovement()
    {
        if(wakePointer is not Native.Point start||!Native.GetCursorPos(out var point))return;
        double dx=point.X-(double)start.X,dy=point.Y-(double)start.Y;
        double threshold=Math.Max(2,2*owner.Scale);
        if(dx*dx+dy*dy>=threshold*threshold)wakePointer=null;
    }

    internal void ApplyPinnedOrder()
    {
        StopWave();var appItems=layout.Where(n=>n.Entry!=null).ToDictionary(n=>n.Entry!.Id);var ordered=owner.Store.PinnedEntries().Where(e=>appItems.ContainsKey(e.Id)).Select(e=>appItems[e.Id]).ToArray();
        for(int i=0;i<ordered.Length;i++){var view=ordered[i].Element;int current=items.Children.IndexOf(view);if(current!=i){items.Children.Remove(view);items.Children.Insert(i,view);}}
        var chrome=layout.Where(n=>n.Entry==null).ToArray();layout.Clear();layout.AddRange(ordered);layout.AddRange(chrome);items.UpdateLayout();RefreshGeometry();
    }
    bool mouseInhibited;
    readonly List<(FrameworkElement View,Transform Original,TranslateTransform Shift)> pinPreview=new();
    double[] pinThresholds=Array.Empty<double>();
    bool pinExpands;
    internal bool PreviewingPin=>pinPreview.Count>0;
    internal int PinDestination{get;private set;}=-1;
    internal bool PreviewPin(Entry entry,Native.Point p)
    {
        if(!Contains(p)){MovePinGap(-1);return false;}
        if(entry.Pinned)return true;
        if(pinPreview.Count==0)
        {
            if(!entry.IsVirtual&&!System.IO.File.Exists(entry.Path)&&!System.IO.Directory.Exists(entry.Path))return false;
            ResetMagnification();
            int count=owner.Store.PinnedEntries().Count;
            pinExpands=owner.Layout.VisibleDockCount(count+1)>owner.Layout.VisibleDockCount(count);
            pinThresholds=layout.Where(n=>n.Entry!=null).Select(n=>n.Left+n.Width/2).ToArray();
            foreach(var n in layout)
            {
                var original=n.Element.RenderTransform;var shift=new TranslateTransform();var group=new TransformGroup();
                group.Children.Add(original);group.Children.Add(shift);
                n.Element.RenderTransform=group;pinPreview.Add((n.Element,original,shift));
            }
            // Keep the reserved space until release, including brief excursions
            // outside the bar. Hit testing never follows animated tile positions.
            owner.PositionDockPreview();
        }
        double x=items.PointFromScreen(new Point(p.X,p.Y)).X;
        int index=PinDestination;
        double hysteresis=3*owner.Layout.Unit;
        if(index<0){index=0;while(index<pinThresholds.Length&&x>=pinThresholds[index])index++;}
        else
        {
            while(index<pinThresholds.Length&&x>=pinThresholds[index]+hysteresis)index++;
            while(index>0&&x<pinThresholds[index-1]-hysteresis)index--;
        }
        MovePinGap(index);
        return true;
    }
    void MovePinGap(int index)
    {
        if(index==PinDestination)return;
        int prior=PinDestination;PinDestination=index;
        double stride=owner.Layout.DockTile;
        int last=pinThresholds.Length-1;
        double Shift(int i,int slot)=>slot<0?0:pinExpands?(i<slot?-stride/2:stride/2):i>=slot&&i<last?stride:0;
        for(int i=0;i<pinPreview.Count;i++)
        {
            double target=Shift(i,index);
            if(target!=Shift(i,prior))Anim.Retarget(pinPreview[i].Shift,TranslateTransform.XProperty,target,owner.Store.Data.ReducedMotion);
        }
        if(!pinExpands&&last>=0&&(index<=last&&index>=0)!=(prior<=last&&prior>=0))
        {
            var overflow=pinPreview[last].View;
            double current=overflow.Opacity,target=index>=0&&index<=last?0:1;
            overflow.Opacity=target;
            overflow.BeginAnimation(OpacityProperty,owner.Store.Data.ReducedMotion||!Anim.Allowed?null:
                new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(110)){FillBehavior=FillBehavior.Stop});
        }
    }
    internal void ClearPinPreview()
    {
        if(pinPreview.Count==0){PinDestination=-1;return;}
        foreach(var n in pinPreview){n.View.RenderTransform=n.Original;n.View.BeginAnimation(OpacityProperty,null);n.View.Opacity=1;}
        pinPreview.Clear();pinThresholds=Array.Empty<double>();PinDestination=-1;owner.PositionDockPreview();
    }
    internal Dictionary<string,Point> PinArtworkPositions()=>layout.Where(n=>n.Entry!=null)
        .ToDictionary(n=>n.Entry!.Id,ArtworkCenter);
    Point ArtworkCenter(DockItem n)=>n.Element.PointToScreen(new Point(n.Width/2,n.Element.ActualHeight-owner.Layout.Icon/2-12*owner.Layout.Unit));
    internal void LandPin(IReadOnlyDictionary<string,Point> previous,string? inserted,Native.Point pointer)
    {
        if(owner.Store.Data.ReducedMotion||!Anim.Allowed||!IsVisible)return;
        UpdateLayout();RefreshGeometry();
        foreach(var n in layout)
        {
            if(n.Entry==null||n.Landing is not {} shift)continue;
            Point old;
            if(n.Entry.Id==inserted)old=new Point(pointer.X,pointer.Y);
            else if(!previous.TryGetValue(n.Entry.Id,out old))continue;
            shift.BeginAnimation(TranslateTransform.XProperty,null);
            shift.BeginAnimation(TranslateTransform.YProperty,null);
            var next=ArtworkCenter(n);
            shift.X=(old.X-next.X)/owner.Scale;shift.Y=(old.Y-next.Y)/owner.Scale;
            Anim.Retarget(shift,TranslateTransform.XProperty,0,false);
            Anim.Retarget(shift,TranslateTransform.YProperty,0,false);
        }
    }

    void RefreshGeometry()
    {
        if (layout.Count == 0) return;
        foreach (var item in layout) if (item.Shift != null) item.Shift.X = 0;
        items.UpdateLayout();
        for (var i = 0; i < layout.Count; i++)
        {
            var item = layout[i];
            var point = item.Element.TranslatePoint(new Point(0, 0), items);
            item.Left = point.X-(item.Landing?.X??0);
            item.Width = item.Element.ActualWidth;
        }
        for(var i=0;i<layout.Count;i++) layout[i].GapAfter=i+1<layout.Count?layout[i+1].Left-(layout[i].Left+layout[i].Width):0;
        currentScale = Enumerable.Repeat(1.0, layout.Count).ToArray(); currentLeft = layout.Select(x => x.Left).ToArray();
        ApplyTransforms();
    }

    void EnsureWave()
    {
        if (mouseInhibited) return;
        if (!pointerInside && currentScale.All(s => Math.Abs(s - 1) < .002) && currentLeft.Select((x, i) => Math.Abs(x - layout[i].Left)).All(d => d < .25))
        {
            if (running) { StopWave(); ApplyRest(); }
            return;
        }
        if (running) return;
        running = true; lastFrame = Stopwatch.GetTimestamp();
        System.Windows.Media.CompositionTarget.Rendering += OnFrame;
    }

    internal void ObservePointer(Native.Point p,bool allow=true)
    {
        // OLE cancellation need not deliver a final DragLeave/Drop. Gesture end is
        // authoritative; a missing routed event must not disable the wave forever.
        if(mouseInhibited&&!owner.InternalDragging&&(Native.GetAsyncKeyState(1)&0x8000)==0)mouseInhibited=false;
        ObserveTip(p,allow);
        bool inside=allow && WaveHit(p);
        if(inside) pointerX=items.PointFromScreen(new Point(p.X,p.Y)).X;
        if(pointerInside==inside && !inside && !running)return;
        pointerInside=inside;EnsureWave();
    }
    bool WaveHit(Native.Point p)
    {
        if(!IsVisible||wakePointer!=null||mouseInhibited||owner.InternalDragging||owner.MenuOpen||owner.Busy
            ||owner.Store.Data.ReducedMotion||!Anim.Allowed||!Native.GetWindowRect(Handle,out var r)||!r.Contains(p)||Native.WindowFromPoint(p)!=Handle)return false;
        var apps=layout.Where(i=>i.Entry!=null).ToArray();if(apps.Length==0)return false;
        var point=items.PointFromScreen(new Point(p.X,p.Y));
        return point.X>=apps[0].Left && point.X<=apps[^1].Left+apps[^1].Width;
    }

    void StopWave()
    {
        if (!running) return;
        running = false;
        System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
    }

    void ResetMagnification()
    {
        pointerInside = false;
        StopWave();
        if (layout.Count > 0)
        {
            currentScale = Enumerable.Repeat(1.0, layout.Count).ToArray();
            currentLeft = layout.Select(x => x.Left).ToArray();
        }
        ApplyTransforms();
    }

    void ApplyRest()
    {
        foreach (var item in layout)
        {
            if (item.Shift != null) item.Shift.X = 0;
            if (item.Icon != null) { item.Icon.ScaleX = 1; item.Icon.ScaleY = 1; }
        }
    }

    void OnFrame(object? sender, EventArgs e)
    {
        RenderFrames++;
        var now = Stopwatch.GetTimestamp();
        var dt = Math.Clamp((now - lastFrame) / (double)Stopwatch.Frequency, 0, .05);
        lastFrame = now;
        if (layout.Count == 0 || Surface == null) { StopWave(); return; }
        // WPF MouseLeave is not guaranteed during OLE capture, window animation, or another popup.
        // Validate screen-space ownership every active frame; there is no idle render subscription.
        if(Native.GetCursorPos(out var p))
        {
            bool valid=WaveHit(p);
            pointerInside=valid;
            if(valid)pointerX=items.PointFromScreen(new Point(p.X,p.Y)).X;
        }

        if (!pointerInside || mouseInhibited)
        {
            for (var i = 0; i < currentScale.Length; i++) currentScale[i] += (1 - currentScale[i]) * Ease(dt);
            for (var i = 0; i < currentLeft.Length; i++) currentLeft[i] += (layout[i].Left - currentLeft[i]) * Ease(dt);
        }
        else
        {
            var (lefts, scales) = Compute();
            for (var i = 0; i < currentScale.Length; i++)
            {
                currentScale[i] += (scales[i] - currentScale[i]) * Ease(dt);
                currentLeft[i] += (lefts[i] - currentLeft[i]) * Ease(dt);
            }
            if(currentScale.Select((s,i)=>Math.Abs(s-scales[i])).All(d=>d<.0015) && currentLeft.Select((x,i)=>Math.Abs(x-lefts[i])).All(d=>d<.2))
            { currentScale=scales;currentLeft=lefts;ApplyTransforms();StopWave();return; }
        }
        ApplyTransforms();
        if (!pointerInside && currentScale.All(s => Math.Abs(s - 1) < .0015) && currentLeft.Select((x, i) => Math.Abs(x - layout[i].Left)).All(d => d < .2))
        {
            StopWave(); ApplyRest();
        }
    }

    static double Ease(double dt) => 1 - Math.Exp(-dt * 26);

    (double[] Lefts, double[] Scales) Compute()
    {
        var n = layout.Count;
        var scales = new double[n];
        var lefts = new double[n];
        var slot=Math.Max(24,owner.Layout.DockTile);
        for (var i = 0; i < n; i++)
        {
            var item = layout[i];
            lefts[i]=item.Left;scales[i]=1;
            if (item.Icon == null || item.Entry == null) continue;
            var center = item.Left + item.Width / 2;
            var d=(center-pointerX)/(slot*1.05);
            // The compact base keeps the absolute peak smaller. Preserve a
            // visible shoulder on both sides as the focus moves between tiles.
            double squared=d*d;
            var weight=Math.Max(0,(WaveProfile(squared)-waveEdge)/(1-waveEdge));
            scales[i]=1+(Peak-1)*weight;
            // Fixed baseline slots, bounded local displacement. Never translate/re-anchor the
            // whole row to the pointer. Distant icons, divider and drawer remain exactly fixed.
            lefts[i]+=4*owner.Layout.Unit*d*weight;
        }
        return (lefts, scales);
    }

    void ApplyTransforms()
    {
        for (var i = 0; i < layout.Count && i < currentScale.Length; i++)
        {
            var item = layout[i];
            if (item.Shift != null) item.Shift.X = currentLeft[i] - item.Left;
            if (item.Icon != null && item.Entry != null)
            {
                var s = currentScale[i];
                item.Icon.ScaleX = s; item.Icon.ScaleY = s;
            }
        }
    }

    void Animate(Image image, double scale, double lift) { }

    void AnimateIcon(FrameworkElement icon, double scale)
    {
        if (icon.RenderTransform is not ScaleTransform transform) return;
        var ms = owner.Store.Data.ReducedMotion ? 0 : 140;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease });
    }

    // A single delayed label, anchored to stable slots rather than magnified artwork.
    // No between-show shortcut and no permanent polling timer.
    readonly DispatcherTimer tipTimer=new(){Interval=TimeSpan.FromMilliseconds(1000)};
    readonly ToolTip labelTip=new(){Placement=System.Windows.Controls.Primitives.PlacementMode.Top,StaysOpen=true,IsHitTestVisible=false,MaxWidth=360};
    DockItem? tipCandidate;string tipSuppressed="";
    internal bool TipVisible=>labelTip.IsOpen;
    internal nint TipWindowHandle=>(PresentationSource.FromVisual(labelTip) as System.Windows.Interop.HwndSource)?.Handle??0;
    internal string TipEntryId=>tipCandidate?.Entry?.Id??"";
    internal int TipOpens;
    internal bool TipHasNativeSurface {get{if(!labelTip.IsOpen||PresentationSource.FromVisual(labelTip) is not System.Windows.Interop.HwndSource source||!Native.IsWindowVisible(source.Handle)||!Native.GetWindowRect(source.Handle,out var r)||r.Right<=r.Left||r.Bottom<=r.Top)return false;var z=new List<nint>();Native.EnumWindows((h,_)=>{if(Native.IsWindowVisible(h))z.Add(h);return true;},0);return z.IndexOf(source.Handle)>=0&&z.IndexOf(source.Handle)<z.IndexOf(Handle);}}
    void CancelTip(bool suppress=false){if(suppress&&tipCandidate!=null)tipSuppressed=tipCandidate.Entry?.Id??"drawer";tipTimer.Stop();labelTip.IsOpen=false;tipCandidate=null;}
    internal void DismissTip()=>CancelTip(true);
    void ObserveTip(Native.Point p,bool allow){
        if(!allow||!IsVisible||owner.Busy||owner.MenuOpen||owner.InternalDragging||!Contains(p)||Native.WindowFromPoint(p)!=Handle||(Native.GetAsyncKeyState(1)&0x8000)!=0||(Native.GetAsyncKeyState(2)&0x8000)!=0){CancelTip();if(!IsVisible||!Contains(p))tipSuppressed="";return;}
        var point=items.PointFromScreen(new Point(p.X,p.Y));var item=layout.FirstOrDefault(n=>point.X>=n.Left&&point.X<n.Left+n.Width&&(n.Entry!=null||n.Element==drawerButton));
        if(item==null){CancelTip();tipSuppressed="";return;}string key=item.Entry?.Id??"drawer";if(key==tipSuppressed)return;tipSuppressed="";
        if(item==tipCandidate)return;CancelTip();tipCandidate=item;tipTimer.Start();
    }
    void ShowTip(){tipTimer.Stop();var expected=tipCandidate;if(expected==null)return;Native.GetCursorPos(out var p);ObserveTip(p,true);if(tipCandidate!=expected)return;
        var text=Theme.Text(expected.Entry?.Name??"应用抽屉 · Ctrl+Alt+空格",12.5);text.TextWrapping=TextWrapping.Wrap;text.MaxWidth=320;labelTip.Content=text;labelTip.PlacementTarget=this;var anchor=items.TranslatePoint(new Point(expected.Left+expected.Width/2,0),this);labelTip.PlacementRectangle=new Rect(anchor.X,0,0,0);labelTip.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));labelTip.HorizontalOffset=-labelTip.DesiredSize.Width/2;labelTip.VerticalOffset=-8*owner.Layout.Unit;labelTip.IsOpen=true;TipOpens++;
    }

    public void UpdateDots()
    {
        foreach (var e in owner.Store.Data.Entries.Where(e => e.Pinned))
            if (dots.TryGetValue(e.Id, out var dot)) dot.Opacity = Native.Windows(e.Target).Count > 0 ? .95 : 0;
    }

    internal string MagnifyState => string.Join(",",currentScale.Select(s=>s.ToString("F3")))+" | "+string.Join(",",currentLeft.Select((x,i)=>$"{(x-layout[i].Left):F1}"));
    internal double MaxScale => currentScale.Length==0?1:currentScale.Max();
    internal bool WaveRunning => running;
    internal Rect GatewayBounds=>drawerButton!.TransformToAncestor(Surface!).TransformBounds(new Rect(drawerButton.RenderSize));
    internal double MaxDisplacement=>currentLeft.Length==0?0:currentLeft.Select((x,i)=>Math.Abs(x-layout[i].Left)).Max();
    internal Native.Point GatewayCenter { get{var p=drawerButton!.PointToScreen(new Point(drawerButton.ActualWidth/2,drawerButton.ActualHeight/2));return new Native.Point((int)p.X,(int)p.Y);} }
    internal double GatewayLift => sheetLift.Y;
    internal double GatewayCenterFromTop => GatewayBounds.Top + GatewayBounds.Height / 2;
    internal double GatewayPhase => gatewayPhase;
    internal bool GatewayPressActive => drawerButton != null && Mouse.Captured == drawerButton;

    internal void PaintGateway(GatewayPose pose)
    {
        gatewayPhase = pose.Phase;
        if (drawerGlyph is { } glyph)
            glyph.Paint(pose, new Point(glyph.Width / 2, glyph.Height / 2));
    }

    internal void SetGatewayRest(bool open)
    {
        double phase = open ? 1 : 0;
        if (gatewayPhase != phase) PaintGateway(GatewayPose.At(phase, 29 * owner.Layout.Unit));
    }
    internal IReadOnlyList<Rect> ArtworkBounds => layout.Where(i=>i.Entry!=null).Select(i=>
    {
        var host=((Grid)((Border)i.Element).Child).Children.OfType<Grid>().First();
        return host.TransformToAncestor(Surface!).TransformBounds(new Rect(0,0,host.ActualWidth,host.ActualHeight));
    }).ToArray();

    // Hit test in screen coordinates, so the test can prove a magnified tile still owns the
    // pointer position (transforms are honoured by WPF hit testing).
    internal string? HitTestEntry(Native.Point screen)
    {
        var local=PointFromScreen(new System.Windows.Point(screen.X,screen.Y));
        var hit=InputHitTest(local) as DependencyObject;
        while(hit!=null)
        {
            if(hit is FrameworkElement element && element.Tag is Entry entry) return entry.Id;
            hit=VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    internal Native.Point TileCenter(string id)
    {
        var tile = items.Children.OfType<FrameworkElement>().First(x => x.Tag is Entry e && e.Id == id);
        var p = tile.PointToScreen(new Point(tile.ActualWidth / 2, tile.ActualHeight / 2));
        return new Native.Point((int)p.X, (int)p.Y);
    }

    void BlankOver(object? s, DragEventArgs a) { if (a.Handled) return; if (a.Data.GetDataPresent(DataFormats.FileDrop)) { bool ext=!a.Data.GetDataPresent("MacDock.Entry"); if(ext){ var files=(string[])a.Data.GetData(DataFormats.FileDrop); var destDir=owner.OverrideDesktopFolder??Desktop.UserFolder; bool copy=Controller.ShouldCopyToDirectory(files,destDir,a.KeyStates); a.Effects=copy?DragDropEffects.Copy:DragDropEffects.Move; } else a.Effects=DragDropEffects.Link; a.Handled = true; } else if(a.Data.GetDataPresent(DataFormats.Bitmap)){ a.Effects=DragDropEffects.Copy; a.Handled=true; } }
    void BlankDrop(object? s, DragEventArgs a)
    {
        if(a.Handled)return;
        if(a.Data.GetDataPresent("MacDock.Entry")){var entry=owner.Store.Data.Entries.FirstOrDefault(e=>e.Id==(string)a.Data.GetData("MacDock.Entry"));if(entry!=null)owner.SetPinned(new[]{entry},true);a.Effects=DragDropEffects.Link;a.Handled=true;return;}
        if (a.Data.GetDataPresent(DataFormats.FileDrop)) { var files=(string[])a.Data.GetData(DataFormats.FileDrop); var destDir=owner.OverrideDesktopFolder??Desktop.UserFolder; bool copy=Controller.ShouldCopyToDirectory(files,destDir,a.KeyStates); owner.DropFilesToDesktop(files, copy); a.Effects = copy ? DragDropEffects.Copy : DragDropEffects.Move; a.Handled = true; } else if (a.Data.GetDataPresent(DataFormats.Bitmap)) { owner.SaveDroppedBitmapToDesktop(a.Data); a.Effects = DragDropEffects.Copy; a.Handled = true; }
    }
    static DragDropEffects ReorderEffect(DragEventArgs a)=>(a.AllowedEffects&DragDropEffects.Move)!=0?DragDropEffects.Move:DragDropEffects.Link;
    static bool InCenterDropZone(FrameworkElement tile, DragEventArgs a) { var p = a.GetPosition(tile); double w = tile.ActualWidth, h = tile.ActualHeight; return w > 0 && h > 0 && Math.Abs(p.X - w * 0.5) <= w * 0.325 && Math.Abs(p.Y - h * 0.5) <= h * 0.325; }
    internal static bool AcceptsFileDropTarget(Entry? target)
    {
        if(target==null||target.IsVirtual)return false;
        if(System.IO.Directory.Exists(target.Path)||ShellDrop.IsExecutableTarget(target.Path))return true;
        if(target.Path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))
        {
            var resolved=IconResources.ReadLink(target.Path).Target;
            if(!string.IsNullOrWhiteSpace(resolved)&&System.IO.Directory.Exists(resolved))return true;
        }
        return false;
    }
    bool ReorderIntent(DragEventArgs a,bool drawer,Entry? target=null)
    {
        if(!a.Data.GetDataPresent("MacDock.Entry"))return false;
        if(target!=null&&a.Data.GetDataPresent(DataFormats.FileDrop)&&(a.Data.GetData("MacDock.Entry") as string)!=target.Id&&AcceptsFileDropTarget(target))return false;
        var origin=a.Data.GetData("MacDock.Origin") as string??"dock";
        if(!drawer)return origin=="dock"&&(a.KeyStates&(DragDropKeyStates.ControlKey|DragDropKeyStates.ShiftKey))==0;
        return origin=="drawer"&&owner.Store.Data.Mode=="自定义"&&(a.KeyStates&(DragDropKeyStates.ControlKey|DragDropKeyStates.ShiftKey))==0;
    }

    static bool MovedPastThreshold(Point startLocal, Native.Point startScreen, Point currentLocal, double scale)
    {
        double threshold = Math.Max(5, SystemParameters.MinimumHorizontalDragDistance);
        if ((currentLocal - startLocal).Length >= threshold) return true;
        if (Native.GetCursorPos(out var cur))
        {
            double s = Math.Max(1, scale);
            double dx = cur.X - startScreen.X, dy = cur.Y - startScreen.Y;
            if (dx * dx + dy * dy >= (threshold * s) * (threshold * s)) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- drawer tiles
    public FrameworkElement Tile(Entry e, bool inDrawer)
    {
        var l = owner.Layout;
        var box = inDrawer ? l.DrawerTile : l.DockTile;
        var height = inDrawer ? l.DrawerTileHeight : l.DockTileHeight;
        var iconSize = inDrawer ? l.DrawerIcon : l.Icon;
        var root = new Border
        {
            Width = box, Height = height, CornerRadius = new CornerRadius(inDrawer ? 12 : 10),
            Background = Brushes.Transparent, AllowDrop = true, Cursor = inDrawer && owner.Store.Data.DoubleClickToOpen ? Cursors.Arrow : Cursors.Hand, Tag = e,
            Margin = new Thickness(0, 0, inDrawer ? l.GapX : l.DockGap / 2, inDrawer ? l.RowHeight - l.DrawerTileHeight : 0)
        };
        System.Windows.Automation.AutomationProperties.SetName(root, e.Name);
        if(!inDrawer)Theme.Tip(root,e.Name);
        System.Windows.Automation.AutomationProperties.SetHelpText(root,e.Name);

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var iconScale = new ScaleTransform(1, 1);
        var iconHost = new Grid
        {
            Width = iconSize, Height = iconSize, RenderTransformOrigin = new Point(.5, .55),
            RenderTransform = iconScale
        };
        iconHost.Children.Add(Icons.View(owner.Icon(e.Path, iconSize), iconSize));
        panel.Children.Add(iconHost);
        if (inDrawer)
        {
            var label = new TextBlock
            {
                Text = e.Name, Foreground = Theme.Ink, FontSize = Math.Max(13,13*l.Unit), FontFamily = Theme.Ui, FontWeight=FontWeights.Medium, TextWrapping=TextWrapping.Wrap,Height=2*l.DrawerLabelLine,LineHeight=l.DrawerLabelLine,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(2, l.DrawerLabelGap, 2, 0), MaxWidth = box - 4
            };
            panel.Children.Add(label);
        }
        else
        {
            var dot = new Border
            {
                Width = 4.2, Height = 4.2, CornerRadius = new CornerRadius(2.1), Background = Theme.Ink,
                Opacity = Native.Windows(e.Target).Count > 0 ? .95 : 0, Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            panel.Children.Add(dot); dots[e.Id] = dot;
        }
        root.Child = panel;

        if(inDrawer)root.BorderThickness=new Thickness(1);
        root.MouseEnter += (_, _) => { if (inDrawer) owner.Drawer?.PaintTile(root); AnimateIcon(iconHost, inDrawer ? 1.05 : 1); };
        root.MouseLeave += (_, _) => { if(inDrawer)owner.Drawer?.PaintTile(root);AnimateIcon(iconHost, 1); };

        Point start = default; Native.Point startScreen = default; bool dragged = false, pressedHere = false, selectionHandled = false;
        if(inDrawer)PointerSort.Attach(root,owner.Drawer,()=>true,()=>owner.Drawer!.SortTiles(e.Source=="manual"),()=>owner.Drawer?.Scroller,(ids,id,index)=>owner.CommitSort(ids,id,index,false),active=>{if(active)ClearDoubleClickState();owner.InternalDragging=active;},()=>{if(Native.IsControlDown()||Native.IsShiftDown()){var ctrl=Native.IsControlDown();var shift=Native.IsShiftDown();ClearDoubleClickState();owner.Drawer?.FocusGrid();owner.Drawer?.ClickSelect(e,ctrl,shift);return;}ActivateTile(e,root,true);},()=>{owner.Drawer!.FocusGrid();if(owner.Drawer.IsSelected(e)!=true)owner.Drawer.ClickSelect(e,false,false);},external:()=>{ClearDoubleClickState();owner.InternalDragging=true;try{var data=ShellOps.DragData(e.Id,owner.DragPaths(e),false,"drawer",e.IsAlias);System.Windows.DragDrop.DoDragDrop(root,data,DragDropEffects.Copy|DragDropEffects.Link|(e.IsAlias?DragDropEffects.None:DragDropEffects.Move));}finally{owner.InternalDragging=false;owner.FinishDrag();}},reduced:()=>owner.Store.Data.ReducedMotion,prepare:()=>owner.SettleDrawerMotion(),owner:owner,sortAllowed:()=>owner.Store.Data.Mode=="自定义");
        root.PreviewMouseLeftButtonDown += (_, a) =>
        {
            start = a.GetPosition(root); Native.GetCursorPos(out startScreen); dragged = false; pressedHere = true; selectionHandled = false;
            if (!inDrawer) return;
            bool ctrl = Native.IsControlDown();
            bool shift = Native.IsShiftDown();
            if (ctrl || shift)
            {
                selectionHandled = true;
                ClearDoubleClickState();
                owner.Drawer?.FocusGrid();
                owner.Drawer?.ClickSelect(e, ctrl, shift);
                a.Handled = true;
                return;
            }
            owner.Drawer?.FocusGrid();
            if (owner.Drawer?.IsSelected(e) != true) owner.Drawer?.ClickSelect(e, false, false);
        };
        root.PreviewMouseMove += (_, a) =>
        {
            if (a.LeftButton != MouseButtonState.Pressed || dragged || !pressedHere) return;
            if (!MovedPastThreshold(start, startScreen, a.GetPosition(root), owner.Scale)) return;

            dragged = true; pressedHere = false; ClearDoubleClickState(); owner.InternalDragging = true;
            if (!inDrawer) { pointerInside = false; EnsureWave(); }
            try
            {
                var reference=e.IsAlias||(owner.Drawer?.Selection.Any(x=>x.IsAlias)==true);
                var data = ShellOps.DragData(e.Id, owner.DragPaths(e), !reference&&Native.IsShiftDown(),inDrawer?"drawer":"dock",reference);
                System.Windows.DragDrop.DoDragDrop(root, data, DragDropEffects.Copy | DragDropEffects.Link | (reference?DragDropEffects.None:DragDropEffects.Move));
            }
            finally { owner.InternalDragging = false; }
        };
        root.LostMouseCapture += (_, _) => { pressedHere = false; };
        root.MouseLeftButtonUp += (_, a) =>
        {
            bool moved = dragged || MovedPastThreshold(start, startScreen, a.GetPosition(root), owner.Scale);
            bool shouldOpen = pressedHere && !moved && !selectionHandled && !owner.InternalDragging && PointerSort.Active == null && root.IsMouseOver && !Native.IsControlDown() && !Native.IsShiftDown();
            pressedHere = false;
            selectionHandled = false;
            if (shouldOpen) ActivateTile(e, root, inDrawer);
            a.Handled = true;
        };

        root.PreviewMouseRightButtonDown += (_, _) =>
        {
            Native.SuppressAppStartingCursor();
            if (!inDrawer) return;
            owner.Drawer?.FocusGrid();
            if (owner.Drawer?.IsSelected(e) != true) owner.Drawer?.ClickSelect(e, false, false);
        };
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            owner.MenuOpen = true;
            var entries = inDrawer ? owner.Drawer?.Selection ?? new[] { e } : new[] { e };
            if (entries.Count == 0) entries = new[] { e };
            EntryMenu.Fill(menu, owner, entries);
        };
        menu.Closed += (_, _) => owner.MenuOpen = false;
        root.ContextMenu = menu;
        root.ContextMenuOpening+=(_,a)=>
        {
            if(!inDrawer)return;
            a.Handled=true;owner.Drawer?.OpenItemMenu(root,false);
        };

        ShellDrop? bridge = null; string[]? files = null; bool entered = false;
        var hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        hover.Tick += (_, _) =>
        {
            hover.Stop(); var w = Native.Windows(e.Target);
            if (w.Count > 0) { Native.ActivateWindow(w[0].Handle); owner.Notify("窗口已激活，继续拖入目标窗口；松手前请确认目标。", false); }
        };
        void Reset() { hover.Stop(); bridge?.Dispose(); bridge = null; files = null; entered = false; }
        root.DragEnter += (_, a) =>
        {
            if(ReorderIntent(a,inDrawer,e)){a.Effects=ReorderEffect(a);a.Handled=true;return;}
            if (a.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if(!AcceptsFileDropTarget(e)||!InCenterDropZone(root,a)){Reset();return;}
                Reset(); files = (string[])a.Data.GetData(DataFormats.FileDrop); bridge = new ShellDrop(e.IsVirtual ? e.ShellVerb ?? "" : e.Path, files); Native.GetCursorPos(out var p);
                a.Effects = (DragDropEffects)bridge.Enter((uint)a.KeyStates, p, (uint)a.AllowedEffects); if(inDrawer && a.Effects!=DragDropEffects.None)owner.Drawer?.SetDropTarget(e); entered = true; hover.Start(); a.Handled = true;
            }
        };
        root.DragOver += (_, a) =>
        {
            if (ReorderIntent(a,inDrawer,e))
            {
                a.Effects = ReorderEffect(a); a.Handled = true; return;
            }
            if (files != null && bridge != null) { Native.GetCursorPos(out var p); a.Effects = (DragDropEffects)bridge.Over((uint)a.KeyStates, p, (uint)a.AllowedEffects); a.Handled = true; }
        };
        root.DragLeave += (_, _) => { if(inDrawer)owner.Drawer?.SetDropTarget(null); Reset(); };
        root.Drop += (_, a) =>
        {
            if (ReorderIntent(a,inDrawer,e))
            {
                var scope=inDrawer?owner.Drawer!.SortTiles(e.Source=="manual").Select(x=>((Entry)x.Tag).Id).ToArray():owner.Store.PinnedEntries().Select(x=>x.Id).ToArray();owner.CommitSort(scope,(string)a.Data.GetData("MacDock.Entry"),Array.IndexOf(scope,e.Id),!inDrawer);a.Effects = ReorderEffect(a);
                a.Handled = true; return;
            }
            if (a.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if(!AcceptsFileDropTarget(e)||!InCenterDropZone(root,a)){Reset();return;}
                bool keepDrawer = inDrawer && !ShellDrop.IsExecutableTarget(e.Path);
                if (keepDrawer) owner.BeginInternalDropProtection();
                owner.Busy = true;
                try
                {
                    Native.GetCursorPos(out var p);
                    if (!entered) { files = (string[])a.Data.GetData(DataFormats.FileDrop); bridge = new ShellDrop(e.IsVirtual ? e.ShellVerb ?? "" : e.Path, files); bridge.Enter((uint)a.KeyStates, p, (uint)a.AllowedEffects); }
                    var effect = bridge?.Drop((uint)a.KeyStates, p, (uint)a.AllowedEffects) ?? 0;
                    a.Effects = (DragDropEffects)effect;
                    if (effect == 0) owner.Notify("此目标未接受文件。可悬停激活运行窗口，再继续拖入窗口。", false);
                    else if (!ShellDrop.IsExecutableTarget(e.Path)) owner.SyncDesktopNow();
                }
                catch (Exception ex) { owner.Notify("拖放未完成：" + ex.Message); a.Effects = DragDropEffects.None; }
                finally { if(inDrawer)owner.Drawer?.SetDropTarget(null); owner.SetExternalFileDrag(false); Reset(); if (keepDrawer) owner.EndInternalDropProtection(true); owner.Busy = false; }
                a.Handled = true;
            }
        };
        root.Unloaded += (_, _) => Reset();
        return root;
    }
}
