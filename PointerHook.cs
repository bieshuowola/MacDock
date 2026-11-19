using System.Runtime.InteropServices;
using System.Threading;

namespace MacDock;

// A low-level mouse hook is silently removed by Windows when its callback is delayed past
// LowLevelHooksTimeout (~300 ms). Installing it on the WPF UI thread means any layout pass,
// icon extraction or config save can kill wake-on-hover for the rest of the session.
// This runs the hook on its own message-pumping thread so the callback never waits on the UI.
internal sealed class PointerHook : IDisposable
{
    readonly Native.MouseProc proc;
    readonly Native.MouseProc keyProc;
    readonly Action<Native.Point,bool> handler;
    readonly Func<Native.Point,uint,uint,bool>? filter;
    Thread? thread;
    uint threadId;
    nint hook;
    nint keyHook;
    readonly Action<WakeShortcut,nint>? wakePressed;
    static readonly Func<int,bool> KeyHeld=Native.KeyHeld;
    volatile WakeShortcut? wakeShortcut;
    volatile bool wakeSuspended;
    const uint WakeBindingChanged=0x8001;
    internal static int Installations;
    internal static bool AuditInput;
    internal static long NonInjectedInputs;
    volatile bool disposed;

    public bool Available => hook != 0;
    internal bool WakeKeyListening => Volatile.Read(ref keyHook)!=0;
    internal void SetWakeSuspended(bool suspended) => wakeSuspended=suspended;
    internal void SetWakeShortcut(WakeShortcut? shortcut)
    {
        wakeShortcut=shortcut;
        uint owner=threadId;
        if(owner!=0)Native.PostThreadMessage(owner,WakeBindingChanged,0,0);
    }

    public PointerHook(Action<Native.Point,bool> handler,Func<Native.Point,uint,uint,bool>? filter=null,WakeShortcut? wakeShortcut=null,Action<WakeShortcut,nint>? wakePressed=null)
    {
        this.handler = handler;
        this.filter=filter;
        this.wakeShortcut=wakeShortcut;
        this.wakePressed=wakePressed;
        proc = Callback;
        keyProc = ObserveWakeKey;
        var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = Native.GetCurrentThreadId();
            Native.PeekMessage(out _, 0, 0, 0, 0); // force a message queue before installing the hook
            hook = Native.SetWindowsHookEx(14, proc, Native.GetModuleHandle(null), 0);
            UpdateWakeListener();
            Interlocked.Increment(ref Installations);
            ready.Set();
            while (!disposed && Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                if(message.hwnd==0&&message.message==WakeBindingChanged)UpdateWakeListener();
                else
                {
                    Native.TranslateMessage(ref message);
                    Native.DispatchMessage(ref message);
                }
            }
            if (hook != 0) { Native.UnhookWindowsHookEx(hook); hook = 0; }
            RemoveWakeListener();
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "MacDock pointer hook" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(2000);
    }

    void UpdateWakeListener()
    {
        if(!disposed&&wakePressed!=null&&wakeShortcut!=null)
        {
            if(keyHook==0)
                Volatile.Write(ref keyHook,Native.SetWindowsHookEx(13,keyProc,Native.GetModuleHandle(null),0));
        }
        else RemoveWakeListener();
    }

    void RemoveWakeListener()
    {
        nint listener=Interlocked.Exchange(ref keyHook,0);
        if(listener!=0)Native.UnhookWindowsHookEx(listener);
    }

    nint ObserveWakeKey(int code,nint message,nint data)
    {
        if(code==0&&!disposed&&!wakeSuspended&&message is 0x0100 or 0x0104)
        {
            try
            {
                var shortcut=wakeShortcut;
                if(shortcut!=null&&shortcut.MatchesPress(Marshal.ReadInt32(data),KeyHeld))
                    wakePressed?.Invoke(shortcut,Native.GetForegroundWindow());
            }
            catch { /* A Dock callback failure must still forward the original input. */ }
        }
        return Native.CallNextHookEx(0,code,message,data);
    }

    nint Callback(int code, nint wp, nint lp)
    {
        if (code >= 0 && !disposed)
        {
            try
            {
                var data = Marshal.PtrToStructure<Native.Mouse>(lp);
#if MACDOCK_EXPERIENCE_CHECKS
                GestureChecks.Trace?.Invoke("pointer-raw",new{message=(uint)wp,data.Pt.X,data.Pt.Y,data.Flags,extra=(long)data.Extra});
#endif
                if(AuditInput&&(data.Flags&1)==0)Interlocked.Increment(ref NonInjectedInputs);
                if(filter?.Invoke(data.Pt,(uint)wp,data.Data)==true)return 1;
                if (wp == 0x0200) handler(data.Pt, false);
                else if (wp is 0x0201 or 0x0204 or 0x0207) handler(data.Pt, true);
            }
            catch { }
        }
        return Native.CallNextHookEx(hook, code, wp, lp);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (threadId != 0) Native.PostThreadMessage(threadId, 0x0012, 0, 0); // WM_QUIT
        if(thread!=null && thread.ManagedThreadId!=Environment.CurrentManagedThreadId)thread.Join(250);
        thread = null;
    }
}
