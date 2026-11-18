using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
namespace MacDock;
internal static class GameWakeChecks
{
    internal static int Fixture(string commands)
    {
        var app=new Application();var screen=System.Windows.Forms.Screen.PrimaryScreen!;var b=Native.MonitorArea(screen,false);var work=Native.MonitorArea(screen,true);
        var w=new Window{Title="MacDock borderless game surrogate (not a real game)",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Background=new SolidColorBrush(Color.FromRgb(18,33,59)),Content=new TextBlock{Text="MacDock 前台/后台游戏窗口兼容性测试\n此窗口不是实际游戏",Foreground=Brushes.White,FontSize=28,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        string last="";var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
        void Command(){string cmd=File.Exists(commands)?File.ReadAllText(commands).Trim():"";if(cmd==last)return;last=cmd;var h=new WindowInteropHelper(w).Handle;if(cmd=="exit"){w.Close();return;}if(cmd=="min"){w.WindowState=WindowState.Minimized;return;}if(cmd=="work"||cmd=="full"){w.WindowState=WindowState.Normal;var r=cmd=="full"?b:work;Native.SetWindowPos(h,0,r.Left,r.Top,r.Width,r.Height,0x40);Native.SetForegroundWindow(h);}}
        w.PreviewMouseLeftButtonDown+=(_,_)=>File.AppendAllText(commands+".clicks","down\n");
        w.Loaded+=(_,_)=>{Command();File.WriteAllText(commands+".ready",new WindowInteropHelper(w).Handle.ToInt64().ToString());timer.Start();};timer.Tick+=(_,_)=>Command();w.Closed+=(_,_)=>timer.Stop();return app.Run(w);
    }
    internal static async Task Run(Controller c)
    {
        string dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(dir);var results=new List<object>();Process? game=null;var path=Path.Combine(dir,"fixture-command.txt");var desktop=new Window{Title="MacDock desktop interaction surrogate",Left=50,Top=100,Width=350,Height=220,Background=Brushes.LightGray};Native.GetCursorPos(out var prior);
        void Check(string name,bool pass,object? detail=null){results.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(dir,"game-progress.json"),JsonSerializer.Serialize(results));if(!pass)throw new Exception(name);}
        async Task Command(string command){File.WriteAllText(path,command);await Task.Delay(350);}
        async Task Wake(){var a=c.WorkArea;Native.MovePointer(a.Left+a.Width/2,a.Top+200);await Task.Delay(600);Native.MovePointer(a.Left+a.Width/2,a.Bottom-(int)(12*c.Scale*c.Layout.Unit));await Task.Delay(600);}
        try{
            desktop.Show();c.CloseDrawer(collapseDock:true);await Task.Delay(400);File.WriteAllText(path,"work");game=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){ArgumentList={"--game-fixture",path},UseShellExecute=false});
            for(int i=0;i<100&&!File.Exists(path+".ready");i++)await Task.Delay(50);Check("separate-process fixture has real native foreground",File.Exists(path+".ready"));nint gameH=(nint)long.Parse(File.ReadAllText(path+".ready"));
            Native.SetForegroundWindow(gameH);await Task.Delay(300);Check("windowed game surrogate leaves taskbar usable",Native.TaskbarInteractive(c.Screen)&&!Native.Fullscreen(c.Screen),new{foreground=Native.Class(Native.GetForegroundWindow()),taskbar=Native.TaskbarInteractive(c.Screen)});await Wake();Check("real pointer wakes over windowed foreground",c.Dock.IsVisible);
            c.ToggleDrawer();await Task.Delay(550);Native.SetForegroundWindow(gameH);Native.MovePointer(c.WorkArea.Left+90,c.WorkArea.Top+260);Native.TestButton(true);Native.TestButton(false);await Task.Delay(500);Check("first click after foreground switch reaches other application during dismissal",File.Exists(path+".clicks")&&File.ReadAllLines(path+".clicks").Length>=1);Check("foreground dismissal releases modal regions and both surfaces",c.Drawer?.IsVisible!=true&&!c.Dock.IsVisible);
            Native.MovePointer(80,200);c.Collapse();await Task.Delay(400);
            await Command("full");Check("full-monitor foreground is suppressed when taskbar covered",Native.Fullscreen(c.Screen),new{foreground=Native.GetForegroundWindow().ToInt64(),game=gameH.ToInt64(),taskbar=Native.TaskbarInteractive(c.Screen)});await Wake();Check("active full-monitor surrogate never wakes or steals focus",!c.Dock.IsVisible&&Native.GetForegroundWindow()==gameH);
            // Win+T is the normal shell command to enter the taskbar. No taskbar style,
            // registry, game hooks or global Z-order modification is used by this test.
            Native.TestKey(0x5B,true);Native.TestKey(0x54,true);Native.TestKey(0x54,false);Native.TestKey(0x5B,false);await Task.Delay(500);Check("taskbar can be entered with full-monitor content still behind",Native.TaskbarInteractive(c.Screen)&&!Native.Fullscreen(c.Screen));await Wake();Check("exposed-taskbar desktop interaction wakes despite game behind",c.Dock.IsVisible);Native.MovePointer(70,180);c.Collapse();await Task.Delay(400);
            for(int i=0;i<4;i++){
                Native.SetForegroundWindow(gameH);await Task.Delay(250);Check("returning to foreground full screen restores suppression "+i,Native.Fullscreen(c.Screen));await Wake();Check("foreground game remains protected "+i,!c.Dock.IsVisible);
                Native.SetForegroundWindow(new WindowInteropHelper(desktop).Handle);await Task.Delay(350);Check("background full-screen window does not latch suppression "+i,!Native.Fullscreen(c.Screen));await Wake();Check("real pointer wakes after switch to ordinary window "+i,c.Dock.IsVisible);Native.MovePointer(70,180);c.Collapse();await Task.Delay(400);
            }
            for(int i=0;i<2;i++){
                var desktopH=new WindowInteropHelper(desktop).Handle;Native.SetForegroundWindow(desktopH);await Task.Delay(180);Native.SetForegroundWindow(gameH);await Task.Delay(220);
                Native.TestKey(0x12,true);Native.TestKey(9,true);Native.TestKey(9,false);Native.TestKey(0x12,false);await Task.Delay(450);
                Check("actual Alt-Tab leaves fullscreen foreground "+i,Native.GetForegroundWindow()==desktopH&&!Native.Fullscreen(c.Screen));await Wake();Check("actual Alt-Tab permits real pointer wake "+i,c.Dock.IsVisible);Native.MovePointer(70,180);c.Collapse();await Task.Delay(400);
                Native.TestKey(0x12,true);Native.TestKey(9,true);Native.TestKey(9,false);Native.TestKey(0x12,false);await Task.Delay(450);Check("actual Alt-Tab back restores game protection "+i,Native.GetForegroundWindow()==gameH&&Native.Fullscreen(c.Screen));
            }
            await Command("min");Native.SetForegroundWindow(new WindowInteropHelper(desktop).Handle);await Task.Delay(300);await Wake();Check("minimized game process cannot suppress wake",c.Dock.IsVisible&&!Native.Fullscreen(c.Screen));
            Check("no frame clocks remain after policy changes",!c.DockMotionRunning&&Anim.ActiveTimers==0);
        }catch(Exception e){results.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{File.WriteAllText(path,"exit");if(game!=null&&!game.HasExited){await Task.Delay(200);if(!game.HasExited)game.Kill();}game?.Dispose();desktop.Close();Native.MovePointer(prior.X,prior.Y);File.WriteAllText(Path.Combine(dir,"game-wake.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
}
