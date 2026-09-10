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
        if (local.OverrideMaterial ?? local.OverrideColors) { result.Material = local.Material; result.GlassOpacity=local.GlassOpacity; result.GlassBlurStrength=local.GlassBlurStrength; result.GlassColorDepth=local.GlassColorDepth; }
        if(local.OverrideIcons) { result.IconBackground=local.IconBackground;result.IconOpacity=local.IconOpacity;result.IconBorderMode=local.IconBorderMode;result.IconBorderColor=local.IconBorderColor;result.IconBorderWidth=local.IconBorderWidth;result.IconShadowDirection=local.IconShadowDirection;result.IconShadowDepth=local.IconShadowDepth;result.IconShadowStrength=local.IconShadowStrength; }
        if (local.OverrideBackground)
        {
            result.BackgroundPath = local.BackgroundPath; result.BackgroundDim = local.BackgroundDim;
        }
        return result;
    }

    public static double GlassStrength(ThemeProfile profile)
    {
        double value=profile.GlassBlurStrength>=0?profile.GlassBlurStrength:profile.GlassOpacity;
        return double.IsFinite(value) && value>=0 ? Math.Clamp(value,0,1) : .7;
    }
    public static double GlassDepth(ThemeProfile profile) => double.IsFinite(profile.GlassColorDepth) ? Math.Clamp(profile.GlassColorDepth,0,1) : .7;
    public static Color GlassSurfaceColor(ThemeProfile profile)
    {
        var color=Parse(profile.Surface,"#FFFFFF");
        color.A=profile.Material=="Frosted"?(byte)Math.Max(1,Math.Round(255*GlassDepth(profile))): (byte)255;
        return color;
    }
    public static Color Parse(string value, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(value); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    public static void Apply(ResourceDictionary resources, ThemeProfile profile, double milliseconds)
    {
        resources["IconAppearanceProfile"]=profile.Copy();
        var baseline = new ResourceDictionary { Source = new Uri($"pack://application:,,,/LaunchPad;component/Themes/{(profile.BaseTheme == "Dark" ? "Dark" : "Light")}.xaml") };
        var surface = Parse(profile.Surface, "#FFFFFF");
        var text = Parse(profile.Text, "#1F2328");
        var accent = Parse(profile.Accent, "#2E6D99");
        Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)(a.R+(b.R-a.R)*t), (byte)(a.G+(b.G-a.G)*t), (byte)(a.B+(b.B-a.B)*t));
        var colors = new Dictionary<string, Color>();
        foreach (string key in baseline.Keys)
            if (key is not ("TileSurfaceBrush" or "TileRimBrush") && baseline[key] is SolidColorBrush brush) colors[key] = brush.Color;
        colors["PanelBgBrush"] = surface; colors["WindowBgBrush"] = surface;
        colors["SubPanelBgBrush"] = Mix(surface,text,.035); colors["SearchBgBrush"] = Mix(surface,text,.05);
        colors["TextPrimaryBrush"] = text; colors["TextSecondaryBrush"] = Mix(text,surface,.22); colors["TextHintBrush"] = Mix(text,surface,.4);
        colors["AccentBrush"] = accent; colors["AccentDeepBrush"] = accent; colors["FolderBrush"] = accent;
        colors["AccentLightBrush"] = Mix(surface,accent,.17); colors["HoverBrush"] = Mix(surface,accent,.1);
        colors["BorderBrush"] = Mix(surface,text,.14); colors["DividerBrush"] = Mix(surface,text,.08);
        colors["TabActiveBgBrush"] = accent;
        colors["TabActiveTextBrush"] = accent.R*.299+accent.G*.587+accent.B*.114 > 155 ? Color.FromRgb(25,28,35) : Colors.White;
        if (profile.Material == "Frosted")
        {
            byte alpha = 185;
            foreach (var key in new[] { "SubPanelBgBrush", "SearchBgBrush", "HoverBrush" })
            {
                var color = colors[key]; color.A = alpha; colors[key] = color;
            }
        }
        var folderSurface=surface;
        folderSurface.A=profile.Material=="Frosted"?(byte)185:(byte)255;
        colors["FolderSurfaceBrush"]=folderSurface;
        colors["FolderBackdropBrush"]=Color.FromArgb(profile.Material=="Frosted"?(byte)45:(byte)80,0,0,0);
        foreach (var pair in colors)
        {
            var previous = resources[pair.Key] as SolidColorBrush;
            var start = previous?.Color ?? pair.Value;
            var next = new SolidColorBrush(pair.Value);
            if (milliseconds > 0 && start != pair.Value)
                next.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(start,pair.Value,TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            resources[pair.Key] = next;
        }
        Color Alpha(Color color,byte alpha) { color.A=alpha; return color; }
        var offsets=new[] { 0d,.35,.72,1d };
        bool frosted=false;
        var tileColors=Enumerable.Repeat(frosted?Alpha(surface,205):colors["SubPanelBgBrush"],4).ToArray();
        var rimColors=Enumerable.Repeat(Alpha(Colors.White,frosted?(byte)100:(byte)0),4).ToArray();
        void SetGradient(string key,Color[] targets)
        {
            var old=resources[key] as LinearGradientBrush;
            var gradient=new LinearGradientBrush { StartPoint=new Point(0,0),EndPoint=new Point(1,1) };
            for(int i=0;i<targets.Length;i++)
            {
                var stop=new GradientStop(targets[i],offsets[i]);
                if(milliseconds>0 && old?.GradientStops.Count==targets.Length)
                    stop.BeginAnimation(GradientStop.ColorProperty,new ColorAnimation(old.GradientStops[i].Color,targets[i],TimeSpan.FromMilliseconds(milliseconds)));
                gradient.GradientStops.Add(stop);
            }
            resources[key]=gradient;
        }
        SetGradient("TileSurfaceBrush",tileColors); SetGradient("TileRimBrush",rimColors);
    }
}
