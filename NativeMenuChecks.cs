using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
namespace MacDock;
internal static class NativeMenuChecks
{
    [DllImport("user32.dll")]static extern bool GetMenuItemRect(nint window,nint menu,uint index,out Native.Rect rect);
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(output);var results=new List<object>();
        void Check(string name,bool pass,object? detail=null){results.Add(new{name,pass,detail});if(!pass)throw new Exception(name);}
        Native.GetCursorPos(out var original);PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
        try{
            c.ToggleDrawer();await Task.Delay(600);var d=c.Drawer!;var drawerHandle=d.Handle;var entry=c.Store.Data.Entries.First(e=>e.Source=="desktop"&&File.Exists(e.Path)&&e.Path.EndsWith(".txt",StringComparison.OrdinalIgnoreCase));var stamp=File.ReadAllBytes(entry.Path);entry.Pinned=false;c.Changed();
            for(int cycle=0;cycle<10;cycle++){
                d.EnsureVisible(entry.Id);
                await Task.Delay(120);
                var point=d.TileCenter(entry.Id);
                Native.MovePointer(point.X,point.Y);await Task.Delay(100);
                var hitWindow=Native.WindowFromPoint(point);
                var viewport=d.Scroller.PointToScreen(new Point());
                Check("rightclick hits actual drawer",hitWindow==d.Handle,new
                {
                    point=new{point.X,point.Y},hit=(long)hitWindow,expected=(long)d.Handle,hitClass=Native.Class(hitWindow),
                    viewport=new{viewport.X,viewport.Y,d.Scroller.ActualWidth,d.Scroller.ActualHeight},
                    d.ScrollOffset,d.Scroller.ScrollableHeight,d.IsVisible,c.DrawerClosing
                });
                bool displayed=false,above=false,hit=false,submenus=true;string[] labels=Array.Empty<string>();Thread? driver=null;
                ShellMenu.MenuDisplayed=()=>{displayed=true;labels=ShellMenu.LastLabels.ToArray();var menu=ShellMenu.ActivePopup;var rar=ShellMenu.LastLabels.FindIndex(t=>t.Contains("WinRAR",StringComparison.OrdinalIgnoreCase));int rarPosition=rar<0?-1:ShellMenu.LastPositions[rar];driver=new Thread(()=>{
                    Thread.Sleep(280);d.Dispatcher.BeginInvoke(()=>{c.Position();d.RefreshMaterial();});Thread.Sleep(150);
                    var order=new List<nint>();Native.EnumWindows((h,_)=>{if(Native.IsWindowVisible(h))order.Add(h);return true;},0);var menus=order.Where(h=>{Native.GetWindowThreadProcessId(h,out var pid);return pid==Environment.ProcessId&&Native.Class(h)=="#32768";}).ToArray();above=menus.Length>0&&menus.All(h=>order.IndexOf(h)<order.IndexOf(drawerHandle));
                    if(GetMenuItemRect(0,menu,0,out var first)){var p=new Native.Point(first.Left+20,(first.Top+first.Bottom)/2);hit=menus.Contains(Native.WindowFromPoint(p));}
                    if(cycle==2&&rarPosition>=0&&GetMenuItemRect(0,menu,(uint)rarPosition,out var item)){Native.MovePointer((item.Left+item.Right)/2,(item.Top+item.Bottom)/2);Thread.Sleep(650);int count=0;bool valid=true;var z=new List<nint>();Native.EnumWindows((h,_)=>{if(Native.IsWindowVisible(h))z.Add(h);return true;},0);foreach(var h in z){Native.GetWindowThreadProcessId(h,out var pid);if(pid==Environment.ProcessId&&Native.Class(h)=="#32768"){count++;valid&=z.IndexOf(h)<z.IndexOf(drawerHandle);}}submenus=count>=2&&valid;Native.TestKey(27,true);Thread.Sleep(60);Native.TestKey(27,false);Thread.Sleep(100);}
                    using(var image=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height)){using(var g=System.Drawing.Graphics.FromImage(image))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,image.Size);if(cycle<3)image.Save(Path.Combine(output,"menu-"+cycle+".png"));}
                    if(cycle<2&&GetMenuItemRect(0,menu,0,out var command)){Native.MovePointer(command.Left+25,(command.Top+command.Bottom)/2);Thread.Sleep(50);Native.TestButton(true);Thread.Sleep(40);Native.TestButton(false);}else{Native.TestKey(27,true);Thread.Sleep(65);Native.TestKey(27,false);}
                }){IsBackground=true};driver.Start();};
                Native.TestRightButton(true);await Task.Delay(40);Native.TestRightButton(false);for(int n=0;n<100&&(!displayed||c.MenuOpen);n++)await Task.Delay(50);driver?.Join(2500);ShellMenu.MenuDisplayed=null;
                Check("native menu above glass cycle "+cycle,displayed&&above&&hit,new{displayed,above,hit,labels});if(cycle==2)Check("third-party submenu above drawer",submenus&&labels.Any(t=>t.Contains("WinRAR",StringComparison.OrdinalIgnoreCase)));
                if(cycle<2){bool expected=cycle==0;Check("MacDock pin command and immediate state",entry.Pinned==expected&&labels[0].Contains(expected?"固定到 Dock":"取消固定"));var reload=new Store(Path.GetDirectoryName(c.Store.FilePath));Check("pin command persisted",reload.Data.Entries.Single(e=>e.Id==entry.Id).Pinned==expected);Check("pin command leaves original bytes intact",stamp.SequenceEqual(File.ReadAllBytes(entry.Path)));}
                Check("menu cancellation retains drawer",d.IsVisible&&!c.MenuOpen);
            }
            Check("no concurrent physical input",PointerHook.NonInjectedInputs==0);
        }catch(Exception e){results.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{ShellMenu.MenuDisplayed=null;PointerHook.AuditInput=false;Native.TestButton(false);if(PointerHook.NonInjectedInputs==0)Native.MovePointer(original.X,original.Y);File.WriteAllText(Path.Combine(output,"native-menus.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();}
    }
}
