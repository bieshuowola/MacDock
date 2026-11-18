using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace MacDock;
internal static class ResumeChecks
{
    static readonly List<object> results=new();
    static void Check(string name,bool pass,string detail=""){results.Add(new{name,pass,detail});if(!pass)throw new Exception(name+": "+detail);}
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(output);Native.GetCursorPos(out var original);var fg=Native.GetForegroundWindow();PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
        try
        {
            c.Store.Data.Entries.AddRange(Enumerable.Range(0,5).Select(n=>new Entry{Path=Environment.ProcessPath!,Name="Pinned fixture "+n,Source="manual",Pinned=true}));c.Changed();c.ToggleDrawer();await Task.Delay(550);
            Check("balanced default grid",c.Layout.Columns<=8&&c.Drawer!.Width/c.Drawer.Height<1.9,c.Drawer!.GridDiagnostics);
            Check("opening resets to first complete row",c.Drawer.Scroller.VerticalOffset<.1);
            var button=Walk<Button>(c.Drawer).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="切换列表和网格");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);Check("list view persisted",c.Store.Data.ListView&&new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.ListView);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);Check("grid returns with eight or fewer columns",!c.Store.Data.ListView&&c.Drawer.MeasuredColumns()==c.Layout.Columns);
            var entry=c.Store.Data.Entries.First(e=>e.Pinned);var p=c.Dock.TileCenter(entry.Id);Native.MovePointer(p.X,p.Y);await Task.Delay(400);Check("visible Dock magnifies with drawer open",c.Dock.MaxScale>1.1,c.Dock.MagnifyState);
            Native.GetWindowRect(c.Drawer.Handle,out var r);Native.MovePointer(r.Left+60,r.Top+20);await Task.Delay(400);Check("drawer pointer restores Dock wave",c.Dock.MaxScale<1.002);
            var tile=c.Drawer.TileCenter(c.Store.Data.Entries.First(e=>e.InDrawer).Id);Native.MovePointer(tile.X,tile.Y);Native.TestRightButton(true);await Task.Delay(35);Native.TestRightButton(false);await Task.Delay(700);
            Check("drawer item menu remains open",c.MenuOpen&&c.Drawer.IsVisible);
            Native.MovePointer(tile.X+3,tile.Y+3);await Task.Delay(250);Check("minor movement does not close menu",c.MenuOpen);
            Native.TestKey(27,true);Native.TestKey(27,false);await Task.Delay(250);Check("menu Escape leaves drawer",!c.MenuOpen&&c.Drawer.IsVisible);
            c.CloseDrawer();await Task.Delay(240);c.Expand();p=c.Dock.TileCenter(entry.Id);Native.MovePointer(p.X,p.Y);Native.TestRightButton(true);await Task.Delay(35);Native.TestRightButton(false);await Task.Delay(650);Check("Dock item menu remains open",c.MenuOpen&&c.Dock.IsVisible);Native.TestKey(27,true);Native.TestKey(27,false);await Task.Delay(200);Check("Dock menu exits normally",!c.MenuOpen);
            c.Collapse();Native.MovePointer(c.WorkArea.Left+20,c.WorkArea.Top+30);await Task.Delay(700);Check("collapsed before click wake",!c.Dock.IsVisible);Native.GetWindowRect(c.IndicatorHandle,out r);Native.MovePointer((r.Left+r.Right)/2,(r.Top+r.Bottom)/2);Native.TestButton(true);Native.TestButton(false);await Task.Delay(250);Check("indicator click wakes immediately",c.Dock.IsVisible);
            Check("no physical input interference",PointerHook.NonInjectedInputs==0);
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally{PointerHook.AuditInput=false;if(PointerHook.NonInjectedInputs==0){Native.MovePointer(original.X,original.Y);Native.SetForegroundWindow(fg);}File.WriteAllText(Path.Combine(output,"resume-test.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();}
    }
    static IEnumerable<T> Walk<T>(DependencyObject o) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(o);i++){var c=VisualTreeHelper.GetChild(o,i);if(c is T t)yield return t;foreach(var item in Walk<T>(c))yield return item;}}
}
