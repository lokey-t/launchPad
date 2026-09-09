using System.Windows;
using System.Windows.Threading;
using LaunchPad.Models;
using LaunchPad.Services;
using Microsoft.Win32;

namespace LaunchPad;

public partial class App : Application
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativeMethods.POINT point, uint flags);

    [System.Runtime.InteropServices.DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
    private Mutex _mutex;
    private MainWindow _mainWindow;
    private SettingsWindow _settingsWindow;
    private HotkeyService _hotkey;
    private readonly List<int> _categoryHotkeyIds = new();
    private const int CategoryHotkeyBaseId = 0x0D01;
    private System.Windows.Forms.NotifyIcon _tray;
    private bool _isQuitting;

    /// <summary>是否正在退出程序（设置窗口据此决定是否允许真正关闭）。</summary>
    public bool IsQuitting => _isQuitting;

    /// <summary>设置窗口当前是否可见（失焦隐藏时判断用）。</summary>
    public bool IsSettingsVisible => _settingsWindow?.IsVisible == true;

    /// <summary>全局配置（所有窗口共享）。</summary>
    public LauncherConfig Config { get; private set; }

    /// <summary>快捷键是否注册成功（供设置页显示）。</summary>
    public bool HotkeyOk { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), @"LaunchPad\crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {args.Exception}\r\n\r\n");
            }
            catch { }
            MessageBox.Show($"发生未处理的错误：{args.Exception.Message}", "LaunchPad",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        _mutex = new Mutex(true, "LaunchPad_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            var shortcut = ConfigService.ReadHotkey();
            MessageBox.Show($"软件已运行，按 {LaunchPad.MainWindow.FormatHotkey(shortcut.Modifiers, shortcut.Key)} 打开主界面。",
                "LaunchPad 已在运行", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Config = ConfigService.Load();
        SmoothScrollService.Initialize();
        ApplyTheme(Config.Theme);

        _mainWindow = new MainWindow(this);
        _settingsWindow = new SettingsWindow(this);

        SetupHotkey();
        SetupTray();

        if (Config.MinimizeToTray && Config.AutoStart)
        {
            _mainWindow.Hide();
        }
        else
        {
            ShowMain();
        }
    }

    // ---------- 主界面显隐 ----------

    /// <summary>保存配置并同步开机自启开关。</summary>
    public void SaveConfig()
    {
        ConfigService.Save(Config);
        StartupService.SetEnabled(Config.AutoStart);
    }

    public void OpenSettings()
    {
        _settingsWindow.RefreshFromConfig();
        MotionService.Show(_settingsWindow, Config);
    }

    /// <summary>设置改动后刷新主窗口（分类、尺寸、快捷键标签等）。</summary>
    public void RefreshMainWindow()
    {
        _mainWindow?.ReloadFromConfig();
    }

    public void ShowMain()
    {
        ApplyPosition();
        MotionService.Show(_mainWindow, Config);
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
    }

    public void ToggleMain()
    {
        if (_mainWindow.IsVisible && !MotionService.IsClosing(_mainWindow))
            _mainWindow.HideAnimated();
        else
            ShowMain();
    }

    /// <summary>按配置的弹窗位置重定位主窗口（启动、设置变更时调用；已显示的窗口也立即生效）。</summary>
    public void ApplyPosition()
    {
        var w = _mainWindow;
        double nl = w.Left, nt = w.Top;
        if (Config.Position == "Cursor")
        {
            NativeMethods.GetCursorPos(out var pt);
            var area = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(pt.X, pt.Y)).WorkingArea;
            // 窗口中心贴近鼠标位置，并钳制在工作区范围内
            var monitor = MonitorFromPoint(pt, 2);
            double scaleX = 1, scaleY = 1;
            if (GetDpiForMonitor(monitor, 0, out var dpiX, out var dpiY) == 0 && dpiX > 0 && dpiY > 0)
            {
                scaleX = dpiX / 96.0;
                scaleY = dpiY / 96.0;
            }
            nl = pt.X / scaleX - w.Width / 2;
            nt = pt.Y / scaleY - w.Height / 2;
            nl = Math.Max(area.Left / scaleX, Math.Min(nl, area.Right / scaleX - w.Width));
            nt = Math.Max(area.Top / scaleY, Math.Min(nt, area.Bottom / scaleY - w.Height));
        }
        else
        {
            var area = SystemParameters.WorkArea;
            nl = area.Left + (area.Width - w.Width) / 2;
            nt = area.Top + (area.Height - w.Height) / 2;
        }
        w.Left = nl;
        w.Top = nt;
    }

    // ---------- 全局热键 ----------

    private void SetupHotkey()
    {
        _hotkey = new HotkeyService();
        _hotkey.Pressed += () => Dispatcher.Invoke(ToggleMain);
        HotkeyOk = _hotkey.Register(Config.HotkeyModifiers, Config.HotkeyKey);
        RegisterCategoryHotkeys();
    }

    /// <summary>注册所有分类的全局快捷键（设置变更后重新调用）。</summary>
    public void RegisterCategoryHotkeys()
    {
        foreach (var id in _categoryHotkeyIds)
            _hotkey.Unregister(id);
        _categoryHotkeyIds.Clear();

        int idx = 0;
        foreach (var cat in Config.Categories)
        {
            if (cat.HotkeyModifiers == 0 || cat.HotkeyKey == 0) { idx++; continue; }
            int id = CategoryHotkeyBaseId + idx;
            var captured = cat;
            bool ok = _hotkey.Register(id, cat.HotkeyModifiers, cat.HotkeyKey, () =>
            {
                Dispatcher.Invoke(() =>
                {
                    // 若已开启"分类快捷键可关闭窗口"，且窗口已打开且当前就是该分类，则关闭窗口
                    if (Config.AllowCategoryHotkeyToClose
                        && _mainWindow.IsVisible && !MotionService.IsClosing(_mainWindow))
                    {
                        var active = _mainWindow.GetActiveCategory();
                        if (active != null && active.Id == captured.Id)
                        {
                            _mainWindow.HideAnimated();
                            return;
                        }
                    }
                    ShowMain();
                    _mainWindow.SelectCategory(captured);
                });
            });
            if (ok) _categoryHotkeyIds.Add(id);
            idx++;
        }
    }

    /// <summary>设置页修改快捷键后重新注册（主热键 + 分类热键）。</summary>
    public bool ReRegisterHotkey()
    {
        HotkeyOk = _hotkey.Register(Config.HotkeyModifiers, Config.HotkeyKey);
        RegisterCategoryHotkeys();
        return HotkeyOk;
    }

    // ---------- 托盘 ----------

    private void SetupTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "LaunchPad 应用启动器",
            Visible = true
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMain);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示主界面", null, (_, _) => Dispatcher.Invoke(ShowMain));
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApp));
        _tray.ContextMenuStrip = menu;
    }

    public void ExitApp()
    {
        if (_isQuitting) return;
        _isQuitting = true;
        Shutdown();
    }

    // ---------- 主题 ----------

    public void ApplyTheme(string theme)
    {
        var name = theme switch
        {
            "Dark" => "Dark",
            "System" => IsSystemDark() ? "Dark" : "Light",
            _ => "Light"
        };
        if (Resources.MergedDictionaries.Count > 0)
            Resources.MergedDictionaries[0].Source = new Uri($"Themes/{name}.xaml", UriKind.Relative);
        if (Config != null) ThemeService.Apply(Resources,ThemeService.Global(Config),MotionService.Duration(Config,280));
    }

    public void RefreshMainTheme() => _mainWindow?.RefreshTheme();

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Mutex 冲突/启动失败时 Config 尚未加载，禁止把 null 覆盖写入用户配置
        if (Config != null)
            ConfigService.Save(Config);
        _hotkey?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
