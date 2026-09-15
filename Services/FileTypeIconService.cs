using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LaunchPad.Services;

/// <summary>Explorer file icons, with distinct silhouettes as well as badge colors.</summary>
internal static class FileTypeIconService
{
    public static string EnsureIcon(string kind)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaunchPad", "FileIcons");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, kind + "-v1.ico");
        if (File.Exists(path)) return path;
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var images = sizes.Select(size => Render(kind, size)).ToArray();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new BinaryWriter(File.Create(temporary)))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                var offset = 6 + sizes.Length * 16;
                for (var i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
                }
                foreach (var image in images) writer.Write(image);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }

    private static byte[] Render(string kind, int size)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 64f, size / 64f);
        using var paper = new SolidBrush(Color.FromArgb(242, 245, 250));
        using var edge = new Pen(Color.FromArgb(163, 177, 198), 1.5f);
        var points = new[] { new PointF(10, 3), new PointF(37, 3), new PointF(50, 16), new PointF(50, 58), new PointF(10, 58) };
        g.FillPolygon(paper, points); g.DrawPolygon(edge, points);
        g.DrawLines(edge, new[] { new PointF(37, 4), new PointF(37, 16), new PointF(49, 16) });
        using var line = new Pen(Color.FromArgb(182, 195, 213), 3);
        g.DrawLine(line, 18, 26, 39, 26); g.DrawLine(line, 18, 34, 31, 34);
        using var badge = new SolidBrush(kind == "Plugin" ? Color.FromArgb(133, 83, 225) : kind == "Theme" ? Color.FromArgb(13, 151, 151) : Color.FromArgb(49, 119, 220));
        g.FillEllipse(badge, 29, 29, 34, 34);
        using var white = new SolidBrush(Color.White);
        using var pen = new Pen(Color.White, 2.5f) { LineJoin = LineJoin.Round };
        if (kind == "Plugin")
        {
            g.FillRectangle(white, 38, 40, 17, 15);
            g.FillEllipse(white, 43, 35, 7, 9);
            g.FillEllipse(white, 51, 44, 8, 7);
            g.FillEllipse(badge, 34, 44, 8, 7);
        }
        else if (kind == "Theme")
        {
            g.FillEllipse(white, 36, 37, 21, 19);
            g.FillEllipse(badge, 49, 48, 9, 9);
            g.FillEllipse(badge, 40, 41, 3, 3); g.FillEllipse(badge, 46, 39, 3, 3);
            g.FillEllipse(badge, 51, 43, 3, 3); g.FillEllipse(badge, 39, 47, 3, 3);
        }
        else
        {
            g.DrawRectangle(pen, 37, 42, 20, 13);
            g.DrawRectangle(pen, 36, 37, 22, 5);
            g.DrawLine(pen, 43, 47, 51, 47);
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, ImageFormat.Png); return stream.ToArray();
    }
}
