using System.Windows;
namespace LaunchPad.Setup;
public partial class SetupApp : Application
{
    private Mutex _mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);_mutex=new Mutex(true,"LaunchPad.Setup."+Environment.UserName,out bool first);
        if(!first){MessageBox.Show("安装程序已经运行。 / Setup is already running.","LaunchPad");Shutdown();return;}
        MainWindow=new SetupWindow();MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e){_mutex?.Dispose();base.OnExit(e);}
}
