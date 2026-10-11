using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FrameSpool
{
    internal static class UpdateVerification
    {
        sealed class ResponseHandler : HttpMessageHandler
        {
            readonly byte[] data;
            public ResponseHandler(byte[] bytes) { data=bytes; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {token.ThrowIfCancellationRequested();return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(data)});}
        }
        internal static void Run(string directory,List<VerificationCheck> checks)
        {
            Action<string,bool> check=(name,passed)=>checks.Add(new VerificationCheck{Name=name,Passed=passed,Detail="Isolated updater fixtures"});
            string folder=Path.Combine(directory,"updater-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            byte[] data=Encoding.UTF8.GetBytes("verified release fixture");string hash=Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            Func<bool,string,string> release=(installed,url)=>JsonSerializer.Serialize(new {draft=false,prerelease=false,tag_name="v9.8.7",assets=new[]{new {name="FrameSpool-9.8.7-win-x64."+(installed?"msi":"exe"),browser_download_url=url,size=data.Length,digest="sha256:"+hash}}});
            string exeUrl=AppUpdates.Repository+"/releases/download/v9.8.7/FrameSpool-9.8.7-win-x64.exe";
            string msiUrl=exeUrl.Replace(".exe",".msi");var current=AppUpdates.ParseVersion("0.2.2");
            var portable=AppUpdates.ReadRelease(release(false,exeUrl),false,current);
            check("Updater selects portable EXE and verifies release contract",portable.Name.EndsWith(".exe")&&portable.Hash==hash);
            check("Updater selects MSI for registered installations",AppUpdates.ReadRelease(release(true,msiUrl),true,current).Name.EndsWith(".msi"));
            check("Updater normalizes three and four component versions",AppUpdates.ParseVersion("v0.2.2")==AppUpdates.ParseVersion("0.2.2.0"));
            check("Updater skips current and older releases",AppUpdates.ReadRelease(release(false,exeUrl),false,AppUpdates.ParseVersion("9.8.7"))==null);
            check("Updater skips preview releases",AppUpdates.ReadRelease(release(false,exeUrl).Replace("\"prerelease\":false","\"prerelease\":true"),false,current)==null);
            bool rejected=false;try{AppUpdates.ReadRelease(release(false,"https://example.com/other.exe"),false,current);}catch(InvalidDataException){rejected=true;}
            check("Updater rejects downloads outside the configured GitHub repository",rejected);
            rejected=false;try{AppUpdates.ReadRelease(release(false,exeUrl).Replace("sha256:"+hash,"sha256:bad"),false,current);}catch(InvalidDataException){rejected=true;}
            check("Updater requires a complete SHA256 digest",rejected);
            Task.Run(async ()=>
            {
                using(var client=new HttpClient(new ResponseHandler(data)))
                {
                    var payload=Path.Combine(folder,"payload.bin");await AppUpdates.Download(client,portable,payload,null,CancellationToken.None);
                    check("Updater downloads and verifies complete payloads",File.ReadAllBytes(payload).Length==data.Length);
                    rejected=false;try{await AppUpdates.Download(client,portable,payload,null,CancellationToken.None);}catch(IOException){rejected=true;}
                    check("Download collisions preserve existing files",rejected&&File.ReadAllBytes(payload).Length==data.Length);
                    string target=Path.Combine(folder,"renamed-photo-app.exe");File.WriteAllText(target,"previous version");
                    AppUpdates.ReplacePortable(payload,target,hash);check("Portable update replaces renamed executable atomically",File.ReadAllBytes(target).Length==data.Length);
                    File.WriteAllText(target,"previous version");rejected=false;
                    try{AppUpdates.ReplacePortable(payload,target,hash,()=>{throw new IOException("Simulated restart failure");});}catch(IOException){rejected=true;}
                    check("Failed portable restart restores the previous executable",rejected&&File.ReadAllText(target)=="previous version");
                    rejected=false;try{AppUpdates.ReplacePortable(payload,target,new string('0',64));}catch(InvalidDataException){rejected=true;}
                    check("Corrupt update preserves the original executable",rejected&&File.ReadAllText(target)=="previous version");
                    portable.Hash=new string('0',64);var bad=Path.Combine(folder,"bad.bin");rejected=false;
                    try{await AppUpdates.Download(client,portable,bad,null,CancellationToken.None);}catch(InvalidDataException){rejected=true;}
                    check("Checksum failures remove untrusted download files",rejected&&!File.Exists(bad));
                    using(var cancel=new CancellationTokenSource())
                    {cancel.Cancel();rejected=false;try{await AppUpdates.Download(client,portable,bad,null,cancel.Token);}catch(OperationCanceledException){rejected=true;}check("Cancelled updates keep the app and remove partial downloads",rejected&&!File.Exists(bad)&&File.ReadAllText(target)=="previous version");}
                }
                using(var client=new HttpClient(new ResponseHandler(Encoding.UTF8.GetBytes(release(false,exeUrl)))))
                {var update=await AppUpdates.Check(client,false,current,CancellationToken.None);check("GitHub update check parses a stable release response",update.Version=="9.8.7");}
            }).GetAwaiter().GetResult();
            check("Portable copies never update a separate MSI installation",!AppUpdates.IsInstalled(Path.Combine(folder,"renamed-photo-app.exe")));
        }
    }
}
