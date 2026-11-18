using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
namespace MacDock;
internal static class ContourChecks
{
    internal static async Task Run(Controller c)
    {
        var dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;var results=new List<object>();var frames=new List<object>();var clock=System.Diagnostics.Stopwatch.StartNew();var color=Color.FromRgb(30,58,120);
        var bg=new Window{Title="MacDock contour fixture",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Left=c.WorkArea.Left/c.Scale,Top=c.WorkArea.Top/c.Scale,Width=c.WorkArea.Width/c.Scale,Height=c.WorkArea.Height/c.Scale,Background=new SolidColorBrush(color)};
        void Check(string name,bool pass,object? detail=null){results.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(dir,"contours.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));}
        System.Drawing.Bitmap Capture(){var a=c.WorkArea;var b=new System.Drawing.Bitmap(a.Width,a.Height);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(a.Left,a.Top,0,0,b.Size);return b;}
        void Shot(string name){using var b=Capture();b.Save(Path.Combine(dir,name+".png"));}
        double Shadow(System.Drawing.Bitmap baseline,int x,int y){using var b=new System.Drawing.Bitmap(1,1);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(x,y,0,0,b.Size);var p=b.GetPixel(0,0);var prior=baseline.GetPixel(x-c.WorkArea.Left,y-c.WorkArea.Top);return(prior.R-p.R)*.2126+(prior.G-p.G)*.7152+(prior.B-p.B)*.0722;}
        try{
            // Exercise intermediate geometry, not just the resting frame: a hollow
            // figure flattened to no points previously left the old full-size mesh.
            var contour=new SoftContour();
            var viewport=(System.Windows.Controls.Viewport3D)VisualTreeHelper.GetChild(contour,0);
            var model=(System.Windows.Media.Media3D.GeometryModel3D)((System.Windows.Media.Media3D.ModelVisual3D)viewport.Children[1]).Content;
            var mesh=(System.Windows.Media.Media3D.MeshGeometry3D)model.Geometry;
            foreach(double p in new[]{1.0,.1,.3,.6,.9,1.0,.6,.1}){
                contour.Shape(1000,820,26,new Rect(0,0,1000,700),p==1?null:new GeniePose(1000,700,820,820,790,p),1);
                var bounds=mesh.Bounds;
                Check("shadow mesh follows current pose "+p,!bounds.IsEmpty&&double.IsFinite(bounds.SizeX)&&(p>=.9?bounds.SizeX>950:bounds.SizeX<900),new{p,bounds=bounds.ToString(),vertices=mesh.Positions.Count});
            }
            bg.Show();Native.MovePointer(100,150);await Task.Delay(150);foreach(var e in c.Store.Data.Entries)e.Pinned=false;foreach(var e in c.Store.Data.Entries.Where(e=>e.InDrawer).Take(4))e.Pinned=true;c.Changed();
            c.DockAnimationFrame=(p,v)=>frames.Add(new{kind="dock",ms=clock.Elapsed.TotalMilliseconds,p,v});
            var sense=c.WakeBounds;int x=(sense.Left+sense.Right)/2,y=(sense.Top+sense.Bottom)/2;
            for(int i=0;i<6;i++){
                Native.MovePointer(x,y);await Task.Delay(500);Check("actual Dock wake "+i,c.Dock.IsVisible&&!c.DockMotionRunning);
                if(i%2==0){Native.MovePointer(100,150);await Task.Delay(270);Native.MovePointer(x,y);await Task.Delay(70);Native.MovePointer(100,150);await Task.Delay(100);Native.MovePointer(x,y);await Task.Delay(500);Check("Dock fast reversal settles "+i,c.Dock.IsVisible&&!c.DockMotionRunning);}
                Native.MovePointer(100,150);await Task.Delay(600);Check("actual Dock hide "+i,!c.Dock.IsVisible&&!c.DockMotionRunning);
            }
            c.DockAnimationFrame=null;c.Busy=true;c.ToggleDrawer();await Task.Delay(650);c.CloseDrawer(keepDock:true);await Task.Delay(450);
            foreach(double target in new[]{.30,.55,.82,.97}){
                var backdrop=target==.30?Color.FromRgb(22,24,34):target==.55?Color.FromRgb(231,222,244):target==.82?Colors.White:color;
                bg.Background=new SolidColorBrush(backdrop);await Task.Delay(100);
                using var baseline=Capture();
                var pixel=baseline.GetPixel(12,12);
                Check("test backdrop is visible "+target,pixel.R==backdrop.R&&pixel.G==backdrop.G&&pixel.B==backdrop.B);
                bool held=false;c.DrawerAnimationFrame=(p,_,_,_)=>{frames.Add(new{kind="drawer",ms=clock.Elapsed.TotalMilliseconds,p});if(!held&&p>=target){held=true;c.PauseDrawerMotion();}};
                c.ToggleDrawer();for(int i=0;i<100&&!held;i++)await Task.Delay(10);c.DrawerAnimationFrame=null;await Task.Delay(110);var d=c.Drawer!;var pose=d.CurrentPose!;var row=pose.Row(.5,true);var r=d.BodyBounds;int px=r.Left+(int)Math.Round((row.Left-7)*c.Scale),py=r.Top+(int)Math.Round(row.Y*c.Scale);double depth=Shadow(baseline,px,py);
                Check("curved shadow visible throughout opening "+target,held&&depth>1,new{phase=c.DrawerProgress,depth,x=px,y=py});
                if(target<=.55){double ghost=Shadow(baseline,r.Left-7,r.Top+(int)Math.Round(d.PanelHeight*c.Scale*.5));Check("no resting rectangle behind intermediate pose "+target,Math.Abs(ghost)<1,new{phase=c.DrawerProgress,ghost});}
                Shot("opening-"+target.ToString(System.Globalization.CultureInfo.InvariantCulture));c.ResumeDrawerMotion();await Task.Delay(500);c.CloseDrawer(keepDock:true);await Task.Delay(450);
            }
            foreach(double target in new[]{.7,.35}){
                bg.Background=Brushes.White;await Task.Delay(100);using var baseline=Capture();
                c.ToggleDrawer();await Task.Delay(600);bool held=false;
                c.DrawerAnimationFrame=(p,_,_,_)=>{if(!held&&p<=target){held=true;c.PauseDrawerMotion();}};
                c.CloseDrawer(keepDock:true);for(int i=0;i<100&&!held;i++)await Task.Delay(10);c.DrawerAnimationFrame=null;await Task.Delay(110);
                var d=c.Drawer!;var pose=d.CurrentPose!;var row=pose.Row(.5,true);var r=d.BodyBounds;
                double depth=Shadow(baseline,r.Left+(int)Math.Round((row.Left-7)*c.Scale),r.Top+(int)Math.Round(row.Y*c.Scale));
                Check("curved shadow follows closing "+target,held&&depth>1,new{phase=c.DrawerProgress,depth});
                double ghost=Shadow(baseline,r.Left-7,r.Top+(int)Math.Round(d.PanelHeight*c.Scale*.5));
                Check("no resting rectangle during closing "+target,Math.Abs(ghost)<1,new{phase=c.DrawerProgress,ghost});
                Shot("closing-"+target.ToString(System.Globalization.CultureInfo.InvariantCulture));c.ResumeDrawerMotion();await Task.Delay(450);
            }
            using(var baseline=Capture()){
                c.ToggleDrawer();await Task.Delay(600);var full=c.Drawer!.BodyBounds;Check("settled exterior shadow retained",Shadow(baseline,full.Left-7,full.Top+(int)(c.Drawer.PanelHeight*c.Scale*.5))>1);Shot("settled-blue");
            }
            foreach(var mode in new[]{"light","dark"}){Theme.Apply(mode);bg.Background=Brushes.White;await Task.Delay(180);Shot(mode+"-white");bg.Background=new SolidColorBrush(color);await Task.Delay(180);Shot(mode+"-blue");}
            Theme.Apply(c.Store.Data.Appearance);
            for(int i=0;i<6;i++){c.CloseDrawer(keepDock:true);await Task.Delay(55);c.ToggleDrawer();await Task.Delay(45);c.CloseDrawer(keepDock:true);await Task.Delay(55);c.ToggleDrawer();await Task.Delay(450);Check("Genie quick reversals recover "+i,c.Drawer.IsVisible&&!c.Drawer.IsDeforming&&!c.DrawerMotionRunning);}
            c.CloseDrawer(collapseDock:true);await Task.Delay(600);Check("all moving sheets hidden after close",!c.Drawer.IsVisible&&!c.Dock.IsVisible&&Anim.ActiveTimers==0);
        }catch(Exception e){results.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{c.DockAnimationFrame=null;c.DrawerAnimationFrame=null;bg.Close();File.WriteAllText(Path.Combine(dir,"contours.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(Path.Combine(dir,"contour-frames.json"),JsonSerializer.Serialize(frames));Application.Current.Shutdown();}
    }
}
