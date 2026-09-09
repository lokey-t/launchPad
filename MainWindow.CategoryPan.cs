using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LaunchPad.Models;
namespace LaunchPad;
public partial class MainWindow
{
    private Point _panStart;
    private double _panOffset;
    private bool _panPressed, _panning;
    private CategoryTab _panTab;
    private void CategoryPan_Down(object sender,MouseButtonEventArgs e)
    {
        if (_dragging || CategoryStrip.ScrollableWidth<=0) return;
        _panTab=null;
        for(var node=e.OriginalSource as DependencyObject;node!=null && node!=CategoryStrip;node=VisualTreeHelper.GetParent(node))
            if(node is FrameworkElement { DataContext:CategoryTab tab }) { _panTab=tab; break; }
        _panStart=e.GetPosition(CategoryStrip); _panOffset=CategoryStrip.HorizontalOffset;
        _panPressed=true; _panning=false; Mouse.Capture(CategoryStrip); e.Handled=true;
    }
    private void CategoryPan_Move(object sender,MouseEventArgs e)
    {
        if (!_panPressed) return;
        if(e.LeftButton!=MouseButtonState.Pressed) { EndCategoryPan(false); return; }
        PanCategories(e.GetPosition(CategoryStrip).X-_panStart.X); e.Handled=true;
    }
    private void PanCategories(double delta)
    {
        if(Math.Abs(delta)>6) _panning=true;
        if(!_panning) return;
        CategoryStrip.Cursor=Cursors.SizeWE;
        CategoryStrip.ScrollToHorizontalOffset(_panOffset-delta);
    }
    private void CategoryPan_Up(object sender,MouseButtonEventArgs e)
    {
        if(!_panPressed) return;
        EndCategoryPan(true); e.Handled=true;
    }
    private void EndCategoryPan(bool click)
    {
        var tab=click && !_panning ? _panTab:null;
        _panPressed=false; _panning=false; _panTab=null; CategoryStrip.Cursor=null;
        if(Mouse.Captured==CategoryStrip) Mouse.Capture(null);
        if(tab!=null) SelectTab(tab);
    }
    private void CategoryPan_Wheel(object sender,MouseWheelEventArgs e)
    {
        if(CategoryStrip.ScrollableWidth<=0) return;
        CategoryStrip.ScrollToHorizontalOffset(CategoryStrip.HorizontalOffset-e.Delta*.65); e.Handled=true;
    }
    private void ScrollCategoryDragEdge(Point point)
    {
        if(!Contains(CategoryStrip,point) || CategoryStrip.ScrollableWidth<=0) return;
        var p=Root.TranslatePoint(point,CategoryStrip);
        double delta=p.X<22?-8:p.X>CategoryStrip.ActualWidth-22?8:0;
        if(delta!=0) CategoryStrip.ScrollToHorizontalOffset(CategoryStrip.HorizontalOffset+delta);
    }
    private void RevealSelectedCategory()
    {
        CategoryTabs.UpdateLayout();
        if(_activeTab==null || CategoryTabs.ItemContainerGenerator.ContainerFromItem(_activeTab) is not FrameworkElement row) return;
        double left=row.TranslatePoint(new Point(),CategoryStrip).X;
        if(left<0) CategoryStrip.ScrollToHorizontalOffset(CategoryStrip.HorizontalOffset+left);
        else if(left+row.ActualWidth>CategoryStrip.ViewportWidth)
            CategoryStrip.ScrollToHorizontalOffset(CategoryStrip.HorizontalOffset+left+row.ActualWidth-CategoryStrip.ViewportWidth);
    }
}
