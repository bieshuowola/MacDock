using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace MacDock;

internal static class StabilityChecks
{
    static IEnumerable<FrameworkElement> Elements(DependencyObject root)
    { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var n=VisualTreeHelper.GetChild(root,i);if(n is FrameworkElement f)yield return f;foreach(var d in Elements(n))yield return d;} }
    internal static async Task Run(Controller c,bool onlyStore=false)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;var results=new List<object>();
        void Check(string name,bool pass,object? detail=null){results.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(output,"stability.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));if(!pass)throw new Exception(name);}
        try{
            var folder=Path.Combine(output,"store-safety");Directory.CreateDirectory(folder);var s=new Store(folder);
            var pin=new Entry{Path=Path.Combine(Desktop.UserFolder,"missing-test-only.lnk"),Name="unavailable pin",Source="desktop",Pinned=true};s.Data.Entries.Add(pin);s.Save();s.Sync(Array.Empty<DesktopItem>());s.Save();
            Check("missing desktop snapshot cannot erase an explicit pin",new Store(folder).PinnedEntries().Single().Id==pin.Id);
            File.Delete(s.FilePath);var missing=new Store(folder);Check("missing primary reads backup before initialization",missing.PinnedEntries().Single().Id==pin.Id);missing.Save();
            var valid=File.ReadAllBytes(s.FilePath);File.WriteAllText(s.FilePath,"{truncated");var recovered=new Store(folder);recovered.Save();Check("corrupt primary recovery retains a readable backup",new Store(folder).Data.Entries.Count==1&&JsonSerializer.Deserialize<Config>(File.ReadAllText(s.FilePath+".bak"))!.Entries.Count==1);
            File.WriteAllText(s.FilePath,"{broken primary");File.WriteAllText(s.FilePath+".bak","{broken backup");var journal=new Store(folder);Check("invalid primary and backup recover the arrangement journal without overwriting originals",journal.PinnedEntries().Single().Id==pin.Id&&File.ReadAllText(s.FilePath)=="{broken primary");
            Check("immutable arrangements survive backup rotation",Directory.GetFiles(Path.Combine(folder,"arrangements")).Length>0);
            if(onlyStore)return;
            foreach(var e in c.Store.Data.Entries)e.Pinned=false;foreach(var e in c.Store.Data.Entries.Where(e=>e.InDrawer).Take(5))e.Pinned=true;c.Changed();c.Expand();await Task.Delay(500);
            var blocked=c.Store.FilePath+".tmp";Directory.CreateDirectory(blocked);var arrangement=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();
            try{bool saved=c.PersistArrangement(()=>c.Store.Data.Entries.ForEach(e=>e.Pinned=false));Check("failed write cannot display an unsaved arrangement",!saved&&arrangement.SequenceEqual(c.Store.PinnedEntries().Select(e=>e.Id)));}finally{Directory.Delete(blocked);}
            var pins=c.Store.PinnedEntries().ToArray();
            int cycles=int.TryParse(Environment.GetEnvironmentVariable("MACDOCK_MENU_CYCLES"),out var requested)?Math.Clamp(requested,10,30):30;
            for(int i=0;i<cycles;i++){
                if(i%5==0){c.ToggleDrawer();await Task.Delay(450);c.CloseDrawer(keepDock:true);await Task.Delay(350);}
                c.Expand();await Task.Delay(150);var e=pins[i%pins.Length];var tile=Elements(c.Dock).Single(f=>f.Tag is Entry item&&item.Id==e.Id);var p=c.Dock.TileCenter(e.Id);
                Native.MovePointer(p.X,p.Y);await Task.Delay(110);Native.TestRightButton(true);await Task.Delay(30);Native.TestRightButton(false);await Task.Delay(220);
                Check("real Dock item rightclick stays open "+i,tile.ContextMenu?.IsOpen==true&&c.MenuOpen&&c.Dock.IsVisible);
                Native.MovePointer(p.X-40,p.Y-50);await Task.Delay(230);Check("menu survives pointer movement "+i,tile.ContextMenu!.IsOpen);
                Native.TestKey(27,true);await Task.Delay(30);Native.TestKey(27,false);await Task.Delay(180);Check("menu Esc releases interaction guard "+i,!tile.ContextMenu!.IsOpen&&!c.MenuOpen);
                Native.MovePointer(p.X,p.Y);await Task.Delay(270);Check("wave resumes after menu "+i,c.Dock.MaxScale>1.08,c.Dock.MagnifyState);
            }
            var order=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();var reload=new Store(Path.GetDirectoryName(c.Store.FilePath));Check("pins and order persist on reload",order.SequenceEqual(reload.PinnedEntries().Select(e=>e.Id)));
            using var b=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,b.Size);b.Save(Path.Combine(output,"stability-final.png"));
        }catch(Exception ex){results.Add(new{name="error",pass=false,detail=(object)ex.ToString()});}
        finally{File.WriteAllText(Path.Combine(output,"stability.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
}
