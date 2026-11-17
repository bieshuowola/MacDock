using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Runtime.InteropServices;
namespace MacDock;

// Dock projection; the drawer uses the same contour in its own foreground scene.
internal sealed class RestShadow:GlassWindow
{
    const double Pad=SoftContour.Pad;
    readonly SoftContour canvas=new();
    double laidOutWidth,laidOutHeight;
    [DllImport("user32.dll")]static extern nint GetWindow(nint h,uint command);
    internal RestShadow():base(material:false)
    {
        Title="MacDock rounded glass shadow";ShowActivated=false;IsHitTestVisible=false;Focusable=false;
        KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.None);Content=canvas;
        SourceInitialized+=(_,_)=>HwndSource.FromHwnd(Handle).AddHook((nint h,int m,nint w,nint l,ref bool handled)=>{if(m==0x84){handled=true;return -1;}return 0;});
    }
    internal void Shape(double width,double height,double radius,Rect sheet,GeniePose? pose,double alpha){Width=width+2*Pad;Height=height+2*Pad;canvas.Shape(width,height,radius,sheet,pose,alpha);}
    internal void Sync(nint below,Native.Rect bounds,double dpi,bool visible)
    {
        if(!visible){if(IsVisible)Hide();return;}
        bool justShown=!IsVisible;
        if(justShown){Show();Native.SetWindowLongPtr(Handle,-20,(nint)(Native.GetWindowLongPtr(Handle,-20).ToInt64()|0x20));}
        int x=bounds.Left-(int)Math.Round(Pad*dpi),y=bounds.Top-(int)Math.Round(Pad*dpi),w=(int)Math.Round(Width*dpi),h=(int)Math.Round(Height*dpi);
        if(!Native.GetWindowRect(Handle,out var old)||old.Left!=x||old.Top!=y||old.Right-old.Left!=w||old.Bottom-old.Top!=h||GetWindow(below,2)!=Handle)
            Native.SetWindowPos(Handle,below,x,y,w,h,0x10);
        if(justShown||laidOutWidth!=Width||laidOutHeight!=Height){laidOutWidth=Width;laidOutHeight=Height;canvas.UpdateLayout();}
    }
}

