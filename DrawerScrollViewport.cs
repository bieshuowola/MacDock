using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MacDock;

// Keep the live tile tree arranged in content coordinates. Moving through it
// only changes the render transform; the ScrollViewer still owns keyboard,
// thumb, automation and BringIntoView commands in device-independent pixels.
internal sealed class DrawerScrollViewport : Decorator, IScrollInfo
{
    readonly TranslateTransform translation=new();
    Size extent,viewport;
    double offset;

    internal DrawerScrollViewport(UIElement content)
    {
        Child=content;
        content.RenderTransform=translation;
        ClipToBounds=true;
    }

    public ScrollViewer? ScrollOwner { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth=>extent.Width;
    public double ExtentHeight=>extent.Height;
    public double ViewportWidth=>viewport.Width;
    public double ViewportHeight=>viewport.Height;
    public double HorizontalOffset=>0;
    public double VerticalOffset=>offset;

    protected override Size MeasureOverride(Size available)
    {
        Child.Measure(new Size(available.Width,double.PositiveInfinity));
        var desired=Child.DesiredSize;
        return new Size(Math.Min(available.Width,desired.Width),Math.Min(available.Height,desired.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var next=new Size(finalSize.Width,Math.Max(finalSize.Height,Child.DesiredSize.Height));
        bool changed=viewport!=finalSize||extent!=next;
        extent=next;
        viewport=finalSize;
        Child.Arrange(new Rect(extent));
        double prior=offset;
        SetVerticalOffset(offset);
        if(changed&&offset==prior)ScrollOwner?.InvalidateScrollInfo();
        return finalSize;
    }

    public void SetHorizontalOffset(double value) { }
    public void SetVerticalOffset(double value)
    {
        if(double.IsNaN(value))return;
        double next=Math.Clamp(value,0,Math.Max(0,ExtentHeight-ViewportHeight));
        if(Math.Abs(next-offset)<.001)return;
        offset=next;
        translation.Y=-offset;
        ScrollOwner?.InvalidateScrollInfo();
    }

    public Rect MakeVisible(Visual visual,Rect rectangle)
    {
        if(!IsAncestorOf(visual))return Rect.Empty;
        // BringIntoView() defers computing its rectangle until layout has run.
        if(rectangle.IsEmpty)
        {
            if(visual is not UIElement element)return Rect.Empty;
            rectangle=new Rect(element.RenderSize);
        }
        var bounds=visual.TransformToAncestor(this).TransformBounds(rectangle);
        double prior=offset;
        if(bounds.Top<0)SetVerticalOffset(offset+bounds.Top);
        else if(bounds.Bottom>ViewportHeight)SetVerticalOffset(offset+Math.Min(bounds.Top,bounds.Bottom-ViewportHeight));
        bounds.Offset(0,prior-offset);
        bounds.Intersect(new Rect(viewport));
        return bounds;
    }

    public void LineUp()=>SetVerticalOffset(offset-16);
    public void LineDown()=>SetVerticalOffset(offset+16);
    public void PageUp()=>SetVerticalOffset(offset-ViewportHeight);
    public void PageDown()=>SetVerticalOffset(offset+ViewportHeight);
    public void MouseWheelUp()=>SetVerticalOffset(offset-48);
    public void MouseWheelDown()=>SetVerticalOffset(offset+48);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
}
