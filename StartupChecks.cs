using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
namespace MacDock;
internal static class StartupChecks
{
    internal static async Task Run(Controller c)
    {
        var dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var results=new List<object>();
        double Now()=>(DateTime.UtcNow-Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalMilliseconds;
        void Mark(string stage,object? detail=null)=>results.Add(new{stage,processMilliseconds=Now(),detail});
        try
        {
            Mark("controller-ready");c.Busy=true;c.Expand();
            var deadline=Stopwatch.StartNew();
            while(c.Store.PinnedEntries().Any(e=>c.Icon(e.Path,c.Layout.Icon).Missing)&&deadline.ElapsedMilliseconds<15000)await Task.Delay(10);
            var frame=new TaskCompletionSource();EventHandler? handler=null;handler=(_,_)=>{CompositionTarget.Rendering-=handler;frame.TrySetResult();};CompositionTarget.Rendering+=handler;await frame.Task;await Task.Delay(20);
            Mark("dock-complete",new{missing=c.Store.PinnedEntries().Count(e=>c.Icon(e.Path,c.Layout.Icon).Missing),hotkey=c.HotkeyAvailable});
            var started=Now();c.ToggleDrawer();
            while(c.DrawerProgress<.999&&Now()-started<4000)await Task.Delay(10);
            deadline.Restart();while(c.Store.Data.Entries.Where(e=>e.InDrawer).Any(e=>c.Icon(e.Path,c.Layout.DrawerIcon).Loading)&&deadline.ElapsedMilliseconds<15000)await Task.Delay(10);
            Mark("first-drawer-ready",new{fromRequest=Now()-started,missing=c.Store.Data.Entries.Count(e=>e.InDrawer&&c.Icon(e.Path,c.Layout.DrawerIcon).Missing)});
            using var bitmap=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);using(var g=System.Drawing.Graphics.FromImage(bitmap))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,bitmap.Size);bitmap.Save(Path.Combine(dir,"startup-drawer.png"));
        }
        catch(Exception e){results.Add(new{error=e.ToString()});}
        finally{File.WriteAllText(Path.Combine(dir,"startup-check.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
}
