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
    internal sealed record Manifest(string Version,string FileName,long Size,string Sha256,string Notes,bool IsRevision=false,
        long BuildSequence=0,string? BuildFingerprint=null,string? PayloadSha256=null);
    internal long LocalBuildSequence { get; init; }
    internal string? LocalBuildFingerprint { get; init; }
    internal string? LocalBinaryPath { get; init; }
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Manifest,object> verified = new();
    static readonly object Verified = new();
    const int MaxMetadata=64*1024;
    const long MaxPackage=250L*1024*1024;
    static readonly Regex RepoName=new(@"\A[A-Za-z0-9][A-Za-z0-9-]*/[A-Za-z0-9_.-]+\z");
    static readonly Regex ReleaseTag=new(@"\Av(0|[1-9][0-9]*)(\.(0|[1-9][0-9]*)){2,3}\z");
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
                string.IsNullOrWhiteSpace(repo)||string.IsNullOrWhiteSpace(key)||
                !long.TryParse(values.GetValueOrDefault("MacDockBuildSequence"),out var sequence)||sequence<=0||
                !Guid.TryParseExact(values.GetValueOrDefault("MacDockBuildFingerprint"),"N",out var fingerprint)||fingerprint==Guid.Empty)
                return null;
            return new ReleaseUpdates(repo,Convert.FromBase64String(key),Http)
            {
                LocalBuildSequence=sequence,LocalBuildFingerprint=fingerprint.ToString("N")
            };
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
            request.Headers.CacheControl=new CacheControlHeaderValue{NoCache=true,NoStore=true};
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
        var bytes=await SmallFile(Asset(tag,"macdock-update.json"),cancellation).ConfigureAwait(false);
        var signature=await SmallFile(Asset(tag,"macdock-update.sig"),cancellation).ConfigureAwait(false);
        var manifest=VerifyManifest(bytes,signature,version);
        if(manifest.BuildSequence<LocalBuildSequence)return null;
        if(manifest.BuildSequence==LocalBuildSequence)
        {
            if(version==installed&&string.Equals(LocalBuildFingerprint,manifest.BuildFingerprint,StringComparison.OrdinalIgnoreCase))
                return null;
            throw new InvalidDataException("构建序号与已安装构建冲突。");
        }
        if(string.Equals(LocalBuildFingerprint,manifest.BuildFingerprint,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新重复使用了已安装构建的指纹。");
        var binary=LocalBinaryPath??typeof(ReleaseUpdates).Assembly.Location;
        if(version==installed&&File.Exists(binary))
        {
            using var file=File.OpenRead(binary);
            if(CryptographicOperations.FixedTimeEquals(SHA256.HashData(file),Convert.FromHexString(manifest.PayloadSha256!)))
                return null;
        }
        manifest=manifest with { IsRevision=version==installed };
        verified.Add(manifest,Verified);
        return manifest;
    }

    Manifest VerifyManifest(byte[] bytes,byte[] signature,Version version)
    {
        using var key=ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey,out var consumed);
        if(consumed!=publicKey.Length||key.KeySize!=256||signature.Length!=64||
            !key.VerifyData(bytes,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            throw new InvalidDataException("更新签名验证失败。");
        var manifest=JsonSerializer.Deserialize<Manifest>(bytes);
        if(manifest==null||manifest.Version!=version.ToString()||manifest.FileName!=$"MacDock-{version}-win-x64-Setup.exe"||
            manifest.Size is <=0 or >MaxPackage||manifest.Sha256?.Length!=64||!manifest.Sha256.All(Uri.IsHexDigit)||manifest.Notes==null||
            manifest.BuildSequence<=0||!Guid.TryParseExact(manifest.BuildFingerprint,"N",out var fingerprint)||fingerprint==Guid.Empty||
            manifest.PayloadSha256?.Length!=64||!manifest.PayloadSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("更新清单格式或版本不符。");
        return manifest;
    }

    internal async Task<string> Download(Manifest manifest,string cache,CancellationToken cancellation)
    {
        if(!verified.TryGetValue(manifest,out _))
            throw new InvalidDataException("下载需要经过签名验证的更新清单。");
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
