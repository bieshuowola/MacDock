#if MACDOCK_EXPERIENCE_CHECKS
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application=System.Windows.Application;
using Color=System.Windows.Media.Color;

namespace MacDock;

internal static class ExperienceChecks
{
    static string Output=>Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS") ?? AppContext.BaseDirectory;
    static async Task Key(byte key,params byte[] modifiers)
    {
        foreach(var modifier in modifiers)Native.TestKey(modifier,true);
        bool extended=key is >=0x21 and <=0x28 or 0x2D or 0x2E or 0x5D;
        Native.TestKey(key,true,extended);await Task.Delay(30);Native.TestKey(key,false,extended);
        foreach(var modifier in modifiers.Reverse())Native.TestKey(modifier,false);
        await Task.Delay(120);
    }

    static bool WaveAtRest(DockWindow dock)=>dock.MagnifyState.Split('|')[0].Split(',').All(s=>double.TryParse(s,out var scale)&&Math.Abs(scale-1)<.003);

    internal static async Task Run(Controller c)
    {
        var checks=new List<object>();
        void Check(string name,bool pass,object? detail=null)=>checks.Add(new{name,pass,detail});
        string prefix="MDQA"+Guid.NewGuid().ToString("N")[..8];
        string FilePath(string suffix)=>Path.Combine(Desktop.UserFolder,prefix+suffix);
        var target=Path.Combine(Output,prefix+"-C.txt");
        var a=FilePath("-A.txt");var b=FilePath("-B");var link=FilePath("-C.lnk");
        System.Windows.IDataObject? savedClipboard=null;
        bool clipboardChanged=false;
        Window? background=null;
        var input=new List<object>();
        ProcessInputEventHandler trace=(_,e)=>
        {
            if(e.StagingItem.Input is KeyEventArgs a&&a.IsDown)
                input.Add(new{a.Key,a.SystemKey,modifiers=Keyboard.Modifiers,focus=Keyboard.FocusedElement?.GetType().Name,a.Handled,c.MenuOpen,c.Busy,foreground=Native.Class(Native.GetForegroundWindow()),selected=c.Drawer?.SelectedCount});
        };
        InputManager.Current.PostProcessInput+=trace;
        try
        {
            if(!c.Store.FilePath.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Use an isolated profile.");
            background=new Window{Title="MacDock keyboard test fixture",Width=400,Height=200,ShowInTaskbar=false};
            background.Show();background.Activate();await Task.Delay(350);
            var pinned=c.Store.PinnedEntries().First(e=>!e.IsVirtual&&File.Exists(e.Path));
            c.Busy=true;c.Expand();c.SettleDockMotion();c.Dock.UpdateLayout();
            var tile=c.Dock.TileCenter(pinned.Id);
            var band=c.WakeBounds;
            Native.GetWindowRect(c.Dock.Handle,out var dock);
            int y=Math.Min(dock.Bottom-2,band.Bottom-2);
            if(y<Math.Max(dock.Top+1,band.Top+1))throw new InvalidOperationException("No overlapping Dock wake row.");
            Native.MovePointer(tile.X,y-150);c.Busy=false;c.Collapse();await Task.Delay(480);
            Native.MovePointer(tile.X,y);await Task.Delay(560);
            Check("real stationary pointer wakes into neutral icons",c.Dock.IsVisible&&WaveAtRest(c.Dock),new{c.Dock.MagnifyState,c.Diagnostics,popup=Native.TaskbarPopupVisible(c.WorkArea)});
            Native.MovePointer(tile.X+1,y);await Task.Delay(100);
            Check("one pixel wake jitter keeps neutral icons",WaveAtRest(c.Dock),c.Dock.MagnifyState);
            Native.MovePointer(tile.X+9,y-5);await Task.Delay(210);
            Check("real movement restores wave without a re-entry",c.Dock.IsVisible&&!WaveAtRest(c.Dock),c.Dock.MagnifyState);
            Native.MovePointer(tile.X,y-150);await Task.Delay(450);
            Check("moving out still hides Dock",!c.Dock.IsVisible);

            Native.MovePointer(tile.X,y);var wakeClock=System.Diagnostics.Stopwatch.StartNew();
            while(!c.Dock.IsVisible&&wakeClock.ElapsedMilliseconds<650)await Task.Delay(10);
            var phase=c.DockProgress;
            Native.MovePointer(tile.X+10,y-7);await Task.Delay(180);
            Check("first meaningful movement is accepted before the reveal finishes",phase<.97&&c.Dock.IsVisible&&!WaveAtRest(c.Dock),new{phase,c.Dock.MagnifyState,c.Diagnostics});
            Native.MovePointer(tile.X,y-150);await Task.Delay(430);

            File.WriteAllText(a,"MacDock test file");Directory.CreateDirectory(b);File.WriteAllText(target,"MacDock shortcut target");
            if(!string.Equals(Desktop.CreateShortcut(target),link,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected test shortcut destination.");
            c.RefreshDesktop();
            await Key(0x20,0x11,0x12);await Task.Delay(520);
            var drawer=c.Drawer!;
            Check("actual global shortcut opens keyboard-ready drawer",drawer.IsVisible&&drawer.Scroller.IsKeyboardFocused);
            await Key(0x46,0x11);
            var search=(TextBox)Keyboard.FocusedElement;
            search.Text=prefix;
            await Task.Delay(150);
            await Key(0x41,0x11);await Key(0x2E);
            Check("Ctrl+A and Delete edit search text only",search.Text.Length==0&&File.Exists(a)&&File.Exists(link));
            search.Text=prefix;await Task.Delay(150);
            await Key(0x1B);await Key(0x24);
            var first=drawer.Selection.Single();
            await Key(0x27,0x10);
            Check("Shift+Right selects contiguous items",drawer.Selection.Count==2&&drawer.Selection.Contains(first),drawer.Selection.Count);
            await Key(0x27,0x11);await Key(0x20);
            Check("Ctrl+arrow and Space extend selection",drawer.Selection.Count==3,drawer.Selection.Count);
            await Key(0x1B);
            Check("Escape clears selection before closing drawer",drawer.IsVisible&&drawer.Selection.Count==0);

            await Key(0x24);
            int displayed=0;ShellMenu.MenuDisplayed=()=>{displayed++;_=Task.Run(async()=>{await Task.Delay(180);Native.TestKey(0x1B,true);Native.TestKey(0x1B,false);});};
            await Key(0x5D);await Task.Delay(240);
            Check("Windows context-menu key opens the native selected item menu",displayed==1&&drawer.Scroller.IsKeyboardFocused,new{displayed,drawer.Scroller.IsKeyboardFocused});
            Native.PostMessage(drawer.Handle,0x007B,drawer.Handle,-1);await Task.Delay(440);
            Check("Windows keyboard context message opens the item menu",displayed==2&&drawer.Scroller.IsKeyboardFocused);
            ShellMenu.MenuDisplayed=null;
            savedClipboard=System.Windows.Clipboard.GetDataObject();clipboardChanged=true;
            await Key(0x41,0x11);await Key(0x43,0x11);
            var copied=ShellOps.ReadFileClipboard();
            Check("Ctrl+C copies the selected real files, folder and shortcut",!copied.Move&&copied.Paths.Length==3&&copied.Paths.Contains(link,StringComparer.OrdinalIgnoreCase));
            await Key(0x58,0x11);
            var cut=ShellOps.ReadFileClipboard();
            Check("Ctrl+X puts cut file objects on the OLE clipboard",cut.Move&&cut.Paths.Length==3);
            var outside=Path.Combine(Output,prefix+"-Paste.txt");File.WriteAllText(outside,"MacDock paste test");
            ShellOps.SetFileClipboard(new[]{outside},false);await Key(0x56,0x11);
            Check("Ctrl+V copies into the real desktop and updates drawer",File.Exists(FilePath("-Paste.txt"))&&drawer.SortTiles(false).Any(x=>((Entry)x.Tag).Path==FilePath("-Paste.txt")));

            var before=c.Store.PinnedEntries().Select(e=>e.Id).ToArray();
            await Key(0x75);await Task.Delay(160);
            Check("F6 focuses Dock without adding another global shortcut",c.Dock.KeyboardActive&&Native.GetForegroundWindow()==c.Dock.Handle,new{c.Dock.KeyboardActive,drawer.IsVisible,focus=Keyboard.FocusedElement?.GetType().Name,foreground=Native.Class(Native.GetForegroundWindow())});
            await Key(0x2E,0x10);
            Check("Dock Shift+Delete only unpins and persists",File.Exists(pinned.Path)&&c.Store.PinnedEntries().Select(e=>e.Id).SequenceEqual(before.Skip(1))&&new Store(Path.GetDirectoryName(c.Store.FilePath)).PinnedEntries().Count==before.Length-1);
            await Key(0x75);await Task.Delay(180);
            Check("F6 returns keyboard focus to drawer",drawer.Scroller.IsKeyboardFocused);

            void HoverFile(string path)
            {
                drawer.ClearSelection();drawer.FocusGrid();
                var view=drawer.SortTiles(false).Single(v=>((Entry)v.Tag).Path==path);
                view.BringIntoView();drawer.UpdateLayout();
                var at=view.PointToScreen(new System.Windows.Point(view.ActualWidth/2,view.ActualHeight/2));
                Native.MovePointer((int)at.X,(int)at.Y);
            }
            HoverFile(a);await Task.Delay(180);
            Check("hover highlights a real file without creating a selection",drawer.SelectedCount==0&&drawer.SortTiles(false).Any(v=>((Entry)v.Tag).Path==a&&v.IsMouseOver));
            await Key(0x43,0x11);
            Check("hover-only Ctrl+C copies that file",ShellOps.ReadFileClipboard().Paths.SequenceEqual(new[]{a}));
            await Key(0x58,0x11);
            Check("hover-only Ctrl+X cuts that file without moving it",ShellOps.ReadFileClipboard().Move&&ShellOps.ReadFileClipboard().Paths.SequenceEqual(new[]{a})&&File.Exists(a));
            await Key(0x71);
            if(Keyboard.FocusedElement is TextBox renameBox)
            {
                renameBox.Text=prefix+"-Renamed.txt";
                await Key(0x0D);await Task.Delay(220);
            }
            var renamed=FilePath("-Renamed.txt");
            Check("F2 renames the actual file and refreshes drawer",!File.Exists(a)&&File.Exists(renamed)&&c.Store.Data.Entries.Any(e=>e.Path==renamed));
            if(File.Exists(renamed))
            {
                HoverFile(renamed);await Task.Delay(180);
                await Key(0x2E,0x10);await Task.Delay(230);
                bool confirmation=Native.Class(Native.GetForegroundWindow())=="#32770";
                if(confirmation)await Key(0x1B);
                Check("Shift+Delete requires native confirmation; Escape preserves file",confirmation&&File.Exists(renamed));
                if(confirmation&&File.Exists(renamed))
                {
                    await Key(0x2E,0x10);
                    var prompt=Native.GetForegroundWindow();
                    var confirm=Native.Class(prompt)=="#32770"?FindDeleteConfirmation(prompt):null;
                    if(confirm is System.Windows.Point at)
                    {
                        Native.MovePointer((int)at.X,(int)at.Y);await Task.Delay(70);
                        Native.TestButton(true);await Task.Delay(30);Native.TestButton(false);
                        await Task.Delay(530);
                    }
                    else if(Native.Class(prompt)=="#32770")await Key(0x1B);
                    Check("confirming native Shift+Delete removes only the chosen temporary file",confirm!=null&&!File.Exists(renamed)&&File.Exists(target)&&Directory.Exists(b));
                }
            }

            HoverFile(link);await Task.Delay(180);
            await Key(0x2E);await Task.Delay(430);
            Check("Delete recycles the desktop shortcut and retains its target",!File.Exists(link)&&File.Exists(target)&&c.Store.Data.Entries.All(e=>e.Path!=link));
        }
        catch(Exception error){Check("exception",false,error.ToString());}
        finally
        {
            ShellMenu.MenuDisplayed=null;
            InputManager.Current.PostProcessInput-=trace;
            background?.Close();
            if(clipboardChanged){try{if(savedClipboard!=null)System.Windows.Clipboard.SetDataObject(savedClipboard,true);else System.Windows.Clipboard.Clear();}catch(Exception e){Check("restore clipboard",false,e.GetType().Name);}}
            foreach(var file in Directory.EnumerateFiles(Desktop.UserFolder,prefix+"*"))File.Delete(file);
            if(Directory.Exists(b))Directory.Delete(b);
            File.WriteAllText(Path.Combine(Output,"experience.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
            File.WriteAllText(Path.Combine(Output,"keyboard-trace.json"),JsonSerializer.Serialize(input,new JsonSerializerOptions{WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}}));
            bool allPass=checks.All(item=>(bool)item.GetType().GetProperty("pass")!.GetValue(item)!);
            Environment.ExitCode=allPass?0:1;
            Application.Current.Shutdown(allPass?0:1);
            Environment.Exit(allPass?0:1);
        }
    }

    static System.Windows.Point? FindDeleteConfirmation(nint prompt)
    {
        var window=System.Windows.Automation.AutomationElement.FromHandle(prompt);
        var type=new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ControlTypeProperty,System.Windows.Automation.ControlType.Button);
        foreach(System.Windows.Automation.AutomationElement button in window.FindAll(System.Windows.Automation.TreeScope.Descendants,type))
        {
            var name=button.Current.Name.Replace("&","").Trim();
            if(name is "是" or "是(Y)" or "Yes" or "删除" or "Delete")
            {
                var r=button.Current.BoundingRectangle;
                if(!r.IsEmpty)return new System.Windows.Point(r.Left+r.Width/2,r.Top+r.Height/2);
            }
        }
        return null;
    }

    internal static async Task Capture(Controller c)
    {
        Window? backdrop = null;
        try
        {
        Directory.CreateDirectory(Output);
        var area=c.WorkArea;
        backdrop=new Window{Title="MacDock 演示背景",ShowInTaskbar=false,Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=area.Width/c.Scale,Height=area.Height/c.Scale};
        var gradient=new LinearGradientBrush(Color.FromRgb(61,88,134),Color.FromRgb(193,164,157),24);
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(118,149,179),.46));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(197,187,177),.77));
        backdrop.Content=new Grid{Background=gradient};
        backdrop.Loaded+=(_,_)=>Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(backdrop).Handle,0,area.Left,area.Top,area.Width,area.Height,0x10);
        backdrop.Show();
        c.Store.Data.Appearance="light";Theme.Apply("light");c.Store.Data.DockOrder.Clear();c.Store.Data.Entries.Clear();
        foreach(var item in Desktop.DefaultPins())
        {
            if(!item.Path.StartsWith("::",StringComparison.Ordinal))continue;
            var name=item.Path.Contains("59031A47",StringComparison.OrdinalIgnoreCase)?"用户文件夹":item.Name;
            c.Store.Data.Entries.Add(new Entry{Path=item.Path,Name=name,Source="system",Pinned=true});
        }
        var windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach(var (file,name,pinned) in new[]{("explorer.exe","文件资源管理器",true),(@"System32\notepad.exe","记事本",true),(@"System32\calc.exe","计算器",true),(@"System32\mspaint.exe","画图",false),(@"System32\charmap.exe","字符映射表",false),(@"System32\write.exe","写字板",false),(@"System32\Magnify.exe","放大镜",false)})
        {
            var path=Path.Combine(windows,file);if(File.Exists(path))c.Store.Data.Entries.Add(new Entry{Path=path,Name=name,Source="desktop",AutoName=false,Pinned=pinned});
        }
        foreach(var (name,extension) in new[]{("工作计划","txt"),("配色参考","png"),("会议记录","txt"),("素材索引","txt"),("草稿","txt"),("照片",""),("项目资料",""),("参考文档",""),("待整理",""),("共享文件",""),("学习笔记","txt"),("流程说明","txt")})
        {
            var path=Path.Combine(Output,extension==""?name:$"{name}.{extension}");
            if(extension=="")Directory.CreateDirectory(path);
            else if(extension=="png"){using var sample=new System.Drawing.Bitmap(32,32);sample.Save(path);}
            else File.WriteAllText(path,name);
            c.Store.Data.Entries.Add(new Entry{Path=path,Name=name,Source="desktop",AutoName=false});
        }
        c.Store.Data.DrawerRows=4;
        c.Dock.Rebuild();c.Position();c.Expand();c.SettleDockMotion();c.Dock.UpdateLayout();
        Native.GetWindowRect(c.Dock.Handle,out var dock);
        var dockRect=new System.Drawing.Rectangle(dock.Left-36,dock.Top-28,dock.Right-dock.Left+72,Math.Min(dock.Bottom+30,area.Bottom)-dock.Top+28);
        var dockFramesDir=Path.Combine(Output,"dock_frames");Directory.CreateDirectory(dockFramesDir);
        var pinnedList=c.Store.PinnedEntries().ToList();
        var firstPt=c.Dock.TileCenter(pinnedList.First().Id);
        var lastPt=c.Dock.TileCenter(pinnedList.Last().Id);
        int dIdx=0;
        Native.MovePointer(area.Left+24,area.Top+24);await Task.Delay(160);
        var restPath=Path.Combine(dockFramesDir,$"frame_{dIdx++:D3}.png");SaveTo(restPath,dockRect);
        for(int i=0;i<7;i++)File.Copy(restPath,Path.Combine(dockFramesDir,$"frame_{dIdx++:D3}.png"),true);
        for(int i=0;i<45;i++)
        {
            double t=i/44.0;
            int px=(int)Math.Round(firstPt.X-44+(lastPt.X-firstPt.X+88)*t);
            Native.MovePointer(px,firstPt.Y);
            await Task.Delay(20);
            SaveTo(Path.Combine(dockFramesDir,$"frame_{dIdx++:D3}.png"),dockRect);
        }
        Native.MovePointer(area.Left+24,area.Top+24);
        for(int i=0;i<22;i++){await Task.Delay(20);SaveTo(Path.Combine(dockFramesDir,$"frame_{dIdx++:D3}.png"),dockRect);}

