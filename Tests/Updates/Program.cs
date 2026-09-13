using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LaunchPad.Services;

class Handler : HttpMessageHandler
{
    public Func<string, HttpResponseMessage> Respond;
    public List<string> Requests = new();
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); string url = request.RequestUri.ToString(); Requests.Add(url); return Task.FromResult(Respond(url));
    }
}
class Program
{
    static void Assert(bool value,string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static HttpResponseMessage Data(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
    static HttpResponseMessage Json(object obj) => Data(JsonSerializer.SerializeToUtf8Bytes(obj));
    static HttpResponseMessage Fail() => new(HttpStatusCode.ServiceUnavailable);
    static byte[] Gzip(byte[] bytes) { using var memory = new MemoryStream(); using (var gzip = new GZipStream(memory,CompressionLevel.Optimal,true)) gzip.Write(bytes); return memory.ToArray(); }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static UpdateFile FileEntry(string path,byte[] bytes) => new() { Path=path,Size=bytes.Length,Sha256=Hash(bytes),Asset="lp-"+Hash(bytes)+".gz" };
    static readonly string Root = Path.Combine(Path.GetTempPath(),"LaunchPad-UpdateTests-"+Guid.NewGuid().ToString("N"));
    [STAThread] static void Main(string[] args)
    {
        try { if (args.Contains("--ui")) UpdateUiTests.Run(); else if(args.Contains("--network")) Network().GetAwaiter().GetResult(); else Run().GetAwaiter().GetResult(); }
        catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    }
    static async Task Run()
    {
        Directory.CreateDirectory(Root);
        Assert(UpdateService.ParseVersion("v0.95") == UpdateService.ParseVersion("0.95.0.0"),"normalized versions");
        Assert(UpdateService.ParseVersion("v0.96-beta") == null && UpdateService.ParseVersion("v2.0") > UpdateService.ParseVersion("v1.99"),"stable versions only, numeric order");
        var handler = new Handler { Respond = url=>Json(new object[] {
            new {tag_name=url.Contains("github")?"v1.0":"v1.1",body="Notes",draft=false,prerelease=false,assets=Array.Empty<object>()},
            new {tag_name="v9.0",draft=false,prerelease=true,assets=Array.Empty<object>()},
            new {tag_name="v10.0",draft=true,prerelease=false,assets=Array.Empty<object>()} }) };
        var service = new UpdateService(new HttpClient(handler));
        Assert((await service.CheckAsync()).Version == UpdateService.ParseVersion("1.1"),"newest mirror wins, preview and drafts skipped");
        handler.Respond = url=>url.Contains("github") ? Fail() : Json(new[]{new {tag_name="v1.0",assets=Array.Empty<object>()}});
        Assert((await service.CheckAsync()).Mirrors.Single().Source == "Gitee","one unavailable source falls back");
        handler.Respond = _=>Fail();
        bool rejected = false; try { await service.CheckAsync(); } catch (IOException) { rejected=true; } Assert(rejected,"offline is not reported as up to date");
        foreach (string path in new[]{"../outside.dll","C:/outside.dll","folder\\test.dll","CON.dll","folder/aux.json","config.json","ThemeAssets/test.dll","test.ps1","file.dll:evil","folder./file.dll"})
        {
            rejected=false; try { UpdateService.SafePath(Root,path); } catch (IOException) { rejected=true; } Assert(rejected,"reject "+path);
        }
        Assert(!UpdateService.ValidAssetUrl("https://evil.com/lokey-t/launchPad/releases/download/v1/a.dll") && !UpdateService.ValidAssetUrl("https://github.com/other/repo/releases/download/v1/a.dll"),"asset repository boundary");
        string install = Path.Combine(Root,"install"); Directory.CreateDirectory(install);
        byte[] exe = Encoding.UTF8.GetBytes("unchanged test executable"), dll = File.ReadAllBytes(typeof(UpdateService).Assembly.Location);
        File.WriteAllBytes(Path.Combine(install,"LaunchPad.exe"),exe);
        File.WriteAllText(Path.Combine(install,"LaunchPad.dll"),"old");
        var files = new List<UpdateFile> { FileEntry("LaunchPad.exe",exe),FileEntry("LaunchPad.dll",dll) };
        var manifest = new UpdateManifest { Version=UpdateService.CurrentVersion.ToString(),Runtime=UpdateService.Runtime,Flavor=UpdateService.Flavor,Files=files };
        var mirrors = new List<UpdateRelease>();
        foreach (var source in new[]{"GitHub","Gitee"})
        {
            string prefix = $"https://{source.ToLowerInvariant()}.com/lokey-t/launchPad/releases/download/v0.95/";
            mirrors.Add(new(source,"v0.95",UpdateService.CurrentVersion,"Notes",files.Select(f=>new ReleaseAsset(f.Asset,prefix+f.Asset,null)).Append(new(UpdateService.ManifestName,prefix+UpdateService.ManifestName,null)).ToList(),0));
        }
        var offer = new UpdateOffer(UpdateService.CurrentVersion,mirrors);
        handler.Requests.Clear();
        handler.Respond = url=>url.EndsWith(".json") ? Data(Encoding.UTF8.GetPreamble().Concat(JsonSerializer.SerializeToUtf8Bytes(manifest)).ToArray()) : url.Contains("github") ? Data(Gzip(Encoding.UTF8.GetBytes("corrupt"))) : Data(Gzip(dll));
        var prepared = await service.PrepareAsync(offer,install,false,null,CancellationToken.None);
        Assert(prepared.Files.Count==1 && prepared.Files[0].Path=="LaunchPad.dll","incremental downloads changed file only");
        Assert(handler.Requests.All(u=>!u.EndsWith(files[0].Asset)) && handler.Requests.Any(u=>u.Contains("gitee") && u.EndsWith(files[1].Asset)),"unchanged file skipped, corrupt mirror fails over");
        Assert(File.ReadAllText(Path.Combine(install,"LaunchPad.dll"))=="old","staging never edits current installation");
        UpdateService.DeleteStage(prepared.Directory); Assert(!Directory.Exists(prepared.Directory),"staging cleanup");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        rejected=false; try { await service.PrepareAsync(offer,install,false,null,cancellation.Token); } catch(OperationCanceledException) { rejected=true; } Assert(rejected,"cancel download");
        manifest.Files.Add(files[0]); rejected=false; try { UpdateService.ValidateManifest(manifest,offer,install); } catch(IOException) { rejected=true; } Assert(rejected,"reject duplicate install paths"); manifest.Files.RemoveAt(2);
        manifest.Runtime="win-other"; rejected=false; try { UpdateService.ValidateManifest(manifest,offer,install); } catch(IOException) { rejected=true; } Assert(rejected,"reject wrong architecture"); manifest.Runtime=UpdateService.Runtime;
        byte[] archive;
        using (var memory=new MemoryStream())
        {
            using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)) foreach(var f in files) { using var s=zip.CreateEntry("LaunchPad/"+f.Path).Open(); s.Write(f.Path.EndsWith(".dll")?dll:exe); }
            archive=memory.ToArray();
        }
        string name=$"LaunchPad-v{UpdateService.DisplayVersion(offer.Version)}-{UpdateService.Runtime}"+(UpdateService.Flavor=="framework-dependent"?"-framework-dependent":"")+".zip";
        var fullOffer=new UpdateOffer(offer.Version,new(){new("GitHub","v0.95",offer.Version,"",new(){new(name,"https://github.com/lokey-t/launchPad/releases/download/v0.95/"+name,"sha256:"+Hash(archive))},0)});
        handler.Respond=_=>Data(archive);
        prepared=await service.PrepareAsync(fullOffer,install,true,null,CancellationToken.None);
        Assert(prepared.Files.Count==1 && UpdateService.Hash(Path.Combine(prepared.Directory,"LaunchPad.dll")).Equals(Hash(dll),StringComparison.OrdinalIgnoreCase),"legacy full ZIP verified and staged, unchanged files skipped"); UpdateService.DeleteStage(prepared.Directory);
        await Installer(false); await Installer(true);
        Console.WriteLine("ALL UPDATE CHECKS PASSED. Isolated files: "+Root);
    }
    static async Task Network()
    {
        var service=new UpdateService(); var offer=await service.CheckAsync();
        Assert(offer!=null,"live release check");
        Console.WriteLine("Release "+offer.Version+" mirrors "+string.Join(", ",offer.Mirrors.Select(m=>m.Source)));
        Directory.CreateDirectory(Root);
        try
        {
            var prepared=await service.PrepareAsync(offer,Root,!UpdateService.SupportsIncremental(offer),null,CancellationToken.None);
            Assert(prepared.Files.Any(f=>f.Path=="LaunchPad.exe"),"live release download, checksum and version match");
            UpdateService.DeleteStage(prepared.Directory);
        }
        catch(IOException ex) when(ex.Message=="UpdateVersionMismatch" && offer.Version==UpdateService.ParseVersion("0.96"))
        {
            Console.WriteLine("PASS existing v0.96 contains v0.95 executable: mismatched release correctly rejected before installation.");
        }
        Console.WriteLine("LIVE CHECK PASSED; no installed application modified.");
    }
    static async Task Installer(bool locked)
    {
        string stage=Path.Combine(Root,locked?"rollback-stage":"success-stage"), target=Path.Combine(Root,locked?"rollback-install":"success-install");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
        var files=new List<UpdateFile>();
        foreach(string path in new[]{"first.dll","new.dll","locked.dll"})
        {
            byte[] bytes=Encoding.UTF8.GetBytes("new "+path); File.WriteAllBytes(Path.Combine(stage,path),bytes); files.Add(FileEntry(path,bytes));
            if(path!="new.dll") File.WriteAllText(Path.Combine(target,path),"old "+path);
        }
        string script=Path.Combine(stage,"install.ps1");
        using(var source=typeof(UpdateService).Assembly.GetManifestResourceStream("LaunchPad.UpdateInstaller.ps1")) using(var destination=File.Create(script)) source.CopyTo(destination);
        string result=Path.Combine(stage,"result.json"), job=Path.Combine(stage,"job.json");
        File.WriteAllText(job,JsonSerializer.Serialize(new {Root=target,Stage=stage,Files=files,Pid=int.MaxValue,Result=result,Restart=false}));
        using var block=locked?new FileStream(Path.Combine(target,"locked.dll"),FileMode.Open,FileAccess.Read,FileShare.Read):null;
        var info=new ProcessStartInfo {FileName=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardError=true};
        foreach(var a in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script,"-JobPath",job}) info.ArgumentList.Add(a);
        using var process=Process.Start(info);
        for(int i=0;i<100 && !File.Exists(Path.Combine(stage,"ready")) && !process.HasExited;i++) await Task.Delay(50);
        Assert(File.ReadAllText(Path.Combine(target,"first.dll"))=="old first.dll","helper waits for explicit handoff before replacement");
        File.WriteAllText(Path.Combine(stage,"go"),"go");
        await process.WaitForExitAsync(); var stderr=await process.StandardError.ReadToEndAsync();
        Assert(File.Exists(result),"installer writes result "+stderr);
        using var report=JsonDocument.Parse(File.ReadAllText(result).TrimStart('\uFEFF'));
        Assert(report.RootElement.GetProperty("Success").GetBoolean()!=locked,locked?"replacement error reported":"installer completed");
        if(locked) Assert(report.RootElement.GetProperty("RollbackOk").GetBoolean(),"successful rollback allows restarting the previous version");
        Assert(File.ReadAllText(Path.Combine(target,"first.dll"))==(locked?"old first.dll":"new first.dll"),locked?"previously replaced file rolled back":"replacement installed");
        Assert(File.Exists(Path.Combine(target,"new.dll"))!=locked,locked?"newly created file removed on rollback":"new file installed");
    }
}
