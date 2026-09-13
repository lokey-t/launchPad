using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LaunchPad.Services;

public sealed record ReleaseAsset(string Name, string Url, string Digest);
public sealed record UpdateRelease(string Source, string Tag, Version Version, string Notes, List<ReleaseAsset> Assets, long Latency);
public sealed record UpdateOffer(Version Version, List<UpdateRelease> Mirrors);
public sealed class UpdateFile
{
    public string Path { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }
    public string Asset { get; set; }
}
public sealed class UpdateManifest
{
    public int Format { get; set; } = 1;
    public string Version { get; set; }
    public string Runtime { get; set; }
    public string Flavor { get; set; }
    public List<UpdateFile> Files { get; set; } = new();
}
public sealed record UpdateProgress(string Source, int Completed, int Total);
public sealed record PreparedUpdate(string Directory, List<UpdateFile> Files);

/// <summary>Release metadata and file-level incremental updates. No configuration files are installed.</summary>
public sealed class UpdateService
{
    public const string GitHub = "https://github.com/lokey-t/launchPad/releases";
    public const string Gitee = "https://gitee.com/lokey-t/launchPad/releases";
    private const long MaxBytes = 1024L * 1024 * 1024;
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    public static Version CurrentVersion => Normalize(typeof(App).Assembly.GetName().Version);
    public static string Runtime => "win-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public static string Flavor => File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "coreclr.dll")) || string.IsNullOrEmpty(typeof(object).Assembly.Location) ? "self-contained" : "framework-dependent";
    public static string ManifestName => $"LaunchPad-update-{Runtime}-{Flavor}.json";

