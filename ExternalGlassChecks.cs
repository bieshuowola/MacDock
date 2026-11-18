using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace MacDock;
internal static class ExternalGlassChecks
{
    internal static int Fixture(string path)
    {
        var a=System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;var window=new System.Windows.Forms.Form{Text="MacDock external glass fixture",FormBorderStyle=System.Windows.Forms.FormBorderStyle.None,ShowInTaskbar=false,TopMost=true,StartPosition=System.Windows.Forms.FormStartPosition.Manual,Bounds=a,BackColor=System.Drawing.Color.Black};
        string previous="",state="black";
        window.Paint+=(_,e)=>{if(state is "black" or "white")e.Graphics.Clear(state=="black"?System.Drawing.Color.Black:System.Drawing.Color.White);else{int count=state.StartsWith("stripe")?180:2;for(int i=0;i<count;i++){var brush=state.EndsWith("red")?(i%2==0?System.Drawing.Brushes.Red:(count==180?System.Drawing.Brushes.Black:System.Drawing.Brushes.Blue)):(i%2==0?System.Drawing.Brushes.Blue:(count==180?System.Drawing.Brushes.Black:System.Drawing.Brushes.Red));e.Graphics.FillRectangle(brush,i*window.ClientSize.Width/(float)count,0,window.ClientSize.Width/(float)count+1,window.ClientSize.Height);}}};
        var timer=new System.Windows.Forms.Timer{Interval=60};timer.Tick+=(_,_)=>{try{state=File.ReadAllText(path);if(state==previous)return;previous=state;if(state is "black" or "white")window.BackColor=state=="black"?System.Drawing.Color.Black:System.Drawing.Color.White;if(state=="exit"){window.Close();return;}window.Refresh();File.WriteAllText(path+".ready",window.Handle.ToInt64()+"|"+state);}catch{}};window.Shown+=(_,_)=>timer.Start();window.FormClosed+=(_,_)=>timer.Dispose();System.Windows.Forms.Application.Run(window);window.Dispose();return 0;
    }
    internal static async Task<bool> Run(Controller c,bool shutdown=true)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(output);var control=Path.Combine(output,"backdrop-state");File.WriteAllText(control,"black");var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--external-fixture");start.ArgumentList.Add(control);using var process=Process.Start(start)!;var checks=new List<object>();
        bool passed=true;void Check(string name,bool pass,object? data=null){passed&=pass;checks.Add(new{name,pass,data});}
        double[] Sample(int x,int y,int w,int h){using var b=new System.Drawing.Bitmap(w,h);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(x,y,0,0,b.Size);double r=0,gc=0,blue=0,sq=0;for(int yy=0;yy<h;yy++)for(int xx=0;xx<w;xx++){var p=b.GetPixel(xx,yy);r+=p.R;gc+=p.G;blue+=p.B;sq+=p.R*p.R;}int n=w*h;return new[]{r/n,gc/n,blue/n,Math.Sqrt(Math.Max(0,sq/n-r/n*r/n))};}
        try
        {
            for(int n=0;n<100&&!File.Exists(control+".ready");n++)await Task.Delay(50);c.Busy=true;c.Expand();if(c.Drawer?.IsVisible!=true)c.ToggleDrawer();await Task.Delay(600);for(int wait=0;wait<50&&Anim.ActiveTimers>0;wait++)await Task.Delay(40);
            foreach(var mode in new[]{"light","dark"})
            {
                Theme.Apply(mode);await Task.Delay(150);Native.GetWindowRect(c.Dock.Handle,out var d);var a=c.Drawer!.BodyBounds;var positions=new[]{(d.Left+12,d.Top+8,Math.Max(15,d.Right-d.Left-24),8),(a.Left+(a.Right-a.Left)/4) is int middle?(middle-20,a.Top+12,40,8):default};
                var phases=new Dictionary<string,double[][]>();foreach(var phase in new[]{"black","white","split-red","split-blue","stripe-red","stripe-blue"})
                {File.WriteAllText(control,phase);for(int wait=0;wait<100;wait++){await Task.Delay(40);if(File.ReadAllText(control+".ready").EndsWith("|"+phase))break;}await Task.Delay(240);var fixtureHandle=(nint)long.Parse(File.ReadAllText(control+".ready").Split('|')[0]);Native.SetWindowPos(fixtureHandle,-1,c.WorkArea.Left,c.WorkArea.Top,c.WorkArea.Width,c.WorkArea.Height,0x50);Native.SetWindowPos(c.Dock.Handle,-1,0,0,0,0,0x13);Native.SetWindowPos(c.Drawer.Handle,-1,0,0,0,0,0x13);await Task.Delay(100);if(phase=="black"){var windows=new List<object>();Native.EnumWindows((h,_)=>{if(Native.IsWindowVisible(h)&&Native.GetWindowRect(h,out var wr)){Native.GetWindowThreadProcessId(h,out var pid);windows.Add(new{h=(long)h,pid,cls=Native.Class(h),rect=$"{wr.Left},{wr.Top},{wr.Right},{wr.Bottom}",style=Native.GetWindowLongPtr(h,-20).ToInt64()});}return true;},0);File.WriteAllText(Path.Combine(output,mode+"-zorder.json"),JsonSerializer.Serialize(windows));}phases[phase]=positions.Select(p=>Sample(p.Item1,p.Item2,p.Item3,p.Item4)).ToArray();Check(mode+" actual external source "+phase,phase is not ("black" or "white")||Math.Abs(Sample(c.WorkArea.Left+20,c.WorkArea.Top+20,10,10).Take(3).Average()- (phase=="black"?0:255))<10);if(phase=="split-blue"){using var b=new System.Drawing.Bitmap(a.Right-a.Left,a.Bottom-a.Top);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(a.Left,a.Top,0,0,b.Size);b.Save(Path.Combine(output,mode+"-external-drawer.png"));}}
                for(int i=0;i<2;i++){Check(mode+" large background range "+i,phases["white"][i][0]-phases["black"][i][0]>(mode=="light"?60:25),new{black=phases["black"][i],white=phases["white"][i]});Check(mode+" real dynamic colour "+i,Math.Abs(phases["stripe-red"][i][0]-phases["stripe-blue"][i][0])+Math.Abs(phases["stripe-red"][i][2]-phases["stripe-blue"][i][2])>10,new{before=phases["stripe-red"][i],after=phases["stripe-blue"][i]});}
                double L(double n){n/=255;return n<=.04045?n/12.92:Math.Pow((n+.055)/1.055,2.4);}
                double Light(double[] rgb)=>.2126*L(rgb[0])+.7152*L(rgb[1])+.0722*L(rgb[2]);
                var ink=Theme.Ink.Color;double text=Light(new double[]{ink.R,ink.G,ink.B});
                for(int i=0;i<2;i++){
                    Check(mode+" narrow stripes are genuinely blurred "+i,phases["stripe-red"][i][3]<35,phases["stripe-red"][i][3]);
                    var contrast=new[]{"black","white"}.Select(p=>{double back=Light(phases[p][i]);return(Math.Max(text,back)+.05)/(Math.Min(text,back)+.05);}).Min();Check(mode+" sampled text contrast "+i,contrast>=4.5,contrast);
                }
                File.WriteAllText(Path.Combine(output,mode+"-samples.json"),JsonSerializer.Serialize(phases,new JsonSerializerOptions{WriteIndented=true}));
            }
        }
        catch(Exception e){passed=false;checks.Add(new{name="error",pass=false,data=e.ToString()});}
        finally{File.WriteAllText(control,"exit");c.Busy=false;c.CloseDrawer();Theme.Apply(c.Store.Data.Appearance);File.WriteAllText(Path.Combine(output,"external-glass.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));if(shutdown)System.Windows.Application.Current.Shutdown();}
        return passed;
    }
}
