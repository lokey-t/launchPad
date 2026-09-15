using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace LaunchPad.Services;

/// <summary>Current-user, zero-delay logon task with legacy Run fallback.</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly object Gate = new();
    private static string _applied;
    private static string UserId => WindowsIdentity.GetCurrent().User.Value;
    private static string TaskName => "LaunchPad-Startup-" + UserId;

    internal static string TaskXml(string executable, string userId)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string name, object value) => new(ns + name, value);
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", "LaunchPad: start when the current user signs in.")),
            E("Triggers", E("LogonTrigger", new[]{E("Enabled", true), E("UserId", userId), E("Delay", "PT0S")})),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "User"), E("UserId", userId), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
            E("Settings", new[]{ E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", false), E("StopIfGoingOnBatteries", false), E("AllowHardTerminate", false), E("StartWhenAvailable", true), E("RunOnlyIfNetworkAvailable", false), E("Enabled", true), E("ExecutionTimeLimit", "PT0S"), E("Priority", 6)}),
            new XElement(ns + "Actions", new XAttribute("Context", "User"), E("Exec", new[]{E("Command", executable), E("Arguments", "--autostart"), E("WorkingDirectory", Path.GetDirectoryName(executable))}))).ToString();
    }

    public static bool IsEnabled()
    {
        object service = null, folder = null, task = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            ((dynamic)service).Connect(); folder = ((dynamic)service).GetFolder(@"\");
            task = ((dynamic)folder).GetTask(TaskName);
            if (((dynamic)task).Enabled) return true;
        }
        catch { }
        finally { Release(task); Release(folder); Release(service); }
        try { using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath); return key?.GetValue("LaunchPad") != null; }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        lock (Gate)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable)) return;
            var signature = enabled + "|" + executable;
            if (_applied == signature) return; // Saving other settings must not repeatedly register a task.
            object service = null, folder = null, task = null;
            bool taskReady = false;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
                ((dynamic)service).Connect(); folder = ((dynamic)service).GetFolder(@"\");
                if (enabled)
                    task = ((dynamic)folder).RegisterTask(TaskName, TaskXml(executable, UserId), 6, UserId, null, 3, null);
                else
                {
                    try { ((dynamic)folder).DeleteTask(TaskName, 0); }
                    catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002)) { }
                }
                taskReady = true;
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("LaunchPad startup task: " + ex.Message); }
            finally { Release(task); Release(folder); Release(service); }
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (enabled && !taskReady) key.SetValue("LaunchPad", $"\"{executable}\" --autostart");
                else key.DeleteValue("LaunchPad", false);
                if (taskReady) _applied = signature;
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("LaunchPad startup registration: " + ex.Message); }
        }
    }

    private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
