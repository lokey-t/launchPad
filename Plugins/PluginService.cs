using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LaunchPad.PluginSdk;
namespace LaunchPad.Plugins;

public sealed class PluginCommand { public string Id { get; set; } public string Title { get; set; } public bool Context { get; set; } }
public sealed class PluginSetting { public string Key { get; set; } public string Title { get; set; } public string Default { get; set; } = ""; }
public sealed class PluginManifest
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string Description { get; set; }
    public string Author { get; set; }
    public int ApiVersion { get; set; } = 1;
    public string Executable { get; set; }
    public string SearchPrefix { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<PluginCommand> Commands { get; set; } = new();
    public List<PluginSetting> Settings { get; set; } = new();
}
public sealed class PluginState
{
    public bool Enabled { get; set; }
    public int Failures { get; set; }
    public string LastError { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string,string> Settings { get; set; } = new();
}
public sealed record InstalledPlugin(PluginManifest Manifest, PluginState State);
public sealed record PluginSearchResult(string PluginId, string PluginName, Result Result);

/// <summary>Native plugins are trusted programs, not sandboxed code. Only host-mediated effects are permission gated.</summary>
public sealed class PluginService
{
    private static readonly Lazy<PluginService> Shared = new(()=>new(Path.Combine(Services.ConfigService.DataDirectory,"Plugins")));
    public static PluginService Current => Shared.Value;
    public static void StopIfStarted() { if(Shared.IsValueCreated)Shared.Value.Stop(); }
    private readonly CancellationTokenSource _shutdown = new();
    public void Stop() { _shutdown.Cancel(); }
    private readonly string _root;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _slots = new(4);
    private readonly Dictionary<string,SemaphoreSlim> _locks = new();
    private readonly Dictionary<string,CancellationTokenSource> _running = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive=true, WriteIndented=false };
    public PluginService(string root) { _root=Path.GetFullPath(root); }
    private string Packages => Path.Combine(_root,"Packages");
    private string Data => Path.Combine(_root,"Data");
    private static void CheckId(string id) { if(!Regex.IsMatch(id??"",@"^[a-z][a-z0-9.-]{2,79}$") || id.Contains(".."))throw new IOException("Invalid plugin ID"); }
    private static string SafePath(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative) || relative.Length>220 || relative.Contains('\\') || relative.Split('/').Any(s=>!Regex.IsMatch(s,@"^[a-zA-Z0-9_][a-zA-Z0-9_.-]*$") || s.EndsWith('.') || Regex.IsMatch(s,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)",RegexOptions.IgnoreCase)))throw new IOException("Invalid plugin path");
        string full=Path.GetFullPath(Path.Combine(root,relative));
        for(string current=full;current!=null;current=Path.GetDirectoryName(current))
            if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked plugin path");
        return full;
    }
    public static PluginManifest ReadManifest(string text)
    {
        if(text.Length>65536)throw new IOException("Manifest too large");
        var m=JsonSerializer.Deserialize<PluginManifest>(text.TrimStart('\uFEFF'),Json)??throw new IOException("Missing manifest");
        CheckId(m.Id);
        if(m.ApiVersion!=1)throw new IOException("Unsupported plugin API version");
        if(string.IsNullOrWhiteSpace(m.Name)||m.Name.Length>100||!Version.TryParse(m.Version,out _)||m.Commands==null||m.Settings==null||m.Permissions==null||m.Commands.Count>30||m.Settings.Count>20||m.Permissions.Count>10||(m.Description?.Length??0)>2000||(m.Author?.Length??0)>100)throw new IOException("Invalid manifest");
        if(m.Executable==null || !m.Executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Plugin entry must be an EXE");
        SafePath(Path.GetTempPath(),m.Executable);
        if(m.SearchPrefix!=null && (m.SearchPrefix.Length<1||m.SearchPrefix.Length>30))throw new IOException("Invalid search prefix");
        if(m.Permissions.Any(p=>p is not ("clipboard.write" or "files.open" or "context.read")))throw new IOException("Unknown permission");
        if(m.Commands.Any(c=>c==null||!Regex.IsMatch(c.Id??"",@"^[a-zA-Z0-9_-]{1,60}$")||string.IsNullOrWhiteSpace(c.Title)||c.Title.Length>100)||m.Commands.Select(c=>c.Id).Distinct().Count()!=m.Commands.Count)throw new IOException("Invalid commands");
        if(m.Settings.Any(s=>s==null||!Regex.IsMatch(s.Key??"",@"^[a-zA-Z0-9_-]{1,60}$")||string.IsNullOrWhiteSpace(s.Title)||s.Title.Length>100||(s.Default?.Length??0)>4096)||m.Settings.Select(s=>s.Key).Distinct().Count()!=m.Settings.Count)throw new IOException("Invalid settings");
        return m;
    }
    private PluginManifest Manifest(string id) { CheckId(id);return ReadManifest(File.ReadAllText(SafePath(Packages,id+"/plugin.json"))); }
    private PluginState State(string id)
    {
        string path=SafePath(Data,id+"/state.json");
        if(!File.Exists(path))return new();
        var state=JsonSerializer.Deserialize<PluginState>(File.ReadAllText(path),Json)??new();
        state.Settings??=new();state.Errors??=new();return state;
    }
    private void Save(string id,PluginState state)
    {
        string path=SafePath(Data,id+"/state.json");Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(state,Json));File.Move(path+".tmp",path,true);
    }
    public List<InstalledPlugin> List()
    {
        lock(_gate)
        {
            if(!Directory.Exists(Packages))return new();
            var list=new List<InstalledPlugin>();
            foreach(var directory in Directory.EnumerateDirectories(Packages))
                try {string id=Path.GetFileName(directory);var manifest=Manifest(id);if(manifest.Id==id)list.Add(new(manifest,State(id)));} catch { }
            return list.OrderBy(p=>p.Manifest.Name).ToList();
        }
    }
    public void SetEnabled(string id,bool enabled)
    {
        lock(_gate){Manifest(id);var state=State(id);state.Enabled=enabled;state.Failures=0;state.LastError=null;Save(id,state);if(!enabled&&_running.TryGetValue(id,out var source))source.Cancel();}
    }
    public void SetSetting(string id,string key,string value)
    {
        lock(_gate){if(!Manifest(id).Settings.Any(s=>s.Key==key)||value.Length>4096)throw new IOException("Invalid setting");var state=State(id);state.Settings[key]=value;Save(id,state);}
    }
    public PluginManifest Inspect(string package)
    {
        using var zip=ZipFile.OpenRead(package);var entry=zip.GetEntry("plugin.json")??throw new IOException("plugin.json must be at package root");
        if(entry.Length>65536)throw new IOException("Manifest too large");using var reader=new StreamReader(entry.Open());return ReadManifest(reader.ReadToEnd());
    }
    public void Install(string package)
    {
        lock(_gate)
        {
            var manifest=Inspect(package);string destination=SafePath(Packages,manifest.Id);
            if(Directory.Exists(destination))throw new IOException("Plugin already installed; uninstall it before replacing the package.");
            string staging=SafePath(_root,"staging-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try
            {
                using var zip=ZipFile.OpenRead(package);
                if(zip.Entries.Count>4096||new FileInfo(package).Length>200L*1024*1024)throw new IOException("Plugin too large");
                long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries)
                {
                    if(entry.Name.Length==0)continue;
                    if(((entry.ExternalAttributes>>16)&0xF000)==0xA000||!names.Add(entry.FullName)||(total+=entry.Length)>512L*1024*1024)throw new IOException("Invalid plugin archive");
                    string file=SafePath(staging,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(file));
                    using var input=entry.Open();using var output=File.Create(file);var buffer=new byte[81920];long written=0;int count;
                    while((count=input.Read(buffer))>0){written+=count;if(written>entry.Length)throw new IOException("Invalid archive size");output.Write(buffer,0,count);}
                }
                var installed=ReadManifest(File.ReadAllText(Path.Combine(staging,"plugin.json")));
                if(installed.Id!=manifest.Id||!File.Exists(SafePath(staging,installed.Executable)))throw new IOException("Missing executable");
                Directory.CreateDirectory(Packages);Directory.Move(staging,destination);
                var state=State(manifest.Id);state.Enabled=false;state.Failures=0;Save(manifest.Id,state);
            }
            finally {if(Directory.Exists(staging))Directory.Delete(staging,true);}
        }
    }
    public void Uninstall(string id)
    {
        lock(_gate)
        {
            SetEnabled(id,false);var path=SafePath(Packages,id);
            // Refuse traversing any links during recursive removal.
            void Check(string dir){foreach(var item in Directory.EnumerateFileSystemEntries(dir)){if((File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked plugin path");if(Directory.Exists(item))Check(item);}}
            Check(path);Directory.Delete(path,true);
        }
    }
    public async Task<Response> Invoke(string id,Request request,CancellationToken cancellation=default)
    {
        SemaphoreSlim serial;lock(_gate){if(!_locks.TryGetValue(id,out serial))_locks[id]=serial=new(1);}
        _shutdown.Token.ThrowIfCancellationRequested();
        await serial.WaitAsync(cancellation);
        try
        {
            await _slots.WaitAsync(cancellation);
            try {return await Run(id,request,cancellation);} finally {_slots.Release();}
        }
        finally {serial.Release();}
    }
    private async Task<Response> Run(string id,Request request,CancellationToken cancellation)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation,_shutdown.Token);timeout.CancelAfter(TimeSpan.FromSeconds(8));
        PluginManifest manifest;PluginState state;
        lock(_gate)
        {
            _shutdown.Token.ThrowIfCancellationRequested();
            manifest=Manifest(id);state=State(id);if(!state.Enabled)throw new IOException("Plugin disabled");
            _running[id]=timeout;
        }
        using var process=new Process();
        try
        {
            request.Id=Guid.NewGuid().ToString("N");request.ApiVersion=1;
            if(request.Method is not ("search" or "execute"))throw new IOException("Unknown plugin method");
            if(request.Method=="execute"&&!manifest.Commands.Any(c=>c.Id==request.Command))throw new IOException("Unknown plugin command");
            if(!manifest.Permissions.Contains("context.read"))request.ContextPath=null;
            request.Settings=manifest.Settings.ToDictionary(s=>s.Key,s=>state.Settings.GetValueOrDefault(s.Key,s.Default??""));
            request.DataDirectory=SafePath(Data,id+"/files");Directory.CreateDirectory(request.DataDirectory);
            var start=new ProcessStartInfo(SafePath(Path.Combine(Packages,id),manifest.Executable)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=Path.Combine(Packages,id)};
            process.StartInfo=start;if(!process.Start())throw new IOException("Plugin failed to start");
            using var kill=timeout.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch{}});
            var errors=Drain(process.StandardError,timeout.Token);
            var output=ReadLine(process.StandardOutput,timeout.Token);
            var payload=JsonSerializer.Serialize(request,Json);if(payload.Length>262144)throw new IOException("Request too large");
            await process.StandardInput.WriteLineAsync(payload.AsMemory(),timeout.Token);process.StandardInput.Close();
            var text=await output;
            // Plugins are one-request workers. Do not wait forever for a plugin to exit after responding.
            try {if(!process.HasExited)process.Kill(true);}catch{}
            await process.WaitForExitAsync(timeout.Token);await errors;
            var response=JsonSerializer.Deserialize<Response>(text,Json)??throw new IOException("Empty plugin response");
            if(response.Id!=request.Id||response.ApiVersion!=1||response.Results==null||response.Results.Count>20||response.Results.Any(r=>r==null||string.IsNullOrWhiteSpace(r.Title)||r.Title.Length>200||(r.Description?.Length??0)>2000||(r.Value?.Length??0)>65536||!manifest.Commands.Any(c=>c.Id==r.Command))||(response.Message?.Length??0)>4096||(response.Value?.Length??0)>65536)throw new IOException("Invalid plugin response");
            if(response.Action!=null && (request.Method!="execute" || response.Action switch {"clipboard"=>!manifest.Permissions.Contains("clipboard.write"),"open"=>!manifest.Permissions.Contains("files.open"),_=>true}))throw new IOException("Plugin action not permitted");
            lock(_gate){var latest=State(id);if(!latest.Enabled)throw new OperationCanceledException("Plugin disabled");latest.Failures=0;Save(id,latest);}return response;
        }
        catch(Exception ex)
        {
            if(!cancellation.IsCancellationRequested && !_shutdown.IsCancellationRequested)
                lock(_gate){var latest=State(id);if(latest.Enabled){latest.Failures++;latest.LastError=ex is OperationCanceledException?"Plugin timed out":ex.Message;latest.Errors.Add(DateTimeOffset.Now.ToString("u")+" "+latest.LastError);latest.Errors=latest.Errors.TakeLast(20).ToList();if(latest.Failures>=3)latest.Enabled=false;Save(id,latest);}}
            throw;
        }
        finally
        {
            try{if(process.Id>0&&!process.HasExited)process.Kill(true);}catch{}
            lock(_gate)_running.Remove(id);
        }
    }
    private static async Task<string> ReadLine(StreamReader reader,CancellationToken token)
    {
        var text=new StringBuilder();char[] buffer=new char[1];
        while(await reader.ReadAsync(buffer.AsMemory(),token)>0){if(buffer[0]=='\n')return text.ToString();text.Append(buffer[0]);if(text.Length>262144)throw new IOException("Plugin response too large");}
        throw new IOException("Plugin exited without a response");
    }
    private static async Task Drain(StreamReader reader,CancellationToken token)
    {
        char[] buffer=new char[4096];while(await reader.ReadAsync(buffer.AsMemory(),token)>0){} // Drain to prevent stderr pipe deadlocks; never retain unbounded output.
    }
    public async Task<List<PluginSearchResult>> Search(string query,CancellationToken token)
    {
        var plugins=List().Where(p=>p.State.Enabled&&!string.IsNullOrEmpty(p.Manifest.SearchPrefix)&&query.StartsWith(p.Manifest.SearchPrefix,StringComparison.OrdinalIgnoreCase)).ToList();
        var results=await Task.WhenAll(plugins.Select(async p=>{try{var r=await Invoke(p.Manifest.Id,new(){Method="search",Query=query[p.Manifest.SearchPrefix.Length..].Trim()},token);return r.Results.Select(item=>new PluginSearchResult(p.Manifest.Id,p.Manifest.Name,item)).ToList();}catch{token.ThrowIfCancellationRequested();return new List<PluginSearchResult>();}}));
        return results.SelectMany(r=>r).Take(20).ToList();
    }
}
