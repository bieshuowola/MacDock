using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;

namespace MacDock;

internal static class DiagnosticsLog
{
    static readonly Channel<string> Queue=Channel.CreateBounded<string>(new BoundedChannelOptions(128){SingleReader=true,FullMode=BoundedChannelFullMode.DropOldest});
    static readonly string Folder=Path.Combine(Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MacDock"),"diagnostics");
    static readonly string Current=Path.Combine(Folder,"events.jsonl");
    static readonly Task Writer=Task.Run(Write);
    internal static void Error(Exception error,string area="application")
    {
        try
        {
            // Record method identities without arguments, paths, exception messages or file contents.
            var stack=new StackTrace(error,false).GetFrames().Take(24).Select(f=>MethodName(f.GetMethod())).ToArray();
            Event(area,new{type=error.GetType().FullName,hresult=error.HResult,stack,inner=error.InnerException?.GetType().FullName});
        }
        catch{}
    }

    static string MethodName(MethodBase? method)=>method==null?"":method.DeclaringType?.FullName+"."+method.Name;

    internal static void Event(string name,object detail)
    {
        try{Queue.Writer.TryWrite(JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,version=ReleaseUpdates.CurrentVersion,name,detail}));}
        catch{}
    }

    static async Task Write()
    {
        try
        {
            foreach(var file in new[]{Current,Current+".old"})
                if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)>TimeSpan.FromDays(14))File.Delete(file);
        }
        catch{}
        await foreach(var entry in Queue.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if(File.Exists(Current)&&new FileInfo(Current).Length>=512*1024)File.Move(Current,Current+".old",true);
                File.AppendAllText(Current,entry+Environment.NewLine);
            }
            catch{}
        }
    }

    internal static void Finish()
    {
        Queue.Writer.TryComplete();
        try{Writer.Wait(TimeSpan.FromMilliseconds(350));}catch{}
    }

    internal static void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        Process.Start(new ProcessStartInfo(Folder){UseShellExecute=true});
    }

    internal static void Export(string destination)
    {
        string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            ExportNew(temp);
            File.Move(temp,destination);
        }
        finally{try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    static void ExportNew(string destination)
    {
        using var file=new FileStream(destination,FileMode.CreateNew);
        using var zip=new ZipArchive(file,ZipArchiveMode.Create);
        foreach(var path in new[]{Current,Current+".old"})
        {
            if(!File.Exists(path))continue;
            using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            using var output=zip.CreateEntry(Path.GetFileName(path)).Open();
            input.CopyTo(output);
        }
        using var info=new StreamWriter(zip.CreateEntry("environment.json").Open());
        info.Write(JsonSerializer.Serialize(new{version=ReleaseUpdates.CurrentVersion,system=Environment.OSVersion.Version.ToString(),
            architecture=System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString()}));
    }
}
