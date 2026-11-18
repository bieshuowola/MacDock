using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
namespace MacDock;

// Local sorting owns one captured gesture. File OLE is reserved for explicit Ctrl/Shift
// and other views. Geometry is measured once, independently of the preview transforms.
internal sealed class PointerSort
{
    internal static PointerSort? Active;
    internal static string LastFocusTrace="";
    internal int Destination=>destination;
    internal bool HasGap=>nodes.Any(n=>n.View!=source&&(Math.Abs(n.Shift.X)>.5||Math.Abs(n.Shift.Y)>.5));
    readonly FrameworkElement source;readonly GlassWindow window;readonly Func<IReadOnlyList<FrameworkElement>> elements;
    readonly ScrollViewer? scroll;readonly Action<IReadOnlyList<string>,string,int> commit;readonly Action<bool> state;readonly Action click;
    Action? external;
    Controller? owner;bool overDock;Func<bool>? sortAllowed;
    readonly ScaleTransform ghostScale=new();bool ghostDock;
    Action? prepare;bool preparing;
    IInputElement? focusBefore;
    bool reducedMotion;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(35)};
    readonly List<(FrameworkElement View,Rect Bounds,Transform Original,TranslateTransform Shift,double Opacity)> nodes=new();
    readonly List<Entry> draggedEntries=new();
    readonly HashSet<string> draggedIds=new(StringComparer.Ordinal);
    internal bool IsMultiDrag => draggedEntries.Count > 1;
    internal bool IsDraggingDrawer => started && !ended && owner != null;
    internal nint GhostHandle => ghost is { IsVisible: true } g ? g.Handle : 0;
    Point down,offset;Native.Point downScreen;double initialScroll;int destination=-1;bool started,moved,ended,hotkey;GlassWindow? ghost;HwndSourceHook? hook;
    PointerSort(FrameworkElement root,GlassWindow window,Func<IReadOnlyList<FrameworkElement>> elements,ScrollViewer? scroll,Action<IReadOnlyList<string>,string,int> commit,Action<bool> state,Action click,Point down,Native.Point downScreen)
    {source=root;this.window=window;this.elements=elements;this.scroll=scroll;this.commit=commit;this.state=state;this.click=click;this.down=down;this.downScreen=downScreen;timer.Tick+=(_,_)=>Update(autoScroll:true);}
    bool ExceededScreenDragThreshold(Native.Point cur)
    {
        double threshold=Math.Max(5,SystemParameters.MinimumHorizontalDragDistance);
        double scale=PresentationSource.FromVisual(window)!=null?Math.Max(1,VisualTreeHelper.GetDpi(window).DpiScaleX):1;
        double dx=cur.X-downScreen.X,dy=cur.Y-downScreen.Y;
        return dx*dx+dy*dy>=(threshold*scale)*(threshold*scale);
    }
    bool ExceededDragThreshold(Point surfacePos)
    {
        double threshold=Math.Max(5,SystemParameters.MinimumHorizontalDragDistance);
        if((surfacePos-down).Length>=threshold)return true;
        return Native.GetCursorPos(out var cur)&&ExceededScreenDragThreshold(cur);
    }
    internal static void Attach(FrameworkElement root,GlassWindow? window,Func<bool> allowed,Func<IReadOnlyList<FrameworkElement>> elements,Func<ScrollViewer?>? scroll,Action<IReadOnlyList<string>,string,int> commit,Action<bool> state,Action click,Action? select=null,Action? external=null,Func<bool>? reduced=null,Action? prepare=null,Controller? owner=null,Func<bool>? sortAllowed=null)
    {
        root.PreviewMouseLeftButtonDown+=(_,a)=>
        {
            if(!allowed()||Native.IsControlDown()||Native.IsShiftDown()||(Keyboard.Modifiers&ModifierKeys.Alt)!=0)return;
            var host=Window.GetWindow(root) as GlassWindow??window;
            if(host==null)return;
            Active?.End(false);
            select?.Invoke();
            Native.GetCursorPos(out var pointer);
            var session=new PointerSort(root,host,elements,scroll?.Invoke(),commit,state,click,a.GetPosition(host.Surface),pointer)
                {external=external,reducedMotion=reduced?.Invoke()==true,prepare=prepare,owner=owner,sortAllowed=sortAllowed};
            Active=session;
            if(!root.CaptureMouse()&&!session.CaptureOnFlatSurface())session.End(false);
            a.Handled=true;
        };
        root.PreviewMouseMove+=(_,a)=>{if(Active is not { } s||s.source!=root)return;a.Handled=true;if(a.LeftButton!=MouseButtonState.Pressed){s.End(false);return;}if(!s.started&&s.ExceededDragThreshold(a.GetPosition(s.window.Surface))){s.moved=true;s.Start();}if(s.started){s.moved=true;s.Update();}};
        root.PreviewMouseLeftButtonUp+=(_,a)=>{if(Active is not { } s||s.source!=root)return;a.Handled=true;if(s.ExceededDragThreshold(a.GetPosition(s.window.Surface)))s.moved=true;s.End(s.started||new Rect(root.RenderSize).Contains(a.GetPosition(root)));};
        root.PreviewMouseRightButtonDown+=(_,_)=>{if(Active is {} s&&s.source==root)s.End(false);};
        root.LostMouseCapture+=(_,_)=>{if(Active is { } s&&s.source==root&&!s.preparing)s.End(false);};
        root.Unloaded+=(_,_)=>
        {
            if(Active is not {} s||s.source!=root||s.preparing)return;
            // WPF can deliver the mesh-to-flat Unloaded event after the same tile
            // has already been reattached, even while the preview HWND is created.
            if(s.window.IsVisible&&s.window.Surface is {} surface&&root.IsDescendantOf(surface))return;
            s.End(false);
        };
    }
    bool CaptureOnFlatSurface()
    {
        if(prepare==null)return false;
        preparing=true;
        try
        {
            prepare();
            window.UpdateLayout();
            if(window.Surface is not {} surface||!source.IsDescendantOf(surface))return false;
            down=surface.PointFromScreen(new Point(downScreen.X,downScreen.Y));
            return source.CaptureMouse();
        }
        finally{preparing=false;}
    }

    void Start()
    {
        focusBefore=Keyboard.FocusedElement;
        LastFocusTrace="start="+focusBefore?.GetType().Name;
        if(prepare!=null&&!CaptureOnFlatSurface()){End(false);return;}
        started=true;moved=true;state(true);window.UpdateLayout();initialScroll=scroll?.VerticalOffset??0;
        if(ended)return;
        foreach(var view in elements()){
            // TranslatePoint round-trips through the presentation root. A clipped row in
            // Viewport2DVisual3D has no screen projection and returns (0,0). Stay wholly
            // inside the shared 2D source tree, including rows outside the viewport.
            var p=view.TransformToAncestor(window.Surface!).Transform(new Point());var shift=new TranslateTransform();var original=view.RenderTransform;
            nodes.Add((view,new Rect(p,view.RenderSize),original,shift,view.Opacity));var group=new TransformGroup();group.Children.Add(original);group.Children.Add(shift);view.RenderTransform=group;
        }
        FrameworkElement preview;double width=source.ActualWidth,height=source.ActualHeight;
        draggedEntries.Clear(); draggedIds.Clear();
        if(owner!=null && source.Tag is Entry primary)
        {
            if(owner.Drawer!=null && owner.Drawer.IsSelected(primary) && owner.Drawer.Selection.Count>1)
            {
                draggedEntries.Add(primary);
                foreach(var item in owner.Drawer.Selection) if(item.Id!=primary.Id) draggedEntries.Add(item);
            }
            else draggedEntries.Add(primary);
            foreach(var item in draggedEntries) draggedIds.Add(item.Id);

            double iconSize=owner.Layout.DrawerIcon;
            if(draggedEntries.Count==1)
            {
                width=height=iconSize;
                preview=Icons.View(owner.Icon(primary.Path,width),width);
                preview.RenderTransformOrigin=new Point(.5,.5);
                preview.RenderTransform=ghostScale;
                offset=new Point(width/2,height/2);
            }
            else
            {
                width=iconSize+36; height=iconSize+32;
                var canvas=new Grid{Width=width,Height=height,RenderTransformOrigin=new Point(.5,.5),RenderTransform=ghostScale};
                var cards=draggedEntries.Take(3).ToList();
                var offsets=new (double X,double Y,double Angle,double Alpha)[]{(0,0,0,1.0),(6,5,-5,0.92),(12,9,6,0.84)};
                for(int i=cards.Count-1;i>=0;i--)
                {
                    var (dx,dy,angle,alpha)=offsets[i];
                    var icon=Icons.View(owner.Icon(cards[i].Path,iconSize),iconSize);
                    icon.Opacity=alpha;
                    icon.HorizontalAlignment=HorizontalAlignment.Left;
                    icon.VerticalAlignment=VerticalAlignment.Top;
                    icon.Margin=new Thickness(8+dx,12+dy,0,0);
                    icon.RenderTransformOrigin=new Point(.5,.5);
                    if(Math.Abs(angle)>0.1) icon.RenderTransform=new RotateTransform(angle);
                    else icon.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=10,ShadowDepth=2,Opacity=.38,Color=Colors.Black};
                    canvas.Children.Add(icon);
                }
                var badge=new Border
                {
                    MinWidth=22,Height=22,Padding=new Thickness(5,0,5,0),CornerRadius=new CornerRadius(11),
                    Background=new SolidColorBrush(Color.FromRgb(0,120,212)),
                    BorderBrush=Brushes.White,BorderThickness=new Thickness(1.5),
                    HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Top,
                    Margin=new Thickness(0,2,4,0),
                    Child=new TextBlock{Text=draggedEntries.Count.ToString(),Foreground=Brushes.White,FontSize=11.5,FontWeight=FontWeights.Bold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}
                };
                canvas.Children.Add(badge);
                preview=canvas;
                offset=new Point(8+iconSize/2,12+iconSize/2);
            }
        }
        else{var dpi=VisualTreeHelper.GetDpi(source);var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(width*dpi.DpiScaleX)),Math.Max(1,(int)Math.Ceiling(height*dpi.DpiScaleY)),dpi.PixelsPerInchX,dpi.PixelsPerInchY,PixelFormats.Pbgra32);image.Render(source);image.Freeze();preview=new Image{Source=image,Stretch=Stretch.Fill};var rect=nodes.Single(n=>n.View==source).Bounds;offset=new Point(down.X-rect.X,down.Y-rect.Y);}
        var previewWindow=new GlassWindow(material:false){Width=width,Height=height,IsHitTestVisible=false,Opacity=.94,Content=preview,Title="MacDock drag preview"};
        try
        {
            // Window creation can dispatch capture and layout events. End must
            // never close a preview while WPF is still creating its HWND.
            new WindowInteropHelper(previewWindow).EnsureHandle();
            if(ended)return;
            var handle=previewWindow.Handle;
            Native.SetWindowLongPtr(handle,-20,(nint)(Native.GetWindowLongPtr(handle,-20).ToInt64()|0x20));
            Native.GetCursorPos(out var initialPointer);
            MoveGhost(previewWindow,initialPointer,VisualTreeHelper.GetDpi(window));
            previewWindow.Show();
            if(ended)return;
            Native.GetCursorPos(out var currentPointer);
            MoveGhost(previewWindow,currentPointer,VisualTreeHelper.GetDpi(previewWindow));
            ghost=previewWindow;
        }
        catch(Exception error)
        {
            Program.Log(error);
            End(false);
            return;
        }
        finally{if(ghost!=previewWindow)previewWindow.Close();}
        RaiseGhost(ghost.Handle);
        if(IsMultiDrag) foreach(var n in nodes) { if(n.View.Tag is Entry item && draggedIds.Contains(item.Id)) n.View.Opacity=0.32; }
        else source.Opacity=0;
        hotkey=Native.RegisterHotKey(window.Handle,0x4D17,0,27);hook=(nint h,int m,nint w,nint l,ref bool handled)=>{if(m==0x312&&(int)w==0x4D17){handled=true;End(false);}return 0;};HwndSource.FromHwnd(window.Handle).AddHook(hook);timer.Start();Update();
        LastFocusTrace+=";preview="+Keyboard.FocusedElement?.GetType().Name;
    }
    static bool NeedsGhostRaise(nint ghostHwnd,GlassWindow? primary,Controller? ctrl)
    {
        bool CheckSurface(GlassWindow? s) => s!=null && (
            Native.IsWindowAbove(s.Handle,ghostHwnd) ||
            Native.IsWindowAbove(s.HostGlassBackdropHandle,ghostHwnd) ||
            Native.IsWindowAbove(s.HostGlassShadowHandle,ghostHwnd));
        return CheckSurface(primary) || CheckSurface(ctrl?.Dock) || CheckSurface(ctrl?.Drawer);
    }
    void MoveGhost(GlassWindow preview,Native.Point pointer,DpiScale scale)
    {
        var position=new Native.Point(pointer.X-(int)Math.Round(offset.X*scale.DpiScaleX),pointer.Y-(int)Math.Round(offset.Y*scale.DpiScaleY));
        bool samePos=Native.GetWindowRect(preview.Handle,out var actual)&&actual.Left==position.X&&actual.Top==position.Y;
        bool raise=NeedsGhostRaise(preview.Handle,window,owner);
        if(samePos&&!raise)return;
        if(raise)Native.SetWindowPos(preview.Handle,-1,position.X,position.Y,0,0,samePos?0x0213u:0x0211u);
        else Native.SetWindowPos(preview.Handle,0,position.X,position.Y,0,0,0x0215u);
    }
    static void RaiseGhost(nint hwnd)=>Native.SetWindowPos(hwnd,-1,0,0,0,0,0x213);
    internal static void SurfaceRepositioned(GlassWindow surface)
    {
        if(Active?.ghost is not {IsVisible:true} preview||surface is not (DockWindow or DrawerWindow))return;
        // Positioning either glass surface or its compositor backdrop/shadow can raise its HWND
        // in the topmost band. Repair ordering across all MacDock surfaces immediately.
        if(NeedsGhostRaise(preview.Handle,surface,Active.owner))
            RaiseGhost(preview.Handle);
    }
    void ResetGap()
    {
        if(destination<0)return;
        destination=-1;
        foreach(var node in nodes)
        {
            if(node.View==source)continue;
            RetargetShift(node.Shift,TranslateTransform.XProperty,0);
            RetargetShift(node.Shift,TranslateTransform.YProperty,0);
        }
    }
    void RetargetShift(TranslateTransform shift,DependencyProperty property,double value)
    {
        if((double)shift.GetAnimationBaseValue(property)!=value)Anim.Retarget(shift,property,value,reducedMotion);
    }
    void Update(bool autoScroll=false)
    {
        if(ended||!started||ghost==null)return;if(!hotkey&&(Native.GetAsyncKeyState(27)&0x8000)!=0){End(false);return;}
        Native.GetCursorPos(out var raw);var p=window.Surface!.PointFromScreen(new Point(raw.X,raw.Y));var scale=VisualTreeHelper.GetDpi(window);
        MoveGhost(ghost,raw,VisualTreeHelper.GetDpi(ghost));
        if(owner!=null&&external!=null&&(Native.IsControlDown()||Native.IsShiftDown())){End(false);external();return;}
        bool corridor=false;
        if(owner!=null){
            var dock=owner.Dock;var wake=owner.WakeBounds;
            if(!dock.IsVisible&&(wake.Contains(raw)||Native.GetWindowRect(dock.Handle,out var hidden)&&hidden.Contains(raw))){owner.Expand();owner.SettleDockMotion();}
            corridor=Native.GetWindowRect(dock.Handle,out var dr)&&raw.X>=dr.Left-28&&raw.X<=dr.Right+28&&raw.Y>=dr.Top-32&&raw.Y<=dr.Bottom+24;
            overDock=dock.PreviewPin((Entry)source.Tag,raw);
            if(ghostDock!=overDock){ghostDock=overDock;double target=overDock?owner.Layout.Icon/owner.Layout.DrawerIcon:1;Anim.Retarget(ghostScale,ScaleTransform.ScaleXProperty,target,reducedMotion);Anim.Retarget(ghostScale,ScaleTransform.ScaleYProperty,target,reducedMotion);}
            if(overDock||corridor){(window as DrawerWindow)?.SetDropTarget(null);ResetGap();return;}
        }
        if(external!=null&&Native.GetWindowRect(window.Handle,out var bounds)){int margin=(int)(28*scale.DpiScaleX);if(raw.X<bounds.Left-margin||raw.X>bounds.Right+margin||raw.Y<bounds.Top-margin||raw.Y>bounds.Bottom+margin){(window as DrawerWindow)?.SetDropTarget(null);End(false);external();return;}}

        if(owner!=null && window is DrawerWindow drawer)
        {
            var dropTarget=drawer.HitDropTarget(p,draggedIds);
            if(dropTarget!=null)
            {
                drawer.SetDropTarget(dropTarget);
                ResetGap();
                return;
            }
            drawer.SetDropTarget(null);
            if(IsMultiDrag){ResetGap();return;}
        }
        
        if(autoScroll&&scroll!=null){var sp=scroll.PointToScreen(new Point());double y=(raw.Y-sp.Y)/scale.DpiScaleY;if(y<28&&y>=-10)scroll.ScrollToVerticalOffset(scroll.VerticalOffset-16);else if(y>scroll.ActualHeight-28&&y<scroll.ActualHeight+10)scroll.ScrollToVerticalOffset(scroll.VerticalOffset+16);}
        if(sortAllowed?.Invoke()==false){destination=-1;return;}
        double dy=(scroll?.VerticalOffset??0)-initialScroll;
        var row=nodes.OrderBy(n=>Math.Abs(n.Bounds.Top+n.Bounds.Height/2-dy-p.Y)).First().Bounds;
        int gap=nodes.FindIndex(n=>Math.Abs(n.Bounds.Top-row.Top)<row.Height*.4&&p.X<n.Bounds.Left+n.Bounds.Width/2);
        if(p.Y<nodes[0].Bounds.Top-dy)gap=0;
        else if(p.Y>nodes[^1].Bounds.Bottom-dy)gap=nodes.Count;
        else if(gap<0)gap=nodes.FindLastIndex(n=>Math.Abs(n.Bounds.Top-row.Top)<row.Height*.4)+1;
        int from=nodes.FindIndex(n=>n.View==source);int index=Math.Clamp(gap-(gap>from?1:0),0,nodes.Count-1);if(index==destination)return;destination=index;
        var order=Enumerable.Range(0,nodes.Count).Where(i=>i!=from).ToList();order.Insert(index,from);
        for(int slot=0;slot<order.Count;slot++){int i=order[slot];if(i==from)continue;var n=nodes[i];RetargetShift(n.Shift,TranslateTransform.XProperty,nodes[slot].Bounds.X-n.Bounds.X);RetargetShift(n.Shift,TranslateTransform.YProperty,nodes[slot].Bounds.Y-n.Bounds.Y);}
    }
    internal void End(bool accept)
    {
        if(ended)return;ended=true;Active=null;timer.Stop();if(hotkey)Native.UnregisterHotKey(window.Handle,0x4D17);if(hook!=null)HwndSource.FromHwnd(window.Handle)?.RemoveHook(hook);
        foreach(var n in nodes){n.View.RenderTransform=n.Original;n.View.Opacity=n.Opacity;}
        Native.GetCursorPos(out var release);int pinIndex=owner?.Dock.PinDestination??-1;bool pin=overDock&&owner?.Dock.Contains(release)==true;
        var previous=owner?.Dock.PreviewingPin==true?owner.Dock.PinArtworkPositions():null;
        owner?.Dock.ClearPinPreview();
        // The last preview tick may still describe the preceding pointer position.
        var dropTarget=accept&&started&&owner!=null&&!pin&&!overDock&&window is DrawerWindow drawer&&drawer.Contains(release)
            ?drawer.HitDropTarget(drawer.Surface!.PointFromScreen(new Point(release.X,release.Y)),draggedIds):null;
        (window as DrawerWindow)?.SetDropTarget(null);
        string? inserted=null;
        try
        {
            if(source.IsMouseCaptured)source.ReleaseMouseCapture();
            if(started)
            {
                if(accept && dropTarget!=null && owner!=null)
                {
                    owner.BeginInternalDropProtection();
                    try
                    {
                        state(false);
                        ghost?.Close();
                        owner.DropEntriesToTarget(dropTarget,draggedEntries.ToArray(),Native.IsControlDown());
                    }
                    finally { owner.EndInternalDropProtection(true); }
                }
                else
                {
                    state(false);
                    if(accept&&pin)
                    {
                        if(pinIndex>=0)
                        {
                            var entry=(Entry)source.Tag;
                            owner!.InsertPin(entry,pinIndex);
                            if(entry.Pinned)inserted=entry.Id;
                        }
                    }
                    else if(accept&&window.Contains(release)&&destination>=0&&!IsMultiDrag)
                        commit(nodes.Select(n=>((Entry)n.View.Tag).Id).ToArray(),((Entry)source.Tag).Id,destination);
                }
            }
            else if(accept&&!moved&&!started&&!ExceededScreenDragThreshold(release))click();
            if(previous!=null)owner!.Dock.LandPin(previous,inserted,release);
        }
        finally{ghost?.Close();}
        LastFocusTrace+=";preview-closed="+Keyboard.FocusedElement?.GetType().Name;
        // Sorting may remove the hit visual and a preview has its own focus scope.
        LastFocusTrace+=";committed="+Keyboard.FocusedElement?.GetType().Name;
        // Reconcile WPF input after commit, without activating any native window.
        if(started&&focusBefore is DependencyObject target&&Window.GetWindow(target) is GlassWindow active&&Native.GetForegroundWindow()==active.Handle){Keyboard.Focus(focusBefore);Mouse.Synchronize();}
    }
}
