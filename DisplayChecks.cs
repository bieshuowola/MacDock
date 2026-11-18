using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MacDock;

internal static class DisplayChecks
{
    internal static async Task Run(Controller c)
    {
        var folder=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var results=new List<object>();
        void Check(string name,bool pass,object? detail=null)=>results.Add(new{name,pass,detail});
        try
        {
            var original=c.WorkArea;
            var sense=c.WakeBounds;
            int x=(sense.Left+sense.Right)/2;
            Native.MovePointer(x,sense.Top-30);
            await Task.Delay(150);
            Native.MovePointer(x,original.Bottom-1);
            await Task.Delay(600);
            Native.GetCursorPos(out var actual);
            Check("actual lower work-area pixel wakes the Dock",c.Dock.IsVisible&&sense.Bottom==original.Bottom,
                new{actual.X,actual.Y,expected=original.Bottom-1,c.Diagnostics});
            Native.MovePointer(x,sense.Top-150);
            await Task.Delay(550);
            Native.MovePointer(x,original.Bottom);
            await Task.Delay(550);
            var taskbar=Native.Class(Native.GetAncestor(Native.WindowFromPoint(new Native.Point(x,original.Bottom)),2));
            Check("native taskbar remains outside the wake rectangle",!c.Dock.IsVisible&&!sense.Contains(new Native.Point(x,original.Bottom)),taskbar);
            c.ToggleDrawer();
            await Task.Delay(650);
            c.TestLayoutArea(System.Drawing.Rectangle.Empty);c.Position();
            Check("zero work area preserves last layout and disables overlays",c.WorkArea==original&&c.WakeBounds.Bottom==0&&
                !c.Dock.IsVisible&&c.Drawer?.IsVisible==false&&Native.IsWindowVisible(c.IndicatorHandle)==false);
            c.Position();
            var smaller=new System.Drawing.Rectangle(original.X+120,original.Y+60,1280,720);
            c.TestLayoutArea(smaller);c.Position();
            c.ToggleDrawer();
            await Task.Delay(650);
            var body=c.Drawer!.BodyBounds;
            Check("new work area reflows live windows",c.Drawer.IsVisible&&body.Left>=smaller.Left&&body.Right<=smaller.Right&&
                c.WakeBounds.Bottom==smaller.Bottom&&c.Dock.Width>0);
            c.CloseDrawer(collapseDock:true);
            await Task.Delay(550);
            c.TestLayoutArea(original);c.Position();
            c.ToggleDrawer();
            await Task.Delay(650);
            Check("return to original work area restores drawer and wake boundary",c.Drawer.IsVisible&&c.WakeBounds.Bottom==original.Bottom);

            var entry=c.Store.PinnedEntries().FirstOrDefault(e=>e.Path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)&&
                PackagedIcons.ShortcutIdentity(e.Path)!=null);
            if(entry!=null)
            {
                string id=PackagedIcons.ShortcutIdentity(entry.Path)!;
                void Capture(string file,BitmapSource bitmap)
                {
                    using var stream=File.Create(Path.Combine(folder,file));
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));encoder.Save(stream);
                }
                Theme.Apply("light");
                var light=PackagedIcons.Load(entry.Path,64) as BitmapSource;
                var shell=Native.AuditShellIcon(entry.Path,64) as BitmapSource;
                if(shell!=null)Capture("icon-shortcut-shell.png",shell);
                if(light!=null)Capture("icon-package-light.png",light);
                Check("light icon resolved by exact package identity",light!=null,id);
                Theme.Apply("dark");
                var dark=PackagedIcons.Load(entry.Path,64) as BitmapSource;
                if(dark!=null)Capture("icon-package-dark.png",dark);
                Check("dark variant is available",dark!=null);
                string alias=Path.Combine(folder,"packaged-link-fixture.lnk");
                File.Copy(entry.Path,alias);
                string first=IconResources.Stamp(alias);
                File.Move(alias,alias+".original");
                File.Copy(alias+".original",alias);
                File.SetCreationTimeUtc(alias,DateTime.UtcNow.AddMinutes(-1));
                Check("recreated shortcut rediscovers same package",first!=IconResources.Stamp(alias)&&
                    PackagedIcons.ShortcutIdentity(alias)==id&&PackagedIcons.Load(alias,64)!=null);
                Theme.Apply(c.Store.Data.Appearance);
            }
            else Check("packaged pinned fixture present",false);
        }
        catch(Exception e){Check("exception",false,e.ToString());}
        finally
        {
            File.WriteAllText(Path.Combine(folder,"display-checks.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();
        }
    }
}
