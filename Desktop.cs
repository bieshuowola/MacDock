using System.IO;
using Microsoft.Win32;

namespace MacDock;

public sealed record DesktopItem(string Path,string Name,string Source,string? Shell);

// The desktop is the only automatic source of drawer/dock entries.
// It mirrors the user desktop, the public desktop and the shell namespace icons that Windows
// itself shows on the desktop, so nothing from the Start Menu or system tools leaks in.
// Enumeration is filesystem-based, so it keeps working when the user switches desktop icons
// off in Windows (Explorer's "Show desktop icons") and the shell stops painting them.
internal static class Desktop
{
    const string UserFiles="::{59031A47-3F72-44A7-89C5-5595FE6B30EE}";
    internal static IReadOnlySet<string> ReadableFolders {get;private set;}=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal static bool ViewAvailable {get;private set;}
    static List<DesktopItem> lastConfirmedShell=new();
    internal static readonly (string Clsid,string Name,string Shell)[] SystemIcons =
    {
        ("{20D04FE0-3AEA-1069-A2D8-08002B30309D}","此电脑","shell:MyComputerFolder"),
        ("{645FF040-5081-101B-9F08-00AA002F954E}","回收站","shell:RecycleBinFolder"),
        ("{59031A47-3F72-44A7-89C5-5595FE6B30EE}","用户文件","shell:UsersFilesFolder"),
        ("{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}","控制面板","shell:ControlPanelFolder"),
        ("{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}","网络","shell:NetworkPlacesFolder")
    };

    public static string UserFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    public static string PublicFolder => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public static IEnumerable<string> Folders()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in new[] { UserFolder, PublicFolder })
            if (!string.IsNullOrWhiteSpace(f) && Directory.Exists(f) && seen.Add(f)) yield return f;
    }

    // True only when at least one desktop folder can actually be listed. Used to distinguish
    // "the user deleted everything" from "the folder is temporarily unavailable".
    public static bool Reachable()
    {
        foreach (var folder in Folders())
        {
            try { Directory.EnumerateFileSystemEntries(folder).Take(1).ToList(); return true; }
            catch { }
        }
        return false;
    }

    public static bool IsDesktopPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("::", StringComparison.Ordinal)) return false;
        foreach (var folder in new[] { UserFolder, PublicFolder })
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),root,StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Explorer hides known extensions; keep the label identical to the desktop.
    public static string DisplayName(string path)
    {
        var shellName=Native.ShellDisplayName(path);if(!string.IsNullOrWhiteSpace(shellName))return shellName;
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return name;
        return ext.ToLowerInvariant() switch
        {
            ".lnk" or ".url" or ".exe" or ".appref-ms" or ".txt" or ".zip" or ".7z" or ".rar" or ".pdf"
                or ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".mp4" or ".mov" or ".mkv"
                or ".mp3" or ".wav" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" => Path.GetFileNameWithoutExtension(path),
            _ => name
        };
    }

    public static List<DesktopItem> Enumerate()=>Enumerate(false,out _);

    internal static List<DesktopItem> Enumerate(bool firstRun,out IReadOnlyList<DesktopView.PositionedItem> positions)
    {
        var items = new List<DesktopItem>();
        var readable=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in new[] { UserFolder, PublicFolder })
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            string[] paths;try { paths=Directory.GetFileSystemEntries(dir);readable.Add(dir); } catch { continue; }
            foreach (var path in SafeFiles(paths))
            {
                var name = DisplayName(path);
                if (name.Length == 0) continue;
                items.Add(new DesktopItem(path, name, "desktop", null));
            }
        }
        ReadableFolders=readable;
        var system = new List<DesktopItem>();
        var view=DesktopView.Read(firstRun,out positions);ViewAvailable=view!=null;
        if(view!=null)
        {
            foreach(var path in view.Where(p=>p.StartsWith("::",StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var name=Native.ShellDisplayName(path);if(!string.IsNullOrWhiteSpace(name))system.Add(new DesktopItem(path,name,"system","shell:"+path));
            }
            // The user-files shell item reports a filesystem parsing name, despite being a
            // special desktop icon. Include it only when the actual view confirms it.
            var userProfile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if(!system.Any(i=>i.Path.Equals(UserFiles,StringComparison.OrdinalIgnoreCase))&&view.Contains(userProfile,StringComparer.OrdinalIgnoreCase))
                system.Add(new DesktopItem(UserFiles,Environment.UserName,"system","shell:UsersFilesFolder"));
            if(firstRun)positions=positions.Select(p=>p.Path.Equals(userProfile,StringComparison.OrdinalIgnoreCase)?p with{Path=UserFiles}:p).ToArray();
            lastConfirmedShell=system.ToList();
        }
        else
        {
            system.AddRange(lastConfirmedShell.Where(i=>!SystemIcons.Any(s=>i.Path.Equals("::"+s.Clsid,StringComparison.OrdinalIgnoreCase))));
            foreach(var icon in SystemIcons)if(SystemIconVisible(icon.Clsid))system.Add(new DesktopItem("::"+icon.Clsid,icon.Name,"system",icon.Shell));
        }
        system.AddRange(items);
        return system;
    }

    internal static IEnumerable<DesktopItem> DefaultPins()
    {
        foreach(int index in new[]{0,1,3,2})
        {
            var icon=SystemIcons[index];
            string path="::"+icon.Clsid;
            var name=Native.ShellDisplayName(path);
            if(!string.IsNullOrWhiteSpace(name))yield return new DesktopItem(path,name,"default-pin",icon.Shell);
            else if(index==2)
            {
                string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if(!string.IsNullOrWhiteSpace(profile)&&Directory.Exists(profile))
                    yield return new DesktopItem(profile,DisplayName(profile),"default-pin",null);
            }
        }
    }

    static IEnumerable<string> SafeFiles(string[] all)
    {
        Array.Sort(all, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), true));
        foreach (var path in all)
        {
            var name = Path.GetFileName(path);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("~$", StringComparison.Ordinal)) continue;
            try
            {
                var attr = File.GetAttributes(path);
                if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
            }
            catch { continue; }
            yield return path;
        }
    }

    public static string CreateShortcut(string target)
    {
        if(!File.Exists(target)&&!Directory.Exists(target))throw new FileNotFoundException("目标不存在",target);
        var stem=Path.GetFileNameWithoutExtension(target);var file=Path.Combine(UserFolder,stem+".lnk");
        for(int n=2;File.Exists(file)||Directory.Exists(file);n++)file=Path.Combine(UserFolder,$"{stem} ({n}).lnk");
        object? shell=null,link=null;
        try { shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);link=((dynamic)shell!).CreateShortcut(file);((dynamic)link).TargetPath=target;((dynamic)link).WorkingDirectory=Path.GetDirectoryName(target);((dynamic)link).Save();return file; }
        finally {if(link!=null)System.Runtime.InteropServices.Marshal.ReleaseComObject(link);if(shell!=null)System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);}
    }

    // Windows hides selected desktop icons through the per-user HideDesktopIcons keys.
    static bool SystemIconVisible(string clsid)
    {
        try
        {
            using var explorer = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
            if (explorer?.GetValue(clsid) is int hidden) return hidden == 0;
            using var classic = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu");
            if (classic?.GetValue(clsid) is int old) return old == 0;
        }
        catch { }
        // Unknown is not proof of visibility. Never import a registered namespace by default.
        return false;
    }
}
