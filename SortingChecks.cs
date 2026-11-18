using System.IO;
using System.Text.Json;
using System.Windows;
namespace MacDock;
internal static class SortingChecks
{
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(output);var results=new List<object>();
        void Check(string name,bool pass,object? detail=null){results.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(output,"sorting-progress.json"),JsonSerializer.Serialize(results));if(!pass)throw new Exception(name);}
        PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;Native.GetCursorPos(out var prior);
        try{
            c.Busy=true;c.Store.Data.Mode="自定义";c.Store.Data.ListView=false;foreach(var e in c.Store.Data.Entries)e.Pinned=false;foreach(var e in c.Store.Data.Entries.Where(e=>e.InDrawer).Take(6))e.Pinned=true;c.Changed();c.ToggleDrawer();await Task.Delay(800);c.Busy=false;
            var d=c.Drawer!;var initial=c.Store.Data.Entries.Where(e=>e.InDrawer).ToArray();var dockBefore=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();var bounds=c.Dock.ArtworkBounds.ToArray();
            var source=d.TileCenter(initial[0].Id);var target=d.TileCenter(initial[10].Id);
            await Task.Run(()=>{Native.MovePointer(source.X,source.Y);Thread.Sleep(100);Native.TestButton(true);Thread.Sleep(80);for(int n=1;n<=22;n++){Native.MovePointer(source.X+(target.X-source.X)*n/22,source.Y+(target.Y-source.Y)*n/22);Thread.Sleep(22);}Thread.Sleep(160);Native.TestButton(false);});await Task.Delay(600);
            Check("actual drawer cross-row drag changes order",!initial.Select(e=>e.Id).SequenceEqual(c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>e.Id)));
            Check("drawer drag preserves pinned order",dockBefore.SequenceEqual(c.Store.PinnedEntries().Select(e=>e.Id)));
            Check("Dock artwork remains separated",c.Dock.ArtworkBounds.Select(r=>Math.Round(r.X)).Distinct().Count()==bounds.Length,new{before=bounds.Select(r=>r.ToString()),after=c.Dock.ArtworkBounds.Select(r=>r.ToString())});
            async Task Drag(int from,int to,bool dock,bool cancel=false){
                var order=(dock?c.Store.PinnedEntries():c.Store.Data.Entries.Where(e=>e.InDrawer).ToArray()).Select(e=>e.Id).ToArray();
                if(!dock){d.EnsureVisible(order[to]);await Task.Delay(100);}var a=dock?c.Dock.TileCenter(order[from]):d.TileCenter(order[from]);var z=dock?c.Dock.TileCenter(order[to]):d.TileCenter(order[to]);
                var other=dock?c.Store.Data.Entries.Select(e=>e.Id).ToArray():c.Store.PinnedEntries().Select(e=>e.Id).ToArray();var launches=c.LaunchCount;
                await Task.Run(()=>{Native.MovePointer(a.X,a.Y);Thread.Sleep(80);Native.TestButton(true);Thread.Sleep(50);for(int n=1;n<=26;n++){Native.MovePointer(a.X+(z.X-a.X)*n/26,a.Y+(z.Y-a.Y)*n/26);Thread.Sleep(14);}Thread.Sleep(120);});await Task.Delay(80);
                var active=PointerSort.Active;Check((dock?"Dock":"drawer")+" captured drag "+from+" to "+to,active!=null);int dest=active!.Destination;if((from==0&&to==5&&dock)||(from==0&&to==12&&!dock)){using var picture=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);using(var graphics=System.Drawing.Graphics.FromImage(picture))graphics.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,picture.Size);picture.Save(Path.Combine(output,dock?"dock-insertion.png":"drawer-insertion.png"));}Check("preview has a genuine insertion gap",active.HasGap,new{dest});
                if(cancel){Native.TestKey(27,true);await Task.Delay(90);Native.TestKey(27,false);}Native.TestButton(false);await Task.Delay(40);
                if(dock){var sc=d.Scroller;double prior=sc.VerticalOffset;var pt=sc.PointToScreen(new Point(sc.ActualWidth*.5,sc.ActualHeight*.4));Native.MovePointer((int)pt.X,(int)pt.Y);Native.TestWheel(prior>100?120:-120);await Task.Delay(220);Check("Dock drag leaves drawer wheel immediately usable",Math.Abs(sc.VerticalOffset-prior)>10);sc.ScrollToVerticalOffset(prior);await Task.Delay(60);}else await Task.Delay(200);
                var expected=order.ToList();if(!cancel){expected.Remove(order[from]);expected.Insert(dest,order[from]);}
                var actual=(dock?c.Store.PinnedEntries():c.Store.Data.Entries.Where(e=>e.InDrawer).ToArray()).Select(e=>e.Id).ToArray();
                Check("release matches displayed insertion slot",actual.SequenceEqual(expected),new{dock,cancel,from,to,dest});Check("opposite surface order isolated",other.SequenceEqual(dock?c.Store.Data.Entries.Select(e=>e.Id):c.Store.PinnedEntries().Select(e=>e.Id)));Check("drag never launches or leaves capture",c.LaunchCount==launches&&PointerSort.Active==null&&!c.InternalDragging);
                Check("Dock remains nonoverlapping",c.Dock.ArtworkBounds.Select(r=>Math.Round(r.X)).Distinct().Count()==bounds.Length);
            }
            foreach(var pair in new[]{(0,12),(22,1),(4,30),(29,8)})await Drag(pair.Item1,pair.Item2,false);
            foreach(var pair in new[]{(0,5),(5,0),(1,4),(4,2)})await Drag(pair.Item1,pair.Item2,true);
            await Drag(0,5,true,true);await Drag(0,12,false,true);await Drag(55,64,false);await Drag(64,50,false);
            var drawerOrder=c.Store.Data.Entries.Select(e=>e.Id).ToArray();var dockOrder=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();c.CloseDrawer(keepDock:true);await Task.Delay(250);c.ToggleDrawer();await Task.Delay(450);Check("close/reopen retains both orders",drawerOrder.SequenceEqual(c.Store.Data.Entries.Select(e=>e.Id))&&dockOrder.SequenceEqual(c.Store.PinnedEntries().Select(e=>e.Id)));c.SyncDesktopNow();await Task.Delay(150);Check("desktop refresh retains custom order",drawerOrder.SequenceEqual(c.Store.Data.Entries.Select(e=>e.Id)));var reload=new Store(Path.GetDirectoryName(c.Store.FilePath));Check("disk reload retains independent orders",drawerOrder.SequenceEqual(reload.Data.Entries.Select(e=>e.Id))&&dockOrder.SequenceEqual(reload.PinnedEntries().Select(e=>e.Id)));
            var pin=c.Store.PinnedEntries().First();var hit=c.Dock.TileCenter(pin.Id);Native.MovePointer(hit.X,hit.Y);await Task.Delay(400);Check("Dock wave works after alternating sorts",c.Dock.MaxScale>1.08);Native.GetWindowRect(d.Handle,out var dr);Native.MovePointer(dr.Left+80,dr.Top+25);await Task.Delay(350);Check("wave restores without residual scale",c.Dock.MaxScale<1.005);
            using var b=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,b.Size);b.Save(Path.Combine(output,"sorting.png"));
            Check("no concurrent physical mouse input",PointerHook.NonInjectedInputs==0);
            File.WriteAllText(Path.Combine(output,"expected-orders.json"),JsonSerializer.Serialize(new{drawer=c.Store.Data.Entries.Select(e=>e.Id).ToArray(),dock=c.Store.PinnedEntries().Select(e=>e.Id).ToArray()}));
        }catch(Exception e){results.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{Native.TestButton(false);PointerHook.AuditInput=false;if(PointerHook.NonInjectedInputs==0)Native.MovePointer(prior.X,prior.Y);File.WriteAllText(Path.Combine(output,"sorting.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();}
    }
    internal static async Task VerifyRestart(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;var records=new List<object>();try{
            using var expected=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"expected-orders.json")));var drawer=expected.RootElement.GetProperty("drawer").EnumerateArray().Select(e=>e.GetString()).ToArray();var dock=expected.RootElement.GetProperty("dock").EnumerateArray().Select(e=>e.GetString()).ToArray();
            c.ToggleDrawer();await Task.Delay(600);var surface=c.Drawer!.Surface!;var views=c.Drawer.SortTiles(false);var rendered=views.OrderBy(v=>Math.Round(v.TransformToAncestor(surface).Transform(new Point()).Y,1)).ThenBy(v=>v.TransformToAncestor(surface).Transform(new Point()).X).Select(v=>((Entry)v.Tag).Id).ToArray();
            records.Add(new{name="cold process drawer order restored in actual grid",pass=drawer.SequenceEqual(c.Store.Data.Entries.Select(e=>e.Id))&&rendered.SequenceEqual(c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>e.Id)),storeSame=drawer.SequenceEqual(c.Store.Data.Entries.Select(e=>e.Id)),rendered,positions=views.Select(v=>new{id=((Entry)v.Tag).Id,p=v.TransformToAncestor(surface).Transform(new Point()).ToString()}).ToArray()});
            var visible=c.Store.PinnedEntries().Take(c.Layout.VisibleDockCount(c.Store.PinnedEntries().Count)).OrderBy(e=>c.Dock.TileCenter(e.Id).X).Select(e=>e.Id);records.Add(new{name="cold process Dock order and layout restored",pass=dock.SequenceEqual(c.Store.PinnedEntries().Select(e=>e.Id))&&visible.SequenceEqual(dock.Take(c.Layout.VisibleDockCount(dock.Length)))});
        }catch(Exception e){records.Add(new{name="error",pass=false,detail=e.ToString()});}finally{File.WriteAllText(Path.Combine(output,"restart-order.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();}
    }
    internal static async Task Edge(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;var results=new List<object>();
        void Check(string name,bool pass,object? detail=null){results.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(output,"edge-progress.json"),JsonSerializer.Serialize(results));if(!pass)throw new Exception(name);}
        Native.GetCursorPos(out var prior);PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
        try{
            c.Store.Data.Mode="自定义";c.Store.Data.ListView=false;foreach(var e in c.Store.Data.Entries)e.Pinned=false;foreach(var e in c.Store.Data.Entries.Where(e=>e.InDrawer).Take(6))e.Pinned=true;c.Changed();c.ToggleDrawer();await Task.Delay(600);var d=c.Drawer!;
            foreach(bool upward in new[]{false,true}){
                if(upward)d.Scroller.ScrollToEnd();else d.Scroller.ScrollToTop();await Task.Delay(200);
                var order=c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>e.Id).ToArray();var id=upward?order[^1]:order[0];var from=Array.IndexOf(order,id);d.EnsureVisible(id);await Task.Delay(80);
                var a=d.TileCenter(id);var edge=d.Scroller.PointToScreen(new Point(d.Scroller.ActualWidth/2,upward?8:d.Scroller.ActualHeight-8));var offset=d.Scroller.VerticalOffset;var pins=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();
                await Task.Run(()=>{Native.MovePointer(a.X,a.Y);Thread.Sleep(60);Native.TestButton(true);Thread.Sleep(60);for(int n=1;n<=18;n++){Native.MovePointer(a.X+((int)edge.X-a.X)*n/18,a.Y+((int)edge.Y-a.Y)*n/18);Thread.Sleep(16);}});await Task.Delay(650);
                var active=PointerSort.Active;Check("captured edge scroll "+upward,active!=null);Check("auto-scroll advances during held drag "+upward,upward?d.Scroller.VerticalOffset<offset-30:d.Scroller.VerticalOffset>offset+30,new{before=offset,after=d.Scroller.VerticalOffset});var dest=active!.Destination;Native.TestButton(false);await Task.Delay(250);var expected=order.ToList();expected.RemoveAt(from);expected.Insert(dest,id);Check("edge drop matches moving gap",expected.SequenceEqual(c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>e.Id)));Check("edge scrolling does not affect Dock",pins.SequenceEqual(c.Store.PinnedEntries().Select(e=>e.Id)));
            }
            c.Store.Data.ListView=true;d.Render();d.Scroller.ScrollToTop();await Task.Delay(200);var list=c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>e.Id).ToArray();var start=d.TileCenter(list[0]);var end=d.TileCenter(list[4]);
            await Task.Run(()=>{Native.MovePointer(start.X,start.Y);Thread.Sleep(50);Native.TestButton(true);Thread.Sleep(50);for(int n=1;n<=16;n++){Native.MovePointer(start.X+(end.X-start.X)*n/16,start.Y+(end.Y-start.Y)*n/16);Thread.Sleep(20);}});await Task.Delay(120);var destination=PointerSort.Active?.Destination??-1;Check("list view starts captured drag",destination>=0);Native.TestButton(false);await Task.Delay(220);var listExpected=list.ToList();listExpected.RemoveAt(0);listExpected.Insert(destination,list[0]);Check("list view insertion matches release",listExpected.SequenceEqual(c.Store.Data.Entries.Where(e=>e.InDrawer).Select(e=>e.Id)));
            c.Store.Data.ListView=false;var fixture=Path.Combine(output,"native-handoff");Directory.CreateDirectory(fixture);var target=Path.Combine(fixture,"target");Directory.CreateDirectory(target);var file=Path.Combine(fixture,"source.txt");File.WriteAllText(file,"native handoff payload");c.Store.Add(file,false);c.Store.Add(target,true);c.Changed();await Task.Delay(250);var entry=c.Store.Data.Entries.Single(e=>e.Path==file);var folder=c.Store.Data.Entries.Single(e=>e.Path==target);d.EnsureVisible(entry.Id);await Task.Delay(150);start=d.TileCenter(entry.Id);end=c.Dock.TileCenter(folder.Id);var handle=c.Dock.Handle;var launched=c.LaunchCount;
            await Task.Run(()=>{try{Native.MovePointer(start.X,start.Y);Thread.Sleep(100);Native.TestButton(true);Thread.Sleep(60);Native.MovePointer(start.X+12,start.Y+4);Thread.Sleep(100);for(int n=1;n<=24;n++){Native.MovePointer(start.X+(end.X-start.X)*n/24,start.Y+(end.Y-start.Y)*n/24);Thread.Sleep(20);}Thread.Sleep(150);Native.TestKey(17,true);Thread.Sleep(100);Native.TestButton(false);Thread.Sleep(700);}finally{Native.TestButton(false);Native.TestKey(17,false);}});await Task.Delay(250);
            Check("drawer captured drag hands off to native folder target",File.Exists(Path.Combine(target,"source.txt"))&&File.ReadAllText(Path.Combine(target,"source.txt"))=="native handoff payload",new{start,end,hit=Native.WindowFromPoint(end).ToInt64(),dock=handle.ToInt64()});Check("handoff preserves original file and never launches",File.Exists(file)&&c.LaunchCount==launched);Check("handoff releases drag state",PointerSort.Active==null&&!c.InternalDragging);Check("no concurrent physical input",PointerHook.NonInjectedInputs==0);
        }catch(Exception e){results.Add(new{name="error",pass=false,detail=(object)e.ToString()});}finally{PointerSort.Active?.End(false);Native.TestButton(false);Native.TestKey(17,false);PointerHook.AuditInput=false;if(PointerHook.NonInjectedInputs==0)Native.MovePointer(prior.X,prior.Y);File.WriteAllText(Path.Combine(output,"sorting-edge.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();}
    }
}
