using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace MacDock;
internal static class MenuFocusChecks
{
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(output);var results=new List<object>();var previous=Native.GetForegroundWindow();
        try{
            c.Busy=true;c.Store.Data.Entries.Clear();c.Store.Data.DockCapacity=4;for(int n=0;n<5;n++)c.Store.Data.Entries.Add(new Entry{Path=Environment.ProcessPath!,Name="Menu fixture "+n,Source="manual",Pinned=true});c.Changed();c.Expand();await Task.Delay(450);
            Native.GetWindowRect(c.Dock.Handle,out var r);var p=new Native.Point(r.Left+4,(r.Top+r.Bottom)/2);Native.MovePointer(p.X,p.Y);await Task.Delay(50);if(Native.WindowFromPoint(p)!=c.Dock.Handle)throw new Exception("Menu point does not hit visible Dock");Native.TestRightButton(true);await Task.Delay(40);Native.TestRightButton(false);await Task.Delay(500);
            var menu=c.Dock.ContextMenu!;var parent=menu.Items.OfType<MenuItem>().Single(i=>i.Header.ToString()=="更多固定项目");bool focused=parent.Focus();Native.TestKey(0x27,true);await Task.Delay(70);Native.TestKey(0x27,false);await Task.Delay(250);
            results.Add(new{name="real management menu keyboard focus",pass=menu.IsOpen&&parent.IsSubmenuOpen,detail=new{focused,parent.IsSubmenuOpen,menu.IsKeyboardFocusWithin,focus=Keyboard.FocusedElement?.GetType().Name,foreground=(long)Native.GetForegroundWindow(),foregroundClass=Native.Class(Native.GetForegroundWindow()),hit=(long)Native.WindowFromPoint(p),dock=(long)c.Dock.Handle}});
            using(var b=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height)){using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,b.Size);b.Save(Path.Combine(output,"menu-focus.png"));}
            Native.TestKey(27,true);await Task.Delay(70);Native.TestKey(27,false);await Task.Delay(150);Native.TestKey(27,true);await Task.Delay(70);Native.TestKey(27,false);await Task.Delay(200);results.Add(new{name="menu Esc restores previous window focus",pass=!menu.IsOpen&&Native.GetForegroundWindow()==previous,detail=new{before=(long)previous,after=(long)Native.GetForegroundWindow()}});
        }catch(Exception e){results.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{c.Dock.ContextMenu!.IsOpen=false;await Task.Delay(150);File.WriteAllText(Path.Combine(output,"menu-focus.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();}
    }
}
