using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace MacDock;
internal static class Tests
{
    static readonly List<object> results=new();
    static string Output => Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS") ?? Path.Combine(AppContext.BaseDirectory,"test-results");
    static void Check(string name,bool pass,string detail="") { results.Add(new { name,pass,detail });Write("progress.json");if(!pass)throw new Exception("FAIL: "+name+" "+detail); }
    static void Write(string file) { Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,file),JsonSerializer.Serialize(results,new JsonSerializerOptions { WriteIndented=true })); }
    internal static void Responsive(Controller c)
    {
        try
        {
            c.TestIndicator(false);var original=c.WorkArea;var drawer=new DrawerWindow(c);
            foreach(var (w,h) in new[]{(800,552),(1280,672),(2560,1392),(2160,1572),(800,552)})
            {
                c.TestLayoutArea(new System.Drawing.Rectangle(0,0,w,h));drawer.Prepare();
                var surface=drawer.Surface!;surface.Measure(new Size(drawer.PanelWidth,drawer.PanelHeight));surface.Arrange(new Rect(0,0,drawer.PanelWidth,drawer.PanelHeight));surface.UpdateLayout();
                Check($"live drawer reflow {w}x{h} chrome fits",drawer.ChromeWithinSurface,$"surface={surface.ActualWidth}x{surface.ActualHeight}");
                Check($"live drawer reflow {w}x{h} columns",drawer.MeasuredColumns()==c.Layout.Columns,$"actual={drawer.MeasuredColumns()}; expected={c.Layout.Columns}");
            }
            c.TestLayoutArea(original);
            var entry=new Entry{Path=Environment.ProcessPath!,Name="recovery refresh fixture",Source="legacy"};c.Store.Data.Entries.Add(entry);
            var recovery=c.RecoveryMenu()!;
            Check("recovery menu includes an unconfirmed legacy entry",recovery.Items.OfType<System.Windows.Controls.MenuItem>().Any(i=>i.Header.ToString()==entry.Name));
            entry.Source="manual";
            Check("settings recovery refreshes after promotion",c.RecoveryMenu()?.Items.OfType<System.Windows.Controls.MenuItem>().All(item=>item.Header.ToString()!=entry.Name)!=false);
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally{Write("responsive-test.json");System.Windows.Application.Current.Shutdown();}
    }
    internal static async Task AutomationHost(Controller c)
    {
        try
        {
            PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;
            c.Expand();await Task.Delay(350);var target=c.Store.Data.Entries.First(e=>e.Pinned);var p=c.Dock.TileCenter(target.Id);
            var ready=new{targetX=p.X,targetY=p.Y,wakeX=c.WorkArea.Left+c.WorkArea.Width/2,wakeY=c.WorkArea.Bottom-(int)(12*c.Scale*c.Layout.Unit),dock=(long)c.Dock.Handle,indicator=(long)c.IndicatorHandle,profile=c.Store.FilePath};
            c.Collapse();await Task.Delay(350);Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"automation-ready.json"),JsonSerializer.Serialize(ready));
            for(int i=0;i<1200&&!File.Exists(Path.Combine(Output,"driver-done"));i++)await Task.Delay(100);
            Check("external GUI driver completed",File.Exists(Path.Combine(Output,"driver-done")));
            Check("external GUI run has no unaccounted physical input",PointerHook.NonInjectedInputs==0);
            Check("external OLE release never launches an application",c.LaunchCount==0,"launches="+c.LaunchCount);
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally{PointerHook.AuditInput=false;Write("automation-host.json");System.Windows.Application.Current.Shutdown();}
    }
    internal static int DataBoundary()
    {
        try
        {
            var root=Path.Combine(Output,"migration-profile");Directory.CreateDirectory(root);
            var actual=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacDock","config.json");
            var bytes=File.ReadAllBytes(actual);var original=JsonSerializer.Deserialize<Config>(bytes)!;
            File.WriteAllBytes(Path.Combine(root,"config.json"),bytes);
            var store=new Store(root);var scan=Desktop.Enumerate();store.Sync(scan,Desktop.ReadableFolders);store.Save();
            Check("actual view available for source audit",Desktop.ViewAvailable,DesktopView.LastError??"");
            Check("all current filesystem entries are direct desktop children",scan.Where(e=>e.Source=="desktop").All(e=>Desktop.IsDesktopPath(e.Path)));
            Check("libraries and search providers not auto imported",scan.All(e=>!new[]{"::{031E4825-7B94-4dc3-B131-E946B44C8DD5}","::{04731B67-D933-450a-90E6-4ACD2E9408FE}","::{9343812e-1c37-4a49-a12e-4b2d810d956b}"}.Contains(e.Path,StringComparer.OrdinalIgnoreCase)));
            Check("migration retains every legacy id",original.Entries.All(e=>store.Data.Entries.Any(x=>x.Id==e.Id)));
            Check("migration retains pin choices and their order",original.Entries.Where(e=>e.Pinned).Select(e=>e.Id).SequenceEqual(store.Data.Entries.Where(e=>e.Pinned).Select(e=>e.Id)));
            Check("historical manual label is not treated as proof of user addition",original.Version>=3||store.Data.Entries.Where(e=>original.Entries.Any(o=>o.Id==e.Id&&o.Source=="manual")).All(e=>e.Source=="legacy"&&!e.InDrawer));
            var fake=Path.Combine(Output,"manual-original.txt");File.WriteAllText(fake,"manual original");store.Add(fake,false);var manual=store.Data.Entries.Single(e=>e.Path==fake);store.Sync(scan,Desktop.ReadableFolders);
            Check("explicit manual item remains a separately identified alias",manual.Source=="manual"&&manual.Origin=="user-added"&&manual.IsAlias&&manual.InDrawer&&store.Data.Entries.Contains(manual));
            Check("desktop snapshot matches drawer automatic sources exactly",store.Data.Entries.Where(e=>e.Source is "desktop" or "system").Select(e=>e.Path).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).SequenceEqual(scan.Select(e=>e.Path).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase));
            Check("original real configuration remains byte-identical",bytes.SequenceEqual(File.ReadAllBytes(actual)));
            results.Add(new{name="source counts",pass=true,desktop=scan.Count(e=>e.Source=="desktop"),shell=scan.Count(e=>e.Source=="system"),archived=store.Data.Entries.Count(e=>e.Source=="retired-auto"),legacy=store.Data.Entries.Count(e=>e.Source=="legacy"),pins=store.Data.Entries.Count(e=>e.Pinned)});
            return 0;
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});return 1;}
        finally{Write("data-test.json");}
    }
    internal static int DpiPreviews()
    {
        try
        {
            Directory.CreateDirectory(Output);var fixture=Path.Combine(Output,"dpi-document.txt");File.WriteAllText(fixture,"DPI fixture");
            foreach(var scale in new[]{1.0,1.25,1.5,2.0})
            {
                var board=new System.Windows.Controls.StackPanel{Orientation=System.Windows.Controls.Orientation.Horizontal,Background=System.Windows.Media.Brushes.DimGray};
                foreach(var (path,name) in new[]{(Environment.ProcessPath!,"应用程序"),(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"文件夹"),(fixture,"文档")})
                {
                    var cell=new System.Windows.Controls.StackPanel{Width=110,Margin=new Thickness(12,18,12,18)};
                    cell.Children.Add(Icons.View(Icons.Measure(Native.Icon(path,(int)Math.Ceiling(56*scale*2))),56));
                    var text=Theme.Text(name,12.25);text.TextAlignment=TextAlignment.Center;text.Margin=new Thickness(0,12,0,0);cell.Children.Add(text);board.Children.Add(cell);
                }
                board.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));board.Arrange(new Rect(board.DesiredSize));board.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(board.ActualWidth*scale),(int)Math.Ceiling(board.ActualHeight*scale),96*scale,96*scale,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(board);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(Output,$"dpi-{scale*100:0}.png")))encoder.Save(stream);
                Check($"DPI render {scale*100:0}% has complete label bounds",board.Children.OfType<System.Windows.Controls.StackPanel>().All(cell=>cell.ActualHeight>=80&&cell.Children.OfType<System.Windows.Controls.TextBlock>().All(t=>t.ActualWidth>=t.DesiredSize.Width-.1)),$"{bitmap.PixelWidth}x{bitmap.PixelHeight}; offscreen render, not a monitor switch");
            }
            return 0;
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.ToString()});return 1;}
        finally{Write("dpi-test.json");}
    }
    internal static int MenuOverflow()
    {
        try
        {
            var pinned=new[]{new Entry{Name="A",Pinned=true},new Entry{Name="B_item",Pinned=true},new Entry{Name="C",Pinned=true}};
            Entry? launched=null;
            var menu=Controller.PinnedOverflow(pinned,1,e=>launched=e)!;
            Check("overflow retains user order and labels",menu.Items.Count==2&&((System.Windows.Controls.MenuItem)menu.Items[0]).Header.ToString()=="B__item"&&((System.Windows.Controls.MenuItem)menu.Items[1]).Header.ToString()=="C");
            ((System.Windows.Controls.MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check("overflow launches original entry without altering pins",ReferenceEquals(launched,pinned[1])&&pinned.All(e=>e.Pinned));
            Check("overflow absent when everything fits",Controller.PinnedOverflow(pinned,3,_=>{})==null);
            return 0;
        }
        catch(Exception e){results.Add(new{name="error",pass=false,detail=e.Message});return 1;}
        finally{Write("menu-test.json");}
    }
    public static int Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"MacDock-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var i=new Intent();Check("approach waits",!i.Feed(0,true,false,false,true));Check("approach 99ms",!i.Feed(99,true,false,false,false));Check("approach 100ms",i.Feed(100,true,false,false,false));
            i.Reset();Check("horizontal pass starts",!i.Feed(0,true,false,false,false));Check("horizontal 179ms",!i.Feed(179,true,false,false,false));Check("horizontal 180ms",i.Feed(180,true,false,false,false));
            Check("exit resets",!i.Feed(500,false,false,false,false)&&i.Since<0);
            Check("desktop drag blocked",!i.Feed(0,true,true,false,true)&&!i.Feed(2000,true,true,false,true));
            Check("explicit drag starts",!i.Feed(0,true,true,true,true));Check("explicit drag dwell",i.Feed(650,true,true,true,true));
            i.Reset();i.Feed(0,true,false,false,true);Check("click cancels",!i.Feed(250,true,true,false,true)&&i.Since<0);
            var a=Path.Combine(root,"alpha.txt");File.WriteAllText(a,"safe original");var b=Path.Combine(root,"beta.png");File.WriteAllText(b,"image fixture");
            var store=new Store(Path.Combine(root,"settings"));store.Data.Entries.Clear();store.Add(a,true);store.Add(b,false);Check("duplicate merges pin",!store.Add(b,true)&&store.Data.Entries.Count==2&&store.Data.Entries[1].Pinned);
            var id=store.Data.Entries[0].Id;store.Reorder(id,store.Data.Entries[1].Id);Check("reorder",store.Data.Entries[1].Id==id);store.Save();
            Check("persistence",new Store(Path.Combine(root,"settings")).Data.Entries[1].Id==id);
            store.Data.Mode="按类型";store.Save();File.WriteAllText(store.FilePath,"{broken");var recovered=new Store(Path.Combine(root,"settings"));Check("backup recovery",recovered.Warning!=null && recovered.Data.Entries.Count==2);
            Check("damaged config preserved",Directory.GetFiles(Path.Combine(root,"settings"),"*.damaged-*").Length==1);
            Check("source file untouched",File.ReadAllText(a)=="safe original");
            Check("real categories",Entry.Classify(root)=="文件夹"&&Entry.Classify(a)=="文档"&&Entry.Classify(b)=="图片");
            foreach(var (s,initial) in new[]{("中文","Z"),("文件","W"),("应用","Y"),("阿里","A"),("百度","B"),("Apple","A"),("7zip","#")})Check("initial "+s,Alphabet.Initial(s)==initial,Alphabet.Initial(s));
            Check("grouping",Alphabet.Groups(store.Data.Entries,"按类型").Count()==2);
            foreach(var screen in new[]{(800,600,1.0),(1280,720,1.0),(1920,1080,1.25),(2560,1440,1.0),(2560,1440,1.5),(3840,2160,2.0)})
            {
                var layout=new Layout(screen.Item1/screen.Item3,(screen.Item2-48)/screen.Item3);
                Check($"layout {screen.Item1}x{screen.Item2} DPI {screen.Item3}",layout.DockWidth(80)<layout.WorkWidth&&layout.DrawerWidth<layout.WorkWidth&&layout.DrawerHeight(20,8)+layout.DockHeight<layout.WorkHeight&&layout.Icon>=28&&layout.Columns>=1);
            }
            foreach(int capacity in new[]{4,9,18}) { var metrics=new Layout(2560,1392,capacity,3,6);Check("capacity "+capacity,metrics.VisibleDockCount(40)==capacity&&metrics.Columns==6); }
            Check("desktop auto density",new Layout(2560,1392).EffectiveColumns>=6);

            // Desktop is the only automatic source: real desktop paths and shell namespace icons.
            var scan=Desktop.Enumerate();
            Check("desktop scan only existing items",scan.All(i=>i.Path.StartsWith("::")||File.Exists(i.Path)||Directory.Exists(i.Path)),string.Join(";",scan.Select(i=>i.Path).Take(3)));
            Check("desktop scan skips desktop.ini",scan.All(i=>!i.Path.EndsWith("desktop.ini",StringComparison.OrdinalIgnoreCase)));
            Check("desktop scan excludes start menu",scan.All(i=>!i.Path.Contains("Start Menu",StringComparison.OrdinalIgnoreCase)));
            Check("desktop scope excludes nested files",!Desktop.IsDesktopPath(Path.Combine(Desktop.UserFolder,"nested","file.txt")));
            Check("desktop scan deduplicated",scan.Select(i=>i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==scan.Count);
            var viewPaths=DesktopView.Read();
            Check("special entries confirmed by actual desktop view",viewPaths==null||scan.Where(i=>i.Source=="system").All(i=>viewPaths.Contains(i.Path,StringComparer.OrdinalIgnoreCase)||i.Path=="::{59031A47-3F72-44A7-89C5-5595FE6B30EE}"&&viewPaths.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),StringComparer.OrdinalIgnoreCase)));
            Check("every filesystem source is a direct desktop child",scan.Where(i=>i.Source=="desktop").All(i=>Desktop.IsDesktopPath(i.Path)));
            Check("desktop scan labels shortcuts",scan.Where(i=>i.Path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)).All(i=>!i.Name.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)));
            var win=Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var hi=Native.Icon(Path.Combine(win,"explorer.exe"),128);
            Check("shell icon rendered natively",hi is System.Windows.Media.Imaging.BitmapSource big&&big.PixelWidth>=64,hi is System.Windows.Media.Imaging.BitmapSource b2?b2.PixelWidth+"x"+b2.PixelHeight:"none");

            var mirror=new Store(Path.Combine(root,"mirror"));
            var m1=Path.Combine(root,"one.txt");File.WriteAllText(m1,"1");
            var m2=Path.Combine(root,"two.txt");File.WriteAllText(m2,"2");
            mirror.Initialize(new[]{new DesktopItem(m1,"one","desktop",null),new DesktopItem(m2,"two","desktop",null)},Array.Empty<DesktopView.PositionedItem>(),Array.Empty<DesktopItem>());
            Check("mirror seeds desktop items",mirror.Data.Entries.Count==2);
            mirror.Data.Entries[0].Pinned=true;
            Check("mirror drops deleted items",mirror.Sync(new[]{new DesktopItem(m1,"one","desktop",null)})&&mirror.Data.Entries.Count==1&&mirror.Data.Entries[0].Pinned);
            Check("mirror renames keep pin and identity",mirror.Repoint(m1,Path.Combine(root,"renamed.txt"))&&mirror.Data.Entries[0].Path.EndsWith("renamed.txt")&&mirror.Data.Entries[0].Pinned);
            var manualFixture=new Entry{Path=m2,Name="手工添加",Source="manual"};mirror.Data.Entries.Add(manualFixture);
            mirror.Sync(new[]{new DesktopItem(m1,"one","desktop",null)});
            Check("manual entries survive sync",mirror.Data.Entries.Any(e=>e.Id==manualFixture.Id&&e.Source=="manual")&&mirror.Data.Entries.Any(e=>e.Pinned&&e.Path.EndsWith("renamed.txt")));
            var unavailable=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var snapshot=mirror.Data.Entries.Select(e=>e.Id).ToArray();mirror.Sync(Array.Empty<DesktopItem>(),unavailable);
            Check("unavailable desktop never drops mirrored entries",snapshot.SequenceEqual(mirror.Data.Entries.Select(e=>e.Id)));
            var legacyRoot=Path.Combine(root,"legacy");Directory.CreateDirectory(legacyRoot);
            var legacyPin=new Entry{Path=Path.Combine(root,"Start Menu","pinned.lnk"),Pinned=true,Name="用户的固定名称"};
            File.WriteAllText(Path.Combine(legacyRoot,"config.json"),JsonSerializer.Serialize(new Config{Version=1,Entries=new(){legacyPin,new Entry{Path=Path.Combine(root,"Start Menu","auto.lnk")}}}));
            var migrated=new Store(legacyRoot);
            Check("migration preserves non-desktop user pin and custom name",migrated.Data.Entries[0].Id==legacyPin.Id&&migrated.Data.Entries[0].Pinned&&migrated.Data.Entries[0].Name==legacyPin.Name);
            Check("migration keeps all legacy records without mixing drawer",migrated.Data.Entries.Count==2&&migrated.Data.Entries.All(e=>!e.InDrawer));
            Check("legacy configuration retained before migration",File.Exists(Path.Combine(legacyRoot,"config.json.before-v3")));
            Check("legacy auto import is never assumed user-added",migrated.Data.Entries.All(e=>e.Source=="legacy"&&e.IsAlias));
            var erroneous=new Entry{Path="::{031E4825-7B94-4dc3-B131-E946B44C8DD5}",Name="库",Source="system"};
            migrated.Data.Entries.Add(erroneous);migrated.Sync(Array.Empty<DesktopItem>());
            Check("wrong automatic namespace archived without deleting data",migrated.Data.Entries.Contains(erroneous)&&erroneous.Source=="retired-auto"&&!erroneous.InDrawer);
            migrated.Add(m2,false);var added=migrated.Data.Entries.Single(e=>e.Path==m2);
            Check("explicit user addition stays identifiable and visible",added.Source=="manual"&&added.Origin=="user-added"&&added.InDrawer&&added.IsAlias);
            migrated.Sync(Array.Empty<DesktopItem>());Check("explicit manual entry unaffected by desktop cleanup",migrated.Data.Entries.Contains(added));
            mirror.Data.Entries[0].AutoName=false;mirror.Data.Entries[0].Name="我的名字";
            var renamed=mirror.Data.Entries[0].Path;File.Move(m1,renamed,true);
            mirror.Sync(new[]{new DesktopItem(renamed,"one","desktop",null)});
            Check("custom entry names survive sync",mirror.Data.Entries.First(e=>e.Source=="desktop").Name=="我的名字");
            // Icon container: the artwork is measured, not blindly stretched.
            var probe=new System.Windows.Media.Imaging.WriteableBitmap(32,32,96,96,System.Windows.Media.PixelFormats.Pbgra32,null);
            var canvasPixels=new byte[32*32*4];
            for(var y=8;y<24;y++) for(var x=8;x<24;x++){var o=(y*32+x)*4;canvasPixels[o+3]=255;canvasPixels[o+2]=255;}
            probe.WritePixels(new Int32Rect(0,0,32,32),canvasPixels,32*4,0);
            var measured=Icons.Measure(probe);
            Check("icon content measured from alpha",measured.Content.Width==16&&measured.Content.Height==16&&!measured.Filled,$"{measured.Content.Width}x{measured.Content.Height} filled={measured.Filled}");
            var solid=new System.Windows.Media.Imaging.WriteableBitmap(32,32,96,96,System.Windows.Media.PixelFormats.Pbgra32,null);
            var solidPixels=new byte[32*32*4];for(var p4=0;p4<solidPixels.Length;p4+=4)solidPixels[p4+3]=255;
            solid.WritePixels(new Int32Rect(0,0,32,32),solidPixels,32*4,0);
            Check("full-bleed icon detected",Icons.Measure(solid).Filled);

            // 1.9.8: Icon stale-while-revalidate, blank detection, CLSID / StockIcon fallbacks.
            var blankBitmap=new System.Windows.Media.Imaging.WriteableBitmap(32,32,96,96,System.Windows.Media.PixelFormats.Pbgra32,null);
            Check("blank icon treated as missing",Icons.Measure(blankBitmap).Missing);
            var swr=new IconEntry();
            swr.Update(measured,"v1");
            swr.Update(IconEntry.Empty,"v2");
            Check("stale-while-revalidate retains last valid icon",!swr.Missing&&swr.Image!=null&&swr.Stamp=="");
            swr.Update(Icons.Measure(solid),"v3");
            Check("stale-while-revalidate resets retry count on recovery",!swr.Missing&&swr.Stamp=="v3"&&swr.RetryCount==0);
            Check("This PC CLSID icon resolved",Native.Icon("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",64)!=null);
            Check("StockIcon fallback resolved",Native.StockIcon(Environment.ProcessPath!,64)!=null);

            // Wake bindings are opt-in. Old unsupported key values should load unbound.
            var defaultCfg=new Store(Path.Combine(root,"cfg-defaults")).Data;
            Check("DoubleClickToOpen defaults to false",!defaultCfg.DoubleClickToOpen);
            Check("Wake shortcut defaults to unbound",defaultCfg.SuppressWakeKey==0&&defaultCfg.WakeShortcutKeys==null);
            var cfgDir=Path.Combine(root,"cfg-198");
            var cfgStore=new Store(cfgDir);
            cfgStore.Data.DoubleClickToOpen=true;
            cfgStore.Data.SuppressWakeKey=0x91;
            cfgStore.Data.WakeDisabled=true;
            cfgStore.SaveSettings();
            var cfgReload=new Store(cfgDir);
            Check("valid legacy single-key binding and mode persist",cfgReload.Data.DoubleClickToOpen&&cfgReload.Data.SuppressWakeKey==0x91&&cfgReload.Data.WakeDisabled);
            cfgReload.Data.SuppressWakeKey=0x13;cfgReload.Data.WakeDisabled=true;cfgReload.SaveSettings();
            var unsupportedWake=new Store(cfgDir).Data;
            Check("unsupported legacy wake key recovers to unbound",unsupportedWake.SuppressWakeKey==0&&!unsupportedWake.WakeDisabled&&
                !Native.IsWakeBindingKey(0x03)&&!Native.IsWakeBindingKey(0xE7)&&!Native.IsWakeBindingKey(0x11));
            Check("SuppressWakeKey labels valid",Native.KeyName(0)=="未设置快捷键"&&Native.KeyName(0x11)=="Ctrl");
            cfgReload.Data.WakeShortcutKeys=new[]{0x77,0xA2,0xA5,0xA1};cfgReload.SaveSettings();
            var shortcutConfig=new Store(cfgDir).Data;
            var shortcut=WakeShortcut.Create(shortcutConfig.WakeShortcutKeys??Array.Empty<int>());
            Check("four-key chord and mode persist in display order",shortcut?.Display=="Ctrl + Alt + Shift + F8"&&
                shortcutConfig.SuppressWakeKey==0&&shortcutConfig.WakeDisabled);
            cfgReload.Data.WakeShortcutKeys=new[]{0x11,0x12,0x20};cfgReload.SaveSettings();
            var ctrlAltSpaceConfig=new Store(cfgDir).Data;
            var ctrlAltSpace=WakeShortcut.Create(ctrlAltSpaceConfig.WakeShortcutKeys??Array.Empty<int>());
            Check("Ctrl + Alt + Space shortcut is permitted and persists",ctrlAltSpace?.Display=="Ctrl + Alt + Space"&&
                ctrlAltSpaceConfig.WakeShortcutKeys?.SequenceEqual(new[]{0x11,0x12,0x20})==true);
            var pressed=new HashSet<int>{0x77,0x11,0x12};
            Check("final modifier completes chord in any order",shortcut?.MatchesPress(0xA1,pressed.Contains)==true);
            pressed.Add(0x10);
            Check("chord rejects a repeated member",shortcut?.MatchesPress(0xA1,pressed.Contains)==false);
            pressed.Remove(0x11);pressed.Add(0x41);
            Check("extra held key cannot complete an exact shortcut",shortcut?.MatchesPress(0xA2,pressed.Contains)==false&&
                WakeShortcut.Create(new[]{0x11,0x12,0x10,0x77,0x41})==null);
            _ = Native.HasMaximizedWindow(System.Windows.Forms.Screen.PrimaryScreen!);
            Check("ShellDrop recognizes executable targets",ShellDrop.IsExecutableTarget(Environment.ProcessPath!));
            var dummyBat=Path.Combine(root,"tool.bat");File.WriteAllText(dummyBat,"@echo off");
            Check("ShellDrop recognizes batch scripts",ShellDrop.IsExecutableTarget(dummyBat));

            // Real shell file operations (IFileOperation) against real files.
            var ops=Path.Combine(root,"ops");Directory.CreateDirectory(ops);
            var opSrc=Path.Combine(ops,"a.txt");File.WriteAllText(opSrc,"a");
            var opDest=Path.Combine(ops,"dest");Directory.CreateDirectory(opDest);
            Check("shell copy real file",ShellOps.Copy(new[]{opSrc},opDest,0,true)&&File.Exists(Path.Combine(opDest,"a.txt")));
            Check("shell rename real file",ShellOps.Rename(opSrc,"b.txt",0,true)&&File.Exists(Path.Combine(ops,"b.txt"))&&!File.Exists(opSrc));
            Check("shell move real file",ShellOps.Move(new[]{Path.Combine(ops,"b.txt")},opDest,0,true)&&File.Exists(Path.Combine(opDest,"b.txt")));
            Check("shell rename keeps source intact on refusal",!ShellOps.Rename(Path.Combine(opDest,"a.txt"),"a.txt",0,true)&&File.Exists(Path.Combine(opDest,"a.txt")));
            Check("shell delete recycles file",ShellOps.Delete(new[]{Path.Combine(opDest,"b.txt")},false,0,true)&&!File.Exists(Path.Combine(opDest,"b.txt")));

            // Clipboard carries real files plus the copy/move intent, like Explorer.
            string? hadText=null;string[]? hadFiles=null;
            try { if(Clipboard.ContainsText())hadText=Clipboard.GetText(); } catch { }
            try { if(Clipboard.ContainsFileDropList())hadFiles=Clipboard.GetFileDropList().Cast<string>().ToArray(); } catch { }
            try
            {
                var clipPath=Path.Combine(opDest,"a.txt");
                Check("file clipboard accepts paths",ShellOps.SetFileClipboard(new[]{clipPath},false));
                var (read,move)=ShellOps.ReadFileClipboard();
                Check("file clipboard roundtrip",read.Length==1&&string.Equals(read[0],clipPath,StringComparison.OrdinalIgnoreCase)&&!move,string.Join(";",read)+" move="+move);
                Check("file clipboard marks cut",ShellOps.SetFileClipboard(new[]{clipPath},true)&&ShellOps.ReadFileClipboard().Move);
            }
            finally
            {
                try
                {
                    if(hadFiles is { Length:>0 })ShellOps.SetFileClipboard(hadFiles,false);
                    else if(hadText!=null)Clipboard.SetText(hadText);
                    else Clipboard.Clear();
                }
                catch { }
            }

            // Archive support goes through the archiver that ships with Windows.
            var archiveSource=Path.Combine(ops,"pack");Directory.CreateDirectory(archiveSource);
            File.WriteAllText(Path.Combine(archiveSource,"inside.txt"),"archived");
            Check("system archive creates zip",ShellOps.Compress(new[]{archiveSource},out var zipPath,out var zipError)&&File.Exists(zipPath),zipError);
            Directory.Delete(archiveSource,true);
            Check("system archive extracts zip",ShellOps.Extract(zipPath,out var unzipped,out var unzipError)&&File.Exists(Path.Combine(unzipped,"pack","inside.txt")),unzipError);
            Check("desktop reachable",Desktop.Reachable());

            Check("motion preference",true,$"clientAreaAnimation={SystemParameters.ClientAreaAnimation}; menuAnimation={SystemParameters.MenuAnimation}; reducedMotion={SystemParameters.MinimizeAnimation}");
            Write("self-test.json");return 0;
        }
        catch(Exception e) { results.Add(new { error=e.ToString() });Write("self-test.json");return 1; }
        finally { Directory.Delete(root,true); }
    }
    public static async Task SettingsShot(Controller c)
    {
        try
        {
            await c.ShowSettings(w =>
            {
                try
                {
                    w.UpdateLayout();
                    var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(w.ActualWidth * dpi.DpiScaleX), (int)(w.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(w);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    Directory.CreateDirectory(Output);
                    using (var fs = File.Create(Path.Combine(Output, "settings.png"))) enc.Save(fs);
                }
                catch (Exception e) { File.WriteAllText(Path.Combine(Output, "settings-error.txt"), e.ToString()); }
                finally { w.Close(); }
            });
        }
        finally { System.Windows.Application.Current.Shutdown(); }
    }

    public static async Task Idle(Controller c)
    {
        try
        {
            await Task.Delay(12000);
            async Task<int> Frames(int ms)
            {
                var n = 0; EventHandler h = (_, _) => n++;
                System.Windows.Media.CompositionTarget.Rendering += h;
                await Task.Delay(ms);
                System.Windows.Media.CompositionTarget.Rendering -= h;
                return n;
            }
            var withIndicator = await Frames(3000); var timers = Anim.ActiveTimers;
            c.TestIndicator(false);
            await Task.Delay(400);
            var withoutIndicator = await Frames(3000);
            c.Expand(); await Task.Delay(400);
            var withDock = await Frames(3000);
            File.WriteAllText(Path.Combine(Output, "idle.json"), JsonSerializer.Serialize(new { withIndicator, withoutIndicator, withDock, animTimers = timers, pointers = c.PointerEvents, winEvents = c.WinEventCount, prefs = c.PrefCount, positions = c.PositionCount, dispatches = c.PointerDispatch }));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(Output, "idle-error.txt"), e.ToString()); }
        finally { System.Windows.Application.Current.Shutdown(); }
    }

    // Lightweight visual/geometry probe used during development iterations.
    public static async Task Probe(Controller c)
    {
        try
        {
            await Task.Delay(400);
            int dockX = c.WorkArea.Left + c.WorkArea.Width / 2;
            Native.MovePointer(dockX, c.WorkArea.Bottom - 40); await Task.Delay(200);
            Native.MovePointer(dockX, c.WorkArea.Bottom - 15); await Task.Delay(500);
            Native.GetWindowRect(c.Dock.Handle, out var dock);
            using (var bmp = new System.Drawing.Bitmap(c.Screen.Bounds.Width, c.Screen.Bounds.Height))
            { using var g = System.Drawing.Graphics.FromImage(bmp); g.CopyFromScreen(c.Screen.Bounds.Location, System.Drawing.Point.Empty, bmp.Size); bmp.Save(Path.Combine(Output, "probe-dock.png")); }
            c.Busy = true; c.ToggleDrawer(); await Task.Delay(600);
            Native.GetWindowRect(c.Drawer!.Handle, out var drawer);
            using (var bmp = new System.Drawing.Bitmap(c.Screen.Bounds.Width, c.Screen.Bounds.Height))
            { using var g = System.Drawing.Graphics.FromImage(bmp); g.CopyFromScreen(c.Screen.Bounds.Location, System.Drawing.Point.Empty, bmp.Size); bmp.Save(Path.Combine(Output, "probe-drawer.png")); }
            Directory.CreateDirectory(Output);
            if (c.Dock.Surface != null)
            {
                var sfc = c.Dock.Surface;
                sfc.UpdateLayout();
                var w = (int)Math.Ceiling(sfc.ActualWidth * 4); var h = (int)Math.Ceiling(sfc.ActualHeight * 4);
                var rtbDock = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 384, 384, System.Windows.Media.PixelFormats.Pbgra32);
                rtbDock.Render(sfc);
                var enc0 = new System.Windows.Media.Imaging.PngBitmapEncoder(); enc0.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtbDock));
                using (var fs0 = File.Create(Path.Combine(Output, "probe-dock-render.png"))) enc0.Save(fs0);
            }
            File.WriteAllText(Path.Combine(Output, "probe.json"), JsonSerializer.Serialize(new
            {
                screen = c.Screen.Bounds,
                dock = new { dock.Left, dock.Top, dock.Right, dock.Bottom, dipWidth = c.Dock.Width, dipHeight = c.Dock.Height },
                drawer = new { drawer.Left, drawer.Top, drawer.Right, drawer.Bottom, dipWidth = c.Drawer.Width, dipHeight = c.Drawer.Height },
                columns = c.Drawer.MeasuredColumns(), rows = c.Drawer.VisibleGridRows,
                grid = c.Drawer.GridDiagnostics,
                glyph = c.Dock.GlyphDiag,
                drawerOpacity = c.Drawer.SurfaceOpacity,
                scale = c.Scale,
                layoutDrawerWidth = c.Layout.DrawerWidth,
                layoutUnit = c.Layout.Unit,
                drawerWidthDip = c.Drawer.Width,
                drawerActual = c.Drawer.ActualWidth,
                workArea = new { c.WorkArea.Width, c.WorkArea.Height, c.WorkArea.Left, c.WorkArea.Top },
                drawerGroups = c.Drawer.RenderedGroups,
                entries = c.Store.Data.Entries.Count,
                searchText = "" ,
                layout = new { c.Layout.Unit, c.Layout.DrawerTile, c.Layout.GapX, c.Layout.Chrome, c.Layout.Columns, c.Layout.VisibleRows, c.Layout.DrawerWidth }
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe-error.txt"), e.ToString()); }
        finally { System.Windows.Application.Current.Shutdown(); }
    }

    // Material check on light, dark and busy backgrounds: no white outline, content stays readable.
    public static async Task Contrast(Controller c)
    {
        var area=c.Screen.Bounds;
        try
        {
            foreach(var (name,background) in new (string,System.Windows.Media.Brush)[]
            {
                ("light",new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236,239,243))),
                ("dark",System.Windows.Media.Brushes.Black),
                ("busy",BusyBrush())
            })
            {
                var fixture=new Window
                {
                    WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,
                    Left=0,Top=0,Width=area.Width/c.Scale-4,Height=area.Height/c.Scale-4,
                    Background=background,Content=null
                };
                fixture.Show();await Task.Delay(250);
                c.Expand();c.ToggleDrawer();await Task.Delay(700);
                Directory.CreateDirectory(Output);
                using(var bmp=new System.Drawing.Bitmap(area.Width,area.Height))
                { using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(area.Location,System.Drawing.Point.Empty,bmp.Size);bmp.Save(Path.Combine(Output,"contrast-"+name+".png")); }
                c.CloseDrawer();c.Collapse();fixture.Close();await Task.Delay(350);
            }
        }
        catch(Exception e) { results.Add(new{error=e.ToString()});Program.Log(e); }
        finally { Write("contrast-test.json");System.Windows.Application.Current.Shutdown(); }
    }

    static System.Windows.Media.Brush BusyBrush()
    {
        var brush=new System.Windows.Media.LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,1) };
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(240,120,90),0));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(60,90,200),.45));
        brush.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(30,170,120),1));
        return brush;
    }

    // Shows the real Explorer context menu for a desktop item and captures it, then dismisses it.
    public static async Task NativeMenu(Controller c)
    {
        try
        {
            await Task.Delay(500);
            c.Busy=true;c.ToggleDrawer();await Task.Delay(700);
            var entry=c.Store.Data.Entries.FirstOrDefault(e=>e.InDrawer&&!e.IsVirtual&&File.Exists(e.Path));
            if(entry==null) { results.Add(new{error="no real desktop entry"}); return; }
            c.Drawer!.EnsureVisible(entry.Id);await Task.Delay(180);
            var point=c.Drawer.TileCenter(entry.Id);
            Native.MovePointer(point.X,point.Y);await Task.Delay(200);
            var shot=new Thread(()=>
            {
                Thread.Sleep(3200);
                try
                {
                    var b=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);
                    using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,b.Size);
                    Directory.CreateDirectory(Output);b.Save(Path.Combine(Output,"native-menu.png"));
                }
                catch(Exception e) { File.WriteAllText(Path.Combine(Output,"native-menu-error.txt"),e.ToString()); }
                Native.TestKey(0x1B,true);Native.TestKey(0x1B,false);
            });
            shot.IsBackground=true;shot.SetApartmentState(ApartmentState.STA);shot.Start();
            var started=Environment.TickCount64;
            c.ShowNativeMenu(new[]{entry});
            var elapsed=Environment.TickCount64-started;
            shot.Join(6000);
            results.Add(new{name="native shell menu opened",pass=ShellMenu.LastItems>0,detail=$"items={ShellMenu.LastItems} command={ShellMenu.LastCommand} elapsed={elapsed}ms entry={entry.Name}"});
        }
        catch(Exception e) { results.Add(new{error=e.ToString()});Program.Log(e); }
        finally { c.Busy=false;c.CloseDrawer();Write("native-menu-test.json");System.Windows.Application.Current.Shutdown(); }
    }

    // End-to-end acceptance on the real desktop: a file is created, selected with the mouse,
    // copied with Ctrl+C, pasted with Ctrl+V, renamed and deleted through the shell, and every
    // step is checked in the drawer. Everything created here is removed afterwards.
    public static async Task FileOps(Controller c)
    {
        var desktop=Desktop.UserFolder;
        var name="MacDock 验收 "+Guid.NewGuid().ToString("N")[..8];
        var original=Path.Combine(desktop,name+".txt");
        var renamed=Path.Combine(desktop,name+" 重命名.txt");
        string? copy=null;
        string? hadText=null;string[]? hadFiles=null;
        try
        {
            try { if(Clipboard.ContainsText())hadText=Clipboard.GetText(); } catch { }
            try { if(Clipboard.ContainsFileDropList())hadFiles=Clipboard.GetFileDropList().Cast<string>().ToArray(); } catch { }
            File.WriteAllText(original,"MacDock shell acceptance");
            await Task.Delay(1600);
            Check("desktop addition appears in drawer",c.Store.Data.Entries.Any(e=>string.Equals(e.Path,original,StringComparison.OrdinalIgnoreCase)));
            c.Busy=true;c.ToggleDrawer();await Task.Delay(600);
            var entry=c.Store.Data.Entries.First(e=>string.Equals(e.Path,original,StringComparison.OrdinalIgnoreCase));
            c.Drawer!.EnsureVisible(entry.Id);await Task.Delay(180);
            var point=c.Drawer.TileCenter(entry.Id);
            Native.MovePointer(point.X,point.Y);await Task.Delay(220);
            Native.TestKey(0x11,true);await Task.Delay(30);
            Native.TestButton(true);await Task.Delay(60);Native.TestButton(false);
            Native.TestKey(0x11,false);await Task.Delay(260);
            Check("ctrl click selects the desktop item",c.Drawer.SelectedCount==1,$"selected={c.Drawer.SelectedCount}; point={point.X},{point.Y}");
            using(var bmp=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height))
            { using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,bmp.Size);Directory.CreateDirectory(Output);bmp.Save(Path.Combine(Output,"file-selected.png")); }

            Native.TestKey(0x11,true);Native.TestKey(0x43,true);Native.TestKey(0x43,false);Native.TestKey(0x11,false);
            await Task.Delay(400);
            var clipboardHit=false;
            try { clipboardHit=Clipboard.ContainsFileDropList()&&Clipboard.GetFileDropList().Cast<string>().Any(p=>string.Equals(p,original,StringComparison.OrdinalIgnoreCase)); } catch { }
            Check("Ctrl+C puts the real file on the clipboard",clipboardHit);

            Native.TestKey(0x11,true);Native.TestKey(0x56,true);Native.TestKey(0x56,false);Native.TestKey(0x11,false);
            await Task.Delay(1800);
            copy=Directory.GetFiles(desktop,name+"*.txt").FirstOrDefault(p=>!string.Equals(p,original,StringComparison.OrdinalIgnoreCase));
            Check("Ctrl+V creates a real copy on the desktop",copy!=null&&File.Exists(copy));
            Check("pasted file appears in drawer",copy!=null&&c.Store.Data.Entries.Any(e=>string.Equals(e.Path,copy,StringComparison.OrdinalIgnoreCase)),copy??"none");
            using(var bmp=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height))
            { using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,bmp.Size);bmp.Save(Path.Combine(Output,"file-pasted.png")); }

            Check("shell rename of a desktop file",copy!=null&&ShellOps.Rename(copy,name+" 重命名.txt",0,true));
            await Task.Delay(1600);
            Check("rename syncs into drawer",c.Store.Data.Entries.Any(e=>string.Equals(e.Path,renamed,StringComparison.OrdinalIgnoreCase)));
            Check("old path gone after rename",!c.Store.Data.Entries.Any(e=>string.Equals(e.Path,copy,StringComparison.OrdinalIgnoreCase)));

            Check("shell delete recycles the copy",ShellOps.Delete(new[]{renamed},false,0,true),$"hr=0x{ShellOps.LastHResult:X8} aborted={ShellOps.LastAborted} exists={File.Exists(renamed)}");
            await Task.Delay(1600);
            Check("delete syncs into drawer",!c.Store.Data.Entries.Any(e=>string.Equals(e.Path,renamed,StringComparison.OrdinalIgnoreCase)));
        }
        catch(Exception e) { results.Add(new{error=e.ToString()});Program.Log(e); }
        finally
        {
            foreach(var p in new[]{original}.Concat(Directory.GetFiles(desktop,name+"*.txt")))
            { try { if(File.Exists(p))File.Delete(p); } catch { } }
            try
            {
                if(hadFiles is { Length:>0 })ShellOps.SetFileClipboard(hadFiles,false);
                else if(hadText!=null)Clipboard.SetText(hadText);
                else Clipboard.Clear();
            }
            catch { }
            c.Busy=false;c.CloseDrawer();c.SyncDesktop(true);
            Write("file-test.json");System.Windows.Application.Current.Shutdown();
        }
    }

    public static async Task Ui(Controller c)
    {
        var root=Path.Combine(Path.GetTempPath(),"MacDock-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        Native.GetCursorPos(out var original);
        var saved=JsonSerializer.Serialize(c.Store.Data.Entries);string mode=c.Store.Data.Mode;
        try
        {
            Check("mouse hook installed",c.HookAvailable,System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            Check("indicator desktop available",System.Windows.Forms.Screen.AllScreens.Length>0);
            var area=c.WorkArea;int x=area.Left+area.Width/2,y=area.Bottom-(int)(15*c.Scale);
            // Park the pointer away from the wake strip first so the intent state is deterministic.
            Native.MovePointer(x,area.Top+(int)(200*c.Scale));await Task.Delay(450);
            for(int step=0;step<6;step++){Native.MovePointer(x,y-(int)(120*c.Scale)+step*18);await Task.Delay(25);}
            Native.MovePointer(x,y);await Task.Delay(650);
            Check("real pointer approach expands",c.Dock.IsVisible,c.Diagnostics+" | "+c.Trail);
            Check("dock does not activate",Native.GetForegroundWindow()!=c.Dock.Handle);
            Native.MovePointer(x,y-250);await Task.Delay(550);Check("leave collapses",!c.Dock.IsVisible);
            var hit=Native.WindowFromPoint(new Native.Point(x,y));Check("hidden indicator click-through",hit!=0&&hit!=c.IndicatorHandle&&hit!=c.Dock.Handle,"underlying hwnd "+hit);
            // The pure intent tests cover pressed states; never inject a global button-down on a user desktop.
            c.Busy=true;c.ToggleDrawer();await Task.Delay(250);Check("drawer opens",c.Drawer?.IsVisible==true);
            Check("drawer uses a landscape layout",c.Drawer!.Width>c.Drawer.Height*1.25,$"{c.Drawer.Width:F0}x{c.Drawer.Height:F0}");
            var selectable=c.Store.Data.Entries.FirstOrDefault(e=>e.InDrawer&&!e.IsVirtual&&File.Exists(e.Path));
            if(selectable!=null)
            {
                var tilePoint=c.Drawer.TileCenter(selectable.Id);
                Native.MovePointer(tilePoint.X,tilePoint.Y);await Task.Delay(140);
                Native.TestKey(0x11,true);await Task.Delay(40);
                Native.TestButton(true);await Task.Delay(60);Native.TestButton(false);
                Native.TestKey(0x11,false);await Task.Delay(250);
                Check("ctrl click selects a real item",c.Drawer.SelectedCount==1,$"selected={c.Drawer.SelectedCount}; tile={tilePoint.X},{tilePoint.Y}; drawer={c.Drawer.Width:F0}x{c.Drawer.Height:F0}");
                Native.TestKey(0x1B,true);Native.TestKey(0x1B,false);await Task.Delay(120);
                await Task.Delay(180);Check("Escape closes selection and retains Dock",c.Drawer.SelectedCount==0&&!c.Drawer.IsVisible&&c.Dock.IsVisible);c.ToggleDrawer();await Task.Delay(320);
            }
            foreach(var m in new[]{"自定义","按类型","首字母"}) { c.Store.Data.Mode=m;c.Drawer!.Render();await Task.Delay(40);Check("drawer render "+m,c.Drawer.IsVisible); }
            c.CloseDrawer();c.Busy=false;Native.MovePointer(x,y-250);await Task.Delay(550);
            var src=Path.Combine(root,"source.txt");var dest=Path.Combine(root,"destination");Directory.CreateDirectory(dest);File.WriteAllText(src,"shell native fixture");
            using(var drop=new ShellDrop(dest,new[]{src}))
            {
                uint effect=drop.Enter(9,new Native.Point(x,y),3);Check("Shell folder accepts Ctrl-copy",effect==1,"effect "+effect);
                effect=drop.Drop(8,new Native.Point(x,y),3);await Task.Delay(300);Check("Shell copy result",effect==1&&File.Exists(src)&&File.ReadAllText(Path.Combine(dest,"source.txt"))=="shell native fixture");
            }
            var move=Path.Combine(root,"move.txt");File.WriteAllText(move,"move fixture");
            using(var drop=new ShellDrop(dest,new[]{move}))
            {
                uint effect=drop.Enter(5,new Native.Point(x,y),3);Check("Shell folder accepts Shift-move",effect==2,"effect "+effect);
                effect=drop.Drop(4,new Native.Point(x,y),3);await Task.Delay(300);Check("Shell move result",effect==2&&!File.Exists(move)&&File.Exists(Path.Combine(dest,"move.txt")));
            }
            using(var drop=new ShellDrop(src,new[]{Path.Combine(dest,"source.txt")}))Check("incompatible document rejects",drop.Enter(0,new Native.Point(x,y),3)==0);
            // Standard OLE file source -> actual WPF target -> Shell folder, driven by Windows input.
            var actual=Path.Combine(root,"actual-drag.txt");File.WriteAllText(actual,"end to end file");
            c.Store.Add(dest,true);c.Changed();var target=c.Store.Data.Entries.First(e=>e.Path==dest);
            c.Store.Save();Check("profile config saved and reloaded",new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.Entries.Any(e=>e.Path==dest));
            c.CloseDrawer();c.Expand();await Task.Delay(400);c.Collapse();
            Native.Rect dockBounds;Native.MovePointer(x,y);
            c.Expand();await Task.Delay(320);
            Native.GetWindowRect(c.Dock.Handle,out dockBounds);
            var tileAt=c.Dock.TileCenter(target.Id);
            Check("dock tile reachable for a drop",tileAt.X>dockBounds.Left&&tileAt.X<dockBounds.Right&&tileAt.Y>dockBounds.Top&&tileAt.Y<dockBounds.Bottom,$"tile={tileAt.X},{tileAt.Y}; dock={dockBounds.Left},{dockBounds.Top},{dockBounds.Right},{dockBounds.Bottom}");
            Native.MovePointer(tileAt.X,tileAt.Y);await Task.Delay(420);
            Check("dock magnifies under the pointer",c.Dock.MaxScale>1.08,c.Dock.MagnifyState+$"; hit={Native.WindowFromPoint(tileAt)}; dock={c.Dock.Handle}; Busy={c.Busy}; menu={c.MenuOpen}; dragging={c.InternalDragging}; reduced={c.Store.Data.ReducedMotion}; "+c.Diagnostics+"; "+c.Trail);
            Native.MovePointer(tileAt.X+6,tileAt.Y);await Task.Delay(160);
            Check("magnification keeps a stable wave",c.Dock.MaxScale>1.05,c.Dock.MagnifyState);
            Check("magnified dock keeps its click target",c.Dock.HitTestEntry(tileAt)!=null,"hit="+(c.Dock.HitTestEntry(tileAt)??"none"));
            Native.MovePointer(area.Left+(int)(60*c.Scale),area.Top+(int)(180*c.Scale));await Task.Delay(900);
            Check("dock returns to rest after leaving",c.Dock.MaxScale<1.02&&!c.Dock.WaveRunning,c.Dock.MagnifyState);
            c.Expand();await Task.Delay(320);
            c.Busy=true;c.ToggleDrawer();await Task.Delay(300);
            c.CloseDrawer();c.Busy=false;await Task.Delay(300);
            c.Store.Data.Entries.Remove(target);c.Changed();
            var gridFixtures=Enumerable.Range(0,40).Select(n=>new Entry { Path=src,Name="布局验收 "+n }).ToArray();c.Store.Data.Entries.AddRange(gridFixtures);c.Store.Data.Mode="自定义";c.Store.Data.DockCapacity=0;c.Store.Data.DrawerRows=0;c.Store.Data.DrawerColumns=0;c.Changed();
            var order=c.Store.Data.Entries.Select(e=>e.Id).ToArray();
            Exception? settingsFailure=null;
            await c.ShowSettings(w=>
            {
                try
                {
                    static IEnumerable<DependencyObject> Walk(DependencyObject o) { yield return o;foreach(var child in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>())foreach(var d in Walk(child))yield return d; }
                    var boxes=new[]{"Dock 显示数量","抽屉可见行数","抽屉列数"}.Select(name=>Walk(w).OfType<System.Windows.Controls.ComboBox>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)==name)).ToArray();
                    var save=Walk(w).OfType<System.Windows.Controls.Button>().Single(b=>b.Content as string=="保存布局");
                    boxes[0].SelectedItem="7";boxes[1].SelectedItem="3";boxes[2].SelectedItem="6";
                    Check("settings layout remains unchanged until saved",c.Store.Data.DockCapacity==0&&c.Store.Data.DrawerRows==0&&c.Store.Data.DrawerColumns==0&&save.IsEnabled);
                    save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,save));
                    c.Drawer!.UpdateLayout();Check("actual six columns with scrollbar",c.Drawer.MeasuredColumns()==6,"columns "+c.Drawer.MeasuredColumns()+"; "+c.Drawer.GridDiagnostics);
                    Check("actual three visible grid rows",c.Drawer.VisibleGridRows>=2.95&&c.Drawer.VisibleGridRows<3.15,"rows "+c.Drawer.VisibleGridRows.ToString("F3")+"; "+c.Drawer.GridDiagnostics);
                    Check("settings keep entry order",order.SequenceEqual(c.Store.Data.Entries.Select(e=>e.Id)));
                    var reload=new Store(Path.GetDirectoryName(c.Store.FilePath));Check("saved layout persisted",reload.Data.DockCapacity==7&&reload.Data.DrawerColumns==6);
                    foreach(var b in boxes)b.SelectedIndex=0;
                    save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,save));
                    Check("automatic layout restored",c.Store.Data.DockCapacity==0&&c.Store.Data.DrawerRows==0&&c.Store.Data.DrawerColumns==0);
                }
                catch(Exception ex) { settingsFailure=ex; }
                finally { w.Close(); }
            });
            if(settingsFailure!=null)throw settingsFailure;
            foreach(var entry in gridFixtures)c.Store.Data.Entries.Remove(entry);c.Changed();
            await Material(c);
            c.Expand();Native.MovePointer(x,y-250);await Task.Delay(230);
            using(var bmp=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height))
            { using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,bmp.Size);Directory.CreateDirectory(Output);bmp.Save(Path.Combine(Output,"desktop-dock.png")); }
            c.Busy=true;c.ToggleDrawer();await Task.Delay(200);
            using(var bmp=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height))
            { using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,bmp.Size);bmp.Save(Path.Combine(Output,"desktop-drawer.png")); }
            c.CloseDrawer();c.Busy=false;await Task.Delay(600);
            var fixture=new Window { Title="MacDock window-switch test",Width=320,Height=180,WindowStartupLocation=WindowStartupLocation.CenterScreen };
            fixture.Show();await Task.Delay(100);var fh=new System.Windows.Interop.WindowInteropHelper(fixture).Handle;
            Check("running window discovered",Native.Windows(Environment.ProcessPath!).Any(w=>w.Handle==fh));
            c.Launch(new Entry { Path=Environment.ProcessPath!,Name="test" });await Task.Delay(100);
            Check("running window activation",Native.GetForegroundWindow()==fh);fixture.Close();
            bool priorStartup=Controller.StartupEnabled();
            string? priorRun=null;
            using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
                priorRun=key?.GetValue("MacDock") as string;
            try
            {
                Controller.SetStartup(true);
                Check("startup enabled",Controller.StartupEnabled());
                Controller.SetStartup(false);
                Check("startup disabled",!Controller.StartupEnabled());
            }
            finally
            {
                Controller.SetStartup(false);
                if(priorRun!=null)
                {
                    using var writeKey=Microsoft.Win32.Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
                    writeKey.SetValue("MacDock",priorRun);
                }
                else if(priorStartup)
                {
                    Controller.SetStartup(true);
                }
            }
            c.Expand();
            using(var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--fullscreen-fixture") { UseShellExecute=false })!)
            {
                await Task.Delay(700);child.Refresh();Native.SetForegroundWindow(child.MainWindowHandle);await Task.Delay(400);
                Check("borderless fullscreen detected",Native.Fullscreen(c.Screen));
                Check("fullscreen hides dock and drawer",!c.Dock.IsVisible&&c.Drawer?.IsVisible!=true);
                c.Expand();Check("fullscreen blocks wake",!c.Dock.IsVisible);
                await Task.Delay(2300);
            }
            c.CheckFullscreen();
            for(int cycle=0;cycle<20;cycle++) { c.Busy=true;c.ToggleDrawer();await Task.Delay(15);c.CloseDrawer();c.Busy=false; }
            await Task.Delay(700);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            Check("20 drawer lifecycle cycles",c.Drawer?.IsVisible!=true,$"drawer={c.Drawer?.IsVisible}; dock={c.Dock.IsVisible}");
            Native.MovePointer(area.Left+(int)(80*c.Scale),area.Top+(int)(120*c.Scale));await Task.Delay(2500);
            var proc=Process.GetCurrentProcess();proc.Refresh();var before=proc.TotalProcessorTime;var memory=proc.PrivateMemorySize64;
            await Task.Delay(20000);proc.Refresh();var cpu=(proc.TotalProcessorTime-before).TotalMilliseconds/20000*100;
            Check("idle 20s CPU < 2 percent of one core",cpu<2,$"CPU {cpu:F3}% of one core; private {proc.PrivateMemorySize64/1048576.0:F1} MiB; delta {(proc.PrivateMemorySize64-memory)/1048576.0:F2} MiB; pointerEvents={c.PointerEvents}; dispatches={c.PointerDispatch}");
        }
        catch(Exception e) { results.Add(new { error=e.ToString() });Program.Log(e); }
        finally
        {
            c.Busy=false;c.CloseDrawer();c.Store.Data.Entries=JsonSerializer.Deserialize<List<Entry>>(saved)!;c.Store.Data.Mode=mode;c.Changed();Native.SetCursorPos(original.X,original.Y);
            Write("ui-test.json");Directory.Delete(root,true);System.Windows.Application.Current.Shutdown();
        }
    }
    internal static async Task VisualOnly(Controller c)
    {
        try { await Material(c); }catch(Exception e){results.Add(new { error=e.ToString() });}
        finally { Write("visual-test.json");System.Windows.Application.Current.Shutdown(); }
    }
    static async Task Material(Controller c)
    {
        if(Native.HostMaterial){Check("independent external-process glass test",await ExternalGlassChecks.Run(c,false));return;}
        var area=c.WorkArea;
        var stripes=new System.Windows.Controls.Grid();
        var colors=new List<System.Windows.Controls.Border>();
        for(int j=0;j<120;j++)
        {
            stripes.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition());
            var b=new System.Windows.Controls.Border { Background=j%2==0?System.Windows.Media.Brushes.Red:System.Windows.Media.Brushes.Black };System.Windows.Controls.Grid.SetColumn(b,j);stripes.Children.Add(b);if(j%2==0)colors.Add(b);
        }
        var fixture=new Window { Title="MacDock live glass test",Topmost=true,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,Left=area.Left/c.Scale,Top=(area.Bottom-850*c.Scale)/c.Scale,Width=area.Width/c.Scale,Height=850,Content=stripes };
        c.Busy=true;
        try
        {
            c.CloseDrawer();fixture.Show();await Task.Delay(120);c.Expand();c.ToggleDrawer();await Task.Delay(300);
            Native.SetWindowPos(c.Dock.Handle,-1,0,0,0,0,0x13);Native.SetWindowPos(c.Drawer!.Handle,-1,0,0,0,0,0x13);await Task.Delay(150);Check("Dock composition API accepted",c.Dock.MaterialAvailable);Check("drawer composition API accepted",c.Drawer!.MaterialAvailable);
            Native.GetWindowRect(c.Dock.Handle,out var dr);Native.GetWindowRect(c.Drawer.Handle,out var ar);
            var samples=new[]{new System.Drawing.Rectangle(dr.Left+8,dr.Top+5,Math.Max(30,dr.Right-dr.Left-16),6),new System.Drawing.Rectangle(ar.Left+(ar.Right-ar.Left)/2-30,ar.Bottom-34,60,14)};
            static (double r,double b,double variance) Sample(System.Drawing.Rectangle rect)
            {
                using var bitmap=new System.Drawing.Bitmap(rect.Width,rect.Height);using(var g=System.Drawing.Graphics.FromImage(bitmap))g.CopyFromScreen(rect.Location,System.Drawing.Point.Empty,rect.Size);
                double red=0,blue=0,sq=0;int n=0;for(int yy=0;yy<rect.Height;yy++)for(int xx=0;xx<rect.Width;xx++){var p=bitmap.GetPixel(xx,yy);red+=p.R;blue+=p.B;sq+=p.R*p.R;n++;}
                return(red/n,blue/n,Math.Sqrt(Math.Max(0,sq/n-red/n*(red/n))));
            }
            var first=samples.Select(Sample).ToArray();
            foreach(var b in colors)b.Background=System.Windows.Media.Brushes.Blue;await Task.Delay(300);
            var second=samples.Select(Sample).ToArray();
            for(int k=0;k<2;k++)Check(k==0?"Dock live background changes":"drawer live background changes",Math.Abs(first[k].r-second[k].r)+Math.Abs(first[k].b-second[k].b)>8,$"red phase R={first[k].r:F1} B={first[k].b:F1}; blue phase R={second[k].r:F1} B={second[k].b:F1}; red stripe stddev={first[k].variance:F1}");
            for(int k=0;k<2;k++)Check(k==0?"Dock stripe blur attenuation":"drawer stripe blur attenuation",first[k].variance<65,$"red stripe standard deviation {first[k].variance:F1}; source ideal 127.5");
            Directory.CreateDirectory(Output);
            using(var bmp=new System.Drawing.Bitmap(ar.Right-ar.Left,(int)((c.Layout.DrawerHeight(2,2)+c.Layout.DockHeight+45*c.Layout.Unit)*c.Scale)))
            { using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen(ar.Left,ar.Top,0,0,bmp.Size);bmp.Save(Path.Combine(Output,"live-glass.png")); }
        }
        finally { c.CloseDrawer();fixture.Close();c.Busy=false; }
    }
}
