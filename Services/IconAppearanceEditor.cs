using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using LaunchPad.Models;
using LaunchPad.Controls;
namespace LaunchPad.Services;
public static class IconAppearanceEditor
{
    public static bool Show(Window owner, ThemeProfile profile, AppItem entry = null)
    {
        var draft = profile.Copy(); string localColor = entry?.IconBackground; double? localOpacity = entry?.IconOpacity;
        var window = new Window { Owner = owner, Title = "图标外观", Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LaunchPad;component/Themes/ThemeCenter.xaml") });
        var panel = new StackPanel(); var card = new Border { Padding = new Thickness(24), CornerRadius = new CornerRadius(18), Child = panel, BorderThickness = new Thickness(1) }; window.Content = card;
        WindowMaterialService.Attach(window, card, () => WindowMaterialService.ForWindow(owner, (Application.Current as App).Config)); InputBehavior.Apply(window);
        window.Loaded += (_, _) => MotionService.EnterDialog(window, (Application.Current as App).Config);
        TextBlock Label(string text) { var t = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap }; t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush"); panel.Children.Add(t); return t; }
        Button Button(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 4, 0, 4) }; b.SetResourceReference(Control.BackgroundProperty, "SubPanelBgBrush"); b.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush"); b.SetResourceReference(Control.BorderBrushProperty, "BorderBrush"); b.Click += (_, _) => action(); panel.Children.Add(b); return b; }
        Label(entry == null ? "图标外观" : "图标外观 · " + entry.Name).FontSize = 20;
        var preview = new IconSurface { Width = 58, Height = 58, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 12, 0, 16), Child = new TextBlock { Text = "◇", FontSize = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }; panel.Children.Add(preview);
        void Refresh() { var p = draft.Copy(); if (entry != null) { p.IconBackground = localColor ?? draft.IconBackground; p.IconOpacity = localOpacity ?? draft.IconOpacity; } preview.Profile = p; }
        string Pick(string current) { using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true }; var c = ThemeService.Parse(current, "#FFFFFF"); dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B); return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}" : null; }

        // 阴影设置子弹窗：拖动太阳决定方向与距离，滑块调节强度
        (bool ok, double dir, double depth, double strength) OpenShadowEditor(double dir0, double depth0, double strength0)
        {
            var win = new Window { Owner = window, Title = "阴影设置", Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize };
            win.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LaunchPad;component/Themes/ThemeCenter.xaml") });
            var root = new StackPanel(); var winCard = new Border { Padding = new Thickness(24), CornerRadius = new CornerRadius(18), Child = root, BorderThickness = new Thickness(1) }; win.Content = winCard;
            WindowMaterialService.Attach(win, winCard, () => WindowMaterialService.ForWindow(owner, (Application.Current as App).Config)); InputBehavior.Apply(win);
            win.Loaded += (_, _) => MotionService.EnterDialog(win, (Application.Current as App).Config);
            TextBlock WinLabel(string text) { var t = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 8), TextWrapping = TextWrapping.Wrap }; t.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush"); root.Children.Add(t); return t; }
            Button WinButton(string text, Action action) { var b = new Button { Content = text, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 4, 0, 4) }; b.SetResourceReference(Control.BackgroundProperty, "SubPanelBgBrush"); b.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush"); b.SetResourceReference(Control.BorderBrushProperty, "BorderBrush"); b.Click += (_, _) => action(); root.Children.Add(b); return b; }
            WinLabel("拖动太阳调整阴影的方向与距离").FontSize = 16;
            const double CanvasW = 272, CanvasH = 196, IconSize = 58, SunSize = 30, MaxDist = 86;
            var canvas = new Canvas { Width = CanvasW, Height = CanvasH, ClipToBounds = true, Margin = new Thickness(0, 6, 0, 6) };
            var refCircle = new Ellipse { Width = MaxDist * 2, Height = MaxDist * 2, Stroke = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, Fill = Brushes.Transparent };
            canvas.Children.Add(refCircle); Canvas.SetLeft(refCircle, CanvasW / 2 - MaxDist); Canvas.SetTop(refCircle, CanvasH / 2 - MaxDist);
            var icon = new Border { Width = IconSize, Height = IconSize, CornerRadius = new CornerRadius(12), Child = new TextBlock { Text = "◇", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, IsHitTestVisible = false };
            icon.SetResourceReference(Border.BackgroundProperty, "SubPanelBgBrush");
            canvas.Children.Add(icon); Canvas.SetLeft(icon, CanvasW / 2 - IconSize / 2); Canvas.SetTop(icon, CanvasH / 2 - IconSize / 2);
            var sun = new Ellipse { Width = SunSize, Height = SunSize, Cursor = Cursors.Hand };
            sun.Fill = new RadialGradientBrush { GradientOrigin = new Point(.3, .3), Center = new Point(.3, .3), GradientStops = { new GradientStop(Color.FromRgb(255, 235, 150), 0), new GradientStop(Color.FromRgb(255, 176, 46), 1) } };
            sun.Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)); sun.StrokeThickness = 2;
            canvas.Children.Add(sun);
            var body = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 6) };
            body.Children.Add(canvas);
            // 右侧：与主界面图标同尺寸的真实预览，实时反映当前阴影参数
            var config = (Application.Current as App)?.Config;
            double boxSize = config?.IconSize == "Small" ? 52 : config?.IconSize == "Large" ? 80 : 64;
            var previewHost = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(22, 0, 0, 0) };
            var previewTitle = new TextBlock { Text = "主界面效果", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) }; previewTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); previewHost.Children.Add(previewTitle);
            var realPreview = new IconSurface { Width = boxSize, Height = boxSize, CornerRadius = new CornerRadius(boxSize * .19), Child = new TextBlock { Text = "◇", FontSize = boxSize * .5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            previewHost.Children.Add(realPreview);
            var subPreview = new TextBlock { Text = "拖动太阳或滑块实时更新", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), FontSize = 11 }; subPreview.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush"); previewHost.Children.Add(subPreview);
            body.Children.Add(previewHost);
            root.Children.Add(body);
            double curDir = dir0, curDepth = depth0, curStrength = strength0;
            void PlaceSun(double dirDeg, double depth)
            {
                double angle = Math.PI * (dirDeg + 180) / 180.0;
                double dist = depth / 10.0 * MaxDist;
                Canvas.SetLeft(sun, CanvasW / 2 + dist * Math.Cos(angle) - SunSize / 2);
                Canvas.SetTop(sun, CanvasH / 2 - dist * Math.Sin(angle) - SunSize / 2);
            }
            var summary = WinLabel("");
            void ApplyShadow()
            {
                double cx = Canvas.GetLeft(sun) + SunSize / 2, cy = Canvas.GetTop(sun) + SunSize / 2;
                double dx = cx - CanvasW / 2, dy = cy - CanvasH / 2;
                double dist = Math.Min(MaxDist, Math.Sqrt(dx * dx + dy * dy));
                double angle = Math.Atan2(-dy, dx);
                curDir = (angle * 180 / Math.PI + 180) % 360; curDepth = dist / MaxDist * 10;
                icon.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = curStrength, BlurRadius = 10, ShadowDepth = curDepth, Direction = curDir };
                var p = draft.Copy(); p.IconBorderMode = "Shadow"; p.IconShadowDirection = curDir; p.IconShadowDepth = curDepth; p.IconShadowStrength = curStrength;
                realPreview.Profile = p;
                summary.Text = $"方向 {(int)Math.Round(curDir) % 360}° · 距离 {curDepth:0.#} · 强度 {curStrength:P0}";
            }
            bool dragging = false;
            sun.MouseLeftButtonDown += (_, e) => { dragging = true; sun.CaptureMouse(); e.Handled = true; };
            sun.MouseMove += (_, e) =>
            {
                if (!dragging) return;
                var p = e.GetPosition(canvas);
                double dx = p.X - CanvasW / 2, dy = p.Y - CanvasH / 2;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > MaxDist) { dx = dx / dist * MaxDist; dy = dy / dist * MaxDist; }
                Canvas.SetLeft(sun, CanvasW / 2 + dx - SunSize / 2);
                Canvas.SetTop(sun, CanvasH / 2 + dy - SunSize / 2);
                ApplyShadow();
            };
            sun.MouseLeftButtonUp += (_, _) => { dragging = false; sun.ReleaseMouseCapture(); };
            WinLabel("阴影强度");
            var strength = new Slider { Minimum = 0, Maximum = 1, Value = strength0, TickFrequency = .01, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new Thickness(0, 6, 0, 8) }; strength.Style = (Style)win.FindResource("OpacitySlider"); root.Children.Add(strength);
            strength.ValueChanged += (_, _) => { curStrength = strength.Value; ApplyShadow(); };
            WinButton("恢复默认配置", () => { curDir = 270; curDepth = 3; curStrength = .3; strength.Value = .3; PlaceSun(270, 3); ApplyShadow(); });
            WinButton("取消", () => win.DialogResult = false);
            WinButton("确认", () => win.DialogResult = true);
            PlaceSun(dir0, depth0); strength.Value = strength0; ApplyShadow();
            bool ok = win.ShowDialog() == true;
            return (ok, curDir, curDepth, curStrength);
        }

        Button("选择图标底色…", () => { var c = Pick(localColor ?? draft.IconBackground); if (c == null) return; if (entry == null) draft.IconBackground = c; else localColor = c; Refresh(); });
        var opacityLabel = Label("");
        var opacity = new Slider { Minimum = 0, Maximum = 1, Value = localOpacity ?? draft.IconOpacity, TickFrequency = .01, IsSnapToTickEnabled = true, IsMoveToPointEnabled = true, Margin = new Thickness(0, 6, 0, 10) }; opacity.Style = (Style)window.FindResource("OpacitySlider"); panel.Children.Add(opacity);
        void Percent() => opacityLabel.Text = $"底色不透明度 {opacity.Value:P0}";
        opacity.ValueChanged += (_, _) => { if (entry == null) draft.IconOpacity = opacity.Value; else localOpacity = opacity.Value; Percent(); Refresh(); }; Percent();
        if (entry == null)
        {
            Button noneBtn = null, shadowBtn = null, lineBtn = null;
            Button lineColor = null; TextBlock widthLabel = null; Slider width = null; TextBlock shadowSummary = null; Button shadowEdit = null;
            Label("边框样式 · 点击切换");
            Button ModeButton(string text, string mode)
            {
                var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(36, 0, 0, 0)), VerticalAlignment = VerticalAlignment.Center };
                if (mode == "Shadow") swatch.Effect = new DropShadowEffect { Color = Colors.Black, Opacity = .35, BlurRadius = 5, ShadowDepth = 2, Direction = 270 };
                if (mode == "Line") { swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)); swatch.BorderThickness = new Thickness(1.5); }
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(swatch); content.Children.Add(new TextBlock { Text = " " + text, VerticalAlignment = VerticalAlignment.Center });
                var b = new Button { Content = content, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(12, 8, 12, 8), HorizontalContentAlignment = HorizontalAlignment.Left };
                b.SetResourceReference(Control.BackgroundProperty, "SubPanelBgBrush"); b.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush"); b.SetResourceReference(Control.BorderBrushProperty, "BorderBrush");
                b.Click += (_, _) => { draft.IconBorderMode = mode; SyncBorder(); Refresh(); };
                panel.Children.Add(b); return b;
            }
            void StyleSelected(Button b, bool selected)
            {
                if (selected) { b.SetResourceReference(Control.BackgroundProperty, "TabActiveBgBrush"); b.SetResourceReference(Control.ForegroundProperty, "TabActiveTextBrush"); b.BorderThickness = new Thickness(2); b.SetResourceReference(Control.BorderBrushProperty, "TabActiveTextBrush"); }
                else { b.SetResourceReference(Control.BackgroundProperty, "SubPanelBgBrush"); b.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush"); b.BorderThickness = new Thickness(1); b.SetResourceReference(Control.BorderBrushProperty, "BorderBrush"); }
            }
            void SyncBorder()
            {
                StyleSelected(noneBtn, draft.IconBorderMode == "None"); StyleSelected(shadowBtn, draft.IconBorderMode == "Shadow"); StyleSelected(lineBtn, draft.IconBorderMode == "Line");
                lineColor.Visibility = width.Visibility = widthLabel.Visibility = draft.IconBorderMode == "Line" ? Visibility.Visible : Visibility.Collapsed;
                shadowEdit.Visibility = shadowSummary.Visibility = draft.IconBorderMode == "Shadow" ? Visibility.Visible : Visibility.Collapsed;
            }
            noneBtn = ModeButton("无边框", "None"); shadowBtn = ModeButton("阴影边框", "Shadow"); lineBtn = ModeButton("直线边框", "Line");
            shadowEdit = Button("修改阴影…", () =>
            {
                var (ok, dir, depth, strength) = OpenShadowEditor(draft.IconShadowDirection, draft.IconShadowDepth, draft.IconShadowStrength);
                if (!ok) return;
                draft.IconShadowDirection = dir; draft.IconShadowDepth = depth; draft.IconShadowStrength = strength;
                shadowSummary.Text = $"阴影 · 方向 {(int)Math.Round(dir) % 360}° · 距离 {depth:0.#} · 强度 {strength:P0}";
                Refresh();
            });
            shadowSummary = Label("");
            lineColor = Button("选择线条颜色…", () => { var c = Pick(draft.IconBorderColor); if (c != null) { draft.IconBorderColor = c; Refresh(); } });
            widthLabel = Label("线条宽度"); width = new Slider { Minimum = 0, Maximum = 8, Value = Math.Clamp(draft.IconBorderWidth, 0, 8), TickFrequency = .5, IsSnapToTickEnabled = true }; width.Style = (Style)window.FindResource("OpacitySlider"); panel.Children.Add(width);
            width.ValueChanged += (_, _) => { draft.IconBorderWidth = width.Value; widthLabel.Text = $"线条宽度 · {width.Value:0.#} px"; Refresh(); };
            Button("恢复默认设置", () =>
            {
                draft.IconBackground = "#FFFFFF"; draft.IconOpacity = 0; draft.IconBorderMode = "None"; draft.IconBorderColor = "#FFFFFF"; draft.IconBorderWidth = 1; draft.IconShadowDirection = 270; draft.IconShadowDepth = 3; draft.IconShadowStrength = .3;
                opacity.Value = 0; width.Value = 1; shadowSummary.Text = ""; SyncBorder(); Refresh();
            });
            SyncBorder();
        }
        else
        {
            Label("单独调整的项目不再跟随主题，未调整的项目继续跟随。");
            Button("底色恢复跟随主题", () => { localColor = null; Refresh(); });
            Button("透明度恢复跟随主题", () => { opacity.Value = draft.IconOpacity; localOpacity = null; Refresh(); });
        }
        Button("保存", () => window.DialogResult = true); Button("取消", () => window.DialogResult = false); Refresh();
        if (window.ShowDialog() != true) return false;
        if (entry != null) { entry.IconBackground = localColor; entry.IconOpacity = localOpacity; }
        else { profile.IconBackground = draft.IconBackground; profile.IconOpacity = draft.IconOpacity; profile.IconBorderMode = draft.IconBorderMode; profile.IconBorderColor = draft.IconBorderColor; profile.IconBorderWidth = draft.IconBorderWidth; profile.IconShadowDirection = draft.IconShadowDirection; profile.IconShadowDepth = draft.IconShadowDepth; profile.IconShadowStrength = draft.IconShadowStrength; }
        return true;
    }
}
