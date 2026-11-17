using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace MacDock;

// Only the four small corner patches need a coverage texture. The live material
// and controls remain vector-rendered; no full-window offscreen mask or blur.
internal sealed class CoverageBorder:Border
{
    readonly Dictionary<(int,double),BitmapSource> masks=new();
    double sampleScale=1;
    internal void SampleScale(double value){if(value==sampleScale)return;sampleScale=value;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        var radius=CornerRadius.TopLeft;
        if(radius<=0||Background is not (LinearGradientBrush or SolidColorBrush)||BorderThickness!=new Thickness(0))
        {base.OnRender(dc);return;}
        double scale=VisualTreeHelper.GetDpi(this).DpiScaleX*sampleScale;
        double r=Math.Min(radius,Math.Min(RenderSize.Width,RenderSize.Height)/2);
        int pixels=(int)Math.Ceiling(r*scale);double patch=pixels/scale;
        if(RenderSize.Width<2*patch||RenderSize.Height<2*patch){base.OnRender(dc);return;}
        var key=(pixels,r*scale);
        if(!masks.TryGetValue(key,out var mask))
        {
            if(masks.Count>=64)masks.Clear();var data=new byte[pixels*pixels*4];const int samples=16;
            for(int y=0;y<pixels;y++)for(int x=0;x<pixels;x++)
            {
                int covered=0;double rr=r*scale,farX=Math.Max(0,rr-x),farY=Math.Max(0,rr-y),nearX=Math.Max(0,rr-x-1),nearY=Math.Max(0,rr-y-1);
                if(farX*farX+farY*farY<=rr*rr)covered=samples*samples;
                else if(nearX*nearX+nearY*nearY<rr*rr)
                    for(int sy=0;sy<samples;sy++)for(int sx=0;sx<samples;sx++)
                    {double dx=Math.Max(0,rr-x-(sx+.5)/samples),dy=Math.Max(0,rr-y-(sy+.5)/samples);if(dx*dx+dy*dy<=rr*rr)covered++;}
                byte a=(byte)((covered*255+samples*samples/2)/(samples*samples));int offset=(y*pixels+x)*4;
                data[offset]=data[offset+1]=data[offset+2]=data[offset+3]=a;
            }
            mask=BitmapSource.Create(pixels,pixels,96*scale,96*scale,PixelFormats.Pbgra32,null,data,pixels*4);mask.Freeze();masks[key]=mask;
        }
        Brush brush=Background.CloneCurrentValue();
        if(brush is LinearGradientBrush linear&&linear.MappingMode==BrushMappingMode.RelativeToBoundingBox)
        {
            linear.StartPoint=new Point(linear.StartPoint.X*RenderSize.Width,linear.StartPoint.Y*RenderSize.Height);
            linear.EndPoint=new Point(linear.EndPoint.X*RenderSize.Width,linear.EndPoint.Y*RenderSize.Height);linear.MappingMode=BrushMappingMode.Absolute;
        }
        brush.Freeze();double w=RenderSize.Width,h=RenderSize.Height;
        dc.DrawRectangle(brush,null,new Rect(patch,0,w-2*patch,h));
        dc.DrawRectangle(brush,null,new Rect(0,patch,patch,h-2*patch));
        dc.DrawRectangle(brush,null,new Rect(w-patch,patch,patch,h-2*patch));
        for(int k=0;k<4;k++)
        {
            var rect=new Rect(k%2==0?0:w-patch,k<2?0:h-patch,patch,patch);
            var image=new ImageBrush(mask){ViewportUnits=BrushMappingMode.Absolute,Viewport=rect,Stretch=Stretch.Fill};
            var transform=new ScaleTransform(k%2==0?1:-1,k<2?1:-1,.5,.5);transform.Freeze();image.RelativeTransform=transform;image.Freeze();
            dc.PushOpacityMask(image);dc.DrawRectangle(brush,null,rect);dc.Pop();
        }
    }
}
