using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock;

// One measured shell icon: the bitmap plus the bounds of its real (non-transparent) pixels.
// That lets every tile place the artwork at the same optical size instead of scaling raw
// bitmaps whose transparent padding differs wildly between vendors.
internal sealed class IconEntry
{
    public ImageSource? Image { get; internal set; }
    public Int32Rect Content { get; internal set; }
    public bool Filled { get; internal set; }
    internal bool LightGlyph;
    internal string Stamp="";
    internal bool Loading;
    internal int Revision;
    internal int RetryCount;
    internal string Fingerprint="";
    ImageSource? artwork;
    internal ImageSource? Artwork
    {
        get{if(artwork!=null)return artwork;artwork=Image;if(Image is BitmapSource bitmap&&Content.Width>0&&Content.Height>0){var crop=new CroppedBitmap(bitmap,Content);crop.Freeze();artwork=crop;}return artwork;}
    }
    internal event EventHandler? Changed;
    internal void Update(IconEntry next,string stamp)
    {
        if(next.Missing)
        {
            GestureChecks.Trace?.Invoke("icon-update",new{slot=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this),same=false,missing=true,preserved=Image!=null,oldStamp=Stamp,stamp,preserve=Icons.PreserveVisuals});
            Loading=false;
            Stamp="";
            return;
        }
        RetryCount=0;
        bool same=(ReferenceEquals(Image,next.Image)||Fingerprint.Length>0&&Fingerprint==next.Fingerprint)&&Content==next.Content&&Filled==next.Filled&&LightGlyph==next.LightGlyph;
        GestureChecks.Trace?.Invoke("icon-update",new{slot=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this),same,oldStamp=Stamp,stamp,preserve=Icons.PreserveVisuals});
        Stamp=stamp;Loading=false;
        if(Icons.PreserveVisuals&&same)return;
        Image=next.Image;Content=next.Content;Filled=next.Filled;LightGlyph=next.LightGlyph;Fingerprint=next.Fingerprint;artwork=null;Changed?.Invoke(this,EventArgs.Empty);
    }
    public bool Missing => Image == null;
    public static readonly IconEntry Empty = new();
}

