using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace MacDock;

internal static class MirrorChecks
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool GetMenuItemRect(nint owner,nint menu,uint item,out Native.Rect rect);
    static IEnumerable<T> Walk<T>(DependencyObject o)where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(o);i++){var c=VisualTreeHelper.GetChild(o,i);if(c is T t)yield return t;foreach(var v in Walk<T>(c))yield return v;}}
    internal static async Task Run(Controller c)
    {
        var output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;Directory.CreateDirectory(output);
        var checks=new List<object>();void Check(string name,bool pass,object? detail=null){checks.Add(new{name,pass,detail});if(!pass)throw new Exception(name);}
        var token="MacDock-mirror-"+Guid.NewGuid().ToString("N");var outside=Path.Combine(Path.GetTempPath(),token);var folder=Path.Combine(Desktop.UserFolder,token);Directory.CreateDirectory(outside);
        var newFolders=new List<string>();
        Native.GetCursorPos(out var pointer);var foreground=Native.GetForegroundWindow();PointerHook.NonInjectedInputs=0;PointerHook.AuditInput=true;var clipboard=System.Windows.Clipboard.GetDataObject();
        bool Has(string path)=>c.Store.Data.Entries.Any(e=>e.InDrawer&&e.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
        async Task Wait(Func<bool> ready){for(int n=0;n<80&&!ready();n++)await Task.Delay(60);Check("real watcher converges",ready());}
        void Key(byte key){Native.TestKey(key,true);Native.TestKey(key,false);}
        void Shot(string name){using var b=new System.Drawing.Bitmap(c.Screen.Bounds.Width,c.Screen.Bounds.Height);using(var g=System.Drawing.Graphics.FromImage(b))g.CopyFromScreen(c.Screen.Bounds.Location,System.Drawing.Point.Empty,b.Size);b.Save(Path.Combine(output,name+".png"));}
        async Task Menu(string name,Action trigger,string? verb=null)
        {
            bool displayed=false,stable=false;int windows=0;string[] verbs=Array.Empty<string>(),labels=Array.Empty<string>();var bounds=new List<string>();Thread? worker=null;
            ShellMenu.MenuDisplayed=()=>{displayed=true;verbs=ShellMenu.LastVerbs.ToArray();labels=ShellMenu.LastLabels.ToArray();worker=new Thread(()=>{Thread.Sleep(650);stable=c.MenuOpen&&c.Drawer?.IsVisible==true;Native.EnumWindows((h,_)=>{if(Native.IsWindowVisible(h)&&Native.Class(h)=="#32768"){windows++;Native.GetWindowRect(h,out var r);bounds.Add($"{r.Left},{r.Top},{r.Right},{r.Bottom}");}return true;},0);Shot(name);if(verb=="paste"&&GetMenuItemRect(0,ShellMenu.ActivePopup,0,out var first)){Native.MovePointer((first.Left+first.Right)/2,(first.Top+first.Bottom)/2);Thread.Sleep(120);Native.TestButton(true);Thread.Sleep(35);Native.TestButton(false);}else if(verb!=null&&Array.FindIndex(verbs,v=>v==verb) is int index&&index>=0){Key(0x24);for(int n=0;n<index;n++)Key(0x28);Key(13);}else Key(27);}){IsBackground=true};worker.Start();};
            try{trigger();for(int n=0;n<80&&(!displayed||c.MenuOpen);n++)await Task.Delay(60);worker?.Join(2000);Check(name+" uses actual stable Shell popup",displayed&&stable&&windows>0&&ShellMenu.LastItems>0,new{displayed,stable,windows,bounds,ShellMenu.LastItems,ShellMenu.LastCommand,verbs,labels});Check(name+" leaves drawer usable",c.Drawer?.IsVisible==true&&!c.MenuOpen);if(verb!=null)Check(name+" native command selected",verbs.Contains(verb)&&ShellMenu.LastCommand>0);}
            finally{ShellMenu.MenuDisplayed=null;}
        }
        try
        {
            c.ToggleDrawer();await Task.Delay(600);var d=c.Drawer!;Native.GetWindowRect(d.Handle,out var initial);Native.MovePointer(initial.Right-90,initial.Top+25);Native.TestButton(true);Native.TestButton(false);await Task.Delay(120);
            Check("drawer opening has no search caret",!d.TextInputActive&&d.IsKeyboardFocusWithin);
            Native.TestKey(0x11,true);Key(0x46);Native.TestKey(0x11,false);await Task.Delay(100);Check("Ctrl+F deliberately starts editing",d.TextInputActive,new{foreground=(long)Native.GetForegroundWindow(),drawer=(long)d.Handle,PointerHook.NonInjectedInputs});
            var field=Walk<TextBox>(d).Single();field.Text="回收";await Task.Delay(100);Check("Chinese search filters actual entries",Walk<TextBlock>(d).Any(t=>t.Text.Contains("回收"))&&d.MeasuredColumns()<=1);field.Clear();d.FocusGrid();Check("search returns without automatic caret",!d.TextInputActive);
            await Task.Delay(150);Shot("default-glass-search-fonts");
            Check("actual desktop icon preference read",DesktopView.IconSizeAvailable&&DesktopView.IconSizeDip is >=16 and <=256,new{DesktopView.IconSizeDip,DesktopView.IconSizeAvailable});
            foreach(var dpi in new[]{1.0,1.25,1.5,2.0})foreach(var icon in new[]{32,48,96}){var l=new Layout(2560/dpi,1392/dpi,DesktopIconDip:icon);Check("bounded DIP layout "+dpi+" desktop "+icon,l.DrawerWidth<l.WorkWidth&&l.DrawerHeight(99,0)+l.DockHeight+70<l.WorkHeight&&l.DrawerIcon>=40&&l.VisibleRows>=1);}
            Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"nested.txt"),"must not enumerate nested children");await Wait(()=>Has(folder));Check("desktop scope is nonrecursive",!Has(Path.Combine(folder,"nested.txt")));
            var source=Path.Combine(outside,token+" 中文 file.txt");File.WriteAllText(source,"original content");Check("clipboard fixture",ShellOps.SetFileClipboard(new[]{source},false));c.PasteToDesktop();var file=Path.Combine(Desktop.UserFolder,Path.GetFileName(source));await Wait(()=>Has(file));Check("drawer paste creates same real desktop object",File.ReadAllText(file)=="original content"&&File.Exists(source));
            var entry=c.Store.Data.Entries.Single(e=>e.Path==file);var id=entry.Id;c.SetPinned(new[]{entry},true);var manual=new Entry{Path=source,Name="manual boundary",Source="manual",Origin="user-added"};c.Store.Data.Entries.Add(manual);
            var renamed=Path.Combine(Desktop.UserFolder,token+" renamed.txt");File.Move(file,renamed);await Wait(()=>Has(renamed)&&!Has(file));entry=c.Store.Data.Entries.Single(e=>e.Path==renamed);Check("external rename preserves identity and pin",entry.Id==id&&entry.Pinned);
            if(!d.IsVisible)c.ToggleDrawer();d.Prepare();await Task.Delay(200);d.EnsureVisible(entry.Id);await Task.Delay(200);var p=d.TileCenter(entry.Id);Native.MovePointer(p.X,p.Y);await Task.Delay(100);Shot("before-native-file-menu");Check("real file tile receives rightclick",d.IsVisible&&Native.WindowFromPoint(p)==d.Handle,new{d.IsVisible,point=$"{p.X},{p.Y}",hit=(long)Native.WindowFromPoint(p),drawer=(long)d.Handle,entry.Source,entry.IsAlias,diagnostics=d.GridDiagnostics});
            await Menu("native-file-rightclick-copy",()=>{Native.TestRightButton(true);Native.TestRightButton(false);},"copy");Check("native copy targets correct real file",ShellOps.ReadFileClipboard().Paths.Contains(renamed,StringComparer.OrdinalIgnoreCase));
            File.AppendAllText(renamed," updated");await Task.Delay(750);Check("file content is shared, no separate database copy",File.ReadAllText(entry.Path).EndsWith(" updated")&&Has(renamed));
            var shortcut=Desktop.CreateShortcut(source);await Wait(()=>Has(shortcut));var link=c.Store.Data.Entries.Single(e=>e.Path==shortcut);Check("shortcut resolves actual target",Shell.Resolve(shortcut).Equals(source,StringComparison.OrdinalIgnoreCase));
            c.DeleteEntries(new[]{link},false);await Wait(()=>!Has(shortcut));Check("drawer delete recycles shortcut only",!File.Exists(shortcut)&&File.Exists(source));
            Check("external move from desktop",ShellOps.Move(new[]{renamed},folder,0,true));await Wait(()=>!Has(renamed));Check("moving does not automatically import nested file",File.Exists(Path.Combine(folder,Path.GetFileName(renamed)))&&!c.Store.Data.Entries.Any(e=>e.Source=="desktop"&&e.Path==Path.Combine(folder,Path.GetFileName(renamed))));
            var moveSource=Path.Combine(outside,token+" cut.txt");File.WriteAllText(moveSource,"cut safe");Check("cut clipboard fixture",ShellOps.SetFileClipboard(new[]{moveSource},true));c.PasteToDesktop();var cut=Path.Combine(Desktop.UserFolder,Path.GetFileName(moveSource));await Wait(()=>Has(cut));Check("drawer cut paste moves real file",!File.Exists(moveSource)&&File.ReadAllText(cut)=="cut safe");File.Delete(cut);await Wait(()=>!Has(cut));Check("manual non-desktop entry survives mirror operations",c.Store.Data.Entries.Contains(manual)&&File.Exists(source));
            if(!d.IsVisible)c.ToggleDrawer();d.Prepare();d.Activate();await Task.Delay(250);Native.GetWindowRect(d.Handle,out var rect);Native.MovePointer(rect.Left+100,rect.Bottom-22);Check("background click reaches drawer",Native.WindowFromPoint(new Native.Point(rect.Left+100,rect.Bottom-22))==d.Handle);await Menu("native-desktop-background",()=>{Native.TestRightButton(true);Native.TestRightButton(false);});
            var virtualEntry=c.Store.Data.Entries.FirstOrDefault(e=>e.InDrawer&&e.IsVirtual);if(virtualEntry!=null)await Menu("native-special-shell-item",()=>c.ShowNativeMenu(new[]{virtualEntry}));
            var nativePaste=Path.Combine(outside,token+" native paste.txt");File.WriteAllText(nativePaste,"native background paste");ShellOps.SetFileClipboard(new[]{nativePaste},false);await Menu("native-background-paste",c.ShowDesktopBackground,"paste");var nativeDesktop=Path.Combine(Desktop.UserFolder,Path.GetFileName(nativePaste));await Wait(()=>Has(nativeDesktop));Check("background menu paste writes real desktop",File.ReadAllText(nativeDesktop)=="native background paste"&&File.Exists(nativePaste));
            var oldFolders=Directory.GetDirectories(Desktop.UserFolder).ToHashSet(StringComparer.OrdinalIgnoreCase);Thread? creator=null;bool submenu=false;
            ShellMenu.MenuDisplayed=()=>{var index=ShellMenu.LastLabels.FindIndex(label=>label.StartsWith("新建")||label.StartsWith("New",StringComparison.OrdinalIgnoreCase));var position=index>=0?ShellMenu.LastPositions[index]:-1;var menu=ShellMenu.ActivePopup;creator=new Thread(()=>{Thread.Sleep(450);if(position<0||!GetMenuItemRect(0,menu,(uint)position,out var item)){Key(27);return;}Native.MovePointer((item.Left+item.Right)/2,(item.Top+item.Bottom)/2);Thread.Sleep(500);int count=0;Native.EnumWindows((h,_)=>{if(Native.Class(h)=="#32768"&&Native.IsWindowVisible(h))count++;return true;},0);submenu=count>=2;Shot("native-new-submenu");Key(0x27);Thread.Sleep(120);Key(0x28);Key(0x24);Thread.Sleep(120);Key(13);Thread.Sleep(600);Key(27);}){IsBackground=true};creator.Start();};
            try{c.ShowDesktopBackground();creator?.Join(2500);}finally{ShellMenu.MenuDisplayed=null;}
            await Task.Delay(500);var addedFolders=Directory.GetDirectories(Desktop.UserFolder).Where(path=>!oldFolders.Contains(path)).ToArray();Check("native New submenu opens",submenu);Check("native New folder creates real desktop object",addedFolders.Length==1,addedFolders);
            var addedFolder=addedFolders.Single();Check("new fixture folder is empty and no parallel input",!Directory.EnumerateFileSystemEntries(addedFolder).Any()&&PointerHook.NonInjectedInputs==0);newFolders.Add(addedFolder);await Wait(()=>Has(addedFolder));
            if(!d.IsVisible)c.ToggleDrawer();await Task.Delay(350);
            var olePath=Path.Combine(outside,token+" ole.txt");File.WriteAllText(olePath,"real drawer background OLE");
            var oleData=new System.Windows.DataObject(DataFormats.FileDrop,new[]{olePath});bool dragging=false;var effect=DragDropEffects.None;
            var sourceWindow=new Window{Title="MacDock desktop mirror OLE fixture",Left=20,Top=90,Width=220,Height=110,Topmost=true,Content="临时文件拖放验收"};
            sourceWindow.PreviewMouseMove+=(_,a)=>{if(dragging||a.LeftButton!=System.Windows.Input.MouseButtonState.Pressed)return;dragging=true;effect=System.Windows.DragDrop.DoDragDrop(sourceWindow,oleData,DragDropEffects.Copy|DragDropEffects.Move);};
            c.Busy=true;sourceWindow.Show();await Task.Delay(200);Native.GetWindowRect(d.Handle,out var dropRect);var dropPoint=new Native.Point((dropRect.Left+dropRect.Right)/2,dropRect.Bottom-22);var sourcePoint=sourceWindow.PointToScreen(new Point(100,55));
            async Task Drop(bool cancel){await Task.Run(()=>{try{Native.MovePointer((int)sourcePoint.X,(int)sourcePoint.Y);Thread.Sleep(80);Native.TestButton(true);Native.MovePointer((int)sourcePoint.X+15,(int)sourcePoint.Y+10);Thread.Sleep(150);Native.TestKey(0x11,true);Native.MovePointer(dropPoint.X,dropPoint.Y);Thread.Sleep(500);if(cancel){Native.TestKey(27,true);Thread.Sleep(120);Native.TestKey(27,false);Thread.Sleep(80);}Native.TestButton(false);Thread.Sleep(200);}finally{Native.TestButton(false);Native.TestKey(0x11,false);}});}
            try{await Drop(false);var actualDrop=Path.Combine(Desktop.UserFolder,Path.GetFileName(olePath));Check("external OLE background drop copies to real desktop",dragging&&effect==DragDropEffects.Copy&&File.Exists(actualDrop)&&File.Exists(olePath),effect);c.SyncDesktopNow();Check("OLE result is a desktop mirror, never an alias",c.Store.Data.Entries.Single(e=>e.Path==actualDrop).Source=="desktop");
                var canceled=Path.Combine(outside,token+" canceled.txt");File.WriteAllText(canceled,"cancel safe");oleData=new System.Windows.DataObject(DataFormats.FileDrop,new[]{canceled});dragging=false;await Drop(true);Check("OLE Escape cancels without creating desktop object",dragging&&!File.Exists(Path.Combine(Desktop.UserFolder,Path.GetFileName(canceled)))&&File.Exists(canceled),new{dragging,effect,d.IsVisible,sourceExists=File.Exists(canceled),destinationExists=File.Exists(Path.Combine(Desktop.UserFolder,Path.GetFileName(canceled)))});
            }finally{sourceWindow.Close();if(d.IsVisible)d.Activate();c.Busy=false;}
            await Menu("native-folder-menu",()=>c.ShowNativeMenu(new[]{c.Store.Data.Entries.Single(e=>e.Path==folder)}));
            // Cancelled native menus do not create an error notification or close the drawer.
            c.MenuOpen=false;c.Busy=false;Native.GetWindowRect(d.Handle,out rect);var hit=Native.WindowFromPoint(new Native.Point(rect.Left,rect.Top));Check("rounded empty corner does not intercept desktop input",hit!=d.Handle&&Native.Class(hit)!="STATIC",new{handle=(long)hit,cls=Native.Class(hit)});
            var pin=c.Store.Data.Entries.Single(e=>e.Path==folder);c.SetPinned(new[]{pin},true);await Task.Delay(100);p=c.Dock.TileCenter(pin.Id);Native.MovePointer(p.X,p.Y);await Task.Delay(380);Check("wave remains active with native-menu drawer",c.Dock.MaxScale>1.08,c.Dock.MagnifyState);Native.MovePointer(rect.Left+80,rect.Top+30);await Task.Delay(350);Check("wave resets inside drawer",c.Dock.MaxScale<1.005);
            Check("no parallel physical input",PointerHook.NonInjectedInputs==0);
        }
        catch(Exception e){checks.Add(new{name="error",pass=false,detail=e.ToString()});}
        finally
        {
            ShellMenu.MenuDisplayed=null;PointerHook.AuditInput=false;c.MenuOpen=false;c.Busy=false;
            checks.Add(new{name="input audit total",pass=PointerHook.NonInjectedInputs==0,detail=PointerHook.NonInjectedInputs});
            // Only this test's unique direct desktop objects are cleaned up. User objects are never touched.
            foreach(var path in Directory.GetFiles(Desktop.UserFolder,token+"*"))try{File.Delete(path);}catch{}
            if(Directory.Exists(folder))Directory.Delete(folder,true);if(Directory.Exists(outside))Directory.Delete(outside,true);
            foreach(var path in newFolders)if(Directory.Exists(path)&&!Directory.EnumerateFileSystemEntries(path).Any())Directory.Delete(path);
            try{if(clipboard!=null)System.Windows.Clipboard.SetDataObject(clipboard,true);}catch{}
            if(PointerHook.NonInjectedInputs==0){Native.MovePointer(pointer.X,pointer.Y);Native.SetForegroundWindow(foreground);}
            File.WriteAllText(Path.Combine(output,"mirror-test.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));System.Windows.Application.Current.Shutdown();
        }
    }
}
