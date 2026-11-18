using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace MacDock;
internal static class GenieChecks
{
    internal static async Task Run(Controller c)
    {
        var dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(dir);var results=new List<object>();var frames=new List<object>();var layers=new List<object>();int unarmed=0,badHandoff=0;var watch=Stopwatch.StartNew();c.DrawerLayerFrame=(stage,material)=>{layers.Add(new{ms=watch.Elapsed.TotalMilliseconds,stage,material});if(material==null)return;var m=JsonSerializer.SerializeToElement(material);if(stage=="begin-after-resize"&&!m.GetProperty("deforming").GetBoolean())unarmed++;if(stage=="end-normal-backdrop"&&(m.GetProperty("frontVisible").GetBoolean()||m.GetProperty("backdropVisible").GetBoolean())){var f=m.GetProperty("front");var b=m.GetProperty("backdrop");int height=f.GetProperty("Bottom").GetInt32()-f.GetProperty("Top").GetInt32();if(m.GetProperty("composedHeight").GetInt32()!=height||b.GetProperty("Bottom").GetInt32()-b.GetProperty("Top").GetInt32()!=height)badHandoff++;}};
        void Check(string name,bool pass,object? detail=null){results.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(dir,"genie-progress.json"),JsonSerializer.Serialize(results));if(!pass)throw new Exception(name);}
        Native.GetCursorPos(out var prior);PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;int shot=0;
        var backdrop=new Window{Title="MacDock Genie dynamic background fixture",Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=20,Top=80,Width=c.WorkArea.Width/c.Scale-40,Height=c.WorkArea.Height/c.Scale-110,Background=Brushes.DarkSlateBlue};
        try{
            backdrop.Show();Native.MovePointer(100,100);c.Expand();c.Busy=true;await Task.Delay(450);
            var icons=c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>c.Icon(e.Path,64)).ToArray();for(int i=0;i<250&&icons.Any(s=>s.Loading);i++)await Task.Delay(30);
            foreach(var e in c.Store.Data.Entries)e.Pinned=false;foreach(var e in c.Store.Data.Entries.Where(e=>e.InDrawer).Take(5))e.Pinned=true;c.Changed();await Task.Delay(80);
            var phases=new List<double>();
            c.DrawerAnimationFrame=(p,y,s,a)=>{phases.Add(p);var pose=c.Drawer!.CurrentPose;if(pose==null)return;var top=pose.Row(0);var mid=pose.Row(.5);var bottom=pose.Row(1);frames.Add(new{ms=watch.Elapsed.TotalMilliseconds,p,y,a,top,mid,bottom,c.DrawerVelocity});if(Environment.GetEnvironmentVariable("MACDOCK_GENIE_SHOTS")=="1"&&p is >.2 and <.95&&shot<6){Capture(c,Path.Combine(dir,$"opening-{shot++}.png"));}};
            c.ToggleDrawer();await Task.Delay(650);c.DrawerAnimationFrame=null;
            Check("actual live controls settle at flat unscaled geometry",c.Drawer is {IsVisible:true,IsDeforming:false}&&c.DrawerProgress==1&&!c.DrawerMotionRunning&&c.Drawer.Surface!.RenderTransform.Value.IsIdentity);
            Check("curved ribbon has multiple actual intermediate phases",phases.Count>6&&phases.Any(p=>p>.15&&p<.7),phases);
            Check("live host backdrop remains available",c.Dock.MaterialAvailable&&c.Drawer!.MaterialAvailable);
            Capture(c,Path.Combine(dir,"settled.png"));
            // An actual in-flight close/reopen retarget keeps both pose and speed.
            bool reversed=false;double before=0,after=0,v0=0,v1=0;
            c.DrawerAnimationFrame=(p,_,_,_)=>{if(!reversed&&p is >.3 and <.8){reversed=true;before=p;v0=c.DrawerVelocity;c.DrawerAnimationFrame=null;c.ToggleDrawer();after=c.DrawerProgress;v1=c.DrawerVelocity;}};
            c.CloseDrawer(keepDock:true);await Task.Delay(650);c.DrawerAnimationFrame=null;
            Check("reversal preserves continuous pose and velocity",reversed&&Math.Abs(before-after)<1e-8&&Math.Abs(v0-v1)<1e-8,new{before,after,v0,v1});
            Check("reverse finishes once with no hidden curved state",c.Drawer!.IsVisible&&!c.Drawer.IsDeforming&&!c.DrawerMotionRunning);
            // Pause the moving control on down, exercise its real WPF/3D input mapping.
            c.CloseDrawer(keepDock:true);await Task.Delay(500);var clickWatch=Stopwatch.StartNew();c.ToggleDrawer();var returnTime=clickWatch.Elapsed.TotalMilliseconds;var returnP=c.DrawerProgress;await Task.Delay(150);var delayTime=clickWatch.Elapsed.TotalMilliseconds;var delayP=c.DrawerProgress;
            var close=Find<Button>(c.Drawer).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="关闭抽屉");
            var point=close.PointToScreen(new Point(close.ActualWidth/2,close.ActualHeight/2));Native.MovePointer((int)point.X,(int)point.Y);Native.TestButton(true);await Task.Delay(60);double paused=c.DrawerProgress;await Task.Delay(60);Check("moving control pauses between real down and up",c.Drawer.IsDeforming&&Math.Abs(c.DrawerProgress-paused)<1e-8,new{paused,now=c.DrawerProgress,point,returnTime,returnP,delayTime,delayP,c.Trail});Native.TestButton(false);await Task.Delay(500);
            Check("curved close button receives click and keeps Dock",!c.Drawer.IsVisible&&c.Dock.IsVisible&&!c.DrawerMotionRunning,new{c.Drawer.IsVisible,dock=c.Dock.IsVisible,c.DrawerProgress,c.DrawerMotionRunning,c.Drawer.LastPointerInput,c.Trail});
            for(int i=0;i<10;i++){c.ToggleDrawer();await Task.Delay(35+i%3*14);c.CloseDrawer(keepDock:true);await Task.Delay(25+i%4*12);}c.ToggleDrawer();await Task.Delay(650);
            Check("rapid interrupts recover exact flat geometry and no pending clocks",c.Drawer.IsVisible&&!c.Drawer.IsDeforming&&c.DrawerProgress==1&&Anim.ActiveTimers==0,Anim.ActiveTimers);
            c.Busy=false;var entry=c.Store.PinnedEntries()[0];var center=c.Dock.TileCenter(entry.Id);Native.MovePointer(center.X,center.Y);await Task.Delay(250);Check("open drawer preserves Dock wave",c.Dock.MaxScale>1.1);
            // Native menu invoked during a moving close must settle its owner's geometry.
            c.CloseDrawer(keepDock:true);await Task.Delay(100);c.ToggleDrawer();await Task.Delay(650);
            var full=c.Drawer.SortTiles(false);Check("all current desktop tiles survive repeated reparenting",full.Count>=50,full.Count);
            var originalOrder=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();c.CloseDrawer(keepDock:true);await Task.Delay(350);c.ToggleDrawer();await Task.Delay(150);
            var tile=c.Drawer.SortTiles(false)[0];var tp=tile.PointToScreen(new Point(tile.ActualWidth/2,tile.ActualHeight/2));Native.MovePointer((int)tp.X,(int)tp.Y);Native.TestButton(true);await Task.Delay(30);Native.MovePointer((int)tp.X+20,(int)tp.Y+8);await Task.Delay(180);
            Check("sort during reveal settles flat and keeps capture",PointerSort.Active!=null&&!c.Drawer.IsDeforming&&c.InternalDragging);
            Native.TestKey(27,true);await Task.Delay(35);Native.TestKey(27,false);Native.TestButton(false);await Task.Delay(120);Check("canceled reveal drag preserves Dock and releases capture",PointerSort.Active==null&&!c.InternalDragging&&originalOrder.SequenceEqual(c.Store.PinnedEntries().Select(e=>e.Id)));
            Native.MovePointer(100,100);await Task.Delay(100);c.CloseDrawer(collapseDock:true);await Task.Delay(600);Check("closed animation leaves no visible/input mesh or timer",!c.Drawer.IsVisible&&!c.Drawer.IsDeforming&&!c.Dock.IsVisible&&Anim.ActiveTimers==0);
            Check("every expanded HWND already has its curved clip armed",unarmed==0,new{unarmed});Check("every visible normal backdrop handoff has matching native and composed sizes",badHandoff==0,new{badHandoff});
            Check("no physical concurrent inputs",PointerHook.NonInjectedInputs==0);
        }catch(Exception e){results.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{c.DrawerAnimationFrame=null;c.DrawerLayerFrame=null;PointerSort.Active?.End(false);Native.TestButton(false);PointerHook.AuditInput=false;backdrop.Close();Native.MovePointer(prior.X,prior.Y);File.WriteAllText(Path.Combine(dir,"genie-test.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));File.WriteAllText(Path.Combine(dir,"genie-frames.json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));File.WriteAllText(Path.Combine(dir,"genie-layers.json"),JsonSerializer.Serialize(layers,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
    static IEnumerable<T> Find<T>(DependencyObject root)where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var ch=VisualTreeHelper.GetChild(root,i);if(ch is T t)yield return t;foreach(var t1 in Find<T>(ch))yield return t1;}}
    internal static async Task Shadow(Controller c)
    {
        string dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(dir);var records=new List<object>();var color=Color.FromRgb(200,217,245);Native.GetCursorPos(out var prior);
        var backdrop=new Window{Title="MacDock shadow isolation fixture",Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=c.WorkArea.Left/c.Scale,Top=c.WorkArea.Top/c.Scale,Width=c.WorkArea.Width/c.Scale,Height=c.WorkArea.Height/c.Scale,Background=new SolidColorBrush(color)};
        void Check(string name,bool pass,object? detail=null){records.Add(new{name,pass,detail});if(!pass)throw new Exception(name);}
        double Difference(int x,int y){using var pixel=new System.Drawing.Bitmap(1,1);using(var g=System.Drawing.Graphics.FromImage(pixel))g.CopyFromScreen(x,y,0,0,pixel.Size);var sample=pixel.GetPixel(0,0);return Math.Abs(sample.R-color.R)+Math.Abs(sample.G-color.G)+Math.Abs(sample.B-color.B);}
        async Task Hold(double target,bool opening,string name){bool held=false;c.DrawerAnimationFrame=(p,_,_,_)=>{if(!held&&(opening?p>=target:p<=target)){held=true;c.PauseDrawerMotion();}};if(opening)c.ToggleDrawer();else c.CloseDrawer(keepDock:true);for(int i=0;i<100&&!held;i++)await Task.Delay(10);c.DrawerAnimationFrame=null;Check(name+" reaches actual moving pose",held);await Task.Delay(100);var d=c.Drawer!;var pose=d.CurrentPose!;var r=d.BodyBounds;Capture(c,Path.Combine(dir,name+".png"));int x=r.Left+8,y=r.Top+(int)(d.PanelHeight*c.Scale*.55);var blank=Difference(x,y);var outside=Difference(r.Left-6,y);
            Check(name+" no premature final rectangular fill or shadow",blank<8&&outside<8,new{blank,outside,phase=c.DrawerProgress});var edge=pose.Row(.5);double curved=Difference(r.Left+(int)((edge.Left-3)*c.Scale),r.Top+(int)(edge.Y*c.Scale));Check(name+" curved edge has a soft local shadow",curved>.5,new{curved});if(name=="open-0.48"){var sx=r.Left+(int)((edge.Right-8)*c.Scale);var sy=r.Top+(int)(edge.Y*c.Scale);using var sample=new System.Drawing.Bitmap(1,1);System.Drawing.Color Pixel(){using(var g=System.Drawing.Graphics.FromImage(sample))g.CopyFromScreen(sx,sy,0,0,sample.Size);return sample.GetPixel(0,0);}var before=Pixel();backdrop.Background=new SolidColorBrush(Color.FromRgb(245,140,140));await Task.Delay(120);var after=Pixel();Capture(c,Path.Combine(dir,"mid-live-color-change.png"));Check("deforming host glass samples real changing background",Math.Abs(before.R-after.R)+Math.Abs(before.G-after.G)+Math.Abs(before.B-after.B)>20,new{before=before.ToString(),after=after.ToString()});backdrop.Background=new SolidColorBrush(color);await Task.Delay(80);}c.ResumeDrawerMotion();await Task.Delay(500);}
        try{
            backdrop.Show();c.Busy=true;c.Expand();await Task.Delay(400);var icons=c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>c.Icon(e.Path,c.Layout.DrawerIcon)).ToArray();for(int i=0;i<200&&icons.Any(e=>e.Loading);i++)await Task.Delay(30);
            foreach(double p in new[]{.25,.48,.70}){await Hold(p,true,"open-"+p);c.CloseDrawer(keepDock:true);await Task.Delay(500);}
            c.ToggleDrawer();await Task.Delay(600);var full=c.Drawer!.BodyBounds;var depth=Difference(full.Left-5,full.Top+(int)(c.Drawer.PanelHeight*c.Scale*.55));Capture(c,Path.Combine(dir,"rest-shadow.png"));Check("settled drawer retains exterior rounded shadow",depth>1,new{depth});await Hold(.55,false,"closing-mid");
            c.ToggleDrawer();await Task.Delay(80);c.CloseDrawer(keepDock:true);await Task.Delay(40);c.ToggleDrawer();await Task.Delay(600);Check("fast reverse restores clean complete glass and no clocks",c.Drawer.IsVisible&&!c.Drawer.IsDeforming&&Anim.ActiveTimers==0);Capture(c,Path.Combine(dir,"reverse-rest.png"));
        }catch(Exception e){records.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{c.DrawerAnimationFrame=null;backdrop.Close();Native.MovePointer(prior.X,prior.Y);File.WriteAllText(Path.Combine(dir,"shadow-test.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
    static void Capture(Controller c,string path){var a=c.WorkArea;int x=Math.Max(a.Left,(int)(a.Left+a.Width/2-c.Layout.DrawerWidth*c.Scale/2-15)),y=Math.Max(a.Top,(int)(a.Bottom-(c.Layout.DrawerWidth+250)*c.Scale));using var b=new System.Drawing.Bitmap(Math.Min(a.Width-x,(int)(c.Layout.DrawerWidth*c.Scale+30)),a.Bottom-y);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(x,y,0,0,b.Size);b.Save(path);}
}
