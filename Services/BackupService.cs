using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaunchPad.Models;

namespace LaunchPad.Services;

public sealed class BackupPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool Saved { get; set; }
    public string Version { get; set; }
    public string Summary => $"{CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {(Saved ? AppLanguage.T("已保存") : AppLanguage.T("自动备份"))} · v{Version}";
}

/// <summary>Portable ZIP envelope; app versions do not gate configuration import.</summary>
public static class BackupService
{
    private static string Root => Path.Combine(Path.GetDirectoryName(ConfigService.ConfigPath), "Backups");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private const long MaxExpandedSize = 512L * 1024 * 1024;
    public static string LastError { get; private set; }
    private static string _lastFingerprint;
    private static string _externalSignature;
    public static event Action Changed;
    private static string ArchivePath(BackupPoint point) => Path.Combine(Root, point.Id + ".qdtbackup");
    private static string InfoPath(BackupPoint point) => Path.Combine(Root, point.Id + ".json");

    public static List<BackupPoint> List()
    {
        Directory.CreateDirectory(Root);
        var result = new List<BackupPoint>();
        foreach (var path in Directory.EnumerateFiles(Root, "*.json"))
        {
            try
            {
                var point = JsonSerializer.Deserialize<BackupPoint>(File.ReadAllText(path), Options);
                if (point != null && Guid.TryParseExact(point.Id, "N", out _) &&
                    Path.GetFileNameWithoutExtension(path) == point.Id && File.Exists(ArchivePath(point))) result.Add(point);
            }
            catch (JsonException) { /* An interrupted metadata write must not hide other points. */ }
        }
        return result.OrderByDescending(p => p.CreatedUtc).ToList();
    }

