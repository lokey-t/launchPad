using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LaunchPad.Models;

namespace LaunchPad.Services;

public static class PromptDialog
{
    public static string Show(Window owner, string title, string label, string defaultValue)
    {
        var window = CreateWindow(owner, title, label, defaultValue, true, true);
        return window.ShowDialog() == true ? ((TextBox)window.Tag).Text.Trim() : null;
    }

    public static bool Confirm(Window owner, string title, string message) =>
        CreateWindow(owner, title, message, "", false, true).ShowDialog() == true;

    public static void Notify(Window owner, string title, string message) =>
        CreateWindow(owner, title, message, "", false, false).ShowDialog();

    /// <summary>弹出全局快捷键捕获对话框。返回 (modifiers, key)，取消返回 null。</summary>
    public static (int modifiers, int key)? CaptureHotkey(Window owner, string title, string description,
        int initialMods = 0, int initialKey = 0)
    {
        Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
        var config = (Application.Current as App)?.Config ?? new LauncherConfig();
        int capturedMods = initialMods, capturedKey = initialKey;
        bool hasCapture = initialMods != 0 && initialKey != 0;

        var window = new Window
        {
            Owner = owner, Title = title, Width = 420, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Opacity = 0
        };
        var card = new Border
        {
            Margin = new Thickness(16), Padding = new Thickness(28), CornerRadius = new CornerRadius(20),
            Background = Resource("PanelBgBrush"), BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1)
        };
        var layout = new StackPanel(); card.Child = layout; window.Content = card;
        InputBehavior.Apply(window);

        // 标题栏
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        var heading = new TextBlock
        {
            Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold,
            Foreground = Resource("TextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(heading);
        var close = MakeButton("✕", false, 30); close.Height = 30; close.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(close); layout.Children.Add(header);
        header.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed && (e.OriginalSource == heading || e.OriginalSource == header)) window.DragMove(); };

