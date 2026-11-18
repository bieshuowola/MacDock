using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace MacDock;
internal static class RenderInputChecks
{
    internal static async Task Run(Controller c,bool baseline)
    {
        var dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;var records=new List<object>();
        void Check(string name,bool pass,object? detail=null){records.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(dir,"render-input.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));}
        void Shot(string name){var r=c.Drawer!.BodyBounds;int pad=30;using var b=new System.Drawing.Bitmap(r.Right-r.Left+2*pad,(int)Math.Round(c.Drawer.PanelHeight*c.Scale)+2*pad);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(r.Left-pad,r.Top-pad,0,0,b.Size);b.Save(Path.Combine(dir,name+".png"));}
        object Input()=>new{focus=Keyboard.FocusedElement?.GetType().Name,over=Mouse.DirectlyOver?.GetType().Name,capture=Mouse.Captured?.GetType().Name,scrollFocus=c.Drawer!.Scroller.IsKeyboardFocused,drawerActive=c.Drawer.IsActive,foreground=(long)Native.GetForegroundWindow(),drawer=(long)c.Drawer.Handle,drag=c.InternalDragging,offset=c.Drawer.Scroller.VerticalOffset,wheel=c.Drawer.LastWheelInput,diagnostics=((SmoothScrollViewer)c.Drawer.Scroller).WheelDiagnostics};
        var bg=new Window{Title="MacDock render input fixture",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Left=c.WorkArea.Left/c.Scale,Top=c.WorkArea.Top/c.Scale,Width=c.WorkArea.Width/c.Scale,Height=c.WorkArea.Height/c.Scale,Background=new SolidColorBrush(Color.FromRgb(30,58,120))};
        try{
            if(!Path.GetFullPath(c.Store.FilePath).Equals(Path.GetFullPath(Path.Combine(dir,"profile","config.json")),StringComparison.OrdinalIgnoreCase))throw new Exception("Not isolated");
            c.Store.Data.Mode="自定义";c.Store.Data.ListView=false;foreach(var e in c.Store.Data.Entries)e.Pinned=false;foreach(var e in c.Store.Data.Entries.Where(e=>e.InDrawer).Take(4))e.Pinned=true;c.Changed();
            bg.Show();c.ToggleDrawer();await Task.Delay(700);c.Busy=true;Check("initial drawer owns focus",c.Drawer!.IsActive,Input());Shot("blue-all");
            var shadows=new List<nint>();Native.EnumWindows((h,_)=>{Native.GetWindowThreadProcessId(h,out var pid);var text=new StringBuilder(256);Native.GetWindowText(h,text,256);if(pid==Environment.ProcessId&&Native.IsWindowVisible(h)&&text.ToString()=="MacDock rounded glass shadow")shadows.Add(h);return true;},0);
            foreach(var h in shadows)Native.ShowWindow(h,0);await Task.Delay(120);Shot("blue-no-shadow");c.Drawer!.Opacity=0;await Task.Delay(120);Shot("blue-backdrop-only");c.Drawer.Opacity=1;foreach(var h in shadows)Native.ShowWindow(h,4);
            bg.Background=System.Windows.Media.Brushes.White;await Task.Delay(150);Shot("white-all");bg.Background=new SolidColorBrush(Color.FromRgb(30,58,120));c.Busy=false;
            var d=c.Drawer;var sc=d.Scroller;var center=sc.PointToScreen(new Point(sc.ActualWidth*.5,sc.ActualHeight*.35));Native.MovePointer((int)center.X,(int)center.Y);Native.TestWheel(-120);await Task.Delay(250);Check("wheel works before drag",sc.VerticalOffset>10,Input());sc.ScrollToTop();await Task.Delay(120);
            int routes=0;d.AddHandler(Mouse.PreviewMouseWheelEvent,new MouseWheelEventHandler((_,_)=>routes++),true);
            for(int round=0;round<12;round++){
                sc.ScrollToVerticalOffset((round%3)*160);await Task.Delay(100);var top=sc.PointToScreen(new Point());
                var viewport=sc.TransformToAncestor(d.Surface!).TransformBounds(new Rect(sc.RenderSize));
                var candidates=d.SortTiles(false).Where(v=>{var b=v.TransformToAncestor(d.Surface!).TransformBounds(new Rect(v.RenderSize));return b.Top+v.ActualHeight/2>viewport.Top+40&&b.Top+v.ActualHeight/2<viewport.Bottom-40;}).Select(v=>d.TileCenter(((Entry)v.Tag).Id)).ToArray();
                var a=candidates[round%3];var z=candidates[Math.Min(candidates.Length-1,10+round%4)];int launches=c.LaunchCount;int delta=sc.VerticalOffset>100?120:-120;
                await Task.Run(()=>{Native.MovePointer(a.X,a.Y);Thread.Sleep(60);Native.TestButton(true);Thread.Sleep(40);for(int i=1;i<=22;i++){Native.MovePointer(a.X+(z.X-a.X)*i/22,a.Y+(z.Y-a.Y)*i/22);Thread.Sleep(14);}Thread.Sleep(100);if(round%4==1){Native.TestKey(27,true);Thread.Sleep(30);Native.TestKey(27,false);}if(round%4==2)Native.MovePointer(c.WorkArea.Left+20,c.WorkArea.Top+20);Native.TestButton(false);});
                if(round%4==2)Native.MovePointer(z.X,z.Y);
                var before=Input();double offset=sc.VerticalOffset;int oldRoutes=routes;Native.TestWheel(delta);await Task.Delay(240);Check("wheel immediately after release without extra click "+round,Math.Abs(sc.VerticalOffset-offset)>10,new{before,after=Input(),wheelRoutes=routes-oldRoutes,focusTrace=PointerSort.LastFocusTrace,cancel=round%4==1,invalid=round%4==2});Check("drag never launches and clears capture "+round,c.LaunchCount==launches&&PointerSort.Active==null&&Mouse.Captured==null);
            }
            Check("configuration saves after real sorts",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.Entries.Select(e=>e.Id).SequenceEqual(c.Store.Data.Entries.Select(e=>e.Id)));
            sc.ScrollToTop();await Task.Delay(100);Native.TestKey(9,true);await Task.Delay(30);Native.TestKey(9,false);await Task.Delay(70);
            center=sc.PointToScreen(new Point(sc.ActualWidth*.5,sc.ActualHeight*.4));Native.MovePointer((int)center.X,(int)center.Y);var tabBefore=Input();Native.TestWheel(-120);await Task.Delay(240);
            Check("wheel follows hovered grid with focus outside scroller",sc.VerticalOffset>10,new{before=tabBefore,after=Input()});
        }catch(Exception e){records.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{PointerSort.Active?.End(false);Native.TestButton(false);bg.Close();File.WriteAllText(Path.Combine(dir,"render-input.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
}
