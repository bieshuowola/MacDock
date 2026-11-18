using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
namespace MacDock;
internal static class CornerChecks
{
    internal static async Task Run(Controller c)
    {
#if MACDOCK_EXPERIENCE_CHECKS
        if(Environment.GetEnvironmentVariable("MACDOCK_DRAWER_SCROLL_CHECK")=="1"){await DrawerScrollChecks.Run(c);return;}
#endif
        if(Environment.GetEnvironmentVariable("MACDOCK_STARTUP_CHECK")=="1"){await StartupChecks.Run(c);return;}
        if(Environment.GetEnvironmentVariable("MACDOCK_CORNER_QUALITY")=="1"){await CornerCoverageChecks.Run(c);return;}
        var dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(dir);var notes=new List<object>();var background=Environment.GetEnvironmentVariable("MACDOCK_CORNER_BACKGROUND") switch {"light"=>Colors.White,"dark"=>Color.FromRgb(24,28,36),_=>Colors.Black};var w=new Window{Title="MacDock corner pixel fixture",Topmost=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=c.WorkArea.Left/c.Scale,Top=c.WorkArea.Top/c.Scale,Width=c.WorkArea.Width/c.Scale,Height=c.WorkArea.Height/c.Scale,Background=new SolidColorBrush(background)};
        object Sample(string name){var d=c.Drawer!;var r=d.BodyBounds;int width=r.Right-r.Left,height=(int)Math.Round(d.PanelHeight*c.Scale),radius=(int)(d.MaterialRadius*c.Scale);using var picture=new System.Drawing.Bitmap(width,height);using(var g=System.Drawing.Graphics.FromImage(picture))g.CopyFromScreen(r.Left,r.Top,0,0,picture.Size);picture.Save(Path.Combine(dir,name+".png"));var counts=new int[4];var maxima=new double[4];for(int k=0;k<4;k++)for(int y=0;y<radius;y++)for(int x=0;x<radius;x++){if(Math.Pow(radius-x-.5,2)+Math.Pow(radius-y-.5,2)<Math.Pow(radius+2,2))continue;int xx=(k%2==0)?x:width-1-x,yy=k<2?y:height-1-y;var p=picture.GetPixel(xx,yy);double excess=(p.R-background.R)*.2126+(p.G-background.G)*.7152+(p.B-background.B)*.0722;maxima[k]=Math.Max(maxima[k],excess);if(excess>12)counts[k]++;}var result=new{name,counts,maxima,material=d.MaterialSnapshot};notes.Add(result);return result;}
        try{
            w.Show();c.Busy=true;c.Expand();await Task.Delay(450);c.ToggleDrawer();await Task.Delay(1000);c.SettleDrawerMotion();c.Drawer!.Activate();await Task.Delay(150);Sample("normal-all-layers");c.Drawer!.Opacity=0;await Task.Delay(120);Sample("normal-backdrop-only");c.Drawer.Opacity=1;await Task.Delay(80);
            var rect=c.Drawer.BodyBounds;var anchor=c.Dock.GatewayCenter;c.Drawer.SetGenie(1,(anchor.X-rect.Left)/c.Scale,(anchor.Y-rect.Top)/c.Scale);await Task.Delay(150);Sample("flat-curved-mode");c.Drawer.Opacity=0;await Task.Delay(100);Sample("flat-curved-backdrop-only");c.Drawer.Opacity=1;c.SettleDrawerMotion();await Task.Delay(100);
            int frames=0,bad=0;var maxima=new int[4];string layer="all";EventHandler? inspect=null;
            inspect=(_,_)=>{var d=c.Drawer!;if(!d.IsVisible||c.DrawerProgress<.985)return;var r=d.BodyBounds;int width=r.Right-r.Left,height=(int)Math.Round(d.PanelHeight*c.Scale),radius=(int)(d.MaterialRadius*c.Scale);using var picture=new System.Drawing.Bitmap(width,height);using(var g=System.Drawing.Graphics.FromImage(picture))g.CopyFromScreen(r.Left,r.Top,0,0,picture.Size);int[] white=new int[4];var pose=d.CurrentPose;for(int k=0;k<4;k++)for(int y=0;y<radius;y++)for(int x=0;x<radius;x++){int xx=k%2==0?x:width-1-x,yy=k<2?y:height-1-y;bool excluded;if(pose==null)excluded=Math.Pow(radius-x-.5,2)+Math.Pow(radius-y-.5,2)>Math.Pow(radius+2,2);else{var top=pose.Row(0);var bottom=pose.Row(1);double localY=(yy+.5)/c.Scale,t=(localY-top.Y)/Math.Max(.5,bottom.Y-top.Y);var row=pose.Row(Math.Clamp(t,0,1),true);excluded=t<0||t>1||xx/c.Scale<row.Left-2||xx/c.Scale>row.Right+2;}if(!excluded)continue;var p=picture.GetPixel(xx,yy);double excess=(p.R-background.R)*.2126+(p.G-background.G)*.7152+(p.B-background.B)*.0722;if(excess>12)white[k]++;}frames++;for(int k=0;k<4;k++)maxima[k]=Math.Max(maxima[k],white[k]);if(white.Any(n=>n>0)){if(bad<16)picture.Save(Path.Combine(dir,$"transient-{layer}-{bad}.png"));bad++;notes.Add(new{stage="bright-exterior",layer,phase=c.DrawerProgress,white,material=d.MaterialSnapshot});}};
            CompositionTarget.Rendering+=inspect;
            try{foreach(var testLayer in new[]{"all","backdrop"}){layer=testLayer;c.Drawer.Opacity=layer=="all"?1:0;for(int i=0;i<8;i++){c.CloseDrawer(keepDock:true);await Task.Delay(330);c.ToggleDrawer();await Task.Delay(410);}}}finally{CompositionTarget.Rendering-=inspect;c.Drawer.Opacity=1;}
            notes.Add(new{stage="transient-summary",frames,bad,maxima});
        }catch(Exception e){notes.Add(new{error=e.ToString()});}
        finally{w.Close();File.WriteAllText(Path.Combine(dir,"corners.json"),JsonSerializer.Serialize(notes,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
}
