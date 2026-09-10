using System.Windows;
using LaunchPad.Controls;
using LaunchPad.Services;
namespace LaunchPad;
public partial class MainWindow
{
    private ThemeBackground _themeBackground;
    private WindowMaterialService _windowMaterial;
    private GlassChrome _folderGlass;
    private void InitializeThemeBackground()
    {
        _windowMaterial=new WindowMaterialService(this) { Surface=RootCard };
        _themeBackground = new ThemeBackground { Margin = new Thickness(20) };
        Root.Children.Insert(0,_themeBackground);
        _folderGlass=new GlassChrome { Radius=22,Margin=new Thickness(-18,-16,-18,-18) };
        ((System.Windows.Controls.Grid)FolderCard.Child).Children.Insert(0,_folderGlass);
        RootCard.Background = System.Windows.Media.Brushes.Transparent;
        _themeBackground.Failed += message => ShowToast(message);
        Closed += (_,_)=>_themeBackground.Dispose();
    }
    public void RefreshTheme()
    {
        ThemeButton.Visibility=_app.Config.ShowThemeButton ? Visibility.Visible : Visibility.Collapsed;
        if (_themeBackground == null) return;
        var profile=ThemeService.Resolve(_app.Config,_activeTab?.Category);
        double duration=MotionService.Duration(_app.Config,360);
        if(!_windowMaterial.Apply(profile,duration) && IsLoaded)
            ShowToast("当前系统无法启用窗口玻璃效果。");
        ThemeService.Apply(Resources,profile,duration);
        if(profile.Material is "Frosted" or "Liquid") _folderGlass.SetProfile(profile);
        _folderGlass.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(
            _folderGlass.Opacity,profile.Material is "Frosted" or "Liquid"?1:0,TimeSpan.FromMilliseconds(duration)));
        _ = _themeBackground.SetAsync(profile,duration);
    }
    private void OpenThemeCenter(object sender,RoutedEventArgs e)
    {
        WithFeatureDialog(()=>new ThemeCenterWindow(_app,this,_activeTab?.Category).ShowDialog());
    }
}
