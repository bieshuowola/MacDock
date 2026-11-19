using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using System.Runtime.InteropServices;
namespace MacDock;

// A common, immutable silhouette for live WPF content, the host backdrop and input.
// The top unfurls first; the lower edge stays tethered to the actual drawer button.
internal sealed record GeniePose(double Width,double Height,double FrameHeight,double AnchorX,double AnchorY,double Progress)
{
    [DllImport("gdi32.dll")]static extern nint CreatePolygonRgn([In]Native.Point[] points,int count,int fill);
    [DllImport("gdi32.dll")]static extern bool DeleteObject(nint obj);
    [DllImport("user32.dll")]static extern int SetWindowRgn(nint hwnd,nint region,bool redraw);
    [DllImport("gdi32.dll")]static extern nint CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
    internal static void SetRestRegion(nint hwnd,double radius,double scale)
    {
        if(hwnd==0)return;
        // The layered foreground already has rounded per-pixel alpha. A GDI round
        // region is binary and loses the final right/bottom pixel as well as partial
        // coverage along its arc. It must not be the visual antialiasing boundary.
        SetWindowRgn(hwnd,0,false);
    }
    [ThreadStatic] static Native.Point[]? inputRegionPoints;
    internal static void SetInputRegion(nint hwnd,GeniePose? pose,double scale)
    {
        if(hwnd==0)return;if(pose==null){SetWindowRgn(hwnd,0,false);return;}
        int count=(Rows+1)*2;
        var edge=inputRegionPoints;
        if(edge==null||edge.Length!=count)edge=inputRegionPoints=new Native.Point[count];
        for(int i=0;i<=Rows;i++){var r=pose.Row(RowT(i),true);int y=(int)Math.Round(r.Y*scale)+(i==0?-(int)(8*scale):i==Rows?(int)(14*scale):0);edge[i]=new Native.Point((int)Math.Floor((r.Left-14)*scale),y);edge[edge.Length-1-i]=new Native.Point((int)Math.Ceiling((r.Right+14)*scale),y);}
        var region=CreatePolygonRgn(edge,edge.Length,1);if(region!=0&&SetWindowRgn(hwnd,region,false)==0)DeleteObject(region);
    }
    internal const int Rows=64;
    // Concentrate tessellation at rounded ends. Uniform rows left an entire 26-DIP
    // corner represented by only one or two trapezoids on a tall desktop drawer.
    internal static double RowT(int row)=>.5-.5*Math.Cos(Math.PI*row/Rows);
    internal static double Smooth(double x){x=Math.Clamp(x,0,1);return x*x*(3-2*x);}
    internal (double Left,double Right,double Y,double Spread) Row(double t,bool rounded=false)
    {
        double p=Math.Clamp(Progress,0,1),spread=Smooth((p-.38*t)/(1-.38*t));
        double top=AnchorY*(1-Smooth(p/.72)),bottom=AnchorY+(Height-AnchorY)*Smooth((p-.16)/.84);
        double width=32+(Width-32)*spread,center=Width/2+(AnchorX-Width/2)*(1-spread);
        double radius=Math.Min(26,width*.24),edge=Math.Min(t,1-t)*Height;
        double inset=rounded&&edge<radius?radius-Math.Sqrt(Math.Max(0,radius*radius-(radius-edge)*(radius-edge))):0;
        return(center-width/2+inset,center+width/2-inset,top+Math.Max(.5,bottom-top)*t,spread);
    }
    internal bool Contains(double x,double y)
    {
        var top=Row(0);var bottom=Row(1);if(y<top.Y||y>bottom.Y)return false;
        double t=(y-top.Y)/Math.Max(.5,bottom.Y-top.Y);var r=Row(t,true);return x>=r.Left&&x<=r.Right;
    }
}

