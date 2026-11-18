using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;

namespace MacDock;

internal static class QualityChecks
{
    static readonly List<object> results=new();
    static bool collectAll;
    internal static string Output=>Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")??Path.Combine(AppContext.BaseDirectory,"test-results");
    static void Save(string file)=>File.WriteAllText(Path.Combine(Output,file),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    static void Check(string name,bool pass,string detail="") { if(PointerHook.AuditInput&&Interlocked.Read(ref PointerHook.NonInjectedInputs)>0)throw new Exception("GUI input interference detected; result is not accepted");results.Add(new{name,pass,detail});Save("quality-progress.json");if(!pass&&!collectAll)throw new Exception(name+": "+detail); }
    public static async Task Run(Controller c,bool baseline)
    {
        Directory.CreateDirectory(Output);Native.GetCursorPos(out var original);
        PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
        try
        {
            await Task.Delay(300);
            var a=c.WorkArea;int x=a.Left+a.Width/2,y=a.Bottom-(int)(12*c.Scale*c.Layout.Unit),away=a.Bottom-(int)(200*c.Scale);
            results.Add(new { name="environment",pass=true,screen=c.Screen.Bounds.ToString(),work=a.ToString(),dpi=c.Scale,config=c.Store.FilePath,runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription });
            Check("hook available",c.HookAvailable,c.Diagnostics);
            for(int cycle=0;cycle<(baseline?3:Environment.GetEnvironmentVariable("MACDOCK_QUALITY_QUICK")=="1"?5:30);cycle++)
            {
                Native.MovePointer(x,away);await Task.Delay(100);
                Native.MovePointer(x,y);await Task.Delay(620);
                Native.GetCursorPos(out var observed);Check($"wake {cycle+1}",c.Dock.IsVisible,c.Diagnostics+$" wanted={x},{y}; actual={observed.X},{observed.Y}");
                Native.MovePointer(x,away);await Task.Delay(620);
                Check($"collapse {cycle+1}",!c.Dock.IsVisible,c.Diagnostics);
                Check($"indicator {cycle+1}",Native.IsWindowVisible(c.IndicatorHandle));
            }
            Check("no watchdog or hook reinstall needed",PointerHook.Installations==1,"installations="+PointerHook.Installations);
            await Motion(c,x,y,away);
            await FileDrag(c);
            await DesktopSync(c);
            if(Native.HostMaterial)Check("independent external-process glass test",await ExternalGlassChecks.Run(c,false));else await Glass(c);
            Native.MovePointer(x,away);await Task.Delay(650);Check("collapsed before resume simulation",!c.Dock.IsVisible);
            c.TestResumeLifecycle();await Task.Delay(400);
            Native.MovePointer(x,away);await Task.Delay(100);Native.MovePointer(x,y);await Task.Delay(650);
            Check("resume handler restores usable input",c.Dock.IsVisible&&PointerHook.Installations==2,c.Diagnostics);
            Native.MovePointer(x,away);await Task.Delay(700);
            Check("no live animation clocks after interaction",!c.Dock.WaveRunning&&Anim.ActiveTimers==0,"timers="+Anim.ActiveTimers);
            var proc=Process.GetCurrentProcess();proc.Refresh();var cpu=proc.TotalProcessorTime;var memory=proc.PrivateMemorySize64;var handles=proc.HandleCount;
            double seconds=int.TryParse(Environment.GetEnvironmentVariable("MACDOCK_QUALITY_IDLE_SECONDS"),out var requested)?Math.Clamp(requested,10,300):Environment.GetEnvironmentVariable("MACDOCK_QUALITY_QUICK")=="1"?10:120;
            File.WriteAllText(Path.Combine(Output,"idle-ready"),Environment.ProcessId.ToString());
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            proc.Refresh();
            Check("idle soak CPU",(proc.TotalProcessorTime-cpu).TotalSeconds/seconds*100<2,$"seconds={seconds}; CPU-one-core={(proc.TotalProcessorTime-cpu).TotalSeconds/seconds*100:F3}%; privateMiB={proc.PrivateMemorySize64/1048576.0:F1}; deltaMiB={(proc.PrivateMemorySize64-memory)/1048576.0:F2}; handlesDelta={proc.HandleCount-handles}; animations={Anim.ActiveTimers}");
            Check("no unaccounted physical input during GUI run",PointerHook.NonInjectedInputs==0);
        }
        catch(Exception ex){results.Add(new{error=ex.ToString()});}
        finally { if(c.TestTrayMenu!=null)c.TestTrayMenu.IsOpen=false;if(c.Dock.ContextMenu!=null)c.Dock.ContextMenu.IsOpen=false;await Task.Delay(120);if(PointerHook.NonInjectedInputs==0)Native.MovePointer(original.X,original.Y);PointerHook.AuditInput=false;Save(baseline?"baseline.json":"quality.json");System.Windows.Application.Current.Shutdown(); }
    }

    static async Task Motion(Controller c,int x,int y,int away)
    {
        c.Store.Data.Entries.Clear();
        foreach(var name in new[]{"窗口切换验收","图标 B","图标 C","图标 D","图标 E"})c.Store.Data.Entries.Add(new Entry{Path=Environment.ProcessPath!,Name=name,Source="manual",Pinned=true});
        c.Changed();await HoldDock(c);
        var first=c.Store.Data.Entries[0];var point=c.Dock.TileCenter(first.Id);
        Native.MovePointer(point.X,point.Y);await Task.Delay(420);
        Native.GetCursorPos(out var actual);Check("wave magnifies a real screen hit",c.Dock.MaxScale>1.1,c.Dock.MagnifyState+$"; visible={c.Dock.IsVisible}; point={actual.X},{actual.Y}; hit={Native.WindowFromPoint(actual)}; dock={c.Dock.Handle}");
        Check("stationary wave settles without render loop",!c.Dock.WaveRunning,c.Dock.MagnifyState);
        Check("magnified artwork is not clipped",c.Dock.ArtworkBounds.All(r=>r.Top>=-.5 && r.Bottom<=c.Dock.Surface!.ActualHeight+.5),string.Join(";",c.Dock.ArtworkBounds));
        await StableAnchor(c);
        c.ToggleDrawer();await Task.Delay(320);Native.GetWindowRect(c.Drawer!.Handle,out var drawer);
        Native.MovePointer(drawer.Left+drawer.Right>0?(drawer.Left+drawer.Right)/2:x,drawer.Top+100);await Task.Delay(400);
        Check("moving to drawer resets magnification while Dock remains visible",c.Dock.IsVisible&&c.Dock.MaxScale<1.002&&!c.Dock.WaveRunning,c.Dock.MagnifyState);
        c.CloseDrawer();await Task.Delay(200);await HoldDock(c);
        Native.GetWindowRect(c.Dock.Handle,out var bounds);
        for(int n=0;n<80;n++){Native.MovePointer(bounds.Left+8+n%(Math.Max(1,bounds.Right-bounds.Left-16)),n%2==0?(bounds.Top+bounds.Bottom)/2:bounds.Top-20);await Task.Delay(12);}
        Native.MovePointer(x,away);await Task.Delay(650);
        Check("rapid crossings end at rest",c.Dock.MaxScale<1.002&&!c.Dock.WaveRunning,c.Dock.MagnifyState);
        await HoldDock(c);
        Check("resting optical icon centres",c.Dock.ArtworkBounds.All(r=>Math.Abs((r.Top+r.Bottom)/2-c.Dock.Surface!.ActualHeight/2)<3*c.Layout.Unit),string.Join(";",c.Dock.ArtworkBounds));
        var fixture=new Window{Title="MacDock single-click fixture",Width=320,Height=150};fixture.Show();await Task.Delay(100);
        Check("click fixture discoverable",Native.Windows(Environment.ProcessPath!).Count>0);
        await HoldDock(c);point=c.Dock.TileCenter(first.Id);Native.MovePointer(point.X,point.Y);await Task.Delay(300);point=c.Dock.TileCenter(first.Id);var launches=c.LaunchCount;
        Native.MovePointer(point.X,point.Y);await Task.Delay(100);var hit=c.Dock.HitTestEntry(point);Check("single click target really owns the screen point",Native.WindowFromPoint(point)==c.Dock.Handle);Native.TestButton(true);await Task.Delay(70);Native.TestButton(false);await Task.Delay(200);
        Check("single click launches exactly once",c.LaunchCount==launches+1,$"before={launches}; after={c.LaunchCount}; hit={hit}; expected={first.Id}; visible={c.Dock.IsVisible}");
        fixture.WindowState=WindowState.Maximized;await Task.Delay(100);c.Launch(first);await Task.Delay(100);Check("switching preserves maximized window",fixture.WindowState==WindowState.Maximized);fixture.Close();
        var capacity=c.Store.Data.DockCapacity;c.Store.Data.DockCapacity=4;c.Changed();await HoldDock(c);
        Native.GetWindowRect(c.Dock.Handle,out var realDock);var menuPoint=new Native.Point(realDock.Left+4,(realDock.Top+realDock.Bottom)/2);Native.MovePointer(menuPoint.X,menuPoint.Y);await Task.Delay(50);Check("management menu click belongs to Dock",Native.WindowFromPoint(menuPoint)==c.Dock.Handle);Native.TestRightButton(true);await Task.Delay(35);Native.TestRightButton(false);
        await Task.Delay(250);Check("themed management menu opens from actual rightclick",c.MenuOpen);
        var management=c.Dock.ContextMenu!;var parent=management.Items.OfType<System.Windows.Controls.MenuItem>().Single(i=>i.Header.ToString()=="更多固定项目");
        parent.Focus();Native.TestKey(0x27,true);Native.TestKey(0x27,false);await Task.Delay(220);
        var overflow=parent.Items.OfType<System.Windows.Controls.MenuItem>().First();
        Check("overflow submenu really renders and accepts keyboard expansion",parent.IsSubmenuOpen&&overflow.IsVisible&&PresentationSource.FromVisual(overflow)!=null,$"open={parent.IsSubmenuOpen}; childVisible={overflow.IsVisible}; focus={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name}; menuFocus={management.IsKeyboardFocusWithin}; parentFocus={parent.IsKeyboardFocusWithin}; foreground={Native.Class(Native.GetForegroundWindow())}; "+c.Trail);
        Native.TestKey(0x1B,true);Native.TestKey(0x1B,false);await Task.Delay(120);
        Check("Escape closes submenu first",!parent.IsSubmenuOpen&&c.MenuOpen);
        Native.TestKey(0x1B,true);Native.TestKey(0x1B,false);await Task.Delay(250);Check("management menu Escape closes",!c.MenuOpen);
        c.Store.Data.DockCapacity=capacity;c.Changed();
    }
    static async Task HoldDock(Controller c)
    {
        c.Position();var a=c.WorkArea;Native.MovePointer(a.Left+a.Width/2,a.Bottom-(int)(12*c.Scale*c.Layout.Unit));c.Expand();await Task.Delay(300);
    }
    static async Task StableAnchor(Controller c)
    {
        await HoldDock(c);Native.GetWindowRect(c.Dock.Handle,out var original);var gateway=c.Dock.GatewayBounds;
        var centers=c.Store.Data.Entries.Where(e=>e.Pinned).Select(e=>c.Dock.TileCenter(e.Id)).ToArray();
        var frames=new List<object>();bool stable=true,local=true,clear=true;
        foreach(var center in centers.Concat(centers.Reverse()))
        {
            Native.MovePointer(center.X,center.Y);await Task.Delay(280);Native.GetWindowRect(c.Dock.Handle,out var bounds);
            stable&=bounds.Left==original.Left&&bounds.Top==original.Top&&bounds.Right==original.Right&&bounds.Bottom==original.Bottom&&Math.Abs(c.Dock.GatewayBounds.X-gateway.X)<.1;
            local&=c.Dock.MaxDisplacement<4*c.Layout.Unit;
            var artwork=c.Dock.ArtworkBounds;for(int i=1;i<artwork.Count;i++)clear&=artwork[i-1].Right<=artwork[i].Left+1;
            frames.Add(new{point=$"{center.X},{center.Y}",window=$"{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}",wave=c.Dock.MagnifyState,gateway=c.Dock.GatewayBounds.ToString()});
        }
        File.WriteAllText(Path.Combine(Output,"anchor-sweep.json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));
        Check("horizontal sweep keeps window and gateway anchors fixed",stable);
        Check("horizontal sweep displaces only local neighbours",local);
        Check("magnified application artwork never overlaps",clear);
        var p=c.Dock.GatewayCenter;Native.MovePointer(p.X,p.Y);await Task.Delay(350);
        Check("drawer gateway has no application magnification",c.Dock.MaxScale<1.002&&!c.Dock.WaveRunning,c.Dock.MagnifyState);
        Native.TestButton(true);await Task.Delay(50);Native.TestButton(false);await Task.Delay(350);
        Check("real gateway click opens drawer at fixed position",c.Drawer?.IsVisible==true);
        Native.MovePointer(centers[0].X,centers[0].Y);await Task.Delay(300);
        Check("open drawer keeps real Dock application hover active",c.Dock.MaxScale>1.1);
        c.CloseDrawer();await Task.Delay(200);await HoldDock(c);
    }

    static async Task FileDrag(Controller c)
    {
        var root=Path.Combine(Output,"ole-fixture");Directory.CreateDirectory(root);var file=Path.Combine(root,"incoming.txt");File.WriteAllText(file,"quality OLE payload");var target=Path.Combine(root,"folder");Directory.CreateDirectory(target);
        var copy=Path.Combine(target,"incoming.txt");if(File.Exists(copy))File.Delete(copy);
        c.Store.Add(target,true);c.Changed();var entry=c.Store.Data.Entries.First(e=>e.Path==target);await HoldDock(c);var centre=c.Dock.TileCenter(entry.Id);Native.MovePointer(centre.X,c.WorkArea.Top+100);c.Collapse();await Task.Delay(400);
        var a=c.WorkArea;int x=a.Left+a.Width/2,y=a.Bottom-(int)(12*c.Scale*c.Layout.Unit);
        var source=new Window{Title="MacDock OLE source",Width=300,Height=120,Left=(x-150)/c.Scale,Top=(y-400)/c.Scale,Topmost=true,Content="文件拖放自动验收"};bool started=false,woke=false;
        var payload=ShellOps.DragData("drawer-fixture",new[]{file},false,"drawer");var allowed=System.Windows.DragDropEffects.Copy|System.Windows.DragDropEffects.Move;var result=System.Windows.DragDropEffects.None;
        var dockHandle=c.Dock.Handle;
        source.PreviewMouseMove+=(_,e)=>{if(started||e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed)return;started=true;result=System.Windows.DragDrop.DoDragDrop(source,payload,allowed);};
        async Task Drive()
        { var copyIntent=(payload.GetData("MacDock.Origin") as string)!="dock";
            await Task.Run(()=>{try{Native.MovePointer(x,y-340);Thread.Sleep(100);Native.TestButton(true);Native.MovePointer(x+20,y-330);Thread.Sleep(150);Native.TestKey(0x12,true);Native.MovePointer(x,y);Thread.Sleep(950);woke=Native.IsWindowVisible(dockHandle);Native.TestKey(0x12,false);if(copyIntent)Native.TestKey(0x11,true);Native.MovePointer(centre.X,centre.Y);Thread.Sleep(280);if(Native.WindowFromPoint(centre)!=dockHandle){Native.TestKey(0x1B,true);Native.TestKey(0x1B,false);throw new Exception("Drop point occluded; operation canceled");}Native.TestButton(false);Thread.Sleep(300);}finally{Native.TestButton(false);Native.TestKey(0x12,false);Native.TestKey(0x11,false);}});
        }
        try
        {
            source.Show();await Task.Delay(200);var hwnd=c.Dock.Handle;var launches=c.LaunchCount;
            await Drive();
            Check("real OLE source started",started);Check("Alt file drag wakes Dock",woke,c.Diagnostics);
            Check("OLE drop copies through native folder target",File.Exists(copy)&&File.ReadAllText(copy)=="quality OLE payload"&&File.Exists(file));
            Check("OLE release never launches an application",c.LaunchCount==launches);
            var from=c.Store.Data.Entries[0];var to=c.Store.Data.Entries[1];await HoldDock(c);centre=c.Dock.TileCenter(to.Id);
            payload=ShellOps.DragData(from.Id,new[]{from.Path},false,"dock",true);allowed=System.Windows.DragDropEffects.Copy|System.Windows.DragDropEffects.Link;started=false;
            await Drive();
            Check("reference-only OLE still reorders Dock without native move permission",started&&result==System.Windows.DragDropEffects.Link&&c.Store.PinnedEntries().ToList().IndexOf(from)>c.Store.PinnedEntries().ToList().IndexOf(to)&&File.Exists(from.Path));
            c.Store.Add(file,false);var add=c.Store.Data.Entries.Single(e=>e.Path==file);await HoldDock(c);Native.GetWindowRect(c.Dock.Handle,out var bounds);centre=new Native.Point(bounds.Left+6,(bounds.Top+bounds.Bottom)/2);
            payload=ShellOps.DragData(add.Id,new[]{file},false,"drawer",true);started=false;
            await Drive();
            Check("drawer OLE to Dock blank pins reference and never moves source",started&&add.Pinned&&File.ReadAllText(file)=="quality OLE payload");
            Check("internal reference drags never trigger an application launch",c.LaunchCount==launches);
        }
        finally{Native.TestButton(false);Native.TestKey(0x12,false);Native.TestKey(0x11,false);source.Close();}
    }
    internal static async Task DragOnly(Controller c)
    {
        Directory.CreateDirectory(Output);Native.GetCursorPos(out var original);var foreground=Native.GetForegroundWindow();
        try
        {
            PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
            // A separate test region avoids covering or dismissing the user's system search UI.
            c.TestLayoutArea(new System.Drawing.Rectangle(c.WorkArea.Left,c.WorkArea.Top,650,c.WorkArea.Height));
            c.Store.Data.Entries.Clear();foreach(var name in new[]{"reference A","reference B"})c.Store.Data.Entries.Add(new Entry{Path=Environment.ProcessPath!,Name=name,Source="manual",Pinned=true});c.Changed();
            await FileDrag(c);Check("no physical mouse interference",PointerHook.NonInjectedInputs==0);
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally{PointerHook.AuditInput=false;if(PointerHook.NonInjectedInputs==0)Native.MovePointer(original.X,original.Y);Native.SetForegroundWindow(foreground);Save("drag-test.json");System.Windows.Application.Current.Shutdown();}
    }

    static async Task Glass(Controller c)
    {
        var a=c.WorkArea;
        var backdrop=new Window{Title="Live backdrop quality fixture",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Topmost=true,Left=a.Left/c.Scale,Top=a.Top/c.Scale,Width=a.Width/c.Scale,Height=a.Height/c.Scale,Background=System.Windows.Media.Brushes.Black};
        c.Busy=true;
        var hiddenOverlays=new List<nint>();
        try
        {
            c.CloseDrawer();await Task.Delay(200);backdrop.Show();Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(backdrop).Handle,-1,a.Left,a.Top,a.Width,a.Height,0x10);await Task.Delay(150);c.Expand();c.ToggleDrawer();await Task.Delay(350);
            Native.SetWindowPos(c.Dock.Handle,-1,0,0,0,0,0x13);Native.SetWindowPos(c.Drawer!.Handle,-1,0,0,0,0,0x13);await Task.Delay(120);
            Native.GetWindowRect(c.Dock.Handle,out var dock);Native.GetWindowRect(c.Drawer!.Handle,out var drawer);
            var visual=c.Dock.Surface!;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(Math.Max(1,(int)(visual.ActualWidth*c.Scale)),Math.Max(1,(int)(visual.ActualHeight*c.Scale)),96*c.Scale,96*c.Scale,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(Output,"dock-content-alpha.png")))encoder.Save(stream);
            if(Environment.GetEnvironmentVariable("MACDOCK_TEST_HIDE_CANVAS")=="1")Native.EnumWindows((h,_)=>{if(Native.IsWindowVisible(h)&&(Native.Class(h).StartsWith("ShellHandwritingCanvas",StringComparison.Ordinal)||Native.Class(h)=="CEF-OSC-WIDGET")){hiddenOverlays.Add(h);Native.ShowWindow(h,0);}return true;},0);
            var overlaps=new List<object>();Native.EnumWindows((h,_)=>{if(h==c.Dock.Handle||h==c.Drawer.Handle||!Native.IsWindowVisible(h)||!Native.GetWindowRect(h,out var r))return true;if(r.Left<dock.Right&&r.Right>dock.Left&&r.Top<dock.Bottom&&r.Bottom>dock.Top){Native.GetWindowThreadProcessId(h,out var pid);string process="";try{using var p=Process.GetProcessById((int)pid);process=p.ProcessName;}catch{}overlaps.Add(new{hwnd=(long)h,cls=Native.Class(h),pid,process,rect=$"{r.Left},{r.Top},{r.Right},{r.Bottom}"});}return true;},0);File.WriteAllText(Path.Combine(Output,"material-overlaps.json"),JsonSerializer.Serialize(new{dock=$"{dock.Left},{dock.Top},{dock.Right},{dock.Bottom}",overlaps},new JsonSerializerOptions{WriteIndented=true}));
            var samples=new[]{new System.Drawing.Rectangle(dock.Left+12,dock.Top+4,Math.Max(10,dock.Right-dock.Left-24),5),new System.Drawing.Rectangle((drawer.Left+drawer.Right)/2-20,drawer.Top+9,40,7)};
            Check("dark backdrop fixture established",Sample(new System.Drawing.Rectangle(a.Left+20,a.Top+20,20,20)).Mean<5);
            var dark=samples.Select(Sample).ToArray();Capture(c.Dock,"dark-dock");Capture(c.Drawer,"dark-drawer");
            backdrop.Background=System.Windows.Media.Brushes.White;await Task.Delay(300);Check("white backdrop fixture established",Sample(new System.Drawing.Rectangle(a.Left+20,a.Top+20,20,20)).Mean>240);var light=samples.Select(Sample).ToArray();Capture(c.Dock,"light-dock");Capture(c.Drawer,"light-drawer");
            for(int i=0;i<2;i++){Check($"glass responds to light background {i}",light[i].Mean-dark[i].Mean>15,$"dark={dark[i].Mean:F1}; white={light[i].Mean:F1}");Check($"primary text contrast on white {i}",Contrast(light[i].Mean)>=4.5,$"contrast={Contrast(light[i].Mean):F2}; background={light[i].Mean:F1}");}
            var red=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);var blue=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);
            var pattern=new System.Windows.Controls.Grid();for(int k=0;k<180;k++){pattern.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());var cell=new System.Windows.Controls.Border{Background=k%2==0?red:System.Windows.Media.Brushes.Black};System.Windows.Controls.Grid.SetColumn(cell,k);pattern.Children.Add(cell);}backdrop.Content=pattern;await Task.Delay(300);var before=samples.Select(Sample).ToArray();
            foreach(var item in pattern.Children.OfType<System.Windows.Controls.Border>().Where(b=>ReferenceEquals(b.Background,red)))item.Background=blue;await Task.Delay(300);var after=samples.Select(Sample).ToArray();
            for(int i=0;i<2;i++){Check($"real-time coloured background {i}",Math.Abs(before[i].Red-after[i].Red)+Math.Abs(before[i].Blue-after[i].Blue)>15,$"red-phase={before[i]}; blue-phase={after[i]}");Check($"actual blur attenuation {i}",before[i].Deviation<25,"stripe deviation="+before[i].Deviation.ToString("F2"));}
            Capture(c.Dock,"dynamic-dock");Capture(c.Drawer,"dynamic-drawer");
        }
        finally{foreach(var h in hiddenOverlays)Native.ShowWindow(h,4);c.CloseDrawer();backdrop.Close();c.Busy=false;}
    }
    internal static async Task MaterialOnly(Controller c)
    {
        Directory.CreateDirectory(Output);
        collectAll=true;
        try{foreach(var mode in new[]{"light","dark"}){Theme.Apply(mode);var parent=Output;var sub=Path.Combine(parent,mode);Directory.CreateDirectory(sub);Environment.SetEnvironmentVariable("MACDOCK_TEST_RESULTS",sub);try{await Glass(c);}finally{Environment.SetEnvironmentVariable("MACDOCK_TEST_RESULTS",parent);}}}catch(Exception e){results.Add(new{error=e.ToString()});}
        finally{Save("material-comparison.json");System.Windows.Application.Current.Shutdown();}
    }
    static double Contrast(double grey) {double L(double v){v/=255;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}var ink=Theme.Ink.Color;double foreground=.2126*L(ink.R)+.7152*L(ink.G)+.0722*L(ink.B),background=L(grey);return (Math.Max(foreground,background)+.05)/(Math.Min(foreground,background)+.05);}
    static async Task DesktopSync(Controller c)
    {
        var folder=Path.Combine(Desktop.UserFolder,"MacDock-quality-"+Guid.NewGuid().ToString("N"));var renamed=folder+"-renamed";
        string? shortcut=null;
        try
        {
            c.Busy=true;Directory.CreateDirectory(folder);await Task.Delay(900);c.Busy=false;await Task.Delay(1200);
            Check("desktop watcher resumes changes deferred during a menu/operation",c.Store.Data.Entries.Any(e=>e.Path==folder));
            var entry=c.Store.Data.Entries.First(e=>e.Path==folder);entry.Pinned=true;var id=entry.Id;c.Changed();
            Directory.Move(folder,renamed);await Task.Delay(1200);
            Check("desktop rename keeps identity and pin",c.Store.Data.Entries.Any(e=>e.Path==renamed&&e.Id==id&&e.Pinned));
            var file=Path.Combine(renamed,"original.txt");File.WriteAllText(file,"shortcut target preserved");shortcut=Desktop.CreateShortcut(file);await Task.Delay(1000);
            Check("drawer addition is a real desktop shortcut",c.Store.Data.Entries.Any(e=>e.Path==shortcut&&e.InDrawer)&&File.ReadAllText(file)=="shortcut target preserved");
            c.AddFiles(new[]{file},false);var added=c.Store.Data.Entries.Single(e=>e.Path==file);
            Check("non-desktop user addition is a labeled alias",added.Source=="manual"&&added.Origin=="user-added"&&added.InDrawer);
            c.DeleteEntries(new[]{added},false);Check("Delete on manual alias preserves original file",File.Exists(file)&&File.ReadAllText(file)=="shortcut target preserved"&&!c.Store.Data.Entries.Contains(added));
            var legacy=new Entry{Path=file,Name="legacy safety fixture",Source="legacy",Origin="unconfirmed"};c.Store.Data.Entries.Add(legacy);
            var menu=new System.Windows.Controls.ContextMenu();EntryMenu.Fill(menu,c,new[]{legacy});
            var headers=menu.Items.OfType<System.Windows.Controls.MenuItem>().Select(i=>i.Header is System.Windows.Controls.Grid g?string.Join(" ",g.Children.OfType<System.Windows.Controls.TextBlock>().Select(t=>t.Text)):i.Header.ToString()).ToArray();
            Check("unconfirmed legacy aliases never expose real deletion",headers.All(t=>!t!.Contains("回收站")&&!t.Contains("永久删除"))&&headers.Any(t=>t!.Contains("保留原文件")));
            c.DeleteEntries(new[]{legacy},false);Check("Delete on legacy alias never deletes original",File.Exists(file)&&!c.Store.Data.Entries.Contains(legacy));
            File.Delete(shortcut);await Task.Delay(1000);Check("desktop delete reflected automatically",!c.Store.Data.Entries.Any(e=>e.Path==shortcut));shortcut=null;
        }
        finally
        {
            c.Busy=false;if(shortcut!=null&&File.Exists(shortcut))File.Delete(shortcut);
            foreach(var p in new[]{folder,renamed})if(Directory.Exists(p))Directory.Delete(p,true);
            c.SyncDesktop(true);
        }
    }
    static (double Mean,double Red,double Blue,double Deviation) Sample(System.Drawing.Rectangle r)
    {
        using var b=new System.Drawing.Bitmap(r.Width,r.Height);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(r.Location,System.Drawing.Point.Empty,b.Size);
        double mean=0,red=0,blue=0,sq=0;int n=r.Width*r.Height;for(int y=0;y<r.Height;y++)for(int x=0;x<r.Width;x++){var p=b.GetPixel(x,y);mean+=(p.R+p.G+p.B)/3.0;red+=p.R;blue+=p.B;sq+=p.R*p.R;}return(mean/n,red/n,blue/n,Math.Sqrt(Math.Max(0,sq/n-Math.Pow(red/n,2))));
    }
    static void Capture(GlassWindow w,string name)
    {
        Native.GetWindowRect(w.Handle,out var r);using var b=new System.Drawing.Bitmap(r.Right-r.Left,r.Bottom-r.Top);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(r.Left,r.Top,0,0,b.Size);b.Save(Path.Combine(Output,name+".png"));
    }
}