    private static void WriteInfo(BackupPoint point)
    {
        var temp = InfoPath(point) + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(point, Options));
        File.Move(temp, InfoPath(point), true);
    }

    public static void Update(BackupPoint point, string name, bool saved)
    {
        point.Name = name;
        point.Saved = saved;
        WriteInfo(point);
        Changed?.Invoke();
    }

    public static void Delete(BackupPoint point)
    {
        File.Delete(ArchivePath(point));
        File.Delete(InfoPath(point));
        Changed?.Invoke();
    }

    // Snapshot a referenced file's bytes, not just its path: replacing a background is a change too.
    private static (JsonObject Config, Dictionary<string, byte[]> Assets, string Fingerprint) Capture(LauncherConfig config)
    {
        var node = JsonNode.Parse(ConfigService.Serialize(config)).AsObject();
        var assets = new Dictionary<string, byte[]>();
        VisitAssets(node, (path) =>
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            // Missing source images already fall back to default appearance in the app.
            // Keep their path so a deleted file cannot prevent backing up the rest of the configuration.
            if (!File.Exists(path)) return path;
            var bytes = File.ReadAllBytes(path);
            if (assets.Values.Sum(x => (long)x.Length) + bytes.Length > MaxExpandedSize)
                throw new IOException("备份静态文件超过 512 MB。");
            var key = "assets/" + Convert.ToHexString(SHA256.HashData(bytes)) + Path.GetExtension(path).ToLowerInvariant();
            assets.TryAdd(key, bytes);
            return "qdtasset:" + key;
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(node.ToJsonString())));
        return (node, assets, fingerprint);
    }

    private static void VisitAssets(JsonNode node, Func<string, string> transform)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToList())
            {
                if ((pair.Key.Equals("CustomIconPath", StringComparison.OrdinalIgnoreCase) ||
                     pair.Key.Equals("BackgroundPath", StringComparison.OrdinalIgnoreCase) ||
                     pair.Value is JsonValue token && token.TryGetValue<string>(out var reference) && reference.StartsWith("qdtasset:", StringComparison.Ordinal)) && pair.Value is JsonValue value && value.TryGetValue<string>(out var path))
                    obj[pair.Key] = transform(path);
                else if (pair.Value != null) VisitAssets(pair.Value, transform);
            }
        else if (node is JsonArray array)
            foreach (var child in array) if (child != null) VisitAssets(child, transform);
    }

    public static BackupPoint Create(LauncherConfig config, string name, bool saved = true)
    {
        var snapshot = Capture(config);
        return WriteSnapshot(snapshot.Config, snapshot.Assets, name, saved);
    }

    private static BackupPoint WriteSnapshot(JsonObject config, Dictionary<string, byte[]> assets, string name, bool saved)
    {
        Directory.CreateDirectory(Root);
        var point = new BackupPoint { Name = name, Saved = saved,
            Version = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? "unknown" };
        var temp = ArchivePath(point) + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                    writer.Write(JsonSerializer.Serialize(new { Format = "LaunchPad.Backup", FormatVersion = 1, point.Version, point.CreatedUtc }, Options));
                using (var writer = new StreamWriter(archive.CreateEntry("config.json").Open())) writer.Write(config.ToJsonString(Options));
                foreach (var asset in assets)
                {
                    using var stream = archive.CreateEntry(asset.Key, CompressionLevel.Optimal).Open();
                    stream.Write(asset.Value);
                }
            }
            File.Move(temp, ArchivePath(point));
            WriteInfo(point);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Changed?.Invoke();
        return point;
    }

    public static void CheckAutomatic(LauncherConfig config)
    {
        if (!config.AutoBackup) { _lastFingerprint = null; return; }
        try
        {
            var snapshot = Capture(config);
            if (snapshot.Fingerprint == _lastFingerprint) { LastError = null; return; }
            if (_lastFingerprint == null && List().FirstOrDefault(p => !p.Saved) is BackupPoint latest)
            {
                try
                {
                    var previous = Read(ArchivePath(latest));
                    if (JsonNode.DeepEquals(previous.Config, snapshot.Config))
                    {
                        _lastFingerprint = snapshot.Fingerprint;
                        LastError = null;
                        return;
                    }
                }
                catch (InvalidDataException) { /* A damaged old point must not block new backups. */ }
                catch (JsonException) { }
            }
            WriteSnapshot(snapshot.Config, snapshot.Assets, "自动备份", false);
            foreach (var old in List().Where(p => !p.Saved).Skip(30).ToList()) Delete(old);
            _lastFingerprint = snapshot.Fingerprint;
            LastError = null;
        }
        catch (Exception ex) { LastError = "自动备份失败：" + ex.Message; }
        Changed?.Invoke();
    }

    public static void CheckExternalChanges(LauncherConfig config)
    {
        if (!config.AutoBackup) { _externalSignature = null; return; }
        try
        {
            var node = JsonNode.Parse(ConfigService.Serialize(config));
            var signature = new StringBuilder();
            VisitAssets(node, path =>
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var file = new FileInfo(path);
                    signature.Append(path).Append(file.Exists ? file.LastWriteTimeUtc.Ticks : 0).Append(file.Exists ? file.Length : 0);
                }
                return path;
            });
            var value = signature.ToString();
            if (_externalSignature == value) return;
            CheckAutomatic(config);
            if (LastError == null) _externalSignature = value;
        }
        catch (Exception ex) { LastError = "自动备份失败：" + ex.Message; }
    }

    public static void Export(BackupPoint point, string destination) => File.Copy(ArchivePath(point), destination, true);

    private static (JsonObject Config, Dictionary<string, byte[]> Assets) Read(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 10000 || archive.Entries.Sum(e => e.Length) > MaxExpandedSize)
            throw new InvalidDataException("备份内容过大。");
        if (archive.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count)
            throw new InvalidDataException("备份包含重复文件。");
        var entry = archive.GetEntry("config.json") ?? throw new InvalidDataException("备份缺少 config.json。");
        using var reader = new StreamReader(entry.Open());
        var config = JsonNode.Parse(reader.ReadToEnd()) as JsonObject ?? throw new InvalidDataException("配置格式无效。");
        ConfigService.Deserialize(config.ToJsonString()); // Validate before adding a point or changing live data.
        var assets = new Dictionary<string, byte[]>();
        foreach (var asset in archive.Entries.Where(e => e.FullName.StartsWith("assets/", StringComparison.Ordinal)))
        {
            var filename = asset.FullName[7..];
            if (filename.Length == 0 || filename.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || filename.Contains("..") || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("备份静态文件路径无效。");
            using var stream = asset.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            assets.Add(asset.FullName, buffer.ToArray());
        }
        VisitAssets(config, value =>
        {
            if (value?.StartsWith("qdtasset:", StringComparison.Ordinal) == true && !assets.ContainsKey(value[9..]))
                throw new InvalidDataException("备份缺少引用的静态文件。");
            return value;
        });
        return (config, assets);
    }

    public static BackupPoint Import(string path)
    {
        Read(path);
        // Preserve the original envelope too, including future metadata and resources.
        var point = new BackupPoint { Name = Path.GetFileNameWithoutExtension(path), Saved = true, Version = "unknown" };
        using (var archive = ZipFile.OpenRead(path))
        {
            if (archive.GetEntry("manifest.json") is ZipArchiveEntry manifest)
            {
                using var reader = new StreamReader(manifest.Open());
                var info = JsonNode.Parse(reader.ReadToEnd());
                point.Version = info?["Version"]?.ToString() ?? "unknown";
            }
        }
        Directory.CreateDirectory(Root);
        File.Copy(path, ArchivePath(point));
        WriteInfo(point);
        Changed?.Invoke();
        return point;
    }

    public static LauncherConfig Restore(BackupPoint point)
    {
        var data = Read(ArchivePath(point));
        var assetRoot = Path.Combine(Path.GetDirectoryName(ConfigService.ConfigPath), "RestoredAssets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(assetRoot);
        foreach (var asset in data.Assets) File.WriteAllBytes(Path.Combine(assetRoot, asset.Key[7..]), asset.Value);
        VisitAssets(data.Config, value => value?.StartsWith("qdtasset:", StringComparison.Ordinal) == true
            ? Path.Combine(assetRoot, value[9..][7..]) : value);
        return ConfigService.Deserialize(data.Config.ToJsonString());
    }
}
