using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaunchPad.Controls;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad;

public sealed class ThemeCenterWindow : Window
{
    private readonly App _app;
    private readonly ComboBox _scope = new() { MinHeight = 34 };
    private readonly CheckBox _colors = new() { Content = "为此分类单独设置配色", Margin = new Thickness(0,8,0,8) };
    private readonly CheckBox _background = new() { Content = "为此分类单独设置背景", Margin = new Thickness(0,8,0,8) };
    private readonly ThemeBackground _preview = new();
    private readonly Border _previewCard = new() { Height = 190, CornerRadius = new CornerRadius(14), ClipToBounds = true };
    private readonly TextBlock _file = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0,8,0,8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) };
    private readonly Slider _dim = new() { Minimum = 0, Maximum = 1, TickFrequency = .01, SmallChange=.01, LargeChange=.1, IsSnapToTickEnabled = true, IsMoveToPointEnabled=true };
    private readonly ComboBox _base = new() { ItemsSource = new[] { "浅色控件", "深色控件" }, MinHeight = 30 };
    private readonly WrapPanel _materialOptions = new();
    private readonly Dictionary<string,Button> _materialButtons = new();
    private readonly CheckBox _materialOverride = new() { Content="为此分类单独设置材质",Margin=new Thickness(0,8,0,8) };
    private readonly WrapPanel _backgroundActions = new();
    private readonly WindowMaterialService _windowMaterial;
    private readonly Slider _glassOpacity = new() { Minimum = 0, Maximum = 1, TickFrequency = .01, SmallChange=.01, LargeChange=.05, IsSnapToTickEnabled = true, IsMoveToPointEnabled=true };
    private readonly Slider _glassColorDepth = new() { Minimum=0, Maximum=1, TickFrequency=.01, SmallChange=.01, LargeChange=.05, IsSnapToTickEnabled=true, IsMoveToPointEnabled=true };
    private readonly StackPanel _backgroundSection = new();
    private readonly StackPanel _glassSection = new();
    private Border _surfaceCard;
    private readonly TextBlock _previewTitle = new();
    private readonly TextBlock _backgroundHelp = new() { TextWrapping=TextWrapping.Wrap,FontSize=11 };
    private readonly TextBlock _materialHint = new() { FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,6) };
    private readonly Dictionary<string,Button> _swatches = new();
    private ThemeProfile _draft;
    private bool _loading, _previewFailed;
    private string _previewPaletteKey;
    private Task _previewTask = Task.CompletedTask;
    private bool _allowClose, _closing;
    private bool _saving;
    private AppCategory Category => _scope.SelectedItem as AppCategory;

    public ThemeCenterWindow(App app, Window owner, AppCategory category = null)
    {
        _windowMaterial=new WindowMaterialService(this);
        _app = app; Owner = owner; Title = "主题中心"; Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/LaunchPad;component/Themes/ThemeCenter.xaml") });
        SetResourceReference(ForegroundProperty,"TextPrimaryBrush");
        Width = 860; Height = 730; MinWidth = 760; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false;
        InputBehavior.Apply(this);
        var scopeText=new FrameworkElementFactory(typeof(TextBlock)); scopeText.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));
        var scopeMark=new FrameworkElementFactory(typeof(TextBlock));
        scopeMark.SetValue(TextBlock.TextProperty,"（已自定义）"); scopeMark.SetValue(TextBlock.MarginProperty,new Thickness(6,0,0,0));
        scopeMark.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush");
        scopeMark.SetBinding(TextBlock.VisibilityProperty,new System.Windows.Data.Binding("HasThemeOverrides") { Converter=new System.Windows.Controls.BooleanToVisibilityConverter(), FallbackValue=Visibility.Collapsed });
        var scopePanel=new FrameworkElementFactory(typeof(StackPanel)); scopePanel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal); scopePanel.AppendChild(scopeText); scopePanel.AppendChild(scopeMark);
        _scope.ItemTemplate=new DataTemplate { VisualTree=scopePanel };
        _colors.SetResourceReference(ForegroundProperty,"TextPrimaryBrush"); _background.SetResourceReference(ForegroundProperty,"TextPrimaryBrush");
        var card = new Border { CornerRadius = new CornerRadius(18), Padding = new Thickness(26), BorderThickness = new Thickness(1) };
        _surfaceCard=card; _windowMaterial.Surface=card;
        card.SetResourceReference(Border.BackgroundProperty,"PanelBgBrush"); card.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        card.Child = root; Content = card;
        var header = new DockPanel { Margin = new Thickness(0,0,0,20) };
        var close=CloseIconButton(()=>Close()); close.VerticalAlignment=VerticalAlignment.Top; DockPanel.SetDock(close,Dock.Right); header.Children.Add(close);
        var heading = new StackPanel(); heading.Children.Add(Label("主题中心",24)); heading.Children.Add(Label("让每个分类拥有自己的氛围。修改后点击应用保存。",12)); header.Children.Add(heading);
        header.MouseLeftButtonDown += (_,e) => { if (e.OriginalSource is TextBlock) DragMove(); }; root.Children.Add(header);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1,GridUnitType.Star) }); Grid.SetRow(columns,1); root.Children.Add(columns);
        var left = new StackPanel(); var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; columns.Children.Add(leftScroll);
        left.Children.Add(Label("应用范围",15));
        _scope.Items.Add(new ScopeName("全局默认")); foreach (var cat in app.Config.Categories) _scope.Items.Add(cat);
        left.Children.Add(_scope); left.Children.Add(_colors);
        left.Children.Add(Label("预设配色",15));
        var presets = new WrapPanel();
        foreach (var preset in ThemeService.Presets)
        {
            var button = Button(preset.Name,()=>
            {
                _draft.Name = preset.Name; _draft.BaseTheme = preset.BaseTheme; _draft.Surface = preset.Surface;
                _draft.Text = preset.Text; _draft.Accent = preset.Accent; _draft.OverrideColors = true;
                LoadEditors(); RefreshPreview();
            });
            button.Width = 100; button.Height = 47; button.Background = new SolidColorBrush(ThemeService.Parse(preset.Surface,"#FFF"));
            button.Foreground = new SolidColorBrush(ThemeService.Parse(preset.Text,"#222"));
            presets.Children.Add(button);
        }
        left.Children.Add(presets); left.Children.Add(Label("窗口材质",15)); left.Children.Add(_materialOverride);
        foreach(var (value,title) in new[] { ("Solid","普通"),("Frosted","磨砂玻璃") })
        {
            var material=value;
            var button=Button(title,()=>SelectMaterial(material));
            button.MinWidth=92; button.MinHeight=38;
            _materialButtons[material]=button; _materialOptions.Children.Add(button);
        }
        left.Children.Add(_materialOptions);
        _materialHint.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondaryBrush"); left.Children.Add(_materialHint);
        left.Children.Add(Label("图标外观",15));
        left.Children.Add(Button("设置图标底色与边框…",()=> { var profile=Category==null?_draft.Copy():ThemeService.Resolve(_app.Config,new AppCategory { ThemeOverride=_draft }); if(IconAppearanceEditor.Show(this,profile)) { _draft.OverrideIcons=true; _draft.IconBackground=profile.IconBackground; _draft.IconOpacity=profile.IconOpacity; _draft.IconBorderMode=profile.IconBorderMode; _draft.IconBorderColor=profile.IconBorderColor; _draft.IconBorderWidth=profile.IconBorderWidth; _draft.IconShadowDirection=profile.IconShadowDirection; _draft.IconShadowDepth=profile.IconShadowDepth; _draft.IconShadowStrength=profile.IconShadowStrength; RefreshPreview(); } }));
        left.Children.Add(Label("编辑配色",15)); left.Children.Add(_base);
        foreach (var (key,title) in new[] { ("Surface","面板底色"),("Text","文字颜色"),("Accent","强调色") })
        {
            var button = Button(title,()=>PickColor(key)); button.HorizontalContentAlignment = HorizontalAlignment.Left;
            _swatches[key] = button; left.Children.Add(button);
        }
        left.Children.Add(Label("分类可独立设置配色或背景；材质也可独立设置；取消勾选即跟随全局。",11));
        var right = new StackPanel(); Grid.SetColumn(right,2); columns.Children.Add(new Border { Child = new ScrollViewer { Content=right, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled }, Margin = new Thickness(0) }); Grid.SetColumn(columns.Children[1],2);
        _previewTitle.Text="实时预览"; _previewTitle.FontSize=15; _previewTitle.Margin=new Thickness(0,8,0,8); right.Children.Add(_previewTitle); right.Children.Add(_previewCard);
        var previewGrid = new Grid(); previewGrid.Children.Add(_preview);
        var demo = new StackPanel { Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center };
        var demoTitle = new TextBlock { Text = "启动台   ·   我的分类", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,14) }; demoTitle.SetResourceReference(TextBlock.ForegroundProperty,"TextPrimaryBrush"); demo.Children.Add(demoTitle);
        var icons = new UniformGridCompat();
        foreach (string name in new[] { "工作", "文件", "照片", "音乐" })
        {
            var tile = new StackPanel { Margin = new Thickness(5) };
            var glyph = new IconSurface { Width = 42, Height = 42, CornerRadius = new CornerRadius(11), BorderThickness=new Thickness(1), Child = new TextBlock { Text = "◇", FontSize = 25, HorizontalAlignment = HorizontalAlignment.Center } };
 tile.Children.Add(glyph);
            var text = new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,6,0,0) }; text.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondaryBrush"); tile.Children.Add(text); icons.Children.Add(tile);
        }
        demo.Children.Add(icons); previewGrid.Children.Add(demo); _previewCard.Child = previewGrid;
        _backgroundSection.Children.Add(_background); _backgroundSection.Children.Add(Label("背景素材",15));
        var actions = _backgroundActions; actions.Children.Add(Button("上传背景…",PickBackground)); actions.Children.Add(Button("清除背景",()=> { _draft.BackgroundPath=null; _draft.OverrideBackground=true; LoadEditors(); RefreshPreview(); })); _backgroundSection.Children.Add(actions);
        _backgroundSection.Children.Add(_file); _backgroundSection.Children.Add(_backgroundHelp);
        var opacityCard=new Border { CornerRadius=new CornerRadius(12), Padding=new Thickness(14,8,14,8), Margin=new Thickness(0,12,0,0) };
        opacityCard.SetResourceReference(BackgroundProperty,"SubPanelBgBrush");
        var opacityPanel=new StackPanel(); opacityCard.Child=opacityPanel;
        var opacityHeader=new DockPanel(); var percent=Label("",12); percent.FontWeight=FontWeights.SemiBold;
        percent.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Value") { Source=_dim,StringFormat="{0:P0}" });
        percent.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush"); DockPanel.SetDock(percent,Dock.Right); opacityHeader.Children.Add(percent); opacityHeader.Children.Add(Label("背景遮罩",12));
        opacityPanel.Children.Add(opacityHeader);
        _dim.Style=(Style)FindResource("OpacitySlider"); opacityPanel.Children.Add(_dim);
        var ends=new DockPanel(); var solid=Label("柔和易读",11); DockPanel.SetDock(solid,Dock.Right); ends.Children.Add(solid); ends.Children.Add(Label("清晰通透",11)); opacityPanel.Children.Add(ends);
        _backgroundSection.Children.Add(opacityCard);
        var glassCard=new Border { CornerRadius=new CornerRadius(12), Padding=new Thickness(14,8,14,8), Margin=new Thickness(0,12,0,0) };
        glassCard.SetResourceReference(BackgroundProperty,"SubPanelBgBrush");
        var glassPanel=new StackPanel(); glassCard.Child=glassPanel;
        var glassHeader=new DockPanel(); var glassPercent=Label("",12); glassPercent.FontWeight=FontWeights.SemiBold;
        glassPercent.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Value") { Source=_glassOpacity,StringFormat="{0:P0}" });
        glassPercent.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush"); DockPanel.SetDock(glassPercent,Dock.Right); glassHeader.Children.Add(glassPercent); glassHeader.Children.Add(Label("磨砂强度",12));
        glassPanel.Children.Add(glassHeader);
        _glassOpacity.Style=(Style)FindResource("OpacitySlider"); glassPanel.Children.Add(_glassOpacity);
        var glassEnds=new DockPanel(); var glassRight=Label("深度磨砂",11); DockPanel.SetDock(glassRight,Dock.Right); glassEnds.Children.Add(glassRight); glassEnds.Children.Add(Label("清晰透明",11)); glassPanel.Children.Add(glassEnds);
        _glassSection.Children.Add(glassCard);
        var depthCard=new Border { CornerRadius=new CornerRadius(12), Padding=new Thickness(14,8,14,8), Margin=new Thickness(0,12,0,0) };
        depthCard.SetResourceReference(BackgroundProperty,"SubPanelBgBrush");
        var depthPanel=new StackPanel(); depthCard.Child=depthPanel;
        var depthHeader=new DockPanel(); var depthPercent=Label("",12); depthPercent.FontWeight=FontWeights.SemiBold;
        depthPercent.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Value") { Source=_glassColorDepth,StringFormat="{0:P0}" });
        depthPercent.SetResourceReference(TextBlock.ForegroundProperty,"AccentDeepBrush"); DockPanel.SetDock(depthPercent,Dock.Right);
        depthHeader.Children.Add(depthPercent); depthHeader.Children.Add(Label("玻璃颜色深度",12)); depthPanel.Children.Add(depthHeader);
        _glassColorDepth.Style=(Style)FindResource("OpacitySlider"); depthPanel.Children.Add(_glassColorDepth);
        var depthEnds=new DockPanel(); var depthRight=Label("不透明底色",11); DockPanel.SetDock(depthRight,Dock.Right);
        depthEnds.Children.Add(depthRight); depthEnds.Children.Add(Label("无染色",11)); depthPanel.Children.Add(depthEnds);
        depthPanel.Children.Add(Label("使用当前配色的面板底色，不影响磨砂强度。",11));
        _glassSection.Children.Add(depthCard);
        _glassSection.Children.Insert(0,Button("恢复磨砂默认配置（70%）",()=> { EnsureMaterialOverride(); _draft.GlassBlurStrength=.7; _draft.GlassColorDepth=.7; LoadEditors(); RefreshPreview(); }));

        right.Children.Add(_backgroundSection); right.Children.Add(_glassSection); right.Children.Add(_status);
        var footer = new DockPanel { Margin = new Thickness(0,18,0,0) }; Grid.SetRow(footer,2); root.Children.Add(footer);
        var saveClose = Button("保存并关闭",async ()=> { if (await SaveAsync()) Close(); }); saveClose.SetResourceReference(BackgroundProperty,"TabActiveBgBrush"); saveClose.SetResourceReference(ForegroundProperty,"TabActiveTextBrush"); DockPanel.SetDock(saveClose,Dock.Right); footer.Children.Add(saveClose);
        var apply = Button("应用并保存",async ()=>await SaveAsync()); DockPanel.SetDock(apply,Dock.Right); footer.Children.Add(apply);
        var cancel = Button("取消",()=>Close()); DockPanel.SetDock(cancel,Dock.Right); footer.Children.Add(cancel);
        var restore=Button("恢复跟随全局",()=> { if (Category != null) { _draft = ThemeService.Global(app.Config); _draft.OverrideColors=false; _draft.OverrideBackground=false; _draft.OverrideMaterial=false; _draft.OverrideIcons=false; LoadEditors(); RefreshPreview(); } }); restore.HorizontalAlignment=HorizontalAlignment.Left; footer.Children.Add(restore);
        var resetAll=Button("恢复默认主题",ResetDefaultTheme); resetAll.HorizontalAlignment=HorizontalAlignment.Left; footer.Children.Add(resetAll);
        _scope.SelectionChanged+=(_,_)=>restore.IsEnabled=Category!=null;
        _scope.SelectionChanged += (_,_)=>LoadScope();
        _materialOverride.Click+=(_,_)=> { if(_materialOverride.IsChecked==true) EnsureMaterialOverride(); else _draft.OverrideMaterial=false; LoadEditors(); RefreshPreview(); };
        _colors.Click += (_,_)=> { _draft.OverrideColors=_colors.IsChecked==true; RefreshPreview(); };
        _background.Click += (_,_)=> { _draft.OverrideBackground=_background.IsChecked==true; RefreshPreview(); };
        _dim.ValueChanged += (_,_)=> { if (!_loading) { _draft.BackgroundDim=_dim.Value; RefreshPreview(); } };
        _glassOpacity.ValueChanged += (_,_)=> { if (!_loading)
        {
            EnsureMaterialOverride(); _draft.GlassBlurStrength=_glassOpacity.Value; RefreshPreview();
        } };
        _glassColorDepth.ValueChanged += (_,_)=> { if (!_loading) { EnsureMaterialOverride(); _draft.GlassColorDepth=_glassColorDepth.Value; RefreshPreview(); } };
        _base.SelectionChanged += (_,_)=> { if (!_loading) { _draft.BaseTheme=_base.SelectedIndex==1?"Dark":"Light"; RefreshPreview(); } };
        _preview.Failed += message=> { _previewFailed=true; _status.Text=message; };
        _scope.SelectedItem = category is null ? _scope.Items[0] : category;
        Loaded += (_,_)=>MotionService.EnterDialog(this,app.Config);
        Closing += (_,e)=>
        {
            if (_allowClose) return;
            e.Cancel=true; if (_closing) return; _closing=true;
            MotionService.Hide(this,app.Config,()=> { _allowClose=true; Close(); });
        };
        Closed += (_,_)=>_preview.Dispose();
    }

    private sealed record ScopeName(string Name) { public bool HasThemeOverrides => false; }
    private sealed class UniformGridCompat : System.Windows.Controls.Primitives.UniformGrid { public UniformGridCompat() { Columns=4; } }
    private static TextBlock Label(string text,double size)
    {
        var label=new TextBlock { Text=text, FontSize=size, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,8) };
        label.SetResourceReference(TextBlock.ForegroundProperty,"TextPrimaryBrush"); return label;
    }
    private static Button Button(string text,Action action)
    {
        var b=new Button { Content=text, Padding=new Thickness(12,8,12,8), Margin=new Thickness(0,4,8,4), Cursor=System.Windows.Input.Cursors.Hand };
        b.SetResourceReference(Control.BackgroundProperty,"SubPanelBgBrush"); b.SetResourceReference(Control.ForegroundProperty,"TextPrimaryBrush"); b.SetResourceReference(Control.BorderBrushProperty,"BorderBrush");
        b.Click+=(_,_)=>action(); return b;
    }
    private static Button CloseIconButton(Action action)
    {
        var b=new Button { Content="✕", Width=32, Height=32, Padding=new Thickness(0), FontSize=14, Cursor=System.Windows.Input.Cursors.Hand, FocusVisualStyle=null };
        var border=new FrameworkElementFactory(typeof(Border));
        border.Name="bd";
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(16));
        border.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template=new ControlTemplate(typeof(Button)){ VisualTree=border };
        var hover=new Trigger { Property=UIElement.IsMouseOverProperty, Value=true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty,new SolidColorBrush(Color.FromArgb(38,0,0,0)),"bd"));
        template.Triggers.Add(hover);
        b.Template=template;
        b.Click+=(_,_)=>action();
        return b;
    }
    private void ResetDefaultTheme()
    {
        if (!PromptDialog.Confirm(this, "恢复默认主题", "确定要恢复默认主题吗？\n全局配色、背景、材质、图标外观以及各分类的独立主题配置将全部回归默认。"))
            return;
        _app.Config.GlobalTheme = new ThemeProfile();
        foreach (var cat in _app.Config.Categories) cat.ThemeOverride = null;
        _app.SaveConfig(); _app.ApplyTheme(_app.Config.Theme); _app.RefreshMainTheme();
        if (_scope.SelectedItem != _scope.Items[0]) _scope.SelectedItem = _scope.Items[0];
        _draft = ThemeService.Global(_app.Config);
        LoadEditors(); RefreshPreview();
        _status.Text = "已恢复默认主题并保存。";
    }
    private void LoadScope()
    {
        _draft=Category?.ThemeOverride?.Copy()??ThemeService.Global(_app.Config);
        if (Category != null && Category.ThemeOverride == null) { _draft.OverrideColors=false; _draft.OverrideBackground=false; _draft.OverrideMaterial=false; _draft.OverrideIcons=false; }
        _draft.OverrideMaterial ??= _draft.OverrideColors;
        LoadEditors(); RefreshPreview();
    }
    private void LoadEditors()
    {
        _loading=true;
        _materialOverride.Visibility=_colors.Visibility=_background.Visibility=Category == null?Visibility.Collapsed:Visibility.Visible;
        _materialOverride.IsChecked=_draft.OverrideMaterial??_draft.OverrideColors;
        _colors.IsChecked=_draft.OverrideColors; _background.IsChecked=_draft.OverrideBackground;
        _base.SelectedIndex=_draft.BaseTheme=="Dark"?1:0; _dim.Value=double.IsFinite(_draft.BackgroundDim)?Math.Clamp(_draft.BackgroundDim,0,1):.35;
        _glassOpacity.Value=ThemeService.GlassStrength(Category!=null && !(_draft.OverrideMaterial??_draft.OverrideColors)?ThemeService.Global(_app.Config):_draft);
        _glassColorDepth.Value=ThemeService.GlassDepth(Category!=null && !(_draft.OverrideMaterial??_draft.OverrideColors)?ThemeService.Global(_app.Config):_draft);
        var material=_draft.Material == "Frosted"?_draft.Material:"Solid";
        foreach(var pair in _materialButtons)
        {
            bool selected=pair.Key==material;
            pair.Value.SetResourceReference(Control.BackgroundProperty,selected?"TabActiveBgBrush":"SubPanelBgBrush");
            pair.Value.SetResourceReference(Control.ForegroundProperty,selected?"TabActiveTextBrush":"TextPrimaryBrush");
            pair.Value.BorderThickness=new Thickness(selected?2:1);
        }
        _materialHint.Text=material switch { "Frosted"=>"模糊窗口后方的桌面与其他窗口，叠加细腻磨砂。启用后停用自定义背景。",_=>"使用主题配色，可添加图片、动图或视频背景。" };
        _file.Text=string.IsNullOrEmpty(_draft.BackgroundPath)?"未设置背景 · 使用主题底色":Path.GetFileName(_draft.BackgroundPath);
        foreach (var pair in _swatches)
        {
            string value=pair.Key=="Surface"?_draft.Surface:pair.Key=="Text"?_draft.Text:_draft.Accent;
            pair.Value.Content=$"{(pair.Key=="Surface"?"面板底色":pair.Key=="Text"?"文字颜色":"强调色")}    {value}";
        }
        _status.Text=""; _loading=false;
    }
    private void EnsureMaterialOverride()
    {
        if(Category!=null && !(_draft.OverrideMaterial??_draft.OverrideColors))
        {
            var inherited=ThemeService.Global(_app.Config);
            _draft.Material=inherited.Material; _draft.GlassBlurStrength=inherited.GlassBlurStrength;
            _draft.GlassOpacity=inherited.GlassOpacity; _draft.GlassColorDepth=inherited.GlassColorDepth;
        }
        _draft.OverrideMaterial=true; _materialOverride.IsChecked=true;
    }
    private void SelectMaterial(string material)
    {
        if(_loading || _draft==null) return;
        EnsureMaterialOverride(); _draft.Material=material;
        LoadEditors(); RefreshPreview();
    }
    private void RefreshPreview()
    {
        if (_draft==null) return;
        var category=Category==null?null:new AppCategory { ThemeOverride=_draft };
        var effective=category==null?_draft:ThemeService.Resolve(_app.Config,category);
        string paletteKey=$"{effective.BaseTheme}|{effective.Surface}|{effective.Text}|{effective.Accent}|{effective.Material}|{effective.IconBackground}|{effective.IconOpacity}|{effective.IconBorderMode}|{effective.IconBorderColor}|{effective.IconBorderWidth}|{effective.IconShadowDirection}|{effective.IconShadowDepth}|{effective.IconShadowStrength}";
        if(paletteKey!=_previewPaletteKey)
        {
            ThemeService.Apply(Resources,effective,0);
            ThemeService.Apply(_previewCard.Resources,effective,0);
            _previewPaletteKey=paletteKey;
        }
        bool glass=effective.Material == "Frosted";
        _backgroundActions.IsEnabled=_dim.IsEnabled=_background.IsEnabled=!glass;
        _backgroundSection.Visibility=glass?Visibility.Collapsed:Visibility.Visible;
        _glassSection.Visibility=glass?Visibility.Visible:Visibility.Collapsed;
        _file.Text=glass?"背景已停用"+(string.IsNullOrEmpty(_draft.BackgroundPath)?"":" · 原背景已保留"):string.IsNullOrEmpty(_draft.BackgroundPath)?"未设置背景 · 使用主题底色":Path.GetFileName(_draft.BackgroundPath);
        _previewTitle.Text=glass?"实时窗口材质预览（可拖动窗口查看）":"实时预览";
        var tint=ThemeService.GlassSurfaceColor(effective);
        _surfaceCard.Background=new SolidColorBrush(tint);
        if(!_windowMaterial.Apply(effective,0) && IsLoaded) _status.Text="当前系统无法启用窗口玻璃效果。";
        // The window already supplies the glass tint; avoid applying it twice inside the preview.
        _preview.Visibility=glass?Visibility.Collapsed:Visibility.Visible;
        _previewFailed=false; _previewTask=_preview.SetAsync(effective,0);
    }
    private void PickColor(string key)
    {
        using var dialog=new System.Windows.Forms.ColorDialog { FullOpen=true };
        var color=ThemeService.Parse(key=="Surface"?_draft.Surface:key=="Text"?_draft.Text:_draft.Accent,"#FFFFFF");
        dialog.Color=System.Drawing.Color.FromArgb(color.R,color.G,color.B);
        if (dialog.ShowDialog(new WindowHandle(this))!=System.Windows.Forms.DialogResult.OK) return;
        string value=$"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        if (key=="Surface") _draft.Surface=value; else if(key=="Text") _draft.Text=value; else _draft.Accent=value;
        _draft.Name="自定义"; _draft.OverrideColors=true; LoadEditors(); RefreshPreview();
    }
    private sealed class WindowHandle(Window window) : System.Windows.Forms.IWin32Window { public IntPtr Handle => new System.Windows.Interop.WindowInteropHelper(window).Handle; }
    private void PickBackground()
    {
        if (!_backgroundActions.IsEnabled) return;
        var dialog=new Microsoft.Win32.OpenFileDialog { Title="选择背景素材", Filter="背景素材|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.mp4;*.wmv;*.avi;*.mov;*.m4v" };
        if(dialog.ShowDialog(this)!=true) return;
        _draft.BackgroundPath=dialog.FileName; _draft.OverrideBackground=true; LoadEditors(); RefreshPreview();
    }
    private async Task<bool> SaveAsync()
    {
        if (_saving) return false;
        _saving=true; IsEnabled=false;
        try
        {
            await _previewTask;
            if (_previewFailed) { _status.Text="请更换有效背景或清除背景后再保存。"; return false; }
            var saved=_draft.Copy();
            if (!string.IsNullOrEmpty(saved.BackgroundPath) && (Category==null || saved.OverrideBackground))
            {
                var directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"LaunchPad","Backgrounds");
                if (!Path.GetFullPath(saved.BackgroundPath).StartsWith(Path.GetFullPath(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(directory);
                    string target=Path.Combine(directory,Guid.NewGuid().ToString("N")+Path.GetExtension(saved.BackgroundPath));
                    await Task.Run(()=>File.Copy(saved.BackgroundPath,target)); saved.BackgroundPath=target;
                }
            }
            if(Category==null) _app.Config.GlobalTheme=saved;
            else Category.ThemeOverride=!saved.OverrideIcons&&!saved.OverrideColors&&!saved.OverrideBackground&&!(saved.OverrideMaterial??saved.OverrideColors)?null:saved;
            _app.SaveConfig(); _app.ApplyTheme(_app.Config.Theme); _app.RefreshMainTheme();
            _draft=saved; _status.Text="已应用并保存";
            return true;
        }
        catch(Exception) { _status.Text="保存失败，请检查文件是否可用及磁盘空间。"; return false; }
        finally { _saving=false; IsEnabled=true; }
    }
}
