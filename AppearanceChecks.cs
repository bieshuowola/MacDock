using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock;

internal static class AppearanceChecks
{
    static readonly List<object> results=new();
    static string Output=>Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
    static void Check(string name,bool pass,string detail=""){results.Add(new{name,pass,detail});if(!pass)throw new Exception(name+": "+detail);}
    static async Task Until(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(15);while(!ready()&&DateTime.UtcNow<end)await Task.Delay(40);if(!ready())throw new Exception("icon readiness timeout");}
    internal static async Task Run(Controller c)
    {
        Directory.CreateDirectory(Output);Native.GetCursorPos(out var original);var foreground=Native.GetForegroundWindow();
        var folder=Path.Combine(Output,"future-"+Guid.NewGuid().ToString("N"));var link=Path.Combine(Desktop.UserFolder,"MacDock-icon-test-"+Guid.NewGuid().ToString("N")+".lnk");
        PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
        try
        {
            Directory.CreateDirectory(folder);var resource=Path.Combine(folder,"future.ico");IconTests.WriteIcon(resource,new[]{64,256},new[]{Colors.Lime,Colors.Red});IconTests.Shortcut(link,Environment.ProcessPath!,resource+",0");
            await Until(()=>c.Store.Data.Entries.Any(e=>e.Path==link));
            var entry=c.Store.Data.Entries.Single(e=>e.Path==link);entry.Pinned=true;c.Changed();
            var loaded=c.Store.Data.Entries.Where(e=>e.InDrawer||e.Pinned).Select(e=>c.Icon(e.Path,56)).ToArray();await Until(()=>loaded.All(i=>!i.Loading));
            Check("every current desktop icon resolves asynchronously",loaded.All(i=>!i.Missing),"count="+loaded.Length);
            var icon=c.Icon(link,56);await Until(()=>!icon.Loading);Check("new desktop shortcut automatically receives real resource",!icon.Missing&&icon.Content.Width>0);
            c.ToggleDrawer();await Task.Delay(350);var scroll=c.Drawer!.Scroller;var bar=scroll.OverlayBar!;
            Check("scrollbar starts fully hidden with no hit interception",bar!=null&&bar.Opacity<.01&&!bar.IsHitTestVisible);
            Check("drawer genuinely requires scrolling",scroll.ScrollableHeight>50);
            var point=scroll.PointToScreen(new Point(scroll.ActualWidth/2,scroll.ActualHeight/2));Native.MovePointer((int)point.X,(int)point.Y);
            await Task.Delay(100);Check("wheel point belongs to drawer",Native.WindowFromPoint(new Native.Point((int)point.X,(int)point.Y))==c.Drawer.Handle);Native.TestWheel(-120);await Task.Delay(230);var offset=scroll.VerticalOffset;
            Check("real wheel smoothly scrolls and reveals capsule",offset>30&&bar!.Opacity>.5,"offset="+offset+"; opacity="+bar!.Opacity);
            Native.TestWheel(120);await Task.Delay(230);Check("rapid wheel reversal restores earlier content",scroll.VerticalOffset<offset);
            await Task.Delay(1400);Check("capsule fades completely after inactivity",bar!.Opacity<.01&&!bar.IsHitTestVisible);
            var edge=scroll.PointToScreen(new Point(scroll.ActualWidth-4,scroll.ActualHeight/2));Native.MovePointer((int)edge.X,(int)edge.Y);await Task.Delay(160);
            Check("approaching scroll edge makes drag target available",bar.Opacity>.5&&bar.IsHitTestVisible);
            bar.ApplyTemplate();var track=(Track)bar.Template.FindName("PART_Track",bar);var thumb=track.Thumb;var center=thumb.PointToScreen(new Point(thumb.ActualWidth/2,thumb.ActualHeight/2));
            Native.MovePointer((int)center.X,(int)center.Y);Native.TestButton(true);await Task.Delay(80);Native.MovePointer((int)center.X,(int)center.Y+65);await Task.Delay(250);Native.TestButton(false);await Task.Delay(150);
            Check("capsule accepts actual thumb dragging",scroll.VerticalOffset>40&&bar.Opacity>.6,"offset="+scroll.VerticalOffset);
            Native.MovePointer((int)point.X,(int)point.Y);await Task.Delay(1400);Check("drag completion also fades without a continuous loop",bar.Opacity<.01&&!bar.IsHitTestVisible);
            var viewport=scroll.ViewportWidth;foreach(var mode in new[]{"light","dark","system"}){c.SetAppearance(mode);await Task.Delay(180);Check("live theme "+mode,Theme.Dark==(mode=="dark"||mode=="system"&&Theme.SystemDark())&&new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.Appearance==mode);Check("theme preserves overlay grid width "+mode,Math.Abs(scroll.ViewportWidth-viewport)<.1);Capture(c.Drawer,mode+"-real-desktop-drawer");Capture(c.Dock,mode+"-compact-dock");}
            var old=icon.Image;IconTests.WriteIcon(resource,new[]{64,256},new[]{Colors.Lime,Colors.Blue});c.Icon(link,56);await Until(()=>!icon.Loading);Check("resource update replaces a live slot without rebuilding tile",!ReferenceEquals(old,icon.Image));
            var stable=icon.Image;for(int i=0;i<100;i++)c.Icon(link,56);await Task.Delay(120);Check("unchanged resource reuses its loaded bitmap",ReferenceEquals(stable,icon.Image)&&!icon.Loading);
            await c.ShowSettings(w=>w.Dispatcher.BeginInvoke(async()=>{try{var choice=Find<ComboBox>(w).Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="外观模式");choice.SelectedIndex=1;await Task.Delay(120);Check("settings theme control previews immediately",!Theme.Dark&&c.Store.Data.Appearance=="light");choice.SelectedIndex=2;await Task.Delay(120);Check("settings dark selection updates existing surfaces",Theme.Dark);choice.SelectedIndex=0;}finally{w.Close();}}));
            Check("no physical input interference",PointerHook.NonInjectedInputs==0);
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally
        {
            PointerHook.AuditInput=false;Native.TestButton(false);if(File.Exists(link))File.Delete(link);if(Directory.Exists(folder))Directory.Delete(folder,true);
            if(PointerHook.NonInjectedInputs==0){Native.MovePointer(original.X,original.Y);Native.SetForegroundWindow(foreground);}
            File.WriteAllText(Path.Combine(Output,"appearance-test.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();
        }
    }
    static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Find<T>(child))yield return nested;}}
    static void Capture(GlassWindow window,string name){Native.GetWindowRect(window.Handle,out var r);using var bitmap=new System.Drawing.Bitmap(r.Right-r.Left,r.Bottom-r.Top);using(var g=System.Drawing.Graphics.FromImage(bitmap))g.CopyFromScreen(r.Left,r.Top,0,0,bitmap.Size);bitmap.Save(Path.Combine(Output,name+".png"));}
}
