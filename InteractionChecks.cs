using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MacDock;

internal static class InteractionChecks
{
    internal static Action<string,object?>? Record;

    [DllImport("user32.dll")] static extern nint GetCapture();

    internal static void Observe(ContextMenu menu)
    {
        if (Record == null) return;
        var source = PresentationSource.FromVisual(menu) as HwndSource;
        void Log(string name, object? detail = null) => Record?.Invoke(name, new {
            fg = (long)Native.GetForegroundWindow(), capture = (long)GetCapture(),
            popup = (long)(source?.Handle ?? 0), open = menu.IsOpen, detail
        });
        Log("wpf-open");
        HwndSourceHook hook = (nint h,int msg,nint w,nint l,ref bool handled) => {
            if (msg is 0x6 or 0x7 or 0x8 or 0x1f or 0x215 or 0x18)
                Log("popup-message", new { message=msg, w=(long)w, l=(long)l });
            return 0;
        };
        MouseEventHandler lostCapture = (_,e) => Log("wpf-lost-capture",e.OriginalSource?.GetType().Name);
        KeyboardFocusChangedEventHandler lostFocus = (_,e) => Log("wpf-lost-focus",e.NewFocus?.GetType().Name);
        RoutedEventHandler? closed = null;
        closed = (_,_) => {
            Log("wpf-closed",Environment.StackTrace);
            menu.Closed -= closed;
            menu.RemoveHandler(Mouse.LostMouseCaptureEvent,lostCapture);
            menu.RemoveHandler(Keyboard.LostKeyboardFocusEvent,lostFocus);
            source?.RemoveHook(hook);
        };
        source?.AddHook(hook);
        menu.AddHandler(Mouse.LostMouseCaptureEvent,lostCapture,true);
        menu.AddHandler(Keyboard.LostKeyboardFocusEvent,lostFocus,true);
        menu.Closed += closed;
    }

