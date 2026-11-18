using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using ComData = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace MacDock;

// File work is delegated to the Windows shell itself: IFileOperation for copy/move/rename/delete
// (recycle bin, conflict prompts, elevation and progress come from Explorer), the OLE clipboard
// for cut/copy/paste, the shell property sheet, and the archive tool that ships with Windows.
internal static class ShellOps
{
    // ---------------------------------------------------------------- IFileOperation
    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation
    {
        [PreserveSig] int Advise(nint sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(nint dialog);
        [PreserveSig] int SetProperties(nint properties);
        [PreserveSig] int SetOwnerWindow(nint hwnd);
        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);
        [PreserveSig] int ApplyPropertiesToItems(nint items);
        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, nint sink);
        [PreserveSig] int RenameItems(nint items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        [PreserveSig] int MoveItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, nint sink);
        [PreserveSig] int MoveItems(nint items, IShellItem destination);
        [PreserveSig] int CopyItem(IShellItem item, IShellItem destination, [MarshalAs(UnmanagedType.LPWStr)] string? newName, nint sink);
        [PreserveSig] int CopyItems(nint items, IShellItem destination);
        [PreserveSig] int DeleteItem(IShellItem item, nint sink);
        [PreserveSig] int DeleteItems(nint items);
        [PreserveSig] int NewItem(IShellItem destination, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, nint sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted(out int aborted);
    }

    const uint FOF_ALLOWUNDO = 0x0040;
    const uint FOF_RENAMEONCOLLISION = 0x0008;
    const uint FOF_SILENT = 0x0004;
    const uint FOF_NOERRORUI = 0x0400;
    const uint FOF_WANTNUKEWARNING = 0x4000;
    const uint FOFX_RECYCLEONDELETE = 0x00080000;
    const uint FOFX_ADDUNDORECORD = 0x20000000;

    static IShellItem Item(string path)
    {
        var iid = typeof(IShellItem).GUID;
        Shell.SHCreateItemFromParsingName(path, 0, ref iid, out var item);
        return item;
    }
    static void WithItem(string path,Action<IShellItem> action) { var item=Item(path);try{action(item);}finally{Marshal.ReleaseComObject(item);} }

    [ThreadStatic] internal static int LastHResult, LastAborted;

    internal static Task<T> RunSta<T>(Func<T> operation)
    {
        var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new System.Threading.Thread(()=>
        {
            try { completion.SetResult(operation()); }
            catch(Exception error) { completion.SetException(error); }
        }) { IsBackground=true,Name="MacDock Shell file operation" };
        worker.SetApartmentState(System.Threading.ApartmentState.STA);
        worker.Start();
        return completion.Task;
    }

    static bool Run(nint owner, uint flags, Action<IFileOperation> build, bool quiet = false)
    {
        LastHResult = -1; LastAborted = 0;
        var type = Type.GetTypeFromCLSID(new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"))!;
        var op = (IFileOperation)Activator.CreateInstance(type)!;
        try
        {
            Marshal.ThrowExceptionForHR(op.SetOperationFlags(flags | (quiet ? FOF_SILENT | FOF_NOERRORUI : 0)));
            if (owner != 0) Marshal.ThrowExceptionForHR(op.SetOwnerWindow(owner));
            build(op);
            var hr = op.PerformOperations();
            op.GetAnyOperationsAborted(out var aborted);
            LastHResult = hr; LastAborted = aborted;
            return hr >= 0 && aborted == 0;
        }
        catch (Exception e) { Program.Log(e); return false; }
        finally { Marshal.ReleaseComObject(op); }
    }

    /// <summary>
    /// Shell copy. RENAMEONCOLLISION is what Explorer's own paste does: pasting into the same
    /// folder creates "name - copy" instead of raising a blocking replace dialog.
    /// </summary>
    public static bool Copy(IReadOnlyList<string> paths, string destination, nint owner, bool quiet = false)
    {
        if (paths.Count == 0 || !Directory.Exists(destination)) return false;
        return Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOF_RENAMEONCOLLISION, op =>
        {
            var target = Item(destination);
            try { foreach (var p in paths) WithItem(p,item=>op.CopyItem(item, target, null, 0)); }
            finally { Marshal.ReleaseComObject(target); }
        }, quiet);
    }

