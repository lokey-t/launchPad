using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LaunchPad.Models;
using LaunchPad.Services;
namespace LaunchPad;
public partial class SettingsWindow
{
    private AppCategory _sortCategory;
    private Point _sortStart;
    private ObservableCollection<AppCategory> _categoryPreview;
    private DispatcherTimer _categorySortTimer;
    private void InitializeCategorySorting()
    {
        CategoryList.LostMouseCapture+=(_,e)=> { if(e.OriginalSource==CategoryList && _sortCategory!=null) FinishCategorySort(false); };
        PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape && _sortCategory!=null) { FinishCategorySort(false); e.Handled=true; } };
        IsVisibleChanged+=(_,_)=> { if(!IsVisible) FinishCategorySort(false); };
        _categorySortTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(40) };
        _categorySortTimer.Tick+=(_,_)=>
        {
            var point=Mouse.GetPosition(CategoryList);
            var scroll=Descendants(CategoryList).OfType<ScrollViewer>().FirstOrDefault();
            if(scroll!=null)
            {
                if(point.Y<28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset-12);
                else if(point.Y>CategoryList.ActualHeight-28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset+12);
            }
            UpdateCategorySort(point);
        };
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
        {
            var child=VisualTreeHelper.GetChild(node,i); yield return child;
            foreach(var nested in Descendants(child)) yield return nested;
        }
    }
    private void CategorySort_Down(object sender,MouseButtonEventArgs e)
    {
        for(var node=e.OriginalSource as DependencyObject;node!=null && node!=CategoryList;node=VisualTreeHelper.GetParent(node))
        {
            if(node is not ListBoxItem row || row.DataContext is not AppCategory category) continue;
            _sortCategory=category; _sortStart=e.GetPosition(CategoryList); CategoryList.SelectedItem=category;
            Mouse.Capture(CategoryList); e.Handled=true; break;
        }
    }
    private void CategorySort_Move(object sender,MouseEventArgs e)
    {
        if(_sortCategory==null) return;
        if(e.LeftButton!=MouseButtonState.Pressed) { FinishCategorySort(false); return; }
        var point=e.GetPosition(CategoryList);
        if(_categoryPreview==null && Math.Abs(point.Y-_sortStart.Y)<6) return;
        if(_categoryPreview==null)
        {
            _categoryPreview=new ObservableCollection<AppCategory>(_app.Config.Categories);
            CategoryList.ItemsSource=_categoryPreview; CategoryList.UpdateLayout();
            CategoryList.Cursor=Cursors.SizeNS; _categorySortTimer.Start();
        }
        UpdateCategorySort(point); e.Handled=true;
    }
    private void UpdateCategorySort(Point point)
    {
        if(_categoryPreview==null || _sortCategory==null) return;
        int target=_categoryPreview.Count-1;
        for(int i=0;i<_categoryPreview.Count;i++)
        {
            if(CategoryList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) continue;
            double top=row.TranslatePoint(new Point(),CategoryList).Y-(row.RenderTransform is TranslateTransform tt?tt.Y:0);
            if(point.Y<top+row.ActualHeight) { target=i; break; }
        }
        PreviewCategoryOrder(target);
    }
    private void PreviewCategoryOrder(int target)
    {
        int old=_categoryPreview.IndexOf(_sortCategory); target=Math.Clamp(target,0,_categoryPreview.Count-1);
        if(old==target) return;
        var positions=new Dictionary<AppCategory,Point>();
        foreach(var category in _categoryPreview)
            if(CategoryList.ItemContainerGenerator.ContainerFromItem(category) is FrameworkElement row) positions[category]=row.TranslatePoint(new Point(),CategoryList);
        _categoryPreview.Move(old,target);
        foreach(var category in _categoryPreview)
            if(CategoryList.ItemContainerGenerator.ContainerFromItem(category) is FrameworkElement row) row.RenderTransform=Transform.Identity;
        CategoryList.UpdateLayout();
        foreach(var category in _categoryPreview)
        {
            if(CategoryList.ItemContainerGenerator.ContainerFromItem(category) is not FrameworkElement row || !positions.TryGetValue(category,out var before)) continue;
            var move=new TranslateTransform(0,before.Y-row.TranslatePoint(new Point(),CategoryList).Y);
            row.RenderTransform=move;
            move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(move.Y,0,TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config,170))) { EasingFunction=MotionService.Easing(_app.Config) });
        }
        CategoryList.SelectedItem=_sortCategory;
    }
    private void CategorySort_Up(object sender,MouseButtonEventArgs e)
    {
        if(_sortCategory==null) return;
        var point=e.GetPosition(CategoryList);
        FinishCategorySort(point.X>=0 && point.X<=CategoryList.ActualWidth && point.Y>=0 && point.Y<=CategoryList.ActualHeight); e.Handled=true;
    }
    private void FinishCategorySort(bool commit)
    {
        _categorySortTimer?.Stop();
        var selected=_sortCategory; _sortCategory=null;
        if(commit && _categoryPreview!=null)
        {
            for(int i=0;i<_categoryPreview.Count;i++)
            {
                int old=_app.Config.Categories.IndexOf(_categoryPreview[i]);
                if(old!=i) _app.Config.Categories.Move(old,i);
            }
            _app.RefreshMainWindow();
        }
        _categoryPreview=null; CategoryList.ItemsSource=_app.Config.Categories; CategoryList.UpdateLayout();
        foreach(var category in _app.Config.Categories)
            if(CategoryList.ItemContainerGenerator.ContainerFromItem(category) is FrameworkElement row) row.RenderTransform=Transform.Identity;
        CategoryList.SelectedItem=selected; CategoryList.Cursor=null;
        if(Mouse.Captured==CategoryList) Mouse.Capture(null);
    }
}
