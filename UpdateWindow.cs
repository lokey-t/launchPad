using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaunchPad.Services;

namespace LaunchPad;

public sealed class UpdateWindow : Window
{
    private readonly App _app;
    private readonly UpdateService _service;
    private readonly UpdateOffer _offer;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _update, _later, _ignore, _manual;
    private readonly StackPanel _sources;
    private CancellationTokenSource _download;
    private bool _busy, _closing, _installing;
    private static string T(string text) => AppLanguage.T(text);

    public UpdateWindow(App app, UpdateService service, UpdateOffer offer)
    {
        _app = app; _service = service; _offer = offer;
        Title = T("发现新版本"); Width = 570; SizeToContent = SizeToContent.Height; MaxHeight = Math.Max(400,SystemParameters.WorkArea.Height-40);
        WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = true; FontFamily = new FontFamily("Microsoft YaHei UI");
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LaunchPad;component/Themes/ThemeCenter.xaml") });
        var card = new Border { CornerRadius = new CornerRadius(20), BorderThickness = new Thickness(1), Padding = new Thickness(28) };
        card.SetResourceReference(Border.BorderBrushProperty,"BorderBrush"); Content = card;
        WindowMaterialService.Attach(this,card,() => ThemeService.Global(app.Config));
        var body = new StackPanel(); card.Child = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var header = new DockPanel(); body.Children.Add(header);
        var close = Button("✕",()=>Dismiss()); close.Background = Brushes.Transparent; close.BorderThickness = new Thickness(0); close.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
        header.Children.Add(Text("LaunchPad · " + T("软件更新"),12,"AccentDeepBrush"));
        body.Children.Add(Text(T("发现新版本"),26));
        body.Children.Add(Text($"v{UpdateService.DisplayVersion(UpdateService.CurrentVersion)}  →  v{UpdateService.DisplayVersion(offer.Version)}",14,"AccentDeepBrush"));
        var notes = Text(string.IsNullOrWhiteSpace(offer.Mirrors[0].Notes) ? T("新版本已准备就绪，更新以获得最新体验。") : offer.Mirrors[0].Notes,13,"TextSecondaryBrush");
        body.Children.Add(new ScrollViewer { Content = notes, MaxHeight = 155, Margin = new Thickness(0,8,0,8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        bool incremental = UpdateService.SupportsIncremental(offer);
        body.Children.Add(Text(incremental ? T("默认增量更新：仅下载变动文件，完成后重启。你的应用、主题和备份会保留。") : T("此版本未提供增量资源，将下载完整包更新。你的应用、主题和备份会保留。"),12,"TextSecondaryBrush"));
        _status = Text(T("将自动选择可用下载源，失败时切换 GitHub / Gitee。"),12,"TextHintBrush"); body.Children.Add(_status);
        _progress = new ProgressBar { Height = 4, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, Margin = new Thickness(0,8,0,12) };
        _progress.SetResourceReference(ForegroundProperty,"AccentDeepBrush"); body.Children.Add(_progress);
        _sources = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed, Margin = new Thickness(0,0,0,12) };
        _sources.Children.Add(Button("GitHub ↗",()=>OpenPage(UpdateService.GitHub)));
        _sources.Children.Add(Button("Gitee ↗",()=>OpenPage(UpdateService.Gitee))); body.Children.Add(_sources);
        var actions = new WrapPanel { Margin = new Thickness(0,12,0,0) }; body.Children.Add(actions);
        _update = Button(incremental ? T("立即更新") : T("完整包更新"),async ()=>await Download()); _update.SetResourceReference(BackgroundProperty,"TabActiveBgBrush"); _update.SetResourceReference(ForegroundProperty,"TabActiveTextBrush");
        _manual = Button(T("自主下载"),()=>_sources.Visibility = _sources.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
        _later = Button(T("下次再说"),()=>Dismiss());
        _ignore = Button(T("忽略本版本"),()=> {
            string previous = app.Config.IgnoredUpdateVersion;
            app.Config.IgnoredUpdateVersion = offer.Version.ToString();
            if (ConfigService.Save(app.Config)) Dismiss(); else app.Config.IgnoredUpdateVersion = previous;
        });
        foreach (var button in new[]{_update,_manual,_later,_ignore}) actions.Children.Add(button);
        body.Children.Add(Text(T("下次再说：本次运行不再提醒，下次启动时重新检测。"),11,"TextHintBrush"));
        InputBehavior.Apply(this); Loaded += (_,_)=>MotionService.EnterDialog(this,app.Config);
        Closing += (_,e)=> { if (!_closing && !app.IsQuitting) { e.Cancel = true; Dismiss(); } };
        Closed += (_,_)=> { _download?.Cancel(); _download?.Dispose(); };
    }
    private static TextBlock Text(string text,double size,string brush = "TextPrimaryBrush")
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,7), LineHeight = size * 1.65 };
        block.SetResourceReference(TextBlock.ForegroundProperty,brush); return block;
    }
    private static Button Button(string text,Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(13,9,13,9), Margin = new Thickness(0,0,8,7), MinHeight = 36, Cursor = System.Windows.Input.Cursors.Hand };
        button.SetResourceReference(BackgroundProperty,"SubPanelBgBrush");
        button.SetResourceReference(ForegroundProperty,"TextPrimaryBrush");
        button.SetResourceReference(BorderBrushProperty,"BorderBrush");
        button.Click += (_,_)=>action(); return button;
    }
    private void OpenPage(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { _status.Text = T("无法打开浏览器，请稍后重试。"); }
    }
    private void Dismiss()
    {
        if (_installing || _closing) return;
        if (_busy) { _download.Cancel(); _status.Text = T("正在取消下载…"); return; }
        _closing = true; MotionService.Hide(this,_app.Config,Close);
    }
    private async Task Download()
    {
        if (_busy) return;
        _busy = true; _update.IsEnabled = _ignore.IsEnabled = _manual.IsEnabled = false; _later.Content = T("取消下载");
        _progress.Visibility = Visibility.Visible; _progress.IsIndeterminate = true;
        _download?.Dispose(); _download = new CancellationTokenSource(); PreparedUpdate prepared = null;
        try
        {
            var progress = new Progress<UpdateProgress>(p=> {
                _progress.IsIndeterminate = p.Total == 0;
                if (p.Total > 0) _progress.Value = p.Completed * 100.0 / p.Total;
                if (p.Source.Length > 0) _status.Text = string.Format(T("正在通过 {0} 下载并校验更新…"),p.Source);
            });
            // Hashing and decompression run off the dispatcher so the dialog remains responsive.
            prepared = await Task.Run(()=>_service.PrepareAsync(_offer,AppContext.BaseDirectory,!UpdateService.SupportsIncremental(_offer),progress,_download.Token));
            _download.Token.ThrowIfCancellationRequested();
            if (prepared.Files.Count == 0) { _status.Text = T("程序文件已是最新状态。"); return; }
            _installing = true; _later.IsEnabled = false;
            _status.Text = T("校验完成，正在准备重启…");
            if (!ConfigService.Save(_app.Config)) throw new IOException("UpdateSaveFailed");
            await UpdateService.StartInstaller(prepared,AppContext.BaseDirectory);
            await File.WriteAllTextAsync(System.IO.Path.Combine(prepared.Directory,"go"),"go");
            prepared = null; _app.ExitApp();
        }
        catch (OperationCanceledException) { _status.Text = T("已取消更新，当前版本未改变。"); }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            _status.Text = ex.Message == "UpdateVersionMismatch"
                ? T("发行包内的版本号与发布版本不一致，已停止安装。请等待发布者修正，或通过自主下载查看详情。")
                : T("更新未能完成，当前版本未改变。请重试或选择自主下载。若软件位于只读目录，请先移至可写目录。");
            _sources.Visibility = Visibility.Visible;
        }
        finally
        {
            if (prepared != null) UpdateService.DeleteStage(prepared.Directory);
            _busy = _installing = false; _update.IsEnabled = _ignore.IsEnabled = _manual.IsEnabled = _later.IsEnabled = true;
            _later.Content = T("下次再说"); _progress.Visibility = Visibility.Collapsed;
        }
    }
}
