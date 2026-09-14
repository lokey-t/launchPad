using System.Text;
using System.Text.Json;
using LaunchPad.Setup;

static class Program
{
    [STAThread]static int Main(string[] args)
    {
        // Runs from NSIS's private extraction folder using the application runtime once.
        if(args.Length!=4)return 2;
        string target=args[0],source=AppContext.BaseDirectory,status=args[1];
        bool desktop=args[2]=="1",test=args[3]=="test";
        void Report(string message)=>File.WriteAllText(status,message,Encoding.Unicode);
        try
        {
            if(test && !Path.GetFullPath(target).StartsWith(Path.Combine(Path.GetTempPath(),"LaunchPad-NSISTest-"),StringComparison.OrdinalIgnoreCase))throw new IOException("InvalidDirectory");
            var manifest=JsonSerializer.Deserialize<PayloadManifest>(File.ReadAllText(Path.Combine(source,"payload.json")).TrimStart('\uFEFF'));
            InstallEngine.InstallAsync(target,manifest,null,null,CancellationToken.None,test?(_,_)=>Task.CompletedTask:null,source).GetAwaiter().GetResult();
            if(!test)try{InstallationDiscovery.Register(Path.GetFullPath(target),Version.Parse(manifest.Version),desktop);}catch{Report("ShortcutWarning");return 0;}
            Report("OK");return 0;
        }
        catch(Exception e){Report(e.Message);return 1;}
    }
}
