using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LaunchPad.Plugins;
using LaunchPad.PluginSdk;
using LaunchPad.Services;
namespace LaunchPad;
public partial class MainWindow
{
    private CancellationTokenSource _pluginSearch;
    private async void QueuePluginSearch(string query)
    {
        _pluginSearch?.Cancel();
        var cancel=new CancellationTokenSource();_pluginSearch=cancel;
        if(PluginResultsHost==null){_pluginSearch=null;cancel.Dispose();return;}
        PluginResultsHost.Children.Clear();PluginResultsHost.Visibility=Visibility.Collapsed;
        if(query.Length==0){_pluginSearch=null;cancel.Dispose();return;}
        try
        {
            await Task.Delay(250,cancel.Token);
            var results=await PluginService.Current.Search(query,cancel.Token);
            if(cancel.IsCancellationRequested||SearchBox.Text.Trim()!=query)return;
            foreach(var result in results)
            {
                var text=new StackPanel();
                var title=new TextBlock{Text=result.Result.Title,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};title.SetResourceReference(TextBlock.ForegroundProperty,"TextPrimaryBrush");
                var detail=new TextBlock{Text=result.PluginName+" · "+result.Result.Description,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};detail.SetResourceReference(TextBlock.ForegroundProperty,"TextSecondaryBrush");
                text.Children.Add(title);text.Children.Add(detail);
                var button=new Button{Style=(Style)FindResource("PluginResultButtonStyle"),Content=text,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(14,10,14,10),Margin=new Thickness(0,0,0,6)};
                button.Click+=async(_,_)=>{button.IsEnabled=false;try{await ExecutePlugin(result.PluginId,result.Result.Command,result.Result.Value,null);}finally{button.IsEnabled=true;}};
                PluginResultsHost.Children.Add(button);
            }
            PluginResultsHost.Visibility=results.Count>0?Visibility.Visible:Visibility.Collapsed;
        }
        catch(OperationCanceledException){}
        catch(Exception ex){ShowToast(AppLanguage.T("插件操作失败")+": "+ex.Message);}
        finally{if(_pluginSearch==cancel)_pluginSearch=null;cancel.Dispose();}
    }
    private void PluginActions_Opened(object sender,RoutedEventArgs e)
    {
        if(sender is not MenuItem menu||e.OriginalSource!=menu)return;
        menu.Items.Clear();AddPluginCommands(menu.Items,false,null);
        if(menu.Items.Count==0)menu.Items.Add(new MenuItem{Header=AppLanguage.T("暂无可用插件操作"),IsEnabled=false});
    }
    private void AddPluginContext(ContextMenu menu,string path)
    {
        var child=new MenuItem{Header=AppLanguage.T("插件操作"),Icon="◇"};AddPluginCommands(child.Items,true,path);if(child.Items.Count>0)menu.Items.Add(child);
    }
    private void AddPluginCommands(ItemCollection items,bool context,string path)
    {
        foreach(var plugin in PluginService.Current.List().Where(p=>p.State.Enabled))
        foreach(var command in plugin.Manifest.Commands.Where(c=>c.Context==context))
        {
            var option=new MenuItem{Header=plugin.Manifest.Name+" · "+command.Title};
            option.Click+=async(_,e)=>{e.Handled=true;await ExecutePlugin(plugin.Manifest.Id,command.Id,null,path);};items.Add(option);
        }
    }
    private async Task ExecutePlugin(string id,string command,string value,string contextPath)
    {
        try
        {
            var response=await PluginService.Current.Invoke(id,new Request{Method="execute",Command=command,Value=value,ContextPath=contextPath});
            if(response.Action=="clipboard")Clipboard.SetText(response.Value??"");
            if(response.Action=="open")
            {
                if(string.IsNullOrEmpty(response.Value)||!Path.IsPathFullyQualified(response.Value)||(!File.Exists(response.Value)&&!Directory.Exists(response.Value)))throw new IOException("Invalid target path");
                Process.Start(new ProcessStartInfo(response.Value){UseShellExecute=true});
            }
            ShowToast(response.Message??AppLanguage.T("插件操作完成"));
        }
        catch(Exception ex){ShowToast(AppLanguage.T("插件操作失败")+": "+ex.Message);}
    }
}
