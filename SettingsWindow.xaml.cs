using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private bool _capturing;
    private bool _refreshing;
    private int _pendingMods = -1;
    private int _pendingKey;

    public SettingsWindow(App app)
    {
        _app = app;
        // 构造期间 Slider 初始 Value(0) 会被 Minimum 钳制并触发 ValueChanged，
        // 若此时写入配置会把用户已保存的悬停秒数覆盖为最小值；先屏蔽事件写入。
        _refreshing = true;
        InitializeComponent();
        _refreshing = false;
        InputBehavior.Apply(this);
        InitializeCategorySorting();
        InitializeHoverMagnets();
        Closing += SettingsWindow_Closing;
    }

    /// <summary>关闭时隐藏而不是销毁，保证可反复打开；退出程序时允许真正关闭。</summary>
    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_app.IsQuitting) return;
        e.Cancel = true;
        MotionService.Hide(this, _app.Config);
    }

    /// <summary>从配置刷新所有控件（打开设置前调用）。</summary>
    public void RefreshFromConfig()
    {
        _pendingMods = -1;
        _refreshing = true;
        _capturing = false;
        var c = _app.Config;
        AutoStartToggle.IsChecked = c.AutoStart;
        MinimizeTrayToggle.IsChecked = c.MinimizeToTray;
        SingleClickToggle.IsChecked = c.LaunchMode == "Single";
        HideAfterLaunchToggle.IsChecked = c.HideAfterLaunch;
        HideOnFocusLostToggle.IsChecked = c.HideOnFocusLost;

        HotkeyInput.Text = MainWindow.FormatHotkey(c.HotkeyModifiers, c.HotkeyKey);
        HotkeyStatus.Text = "点击“修改”后按下新的组合键（如 Ctrl + Alt + Q）";

        switch (c.Theme)
        {
            case "Dark": ThemeDark.IsChecked = true; break;
            case "System": ThemeSystem.IsChecked = true; break;
            default: ThemeLight.IsChecked = true; break;
        }
        switch (c.IconSize)
        {
            case "Small": SizeSmall.IsChecked = true; break;
            case "Large": SizeLarge.IsChecked = true; break;
            default: SizeMedium.IsChecked = true; break;
        }
        switch (c.Position)
        {
            case "Cursor": PosCursor.IsChecked = true; break;
            default: PosCenter.IsChecked = true; break;
        }

        AnimationOff.IsChecked = c.AnimationMode == "Off";
        AnimationFast.IsChecked = c.AnimationMode == "Fast";
        AnimationBalanced.IsChecked = c.AnimationMode == "Balanced";
        AnimationOptimized.IsChecked = c.AnimationMode == "Optimized";
        FolderHoverDelay.Value = c.FolderHoverSeconds;
        CategoryHoverDelay.Value = c.CategoryHoverSeconds;
        ShowThemeButtonToggle.IsChecked=c.ShowThemeButton;
        GeneralHotkeyHint.Text = $"按 {MainWindow.FormatHotkey(c.HotkeyModifiers, c.HotkeyKey)} 随时呼出或隐藏启动台。";
        AboutPathText.Text = $"配置文件：{ConfigService.ConfigPath}";
        AllowCatHotkeyCloseToggle.IsChecked = _app.Config.AllowCategoryHotkeyToClose;
        RefreshCategoryList();
        _refreshing = false;
        var selectedMode = new[] { AnimationOff, AnimationFast, AnimationBalanced, AnimationOptimized }
            .FirstOrDefault(option => option.IsChecked == true);
        if (selectedMode != null) Animation_Changed(selectedMode, new RoutedEventArgs());
    }

    private void RefreshCategoryList()
    {
        var selectedHotkey=HotkeyCategoryList.SelectedItem;
        HotkeyCategoryList.ItemsSource=null;
        HotkeyCategoryList.ItemsSource=_app.Config.Categories;
        CategoryList.ItemsSource = null;
        CategoryList.ItemsSource = _app.Config.Categories;
        CategoryList.SelectedItem = null;
        RenameCatBtn.IsEnabled = DeleteCatBtn.IsEnabled = AddCatHotkeyBtn.IsEnabled = false;
        AddCatHotkeyBtn.Content = "添加快捷键";
        ClearCatHotkeyBtn.IsEnabled = false;
        HotkeyCategoryList.SelectedItem=selectedHotkey;
    }

    // ---------- 导航 ----------

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        // XAML 解析时 IsChecked 会提前触发本事件，此时控件尚未初始化
        if (PanelGeneral == null) return;
        var tag = (sender as RadioButton)?.Tag as string;
        var headings = tag switch
        {
            "hotkey" => ("快捷键", "从任何应用，一键回到启动台。"),
            "appearance" => ("外观与动效", "选择适合你的视觉风格与操作节奏。"),
            "manage" => ("分类管理", "整理分类，让常用工具各就其位。"),
            "about" => ("关于 LaunchPad", "轻量、专注的桌面应用启动器。"),
            _ => ("常规与启动", "设置启动方式，以及启动台的日常行为。")
        };
        PageTitle.Text = headings.Item1;
        PageDescription.Text = headings.Item2;
        PanelGeneral.Visibility = tag == "general" ? Visibility.Visible : Visibility.Collapsed;
        PanelHotkey.Visibility = tag == "hotkey" ? Visibility.Visible : Visibility.Collapsed;
        PanelAppearance.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        PanelManage.Visibility = tag == "manage" ? Visibility.Visible : Visibility.Collapsed;
        PanelAbout.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.ScrollToTop();
        SettingsPages.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config, 160))));
    }

    private void Header_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    /// <summary>空白区域拖动窗口；交互控件（按钮/输入框/列表/开关）上不触发。</summary>
    private void Content_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject d && IsInteractiveControl(d))
            return;
        DragMove();
    }

    private static bool IsInteractiveControl(DependencyObject d)
    {
        while (d != null)
        {
            if (d is ButtonBase or TextBox or ListBox or ComboBox or ScrollBar or PasswordBox or DatePicker)
                return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    // ---------- 常规 ----------

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        _app.Config.AutoStart = AutoStartToggle.IsChecked == true;
    }

    private void Tray_Changed(object sender, RoutedEventArgs e)
    {
        _app.Config.MinimizeToTray = MinimizeTrayToggle.IsChecked == true;
    }

    private void LaunchMode_Changed(object sender, RoutedEventArgs e)
    {
        _app.Config.LaunchMode = SingleClickToggle.IsChecked == true ? "Single" : "Double";
    }

    private void HideAfterLaunch_Changed(object sender, RoutedEventArgs e)
    {
        _app.Config.HideAfterLaunch = HideAfterLaunchToggle.IsChecked == true;
    }

    private void HideOnFocusLost_Changed(object sender, RoutedEventArgs e)
    {
        _app.Config.HideOnFocusLost = HideOnFocusLostToggle.IsChecked == true;
    }

    // ---------- 外观 ----------

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        if ((sender as RadioButton)?.Tag is string t)
        {
            _app.Config.Theme = t;
            var background = _app.Config.GlobalTheme;
            _app.Config.GlobalTheme = null;
            var palette = ThemeService.Global(_app.Config);
            if (background != null) { palette.BackgroundPath=background.BackgroundPath; palette.BackgroundDim=background.BackgroundDim; _app.Config.GlobalTheme=palette; }
            _app.ApplyTheme(t);
            _app.RefreshMainTheme();
        }
    }

    private void OpenThemeCenter(object sender,RoutedEventArgs e) => new ThemeCenterWindow(_app,this).ShowDialog();
    private void ShowThemeButton_Changed(object sender,RoutedEventArgs e)
    {
        if(_refreshing || ShowThemeButtonToggle==null) return;
        _app.Config.ShowThemeButton=ShowThemeButtonToggle.IsChecked==true;
        _app.RefreshMainTheme();
    }

    private void Size_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        if ((sender as RadioButton)?.Tag is string t)
        {
            _app.Config.IconSize = t;
            _app.RefreshMainWindow();
        }
    }

    private void Pos_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        if ((sender as RadioButton)?.Tag is string t)
        {
            _app.Config.Position = t;
            _app.ApplyPosition();
        }
    }

    // ---------- 快捷键捕获 ----------

    private void Animation_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing || (sender as RadioButton)?.Tag is not string mode) return;
        _app.Config.AnimationMode = mode;
        AnimationDescription.Text = mode switch
        {
            "Off" => "即时切换，无过渡动画。",
            "Fast" => "缩短过渡时间，让操作更利落。",
            "Optimized" => "更柔和的减速曲线，强调连续、舒展的运动。",
            _ => "兼顾响应速度与视觉连续性。"
        };
    }

    private void HoverDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_refreshing || FolderHoverDelay == null || CategoryHoverDelay == null) return;
        if (!_adjustingHover && sender is Slider slider && slider!=_hoverDragSlider && Mouse.LeftButton==MouseButtonState.Pressed)
        {
            _adjustingHover=true;
            slider.SetCurrentValue(Slider.ValueProperty,SnapHoverValue(slider.Value));
            _adjustingHover=false;
        }
        _app.Config.FolderHoverSeconds = FolderHoverDelay.Value;
        _app.Config.CategoryHoverSeconds = CategoryHoverDelay.Value;
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e) => Done_Click(sender, e);

    private void ModifyHotkey_Click(object sender, RoutedEventArgs e)
    {
        _app.SuspendHotkeys();
        try
        {
            var result = PromptDialog.CaptureHotkey(this, "修改全局快捷键",
                "按下该组合键可随时呼出或隐藏启动台。",
                _app.Config.HotkeyModifiers, _app.Config.HotkeyKey);
            if (result == null) return;
            var (mods, key) = result.Value;

            if (mods == 0 && key == 0)
            {
                PromptDialog.Notify(this, "无法清除", "全局呼出快捷键不可清除，请设置一个组合键。");
                return;
            }

            var conflict = _app.Config.Categories.FirstOrDefault(c => c.HotkeyModifiers == mods && c.HotkeyKey == key);
            if (conflict != null)
            {
                PromptDialog.Notify(this, "快捷键冲突", "该组合键已被分类“" + conflict.Name + "”占用，请换一个。");
                return;
            }

            var oldMods = _app.Config.HotkeyModifiers;
            var oldKey = _app.Config.HotkeyKey;
            _app.Config.HotkeyModifiers = mods;
            _app.Config.HotkeyKey = key;
            var ok = _app.ReRegisterHotkey();
            if (!ok)
            {
                _app.Config.HotkeyModifiers = oldMods;
                _app.Config.HotkeyKey = oldKey;
                _app.ReRegisterHotkey();
                PromptDialog.Notify(this, "快捷键注册失败", "可能与其他软件冲突，已恢复原快捷键。请更换组合键。");
                return;
            }

            _app.SaveConfig();
            HotkeyInput.Text = MainWindow.FormatHotkey(mods, key);
            GeneralHotkeyHint.Text = "按 " + MainWindow.FormatHotkey(mods, key) + " 随时呼出或隐藏启动台。";
            HotkeyStatus.Text = "快捷键已修改并立即生效。";
        }
        finally
        {
            _app.ReRegisterHotkey();
        }
    }

    private void HotkeyInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (!_capturing) return;

        var mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= (int)NativeMethods.MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= (int)NativeMethods.MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= (int)NativeMethods.MOD_SHIFT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= (int)NativeMethods.MOD_WIN;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        if (key == Key.Escape)
        {
            CancelCapture();
            return;
        }
        if (mods == 0)
        {
            HotkeyStatus.Text = "请至少包含一个修饰键（Ctrl / Alt / Shift / Win）";
            return;
        }

        _pendingMods = mods;
        _pendingKey = KeyInterop.VirtualKeyFromKey(key);
        HotkeyInput.Text = MainWindow.FormatHotkey(mods, _pendingKey);
        HotkeyStatus.Text = "已捕获，点击“完成”保存并生效。";
        _capturing = false;
    }

    private void HotkeyInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_capturing) CancelCapture();
    }

    private void CancelCapture()
    {
        _capturing = false;
        HotkeyInput.Text = MainWindow.FormatHotkey(_app.Config.HotkeyModifiers, _app.Config.HotkeyKey);
        HotkeyStatus.Text = "点击“修改”后按下新的组合键（如 Ctrl + Alt + Q）";
    }

    // ---------- 分类管理 ----------

    private void CategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var cat = CategoryList.SelectedItem as AppCategory;
        var has = cat != null;
        RenameCatBtn.IsEnabled = has;
        DeleteCatBtn.IsEnabled = has;
    }

    private void HotkeyCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var cat=HotkeyCategoryList.SelectedItem as AppCategory;
        bool has=cat!=null;
        AddCatHotkeyBtn.IsEnabled = has;
        bool hasHotkey = has && (cat.HotkeyModifiers != 0 || cat.HotkeyKey != 0);
        AddCatHotkeyBtn.Content = hasHotkey ? "修改快捷键" : "添加快捷键";
        ClearCatHotkeyBtn.IsEnabled = hasHotkey;
    }

    private void RenameCat_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not AppCategory cat) return;
        var name = PromptDialog.Show(this, "重命名分类", "分类名称：", cat.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (_app.Config.Categories.Any(c => c != cat && c.Name == name))
        {
            PromptDialog.Notify(this, "分类名称重复", "已存在同名分类，请换一个名称。");
            return;
        }
        cat.Name = name;
        RefreshCategoryList();
    }

    private void DeleteCat_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryList.SelectedItem is not AppCategory cat) return;
        var r = PromptDialog.Confirm(this, "删除分类",
            $"删除分类“{cat.Name}”？其中 {cat.Entries.Count} 个条目将一并移除（磁盘文件不受影响）。");
        if (!r) return;
        _app.Config.Categories.Remove(cat);
        CategoryList.SelectedItem = null;
        RefreshCategoryList();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        var total = _app.Config.Categories.Sum(c => c.Entries.Count);
        var r = PromptDialog.Confirm(this, "清空条目", $"清空全部 {total} 个条目？（磁盘文件不受影响）");
        if (!r) return;
        foreach (var c in _app.Config.Categories)
            c.Entries.Clear();
        RefreshCategoryList();
    }

    // ---------- 分类快捷键 ----------

    private void AddCatHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeyCategoryList.SelectedItem is not AppCategory cat) return;
        _app.SuspendHotkeys();
        try
        {
        var result = PromptDialog.CaptureHotkey(this, "设置分类快捷键",
            $"为分类“{cat.Name}”设置全局快捷键，按下后启动台打开并直接切换到该分类。",
            cat.HotkeyModifiers, cat.HotkeyKey);
        if (result == null) return; // 取消
        var (mods, key) = result.Value;
        var capturedId = cat.Id;

        if (mods == 0 && key == 0)
        {
            // 清除快捷键
            cat.HotkeyModifiers = 0;
            cat.HotkeyKey = 0;
            AddCatHotkeyBtn.Content = "添加快捷键";
            RefreshCategoryList();
            HotkeyCategoryList.SelectedItem = _app.Config.Categories.FirstOrDefault(c => c.Id == capturedId);
            _app.SaveConfig();
            return;
        }

        // 冲突检测：与主呼出热键或其他分类热键重复
        if (mods == _app.Config.HotkeyModifiers && key == _app.Config.HotkeyKey)
        {
            PromptDialog.Notify(this, "快捷键冲突", "该组合键已被主呼出快捷键占用，请换一个。");
            return;
        }
        var conflict = _app.Config.Categories.FirstOrDefault(c =>
            c != cat && c.HotkeyModifiers == mods && c.HotkeyKey == key);
        if (conflict != null)
        {
            PromptDialog.Notify(this, "快捷键冲突", $"该组合键已被分类“{conflict.Name}”占用，请换一个。");
            return;
        }

        cat.HotkeyModifiers = mods;
        cat.HotkeyKey = key;
        AddCatHotkeyBtn.Content = "修改快捷键";
        RefreshCategoryList();
        HotkeyCategoryList.SelectedItem = _app.Config.Categories.FirstOrDefault(c => c.Id == capturedId);
        _app.SaveConfig();
        }
        finally { _app.ReRegisterHotkey(); }
    }

    private void ClearCatHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (HotkeyCategoryList.SelectedItem is not AppCategory cat) return;
        cat.HotkeyModifiers = 0;
        cat.HotkeyKey = 0;
        AddCatHotkeyBtn.Content = "添加快捷键";
        ClearCatHotkeyBtn.IsEnabled = false;
        var capturedId = cat.Id;
        RefreshCategoryList();
        HotkeyCategoryList.SelectedItem = _app.Config.Categories.FirstOrDefault(c => c.Id == capturedId);
    }

    private void AllowCatHotkeyClose_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing || AllowCatHotkeyCloseToggle == null) return;
        _app.Config.AllowCategoryHotkeyToClose = AllowCatHotkeyCloseToggle.IsChecked == true;
    }

    // ---------- 完成 ----------

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _app.SaveConfig();
        _app.RegisterCategoryHotkeys();
        _refreshing = false;
        _app.RefreshMainWindow();
        Close();
    }
}
