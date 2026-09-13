using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using LaunchPad.Services;
using Microsoft.Win32;

namespace LaunchPad;

public partial class SettingsWindow
{
    private void FactoryReset_Click(object sender, RoutedEventArgs e) => BackupAction(() =>
    {
        if (!PromptDialog.Confirm(this, AppLanguage.T("恢复出厂设置"), AppLanguage.T("将清空所有分类与条目，并重置主题、快捷键、语言和启动选项。所有备份点都会保留，磁盘原文件不会删除。确定恢复？"))) return;
        _app.RestoreConfig(new LaunchPad.Models.LauncherConfig());
        RefreshBackups();
        PromptDialog.Notify(this, AppLanguage.T("恢复完成"), AppLanguage.T("已恢复初始设置。备份点仍然保留，可在此页面恢复。"));
    });

    private void InitializeBackups()
    {
        BackupService.Changed += () => Dispatcher.BeginInvoke(new Action(RefreshBackups));
        // Existing settings handlers update the shared model first; persist after routed events finish.
        RoutedEventHandler persist = (_, _) =>
        {
            if (_refreshing || !IsVisible) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_refreshing) _app.SaveConfig();
            }));
        };
        AddHandler(ToggleButton.CheckedEvent, persist, true);
        AddHandler(ToggleButton.UncheckedEvent, persist, true);
        AddHandler(Button.ClickEvent, persist, true);
        AddHandler(RangeBase.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>((s, e) => persist(s, e)), true);
    }

    private void AutoBackup_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshing) return;
        _app.Config.AutoBackup = AutoBackupToggle.IsChecked == true;
        _app.SaveConfig();
        RefreshBackups();
    }

    private void RefreshBackups()
    {
        if (BackupList == null) return;
        try
        {
            var selected = (BackupList.SelectedItem as BackupPoint)?.Id;
            var points = BackupService.List();
            BackupList.ItemsSource = points;
            BackupList.SelectedItem = points.FirstOrDefault(p => p.Id == selected);
            BackupStatus.Text = BackupService.LastError ?? string.Format(AppLanguage.T("{0} 条已保存 · {1} / 30 条自动备份"),points.Count(p => p.Saved),points.Count(p => !p.Saved));
        }
        catch (Exception ex) { BackupStatus.Text = AppLanguage.T("无法读取备份：") + ex.Message; }
    }

    private void BackupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BackupActions == null) return;
        BackupActions.IsEnabled = BackupList.SelectedItem is BackupPoint;
        SaveBackupButton.IsEnabled = BackupList.SelectedItem is BackupPoint { Saved: false };
    }

    private void BackupAction(Action action)
    {
        try { action(); RefreshBackups(); }
        catch (Exception ex) { PromptDialog.Notify(this, AppLanguage.T("备份与恢复"), ex.Message); }
    }

    private void CreateBackup_Click(object sender, RoutedEventArgs e) => BackupAction(() =>
    {
        var name = PromptDialog.Show(this, AppLanguage.T("创建备份"), AppLanguage.T("给当前配置留一个名字"), $"手动备份 {DateTime.Now:MM-dd HH:mm}");
        if (string.IsNullOrWhiteSpace(name)) return;
        var point = BackupService.Create(_app.Config, name);
        RefreshBackups();
        BackupList.SelectedItem = ((IEnumerable<BackupPoint>)BackupList.ItemsSource).First(p => p.Id == point.Id);
    });

    private void ImportBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = AppLanguage.T("LaunchPad 备份 (*.qdtbackup)|*.qdtbackup"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) OpenBackupFile(dialog.FileName);
    }

    public void OpenBackupFile(string path) => BackupAction(() =>
    {
        NavBackup.IsChecked = true;
        var point = BackupService.Import(path);
        RefreshBackups();
        BackupList.SelectedItem = ((IEnumerable<BackupPoint>)BackupList.ItemsSource).First(p => p.Id == point.Id);
        PromptDialog.Notify(this, AppLanguage.T("备份已导入"), AppLanguage.T("已加入保存的备份点。点击“恢复”应用此配置，当前配置尚未更改。"));
    });

    private void SaveBackup_Click(object sender, RoutedEventArgs e) => BackupAction(() =>
    {
        if (BackupList.SelectedItem is BackupPoint point) BackupService.Update(point, point.Name, true);
    });

    private void RenameBackup_Click(object sender, RoutedEventArgs e) => BackupAction(() =>
    {
        if (BackupList.SelectedItem is not BackupPoint point) return;
        var name = PromptDialog.Show(this, AppLanguage.T("重命名备份"), AppLanguage.T("备份名称"), point.Name);
        if (!string.IsNullOrWhiteSpace(name)) BackupService.Update(point, name, point.Saved);
    });

    private void DeleteBackup_Click(object sender, RoutedEventArgs e) => BackupAction(() =>
    {
        if (BackupList.SelectedItem is BackupPoint point && PromptDialog.Confirm(this, AppLanguage.T("删除备份"), string.Format(AppLanguage.T("确定删除“{0}”？此操作无法撤销。"),point.Name))) BackupService.Delete(point);
    });

    private void ExportBackup_Click(object sender, RoutedEventArgs e) => BackupAction(() =>
    {
        if (BackupList.SelectedItem is not BackupPoint point) return;
        var dialog = new SaveFileDialog { Filter = AppLanguage.T("LaunchPad 备份 (*.qdtbackup)|*.qdtbackup"), DefaultExt = ".qdtbackup", FileName = $"LaunchPad-{point.CreatedUtc.ToLocalTime():yyyyMMdd-HHmmss}.qdtbackup" };
        if (dialog.ShowDialog(this) == true) BackupService.Export(point, dialog.FileName);
    });

    private void RestoreBackup_Click(object sender, RoutedEventArgs e) => BackupAction(() =>
    {
        if (BackupList.SelectedItem is not BackupPoint point) return;
        if (!PromptDialog.Confirm(this, AppLanguage.T("恢复备份"), string.Format(AppLanguage.T("恢复“{0}”？当前状态会先保存在新的备份点中。"),point.Name))) return;
        var config = BackupService.Restore(point);
        BackupService.Create(_app.Config, $"恢复前 {DateTime.Now:MM-dd HH:mm:ss}");
        _app.RestoreConfig(config);
        PromptDialog.Notify(this, AppLanguage.T("恢复完成"), AppLanguage.T("配置、图标和背景已恢复。原配置已保存，可随时恢复。") );
    });
}
