using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
namespace LaunchPad.Setup;
public static class InstallationDiscovery
{
    public const string RegistryPath=@"Software\LaunchPad\Installation";
    public static string DefaultDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","LaunchPad");
    public static string ConfigPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"LaunchPad","config.json");
    public static string Detect()
    {
        using var key=Registry.CurrentUser.OpenSubKey(RegistryPath);var installed=key?.GetValue("InstallLocation") as string;
        if(IsInstallation(installed))return installed;
        foreach(var process in Process.GetProcessesByName("LaunchPad"))using(process)try{var dir=Path.GetDirectoryName(process.MainModule.FileName);if(IsInstallation(dir))return dir;}catch{}
        foreach(var path in new[]{@"Software\Microsoft\Windows\CurrentVersion\Run",@"Software\Classes\LaunchPad.Backup\shell\open\command"})
        {
            using var entry=Registry.CurrentUser.OpenSubKey(path);var command=entry?.GetValue(path.EndsWith("Run")?"LaunchPad":"") as string;
            string exe=ExecutableFromCommand(command);if(exe!=null&&IsInstallation(Path.GetDirectoryName(exe)))return Path.GetDirectoryName(exe);
        }
        return IsInstallation(DefaultDirectory)?DefaultDirectory:null;
    }
    public static string ExecutableFromCommand(string command)
    {
        if(string.IsNullOrWhiteSpace(command))return null;command=Environment.ExpandEnvironmentVariables(command.Trim());
        if(command.StartsWith('"')){int end=command.IndexOf('"',1);return end>1?command[1..end]:null;}
        int suffix=command.IndexOf(".exe",StringComparison.OrdinalIgnoreCase);return suffix>=0?command[..(suffix+4)]:null;
    }
    public static bool IsInstallation(string directory)
    {
        try{var file=Path.Combine(directory,"LaunchPad.exe");return File.Exists(file)&&FileVersionInfo.GetVersionInfo(file).ProductName?.Equals("LaunchPad",StringComparison.OrdinalIgnoreCase)==true;}catch{return false;}
    }
    public static Version InstalledVersion(string directory)
    {
        try{return Version.TryParse(FileVersionInfo.GetVersionInfo(Path.Combine(directory,"LaunchPad.exe")).FileVersion,out var v)?v:null;}catch{return null;}
    }
    public static void Register(string directory,Version version,bool desktop)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RegistryPath);key.SetValue("InstallLocation",directory);key.SetValue("Version",version.ToString());
        using var run=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true);
        if(run?.GetValue("LaunchPad")!=null)run.SetValue("LaunchPad","\""+Path.Combine(directory,"LaunchPad.exe")+"\"");
        Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"LaunchPad.lnk"),directory);
        if(desktop)Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"LaunchPad.lnk"),directory);
    }
    private static void Shortcut(string destination,string directory)
    {
        object shell=null,shortcut=null;
        try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic d=shell;shortcut=d.CreateShortcut(destination);dynamic link=shortcut;link.TargetPath=Path.Combine(directory,"LaunchPad.exe");link.WorkingDirectory=directory;link.IconLocation=Path.Combine(directory,"LaunchPad.exe")+",0";link.Description="LaunchPad";link.Save();}
        finally{if(shortcut!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);if(shell!=null)System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
    }
}
