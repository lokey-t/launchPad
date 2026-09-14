using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchPad.Setup;

public sealed class PayloadFile { public string Path{get;set;} public long Size{get;set;} public string Sha256{get;set;} }
public sealed class PayloadManifest { public int Format{get;set;} public string Version{get;set;} public string Runtime{get;set;} public string Sha256{get;set;} public List<PayloadFile> Files{get;set;}=new(); }
public sealed record InstallProgress(string Phase,double Percent);

public static class InstallEngine
{
    private const long MaxBytes=1024L*1024*1024;
    public static PayloadManifest Manifest()
    {
        using var stream=typeof(InstallEngine).Assembly.GetManifestResourceStream("payload.json")??throw new IOException("MissingPayload");
        using var reader=new StreamReader(stream);
        return JsonSerializer.Deserialize<PayloadManifest>(reader.ReadToEnd().TrimStart('\uFEFF'));
    }
    public static Stream Payload()=>typeof(InstallEngine).Assembly.GetManifestResourceStream("payload.zip")??throw new IOException("MissingPayload");
    public static string Hash(string path){using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
    public static void NoLinks(string path)
    {
        for(var p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))
            if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("LinkedPath");
    }
    public static string SafeFile(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Length>220||relative.Contains('\\')||relative.Split('/').Any(s=>!Regex.IsMatch(s,@"^[a-zA-Z0-9_][a-zA-Z0-9_.-]*$")||s.EndsWith('.')||Regex.IsMatch(s,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)",RegexOptions.IgnoreCase)))throw new IOException("InvalidPayload");
        if(relative.Split('/').Any(s=>s.Equals("config.json",StringComparison.OrdinalIgnoreCase))||!new[]{".exe",".dll",".json",".dat",".bin",".pdb"}.Contains(Path.GetExtension(relative).ToLowerInvariant()))throw new IOException("InvalidPayload");
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var full=Path.GetFullPath(Path.Combine(root,relative));if(!full.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new IOException("InvalidPayload");NoLinks(full);return full;
    }
    public static string ValidateDirectory(string directory)
    {
        if(string.IsNullOrWhiteSpace(directory)||!Path.IsPathFullyQualified(directory)||directory.StartsWith(@"\\"))throw new IOException("InvalidDirectory");
        string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
        if(root.Length<4||root.Length>160||Path.GetPathRoot(root).TrimEnd('\\')==root)throw new IOException("InvalidDirectory");
        foreach(var reserved in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Windows),Path.GetDirectoryName(InstallationDiscovery.ConfigPath)})
            if(root.Equals(reserved,StringComparison.OrdinalIgnoreCase)||root.StartsWith(reserved+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("InvalidDirectory");
        NoLinks(root);
        if(Directory.Exists(root)&&Directory.EnumerateFileSystemEntries(root).Any()&&!InstallationDiscovery.IsInstallation(root))throw new IOException("ChooseEmptyDirectory");
        return root;
    }
    public static void ValidateManifest(PayloadManifest manifest,string target)
    {
        if(manifest?.Format!=1||!Version.TryParse(manifest.Version,out _)||manifest.Runtime!="win-"+RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()||manifest.Files==null||manifest.Files.Count is <3 or >4096||!Regex.IsMatch(manifest.Sha256??"",@"^[a-fA-F0-9]{64}$"))throw new IOException("InvalidPayload");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var f in manifest.Files)
            if(f==null||!paths.Add(SafeFile(target,f.Path))||f.Size<0||f.Size>MaxBytes||(total+=f.Size)>MaxBytes||!Regex.IsMatch(f.Sha256??"",@"^[a-fA-F0-9]{64}$"))throw new IOException("InvalidPayload");
        foreach(var required in new[]{"LaunchPad.exe","LaunchPad.dll","coreclr.dll"})if(!manifest.Files.Any(f=>f.Path==required))throw new IOException("InvalidPayload");
    }
    public static async Task InstallAsync(string directory,PayloadManifest manifest,Func<Stream> payload,IProgress<InstallProgress> progress,CancellationToken token,Func<string,CancellationToken,Task> closeApplication=null,string sourceDirectory=null)
    {
        string target=ValidateDirectory(directory);ValidateManifest(manifest,target);
        var version=Version.Parse(manifest.Version);
        if(InstallationDiscovery.InstalledVersion(target)>version)throw new IOException("NewerVersionInstalled");
        Directory.CreateDirectory(target);
        string stage=Path.Combine(target,".launchpad-install-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);NoLinks(stage);
        var records=new List<(string Target,string Backup,bool Existed)>();bool rollbackFailed=false;
        try
        {
            progress?.Report(new("Verifying",3));string archivePath=Path.Combine(stage,"payload.zip");
            string filesRoot=Path.Combine(stage,"files");
            if(sourceDirectory!=null)
            {
                int done=0;
                foreach(var file in manifest.Files)
                {
                    token.ThrowIfCancellationRequested();
                    string source=SafeFile(sourceDirectory,file.Path),path=SafeFile(filesRoot,file.Path);
                    if(new FileInfo(source).Length!=file.Size)throw new IOException("InvalidPayload");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    await using(var input=File.OpenRead(source))await using(var output=File.Create(path))await Copy(input,output,file.Size,token);
                    if(!Hash(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("InvalidPayload");
                    progress?.Report(new("Verifying",5+65.0*++done/manifest.Files.Count));
                }
            }
            else
            {
            using(var input=payload())await using(var output=File.Create(archivePath))await Copy(input,output,MaxBytes,token);
            if(!Hash(archivePath).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("InvalidPayload");
            using(var zip=ZipFile.OpenRead(archivePath))
            {
                if(zip.Entries.Count!=manifest.Files.Count)throw new IOException("InvalidPayload");
                var entries=zip.Entries.ToDictionary(e=>e.FullName,StringComparer.OrdinalIgnoreCase);int done=0;
                foreach(var file in manifest.Files)
                {
                    token.ThrowIfCancellationRequested();if(!entries.TryGetValue(file.Path,out var entry)||entry.Length!=file.Size)throw new IOException("InvalidPayload");
                    string path=SafeFile(filesRoot,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(path));
                    await using(var input=entry.Open())await using(var output=File.Create(path))await Copy(input,output,file.Size,token);
                    if(new FileInfo(path).Length!=file.Size||!Hash(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("InvalidPayload");
                    progress?.Report(new("Verifying",5+65.0*++done/manifest.Files.Count));
                }
            }
            }
            if(AssemblyName.GetAssemblyName(SafeFile(filesRoot,"LaunchPad.dll")).Version!=version)throw new IOException("VersionMismatch");
            token.ThrowIfCancellationRequested();progress?.Report(new("Closing",72));
            await (closeApplication??RunningApplication.CloseAsync)(target,token);
            progress?.Report(new("Installing",76));
            // Back up every overwritten file before the first replacement, on the same volume.
            foreach(var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();string path=SafeFile(target,file.Path),backup=SafeFile(Path.Combine(stage,"rollback"),file.Path);
                if(File.Exists(path)){Directory.CreateDirectory(Path.GetDirectoryName(backup));File.Copy(path,backup);}
            }
            int installed=0;
            foreach(var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();string path=SafeFile(target,file.Path),backup=SafeFile(Path.Combine(stage,"rollback"),file.Path);
                records.Add((path,backup,File.Exists(backup)));Directory.CreateDirectory(Path.GetDirectoryName(path));
                Replace(SafeFile(filesRoot,file.Path),path);
                if(!Hash(path).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new IOException("InstallFailed");
                progress?.Report(new("Installing",76+24.0*++installed/manifest.Files.Count));
            }
        }
        catch(Exception failure)
        {
            foreach(var record in records.AsEnumerable().Reverse())
            {
                try
                {
                    NoLinks(record.Target);
                    if(record.Existed){if(!File.Exists(record.Target)||Hash(record.Target)!=Hash(record.Backup))Replace(record.Backup,record.Target);}
                    else if(File.Exists(record.Target))File.Delete(record.Target);
                }
                catch{rollbackFailed=true;}
            }
            if(rollbackFailed)throw new IOException("RollbackFailed: "+stage,failure);
            throw;
        }
        finally
        {
            if(!rollbackFailed)CleanupStage(target,stage);
        }
    }
    private static void Replace(string source,string target)
    {
        string temporary=target+".setup-"+Guid.NewGuid().ToString("N");
        try{File.Copy(source,temporary);if(File.Exists(target))File.Replace(temporary,target,null);else File.Move(temporary,target);}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private static async Task Copy(Stream input,Stream output,long max,CancellationToken token)
    {
        var buffer=new byte[81920];long total=0;int count;
        while((count=await input.ReadAsync(buffer,token))>0){total+=count;if(total>max)throw new IOException("InvalidPayload");await output.WriteAsync(buffer.AsMemory(0,count),token);}
    }
    private static void CleanupStage(string target,string stage)
    {
        try
        {
            if(Path.GetDirectoryName(Path.GetFullPath(stage))!=target||!Path.GetFileName(stage).StartsWith(".launchpad-install-"))return;
            NoLinks(stage);
            void CheckTree(string root){foreach(var p in Directory.EnumerateFileSystemEntries(root)){NoLinks(p);if(Directory.Exists(p))CheckTree(p);}}
            CheckTree(stage);Directory.Delete(stage,true);
        }
        catch{}
    }
}
