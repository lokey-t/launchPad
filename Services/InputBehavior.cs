using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LaunchPad.Services;

public static class InputBehavior
{
    static InputBehavior()
    {
        EventManager.RegisterClassHandler(typeof(ContextMenu), Keyboard.PreviewKeyDownEvent, new KeyEventHandler(SuppressNavigationHints));
    }

    public static void SuppressNavigationHints(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.LeftAlt or Key.RightAlt) e.Handled = true;
    }
    public static void Apply(Window window)
    {
        KeyboardNavigation.SetTabNavigation(window, KeyboardNavigationMode.None);
        KeyboardNavigation.SetControlTabNavigation(window, KeyboardNavigationMode.None);
        window.PreviewKeyDown += SuppressNavigationHints;
        var style = new Style(typeof(Control));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control))));
        window.Resources[SystemParameters.FocusVisualStyleKey] = style;
    }
}
