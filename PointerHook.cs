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
    volatile bool desktopWakeKeyHeld;
    internal static int Installations;
    internal static bool AuditInput;
    internal static long NonInjectedInputs;
    volatile bool disposed;

    public bool Available => hook != 0;
    internal bool DesktopWakeKeyHeld => desktopWakeKeyHeld;

    public PointerHook(Action<Native.Point,bool> handler,Func<Native.Point,uint,uint,bool>? filter=null,bool suppressDesktopSearch=false)
    {
        this.handler = handler;
        this.filter=filter;
        proc = Callback;
        keyProc = FilterDesktopKey;
        var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = Native.GetCurrentThreadId();
            Native.PeekMessage(out _, 0, 0, 0, 0); // force a message queue before installing the hook
            hook = Native.SetWindowsHookEx(14, proc, Native.GetModuleHandle(null), 0);
            if(suppressDesktopSearch)keyHook=Native.SetWindowsHookEx(13,keyProc,Native.GetModuleHandle(null),0);
            Interlocked.Increment(ref Installations);
            ready.Set();
            while (!disposed && Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
            if (hook != 0) { Native.UnhookWindowsHookEx(hook); hook = 0; }
            if (keyHook != 0) { Native.UnhookWindowsHookEx(keyHook); keyHook = 0; }
            desktopWakeKeyHeld=false;
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "MacDock pointer hook" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(2000);
    }

    nint FilterDesktopKey(int code,nint message,nint data)
    {
        if(code>=0&&!disposed&&Marshal.PtrToStructure<Native.LowLevelKey>(data) is
            {VirtualKey:Native.GraveWakeKey,Scan:0x29})
        {
            if(desktopWakeKeyHeld)
            {
                if(message is 0x101 or 0x105)desktopWakeKeyHeld=false;
                return 1;
            }
            // Let a press that began in another app keep its repeats and release there.
            if(message==0x100&&!Native.KeyHeld(Native.GraveWakeKey)&&!Native.KeyHeld(0x10)&&
                !Native.KeyHeld(0x11)&&!Native.KeyHeld(0x12)&&!Native.KeyHeld(0x5B)&&!Native.KeyHeld(0x5C)&&
                Native.DesktopSearchHasFocus())
            {
                desktopWakeKeyHeld=true;
                return 1;
            }
        }
        return Native.CallNextHookEx(keyHook,code,message,data);
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
