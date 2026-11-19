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
        const string fingerprint="c4d71686b4a64a3db2a530087fd161c8";
        byte[] manifest=JsonSerializer.SerializeToUtf8Bytes(new ReleaseUpdates.Manifest("1.9.1","MacDock-1.9.1-win-x64-Setup.exe",payload.Length,Convert.ToHexString(SHA256.HashData(payload)),"Fixture",BuildSequence:20,BuildFingerprint:fingerprint,PayloadSha256:new string('a',64)));
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
            var sameVersion=new Version(1,9,1);
            ReleaseUpdates Installed(long sequence,string identity)=>new("fixture/MacDock",key.ExportSubjectPublicKeyInfo(),http)
                {LocalBuildSequence=sequence,LocalBuildFingerprint=identity};
            var revised=Installed(19,Guid.NewGuid().ToString("N"));
            Check("same version newer signed build", (await revised.Check(sameVersion,CancellationToken.None))?.IsRevision==true);
            var current=Installed(20,fingerprint);
            Check("installed revision is ignored",await current.Check(sameVersion,CancellationToken.None)==null);
            Check("repeated installed revision check is quiet",await current.Check(sameVersion,CancellationToken.None)==null);
            var newer=Installed(21,Guid.NewGuid().ToString("N"));
            Check("different hash cannot roll back newer build",await newer.Check(sameVersion,CancellationToken.None)==null);
            Check("higher version with older build sequence cannot update",await newer.Check(old,CancellationToken.None)==null);
            Check("lower version cannot update",await updates.Check(new Version(1,9,2),CancellationToken.None)==null);
            Check("equal sequence with a conflicting fingerprint is rejected",await Rejected(()=>Installed(20,Guid.NewGuid().ToString("N")).Check(sameVersion,CancellationToken.None)));
            Check("unverified copy cannot be downloaded",await Rejected(()=>updates.Download(valid with { Notes="unverified" },cache,CancellationToken.None)));
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
            offline=false;
            const string fourthPart="1.9.9.1";
            tag="v"+fourthPart;
            manifest=JsonSerializer.SerializeToUtf8Bytes(new ReleaseUpdates.Manifest(fourthPart,
                $"MacDock-{fourthPart}-win-x64-Setup.exe",payload.Length,Convert.ToHexString(SHA256.HashData(payload)),
                "Fixture",BuildSequence:20,BuildFingerprint:fingerprint,PayloadSha256:new string('a',64)));
            signature=key.SignData(manifest,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            var fourthPartUpdate=await updates.Check(new Version(1,9,9),CancellationToken.None);
            Check("signed four-part release is a version upgrade",fourthPartUpdate is {Version:fourthPart,IsRevision:false});
            Check("four-part installer downloads under its exact tag",fourthPartUpdate!=null&&
                File.ReadAllBytes(await updates.Download(fourthPartUpdate,cache,CancellationToken.None)).SequenceEqual(payload));
            Check("matching four-part build is quiet",await current.Check(new Version(1,9,9,1),CancellationToken.None)==null);
            tag="v1.9.9.2";
            Check("four-part version mismatching the signed manifest is rejected",await Rejected(()=>updates.Check(old,CancellationToken.None)));
        }
        catch(Exception e){tests.Add(new Result("exception",false,e.ToString()));}
        File.WriteAllText(Path.Combine(output,"update-checks.json"),JsonSerializer.Serialize(tests,new JsonSerializerOptions{WriteIndented=true}));
        return tests.All(t=>t.pass)?0:1;
    }
}