    public UpdateService(HttpClient http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any()) _http.DefaultRequestHeaders.UserAgent.ParseAdd("LaunchPad-Updater/1.0");
    }
    private static Version Normalize(Version v) => v == null ? new Version(0,0,0,0) : new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision));
    public static Version ParseVersion(string tag) => Regex.IsMatch(tag ?? "", @"^[vV]?\d+\.\d+(\.\d+){0,2}$") && Version.TryParse(tag.TrimStart('v','V'), out var v) ? Normalize(v) : null;
    public static string DisplayVersion(Version version) => version.Revision > 0 ? version.ToString(4) : version.Build > 0 ? version.ToString(3) : version.ToString(2);

    public async Task<UpdateOffer> CheckAsync(CancellationToken token = default)
    {
        // Probe both hosts concurrently. Compare versions before latency so a stale mirror never wins.
        var results = await Task.WhenAll(ReadReleases("GitHub", "https://api.github.com/repos/lokey-t/launchPad/releases?per_page=30", token),
            ReadReleases("Gitee", "https://gitee.com/api/v5/repos/lokey-t/launchPad/releases?per_page=30", token));
        token.ThrowIfCancellationRequested();
        if (results.All(r => r == null)) throw new IOException("UpdateCheckFailed");
        var releases = results.Where(r => r != null).SelectMany(r => r).ToList();
        var latest = releases.Where(r => r.Version > CurrentVersion).OrderByDescending(r => r.Version).FirstOrDefault();
        return latest == null ? null : new UpdateOffer(latest.Version, releases.Where(r => r.Version == latest.Version).OrderBy(r => r.Latency).ToList());
    }
    private async Task<List<UpdateRelease>> ReadReleases(string source, string url, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var watch = Stopwatch.StartNew();
            using var doc = JsonDocument.Parse(await DownloadBytes(url, 4 * 1024 * 1024, timeout.Token));
            var result = new List<UpdateRelease>();
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if ((r.TryGetProperty("draft",out var d) && d.ValueKind == JsonValueKind.True) || (r.TryGetProperty("prerelease",out var p) && p.ValueKind == JsonValueKind.True)) continue;
                string tag = r.GetProperty("tag_name").GetString(); var version = ParseVersion(tag); if (version == null) continue;
                var assets = new List<ReleaseAsset>();
                if (r.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.Array)
                    foreach (var asset in a.EnumerateArray())
                        if (asset.TryGetProperty("name",out var n) && asset.TryGetProperty("browser_download_url",out var u) && ValidAssetUrl(u.GetString()))
                            assets.Add(new(n.GetString(),u.GetString(),asset.TryGetProperty("digest",out var h) ? h.GetString() : null));
                result.Add(new(source,tag,version,r.TryGetProperty("body",out var b) ? b.GetString() ?? "" : "",assets,watch.ElapsedMilliseconds));
            }
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
    }
    public static bool ValidAssetUrl(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0 &&
        (uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("gitee.com",StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.StartsWith("/lokey-t/launchPad/releases/download/",StringComparison.OrdinalIgnoreCase);

    private async Task<byte[]> DownloadBytes(string url, long limit, CancellationToken token)
    {
        using var response = await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new IOException("UpdateTooLarge");
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var output = new MemoryStream();
        await CopyBounded(stream,output,limit,token); return output.ToArray();
    }
    private static async Task CopyBounded(Stream input, Stream output, long limit, CancellationToken token)
    {
        byte[] buffer = new byte[81920]; long total = 0; int read;
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(20));
            try { read = await input.ReadAsync(buffer,idle.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("UpdateDownloadTimeout"); }
            if (read == 0) break;
            total += read; if (total > limit) throw new IOException("UpdateTooLarge"); await output.WriteAsync(buffer.AsMemory(0,read),token);
        }
    }
    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 220 || relative.Contains('\\') || relative.Split('/').Any(s => !Regex.IsMatch(s,@"^[a-zA-Z0-9_][a-zA-Z0-9_.-]*$") || s.EndsWith('.') || Regex.IsMatch(s,@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)",RegexOptions.IgnoreCase))) throw new IOException("UpdateInvalidPath");
        if (relative.Split('/').Any(s => s.Equals("config.json",StringComparison.OrdinalIgnoreCase) || s.Equals("ThemeAssets",StringComparison.OrdinalIgnoreCase) || s.Equals("Backups",StringComparison.OrdinalIgnoreCase)) || !new[]{".dll",".exe",".json",".pdb",".dat",".bin"}.Contains(System.IO.Path.GetExtension(relative).ToLowerInvariant())) throw new IOException("UpdateInvalidPath");
        var fullRoot = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot,relative));
        if (!path.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase)) throw new IOException("UpdateInvalidPath");
        for (var current = path; current != null; current = System.IO.Path.GetDirectoryName(current))
            if ((File.Exists(current) || System.IO.Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("UpdateLinkedPath");
        return path;
    }
    public static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static void ValidateManifest(UpdateManifest manifest, UpdateOffer offer, string root, bool fullPackage = false)
    {
        if (manifest == null || manifest.Format != 1 || ParseVersion(manifest.Version) != offer.Version || manifest.Runtime != Runtime || manifest.Flavor != Flavor || manifest.Files == null || manifest.Files.Count is < 1 or > 4096) throw new IOException("UpdateInvalidManifest");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var f in manifest.Files)
        {
            if (f == null || !paths.Add(SafePath(root,f.Path)) || !Regex.IsMatch(f.Sha256 ?? "",@"^[a-fA-F0-9]{64}$") || f.Size < 0 || f.Size > MaxBytes || (total += f.Size) > MaxBytes || f.Asset != "lp-" + f.Sha256.ToLowerInvariant() + ".gz") throw new IOException("UpdateInvalidManifest");
        }
        if (!manifest.Files.Any(f => f.Path == "LaunchPad.exe") || (!fullPackage && !manifest.Files.Any(f => f.Path == "LaunchPad.dll"))) throw new IOException("UpdateInvalidManifest");
    }
    public static bool SupportsIncremental(UpdateOffer offer) => offer.Mirrors.Any(r => r.Assets.Any(a => a.Name == ManifestName));

    public async Task<PreparedUpdate> PrepareAsync(UpdateOffer offer, string installRoot, bool fullPackage, IProgress<UpdateProgress> progress, CancellationToken token)
    {
        string stage = System.IO.Path.Combine(System.IO.Path.GetTempPath(),"LaunchPadUpdate",Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(stage);
        try
        {
            SafePath(stage,"LaunchPad.dll");
            UpdateManifest manifest;
            if (fullPackage) manifest = await PrepareFull(offer,stage,progress,token);
            else
            {
                manifest = null;
                foreach (var mirror in offer.Mirrors)
                {
                    var asset = mirror.Assets.FirstOrDefault(a => a.Name == ManifestName); if (asset == null) continue;
                    try { manifest = JsonSerializer.Deserialize<UpdateManifest>(System.Text.Encoding.UTF8.GetString(await DownloadBytes(asset.Url,4*1024*1024,token)).TrimStart('\uFEFF'),Json); ValidateManifest(manifest,offer,installRoot); break; }
                    catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or OperationCanceledException) { token.ThrowIfCancellationRequested(); manifest = null; }
                }
                if (manifest == null) throw new IOException("UpdateDownloadFailed");
            }
            ValidateManifest(manifest,offer,installRoot,fullPackage);
            var changed = new List<UpdateFile>();
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested(); var target = SafePath(installRoot,file.Path);
                if (!File.Exists(target) || new FileInfo(target).Length != file.Size || !Hash(target).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)) changed.Add(file);
            }
            int completed = 0;
            foreach (var file in changed)
            {
                if (!fullPackage)
                {
                    bool ok = false;
                    foreach (var mirror in offer.Mirrors)
                    {
                        var asset = mirror.Assets.FirstOrDefault(a => a.Name == file.Asset); if (asset == null) continue;
                        progress?.Report(new(mirror.Source,completed,changed.Count));
                        var destination = SafePath(stage,file.Path); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
                        try
                        {
                            using var response = await _http.GetAsync(asset.Url,HttpCompletionOption.ResponseHeadersRead,token); response.EnsureSuccessStatusCode();
                            await using (var stream = await response.Content.ReadAsStreamAsync(token))
                            await using (var gzip = new GZipStream(stream,CompressionMode.Decompress))
                            await using (var output = File.Create(destination)) await CopyBounded(gzip,output,file.Size,token);
                            if (new FileInfo(destination).Length != file.Size || !Hash(destination).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase)) throw new IOException("UpdateHashFailed");
                            ok = true; break;
                        }
                        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException) { token.ThrowIfCancellationRequested(); }
                    }
                    if (!ok) throw new IOException("UpdateDownloadFailed");
                }
                progress?.Report(new("",++completed,changed.Count));
            }
            // The assembly inside a package must agree with the release tag.
            Version packageVersion;
            if (manifest.Files.Any(f => f.Path == "LaunchPad.dll"))
            {
                var dll = changed.Any(f => f.Path == "LaunchPad.dll") ? SafePath(stage,"LaunchPad.dll") : SafePath(installRoot,"LaunchPad.dll");
                packageVersion = Normalize(AssemblyName.GetAssemblyName(dll).Version);
            }
            else
            {
                // Legacy single-file packages expose their version in the executable resource.
                var exe = changed.Any(f => f.Path == "LaunchPad.exe") ? SafePath(stage,"LaunchPad.exe") : SafePath(installRoot,"LaunchPad.exe");
                packageVersion = ParseVersion(FileVersionInfo.GetVersionInfo(exe).FileVersion);
            }
            if (packageVersion != offer.Version) throw new IOException("UpdateVersionMismatch");
            return new(stage,changed);
        }
        catch { DeleteStage(stage); throw; }
    }
    private async Task<UpdateManifest> PrepareFull(UpdateOffer offer,string stage,IProgress<UpdateProgress> progress,CancellationToken token)
    {
        foreach (var mirror in offer.Mirrors)
        {
            string suffix = Runtime + (Flavor == "framework-dependent" ? "-framework-dependent" : "") + ".zip";
            var asset = mirror.Assets.FirstOrDefault(a => a.Name.Equals($"LaunchPad-v{DisplayVersion(offer.Version)}-{suffix}",StringComparison.OrdinalIgnoreCase)); if (asset == null) continue;
            try
            {
                progress?.Report(new(mirror.Source,0,0));
                string hash = asset.Digest?.StartsWith("sha256:") == true ? asset.Digest[7..] : null;
                if (hash == null)
                {
                    var sums = mirror.Assets.FirstOrDefault(a => a.Name.EndsWith("sha256.txt",StringComparison.OrdinalIgnoreCase) || a.Name == "SHA256SUMS.txt");
                    if (sums != null)
                    {
                        string text = System.Text.Encoding.UTF8.GetString(await DownloadBytes(sums.Url,65536,token));
                        var match = Regex.Match(text,@"(?im)^([a-f0-9]{64})\s+\*?" + Regex.Escape(asset.Name) + @"\s*$"); if (match.Success) hash = match.Groups[1].Value;
                    }
                }
                if (!Regex.IsMatch(hash ?? "",@"^[a-fA-F0-9]{64}$")) continue;
                string zipPath = System.IO.Path.Combine(stage,"package.zip");
                using (var response = await _http.GetAsync(asset.Url,HttpCompletionOption.ResponseHeadersRead,token))
                {
                    response.EnsureSuccessStatusCode(); await using var input = await response.Content.ReadAsStreamAsync(token); await using var output = File.Create(zipPath); await CopyBounded(input,output,MaxBytes,token);
                }
                if (!Hash(zipPath).Equals(hash,StringComparison.OrdinalIgnoreCase)) throw new IOException("UpdateHashFailed");
                using var zip = ZipFile.OpenRead(zipPath);
                if (zip.Entries.Count > 4096) throw new IOException("UpdateTooLarge");
                var executable = zip.Entries.Single(e => e.Name == "LaunchPad.exe"); string prefix = executable.FullName[..^"LaunchPad.exe".Length];
                var manifest = new UpdateManifest { Version = offer.Version.ToString(), Runtime = Runtime, Flavor = Flavor };
                long size = 0; var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
                {
                    if (!entry.FullName.StartsWith(prefix,StringComparison.Ordinal)) throw new IOException("UpdateInvalidPath");
                    string relative = entry.FullName[prefix.Length..];
                    // Release documentation is not an installed executable asset.
                    if (relative is "README.md" or "README.en.md" or "LICENSE" or "LICENSE.txt") continue;
                    string path = SafePath(stage,relative); if (!paths.Add(path) || entry.Length < 0 || (size += entry.Length) > MaxBytes) throw new IOException("UpdateInvalidPath");
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    await using (var input = entry.Open()) await using (var output = File.Create(path)) await CopyBounded(input,output,entry.Length,token);
                    string fileHash = Hash(path); manifest.Files.Add(new() { Path = relative, Size = entry.Length, Sha256 = fileHash, Asset = "lp-" + fileHash.ToLowerInvariant() + ".gz" });
                }
                return manifest;
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException or InvalidOperationException) { token.ThrowIfCancellationRequested(); }
        }
        throw new IOException("UpdateDownloadFailed");
    }
    public static void DeleteStage(string stage)
    {
        // Only remove a UUID directory created under our dedicated temporary root.
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(),"LaunchPadUpdate"));
        if (string.IsNullOrEmpty(stage) || System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(stage)) != root || !Guid.TryParseExact(System.IO.Path.GetFileName(stage),"N",out _)) return;
        try
        {
            SafePath(stage,"LaunchPad.dll");
            bool NoLinks(string directory)
            {
                foreach (string entry in System.IO.Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    if ((attributes & FileAttributes.Directory) != 0 && !NoLinks(entry)) return false;
                }
                return true;
            }
            if (System.IO.Directory.Exists(stage) && NoLinks(stage)) System.IO.Directory.Delete(stage,true);
        }
        catch { }
    }
    public static async Task StartInstaller(PreparedUpdate update,string installRoot)
    {
        foreach (var file in update.Files) SafePath(installRoot,file.Path);
        var probe = System.IO.Path.Combine(installRoot,".launchpad-write-" + Guid.NewGuid().ToString("N"));
        try { await File.WriteAllTextAsync(probe,""); } finally { if (File.Exists(probe)) File.Delete(probe); }
        string script = System.IO.Path.Combine(update.Directory,"install.ps1");
        using (var input = typeof(UpdateService).Assembly.GetManifestResourceStream("LaunchPad.UpdateInstaller.ps1"))
        using (var output = File.Create(script)) await input.CopyToAsync(output);
        var job = new { Root = System.IO.Path.GetFullPath(installRoot), Stage = update.Directory, Files = update.Files, Pid = Environment.ProcessId,
            Result = System.IO.Path.Combine(ConfigService.DataDirectory,"update-result.json"), Restart = true };
        string jobPath = System.IO.Path.Combine(update.Directory,"job.json"); await File.WriteAllTextAsync(jobPath,JsonSerializer.Serialize(job));
        var start = new ProcessStartInfo { FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"), UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",script,"-JobPath",jobPath}) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("UpdateInstallerFailed");
        for (int i = 0; i < 100; i++)
        {
            if (File.Exists(System.IO.Path.Combine(update.Directory,"ready"))) return;
            if (process.HasExited) throw new IOException("UpdateInstallerFailed");
            await Task.Delay(100);
        }
        // Without this handshake the helper never modifies the installation.
        throw new IOException("UpdateInstallerFailed");
    }
}
