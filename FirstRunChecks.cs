using System.IO;
using System.Text.Json;
using System.Windows;

namespace MacDock;

internal static class FirstRunChecks
{
    internal static async Task Run(Controller controller)
    {
        string output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var checks=new List<object>();
        void Check(string name,bool pass,object? detail=null)=>checks.Add(new{name,pass,detail});
        try
        {
            var store=controller.Store;
            if(Path.GetFullPath(store.FilePath)!=Path.Combine(Path.GetFullPath(output),"profile","config.json"))
                throw new InvalidOperationException("Expected an isolated first-run profile.");
            var mirror=Desktop.Enumerate(true,out var positions);
            var byPath=mirror.Select(e=>e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var expected=positions.Where(p=>byPath.Remove(p.Path)).Select(p=>p.Path).ToArray();
            var mirrored=store.Data.Entries.Where(e=>e.InDrawer).ToArray();
            int columns=controller.Layout.Columns;
            var visual=mirrored.Chunk(columns*controller.Layout.VisibleRows).SelectMany(page=>page.Where(e=>e!=null)
                .Select((e,slot)=>(e.Path,slot)).OrderBy(item=>item.slot%columns).Select(item=>item.Path));
            Check("Explorer coordinate order seeds a clean profile",expected.Length>1&&expected.SequenceEqual(visual.Take(expected.Length)),
                new{positioned=expected.Length,mirrored=mirrored.Length,desktop=DesktopView.LastError});
            Check("unpositioned desktop items remain in the drawer",mirrored.Select(e=>e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(mirror.Select(e=>e.Path)));
            var pins=store.PinnedEntries().ToArray();
            var system=Desktop.DefaultPins().ToArray();
            Check("four real Shell defaults in the specified order",pins.Length==4&&pins.Select(e=>e.Path).SequenceEqual(system.Select(e=>e.Path)),pins.Select(e=>new{e.Name,e.Path,e.Source}));
            var oldPositions=positions.ToArray();
            controller.ToggleDrawer();await Task.Delay(720);
            var drawer=controller.Drawer!;
            Check("drawer displays the clean mirror in stored order",drawer.IsVisible&&drawer.SortTiles(false).Select(e=>((Entry)e.Tag).Id).SequenceEqual(mirrored.Select(e=>e.Id)));
            Check("Dock displays all default artwork",controller.Dock.ArtworkBounds.Count==pins.Length);
            using(var bitmap=new System.Drawing.Bitmap(controller.WorkArea.Width,controller.WorkArea.Height))
            {
                using(var capture=System.Drawing.Graphics.FromImage(bitmap))
                    capture.CopyFromScreen(controller.WorkArea.Location,System.Drawing.Point.Empty,bitmap.Size);
                bitmap.Save(Path.Combine(output,"first-run.png"));
            }
            controller.CloseDrawer(keepDock:true);await Task.Delay(440);
            foreach(var pin in pins)
            {
                controller.Expand();controller.SettleDockMotion();
                var point=controller.Dock.TileCenter(pin.Id);
                Native.MovePointer(point.X,point.Y);await Task.Delay(80);
                int launched=controller.LaunchCount;
                Native.TestButton(true);await Task.Delay(28);Native.TestButton(false);
                await Task.Delay(550);
                var fg=Native.GetForegroundWindow();
                Check("physical default pin click opens a Shell window",controller.LaunchCount==launched+1&&Native.Class(fg)=="CabinetWClass",new{pin.Name,foreground=Native.Class(fg)});
            }
            foreach(var pin in pins)pin.Pinned=false;
            if(mirrored.Length>1)store.Reorder(mirrored[0].Id,mirrored[1].Id);
            store.Save();
            var savedOrder=store.Data.Entries.Select(e=>e.Id).ToArray();
            var reload=new Store(Path.GetDirectoryName(store.FilePath));
            reload.Initialize(mirror,positions,system);
            Check("explicit empty Dock and manual order survive reopen",!reload.IsFirstRun&&reload.PinnedEntries().Count==0&&savedOrder.SequenceEqual(reload.Data.Entries.Select(e=>e.Id)));
            File.Move(store.FilePath,store.FilePath+".validation-preserved");
            var recovered=new Store(Path.GetDirectoryName(store.FilePath));
            Check("missing primary recovers the last cleared layout instead of defaults",!recovered.IsFirstRun&&recovered.PinnedEntries().Count==0&&savedOrder.SequenceEqual(recovered.Data.Entries.Select(e=>e.Id)));
            File.Move(store.FilePath+".validation-preserved",store.FilePath);
            Desktop.Enumerate(true,out var after);
            Check("Explorer desktop coordinates remain unchanged",oldPositions.SequenceEqual(after));
            var fresh=new Store(Path.Combine(output,"no-explorer-profile"));
            fresh.Initialize(mirror,Array.Empty<DesktopView.PositionedItem>(),system);
            Check("unavailable Explorer positions retain every item",fresh.Data.Entries.Count(e=>e.InDrawer)==mirror.Count&&!fresh.IsFirstRun);
        }
        catch(Exception error){Check("exception",false,error.ToString());}
        finally
        {
            Native.TestButton(false);
            File.WriteAllText(Path.Combine(output,"first-run.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();
        }
    }
}