// The original live controls are hosted on a tessellated mesh (not a screenshot),
// including WPF's texture-to-control hit mapping. The mesh resources survive the
// return to the flat, pixel-aligned controls at rest.
internal sealed class GenieVisual
{
    const int Columns=8;
    readonly Viewport3D viewport;readonly Viewport2DVisual3D host;readonly MeshGeometry3D mesh;
    readonly double frame;
    SoftContour? shadow;
    GeniePose? previous;
    internal UIElement View=>viewport;
    internal GenieVisual(double width,double frame,bool shareShadow=false)
    {
        this.frame=frame;
        mesh=new MeshGeometry3D();var uv=new PointCollection();var indices=new Int32Collection();
        const int cols=Columns;
        for(int r=0;r<=GeniePose.Rows;r++)for(int c=0;c<=cols;c++)uv.Add(new Point((double)c/cols,GeniePose.RowT(r)));
        for(int r=0;r<GeniePose.Rows;r++)for(int c=0;c<cols;c++){int a=r*(cols+1)+c,b=a+cols+1;indices.Add(a);indices.Add(b);indices.Add(a+1);indices.Add(a+1);indices.Add(b);indices.Add(b+1);}
        uv.Freeze();indices.Freeze();mesh.TextureCoordinates=uv;mesh.TriangleIndices=indices;
        var material=new DiffuseMaterial(Brushes.White);Viewport2DVisual3D.SetIsVisualHostMaterial(material,true);
        // Supersample only the cached live foreground texture. At-rest rounded alpha
        // must survive the final 3D texture sampling on a transparent layered HWND.
        host=new Viewport2DVisual3D{Geometry=mesh,Material=material,CacheMode=new BitmapCache(2){SnapsToDevicePixels=false}};
        // Reserve the existing feather outside the sheet in the same render target.
        // Camera padding and the opposite margin preserve the original pixel/input map.
        double pad=shareShadow?SoftContour.Pad:0;
        viewport=new Viewport3D{Width=width+2*pad,Height=frame+2*pad,Margin=new Thickness(-pad),ClipToBounds=false,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Camera=new OrthographicCamera(new Point3D(width/2,frame/2,10),new Vector3D(0,0,-1),new Vector3D(0,1,0),width+2*pad)};
        viewport.Children.Add(new ModelVisual3D{Content=new AmbientLight(Colors.White)});viewport.Children.Add(host);
        RenderOptions.SetEdgeMode(viewport,EdgeMode.Unspecified);
    }
    internal void Set(GeniePose pose)
    {
        if(Equals(previous,pose))return;
        previous=pose;
        var points=new Point3DCollection((GeniePose.Rows+1)*(Columns+1));
        for(int r=0;r<=GeniePose.Rows;r++){var row=pose.Row(GeniePose.RowT(r));for(int col=0;col<=Columns;col++)points.Add(new Point3D(row.Left+(row.Right-row.Left)*col/Columns,frame-row.Y,0));}
        points.Freeze();mesh.Positions=points;
    }
    internal void Attach(FrameworkElement visual)
    {
        // Viewport2DVisual3D does not arrange its live texture like a Grid child.
        var size=new Size(visual.Width,visual.Height);
        visual.Measure(size);visual.Arrange(new Rect(size));
        host.Visual=visual;
    }
    internal void Detach()=>host.Visual=null;
    internal void AttachShadow(SoftContour contour)
    {
        if(shadow==contour)return;DetachShadow();contour.ReleaseProjection();
        // SoftContour's padded coordinates become this scene's sheet coordinates;
        // the four-DIP projection offset is already baked into the contour mesh.
        contour.Projection.Transform=new TranslateTransform3D(-SoftContour.Pad,-SoftContour.Pad,-.01);
        viewport.Children.Insert(1,contour.Projection);shadow=contour;
    }
    internal void DetachShadow()
    {
        if(shadow==null)return;viewport.Children.Remove(shadow.Projection);shadow.RestoreProjection();shadow=null;
    }
}

// Finite analytic spring, independent of the approved Dock motion. Retargets preserve
// position and velocity; callbacks may re-enter without an old completion hiding new UI.
internal sealed class DrawerMotion
{
    bool rendering;
    TimeSpan? lastRenderingTime;
    readonly Stopwatch clock=new();readonly Action<double> render;readonly Action<bool> completed;
    double last;long revision;bool paused,startOnRender;
    volatile bool inputPause,inputEnabled;
    internal bool InputPaused=>inputPause;
    internal void PauseFromHook(){if(inputEnabled)inputPause=true;}
    internal double Value{get;private set;}internal double Velocity{get;private set;}internal double Target{get;private set;}
    internal bool Running=>rendering||paused;
    internal DrawerMotion(Action<double> render,Action<bool> completed){this.render=render;this.completed=completed;}
    internal void Snap(double p){GestureChecks.Trace?.Invoke("drawer-snap",new{p,oldTarget=Target,phase=Value});revision++;Stop();paused=false;inputPause=false;Value=Target=p;Velocity=0;render(p);}
    internal void To(bool open,bool startOnRender=false){GestureChecks.Trace?.Invoke("drawer-target",new{open,phase=Value,velocity=Velocity});Target=open?1:0;revision++;paused=false;inputPause=false;if(Math.Abs(Value-Target)<.0001&&Math.Abs(Velocity)<.002){Finish(revision);return;}Start(startOnRender);}
    void Start(bool onRender=false)
    {
        if (rendering) return;
        inputEnabled = true;
        clock.Restart();
        startOnRender = onRender;
        last = 0;
        lastRenderingTime = null;
        rendering = true;
        CompositionTarget.Rendering += RenderFrame;
        Anim.ActiveTimers++;
    }
    void RenderFrame(object? sender, EventArgs e)
    {
        if (!rendering || e is not RenderingEventArgs frame || frame.RenderingTime == lastRenderingTime) return;
        lastRenderingTime = frame.RenderingTime;
        if(startOnRender)
        {
            // Advance the first presented pose by at most one 120 Hz frame. A slow
            // first callback still cannot spend the whole wait in a single spring step.
            last=-Math.Min(clock.Elapsed.TotalSeconds,1.0/120);
            clock.Restart();
            startOnRender=false;
        }
        Tick();
    }
    internal void Pause(){if(!rendering)return;Stop();paused=true;}
    internal void Resume(){inputPause=false;if(!paused)return;paused=false;Start();}
    void Tick(){if(inputPause){Pause();return;}long request=revision;double dt=Math.Clamp(clock.Elapsed.TotalSeconds-last,0,.04);last=clock.Elapsed.TotalSeconds;double w=Target==1?30:38,x=Value-Target,b=Velocity+w*x,e=Math.Exp(-w*dt);Value=Target+e*(x+b*dt);Velocity=e*(Velocity-w*b*dt);render(Value);if(request!=revision)return;if(!Anim.Allowed||Math.Abs(Value-Target)<.002&&Math.Abs(Velocity)<.065)Finish(request);}
    void Finish(long request){if(request!=revision)return;Stop();paused=false;Value=Target;Velocity=0;render(Value);if(request==revision)completed(Target==1);}
    void Stop()
    {
        inputEnabled = false;
        if (!rendering) return;
        rendering = false;
        CompositionTarget.Rendering -= RenderFrame;
        clock.Stop();
        Anim.ActiveTimers--;
    }
    internal void Cancel(){revision++;Stop();paused=false;inputPause=false;}
}
