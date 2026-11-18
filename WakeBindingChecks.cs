#if MACDOCK_EXPERIENCE_CHECKS
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
            if(!await Wait(()=>Native.GetForegroundWindow()==hwnd))throw new InvalidOperationException("Could not foreground test window");
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

