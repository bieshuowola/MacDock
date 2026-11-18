using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
namespace MacDock;

// Captures the final desktop composition, never a RenderTargetBitmap substitute.
// Layer isolation is diagnostic only; the normal capture always retains all layers.
internal static class CornerCoverageChecks
{
    [DllImport("dwmapi.dll")]static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out Native.Rect value,int size);
    internal static async Task Run(Controller c)
    {
        var dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var notes=new List<object>();Window? reference=null,fixture=null;
        var background=Environment.GetEnvironmentVariable("MACDOCK_CORNER_BACKGROUND");
        void Capture(string name,Native.Rect r)
        {
            using var b=new System.Drawing.Bitmap(r.Right-r.Left,r.Bottom-r.Top);
            using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(r.Left,r.Top,0,0,b.Size);
            b.Save(Path.Combine(dir,name+".png"));notes.Add(new{name,r.Left,r.Top,width=b.Width,height=b.Height});
        }
        try
        {
            if(background!="wallpaper")
            {
                fixture=new Window{Title="MacDock corner coverage background",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,Topmost=true,Left=c.WorkArea.Left/c.Scale,Top=c.WorkArea.Top/c.Scale,Width=c.WorkArea.Width/c.Scale,Height=c.WorkArea.Height/c.Scale,Background=new SolidColorBrush(background=="light"?Colors.White:background=="dark"?Color.FromRgb(24,28,36):Colors.Black)};
                fixture.Show();
            }
            c.Busy=true;c.Expand();await Task.Delay(400);c.ToggleDrawer();await Task.Delay(900);c.SettleDrawerMotion();
            var d=c.Drawer!;d.Activate();
            if(fixture!=null)Native.SetWindowPos(new WindowInteropHelper(fixture).Handle,c.Dock.Handle,0,0,0,0,0x13);
            Native.MovePointer(c.WorkArea.Left+10,c.WorkArea.Top+10);await Task.Delay(200);
            var rect=d.BodyBounds;Capture("all",rect);notes.Add(new{scale=c.Scale,radius=d.MaterialRadius,material=d.MaterialSnapshot});
            // A normal decorated window uses DWM's native round corners, not our clips.
            reference=new Window{Title="Windows native corner reference",Topmost=true,Left=c.WorkArea.Left/c.Scale+32,Top=c.WorkArea.Top/c.Scale+96,Width=280,Height=260,Background=new SolidColorBrush(Color.FromRgb(150,150,150)),ResizeMode=ResizeMode.CanResize};
            reference.Show();var handle=new WindowInteropHelper(reference).Handle;int round=2;Native.DwmSetWindowAttribute(handle,33,ref round,4);
            await Task.Delay(240);DwmGetWindowAttribute(handle,9,out var nr,16);Capture("native-dwm",nr);reference.Close();reference=null;
#if MACDOCK_EXPERIENCE_CHECKS
            HostGlass.DiagnosticHideDrawer=true;d.RefreshMaterial();await Task.Delay(160);Capture("foreground",rect);
            HostGlass.DiagnosticHideDrawer=false;d.RefreshMaterial();d.Surface!.Opacity=0;await Task.Delay(160);Capture("backdrop",rect);
            d.Surface.Opacity=1;await Task.Delay(160);
#endif
            var anchor=c.Dock.GatewayCenter;
            foreach(double p in new[]{.25,.55,.8,.94,.985,1.0})
            {
                d.SetGenie(p,(anchor.X-rect.Left)/c.Scale,(anchor.Y-rect.Top)/c.Scale);
                await Task.Delay(100);string name="pose-"+p.ToString("F3",System.Globalization.CultureInfo.InvariantCulture);Capture(name,d.BodyBounds);
                var pose=d.CurrentPose!;var top=pose.Row(0);var bottom=pose.Row(1);
                for(int k=0;k<4;k++)
                {
                    var row=k<2?top:bottom;int x=rect.Left+(int)Math.Round((k%2==0?row.Left:row.Right)*c.Scale)-(k%2==0?0:40);
                    int y=rect.Top+(int)Math.Round(row.Y*c.Scale)-(k<2?0:40);
                    Capture(name+"-corner-"+k,new Native.Rect{Left=x,Top=y,Right=x+40,Bottom=y+40});
                }
            }
            c.SettleDrawerMotion();await Task.Delay(100);Capture("settled",d.BodyBounds);
            // Render-scale fixtures exercise fractional pixel radii without changing
            // the user's Windows display settings. These are not mixed-DPI monitors.
            var canvas=new System.Windows.Controls.Canvas();
            var scaled=new Window{Title="MacDock corner render-scale fixtures",WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=20,Top=20,Width=900,Height=260,Background=Brushes.Black,Content=canvas,Topmost=true};
            double offset=12;
            foreach(double factor in new[]{1.0,1.25,1.5,2.0})
            {
                var patch=new CoverageBorder{Width=140,Height=100,CornerRadius=new CornerRadius(26),Background=(Brush)Application.Current.FindResource("GlassSurface"),LayoutTransform=new ScaleTransform(factor,factor)};patch.SampleScale(factor);
                System.Windows.Controls.Canvas.SetLeft(patch,offset);System.Windows.Controls.Canvas.SetTop(patch,16);canvas.Children.Add(patch);offset+=140*factor+24;
            }
            scaled.Show();await Task.Delay(150);Native.GetWindowRect(new WindowInteropHelper(scaled).Handle,out var sr);Capture("render-scales-100-125-150-200",sr);scaled.Close();
        }
        catch(Exception e){notes.Add(new{error=e.ToString()});}
        finally
        {
#if MACDOCK_EXPERIENCE_CHECKS
            HostGlass.DiagnosticHideDrawer=false;
#endif
            reference?.Close();fixture?.Close();File.WriteAllText(Path.Combine(dir,"coverage.json"),JsonSerializer.Serialize(notes,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();
        }
    }
}
