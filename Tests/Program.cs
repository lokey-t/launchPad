using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LaunchPad;
using LaunchPad.Models;
using LaunchPad.Services;

internal static class Program
{
    private static void Assert(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    [STAThread]
    private static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "LaunchPad-BackupTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        typeof(ConfigService).GetProperty("DataDirectory", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, root);
        var config = new LauncherConfig { AutoBackup = true };
        var category = config.GetOrCreateCategory("测试分类");
        var icon = Path.Combine(root, "icon.png");
        File.WriteAllBytes(icon, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg=="));
        category.Entries.Add(new AppItem { Name = "示例应用", Path = @"C:\example.exe", CustomIconPath = icon });
        config.GlobalTheme = new ThemeProfile { BackgroundPath = icon };
        ConfigService.Save(config);
        Assert(BackupService.List().Count == 1, "first automatic point");
        ConfigService.Save(config);
        Assert(BackupService.List().Count == 1, "unchanged save deduplicated");
        var protectedPoint = BackupService.List()[0];
        BackupService.Update(protectedPoint, "永久保留", true);
        for (var i = 0; i < 35; i++) { config.HotkeyKey = 40 + i; ConfigService.Save(config); }
        Assert(BackupService.List().Count(p => !p.Saved) == 30, "automatic retention capped at 30");
        Assert(BackupService.List().Any(p => p.Id == protectedPoint.Id), "saved point survives rotation");
        var manual = BackupService.Create(config, "手动备份");
        Assert(manual.Saved, "manual points saved by default");
        var export = Path.Combine(root, "roundtrip.qdtbackup");
        BackupService.Export(manual, export);
        using (var zip = ZipFile.OpenRead(export))
            Assert(zip.GetEntry("config.json") != null && zip.Entries.Count(e => e.FullName.StartsWith("assets/")) == 1, "portable package deduplicates icon/background bytes");
        var imported = BackupService.Import(export);
        Assert(imported.Saved, "imported point saved");
        File.Delete(icon);
        var restored = BackupService.Restore(imported);
        Assert(File.Exists(restored.GlobalTheme.BackgroundPath) && File.Exists(((AppItem)restored.Categories[0].Entries[0]).CustomIconPath), "restore assets without original files");
        Assert(((AppItem)restored.Categories[0].Entries[0]).Path == @"C:\example.exe", "application target unchanged");
        var future = JsonNode.Parse(ConfigService.Serialize(restored));
        future["FutureSetting"] = new JsonObject { ["Enabled"] = true };
        future["GlobalTheme"]["FutureEffect"] = 42;
        future["Categories"][0]["FutureCategory"] = "retained";
        future["Categories"][0]["Entries"][0]["FutureIcon"] = "retained";
        future["Categories"][0]["Entries"].AsArray().Add(new JsonObject { ["kind"] = "future-widget", ["Id"] = "future-id", ["Name"] = "Future" });
        var older = ConfigService.Deserialize(future.ToJsonString());
        older.Theme = "Dark";
        older.GlobalOrder.Clear();
        var roundtrip = JsonNode.Parse(ConfigService.Serialize(older));
        Assert(roundtrip["FutureSetting"]["Enabled"].GetValue<bool>() && roundtrip["GlobalTheme"]["FutureEffect"].GetValue<int>() == 42 &&
            roundtrip["Categories"][0]["FutureCategory"].ToString() == "retained" && roundtrip["Categories"][0]["Entries"][0]["FutureIcon"].ToString() == "retained", "future fields survive editing at all levels");
        Assert(older.Categories[0].Entries.Count == 1 && roundtrip["Categories"][0]["Entries"].AsArray().Count == 2 && roundtrip["GlobalOrder"].AsArray().Any(n => n.ToString() == "future-id"), "unknown entry types retained but hidden");
        var old = ConfigService.Deserialize("{\"Categories\":[]}");
        Assert(old.IconSize == "Medium" && !old.AutoBackup, "old configuration uses new defaults");
        var malicious = Path.Combine(root, "invalid.qdtbackup");
        using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("config.json").Open())) writer.Write("{\"Categories\":[]}");
            zip.CreateEntry("assets/../../escape.txt");
        }
        var count = BackupService.List().Count;
        try { BackupService.Import(malicious); throw new Exception("unsafe archive accepted"); }
        catch (InvalidDataException) { Assert(BackupService.List().Count == count, "path traversal rejected without adding point"); }
        BackupService.Update(imported, "重命名后的备份", true);
        Assert(BackupService.List().Any(p => p.Name == "重命名后的备份"), "rename persisted");
        BackupService.Delete(imported);
        Assert(BackupService.List().All(p => p.Id != imported.Id), "saved point can be deleted");
        var latest = BackupService.List().First(p => !p.Saved);
        var live = BackupService.Restore(latest);
        ConfigService.Save(live);
        BackupService.CheckExternalChanges(live);
        var before = BackupService.List().First(p => !p.Saved).Id;
        File.AppendAllText(live.GlobalTheme.BackgroundPath, "changed");
        BackupService.CheckExternalChanges(live);
        Assert(BackupService.List().First(p => !p.Saved).Id != before, "static file change creates new point");
        config.AutoBackup = false;
        ConfigService.Save(config);
        before = BackupService.List().First(p => !p.Saved).Id;
        config.Theme = "Dark";
        ConfigService.Save(config);
        Assert(BackupService.List().First(p => !p.Saved).Id == before, "disabled automatic backup creates no points");

        typeof(BackupFileAssociation).GetProperty("PipeName", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, "LaunchPad.Test." + Guid.NewGuid().ToString("N"));
        using (var cancellation = new CancellationTokenSource())
        {
            var received = new TaskCompletionSource<string>();
            var listener = Task.Run(() => BackupFileAssociation.Listen(path => received.TrySetResult(path), cancellation.Token));
            Assert(BackupFileAssociation.Forward(export), "secondary instance forwards backup file");
            Assert(received.Task.Wait(TimeSpan.FromSeconds(5)) && received.Task.Result == export, "running instance receives restore path");
            cancellation.Cancel();
            listener.GetAwaiter().GetResult();
        }
        config.AnimationMode = "Off";
        var app = new App();
        app.InitializeComponent();
        typeof(App).GetProperty("Config").SetValue(app, config);
        config.GlobalTheme = null;
        var window = new SettingsWindow(app);
        window.RefreshFromConfig();
        ((RadioButton)window.FindName("NavBackup")).IsChecked = true;
        var surface = (FrameworkElement)window.Content;
        surface.Measure(new Size(860, 680));
        surface.Arrange(new Rect(0, 0, 860, 680));
        surface.UpdateLayout();
        ((FrameworkElement)window.FindName("SettingsPages")).BeginAnimation(UIElement.OpacityProperty, null);
        foreach (var theme in new[] { "Light", "Dark" })
        {
            config.Theme = theme;
            app.ApplyTheme(theme);
            window.RefreshMaterial();
            surface.UpdateLayout();
            var bitmap = new RenderTargetBitmap(860, 680, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(surface);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, $"backup-{theme}.png"));
            encoder.Save(stream);
        }
        Assert(((FrameworkElement)window.FindName("PanelBackup")).Visibility == Visibility.Visible, "settings backup page renders in both themes");
        Console.WriteLine("All backup tests passed. Isolated data: " + root);
    }
}
