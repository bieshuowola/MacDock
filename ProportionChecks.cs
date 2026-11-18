using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacDock;

internal static class ProportionChecks
{
    internal static async Task Run(Controller c)
    {
        string folder=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var checks=new List<object>();
        void Check(string name,bool pass,object? detail=null)=>checks.Add(new{name,pass,detail});
        var area=c.WorkArea;
        var scene=new Window{Title="MacDock visual proportion fixture",ShowInTaskbar=false,WindowStyle=WindowStyle.None,
            ResizeMode=ResizeMode.NoResize,Left=area.Left/c.Scale,Top=area.Top/c.Scale,Width=area.Width/c.Scale,Height=area.Height/c.Scale,
            Background=new LinearGradientBrush(Color.FromRgb(66,44,100),Color.FromRgb(171,144,205),25)};
        static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
        {
            if(root is T result)yield return result;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                foreach(var child in Find<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
        void Shot(string name)
        {
            int width=Math.Min(area.Width,1410),height=Math.Min(area.Height,1150);
            using var image=new System.Drawing.Bitmap(width,height);
            using(var graphics=System.Drawing.Graphics.FromImage(image))graphics.CopyFromScreen(area.Left+(area.Width-width)/2,area.Bottom-height,0,0,image.Size);
            image.Save(Path.Combine(folder,name+".png"));
        }
        try
        {
            scene.Show();await Task.Delay(300);
            var wake=c.WakeBounds;
            Native.MovePointer((wake.Left+wake.Right)/2,(wake.Top+wake.Bottom)/2);
            await Task.Delay(530);
            var gateway=c.Dock.GatewayCenter;
            Native.MovePointer(gateway.X,gateway.Y);await Task.Delay(260);
            Check("normal Dock geometry",c.Dock.IsVisible&&Native.GetWindowRect(c.Dock.Handle,out _));
            Native.GetWindowRect(c.Dock.Handle,out var dock);
            var baseArt=c.Dock.ArtworkBounds.Select(x=>x.Width).ToArray();
            var pins=c.Store.PinnedEntries().Take(baseArt.Length).ToArray();
            Shot("dock-rest");
            int target=pins.Length/2;
            var hit=c.Dock.TileCenter(pins[target].Id);
            Native.MovePointer(hit.X,hit.Y);await Task.Delay(1190);
            var magnified=c.Dock.ArtworkBounds;
            Check("hover matches hit and label without overlap",c.Dock.HitTestEntry(hit)==pins[target].Id&&c.Dock.TipEntryId==pins[target].Id&&c.Dock.TipVisible&&
                magnified.Zip(magnified.Skip(1),(a,b)=>a.Right<=b.Left).All(ok=>ok),
                new{scales=magnified.Select((r,i)=>r.Width/baseArt[i]).ToArray(),hoverWidth=magnified[target].Width});
            Shot("dock-hover");
            Native.MovePointer(gateway.X,gateway.Y);await Task.Delay(70);Native.TestButton(true);await Task.Delay(28);Native.TestButton(false);
            await Task.Delay(720);
            var drawer=c.Drawer!;
            var body=drawer.BodyBounds;
            var tile=drawer.SortTiles(false).First();
            var text=Find<TextBlock>(tile).First();
            Check("drawer geometry and controls fit",drawer.IsVisible&&drawer.ChromeWithinSurface&&drawer.MeasuredColumns()==c.Layout.Columns&&
                body.Left>=area.Left&&body.Right<=area.Right&&body.Top>=area.Top&&body.Bottom<dock.Top&&Math.Abs(drawer.VisibleGridRows-c.Layout.VisibleRows)<.1,
                new{dockWidth=dock.Right-dock.Left,dockHeight=dock.Bottom-dock.Top,drawerWidth=body.Right-body.Left,drawerHeight=body.Bottom-body.Top,
                    gap=dock.Top-body.Bottom,taskbarGap=area.Bottom-dock.Bottom,dockIcon=baseArt.First(),drawerIcon=c.Layout.DrawerIcon,
                    columns=drawer.MeasuredColumns(),rows=c.Layout.VisibleRows,text.FontSize,text.LineHeight,textHeight=text.ActualHeight});
            Shot("drawer-settled");
            var grid=drawer.Scroller.PointToScreen(new Point(drawer.Scroller.ActualWidth*.6,drawer.Scroller.ActualHeight*.5));
            Native.MovePointer((int)grid.X,(int)grid.Y);Native.TestWheel(-120);await Task.Delay(390);
            Check("real mouse wheel scrolls the grid",drawer.Scroller.VerticalOffset>1);
            Native.TestKey(17,true);Native.TestKey(70,true);Native.TestKey(70,false);Native.TestKey(17,false);await Task.Delay(100);
            foreach(byte key in new byte[]{87,79,82,75})
            {
                Native.TestKey(key,true);await Task.Delay(25);Native.TestKey(key,false);await Task.Delay(35);
            }
            await Task.Delay(230);
            var search=Find<TextBox>(drawer).FirstOrDefault();
            Check("real keyboard search filters",search?.Text.Equals("work",StringComparison.OrdinalIgnoreCase)==true&&drawer.SortTiles(false).Count is >0 and <10,
                new{text=search?.Text,results=drawer.SortTiles(false).Count});
            var close=Find<Button>(drawer).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="关闭抽屉");
            var closePoint=close.PointToScreen(new Point(close.ActualWidth/2,close.ActualHeight/2));
            Native.MovePointer((int)closePoint.X,(int)closePoint.Y);await Task.Delay(60);
            Native.TestButton(true);await Task.Delay(32);Native.TestButton(false);await Task.Delay(480);
            Check("close button remains usable after searching",!drawer.IsVisible&&c.Dock.IsVisible);
        }
        catch(Exception e){Check("exception",false,e.ToString());}
        finally
        {
            Native.TestButton(false);Native.TestKey(17,false);scene.Close();
            File.WriteAllText(Path.Combine(folder,"proportion.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();
        }
    }
}