internal sealed class SoftContour:FrameworkElement
{
    internal const double Pad=28;
    readonly System.Windows.Media.Media3D.MeshGeometry3D mesh=new();
    readonly System.Windows.Controls.Viewport3D viewport=new(){IsHitTestVisible=false,ClipToBounds=false};
    readonly ImageBrush profile;
    readonly System.Windows.Media.Media3D.OrthographicCamera camera=new();
    internal readonly System.Windows.Media.Media3D.ModelVisual3D Projection;
    int topologyCount;
    double panelWidth,panelHeight,curve;Rect? priorSheet;GeniePose? priorPose;
    internal SoftContour()
    {
        IsHitTestVisible=false;Focusable=false;
        double Cdf(double x){double sign=x<0?-1:1;x=Math.Abs(x)/Math.Sqrt(2);double t=1/(1+.3275911*x);double erf=1-(((((1.061405429*t-1.453152027)*t)+1.421413741)*t-.284496736)*t+.254829592)*t*Math.Exp(-x*x);return .5*(1+sign*erf);}
        var pixels=new byte[2048*4];for(int i=0;i<2048;i++){double d=(i/2047.0*2-1)*20;pixels[i*4+3]=(byte)Math.Round(255*(Cdf((d+6)/(14.0/3))-Cdf((d-6)/(14.0/3))));}pixels[3]=pixels[^1]=0;
        var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(2048,1,96,96,PixelFormats.Pbgra32,null,pixels,2048*4);bitmap.Freeze();profile=new ImageBrush(bitmap){Stretch=Stretch.Fill,Opacity=.24};
        var material=new System.Windows.Media.Media3D.DiffuseMaterial(profile);
        viewport.Children.Add(new System.Windows.Media.Media3D.ModelVisual3D{Content=new System.Windows.Media.Media3D.AmbientLight(Colors.White)});
        Projection=new System.Windows.Media.Media3D.ModelVisual3D{Content=new System.Windows.Media.Media3D.GeometryModel3D(mesh,material){BackMaterial=material}};
        viewport.Children.Add(Projection);
        viewport.Camera=camera;AddVisualChild(viewport);AddLogicalChild(viewport);
    }
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?viewport:throw new ArgumentOutOfRangeException(nameof(index));
    internal void ReleaseProjection()=>viewport.Children.Remove(Projection);
    internal void RestoreProjection(){Projection.Transform=System.Windows.Media.Media3D.Transform3D.Identity;if(!viewport.Children.Contains(Projection))viewport.Children.Add(Projection);}
    protected override Size MeasureOverride(Size available){var s=new Size(Width,Height);viewport.Measure(s);return s;}
    protected override Size ArrangeOverride(Size final){viewport.Arrange(new Rect(final));return final;}
    internal void Shape(double width,double height,double radius,Rect sheet,GeniePose? pose,double alpha)
    {
        profile.Opacity=.24*Math.Clamp(alpha,0,1);
        if(pose is {Progress:>=1})pose=null;
        if(width==panelWidth&&height==panelHeight&&radius==curve&&sheet==priorSheet&&Equals(pose,priorPose))return;
        panelWidth=width;panelHeight=height;curve=radius;priorSheet=sheet;priorPose=pose;
        Width=width+2*Pad;Height=height+2*Pad;
        if(camera.Width!=Width||camera.Position.Y!=Height/2){camera.Position=new System.Windows.Media.Media3D.Point3D(Width/2,Height/2,10);camera.LookDirection=new System.Windows.Media.Media3D.Vector3D(0,0,-1);camera.UpDirection=new System.Windows.Media.Media3D.Vector3D(0,1,0);camera.Width=Width;}
        List<Point> points;
        if(pose==null){
            var r=sheet;r.Offset(Pad,Pad);
            double rx=Math.Clamp(radius,0,Math.Min(r.Width,r.Height)/2);
            const int arcSteps=24;
            points=new List<Point>(arcSteps*4+4);
            if(rx<=0.01){
                points.Add(new Point(r.Left,r.Top));points.Add(new Point(r.Right,r.Top));
                points.Add(new Point(r.Right,r.Bottom));points.Add(new Point(r.Left,r.Bottom));
            }else{
                double cx0=r.Left+rx,cy0=r.Top+rx,cx1=r.Right-rx,cy1=r.Bottom-rx;
                for(int i=0;i<=arcSteps;i++){double a=Math.PI+i*(Math.PI/2)/arcSteps;points.Add(new Point(cx0+rx*Math.Cos(a),cy0+rx*Math.Sin(a)));}
                for(int i=0;i<=arcSteps;i++){double a=-Math.PI/2+i*(Math.PI/2)/arcSteps;points.Add(new Point(cx1+rx*Math.Cos(a),cy0+rx*Math.Sin(a)));}
                for(int i=0;i<=arcSteps;i++){double a=i*(Math.PI/2)/arcSteps;points.Add(new Point(cx1+rx*Math.Cos(a),cy1+rx*Math.Sin(a)));}
                for(int i=0;i<=arcSteps;i++){double a=Math.PI/2+i*(Math.PI/2)/arcSteps;points.Add(new Point(cx0+rx*Math.Cos(a),cy1+rx*Math.Sin(a)));}
            }
        }
        else{
            points=new List<Point>((GeniePose.Rows+1)*2);
            for(int i=0;i<=GeniePose.Rows;i++){var row=pose.Row(GeniePose.RowT(i),true);points.Add(new Point(row.Left+Pad,row.Y+Pad));}
            for(int i=GeniePose.Rows;i>=0;i--){var row=pose.Row(GeniePose.RowT(i),true);points.Add(new Point(row.Right+Pad,row.Y+Pad));}
        }
        for(int i=points.Count-1;i>0;i--)if((points[i]-points[i-1]).Length<.001)points.RemoveAt(i);
        if(points.Count>2&&(points[0]-points[^1]).Length<.001)points.RemoveAt(points.Count-1);
        if(points.Count<3){mesh.TriangleIndices=new Int32Collection();mesh.Positions=new System.Windows.Media.Media3D.Point3DCollection();topologyCount=0;return;}
        var positions=new System.Windows.Media.Media3D.Point3DCollection();var uv=new PointCollection();var indices=new Int32Collection();
        for(int i=0;i<points.Count;i++){
            var before=points[i]-points[(i+points.Count-1)%points.Count];var after=points[(i+1)%points.Count]-points[i];before.Normalize();after.Normalize();var n1=new Vector(-before.Y,before.X);var n2=new Vector(-after.Y,after.X);var normal=n1+n2;if(normal.Length<.001)normal=n2;normal.Normalize();double length=20/Math.Max(.5,Vector.Multiply(normal,n2));
            for(int side=0;side<2;side++){var p=points[i]+normal*(side==0?-length:length);positions.Add(new System.Windows.Media.Media3D.Point3D(p.X,Height-p.Y,0));uv.Add(new Point(side,(double)i/(points.Count-1)));}
            int a=i*2,b=((i+1)%points.Count)*2;indices.Add(a);indices.Add(b);indices.Add(a+1);indices.Add(a+1);indices.Add(b);indices.Add(b+1);
        }
        positions.Freeze();uv.Freeze();indices.Freeze();mesh.Positions=positions;if(topologyCount!=points.Count){topologyCount=points.Count;mesh.TextureCoordinates=uv;mesh.TriangleIndices=indices;}
    }
}
internal sealed class LegacyDockContour:FrameworkElement
{
    internal const double Pad=28;
    static readonly Pen stroke=CreateStroke();
    static Pen CreateStroke(){var pen=new Pen(Brushes.Black,12);pen.Freeze();return pen;}
    readonly DrawingVisual outline=new(){Opacity=.24,Effect=new BlurEffect{Radius=14,RenderingBias=RenderingBias.Quality}};
    double panelWidth,panelHeight,curve;Rect? priorSheet;GeniePose? priorPose;
    internal LegacyDockContour(){IsHitTestVisible=false;Focusable=false;AddVisualChild(outline);}
    protected override int VisualChildrenCount=>1;
    protected override Visual GetVisualChild(int index)=>index==0?outline:throw new ArgumentOutOfRangeException(nameof(index));
    internal void Shape(double width,double height,double radius,Rect sheet,GeniePose? pose,double alpha)
    {
        outline.Opacity=.24*Math.Clamp(alpha,0,1);
        if(pose is {Progress:>=1})pose=null;
        if(width==panelWidth&&height==panelHeight&&radius==curve&&sheet==priorSheet&&Equals(pose,priorPose))return;
        panelWidth=width;panelHeight=height;curve=radius;priorSheet=sheet;priorPose=pose;
        Width=width+2*Pad;Height=height+2*Pad;
        Geometry geometry;
        if(pose==null){var r=sheet;r.Offset(Pad,Pad+4);geometry=new RectangleGeometry(r,radius,radius);}
        else{
            var path=new StreamGeometry();using(var g=path.Open()){
                var first=pose.Row(0,true);g.BeginFigure(new Point(first.Left+Pad,first.Y+Pad+4),false,true);
                for(int i=1;i<=GeniePose.Rows;i++){var row=pose.Row(GeniePose.RowT(i),true);g.LineTo(new Point(row.Left+Pad,row.Y+Pad+4),true,false);}
                for(int i=GeniePose.Rows;i>=0;i--){var row=pose.Row(GeniePose.RowT(i),true);g.LineTo(new Point(row.Right+Pad,row.Y+Pad+4),true,false);}
            }path.Freeze();geometry=path;
        }
        using var drawing=outline.RenderOpen();
        drawing.DrawGeometry(null,stroke,geometry);
    }
}
