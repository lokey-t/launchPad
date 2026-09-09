using Microsoft.Win32;

namespace LaunchPad.Services;

/// <summary>开机自启：写入/删除当前用户注册表 Run 键。</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LaunchPad";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enabled)
                key.SetValue(AppName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(AppName, false);
        }
        catch
        {
            // 忽略：注册表写入失败不阻断主流程
        }
    }
}
