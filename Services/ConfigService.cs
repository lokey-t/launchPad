using System.IO;
using System.Text.Json;
using LaunchPad.Models;

namespace LaunchPad.Services;

/// <summary>配置持久化：%AppData%\LaunchPad\config.json。</summary>
public static class ConfigService
{
    private static bool _saveBlocked;
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaunchPad");

    private static readonly string FilePath = Path.Combine(Dir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var cfg = JsonSerializer.Deserialize<LauncherConfig>(json, JsonOpts);
                if (cfg?.Categories == null || cfg.GlobalOrder == null ||
                    cfg.Categories.Any(c => c == null || c.Entries == null ||
                        c.Entries.Any(e => e == null || e.Name == null ||
                            e is AppFolder f && (f.Items == null || f.Items.Any(i => i == null)))))
                    throw new JsonException("配置结构无效。");
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
        return new LauncherConfig();
    }

    public static void Save(LauncherConfig config)
    {
        if (_saveBlocked || config == null) return;
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(config, JsonOpts);
            File.WriteAllText(temporaryPath, json);
            if (File.Exists(FilePath))
                File.Replace(temporaryPath, FilePath, FilePath + ".bak");
            else
                File.Move(temporaryPath, FilePath);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"配置保存失败，改动尚未保存：{ex.Message}", "LaunchPad");
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
