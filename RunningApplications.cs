using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MacDock;

internal sealed class RunningApplications
{
    internal sealed record Target(string? Image, string? AppId, string Arguments)
    {
        internal static Target? FromEntry(Entry entry)
        {
            if(entry.IsVirtual)return null;
            var link=entry.Path.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)?IconResources.ReadLink(entry.Path):null;
            string path=link?.Target??entry.Path;
            string? appId=link?.AppId??PackagedIcons.AppId(path);
            return appId!=null ? new(null,appId,"") :
                Path.IsPathFullyQualified(path)&&Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase)
                    ?new(Path.GetFullPath(path),null,link?.Arguments??""):null;
        }
    }

    readonly record struct Instance(uint Id,long Created);
    sealed record ProcessInfo(Instance Instance,uint Parent,string Path,string? AppId,string[]? Arguments);
    sealed record Association(ProcessInfo Process,bool Main);
    sealed record Launch(Target Target,Process Process,Instance Instance);
    readonly ConcurrentQueue<Launch> pendingLaunches=new();
    readonly List<Launch> launches=new();
    readonly Dictionary<Target,Dictionary<Instance,Association>> previous=new();
    readonly object sync=new();
    static readonly uint Session=ProcessIdToSessionId((uint)Environment.ProcessId,out uint session)?session:uint.MaxValue;

    internal void RememberLaunch(Target? target,Process? process)
    {
        if(process==null)return;
        bool retained=false;
        try
        {
            if(target?.Image==null||ApplicationFolder(target.Image)==null)return;
            var handle=process.SafeHandle;
            var image=new StringBuilder(32768);
            int length=image.Capacity;
            if(!QueryFullProcessImageName(handle,0,image,ref length)||
                !string.Equals(target.Image,image.ToString(),StringComparison.OrdinalIgnoreCase)||
                !GetProcessTimes(handle,out long created,out _,out _,out _))return;
            pendingLaunches.Enqueue(new(target,process,new((uint)process.Id,created)));
            retained=true;
        }
        catch(InvalidOperationException) { }
        catch(System.ComponentModel.Win32Exception) { }
        finally { if(!retained)process.Dispose(); }
    }

    internal HashSet<Target> Scan(IReadOnlyCollection<Target> targets)
    {
        lock(sync)
        {
            var results=new HashSet<Target>();
            while(pendingLaunches.TryDequeue(out var launch))launches.Add(launch);
            var exitedLaunches=new List<(Launch Launch,long Exited)>();
            for(int i=launches.Count-1;i>=0;i--)
            {
                var launch=launches[i];
                if(!targets.Contains(launch.Target))
                {
                    launch.Process.Dispose();
                    launches.RemoveAt(i);
                }
                else if(launch.Process.HasExited)
                {
                    exitedLaunches.Add((launch,launch.Process.ExitTime.ToFileTimeUtc()));
                    launch.Process.Dispose();
                    launches.RemoveAt(i);
                }
            }
            if(targets.Count==0){previous.Clear();return results;}
            var visible=Native.ApplicationWindowProcessIds();
            var argumentImages=targets.Where(t=>t.Arguments.Length!=0).Select(t=>t.Image!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ids=new HashSet<uint>();
            var processes=Snapshot(targets.Any(t=>t.AppId!=null),argumentImages,ids);
            foreach(var target in previous.Keys.Except(targets).ToArray())previous.Remove(target);
            foreach(var target in targets)
            {
                var associated=Associate(target,processes,ids,exitedLaunches);
                foreach(var match in associated.Values)
                    if(match.Main||visible.Contains(match.Process.Instance.Id))results.Add(target);
                previous[target]=associated;
            }
            return results;
        }
    }

    internal IReadOnlyList<nint> ClosableWindows(Target? target)
    {
        if(target==null)return Array.Empty<nint>();
        var windows=Native.ApplicationWindows();
        if(windows.Count==0)return Array.Empty<nint>();
        string[]? arguments=target.Arguments.Length==0?null:SplitArguments("app.exe "+target.Arguments);
        string? appFolder=ApplicationFolder(target.Image);

        Dictionary<Instance,Association>? priorSnapshot=null;
        lock(sync)
        {
            if(previous.TryGetValue(target,out var prior)&&prior.Count>0)
                priorSnapshot=new Dictionary<Instance,Association>(prior);
        }

        var queried=new Dictionary<uint,ProcessInfo?>();
        ProcessInfo? Query(uint pid)
        {
            if(queried.TryGetValue(pid,out var cached))return cached;
            var info=QueryProcess(pid,target.AppId!=null,target.Arguments.Length!=0?target.Image:null);
            queried[pid]=info;
            return info;
        }

        bool MatchesDirect(ProcessInfo p) =>
            target.AppId!=null
                ?string.Equals(target.AppId,p.AppId,StringComparison.OrdinalIgnoreCase)
                :string.Equals(target.Image,p.Path,StringComparison.OrdinalIgnoreCase)&&
                    (target.Arguments.Length==0||arguments!=null&&p.Arguments!=null&&arguments.SequenceEqual(p.Arguments));

        var handles=new List<nint>();
        var seenHandles=new HashSet<nint>();
        bool candidateInAppFolder=false;

        foreach(var w in windows)
        {
            uint effectivePid=w.HostedProcessId!=0?w.HostedProcessId:w.ProcessId;
            var info=Query(effectivePid);
            if(info==null)continue;
            bool matched=MatchesDirect(info)||(priorSnapshot!=null&&priorSnapshot.ContainsKey(info.Instance));
            if(matched)
            {
                if(seenHandles.Add(w.Handle))handles.Add(w.Handle);
            }
            else if(appFolder!=null&&info.Path.StartsWith(appFolder,StringComparison.OrdinalIgnoreCase))
            {
                candidateInAppFolder=true;
            }
        }

        if(candidateInAppFolder)
        {
            Dictionary<Instance,Association> associated;
            lock(sync)
            {
                var argumentImages=target.Arguments.Length!=0&&target.Image!=null
                    ?new HashSet<string>(StringComparer.OrdinalIgnoreCase){target.Image}
                    :new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var ids=new HashSet<uint>();
                var processes=Snapshot(target.AppId!=null,argumentImages,ids);
                associated=Associate(target,processes,ids,Array.Empty<(Launch,long)>());
            }
            foreach(var w in windows)
            {
                if(seenHandles.Contains(w.Handle))continue;
                uint effectivePid=w.HostedProcessId!=0?w.HostedProcessId:w.ProcessId;
                var info=Query(effectivePid);
                if(info!=null&&associated.ContainsKey(info.Instance)&&seenHandles.Add(w.Handle))
                    handles.Add(w.Handle);
            }
        }

        return handles;
    }

    Dictionary<Instance,Association> Associate(Target target,List<ProcessInfo> processes,HashSet<uint> ids,IReadOnlyList<(Launch Launch,long Exited)> exitedLaunches)
    {
        var associated=new Dictionary<Instance,Association>();
        var parents=new Dictionary<uint,Association>();
        previous.TryGetValue(target,out var prior);
        string[]? arguments=target.Arguments.Length==0?null:SplitArguments("app.exe "+target.Arguments);
        string? appFolder=ApplicationFolder(target.Image);
        var exitedParents=exitedLaunches.Where(l=>l.Launch.Target==target)
            .ToLookup(l=>l.Launch.Instance.Id);
        foreach(var p in processes)
        {
            bool main=target.AppId!=null
                ?string.Equals(target.AppId,p.AppId,StringComparison.OrdinalIgnoreCase)
                :string.Equals(target.Image,p.Path,StringComparison.OrdinalIgnoreCase)&&
                    (target.Arguments.Length==0||arguments!=null&&p.Arguments!=null&&arguments.SequenceEqual(p.Arguments));

            bool child=appFolder!=null&&p.Path.StartsWith(appFolder,StringComparison.OrdinalIgnoreCase)&&
                (parents.TryGetValue(p.Parent,out var parent)&&parent.Process.Instance.Created<p.Instance.Created||
                 prior!=null&&prior.TryGetValue(p.Instance,out var existing)&&!existing.Main||
                 !ids.Contains(p.Parent)&&
                    (exitedParents[p.Parent].Any(l=>l.Launch.Instance.Created<p.Instance.Created&&p.Instance.Created<=l.Exited)||
                     prior!=null&&prior.Values.Any(a=>a.Process.Instance.Id==p.Parent&&a.Process.Instance.Created<p.Instance.Created)));
            if(!main&&!child)continue;
            var match=new Association(p,main&&!child);
            associated[p.Instance]=match;
            parents[p.Instance.Id]=match;
        }
        return associated;
    }

    static ProcessInfo? QueryProcess(uint pid,bool readAppId,string? argumentImage)
    {
        if(!ProcessIdToSessionId(pid,out uint session)||session!=Session)return null;
        using var process=OpenProcess(0x1000,false,pid);
        if(process.IsInvalid||!GetProcessTimes(process,out long created,out _,out _,out _)||
            !GetExitCodeProcess(process,out uint status)||status!=259)return null;
        var path=new StringBuilder(32768);
        int size=path.Capacity;
        if(!QueryFullProcessImageName(process,0,path,ref size))return null;
        string image=path.ToString();
        string? identity=null;
        if(readAppId)
        {
            var appId=new StringBuilder(256);
            size=appId.Capacity;
            if(GetApplicationUserModelId(process,ref size,appId)==0)identity=appId.ToString();
        }
        string[]? args=argumentImage!=null&&string.Equals(argumentImage,image,StringComparison.OrdinalIgnoreCase)
            ?ReadArguments(process):null;
        return new(new(pid,created),0,image,identity,args);
    }

    static string? ApplicationFolder(string? executable)
    {
        if(executable==null)return null;
        string? directory=Path.GetDirectoryName(executable);
        if(string.IsNullOrEmpty(directory)||directory==Path.GetPathRoot(directory))return null;
        string folder=Path.TrimEndingDirectorySeparator(directory)+Path.DirectorySeparatorChar;
        string windows=Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.Windows))+Path.DirectorySeparatorChar;
        if(folder.StartsWith(windows,StringComparison.OrdinalIgnoreCase))return null;
        return folder;
    }

    static List<ProcessInfo> Snapshot(bool readAppIds,HashSet<string> argumentImages,HashSet<uint> ids)
    {
        var processes=new List<ProcessInfo>();
        using var snapshot=CreateToolhelp32Snapshot(2,0);
        var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>()};
        if(snapshot.IsInvalid||!Process32First(snapshot,ref entry))return processes;
        var path=new StringBuilder(32768);
        var appId=new StringBuilder(256);
        do
        {
            if(!ProcessIdToSessionId(entry.Id,out uint session)||session!=Session)continue;
            ids.Add(entry.Id);
            using var process=OpenProcess(0x1000,false,entry.Id); // PROCESS_QUERY_LIMITED_INFORMATION
            if(process.IsInvalid||!GetProcessTimes(process,out long created,out _,out _,out _)||
                !GetExitCodeProcess(process,out uint status)||status!=259)continue; // STILL_ACTIVE
            int size=path.Capacity;
            if(!QueryFullProcessImageName(process,0,path,ref size))continue;
            string image=path.ToString();
            size=appId.Capacity;
            string? identity=readAppIds&&GetApplicationUserModelId(process,ref size,appId)==0?appId.ToString():null;
            string[]? args=argumentImages.Contains(image)?ReadArguments(process):null;
            processes.Add(new(new(entry.Id,created),entry.Parent,image,identity,args));
        }while(Process32Next(snapshot,ref entry));
        processes.Sort((x,y)=>x.Instance.Created.CompareTo(y.Instance.Created));
        return processes;
    }

    // Query the OS copy of the command line. If access is denied or the class is unavailable,
    // a shortcut with arguments cannot be attributed safely to a shared host process.
    static string[]? ReadArguments(SafeProcessHandle process)
    {
        const int ProcessCommandLineInformation=60;
        NtQueryInformationProcess(process,ProcessCommandLineInformation,0,0,out int bytes);
        if(bytes<Marshal.SizeOf<UnicodeString>()||bytes>128*1024)return null;
        nint buffer=Marshal.AllocHGlobal(bytes);
        try
        {
            if(NtQueryInformationProcess(process,ProcessCommandLineInformation,buffer,bytes,out _)!=0)return null;
            var value=Marshal.PtrToStructure<UnicodeString>(buffer);
            long offset=value.Buffer.ToInt64()-buffer.ToInt64();
            if(value.Length==0||value.Length%2!=0||offset<Marshal.SizeOf<UnicodeString>()||offset+value.Length>bytes)return null;
            return SplitArguments(Marshal.PtrToStringUni(value.Buffer,value.Length/2)!);
        }
        finally{Marshal.FreeHGlobal(buffer);}
    }

    static string[]? SplitArguments(string commandLine)
    {
        nint argv=CommandLineToArgvW(commandLine,out int count);
        if(argv==0)return null;
        try
        {
            var arguments=new string[Math.Max(0,count-1)];
            for(int i=0;i<arguments.Length;i++)arguments[i]=Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,(i+1)*IntPtr.Size))!;
            return arguments;
        }
        finally{LocalFree(argv);}
    }

    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size,Usage,Id;
        public nuint DefaultHeap;
        public uint Module,Threads,Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string Image;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct UnicodeString
    {
        public ushort Length,MaximumLength;
        public nint Buffer;
    }

    [DllImport("kernel32.dll")] static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags,uint processId);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32First(SafeFileHandle snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool Process32Next(SafeFileHandle snapshot,ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint id,out uint session);
    [DllImport("kernel32.dll")] static extern SafeProcessHandle OpenProcess(uint access,bool inherit,uint id);
    [DllImport("kernel32.dll")] static extern bool GetProcessTimes(SafeProcessHandle process,out long created,out long exited,out long kernel,out long user);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(SafeProcessHandle process,out uint code);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(SafeProcessHandle process,uint flags,StringBuilder path,ref int length);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int GetApplicationUserModelId(SafeProcessHandle process,ref int length,StringBuilder id);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(SafeProcessHandle process,int informationClass,nint buffer,int length,out int required);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern nint CommandLineToArgvW(string commandLine,out int count);
    [DllImport("kernel32.dll")] static extern nint LocalFree(nint allocation);
}
