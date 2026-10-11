using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FrameSpool
{
    internal sealed class AppUpdate
    {
        public string Version,Url,Hash,Name;
        public long Size;
    }
    internal sealed class UpdatePlan
    {
        public string Target { get; set; }
        public string Payload { get; set; }
        public string Hash { get; set; }
        public string Version { get; set; }
        public bool Installed { get; set; }
        public int ParentId { get; set; }
        public long ParentStarted { get; set; }
        public bool Restart { get; set; } = true;
        public bool Quiet { get; set; }
    }
    internal static class AppUpdates
    {
        internal const string Repository="https://github.com/BanVictor17/FrameSpool";
        internal const long MaximumDownload=256*1024*1024;
        [DllImport("msi.dll",EntryPoint="MsiEnumRelatedProductsW",CharSet=CharSet.Unicode)]static extern uint RelatedProducts(string upgrade,uint reserved,uint index,StringBuilder product);
        [DllImport("msi.dll",EntryPoint="MsiQueryProductStateW",CharSet=CharSet.Unicode)]static extern int ProductState(string product);
        internal static Version ParseVersion(string value)
        {
            if(value==null)throw new InvalidDataException("The release version is missing.");
            var version=System.Version.Parse(value.TrimStart('v'));
            return new Version(version.Major,version.Minor,Math.Max(0,version.Build),Math.Max(0,version.Revision));
        }
        internal static bool IsInstalled(string executable)
        {
            using(var key=Registry.CurrentUser.OpenSubKey(@"Software\FrameSpool\Installer"))
            {
                string directory=key?.GetValue("InstallDirectory") as string;
                if(String.IsNullOrEmpty(directory)||!String.Equals(Path.GetFullPath(Path.Combine(directory,"FrameSpool.exe")),Path.GetFullPath(executable),StringComparison.OrdinalIgnoreCase))return false;
                for(uint i=0;i<256;i++)
                {var product=new StringBuilder(39);if(RelatedProducts("{AD465D70-5B51-4F54-B655-F75F1D68422A}",0,i,product)!=0)break;if(ProductState(product.ToString())==5)return true;}
                return false;
            }
        }
        internal static HttpClient Client()
        {
            var client=new HttpClient { Timeout=TimeSpan.FromMinutes(10) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FrameSpool/"+Application.ProductVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }
        internal static AppUpdate ReadRelease(string json,bool installed,Version current)
        {
            using(var document=JsonDocument.Parse(json))
            {
                var root=document.RootElement;
                if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())return null;
                var tag=root.GetProperty("tag_name").GetString();
                var version=ParseVersion(tag);if(version<=current)return null;
                // Tags and names have a fixed contract, so the downloader cannot
                // be redirected to an unrelated repository by malformed metadata.
                if(tag!="v"+version.ToString(3))throw new InvalidDataException("Unsupported release tag.");
                string name="FrameSpool-"+version.ToString(3)+"-win-x64."+(installed?"msi":"exe");
                foreach(var asset in root.GetProperty("assets").EnumerateArray())
                {
                    if(asset.GetProperty("name").GetString()!=name)continue;
                    string url=asset.GetProperty("browser_download_url").GetString();
                    string digest=asset.GetProperty("digest").GetString();long size=asset.GetProperty("size").GetInt64();
                    if(url!=Repository+"/releases/download/"+tag+"/"+name||digest==null||!digest.StartsWith("sha256:",StringComparison.Ordinal)||!ValidHash(digest.Substring(7))||size<=0||size>MaximumDownload)
                        throw new InvalidDataException("The release download or checksum is invalid.");
                    return new AppUpdate { Version=version.ToString(3),Name=name,Url=url,Hash=digest.Substring(7),Size=size };
                }
                throw new InvalidDataException("This release has no "+(installed?"MSI installer":"portable EXE")+" for Windows x64 yet. Check its GitHub release page.");
            }
        }
        internal static async Task<AppUpdate> Check(HttpClient client,bool installed,Version current,CancellationToken token)
        {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(25));
                using(var response=await client.GetAsync("https://api.github.com/repos/BanVictor17/FrameSpool/releases/latest",HttpCompletionOption.ResponseHeadersRead,timeout.Token))
                {
                    response.EnsureSuccessStatusCode();
                    using(var stream=await response.Content.ReadAsStreamAsync(timeout.Token))
                    using(var output=new MemoryStream())
                    {
                        var buffer=new byte[8192];int count;
                        while((count=await stream.ReadAsync(buffer,timeout.Token))>0){if(output.Length+count>1024*1024)throw new InvalidDataException("Release metadata is too large.");output.Write(buffer,0,count);}
                        return ReadRelease(Encoding.UTF8.GetString(output.ToArray()),installed,current);
                    }
                }
            }
        }
        internal static async Task Download(HttpClient client,AppUpdate update,string path,IProgress<int> progress,CancellationToken token)
        {
            bool ownsDownload=false;
            try
            {
                using(var response=await client.GetAsync(update.Url,HttpCompletionOption.ResponseHeadersRead,token))
                {
                    response.EnsureSuccessStatusCode();
                    if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength!=update.Size)throw new InvalidDataException("The download size differs from the release.");
                    using(var input=await response.Content.ReadAsStreamAsync(token))
                    using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,true))
                    {
                        ownsDownload=true;
                        var buffer=new byte[65536];long total=0;int count;
                        while((count=await input.ReadAsync(buffer,token))>0)
                        {total+=count;if(total>update.Size)throw new InvalidDataException("The download exceeds its declared size.");await output.WriteAsync(buffer.AsMemory(0,count),token);progress?.Report((int)(total*100/update.Size));}
                        if(total!=update.Size)throw new InvalidDataException("The update download is incomplete.");
                    }
                }
                await Task.Run(()=>VerifyHash(path,update.Hash),token);
            }
            catch{if(ownsDownload&&File.Exists(path))File.Delete(path);throw;}
        }
        internal static bool ValidHash(string hash) { return hash!=null&&hash.Length==64&&hash.All(Uri.IsHexDigit); }
        internal static void VerifyHash(string path,string hash)
        {
            if(!ValidHash(hash))throw new InvalidDataException("Invalid update checksum.");
            using(var stream=File.OpenRead(path))if(!String.Equals(Convert.ToHexString(SHA256.HashData(stream)),hash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The update checksum does not match GitHub. Your current app was kept.");
        }
        internal static void LaunchHelper(string directory,UpdatePlan plan)
        {
            string helper=Path.Combine(directory,"FrameSpool.Update.exe"),file=Path.Combine(directory,"update.json");
            File.Copy(Environment.ProcessPath,helper,false);File.WriteAllText(file,JsonSerializer.Serialize(plan));
            var start=new ProcessStartInfo(helper){UseShellExecute=false,WorkingDirectory=directory,CreateNoWindow=true};
            start.ArgumentList.Add("--apply-update");start.ArgumentList.Add(file);
            using(var process=Process.Start(start)){if(process==null)throw new IOException("The updater could not start.");}
        }
        internal static void ReplacePortable(string payload,string target,string hash,Action afterSwap=null)
        {
            VerifyHash(payload,hash);
            string staged=target+".update-"+Guid.NewGuid().ToString("N"),backup=target+".previous-"+Guid.NewGuid().ToString("N");bool swapped=false;
            try
            {
                File.Copy(payload,staged,false);VerifyHash(staged,hash);
                File.Replace(staged,target,backup);swapped=true;
                VerifyHash(target,hash);afterSwap?.Invoke();
            }
            catch
            {
                if(swapped&&File.Exists(backup))File.Replace(backup,target,null);
                throw;
            }
            finally { if(File.Exists(staged))File.Delete(staged); }
            try{File.Delete(backup);}catch(IOException){ /* A scanner may still hold the previous file. */ }
        }
        static void Restart(string target)
        {using(var process=Process.Start(new ProcessStartInfo(target){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(target)})){if(process==null)throw new IOException("The updated application could not start.");}}
        internal static string MsiArguments(UpdatePlan plan,string log)
        {
            // MSI parses PROPERTY="value" itself. ArgumentList quotes the whole
            // PROPERTY=value token and displays Installer help for paths with spaces.
            Func<string,string> quote=value=>
            {
                if(value.IndexOf('"')>=0)throw new InvalidDataException("Invalid installer path.");
                if(value.EndsWith("\\",StringComparison.Ordinal))value+=".";
                return "\""+value+"\"";
            };
            return "/i "+quote(plan.Payload)+" APPDIR="+quote(Path.GetDirectoryName(plan.Target))+" "+(plan.Quiet?"/qn":"/passive")+" /norestart /L*v "+quote(log+".msi.log");
        }
        internal static int Apply(string planFile)
        {
            UpdatePlan plan=null;string log=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(planFile)),"update.log");
            try
            {
                plan=JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planFile));
                if(plan==null||!File.Exists(plan.Target)||!File.Exists(plan.Payload)||!ValidHash(plan.Hash))throw new InvalidDataException("The update plan is incomplete.");
                if(FileVersionInfo.GetVersionInfo(plan.Target).ProductName!="FrameSpool")throw new InvalidDataException("The update target is not FrameSpool.");
                VerifyHash(plan.Payload,plan.Hash);ParseVersion(plan.Version);
                try
                {
                    using(var parent=Process.GetProcessById(plan.ParentId))
                    {
                        try
                        {
                            if(parent.StartTime.ToUniversalTime().Ticks!=plan.ParentStarted||!String.Equals(parent.MainModule.FileName,plan.Target,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("The running application differs from the update target.");
                            if(!parent.WaitForExit(120000))throw new IOException("Close FrameSpool before installing the update.");
                        }
                        catch(InvalidOperationException) when(parent.HasExited){ }
                    }
                }
                catch(ArgumentException){ /* The original process has already exited. */ }
                if(plan.Installed)
                {
                    if(!IsInstalled(plan.Target))throw new InvalidDataException("This installed application is no longer registered with Windows Installer.");
                    var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"msiexec.exe")){UseShellExecute=false,CreateNoWindow=true};
                    start.Arguments=MsiArguments(plan,log);
                    using(var installer=Process.Start(start))
                    {installer.WaitForExit();if(installer.ExitCode!=0&&installer.ExitCode!=3010)throw new IOException("Windows Installer returned "+installer.ExitCode+". See "+log+".msi.log");}
                    if(ParseVersion(FileVersionInfo.GetVersionInfo(plan.Target).FileVersion)!=ParseVersion(plan.Version))throw new InvalidDataException("The installed application does not match the downloaded version.");
                    if(plan.Restart)Restart(plan.Target);
                }
                else
                {
                    if(ParseVersion(FileVersionInfo.GetVersionInfo(plan.Payload).FileVersion)!=ParseVersion(plan.Version))throw new InvalidDataException("The portable application version does not match the release.");
                    ReplacePortable(plan.Payload,plan.Target,plan.Hash,plan.Restart?()=>Restart(plan.Target):null);
                }
                File.WriteAllText(log,"Successfully updated FrameSpool to "+plan.Version);return 0;
            }
            catch(Exception ex)
            {
                File.WriteAllText(log,ex.ToString());
                if(plan?.Restart==true){MessageBox.Show("Update could not finish: "+ex.Message+"\r\n\r\nLog: "+log,"FrameSpool Update",MessageBoxButtons.OK,MessageBoxIcon.Error);try{if(File.Exists(plan.Target))Restart(plan.Target);}catch{}}
                return 1;
            }
        }
    }
}
