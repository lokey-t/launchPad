using System.Diagnostics;
using System.Windows;
using LaunchPad.Models;

namespace LaunchPad.Services;

/// <summary>应用启动服务：用 ShellExecute 语义启动应用/文件/快捷方式。</summary>
public static class AppLaunchService
{
    public static void Launch(AppItem item)
    {
        if (item == null || string.IsNullOrEmpty(item.Path)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.Path,
                UseShellExecute = true
            };
            if (!string.IsNullOrEmpty(item.Args))
                psi.Arguments = item.Args;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法启动“{item.Name}”：{ex.Message}", "LaunchPad",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
