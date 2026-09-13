using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace LaunchPad.Setup;

public sealed class SetupWindow : Window
{
    private bool _english= !CultureInfo.CurrentUICulture.Name.StartsWith("zh"),_busy,_finished;
    private readonly TextBox _directory=new();private readonly Button _install=new(),_browse=new(),_cancel=new();
    private readonly TextBlock _title=new(),_description=new(),_status=new();private readonly CheckBox _desktop=new(){IsChecked=true};
    private readonly ProgressBar _progress=new(){Minimum=0,Maximum=100,Height=5,Visibility=Visibility.Collapsed};
    private readonly CancellationTokenSource _cancellation=new();private readonly PayloadManifest _manifest;
    private string T(string zh,string en)=>_english?en:zh;
    public SetupWindow()
    {
        ReadPreferences();Title="LaunchPad";Width=620;Height=500;MaxHeight=Math.Max(400,SystemParameters.WorkArea.Height-40);WindowStartupLocation=WindowStartupLocation.CenterScreen;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;FontFamily=new FontFamily("Microsoft YaHei UI");
        var icon=new BitmapImage(new Uri("pack://application:,,,/LaunchPad.Setup;component/icon.png"));Icon=icon;
        var card=new Border{CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(1),Padding=new Thickness(32)};card.SetResourceReference(Border.BackgroundProperty,"Surface");card.SetResourceReference(Border.BorderBrushProperty,"Line");Content=card;
        var root=new Grid();card.Child=root;root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new());root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel();root.Children.Add(header);header.Children.Add(new Image{Source=icon,Width=58,Height=58,Margin=new Thickness(0,0,16,0)});
        var names=new StackPanel{VerticalAlignment=VerticalAlignment.Center};header.Children.Add(names);names.Children.Add(Text("LaunchPad",24));
        try{_manifest=InstallEngine.Manifest();names.Children.Add(Text("v"+_manifest.Version,12,true));}catch{names.Children.Add(Text(T("安装程序","Installer"),12,true));}
        header.MouseLeftButtonDown+=(_,e)=>{if(e.LeftButton==System.Windows.Input.MouseButtonState.Pressed)DragMove();};
        var body=new StackPanel{Margin=new Thickness(0,30,0,18)};
        var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);root.Children.Add(scroll);
        _title.FontSize=24;_title.FontWeight=FontWeights.SemiBold;_title.SetResourceReference(TextBlock.ForegroundProperty,"Ink");body.Children.Add(_title);
        _description.TextWrapping=TextWrapping.Wrap;_description.FontSize=13;_description.LineHeight=22;_description.Margin=new Thickness(0,10,0,22);_description.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(_description);
        body.Children.Add(Text(T("安装位置","Install location"),12,true));
        var location=new Grid{Margin=new Thickness(0,8,0,18)};location.ColumnDefinitions.Add(new());location.ColumnDefinitions.Add(new(){Width=GridLength.Auto});body.Children.Add(location);
        _directory.MinHeight=40;_directory.VerticalContentAlignment=VerticalAlignment.Center;location.Children.Add(_directory);
        _browse.Content=T("更改…","Change…");_browse.Margin=new Thickness(10,0,0,0);Grid.SetColumn(_browse,1);location.Children.Add(_browse);
        _desktop.Content=T("创建桌面快捷方式","Create a desktop shortcut");_desktop.SetResourceReference(ForegroundProperty,"Ink");body.Children.Add(_desktop);
        _progress.Margin=new Thickness(0,20,0,8);_progress.SetResourceReference(ForegroundProperty,"Accent");body.Children.Add(_progress);
        _status.TextWrapping=TextWrapping.Wrap;_status.FontSize=12;_status.LineHeight=19;_status.Margin=new Thickness(0,12,0,0);_status.SetResourceReference(TextBlock.ForegroundProperty,"Muted");body.Children.Add(_status);
        var footer=new DockPanel();Grid.SetRow(footer,2);root.Children.Add(footer);
        _install.MinWidth=130;_install.SetResourceReference(BackgroundProperty,"Accent");_install.Foreground=Brushes.White;_install.BorderThickness=new Thickness(0);DockPanel.SetDock(_install,Dock.Right);footer.Children.Add(_install);
        _cancel.Content=T("取消","Cancel");_cancel.HorizontalAlignment=HorizontalAlignment.Left;footer.Children.Add(_cancel);
        _directory.Text=InstallationDiscovery.Detect()??InstallationDiscovery.DefaultDirectory;_directory.TextChanged+=(_,_)=>UpdateMode();UpdateMode();
        _browse.Click+=(_,_)=>{var picker=new OpenFolderDialog{Title=T("选择安装目录","Choose install folder"),InitialDirectory=Directory.Exists(_directory.Text)?_directory.Text:Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)};if(picker.ShowDialog(this)==true)_directory.Text=InstallationDiscovery.IsInstallation(picker.FolderName)||Path.GetFileName(picker.FolderName).Equals("LaunchPad",StringComparison.OrdinalIgnoreCase)?picker.FolderName:Path.Combine(picker.FolderName,"LaunchPad");};
        _install.Click+=async(_,_)=>{if(_finished){try{Process.Start(new ProcessStartInfo(Path.Combine(_directory.Text,"LaunchPad.exe")){UseShellExecute=true,WorkingDirectory=_directory.Text});Close();}catch{_status.Text=T("无法启动，请从安装目录打开 LaunchPad.exe。","Unable to launch. Open LaunchPad.exe in the installation folder.");}}else await Install();};
        _cancel.Click+=(_,_)=>{if(_busy)_cancellation.Cancel();else Close();};
        Closing+=(_,e)=>{if(_busy){e.Cancel=true;_status.Text=T("正在安装，请等待完成。","Installation is in progress. Please wait.");}};
        Loaded+=(_,_)=>{if(SystemParameters.ClientAreaAnimation)BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(200)));};
    }
    private TextBlock Text(string value,double size,bool muted=false){var t=new TextBlock{Text=value,FontSize=size};t.SetResourceReference(TextBlock.ForegroundProperty,muted?"Muted":"Ink");return t;}
    private void ReadPreferences()
    {
        try
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(InstallationDiscovery.ConfigPath));var r=doc.RootElement;
            if(r.TryGetProperty("Language",out var language))_english=language.GetString()=="en-US";
            if(r.TryGetProperty("Theme",out var theme)&&theme.GetString()=="Dark")
                foreach(var (key,color) in new[]{("Surface","#20242A"),("Secondary","#292E35"),("Ink","#F0F2F5"),("Muted","#A3AAB4"),("Line","#3B424D"),("Accent","#346FAD")})Resources[key]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        catch{}
    }
    private void UpdateMode()
    {
        bool update=InstallationDiscovery.IsInstallation(_directory.Text);
        _title.Text=update?T("更新你的启动台","Update your LaunchPad"):T("一键安装，即刻开始","One click. Ready to launch.");
        _description.Text=update?T("已找到现有版本，将在原目录更新。应用、主题与备份都会保留。","An existing version was found. Update in place and keep your apps, themes and backups."):T("选择安装位置，其余交给我们。已包含运行环境，安装后即可使用。","Choose a location and we'll handle the rest. Runtime included — ready to use after installation.");
        _install.Content=update?T("立即更新","Update now"):T("立即安装","Install now");
        _install.IsEnabled=_manifest!=null;
        if(_manifest==null)_status.Text=T("安装包缺少应用文件，请重新下载安装包。","Application payload is missing. Download the installer again.");
    }
    private async Task Install()
    {
        if(_busy)return;_busy=true;_install.IsEnabled=_browse.IsEnabled=_directory.IsEnabled=_desktop.IsEnabled=false;_progress.Visibility=Visibility.Visible;
        string path=_directory.Text;bool desktop=_desktop.IsChecked==true;
        try
        {
            var progress=new Progress<InstallProgress>(p=>{_progress.Value=p.Percent;_status.Text=p.Phase switch{"Verifying"=>T("正在校验并准备安装文件…","Verifying and preparing files…"),"Closing"=>T("正在保存数据并关闭旧版本…","Saving data and closing the previous version…"),_=>T("正在安装，请稍候…","Installing, please wait…")};_cancel.IsEnabled=p.Phase=="Verifying";});
            await Task.Run(()=>InstallEngine.InstallAsync(path,_manifest,InstallEngine.Payload,progress,_cancellation.Token));
            _directory.Text=Path.GetFullPath(path);_finished=true;
            try{InstallationDiscovery.Register(_directory.Text,Version.Parse(_manifest.Version),desktop);_status.Text=T("安装完成，可以开始使用了。","Installed. You're ready to go.");}catch{_status.Text=T("程序已安装，快捷方式未能创建。可从安装目录启动。","Installed, but shortcuts could not be created. Launch from the installation folder.");}
            _title.Text=T("准备就绪","You're all set");_install.Content=T("立即启动","Launch now");_cancel.Content=T("完成","Done");
        }
        catch(OperationCanceledException){_status.Text=T("已取消，原有程序未改变。","Cancelled. The previous installation is unchanged.");_cancel.Content=T("关闭","Close");}
        catch(Exception e)
        {
            _status.Text=e.Message switch
            {
                "ApplicationStillRunning"=>T("旧版本仍在运行，请从托盘退出 LaunchPad 后重试。","The previous version is still running. Quit LaunchPad from its tray menu and retry."),
                "NewerVersionInstalled"=>T("已安装更新的版本，请下载更新的安装包。","A newer version is installed. Download a newer installer."),
                "ChooseEmptyDirectory"=>T("请选择空文件夹，或选择原有 LaunchPad 的安装目录。","Choose an empty folder or an existing LaunchPad installation."),
                "InvalidDirectory" or "LinkedPath"=>T("此安装目录不可用，请选择其他本地文件夹。","This location cannot be used. Choose another local folder."),
                "InvalidPayload" or "VersionMismatch"=>T("安装包校验失败，请重新下载。","Package verification failed. Download it again."),
                _=>e.Message.StartsWith("RollbackFailed")?T("文件恢复未完成，恢复副本保留在：","Recovery is incomplete. Backup files are at: ")+e.Message["RollbackFailed: ".Length..]:T("安装未完成，原文件已恢复。请确认目录可写，且文件未被占用后重试。","Installation did not finish. Original files were restored. Check folder permissions and close apps using the files, then retry.")
            };
        }
        finally{_busy=false;_install.IsEnabled=!_cancellation.IsCancellationRequested;_cancel.IsEnabled=true;_browse.IsEnabled=_directory.IsEnabled=_desktop.IsEnabled=!_finished;}
    }
}
