#if MACDOCK_EXPERIENCE_CHECKS
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MacDock;

internal static class FieldInteractionChecks
{
    [DllImport("user32.dll")] static extern nint GetShellWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint FindWindow(string name,string? title);

    static IEnumerable<T> Elements<T>(DependencyObject root) where T:DependencyObject
    {
        if(root is T item)yield return item;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Elements<T>(VisualTreeHelper.GetChild(root,i)))yield return child;
    }

    static Process StartFixture(string image,params string[] args)
    {
        var launch=new ProcessStartInfo(image);
        launch.ArgumentList.Add("--maintenance-fixture");
        foreach(var arg in args)launch.ArgumentList.Add(arg);
        return Process.Start(launch)??throw new InvalidOperationException("Fixture failed to start");
    }

    internal static int Fixture(string[] args)
    {
        if(args[1]=="background")
        {
            Thread.Sleep(TimeSpan.FromMinutes(2));
            return 0;
        }
        if(args[1]=="launch-child")
        {
            using var child=StartFixture(args[2],"visible");
            File.WriteAllText(args[3],child.Id.ToString());
            return 0;
        }
        var app=new System.Windows.Application();
        return app.Run(new Window{Title="MacDock child-process fixture",Width=240,Height=130,Left=20,Top=50});
    }

    static async Task CheckRunningDots(Controller c,string output,Action<string,bool,object?> check,Func<Func<bool>,Task> until)
    {
        string executable=Path.Combine(AppContext.BaseDirectory,"MacDock.StatusFixture.exe");
        string childExecutable=Path.Combine(AppContext.BaseDirectory,"MacDock.StatusChild.exe");
        foreach(var path in new[]{executable,childExecutable})File.Copy(Environment.ProcessPath!,path,true);
        string shortcut=Path.Combine(output,"status-alpha.lnk");
        var shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object link=((dynamic)shell).CreateShortcut(shortcut);
        try
        {
            ((dynamic)link).TargetPath=executable;
            ((dynamic)link).Arguments="--maintenance-fixture background alpha";
            ((dynamic)link).Save();
        }
        finally{Marshal.ReleaseComObject(link);Marshal.ReleaseComObject(shell);}

        var target=new Entry{Path=executable,Name="Direct fixture",Pinned=true};
        var alias=new Entry{Path=shortcut,Name="Argument fixture",Pinned=true};
        var decoy=new Entry{Path=Path.Combine(output,"other",Path.GetFileName(executable)),Name="Different path",Pinned=true};
        c.Store.Data.Entries.Clear();c.Store.Data.DockOrder.Clear();
        c.Store.Data.Entries.AddRange(new[]{target,alias,decoy});
        c.Dock.Rebuild();c.Position();c.Expand();c.SettleDockMotion();
        var dock=c.Dock.TileCenter(target.Id);
        Native.MovePointer(dock.X,dock.Y);
        bool Direct()=>c.Dock.DotVisible(target.Id);
        bool Alias()=>c.Dock.DotVisible(alias.Id);
        using var beta=StartFixture(executable,"background","beta");
        using var alpha=StartFixture(executable,"background","alpha");
        try
        {
            await until(()=>Direct()&&Alias());
            check("background executables and shortcuts with exact arguments show their dots",Direct()&&Alias(),null);
            check("same executable name at a different path never matches",!c.Dock.DotVisible(decoy.Id),null);
            alpha.Kill();await alpha.WaitForExitAsync();
            await until(()=>!Alias());
            check("a second instance keeps only its matching dot",Direct()&&!Alias(),null);
            beta.Kill();await beta.WaitForExitAsync();
            await until(()=>!Direct());
            check("final process exit clears the dot without a window event",!Direct(),null);
        }
        finally
        {
            foreach(var process in new[]{alpha,beta})if(!process.HasExited)process.Kill();
        }
        string childPid=Path.Combine(output,"running-dots-child.pid");
        var launcher=StartFixture(executable,"launch-child",childExecutable,childPid);
        using var launcherExit=Process.GetProcessById(launcher.Id);
        c.Dock.TrackLaunch(target,launcher);
        await launcherExit.WaitForExitAsync();
        using var child=Process.GetProcessById(int.Parse(File.ReadAllText(childPid)));
        try
        {
            await until(()=>Native.Windows(childExecutable).Count==1&&Direct());
            await Task.Delay(850);
            check("same-application child keeps the dot after its launcher exits",Direct()&&launcherExit.HasExited,null);
            child.CloseMainWindow();
            await child.WaitForExitAsync();
            await until(()=>!Direct());
            check("child process exit clears the launcher dot",!Direct(),null);
        }
        finally{if(!child.HasExited)child.Kill();}
    }

    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        bool shadowOnly=Environment.GetEnvironmentVariable("MACDOCK_SHADOW_CHECK")=="1";
        var results=new List<object>();
        void Check(string name,bool pass,object? detail=null)=>results.Add(new{name,pass,detail});
        async Task Until(Func<bool> ready)
        {
            for(int i=0;i<120&&!ready();i++)await Task.Delay(20);
            if(!ready())throw new TimeoutException(c.Diagnostics);
        }
        async Task Move(Native.Point p)
        {
            Native.MovePointer(p.X,p.Y);
            await Task.Delay(120);
        }
        async Task Click(Native.Point p)
        {
            Native.MovePointer(p.X,p.Y);
            await Task.Delay(30);
            Native.TestButton(true);
            await Task.Delay(20);
            Native.TestButton(false);
            await Task.Delay(40);
        }
        async Task OpenDrawer()
        {
            c.ToggleDrawer();
            await Until(()=>c.Drawer is {IsVisible:true,IsDeforming:false}&&c.DrawerProgress==1);
        }
        void Key(byte vk)
        {
            Native.TestKey(vk,true);
            Native.TestKey(vk,false);
        }
        void Grab(string name)
        {
            var area=Native.MonitorArea(c.Screen,false);
            using var bitmap=new System.Drawing.Bitmap(area.Width,area.Height);
            using var graphics=System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(area.Location,System.Drawing.Point.Empty,bitmap.Size);
            bitmap.Save(Path.Combine(output,name+".png"),System.Drawing.Imaging.ImageFormat.Png);
        }
        static Border Tile(DependencyObject surface,Entry entry)=>Elements<Border>(surface).Single(b=>b.Tag==entry);
        static double Shadow(Border tile)=>Elements<Grid>(tile).First(g=>g.Children.Count>1&&
            g.Children[1] is Border { Background: RadialGradientBrush }).Children[1].Opacity;
        static bool Borderless(Border tile)=>tile.BorderBrush==Brushes.Transparent&&tile.FocusVisualStyle==null;
        async Task ShadowMotion(string surface,Border[] tiles,Native.Point first,Native.Point second,Native.Point blank,bool selectedAfterClick)
        {
            await Move(blank);
            await Until(()=>tiles.All(t=>Shadow(t)==0));
            async Task<double[]> Observe(Action input,Border tile,int duration)
            {
                var frames=new List<double>();
                EventHandler render=(_,_)=>frames.Add(Shadow(tile));
                CompositionTarget.Rendering+=render;
                try{input();await Task.Delay(duration);}
                finally{CompositionTarget.Rendering-=render;}
                return frames.ToArray();
            }
            var rise=await Observe(()=>Native.MovePointer(first.X,first.Y),tiles[0],155);
            var press=await Observe(()=>Native.TestButton(true),tiles[0],85);
            var release=await Observe(()=>Native.TestButton(false),tiles[0],160);
            double resting=selectedAfterClick?1:.48;
            Check(surface+" hover, down and up retarget the same shadow",
                rise.Any(x=>x>.03&&x<.45)&&press.Any(x=>x>.50&&x<.74)&&
                release.Any(x=>selectedAfterClick?x>.78&&x<.99:x>.50&&x<.74)&&
                Math.Abs(Shadow(tiles[0])-resting)<.001&&Borderless(tiles[0]),new{rise,press,release});
            var fade=await Observe(()=>
            {
                Native.MovePointer(blank.X,blank.Y);
                Task.Run(async()=>{await Task.Delay(22);Native.TestButton(true);Native.TestButton(false);});
            },tiles[0],235);
            Check(surface+" blank click fades the selected shadow",fade.Any(x=>x>.03&&x<.95)&&Shadow(tiles[0])==0,fade);
            for(int i=0;i<8;i++)
            {
                var point=i%2==0?first:second;
                Native.MovePointer(point.X,point.Y);
                await Task.Delay(18);
            }
            await Move(blank);
            await Until(()=>tiles.All(t=>Shadow(t)==0));
            Check(surface+" rapid hover changes leave no shadow or frame",tiles.All(Borderless)&&tiles.All(t=>Shadow(t)==0));
        }
        static void ApplyLightGlyph(IconEntry slot)
        {
            var motif=new DrawingVisual();
            using(var context=motif.RenderOpen())
            {
                context.DrawEllipse(Brushes.White,null,new Point(32,32),11,25);
                context.DrawEllipse(Brushes.White,null,new Point(32,32),25,11);
            }
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32);
            bitmap.Render(motif);bitmap.Freeze();
            slot.Update(Icons.Measure(bitmap),"light-glyph-fixture");
        }
        void CheckSingleShadow(string surface,Border tile)
        {
            var glyph=Elements<Image>(tile).FirstOrDefault();
            int shadows=Elements<Border>(tile).Count(b=>b.Background is RadialGradientBrush);
            Check(surface+" light glyph has exactly one shadow visual",
                glyph!=null&&glyph.Effect==null&&shadows==1&&Borderless(tile),new{shadows,artworkEffect=glyph?.Effect?.GetType().Name});
        }
        var outside=new Native.Point(c.WorkArea.Left+72,c.WorkArea.Top+110);
        var wake=c.WakeBounds;
        var wakePoint=new Native.Point((wake.Left+wake.Right)/2,(wake.Top+wake.Bottom)/2);
        var foreground=Native.GetForegroundWindow();
        Native.GetCursorPos(out var original);
        bool shellPopupOpened=false;
        var thumbnailFixtures=new List<Window>();
        int exit=0;
        try
        {
            var desktop=Path.Combine(output,"desktop");
            Directory.CreateDirectory(desktop);
            c.OverrideDesktopFolder=desktop;
            if(!shadowOnly)await CheckRunningDots(c,output,Check,Until);
            c.Store.Data.Entries.Clear();c.Store.Data.DockOrder.Clear();
            c.Store.Data.DoubleClickToOpen=true;
            if(shadowOnly){c.Store.Data.ReducedMotion=false;c.Store.Data.Appearance="light";Theme.Apply("light");}
            c.Store.Data.Mode="自定义";
            var entries=Enumerable.Range(1,2).Select(i=>
            {
                var path=Path.Combine(desktop,i==1?"field-test-1.cmd":$"field-test-{i}-a-long-file-name-used-to-verify-complete-hover-labels.cmd");
                File.WriteAllText(path,"@exit /b 0\r\n");
                return new Entry{Path=path,Name=$"Field test {i}",Source="desktop",Pinned=true};
            }).ToArray();
            c.Store.Data.Entries.AddRange(entries);
            c.Dock.Rebuild();c.Position();c.Expand();c.SettleDockMotion();c.Dock.UpdateLayout();
            var dockTiles=entries.Select(e=>Tile(c.Dock,e)).ToArray();
            Native.GetWindowRect(c.Dock.Handle,out var dock);
            var blankDock=new Native.Point(dock.Left+6,(dock.Top+dock.Bottom)/2);
            await Move(blankDock);
            if(shadowOnly)
            {
                ApplyLightGlyph(c.Icon(entries[0].Path,c.Layout.Icon));
                CheckSingleShadow("Dock",dockTiles[0]);
                await ShadowMotion("Dock",dockTiles,c.Dock.TileCenter(entries[0].Id),c.Dock.TileCenter(entries[1].Id),blankDock,false);
            }
            int launched=c.LaunchCount;
            var dockPoint=c.Dock.TileCenter(entries[0].Id);
            await Click(dockPoint);
            Check("Dock opens once on a single click with drawer double-click enabled",
                c.LaunchCount==launched+1&&Borderless(dockTiles[0]));
            await Click(blankDock);
            await Until(()=>dockTiles.All(t=>Shadow(t)==0));
            Check("Dock blank click produces no launch or residual shadow",
                c.LaunchCount==launched+1&&dockTiles.All(t=>Shadow(t)==0));
            await OpenDrawer();
            var drawer=c.Drawer!;
            var drawerTiles=entries.Select(e=>Tile(drawer,e)).ToArray();
            var drawerBlank=new Native.Point(drawer.BodyBounds.Right-70,drawer.BodyBounds.Top+280);
            if(shadowOnly)
            {
                ApplyLightGlyph(c.Icon(entries[0].Path,c.Layout.DrawerIcon));
                CheckSingleShadow("Drawer",drawerTiles[0]);
                await ShadowMotion("Drawer",drawerTiles,drawer.TileCenter(entries[0].Id),drawer.TileCenter(entries[1].Id),drawerBlank,true);
            }
            await Click(drawer.TileCenter(entries[0].Id));
            await Until(()=>Shadow(drawerTiles[0])==1);
            Check("Drawer mouse selection uses shadow only",Shadow(drawerTiles[0])==1&&Borderless(drawerTiles[0])&&drawer.SelectedCount==1);
            Grab("drawer-selected");
            await Click(drawer.TileCenter(entries[1].Id));
            await Until(()=>Shadow(drawerTiles[1])==1&&Shadow(drawerTiles[0])==0);
            Check("Drawer selection switches",Shadow(drawerTiles[0])==0&&Shadow(drawerTiles[1])==1&&Borderless(drawerTiles[1]));
            await Click(drawerBlank);
            await Until(()=>drawerTiles.All(t=>Shadow(t)==0));
            Check("Drawer blank click clears selection and highlights",drawer.SelectedCount==0&&drawerTiles.All(t=>Shadow(t)==0));
            await Click(drawer.TileCenter(entries[1].Id));
            Check("Drawer blank click breaks double-click sequence",c.LaunchCount==launched+1);
            await Click(drawer.TileCenter(entries[1].Id));
            await Until(()=>!drawer.IsVisible);
            Check("Drawer double-click launches once and clears selection",c.LaunchCount==launched+2&&drawer.SelectedCount==0);
            await OpenDrawer();
            Key(0x24);await Task.Delay(120);
            Check("Drawer keyboard navigation keeps selection shadow with no focus frame",drawerTiles.All(Borderless)&&drawerTiles.Any(t=>Shadow(t)==1),
                new{drawer.SelectedCount,focus=System.Windows.Input.Keyboard.FocusedElement?.GetType().Name});
            Key(0x75);await Task.Delay(120);
            Check("F6 transfers navigation to Dock",c.Dock.KeyboardActive);
            Key(0x75);await Task.Delay(120);
            Check("F6 returns navigation to Drawer",drawer.IsActive&&!c.Dock.KeyboardActive);
            if(shadowOnly)
            {
                c.Store.Data.DoubleClickToOpen=false;
                int singleLaunches=c.LaunchCount;
                await Click(drawer.TileCenter(entries[1].Id));
                await Until(()=>!drawer.IsVisible);
                Check("Drawer single-click mode starts once",c.LaunchCount==singleLaunches+1);
                c.Expand();c.SettleDockMotion();
                await Click(c.Dock.TileCenter(entries[1].Id));
                Check("Dock single-click mode starts once",c.LaunchCount==singleLaunches+2);
                return;
            }
            await Move(drawerBlank);
            var source=drawer.TileCenter(entries[0].Id);
            var target=drawer.TileCenter(entries[1].Id);
            Native.MovePointer(target.X,target.Y);
            await Task.Delay(650);
            Check("Drawer tooltip waits before opening",drawer.VisibleTileTip==null);
            await Until(()=>drawer.VisibleTileTip!=null);
            Check("Drawer tooltip shows the full file name",drawer.VisibleTileTip==Path.GetFileName(entries[1].Path),drawer.VisibleTileTip);
            Check("Search spans two icon edges",drawer.SearchWidth>c.Layout.DrawerIcon*2&&drawer.SearchWidth<2*(c.Layout.DrawerTile+c.Layout.GapX),drawer.SearchWidth);
            await Move(drawerBlank);
            Check("Moving away closes the tooltip",drawer.VisibleTileTip==null);
            int launchesBeforeDrag=c.LaunchCount;
            await Move(source);
            await Until(()=>drawer.VisibleTileTip!=null);
            Native.TestButton(true);await Task.Delay(45);
            await Move(target);
            Check("Dragging to an application has no open-with target, tip or frame",
                PointerSort.Active!=null&&c.InternalDragging&&drawer.DropTargetEntry==null&&drawer.VisibleTileTip==null&&drawerTiles.All(Borderless));
            Native.TestButton(false);await Task.Delay(170);
            await Click(drawerBlank);
            Check("Tile drop and subsequent blank click cannot open any application or menu",
                c.LaunchCount==launchesBeforeDrag&&drawer.IsVisible&&!c.MenuOpen&&!c.InternalDragging&&PointerSort.Active==null&&drawer.SelectedCount==0);
            source=drawer.TileCenter(entries[0].Id);
            target=drawer.TileCenter(entries[1].Id);
            await Move(source);
            Native.TestKey(0x10,true);
            try
            {
                Native.TestButton(true);await Task.Delay(45);
                await Move(target);
                Native.TestButton(false);await Task.Delay(220);
            }
            finally{Native.TestButton(false);Native.TestKey(0x10,false);}
            await Click(drawerBlank);
            Check("Modifier OLE drop on an application cannot start the application or shell chooser",
                c.LaunchCount==launchesBeforeDrag&&drawer.IsVisible&&!c.MenuOpen&&!c.InternalDragging&&PointerSort.Active==null);

            var payload=Path.Combine(desktop,"payload.txt");
            var folderPath=Path.Combine(desktop,"Folder target");
            var destination=Path.Combine(folderPath,"payload.txt");
            File.WriteAllText(payload,"real test file");
            Directory.CreateDirectory(folderPath);
            c.Store.Add(payload,false,source:"desktop");c.Store.Add(folderPath,false,source:"desktop");
            c.Changed();await Task.Delay(220);
            var fileEntry=c.Store.Data.Entries.Single(e=>e.Path==payload);
            var folderEntry=c.Store.Data.Entries.Single(e=>e.Path==folderPath);
            await Move(drawer.TileCenter(fileEntry.Id));
            Native.TestButton(true);await Task.Delay(45);
            await Move(drawer.TileCenter(folderEntry.Id));
            await Until(()=>drawer.DropTargetEntry?.Id==folderEntry.Id);
            Native.TestButton(false);
            await Until(()=>File.Exists(destination)&&!c.InternalDragging);
            Check("A legal file-to-folder drop still moves the real file without launching",
                !File.Exists(payload)&&File.ReadAllText(destination)=="real test file"&&c.LaunchCount==launchesBeforeDrag);
            await Until(()=>!c.InternalDropProtected&&!c.DrawerInteractionActive);

            c.CloseDrawer(force:true);await Until(()=>!drawer.IsVisible);
            await Move(outside);c.Collapse();await Until(()=>!c.Dock.IsVisible);

            c.SetSuppressWakeKey(Native.GraveWakeKey);
            Native.SetForegroundWindow(GetShellWindow());await Until(Native.DesktopSearchHasFocus);
            void Grave(bool down)=>Native.TestKey(Native.GraveWakeKey,down,scan:0x29);
            Grave(true);
            try
            {
                await Move(wakePoint);
                for(int i=0;i<5;i++){Grave(true);await Task.Delay(90);}
                Check("Desktop grave down and repeats withheld, wake remains suppressed",
                    c.WakeSuppressedByKey&&!Native.KeyHeld(Native.GraveWakeKey)&&!c.Dock.IsVisible);
            }
            finally{Grave(false);}
            await Move(outside);await Move(wakePoint);await Task.Delay(350);
            Check("Release leaves wake disabled and persists the configured binding",
                c.WakeSuppressedByKey&&!c.Dock.IsVisible&&new Store(Path.GetDirectoryName(c.Store.FilePath)).Data is
                    {WakeDisabled:true,SuppressWakeKey:Native.GraveWakeKey});
            Grave(true);Grave(false);
            await Until(()=>!c.WakeSuppressedByKey);
            await Move(outside);await Move(wakePoint);await Until(()=>c.Dock.IsVisible);
            Check("A second press restores wake",!c.WakeSuppressedByKey);
            await OpenDrawer();
            Native.TestKey(0x11,true);Key(0x46);Native.TestKey(0x11,false);
            await Until(()=>drawer.TextInputActive);
            var textKeys=new List<string>();
            drawer.AddHandler(System.Windows.Input.Keyboard.PreviewKeyDownEvent,new System.Windows.Input.KeyEventHandler((_,e)=>
            {
                var key=e.Key==System.Windows.Input.Key.ImeProcessed?e.ImeProcessedKey:e.Key;
                textKeys.Add(key.ToString());
            }),true);
            Grave(true);await Task.Delay(100);
            bool delivered=Native.KeyHeld(Native.GraveWakeKey);
            Grave(false);
            Check("Bound grave still reaches the drawer search field without toggling",delivered&&drawer.TextInputActive&&
                textKeys.Contains("Oem3")&&!c.WakeSuppressedByKey,new{delivered,textKeys});
            c.CloseDrawer(force:true);await Until(()=>!drawer.IsVisible);
            await Move(outside);c.Collapse();await Until(()=>!c.Dock.IsVisible);

            if(Environment.GetEnvironmentVariable("MACDOCK_MAINTENANCE_ONLY")=="1")return;

            var bar=FindWindow("Shell_TrayWnd",null);
            var taskbar=AutomationElement.FromHandle(bar);
            var button=taskbar.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"显示隐藏的图标"));
            if(button==null)throw new InvalidOperationException("Tray overflow button missing");
            const string overflowClass="TopLevelWindowForOverflowXamlIsland";
            bool TrayVisible()=>Native.IsWindowVisible(FindWindow(overflowClass,null));
            for(int round=0;round<2;round++)
            {
                ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                shellPopupOpened=true;
                await Until(TrayVisible);
                await Move(wakePoint);await Until(()=>c.Dock.IsVisible);
                Check($"Hidden-icon tray permits original hover wake ({round+1})",TrayVisible()&&c.Dock.IsVisible);
                if(round==0)Grab("tray-open-with-dock");
                Key(0x1B);await Until(()=>!TrayVisible());
                shellPopupOpened=false;
                await Move(outside);c.Collapse();await Until(()=>!c.Dock.IsVisible);
            }
            Native.GetWindowRect(bar,out var barBounds);
            await Move(new Native.Point(barBounds.Left+60,(barBounds.Top+barBounds.Bottom)/2));
            Native.TestRightButton(true);Native.TestRightButton(false);
            shellPopupOpened=true;
            await Until(()=>Native.TaskbarPopupVisible(c.WorkArea));
            await Move(wakePoint);await Task.Delay(220);
            Check("Taskbar context menu still blocks hover wake",Native.TaskbarPopupVisible(c.WorkArea)&&!c.Dock.IsVisible);
            Key(0x1B);await Until(()=>!Native.TaskbarPopupVisible(c.WorkArea));
            shellPopupOpened=false;
            var explorer=taskbar.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty,"Appid: Microsoft.Windows.Explorer"));
            if(explorer==null)throw new InvalidOperationException("Explorer taskbar button unavailable");
            var explorerBounds=explorer.Current.BoundingRectangle;
            var explorerCenter=new Native.Point((int)(explorerBounds.X+explorerBounds.Width/2),(int)(explorerBounds.Y+explorerBounds.Height/2));
            await Move(explorerCenter);
            Native.TestRightButton(true);Native.TestRightButton(false);
            shellPopupOpened=true;
            await Until(()=>Native.TaskbarPopupVisible(c.WorkArea));
            await Move(wakePoint);await Task.Delay(220);
            Check("Explorer jump list blocks hover wake",Native.TaskbarPopupVisible(c.WorkArea)&&!c.Dock.IsVisible);
            Key(0x1B);await Until(()=>!Native.TaskbarPopupVisible(c.WorkArea));
            shellPopupOpened=false;
            bool HasThumbnail()
            {
                bool found=false;
                Native.EnumWindows((h,_)=>!(found=Native.IsWindowVisible(h)&&Native.Class(h)=="TaskListThumbnailWnd"),0);
                return found;
            }
            async Task<bool> HoverForThumbnail(Native.Point point)
            {
                await Move(outside);await Move(point);
                for(int i=0;i<120&&!HasThumbnail();i++)await Task.Delay(20);
                return HasThumbnail();
            }
            bool thumbnail=await HoverForThumbnail(explorerCenter);
            if(!thumbnail)
            {
                // Explorer may have no eligible windows. Use two ordinary taskbar windows.
                for(int i=0;i<2;i++)
                {
                    var fixture=new Window{Title=$"MacDock thumbnail fixture {i+1}",ShowInTaskbar=true,
                        Width=220,Height=140,Left=80+i*45,Top=100+i*45};
                    thumbnailFixtures.Add(fixture);
                    fixture.Show();
                }
                await Task.Delay(350);
                var group=taskbar.FindAll(TreeScope.Descendants,new PropertyCondition(
                    AutomationElement.ControlTypeProperty,ControlType.Button)).Cast<AutomationElement>()
                    .FirstOrDefault(e=>!e.Current.IsOffscreen&&e.Current.Name.StartsWith("MacDock",StringComparison.Ordinal));
                if(group!=null)
                {
                    var bounds=group.Current.BoundingRectangle;
                    thumbnail=await HoverForThumbnail(new Native.Point((int)(bounds.X+bounds.Width/2),(int)(bounds.Y+bounds.Height/2)));
                }
            }
            if(thumbnail)Check("Taskbar thumbnail remains a blocking popup",Native.TaskbarPopupVisible(c.WorkArea)&&!c.Dock.IsVisible);
            else results.Add(new{name="Taskbar thumbnail",status="not-exercised",detail="Windows did not open a thumbnail on hover."});
            await Move(outside);await Move(wakePoint);await Until(()=>c.Dock.IsVisible);
            Check("Normal Dock wake returns when taskbar menu closes",c.Dock.IsVisible);
        }
        catch(Exception error){Check("error",false,error.ToString());exit=1;}
        finally
        {
            if(shellPopupOpened)Key(0x1B);
            foreach(var fixture in thumbnailFixtures)fixture.Close();
            Native.TestKey(Native.GraveWakeKey,false,scan:0x29);
            Native.TestKey(0x10,false);
            Native.TestButton(false);Native.TestRightButton(false);
            Native.SetCursorPos(original.X,original.Y);
            Native.SetForegroundWindow(foreground);
            File.WriteAllText(Path.Combine(output,"field-interaction.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            if(results.Any(r=>JsonSerializer.SerializeToElement(r).TryGetProperty("pass",out var pass)&&!pass.GetBoolean()))exit=1;
            Application.Current.Shutdown(exit);
        }
    }
}
#endif