        // 说明
        layout.Children.Add(new TextBlock
        {
            Text = description, FontSize = 12, LineHeight = 21,
            Foreground = Resource("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18)
        });

        // 快捷键显示框
        var displayBorder = new Border
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            BorderBrush = Resource("BorderBrush"), Background = Resource("SearchBgBrush"),
            Padding = new Thickness(16, 18, 16, 18), HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var displayText = new TextBlock
        {
            Text = hasCapture ? MainWindow.FormatHotkey(capturedMods, capturedKey) : "请按下组合键…",
            FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = hasCapture ? Resource("AccentDeepBrush") : Resource("TextHintBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        displayBorder.Child = displayText;
        layout.Children.Add(displayBorder);

        // 状态提示
        var statusText = new TextBlock
        {
            Text = hasCapture ? "已捕获当前快捷键，可重新按下更换，或点击“确定”保存。" : "正在监听按键，请按下组合键（需包含 Ctrl / Alt / Shift / Win 之一），按 Esc 取消。",
            FontSize = 11, Foreground = Resource("TextHintBrush"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap
        };
        layout.Children.Add(statusText);

        // 按钮
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        bool finishing = false, allowClose = false;
        (int mods, int key)? result = null;
        void Finish(bool ok)
        {
            if (finishing) return;
            finishing = true;
            if (ok && hasCapture) result = (capturedMods, capturedKey);
            MotionService.Hide(window, config, () => { allowClose = true; window.DialogResult = ok; });
        }
        window.Closing += (_, e) => { if (allowClose) return; e.Cancel = true; Finish(false); };
        close.Click += (_, _) => Finish(false);
        var cancelBtn = MakeButton("取消", false, 84); cancelBtn.Margin = new Thickness(0, 0, 10, 0);
        cancelBtn.Click += (_, _) => Finish(false); buttons.Children.Add(cancelBtn);
        var clearBtn = MakeButton("清除", false, 84); clearBtn.Margin = new Thickness(0, 0, 10, 0);
        clearBtn.IsEnabled = hasCapture;
        clearBtn.Click += (_, _) => { capturedMods = 0; capturedKey = 0; hasCapture = false; result = (0, 0); Finish(true); };
        buttons.Children.Add(clearBtn);
        var okBtn = MakeButton("确定", true, 100); okBtn.IsEnabled = hasCapture;
        okBtn.Click += (_, _) => Finish(true); buttons.Children.Add(okBtn);
        layout.Children.Add(buttons);

        // 键盘捕获
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Finish(false); e.Handled = true; return; }
            if (e.Key == Key.Enter && okBtn.IsEnabled) { Finish(true); e.Handled = true; return; }

            var mods = 0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= (int)NativeMethods.MOD_CONTROL;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= (int)NativeMethods.MOD_ALT;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= (int)NativeMethods.MOD_SHIFT;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= (int)NativeMethods.MOD_WIN;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;
            if (mods == 0)
            {
                statusText.Text = "请至少包含一个修饰键（Ctrl / Alt / Shift / Win）。";
                e.Handled = true;
                return;
            }
            capturedMods = mods;
            capturedKey = KeyInterop.VirtualKeyFromKey(key);
            hasCapture = true;
            displayText.Text = MainWindow.FormatHotkey(capturedMods, capturedKey);
            displayText.Foreground = Resource("AccentDeepBrush");
            displayBorder.BorderBrush = Resource("AccentDeepBrush");
            statusText.Text = "已捕获，可重新按下更换，或点击“确定”保存。";
            okBtn.IsEnabled = true;
            clearBtn.IsEnabled = true;
            e.Handled = true;
        };
        window.Loaded += (_, _) => { MotionService.EnterDialog(window, config); window.Focus(); };
        window.ShowDialog();
        return result;

        Button MakeButton(string text, bool primary, double width)
        {
            var button = new Button
            {
                Content = text, Width = width, Height = 40, FontSize = 13, Cursor = Cursors.Hand,
                Background = Resource(primary ? "AccentDeepBrush" : "SubPanelBgBrush"),
                Foreground = primary ? Brushes.White : Resource("TextSecondaryBrush")
            };
            var template = new ControlTemplate(typeof(Button));
            var surface = new FrameworkElementFactory(typeof(Border));
            surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            surface.AppendChild(presenter); template.VisualTree = surface;
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4)); template.Triggers.Add(disabled);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, .8)); template.Triggers.Add(hover);
            button.Template = template;
            return button;
        }
    }

    internal static Window CreateWindow(Window owner, string title, string description, string initial, bool input, bool cancel)
    {
        Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
        var config = (Application.Current as App)?.Config ?? new LauncherConfig();
        bool dismissOnBackdrop = input && title == "新建分类";
        var window = new Window
        {
            Owner = owner, Title = title, Width = 456, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Opacity = 0
        };
        var card = new Border { Margin = new Thickness(16), Padding = new Thickness(28), CornerRadius = new CornerRadius(20),
            Background = Resource("PanelBgBrush"), BorderBrush = Resource("BorderBrush"), BorderThickness = new Thickness(1) };
        var layout = new StackPanel(); card.Child = layout; window.Content = card;
        InputBehavior.Apply(window);
        Grid dismissLayer = null;
        if (dismissOnBackdrop && owner != null)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Width = owner.ActualWidth;
            window.Height = owner.ActualHeight;
            window.Left = owner.Left;
            window.Top = owner.Top;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            // 遮罩用透明色：依然可命中鼠标（点击空白关闭弹窗），但不再遮挡背后的启动台
            dismissLayer = new Grid { Background = Brushes.Transparent };
            card.Width = 424;
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            window.Content = null;
            dismissLayer.Children.Add(card);
            window.Content = dismissLayer;
        }
        var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        var heading = new TextBlock { Text = title, FontSize = 21, FontWeight = FontWeights.SemiBold,
            Foreground = Resource("TextPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(heading);
        var close = Button("✕", false, 30); close.Height = 30; close.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(close); layout.Children.Add(header);
        header.MouseLeftButtonDown += (_,e) => { if (!dismissOnBackdrop && e.LeftButton == MouseButtonState.Pressed && (e.OriginalSource == heading || e.OriginalSource == header)) window.DragMove(); };
        layout.Children.Add(new TextBlock { Text = input ? (title.Contains("分类") ? "为常用应用建立清晰的分类，之后也可以随时重命名。" : "只修改启动台中的显示名称，不会重命名磁盘文件。") : description,
            FontSize = 12, LineHeight = 21, Foreground = Resource("TextSecondaryBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,20) });
        var box = new TextBox { Text = initial ?? "", FontSize = 14, Height = 42, MaxLength = 48,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Resource("TextPrimaryBrush"),
            CaretBrush = Resource("TextPrimaryBrush"), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(12,0,12,0) };
        window.Tag = box;
        if (input)
        {
            layout.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Resource("TextPrimaryBrush"), Margin = new Thickness(0,0,0,8) });
            var field = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1),
                BorderBrush = Resource("BorderBrush"), Background = Resource("SearchBgBrush"), Child = box };
            box.GotKeyboardFocus += (_,_) => field.BorderBrush = Resource("AccentDeepBrush");
            box.LostKeyboardFocus += (_,_) => field.BorderBrush = Resource("BorderBrush");
            layout.Children.Add(field);
            layout.Children.Add(new TextBlock { Text = "例如：工作、设计、常用工具", FontSize = 11,
                Foreground = Resource("TextHintBrush"), Margin = new Thickness(0,8,0,0) });
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,26,0,0) };
        bool finishing = false, allowClose = false;
        void Finish(bool result)
        {
            if (finishing || result && input && string.IsNullOrWhiteSpace(box.Text)) return;
            finishing = true;
            MotionService.Hide(window, config, () => { allowClose = true; window.DialogResult = result; });
        }
        window.Closing += (_,e) => { if (allowClose) return; e.Cancel = true; Finish(false); };
        if (dismissLayer != null)
            dismissLayer.MouseLeftButtonDown += (_,e) => { if (e.OriginalSource == dismissLayer) { Finish(false); e.Handled = true; } };
        if (dismissOnBackdrop) window.Deactivated += (_,_) => Finish(false);
        close.Click += (_,_) => Finish(false);
        if (cancel)
        {
            var cancelButton = Button("取消", false, 84); cancelButton.Margin = new Thickness(0,0,10,0);
            cancelButton.Click += (_,_) => Finish(false); buttons.Children.Add(cancelButton);
        }
        var ok = Button(input && title.Contains("新建") ? "创建分类" : "确定", true, 100);
        ok.IsEnabled = !input || !string.IsNullOrWhiteSpace(box.Text);
        box.TextChanged += (_,_) => ok.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        ok.Click += (_,_) => Finish(true); buttons.Children.Add(ok); layout.Children.Add(buttons);
        window.PreviewKeyDown += (_,e) =>
        {
            if (e.Key == Key.Escape) { Finish(false); e.Handled = true; }
            if (e.Key == Key.Enter && ok.IsEnabled) { Finish(true); e.Handled = true; }
        };
        window.Loaded += (_,_) => { MotionService.EnterDialog(window, config); if (input) { box.Focus(); box.SelectAll(); } };
        return window;

        Button Button(string text, bool primary, double width)
        {
            var button = new Button { Content = text, Width = width, Height = 40, FontSize = 13, Cursor = Cursors.Hand,
                Background = Resource(primary ? "AccentDeepBrush" : "SubPanelBgBrush"),
                Foreground = primary ? Brushes.White : Resource("TextSecondaryBrush") };
            var template = new ControlTemplate(typeof(Button));
            var surface = new FrameworkElementFactory(typeof(Border));
            surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            surface.AppendChild(presenter); template.VisualTree = surface;
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4)); template.Triggers.Add(disabled);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, .8)); template.Triggers.Add(hover);
            button.Template = template;
            return button;
        }
    }
}
