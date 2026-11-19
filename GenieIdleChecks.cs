#if MACDOCK_EXPERIENCE_CHECKS
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;

namespace MacDock;

internal static class GenieIdleChecks
{
    const nuint InputTag=0x4D44434F;
    [DllImport("user32.dll")]
    static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    [DllImport("user32.dll")]
    static extern bool GetPhysicalCursorPos(out Native.Point point);
    [DllImport("user32.dll")]
    static extern bool GetClipCursor(out Native.Rect rect);
    [StructLayout(LayoutKind.Sequential)]
    struct LastInput {internal uint Size,Tick;}
    [DllImport("user32.dll")]
    static extern bool GetLastInputInfo(ref LastInput input);
    static uint InputStamp()
    {
        var input=new LastInput{Size=(uint)Marshal.SizeOf<LastInput>()};
        if(!GetLastInputInfo(ref input))throw new InvalidOperationException("Cannot read last-input timestamp");
        return input.Tick;
    }
    internal static async Task Run(Controller controller)
    {
        string directory=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")!;
        string sequence=Environment.GetEnvironmentVariable("MACDOCK_GENIE_SEQUENCE")??"hidden:60,hidden:120,shown:120,hidden:60";
        var cases=sequence.Split(',').Select(item=>
        {
            var parts=item.Split(':');
            return (shown:parts[0]=="shown",seconds:Math.Clamp(int.Parse(parts[1]),1,1800),feature:parts.Length>2?parts[2]:"full");
        }).ToArray();
        var clock=Stopwatch.StartNew();
        bool keyboardInput=Environment.GetEnvironmentVariable("MACDOCK_GENIE_INPUT")=="keyboard";
        bool externalForeground=Environment.GetEnvironmentVariable("MACDOCK_GENIE_FOREGROUND")!="same-process";
        nint previousForeground=Native.GetForegroundWindow();
        var output=new List<object>();
        var runInputs=new ConcurrentQueue<object>();
        TaskCompletionSource<string>? idleInterrupted=null;
        EventHandler displayChanged=(_,_)=>System.Threading.Volatile.Read(ref idleInterrupted)?.TrySetResult("display-change");
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=displayChanged;
        void TraceIdle(string name,object? data)
        {
            if(name=="pointer-raw"||name.StartsWith("icon-",StringComparison.Ordinal))runInputs.Enqueue(new{ms=clock.Elapsed.TotalMilliseconds,name,data});
            var waiting=System.Threading.Volatile.Read(ref idleInterrupted);if(waiting!=null&&name=="pointer-raw"&&data!=null){var raw=JsonSerializer.SerializeToElement(data);if(raw.TryGetProperty("extra",out var extra)&&(!extra.TryGetUInt64(out var marker)||marker!=(ulong)InputTag))waiting.TrySetResult("external-pointer-input");}
        }
        using var process=Process.GetCurrentProcess();
        if(Environment.GetEnvironmentVariable("MACDOCK_GENIE_INPUT")=="probe")
        {
            var input=new ConcurrentQueue<object>();
            GestureChecks.Trace=(name,data)=>input.Enqueue(new{name,data});
            Native.GetCursorPos(out var original);
            foreach(string method in new[]{"set","absolute","relative"})
            {
                if(method=="set")Native.SetCursorPos(90,100);
                if(method=="absolute")Native.MovePointer(120,130);
                if(method=="relative")mouse_event(1,1,1,0,0);
                await Task.Delay(100);
                Native.GetCursorPos(out var logical);GetPhysicalCursorPos(out var physical);GetClipCursor(out var clip);
                output.Add(new{method,logical=new{logical.X,logical.Y},physical=new{physical.X,physical.Y},clip=new{clip.Left,clip.Top,clip.Right,clip.Bottom},raw=input.ToArray()});
            }
            GestureChecks.Trace=null;Native.SetCursorPos(original.X,original.Y);
            File.WriteAllText(Path.Combine(directory,"genie-idle.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
            Application.Current.Shutdown();return;
        }
        var background=new Window {Width=380,Height=220,Title="MacDock cold-open measurement",ShowInTaskbar=false};
        void Save()=>File.WriteAllText(Path.Combine(directory,"genie-idle.json"),JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
        object Display()=>new
        {
            controller.Screen.DeviceName,
            bounds=Native.MonitorArea(controller.Screen,false).ToString(),
            work=controller.WorkArea.ToString(),
            scale=controller.Scale,
            tier=System.Windows.Media.RenderCapability.Tier>>16
        };
        async Task Until(Func<bool> condition)
        {
            var deadline=Stopwatch.StartNew();
            while(!condition()&&deadline.ElapsedMilliseconds<3500)await Task.Delay(12);
            if(!condition())throw new TimeoutException(controller.Diagnostics);
        }
        async Task MovePointer(int x,int y)
        {
            if(!Native.SetCursorPos(x-1,y))throw new InvalidOperationException("Could not position the test pointer");
            mouse_event(1,1,0,0,InputTag);
            await Task.Delay(25);
            Native.GetCursorPos(out var actual);
            if(Math.Abs(actual.X-x)>1||Math.Abs(actual.Y-y)>1)throw new InvalidOperationException($"Pointer position mismatch: requested {x},{y}, observed {actual.X},{actual.Y}");
        }
        Task ParkPointer()=>MovePointer(controller.WorkArea.Left+90,controller.WorkArea.Top+100);
        async Task WakeDock()
        {
            if(keyboardInput)return;
            if(!controller.Dock.IsVisible)
            {
                var wake=controller.WakeBounds;
                await MovePointer((wake.Left+wake.Right)/2,(wake.Top+wake.Bottom)/2);
                await Until(()=>controller.Dock.IsVisible);
            }
            controller.SettleDockMotion();
            await Until(()=>Native.WindowFromPoint(controller.Dock.GatewayCenter)==controller.Dock.Handle);
        }
        async Task ClickDrawer()
        {
            if(keyboardInput)
            {
                Native.TestKey(0x11,true);Native.TestKey(0x12,true);Native.TestKey(0x20,true);
                await Task.Delay(25);
                Native.TestKey(0x20,false);Native.TestKey(0x12,false);Native.TestKey(0x11,false);
                return;
            }
            var point=controller.Dock.GatewayCenter;
            await MovePointer(point.X,point.Y);
            await Task.Delay(40);
            if(Native.WindowFromPoint(point)!=controller.Dock.Handle)throw new InvalidOperationException("Gateway test point is not owned by the visible Dock");
            mouse_event(2,0,0,0,InputTag);
            await Task.Delay(25);
            mouse_event(4,0,0,0,InputTag);
        }
        double coldMaximum=0;
        async Task MeasureOpen(int idle,bool shown,int round,int attempt)
        {
            double origin=clock.Elapsed.TotalMilliseconds;
            var utc=DateTimeOffset.UtcNow;
            long qpcOrigin=Stopwatch.GetTimestamp();
            var stages=new List<object>();
            var frames=new List<object>();
            var times=new List<double>();
            var inputs=new ConcurrentQueue<object>();
            bool openComplete=false;
            controller.DrawerTiming=name=>
            {
                stages.Add(new{name,ms=clock.Elapsed.TotalMilliseconds-origin});
                if(name=="open-complete")openComplete=true;
            };
            controller.DrawerAnimationFrame=(phase,_,_,_)=>
            {
                double ms=clock.Elapsed.TotalMilliseconds-origin;
                times.Add(ms);frames.Add(new{ms,phase});
            };
            GestureChecks.Trace=(name,data)=>{TraceIdle(name,data);inputs.Enqueue(new{name,ms=clock.Elapsed.TotalMilliseconds-origin,data});};
            long allocated=GC.GetTotalAllocatedBytes();
            int[] collections=Enumerable.Range(0,3).Select(GC.CollectionCount).ToArray();
            try
            {
                await ClickDrawer();
                await Until(()=>openComplete);
            }
            finally
            {
                GestureChecks.Trace=TraceIdle;controller.DrawerTiming=null;controller.DrawerAnimationFrame=null;
            }
            var intervals=times.Zip(times.Skip(1),(a,b)=>b-a).Order().ToArray();
            if(idle>0&&attempt==1)coldMaximum=intervals.LastOrDefault();
            output.Add(new
            {
                kind="open",idle,shown,round,attempt,utc,origin,qpcOrigin,qpcFrequency=Stopwatch.Frequency,display=Display(),stages,frames,inputs,
                frameCount=times.Count,firstFrame=times.FirstOrDefault(),max=intervals.LastOrDefault(),
                p95=intervals.Length==0?0:intervals[(int)Math.Ceiling(intervals.Length*.95)-1],
                over25=intervals.Count(x=>x>25),over30=intervals.Count(x=>x>30),
                allocated=GC.GetTotalAllocatedBytes()-allocated,
                collections=collections.Select((n,i)=>GC.CollectionCount(i)-n).ToArray()
            });
            Save();
            await Task.Delay(100);
            controller.CloseDrawer(keepDock:true,force:true);
            await Until(()=>controller.Drawer?.IsVisible!=true);
            if(externalForeground)Native.SetForegroundWindow(previousForeground);
        }
        try
        {
            if((Native.GetAsyncKeyState(1)&0x8000)!=0||(Native.GetAsyncKeyState(2)&0x8000)!=0)throw new InvalidOperationException("A pointer button is already held before this test starts");
            GestureChecks.Trace=TraceIdle;
            if(!externalForeground){background.Show();background.Activate();}
            await ParkPointer();await Task.Delay(250);
            output.Add(new{kind="environment",utc=DateTimeOffset.UtcNow,display=Display(),sequence,input=keyboardInput?"Windows hotkey diagnostic":"Windows pointer input",externalForeground,
                runtime=RuntimeInformation.FrameworkDescription,wpfAssembly=typeof(Window).Assembly.Location,
                nativeRenderer=process.Modules.Cast<ProcessModule>().Where(m=>m.ModuleName.Equals("wpfgfx_cor3.dll",StringComparison.OrdinalIgnoreCase)).Select(m=>new{m.FileName,version=m.FileVersionInfo.FileVersion}).ToArray()});Save();
            await WakeDock();
            await MeasureOpen(0,false,-1,1);
            for(int round=0;round<cases.Length;round++)
            {
                var test=cases[round];
                Icons.PreserveVisuals=true;
                controller.Drawer!.DiagnosticRendering=test.feature;
                HostGlass.DiagnosticHideDrawer=test.feature=="glass";
                HostGlass.DiagnosticSkipRegion=test.feature=="region";
                output.Add(new{kind="diagnostic-mode",round,feature=test.feature});Save();
                if(test.shown)
                {
                    await WakeDock();Native.GetWindowRect(controller.Dock.Handle,out var dock);
                    await MovePointer((dock.Left+dock.Right)/2,dock.Bottom-(int)Math.Ceiling(6*controller.Scale));
                    await Task.Delay(300);
                    if(!controller.Dock.IsVisible)throw new InvalidOperationException("Visible-Dock idle condition did not settle");
                }
                else
                {
                    await ParkPointer();await Until(()=>!controller.Dock.IsVisible);await Task.Delay(250);
                }
                process.Refresh();double cpu=process.TotalProcessorTime.TotalMilliseconds;
                var foreground=Native.GetForegroundWindow();Native.GetWindowThreadProcessId(foreground,out uint foregroundProcess);
                uint inputBefore=InputStamp();string displayBefore=JsonSerializer.Serialize(Display());
                double start=clock.Elapsed.TotalMilliseconds;long workingSet=process.WorkingSet64;
                File.WriteAllText(Path.Combine(directory,"idle-start.json"),JsonSerializer.Serialize(new{round,idle=test.seconds,shown=test.shown,utc=DateTimeOffset.UtcNow}));
                var interrupted=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var idleCancel=new System.Threading.CancellationTokenSource();
                System.Threading.Volatile.Write(ref idleInterrupted,interrupted);
                var idleDelay=Task.Delay(TimeSpan.FromSeconds(test.seconds),idleCancel.Token);
                var ended=await Task.WhenAny(idleDelay,interrupted.Task);
                System.Threading.Volatile.Write(ref idleInterrupted,null);
                string? interruptReason=ended==interrupted.Task?await interrupted.Task:null;if(interruptReason!=null)idleCancel.Cancel();
                process.Refresh();
                uint inputAfter=InputStamp();bool displayStable=displayBefore==JsonSerializer.Serialize(Display());
                output.Add(new
                {
                    kind="idle",round,requestedSeconds=test.seconds,shown=test.shown,dockVisible=controller.Dock.IsVisible,
                    actualSeconds=(clock.Elapsed.TotalMilliseconds-start)/1000,
                    cpuMilliseconds=process.TotalProcessorTime.TotalMilliseconds-cpu,
                    residentStart=workingSet,residentEnd=process.WorkingSet64,display=Display(),
                    foregroundProcess,foregroundClass=Native.Class(foreground),ownProcess=Environment.ProcessId,
                    inputBefore,inputAfter,inputUnchanged=inputBefore==inputAfter,displayStable,interruptReason
                });Save();
                if(interruptReason!=null||inputBefore!=inputAfter||!displayStable)throw new InvalidOperationException("Idle sample invalidated by input or display changes");
                if(controller.Dock.IsVisible!=test.shown)throw new InvalidOperationException("Dock idle state changed during measurement");
                await WakeDock();
                for(int attempt=1;attempt<=3;attempt++)await MeasureOpen(test.seconds,test.shown,round,attempt);
                if(test.feature=="full"&&double.TryParse(Environment.GetEnvironmentVariable("MACDOCK_GENIE_STOP_OVER_MS"),out var limit)&&coldMaximum>limit){output.Add(new{kind="performance-screen-failed",round,coldMaximum,limit});Save();break;}
            }
        }
        catch(Exception error){Environment.ExitCode=1;output.Add(new{kind="error",detail=error.ToString()});Save();}
        finally
        {
            System.Threading.Volatile.Write(ref idleInterrupted,null);Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=displayChanged;
            mouse_event(4,0,0,0,InputTag);GestureChecks.Trace=null;controller.DrawerTiming=null;controller.DrawerAnimationFrame=null;
            background.Close();Application.Current.Shutdown(Environment.ExitCode);
            File.WriteAllText(Path.Combine(directory,"raw-input-events.json"),JsonSerializer.Serialize(runInputs,new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
#endif
