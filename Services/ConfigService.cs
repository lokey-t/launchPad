using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using LaunchPad.Models;

namespace LaunchPad.Services;

/// <summary>配置持久化：%AppData%\LaunchPad\config.json。</summary>
public static class ConfigService
{
    private static bool _saveBlocked;
    internal static string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaunchPad");

    private static string Dir => DataDirectory;
    private static string FilePath => Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static LauncherConfig Deserialize(string json)
    {
        var original = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("配置结构无效。");
        if (original["Categories"] is not JsonArray) throw new JsonException("配置缺少分类列表。");
        var readable = original.DeepClone().AsObject();
        FilterFutureEntries(readable);
        var config = JsonSerializer.Deserialize<LauncherConfig>(readable.ToJsonString(), JsonOpts);
        if (config?.Categories == null || config.GlobalOrder == null ||
            config.Categories.Any(c => c == null || c.Entries == null || c.Entries.Any(e =>
                e == null || e.Name == null || e is AppFolder f && (f.Items == null || f.Items.Any(i => i == null)))))
            throw new JsonException("配置结构无效。");
        config.OriginalDocument = original;
        return config;
    }

    private static bool FutureEntry(JsonNode node) => node is JsonObject obj &&
        obj["kind"] is JsonValue kind && kind.TryGetValue<string>(out var text) && text != "app" && text != "folder";

    private static void FilterFutureEntries(JsonNode node)
    {
        if (node is JsonObject obj)
            foreach (var pair in obj.ToList())
            {
                if (pair.Key == "Entries" && pair.Value is JsonArray entries)
                    for (int i = entries.Count - 1; i >= 0; i--) if (FutureEntry(entries[i])) entries.RemoveAt(i);
                if (pair.Value != null) FilterFutureEntries(pair.Value);
            }
        else if (node is JsonArray array) foreach (var child in array) if (child != null) FilterFutureEntries(child);
    }

    public static string Serialize(LauncherConfig config)
    {
        var current = JsonSerializer.SerializeToNode(config, JsonOpts).AsObject();
        // Unknown future entry types remain in the document while this version hides them.
        if (config.OriginalDocument?["Categories"] is JsonArray previous && current["Categories"] is JsonArray categories)
            foreach (var category in categories.OfType<JsonObject>())
            {
                var old = previous.OfType<JsonObject>().FirstOrDefault(c => c["Id"]?.ToString() == category["Id"]?.ToString());
                if (old?["Entries"] is not JsonArray oldEntries || category["Entries"] is not JsonArray entries) continue;
                for (int i = 0; i < oldEntries.Count; i++)
                    if (FutureEntry(oldEntries[i]))
                    {
                        entries.Insert(Math.Min(i, entries.Count), oldEntries[i].DeepClone());
                        var id = oldEntries[i]["Id"]?.ToString();
                        if (id != null && current["GlobalOrder"] is JsonArray order && !order.Any(n => n?.ToString() == id)) order.Add(id);
                    }
            }
        return current.ToJsonString(JsonOpts);
    }

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = Deserialize(json);
                if (cfg?.Categories == null || cfg.GlobalOrder == null ||
                    cfg.Categories.Any(c => c == null || c.Entries == null ||
                        c.Entries.Any(e => e == null || e.Name == null ||
                            e is AppFolder f && (f.Items == null || f.Items.Any(i => i == null)))))
                    throw new JsonException("配置结构无效。");
                // 液态玻璃材质已下线，存量配置统一迁移为磨砂玻璃
                if (cfg.GlobalTheme?.Material == "Liquid") cfg.GlobalTheme.Material = "Frosted";
                foreach (var cat in cfg.Categories)
                    if (cat.ThemeOverride?.Material == "Liquid") cat.ThemeOverride.Material = "Frosted";
                return cfg;
            }
        }
        catch (Exception ex)
        {
            try
            {
                var backup = FilePath + ".corrupt-" + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(FilePath, backup);
                System.Windows.MessageBox.Show($"配置读取失败，原文件已备份至：{backup}\n本次使用默认配置。\n{ex.Message}", "LaunchPad");
            }
            catch
            {
                _saveBlocked = true;
                System.Windows.MessageBox.Show("配置读取失败且无法备份，为保护原数据，本次运行不会保存配置。", "LaunchPad");
            }
        }
        return new LauncherConfig { OnboardingPending = !File.Exists(FilePath) };
    }

    public static bool Save(LauncherConfig config)
    {
        if (_saveBlocked || config == null) return false;
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Dir);
            var json = Serialize(config);
            if (File.Exists(FilePath) && File.ReadAllText(FilePath) == json)
            {
                BackupService.CheckAutomatic(config);
                return true;
            }
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(FilePath))
                File.Replace(temporaryPath, FilePath, FilePath + ".bak");
            else
                File.Move(temporaryPath, FilePath);
            BackupService.CheckAutomatic(config);
            return true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"配置保存失败，改动尚未保存：{ex.Message}", "LaunchPad");
            return false;
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
        }
    }

    public static string ConfigPath => FilePath;

    // Secondary instances must never load defaults and save over the running instance's data.
    public static (int Modifiers, int Key) ReadHotkey()
    {
        try
        {
            var config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(FilePath), JsonOpts);
            return config == null ? (2, 32) : (config.HotkeyModifiers, config.HotkeyKey);
        }
        catch { return (2, 32); }
    }
}
