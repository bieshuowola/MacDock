using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
namespace MacDock;
internal static class BrandChecks
{
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(output);var records=new List<object>();
        void Check(string name,bool pass,object? detail=null){records.Add(new{name,pass,detail});if(!pass)throw new Exception(name);}
        var preview=new Window{Title="MacDock 正式图标验收",Icon=AppBrand.Image,Width=400,Height=220,Left=100,Top=120,Content=new System.Windows.Controls.Image{Source=AppBrand.Image,Width=96,Height=96},ShowInTaskbar=true};Native.GetCursorPos(out var prior);var previous=Native.GetForegroundWindow();
        try{
            Check("WPF uses highest supplied ICO frame",AppBrand.Image.PixelWidth==256&&AppBrand.Image.PixelHeight==256);
            using var tray=AppBrand.Tray();using var bitmap=tray.ToBitmap();bitmap.Save(Path.Combine(output,"actual-tray-resource.png"));Check("tray uses supplied small-size frame",tray.Width==System.Windows.Forms.SystemInformation.SmallIconSize.Width,new{tray.Width,tray.Height});
            using var pe=System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);using var peBitmap=pe!.ToBitmap();peBitmap.Save(Path.Combine(output,"actual-exe-shell-icon.png"));Check("native Shell extracts a non-generic blue brand icon from EXE",Blue(peBitmap)>.15,Blue(peBitmap));
            c.Busy=true;c.ToggleDrawer();await Task.Delay(550);Check("live curved source retains WPF Loaded and logical parent",c.Drawer!.Surface!.IsLoaded&&LogicalTreeHelper.GetParent(c.Drawer.Surface)==c.Drawer);Check("Dock and drawer have same supplied window image",ReferenceEquals(c.Dock.Icon,AppBrand.Image)&&ReferenceEquals(c.Drawer!.Icon,AppBrand.Image));Check("utility surfaces remain excluded from Alt-Tab/taskbar",!c.Dock.ShowInTaskbar&&!c.Drawer!.ShowInTaskbar);
            preview.Show();Native.SetForegroundWindow(new WindowInteropHelper(preview).Handle);await Task.Delay(500);var h=new WindowInteropHelper(preview).Handle;
            Check("visible taskbar test window has native big and small icons",Native.SendMessage(h,0x7F,1,0)!=0&&Native.SendMessage(h,0x7F,0,0)!=0);
            Capture(output,"taskbar-and-window.png");Native.TestKey(0x12,true);Native.TestKey(9,true);Native.TestKey(9,false);await Task.Delay(500);Capture(output,"actual-alt-tab.png");Native.TestKey(27,true);Native.TestKey(27,false);Native.TestKey(0x12,false);await Task.Delay(150);
            Check("temporary ordinary window can be switched without changing product window styles",preview.ShowInTaskbar&&!c.Dock.ShowInTaskbar);
        }catch(Exception e){records.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{Native.TestKey(0x12,false);preview.Close();Native.MovePointer(prior.X,prior.Y);Native.SetForegroundWindow(previous);File.WriteAllText(Path.Combine(output,"brand-test.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
    static double Blue(System.Drawing.Bitmap image){int n=0,all=0;for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++){var c=image.GetPixel(x,y);if(c.A<100)continue;all++;if(c.B>c.R*1.15&&c.B>100)n++;}return (double)n/Math.Max(1,all);}
    static void Capture(string output,string file){var bounds=System.Windows.Forms.SystemInformation.VirtualScreen;using var b=new System.Drawing.Bitmap(bounds.Width,bounds.Height);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(bounds.Left,bounds.Top,0,0,b.Size);b.Save(Path.Combine(output,file));}
}
