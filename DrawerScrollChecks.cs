#if MACDOCK_EXPERIENCE_CHECKS
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MacDock;

internal static class DrawerScrollChecks
{
    internal static async Task Run(Controller controller)
    {
        string output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var observations=new List<object>();
        try
        {
            controller.ToggleDrawer();
            await Task.Delay(750);
            var drawer=controller.Drawer!;
            var scroll=drawer.Scroller;
            if(scroll.ScrollableHeight<50)throw new InvalidOperationException("Test requires the real drawer to overflow.");
            var point=scroll.PointToScreen(new Point(scroll.ActualWidth*.6,scroll.ActualHeight*.45));
            Native.MovePointer((int)point.X,(int)point.Y);
            await Task.Delay(120);
            var offsets=new List<(double Time,double Offset)>();
            var frames=new List<double>();
            var watch=Stopwatch.StartNew();
            TimeSpan? lastRender=null;
            double last=0;
            EventHandler render=(_,e)=>
            {
                if(e is not RenderingEventArgs tick||lastRender==tick.RenderingTime)return;
                lastRender=tick.RenderingTime;
                double now=watch.Elapsed.TotalMilliseconds;
                if(last>0)frames.Add(now-last);
                last=now;
                offsets.Add((now,scroll.VerticalOffset));
            };
            CompositionTarget.Rendering+=render;
            try
            {
                for(int i=0;i<65;i++)
                {
                    Native.TestWheel(i%10<5?-120:120);
                    await Task.Delay(13);
                }
                await Task.Delay(230);
            }
            finally { CompositionTarget.Rendering-=render; }
            double Quantile(double p)=>frames.Count==0?0:frames.Order().ElementAt((int)Math.Ceiling(p*frames.Count)-1);
            observations.Add(new{phase="wheel",count=frames.Count,p95=Quantile(.95),p99=Quantile(.99),max=Quantile(1),longFrames=frames.Count(v=>v>12.2),changes=offsets.Zip(offsets.Skip(1)).Count(p=>Math.Abs(p.First.Offset-p.Second.Offset)>.01),extent=scroll.ExtentHeight,viewport=scroll.ViewportHeight,screen=controller.Screen.DeviceName,scale=controller.Scale,wheel=drawer.LastWheelInput,frames});
            scroll.ScrollToBottom();await Task.Delay(240);
            double before=scroll.VerticalOffset;
            using(var bitmap=new System.Drawing.Bitmap(drawer.BodyBounds.Right-drawer.BodyBounds.Left,drawer.BodyBounds.Bottom-drawer.BodyBounds.Top))
            {
                using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(drawer.BodyBounds.Left,drawer.BodyBounds.Top,0,0,bitmap.Size);
                bitmap.Save(Path.Combine(output,"drawer-bottom.png"));
            }
            controller.CloseDrawer(keepDock:true,force:true);await Task.Delay(550);
            controller.ToggleDrawer();await Task.Delay(550);
            observations.Add(new{phase="reopen",before,after=scroll.VerticalOffset,pass=before>0&&Math.Abs(before-scroll.VerticalOffset)<1});

            if(Environment.GetEnvironmentVariable("MACDOCK_SCROLL_INTERACTION_CHECK")=="1")
            {
                static T? Find<T>(DependencyObject root) where T:DependencyObject
                {
                    for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                    {
                        var child=VisualTreeHelper.GetChild(root,i);
                        if(child is T match)return match;
                        if(Find<T>(child) is {} nested)return nested;
                    }
                    return null;
                }
                var search=Find<TextBox>(drawer)??throw new InvalidOperationException("Search editor not found.");
                search.Text="MacDock-QA-NoMatch-1ECD0B";await Task.Delay(120);
                bool filtered=scroll.ScrollableHeight==0&&Math.Abs(scroll.VerticalOffset)<.5;
                search.Clear();await Task.Delay(180);
                observations.Add(new{phase="filter-clear",pass=filtered&&Math.Abs(before-scroll.VerticalOffset)<1,filtered,before,after=scroll.VerticalOffset});

                var bar=scroll.OverlayBar??throw new InvalidOperationException("Overlay scrollbar not found.");
                var barCenter=bar.PointToScreen(new Point(bar.ActualWidth/2,bar.ActualHeight/2));
                var add=drawer.AddFileButton;
                var addCenter=add.PointToScreen(new Point(add.ActualWidth/2,add.ActualHeight/2));
                var firstRow=drawer.SortTiles(false).Take(controller.Layout.Columns);
                double right=firstRow.Select(t=>t.PointToScreen(new Point(t.ActualWidth,0)).X).Max();
                double midpoint=(right+drawer.BodyBounds.Right)/2;
                bool aligned=Math.Abs(barCenter.X-addCenter.X)<=2&&Math.Abs(barCenter.X-midpoint)<=2;
                observations.Add(new{phase="gutter",pass=aligned,bar=barCenter.X,add=addCenter.X,midpoint});

                var thumb=Find<Thumb>(bar)??throw new InvalidOperationException("Scrollbar thumb not found.");
                var thumbPoint=thumb.PointToScreen(new Point(thumb.ActualWidth/2,thumb.ActualHeight/2));
                Native.MovePointer((int)thumbPoint.X,(int)thumbPoint.Y);
                await Task.Delay(180);
                Native.TestButton(true);
                try
                {
                    for(int i=1;i<=8;i++)
                    {
                        Native.MovePointer((int)thumbPoint.X,(int)thumbPoint.Y-i*12);
                        await Task.Delay(14);
                    }
                }
                finally{Native.TestButton(false);}
                await Task.Delay(160);
                observations.Add(new{phase="thumb",pass=scroll.VerticalOffset<before-15,before,after=scroll.VerticalOffset});

                scroll.ScrollToTop();await Task.Delay(60);
                var lastEntry=(Entry)drawer.SortTiles(false).Last().Tag;
                drawer.ApplyCustomOrder();
                drawer.EnsureVisible(lastEntry.Id);await Task.Delay(90);
                var center=drawer.TileCenter(lastEntry.Id);
                var topLeft=scroll.PointToScreen(new Point());
                bool visible=center.Y>=topLeft.Y&&center.Y<topLeft.Y+scroll.ActualHeight*controller.Scale
                    &&Native.WindowFromPoint(center)==drawer.Handle;
                observations.Add(new{phase="bring-into-view",pass=visible&&scroll.VerticalOffset>0,offset=scroll.VerticalOffset});

                controller.Store.Data.Mode="自定义";
                drawer.Render();
                await Task.Delay(90);
                scroll.ScrollToTop();await Task.Delay(100);
                double initialOffset=scroll.VerticalOffset;
                var sortOrder=drawer.SortTiles(false).Select(t=>((Entry)t.Tag).Id).ToArray();
                int lowerRow=controller.Layout.Columns+1;
                controller.CommitSort(sortOrder,sortOrder[0],lowerRow,false);
                await Task.Delay(160);
                bool changed=drawer.SortTiles(false).Select(t=>((Entry)t.Tag).Id).ElementAt(lowerRow)==sortOrder[0];
                observations.Add(new{phase="reorder-at-top",pass=changed&&initialOffset<.5&&scroll.VerticalOffset<.5,changed,initialOffset,offset=scroll.VerticalOffset});
            }
        }
        catch(Exception error){observations.Add(new{error=error.ToString()});}
        finally
        {
            File.WriteAllText(Path.Combine(output,"drawer-scroll.json"),JsonSerializer.Serialize(observations,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();
        }
    }
}
#endif
