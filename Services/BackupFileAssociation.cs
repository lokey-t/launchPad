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

    private const string ShowCommand="LaunchPad.ShowMain";
    [System.Runtime.InteropServices.DllImport("kernel32.dll",SetLastError=true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint processId);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
    public static bool ForwardShowMain() => ForwardMessage(ShowCommand);
    public static bool Forward(string path) => ForwardMessage(Path.GetFullPath(path));
    private static bool ForwardMessage(string message)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(5000);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            if(GetNamedPipeServerProcessId(pipe.SafePipeHandle,out var processId)) AllowSetForegroundWindow(processId);
            writer.WriteLine(message);
            return true;
        }
        catch { return false; }
    }

    public static async Task Listen(Action<string> open, CancellationToken cancellation, Action showMain=null, Action exitForUpdate=null)
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
                if(path==ShowCommand) { showMain?.Invoke(); continue; }
                if(path=="LaunchPad.ExitForUpdate") { exitForUpdate?.Invoke(); continue; }
                if (path?.Length <= 32767 && Path.GetExtension(path).Equals(".qdtbackup", StringComparison.OrdinalIgnoreCase)) open(path);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { await Task.Delay(500, cancellation); }
        }
    }
}
