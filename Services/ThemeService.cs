using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LaunchPad.Models;

namespace LaunchPad.Services;

public static class ThemeService
{
    public static IReadOnlyList<ThemeProfile> Presets { get; } = new[]
    {
        new ThemeProfile { Name = "云白" },
        new ThemeProfile { Name = "午夜", BaseTheme = "Dark", Surface = "#20242C", Text = "#F2F4F8", Accent = "#93B4FA" },
        new ThemeProfile { Name = "海盐", Surface = "#EAF5F8", Text = "#183F51", Accent = "#157F9B" },
        new ThemeProfile { Name = "苔绿", Surface = "#EDF2EA", Text = "#293E32", Accent = "#477653" },
        new ThemeProfile { Name = "暮紫", BaseTheme = "Dark", Surface = "#302B40", Text = "#F3EBFF", Accent = "#C3A1EB" },
        new ThemeProfile { Name = "蔷薇", Surface = "#FCF0F2", Text = "#59363F", Accent = "#AF5B76" }
    };

    public static ThemeProfile Global(LauncherConfig config)
    {
        if (config.GlobalTheme != null) return config.GlobalTheme.Copy();
        bool dark = config.Theme == "Dark";
        if (config.Theme == "System")
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        return Presets[dark ? 1 : 0].Copy();
    }

    public static ThemeProfile Resolve(LauncherConfig config, AppCategory category)
    {
        var result = Global(config);
        if (category?.ThemeOverride is not ThemeProfile local) return result;
        if (local.OverrideColors)
        {
            result.Name = local.Name; result.BaseTheme = local.BaseTheme;
            result.Surface = local.Surface; result.Text = local.Text; result.Accent = local.Accent;
        }
        if (local.OverrideBackground)
        {
            result.BackgroundPath = local.BackgroundPath; result.BackgroundDim = local.BackgroundDim;
        }
        return result;
    }

    public static Color Parse(string value, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    public static void Apply(ResourceDictionary resources, ThemeProfile profile, double milliseconds)
    {
        var baseline = new ResourceDictionary { Source = new Uri($"pack://application:,,,/LaunchPad;component/Themes/{(profile.BaseTheme == "Dark" ? "Dark" : "Light")}.xaml") };
        var surface = Parse(profile.Surface, "#FFFFFF");
        var text = Parse(profile.Text, "#1F2328");
        var accent = Parse(profile.Accent, "#2E6D99");
        Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R+(b.R-a.R)*t), (byte)(a.G+(b.G-a.G)*t), (byte)(a.B+(b.B-a.B)*t));
        var colors = new Dictionary<string, Color>();
        foreach (string key in baseline.Keys) if (baseline[key] is SolidColorBrush brush) colors[key] = brush.Color;
        colors["PanelBgBrush"] = surface; colors["WindowBgBrush"] = surface;
        colors["SubPanelBgBrush"] = Mix(surface,text,.035); colors["SearchBgBrush"] = Mix(surface,text,.05);
        colors["TextPrimaryBrush"] = text; colors["TextSecondaryBrush"] = Mix(text,surface,.22); colors["TextHintBrush"] = Mix(text,surface,.4);
        colors["AccentBrush"] = accent; colors["AccentDeepBrush"] = accent; colors["FolderBrush"] = accent;
        colors["AccentLightBrush"] = Mix(surface,accent,.17); colors["HoverBrush"] = Mix(surface,accent,.1);
        colors["BorderBrush"] = Mix(surface,text,.14); colors["DividerBrush"] = Mix(surface,text,.08);
        colors["TabActiveBgBrush"] = accent;
        colors["TabActiveTextBrush"] = accent.R*.299+accent.G*.587+accent.B*.114 > 155 ? Color.FromRgb(25,28,35) : Colors.White;
        foreach (var pair in colors)
        {
            var previous = resources[pair.Key] as SolidColorBrush;
            var start = previous?.Color ?? pair.Value;
            var next = new SolidColorBrush(pair.Value);
            if (milliseconds > 0 && start != pair.Value)
                next.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(start,pair.Value,TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            resources[pair.Key] = next;
        }
    }
}
