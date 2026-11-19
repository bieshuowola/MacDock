#if MACDOCK_EXPERIENCE_CHECKS
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;

namespace MacDock;

internal static class WakeBindingChecks
{
    const byte Binding=0x77;
    const int WmInput=0x00FF;
    [StructLayout(LayoutKind.Sequential)]
    struct RawDevice { public ushort Page,Usage; public uint Flags; public nint Target; }
    [StructLayout(LayoutKind.Sequential)]
    struct RawHeader { public uint Type,Size; public nint Device,Parameter; }
    [StructLayout(LayoutKind.Sequential)]
    struct RawKeyboard
    {
        public RawHeader Header;
        public ushort Scan,Flags,Reserved,Key;
        public uint Message,Extra;
    }
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(ref RawDevice device,uint count,uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(nint input,uint command,out RawKeyboard data,ref uint size,uint headerSize);
    [DllImport("user32.dll")] static extern nint GetShellWindow();

    internal static int InputFixture(string folder,string mode)
    {
        bool fullscreen=mode=="full";
        var app=new Application();
        var editor=new TextBox{FontSize=22,Margin=new Thickness(20),VerticalAlignment=VerticalAlignment.Top};
        var window=new Window
        {
            Title=fullscreen?"MacDock borderless raw keyboard fixture":"MacDock wake text receiver",
            Left=140,Top=160,Width=460,Height=220,
            WindowStyle=fullscreen?WindowStyle.None:WindowStyle.SingleBorderWindow,Content=editor
        };
        var keys=Path.Combine(folder,"receiver-keys.txt");
        var rawKeys=Path.Combine(folder,"receiver-raw.txt");
        var input=Path.Combine(folder,"receiver-text.txt");
        File.WriteAllText(input,"");
        editor.TextChanged+=(_,_)=>File.WriteAllText(input,editor.Text);
        if(mode=="ime")
        {
            InputLanguageManager.SetInputLanguage(editor,CultureInfo.GetCultureInfo("zh-CN"));
            InputMethod.SetPreferredImeState(editor,InputMethodState.On);
        }
        else InputMethod.SetIsInputMethodEnabled(editor,false);
        TextCompositionManager.AddPreviewTextInputStartHandler(editor,(_,_)=>File.WriteAllText(Path.Combine(folder,"composition-start.txt"),"started"));
        window.SourceInitialized+=(_,_)=>{
            var hwnd=new WindowInteropHelper(window).Handle;
            if(fullscreen)
            {
                var raw=new RawDevice{Page=1,Usage=6,Target=hwnd};
                File.WriteAllText(Path.Combine(folder,"raw-registration.txt"),
                    RegisterRawInputDevices(ref raw,1,(uint)Marshal.SizeOf<RawDevice>()).ToString());
            }
            HwndSource.FromHwnd(hwnd).AddHook((nint h,int msg,nint wp,nint lp,ref bool handled)=>{
                if(msg is 0x100 or 0x101 or 0x104 or 0x105)
                    File.AppendAllText(keys,$"{msg:X}:{(int)wp:X}\n");
                if(msg==WmInput)
                {
                    uint size=(uint)Marshal.SizeOf<RawKeyboard>();
                    if(GetRawInputData(lp,0x10000003,out var data,ref size,(uint)Marshal.SizeOf<RawHeader>())!=uint.MaxValue&&data.Header.Type==1)
                        File.AppendAllText(rawKeys,$"{data.Message:X}:{data.Key:X}\n");
                }
                return 0;
            });
        };
        window.Loaded+=(_,_)=>{
            var hwnd=new WindowInteropHelper(window).Handle;
            if(fullscreen)
            {
                var monitor=Native.MonitorArea(System.Windows.Forms.Screen.PrimaryScreen!,false);
                Native.SetWindowPos(hwnd,0,monitor.Left,monitor.Top,monitor.Width,monitor.Height,0x40);
            }
            editor.Focus();
            File.WriteAllText(Path.Combine(folder,"ready.txt"),hwnd.ToInt64().ToString());
        };
        return app.Run(window);
    }

    static nint FindWindow(string title)
    {
        nint found=0;var name=new StringBuilder(320);
        Native.EnumWindows((hwnd,_)=>{
            if(!Native.IsWindowVisible(hwnd))return true;
            Native.GetWindowText(hwnd,name,name.Capacity);
            if(!name.ToString().StartsWith(title,StringComparison.Ordinal))return true;
            found=hwnd;return false;
        },0);
        return found;
    }

    internal static async Task Run(Controller c)
    {
        string output=Path.GetDirectoryName(Path.GetDirectoryName(c.Store.FilePath))!;
        var results=new List<object>();
        var childProcesses=new List<Process>();
        var testWindows=new List<nint>();
        var previousForeground=Native.GetForegroundWindow();
        bool targetHeld=false,ctrlHeld=false;
        void Check(string name,bool pass)
        {
            results.Add(new{name,pass});
            if(!pass)throw new InvalidOperationException(name);
        }
        void Boundary(string name,string detail)=>results.Add(new{name,detail});
        async Task<bool> Wait(Func<bool> predicate,int milliseconds=3500)
        {
            var until=Environment.TickCount64+milliseconds;
            do{if(predicate())return true;await Task.Delay(45);}while(Environment.TickCount64<until);
            return false;
        }
        string Read(string file)
        {
            try{return File.ReadAllText(file);}catch(IOException){return "";}
        }
        void Key(byte key,bool down,byte scan=0)
        {
            Native.TestKey(key,down,scan:scan);
            if(key==Binding)targetHeld=down;
            if(key==0x11)ctrlHeld=down;
        }
        void Tap(byte key,byte scan=0){Key(key,true,scan);Key(key,false,scan);}
        void Launch(string exe,IEnumerable<string> args)
        {
            var start=new ProcessStartInfo(exe){UseShellExecute=true};
            foreach(string arg in args)start.ArgumentList.Add(arg);
            if(Process.Start(start) is {} process)childProcesses.Add(process);
        }
        async Task Focus(nint hwnd)
        {
            Native.ActivateWindow(hwnd);
            if(await Wait(()=>Native.GetForegroundWindow()==hwnd,500))return;
            if(Native.GetWindowRect(hwnd,out var bounds))
            {
                var x=(bounds.Left+bounds.Right)/2;
                var y=(bounds.Top+bounds.Bottom)/2;
                const int noMoveOrActivate=0x0013;
                Native.SetWindowPos(hwnd,-1,0,0,0,0,noMoveOrActivate);
                try
                {
                    Native.MovePointer(x,y);
                    var hit=Native.WindowFromPoint(new Native.Point(x,y));
                    if(hit==hwnd||Native.GetAncestor(hit,2)==hwnd)
                    {
                        Native.TestButton(true);
                        Native.TestButton(false);
                    }
                    if(await Wait(()=>Native.GetForegroundWindow()==hwnd,900))return;
                }
                finally{Native.SetWindowPos(hwnd,-2,0,0,0,0,noMoveOrActivate);}
            }
            throw new InvalidOperationException($"Could not foreground test window. Foreground class: {Native.Class(Native.GetForegroundWindow())}");
        }
        async Task<(string Folder,nint Hwnd)> Receiver(string mode)
        {
            string folder=Path.Combine(output,"receiver-"+mode);
            Directory.CreateDirectory(folder);
            Launch(Environment.ProcessPath!,new[]{"--wake-input-fixture",folder,mode});
            string ready=Path.Combine(folder,"ready.txt");
            if(!await Wait(()=>long.TryParse(Read(ready),out _),12000))throw new TimeoutException("Input fixture did not start");
            nint hwnd=(nint)long.Parse(Read(ready));
            testWindows.Add(hwnd);
            await Focus(hwnd);
            return (folder,hwnd);
        }

        int exit=0;
        try
        {
            Check("fresh configuration starts unbound, keyboard hook absent",c.SuppressWakeKey==0&&!c.WakeKeyListening&&!c.WakeSuppressedByKey);
            Check("Pause and Break cannot be set",!c.SetSuppressWakeKey(0x13)&&!c.SetSuppressWakeKey(0x03)&&c.SuppressWakeKey==0);
            var shell=GetShellWindow();
            var receiver=await Receiver("plain");
            if(shell!=0)Native.SetForegroundWindow(shell);
            if(shell==0||!await Wait(()=>Native.GetForegroundWindow()==shell,500))
            {
                Boundary("Explorer foreground","Explorer declined focus in this run. The two taps ran in an independent ordinary window.");
                await Focus(receiver.Hwnd);
            }
            Check("explicit single key registers the keyboard listener",c.SetSuppressWakeKey(Binding)&&await Wait(()=>c.WakeKeyListening));
            Tap(Binding,0x42);
            Check("desktop first press disables and persists",await Wait(()=>c.WakeSuppressedByKey)&&new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.WakeDisabled);
            Tap(Binding,0x42);
            Check("desktop second press reenables wake",await Wait(()=>!c.WakeSuppressedByKey));

            await Focus(receiver.Hwnd);
            string nativeLog=Path.Combine(receiver.Folder,"receiver-keys.txt");
            int initialPresses=Read(nativeLog).Split('\n').Count(x=>x=="100:77");
            Key(0x11,true,0x1D);Tap(Binding,0x42);Key(0x11,false,0x1D);
            await Task.Delay(170);
            Check("Ctrl chord reaches receiver without toggling Dock",
                Read(nativeLog).Contains("100:11")&&Read(nativeLog).Contains("100:77")&&Read(nativeLog).Contains("101:77")&&!c.WakeSuppressedByKey);
            Key(Binding,true,0x42);
            Check("single press heard in other GUI process",await Wait(()=>c.WakeSuppressedByKey));
            c.TestReinstallHook();
            Check("keyboard listener reinstalls while bound",await Wait(()=>c.WakeKeyListening));
            for(int i=0;i<5;i++){Key(Binding,true,0x42);await Task.Delay(65);}
            Key(Binding,false,0x42);
            bool repeatedEvents=await Wait(()=>Read(nativeLog).Split('\n').Count(x=>x=="100:77")>=initialPresses+7&&
                !Native.KeyHeld(Binding));
            Check("reinstall during hold and autorepeat toggle only once",c.WakeSuppressedByKey&&repeatedEvents);
            Tap(Binding,0x42);
            Check("next down works after release and reinstall",await Wait(()=>!c.WakeSuppressedByKey));
            var fourKeys=WakeShortcut.Create(new[]{0x11,0x12,0x10,(int)Binding})!;
            Check("four-key shortcut is configured and loaded",c.SetWakeShortcut(fourKeys)&&
                new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.WakeShortcutKeys?.SequenceEqual(fourKeys.ToArray())==true);
            try
            {
                Key(0x11,true,0x1D);Key(0x12,true,0x38);Key(Binding,true,0x42);
                Key(0x41,true,0x1E);Key(0x10,true,0x2A);
                await Task.Delay(120);
                Check("a fifth held key prevents switching",!c.WakeSuppressedByKey);
                Key(0x41,false,0x1E);Key(0x10,false,0x2A);
                Key(0x10,true,0x2A);
                Check("last modifier completes a four-key chord in an independent window",await Wait(()=>c.WakeSuppressedByKey));
                Key(Binding,true,0x42);Key(0x10,true,0x2A);
                await Task.Delay(120);
                Check("four-key repeats do not switch a second time",c.WakeSuppressedByKey);
                Key(Binding,false,0x42);
                Key(Binding,true,0x42);
                Check("repress after release switches while modifiers stay held",await Wait(()=>!c.WakeSuppressedByKey));
            }
            finally
            {
                Key(0x41,false,0x1E);Key(Binding,false,0x42);Key(0x10,false,0x2A);
                Key(0x12,false,0x38);Key(0x11,false,0x1D);
            }
            var ime=await Receiver("ime");
            nativeLog=Path.Combine(ime.Folder,"receiver-keys.txt");
            string typed=Path.Combine(ime.Folder,"receiver-text.txt");
            Check("legacy grave can be retained as an explicit binding",c.SetSuppressWakeKey(0xC0));
            string beforeGrave=Read(typed);
            Tap(0xC0,0x29);
            Check("the former intercepted key is delivered or composed by the foreground IME",
                await Wait(()=>Read(nativeLog).Contains("101:C0")&&
                    (Read(nativeLog).Contains("100:C0")||Read(typed)!=beforeGrave)));
            c.SetSuppressWakeKey(0);c.SetSuppressWakeKey(Binding);
            await Wait(()=>c.WakeKeyListening);
            string beforeTyping=Read(typed);
            foreach(char key in "NIHAO"){Tap((byte)key);await Task.Delay(55);}
            Tap(0x20);
            bool composition=await Wait(()=>Read(typed).Any(ch=>ch>='\u3400'&&ch<='\u9fff'),900);
            Check("native editor receives ordinary input while bound",Read(typed)!=beforeTyping&&Read(nativeLog).Contains("101:4E"));
            if(composition)Check("active Chinese IME composes and commits text",true);
            else Boundary("Chinese IME composition","No Chinese composition was committed by this Windows input session.");
            c.SetSuppressWakeKey(0x79);
            Tap(0x79,0x44);
            Check("standalone F10 system-key notification is supported",
                await Wait(()=>c.WakeSuppressedByKey&&Read(nativeLog).Contains(":79")));
            c.SetSuppressWakeKey(0);
            c.SetSuppressWakeKey(Binding);
            await Wait(()=>c.WakeKeyListening);

            string browserPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Microsoft\Edge\Application\msedge.exe");
            if(File.Exists(browserPath))
            {
                string token="MacDock Wake "+Guid.NewGuid().ToString("N")[..8];
                string page=Path.Combine(output,"browser.html");
                File.WriteAllText(page,$$"""
                    <!doctype html><title>{{token}}</title><input id="input" autofocus aria-label="Wake keyboard browser field">
                    <script>
                    let down=0,up=0;
                    const input=document.getElementById('input');
                    function show(){ document.title='{{token}} '+down+' '+up+' '+input.value; }
                    document.addEventListener('keydown', e=>{if(e.key==='F8')++down;show();});
                    document.addEventListener('keyup', e=>{if(e.key==='F8')++up;show();});
                    input.addEventListener('input', show);
                    window.addEventListener('focus', ()=>input.focus());
                    input.focus();
                    show();
                    </script>
                    """);
                Launch(browserPath,new[]{"--user-data-dir="+Path.Combine(output,"edge-profile"),"--no-first-run","--disable-background-networking",
                    "--app="+new Uri(page).AbsoluteUri});
                if(await Wait(()=>FindWindow(token+" 0 0")!=0,15000))
                {
                    var hwnd=FindWindow(token);testWindows.Add(hwnd);await Focus(hwnd);
                    await Task.Delay(220);
                    Key(Binding,true,0x42);await Task.Delay(80);Key(Binding,false,0x42);
                    await Task.Delay(80);
                    Key(0x37,true,0x08);await Task.Delay(60);Key(0x37,false,0x08);
                    Check("Edge receives the target down/up and ordinary typing",
                        await Wait(()=>FindWindow(token+" 1 1 7")!=0));
                    Native.PostMessage(hwnd,0x10,0,0);
                }
                else Boundary("Edge","Isolated browser window did not open.");
            }
            else Boundary("Edge","Edge executable unavailable.");

            string consoleToken="MacDock Wake Terminal "+Guid.NewGuid().ToString("N")[..8];
            string consoleResult=Path.Combine(output,"terminal-text.txt");
            string command=$"$host.UI.RawUI.WindowTitle='{consoleToken}'; [IO.File]::WriteAllText('{consoleResult.Replace("'","''")}',(Read-Host 'MacDock input'))";
            Launch(Path.Combine(Environment.SystemDirectory,"conhost.exe"),new[]{
                Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe"),"-NoLogo","-NoProfile","-Command",command});
            if(await Wait(()=>FindWindow(consoleToken)!=0,9000))
            {
                nint hwnd=FindWindow(consoleToken);testWindows.Add(hwnd);await Focus(hwnd);
                Tap(Binding,0x42);Tap(0x31,0x02);Tap(0x33,0x04);Tap(0x37,0x08);Tap(0x0D,0x1C);
                Check("PowerShell terminal input remains usable",await Wait(()=>Read(consoleResult)=="137"));
            }
            else Boundary("PowerShell terminal","Isolated console window did not open.");

            c.SetSuppressWakeKey(0);
            Check("clearing binding removes keyboard hook and restores wake",await Wait(()=>!c.WakeKeyListening)&&!c.WakeSuppressedByKey);

            // Real Ctrl+Alt+Space unbound check, settings recording, hold/repeat isolation, wake strip decoupling, and focus switch
            await Focus(receiver.Hwnd);
            string plainKeysLog=Path.Combine(receiver.Folder,"receiver-keys.txt");
            int spaceCountBefore=Read(plainKeysLog).Split('\n').Count(x=>x is "100:20" or "104:20");
            Key(0x11,true,0x1D);Key(0x12,true,0x38);Tap(0x20,0x39);Key(0x12,false,0x38);Key(0x11,false,0x1D);
            await Task.Delay(220);
            Check("unbound Ctrl+Alt+Space does not open drawer and reaches foreground",
                c.Drawer?.IsVisible!=true&&Read(plainKeysLog).Split('\n').Count(x=>x is "100:20" or "104:20")>spaceCountBefore);

            Window? settingsWindow=null;
            var settingsReady=new TaskCompletionSource<Window>();
            _ = c.ShowSettings(w=>settingsReady.TrySetResult(w));
            settingsWindow=await settingsReady.Task;
            await Task.Delay(200);
            static IEnumerable<DependencyObject> WalkVisual(DependencyObject node)
            {
                yield return node;
                foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
                    foreach(var nested in WalkVisual(child))yield return nested;
            }
            var bindBtn=WalkVisual(settingsWindow).OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="临时关闭 MacDock");
            await Focus(new WindowInteropHelper(settingsWindow).Handle);
            bindBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,bindBtn));
            Check("settings window enters wake recording mode",await Wait(()=>c.WakeRecording));
            Key(0x11,true,0x1D);await Task.Delay(35);
            Key(0x12,true,0x38);await Task.Delay(35);
            Key(0x20,true,0x39);await Task.Delay(45);
            Check("recording Ctrl+Alt+Space never opens drawer",c.Drawer?.IsVisible!=true);
            Key(0x20,false,0x39);await Task.Delay(35);
            Key(0x12,false,0x38);await Task.Delay(35);
            Key(0x11,false,0x1D);
            Check("real Ctrl+Alt+Space binds cleanly in settings",
                await Wait(()=>!c.WakeRecording&&(string?)bindBtn.Content=="Ctrl + Alt + Space")&&
                c.Drawer?.IsVisible!=true&&!c.WakeSuppressedByKey&&
                new Store(Path.GetDirectoryName(c.Store.FilePath)).Data.WakeShortcutKeys?.SequenceEqual(new[]{0x11,0x12,0x20})==true);
            settingsWindow.Close();
            await Wait(()=>!settingsWindow.IsVisible);

            await Focus(receiver.Hwnd);
            var safeOut=new Native.Point(c.WorkArea.Left+100,c.WorkArea.Top+150);
            Native.MovePointer(safeOut.X,safeOut.Y);
            await Wait(()=>!c.Dock.IsVisible&&Native.IsWindowVisible(c.IndicatorHandle)&&c.IndicatorAlpha==1);
            Key(0x11,true,0x1D);Key(0x12,true,0x38);
            for(int r=0;r<4;r++){Key(0x20,true,0x39);await Task.Delay(60);}
            Key(0x20,false,0x39);Key(0x12,false,0x38);Key(0x11,false,0x1D);
            Check("real Ctrl+Alt+Space hold toggles temporary disable once and keeps strip visible",
                await Wait(()=>c.WakeSuppressedByKey)&&Native.IsWindowVisible(c.IndicatorHandle)&&c.IndicatorAlpha==1);

            var senseMid=new Native.Point((c.WakeBounds.Left+c.WakeBounds.Right)/2,(c.WakeBounds.Top+c.WakeBounds.Bottom)/2);
            Native.MovePointer(senseMid.X,senseMid.Y);
            await Task.Delay(380);
            Check("hover wake stays disabled during temporary disable while strip stays visible",
                !c.Dock.IsVisible&&Native.IsWindowVisible(c.IndicatorHandle)&&c.IndicatorAlpha==1);

            Native.GetWindowRect(c.IndicatorHandle,out var stripRect);
            Native.MovePointer((stripRect.Left+stripRect.Right)/2,(stripRect.Top+stripRect.Bottom)/2);
            Native.TestButton(true);await Task.Delay(35);Native.TestButton(false);
            Check("clicking bottom strip wakes Dock during temporary disable",await Wait(()=>c.Dock.IsVisible));
            await Task.Delay(380);
            Check("Dock woken by strip click stays open while pointer remains on Dock",c.Dock.IsVisible);
            Native.MovePointer(safeOut.X,safeOut.Y);
            Check("leaving Dock collapses back to visible strip during temporary disable",
                await Wait(()=>!c.Dock.IsVisible&&Native.IsWindowVisible(c.IndicatorHandle)&&c.IndicatorAlpha==1));

            await Focus(ime.Hwnd);
            bool stripBlinked=false;
            for(int i=0;i<6;i++)
            {
                if(i==1){Key(0x11,true,0x1D);Key(0x12,true,0x38);Tap(0x20,0x39);Key(0x12,false,0x38);Key(0x11,false,0x1D);}
                if(!Native.IsWindowVisible(c.IndicatorHandle)||c.IndicatorAlpha<.95)stripBlinked=true;
                await Task.Delay(45);
            }
            Check("pressing Ctrl+Alt+Space after focus switch restores auto-wake without strip flicker",
                await Wait(()=>!c.WakeSuppressedByKey)&&!stripBlinked&&c.IndicatorAlpha==1);
            Native.MovePointer(senseMid.X,senseMid.Y);
            Check("hover auto-wake works again after restore",await Wait(()=>c.Dock.IsVisible));
            Native.MovePointer(safeOut.X,safeOut.Y);
            await Wait(()=>!c.Dock.IsVisible);
            c.SetWakeShortcut(null);
            await Wait(()=>!c.WakeKeyListening);

            var game=await Receiver("full");
            Check("fullscreen surrogate owns the foreground",Native.Fullscreen(c.Screen));
            c.SetSuppressWakeKey(Binding);
            Check("bound observer active alongside foreground Raw Input",await Wait(()=>c.WakeKeyListening)&&
                Read(Path.Combine(game.Folder,"raw-registration.txt"))=="True");
            Tap(0x57,0x11);Tap(Binding,0x42);Tap(0x41,0x1E);
            string gameLog=Path.Combine(game.Folder,"receiver-keys.txt");
            Check("borderless fullscreen retains WASD and bound key down/up",
                await Wait(()=>Read(gameLog).Contains("101:57")&&Read(gameLog).Contains("101:77")&&Read(gameLog).Contains("101:41"))&&
                Native.GetForegroundWindow()==game.Hwnd&&!c.Dock.IsVisible);
            string rawLog=Path.Combine(game.Folder,"receiver-raw.txt");
            if(await Wait(()=>Read(rawLog).Contains("101:77")&&Read(rawLog).Contains("101:57"),1200))
                Check("separate fullscreen Raw Input receiver sees the same synthetic events",true);
            else Boundary("Raw Input delivery","Registration succeeded. The synthetic keyboard events did not produce observable raw packets.");
            int presses=Read(gameLog).Split('\n').Count(x=>x=="100:77");
            int releases=Read(gameLog).Split('\n').Count(x=>x=="101:77");
            Key(Binding,true,0x42);
            Check("foreground receives held binding before clear",
                await Wait(()=>Read(gameLog).Split('\n').Count(x=>x=="100:77")>presses));
            c.SetSuppressWakeKey(0);
            Check("clear while held unregisters keyboard listener",await Wait(()=>!c.WakeKeyListening));
            Key(Binding,false,0x42);
            Check("release reaches the original foreground after clearing",
                await Wait(()=>Read(gameLog).Split('\n').Count(x=>x=="101:77")>releases||
                    Read(rawLog).Split('\n').Count(x=>x=="101:77")>releases)&&!c.WakeSuppressedByKey);
            await Focus(game.Hwnd);
            Key(Binding,true,0x42);
            bool nextDown=await Wait(()=>Read(gameLog).Split('\n').Count(x=>x=="100:77")>=presses+2);
            Key(Binding,false,0x42);
            Check("another press and release pass through with no listener",nextDown&&
                await Wait(()=>!Native.KeyHeld(Binding)&&(Read(gameLog).Split('\n').Count(x=>x=="101:77")>=releases+2||
                    Read(rawLog).Split('\n').Count(x=>x=="101:77")>=releases+2))&&!c.WakeSuppressedByKey);
            var twoKeys=WakeShortcut.Create(new[]{0x11,(int)Binding})!;
            c.SetWakeShortcut(twoKeys);await Wait(()=>c.WakeKeyListening);
            int rawReleases=Read(rawLog).Split('\n').Count(x=>x=="101:77");
            try
            {
                Key(0x11,true,0x1D);Tap(Binding,0x42);
                Check("Ctrl shortcut switches in borderless fullscreen",await Wait(()=>c.WakeSuppressedByKey)&&
                    Native.GetForegroundWindow()==game.Hwnd);
                if(await Wait(()=>Read(rawLog).Split('\n').Count(x=>x=="101:77")>rawReleases,1200))
                    Check("bound chord reaches the fullscreen Raw Input receiver",true);
                else Boundary("Raw Input chord","The test environment did not report another injected raw release.");
            }
            finally{Key(0x11,false,0x1D);c.SetWakeShortcut(null);}
        }
        catch(Exception error){results.Add(new{name="failure",pass=false,detail=error.ToString()});exit=1;}
        finally
        {
            if(targetHeld)Key(Binding,false,0x42);
            if(ctrlHeld)Key(0x11,false,0x1D);
            foreach(var hwnd in testWindows)Native.PostMessage(hwnd,0x10,0,0);
            foreach(var process in childProcesses)process.Dispose();
            if(previousForeground!=0)Native.ActivateWindow(previousForeground);
            File.WriteAllText(Path.Combine(output,"wake-binding.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown(exit);
        }
    }
}
#endif

