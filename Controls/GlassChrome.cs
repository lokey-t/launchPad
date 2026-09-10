using System.Windows;
using System.Windows.Media;
using LaunchPad.Models;

namespace LaunchPad.Controls;

/// <summary>Surface reflections only; native background blur is separate from UI text.</summary>
public sealed class GlassChrome : FrameworkElement
{
    private ThemeProfile _profile;
    public double Radius { get; set; } = 16;
    public GlassChrome() { IsHitTestVisible = false; }
    public void SetProfile(ThemeProfile profile)
    {
        _profile = profile.Copy(); InvalidateVisual();
    }
    private static Color White(byte alpha) => Color.FromArgb(alpha,255,255,255);
    protected override void OnRender(DrawingContext dc)
    {
        if (_profile?.Material != "Frosted" || ActualWidth<8 || ActualHeight<8) return;
        var bounds = new Rect(1,1,ActualWidth-2,ActualHeight-2);
        var outer = new RectangleGeometry(bounds,Radius,Radius);
        dc.PushClip(outer);
        // The enclosing card owns the only outline; a second inset stroke creates
        // mismatched arcs at the window corners. Keep this surface neutral.
        dc.Pop();
    }

}
