using System.IO;
using System.IO.Pipes;
using Microsoft.Win32;

namespace LaunchPad.Services;

public static class BackupFileAssociation
{
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
    internal static string PipeName { get; set; } = "LaunchPad.Backup." + Environment.UserName;

    public static void Register()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return;
        using var extension = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.qdtbackup");
        extension.SetValue("", "LaunchPad.Backup");
        using var type = Registry.CurrentUser.CreateSubKey(@"Software\Classes\LaunchPad.Backup");
        type.SetValue("", "LaunchPad 备份");
        using var icon = type.CreateSubKey("DefaultIcon");
        icon.SetValue("", $"\"{executable}\",0");
        using var command = type.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{executable}\" \"%1\"");
        SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool Forward(string path)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(5000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            writer.WriteLine(Path.GetFullPath(path));
            return true;
        }
        catch { return false; }
    }

    public static async Task Listen(Action<string> open, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                using var reader = new StreamReader(pipe);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                var path = await reader.ReadLineAsync(timeout.Token);
                if (path?.Length <= 32767 && Path.GetExtension(path).Equals(".qdtbackup", StringComparison.OrdinalIgnoreCase)) open(path);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(500, cancellation); }
        }
    }
}
