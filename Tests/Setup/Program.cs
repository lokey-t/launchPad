using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using LaunchPad.Setup;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

class PreviewApp:SetupApp { protected override void OnStartup(StartupEventArgs e){} protected override void OnExit(ExitEventArgs e){} }

static class Program
{
    static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    static void Assert(bool condition,string label){if(!condition)throw new Exception(label);Console.WriteLine("PASS "+label);}
    [STAThread]static void Main(string[] args){try{if(args.Contains("--ui"))Preview(args[0]);else if(args.Contains("--payload"))FullPayload().GetAwaiter().GetResult();else Run(args[0]).GetAwaiter().GetResult();}catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}}
    static async Task FullPayload()
    {
        var target=Path.Combine(Path.GetTempPath(),"LaunchPad-FullSetup-"+Guid.NewGuid().ToString("N"));
        var manifest=InstallEngine.Manifest();
        await InstallEngine.InstallAsync(target,manifest,InstallEngine.Payload,null,CancellationToken.None,(_,_)=>Task.CompletedTask);
        Assert(manifest.Files.All(f=>InstallEngine.Hash(InstallEngine.SafeFile(target,f.Path)).Equals(f.Sha256,StringComparison.OrdinalIgnoreCase)),"complete embedded application installed and all file hashes verified");
        Assert(!Directory.EnumerateDirectories(target,".launchpad-install-*").Any(),"full installation staging cleaned");
        Console.WriteLine("Isolated application files: "+target);
    }
    static void Pump(int ms){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    static void Preview(string application)
    {
        Console.WriteLine(string.Join(", ",typeof(InstallEngine).Assembly.GetManifestResourceNames()));
        Console.WriteLine("Payload version: "+InstallEngine.Manifest().Version);
        var app=new PreviewApp();
        var resources=System.Xml.Linq.XDocument.Load("Installer/App.xaml").Root.Element(System.Xml.Linq.XName.Get("Application.Resources","http://schemas.microsoft.com/winfx/2006/xaml/presentation"));
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"+string.Concat(resources.Nodes())+"</ResourceDictionary>");
        app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        var window=new SetupWindow();var dir=(TextBox)typeof(SetupWindow).GetField("_directory",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
        var action=(Button)typeof(SetupWindow).GetField("_install",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
        var title=(TextBlock)typeof(SetupWindow).GetField("_title",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
        dir.Text=Path.Combine(Path.GetTempPath(),"LaunchPad-Preview-"+Guid.NewGuid().ToString("N"));window.Show();Pump(350);
        Assert(action.IsEnabled && action.Content.ToString() is "立即安装" or "Install now","embedded payload enables first installation");
        Directory.CreateDirectory("obj/setup-shots");
        void Capture(string name){var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create("obj/setup-shots/"+name+".png");encoder.Save(output);}
        Capture("install");dir.Text=application;Pump(100);
        Assert(action.Content.ToString() is "立即更新" or "Update now","existing installation automatically changes action to update");Capture("update");
        window.Close();app.Shutdown();Console.WriteLine("SETUP UI VERIFIED without installing or registering anything.");
    }
    static async Task Run(string application)
    {
        string root=Path.Combine(Path.GetTempPath(),"LaunchPad-SetupTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string target=Path.Combine(root,"安装目录 with spaces");
        byte[] exe=File.ReadAllBytes(Path.Combine(application,"LaunchPad.exe")),dll=File.ReadAllBytes(Path.Combine(application,"LaunchPad.dll"));
        var data=new Dictionary<string,byte[]>{{"LaunchPad.exe",exe},{"LaunchPad.dll",dll},{"coreclr.dll",Encoding.UTF8.GetBytes("fixture runtime")},{"new.dll",Encoding.UTF8.GetBytes("new file")}};
        byte[] Zip(){using var mem=new MemoryStream();using(var zip=new ZipArchive(mem,ZipArchiveMode.Create,true))foreach(var pair in data){using var file=zip.CreateEntry(pair.Key).Open();file.Write(pair.Value);}return mem.ToArray();}
        var archive=Zip();var manifest=new PayloadManifest{Format=1,Version=AssemblyName.GetAssemblyName(Path.Combine(application,"LaunchPad.dll")).Version.ToString(),Runtime="win-"+RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),Sha256=Hash(archive),Files=data.Select(p=>new PayloadFile{Path=p.Key,Size=p.Value.Length,Sha256=Hash(p.Value)}).ToList()};
        Task Install(CancellationToken token=default,Func<string,CancellationToken,Task> close=null)=>InstallEngine.InstallAsync(target,manifest,()=>new MemoryStream(archive),null,token,close??((_,_)=>Task.CompletedTask));
        await Install();Assert(File.Exists(Path.Combine(target,"LaunchPad.exe")),"fresh installation to a custom Unicode directory");
        Assert(InstallationDiscovery.IsInstallation(target),"legacy executable detection");
        string originalVersion=manifest.Version;manifest.Version="0.1.0.0";bool downgrade=false;try{await Install();}catch(IOException e)when(e.Message=="NewerVersionInstalled"){downgrade=true;}manifest.Version=originalVersion;Assert(downgrade,"installer refuses downgrading a newer application");
        File.WriteAllText(Path.Combine(target,"user-note.txt"),"keep me");File.WriteAllText(Path.Combine(target,"config.json"),"user configuration");
        File.WriteAllText(Path.Combine(target,"coreclr.dll"),"old file");await Install();
        Assert(File.ReadAllText(Path.Combine(target,"coreclr.dll"))=="fixture runtime","same-version repair replaces old files");
        Assert(File.ReadAllText(Path.Combine(target,"user-note.txt"))=="keep me"&&File.ReadAllText(Path.Combine(target,"config.json"))=="user configuration","unrelated files and configuration preserved");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool caught=false;try{await Install(cancelled.Token);}catch(OperationCanceledException){caught=true;}Assert(caught,"cancellation before modifying application");}
        string good=manifest.Sha256;manifest.Sha256=new string('0',64);bool rejected=false;try{await Install();}catch(IOException){rejected=true;}manifest.Sha256=good;Assert(rejected,"corrupted archive rejected");
        foreach(var path in new[]{"../outside.dll","config.json","folder/../../escape.dll","C:/outside.dll","NUL.exe"}){rejected=false;try{InstallEngine.SafeFile(target,path);}catch(IOException){rejected=true;}Assert(rejected,"reject unsafe or user-data payload path: "+path);}
        Assert(InstallationDiscovery.ExecutableFromCommand("\"C:\\Program Files\\LaunchPad\\LaunchPad.exe\" \"%1\"")==@"C:\Program Files\LaunchPad\LaunchPad.exe","legacy quoted path parsing");
        // Share-read allows the rollback copy but prevents atomic replacement, exercising mid-transaction recovery.
        data["LaunchPad.exe"]=exe;data["coreclr.dll"]=Encoding.UTF8.GetBytes("replacement runtime");data.Add("z-locked.dll",Encoding.UTF8.GetBytes("new locked"));
        File.WriteAllText(Path.Combine(target,"z-locked.dll"),"old locked");File.Delete(Path.Combine(target,"new.dll"));
        archive=Zip();manifest.Sha256=Hash(archive);manifest.Files=data.Select(p=>new PayloadFile{Path=p.Key,Size=p.Value.Length,Sha256=Hash(p.Value)}).ToList();
        using(var locked=new FileStream(Path.Combine(target,"z-locked.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            rejected=false;try{await Install();}catch(IOException){rejected=true;}Assert(rejected,"locked target aborts installation");
            Assert(File.ReadAllText(Path.Combine(target,"coreclr.dll"))=="fixture runtime"&&!File.Exists(Path.Combine(target,"new.dll")),"rollback restores overwritten files and removes newly added files");
        }
        Assert(!Directory.GetDirectories(target,".launchpad-install-*").Any(),"staging cleaned after success and rollback");
        string raw=Path.Combine(root,"raw");Directory.CreateDirectory(raw);
        foreach(var pair in data)File.WriteAllBytes(Path.Combine(raw,pair.Key),pair.Value);
        await InstallEngine.InstallAsync(target,manifest,null,null,CancellationToken.None,(_,_)=>Task.CompletedTask,raw);
        Assert(Hash(File.ReadAllBytes(Path.Combine(target,"coreclr.dll")))==Hash(data["coreclr.dll"]),"raw NSIS payload installation");
        File.WriteAllText(Path.Combine(raw,"coreclr.dll"),"corrupt");bool closed=false;rejected=false;
        try{await InstallEngine.InstallAsync(target,manifest,null,null,CancellationToken.None,(_,_)=>{closed=true;return Task.CompletedTask;},raw);}catch(IOException){rejected=true;}
        Assert(rejected&&!closed,"corrupted NSIS payload rejected before closing old application");
        Console.WriteLine("SETUP TESTS PASSED: "+root);
    }
}
