using System.Runtime.InteropServices;

namespace MacDock;

// Read the actual Explorer desktop view, not the much broader desktop namespace folder.
// No selection, positioning or visibility is changed. Files are still enumerated directly.
internal static class DesktopView
{
    internal sealed record PositionedItem(string Path, Native.Point Position);
    internal static string? LastError;
    internal static double IconSizeDip {get;private set;}=48;
    internal static bool IconSizeAvailable {get;private set;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetSize(nint self,out uint mode,out int size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetWindow(nint self,out nint window);
    static void ReadSize(object view)
    {
        nint unknown=0,folder=0,shell=0;
        try{unknown=Marshal.GetIUnknownForObject(view);var iid=new Guid("1af3a467-214f-4298-908e-06b03e0b39f9");if(Marshal.QueryInterface(unknown,ref iid,out folder)<0)return;
            // IFolderView2 ABI from shobjidl_core.h: 14 base methods + 19 preceding methods.
            var call=Marshal.GetDelegateForFunctionPointer<GetSize>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(folder),36*IntPtr.Size));if(call(folder,out _,out var pixels)<0)return;
            iid=new Guid("000214e3-0000-0000-c000-000000000046");double scale=1;if(Marshal.QueryInterface(unknown,ref iid,out shell)>=0){var getWindow=Marshal.GetDelegateForFunctionPointer<GetWindow>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(shell),3*IntPtr.Size));if(getWindow(shell,out var window)>=0)scale=Native.GetDpiForWindow(window)/96.0;}IconSizeDip=Math.Clamp(pixels/Math.Max(1,scale),16,256);IconSizeAvailable=true;
        }catch(Exception e){LastError=e.Message;}finally{if(shell!=0)Marshal.Release(shell);if(folder!=0)Marshal.Release(folder);if(unknown!=0)Marshal.Release(unknown);}
    }
    [ComImport,Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IServiceProvider { [PreserveSig] int QueryService(ref Guid service,ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)] out object result); }
    [ComImport,Guid("000214e2-0000-0000-c000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellBrowser
    {
        void GetWindow(out nint window);void ContextSensitiveHelp(bool enter);
        void InsertMenusSB(nint menu,nint widths);void SetMenuSB(nint menu,nint hole,nint active);void RemoveMenusSB(nint menu);
        void SetStatusTextSB(nint text);void EnableModelessSB(bool enabled);void TranslateAcceleratorSB(nint message,ushort id);
        void BrowseObject(nint pidl,uint flags);void GetViewStateStream(uint mode,out nint stream);void GetControlWindow(uint id,out nint hwnd);
        void SendControlMsg(uint id,uint msg,nint wp,nint lp,out nint result);
        void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object view);
    }
    [ComImport,Guid("cde725b0-ccc9-4519-917e-325d72fab4ce"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFolderView
    {
        void GetCurrentViewMode(out uint mode);void SetCurrentViewMode(uint mode);void GetFolder(ref Guid iid,out nint folder);
        void Item(int index,out nint pidl);void ItemCount(uint flags,out int count);
        void Items(uint flags, ref Guid iid, out nint items);
        void GetSelectionMarkedItem(out int index);
        void GetFocusedItem(out int index);
        [PreserveSig] int GetItemPosition(nint pidl, out Native.Point position);
        [PreserveSig] int GetSpacing(out Native.Point spacing);
    }
    [DllImport("shell32.dll")] static extern int SHGetNameFromIDList(nint pidl,uint type,out nint name);
    static T? WithView<T>(Func<object,T> read)
    {
        object? windows=null,desktop=null,browser=null,view=null;
        try
        {
            windows=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!);
            object location=0;object empty=null!;int hwnd;
            desktop=((dynamic)windows!).FindWindowSW(ref location,ref empty,8,out hwnd,1);
            var service=new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");var iid=typeof(IShellBrowser).GUID;
            Marshal.ThrowExceptionForHR(((IServiceProvider)desktop!).QueryService(ref service,ref iid,out browser));
            ((IShellBrowser)browser).QueryActiveShellView(out view);
            return read(view);
        }
        catch(Exception e){LastError=e.Message;return default;}
        finally{foreach(var item in new[]{view,browser,desktop,windows})if(item!=null&&Marshal.IsComObject(item))Marshal.ReleaseComObject(item);}
    }
    public static IReadOnlyList<string>? Read()=>Read(false, out _);

    internal static IReadOnlyList<string>? Read(bool positions, out IReadOnlyList<PositionedItem> ordered)
    {
        var placed=new List<PositionedItem>();
        int columnTolerance=1;
        var snapshot=WithView<IReadOnlyList<string>>(view=>
        {
            ReadSize(view);
            var folder=(IFolderView)view;folder.ItemCount(2,out var count);
            if(positions&&folder.GetSpacing(out var spacing)==0&&spacing.X>0)
                columnTolerance=Math.Clamp(spacing.X/3,1,40);
            var paths=new List<string>();
            for(int i=0;i<count;i++)
            {
                nint pidl=0,name=0;
                try
                {
                    folder.Item(i,out pidl);
                    if(pidl==0||SHGetNameFromIDList(pidl,0x80028000,out name)<0)continue;
                    var path=Marshal.PtrToStringUni(name);
                    if(string.IsNullOrWhiteSpace(path))continue;
                    paths.Add(path);
                    if(positions&&folder.GetItemPosition(pidl,out var point)==0)
                        placed.Add(new PositionedItem(path,point));
                }
                catch(COMException){/* Explorer may remove an individual item while enumerating. */}
                finally{Marshal.FreeCoTaskMem(name);Marshal.FreeCoTaskMem(pidl);}
            }
            LastError=null;return paths;
        });
        ordered=snapshot==null?Array.Empty<PositionedItem>():ColumnOrder(placed,columnTolerance);
        return snapshot;
    }

    internal static IReadOnlyList<PositionedItem> ColumnOrder(IEnumerable<PositionedItem> items,int tolerance)
    {
        var ordered=new List<PositionedItem>();
        var columns=new List<PositionedItem>();
        foreach(var item in items.OrderBy(p=>p.Position.X))
        {
            if(columns.Count>0&&item.Position.X-(long)columns[0].Position.X>tolerance)
            {
                ordered.AddRange(columns.OrderBy(p=>p.Position.Y).ThenBy(p=>p.Position.X));
                columns.Clear();
            }
            columns.Add(item);
        }
        ordered.AddRange(columns.OrderBy(p=>p.Position.Y).ThenBy(p=>p.Position.X));
        return ordered;
    }
}