        var pin=pinnedList.Skip(2).FirstOrDefault() ?? pinnedList.First();
        var point=c.Dock.TileCenter(pin.Id);
        Native.MovePointer(point.X-12,point.Y);await Task.Delay(40);Native.MovePointer(point.X,point.Y);await Task.Delay(190);
        Save("dock.png",dockRect);
        Native.MovePointer(area.Left+24,area.Top+24);await Task.Delay(140);
        c.ToggleDrawer();
        await Task.Delay(60);
        Native.GetWindowRect(c.Drawer!.Handle,out var drawer);
        var full=new System.Drawing.Rectangle(drawer.Left-22,drawer.Top-14,drawer.Right-drawer.Left+44,Math.Min(dock.Bottom+18,area.Bottom)-drawer.Top+14);
        var cycleDir=Path.Combine(Output,"drawer_frames");Directory.CreateDirectory(cycleDir);
        int cIdx=0;
        var openPhases=Enumerable.Range(0,18).Select(i=>{double t=i/17.0;return 0.04+0.94*(1.0-Math.Pow(1.0-t,2.2));}).ToArray();
        foreach(var p in openPhases){c.PreviewDrawerPhase(p);await Task.Delay(20);SaveTo(Path.Combine(cycleDir,$"frame_{cIdx++:D3}.png"),full);}
        c.SettleDrawerMotion();await Task.Delay(160);
        var settledPath=Path.Combine(cycleDir,$"frame_{cIdx++:D3}.png");SaveTo(settledPath,full);
        Save("drawer.png",full);
        for(int hold=0;hold<29;hold++)File.Copy(settledPath,Path.Combine(cycleDir,$"frame_{cIdx++:D3}.png"),true);
        for(int i=0;i<18;i++)
        {
            double t=i/17.0;
            double p=0.98-0.94*Math.Pow(t,1.6);
            c.PreviewDrawerPhase(p);await Task.Delay(20);SaveTo(Path.Combine(cycleDir,$"frame_{cIdx++:D3}.png"),full);
        }
        c.PreviewDrawerPhase(0.0);await Task.Delay(60);
        var closedPath=Path.Combine(cycleDir,$"frame_{cIdx++:D3}.png");SaveTo(closedPath,full);
        for(int hold=0;hold<9;hold++)File.Copy(closedPath,Path.Combine(cycleDir,$"frame_{cIdx++:D3}.png"),true);
        }
        catch(Exception ex){Program.Log(ex);File.WriteAllText(Path.Combine(Output,"capture-error.txt"),ex.ToString());Environment.ExitCode=1;}
        finally{backdrop?.Close();int code=Environment.ExitCode;Application.Current.Shutdown(code);Environment.Exit(code);}
    }

    static void Save(string name,System.Drawing.Rectangle area)=>SaveTo(Path.Combine(Output,name),area);
    static void SaveTo(string path,System.Drawing.Rectangle area)
    {
        using var bitmap=new System.Drawing.Bitmap(area.Width,area.Height);
        using var graphics=System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(area.Location,System.Drawing.Point.Empty,bitmap.Size);
        bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);
    }

    internal static async Task DragE2E(Controller c)
    {
        Window? backdrop=null;
        Window? otherWindow=null;
        bool pass=false;
        string detail="";
        var desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var testRoot=Path.Combine(Output,"drag-e2e");
        try
        {
            Directory.CreateDirectory(Output);
            if(Directory.Exists(testRoot))Directory.Delete(testRoot,true);
            Directory.CreateDirectory(testRoot);
            var folderPath=Path.Combine(testRoot,"sourcemod");
            Directory.CreateDirectory(folderPath);

            var fileNames=new[]{"genie-0.65.png","genie-0.75.png","genie-0.85.png"};
            var filePaths=new List<string>();
            var colors=new[]{System.Drawing.Color.CornflowerBlue,System.Drawing.Color.MediumSeaGreen,System.Drawing.Color.Coral};
            for(int i=0;i<fileNames.Length;i++)
            {
                var fp=Path.Combine(testRoot,fileNames[i]);
                using var bmp=new System.Drawing.Bitmap(64,64);
                using(var g=System.Drawing.Graphics.FromImage(bmp))g.Clear(colors[i]);
                bmp.Save(fp,System.Drawing.Imaging.ImageFormat.Png);
                filePaths.Add(fp);
            }

            var area=c.WorkArea;
            otherWindow=new Window{Title="后台窗口",Width=240,Height=120,Left=area.Left+20,Top=area.Top+20,ShowInTaskbar=false};
            otherWindow.Show();
            backdrop=new Window{Title="MacDock 拖拽测试背景",ShowInTaskbar=false,Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=area.Width/c.Scale,Height=area.Height/c.Scale};
            var gradient=new LinearGradientBrush(Color.FromRgb(61,88,134),Color.FromRgb(193,164,157),24);
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(118,149,179),.46));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(197,187,177),.77));
            backdrop.Content=new Grid{Background=gradient};
            backdrop.Loaded+=(_,_)=>Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(backdrop).Handle,0,area.Left,area.Top,area.Width,area.Height,0x10);
            backdrop.Show();

            c.Store.Data.Appearance="light";
            c.Store.Data.DoubleClickToOpen=false;
            c.Store.Data.Mode="自定义";
            c.Store.Data.DockOrder.Clear();
            c.Store.Data.Entries.Clear();
            Theme.Apply("light");
            foreach(var item in Desktop.DefaultPins().Take(3))
                c.Store.Data.Entries.Add(new Entry{Path=item.Path,Name=item.Name,Source="system",Pinned=true});
            var picEntries=new List<Entry>();
            for(int i=0;i<filePaths.Count;i++)
            {
                var ent=new Entry{Path=filePaths[i],Name=Path.GetFileNameWithoutExtension(fileNames[i]),Source="desktop",AutoName=false};
                c.Store.Data.Entries.Add(ent);
                picEntries.Add(ent);
            }
            var folderEntry=new Entry{Path=folderPath,Name="sourcemod",Source="desktop",AutoName=false};
            c.Store.Data.Entries.Add(folderEntry);

            c.Dock.Rebuild();c.Position();c.SetExternalFileDrag(true);c.Expand();c.SettleDockMotion();c.Dock.UpdateLayout();
            c.ToggleDrawer();
            await Task.Delay(120);
            c.SettleDrawerMotion();
            await Task.Delay(250);
            var drawer=c.Drawer!;

            // Ctrl multi-select 3 images in single-click mode
            Native.TestKey(0x11,true);
            await Task.Delay(80);
            foreach(var ent in picEntries)
            {
                var pt=drawer.TileCenter(ent.Id);
                Native.MovePointer(pt.X,pt.Y);
                await Task.Delay(60);
                Native.TestButton(true);
                await Task.Delay(40);
                Native.TestButton(false);
                await Task.Delay(90);
            }
            Native.TestKey(0x11,false);
            await Task.Delay(120);

            int selectedCount=drawer.Selection.Count;
            if(selectedCount!=3)
                throw new InvalidOperationException($"Ctrl multi-select failed: expected 3 selected, got {selectedCount}");

            // Drag from first selected image to target folder center
            var startPt=drawer.TileCenter(picEntries[0].Id);
            var endPt=drawer.TileCenter(folderEntry.Id);
            Native.MovePointer(startPt.X,startPt.Y);
            await Task.Delay(80);
            Native.TestButton(true);
            await Task.Delay(60);
            for(int step=1;step<=18;step++)
            {
                double t=step/18.0;
                int x=(int)Math.Round(startPt.X+(endPt.X-startPt.X)*t);
                int y=(int)Math.Round(startPt.Y+(endPt.Y-startPt.Y)*t);
                Native.MovePointer(x,y);
                await Task.Delay(28);
            }
            await Task.Delay(220);

            bool draggingMulti=PointerSort.Active!=null&&PointerSort.Active.IsMultiDrag&&PointerSort.Active.IsDraggingDrawer;
            string? dropTargetId=drawer.DropTargetEntry?.Id;
            Native.GetWindowRect(drawer.Handle,out var dr);
            Native.GetWindowRect(c.Dock.Handle,out var dk);
            var shotArea=new System.Drawing.Rectangle(dr.Left-22,dr.Top-14,dr.Right-dr.Left+44,Math.Min(dk.Bottom+18,area.Bottom)-dr.Top+14);
            SaveTo(Path.Combine(desktop,"drag-verify.png"),shotArea);
            Save("drag-verify.png",shotArea);

            if(!draggingMulti||dropTargetId!=folderEntry.Id)
            {
                Native.TestButton(false);
                throw new InvalidOperationException($"Drag state mismatch: draggingMulti={draggingMulti}, dropTarget={dropTargetId}, expected={folderEntry.Id}");
            }

            Native.TestButton(false);
            await Task.Delay(550);

            bool allMoved=fileNames.All(fn=>File.Exists(Path.Combine(folderPath,fn))&&!File.Exists(Path.Combine(testRoot,fn)));
            bool entriesRefreshed=picEntries.All(p=>c.Store.Data.Entries.All(x=>x.Path!=p.Path))&&drawer.Selection.Count==0&&drawer.DropTargetEntry==null;
            bool stayedOpen=drawer.IsVisible&&!c.DrawerClosing&&c.Dock.IsVisible;
            if(!allMoved||!entriesRefreshed||!stayedOpen)
                throw new InvalidOperationException($"Archive assertion failed: allMoved={allMoved}, entriesRefreshed={entriesRefreshed}, stayedOpen={stayedOpen}, drawerVisible={drawer.IsVisible}, drawerClosing={c.DrawerClosing}, dockVisible={c.Dock.IsVisible}");

            pass=true;
            detail=$"selected={selectedCount}, draggingMulti={draggingMulti}, dropTarget={folderEntry.Name}, allMoved={allMoved}, entriesRefreshed={entriesRefreshed}, stayedOpen={stayedOpen}";
        }
        catch(Exception ex)
        {
            Program.Log(ex);
            detail=ex.ToString();
        }
        finally
        {
            Native.TestKey(0x11,false);
            Native.TestButton(false);
            File.WriteAllText(Path.Combine(Output,"drag-e2e-result.json"),JsonSerializer.Serialize(new{pass,detail},new JsonSerializerOptions{WriteIndented=true}));
            backdrop?.Close();
            otherWindow?.Close();
            Environment.ExitCode=pass?0:1;
            Application.Current.Shutdown(pass?0:1);
            Environment.Exit(pass?0:1);
        }
    }

    internal static async Task ExternalDropAndPin(Controller c)
    {
        Window? backdrop=null;
        Window? otherWindow=null;
        bool pass=false;
        string detail="";
        var desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var rootDir=Path.Combine(Output,"ext-drop-pin");
        try
        {
            Directory.CreateDirectory(Output);
            if(Directory.Exists(rootDir))Directory.Delete(rootDir,true);
            var testDesktop=Path.Combine(rootDir,"test-desktop");
            var outsideSrc=Path.Combine(rootDir,"outside-src");
            Directory.CreateDirectory(testDesktop);
            Directory.CreateDirectory(outsideSrc);
            var srcFile=Path.Combine(outsideSrc,"external-drop-item.txt");
            File.WriteAllText(srcFile,"external ole drop test");

            var area=c.WorkArea;
            backdrop=new Window{Title="MacDock 测试背景",ShowInTaskbar=false,Topmost=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=area.Width/c.Scale,Height=area.Height/c.Scale};
            var gradient=new LinearGradientBrush(Color.FromRgb(61,88,134),Color.FromRgb(193,164,157),24);
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(118,149,179),.46));
            gradient.GradientStops.Add(new GradientStop(Color.FromRgb(197,187,177),.77));
            backdrop.Content=new Grid{Background=gradient};
            backdrop.Loaded+=(_,_)=>Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(backdrop).Handle,0,area.Left,area.Top,area.Width,area.Height,0x10);
            backdrop.Show();

            otherWindow=new Window{Title="外部测试窗口",Width=260,Height=140,Left=area.Left+60,Top=area.Top+60,Topmost=true,ShowInTaskbar=false,Background=Brushes.White};
            bool dragStarted=false;
            DragDropEffects dragEffect=DragDropEffects.None;
            otherWindow.PreviewMouseMove+=(_,a)=>
            {
                if(dragStarted||a.LeftButton!=MouseButtonState.Pressed)return;
                dragStarted=true;
                var data=new DataObject(DataFormats.FileDrop,new[]{srcFile});
                dragEffect=System.Windows.DragDrop.DoDragDrop(otherWindow,data,DragDropEffects.Move|DragDropEffects.Copy);
            };
            otherWindow.Show();

            c.OverrideDesktopFolder=testDesktop;
            c.Store.Data.Appearance="light";
            c.Store.Data.DoubleClickToOpen=false;
            c.Store.Data.DockOrder.Clear();
            c.Store.Data.Entries.Clear();
            Theme.Apply("light");
            foreach(var item in Desktop.DefaultPins().Take(3))
                c.Store.Data.Entries.Add(new Entry{Path=item.Path,Name=item.Name,Source="system",Pinned=true});
            c.Dock.Rebuild();c.Position();c.SetExternalFileDrag(true);c.Expand();c.SettleDockMotion();c.Dock.UpdateLayout();

            otherWindow.Activate();
            await Task.Delay(80);
            // 1. External OLE drag from otherWindow onto Dock general area (Gateway button / Dock edge) -> drops into desktop
            var srcCenter=otherWindow.PointToScreen(new Point(otherWindow.ActualWidth/2,otherWindow.ActualHeight/2));
            var dockTarget=c.Dock.GatewayCenter;
            await Task.Run(()=>
            {
                Native.MovePointer((int)srcCenter.X,(int)srcCenter.Y);
                Thread.Sleep(80);
                Native.TestButton(true);
                Thread.Sleep(60);
                Native.MovePointer((int)srcCenter.X+20,(int)srcCenter.Y+15);
                Thread.Sleep(150);
                for(int step=1;step<=12;step++)
                {
                    double t=step/12.0;
                    Native.MovePointer((int)Math.Round(srcCenter.X+(dockTarget.X-srcCenter.X)*t),(int)Math.Round(srcCenter.Y+(dockTarget.Y-srcCenter.Y)*t));
                    Thread.Sleep(25);
                }
                Thread.Sleep(180);
                Native.TestButton(false);
                Thread.Sleep(400);
            });

            var landedPath=Path.Combine(testDesktop,"external-drop-item.txt");
            for(int w=0;w<25&&(!File.Exists(landedPath)||!c.Store.Data.Entries.Any(x=>string.Equals(x.Path,landedPath,StringComparison.OrdinalIgnoreCase)));w++)await Task.Delay(80);
            var hitHwnd=Native.WindowFromPoint(dockTarget);
            bool droppedToDesktop=dragStarted&&File.Exists(landedPath)&&!File.Exists(srcFile)&&c.Store.Data.Entries.Any(x=>string.Equals(x.Path,landedPath,StringComparison.OrdinalIgnoreCase));
            if(!droppedToDesktop)
                throw new InvalidOperationException($"External Dock drop failed: dragStarted={dragStarted}, effect={dragEffect}, dock={c.Dock.Handle}, hit={(long)hitHwnd}, dockPt=({dockTarget.X},{dockTarget.Y}), dockVis={c.Dock.IsVisible}, landed={File.Exists(landedPath)}, srcGone={!File.Exists(srcFile)}");

            // Self-drop loop prevention: dropping file already in testDesktop without copy must be silently ignored
            bool selfLoopIgnored=!c.DropFilesToDesktop(new[]{landedPath},copy:false)&&File.Exists(landedPath);
            if(!selfLoopIgnored)
                throw new InvalidOperationException("Self-drop loop prevention failed");

            // Hide otherWindow below backdrop before opening drawer & taking screenshot
            otherWindow.Topmost=false;
            Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(otherWindow).Handle,new nint(1),0,0,0,0,0x13);

            // 2. Open drawer and verify footer toolbar + pin lock behavior
            c.ToggleDrawer();
            await Task.Delay(120);
            c.SettleDrawerMotion();
            await Task.Delay(250);
            var drawer=c.Drawer!;

            int footerCount=drawer.FooterActions.Children.Count;
            if(footerCount!=2)
                throw new InvalidOperationException($"Footer toolbar count expected 2 (pin + addFile), got {footerCount}");
            string TipText(FrameworkElement el)=>(el.ToolTip as System.Windows.Controls.ToolTip)?.Content as string ?? el.ToolTip as string ?? "";
            string tipUnlocked=TipText(drawer.PinButton);
            string tipPlus=TipText(drawer.AddFileButton);
            if(tipUnlocked!="锁定抽屉（防止点击外部自动收起）"||string.IsNullOrEmpty(tipPlus))
                throw new InvalidOperationException($"Unexpected tooltips: pin='{tipUnlocked}', plus='{tipPlus}'");

            // Click pin button to enable DrawerPinned
            drawer.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(180);

            bool pinnedOn=c.DrawerPinned&&drawer.PinButton.FocusVisualStyle==null&&drawer.PinButton.BorderThickness.Left==0&&drawer.PinButton.BorderBrush==Brushes.Transparent&&drawer.PinButton.Effect!=null&&drawer.PinButton.Background!=Brushes.Transparent;
            string tipLocked=TipText(drawer.PinButton);
            if(!pinnedOn||tipLocked!="取消锁定抽屉")
                throw new InvalidOperationException($"Pin lock visual/tooltip failed: pinnedOn={pinnedOn}, tipLocked='{tipLocked}'");

            // Capture screenshot showing landed file & locked pin button shadow
            Native.GetWindowRect(drawer.Handle,out var dr);
            Native.GetWindowRect(c.Dock.Handle,out var dk);
            var shotArea=new System.Drawing.Rectangle(dr.Left-22,dr.Top-14,dr.Right-dr.Left+44,Math.Min(dk.Bottom+18,area.Bottom)-dr.Top+14);
            SaveTo(Path.Combine(desktop,"pin-drop-verify.png"),shotArea);
            Save("pin-drop-verify.png",shotArea);

            // 3. Marquee rubber-band selection in drawer blank area
            var tilePt=drawer.TileCenter(c.Store.Data.Entries.First(x=>string.Equals(x.Path,landedPath,StringComparison.OrdinalIgnoreCase)).Id);
            int blankX=Math.Min(dr.Right-60,tilePt.X+160);
            int blankY=tilePt.Y+90;
            Native.MovePointer(blankX,blankY);await Task.Delay(60);
            Native.TestButton(true);await Task.Delay(50);
            Native.MovePointer(tilePt.X-10,tilePt.Y-10);await Task.Delay(120);
            Native.TestButton(false);await Task.Delay(120);
            bool marqueeSelected=drawer.Selection.Count>=1;
            if(!marqueeSelected)
                throw new InvalidOperationException($"Marquee selection failed: selected={drawer.Selection.Count}");

            // Activate otherWindow & trigger focus loss -> drawer MUST stay visible while pinned
            otherWindow.Activate();
            c.DrawerFocusChanged();
            await Task.Delay(350);
            bool stayedOpenWhilePinned=drawer.IsVisible&&!c.DrawerClosing&&c.Dock.IsVisible;
            if(!stayedOpenWhilePinned)
                throw new InvalidOperationException($"Drawer closed despite DrawerPinned=true: visible={drawer.IsVisible}, closing={c.DrawerClosing}");

            // Test clicking top-right close button (×) while pinned resets DrawerPinned and clears shadow
            if(!c.DrawerPinned)throw new InvalidOperationException("Expected DrawerPinned=true before testing close button");
            drawer.CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(350);
            bool closeResetsPin=!c.DrawerPinned&&drawer.PinButton.Effect==null&&(!drawer.IsVisible||c.DrawerClosing);
            if(!closeResetsPin)
                throw new InvalidOperationException($"Close button did not reset pin state: DrawerPinned={c.DrawerPinned}, hasEffect={drawer.PinButton.Effect!=null}");

            // Re-open, pin and unlock to verify focus loss closes drawer
            for(int w=0;w<40&&(c.Drawer?.IsVisible==true||c.DrawerMotionRunning);w++)await Task.Delay(30);
            c.ToggleDrawer();
            for(int w=0;w<60&&(c.Drawer?.IsVisible!=true||c.DrawerMotionRunning||c.DrawerProgress<0.99);w++)await Task.Delay(30);
            drawer.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(120);
            drawer.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await Task.Delay(120);
            bool unlockedOk=!c.DrawerPinned&&drawer.PinButton.Effect==null&&TipText(drawer.PinButton)=="锁定抽屉（防止点击外部自动收起）";
            if(!unlockedOk)
                throw new InvalidOperationException($"Pin unlock failed: DrawerPinned={c.DrawerPinned}, hasEffect={drawer.PinButton.Effect!=null}");

            otherWindow.Activate();
            c.DrawerFocusChanged();
            await Task.Delay(350);
            bool closedAfterUnlock=!drawer.IsVisible||c.DrawerClosing;
            if(!closedAfterUnlock)
                throw new InvalidOperationException("Drawer failed to close on focus loss after unlocking pin");

            pass=true;
            detail=$"droppedToDesktop={droppedToDesktop}, selfLoopIgnored={selfLoopIgnored}, marqueeSelected={marqueeSelected}, footerCount={footerCount}, pinnedOn={pinnedOn}, stayedOpenWhilePinned={stayedOpenWhilePinned}, closeResetsPin={closeResetsPin}, unlockedOk={unlockedOk}, closedAfterUnlock={closedAfterUnlock}";
        }
        catch(Exception ex)
        {
            Program.Log(ex);
            detail=ex.ToString();
        }
        finally
        {
            Native.TestButton(false);
            File.WriteAllText(Path.Combine(Output,"ext-drop-pin-result.json"),JsonSerializer.Serialize(new{pass,detail},new JsonSerializerOptions{WriteIndented=true}));
            backdrop?.Close();
            otherWindow?.Close();
            Environment.ExitCode=pass?0:1;
            Application.Current.Shutdown(pass?0:1);
            Environment.Exit(pass?0:1);
        }
    }

    internal static async Task DrawerStability(Controller c)
    {
        Window? backdrop=null;
        bool pass=false;
        string detail="";
        var rootDir=Path.Combine(Output,"drawer-stability");
        var openFrames=new List<(int Cycle,double Phase,double Opacity,double Ms)>();
        var closeFrames=new List<(int Cycle,double Phase,double Opacity,double Ms)>();
        var cpuFrameMs=new List<double>();
        long cpuEnter=0;
        c.DrawerTiming=stage=>
        {
            if(stage=="frame-enter")cpuEnter=System.Diagnostics.Stopwatch.GetTimestamp();
            else if(stage=="frame-leave"&&cpuEnter!=0){cpuFrameMs.Add(System.Diagnostics.Stopwatch.GetElapsedTime(cpuEnter,System.Diagnostics.Stopwatch.GetTimestamp()).TotalMilliseconds);cpuEnter=0;}
        };
        using var watchdog=new System.Threading.Timer(_=>
        {
            try
            {
                string timeoutDetail=$"TIMEOUT(20s): openFrames={openFrames.Count}, closeFrames={closeFrames.Count}, detail={detail}";
                File.WriteAllText(Path.Combine(Output,"drawer-stability-result.json"),JsonSerializer.Serialize(new{pass=false,detail=timeoutDetail},new JsonSerializerOptions{WriteIndented=true}));
            }
            catch{}
            Environment.Exit(1);
        },null,20000,Timeout.Infinite);
        try
        {
            Directory.CreateDirectory(Output);
            if(Directory.Exists(rootDir))Directory.Delete(rootDir,true);
            Directory.CreateDirectory(rootDir);

            var imgPaths=new List<string>();
            var colors=new[]{System.Drawing.Color.RoyalBlue,System.Drawing.Color.MediumSeaGreen,System.Drawing.Color.IndianRed};
            for(int i=0;i<3;i++)
            {
                var fp=Path.Combine(rootDir,$"large_{i}.png");
                using var bmp=new System.Drawing.Bitmap(1024,1024);
                using(var g=System.Drawing.Graphics.FromImage(bmp))g.Clear(colors[i]);
                bmp.Save(fp,System.Drawing.Imaging.ImageFormat.Png);
                imgPaths.Add(fp);
            }

            var area=c.WorkArea;
            int refreshHz=Native.GetDisplayRefreshRate(c.Screen.DeviceName);
            if(refreshHz<160)throw new InvalidOperationException($"Expected >=160Hz (165Hz) display refresh rate, got {refreshHz}Hz on {c.Screen.DeviceName}");
            double frameBudgetMs=1000.0/Math.Max(60,refreshHz);
            backdrop=new Window{Title="MacDock 稳定性测试背景",ShowInTaskbar=false,ShowActivated=false,Topmost=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=Math.Max(400,(area.Width-40)/c.Scale),Height=Math.Max(300,(area.Height-40)/c.Scale)};
            backdrop.Content=new Grid{Background=Brushes.White};
            backdrop.Loaded+=(_,_)=>Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(backdrop).Handle,0,area.Left+20,area.Top+20,Math.Max(400,area.Width-40),Math.Max(300,area.Height-40),0x10);
            backdrop.Show();

            c.OverrideDesktopFolder=rootDir;
            c.Store.Data.Appearance="light";
            c.Store.Data.ReducedMotion=false;
            c.Store.Data.DockOrder.Clear();
            c.Store.Data.Entries.Clear();
            Theme.Apply("light");
            foreach(var item in Desktop.DefaultPins().Take(3))
                c.Store.Data.Entries.Add(new Entry{Path=item.Path,Name=item.Name,Source="system",Pinned=true});
            foreach(var fp in imgPaths)
                c.Store.Data.Entries.Add(new Entry{Path=fp,Name=Path.GetFileNameWithoutExtension(fp),Source="desktop",AutoName=false});

            c.Dock.Rebuild();c.Position();c.Expand();c.SettleDockMotion();c.Dock.UpdateLayout();

            // Wait for background icon decoding & verify DecodePixelWidth <= 96
            var iconEntries=imgPaths.Select(p=>c.Icon(p,c.Layout.DrawerIcon)).ToArray();
            for(int w=0;w<30&&iconEntries.Any(e=>e.Image==null);w++)await Task.Delay(50);
            foreach(var entry in iconEntries)
            {
                if(entry.Image is not System.Windows.Media.Imaging.BitmapSource bm||bm.PixelWidth>96||bm.PixelHeight>96)
                    throw new InvalidOperationException($"Thumbnail not downsampled <= 96px: {(entry.Image as System.Windows.Media.Imaging.BitmapSource)?.PixelWidth}x{(entry.Image as System.Windows.Media.Imaging.BitmapSource)?.PixelHeight}");
            }

            int cycle=0;
            long lastTick=0;
            string mode="idle";
            bool midCaptured=false;
            bool bottomHaloDetected=false;
            string bottomEdgeSample="";
            c.DrawerAnimationFrame=(phase,top,winOpacity,fgOpacity)=>
            {
                if(mode=="idle")return;
                long now=System.Diagnostics.Stopwatch.GetTimestamp();
                double ms=lastTick==0?0:System.Diagnostics.Stopwatch.GetElapsedTime(lastTick,now).TotalMilliseconds;
                lastTick=now;
                if(mode=="open")
                {
                    openFrames.Add((cycle,phase,fgOpacity,ms));
                    if(cycle==1&&!midCaptured&&phase>=0.18&&phase<=0.82&&c.Drawer is {IsVisible:true} d&&d.CurrentPose is {} pose)
                    {
                        midCaptured=true;
                        var body=d.BodyBounds;
                        int yBottom=body.Top+(int)Math.Round(pose.Row(1).Y*c.Scale);
                        int capX=Math.Max(area.Left,body.Left-8);
                        int capW=Math.Min(area.Right-capX,(body.Right-body.Left)+16);
                        int capY=Math.Max(area.Top,yBottom-24);
                        int capH=Math.Min(area.Bottom-capY,64);
                        if(capW>20&&capH>20)
                        {
                            using var bmp=new System.Drawing.Bitmap(capW,capH);
                            bool screenCaptured=false;
                            try
                            {
                                using var g=System.Drawing.Graphics.FromImage(bmp);
                                g.CopyFromScreen(capX,capY,0,0,new System.Drawing.Size(capW,capH));
                                screenCaptured=true;
                            }
                            catch(System.ComponentModel.Win32Exception)
                            {
                                // Fallback when running in a non-interactive sandbox session without desktop DC
                                if(d.Content is FrameworkElement rootEl&&rootEl.ActualWidth>0&&rootEl.ActualHeight>0)
                                {
                                    int rw=(int)Math.Ceiling(rootEl.ActualWidth*c.Scale),rh=(int)Math.Ceiling(rootEl.ActualHeight*c.Scale);
                                    var rtb=new System.Windows.Media.Imaging.RenderTargetBitmap(rw,rh,96*c.Scale,96*c.Scale,PixelFormats.Pbgra32);
                                    var dv=new DrawingVisual();
                                    using(var dc=dv.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,rootEl.ActualWidth,rootEl.ActualHeight));}
                                    rtb.Render(dv);
                                    rtb.Render(rootEl);
                                    var enc=new System.Windows.Media.Imaging.PngBitmapEncoder();
                                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                                    using var memStream=new MemoryStream();enc.Save(memStream);memStream.Position=0;
                                    using var fullBmp=new System.Drawing.Bitmap(memStream);
                                    int sy=Math.Clamp((int)Math.Round(pose.Row(1).Y*c.Scale)+(int)Math.Round(d.MaterialInset*c.Scale)-24,0,Math.Max(0,fullBmp.Height-capH));
                                    int sx=Math.Clamp((int)Math.Round(d.MaterialInset*c.Scale)-8,0,Math.Max(0,fullBmp.Width-capW));
                                    using var g=System.Drawing.Graphics.FromImage(bmp);
                                    g.DrawImage(fullBmp,new System.Drawing.Rectangle(0,0,capW,capH),new System.Drawing.Rectangle(sx,sy,Math.Min(capW,fullBmp.Width-sx),Math.Min(capH,fullBmp.Height-sy)),System.Drawing.GraphicsUnit.Pixel);
                                }
                            }
                            bmp.Save(Path.Combine(Output,"drawer-stability-open-mid.png"),System.Drawing.Imaging.ImageFormat.Png);
                            int cx=capW/2;
                            var luma=new List<int>();
                            for(int dy=1;dy<=6;dy++)
                            {
                                int py=Math.Clamp((yBottom-capY)+dy,0,capH-1);
                                var px=bmp.GetPixel(cx,py);
                                luma.Add((px.R+px.G+px.B)/3);
                            }
                            bottomEdgeSample=(screenCaptured?"screen:":"rtb:")+string.Join(",",luma);
                            // Check for double white-line halo: bright spike (>248) sandwiched between darker shadow (<240)
                            for(int k=1;k<luma.Count-1;k++)
                            {
                                if(luma[k]>luma[k-1]+8&&luma[k]>luma[k+1]+8)bottomHaloDetected=true;
                            }
                        }
                    }
                }
                else if(mode=="close")
                {
                    closeFrames.Add((cycle,phase,fgOpacity,ms));
                }
            };

            for(cycle=1;cycle<=3;cycle++)
            {
                mode="open";lastTick=0;
                c.ToggleDrawer();
                for(int w=0;w<60&&(c.Drawer?.IsVisible!=true||c.DrawerMotionRunning||c.DrawerProgress<0.99);w++)await Task.Delay(30);
                mode="idle";
                if(c.Drawer?.IsVisible!=true||Math.Abs(c.Drawer.ForegroundOpacity-1.0)>0.01||(c.Drawer.Surface?.Opacity??0)<0.99)
                    throw new InvalidOperationException($"Cycle {cycle} settled invalid state: visible={c.Drawer?.IsVisible}, fullscreen={Native.Fullscreen(c.Screen)}, fg={c.Drawer?.ForegroundOpacity}, surf={c.Drawer?.Surface?.Opacity}");

                var cycleFrames=openFrames.Where(f=>f.Cycle==cycle&&f.Phase>=0.25).ToArray();
                if(cycleFrames.Length==0||cycleFrames.Any(f=>f.Opacity<0.85))
                    throw new InvalidOperationException($"Cycle {cycle} transparent flash detected: count={cycleFrames.Length}, minOpacity={(cycleFrames.Length>0?cycleFrames.Min(f=>f.Opacity):-1):F3}");

                mode="close";lastTick=0;
                c.CloseDrawer(keepDock:true,force:true);
                for(int w=0;w<40&&(c.Drawer?.IsVisible==true||c.DrawerMotionRunning);w++)await Task.Delay(30);
                mode="idle";
                await Task.Delay(60);
            }

            static (double Avg,double P95,double Max,double Fps,int Dropped) Stats(List<(int Cycle,double Phase,double Opacity,double Ms)> list,double budgetMs)
            {
                var valid=list.Where(f=>f.Ms>0).Select(f=>f.Ms).OrderBy(x=>x).ToArray();
                if(valid.Length==0)return (0,0,0,0,0);
                double avg=valid.Average();
                double p95=valid[(int)Math.Min(valid.Length-1,Math.Floor(valid.Length*0.95))];
                double max=valid[^1];
                double fps=1000.0/avg;
                int dropped=valid.Count(ms=>ms>budgetMs*1.65);
                return (avg,p95,max,fps,dropped);
            }

            var openStat=Stats(openFrames,frameBudgetMs);
            var closeStat=Stats(closeFrames,frameBudgetMs);
            if(bottomHaloDetected)
                throw new InvalidOperationException($"Bottom edge double-line halo detected on white backdrop: luma[1..6]={bottomEdgeSample}");

            pass=true;
            double cpuAvg=cpuFrameMs.Count>0?cpuFrameMs.Average():0,cpuMax=cpuFrameMs.Count>0?cpuFrameMs.Max():0;
            detail=$"display={area.Width}x{area.Height}@{refreshHz}Hz(budget={frameBudgetMs:F2}ms), cpu[n={cpuFrameMs.Count},avg={cpuAvg:F2}ms,max={cpuMax:F2}ms], open[n={openFrames.Count},avg={openStat.Avg:F2}ms,p95={openStat.P95:F2}ms,max={openStat.Max:F2}ms,fps={openStat.Fps:F1},drop={openStat.Dropped}], close[n={closeFrames.Count},avg={closeStat.Avg:F2}ms,p95={closeStat.P95:F2}ms,max={closeStat.Max:F2}ms,fps={closeStat.Fps:F1},drop={closeStat.Dropped}], bottomLuma1to6=[{bottomEdgeSample}], thumbMaxPx={iconEntries.Max(e=>((System.Windows.Media.Imaging.BitmapSource)e.Image!).PixelWidth)}";
        }
        catch(Exception ex)
        {
            Program.Log(ex);
            detail=ex.ToString();
        }
        finally
        {
            c.DrawerAnimationFrame=null;
            c.DrawerTiming=null;
            File.WriteAllText(Path.Combine(Output,"drawer-stability-result.json"),JsonSerializer.Serialize(new{pass,detail},new JsonSerializerOptions{WriteIndented=true}));
            backdrop?.Close();
            Environment.ExitCode=pass?0:1;
            Application.Current.Shutdown(pass?0:1);
            Environment.Exit(pass?0:1);
        }
    }

    internal static async Task DockStability(Controller c)
    {
        Window? backdrop=null;
        bool pass=false;
        string detail="";
        var openFrames=new List<(int Cycle,double Phase,double Ms)>();
        var closeFrames=new List<(int Cycle,double Phase,double Ms)>();
        using var watchdog=new System.Threading.Timer(_=>
        {
            try
            {
                string timeoutDetail=$"TIMEOUT(15s): openFrames={openFrames.Count}, closeFrames={closeFrames.Count}, detail={detail}";
                File.WriteAllText(Path.Combine(Output,"dock-stability-result.json"),JsonSerializer.Serialize(new{pass=false,detail=timeoutDetail},new JsonSerializerOptions{WriteIndented=true}));
            }
            catch{}
            Environment.Exit(1);
        },null,15000,Timeout.Infinite);
        try
        {
            Directory.CreateDirectory(Output);
            var area=c.WorkArea;
            int refreshHz=Native.GetDisplayRefreshRate(c.Screen.DeviceName);
            double frameBudgetMs=1000.0/Math.Max(60,refreshHz);

            backdrop=new Window{Title="MacDock Dock 稳定性测试背景",ShowInTaskbar=false,Topmost=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Width=Math.Max(400,(area.Width-40)/c.Scale),Height=Math.Max(300,(area.Height-40)/c.Scale)};
            backdrop.Content=new Grid{Background=Brushes.White};
            backdrop.Loaded+=(_,_)=>Native.SetWindowPos(new System.Windows.Interop.WindowInteropHelper(backdrop).Handle,0,area.Left+20,area.Top+20,Math.Max(400,area.Width-40),Math.Max(300,area.Height-40),0x10);
            backdrop.Show();

            c.Store.Data.Appearance="light";
            c.Store.Data.ReducedMotion=false;
            c.Store.Data.DockOrder.Clear();
            c.Store.Data.Entries.Clear();
            Theme.Apply("light");
            foreach(var item in Desktop.DefaultPins().Take(8))
                c.Store.Data.Entries.Add(new Entry{Path=item.Path,Name=item.Name,Source="system",Pinned=true});
            c.Dock.Rebuild();c.Position();
            c.Dock.Hide();
            await Task.Delay(150);

            int cycle=0;
            long lastTick=0;
            string mode="idle";
            c.DockAnimationFrame=(phase,_)=>
            {
                if(mode=="idle")return;
                long now=System.Diagnostics.Stopwatch.GetTimestamp();
                double ms=lastTick==0?0:System.Diagnostics.Stopwatch.GetElapsedTime(lastTick,now).TotalMilliseconds;
                lastTick=now;
                if(mode=="open")openFrames.Add((cycle,phase,ms));
                else if(mode=="close")closeFrames.Add((cycle,phase,ms));
            };

            for(cycle=1;cycle<=4;cycle++)
            {
                mode="open";lastTick=0;
                c.Busy=true;
                c.Expand();
                for(int w=0;w<60&&(!c.Dock.IsVisible||c.DockMotionRunning||c.DockProgress<0.99);w++)await Task.Delay(25);
                mode="idle";
                if(cycle==1&&c.Dock.IsVisible&&Native.GetWindowRect(c.Dock.Handle,out var dr))
                {
                    var scr=c.Screen.Bounds;
                    int capX=Math.Max(scr.Left,dr.Left-24),capY=Math.Max(scr.Top,dr.Top-24);
                    int capW=Math.Min(scr.Right-capX,(dr.Right-dr.Left)+48),capH=Math.Min(scr.Bottom-capY,(dr.Bottom-dr.Top)+48);
                    if(capW>20&&capH>20)
                    {
                        var shotPath=Path.Combine(Output,"dock-stability-open.png");
                        Save("dock-stability-open.png",new System.Drawing.Rectangle(capX,capY,capW,capH));
                        using var shotBmp=new System.Drawing.Bitmap(shotPath);
                        int ox=dr.Left-capX,oy=dr.Top-capY,radPx=(int)Math.Round(c.Dock.MaterialRadius*c.Scale);
                        int darkEdge=0;
                        for(int y=0;y<Math.Min(radPx,shotBmp.Height-oy);y++)
                        for(int x=0;x<Math.Min(radPx,shotBmp.Width-ox);x++)
                        {
                            double dist=Math.Sqrt(Math.Pow(radPx-x-0.5,2)+Math.Pow(radPx-y-0.5,2));
                            if(dist>radPx+2.5)
                            {
                                var px=shotBmp.GetPixel(ox+x,oy+y);
                                int luma=(px.R+px.G+px.B)/3;
                                if(luma<140)darkEdge++;
                            }
                        }
                        if(darkEdge>0)throw new InvalidOperationException($"Dock corner dark fringe detected: darkEdge={darkEdge}");
                    }
                }
                await Task.Delay(60);

                mode="close";lastTick=0;
                c.Busy=false;
                c.Collapse();
                for(int w=0;w<60&&(c.Dock.IsVisible||c.DockMotionRunning);w++)await Task.Delay(25);
                mode="idle";
                await Task.Delay(80);
            }

            static (double Avg,double P95,double Max,double Fps,int Dropped) Stats(List<(int Cycle,double Phase,double Ms)> list,double budgetMs)
            {
                var valid=list.Where(f=>f.Ms>0).Select(f=>f.Ms).OrderBy(x=>x).ToArray();
                if(valid.Length==0)return (0,0,0,0,0);
                double avg=valid.Average();
                double p95=valid[(int)Math.Min(valid.Length-1,Math.Floor(valid.Length*0.95))];
                double max=valid[^1];
                double fps=1000.0/avg;
                int dropped=valid.Count(ms=>ms>budgetMs*1.65);
                return (avg,p95,max,fps,dropped);
            }

            var openStat=Stats(openFrames,16.5);
            var closeStat=Stats(closeFrames,16.5);
            bool shotExists=File.Exists(Path.Combine(Output,"dock-stability-open.png"));
            bool timerDrivenOk=openFrames.Count>=12&&closeFrames.Count>=12&&openStat.Avg<=18.0&&closeStat.Avg<=18.0&&shotExists;
            pass=timerDrivenOk;
            detail=$"display={area.Width}x{area.Height}@{refreshHz}Hz(panelBudget={frameBudgetMs:F2}ms, dockTimer=15ms), open[n={openFrames.Count},avg={openStat.Avg:F2}ms,p95={openStat.P95:F2}ms,max={openStat.Max:F2}ms,fps={openStat.Fps:F1},drop={openStat.Dropped}], close[n={closeFrames.Count},avg={closeStat.Avg:F2}ms,p95={closeStat.P95:F2}ms,max={closeStat.Max:F2}ms,fps={closeStat.Fps:F1},drop={closeStat.Dropped}], shot={shotExists}";
        }
        catch(Exception ex)
        {
            Program.Log(ex);
            detail=ex.ToString();
        }
        finally
        {
            c.DockAnimationFrame=null;
            File.WriteAllText(Path.Combine(Output,"dock-stability-result.json"),JsonSerializer.Serialize(new{pass,detail},new JsonSerializerOptions{WriteIndented=true}));
            backdrop?.Close();
            Environment.ExitCode=pass?0:1;
            Application.Current.Shutdown(pass?0:1);
            Environment.Exit(pass?0:1);
        }
    }

    internal static async Task DrawerPasteReuse(Controller c)
    {
        var checks=new List<object>();
        bool Check(string name,bool ok,object? detail=null){checks.Add(new{name,pass=ok,detail});return ok;}
        string token="MDPR"+Guid.NewGuid().ToString("N")[..8];
        var createdDesktopFiles=new List<string>();
        var testDesktopDir=Path.Combine(Output,"paste-reuse-desktop-"+token);
        var outsideDir=Path.Combine(Output,"paste-reuse-"+token);
        Directory.CreateDirectory(testDesktopDir);
        Directory.CreateDirectory(outsideDir);
        System.Windows.IDataObject? savedClipboard=null;
        bool clipboardTouched=false;
        bool allPass=false;
        using var watchdog=new System.Threading.Timer(_=>
        {
            try{File.WriteAllText(Path.Combine(Output,"drawer-paste-reuse-result.json"),JsonSerializer.Serialize(new{pass=false,detail="TIMEOUT(20s)",checks},new JsonSerializerOptions{WriteIndented=true}));}catch{}
            Environment.Exit(1);
        },null,20000,Timeout.Infinite);
        try
        {
            if(!c.Store.FilePath.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Use an isolated profile.");

            c.OverrideDesktopFolder=testDesktopDir;
            c.Store.Data.DockOrder.Clear();
            c.Store.Data.Entries.Clear();
            foreach(var item in Desktop.DefaultPins().Take(3))
                c.Store.Data.Entries.Add(new Entry{Path=item.Path,Name=item.Name,Source="system",Pinned=true});

            for(int i=1;i<=3;i++)
            {
                var p=Path.Combine(testDesktopDir,$"{token}-existing-{i}.txt");
                File.WriteAllText(p,$"existing-{i}");
                createdDesktopFiles.Add(p);
                c.Store.Data.Entries.Add(new Entry{Path=p,Name=Path.GetFileNameWithoutExtension(p),Source="desktop",AutoName=false});
            }
            var pasteSrc=Path.Combine(outsideDir,$"{token}-pasted.txt");
            File.WriteAllText(pasteSrc,"pasted-content");
            var expectedPastedPath=Path.Combine(testDesktopDir,Path.GetFileName(pasteSrc));
            createdDesktopFiles.Add(expectedPastedPath);

            c.Dock.Rebuild();c.Position();c.Expand();c.SettleDockMotion();c.Dock.UpdateLayout();
            c.ToggleDrawer();
            c.SettleDrawerMotion();
            var drawer=c.Drawer!;
            await Task.Delay(350);
            drawer.UpdateLayout();

            static System.Windows.Controls.Image? FindImage(DependencyObject root)
            {
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                {
                    var child=VisualTreeHelper.GetChild(root,i);
                    if(child is System.Windows.Controls.Image img)return img;
                    var nested=FindImage(child);
                    if(nested!=null)return nested;
                }
                return null;
            }

            var existingPaths=createdDesktopFiles.Take(3).ToArray();
            var beforeTiles=existingPaths.ToDictionary(p=>p,p=>drawer.TileControlForPath(p),StringComparer.OrdinalIgnoreCase);
            var beforeImages=existingPaths.ToDictionary(p=>p,p=>beforeTiles[p] is {} t?FindImage(t)?.Source:null,StringComparer.OrdinalIgnoreCase);
            Check("existing files rendered in drawer",beforeTiles.Values.All(t=>t!=null));

            double savedScroll=drawer.ScrollOffset;
            savedClipboard=System.Windows.Clipboard.GetDataObject();
            clipboardTouched=true;
            Check("clipboard fixture prepared",ShellOps.SetFileClipboard(new[]{pasteSrc},false));

            c.PasteToDesktop();
            await Task.Delay(250);
            drawer.UpdateLayout();

            bool tilesReused=existingPaths.All(p=>ReferenceEquals(beforeTiles[p],drawer.TileControlForPath(p)));
            Check("existing tile UI controls are reused (not destroyed/rebuilt)",tilesReused,existingPaths.Select(p=>new{path=Path.GetFileName(p),same=ReferenceEquals(beforeTiles[p],drawer.TileControlForPath(p))}).ToArray());

            bool imagesReused=existingPaths.All(p=>beforeImages[p]!=null&&ReferenceEquals(beforeImages[p],drawer.TileControlForPath(p) is {} t?FindImage(t)?.Source:null));
            Check("existing icon ImageSource instances stay cached without placeholder flicker",imagesReused);

            var pastedTile=drawer.TileControlForPath(expectedPastedPath);
            bool pastedAppeared=pastedTile!=null;
            bool pastedSelected=drawer.Selection.Any(e=>string.Equals(e.Path,expectedPastedPath,StringComparison.OrdinalIgnoreCase));
            Check("pasted file appears and is auto-selected",pastedAppeared&&pastedSelected,new{pastedAppeared,pastedSelected});

            bool scrollPreserved=Math.Abs(drawer.ScrollOffset-savedScroll)<0.5;
            Check("ScrollViewer vertical offset preserved",scrollPreserved,new{savedScroll,afterScroll=drawer.ScrollOffset});

            // Capture visual proof of drawer after incremental paste
            if(drawer.Surface is FrameworkElement surface && surface.ActualWidth>0 && surface.ActualHeight>0)
            {
                var rtb=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);
                rtb.Render(surface);
                var enc=new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs=File.Create(Path.Combine(Output,"drawer-paste-reuse.png"));
                enc.Save(fs);
                Check("visual proof screenshot generated",File.Exists(Path.Combine(Output,"drawer-paste-reuse.png")));
            }
            else
            {
                Check("visual proof screenshot generated",false,"surface has zero size");
            }

            allPass=checks.All(item=>(bool)item.GetType().GetProperty("pass")!.GetValue(item)!);
        }
        catch(Exception ex)
        {
            Program.Log(ex);
            Check("exception",false,ex.ToString());
            allPass=false;
        }
        finally
        {
            if(clipboardTouched)try{if(savedClipboard!=null)System.Windows.Clipboard.SetDataObject(savedClipboard,true);else System.Windows.Clipboard.Clear();}catch{}
            foreach(var f in createdDesktopFiles)try{if(File.Exists(f))File.Delete(f);}catch{}
            try{if(Directory.Exists(testDesktopDir))Directory.Delete(testDesktopDir,true);}catch{}
            try{if(Directory.Exists(outsideDir))Directory.Delete(outsideDir,true);}catch{}
            c.SyncDesktopNow();
            File.WriteAllText(Path.Combine(Output,"drawer-paste-reuse-result.json"),JsonSerializer.Serialize(new{pass=allPass,checks},new JsonSerializerOptions{WriteIndented=true}));
            Environment.ExitCode=allPass?0:1;
            Application.Current.Shutdown(allPass?0:1);
            Environment.Exit(allPass?0:1);
        }
    }

    sealed class Feed(Func<Uri,Task<HttpResponseMessage>> reply):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>reply(request.RequestUri!);
    }

    internal static async Task Update(Controller c)
    {
        var checks=new List<object>();
        void Check(string name,bool pass,object? detail=null)=>checks.Add(new{name,pass,detail});
        Window[] Dialogs()=>Application.Current.Windows.OfType<Window>().Where(w=>w.Title=="MacDock 更新"&&w.IsVisible).ToArray();
        IEnumerable<DependencyObject> Descendants(DependencyObject element)
        {
            yield return element;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(element,i)))yield return child;
        }
        bool Text(string value)=>Dialogs().SelectMany(Descendants).OfType<TextBlock>().Any(t=>t.Text.Contains(value,StringComparison.Ordinal));
        try
        {
            await Key(0x20,0x11,0x12);await Task.Delay(400);
            using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var current=Version.Parse(ReleaseUpdates.CurrentVersion);
            var next=new Version(current.Major,current.Minor,current.Build+1).ToString(3);
            var manifest=JsonSerializer.SerializeToUtf8Bytes(new ReleaseUpdates.Manifest(next,$"MacDock-{next}-win-x64-Setup.exe",1,new string('a',64),"演示更新说明"));
            var signature=key.SignData(manifest,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            int checksSent=0;string scenario="current";
            using var http=new HttpClient(new Feed(async uri=>
            {
                if(uri.IdnHost=="api.github.com")
                {
                    checksSent++;await Task.Delay(250);
                    if(scenario=="offline")throw new HttpRequestException("fixture network unreachable");
                    if(scenario=="timeout")throw new TaskCanceledException("fixture timeout");
                    if(scenario=="limited")return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new{tag_name="v"+(scenario=="next"?next:current.ToString(3)),draft=false,prerelease=false}))};
                }
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(uri.AbsolutePath.EndsWith(".sig")?signature:manifest)};
            }));
            var feed=new ReleaseUpdates("fixture/MacDock",key.ExportSubjectPublicKeyInfo(),http);
            var pending=c.CheckUpdates(true,feed);
            await Task.Delay(95);
            Check("manual check immediately shows progress",Dialogs().Length==1&&Text("正在检查更新"));
            await c.CheckUpdates(true,feed);await pending;
            Check("repeated manual check shares one request and window",checksSent==1&&Dialogs().Length==1);
            Check("current stable version is visibly confirmed",Text("当前已是最新版本")&&Text(ReleaseUpdates.CurrentVersion));
            var active=Dialogs().Single().IsActive;
            await Key(0x1B);Check("Escape closes the update window",Dialogs().Length==0,new{active});
            foreach(var open in Dialogs())open.Close();
            foreach(var (failure,label) in new[]{("offline","无法连接 GitHub"),("timeout","请求超时"),("limited","限制了请求")})
            {
                scenario=failure;await c.CheckUpdates(true,feed);
                Check(failure+" produces a visible, retryable result",Text(label)&&Dialogs().SelectMany(Descendants).OfType<Button>().Any(b=>Equals(b.Content,"重试")&&b.IsVisible));
                Dialogs().Single().Close();
            }
            scenario="next";await c.CheckUpdates(false,feed);
            Check("automatic check stays quiet while exposing signed new version",Dialogs().Length==0&&c.UpdateMenuLabel==$"发现新版本 · v{next}");
            await c.CheckUpdates(true,feed);
            Check("manual update displays verified version, notes and explicit action",Text(next)&&Text("演示更新说明")&&Dialogs().SelectMany(Descendants).OfType<Button>().Any(b=>Equals(b.Content,"打开发布页")&&b.IsVisible));
            var w=Dialogs().Single();Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(w).Handle,out var r);
            var screen=System.Windows.Forms.Screen.FromRectangle(System.Drawing.Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom)).WorkingArea;
            Check("dialog is centered within the current monitor work area",Math.Abs((r.Left+r.Right)-(screen.Left+screen.Right))<8&&Math.Abs((r.Top+r.Bottom)-(screen.Top+screen.Bottom))<8);
            w.Close();
        }
        catch(Exception error){Check("exception",false,error.ToString());}
        finally
        {
            File.WriteAllText(Path.Combine(Output,"update-ui.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
            bool allPass=checks.All(item=>(bool)item.GetType().GetProperty("pass")!.GetValue(item)!);
            Environment.ExitCode=allPass?0:1;
            Application.Current.Shutdown(allPass?0:1);
            Environment.Exit(allPass?0:1);
        }
    }
}
#endif
