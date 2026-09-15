using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaunchPad.Plugins;
using LaunchPad.Services;
namespace LaunchPad;
public partial class SettingsWindow
{
    private void RefreshPlugins()
    {
        if(PluginCards==null)return;
        PluginCards.Children.Clear();
        var plugins=PluginService.Current.List();
        if(plugins.Count==0)PluginCards.Children.Add(PluginText(AppLanguage.T("尚未安装插件。导入 .qdtplugin 文件开始使用。"),true));
        foreach(var plugin in plugins)
        {
            var m=plugin.Manifest;var state=plugin.State;
            var content=new StackPanel();
            var card=new Border{CornerRadius=new CornerRadius(14),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12),BorderThickness=new Thickness(1),Child=content};
            card.SetResourceReference(Border.BackgroundProperty,"SubPanelBgBrush");card.SetResourceReference(Border.BorderBrushProperty,"BorderBrush");
            content.Children.Add(PluginText(m.Name+" · "+m.Version));
            content.Children.Add(PluginText((m.Description??"")+"\n"+(m.Author??m.Id),true));
            content.Children.Add(PluginText(AppLanguage.T(state.Enabled?"已启用":"已停用")+(m.SearchPrefix!=null?" · "+AppLanguage.T("搜索前缀")+": "+m.SearchPrefix:""),true));
            if(state.LastError!=null)content.Children.Add(PluginText(AppLanguage.T("最近错误")+": "+state.LastError,true));
            var actions=new WrapPanel{Margin=new Thickness(0,10,0,0)};
            void Button(string label,Action action){var b=new Button{Content=AppLanguage.T(label),Padding=new Thickness(12,7,12,7),Margin=new Thickness(0,0,8,6)};b.Click+=(_,_)=>PluginOperation(action);actions.Children.Add(b);}
            Button(state.Enabled?"停用":"启用",()=>{
                if(!state.Enabled&&!PromptDialog.Confirm(this,AppLanguage.T("启用插件"),m.Name+"\n\n"+AppLanguage.T("插件是独立程序，可使用当前用户权限访问电脑；这不是安全沙箱。仅启用你信任的插件。")+"\n\n"+AppLanguage.T("宿主接口权限")+": "+string.Join(", ",m.Permissions.Select(p=>AppLanguage.T(p switch {"clipboard.write"=>"写入剪贴板","files.open"=>"打开本地文件或文件夹","context.read"=>"读取选中条目的路径",_=>p})))))return;
                PluginService.Current.SetEnabled(m.Id,!state.Enabled);RefreshPlugins();
            });
            if(state.Errors.Count>0)Button("错误日志",()=>PromptDialog.Notify(this,AppLanguage.T("错误日志"),string.Join("\n",state.Errors)));
            Button("卸载",()=>{if(PromptDialog.Confirm(this,AppLanguage.T("卸载插件"),AppLanguage.T("移除插件程序，保留独立设置与数据。"))){PluginService.Current.Uninstall(m.Id);RefreshPlugins();}});
            content.Children.Add(actions);
            foreach(var setting in m.Settings)
            {
                content.Children.Add(PluginText(setting.Title,true));
                var row=new DockPanel{Margin=new Thickness(0,4,0,8)};
                var save=new Button{Content=AppLanguage.T("保存"),Padding=new Thickness(12,6,12,6),Margin=new Thickness(8,0,0,0)};DockPanel.SetDock(save,Dock.Right);row.Children.Add(save);
                var input=new TextBox{Text=state.Settings.GetValueOrDefault(setting.Key,setting.Default??""),MaxLength=4096,Padding=new Thickness(8,6,8,6)};row.Children.Add(input);
                save.Click+=(_,_)=>PluginOperation(()=>{PluginService.Current.SetSetting(m.Id,setting.Key,input.Text);PromptDialog.Notify(this,AppLanguage.T("插件设置"),AppLanguage.T("已保存"));});
                content.Children.Add(row);
            }
            PluginCards.Children.Add(card);
        }
    }
    private static TextBlock PluginText(string text,bool secondary=false)
    {
        var block=new TextBlock{Text=text,FontSize=secondary?12:15,FontWeight=secondary?FontWeights.Normal:FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,7)};
        block.SetResourceReference(TextBlock.ForegroundProperty,secondary?"TextSecondaryBrush":"TextPrimaryBrush");return block;
    }
    private void PluginOperation(Action action)
    {
        try{action();}catch(Exception ex){PromptDialog.Notify(this,AppLanguage.T("插件操作失败"),ex.Message);RefreshPlugins();}
    }
    private async void InstallPlugin_Click(object sender,RoutedEventArgs e)
    {
        var picker=new Microsoft.Win32.OpenFileDialog{Filter="LaunchPad plugin|*.qdtplugin",Title=AppLanguage.T("安装插件")};
        if(picker.ShowDialog(this)!=true)return;
        await OpenPluginFile(picker.FileName);
    }
    public async Task OpenPluginFile(string path)
    {
        if(!InstallPluginButton.IsEnabled)return;
        NavPlugins.IsChecked=true;
        var button=InstallPluginButton;var originalContent=button.Content;button.IsEnabled=false;
        try
        {
            var manifest=await Task.Run(()=>PluginService.Current.Inspect(path));
            if(!PromptDialog.Confirm(this,AppLanguage.T("导入并启用插件"),manifest.Name+" · "+manifest.Version+"\n"+manifest.Description+"\n\n"+AppLanguage.T("确认后将直接导入并启用插件。")+"\n"+AppLanguage.T("插件是独立程序，可使用当前用户权限访问电脑；这不是安全沙箱。仅启用你信任的插件。")+"\n\n"+AppLanguage.T("宿主接口权限")+": "+string.Join(", ",manifest.Permissions.Select(p=>AppLanguage.T(p switch {"clipboard.write"=>"写入剪贴板","files.open"=>"打开本地文件或文件夹","context.read"=>"读取选中条目的路径",_=>p})))))return;
            button.Content=AppLanguage.T("正在导入…");
            await Task.Run(()=>
            {
                PluginService.Current.Install(path);
                PluginService.Current.SetEnabled(manifest.Id,true);
            });
            RefreshPlugins();
        }
        catch(Exception ex){RefreshPlugins();PromptDialog.Notify(this,AppLanguage.T("插件操作失败"),ex.Message);}
        finally{button.Content=originalContent;button.IsEnabled=true;}
    }
}