internal static class Icons
{
    internal static bool PreserveVisuals=true;
    internal static bool IsJumboPlaceholder(BitmapSource bitmap)
    {
        if(bitmap.PixelWidth<64||bitmap.Format.BitsPerPixel!=32)return false;
        var width=bitmap.PixelWidth;var height=bitmap.PixelHeight;var data=new byte[width*height*4];bitmap.CopyPixels(data,width*4,0);
        int border=0,body=0;var inset=Math.Max(3,width/20);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int i=(y*width+x)*4;if(data[i+3]<24)continue;
            bool edge=x<inset||x>=width-inset||y<inset||y>=height-inset;
            if(edge&&Math.Max(data[i],Math.Max(data[i+1],data[i+2]))-Math.Min(data[i],Math.Min(data[i+1],data[i+2]))<20)border++;
            else if(!edge)body++;
        }
        return border>width*2&&body<width*height*.16;
    }
    public static IconEntry Measure(ImageSource? image)
    {
        if (image is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) return new IconEntry { Image = image };
        if (bitmap.Format.BitsPerPixel != 32) return new IconEntry { Image = image, Content = new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight) };
        var width = bitmap.PixelWidth; var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        try { bitmap.CopyPixels(pixels, stride, 0); }
        catch { return new IconEntry { Image = image, Content = new Int32Rect(0, 0, width, height) }; }

        int minX = width, minY = height, maxX = -1, maxY = -1,opaque=0,light=0;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < width; x++)
            {
                if (pixels[row + x * 4 + 3] <= 8) continue;
                if(pixels[row+x*4+3]>192){opaque++;if(pixels[row+x*4]>225&&pixels[row+x*4+1]>225&&pixels[row+x*4+2]>225)light++;}
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        bool hasAlpha=bitmap.Format==PixelFormats.Bgra32||bitmap.Format==PixelFormats.Pbgra32;
        if(hasAlpha)for(int i=0;i<pixels.Length;i+=4)if(pixels[i+3]==0)pixels[i]=pixels[i+1]=pixels[i+2]=0;
        bool comparable=bitmap.IsFrozen&&(hasAlpha||bitmap.Format==PixelFormats.Bgr32)&&!(bitmap is BitmapFrame frame&&frame.ColorContexts is {Count:>0});
        string fingerprint=comparable?FormattableString.Invariant($"{width}/{height}/{bitmap.DpiX:R}/{bitmap.DpiY:R}/{bitmap.Format}/{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels))}"):"";
        if (maxX < minX || maxY < minY) return new IconEntry();

        // Keep the original silhouette. Bounds are measured for optical fitting, never cropping.
        var w = maxX - minX + 1; var h = maxY - minY + 1;
        var filled = w >= width * .94 && h >= height * .94;
        return new IconEntry { Image = image, Content = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1), Filled = filled,LightGlyph=opaque>0&&light>opaque*.80&&opaque<width*height*.75,Fingerprint=fingerprint };
    }

    /// <summary>
    /// Uniform optical fitting, preserving the complete original artwork and aspect ratio.
    /// </summary>
    public static FrameworkElement View(IconEntry icon, double box, bool lightGlyphShadow = true)
    {
        var host = new Grid { Width = box, Height = box, IsHitTestVisible = false, ClipToBounds = false };
        bool rendered=false,dark=false,light=false,filled=false;ImageSource? last=null;Int32Rect content=default;
        void Render(){
            if(PreserveVisuals&&rendered&&ReferenceEquals(last,icon.Image)&&content==icon.Content&&light==icon.LightGlyph&&filled==icon.Filled&&dark==Theme.Dark)return;
            GestureChecks.Trace?.Invoke("icon-view-render",new{slot=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(icon),box,loaded=host.IsLoaded,preserve=PreserveVisuals});
            host.Children.Clear();RenderInto(host,icon,box,lightGlyphShadow);rendered=true;last=icon.Image;content=icon.Content;light=icon.LightGlyph;filled=icon.Filled;dark=Theme.Dark;
        }
        EventHandler update=(_,_)=>Render();
        host.Loaded+=(_,_)=>{icon.Changed-=update;icon.Changed+=update;Theme.Changed-=Render;Theme.Changed+=Render;Render();};host.Unloaded+=(_,_)=>{icon.Changed-=update;Theme.Changed-=Render;};
        Render();return host;
    }
    static void RenderInto(Grid host,IconEntry icon,double box,bool lightGlyphShadow)
    {
        if (icon.Image == null)
        {
            host.Children.Add(new Border
            {
                Width = box * .74, Height = box * .74, CornerRadius = new CornerRadius(box * .2),
                Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            });
            return;
        }
        ImageSource artwork=PreserveVisuals?icon.Artwork!:icon.Image;
        // Remove only measured transparent padding before WPF layout. An oversized original
        // canvas arranged with negative offsets was rounded/clipped inconsistently at fractional DPI.
        if(!PreserveVisuals&&icon.Image is BitmapSource bitmap&&icon.Content.Width>0&&icon.Content.Height>0)
        {var cropped=new CroppedBitmap(bitmap,icon.Content);cropped.Freeze();artwork=cropped;}
        var image = new Image { Source = artwork, Stretch = Stretch.Uniform, IsHitTestVisible = false, SnapsToDevicePixels = false };
        if(lightGlyphShadow&&icon.LightGlyph&&!Theme.Dark)image.Effect=new System.Windows.Media.Effects.DropShadowEffect{Color=Colors.Black,BlurRadius=4,ShadowDepth=0,Opacity=.45};
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(image, EdgeMode.Unspecified);
        // No synthetic plate or frame around valid artwork. Fit actual visible pixels while
        // retaining aspect ratio and every original corner, including legitimate brand borders.
        var nativeWidth=icon.Image is BitmapSource bm?bm.PixelWidth:icon.Image.Width;
        var nativeHeight=icon.Image is BitmapSource bn?bn.PixelHeight:icon.Image.Height;
        var content = icon.Content.Width > 0 ? icon.Content : new Int32Rect(0,0,Math.Max(1,(int)nativeWidth),Math.Max(1,(int)nativeHeight));
        var scale = box * .90 / Math.Max(1, Math.Max(content.Width, content.Height));
        var w = content.Width * scale;
        var h = content.Height * scale;
        image.Width = w; image.Height = h;
        image.HorizontalAlignment = HorizontalAlignment.Center;
        image.VerticalAlignment = VerticalAlignment.Center;
        // Centre the visible pixels, not the bitmap canvas, so asymmetric padding cannot push
        // a glyph off-centre.
        host.Children.Add(image);
    }
}
