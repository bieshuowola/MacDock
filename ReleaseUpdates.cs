using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacDock;

internal sealed class ReleaseUpdates
{
    internal sealed record Manifest(string Version,string FileName,long Size,string Sha256,string Notes,bool IsRevision=false);
    internal DateTimeOffset? LocalBuildTimeUtc { get; set; }
    internal string? LocalSumsPath { get; set; }
    internal string? LocalBinaryPath { get; set; }
    const int MaxMetadata=64*1024;
    const long MaxPackage=250L*1024*1024;
    static readonly Regex RepoName=new(@"\A[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+\z");
    static readonly Regex ReleaseTag=new(@"\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z");
    readonly HttpClient client;
    readonly string repository;
    readonly byte[] publicKey;
    internal static string CurrentVersion=>typeof(ReleaseUpdates).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    internal static ReleaseUpdates? Configured
    {
        get
        {
            var values=typeof(ReleaseUpdates).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a=>a.Key,a=>a.Value);
            if(!values.TryGetValue("MacDockReleaseRepository",out var repo)||!values.TryGetValue("MacDockUpdatePublicKey",out var key)||
                string.IsNullOrWhiteSpace(repo)||string.IsNullOrWhiteSpace(key))return null;
            return new ReleaseUpdates(repo,Convert.FromBase64String(key),Http);
        }
    }
    static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false})
    {
        Timeout=TimeSpan.FromSeconds(25)
    };

    internal ReleaseUpdates(string repository,byte[] publicKey,HttpClient client)
    {
        if(!RepoName.IsMatch(repository))throw new ArgumentException("Invalid GitHub release repository.");
        this.repository=repository;this.publicKey=publicKey;this.client=client;
    }

    static bool Trusted(Uri uri)
    {
        if(uri.Scheme!=Uri.UriSchemeHttps||!uri.IsDefaultPort||uri.UserInfo.Length!=0)return false;
        return uri.IdnHost is "api.github.com" or "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";
    }

    async Task<HttpResponseMessage> Get(Uri uri,CancellationToken cancellation)
    {
        for(int hops=0;hops<5;hops++)
        {
            if(!Trusted(uri))throw new InvalidDataException("更新服务器返回了不受信任的地址。");
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.UserAgent.ParseAdd("MacDock-Update/"+CurrentVersion);
            if(uri.IdnHost=="api.github.com")request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false);
            if(response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var next=response.Headers.Location;
                response.Dispose();
                if(next==null)throw new InvalidDataException("更新重定向缺少地址。");
                uri=new Uri(uri,next);
                continue;
            }
            if(response.IsSuccessStatusCode)return response;
            using(response)response.EnsureSuccessStatusCode();
        }
        throw new InvalidDataException("更新重定向次数过多。");
    }

    async Task<byte[]> SmallFile(Uri address,CancellationToken cancellation)
    {
        using var response=await Get(address,cancellation).ConfigureAwait(false);
        if(response.Content.Headers.ContentLength>MaxMetadata)throw new InvalidDataException("更新元数据过大。");
        await using var input=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var bytes=new MemoryStream();
        byte[] buffer=new byte[8192];
        int count;
        while((count=await input.ReadAsync(buffer,cancellation).ConfigureAwait(false))>0)
        {
            if(bytes.Length+count>MaxMetadata)throw new InvalidDataException("更新元数据过大。");
            bytes.Write(buffer,0,count);
        }
        return bytes.ToArray();
    }

    Uri Asset(string tag,string name)=>new($"https://github.com/{repository}/releases/download/{tag}/{name}");
    internal Uri ReleasePage(Manifest manifest)=>new($"https://github.com/{repository}/releases/tag/v{manifest.Version}");

    internal async Task<Manifest?> Check(Version installed,CancellationToken cancellation)
    {
        var json=await SmallFile(new Uri($"https://api.github.com/repos/{repository}/releases/latest"),cancellation).ConfigureAwait(false);
        using var release=JsonDocument.Parse(json);
        var root=release.RootElement;
        if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
        var tag=root.GetProperty("tag_name").GetString();
        if(tag==null||!ReleaseTag.IsMatch(tag)||!Version.TryParse(tag[1..],out var version))return null;
        if(version<installed)return null;
        if(version==installed)return await CheckSameVersionRevision(root,tag,version,cancellation).ConfigureAwait(false);
        var bytes=await SmallFile(Asset(tag,"macdock-update.json"),cancellation).ConfigureAwait(false);
        var signature=await SmallFile(Asset(tag,"macdock-update.sig"),cancellation).ConfigureAwait(false);
        return VerifyManifest(bytes,signature,version);
    }

    static (DateTimeOffset Time, bool Reliable) GetLocalBuildTimeInfo()
    {
        try
        {
            var exe=Environment.ProcessPath;
            if(!string.IsNullOrWhiteSpace(exe))
            {
                var infoPath=Path.Combine(Path.GetDirectoryName(exe)!,"BUILDINFO.json");
                if(File.Exists(infoPath))
                {
                    using var doc=JsonDocument.Parse(File.ReadAllText(infoPath));
                    if(doc.RootElement.TryGetProperty("built",out var b)&&b.ValueKind==JsonValueKind.String&&
                        DateTimeOffset.TryParse(b.GetString(),out var built))return (built.ToUniversalTime(),true);
                }
                return (new DateTimeOffset(File.GetLastWriteTimeUtc(exe),TimeSpan.Zero),false);
            }
        }
        catch{}
        return (DateTimeOffset.UtcNow,false);
    }

    static string? ComputeSha256(string path)
    {
        try
        {
            using var fs=File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        }
        catch{return null;}
    }

    static string? ParseSha256Sum(string text,string fileName)
    {
        foreach(var raw in text.Split('\n',StringSplitOptions.RemoveEmptyEntries))
        {
            var line=raw.Trim();
            if(line.Length<66)continue;
            var parts=line.Split((char[]?)null,2,StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length==2&&parts[0].Length==64&&parts[0].All(Uri.IsHexDigit)&&
                string.Equals(parts[1].Trim(),fileName,StringComparison.OrdinalIgnoreCase))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    async Task<Manifest?> CheckSameVersionRevision(JsonElement root,string tag,Version version,CancellationToken cancellation)
    {
        if(!root.TryGetProperty("assets",out var assets)||assets.ValueKind!=JsonValueKind.Array)return null;
        string verStr=version.ToString(3);
        string setupName=$"MacDock-{verStr}-win-x64-Setup.exe";
        string portableName=$"MacDock-{verStr}-win-x64-Portable.zip";
        long setupSize=0;
        DateTimeOffset? setupUpdatedAt=null,portableUpdatedAt=null,sumsUpdatedAt=null;
        bool hasSums=false;
        foreach(var asset in assets.EnumerateArray())
        {
            var name=asset.TryGetProperty("name",out var n)?n.GetString():null;
            DateTimeOffset? parsedUpdatedAt=asset.TryGetProperty("updated_at",out var ua)&&DateTimeOffset.TryParse(ua.GetString(),out var dt)?dt.ToUniversalTime():null;
            if(string.Equals(name,setupName,StringComparison.OrdinalIgnoreCase))
            {
                if(asset.TryGetProperty("size",out var sz)&&sz.TryGetInt64(out var s))setupSize=s;
                setupUpdatedAt=parsedUpdatedAt;
            }
            else if(string.Equals(name,portableName,StringComparison.OrdinalIgnoreCase))
            {
                portableUpdatedAt=parsedUpdatedAt;
            }
            else if(string.Equals(name,"SHA256SUMS.txt",StringComparison.OrdinalIgnoreCase))
            {
                hasSums=true;
                sumsUpdatedAt=parsedUpdatedAt;
            }
        }
        if(!hasSums||setupSize is <=0 or >MaxPackage)return null;
        var remoteUpdated=setupUpdatedAt??portableUpdatedAt??sumsUpdatedAt;
        string? localSumsFile=LocalSumsPath;
        string? exeDir=Environment.ProcessPath is string ep?Path.GetDirectoryName(ep):null;
        if(localSumsFile==null&&exeDir!=null)
        {
            var candidate=Path.Combine(exeDir,"SHA256SUMS.txt");
            if(File.Exists(candidate))localSumsFile=candidate;
        }
        string? localSha=localSumsFile!=null&&File.Exists(localSumsFile)?ParseSha256Sum(File.ReadAllText(localSumsFile),setupName):null;
        string? localBinaryFile=LocalBinaryPath??(exeDir!=null?Path.Combine(exeDir,"MacDock.dll"):null);
        if(localBinaryFile!=null&&!File.Exists(localBinaryFile))localBinaryFile=Environment.ProcessPath;
        bool hasLocalBinary=localBinaryFile!=null&&File.Exists(localBinaryFile);
        var (localTime,reliableTime)=LocalBuildTimeUtc is { } explicitTime?(explicitTime.ToUniversalTime(),true):GetLocalBuildTimeInfo();
        var tolerance=reliableTime?TimeSpan.FromSeconds(15):TimeSpan.FromMinutes(2);
        bool timeSuggestsNewer=remoteUpdated!=null&&remoteUpdated.Value>localTime.Add(tolerance);
        bool canHashBinary=hasLocalBinary&&(LocalBinaryPath!=null||LocalBuildTimeUtc==null);
        if(localSha==null&&!canHashBinary&&!timeSuggestsNewer)return null;
        var sumsBytes=await SmallFile(Asset(tag,"SHA256SUMS.txt"),cancellation).ConfigureAwait(false);
        var sumsText=System.Text.Encoding.UTF8.GetString(sumsBytes);
        var remoteSha=ParseSha256Sum(sumsText,setupName);
        if(remoteSha==null)return null;
        if(localSha!=null)
        {
            if(string.Equals(localSha,remoteSha,StringComparison.OrdinalIgnoreCase))return null;
        }
        else
        {
            string? remoteBinSha=hasLocalBinary?ParseSha256Sum(sumsText,Path.GetFileName(localBinaryFile!))??ParseSha256Sum(sumsText,"MacDock.dll"):null;
            if(remoteBinSha!=null&&hasLocalBinary)
            {
                string? targetPath=ParseSha256Sum(sumsText,Path.GetFileName(localBinaryFile!))!=null?localBinaryFile:
                    (exeDir!=null&&File.Exists(Path.Combine(exeDir,"MacDock.dll"))?Path.Combine(exeDir,"MacDock.dll"):localBinaryFile);
                string? localBinSha=targetPath!=null?ComputeSha256(targetPath):null;
                if(localBinSha!=null&&string.Equals(localBinSha,remoteBinSha,StringComparison.OrdinalIgnoreCase))return null;
                if(localBinSha==null&&!timeSuggestsNewer)return null;
            }
            else if(!timeSuggestsNewer)
            {
                return null;
            }
        }
        var notes=root.TryGetProperty("body",out var bodyEl)&&bodyEl.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(bodyEl.GetString())
            ?bodyEl.GetString()!:"检测到同版本修订构建，建议更新以获取最新修复。";
        return new Manifest(verStr,setupName,setupSize,remoteSha,notes,IsRevision:true);
    }

    Manifest VerifyManifest(byte[] bytes,byte[] signature,Version version)
    {
        using var key=ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey,out var consumed);
        if(consumed!=publicKey.Length||key.KeySize!=256||signature.Length!=64||
            !key.VerifyData(bytes,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new InvalidDataException("更新签名验证失败。");
        var manifest=JsonSerializer.Deserialize<Manifest>(bytes);
        if(manifest==null||manifest.Version!=version.ToString(3)||manifest.FileName!=$"MacDock-{version.ToString(3)}-win-x64-Setup.exe"||
            manifest.Size is <=0 or >MaxPackage||manifest.Sha256?.Length!=64||!manifest.Sha256.All(Uri.IsHexDigit)||manifest.Notes==null)
            throw new InvalidDataException("更新清单格式或版本不符。");
        return manifest;
    }

    internal async Task<string> Download(Manifest manifest,string cache,CancellationToken cancellation)
    {
        var directory=Path.Combine(cache,Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var partial=Path.Combine(directory,"setup.partial");
        var installer=Path.Combine(directory,manifest.FileName);
        try
        {
            using var response=await Get(Asset("v"+manifest.Version,manifest.FileName),cancellation).ConfigureAwait(false);
            if(response.Content.Headers.ContentLength is long contentLength&&contentLength!=manifest.Size)
                throw new InvalidDataException("更新包大小不符。");
            await using var input=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total=0;
            var buffer=new byte[64*1024];
            await using(var output=new FileStream(partial,FileMode.CreateNew,FileAccess.Write,FileShare.None,buffer.Length,true))
            {
                int count;
                while((count=await input.ReadAsync(buffer,cancellation).ConfigureAwait(false))>0)
                {
                    total+=count;
                    if(total>manifest.Size)throw new InvalidDataException("更新包超过签名清单指定的大小。");
                    hash.AppendData(buffer,0,count);
                    await output.WriteAsync(buffer.AsMemory(0,count),cancellation).ConfigureAwait(false);
                }
                await output.FlushAsync(cancellation).ConfigureAwait(false);
            }
            if(total!=manifest.Size||!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(),Convert.FromHexString(manifest.Sha256)))
                throw new InvalidDataException("更新包 SHA-256 校验失败。");
            File.Move(partial,installer);
            return installer;
        }
        catch
        {
            try{File.Delete(partial);Directory.Delete(directory);}catch(IOException){}catch(UnauthorizedAccessException){}
            throw;
        }
    }
}
