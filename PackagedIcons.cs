using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Windows.Management.Deployment;

namespace MacDock;

internal static class PackagedIcons
{
    const string AppsFolder="::{4234d49b-0245-4df3-b780-3893943456e1}";
    [ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkIdentity
    {
        void GetPath(nint path,int length,nint data,uint flags);
        void GetIDList(out nint pidl);
    }
    [DllImport("shell32.dll")] static extern int SHGetNameFromIDList(nint pidl,uint kind,out nint name);

    internal static string? AppId(string path)
    {
        if(path.StartsWith("shell:AppsFolder\\",StringComparison.OrdinalIgnoreCase)||path.StartsWith(AppsFolder+"\\",StringComparison.OrdinalIgnoreCase))
            path=path[(path.LastIndexOf('\\')+1)..];
        int split=path.IndexOf('!');
        if(split<=0||split==path.Length-1||!path[..split].Contains('_'))return null;
        return path.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-' or '_' or '!')?path:null;
    }
    internal static string? ShortcutIdentity(string path)
    {
        object? link=null;nint pidl=0,name=0;
        try
        {
            link=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"))!);
            ((IPersistFile)link!).Load(path,0);
            ((IShellLinkIdentity)link!).GetIDList(out pidl);
            return pidl!=0&&SHGetNameFromIDList(pidl,0x80028000,out name)>=0?AppId(Marshal.PtrToStringUni(name)??""):null;
        }
        catch(COMException){return null;}
        finally
        {
            if(name!=0)Marshal.FreeCoTaskMem(name);
            if(pidl!=0)Marshal.FreeCoTaskMem(pidl);
            if(link!=null)Marshal.ReleaseComObject(link);
        }
    }

    internal static ImageSource? Load(string path,int pixels)
    {
        string? id;
        if(path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase))
        {
            var link=IconResources.ReadLink(path);
            if(link.Icon.Trim() is not ("" or ",0"))return null;
            id=link.AppId;
        }
        else id=AppId(path);
        if(id==null)return null;
        try
        {
            int delimiter=id.IndexOf('!');
            var family=id[..delimiter];var application=id[(delimiter+1)..];
            var packages=new PackageManager().FindPackagesForUser("",family);
            foreach(var package in packages.Where(p=>!p.IsResourcePackage&&!p.IsFramework))
            {
                var root=package.InstalledPath;
                var manifest=XDocument.Load(Path.Combine(root,"AppxManifest.xml"));
                string? asset=manifest.Descendants().Where(x=>x.Name.LocalName=="Application"&&(string?)x.Attribute("Id")==application)
                    .SelectMany(x=>x.Elements()).FirstOrDefault(x=>x.Name.LocalName=="VisualElements")?.Attribute("Square44x44Logo")?.Value;
                if(string.IsNullOrEmpty(asset))continue;
                var declared=Path.GetFullPath(Path.Combine(root,asset));
                if(!declared.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
                var image=ReadAsset(declared,pixels,Theme.Dark);
                if(image!=null)return image;
            }
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or COMException or System.Xml.XmlException or ArgumentException or InvalidOperationException)
        { /* Some packages expose only PRI resources. Use the Shell item in that case. */ }
        return Native.AuditShellIcon("shell:AppsFolder\\"+id,pixels);
    }

    static BitmapSource? ReadAsset(string declared,int pixels,bool dark)
    {
        string parent=Path.GetDirectoryName(declared)!;
        if(!Directory.Exists(parent))return null;
        string stem=Path.GetFileNameWithoutExtension(declared),extension=Path.GetExtension(declared);
        string form=dark?"_altform-unplated":"_altform-lightunplated";
        var candidates=Directory.EnumerateFiles(parent,stem+".targetsize-*"+extension)
            .Select(p=>(Path:p,Qualifier:Path.GetFileNameWithoutExtension(p)[(stem.Length+12)..].Split('_',2)))
            .Where(x=>x.Qualifier.Length==1||"_"+x.Qualifier[1]==form)
            .Select(x=>(x.Path,Size:int.TryParse(x.Qualifier[0],out int size)?size:0,Themed:x.Qualifier.Length==2))
            .Where(x=>x.Size>0)
            .OrderByDescending(x=>x.Themed).ThenBy(x=>x.Size<pixels).ThenBy(x=>Math.Abs(x.Size-pixels))
            .Select(x=>x.Path);
        foreach(var candidate in candidates.Append(declared))
        {
            if(!File.Exists(candidate))continue;
            var image=new BitmapImage();
            image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.CreateOptions=BitmapCreateOptions.IgnoreImageCache;
            image.UriSource=new Uri(candidate);image.EndInit();image.Freeze();
            return image;
        }
        return null;
    }
}
