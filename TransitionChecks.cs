using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacDock;

internal static class TransitionChecks
{
    static readonly List<object> results=new();
    static string Output=>Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
    static void Check(string name,bool pass,string detail=""){results.Add(new{name,pass,detail});if(!pass)throw new Exception(name+": "+detail);}
    static void Save(string file)=>File.WriteAllText(Path.Combine(Output,file),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    internal static async Task GpuHost(Controller c)
    {
        Directory.CreateDirectory(Output);var foreground=Native.GetForegroundWindow();
        try
        {
            c.Busy=true;c.ToggleDrawer();await Task.Delay(1200);c.Drawer!.FocusGrid();await Task.Delay(250);
            var frames=c.Dock.RenderFrames;var timers=Anim.ActiveTimers;
            File.WriteAllText(Path.Combine(Output,"gpu-ready.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,phase="open",tier=RenderCapability.Tier>>16,glass=c.Drawer.MaterialAvailable&&c.Dock.MaterialAvailable}));
            await Task.Delay(21000);Check("open idle has no wave frames or finite animation timers",c.Dock.RenderFrames==frames&&Anim.ActiveTimers==0&&timers==0,$"framesDelta={c.Dock.RenderFrames-frames}; timers={Anim.ActiveTimers}");
            c.CloseDrawer(collapseDock:true);await Task.Delay(600);c.Busy=false;frames=c.Dock.RenderFrames;
            File.WriteAllText(Path.Combine(Output,"gpu-ready.json"),JsonSerializer.Serialize(new{pid=Environment.ProcessId,phase="collapsed",tier=RenderCapability.Tier>>16}));
            await Task.Delay(21000);Check("collapsed idle has no wave frames or animation timers",c.Dock.RenderFrames==frames&&Anim.ActiveTimers==0&&!c.Dock.IsVisible,$"framesDelta={c.Dock.RenderFrames-frames}; timers={Anim.ActiveTimers}");
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally{Save("gpu-host.json");Native.SetForegroundWindow(foreground);System.Windows.Application.Current.Shutdown();}
    }
    internal static int Logic()
    {
        Directory.CreateDirectory(Output);
        try
        {
            int count=0;var gate=new ModalInputGate(()=>count++);var area=new ModalInputGate.Regions(new Native.Rect{Left=50,Top=50,Right=200,Bottom=200},new Native.Rect{Left=70,Top=220,Right=180,Bottom=260});var p=new Native.Point(10,10);
            gate.Current=area;Check("drawer click passes",!gate.Filter(new(100,100),0x201,now:0));Check("Dock click passes",!gate.Filter(new(100,240),0x201,now:0));
            Check("outside down is consumed synchronously",gate.Filter(p,0x201,now:10)&&count==1&&gate.Current==null);
            Check("matching up remains consumed after modal closes",gate.Filter(p,0x202,now:20));
            Check("second double-click down is consumed",gate.Filter(p,0x201,now:90));Check("second double-click up is consumed",gate.Filter(p,0x202,now:100));
            Check("later clicks pass without sticky ownership",!gate.Filter(p,0x201,now:110)&&!gate.Filter(p,0x202,now:120));
            gate.Current=area;Check("right dismissal owns both phases",gate.Filter(p,0x204,now:700)&&gate.Filter(p,0x205,now:710));
            Check("ordinary click elsewhere is not mistaken for double click",!gate.Filter(new(30,30),0x204,now:720));
            gate.Current=area with{Suspended=true};Check("file dialog or Shell menu suspends modality",!gate.Filter(p,0x201,now:1300));
            gate.Current=area;Check("X button dismissal preserves the pair",gate.Filter(p,0x20B,0x10000,2000)&&gate.Filter(p,0x20C,0x10000,2010));
            var assocArea=area with{AssociatedWindows=new nint[]{12345}};
            Check("associated context window handle is recognized",ModalInputGate.IsAssociatedWindow(12345,assocArea)&&!ModalInputGate.IsAssociatedWindow(99999,assocArea)&&!ModalInputGate.IsAssociatedWindow(0,assocArea));
            var spring=Enumerable.Range(0,1001).Select(n=>Anim.DrawerSpring(n/1000.0)).ToArray();Check("spring starts and ends exactly at stable geometry",spring[0]==0&&spring[^1]==1);Check("spring overshoot is subtle and bounded",spring.Max()>1&&spring.Max()<1.06&&spring.Min()>=0,$"peak={spring.Max():F5}");
            Check("suppress wake key active on clean desktop",Controller.ShouldSuppressDockByKey(true,false,false,false,false));
            Check("wake suppression respects dialogs, menus, drag and drawer",!Controller.ShouldSuppressDockByKey(false,false,false,false,false)&&!Controller.ShouldSuppressDockByKey(true,true,false,false,false)&&!Controller.ShouldSuppressDockByKey(true,false,true,false,false)&&!Controller.ShouldSuppressDockByKey(true,false,false,true,false)&&!Controller.ShouldSuppressDockByKey(true,false,false,false,true));
            Check("Oem3 key label displays tilde",Native.KeyName(0xC0)=="~");
            return 0;
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});return 1;}
        finally{Save("modal-logic.json");}
    }
    internal static async Task Run(Controller c)
    {
        Directory.CreateDirectory(Output);Native.GetCursorPos(out var original);var originalForeground=Native.GetForegroundWindow();PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
        var sentinel=new Window{Title="MacDock modal click sentinel",Topmost=true,Width=220,Height=180,Left=20,Top=100,Background=System.Windows.Media.Brushes.SlateGray};int downs=0,ups=0;
        sentinel.PreviewMouseDown+=(_,_)=>downs++;sentinel.PreviewMouseUp+=(_,_)=>ups++;
        try
        {
            sentinel.Show();var loading=c.Store.Data.Entries.Where(e=>e.InDrawer||e.Pinned).Select(e=>c.Icon(e.Path,56)).ToArray();var ready=DateTime.UtcNow.AddSeconds(15);while(loading.Any(i=>i.Loading)&&DateTime.UtcNow<ready)await Task.Delay(30);Check("initial icon worker is ready",loading.All(i=>!i.Loading));
            var frames=new List<object>();var watch=System.Diagnostics.Stopwatch.StartNew();var ys=new List<int>();
            c.DrawerAnimationFrame=(progress,top,scale,alpha)=>{ys.Add(top);frames.Add(new{ms=watch.Elapsed.TotalMilliseconds,progress,top,scale,alpha});};c.ToggleDrawer();
            for(int i=0;i<24;i++)
            {
                await Task.Delay(16);
            }
            c.DrawerAnimationFrame=null;File.WriteAllText(Path.Combine(Output,"opening-frames.json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));
            Check("drawer finishes at sharp stable scale",Math.Abs(c.Drawer!.EntranceScale-1)<.00001&&c.Drawer.ForegroundOpacity==1);
            Native.GetWindowRect(c.Dock.Handle,out var dock);var drawer=c.Drawer.BodyBounds;var gap=(dock.Top-drawer.Bottom)/c.Scale;
            Check("real glass surfaces separated by 22 DIP",Math.Abs(gap-22)<=1,$"gap={gap:F2} DIP");Check("curved reveal keeps native input window anchored",ys.Count>4&&ys.Distinct().Count()==1,"positions="+string.Join(",",ys.Distinct())+"; "+c.Trail);
            Check("finite animation ends",Anim.ActiveTimers==0,"timers="+Anim.ActiveTimers);
            Capture(c.Drawer,"settled-drawer");
            for(int i=0;i<24;i++){Native.MovePointer((dock.Left+dock.Right)/2,drawer.Bottom+(int)((dock.Top-drawer.Bottom)*(i+.5)/24));await Task.Delay(10);}
            Check("pointer crossing enlarged gap preserves both surfaces and resets wave",c.Drawer.IsVisible&&c.Dock.IsVisible&&c.Dock.MaxScale<1.002,$"drawer={c.Drawer.IsVisible}; dock={c.Dock.IsVisible}; maxScale={c.Dock.MaxScale}; {c.Trail}");
            // Escape works even during opening, with selection present, and retains the Dock.
            c.CloseDrawer();await Task.Delay(220);c.ToggleDrawer();var early=c.Drawer.BodyBounds;
            for(int n=0;n<50&&Native.WindowFromPoint(new(early.Right-90,early.Top+25))!=c.Drawer.Handle;n++){await Task.Delay(10);early=c.Drawer.BodyBounds;}
            await Task.Run(()=>{Native.MovePointer(early.Right-90,early.Top+25);Native.TestButton(true);Thread.Sleep(25);Native.TestButton(false);Thread.Sleep(20);Native.TestKey(0x1B,true);Thread.Sleep(70);Native.TestKey(0x1B,false);});await Task.Delay(400);
            Check("Escape during opening closes only drawer",!c.Drawer.IsVisible&&c.Dock.IsVisible&&c.Drawer.EntranceScale==1,$"drawer={c.Drawer.IsVisible}; dock={c.Dock.IsVisible}; foreground={Native.Class(Native.GetForegroundWindow())}; menu={c.MenuOpen}; busy={c.Busy}; "+c.Trail);
            c.ToggleDrawer();await Task.Delay(350);double alpha=-1,resumed=-1;bool reversed=false;
            c.DrawerAnimationFrame=(_,_,_,a)=>{if(!reversed&&a is >.1 and <.85){reversed=true;alpha=a;c.DrawerAnimationFrame=null;c.ToggleDrawer();resumed=c.Drawer.ForegroundOpacity;}};
            c.CloseDrawer(keepDock:true);await Task.Delay(350);c.DrawerAnimationFrame=null;
            Check("reverse opening retains current alpha without flash",reversed&&Math.Abs(alpha-resumed)<.02,$"before={alpha:F4}; after={resumed:F4}; "+c.Trail);
            await Task.Delay(350);Check("reverse animation settles exactly once",c.Drawer.IsVisible&&c.Drawer.EntranceScale==1&&Anim.ActiveTimers==0);
            // The Dock gateway closes the drawer even before its opening animation settles.
            c.CloseDrawer();await Task.Delay(300);c.ToggleDrawer();await Task.Delay(75);
            var closePoint=c.Dock.GatewayCenter;
            var clickPhase=c.DrawerProgress;
            Native.MovePointer(closePoint.X,closePoint.Y);Native.TestButton(true);await Task.Delay(35);Native.TestButton(false);await Task.Delay(420);
            Check("gateway click interrupts opening and retains Dock",clickPhase<1&&!c.Drawer.IsVisible&&c.Dock.IsVisible,$"clickPhase={clickPhase}; phase={c.DrawerProgress}; visible={c.Drawer.IsVisible}; dock={c.Dock.IsVisible}; {c.Trail}");
            c.ToggleDrawer();await Task.Delay(350);var gateway=c.Dock.GatewayBounds;var gp=c.Dock.PointToScreen(new Point(gateway.X+gateway.Width/2,gateway.Y+gateway.Height/2));Native.MovePointer((int)gp.X,(int)gp.Y);Native.TestButton(true);await Task.Delay(30);Native.TestButton(false);await Task.Delay(400);
            Check("gateway closes drawer and keeps Dock",!c.Drawer.IsVisible&&c.Dock.IsVisible);
            c.ToggleDrawer();await Task.Delay(350);var outside=new Native.Point(90,180);Native.MovePointer(outside.X,outside.Y);await Task.Delay(30);
            Check("outside click test point belongs to sentinel",Native.WindowFromPoint(outside)==new System.Windows.Interop.WindowInteropHelper(sentinel).Handle);
            // Drive the two clicks from a worker: UI await delays can resume after the
            // complete close animation, which would correctly make click two an ordinary click.
            await Task.Run(()=>{Native.TestButton(true);Thread.Sleep(30);Native.TestButton(false);Thread.Sleep(40);Native.TestButton(true);Thread.Sleep(25);Native.TestButton(false);});await Task.Delay(300);
            Check("dismissal and double click never reach background",downs==0&&ups==0,$"down={downs}; up={ups}");Check("outside dismissal collapses both windows",!c.Drawer.IsVisible&&!c.Dock.IsVisible);
            Native.TestButton(true);await Task.Delay(30);Native.TestButton(false);await Task.Delay(70);Check("next normal click is immediately usable",downs==1&&ups==1,$"down={downs}; up={ups}");
            c.ToggleDrawer();await Task.Delay(100);Native.ActivateWindow(new System.Windows.Interop.WindowInteropHelper(sentinel).Handle);await Task.Delay(430);Check("foreground window switch closes drawer automatically",!c.Drawer.IsVisible&&!c.Dock.IsVisible);
            for(int i=0;i<15;i++){c.ToggleDrawer();await Task.Delay(42+i%3*15);c.CloseDrawer(keepDock:true);await Task.Delay(36+i%4*9);}
            c.ToggleDrawer();await Task.Delay(380);Check("rapid open-close-reverse cycles recover",c.Drawer.IsVisible&&c.Drawer.EntranceScale==1&&c.Drawer.ForegroundOpacity==1&&Anim.ActiveTimers==0);
            c.CloseDrawer();await Task.Delay(220);c.Store.Data.ReducedMotion=true;c.ToggleDrawer();await Task.Delay(40);Check("reduced motion bypasses spring",c.Drawer.IsVisible&&c.Drawer.EntranceScale==1&&Anim.ActiveTimers==0,"timers="+Anim.ActiveTimers);
            Native.MovePointer(outside.X,outside.Y);await Task.Delay(25);
            int rmDownBefore=downs,rmUpBefore=ups;
            await Task.Run(()=>{Native.TestButton(true);Thread.Sleep(25);Native.TestButton(false);Thread.Sleep(45);Native.TestButton(true);Thread.Sleep(25);Native.TestButton(false);});await Task.Delay(120);
            Check("reduced motion double click never passes through",!c.Drawer.IsVisible&&!c.Dock.IsVisible&&downs==rmDownBefore&&ups==rmUpBefore,$"downs={downs-rmDownBefore}; ups={ups-rmUpBefore}");
            await Task.Delay(520);Native.TestButton(true);await Task.Delay(25);Native.TestButton(false);await Task.Delay(60);
            Check("reduced motion restores normal clicks after double-click window",downs==rmDownBefore+1&&ups==rmUpBefore+1);
            c.Expand();c.ToggleDrawer();await Task.Delay(40);c.CloseDrawer(keepDock:true);Check("reduced motion closes synchronously",!c.Drawer.IsVisible&&c.Dock.IsVisible);c.Store.Data.ReducedMotion=false;
            Check("no physical input interference",PointerHook.NonInjectedInputs==0);
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally{PointerHook.AuditInput=false;Native.TestButton(false);sentinel.Close();if(PointerHook.NonInjectedInputs==0){Native.MovePointer(original.X,original.Y);Native.SetForegroundWindow(originalForeground);}Save("transition-test.json");System.Windows.Application.Current.Shutdown();}
    }
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Find<T>(child))yield return nested;}}
    static void Capture(GlassWindow w,string name){Native.GetWindowRect(w.Handle,out var r);using var b=new System.Drawing.Bitmap(r.Right-r.Left,r.Bottom-r.Top);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(r.Left,r.Top,0,0,b.Size);b.Save(Path.Combine(Output,name+".png"));}
}
