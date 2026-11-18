using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MacDock;

// The genuine Explorer context menu (the "Show more options" menu on Windows 11), built from
// the same shell objects Explorer uses. Installed archivers, image tools and shell extensions
// therefore appear exactly as they do on the desktop, with no reimplementation here.
internal static class ShellMenu
{
    internal static int LastItems, LastCommand;
    internal static readonly List<string> LastVerbs=new();
    internal static readonly List<string> LastLabels=new();
    internal static readonly List<int> LastPositions=new();
    internal static Action? MenuDisplayed;
    internal static nint ActivePopup;

    const uint CMF_NORMAL = 0x0000;
    const uint CMF_EXPLORE = 0x0004;
    const uint CMF_CANRENAME = 0x0010;
    const uint CMF_ITEM_FLAGS = CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME;
    const uint CMF_BG_FLAGS = CMF_NORMAL | CMF_EXPLORE;
    const uint TPM_RETURNCMD = 0x0100;
    const uint TPM_RIGHTBUTTON = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    struct CMINVOKECOMMANDINFO
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public nint lpVerb;
        public nint lpParameters;
        public nint lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public nint hIcon;
    }

    [ComImport, Guid("000214e6-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint hwnd, nint bindCtx, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out nint pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint hwnd, uint flags, out nint enumerator);
        [PreserveSig] int BindToObject(nint pidl, nint bindCtx, ref Guid riid, out nint result);
        [PreserveSig] int BindToStorage(nint pidl, nint bindCtx, ref Guid riid, out nint result);
        [PreserveSig] int CompareIDs(nint parameter, nint pidl1, nint pidl2);
        [PreserveSig] int CreateViewObject(nint hwnd, ref Guid riid, out nint result);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(nint hwnd, uint cidl, [MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref Guid riid, nint reserved, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("000214e4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint indexMenu, int idCmdFirst, int idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(nint idCmd, uint type, nint reserved, nint name, uint max);
    }

    [ComImport, Guid("000214f4-0000-0000-c000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint indexMenu, int idCmdFirst, int idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(nint idCmd, uint type, nint reserved, nint name, uint max);
        [PreserveSig] int HandleMenuMsg(uint msg, nint wParam, nint lParam);
    }

    [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(nint menu, uint indexMenu, int idCmdFirst, int idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFO info);
        [PreserveSig] int GetCommandString(nint idCmd, uint type, nint reserved, nint name, uint max);
        [PreserveSig] int HandleMenuMsg(uint msg, nint wParam, nint lParam);
        [PreserveSig] int HandleMenuMsg2(uint msg, nint wParam, nint lParam, out nint result);
    }

    [DllImport("user32.dll", SetLastError = true)] static extern nint CreatePopupMenu();
    [DllImport("user32.dll")]static extern int GetMenuItemCount(nint menu);
    [DllImport("user32.dll")]static extern uint GetMenuItemID(nint menu,int position);
    [DllImport("user32.dll")]static extern uint GetMenuState(nint menu,uint item,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetMenuString(nint menu,uint item,System.Text.StringBuilder text,int max,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool InsertMenu(nint menu,uint position,uint flags,nuint id,string? text);
    [DllImport("user32.dll", SetLastError = true)] static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll", SetLastError = true)] static extern int TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint owner, nint parameters);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] static extern int SHParseDisplayName(string name, nint bindCtx, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("shell32.dll", PreserveSig = true)] static extern int SHBindToParent(nint pidl, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellFolder folder, out nint child);
    [DllImport("shell32.dll")] static extern void ILFree(nint pidl);
    [DllImport("shell32.dll")]static extern int SHGetDesktopFolder(out IShellFolder folder);

    /// <summary>
    /// Parent folder plus the child PIDLs of the selection. SHBindToParent is the documented
    /// route: it returns both halves in the exact relationship IShellFolder expects.
    /// </summary>
    static IShellFolder? Resolve(IReadOnlyList<string> paths, List<nint> fullPidls, List<nint> children)
    {
        IShellFolder? folder = null;
        foreach (var path in paths)
        {
            if (SHParseDisplayName(path, 0, out var pidl, 0, out _) != 0 || pidl == 0) return null;
            fullPidls.Add(pidl);
            var iid = typeof(IShellFolder).GUID;
            if (SHBindToParent(pidl, ref iid, out var parent, out var child) != 0 || child == 0) return null;
            children.Add(child);
            if (folder == null) folder = parent; else Marshal.ReleaseComObject(parent);
        }
        return folder;
    }

    /// <summary>Shows the shell menu for a single item, including shell-namespace items.</summary>
    public static bool ShowItem(nint owner, string parsingName, int x, int y,Action? rename=null,Action? pin=null,bool pinned=false) => Show(owner, new[] { parsingName }, x, y, true,rename,pin,pinned);
    public static bool ShowBackground(nint owner,int x,int y,Action paste,bool canPaste,Action refresh)
    {
        object? context=null;IShellFolder? folder=null;nint menu=0,pointer=0;
        Native.SuppressAppStartingCursor();
        try{Marshal.ThrowExceptionForHR(SHGetDesktopFolder(out folder));var iid=typeof(IContextMenu).GUID;Marshal.ThrowExceptionForHR(folder.CreateViewObject(owner,ref iid,out pointer));context=Marshal.GetObjectForIUnknown(pointer);var commands=(IContextMenu)context;menu=CreatePopupMenu();LastItems=commands.QueryContextMenu(menu,0,1,0x7FFD,CMF_BG_FLAGS);
            // Folder background handlers supply New and installed extensions. View-specific
            // paste/refresh belong to this host and still use the real desktop IFileOperation.
            InsertMenu(menu,0,0x400u|(canPaste?0u:1u),0x7FFE,"粘贴到桌面(&P)");InsertMenu(menu,1,0x400,0x7FFF,"刷新(&E)");InsertMenu(menu,2,0xC00,0,null);
            return LastItems>=0&&Track(owner,context,menu,x,y,custom:new Dictionary<int,(string Verb,Action Run)>{{0x7FFE,("paste",paste)},{0x7FFF,("refresh",refresh)}});}
        catch(Exception e){Program.Log(e);return false;}finally{if(menu!=0)DestroyMenu(menu);if(pointer!=0)Marshal.Release(pointer);if(context!=null)Marshal.ReleaseComObject(context);if(folder!=null)Marshal.ReleaseComObject(folder);Native.RestoreSystemCursors();}
    }

    /// <summary>
    /// Shows the multi-selection shell menu for items that share one parent folder. Returns
    /// false when they span folders, so the caller keeps its own menu.
    /// </summary>
    public static bool Show(nint owner, IReadOnlyList<string> paths, int x, int y, bool single = false,Action? rename=null,Action? pin=null,bool pinned=false)
    {
        if (paths.Count == 0) return false;
        if (!single)
        {
            var parent = Path.GetDirectoryName(paths[0]);
            if (string.IsNullOrEmpty(parent)) return false;
            if (paths.Any(p => !string.Equals(Path.GetDirectoryName(p), parent, StringComparison.OrdinalIgnoreCase))) return false;
        }

        object? menuObject = null;
        IShellFolder? folder = null;
        var fullPidls = new List<nint>();
        var children = new List<nint>();
        nint menu = 0;
        Native.SuppressAppStartingCursor();
        try
        {
            folder = Resolve(paths, fullPidls, children);InteractionChecks.Record?.Invoke("shell-resolve",null);
            if (folder == null || children.Count == 0) return false;

            // The shell folder hands out the base IContextMenu; version 2/3 are obtained by QI.
            var iid = typeof(IContextMenu).GUID;
            var hr = folder.GetUIObjectOf(owner, (uint)children.Count, children.ToArray(), ref iid, 0, out var candidate);
            if (hr < 0 || candidate == null) return false;
            menuObject = candidate;InteractionChecks.Record?.Invoke("shell-context",null);

            menu = CreatePopupMenu();
            if (menu == 0) return false;
            var context = (IContextMenu)menuObject;
            LastItems = context.QueryContextMenu(menu, 0, 1, 0x7FFD, CMF_ITEM_FLAGS);InteractionChecks.Record?.Invoke("shell-query",LastItems);
            if (LastItems < 0) return false;
            Dictionary<int,(string Verb,Action Run)>? custom=null;
            if(pin!=null){InsertMenu(menu,0,0x400,0x7FFE,pinned?"从 Dock 取消固定(&K)":"固定到 Dock(&K)");InsertMenu(menu,1,0xC00,0,null);custom=new(){{0x7FFE,("macdock-pin",pin)}};}
            return Track(owner, menuObject, menu, x, y,rename,custom);
        }
        catch (Exception e) { Program.Log(e); return false; }
        finally
        {
            if (menu != 0) DestroyMenu(menu);
            foreach (var pidl in fullPidls) ILFree(pidl);
            if (menuObject != null) Marshal.ReleaseComObject(menuObject);
            if (folder != null) Marshal.ReleaseComObject(folder);
            Native.RestoreSystemCursors();
        }
    }

    static bool Track(nint owner, object menuObject, nint popup, int x, int y,Action? rename=null,Dictionary<int,(string Verb,Action Run)>? custom=null)
    {
        var context = (IContextMenu)menuObject;
        var extended = menuObject as IContextMenu2;
        HwndSource? source = HwndSource.FromHwnd(owner);
        HwndSourceHook? hook = null;
        if (source != null)
        {
            hook = (nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
            {
                if(msg==0x0020){Native.SetArrowCursor();handled=true;return 1;}
                if(msg is 0x0117 or 0x0121)Native.SetArrowCursor();
                if(msg==0x0117)InteractionChecks.Record?.Invoke("shell-init-popup",null); if (msg is 0x0117 or 0x011F or 0x002B or 0x002C or 0x0120)
                {
                    nint result = 0;
                    var hr = menuObject is IContextMenu3 three ? three.HandleMenuMsg2((uint)msg, wParam, lParam, out result) : extended != null ? extended.HandleMenuMsg((uint)msg, wParam, lParam) : 0;
                    handled = hr >= 0;
                    if(handled)return result;
                }
                return 0;
            };
            source.AddHook(hook);
        }
        try
        {
            ActivePopup = popup;
            if (MenuDisplayed != null)
            {
                InteractionChecks.Record?.Invoke("shell-inspect-start",null);LastVerbs.Clear();LastLabels.Clear();LastPositions.Clear();
                for(int i=0;i<GetMenuItemCount(popup);i++){
                    var id=GetMenuItemID(popup,i);var state=GetMenuState(popup,(uint)i,0x400);
                    if((state&0x803)!=0)continue;
                    LastPositions.Add(i);
                    var label=new System.Text.StringBuilder(512);GetMenuString(popup,(uint)i,label,512,0x400);LastLabels.Add(label.ToString());
                    var name=Marshal.AllocHGlobal(512);try{Marshal.WriteInt16(name,0);LastVerbs.Add(custom!=null&&custom.TryGetValue((int)id,out var command)?command.Verb:id is >0 and <0x8000&&context.GetCommandString((nint)(id-1),4,0,name,256)>=0?Marshal.PtrToStringUni(name)??"":"");}finally{Marshal.FreeHGlobal(name);}
                }
                InteractionChecks.Record?.Invoke("shell-inspect-end",LastVerbs.Count);
            }
            Native.SetArrowCursor();
            SetForegroundWindow(owner);
            MenuDisplayed?.Invoke();
            Native.SetArrowCursor();
            InteractionChecks.Record?.Invoke("shell-track",null);LastCommand = TrackPopupMenuEx(popup, TPM_RETURNCMD | TPM_RIGHTBUTTON, x, y, owner, 0);
            if (LastCommand <= 0) return true; // cancellation is a successfully displayed menu
            if(custom!=null&&custom.TryGetValue(LastCommand,out var selected)){selected.Run();return true;}
            var buffer=Marshal.AllocHGlobal(512);try{if(context.GetCommandString(LastCommand-1,4,0,buffer,256)>=0&&string.Equals(Marshal.PtrToStringUni(buffer),"rename",StringComparison.OrdinalIgnoreCase)&&rename!=null){rename();return true;}}finally{Marshal.FreeHGlobal(buffer);}
            var info = new CMINVOKECOMMANDINFO
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                fMask = 0,
                hwnd = owner,
                lpVerb = LastCommand - 1,
                nShow = 1
            };
            return context.InvokeCommand(ref info) >= 0;
        }
        catch (Exception e) { Program.Log(e); return false; }
        finally { ActivePopup=0;if (hook != null) source?.RemoveHook(hook); }
    }
}
