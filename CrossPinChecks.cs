using System.IO;
using System.Text.Json;
using System.Windows;
namespace MacDock;
internal static class CrossPinChecks
{
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;var records=new List<object>();
        Window? rejectDrop=null;
        void Check(string name,bool pass,object? detail=null){records.Add(new{name,pass,detail});File.WriteAllText(Path.Combine(output,"cross-pin.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));if(!pass)throw new Exception(name);}
        try{
            var expectedProfile=Path.GetFullPath(Path.Combine(output,"profile","config.json"));
            var realProfile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacDock","config.json");
            if(!string.Equals(Path.GetFullPath(c.Store.FilePath),expectedProfile,StringComparison.OrdinalIgnoreCase)||string.Equals(expectedProfile,realProfile,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing pin tests outside the isolated profile");
            Check("configuration isolated before any pin change",true,c.Store.FilePath);
            c.Store.Data.Mode="自定义";c.Store.Data.ListView=false;foreach(var e in c.Store.Data.Entries)e.Pinned=false;
            var entries=c.Store.Data.Entries.Where(e=>e.InDrawer&&!e.IsVirtual&&File.Exists(e.Path)).Take(12).ToArray();foreach(var e in entries.Take(3))e.Pinned=true;c.Changed();c.ToggleDrawer();await Task.Delay(700);
            async Task Drag(Entry entry,int slot,bool cancel=false,bool invalid=false){
                c.Drawer!.EnsureVisible(entry.Id);await Task.Delay(100);var before=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();var drawer=c.Store.Data.Entries.Select(e=>e.Id).ToArray();var point=c.Drawer.TileCenter(entry.Id);var pins=c.Store.PinnedEntries().ToArray();var target=c.Dock.TileCenter(pins[Math.Min(slot,pins.Length-1)].Id);if(slot>=pins.Length){target=c.Dock.GatewayCenter;target.X-=32;}else target.X-=10;
                var original=File.ReadAllBytes(entry.Path);int launches=c.LaunchCount;
                nint startHit=0;
                await Task.Run(()=>{Native.MovePointer(point.X,point.Y);Thread.Sleep(70);startHit=Native.WindowFromPoint(point);Native.TestButton(true);Thread.Sleep(60);Native.MovePointer(point.X+10,point.Y+4);Thread.Sleep(80);for(int i=1;i<=30;i++){Native.MovePointer(point.X+(target.X-point.X)*i/30,point.Y+(target.Y-point.Y)*i/30);Thread.Sleep(18);}});await Task.Delay(180);
                int index=c.Dock.PinDestination;Check("captured drawer gesture reaches Dock",PointerSort.Active!=null&&c.InternalDragging,
                    new{index,slot,c.Dock.PreviewingPin,startHit=(long)startHit,drawer=(long)c.Drawer.Handle,c.Drawer.LastPointerInput,PointerSort.LastFocusTrace,point=new{point.X,point.Y},c.DrawerProgress});
                if(!entry.Pinned)Check("live insertion preview exists",index>=0&&c.Dock.PreviewingPin);
                if(slot==2&&!cancel&&!invalid){using var preview=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);using(var g=System.Drawing.Graphics.FromImage(preview))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,preview.Size);preview.Save(Path.Combine(output,"insertion-preview.png"));}
                if(invalid)
                {
                    rejectDrop=new Window
                    {
                        Title="MacDock invalid drop fixture",Width=160,Height=100,WindowStyle=WindowStyle.None,
                        Topmost=true,ShowInTaskbar=false,ShowActivated=false
                    };
                    rejectDrop.Show();
                    var hwnd=new System.Windows.Interop.WindowInteropHelper(rejectDrop).Handle;
                    Native.SetWindowPos(hwnd,-1,c.WorkArea.Left+22,c.WorkArea.Top+22,0,0,0x11);
                    Native.MovePointer(c.WorkArea.Left+55,c.WorkArea.Top+55);
                    await Task.Delay(150);
                }if(cancel){Native.TestKey(27,true);await Task.Delay(60);Native.TestKey(27,false);}Native.TestButton(false);await Task.Delay(40);
                var scroll=c.Drawer.Scroller;var pointer=scroll.PointToScreen(new Point(scroll.ActualWidth*.5,scroll.ActualHeight*.4));double prior=scroll.VerticalOffset;Native.MovePointer((int)pointer.X,(int)pointer.Y);Native.TestWheel(prior>100?120:-120);await Task.Delay(220);
                Native.GetCursorPos(out var actual);Check("wheel works after cross drag without clicking",Math.Abs(scroll.VerticalOffset-prior)>10,new{trace=c.Drawer.LastWheelInput,diagnostics=((SmoothScrollViewer)scroll).WheelDiagnostics,focus=System.Windows.Input.Keyboard.FocusedElement?.GetType().Name,raw=GlassWindow.LastRawWheel,point=pointer.ToString(),cursor=new{actual.X,actual.Y},hit=(long)Native.WindowFromPoint(actual),window=(long)c.Drawer.Handle,active=c.Drawer.IsActive,capture=System.Windows.Input.Mouse.Captured?.GetType().Name});scroll.ScrollToVerticalOffset(prior);await Task.Delay(60);
                var expected=before.ToList();if(!cancel&&!invalid&&!expected.Contains(entry.Id))expected.Insert(index,entry.Id);
                Check("pin release/cancel matches displayed insertion",expected.SequenceEqual(c.Store.PinnedEntries().Select(e=>e.Id)),new{cancel,invalid,index});
                Check("cross drag preserves drawer order and original file",drawer.SequenceEqual(c.Store.Data.Entries.Select(e=>e.Id))&&original.SequenceEqual(File.ReadAllBytes(entry.Path)));
                Check("no launch or lingering drag/preview",c.LaunchCount==launches&&PointerSort.Active==null&&!c.InternalDragging&&!c.Dock.PreviewingPin);
                Check("Dock icons remain separated",c.Dock.ArtworkBounds.Select(r=>Math.Round(r.X)).Distinct().Count()==c.Dock.ArtworkBounds.Count);
                var reload=new Store(Path.GetDirectoryName(c.Store.FilePath));Check("pin order durably reloads",expected.SequenceEqual(reload.PinnedEntries().Select(e=>e.Id)));
            }
            await Drag(entries[3],0);await Drag(entries[4],2);await Drag(entries[5],5);await Drag(entries[4],0);await Drag(entries[6],1,true);await Drag(entries[7],2,false,true);
            c.Store.Data.Mode="首字母";c.Save();c.Drawer!.Render();await Task.Delay(150);await Drag(entries[8],1);
            c.Dock.Hide();await Drag(entries[9],2);Check("drag intent wakes a hidden Dock",c.Dock.IsVisible);
            c.Store.Data.Mode="自定义";c.Save();c.Drawer.Render();
            var id=c.Store.PinnedEntries()[0].Id;var hit=c.Dock.TileCenter(id);Native.MovePointer(hit.X,hit.Y);await Task.Delay(400);Check("Dock wave resumes after cross drags",c.Dock.MaxScale>1.08);
            using var picture=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);using(var g=System.Drawing.Graphics.FromImage(picture))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,picture.Size);picture.Save(Path.Combine(output,"cross-pin-final.png"));
            File.WriteAllText(Path.Combine(output,"expected-orders.json"),JsonSerializer.Serialize(new{drawer=c.Store.Data.Entries.Select(e=>e.Id),dock=c.Store.PinnedEntries().Select(e=>e.Id)}));
        }catch(Exception e){records.Add(new{name="error",pass=false,detail=(object)e.ToString()});}
        finally{PointerSort.Active?.End(false);Native.TestButton(false);rejectDrop?.Close();File.WriteAllText(Path.Combine(output,"cross-pin.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));Application.Current.Shutdown();}
    }
}
