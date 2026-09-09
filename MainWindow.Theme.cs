using System.Windows;
using LaunchPad.Controls;
using LaunchPad.Services;
namespace LaunchPad;
public partial class MainWindow
{
    private ThemeBackground _themeBackground;
    private void InitializeThemeBackground()
    {
        _themeBackground = new ThemeBackground { Margin = new Thickness(20) };
        Root.Children.Insert(0,_themeBackground);
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
        ThemeService.Apply(Resources,profile,duration);
        _ = _themeBackground.SetAsync(profile,duration);
    }
    private void OpenThemeCenter(object sender,RoutedEventArgs e)
    {
        WithFeatureDialog(()=>new ThemeCenterWindow(_app,this,_activeTab?.Category).ShowDialog());
    }
}
