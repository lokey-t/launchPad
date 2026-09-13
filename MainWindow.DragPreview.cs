using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LaunchPad.Models;

namespace LaunchPad;
public partial class MainWindow
{
    // Use the same live templates as the grid, including per-item overrides and
    // dynamic theme resources. A separate instance keeps source/placeholder opacity independent.
    private Border CreateDragPreview(AppEntry entry, bool insideFolder)
    {
        var template=(DataTemplate)FindResource(insideFolder ? "FolderItemTemplate" : new DataTemplateKey(entry.GetType()));
        var content=new ContentPresenter { Content=entry, ContentTemplate=template,
            HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
        return new Border
        {
            Width=insideFolder?TileIconW+2*TileIconM:TileWidth,
            Height=insideFolder?TileIconH+2*TileIconM:TileHeight,
            Background=Brushes.Transparent, IsHitTestVisible=false, Child=content
        };
    }
}

