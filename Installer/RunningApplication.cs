using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
namespace LaunchPad.Setup;
public static class RunningApplication
{
    [StructLayout(LayoutKind.Sequential)]private struct UniqueProcess{public int Id;public System.Runtime.InteropServices.ComTypes.FILETIME Start;}
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]private static extern int RmStartSession(out uint handle,int flags,StringBuilder key);
    [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]private static extern int RmRegisterResources(uint handle,uint files,string[] filenames,uint applications,UniqueProcess[] processes,uint services,string[] names);
    [DllImport("rstrtmgr.dll")]private static extern int RmShutdown(uint handle,uint flags,IntPtr callback);
    [DllImport("rstrtmgr.dll")]private static extern int RmEndSession(uint handle);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe,out uint id);
    public static async Task CloseAsync(string directory,CancellationToken token)
    {
        string target=Path.GetFullPath(Path.Combine(directory,"LaunchPad.exe"));
        foreach(var process in Process.GetProcessesByName("LaunchPad"))using(process)
        {
            string path;try{path=process.MainModule.FileName;}catch{continue;}if(!string.Equals(path,target,StringComparison.OrdinalIgnoreCase))continue;
            try
            {
                using var pipe=new NamedPipeClientStream(".","LaunchPad.Backup."+Environment.UserName,PipeDirection.Out);using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(1500);await pipe.ConnectAsync(timeout.Token);
                if(GetNamedPipeServerProcessId(pipe.SafePipeHandle,out uint id)&&id==process.Id){using var writer=new StreamWriter(pipe){AutoFlush=true};await writer.WriteLineAsync("LaunchPad.ExitForUpdate");}
            }
            catch(Exception e)when(e is IOException or OperationCanceledException or UnauthorizedAccessException){token.ThrowIfCancellationRequested();}
            try{await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(3),token);continue;}catch(TimeoutException){}
            // Graceful Windows shutdown for legacy builds; register only this exact application process.
            if(RmStartSession(out var session,0,new StringBuilder(33))==0)
            {
                try{long time=process.StartTime.ToUniversalTime().ToFileTimeUtc();var app=new UniqueProcess{Id=process.Id,Start=new(){dwLowDateTime=(int)time,dwHighDateTime=(int)(time>>32)}};if(RmRegisterResources(session,0,null,1,new[]{app},0,null)==0)RmShutdown(session,0,IntPtr.Zero);}
                finally{RmEndSession(session);}
            }
            try{await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(8),token);}catch(TimeoutException){throw new IOException("ApplicationStillRunning");}
        }
    }
}
