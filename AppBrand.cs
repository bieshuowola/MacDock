using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
namespace MacDock;

// The supplied multi-resolution ICO is shared by PE resources, WPF windows and the tray.
// Do not reuse the Windows four-pane drawer command glyph as the application's identity.
internal static class AppBrand
{
    static readonly Uri uri=new("pack://application:,,,/Assets/Brand/MacDock.ico",UriKind.Absolute);
    internal static readonly BitmapFrame Image=Load();
    static BitmapFrame Load(){using var stream=Application.GetResourceStream(uri).Stream;var frame=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames.OrderByDescending(f=>f.PixelWidth).First();frame.Freeze();return frame;}
    internal static System.Drawing.Icon Tray(){using var stream=Application.GetResourceStream(uri).Stream;using var full=new System.Drawing.Icon(stream);return new System.Drawing.Icon(full,System.Windows.Forms.SystemInformation.SmallIconSize);}
}
