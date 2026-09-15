using System.IO;
using System.IO.Compression;
using System.Text.Json;
using LaunchPad.Plugins;
using LaunchPad.PluginSdk;
class Program
{
 static void Assert(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static async Task Reject(Func<Task> action,string message){bool rejected=false;try{await action();}catch{rejected=true;}Assert(rejected,message);}
 [STAThread] static void Main(string[] args){try{Run(args).GetAwaiter().GetResult();}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
 static async Task Run(string[] args)
 {
  var root=Path.Combine(Path.GetTempPath(),"LaunchPad-PluginTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var service=new PluginService(Path.Combine(root,"plugins"));
  service.Install(Path.Combine(args[0],"Calculator.qdtplugin"));
  Assert(!service.List().Single().State.Enabled,"install never executes or enables plugin");
  await Reject(()=>service.Invoke("sample.calculator",new(){Method="execute",Command="copy"}),"disabled plugin cannot run");
  service.SetEnabled("sample.calculator",true);
  var results=await service.Search("= 12 * 3",default);Assert(results.Single().Result.Title=="36","search invokes independent calculator process");
  var response=await service.Invoke("sample.calculator",new(){Method="execute",Command="copy",Value="36"});Assert(response.Action=="clipboard"&&response.Value=="36","explicit result action uses permission-checked host response");
  service.SetSetting("sample.calculator","digits","2");Assert((await service.Search("= 1 / 3",default)).Single().Result.Title=="0.33","host settings reach plugin");
  service.Install(Path.Combine(args[0],"PathTools.qdtplugin"));service.SetEnabled("sample.path-tools",true);
  response=await service.Invoke("sample.path-tools",new(){Method="execute",Command="copy-path",ContextPath=root});Assert(response.Value==root,"context command receives selected path");
  service.SetSetting("sample.path-tools","folder",root);File.WriteAllText(Path.Combine(root,"hello.txt"),"test");
  Assert((await service.Search("path hello",default)).Single().Result.Title=="hello.txt","folder search uses configured root");
  var fixture=Path.Combine(root,"fixture");Directory.CreateDirectory(fixture);
  foreach(var file in Directory.GetFiles(args[1]))File.Copy(file,Path.Combine(fixture,Path.GetFileName(file)));
  var manifest=new PluginManifest{Id="test.fixture",Name="Fixture",Version="1.0",Executable="Fixture.exe",Commands=new(){new(){Id="run",Title="Run"}},Settings=new(){new(){Key="mode",Title="Mode",Default="invalid"}}};
  File.WriteAllText(Path.Combine(fixture,"plugin.json"),JsonSerializer.Serialize(manifest));var package=Path.Combine(root,"fixture.qdtplugin");ZipFile.CreateFromDirectory(fixture,package);
  service.Install(package);service.SetEnabled(manifest.Id,true);
  for(int i=0;i<3;i++)await Reject(()=>service.Invoke(manifest.Id,new(){Method="execute",Command="run"}),"malformed response rejected");
  Assert(!service.List().Single(p=>p.Manifest.Id==manifest.Id).State.Enabled,"three failures automatically disable plugin");
  Assert(service.List().Single(p=>p.Manifest.Id==manifest.Id).State.Errors.Count==3,"bounded error history persisted");
  service.SetEnabled(manifest.Id,true);service.SetSetting(manifest.Id,"mode","effect");
  await Reject(()=>service.Invoke(manifest.Id,new(){Method="execute",Command="run"}),"undeclared host action rejected");
  service.SetSetting(manifest.Id,"mode","oversize");await Reject(()=>service.Invoke(manifest.Id,new(){Method="execute",Command="run"}),"oversized stdout rejected");
  service.SetEnabled(manifest.Id,true);service.SetSetting(manifest.Id,"mode","slow");
  using(var cancel=new CancellationTokenSource(300)){await Reject(()=>service.Invoke(manifest.Id,new(){Method="execute",Command="run"},cancel.Token),"cancellation terminates worker");}
  Assert(service.List().Single(p=>p.Manifest.Id==manifest.Id).State.Failures==0,"user cancellation is not counted as plugin failure");
  var watch=System.Diagnostics.Stopwatch.StartNew();await Reject(()=>service.Invoke(manifest.Id,new(){Method="execute",Command="run"}),"hung worker times out");Assert(watch.Elapsed.TotalSeconds<12,"timeout is bounded");
  var unsafeZip=Path.Combine(root,"unsafe.qdtplugin");using(var zip=ZipFile.Open(unsafeZip,ZipArchiveMode.Create)){using(var w=new StreamWriter(zip.CreateEntry("plugin.json").Open()))w.Write(JsonSerializer.Serialize(new PluginManifest{Id="test.unsafe",Name="Unsafe",Version="1.0",Executable="bad.exe"}));using(var w=new StreamWriter(zip.CreateEntry("../escaped.exe").Open()))w.Write("no");}
  await Reject(()=>Task.Run(()=>service.Install(unsafeZip)),"archive traversal rejected");Assert(!File.Exists(Path.Combine(root,"plugins","escaped.exe")),"archive cannot escape staging");
  service.Uninstall("sample.calculator");Assert(!service.List().Any(p=>p.Manifest.Id=="sample.calculator"),"uninstall removes plugin registration");
  Assert(File.Exists(Path.Combine(root,"plugins","Data","sample.calculator","state.json")),"uninstall retains independent settings");
  Console.WriteLine("ALL PLUGIN CHECKS PASSED: "+root);
 }
}
