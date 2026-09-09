using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LaunchPad.Models;

namespace LaunchPad.Services;

public static class MotionService
{
    public static double Duration(LauncherConfig config, double balanced)
    {
        if (!SystemParameters.ClientAreaAnimation || config?.AnimationMode == "Off") return 0;
        return balanced * (config?.AnimationMode switch { "Fast" => .55, "Optimized" => 1.2, _ => 1 });
    }

    public static IEasingFunction Easing(LauncherConfig config) => config?.AnimationMode == "Optimized"
        ? new QuinticEase { EasingMode = EasingMode.EaseOut }
        : new CubicEase { EasingMode = EasingMode.EaseOut };

    private sealed class State { public int Version; public bool Closing; }
    private static readonly ConditionalWeakTable<Window, State> States = new();
    public static bool IsClosing(Window window) => States.GetOrCreateValue(window).Closing;

    public static void Show(Window window, LauncherConfig config)
    {
        var state = States.GetOrCreateValue(window);
        if (window.IsVisible && !state.Closing) { window.Activate(); return; }
        ++state.Version;
        state.Closing = false;
        bool wasVisible = window.IsVisible;
        double opacity = wasVisible ? window.Opacity : 0;
        window.BeginAnimation(UIElement.OpacityProperty, null);
        window.Opacity = opacity;
        window.IsHitTestVisible = true;
        if (!wasVisible) window.Show();
        Animate(window, config, true, opacity, null);
        window.Activate();
    }

    public static void EnterDialog(Window window, LauncherConfig config)
    {
        ++States.GetOrCreateValue(window).Version;
        Animate(window, config, true, 0, null);
    }

    public static void Hide(Window window, LauncherConfig config, Action finished = null)
    {
        var state = States.GetOrCreateValue(window);
        if (state.Closing) return;
        state.Closing = true;
        int version = ++state.Version;
        window.IsHitTestVisible = false;
        Animate(window, config, false, window.Opacity, () =>
        {
            if (version != state.Version) return;
            state.Closing = false;
            if (finished != null) finished(); else window.Hide();
        });
    }

    private static void Animate(Window window, LauncherConfig config, bool opening, double from, Action done)
    {
        double ms = Duration(config, opening ? 220 : 160);
        double to = opening ? 1 : 0;
        window.BeginAnimation(UIElement.OpacityProperty, null);
        window.Opacity = to;
        if (window.Content is FrameworkElement root)
        {
            double previousScale = root.RenderTransform is ScaleTransform previous ? previous.ScaleX : 1;
            root.RenderTransformOrigin = new Point(.5, .5);
            var scale = new ScaleTransform(1, 1);
            root.RenderTransform = scale;
            if (ms > 0)
            {
                double start = opening && from == 0 ? .965 : previousScale, end = opening ? 1 : .975;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(start, end, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Easing(config) });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(start, end, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Easing(config) });
            }
        }
        if (ms == 0) { done?.Invoke(); return; }
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = Easing(config), FillBehavior = FillBehavior.Stop };
        if (done != null) animation.Completed += (_, _) => done();
        window.BeginAnimation(UIElement.OpacityProperty, animation);
    }
}
