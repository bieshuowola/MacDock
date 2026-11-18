using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MacDock;

// Resolve the resource chosen by Windows/the shortcut, independently of jumbo icon-list
// decorations. All extraction and shortcut COM work is performed on the icon STA worker.
internal static class IconResources
{
    internal sealed record Link(string Stamp,string Target,string Icon,string Arguments,string? AppId=null);
    internal sealed record Recipe(string SourceStamp,string File,int Index);
    static readonly ConcurrentDictionary<string,Link> links=new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string,Recipe> recipes=new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string,(string Stamp,int[] Sizes)> frames=new(StringComparer.OrdinalIgnoreCase);
    static readonly ConcurrentDictionary<string,(long Tick,string Stamp)> assocStamps=new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> SelfIconExts=new(StringComparer.OrdinalIgnoreCase){".exe",".lnk",".url",".ico",".cpl",".scr"};
    static int generation;
    internal static void InvalidateAssociations()=>assocStamps.Clear();
    internal static void Invalidate(string? path=null)
    {
        if(path==null){links.Clear();recipes.Clear();frames.Clear();assocStamps.Clear();System.Threading.Interlocked.Increment(ref generation);}
        else
        {
            links.TryRemove(path,out _);
            recipes.TryRemove(path,out _);
            var prefix=path+"|";
            foreach(var k in frames.Keys)if(k.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))frames.TryRemove(k,out _);
            var ext=Path.GetExtension(path);
            if(!string.IsNullOrEmpty(ext))assocStamps.TryRemove(ext,out _);
        }
    }
    delegate bool ResourceName(nint module,nint type,nint name,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern nint LoadLibraryEx(string path,nint file,uint flags);
    [DllImport("kernel32.dll")]static extern bool FreeLibrary(nint module);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool EnumResourceNames(nint module,nint type,ResourceName callback,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern nint FindResource(nint module,nint name,nint type);
    [DllImport("kernel32.dll")]static extern nint LoadResource(nint module,nint resource);
    [DllImport("kernel32.dll")]static extern nint LockResource(nint resource);
    [DllImport("kernel32.dll")]static extern uint SizeofResource(nint module,nint resource);
    internal static int ExtractionSize(string path,int index,int requested)
    {
        var key=path+"|"+index;var stamp=FileStamp(path);
        if(!frames.TryGetValue(key,out var cached)||cached.Stamp!=stamp)
        {
            var sizes=new List<int>();nint module=0;
            try
            {
                if(Path.GetExtension(path).Equals(".ico",StringComparison.OrdinalIgnoreCase))
                {
                    using var reader=new BinaryReader(File.Open(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete));
                    if(reader.ReadUInt16()==0&&reader.ReadUInt16()==1){int count=reader.ReadUInt16();if(count>256)throw new InvalidDataException();for(int i=0;i<count;i++){int w=reader.ReadByte(),h=reader.ReadByte();sizes.Add(Math.Max(w==0?256:w,h==0?256:h));reader.BaseStream.Seek(14,SeekOrigin.Current);}}
                }
                else
                {
                    // Map resources as data: DLL entry points and application code never execute.
                    module=LoadLibraryEx(path,0,0x22);int ordinal=0;
                    if(module!=0)EnumResourceNames(module,14,(m,t,n,_)=>
                    {
                        if(index<0?n.ToInt64()!=-(long)index:ordinal++!=index)return true;
                        var resource=FindResource(m,n,t);var ptr=LockResource(LoadResource(m,resource));var length=SizeofResource(m,resource);
                        if(ptr!=0&&length>=6){int count=(ushort)Marshal.ReadInt16(ptr,4);if(count<=256&&length>=6+14*count)for(int i=0;i<count;i++){int w=Marshal.ReadByte(ptr,6+i*14),h=Marshal.ReadByte(ptr,7+i*14);sizes.Add(Math.Max(w==0?256:w,h==0?256:h));}}return false;
                    },0);
                }
            }
            catch{sizes.Clear();}
            finally{if(module!=0)FreeLibrary(module);}
            var validSizes=sizes.Where(n=>n>=16&&n<=256).Distinct().OrderBy(n=>n).ToArray();
            cached=(stamp,validSizes);
            if(validSizes.Length>0){if(frames.Count>512)frames.Clear();frames[key]=cached;}
        }
        // Never manufacture a 128 px image from a 32 px resource and then shrink it again.
        return cached.Sizes.Length==0?requested:cached.Sizes.FirstOrDefault(n=>n>=requested,cached.Sizes[^1]);
    }
    static string FileStamp(string path)
    {
        if(path.StartsWith("::",StringComparison.Ordinal)||path.StartsWith(@"\\",StringComparison.Ordinal))return path;
        try
        {
            var info=new FileInfo(path);
            if(info.Exists)return $"{info.LastWriteTimeUtc.Ticks}/{info.CreationTimeUtc.Ticks}/{info.Length}/{(int)info.Attributes}";
            if(Directory.Exists(path))
            {
                var ini=new FileInfo(Path.Combine(path,"desktop.ini"));
                int dirAttr=(int)File.GetAttributes(path);
                string iniStamp=ini.Exists?$"{ini.LastWriteTimeUtc.Ticks}/{ini.Length}/{(int)ini.Attributes}":"0";
                return $"{Directory.GetLastWriteTimeUtc(path).Ticks}/{dirAttr}/{iniStamp}";
            }
            return "missing";
        }
        catch{return "unavailable";}
    }
    static string AssociationStamp(string path)
    {
        if(string.IsNullOrEmpty(path)||path.StartsWith("::",StringComparison.Ordinal)||path.StartsWith(@"\\",StringComparison.Ordinal))return "";
        var ext=Path.GetExtension(path);
        if(string.IsNullOrEmpty(ext)||SelfIconExts.Contains(ext))return "";
        if(Directory.Exists(path))return "";
        long now=Environment.TickCount64;
        if(assocStamps.TryGetValue(ext,out var cached)&&now-cached.Tick<5000)return cached.Stamp;
        string progId="",hash="",cls="",defaultIcon="",handler="";
        try
        {
            using var uc=Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{ext}\UserChoice");
            if(uc!=null){progId=uc.GetValue("ProgId") as string??"";hash=uc.GetValue("Hash") as string??"";}
            using var kr=Registry.ClassesRoot.OpenSubKey(ext);
            if(kr!=null)cls=kr.GetValue(null) as string??"";
            var effective=string.IsNullOrWhiteSpace(progId)?cls:progId;
            if(!string.IsNullOrWhiteSpace(effective))
            {
                using var icon=Registry.ClassesRoot.OpenSubKey(effective+@"\DefaultIcon");
                using var command=Registry.ClassesRoot.OpenSubKey(effective+@"\shell\open\command");
                defaultIcon=icon?.GetValue(null) as string??"";
                handler=command?.GetValue(null) as string??"";
            }
        }
        catch{}
        string stamp=$"{progId}|{hash}|{cls}|{defaultIcon}|{handler}";
        if(assocStamps.Count>256)assocStamps.Clear();
        assocStamps[ext]=(now,stamp);
        return stamp;
    }
    internal static string Stamp(string path)
    {
        var source=FileStamp(path);var resource=recipes.TryGetValue(path,out var cached)&&cached.SourceStamp==source?FileStamp(cached.File)+"/"+cached.Index:"unresolved";
        var target=links.TryGetValue(path,out var lnk)&&lnk.Stamp==source&&!string.IsNullOrWhiteSpace(lnk.Target)?FileStamp(lnk.Target):"";
        var assoc=AssociationStamp(path);
        // Periodic desktop synchronization revalidates packaged resources even when
        // an installer replaces a package without touching its desktop shortcut.
        var package=lnk!=null&&lnk.Stamp==source&&lnk.AppId!=null
            ?$"/{Theme.Dark}/{Environment.TickCount64/120000}":"";
        return $"{generation}/{source}/{resource}/{target}/{assoc}{package}";
    }
    internal static Link ReadLink(string path)
    {
        var stamp=FileStamp(path);if(links.TryGetValue(path,out var found)&&found.Stamp==stamp)return found;
        object? shell=null,link=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);link=((dynamic)shell!).CreateShortcut(path);
            string target=(string)((dynamic)link).TargetPath;
            var result=new Link(stamp,target,(string)((dynamic)link).IconLocation,(string)((dynamic)link).Arguments,
                string.IsNullOrEmpty(target)?PackagedIcons.ShortcutIdentity(path):PackagedIcons.AppId(target));
            if(links.Count>512)links.Clear();links[path]=result;return result;
        }
        catch{return new Link(stamp,path,"","");}
        finally{if(link!=null)Marshal.ReleaseComObject(link);if(shell!=null)Marshal.ReleaseComObject(shell);}
    }
    static (string File,int Index)? Parse(string specification,string parent)
    {
        if(string.IsNullOrWhiteSpace(specification))return null;
        var comma=specification.LastIndexOf(',');int index=0;var file=specification;
        if(comma>=0&&int.TryParse(specification[(comma+1)..],NumberStyles.Integer,CultureInfo.InvariantCulture,out index))file=specification[..comma];
        file=Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));
        if(file.Length==0)return null;
        if(!Path.IsPathRooted(file))file=Path.Combine(parent,file);
        return File.Exists(file)?(file,index):null;
    }
    internal static Recipe? Describe(string path)
    {
        var stamp=FileStamp(path);if(recipes.TryGetValue(path,out var cached)&&cached.SourceStamp==stamp)return cached;
        (string File,int Index)? location=null;
        try
        {
            var extension=Path.GetExtension(path).ToLowerInvariant();var parent=Path.GetDirectoryName(path)??"";
            if(extension==".lnk")
            {
                var link=ReadLink(path);location=Parse(link.Icon,parent);
                if(location==null&&File.Exists(link.Target)&&new[]{".exe",".dll",".ico",".cpl",".scr"}.Contains(Path.GetExtension(link.Target).ToLowerInvariant()))location=(link.Target,0);
            }
            else if(extension==".url")
            {
                var bytes=File.ReadAllBytes(path);string text;
                try{text=new UTF8Encoding(false,true).GetString(bytes);}catch{Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);text=Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);}
                if(bytes.Length>1&&(bytes[0]==255&&bytes[1]==254||bytes[0]==254&&bytes[1]==255))text=(bytes[0]==255?Encoding.Unicode:Encoding.BigEndianUnicode).GetString(bytes);
                var values=text.Split('\n').Select(s=>s.Trim()).Where(s=>s.Contains('=')).Select(s=>(Key:s[..s.IndexOf('=')],Value:s[(s.IndexOf('=')+1)..])).ToArray();
                var icon=values.LastOrDefault(s=>s.Key.Equals("IconFile",StringComparison.OrdinalIgnoreCase)).Value;
                var iconIndex=values.LastOrDefault(s=>s.Key.Equals("IconIndex",StringComparison.OrdinalIgnoreCase)).Value;
                location=Parse(icon+(iconIndex!=null?","+iconIndex:""),parent);
            }
            else if(new[]{".exe",".dll",".ico",".cpl",".scr"}.Contains(extension))location=(path,0);
            if(location==null){var shell=Native.IconLocation(path);if(shell.HasValue&&File.Exists(shell.Value.Path))location=(shell.Value.Path,shell.Value.Index);}
            if(!location.HasValue)return null;
            var recipe=new Recipe(stamp,location.Value.File,location.Value.Index);
            if(recipes.Count>512)recipes.Clear();recipes[path]=recipe;return recipe;
        }
        catch{return null;}
    }
}
