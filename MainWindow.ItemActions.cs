using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad;

public partial class MainWindow
{
    private bool _featureDialogOpen;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private Border _toast;
    private TextBlock _toastText;

    private T WithFeatureDialog<T>(Func<T> action)
    {
        _featureDialogOpen = true;
        try { return action(); }
        finally { _featureDialogOpen = false; }
    }

    private void EntryMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement target || target.DataContext is not AppEntry entry) return;
        PopulateEntryMenu(menu, entry);
    }

    private void PopulateEntryMenu(ContextMenu menu, AppEntry entry)
    {
        menu.Items.Clear();
        MenuItem ActionItem(string title, string icon, Action action)
        {
            var option = new MenuItem { Header = title, Icon = icon };
            option.Click += (_, e) => { e.Handled = true; action(); };
            return option;
        }
        menu.Items.Add(ActionItem(entry is AppFolder ? "打开文件夹" : "打开", "↗", () =>
        {
            if (entry is AppFolder folder) OpenFolder(folder); else LaunchApp((AppItem)entry);
        }));
        var move = new MenuItem { Header = "修改分类", Icon = "⇄" };
        var owner = EntryMoveService.Owner(_app.Config, entry);
        foreach (var category in _app.Config.Categories)
        {
            var option = ActionItem(category.Name, category == owner ? "✓" : "", () => MoveEntryToCategory(entry, category));
            option.IsEnabled = category != owner || !category.Entries.Contains(entry);
            move.Items.Add(option);
        }
        if (move.Items.Count == 0) move.Items.Add(new MenuItem { Header = "暂无分类", IsEnabled = false });
        menu.Items.Add(move);
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("修改名称…", "✎", () =>
        {
            var name = WithFeatureDialog(() => PromptDialog.Show(this, "修改名称", "显示名称", entry.Name));
            if (string.IsNullOrWhiteSpace(name)) return;
            entry.Name = name.Trim();
            if (_openFolder == entry) FolderTitle.Text = entry.Name;
            _app.SaveConfig();
        }));
        if (entry is AppItem app)
        {
            menu.Items.Add(ActionItem("修改图标…", "▧", () => ChooseEntryIcon(app)));
            var reset = ActionItem("恢复默认图标", "↺", () => { app.CustomIconPath = null; _app.SaveConfig(); });
            reset.IsEnabled = !string.IsNullOrEmpty(app.CustomIconPath);
            menu.Items.Add(reset);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem(entry is AppFolder ? "解散文件夹" : "从启动台移除", "−", () => RemoveEntry(entry)));
    }

    private void MoveEntryToCategory(AppEntry entry, AppCategory category)
    {
        if (IsFolderOpen) HideFolderNow();
        if (!EntryMoveService.IntoCategory(_app.Config, entry, category, category.Entries.Count)) return;
        _app.SaveConfig();
        ReloadFromConfig();
        ShowToast($"“{entry.Name}”已移至“{category.Name}”");
    }

    private void ChooseEntryIcon(AppItem entry)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择自定义图标",
            Filter = "图片与图标|*.png;*.ico;*.jpg;*.jpeg;*.bmp|所有文件|*.*"
        };
        if (WithFeatureDialog(() => picker.ShowDialog(this)) != true) return;
        try
        {
            entry.CustomIconPath = IconService.ImportCustomImage(picker.FileName);
            _app.SaveConfig();
            ShowToast("图标已更新");
        }
        catch (Exception)
        {
            ShowToast("无法使用这张图片，请选择有效的 PNG、ICO、JPG 或 BMP 文件。");
        }
    }

    private string DuplicateFileMessage(string fullPath)
    {
        foreach (var category in _app.Config.Categories)
        {
            var existing = category.Entries.OfType<AppItem>()
                .Concat(category.Entries.OfType<AppFolder>().SelectMany(f => f.Items))
                .FirstOrDefault(i => string.Equals(i.Path, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing == null) continue;
            var categoryText = string.IsNullOrWhiteSpace(category.Name) || category.Name == "未分类"
                ? "" : $"\n所在分类：{category.Name}";
            return $"“{existing.Name}”已存在{categoryText}";
        }
        return "文件已存在";
    }

    private void ShowDuplicateToast(string path) => ShowToast(DuplicateFileMessage(Path.GetFullPath(path)));

    private void ShowToast(string message)
    {
        if (_toast == null)
        {
            _toastText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21 };
            _toastText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
            _toast = new Border { CornerRadius = new CornerRadius(13), Padding = new Thickness(20,12,20,12),
                MaxWidth = 510, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(30,0,30,62),
                IsHitTestVisible = false, Child = _toastText };
            _toast.SetResourceReference(Border.BackgroundProperty, "PanelBgBrush");
            _toast.SetResourceReference(Border.BorderBrushProperty, "AccentDeepBrush");
            Panel.SetZIndex(_toast, 1000);
            Root.Children.Add(_toast);
            _toastTimer.Tick += (_,_) => { _toastTimer.Stop(); _toast.Visibility = Visibility.Collapsed; };
        }
        _toastText.Text = message;
        _toast.Visibility = Visibility.Visible;
        _toast.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config,160))));
        _toastTimer.Stop(); _toastTimer.Start();
    }
}
