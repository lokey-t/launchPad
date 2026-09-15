using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LaunchPad.Models;

namespace LaunchPad.Services;

/// <summary>应用/文件图标提取与缓存（SHGetFileInfo，32px 大图标）。</summary>
public static class IconService
{
    private static readonly ImageSource DirectoryIcon = CreateDirectoryIcon();
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取图标：.lnk 先解析目标再取目标图标；文件不存在时回退为系统默认图标。</summary>
    public static ImageSource GetIcon(AppEntry entry)
    {
        if (entry is AppItem custom && !string.IsNullOrEmpty(custom.CustomIconPath))
        {
            try { return LoadCustomImage(custom.CustomIconPath); }
            catch { /* Missing user image: keep the application usable with its original icon. */ }
        }
        var path = (entry as AppItem)?.Path;
        if (entry is AppFolder) path = "::folder::";
        return GetIcon(path);
    }

    public static ImageSource LoadCustomImage(string path)
    {
        string key = "custom:" + path;
        lock (Cache) { if (Cache.TryGetValue(key, out var found)) return found; }
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 128;
        image.UriSource = new Uri(Path.GetFullPath(path));
        image.EndInit();
        image.Freeze();
        lock (Cache) Cache[key] = image;
        return image;
    }

    public static string ImportCustomImage(string path)
    {
        LoadCustomImage(path); // Validate before modifying the saved entry.
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaunchPad", "Icons");
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        File.Copy(path, destination);
        return destination;
    }

    public static ImageSource GetIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var hit)) return hit;
        }

        var icon = Extract(path);
        lock (Cache)
        {
            Cache[path] = icon;
        }
        return icon;
    }

    private static ImageSource CreateDirectoryIcon()
    {
        // Vector artwork stays crisp at every icon size and is composed inside the existing themed tile.
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var rear = new LinearGradientBrush(Color.FromRgb(107,196,255), Color.FromRgb(42,130,233), 90);
            dc.DrawGeometry(rear, null, Geometry.Parse("M 8,22 Q 8,16 14,16 L 29,16 Q 32,16 35,20 L 38,24 L 51,24 Q 56,24 56,29 L 56,48 Q 56,53 51,53 L 13,53 Q 8,53 8,48 Z"));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(225,244,255)), null, new Rect(13,26,38,21), 3,3);
            var front = new LinearGradientBrush(Color.FromRgb(94,186,255), Color.FromRgb(24,111,224), 90);
            dc.DrawRoundedRectangle(front, new Pen(new SolidColorBrush(Color.FromArgb(95,255,255,255)),.7), new Rect(7,30,50,26), 5,5);
            var mark = new SolidColorBrush(Color.FromArgb(230,255,255,255));
            foreach (var rect in new[]{new Rect(25,37,6,6),new Rect(33,37,6,6),new Rect(25,45,6,6),new Rect(33,45,6,6)})
                dc.DrawRoundedRectangle(mark,null,rect,.7,.7);
            // Stable transparent bounds keep alignment identical in the grid, folder previews and drag image.
            dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,64,64));
        }
        drawing.Freeze();
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private static ImageSource Extract(string path)
    {
        try
        {
            // .lnk 快捷方式：优先解析目标后取目标图标，更贴近"应用本身"的图标
            var target = path;
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = NativeMethods.ResolveLnkTarget(path);
                if (!string.IsNullOrEmpty(resolved) && !resolved.Equals(path, StringComparison.OrdinalIgnoreCase))
                    target = resolved;
            }

            // Filesystem folders (including resolved shortcuts) are not ordinary files.
            // Draw locally so restricted folders and Shell namespace folders cannot lose their icon.
            if (path == "::folder::" || Directory.Exists(target) ||
                target.StartsWith("::{", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return DirectoryIcon;
            var info = new NativeMethods.SHFILEINFO();
            var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
            if (!File.Exists(target))
                flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES;

            var hIcon = NativeMethods.SHGetFileInfo(
                target,
                NativeMethods.FILE_ATTRIBUTE_NORMAL,
                ref info,
                (uint)Marshal.SizeOf(info),
                flags);

            if (info.hIcon != IntPtr.Zero)
            {
                try
                {
                    using var icon = System.Drawing.Icon.FromHandle(info.hIcon);
                    using var bmp = icon.ToBitmap();
                    return bmp.ToImageSource();
                }
                finally
                {
                    NativeMethods.DestroyIcon(info.hIcon);
                }
            }
        }
        catch
        {
            // 忽略：返回 null，UI 显示占位图标
        }
        return null;
    }
}

internal static class BitmapExtensions
{
    public static ImageSource ToImageSource(this System.Drawing.Bitmap bmp)
    {
        var hBitmap = bmp.GetHbitmap();
        try
        {
            return Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
        }
    }
}
