using System.IO;
using System.Text.Json;
using System.Windows;
using LaunchPad.Services;

namespace LaunchPad;

public partial class App
{
    private readonly UpdateService _updates = new();
    private Task<UpdateOffer> _releaseCheck;
    private UpdateWindow _updateWindow;
    private readonly HashSet<string> _promptedVersions = new();

    private async void StartUpdateCheck()
    {
        try
        {
            await Task.Delay(5000,_backupCancellation.Token);
            if (_isQuitting) return;
            string resultPath = Path.Combine(ConfigService.DataDirectory,"update-result.json");
            if (File.Exists(resultPath))
            {
                using var result = JsonDocument.Parse(File.ReadAllText(resultPath).TrimStart('\uFEFF'));
                if (!result.RootElement.GetProperty("Success").GetBoolean())
                    PromptDialog.Notify(null,AppLanguage.T("软件更新"),AppLanguage.T("上次更新未完成。已保留恢复文件，请通过自主下载重新安装；应用配置与备份未受影响。"));
                else if (result.RootElement.TryGetProperty("Stage",out var stage)) UpdateService.DeleteStage(stage.GetString());
                File.Delete(resultPath);
            }
            await CheckUpdatesAsync(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
    public async Task CheckUpdatesAsync(bool manual,Window owner = null)
    {
        if (_updateWindow != null) { _updateWindow.Activate(); return; }
        var pending = _releaseCheck ??= _updates.CheckAsync(_backupCancellation.Token);
        try
        {
            var offer = await pending;
            if (_isQuitting) return;
            if (_updateWindow != null) { _updateWindow.Activate(); return; }
            if (offer == null)
            {
                if (manual) PromptDialog.Notify(owner,AppLanguage.T("软件更新"),AppLanguage.T("当前已是最新版本。"));
                return;
            }
            if (!manual && (UpdateService.ParseVersion(Config.IgnoredUpdateVersion) == offer.Version || _promptedVersions.Contains(offer.Version.ToString()))) return;
            _promptedVersions.Add(offer.Version.ToString());
            _updateWindow = new UpdateWindow(this,_updates,offer);
            // Keep a tray-started update prompt visible without showing the launcher underneath.
            _updateWindow.Closed += (_,_)=>_updateWindow = null;
            _updateWindow.Show(); _updateWindow.Activate();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (manual) PromptDialog.Notify(owner,AppLanguage.T("软件更新"),AppLanguage.T("暂时无法检查更新，请检查网络后重试。"));
        }
        finally { if (ReferenceEquals(_releaseCheck,pending)) _releaseCheck = null; }
    }
}
