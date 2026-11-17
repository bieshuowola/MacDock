using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace MacDock;

internal static class UpdateChecks
{
    sealed record Result(string name,bool pass,string? detail=null);
    sealed class Responses(Func<Uri,HttpResponseMessage> respond):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            =>Task.FromResult(respond(request.RequestUri!));
    }

    internal static async Task<int> Run()
    {
        string output=Environment.GetEnvironmentVariable("MACDOCK_TEST_RESULTS")??AppContext.BaseDirectory;
        Directory.CreateDirectory(output);
        var tests=new List<Result>();
        void Check(string name,bool pass)=>tests.Add(new Result(name,pass));
        var cache=Path.Combine(output,"update-fixtures-"+Guid.NewGuid().ToString("N"));
        using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] payload="MacDock test package; never executed"u8.ToArray();
        byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new ReleaseUpdates.Manifest("1.9.1","MacDock-1.9.1-win-x64-Setup.exe",payload.Length,Convert.ToHexString(SHA256.HashData(payload)),"Fixture"));
        byte[] signature=key.SignData(manifest,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        HttpResponseMessage Bytes(byte[] data)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(data)};
        var tag="v1.9.1";
        bool prerelease=false,tamperManifest=false,tamperPackage=false,wrongRedirect=false,offline=false;
        using var http=new HttpClient(new Responses(uri=>
        {
            if(offline)throw new HttpRequestException("Offline fixture");
            if(uri.IdnHost=="api.github.com")return Bytes(JsonSerializer.SerializeToUtf8Bytes(new{tag_name=tag,draft=false,prerelease}));
            if(uri.AbsolutePath.EndsWith(".json"))return Bytes(tamperManifest?manifest.Concat(new byte[]{32}).ToArray():manifest);
            if(uri.AbsolutePath.EndsWith(".sig"))return Bytes(signature);
            if(wrongRedirect)return new(HttpStatusCode.Redirect){Headers={Location=new Uri("https://example.com/setup.exe")}};
            return Bytes(tamperPackage?new byte[payload.Length]:payload);
        }));
        var updates=new ReleaseUpdates("fixture/MacDock",key.ExportSubjectPublicKeyInfo(),http);
        var old=new Version(1,9,0);
        async Task<bool> Rejected(Func<Task> action)
        {
            try{await action();return false;}
            catch(Exception e) when(e is InvalidDataException or HttpRequestException){return true;}
        }
        try
        {
            var valid=await updates.Check(old,CancellationToken.None);
            Check("new stable signed release accepted",valid?.Version=="1.9.1");
            if(valid==null)throw new InvalidDataException("Test release was not returned.");
            var path=await updates.Download(valid,cache,CancellationToken.None);
            Check("verified installer is byte identical",File.ReadAllBytes(path).SequenceEqual(payload));
            Check("same version does not update",await updates.Check(new Version(1,9,1),CancellationToken.None)==null);
            var revUpdatedAt=DateTimeOffset.UtcNow;
            var revSums=System.Text.Encoding.UTF8.GetBytes($"{Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}  MacDock-1.9.1-win-x64-Setup.exe\n");
            using var revHttp=new HttpClient(new Responses(uri=>
            {
                if(uri.IdnHost=="api.github.com")return Bytes(JsonSerializer.SerializeToUtf8Bytes(new{tag_name="v1.9.1",draft=false,prerelease=false,body="修订说明",assets=new[]{new{name="MacDock-1.9.1-win-x64-Setup.exe",size=(long)payload.Length,updated_at=revUpdatedAt},new{name="SHA256SUMS.txt",size=(long)revSums.Length,updated_at=revUpdatedAt}}}));
                if(uri.AbsolutePath.EndsWith("SHA256SUMS.txt"))return Bytes(revSums);
                return Bytes(payload);
            }));
            var revUpdates=new ReleaseUpdates("fixture/MacDock",key.ExportSubjectPublicKeyInfo(),revHttp){LocalBuildTimeUtc=revUpdatedAt.AddMinutes(-10)};
            var rev=await revUpdates.Check(new Version(1,9,1),CancellationToken.None);
            Check("same version newer revision detected",rev!=null&&rev.IsRevision&&rev.Version=="1.9.1");
            var binDir=Path.Combine(output,"update-bin-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(binDir);
            var binFixture=Path.Combine(binDir,"MacDock.dll");
            File.WriteAllBytes(binFixture,"old-binary-bytes"u8.ToArray());
            var newBinBytes="new-binary-bytes"u8.ToArray();
            var binSums=System.Text.Encoding.UTF8.GetBytes($"{Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()}  MacDock-1.9.1-win-x64-Setup.exe\n{Convert.ToHexString(SHA256.HashData(newBinBytes)).ToLowerInvariant()}  MacDock.dll\n");
            using var fastRevHttp=new HttpClient(new Responses(uri=>
            {
                if(uri.IdnHost=="api.github.com")return Bytes(JsonSerializer.SerializeToUtf8Bytes(new{tag_name="v1.9.1",draft=false,prerelease=false,body="短间隔覆盖修订",assets=new[]{new{name="MacDock-1.9.1-win-x64-Setup.exe",size=(long)payload.Length,updated_at=revUpdatedAt.AddSeconds(5)},new{name="SHA256SUMS.txt",size=(long)binSums.Length,updated_at=revUpdatedAt.AddSeconds(5)}}}));
                if(uri.AbsolutePath.EndsWith("SHA256SUMS.txt"))return Bytes(binSums);
                return Bytes(payload);
            }));
            var fastRevUpdates=new ReleaseUpdates("fixture/MacDock",key.ExportSubjectPublicKeyInfo(),fastRevHttp){LocalBuildTimeUtc=revUpdatedAt,LocalBinaryPath=binFixture};
            var fastRev=await fastRevUpdates.Check(new Version(1,9,1),CancellationToken.None);
            Check("same version short-interval revision detected via binary hash",fastRev!=null&&fastRev.IsRevision&&fastRev.Version=="1.9.1");
            File.WriteAllBytes(binFixture,newBinBytes);
            var sameRev=await fastRevUpdates.Check(new Version(1,9,1),CancellationToken.None);
            Check("same version matching binary hash is ignored even if timestamp newer",sameRev==null);
            prerelease=true;
            Check("prerelease ignored",await updates.Check(old,CancellationToken.None)==null);
            prerelease=false;tamperManifest=true;
            Check("tampered manifest rejected",await Rejected(()=>updates.Check(old,CancellationToken.None)));
            tamperManifest=false;tamperPackage=true;
            Check("tampered installer rejected",await Rejected(()=>updates.Download(valid,cache,CancellationToken.None)));
            tamperPackage=false;wrongRedirect=true;
            Check("non-GitHub redirect rejected",await Rejected(()=>updates.Download(valid,cache,CancellationToken.None)));
            wrongRedirect=false;offline=true;
            Check("offline failure returned without an installer",await Rejected(()=>updates.Check(old,CancellationToken.None)));
            Check("no partial or unverified installers left",Directory.GetFiles(cache,"*",SearchOption.AllDirectories).SequenceEqual(new[]{path}));
        }
        catch(Exception e){tests.Add(new Result("exception",false,e.ToString()));}
        File.WriteAllText(Path.Combine(output,"update-checks.json"),JsonSerializer.Serialize(tests,new JsonSerializerOptions{WriteIndented=true}));
        return tests.All(t=>t.pass)?0:1;
    }
}
