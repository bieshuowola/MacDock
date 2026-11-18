using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MacDock;

internal static class IconAudit
{
    internal static int Run()
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")??Path.Combine(AppContext.BaseDirectory,"test-results");Directory.CreateDirectory(output);
        var entries=Desktop.Enumerate();var report=new List<object>();var page=new WrapPanel{Width=1200,Background=new SolidColorBrush(Color.FromRgb(67,73,84))};int index=0,pageNumber=0;
        void SavePage(){page.Measure(new Size(1200,double.PositiveInfinity));page.Arrange(new Rect(page.DesiredSize));page.UpdateLayout();var bitmap=new RenderTargetBitmap(1200,(int)Math.Ceiling(page.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(page);Save(bitmap,Path.Combine(output,$"atlas-{++pageNumber}.png"));page.Children.Clear();}
        foreach(var entry in entries)
        {
            index++;var resolved=IconResources.Describe(entry.Path);(string Path,int Index)? location=resolved!=null?(resolved.File,resolved.Index):null;var raw=Native.AuditShellIcon(entry.Path,128);var small=Native.AuditSmallIcon(entry.Path);var resource=location.HasValue?Native.ResourceIcon(location.Value.Path,location.Value.Index,128):null;
            var cell=new StackPanel{Width=300,Height=136,Margin=new Thickness(0)};
            var label=Theme.Text($"{index:000} {entry.Name}",12);label.TextTrimming=TextTrimming.CharacterEllipsis;label.Margin=new Thickness(8,6,8,6);cell.Children.Add(label);
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            var variants=new[]{("shell128",raw),("small",small),("resource",resource)};
            foreach(var (kind,source) in variants)
            {
                var slot=new StackPanel{Width=94};var box=new Grid{Width=66,Height=66,Background=new SolidColorBrush(Color.FromRgb(42,47,57))};
                box.Children.Add(Icons.View(Icons.Measure(source),60));slot.Children.Add(box);var text=Theme.Text(kind,10,true);text.TextAlignment=TextAlignment.Center;slot.Children.Add(text);row.Children.Add(slot);
                if(source is BitmapSource bitmap)Save(bitmap,Path.Combine(output,$"{index:000}-{kind}.png"));
            }
            cell.Children.Add(row);page.Children.Add(cell);
            object? Stats(ImageSource? image){if(image is not BitmapSource bitmap)return null;var measure=Icons.Measure(image);return new{width=bitmap.PixelWidth,height=bitmap.PixelHeight,content=measure.Content.ToString(),measure.Filled};}
            report.Add(new{index,entry.Name,entry.Path,location=location?.Path,iconIndex=location?.Index,shell=Stats(raw),small=Stats(small),resource=Stats(resource)});
            if(page.Children.Count==16)SavePage();
        }
        if(page.Children.Count>0)SavePage();File.WriteAllText(Path.Combine(output,"icon-audit.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));return 0;
    }
    static void Save(BitmapSource source,string file){var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using(var stream=File.Create(file))encoder.Save(stream);}
}
