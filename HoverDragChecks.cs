using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace MacDock;

internal static class HoverDragChecks
{
    [DllImport("user32.dll")]static extern nint GetWindow(nint hwnd,uint command);
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        var results=new List<object>();
        void Record(string name,bool pass,object? detail=null)=>results.Add(new{name,pass,detail});
        var area=c.WorkArea;
        var scene=new Window{Title="MacDock hover test background",ShowInTaskbar=false,WindowStyle=WindowStyle.None,
            ResizeMode=ResizeMode.NoResize,Left=area.Left/c.Scale,Top=area.Top/c.Scale,
            Width=area.Width/c.Scale,Height=area.Height/c.Scale,Background=Brushes.WhiteSmoke};
        void Shot(string name,int x,int y,int width,int height)
        {
            using var bitmap=new System.Drawing.Bitmap(width,height);
            using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(x,y,0,0,bitmap.Size);
            bitmap.Save(Path.Combine(output,name+".png"));
        }
        static bool Above(nint ghost,nint surface)
        {
            for(nint h=GetWindow(surface,3);h!=0;h=GetWindow(h,3))if(h==ghost)return true;
            return false;
        }
        static void Move(Native.Point p)=>Native.MovePointer(p.X,p.Y);
        PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
        try
        {
            if(!string.Equals(c.Store.FilePath,Path.Combine(output,"profile","config.json"),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Test requires isolated configuration.");
            scene.Show();c.Store.Data.ReducedMotion=false;c.Store.Data.Mode="自定义";c.Store.Data.ListView=false;c.Changed();
            var pins=c.Store.PinnedEntries().Take(c.Layout.VisibleDockCount(c.Store.PinnedEntries().Count)).ToArray();
            var wake=c.WakeBounds;
            var wakeCenter=new Native.Point((wake.Left+wake.Right)/2,(wake.Top+wake.Bottom)/2);
            Move(wakeCenter);await Task.Delay(520);
            Move(c.Dock.GatewayCenter);await Task.Delay(410);
            var baseSizes=c.Dock.ArtworkBounds.Select(x=>x.Width).ToArray();
            Record("actual hover wake",c.Dock.IsVisible&&baseSizes.Length>=3,new{count=baseSizes.Length,c.Diagnostics});
            Native.GetWindowRect(c.Dock.Handle,out var bar);
            foreach(int slot in new[]{0,pins.Length/2,pins.Length-1})
            {
                var point=c.Dock.TileCenter(pins[slot].Id);
                Move(point);await Task.Delay(1190);
                var scales=c.Dock.ArtworkBounds.Select((r,i)=>Math.Round(r.Width/baseSizes[i],3)).ToArray();
                double neighbor=scales.Where((_,i)=>Math.Abs(i-slot)==1).Max();
                Record("hover focus, hit and label "+slot,scales[slot]>=neighbor+.28&&c.Dock.HitTestEntry(point)==pins[slot].Id&&c.Dock.TipEntryId==pins[slot].Id&&c.Dock.TipVisible,
                    new{slot,pins[slot].Name,scales,neighbor,hit=c.Dock.HitTestEntry(point),tip=c.Dock.TipEntryId});
                Shot("hover-"+slot,bar.Left-20,bar.Top-54,bar.Right-bar.Left+40,bar.Bottom-bar.Top+69);
            }
            var first=c.Dock.TileCenter(pins[0].Id);var last=c.Dock.TileCenter(pins[^1].Id);
            for(int i=0;i<=28;i++)
            {
                Move(new(first.X+(last.X-first.X)*i/28,first.Y));
                await Task.Delay(10);
            }
            Move(new(first.X,bar.Top-120));await Task.Delay(460);
            Record("wave and tip recover after leaving",!c.Dock.TipVisible&&c.Dock.MaxScale<1.01&&!c.Dock.WaveRunning);

            async Task Drag(string name,Native.Point from,Native.Point to,GlassWindow above,bool cancel,int delay)
            {
                Move(from);await Task.Delay(100);Native.TestButton(true);await Task.Delay(50);
                for(int step=1;step<=20;step++)
                {
                    Move(new(from.X+(to.X-from.X)*step/20,from.Y+(to.Y-from.Y)*step/20));
                    await Task.Delay(delay);
                }
                await Task.Delay(140);
                var ghost=Application.Current.Windows.OfType<GlassWindow>().FirstOrDefault(w=>w.Title=="MacDock drag preview");
                bool started=PointerSort.Active!=null&&ghost!=null;
                Record(name+" drag started",started,new{c.InternalDragging,c.Drawer?.LastPointerInput,PointerSort.LastFocusTrace});
                if(started)
                {
                    Shot(name+"-before",to.X-84,to.Y-70,168,140);
                    c.Position(); // a real reposition raises the glass HWNDs while the mouse is held
                    await Task.Delay(180);
                    Native.GetWindowRect(ghost!.Handle,out var preview);
                    var aboveGlass=Above(ghost.Handle,above.Handle);
                    var atCursor=preview.Contains(to);
                    Record(name+" preview above glass during pause",aboveGlass&&atCursor&&Native.IsWindowVisible(ghost.Handle),
                        new{aboveGlass,atCursor,visible=Native.IsWindowVisible(ghost.Handle),inputPasses=Native.WindowFromPoint(to)!=ghost.Handle,preview.Left,preview.Top,preview.Right,preview.Bottom});
                    Shot(name+"-after",to.X-84,to.Y-70,168,140);
                }
                if(cancel){Native.TestKey(27,true);await Task.Delay(55);Native.TestKey(27,false);}
                Native.TestButton(false);await Task.Delay(220);
                Record(name+" cleanup",PointerSort.Active==null&&!c.InternalDragging&&!Application.Current.Windows.OfType<GlassWindow>().Any(w=>w.Title=="MacDock drag preview"));
            }

            Move(wakeCenter);await Task.Delay(520);
            for(int round=0;round<2;round++)
                await Drag("dock-"+round,c.Dock.TileCenter(pins[round].Id),c.Dock.TileCenter(pins[^1].Id),c.Dock,round==0,round==0?11:25);
            c.ToggleDrawer();await Task.Delay(720);
            var drawer=c.Drawer!;
            var candidates=drawer.SortTiles(false).Select(v=>(Entry)v.Tag).Where(e=>!e.Pinned&&!e.IsVirtual).Take(10).ToArray();
            for(int round=0;round<2;round++)
                await Drag("drawer-"+round,drawer.TileCenter(candidates[round].Id),drawer.TileCenter(candidates[round+2].Id),drawer,round==1,round==0?10:26);
            for(int round=0;round<2;round++)
                await Drag("cross-"+round,drawer.TileCenter(candidates[round+4].Id),c.Dock.TileCenter(c.Store.PinnedEntries()[1].Id),c.Dock,round==0,round==0?12:27);
            Record("no unaccounted input",PointerHook.NonInjectedInputs==0,PointerHook.NonInjectedInputs);
        }
        catch(Exception error){Record("exception",false,error.ToString());}
        finally
        {
            PointerSort.Active?.End(false);Native.TestButton(false);PointerHook.AuditInput=false;
            scene.Close();
            File.WriteAllText(Path.Combine(output,"hover-drag.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();
        }
    }
}
