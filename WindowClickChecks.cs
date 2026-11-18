using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace MacDock;

internal static class WindowClickChecks
{
    internal static async Task Run(Controller controller)
    {
        string output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        string fixture=Environment.GetEnvironmentVariable("MACDOCK_WINDOW_FIXTURE")!;
        var results=new List<object>();
        var existing=Native.Windows(fixture).Select(w=>w.Handle).ToHashSet();
        void Check(string name,bool pass)
        {
            results.Add(new{name,pass});
            if(!pass)throw new InvalidOperationException(name);
        }
        async Task<bool> Wait(Func<bool> condition)
        {
            for(int i=0;i<75;i++){if(condition())return true;await Task.Delay(40);}
            return false;
        }
        async Task Click(Entry entry)
        {
            controller.Expand();
            controller.SettleDockMotion();
            var point=controller.Dock.TileCenter(entry.Id);
            Native.MovePointer(point.X,point.Y);
            await Task.Delay(70);
            Native.TestButton(true);
            await Task.Delay(25);
            Native.TestButton(false);
        }
        var other=new Window{Title="MacDock window test foreground",Width=320,Height=160,Left=60,Top=100,ShowInTaskbar=false};
        try
        {
            if(!string.Equals(controller.Store.FilePath,Path.Combine(output,"profile","config.json"),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Window test requires an isolated profile.");
            foreach(var entry in controller.Store.Data.Entries)entry.Pinned=false;
            controller.Store.Add(fixture,true);
            var pin=controller.Store.Data.Entries.Single(e=>e.Path==fixture);
            controller.Changed();
            other.Show();
            await Click(pin);
            Check("first Dock click launches the program",await Wait(()=>Native.Windows(fixture).Count==1));
            var first=Native.Windows(fixture).Single().Handle;
            Check("new program owns the foreground",await Wait(()=>Native.GetForegroundWindow()==first));
            await Click(pin);
            Check("foreground click minimizes the actual window",await Wait(()=>Native.IsIconic(first)));
            await Click(pin);
            Check("next click restores the same window without relaunching",await Wait(()=>Native.GetForegroundWindow()==first&&!Native.IsIconic(first)&&Native.Windows(fixture).Count==1));
            other.Activate();
            await Click(pin);
            Check("click while another application is active brings back the existing window",await Wait(()=>Native.GetForegroundWindow()==first));
            Native.MinimizeWindow(first);
            Check("external minimize reached Windows",await Wait(()=>Native.IsIconic(first)));
            await Click(pin);
            Check("externally minimized window restores without a new process",await Wait(()=>Native.GetForegroundWindow()==first&&!Native.IsIconic(first)&&Native.Windows(fixture).Count==1));
            using var secondProcess=Process.Start(new ProcessStartInfo(fixture,"second"){UseShellExecute=true})!;
            Check("second window is present",await Wait(()=>Native.Windows(fixture).Count==2));
            var second=Native.Windows(fixture).Single(w=>w.Handle!=first).Handle;
            Check("second window is foreground before the next click",await Wait(()=>Native.GetForegroundWindow()==second));
            await Click(pin);
            Check("only the foreground matching window minimizes",await Wait(()=>Native.IsIconic(second)&&!Native.IsIconic(first)));
            other.Activate();
            await Click(pin);
            Check("restores the last window minimized from this Dock entry",await Wait(()=>Native.GetForegroundWindow()==second&&!Native.IsIconic(second)));
            foreach(var window in Native.Windows(fixture))Native.PostMessage(window.Handle,0x10,0,0);
            Check("fixture application exited",await Wait(()=>Native.Windows(fixture).Count==0));
            await Click(pin);
            Check("Dock click relaunches after exit",await Wait(()=>Native.Windows(fixture).Count==1));
        }
        catch(Exception error){results.Add(new{name="error",pass=false,detail=error.ToString()});}
        finally
        {
            foreach(var window in Native.Windows(fixture).Where(w=>!existing.Contains(w.Handle)))
                Native.PostMessage(window.Handle,0x10,0,0);
            other.Close();
            File.WriteAllText(Path.Combine(output,"window-click.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();
        }
    }
}
