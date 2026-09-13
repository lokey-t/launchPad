using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaunchPad.Models;

namespace LaunchPad.Services;

public sealed record ThemePreset(string Id, string Name, string Version);

/// <summary>Theme-only archives. Unknown JSON fields survive import, application and re-export.</summary>
public static class ThemePresetService
{
    public const string Extension = ".qdtstylebackup";
    private const long Limit = 512L * 1024 * 1024;
    private static string Root => Path.Combine(Path.GetDirectoryName(ConfigService.ConfigPath)!, "ThemePresets");
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static string CurrentVersion => typeof(ThemePresetService).Assembly.GetName().Version!.ToString();
    private static string FileFor(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid preset ID.");
        return Path.Combine(Root, id + Extension);
    }
    public static bool IsNewer(string version) => Version.TryParse(version, out var source) && source > Version.Parse(CurrentVersion);
    public static IReadOnlyList<ThemePreset> List()
    {
        if (!Directory.Exists(Root)) return Array.Empty<ThemePreset>();
        var result = new List<ThemePreset>();
        foreach (var path in Directory.EnumerateFiles(Root, "*" + Extension))
        {
            try
            {
                using var zip=ZipFile.OpenRead(path);
                var theme=zip.GetEntry("theme.json"); var manifest=zip.GetEntry("manifest.json");
                if(theme==null || manifest==null || theme.Length>4*1024*1024 || manifest.Length>65536) continue;
                using var themeReader=new StreamReader(theme.Open()); using var manifestReader=new StreamReader(manifest.Open());
                var data=JsonNode.Parse(themeReader.ReadToEnd()); var metadata=JsonNode.Parse(manifestReader.ReadToEnd());
                if(metadata?["Format"]?.GetValue<string>()!="LaunchPad.Theme") continue;
                result.Add(new(Path.GetFileNameWithoutExtension(path),data?["Name"]?.GetValue<string>()??AppLanguage.T("自定义"),metadata?["Version"]?.GetValue<string>()??"0.0"));
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException) { }
        }
        return result.OrderBy(p => p.Name).ToArray();
    }
    public static ThemePreset Save(ThemeProfile profile, string name)
    {
        Directory.CreateDirectory(Root);
        var id = Guid.NewGuid().ToString("N");
        var copy = profile.Copy(); copy.Name = name.Trim();
        Write(copy, FileFor(id));
        return new(id, copy.Name, IsNewer(copy.SourceVersion) ? copy.SourceVersion : CurrentVersion);
    }
    public static ThemePreset Import(string path)
    {
        var theme = Read(path, false); // Validate everything before adding the archive.
        Directory.CreateDirectory(Root);
        var id = Guid.NewGuid().ToString("N");
        AtomicCopy(path, FileFor(id));
        return new(id, theme.Name, theme.SourceVersion);
    }
    public static ThemeProfile Load(ThemePreset preset) => Read(FileFor(preset.Id), true);
    public static void Delete(ThemePreset preset) => File.Delete(FileFor(preset.Id));
    public static void Export(ThemePreset preset, string path) => AtomicCopy(FileFor(preset.Id), path);
    private static void AtomicCopy(string source, string target)
    {
        var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(source, temp); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static bool IsStoredAsset(string value)
    {
        var prefix=Path.Combine(Path.GetDirectoryName(ConfigService.ConfigPath)!,"ThemeAssets")+Path.DirectorySeparatorChar;
        return value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase);
    }
    private static void Visit(JsonNode node, Func<string, string> transform)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToArray())
            {
                if (pair.Value is JsonValue v && v.TryGetValue<string>(out var path) &&
                    (pair.Key.EndsWith("Path", StringComparison.OrdinalIgnoreCase) || path.StartsWith("qdtasset:", StringComparison.Ordinal) || IsStoredAsset(path)))
                    obj[pair.Key] = transform(path);
                else if (pair.Value != null) Visit(pair.Value, transform);
            }
        else if (node is JsonArray array) foreach (var child in array) if (child != null) Visit(child, transform);
    }
    private static string AssetName(byte[] bytes, string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext.Length > 12 || ext.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c))) ext = ".bin";
        return "assets/" + Convert.ToHexString(SHA256.HashData(bytes)) + ext;
    }
    private static void Write(ThemeProfile theme, string target)
    {
        var payload = JsonSerializer.SerializeToNode(theme, Options)!;
        var assets = new Dictionary<string, byte[]>(); long total = 0;
        Visit(payload, path =>
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (!File.Exists(path)) throw new FileNotFoundException(AppLanguage.T("主题素材不存在，请重新选择后保存。"));
            var length = new FileInfo(path).Length;
            if (length > Limit - total) throw new InvalidDataException("Theme assets exceed 512 MB.");
            var bytes = File.ReadAllBytes(path); var key = AssetName(bytes, path);
            if (assets.TryAdd(key, bytes)) total += bytes.Length;
            return "qdtasset:" + key;
        });
        var temp = target + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open()))
                    writer.Write(JsonSerializer.Serialize(new { Format = "LaunchPad.Theme", FormatVersion = 1, Version = IsNewer(theme.SourceVersion) ? theme.SourceVersion : CurrentVersion }));
                using (var writer = new StreamWriter(archive.CreateEntry("theme.json").Open())) writer.Write(payload.ToJsonString());
                foreach (var pair in assets) { using var stream = archive.CreateEntry(pair.Key, CompressionLevel.Optimal).Open(); stream.Write(pair.Value); }
            }
            File.Move(temp, target, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static ThemeProfile Read(string path, bool installAssets)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 1024 || archive.Entries.Sum(e => e.Length) > Limit ||
            archive.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != archive.Entries.Count)
            throw new InvalidDataException("Invalid or oversized theme archive.");
        var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("Missing theme manifest.");
        var themeEntry = archive.GetEntry("theme.json") ?? throw new InvalidDataException("Missing theme configuration.");
        if (manifestEntry.Length > 65536 || themeEntry.Length > 4 * 1024 * 1024) throw new InvalidDataException("Oversized theme configuration.");
        using var manifestReader = new StreamReader(manifestEntry.Open());
        var manifest = JsonNode.Parse(manifestReader.ReadToEnd())!;
        if (manifest["Format"]?.GetValue<string>() != "LaunchPad.Theme") throw new InvalidDataException("Not a LaunchPad theme archive.");
        using var themeReader = new StreamReader(themeEntry.Open());
        var payload = JsonNode.Parse(themeReader.ReadToEnd()) as JsonObject ?? throw new InvalidDataException("Invalid theme configuration.");
        var files = new Dictionary<string, byte[]>();
        foreach (var entry in archive.Entries.Where(e => e.FullName != "manifest.json" && e.FullName != "theme.json"))
        {
            if (!entry.FullName.StartsWith("assets/", StringComparison.Ordinal) || entry.FullName.Contains("..") || entry.FullName.Contains('\\') || entry.FullName[7..].Contains('/') || entry.FullName.Contains(':'))
                throw new InvalidDataException("Invalid theme asset path.");
            using var stream = entry.Open(); using var buffer = new MemoryStream();
            var block=new byte[81920]; int count;
            while((count=stream.Read(block,0,block.Length))>0)
            {
                if(buffer.Length+count>entry.Length || buffer.Length+count>Limit) throw new InvalidDataException("Oversized theme asset.");
                buffer.Write(block,0,count);
            }
            if (buffer.Length != entry.Length) throw new InvalidDataException("Invalid theme asset length.");
            files.Add(entry.FullName, buffer.ToArray());
        }
        var references = new HashSet<string>();
        Visit(payload, value =>
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (!value.StartsWith("qdtasset:", StringComparison.Ordinal) || !files.ContainsKey(value[9..])) throw new InvalidDataException("Missing or external theme asset.");
            references.Add(value[9..]); return value;
        });
        // Deserialize before installing anything. User data outside ThemeProfile is never touched.
        var theme = payload.Deserialize<ThemeProfile>(Options) ?? throw new InvalidDataException("Invalid theme.");
        if (installAssets)
        {
            var assetRoot = Path.Combine(Path.GetDirectoryName(ConfigService.ConfigPath)!, "ThemeAssets");
            Directory.CreateDirectory(assetRoot);
            var installed = new Dictionary<string, string>();
            foreach (var key in references)
            {
                var bytes = files[key]; var destination = Path.Combine(assetRoot, AssetName(bytes, key)[7..]);
                var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.WriteAllBytes(temp, bytes); File.Move(temp, destination, true); }
                finally { if (File.Exists(temp)) File.Delete(temp); }
                installed[key] = destination;
            }
            Visit(payload, value => string.IsNullOrEmpty(value) ? value : installed[value[9..]]);
            theme = payload.Deserialize<ThemeProfile>(Options)!;
        }
        theme.SourceVersion = manifest["Version"]?.GetValue<string>() ?? "0.0";
        return theme;
    }
}
