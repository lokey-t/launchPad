using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LaunchPad.Services;

namespace LaunchPad;

public partial class App
{
    private Window _trayMenuHost;

    private void ShowTrayMenu()
    {
        _trayMenuHost?.Close();
        // A transparent activation owner lets WPF dismiss the menu reliably even when the launcher is hidden.
        var host = new Window { Width=1, Height=1, Left=0, Top=0, WindowStyle=WindowStyle.None,
            AllowsTransparency=true, Background=Brushes.Transparent, Opacity=0, ShowInTaskbar=false,
            ResizeMode=ResizeMode.NoResize, Topmost=true };
        _trayMenuHost = host;
        var anchor = new Border(); host.Content = anchor;
        var menu = CreateTrayMenu();
        anchor.ContextMenu = menu;
        menu.PlacementTarget = anchor; menu.Placement = PlacementMode.MousePoint;
        bool closed = false;
        menu.Closed += (_,_) => { if (!closed) { closed=true; host.Close(); } if (_trayMenuHost == host) _trayMenuHost = null; };
        host.Closed += (_,_) => { closed=true; menu.IsOpen=false; if (_trayMenuHost == host) _trayMenuHost=null; };
        host.Show(); host.Activate(); menu.IsOpen = true;
        double duration = MotionService.Duration(Config,140);
        if (duration > 0) menu.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(duration)));
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu();
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/LaunchPad;component/Themes/Menus.xaml",UriKind.Relative) });
        ThemeService.Apply(menu.Resources,ThemeService.Global(Config),0);
        void Add(string label,string glyph,Action action)
        {
            var item = new MenuItem { Header=AppLanguage.T(label), Icon=glyph };
            item.Click += (_,e) => { e.Handled=true; Dispatcher.BeginInvoke(action); };
            menu.Items.Add(item);
        }
        Add("显示主界面","▦",ShowMain);
        Add("设置","⚙",OpenSettings);
        menu.Items.Add(new Separator());
        Add("退出","⏻",ExitApp);
        return menu;
    }
}
