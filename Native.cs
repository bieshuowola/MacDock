using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using ComData = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace MacDock;
internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; public Point(int x,int y) { X=x;Y=y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; public bool Contains(Point p) => p.X>=Left && p.X<Right && p.Y>=Top && p.Y<Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public Point Pt; public uint Data,Flags,Time; public nint Extra; }
    internal delegate nint MouseProc(int code, nint wp, nint lp);
    internal delegate void EventProc(nint hook,uint evt,nint hwnd,int obj,int child,uint thread,uint time);
    internal delegate bool EnumProc(nint hwnd,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool MoveFileEx(string source,string destination,uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct Margins { public int Left,Right,Top,Bottom; }
    [DllImport("dwmapi.dll")] internal static extern int DwmExtendFrameIntoClientArea(nint hwnd,ref Margins margins);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
    [DllImport("dwmapi.dll")] static extern int DwmIsCompositionEnabled(out bool enabled);
    internal static bool DocumentedMaterial=>Environment.GetEnvironmentVariable("MACDOCK_MATERIAL_BACKEND")?.StartsWith("documented",StringComparison.Ordinal)==true;
    internal static bool HostMaterial=>Environment.GetEnvironmentVariable("MACDOCK_MATERIAL_BACKEND") is null or "host";
    internal static bool TransparencyAllowed
    {
        get{using var preferences=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");return !System.Windows.SystemParameters.HighContrast&&!(preferences?.GetValue("EnableTransparency") is int enabled&&enabled==0)&&DwmIsCompositionEnabled(out bool composition)==0&&composition;}
    }
    [StructLayout(LayoutKind.Sequential)] struct Accent { public int State,Flags,Color,Animation; }
    [StructLayout(LayoutKind.Sequential)] struct CompositionData { public int Attribute;public nint Data;public int Size; }
    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(nint hwnd,ref CompositionData data);
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint hwnd,uint message,nint wp,nint lp);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    internal static bool Acrylic(nint hwnd,bool inactive)
    {
        using var preferences=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
        if(System.Windows.SystemParameters.HighContrast || preferences?.GetValue("EnableTransparency") is int enabled && enabled==0 || DwmIsCompositionEnabled(out bool composition)!=0 || !composition)
        { int off=1;DwmSetWindowAttribute(hwnd,38,ref off,4);var policy=new Accent();nint p=Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());try { Marshal.StructureToPtr(policy,p,false);var cd=new CompositionData { Attribute=19,Data=p,Size=Marshal.SizeOf<Accent>() };SetWindowCompositionAttribute(hwnd,ref cd); }catch(EntryPointNotFoundException){}finally{Marshal.FreeHGlobal(p);}return false; }
        int dark=Theme.Dark?1:0,corner=2,border=unchecked((int)0xFFFFFFFE),type=3;
        if(DocumentedMaterial)
        {
            nint p=Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
            try{Marshal.StructureToPtr(new Accent(),p,false);var reset=new CompositionData{Attribute=19,Data=p,Size=Marshal.SizeOf<Accent>()};SetWindowCompositionAttribute(hwnd,ref reset);}finally{Marshal.FreeHGlobal(p);}
        }
        DwmSetWindowAttribute(hwnd,20,ref dark,4);
        // DWMWA_WINDOW_CORNER_PREFERENCE: let DWM clip the real material with an antialiased
        // rounded corner. The WPF surface below uses the same 8 DIP radius so both meet exactly.
        DwmSetWindowAttribute(hwnd,33,ref corner,4);
        DwmSetWindowAttribute(hwnd,34,ref border,4);
        var margins=new Margins { Left=-1,Right=-1,Top=-1,Bottom=-1 };DwmExtendFrameIntoClientArea(hwnd,ref margins);
        bool success=DwmSetWindowAttribute(hwnd,38,ref type,4)>=0;
        if(!DocumentedMaterial && Environment.OSVersion.Version.Build>=22000)
        {
            // The documented transient material can flatten on inactive windows. Accent blur remains composited without focus.
            // Isolated compatibility path: no injection, no shell modification; fallback to documented DWM on failure.
            int none=1;DwmSetWindowAttribute(hwnd,38,ref none,4);
            var blur=Environment.GetEnvironmentVariable("MACDOCK_MATERIAL_BACKEND")=="blur";
            var accent=new Accent { State=blur?3:4,Flags=2,Color=blur?0:unchecked((int)(Theme.Dark?0x502D2119:0x28D4DADF)) };
            nint memory=Marshal.AllocHGlobal(Marshal.SizeOf<Accent>());
            try { Marshal.StructureToPtr(accent,memory,false);var data=new CompositionData { Attribute=19,Data=memory,Size=Marshal.SizeOf<Accent>() };if(SetWindowCompositionAttribute(hwnd,ref data)!=0)success=true;else DwmSetWindowAttribute(hwnd,38,ref type,4); }
            catch(EntryPointNotFoundException) { DwmSetWindowAttribute(hwnd,38,ref type,4); }
            finally { Marshal.FreeHGlobal(memory); }
        }

        return success;
    }
    internal static void HostBackdrop(nint hwnd)
    {
        int none=1,enable=1,corner=2,border=unchecked((int)0xFFFFFFFE);DwmSetWindowAttribute(hwnd,38,ref none,4);Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(hwnd,17,ref enable,4));DwmSetWindowAttribute(hwnd,33,ref corner,4);DwmSetWindowAttribute(hwnd,34,ref border,4);
        var margins=new Margins{Left=-1,Right=-1,Top=-1,Bottom=-1};DwmExtendFrameIntoClientArea(hwnd,ref margins);
    }
    // DWM rounds at 8 px scaled by DPI, which equals 8 DIPs.
    internal const double CornerRadius = 8;
    [StructLayout(LayoutKind.Sequential)] internal struct MSG { public nint hwnd; public uint message; public nint wParam,lParam; public uint time; public Point pt; public uint lPrivate; }
    [DllImport("user32.dll")] internal static extern nint SetWindowsHookEx(int id,MouseProc cb,nint module,uint thread);
    [DllImport("user32.dll")] internal static extern int GetMessage(out MSG message,nint hwnd,uint min,uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out MSG message,nint hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref MSG message);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread,uint message,nint wParam,nint lParam);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint h,int code,nint wp,nint lp);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min,uint max,nint module,EventProc cb,uint pid,uint thread,uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint h);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    internal static bool IsControlDown() =>
        (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0
        || GetKeyState(0x11) < 0
        || KeyHeld(0x11);
    internal static bool IsShiftDown() =>
        (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0
        || GetKeyState(0x10) < 0
        || KeyHeld(0x10);
    [DllImport("user32.dll")] static extern nint LoadCursor(nint instance, nint name);
    [DllImport("user32.dll")] static extern nint CopyIcon(nint icon);
    [DllImport("user32.dll")] static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] static extern bool SetSystemCursor(nint cursor, uint id);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint action, uint param, nint pvParam, uint winIni);
    static readonly object cursorGate = new();
    static bool cursorsSuppressed;
    static System.Threading.Timer? cursorRestoreTimer;
    internal static void SetArrowCursor() { var arrow = LoadCursor(0, 32512); if (arrow != 0) SetCursor(arrow); }
    internal static void SuppressAppStartingCursor()
    {
        lock (cursorGate)
        {
            SetArrowCursor();
            cursorRestoreTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            if (!cursorsSuppressed)
            {
                var arrow = LoadCursor(0, 32512);
                if (arrow != 0)
                {
                    var copy1 = CopyIcon(arrow);
                    if (copy1 != 0 && !SetSystemCursor(copy1, 32650)) DestroyIcon(copy1);
                    var copy2 = CopyIcon(arrow);
                    if (copy2 != 0 && !SetSystemCursor(copy2, 32514)) DestroyIcon(copy2);
                    cursorsSuppressed = true;
                }
            }
        }
    }
    internal static void RestoreSystemCursors(bool force = false)
    {
        lock (cursorGate)
        {
            if (force)
            {
                cursorRestoreTimer?.Dispose();
                cursorRestoreTimer = null;
                if (!cursorsSuppressed) return;
                cursorsSuppressed = false;
                SystemParametersInfo(0x0057, 0, 0, 0);
                return;
            }
            if (!cursorsSuppressed) return;
            cursorRestoreTimer ??= new System.Threading.Timer(_ =>
            {
                lock (cursorGate)
                {
                    if (!cursorsSuppressed) return;
                    cursorsSuppressed = false;
                    SystemParametersInfo(0x0057, 0, 0, 0);
                }
            }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            cursorRestoreTimer.Change(1500, System.Threading.Timeout.Infinite);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct GUITHREADINFO { public int cbSize; public uint flags; public nint hwndActive,hwndFocus,hwndCapture,hwndMenuOwner,hwndMoveSize,hwndCaret; public Rect rcCaret; }
    [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint idThread,ref GUITHREADINFO lpgui);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point pt);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]internal static extern uint GetDoubleClickTime();
    internal static int DoubleClickWidth=>Math.Max(1,GetSystemMetrics(36)/2);
    internal static int DoubleClickHeight=>Math.Max(1,GetSystemMetrics(37)/2);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct MonitorInfo {public int Size;public Rect Bounds,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
    delegate bool MonitorProc(nint monitor,nint dc,ref Rect rect,nint data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorProc callback,nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(nint monitor,ref MonitorInfo info);
    internal static System.Drawing.Rectangle MonitorArea(System.Windows.Forms.Screen screen,bool work)
    {
        var result=work?screen.WorkingArea:screen.Bounds;
        EnumDisplayMonitors(0,0,(nint m,nint dc,ref Rect rect,nint data)=>{var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>(),Device=""};if(GetMonitorInfo(m,ref info)&&info.Device==screen.DeviceName){var r=work?info.Work:info.Bounds;result=System.Drawing.Rectangle.FromLTRB(r.Left,r.Top,r.Right,r.Bottom);return false;}return true;},0);
        return result;
    }
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,nuint extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,nuint extra);
    internal static void TestButton(bool down)=>mouse_event(down?2u:4u,0,0,0,0);
    internal static void TestRightButton(bool down)=>mouse_event(down?8u:16u,0,0,0,0);
    internal static void TestWheel(int delta)=>mouse_event(0x800,0,0,unchecked((uint)delta),0);
    internal static void TestKey(byte key,bool down,bool extended=false,byte scan=0)=>keybd_event(key,scan,(down?0u:2u)|(extended?1u:0u),0);
    internal static void MovePointer(int x,int y)
    {
        var r=new System.Drawing.Rectangle(GetSystemMetrics(76),GetSystemMetrics(77),GetSystemMetrics(78),GetSystemMetrics(79));
        mouse_event(0xC001,(uint)((x-r.Left)*65535L/Math.Max(1,r.Width-1)),(uint)((y-r.Top)*65535L/Math.Max(1,r.Height-1)),0,0);
    }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd,out Rect r);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc cb,nint param);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(nint hwnd,StringBuilder text,int max);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd,int cmd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint FindWindowEx(nint parent,nint after,string? cls,string? title);
    internal static void ActivateWindow(nint hwnd){if(IsIconic(hwnd))ShowWindow(hwnd,9);SetForegroundWindow(hwnd);}
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(nint hwnd,int command);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd,uint message,nint wParam,nint lParam);
    internal static void MinimizeWindow(nint hwnd)=>ShowWindowAsync(hwnd,6);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(nint hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern nint BeginDeferWindowPos(int numWindows);
    [DllImport("user32.dll")] internal static extern nint DeferWindowPos(nint winPosInfo,nint hwnd,nint after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern bool EndDeferWindowPos(nint winPosInfo);
    [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint period);
    internal static nint FindActiveSysDragImage()
    {
        for(nint h=FindWindowEx(0,0,"SysDragImage",null);h!=0;h=FindWindowEx(0,h,"SysDragImage",null))
        {
            if(IsWindowVisible(h)&&(GetWindowLongPtr(h,-20).ToInt64()&0x8)!=0)return h;
        }
        return 0;
    }
    internal static nint TopmostInsertAfter(nint selfHwnd)
    {
        if(PointerSort.Active?.GhostHandle is nint gh&&gh!=0&&gh!=selfHwnd&&IsWindowVisible(gh))return gh;
        if(DropPreviewHelper.IsActive||FindActiveSysDragImage()!=0)
        {
            nint di=FindActiveSysDragImage();
            if(di!=0&&di!=selfHwnd)return di;
        }
        return new nint(-1);
    }
    internal static bool IsWindowAbove(nint upper,nint lower)
    {
        if(upper==0||lower==0||upper==lower)return false;
        for(nint h=GetWindow(lower,3);h!=0;h=GetWindow(h,3))
        {
            if(h==upper)return true;
        }
        return false;
    }
    internal static void DeferWindowPosAsync(nint hwnd,nint after,int x,int y,int cx,int cy,uint flags)
    {
        if(hwnd==0)return;
        // All MacDock windows live on the UI thread; synchronous BeginDeferWindowPos/EndDeferWindowPos
        // commits geometry and Z-order in one atomic DWM frame without SWP_ASYNCWINDOWPOS Z-order races.
        uint f=flags|0x0210u; // SWP_NOOWNERZORDER | SWP_NOACTIVATE
        nint hdwp=BeginDeferWindowPos(1);
        if(hdwp!=0)
        {
            hdwp=DeferWindowPos(hdwp,hwnd,after,x,y,cx,cy,f);
            if(hdwp!=0&&EndDeferWindowPos(hdwp))return;
        }
        SetWindowPos(hwnd,after,x,y,cx,cy,f);
    }
    internal static void DeferWindowPosPair(nint hwnd1,nint after1,int x1,int y1,int cx1,int cy1,uint flags1,nint hwnd2,nint after2,int x2,int y2,int cx2,int cy2,uint flags2)
    {
        if(hwnd1==0){DeferWindowPosAsync(hwnd2,after2,x2,y2,cx2,cy2,flags2);return;}
        if(hwnd2==0){DeferWindowPosAsync(hwnd1,after1,x1,y1,cx1,cy1,flags1);return;}
        const uint baseFlags=0x0210u; // SWP_NOOWNERZORDER | SWP_NOACTIVATE
        nint hdwp=BeginDeferWindowPos(2);
        if(hdwp!=0)
        {
            hdwp=DeferWindowPos(hdwp,hwnd1,after1,x1,y1,cx1,cy1,flags1|baseFlags);
            if(hdwp!=0)hdwp=DeferWindowPos(hdwp,hwnd2,after2,x2,y2,cx2,cy2,flags2|baseFlags);
            if(hdwp!=0&&EndDeferWindowPos(hdwp))return;
        }
        SetWindowPos(hwnd1,after1,x1,y1,cx1,cy1,flags1|baseFlags);
        SetWindowPos(hwnd2,after2,x2,y2,cx2,cy2,flags2|baseFlags);
    }
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd,int id,uint mod,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd,int id);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point pt);

    // ---- Shell icons -------------------------------------------------------------------
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)]
    static extern void SHCreateItemFromParsingName(string path,nint ctx,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object item);
    [StructLayout(LayoutKind.Sequential)] internal struct SizeI { public int cx,cy; }
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { void GetImage(SizeI size,int flags,out nint bitmap); }
    [StructLayout(LayoutKind.Sequential)] struct Bitmap { public int Type,Width,Height,WidthBytes; public ushort Planes,Bits; public nint BitsPtr; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfoHeader { public int Size,Width,Height; public ushort Planes,BitCount; public int Compression,SizeImage,XPels,Ypels,ClrUsed,ClrImportant; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo { public BitmapInfoHeader Header; public int Colors; }
    [DllImport("gdi32.dll")] static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] static extern int GetObject(nint obj,int size,out Bitmap data);
    [DllImport("gdi32.dll")] static extern int GetDIBits(nint dc,nint bitmap,uint start,uint lines,byte[] bits,ref BitmapInfo info,uint usage);

    /// <summary>Native-size icon (or thumbnail-free shell image) rendered by the shell at the requested pixel size.</summary>
    static readonly HashSet<string> ImageFileExts = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
    readonly record struct IconCacheKey(string Path, long LastWriteUtcTicks, long Length, int Pixels, string Stamp);
    static readonly ConcurrentDictionary<IconCacheKey, ImageSource> imageCache = new();
    static IconCacheKey MakeIconCacheKey(string path, int pixels)
    {
        long ticks = 0, length = -1;
        if (!string.IsNullOrEmpty(path) && !path.StartsWith("::", StringComparison.Ordinal) && !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            try
            {
                var info = new System.IO.FileInfo(path);
                if (info.Exists) { ticks = info.LastWriteTimeUtc.Ticks; length = info.Length; }
                else if (Directory.Exists(path)) { ticks = Directory.GetLastWriteTimeUtc(path).Ticks; length = 0; }
            }
            catch { }
        }
        return new IconCacheKey(path.ToUpperInvariant(), ticks, length, Math.Clamp(pixels, 32, 256), IconResources.Stamp(path));
    }
    internal static void ClearIconCache(string? path = null)
    {
        if (path == null) { imageCache.Clear(); return; }
        var normalized = path.ToUpperInvariant();
        foreach (var k in imageCache.Keys) if (k.Path == normalized) imageCache.TryRemove(k, out _);
    }
    static ImageSource? DecodeImageThumbnail(string path, int maxPx)
    {
        try
        {
            if (!File.Exists(path)) return null;
            int targetPx = Math.Clamp(maxPx, 32, 96);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = targetPx;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
    internal static ImageSource? Icon(string path,int pixels)
    {
        pixels=Math.Clamp(pixels,32,256);
        var cacheKey=MakeIconCacheKey(path,pixels);
        if(imageCache.TryGetValue(cacheKey,out var cached))return cached;
        ImageSource? image=null;
        if(ImageFileExts.Contains(Path.GetExtension(path)))
        {
            image=DecodeImageThumbnail(path,pixels);
        }
        if(image==null)
        {
            var resource=IconResources.Describe(path);
            image=PackagedIcons.Load(path,pixels)??(resource!=null?ResourceIcon(resource.File,resource.Index,pixels):null);
            if(image==null&&path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))
            {
                var target=IconResources.ReadLink(path).Target;
                if(!string.IsNullOrWhiteSpace(target)&&(File.Exists(target)||Directory.Exists(target)))
                {
                    var targetResource=IconResources.Describe(target);
                    image=(targetResource!=null?ResourceIcon(targetResource.File,targetResource.Index,pixels):null)
                        ??FromShellItem(target,pixels)??FromFileInfo(target);
                }
            }
            if(image==null){image=FromShellItem(path,pixels);if(image is BitmapSource bitmap&&Icons.IsJumboPlaceholder(bitmap))image=FromFileInfo(path)??image;}
            image=image ?? FromFileInfo(path) ?? StockIcon(path,pixels);
        }
        if(image!=null)
        {
            if(!image.IsFrozen&&image.CanFreeze)image.Freeze();
            if(image.IsFrozen)
            {
                if(imageCache.Count>=1024)imageCache.Clear();
                imageCache[cacheKey]=image;
            }
        }
        return image;
    }
    internal static ImageSource? AuditShellIcon(string path,int pixels)=>FromShellItem(path,pixels);
    internal static ImageSource? AuditSmallIcon(string path)=>FromFileInfo(path);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHParseDisplayName(string name,nint ctx,out nint pidl,uint sfgaoIn,out uint sfgaoOut);
    [DllImport("shell32.dll",EntryPoint="SHGetFileInfoW")] static extern nint SHGetFileInfoPidl(nint pidl,uint attr,out FileInfo info,uint size,uint flags);
    static nint TryResolvePidl(string path)
    {
        if(SHParseDisplayName(path,0,out var pidl,0,out _)==0&&pidl!=0)return pidl;
        if(path.StartsWith("::",StringComparison.Ordinal)&&SHParseDisplayName("shell:"+path,0,out pidl,0,out _)==0&&pidl!=0)return pidl;
        return 0;
    }
    internal static (string Path,int Index)? IconLocation(string path)
    {
        try
        {
            if(SHGetFileInfo(path,0,out var info,(uint)Marshal.SizeOf<FileInfo>(),0x1000)!=0&&!string.IsNullOrWhiteSpace(info.Name))
                return (Environment.ExpandEnvironmentVariables(info.Name.Trim('"')),info.Index);
            if(path.StartsWith("::",StringComparison.Ordinal))
            {
                var pidl=TryResolvePidl(path);
                if(pidl!=0)
                {
                    try
                    {
                        if(SHGetFileInfoPidl(pidl,0,out info,(uint)Marshal.SizeOf<FileInfo>(),0x1008)!=0&&!string.IsNullOrWhiteSpace(info.Name))
                            return (Environment.ExpandEnvironmentVariables(info.Name.Trim('"')),info.Index);
                    }
                    finally { Marshal.FreeCoTaskMem(pidl); }
                }
            }
            return null;
        }
        catch{return null;}
    }
    static bool HasVisiblePixels(BitmapSource? bitmap)
    {
        if(bitmap==null||bitmap.PixelWidth<=0||bitmap.PixelHeight<=0)return false;
        if(bitmap.Format==PixelFormats.Bgr32||bitmap.Format.BitsPerPixel!=32)return true;
        int stride=bitmap.PixelWidth*4;
        var data=new byte[stride*bitmap.PixelHeight];
        try{bitmap.CopyPixels(data,stride,0);}catch{return false;}
        for(int i=3;i<data.Length;i+=4)if(data[i]>8)return true;
        return false;
    }
    static BitmapSource? HIconToBitmap(nint hIcon)
    {
        if(hIcon==0)return null;
        try
        {
            var direct=Imaging.CreateBitmapSourceFromHIcon(hIcon,System.Windows.Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            if(HasVisiblePixels(direct)){direct.Freeze();return direct;}
        }
        catch{}
        try
        {
            using var sysIcon=System.Drawing.Icon.FromHandle(hIcon);
            using var bmp=sysIcon.ToBitmap();
            if(bmp.Width<=0||bmp.Height<=0)return null;
            var rect=new System.Drawing.Rectangle(0,0,bmp.Width,bmp.Height);
            var locked=bmp.LockBits(rect,System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride=bmp.Width*4;
                var data=new byte[stride*bmp.Height];
                for(int y=0;y<bmp.Height;y++)
                    Marshal.Copy(locked.Scan0+y*locked.Stride,data,y*stride,stride);
                bool visible=false;
                for(int i=3;i<data.Length;i+=4)if(data[i]>8){visible=true;break;}
                if(!visible)return null;
                var source=BitmapSource.Create(bmp.Width,bmp.Height,96,96,PixelFormats.Bgra32,null,data,stride);
                source.Freeze();return source;
            }
            finally { bmp.UnlockBits(locked); }
        }
        catch{return null;}
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHDefExtractIcon(string file,int index,uint flags,out nint large,out nint small,uint size);
    internal static ImageSource? ResourceIcon(string file,int index,int pixels)
    {
        if(string.IsNullOrWhiteSpace(file)||!File.Exists(file))return null;
        int requested=Math.Clamp(pixels,16,256);
        var sizes=new[]{IconResources.ExtractionSize(file,index,requested),requested,256,128,64,48,32}.Distinct().ToArray();
        var indices=index!=0?new[]{index,0}:new[]{0};
        foreach(int idx in indices)
        {
            foreach(int size in sizes)
            {
                nint large=0,small=0;
                try
                {
                    if(SHDefExtractIcon(file,idx,0,out large,out small,(uint)size)>=0)
                    {
                        var bmp=HIconToBitmap(large)??HIconToBitmap(small);
                        if(bmp!=null)return bmp;
                    }
                }
                catch{}
                finally{if(large!=0)DestroyIcon(large);if(small!=0)DestroyIcon(small);}
            }
        }
        var ext=Path.GetExtension(file).ToLowerInvariant();
        if(ext is ".ico" or ".png" or ".bmp")
        {
            try
            {
                using var stream=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                var frame=decoder.Frames.Where(HasVisiblePixels).OrderByDescending(f=>f.PixelWidth>=requested).ThenBy(f=>Math.Abs(f.PixelWidth-requested)).FirstOrDefault();
                if(frame!=null){frame.Freeze();return frame;}
            }
            catch{}
        }
        return null;
    }
    internal static string? ShellDisplayName(string path)
    {
        IShellItem? item=null;nint name=0;
        try{var iid=typeof(IShellItem).GUID;Shell.SHCreateItemFromParsingName(path,0,ref iid,out item);item.GetDisplayName(0,out name);return Marshal.PtrToStringUni(name);}
        catch{return null;}
        finally{if(name!=0)Marshal.FreeCoTaskMem(name);if(item!=null)Marshal.ReleaseComObject(item);}
    }
    static ImageSource? FromShellItem(string path,int pixels)
    {
        object? item=null;
        try
        {
            var iid=typeof(IShellItemImageFactory).GUID;
            try { SHCreateItemFromParsingName(path,0,ref iid,out item); }
            catch when (path.StartsWith("::",StringComparison.Ordinal)) { SHCreateItemFromParsingName("shell:"+path,0,ref iid,out item); }
            if(item is not IShellItemImageFactory factory) return null;
            // ICONONLY keeps shortcuts/document glyphs; BIGGERSIZEOK lets the shell hand back a
            // larger bitmap so WPF downscales instead of magnifying a 32 px resource.
            int clamped=Math.Clamp(pixels,32,256);
            foreach(int flags in new[]{0x04|0x01,0x04,0x00})
            {
                try
                {
                    factory.GetImage(new SizeI { cx=clamped,cy=clamped },flags,out var hbm);
                    if(hbm!=0)
                    {
                        try { var img=FromHBitmap(hbm); if(img!=null) return img; }
                        finally { DeleteObject(hbm); }
                    }
                }
                catch{}
            }
            foreach(int size in new[]{128,64,48,32}.Where(s=>s!=clamped))
            {
                foreach(int flags in new[]{0x04|0x01,0x00})
                {
                    try
                    {
                        factory.GetImage(new SizeI { cx=size,cy=size },flags,out var hbm);
                        if(hbm!=0)
                        {
                            try { var img=FromHBitmap(hbm); if(img!=null) return img; }
                            finally { DeleteObject(hbm); }
                        }
                    }
                    catch{}
                }
            }
            return null;
        }
        catch { return null; }
        finally { if(item!=null) Marshal.ReleaseComObject(item); }
    }
    static ImageSource? FromHBitmap(nint hbm)
    {
        if(GetObject(hbm,Marshal.SizeOf<Bitmap>(),out var bitmap)==0 || bitmap.Width<=0 || bitmap.Height<=0) return null;
        var width=bitmap.Width;var height=bitmap.Height;
        var info=new BitmapInfo { Header=new BitmapInfoHeader { Size=Marshal.SizeOf<BitmapInfoHeader>(),Width=width,Height=-height,Planes=1,BitCount=32,Compression=0 } };
        var buffer=new byte[width*height*4];
        var dc=CreateCompatibleDC(0);
        try { if(GetDIBits(dc,hbm,0,(uint)height,buffer,ref info,0)==0) return null; }
        finally { DeleteDC(dc); }
        bool visible=false,hasRgb=false;
        for(int i=0;i<buffer.Length;i+=4)
        {
            if(buffer[i+3]>8){visible=true;break;}
            if(buffer[i]!=0||buffer[i+1]!=0||buffer[i+2]!=0)hasRgb=true;
        }
        if(!visible)
        {
            if(!hasRgb)return null;
            try
            {
                var fallback=Imaging.CreateBitmapSourceFromHBitmap(hbm,0,System.Windows.Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
                if(HasVisiblePixels(fallback)){fallback.Freeze();return fallback;}
            }
            catch{}
            for(int i=0;i<buffer.Length;i+=4)
                if(buffer[i]!=0||buffer[i+1]!=0||buffer[i+2]!=0)buffer[i+3]=255;
            var opaque=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,buffer,width*4);
            opaque.Freeze();return opaque;
        }
        // Shell images are 32 bpp premultiplied BGRA.
        var source=BitmapSource.Create(width,height,96,96,PixelFormats.Pbgra32,null,buffer,width*4);
        source.Freeze();return source;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern nint SHGetFileInfo(string path,uint attr,out FileInfo info,uint size,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    internal static void DestroyTrayHandle(nint handle) => DestroyIcon(handle);
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileInfo { public nint Icon; public int Index; public uint Attr; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Name; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string Type; }
    static ImageSource? FromFileInfo(string path)
    {
        FileInfo f=default;
        bool ok=SHGetFileInfo(path,0,out f,(uint)Marshal.SizeOf<FileInfo>(),0x100)!=0&&f.Icon!=0;
        if(!ok&&path.StartsWith("::",StringComparison.Ordinal))
        {
            var pidl=TryResolvePidl(path);
            if(pidl!=0)
            {
                try{ok=SHGetFileInfoPidl(pidl,0,out f,(uint)Marshal.SizeOf<FileInfo>(),0x108)!=0&&f.Icon!=0;}
                finally{Marshal.FreeCoTaskMem(pidl);}
            }
        }
        if(!ok||f.Icon==0)return null;
        try { return HIconToBitmap(f.Icon); }
        catch { return null; }
        finally { DestroyIcon(f.Icon); }
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct StockIconInfo { public uint Size; public nint Icon; public int SysImageIndex; public int IconIndex; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Path; }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHGetStockIconInfo(uint siid,uint flags,ref StockIconInfo info);
    internal static ImageSource? StockIcon(string path,int pixels)
    {
        uint? siid=null;
        if(path.StartsWith("::",StringComparison.Ordinal))
        {
            if(path.Equals("::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}",StringComparison.OrdinalIgnoreCase))
            {
                var cp=ResourceIcon(Path.Combine(Environment.SystemDirectory,"imageres.dll"),-27,pixels);
                if(cp!=null)return cp;
                siid=2;
            }
            else if(path.Equals("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",StringComparison.OrdinalIgnoreCase))siid=94;
            else if(path.Equals("::{645FF040-5081-101B-9F08-00AA002F954E}",StringComparison.OrdinalIgnoreCase))siid=31;
            else if(path.Equals("::{59031A47-3F72-44A7-89C5-5595FE6B30EE}",StringComparison.OrdinalIgnoreCase))siid=3;
            else if(path.Equals("::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}",StringComparison.OrdinalIgnoreCase))siid=17;
            else siid=2;
        }
        else if(Directory.Exists(path))siid=3;
        else if(File.Exists(path))
        {
            var ext=Path.GetExtension(path).ToLowerInvariant();
            siid=ext is ".exe" or ".lnk" or ".url" or ".appref-ms"?2u:0u;
        }
        if(siid==null)return null;
        foreach(uint id in siid==94?new[]{94u,15u}:new[]{siid.Value})
        {
            var info=new StockIconInfo{Size=(uint)Marshal.SizeOf<StockIconInfo>(),Path=""};
            if(SHGetStockIconInfo(id,0,ref info)>=0&&!string.IsNullOrWhiteSpace(info.Path))
            {
                var img=ResourceIcon(info.Path,info.IconIndex,pixels);
                if(img!=null)return img;
            }
            info=new StockIconInfo{Size=(uint)Marshal.SizeOf<StockIconInfo>(),Path=""};
            if(SHGetStockIconInfo(id,0x100,ref info)>=0&&info.Icon!=0)
            {
                try{var img=HIconToBitmap(info.Icon);if(img!=null)return img;}
                finally{DestroyIcon(info.Icon);}
            }
        }
        return null;
    }
    public static string Class(nint hwnd) { var s=new StringBuilder(256);GetClassName(hwnd,s,256);return s.ToString(); }
    public static List<(nint Handle,string Title)> Windows(string path)
    {
        var list = new List<(nint,string)>();
        if (!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)) return list;
        EnumWindows((h,_) =>
        {
            if(!IsApplicationWindow(h))return true;
            var t=new StringBuilder(512);GetWindowText(h,t,512); if(t.Length==0) return true;
            GetWindowThreadProcessId(h,out var pid);
            try { using var p=Process.GetProcessById((int)pid); if(string.Equals(p.MainModule?.FileName,path,StringComparison.OrdinalIgnoreCase)) list.Add((h,t.ToString())); } catch { }
            return true;
        },0);
        return list;
    }
    static bool IsApplicationWindow(nint window) =>
        IsWindowVisible(window)&&(GetWindowLongPtr(window,-20).ToInt64()&0x80)==0&&
        (DwmGetWindowAttribute(window,14,out int cloaked,4)!=0||cloaked==0)&&
        Class(window) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");

    internal static HashSet<uint> ApplicationWindowProcessIds()
    {
        var ids=new HashSet<uint>();
        EnumWindows((window,_)=>{
            if(IsApplicationWindow(window)){GetWindowThreadProcessId(window,out uint pid);ids.Add(pid);}
            return true;
        },0);
        return ids;
    }

    public static bool Fullscreen(System.Windows.Forms.Screen screen)
    {
        var h=GetForegroundWindow(); if(h==0 || Class(h) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        GetWindowThreadProcessId(h,out var pid); if(pid==Environment.ProcessId) return false;
        if(!IsWindowVisible(h)||IsIconic(h)||!GetWindowRect(h,out var r)) return false;
        var b=MonitorArea(screen,false);
        if(!(r.Left<=b.Left && r.Top<=b.Top && r.Right>=b.Right && r.Bottom>=b.Bottom))return false;
        // A taskbar HWND normally remains "visible" underneath borderless games. Ask
        // whether the real shell bar is actually exposed and receives input instead.
        // No game-process list or background-window heuristic can latch suppression.
        return !TaskbarInteractive(screen);
    }
    public static bool HasMaximizedWindow(System.Windows.Forms.Screen screen)
    {
        var fg=GetForegroundWindow();
        if(fg!=0&&Class(fg) is "Progman" or "WorkerW")return false;
        var bounds=MonitorArea(screen,false);
        var work=MonitorArea(screen,true);
        if(work.Width<=0||work.Height<=0)return false;
        bool found=false;
        EnumWindows((h,_)=>
        {
            if(!IsWindowVisible(h)||IsIconic(h))return true;
            if(DwmGetWindowAttribute(h,14,out int cloaked,4)==0&&cloaked!=0)return true;
            string cls=Class(h);
            if(cls is "Progman" or "WorkerW"&&FindWindowEx(h,0,"SHELLDLL_DefView",null)!=0){found=false;return false;}
            if(cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")return true;
            GetWindowThreadProcessId(h,out var pid);
            if(pid==Environment.ProcessId)return true;
            long ex=GetWindowLongPtr(h,-20).ToInt64();
            long style=GetWindowLongPtr(h,-16).ToInt64();
            if((ex&0x08000080L)!=0||(style&0x40000000L)!=0)return true;
            bool zoomed=IsZoomed(h)||(style&0x01000000L)!=0;
            if(!zoomed||!GetWindowRect(h,out var r))return true;
            int cx=(r.Left+r.Right)/2,cy=(r.Top+r.Bottom)/2;
            bool onScreen=cx>=bounds.Left&&cx<bounds.Right&&cy>=bounds.Top&&cy<bounds.Bottom;
            bool coversWork=(r.Right-r.Left)>=work.Width*0.8&&(r.Bottom-r.Top)>=work.Height*0.8;
            if(onScreen&&coversWork){found=true;return false;}
            return true;
        },0);
        return found;
    }
    internal static bool ForegroundTextEditing()
    {
        var fg=GetForegroundWindow();if(fg==0)return false;
        uint tid=GetWindowThreadProcessId(fg,out _);if(tid==0)return false;
        var info=new GUITHREADINFO{cbSize=Marshal.SizeOf<GUITHREADINFO>()};
        if(!GetGUIThreadInfo(tid,ref info)||info.hwndFocus==0)return false;
        var cls=Class(info.hwndFocus);
        return cls.Equals("Edit",StringComparison.OrdinalIgnoreCase)||cls.StartsWith("RichEdit",StringComparison.OrdinalIgnoreCase);
    }
    // Keys with ordinary make/break events. Escape stays reserved for cancelling
    // recording. Modifier, IME, system/media and special scan sequences are excluded.
    internal static bool IsWakeBindingKey(int key) => key is
        0x08 or 0x09 or 0x0D or >=0x20 and <=0x28 or 0x2D or 0x2E or
        >=0x30 and <=0x39 or >=0x41 and <=0x5A or >=0x60 and <=0x87 or
        0x91 or >=0xBA and <=0xC0 or >=0xDB and <=0xDF or 0xE2;
    internal static bool KeyHeld(int vk)
    {
        if(vk<=0)return false;
        if((GetAsyncKeyState(vk)&0x8000)!=0)return true;
        return vk switch
        {
            0x10=>(GetAsyncKeyState(0xA0)&0x8000)!=0||(GetAsyncKeyState(0xA1)&0x8000)!=0,
            0x11=>(GetAsyncKeyState(0xA2)&0x8000)!=0||(GetAsyncKeyState(0xA3)&0x8000)!=0,
            0x12=>(GetAsyncKeyState(0xA4)&0x8000)!=0||(GetAsyncKeyState(0xA5)&0x8000)!=0,
            _=>false
        };
    }
    internal static string KeyName(int vk)
    {
        if(vk<=0)return "未设置快捷键";
        return vk switch
        {
            0x10 or 0xA0 or 0xA1=>"Shift",
            0x11 or 0xA2 or 0xA3=>"Ctrl",
            0x12 or 0xA4 or 0xA5=>"Alt",
            0x5B or 0x5C=>"Win",
            0x14=>"CapsLock",
            0x20=>"Space",
            0x91=>"Scroll Lock",
            0xC0=>"~",
            _=>System.Windows.Input.KeyInterop.KeyFromVirtualKey(vk) is var key&&key!=System.Windows.Input.Key.None?key.ToString():$"VK_{vk:X2}"
        };
    }
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd,uint flags);
    [DllImport("user32.dll")] static extern nint GetShellWindow();
    [DllImport("user32.dll")] static extern nint GetWindow(nint hwnd,uint command);

    static bool TaskbarClass(string name)=>name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    const string TrayOverflowClass = "TopLevelWindowForOverflowXamlIsland";
    const string ShellPopupClass = "Windows.UI.Core.CoreWindow";

    static uint ShellProcess()
    {
        var shell=GetShellWindow();
        if(shell==0)return 0;
        GetWindowThreadProcessId(shell,out var process);
        return process;
    }

    static bool IsTaskbarPopup(nint window,string name,uint shell)
    {
        if(name is not ("XamlExplorerHostIslandWindow" or "Xaml_WindowedPopupClass" or "TaskListThumbnailWnd" or
            TrayOverflowClass or "#32768" or ShellPopupClass))return false;
        GetWindowThreadProcessId(window,out var process);
        if(process==0)return false;
        if(name==ShellPopupClass)
        {
            try
            {
                using var host=Process.GetProcessById((int)process);
                var path=host.MainModule?.FileName;
                return path!=null&&path.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"SystemApps","ShellExperienceHost_"),StringComparison.OrdinalIgnoreCase)&&
                    Path.GetFileName(path).Equals("ShellExperienceHost.exe",StringComparison.OrdinalIgnoreCase);
            }
            catch(System.ComponentModel.Win32Exception){return false;}
            catch(ArgumentException){return false;}
            catch(InvalidOperationException){return false;}
        }
        if(process!=shell)return false;
        return name!="Xaml_WindowedPopupClass"||TaskbarClass(Class(GetWindow(window,4)));
    }

    internal static bool TaskbarInputAt(Point point)
    {
        var hit=WindowFromPoint(point);
        if(hit==0)return false;
        var root=GetAncestor(hit,2);
        if(root==0)root=hit;
        var name=Class(root);
        return TaskbarClass(name)||IsTaskbarPopup(root,name,ShellProcess());
    }

    internal static bool TaskbarPopupVisible(System.Drawing.Rectangle? workArea=null)
    {
        uint shell=ShellProcess();
        if(shell==0)return false;
        // ShellExperienceHost keeps the jump list's foreground proxy outside EnumWindows.
        var foreground=GetForegroundWindow();
        if(Class(foreground)==ShellPopupClass&&IsTaskbarPopup(foreground,ShellPopupClass,shell))return true;
        bool found=false;
        EnumWindows((window,_)=>
        {
            if(!IsWindowVisible(window))return true;
            var name=Class(window);
            // Overflow still owns input on its own pixels, but does not block wake elsewhere.
            if(name==TrayOverflowClass||!IsTaskbarPopup(window,name,shell)||!GetWindowRect(window,out var box))return true;
            if(DwmGetWindowAttribute(window,14,out var cloaked,4)==0&&cloaked!=0)return true;
            var bounds=System.Drawing.Rectangle.FromLTRB(box.Left,box.Top,box.Right,box.Bottom);
            if(bounds.Width<8||bounds.Height<8||workArea is { } area&&!area.IntersectsWith(bounds))return true;
            found=true;return false;
        },0);
        return found;
    }
    internal static bool TaskbarInteractive(System.Windows.Forms.Screen screen)
    {
        bool usable=false;var bounds=MonitorArea(screen,false);
        EnumWindows((h,_)=>{
            if(Class(h) is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")||!IsWindowVisible(h)||!GetWindowRect(h,out var r))return true;
            int l=Math.Max(bounds.Left,r.Left),t=Math.Max(bounds.Top,r.Top),right=Math.Min(bounds.Right,r.Right),bottom=Math.Min(bounds.Bottom,r.Bottom);
            // An auto-hide activation sliver is not an exposed bar. Also supports side bars.
            if(right-l<12||bottom-t<12)return true;
            foreach(double f in new[]{.2,.5,.8}){var hit=WindowFromPoint(new Point(l+(int)((right-l)*f),t+(bottom-t)/2));if(hit==h||GetAncestor(hit,2)==h){usable=true;return false;}}
            return true;
        },0);return usable;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct DEVMODEW
    {
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string dmDeviceName;
        public short dmSpecVersion,dmDriverVersion,dmSize,dmDriverExtra;
        public int dmFields,dmPositionX,dmPositionY,dmDisplayOrientation,dmDisplayFixedOutput;
        public short dmColor,dmDuplex,dmYResolution,dmTTOption,dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel,dmPelsWidth,dmPelsHeight,dmDisplayFlags,dmDisplayFrequency;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettingsW(string? deviceName,int modeNum,ref DEVMODEW devMode);
    internal static int GetDisplayRefreshRate(string? deviceName)
    {
        var dm=new DEVMODEW{dmSize=(short)Marshal.SizeOf<DEVMODEW>()};
        return EnumDisplaySettingsW(string.IsNullOrWhiteSpace(deviceName)?null:deviceName,-1,ref dm)&&dm.dmDisplayFrequency>0?dm.dmDisplayFrequency:60;
    }
}

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(nint ctx,ref Guid bhid,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out object result);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint kind,out nint name);
    void GetAttributes(uint mask,out uint attrs);
    void Compare(IShellItem item,uint hint,out int order);
}
[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellDropTarget
{
    [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] ComData data,uint keys,Native.Point point,ref uint effect);
    [PreserveSig] int DragOver(uint keys,Native.Point point,ref uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] ComData data,uint keys,Native.Point point,ref uint effect);
}
[ComImport,Guid("4657278A-411B-11d2-839A-00C04FD918D0")]
internal class DragDropHelper { }
[ComImport,Guid("4657278B-411B-11d2-839A-00C04FD918D0"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDropTargetHelper
{
    [PreserveSig] int DragEnter(nint hwndTarget,[MarshalAs(UnmanagedType.Interface)] ComData dataObject,ref Native.Point pt,uint effect);
    [PreserveSig] int DragLeave();
    [PreserveSig] int DragOver(ref Native.Point pt,uint effect);
    [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] ComData dataObject,ref Native.Point pt,uint effect);
    [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool show);
}
internal static partial class Native
{
    internal static class DropPreviewHelper
    {
        [ThreadStatic] static IDropTargetHelper? instance;
        [ThreadStatic] static nint activeHwnd;
        [ThreadStatic] static object? activeData;
        internal static bool IsActive => activeHwnd != 0;
        internal static int DragEnterCount { get; private set; }
        internal static System.Windows.DragDropEffects LastEnterEffects { get; private set; }
        static IDropTargetHelper? Instance => instance ??= Create();
        static IDropTargetHelper? Create()
        {
            try { return (IDropTargetHelper)new DragDropHelper(); }
            catch { return null; }
        }
        internal static void DragEnter(nint hwnd,System.Windows.DragEventArgs e)
        {
            if(hwnd==0||e.Data is not ComData com)return;
            try
            {
                if(activeHwnd!=0&&(activeHwnd!=hwnd||!ReferenceEquals(activeData,e.Data)))
                {
                    try{Instance?.DragLeave();}catch{}
                }
                activeHwnd=hwnd;
                activeData=e.Data;
                DragEnterCount++;
                LastEnterEffects=e.Effects;
                GetCursorPos(out var pt);
                var helper=Instance;
                if(helper==null)return;
                helper.DragEnter(hwnd,com,ref pt,(uint)e.Effects);
                helper.Show(true);
                EnsureHostBelowDragImage(hwnd);
            }
            catch{}
        }
        internal static void DragOver(nint hwnd,System.Windows.DragEventArgs e)
        {
            if(hwnd!=0&&(activeHwnd!=hwnd||!ReferenceEquals(activeData,e.Data)))
            {
                DragEnter(hwnd,e);
                return;
            }
            try
            {
                GetCursorPos(out var pt);
                var helper=Instance;
                if(helper==null)return;
                helper.DragOver(ref pt,(uint)e.Effects);
                helper.Show(true);
                EnsureHostBelowDragImage(hwnd);
            }
            catch{}
        }
        internal static void DragLeave()
        {
            activeHwnd=0;
            activeData=null;
            try{Instance?.DragLeave();}
            catch{}
        }
        internal static void Drop(System.Windows.DragEventArgs e)
        {
            activeHwnd=0;
            activeData=null;
            if(e.Data is not ComData com)return;
            try{GetCursorPos(out var pt);Instance?.Drop(com,ref pt,(uint)e.Effects);}
            catch{}
        }
        internal static void AttachWindow(GlassWindow window,Action? onLeave=null)
        {
            int epoch=0;
            window.AddHandler(System.Windows.DragDrop.DragEnterEvent,new System.Windows.DragEventHandler((_,a)=>{epoch++;DragEnter(window.Handle,a);}),true);
            window.AddHandler(System.Windows.DragDrop.DragOverEvent,new System.Windows.DragEventHandler((_,a)=>{epoch++;DragOver(window.Handle,a);}),true);
            window.AddHandler(System.Windows.DragDrop.DragLeaveEvent,new System.Windows.DragEventHandler((_,_)=>
            {
                int leaveEpoch=epoch;
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input,new Action(()=>
                {
                    if(leaveEpoch!=epoch)return;
                    bool buttonsHeld=((GetAsyncKeyState(1)|GetAsyncKeyState(2))&0x8000)!=0;
                    if(buttonsHeld&&GetCursorPos(out var p)&&window.Contains(p))return;
                    DragLeave();
                    onLeave?.Invoke();
                }));
            }),true);
            window.AddHandler(System.Windows.DragDrop.DropEvent,new System.Windows.DragEventHandler((_,a)=>{epoch++;Drop(a);}),true);
        }
        static void EnsureHostBelowDragImage(nint hwnd)
        {
            if(hwnd==0)return;
            nint di=FindActiveSysDragImage();
            if(di!=0&&di!=hwnd&&IsWindowAbove(hwnd,di))
                DeferWindowPosAsync(hwnd,di,0,0,0,0,0x0003); // SWP_NOMOVE | SWP_NOSIZE
        }
    }
}
internal sealed class ShellDrop : IDisposable
{
    static readonly HashSet<string> ExecutableExts = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".bat", ".cmd", ".com", ".ps1" };
    IShellDropTarget? target;
    ComData? data;
    readonly string[] droppedFiles;
    readonly string? execTarget;
    readonly string? execFixedArgs;
    internal static bool IsExecutableTarget(string path) => ResolveExecutableTarget(path, out _) != null;
    static string? ResolveExecutableTarget(string path, out string? fixedArgs)
    {
        fixedArgs = null;
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("::", StringComparison.Ordinal)) return null;
        string candidate = path;
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var link = IconResources.ReadLink(path);
            if (string.IsNullOrWhiteSpace(link.Target)) return null;
            candidate = link.Target;
            fixedArgs = string.IsNullOrWhiteSpace(link.Arguments) ? null : link.Arguments;
        }
        if (!File.Exists(candidate)) return null;
        return ExecutableExts.Contains(Path.GetExtension(candidate)) ? candidate : null;
    }
    static string QuoteArg(string arg) => "\"" + arg.Replace("\"", "\\\"") + "\"";
    public ShellDrop(string path,string[] files)
    {
        droppedFiles = files ?? Array.Empty<string>();
        execTarget = ResolveExecutableTarget(path, out execFixedArgs);
        IShellItem? item=null;
        try
        {
            var iid=typeof(IShellItem).GUID; Shell.SHCreateItemFromParsingName(path,0,ref iid,out item);
            var bhid=new Guid("3981e225-f559-11d3-8e3a-00c04f6837d5"); var dt=typeof(IShellDropTarget).GUID;
            item.BindToHandler(0,ref bhid,ref dt,out var obj); target=(IShellDropTarget)obj;
            var fd=new System.Windows.Forms.DataObject();
            var paths=new System.Collections.Specialized.StringCollection(); paths.AddRange(droppedFiles); fd.SetFileDropList(paths);
            data=(ComData)fd;
        }
        catch { target=null; data=null; }
        finally { if(item!=null) Marshal.ReleaseComObject(item); }
    }
    uint ExecEffect(uint allowed)
    {
        if (execTarget == null || droppedFiles.Length == 0) return 0;
        if ((allowed & 1u) != 0) return 1u; // DROPEFFECT_COPY
        if ((allowed & 4u) != 0) return 4u; // DROPEFFECT_LINK
        return (allowed & 2u) != 0 ? 2u : 1u;
    }
    public uint Enter(uint keys,Native.Point pt,uint allowed)
    {
        if(target!=null && data!=null){ uint eff=allowed; if(target.DragEnter(data,keys,pt,ref eff)>=0 && eff!=0) return eff; }
        return ExecEffect(allowed);
    }
    public uint Over(uint keys,Native.Point pt,uint allowed)
    {
        if(target!=null){ uint eff=allowed; if(target.DragOver(keys,pt,ref eff)>=0 && eff!=0) return eff; }
        return ExecEffect(allowed);
    }
    public uint Drop(uint keys,Native.Point pt,uint allowed)
    {
        if(target!=null && data!=null)
        {
            uint eff=allowed;
            if(target.Drop(data,keys,pt,ref eff)>=0 && eff!=0) return eff;
        }
        if(execTarget!=null && droppedFiles.Length>0)
        {
            var fileArgs = string.Join(" ", droppedFiles.Select(QuoteArg));
            var fullArgs = string.IsNullOrWhiteSpace(execFixedArgs) ? fileArgs : $"{execFixedArgs} {fileArgs}";
            if (Path.GetExtension(execTarget).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuoteArg(execTarget)} {fileArgs}",
                    WorkingDirectory = Path.GetDirectoryName(execTarget) ?? "",
                    UseShellExecute = true
                });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(execTarget)
                {
                    Arguments = fullArgs,
                    WorkingDirectory = Path.GetDirectoryName(execTarget) ?? "",
                    UseShellExecute = true
                });
            }
            return ExecEffect(allowed);
        }
        return 0;
    }
    public void Dispose() { if(target!=null) { try { target.DragLeave(); } catch { } Marshal.ReleaseComObject(target); target=null; } data=null; }
}
internal static class Shell
{
    internal static bool CanMatchApplication(Entry entry)=>!entry.IsVirtual&&
        (!entry.Path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)||
         string.IsNullOrWhiteSpace(IconResources.ReadLink(entry.Path).Arguments));
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=false)] internal static extern void SHCreateItemFromParsingName(string path,nint ctx,ref Guid iid,out IShellItem item);
    public static string Resolve(string path)
    {
        if(!path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)) return path;
        var target=IconResources.ReadLink(path).Target;return string.IsNullOrWhiteSpace(target)?path:target;
    }
    public static Process? Launch(Entry e) =>
        Process.Start(new ProcessStartInfo(e.IsVirtual?e.ShellVerb??"explorer.exe":e.Path){UseShellExecute=true});
}
