using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock;

internal static class IconTests
{
    static readonly List<object> results=new();
    static void Check(string name,bool pass,string detail=""){results.Add(new{name,pass,detail});if(!pass)throw new Exception(name+": "+detail);}
    internal static int Run()
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")??Path.Combine(AppContext.BaseDirectory,"test-results");Directory.CreateDirectory(output);
        var folder=Path.Combine(output,"icon-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string first=Path.Combine(folder,"future-app.ico"),second=Path.Combine(folder,"replacement.ico"),link=Path.Combine(folder,"future-app.lnk");
            WriteIcon(first,new[]{32,64,256},new[]{Colors.Red,Colors.Lime,Colors.Blue});WriteIcon(second,new[]{32},new[]{Colors.Orange});
            var image=(BitmapSource)Native.Icon(first,128)!;Check("multi-frame ICO selects original larger frame",image.PixelWidth==256&&Pixel(image).B>200,$"width={image.PixelWidth}; pixel={Pixel(image)}");
            var mid=(BitmapSource)Native.Icon(first,48)!;Check("mid-size uses nearest larger native frame",mid.PixelWidth==64&&Pixel(mid).G>200);
            var low=(BitmapSource)Native.Icon(second,128)!;Check("low resolution resources are not pre-upscaled",low.PixelWidth==32);
            Shortcut(link,Environment.ProcessPath!,first+",0");var recipe=IconResources.Describe(link);
            Check("new shortcut honors its explicit icon instead of executable target",recipe?.File==first&&Pixel((BitmapSource)Native.Icon(link,128)!).B>200);
            var stamp=IconResources.Stamp(link);WriteIcon(first,new[]{64,256},new[]{Colors.Lime,Colors.Magenta});
            Check("resource replacement invalidates an unchanged shortcut",IconResources.Stamp(link)!=stamp&&Pixel((BitmapSource)Native.Icon(link,128)!).R>200);
            Shortcut(link,Environment.GetFolderPath(Environment.SpecialFolder.Windows)+"\\explorer.exe",second+",0");
            Check("shortcut edit updates icon resource and target cache",IconResources.Describe(link)?.File==second&&Shell.Resolve(link).EndsWith("explorer.exe",StringComparison.OrdinalIgnoreCase)&&((BitmapSource)Native.Icon(link,128)!).PixelWidth==32);
            var url=Path.Combine(folder,"future-link.url");File.WriteAllText(url,"[InternetShortcut]\r\nURL=https://example.invalid/\r\nIconFile="+first+"\r\nIconIndex=0\r\n");
            Check("new internet shortcut honors IconFile and IconIndex",IconResources.Describe(url)?.File==first&&Native.Icon(url,128)!=null);
            var doc=Path.Combine(folder,"future-document.txt");File.WriteAllText(doc,"test");
            Check("new files and folders retain Shell type icons",Native.Icon(doc,128)!=null&&Native.Icon(folder,128)!=null);
            var corrupt=Path.Combine(folder,"corrupt.ico");File.WriteAllBytes(corrupt,new byte[]{0,1,2,3});
            Native.Icon(corrupt,128);Check("malformed icon safely falls back without throwing",true);
            foreach(var scale in new[]{1.0,1.25,1.5,2.0})
            {
                var entry=Icons.Measure(Native.Icon(link,(int)(112*scale)));var cell=new Grid{Width=64,Height=64};cell.Children.Add(Icons.View(entry,56));
                cell.Measure(new Size(64,64));cell.Arrange(new Rect(0,0,64,64));cell.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)(64*scale),(int)(64*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(cell);
                var measured=Icons.Measure(bitmap).Content;
                Check($"asymmetric transparent padding is optically centered at {scale*100:0}%",Math.Abs(measured.X+measured.Width/2.0-32*scale)<2&&Math.Abs(measured.Y+measured.Height/2.0-32*scale)<2,measured.ToString());
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,$"future-icon-{scale*100:0}.png")))encoder.Save(stream);
            }
            var full=Icons.Measure(Native.Icon(first,128));Check("original alpha bounds preserved without synthetic frame",full.Content.Width<((BitmapSource)full.Image!).PixelWidth);
            var native=Native.ResourceIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","shell32.dll"),0,128);
            Check("PE resource group extraction returns a native icon",native!=null);
            var generation=IconResources.Stamp(doc);IconResources.Invalidate();Check("explicit refresh invalidates Shell association cache",IconResources.Stamp(doc)!=generation);
            WriteIcon(second,new[]{64},new[]{Colors.White});var white=Icons.Measure(Native.Icon(second,128));Check("white transparent glyph is recognized independently of canvas bounds",white.LightGlyph);
            Theme.Apply("light");var whiteCell=new Grid{Width=64,Height=64,Background=Brushes.White};whiteCell.Children.Add(Icons.View(white,56));whiteCell.Measure(new Size(64,64));whiteCell.Arrange(new Rect(0,0,64,64));whiteCell.UpdateLayout();
            var contrast=new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32);contrast.Render(whiteCell);var contrastData=new byte[64*64*4];contrast.CopyPixels(contrastData,256,0);
            Check("white artwork remains distinguishable on white glass without a rectangular plate",Enumerable.Range(0,64*64).Any(i=>contrastData[i*4]<240));var contrastEncoder=new PngBitmapEncoder();contrastEncoder.Frames.Add(BitmapFrame.Create(contrast));using(var stream=File.Create(Path.Combine(output,"white-glyph-contrast.png")))contrastEncoder.Save(stream);
            return 0;
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});return 1;}
        finally{File.WriteAllText(Path.Combine(output,"icon-test.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));Directory.Delete(folder,true);}
    }
    static Color Pixel(BitmapSource image){var data=new byte[4];image.CopyPixels(new Int32Rect(image.PixelWidth/2,image.PixelHeight/2,1,1),data,4,0);return Color.FromArgb(data[3],data[2],data[1],data[0]);}
    internal static void Shortcut(string path,string target,string icon)
    {
        object? shell=null,item=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);item=((dynamic)shell!).CreateShortcut(path);((dynamic)item).TargetPath=target;((dynamic)item).IconLocation=icon;((dynamic)item).Save();File.SetLastWriteTimeUtc(path,DateTime.UtcNow);}finally{if(item!=null)Marshal.ReleaseComObject(item);if(shell!=null)Marshal.ReleaseComObject(shell);}
    }
    internal static void WriteIcon(string path,int[] sizes,Color[] colors)
    {
        var frames=new List<byte[]>();
        for(int n=0;n<sizes.Length;n++)
        {
            int size=sizes[n];var data=new byte[size*size*4];var color=colors[n];
            // Deliberately asymmetric vendor padding, plus a recognizable narrow silhouette.
            for(int y=size/4;y<size*7/8;y++)for(int x=size/8;x<size*5/8;x++){int k=(y*size+x)*4;data[k]=color.B;data[k+1]=color.G;data[k+2]=color.R;data[k+3]=255;}
            var image=BitmapSource.Create(size,size,96,96,PixelFormats.Bgra32,null,data,size*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var memory=new MemoryStream();encoder.Save(memory);frames.Add(memory.ToArray());
        }
        using(var writer=new BinaryWriter(File.Create(path))){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}foreach(var bytes in frames)writer.Write(bytes);}
        File.SetLastWriteTimeUtc(path,DateTime.UtcNow);
    }
}
