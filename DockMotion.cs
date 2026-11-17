using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
namespace MacDock;

// One finite spring, retargeted in place: position AND velocity survive reversals.
// Only the material silhouette folds. Icon hit slots and icon aspect ratios stay fixed.
internal sealed class DockMotion
{
    bool rendering;
    TimeSpan? lastRenderingTime;
    readonly Action<double> render;readonly Action<bool> completed;
    readonly Stopwatch clock=new();double last;long revision;
    internal double Value {get;private set;}
    internal double Velocity {get;private set;}
    internal double Target {get;private set;}
    internal bool Running=>rendering;
    internal DockMotion(Action<double> render,Action<bool> completed){this.render=render;this.completed=completed;}
    internal void Snap(double value){revision++;Stop();Value=Target=value;Velocity=0;render(value);}
    internal void To(bool open){double target=open?1:0;if(Running&&Target==target)return;Target=target;long request=++revision;if(Math.Abs(Value-target)<.0001&&Math.Abs(Velocity)<.003){Finish(request,target);return;}if(!rendering){clock.Restart();last=0;lastRenderingTime=null;rendering=true;CompositionTarget.Rendering+=RenderFrame;Anim.ActiveTimers++;}}
    void RenderFrame(object? sender,EventArgs e){if(!rendering||e is not RenderingEventArgs frame||frame.RenderingTime==lastRenderingTime)return;lastRenderingTime=frame.RenderingTime;Tick();}
    void Tick(){if(!rendering)return;long request=revision;double target=Target;if(!Anim.Allowed){Finish(request,target);return;}double now=clock.Elapsed.TotalSeconds,dt=Math.Clamp(now-last,0,.04);last=now;
        // Analytic damped-spring step; no accumulated integration error at varying frame rates.
        double omega=target==1?29:35,zeta=target==1?.82:1,a=zeta*omega,x=Value-target,v=Velocity,e=Math.Exp(-a*dt);
        if(zeta<1){double b=omega*Math.Sqrt(1-zeta*zeta),s=Math.Sin(b*dt),c=Math.Cos(b*dt);Value=target+e*(x*c+(v+a*x)/b*s);Velocity=e*(v*c-(a*v+omega*omega*x)/b*s);}
        else{double b=v+a*x;Value=target+e*(x+b*dt);Velocity=e*(v-a*b*dt);}
        render(Value);if(request!=revision)return;bool settled=Math.Abs(Value-target)<.0015&&Math.Abs(Velocity)<.035;
        // End imperceptible tails promptly; the opening peak is only ~1%, below one DIP
        // of icon lift, and the closing surface is already below 1.2% opacity.
        settled|=target==1?Value>1&&Math.Abs(Velocity)<.12:Value<.008&&Math.Abs(Velocity)<.5;
        if(settled)Finish(request,target);
    }
    void Finish(long request,double target){if(request!=revision)return;Stop();Value=target;Velocity=0;render(target);if(request==revision)completed(target==1);}
    void Stop(){if(!rendering)return;rendering=false;CompositionTarget.Rendering-=RenderFrame;clock.Stop();Anim.ActiveTimers--;}
    internal void Cancel(){revision++;Stop();}
    internal static (Rect Bounds,double Radius) Shape(double width,double height,double radius,double phase){double t=Math.Clamp(phase,0,1),fold=1-t;double w=width-Math.Min(width*.09,height*1.15)*fold*fold;double h=height*(.40+.60*t);return(new Rect((width-w)/2,height-h,Math.Max(0,w),Math.Max(0,h)),Math.Min(h/2,radius*(.68+.32*Math.Sqrt(t))));}
}
