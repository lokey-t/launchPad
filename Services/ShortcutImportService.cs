using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using LaunchPad.Models;
namespace LaunchPad.Services;
public static class ShortcutImportService
{
    public static AppItem Create(string path,bool resolve)
    {
        var original=Path.GetFullPath(path);
        var item=new AppItem { Path=original,Name=Path.GetFileNameWithoutExtension(original),AddedAt=DateTimeOffset.UtcNow };
        if(!resolve || !original.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)) return item;
        object com=null;
        try
        {
            com=new NativeMethods.ShellLink();
            var link=(NativeMethods.IShellLinkW)com;
            ((NativeMethods.IPersistFile)com).Load(original,0);
            var target=new StringBuilder(32768);var args=new StringBuilder(32768);var directory=new StringBuilder(32768);
            link.GetPath(target,target.Capacity,IntPtr.Zero,NativeMethods.SLGP_UNCPRIORITY);
            link.GetArguments(args,args.Capacity);link.GetWorkingDirectory(directory,directory.Capacity);
            var value=Environment.ExpandEnvironmentVariables(target.ToString());
            if(string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) || !File.Exists(value) || value.EndsWith(".lnk",StringComparison.OrdinalIgnoreCase)) return item;
            item.Path=Path.GetFullPath(value);item.Args=args.Length==0?null:args.ToString();
            var working=Environment.ExpandEnvironmentVariables(directory.ToString());
            item.WorkingDirectory=Directory.Exists(working)?working:null;
        }
        catch(Exception) { /* Broken and shell-only shortcuts keep their original launch semantics. */ }
        finally { if(com!=null && Marshal.IsComObject(com)) Marshal.FinalReleaseComObject(com); }
        return item;
    }
}