    internal static async Task Run(Controller c, bool motionOnly)
    {
        var output = Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var clock = Stopwatch.StartNew();
        var events = new List<object>();
        var result = new List<object>();
        var sentinel = new Window {
            Title = "MacDock interaction measurement", WindowStyle=WindowStyle.None,
            Left=0,Top=0,Width=c.WorkArea.Width/c.Scale,Height=c.WorkArea.Height/c.Scale,
            Background=new SolidColorBrush(Color.FromRgb(30,58,120)),ShowInTaskbar=false
        };
        int attempt = -1;
        string phase = "";
        Record = (name,detail) => events.Add(new { t=clock.Elapsed.TotalMilliseconds, attempt,phase,name,detail });
        void Log(string name,object? detail=null) => Record?.Invoke(name,detail);
        void Save() {
            File.WriteAllText(Path.Combine(output,"interaction-events.json"),JsonSerializer.Serialize(events));
            File.WriteAllText(Path.Combine(output,"interaction-results.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        }
        async Task Escape() {
            Native.TestKey(27,true);
            await Task.Delay(16);
            Native.TestKey(27,false);
            await Task.Delay(210);
        }
        try {
            sentinel.Show();
            Native.MovePointer(100,120);
            await Task.Delay(180);
            if (!motionOnly) {
                phase="dock";
                var pins = c.Store.PinnedEntries().ToArray();
                int cycles=int.TryParse(Environment.GetEnvironmentVariable("MACDOCK_MENU_CYCLES"),out var requested)?Math.Clamp(requested,48,128):48;
                for (int i=0;i<cycles;i++) {
                    attempt=i;
                    if(i%8==0) {
                        Native.MovePointer(100,120);
                        await Task.Delay(650);
                        var wake=c.WakeBounds;
                        Native.MovePointer((wake.Left+wake.Right)/2,(wake.Top+wake.Bottom)/2);
                        await Task.Delay(145+(i%3)*40);
                    } else c.Expand();
                    if(i%8==3) {
                        c.ToggleDrawer();
                        await Task.Delay(390);
                    }
                    var p = c.Dock.TileCenter(pins[i%pins.Length].Id);
                    Native.MovePointer(p.X,p.Y);
                    await Task.Delay(i%4==0?12:68);
                    Log("right-down",new { drawer=c.Drawer?.IsVisible, dock=c.Dock.IsVisible, hwnd=(long)Native.WindowFromPoint(p), fg=(long)Native.GetForegroundWindow() });
                    Native.TestRightButton(true);
                    await Task.Delay(i%3==0?12:35);
                    Log("right-up");
                    Native.TestRightButton(false);
                    if(i%4==1){await Task.Delay(20);Native.MovePointer(p.X-46,p.Y-88);}
                    await Task.Delay(175);
                    bool opened=c.MenuOpen;
                    Log("sample-menu",new { open=opened, dock=c.Dock.IsVisible, fg=(long)Native.GetForegroundWindow() });
                    if(i%6==4 && opened) {
                        Log("expected-outside-close");
                        Native.MovePointer(105,125);
                        Native.TestButton(true);await Task.Delay(12);Native.TestButton(false);
                        await Task.Delay(210);
                    } else { Log("expected-escape");await Escape(); }
                    result.Add(new { phase, attempt, open=opened, closed=!c.MenuOpen });
                    if(c.Drawer?.IsVisible==true){c.CloseDrawer(keepDock:true);await Task.Delay(340);}
                }
                Save();
                phase="shell";
                c.ToggleDrawer();
                await Task.Delay(650);
                var entries = c.Store.Data.Entries.Where(e=>e.InDrawer).ToArray();
                if(!entries.Any(e=>e.Path.EndsWith(".txt",StringComparison.OrdinalIgnoreCase))){
                    if(!string.Equals(Path.GetFullPath(c.Store.FilePath),Path.GetFullPath(Path.Combine(output,"profile","config.json")),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Menu fixture requires isolated profile");
                    var fixture=Path.Combine(output,"shell-menu-probe.txt");File.WriteAllText(fixture,"MacDock isolated Shell menu fixture");c.Store.Add(fixture,false);c.Changed();await Task.Delay(200);entries=c.Store.Data.Entries.Where(e=>e.InDrawer).ToArray();
                }
                var selected = new[]{entries.First(e=>e.IsVirtual),entries.First(e=>e.Path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)),entries.First(e=>!e.IsVirtual&&Directory.Exists(e.Path)),entries.First(e=>e.Path.EndsWith(".txt",StringComparison.OrdinalIgnoreCase))};
                for (int i=0;i<selected.Length*3;i++) {
                    attempt=i;
                    var entry=selected[i/3];
                    c.Drawer!.EnsureVisible(entry.Id);await Task.Delay(80);
                    var point=c.Drawer!.TileCenter(entry.Id);
                    Native.MovePointer(point.X,point.Y);
                    await Task.Delay(60);
                    long release=0,shown=0;
                    int waitCursor=0;
                    var worker = Task.Run(() => {
                        Native.TestRightButton(true);Thread.Sleep(25);
                        release=Stopwatch.GetTimestamp();Native.TestRightButton(false);
                        var watch=Stopwatch.StartNew();
                        while(watch.ElapsedMilliseconds<2300) {
                            if(FindNativeMenu(Environment.ProcessId)!=0){shown=Stopwatch.GetTimestamp();break;}
                            var cursor = new CursorInfo{Size=Marshal.SizeOf<CursorInfo>()};
                            if(GetCursorInfo(ref cursor)&&(cursor.Handle==LoadCursor(0,32514)||cursor.Handle==LoadCursor(0,32650)))waitCursor++;
                            Thread.Sleep(1);
                        }
                        Thread.Sleep(100);
                        Native.TestKey(27,true);Thread.Sleep(20);Native.TestKey(27,false);
                    });
                    Log("shell-request",entry.Name);
                    await worker;
                    await Task.Delay(170);
                    result.Add(new { phase, attempt, entry.Name, entry.Path, latency=shown==0?-1:Stopwatch.GetElapsedTime(release,shown).TotalMilliseconds, waitCursor, closed=!c.MenuOpen });
                    if(c.Drawer?.IsVisible!=true){c.ToggleDrawer();await Task.Delay(650);}
                }
                Save();
            } else {
                phase="motion";
                bool physical=Environment.GetEnvironmentVariable("MACDOCK_MOTION_REAL")=="1";
                async Task Toggle()
                {
                    var point=c.Dock.GatewayCenter;
                    Native.MovePointer(point.X,point.Y);
                    await Task.Delay(16);
                    Log("gateway-down",new{point.X,point.Y,hit=(long)Native.WindowFromPoint(point)});
                    Native.TestButton(true);
                    await Task.Delay(16);
                    Native.TestButton(false);
                    Log("gateway-up");
                }
                c.Busy=true;
                c.Expand();c.SettleDockMotion();await Task.Delay(250);
                c.ToggleDrawer();await Task.Delay(650);c.CloseDrawer(keepDock:true);await Task.Delay(480);
                if(int.TryParse(Environment.GetEnvironmentVariable("MACDOCK_MOTION_IDLE_SECONDS"),out int idleSeconds)&&idleSeconds>0)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(idleSeconds,180)));
                File.WriteAllText(Path.Combine(output,"motion-ready.txt"),"warm fixture complete");
                Log("display",new { screen=c.Screen.DeviceName, bounds=c.Screen.Bounds.ToString(), work=c.WorkArea.ToString(),dpi=c.Scale });Save();
                bool active=false;
                TimeSpan? lastRender=null;
                EventHandler probe=(_,e)=>{
                    if(!active || e is not RenderingEventArgs r || r.RenderingTime==lastRender)return;
                    lastRender=r.RenderingTime;
                    Log("wpf-render",r.RenderingTime.TotalMilliseconds);
                };
                c.DrawerAnimationFrame=(p,_,_,_)=>Log("pose",p);
                using var process = Process.GetCurrentProcess();
                var cpuStart = process.TotalProcessorTime;
                var elapsedStart = clock.Elapsed;
                using var outputProbe=Environment.GetEnvironmentVariable("MACDOCK_MOTION_GDI")=="1"
                    ? new MotionOutputProbe(output,clock,c.WorkArea):null;
                CompositionTarget.Rendering+=probe;File.AppendAllText(Path.Combine(output,"motion-ready.txt")," + subscribed");
                try {
                    for(int i=0;i<(physical?12:10);i++){
                        attempt=i;
                        if(physical&&i%4==0)
                        {
                            sentinel.Background=i switch
                            {
                                0=>Brushes.White,
                                4=>new SolidColorBrush(Color.FromRgb(18,24,34)),
                                _=>new LinearGradientBrush(Colors.SteelBlue,Colors.Salmon,24)
                            };
                            Log("background",i);
                            await Task.Delay(160);
                        }
                        active=true;Log("open");
                        if(physical)await Toggle();else c.ToggleDrawer();
                        File.AppendAllText(Path.Combine(output,"motion-ready.txt")," open"+i);
                        await Task.Delay(physical&&i%4==1?90:420);
                        Log("close");if(physical)await Toggle();else c.CloseDrawer(keepDock:true);
                        File.AppendAllText(Path.Combine(output,"motion-ready.txt")," close"+i);
                        if(i%3==2)
                        {
                            await Task.Delay(physical?44:77);Log("reverse");if(physical)await Toggle();else c.ToggleDrawer();
                            await Task.Delay(410);Log("close-again");if(physical)await Toggle();else c.CloseDrawer(keepDock:true);
                        }
                        await Task.Delay(400);
                        Log("end");active=false;
                        result.Add(new {phase,attempt,hidden=!c.Drawer!.IsVisible,timers=Anim.ActiveTimers});Save();
                    }
                }finally{CompositionTarget.Rendering-=probe;c.DrawerAnimationFrame=null;}
                process.Refresh();Log("resources",new {cpuMs=(process.TotalProcessorTime-cpuStart).TotalMilliseconds,elapsedMs=(clock.Elapsed-elapsedStart).TotalMilliseconds,privateMiB=process.PrivateMemorySize64/1048576.0});
            }
        }catch(Exception ex){result.Add(new{error=ex.ToString()});}
        finally{try{Save();}catch(Exception e){File.WriteAllText(Path.Combine(output,"trace-error.txt"),e.ToString());}Record=null;sentinel.Close();Application.Current.Shutdown();}
    }

    [StructLayout(LayoutKind.Sequential)] struct CursorInfo { internal int Size,Flags; internal nint Handle; internal Native.Point Position; }
    [DllImport("user32.dll")] static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] static extern nint LoadCursor(nint module,int id);
    static nint FindNativeMenu(int pid) {
        nint found=0;
        Native.EnumWindows((h,_)=>{
            if(!Native.IsWindowVisible(h))return true;
            Native.GetWindowThreadProcessId(h,out var process);
            if(process!=pid||Native.Class(h)!="#32768")return true;
            found=h;
            return false;
        },0);
        return found;
    }
}
