using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad;

public sealed class OnboardingWindow : Window
{
    private readonly App _app;
    private readonly LauncherConfig _draft;
    private readonly StackPanel _body=new();
    private readonly TextBlock _progress=new();
    private readonly TextBlock _title=new();
    private readonly TextBlock _description=new();
    private readonly Button _back=new(), _next=new(), _skip=new();
    private int _step;
    private bool _finished;
    private readonly bool _revisit;
    private string T(string chinese,string english)=>_draft.Language=="en-US"?english:chinese;
    public OnboardingWindow(App app, bool revisit=false)
    {
        _app=app; _revisit=revisit;
        // Work on an isolated draft; dismissing the guide never partially applies settings.
        _draft=ConfigService.Deserialize(ConfigService.Serialize(app.Config));
        Title="LaunchPad"; Width=620; Height=570; ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; WindowStyle=WindowStyle.None;
        AllowsTransparency=true; Background=Brushes.Transparent;
        Icon=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/LaunchPad;component/Themes/ThemeCenter.xaml") });
        ThemeService.Apply(Resources,ThemeService.Global(_draft),0);
        SetResourceReference(ForegroundProperty,"TextPrimaryBrush");
        var card=new Border { CornerRadius=new CornerRadius(20),BorderThickness=new Thickness(1),Padding=new Thickness(34) };
        card.SetResourceReference(Border.BackgroundProperty,"PanelBgBrush"); card.SetResourceReference(Border.BorderBrushProperty,"BorderBrush"); Content=card;
        var root=new Grid(); card.Child=root;
        root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        var heading=new StackPanel();root.Children.Add(heading);
        _progress.FontSize=12;_progress.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush");heading.Children.Add(_progress);
        _title.FontSize=28;_title.FontWeight=FontWeights.SemiBold;_title.Margin=new Thickness(0,14,0,12);heading.Children.Add(_title);
        _description.TextWrapping=TextWrapping.Wrap;_description.FontSize=13;_description.LineHeight=22;_description.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondaryBrush");heading.Children.Add(_description);
        heading.MouseLeftButtonDown+=(_,e)=> { if(e.ClickCount==1) DragMove(); };
        var scroll=new ScrollViewer { Content=_body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,25,0,20) };Grid.SetRow(scroll,1);root.Children.Add(scroll);
        var footer=new DockPanel();Grid.SetRow(footer,2);root.Children.Add(footer);
        foreach(var button in new[]{_next,_back,_skip}) { button.Padding=new Thickness(18,10,18,10);button.Margin=new Thickness(6,0,0,0); }
        _next.SetResourceReference(BackgroundProperty,"TabActiveBgBrush");_next.SetResourceReference(ForegroundProperty,"TabActiveTextBrush");
        DockPanel.SetDock(_next,Dock.Right);footer.Children.Add(_next);DockPanel.SetDock(_back,Dock.Right);footer.Children.Add(_back);_skip.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(_skip);
        _next.Click+=(_,_)=> { if(_step<2) { _step++;Render(); } else Complete(false); };
        _back.Click+=(_,_)=> { _step--;Render(); };
        _skip.Click+=(_,_)=>Complete(true);
        Closing+=(_,e)=> { if(!_finished) { e.Cancel=true; Complete(true); } };
        Loaded+=(_,_)=>MotionService.EnterDialog(this,app.Config);
        InputBehavior.Apply(this);Render();
    }
    private void Label(string text)
    {
        var label=new TextBlock { Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,10),FontSize=13,LineHeight=23 };
        label.SetResourceReference(TextBlock.ForegroundProperty,"TextPrimaryBrush");_body.Children.Add(label);
    }
    private void Choice(string label,string[] values,int selected,Action<int> update)
    {
        Label(label);var box=new ComboBox { ItemsSource=values,SelectedIndex=selected,MinHeight=36,Margin=new Thickness(0,0,0,10) };
        box.SelectionChanged+=(_,_)=> { if(box.SelectedIndex>=0) update(box.SelectedIndex); };_body.Children.Add(box);
    }
    private void Toggle(string text,bool value,Action<bool> update)
    {
        var control=new CheckBox { Content=text,IsChecked=value,Margin=new Thickness(0,10,0,10),FontSize=13 };
        control.SetResourceReference(ForegroundProperty,"TextPrimaryBrush");control.Click+=(_,_)=>update(control.IsChecked==true);_body.Children.Add(control);
    }
    private void Render()
    {
        _body.Children.Clear();_progress.Text=T("初次见面，设置你的启动台","Make LaunchPad yours")+$"   {_step+1} / 3";
        _back.Content=T("上一步","Back");_back.Visibility=_step==0?Visibility.Collapsed:Visibility.Visible;
        _next.Content=_step==2?T("完成并开始","Finish & start"):T("下一步","Next");_skip.Content=_revisit?T("取消","Cancel"):T("使用默认设置","Use defaults");
        if(_step==0)
        {
            _title.Text=T("欢迎使用 LaunchPad","Welcome to LaunchPad");
            _description.Text=T("只需几步，选择适合你的语言和操作方式。所有选项之后都可以在设置中修改。","Choose your language and everyday preferences. You can change everything later in Settings.");
            Choice(T("界面语言","Language"),new[]{"简体中文","English"},_draft.Language=="en-US"?1:0,i=> { _draft.Language=i==1?"en-US":"zh-CN";Render(); });
            Label(string.Format(T("按 {0}，即可呼出或隐藏启动台。","Press {0} to show or hide LaunchPad."),MainWindow.FormatHotkey(_draft.HotkeyModifiers,_draft.HotkeyKey)));
            var shortcut=new Button { Content=T("自定义呼出快捷键…","Customize activation shortcut…"),HorizontalAlignment=HorizontalAlignment.Left,Padding=new Thickness(14,8,14,8) };
            shortcut.Click+=(_,_)=> { var value=PromptDialog.CaptureHotkey(this,T("呼出快捷键","Activation shortcut"),T("按下你习惯的组合键。","Press your preferred key combination."),_draft.HotkeyModifiers,_draft.HotkeyKey);if(value.HasValue){_draft.HotkeyModifiers=value.Value.modifiers;_draft.HotkeyKey=value.Value.key;shortcut.Content=MainWindow.FormatHotkey(_draft.HotkeyModifiers,_draft.HotkeyKey);} };_body.Children.Add(shortcut);
        }
        else if(_step==1)
        {
            _title.Text=T("按你的习惯运行","Fit your everyday routine");
            _description.Text=T("选择启动与添加应用的方式。","Choose how LaunchPad starts and imports apps.");
            Toggle(T("登录 Windows 时自动启动","Launch when signing in to Windows"),_draft.AutoStart,v=>_draft.AutoStart=v);
            Toggle(T("自动启动时先收起到托盘","Start minimized to tray at sign-in"),_draft.MinimizeToTray,v=>_draft.MinimizeToTray=v);
            Toggle(T("解析快捷方式，链接到原文件","Resolve shortcuts to their target files"),_draft.ResolveShortcuts,v=>_draft.ResolveShortcuts=v);
            Choice(T("打开应用的方式","Launch apps with"),new[]{T("单击","Single click"),T("双击","Double click")},_draft.LaunchMode=="Double"?1:0,i=>_draft.LaunchMode=i==1?"Double":"Single");
        }
        else
        {
            _title.Text=T("最后，选一个喜欢的外观","One last thing: your look");
            _description.Text=T("先从基础外观开始。更多配色、磨砂与背景，可在主题中心调整。","Start with a simple look. Explore colors, glass and backgrounds in the theme center later.");
            Choice(T("外观","Appearance"),new[]{T("浅色","Light"),T("深色","Dark"),T("跟随系统","System")},_draft.Theme=="Dark"?1:_draft.Theme=="System"?2:0,i=> { _draft.Theme=new[]{"Light","Dark","System"}[i];ThemeService.Apply(Resources,ThemeService.Global(_draft),160); });
            Label(T("开始使用：\n① 把应用、文件或快捷方式拖入主页。\n② 点击 ＋ 创建分类，拖动图标整理位置。\n③ 关闭窗口后，仍可用快捷键再次打开。","Get started:\n1. Drop apps, files or shortcuts onto home.\n2. Click + to create categories and drag icons to organize.\n3. Use your shortcut to bring LaunchPad back anytime."));
        }
        _body.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(0,1,TimeSpan.FromMilliseconds(MotionService.Duration(_draft,140))));
    }
    private void Complete(bool defaults)
    {
        if(_finished) return;
        if(defaults && _revisit) { _finished=true; Close(); return; }
        var selected=defaults?ConfigService.Deserialize(ConfigService.Serialize(_app.Config)):_draft;
        selected.OnboardingPending=false;
        try { _app.RestoreConfig(selected); }
        catch(Exception ex) { PromptDialog.Notify(this,T("无法保存","Unable to save"),ex.Message);return; }
        _finished=true;Close();
    }
}