    /// <summary>Shell move (same volume = rename, cross volume = copy + delete, native prompts).</summary>
    public static bool Move(IReadOnlyList<string> paths, string destination, nint owner, bool quiet = false, bool renameOnCollision = false)
    {
        if (paths.Count == 0 || !Directory.Exists(destination)) return false;
        return Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | (renameOnCollision?FOF_RENAMEONCOLLISION:0), op =>
        {
            var target = Item(destination);
            try { foreach (var p in paths) WithItem(p,item=>op.MoveItem(item, target, null, 0)); }
            finally { Marshal.ReleaseComObject(target); }
        }, quiet);
    }

    /// <summary>Recycle (default) or permanent delete. Permanent deletes keep the shell confirmation.</summary>
    public static bool Delete(IReadOnlyList<string> paths, bool permanent, nint owner, bool quiet = false)
    {
        if (paths.Count == 0) return false;
        var flags = permanent ? FOF_WANTNUKEWARNING : FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOF_WANTNUKEWARNING;
        var ok = Run(owner, flags, op => { foreach (var p in paths) WithItem(p,item=>Marshal.ThrowExceptionForHR(op.DeleteItem(item, 0))); }, quiet);
        // The recycle-bin path can report "aborted" while the items are already gone; trust the
        // filesystem for the final answer instead of a flag that also covers suppressed prompts.
        if (!ok && LastHResult >= 0) ok = paths.All(p => !File.Exists(p) && !Directory.Exists(p));
        return ok;
    }

    public static bool Rename(string path, string newName, nint owner, bool quiet = false)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return false;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException("名称包含无效字符");
        if (!File.Exists(path) && !Directory.Exists(path)) return false;
        if (string.Equals(Path.GetFileName(path), newName, StringComparison.Ordinal)) return false;
        return Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD, op => WithItem(path,item=>op.RenameItem(item, newName, 0)), quiet);
    }

    // ---------------------------------------------------------------- properties / open with
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern bool SHObjectProperties(nint hwnd, uint type, string name, string? page);
    [DllImport("shell32.dll", PreserveSig = true)]
    static extern int SHMultiFileProperties([MarshalAs(UnmanagedType.Interface)] ComData data, uint flags);
    const uint SHOP_FILEPATH = 2;

    /// <summary>The real Explorer property sheet, including custom tabs installed by other apps.</summary>
    public static bool Properties(nint owner, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return false;
        if (paths.Count == 1) return SHObjectProperties(owner, SHOP_FILEPATH, paths[0], null);
        var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
        return SHMultiFileProperties((ComData)data, 0) >= 0;
    }

    public static bool OpenWith(string path)
    {
        if (!File.Exists(path)) return false;
        Process.Start(new ProcessStartInfo(path) { Verb = "openas", UseShellExecute = true });
        return true;
    }

    // ---------------------------------------------------------------- clipboard
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(nint owner);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)] static extern nint SetClipboardData(uint format, nint memory);
    [DllImport("user32.dll", SetLastError = true)] static extern bool CloseClipboard();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern uint RegisterClipboardFormat(string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll", SetLastError = true)] static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalUnlock(nint memory);

    const uint CF_HDROP = 15;
    const uint GMEM_MOVEABLE = 0x0002, GMEM_ZEROINIT = 0x0040;

    [StructLayout(LayoutKind.Sequential)] struct DropFiles { public uint pFiles; public int x, y; public int fNc; public int fWide; }

    static nint Alloc(nint source, int bytes)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (nuint)bytes);
        if (handle == 0) return 0;
        var pointer = GlobalLock(handle);
        if (pointer == 0) return 0;
        try { if (source != 0) { var buffer = new byte[bytes]; Marshal.Copy(source, buffer, 0, bytes); Marshal.Copy(buffer, 0, pointer, bytes); } }
        finally { GlobalUnlock(handle); }
        return handle;
    }

    /// <summary>Puts real files on the OLE clipboard exactly like Explorer does (CF_HDROP + effect).</summary>
    public static bool SetFileClipboard(IReadOnlyList<string> paths, bool move)
    {
        if (paths.Count == 0) return false;
        var text = new StringBuilder();
        foreach (var p in paths) text.Append(p).Append('\0');
        text.Append('\0');
        var payload = Encoding.Unicode.GetBytes(text.ToString());
        var header = Marshal.SizeOf<DropFiles>();
        var drop = Marshal.AllocHGlobal(header + payload.Length);
        nint dropHandle = 0, effectHandle = 0;
        try
        {
            Marshal.StructureToPtr(new DropFiles { pFiles = (uint)header, fWide = 1 }, drop, false);
            Marshal.Copy(payload, 0, drop + header, payload.Length);
            dropHandle = Alloc(drop, header + payload.Length);
            var effectMemory = Marshal.AllocHGlobal(4);
            try { Marshal.WriteInt32(effectMemory, move ? 2 : 1); effectHandle = Alloc(effectMemory, 4); }
            finally { Marshal.FreeHGlobal(effectMemory); }
            if (dropHandle == 0 || effectHandle == 0) return false;
            if (!OpenClipboard(0)) return false;
            try
            {
                EmptyClipboard();
                if (SetClipboardData(CF_HDROP, dropHandle) == 0) return false;
                dropHandle = 0; // ownership moved to the clipboard
                if (SetClipboardData(RegisterClipboardFormat("Preferred DropEffect"), effectHandle) == 0) return false;
                effectHandle = 0;
                return true;
            }
            finally { CloseClipboard(); }
        }
        catch (Exception e) { Program.Log(e); return false; }
        finally
        {
            Marshal.FreeHGlobal(drop);
            // Handles that were not accepted by the clipboard must still be released.
            if (dropHandle != 0) GlobalFree(dropHandle);
            if (effectHandle != 0) GlobalFree(effectHandle);
        }
    }

    [DllImport("kernel32.dll")] static extern nint GlobalFree(nint memory);

    public static (string[] Paths, bool Move) ReadFileClipboard()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList()) return (Array.Empty<string>(), false);
            var paths = Clipboard.GetFileDropList().Cast<string>().Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            var move = false;
            try
            {
                var data = Clipboard.GetDataObject();
                var raw = data?.GetData("Preferred DropEffect");
                var effect = 0;
                if (raw is MemoryStream stream)
                {
                    if (stream.CanSeek) stream.Position = 0;
                    var bytes = new byte[4]; var read = stream.Read(bytes, 0, 4);
                    if (read == 4) effect = BitConverter.ToInt32(bytes, 0);
                }
                else if (raw is byte[] buffer && buffer.Length >= 4) effect = BitConverter.ToInt32(buffer, 0);
                move = (effect & 2) != 0;
            }
            catch { }
            return (paths, move);
        }
        catch (Exception e) { Program.Log(e); return (Array.Empty<string>(), false); }
    }

    /// <summary>Data object for an OLE drag: real files for Explorer and apps, plus our reorder token.</summary>
    public static DataObject DragData(string? entryId, IReadOnlyList<string> paths, bool move,string origin="",bool reference=false)
    {
        var data = new DataObject();
        if (entryId != null) data.SetData("MacDock.Entry", entryId);
        if(origin.Length>0)data.SetData("MacDock.Origin",origin);
        if (paths.Count > 0)
        {
            data.SetData(DataFormats.FileDrop, paths.ToArray());
            // DROPEFFECT_COPY / MOVE. Streams are handed to OLE as raw bytes, which is exactly the
            // format the shell expects for CFSTR_PREFERREDDROPEFFECT.
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(reference?4:move?2:1)));
        }
        return data;
    }

    // ---------------------------------------------------------------- archives
    public static bool CanExtract(string path) => Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".tar" or ".gz" or ".tgz";

    static string UniqueFile(string directory, string name, string extension)
    {
        var candidate = Path.Combine(directory, name + extension);
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++) candidate = Path.Combine(directory, $"{name} ({n}){extension}");
        return candidate;
    }

    static string UniqueFolder(string directory, string name)
    {
        var candidate = Path.Combine(directory, name);
        for (var n = 2; File.Exists(candidate) || Directory.Exists(candidate); n++) candidate = Path.Combine(directory, $"{name} ({n})");
        return candidate;
    }

    static string StripArchiveExtension(string name)
    {
        var lower = name.ToLowerInvariant();
        foreach (var ext in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".zip", ".tar", ".gz" })
            if (lower.EndsWith(ext, StringComparison.Ordinal)) return name[..^ext.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    static string? ArchiveTool()
    {
        var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        return File.Exists(tar) ? tar : null;
    }

    static (int Code, string Error) RunTool(string exe, string arguments)
    {
        var info = new ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true
        };
        using var process = Process.Start(info);
        if (process == null) return (-1, "无法启动系统压缩组件");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error.Trim());
    }

    /// <summary>
    /// Compresses into a ZIP using the archive tool bundled with Windows (bsdtar). No archive
    /// engine is implemented here; installed archivers stay available through the shell menu.
    /// </summary>
    public static bool Compress(IReadOnlyList<string> paths, out string archive, out string error)
    {
        archive = ""; error = "";
        var items = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (items.Count == 0) { error = "没有可压缩的项目。"; return false; }
        var folder = Path.GetDirectoryName(items[0])!;
        var name = items.Count == 1 ? StripArchiveExtension(Path.GetFileName(items[0])) : "桌面归档";
        archive = UniqueFile(folder, name, ".zip");
        var tool = ArchiveTool();
        if (tool == null) { error = "系统缺少压缩组件，请使用“Windows 更多操作”。"; return false; }
        var args = new StringBuilder("-a -c -f ").Append(Quote(archive));
        foreach (var item in items)
        {
            var parent = Path.GetDirectoryName(item)!;
            var leaf = Path.GetFileName(item);
            if (leaf.StartsWith('-')) { error = "文件名以 - 开头，请使用“Windows 更多操作”。"; return false; }
            args.Append(" -C ").Append(Quote(parent)).Append(' ').Append(Quote(leaf));
        }
        var (code, message) = RunTool(tool, args.ToString());
        if (code == 0) return true;
        try { if (File.Exists(archive)) File.Delete(archive); } catch { }
        error = string.IsNullOrWhiteSpace(message) ? "压缩未完成。" : message;
        return false;
    }

    /// <summary>Extracts next to the archive into its own folder, the way Explorer's Extract All does.</summary>
    public static bool Extract(string archive, out string folder, out string error)
    {
        folder = ""; error = "";
        if (!File.Exists(archive)) { error = "找不到压缩文件。"; return false; }
        if (!CanExtract(archive)) { error = "该格式请使用“Windows 更多操作”中的解压命令。"; return false; }
        var parent = Path.GetDirectoryName(archive)!;
        var name = StripArchiveExtension(Path.GetFileName(archive));
        var target = UniqueFolder(parent, name);
        var tool = ArchiveTool();
        if (tool == null) { error = "系统缺少解压组件，请使用“Windows 更多操作”。"; return false; }
        Directory.CreateDirectory(target);
        var (code, message) = RunTool(tool, $"-x -f {Quote(archive)} -C {Quote(target)}");
        if (code == 0) { folder = target; return true; }
        try { if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target); } catch { }
        error = string.IsNullOrWhiteSpace(message) ? "解压未完成。" : message;
        return false;
    }

    static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
