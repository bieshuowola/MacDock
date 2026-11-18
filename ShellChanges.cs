using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace MacDock;

// Public Shell change notifications complement the nonrecursive filesystem watchers.
// The Desktop PIDL scope covers its direct real/virtual objects, not installed applications.
internal sealed class ShellChanges:IDisposable
{
    const int Message=0x8000+79;
    [StructLayout(LayoutKind.Sequential)]struct Entry{public nint Pidl;[MarshalAs(UnmanagedType.Bool)]public bool Recursive;}
    [DllImport("shell32.dll")]static extern int SHGetSpecialFolderLocation(nint owner,int folder,out nint pidl);
    [DllImport("shell32.dll")]static extern uint SHChangeNotifyRegister(nint owner,int sources,int events,uint message,int count,ref Entry entries);
    [DllImport("shell32.dll")]static extern bool SHChangeNotifyDeregister(uint cookie);
    [DllImport("shell32.dll")]static extern nint SHChangeNotification_Lock(nint change,uint process,out nint items,out int events);
    [DllImport("shell32.dll")]static extern bool SHChangeNotification_Unlock(nint locked);
    [DllImport("shell32.dll")]static extern int SHGetNameFromIDList(nint pidl,uint kind,out nint name);
    readonly HwndSource source;readonly HwndSourceHook hook;readonly uint cookie;
    internal ShellChanges(nint owner,Action<int,string[]> changed)
    {
        source=HwndSource.FromHwnd(owner);hook=(nint h,int msg,nint w,nint l,ref bool handled)=>{
            if(msg!=Message)return 0;
            var locked=SHChangeNotification_Lock(w,(uint)l,out var items,out var events);
            if(locked==0)return 0;
            var paths=new List<string>();
            try{
                // UPDATEIMAGE/ASSOCCHANGED carry non-PIDL payloads; never parse those.
                if((events&0x08008000)==0&&items!=0)for(int i=0;i<((events&0x20001)!=0?2:1);i++){
                    var pidl=Marshal.ReadIntPtr(items,i*IntPtr.Size);if(pidl==0)continue;
                    if(SHGetNameFromIDList(pidl,0x80028000,out var name)>=0){try{if(Marshal.PtrToStringUni(name) is string path)paths.Add(path);}finally{Marshal.FreeCoTaskMem(name);}}
                }
            }finally{SHChangeNotification_Unlock(locked);}
            changed(events,paths.ToArray());handled=true;return 0;
        };
        source.AddHook(hook);
        if(SHGetSpecialFolderLocation(owner,0,out var desktop)>=0){try{var entry=new Entry{Pidl=desktop,Recursive=false};cookie=SHChangeNotifyRegister(owner,0x8003,0x0803FFFF,Message,1,ref entry);}finally{Marshal.FreeCoTaskMem(desktop);}}
        if(cookie==0)Program.Log(new InvalidOperationException("Shell 桌面通知不可用；文件监听与定期校准继续有效。"));
    }
    public void Dispose(){if(cookie!=0)SHChangeNotifyDeregister(cookie);source.RemoveHook(hook);}
}
