using System.Windows;
using System.Windows.Controls;
using LaunchPad.Models;
using LaunchPad.Services;
using Microsoft.Win32;

namespace LaunchPad;

public sealed partial class ThemeCenterWindow
{
    private readonly ComboBox _savedThemes = new() { MinHeight=34, DisplayMemberPath="Name" };
    private StackPanel _presetControls;
    private bool _presetBusy;
    private void BuildPresetControls(StackPanel parent)
    {
        _presetControls=new StackPanel(); parent.Children.Add(_presetControls);
        _presetControls.Children.Add(Label(AppLanguage.T("我的主题"),15));
        _presetControls.Children.Add(_savedThemes);
        var actions=new WrapPanel(); _presetControls.Children.Add(actions);
        actions.Children.Add(Button(AppLanguage.T("保存当前主题…"),()=>SavePreset()));
        actions.Children.Add(Button(AppLanguage.T("导入主题…"),()=>ImportPreset()));
        var selectionActions=new WrapPanel(); _presetControls.Children.Add(selectionActions);
        selectionActions.Children.Add(Button(AppLanguage.T("应用主题"),()=>ApplyPreset()));
        selectionActions.Children.Add(Button(AppLanguage.T("导出…"),()=>ExportPreset()));
        selectionActions.Children.Add(Button(AppLanguage.T("删除"),()=>DeletePreset()));
        _savedThemes.SelectionChanged+=(_,_)=>selectionActions.IsEnabled=_savedThemes.SelectedItem is ThemePreset;
        selectionActions.IsEnabled=false;
        _presetControls.Children.Add(Label(AppLanguage.T("预设只包含主题与素材，不更改其他配置和数据。"),11));
        try { RefreshPresets(); } catch(Exception) { _status.Text=AppLanguage.T("无法读取主题预设。"); }
    }
    private void RefreshPresets(string selected=null)
    {
        _savedThemes.ItemsSource=ThemePresetService.List();
        _savedThemes.SelectedItem=((IEnumerable<ThemePreset>)_savedThemes.ItemsSource).FirstOrDefault(p=>p.Id==selected);
    }
    private async Task PresetAction(Func<Task> action)
    {
        if(_presetBusy || _saving) return;
        _presetBusy=true; IsEnabled=false;
        try { await action(); }
        catch(Exception ex) { PromptDialog.Notify(this,AppLanguage.T("主题预设"),AppLanguage.T("主题操作失败：")+ex.Message); }
        finally { _presetBusy=false; IsEnabled=true; }
    }
    private async void SavePreset()
    {
        if(_presetBusy) return;
        var name=PromptDialog.Show(this,AppLanguage.T("保存主题"),AppLanguage.T("主题名称"),_draft.Name);
        if(string.IsNullOrWhiteSpace(name)) return;
        var theme=Category==null?_draft.Copy():ThemeService.Resolve(_app.Config,new AppCategory { ThemeOverride=_draft });
        await PresetAction(async()=>
        {
            var point=await Task.Run(()=>ThemePresetService.Save(theme,name));
            RefreshPresets(point.Id); _status.Text=AppLanguage.T("主题已保存，可随时切换回来。");
        });
    }
    private async void ImportPreset()
    {
        var dialog=new OpenFileDialog { Title=AppLanguage.T("导入主题"),Filter=AppLanguage.T("LaunchPad 主题")+" (*.qdtstylebackup)|*.qdtstylebackup" };
        if(dialog.ShowDialog(this)!=true) return;
        await PresetAction(async()=>
        {
            var point=await Task.Run(()=>ThemePresetService.Import(dialog.FileName));
            RefreshPresets(point.Id);
            if(ThemePresetService.IsNewer(point.Version)) ShowNewerThemeNotice();
            _status.Text=AppLanguage.T("主题已导入。点击“应用主题”覆盖当前所选范围的主题。");
        });
    }
    private void ShowNewerThemeNotice() => PromptDialog.Notify(this,AppLanguage.T("主题版本提示"),AppLanguage.T("该主题使用更新版本制作。当前版本会应用支持的设置，并保留新功能配置；更新软件后才能获得完整效果。"));
    private async void ApplyPreset()
    {
        if(_savedThemes.SelectedItem is not ThemePreset preset) return;
        await PresetAction(async()=>
        {
            var theme=await Task.Run(()=>ThemePresetService.Load(preset));
            if(ThemePresetService.IsNewer(theme.SourceVersion)) ShowNewerThemeNotice();
            theme.OverrideColors=theme.OverrideBackground=theme.OverrideIcons=true; theme.OverrideMaterial=true;
            _draft=theme; LoadEditors(); RefreshPreview();
            await SaveAsync();
        });
    }
    private void ExportPreset()
    {
        if(_savedThemes.SelectedItem is not ThemePreset preset) return;
        var dialog=new SaveFileDialog { Title=AppLanguage.T("导出主题"),Filter=AppLanguage.T("LaunchPad 主题")+" (*.qdtstylebackup)|*.qdtstylebackup",DefaultExt=ThemePresetService.Extension,AddExtension=true,FileName="LaunchPad-theme"+ThemePresetService.Extension };
        if(dialog.ShowDialog(this)!=true) return;
        _=PresetAction(async()=> { await Task.Run(()=>ThemePresetService.Export(preset,dialog.FileName)); _status.Text=AppLanguage.T("主题与静态素材已导出。"); });
    }
    private void DeletePreset()
    {
        if(_savedThemes.SelectedItem is not ThemePreset preset) return;
        if(!PromptDialog.Confirm(this,AppLanguage.T("删除主题"),string.Format(AppLanguage.T("删除预设“{0}”？当前已应用的主题和素材不受影响。"),preset.Name))) return;
        _=PresetAction(async()=> { await Task.Run(()=>ThemePresetService.Delete(preset)); RefreshPresets(); });
    }
}
