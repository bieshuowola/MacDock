using System.IO;
using System.Text.Json;
namespace MacDock;
internal static class WakeStripChecks
{
    internal static async Task Run(Controller c)
    {
        var dir=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(dir);var records=new List<object>();var frames=new List<object>();Native.GetCursorPos(out var original);var sense=c.WakeBounds;Native.GetWindowRect(c.IndicatorHandle,out var baseline);double full=86*c.Layout.DensityUnit;
        bool ok=true;
        void Check(string name,bool pass,object? detail=null){if(!pass)ok=false;records.Add(new{name,pass,detail});if(!pass)throw new Exception(name);}
        try{
            Native.MovePointer(c.WorkArea.Left+100,c.WorkArea.Top+150);await Task.Delay(700);Check("resting strip retains original length and appearance",Math.Abs(c.IndicatorLength-full)<.01&&c.IndicatorAlpha==1&&Native.IsWindowVisible(c.IndicatorHandle));
            int fixedFrames=0;bool overlap=false,shortened=false;var watch=System.Diagnostics.Stopwatch.StartNew();c.DockAnimationFrame=(p,v)=>{Native.GetWindowRect(c.IndicatorHandle,out var box);if(box.Left==baseline.Left&&box.Top==baseline.Top&&box.Right==baseline.Right&&box.Bottom==baseline.Bottom)fixedFrames++;if(p>0&&p<.5&&c.IndicatorAlpha>0&&c.Dock.IsVisible)overlap=true;if(c.IndicatorLength<full*.75&&c.IndicatorLength>full*.05&&c.IndicatorAlpha>0)shortened=true;frames.Add(new{ms=watch.Elapsed.TotalMilliseconds,p,v,length=c.IndicatorLength,alpha=c.IndicatorAlpha,visible=Native.IsWindowVisible(c.IndicatorHandle)});};
            int x=(sense.Left+sense.Right)/2+(int)(170*c.Layout.Unit*c.Scale),y=(sense.Top+sense.Bottom)/2;Native.MovePointer(x,y);await Task.Delay(650);Check("wide unchanged sensing area wakes despite narrow visible strip",c.Dock.IsVisible&&c.IndicatorAlpha==0);Check("strip contracts through intermediate lengths while Dock emerges",overlap&&shortened&&fixedFrames>5,new{overlap,shortened,fixedFrames});Check("indicator stays input transparent",Native.WindowFromPoint(new((baseline.Left+baseline.Right)/2,(baseline.Top+baseline.Bottom)/2))!=c.IndicatorHandle);
            for(int i=0;i<4;i++){c.Collapse();await Task.Delay(70);double p=c.DockProgress,v=c.DockVelocity,w=c.IndicatorLength,a=c.IndicatorAlpha;c.Expand();Check("reverse inherits same Dock and strip pose "+i,p==c.DockProgress&&v==c.DockVelocity&&w==c.IndicatorLength&&a==c.IndicatorAlpha);await Task.Delay(65);c.Collapse();await Task.Delay(45);c.Expand();await Task.Delay(450);Check("reverse finishes cleanly "+i,c.Dock.IsVisible&&!c.DockMotionRunning&&c.IndicatorAlpha==0);}
            Native.MovePointer(c.WorkArea.Left+100,c.WorkArea.Top+150);await Task.Delay(800);Check("Dock retract restores full strip without clocks",!c.Dock.IsVisible&&Math.Abs(c.IndicatorLength-full)<.01&&c.IndicatorAlpha==1&&!c.DockMotionRunning&&Anim.ActiveTimers==0);
            Native.MovePointer((baseline.Left+baseline.Right)/2,(baseline.Top+baseline.Bottom)/2);Native.TestButton(true);Native.TestButton(false);await Task.Delay(450);Check("unchanged strip center click wakes immediately",c.Dock.IsVisible&&c.IndicatorAlpha==0);var next=c.WakeBounds;Check("visual contraction never changes sensing bounds",sense.Left==next.Left&&sense.Right==next.Right&&sense.Top==next.Top&&sense.Bottom==next.Bottom);
            c.AutoHideDock=false;c.CheckFullscreen();await Task.Delay(250);
            if(c.DesktopResident){Native.MovePointer(c.WorkArea.Left+100,c.WorkArea.Top+150);await Task.Delay(500);Check("desktop-resident mode keeps Dock visible",c.Dock.IsVisible);}
            else{Check("maximized/fullscreen skips forced residency",true,"DesktopResident=false in current desktop environment");}
            c.AutoHideDock=true;c.CheckFullscreen();await Task.Delay(500);
        }catch(Exception e){ok=false;records.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{c.DockAnimationFrame=null;Native.TestButton(false);Native.MovePointer(original.X,original.Y);File.WriteAllText(Path.Combine(dir,"wake-strip.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));File.WriteAllText(Path.Combine(dir,"wake-strip-frames.json"),JsonSerializer.Serialize(frames,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown(ok?0:1);}
    }
}
