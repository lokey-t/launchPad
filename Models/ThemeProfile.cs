namespace LaunchPad.Models;

public class ThemeProfile
{
    public string Name { get; set; } = "自定义";
    public string BaseTheme { get; set; } = "Light";
    public string Material { get; set; } = "Solid";
    public bool? OverrideMaterial { get; set; }
    public string Accent { get; set; } = "#2E6D99";
    public string Surface { get; set; } = "#FFFFFF";
    public string Text { get; set; } = "#1F2328";
    public bool OverrideColors { get; set; } = true;
    public bool OverrideBackground { get; set; } = true;
    public string BackgroundPath { get; set; }
    public double BackgroundDim { get; set; } = .35;
    public double GlassBlurStrength { get; set; } = -1;
    public double GlassColorDepth { get; set; } = .7;
    public double GlassOpacity { get; set; } = -1;
    public bool OverrideIcons { get; set; }
    public string IconBackground { get; set; } = "#FFFFFF";
    public double IconOpacity { get; set; } = 0;
    public string IconBorderMode { get; set; } = "None";
    public string IconBorderColor { get; set; } = "#FFFFFF";
    public double IconBorderWidth { get; set; } = 1;
    public double IconShadowDirection { get; set; } = 270;
    public double IconShadowDepth { get; set; } = 3;
    public double IconShadowStrength { get; set; } = .3;
    public ThemeProfile Copy() => (ThemeProfile)MemberwiseClone();
}
