using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad;

public partial class MainWindow
{
    private CategoryTab _dwellTab;
    private AppEntry _dwellEntry;
    private long _tabDwellStart, _entryDwellStart;
    private Point _dragPoint;
    private bool _externalDragging, _dragEnteredFolder;
    private AppFolder _dragOriginFolder;
    private Border _dropMarker;
    private AppFolder _dropFolder;
    private AppItem _dropMergeApp;
    private AppCategory _dropCategory; // 在分类 Tab 上松开时的目标分类覆盖（优先于 _activeTab.Category）
    private int _dropIndex;
    private bool _validDrop;
    private CategoryTab _pendingCategory;
    private ObservableCollection<AppEntry> _dragPreview;
    // 拖入“已展开文件夹”时的实时让位预览（仅内部拖动；外部文件无 AppItem 不预览）
    private ObservableCollection<AppItem> _folderDragPreview;
    private bool _changingCapture;

    private void InitializeDragRouting()
    {
        Root.PreviewMouseMove += (_, e) =>
        {
            if (!_dragging) return;
            TrackDragAt(e.GetPosition(Root), Environment.TickCount64);
            e.Handled = true;
        };
        Root.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            TrackDragAt(e.GetPosition(Root), Environment.TickCount64);
            CompleteInternalDrop();
            e.Handled = true;
        };
        Root.LostMouseCapture += (_, e) =>
        {
            // A child losing capture while we transfer it to Root is not a cancelled drag.
            if (e.OriginalSource == Root && !_changingCapture && _dragging && Mouse.Captured != Root)
                EndDragSession();
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible && _dragging) EndDragSession(); };
    }

    private void BeginDragSession()
    {
        _dwellTab = null;
        _dwellEntry = null;
        _dragEnteredFolder = false;
        _validDrop = false;
        _changingCapture = true;
        try { Mouse.Capture(Root); }
        finally { _changingCapture = false; }
        TrackDragAt(Mouse.GetPosition(Root), Environment.TickCount64);
    }

    private void TrackDragAt(Point point, long now)
    {
        _dragPoint = point;
        _validDrop = false;
        _dropFolder = null;
        _dropMergeApp = null;
        _dropCategory = null;
        if (_dragGhost != null)
        {
            Canvas.SetLeft(_dragGhost, point.X - _dragGhost.Width / 2);
            Canvas.SetTop(_dragGhost, point.Y - _dragGhost.Height / 2);
        }
        HideDropMarker();
        ScrollCategoryDragEdge(point);
        var tab = Contains(CategoryStrip,point) ? Tabs.FirstOrDefault(t => CategoryTabs.ItemContainerGenerator.ContainerFromItem(t) is FrameworkElement c && Contains(c, point)) : null;
        if (tab != null && tab != _activeTab)
        {
            ResetDragPreview();
            ResetFolderPreview();
            _dwellEntry = null;
            // 在具体分类 Tab 上松开即可落位到该分类（无需等待停留切换），避免移动失败留在原分类
            if (!tab.IsAll && _dragEntry != null)
            {
                _dropFolder = null;
                _dropMergeApp = null;
                _dropCategory = tab.Category;
                _dropIndex = tab.Category.Entries.Count;
                _validDrop = true;
            }
            if (_dwellTab != tab) { _dwellTab = tab; _tabDwellStart = now; FlashStatus($"停留 {_app.Config.CategoryHoverSeconds:0.#} 秒切换到“{tab.Name}”"); }
            if (now - _tabDwellStart >= _app.Config.CategoryHoverSeconds * 1000)
            {
                if (IsFolderOpen) HideFolderNow();
                SelectTab(tab);
                _dwellTab = null;
                FlashStatus("已切换分类，移动到目标位置后松开");
            }
            return;
        }
        _dwellTab = null;
        if (tab != null)
        {
            ResetDragPreview();
            ResetFolderPreview();
            // 在具体分类 Tab 上松开时直接落位到该分类：避免 Tab 切换后光标仍停在 Tab 上，
            // 最后一帧 TrackDragAt 把 _validDrop 重置为 false 导致移动失败、图标留在原分类。
            if (!tab.IsAll && _dragEntry != null)
            {
                _dropFolder = null;
                _dropMergeApp = null;
                _dropCategory = tab.Category;
                _dropIndex = tab.Category.Entries.Count;
                _validDrop = true;
            }
            return;
        }
        if (IsFolderOpen && !_folderClosing)
        {
            _dwellEntry = null;
            if (Contains(FolderCard, point))
            {
                _dragEnteredFolder = true;
                if (_dragEntry is AppFolder) return;
                _dropFolder = _openFolder;
                if (_movingIcons.Count > 0)
                {
                    _dropIndex = _openFolder.Items.Count;
                }
                else
                {
                    _dropIndex = InsertionAt(FolderItems, point);
                    // 内部应用拖入已展开文件夹：其他图标实时平滑让位（排序动画）
                    if (_dragEntry is AppItem incoming && !_openFolder.Items.Contains(incoming))
                        PreviewFolderPlacement(_dropIndex);
                    else
                        ResetFolderPreview();
                }
                _validDrop = true;
                AutoScroll(FolderScroll, point);
            }
            else if (_dragEnteredFolder)
            {
                ResetFolderPreview();
                CloseFolderWithAnim();
                _dragEnteredFolder = false;
            }
            return;
        }
        if (!Contains(GridScroll, point)) { _dwellEntry = null; ResetDragPreview(); return; }
        AutoScroll(GridScroll, point);
        var local = Root.TranslatePoint(point, IconGrid);
        var entry = FindEntry(IconGrid.InputHitTest(local) as DependencyObject);
        if (entry == _dragEntry) entry = null;
        if (_dwellEntry != entry) { _dwellEntry = entry; _entryDwellStart = now; }
        _dropIndex = InsertionAt(IconGrid, point);
        _validDrop = true;
        bool center = entry != null && IsMergeZone(local, entry);
        if (center && entry is AppFolder folder && _dragEntry is not AppFolder)
        {
            _dropFolder = folder;
            _dropIndex = folder.Items.Count;
            ShowTargetMarker(entry);
            if (now - _entryDwellStart >= _app.Config.FolderHoverSeconds * 1000 && !IsFolderOpen)
            {
                OpenFolder(folder);
                _dragEnteredFolder = false;
                FlashStatus("文件夹已展开，移动到插入位置后松开");
            }
            return;
        }
        if (center && entry is AppItem app && _dragEntry is AppItem && now - _entryDwellStart >= 400)
        {
            _dropMergeApp = app;
            ShowTargetMarker(entry);
            return;
        }
        if (!center) _entryDwellStart = now;
        PreviewGridPlacement(_dropIndex);
        ShowDropMarker(IconGrid, _dragPreview != null ? _dragPreview.IndexOf(_dragEntry) : _dropIndex);
    }

    private bool Contains(FrameworkElement element, Point point)
    {
        if (element == null || !element.IsVisible) return false;
        var local = Root.TranslatePoint(point, element);
        return new Rect(element.RenderSize).Contains(local);
    }

    private int InsertionAt(ItemsControl control, Point point)
    {
        for (int i = 0; i < control.Items.Count; i++)
        {
            if (control.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var p = c.TranslatePoint(new Point(), Root);
            if (point.Y < p.Y) return i;
            if (point.Y <= p.Y + c.ActualHeight && point.X < p.X + c.ActualWidth / 2) return i;
        }
        return control.Items.Count;
    }

    private void AutoScroll(ScrollViewer scroll, Point point)
    {
        var p = Root.TranslatePoint(point, scroll);
        if (p.Y < 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 12);
        else if (p.Y > scroll.ActualHeight - 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 12);
    }

    private void EnsureDropMarker()
    {
        if (_dropMarker != null) return;
        _dropMarker = new Border { BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(7), IsHitTestVisible = false };
        _dropMarker.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        DragLayer.Children.Add(_dropMarker);
    }

    private void HideDropMarker() { if (_dropMarker != null) _dropMarker.Visibility = Visibility.Collapsed; }

    private void ShowTargetMarker(AppEntry entry)
    {
        if (IconGrid.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement c) return;
        SetDropMarker(c.TranslatePoint(new Point(), DragLayer), c.ActualWidth, c.ActualHeight);
    }

    private void ShowDropMarker(ItemsControl control, int index) { /* Live placement preview replaces the insertion bar. */ }

    private void SetDropMarker(Point p, double width, double height)
    {
        EnsureDropMarker();
        Canvas.SetLeft(_dropMarker, p.X); Canvas.SetTop(_dropMarker, p.Y);
        _dropMarker.Width = width; _dropMarker.Height = height;
        _dropMarker.Visibility = Visibility.Visible;
    }

    private void PromoteFolderDrag(Point point)
    {
        var item = _folderDragItem;
        if (item == null) return;
        _dragOriginFolder = _openFolder;
        ShowFolderDragVisual(item);
        if (_folderGhost != null)
        {
            FolderDragLayer.Children.Remove(_folderGhost);
            DragLayer.Children.Remove(_folderGhost);
            _folderGhost = null;
        }
        _folderDragItem = null;
        _folderDragging = false;
        _folderLiveReordered = false;
        _pressedEntry = item;
        _pressed = true;
        _dragSourceList = _activeList;
        StartDrag();
        CloseFolderWithAnim();
        TrackDragAt(point, Environment.TickCount64);
    }

    private bool PlaceEntry(AppEntry entry)
    {
        if (!_validDrop) return false;
        if (_dropFolder != null)
        {
            bool placed = EntryMoveService.IntoFolder(_app.Config, entry, _dropFolder, _dropIndex);
            if (placed) _dropIndex++;
            return placed;
        }
        if (_dropMergeApp is AppItem target && entry is AppItem && target != entry)
        {
            var owner = EntryMoveService.Owner(_app.Config, target);
            if (owner == null) return false;
            var folder = new AppFolder { Name = target.Name + " 等" };
            owner.Entries.Insert(owner.Entries.IndexOf(target), folder);
            int global = _app.Config.GlobalOrder.IndexOf(target.Id);
            if (global >= 0) _app.Config.GlobalOrder[global] = folder.Id;
            EntryMoveService.IntoFolder(_app.Config, target, folder, 0);
            return EntryMoveService.IntoFolder(_app.Config, entry, folder, 1);
        }
        var category = _dropCategory ?? _activeTab?.Category ?? EntryMoveService.Owner(_app.Config, entry) ?? GetImportCategory();
        int index = _dropIndex;
        if (_activeTab?.IsAll == true)
        {
            EntryMoveService.NormalizeOrder(_app.Config);
            var visibleBefore = _app.Config.GlobalOrder.Take(_dropIndex).ToHashSet();
            index = category.Entries.Count(e => visibleBefore.Contains(e.Id));
        }
        bool moved = EntryMoveService.IntoCategory(_app.Config, entry, category, index,
            _activeTab?.IsAll == true ? _dropIndex : null);
        if (moved) _dropIndex++;
        return moved;
    }

    private void CompleteInternalDrop()
    {
        // Finish the visual transition before changing the list that owns its glyph elements.
        var reopen = !_folderClosing ? _openFolder : null;
        if (reopen != null) SettleFolderForDrop();
        else if (IsFolderOpen) HideFolderNow();
        bool changed = PlaceEntry(_dragEntry);
        EndDragSession();
        if (changed)
        {
            _app.SaveConfig();
            RefreshDropViews(reopen);
            UpdateStatus();
        }
    }

    private void EndDragSession()
    {
        ResetDragPreview();
        ResetFolderPreview();
        _dragging = false;
        _externalDragging = false;
        _hoverTimer.Stop();
        foreach (var entry in _app.Config.Categories.SelectMany(c => c.Entries))
            if (IconGrid.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement c) c.Opacity = entry == _openFolder ? 0 : 1;
        if (_dragOriginFolder != null) _dragOriginFolder.RefreshThumb();
        _dragOriginFolder = null;
        foreach (UIElement visual in new UIElement[] { _dragGhost, _highlight, _insertHint, _dropMarker })
            if (visual != null) DragLayer.Children.Remove(visual);
        _dragGhost = null; _highlight = null; _insertHint = null; _dropMarker = null;
        _dragEntry = null; _pressedEntry = null; _pressed = false; _dragSourceList = null;
        _dropCategory = null;
        _dwellTab = null; _dwellEntry = null; _validDrop = false;
        DropHint.Visibility = Visibility.Collapsed;
        if (Mouse.Captured == Root) Mouse.Capture(null);
        UpdateStatus();
    }

    private void ExternalDragOver(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        _externalDragging = true;
        _hoverTimer.Start();
        TrackDragAt(e.GetPosition(Root), Environment.TickCount64);
        e.Effects = _validDrop || _dwellTab != null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void ExternalDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        TrackDragAt(e.GetPosition(Root), Environment.TickCount64);
        var reopen = !_folderClosing ? _openFolder : null;
        if (reopen != null) SettleFolderForDrop();
        else if (IsFolderOpen) HideFolderNow();
        int added = 0, skipped = 0;
        foreach (string path in (string[])e.Data.GetData(DataFormats.FileDrop))
        {
            var temporary = new AppCategory();
            if (AddFile(temporary, path) && PlaceEntry(temporary.Entries[0])) added++;
            else skipped++;
        }
        EndDragSession();
        if (added > 0)
        {
            _app.SaveConfig();
            RefreshDropViews(reopen);
        }
        FlashStatus($"已添加 {added} 个，跳过 {skipped} 个重复或无效文件");
        e.Handled = true;
    }

    private void RefreshDropViews(AppFolder open)
    {
        if (_activeTab.IsAll) RebuildAll();
        if (open == null) return;
        RefreshFolderItems();
        Root.UpdateLayout();
        _openFolderContainer = IconGrid.ItemContainerGenerator.ContainerFromItem(open) as FrameworkElement;
        if (_openFolderContainer != null)
        {
            _openFolderContainer.Opacity = 0;
            _openFolderContainer.IsHitTestVisible = false;
        }
        CacheFolderGridGeometry();
    }

    private void ResetDragPreview()
    {
        if (_dragPreview == null) return;
        _dragPreview = null;
        IconGrid.ItemsSource = _activeList;
        IconGrid.UpdateLayout();
        foreach (var entry in _activeList)
            if (IconGrid.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement container)
            {
                container.RenderTransform = Transform.Identity;
                container.Opacity = entry == _openFolder ? 0 : 1;
            }
    }

    private void PreviewGridPlacement(int insertion)
    {
        if (_dragEntry == null || _activeList == null) return;
        if (_dragPreview == null)
        {
            _dragPreview = new ObservableCollection<AppEntry>(_activeList);
            if (!_dragPreview.Contains(_dragEntry)) _dragPreview.Add(_dragEntry);
            IconGrid.ItemsSource = _dragPreview;
            IconGrid.UpdateLayout();
        }
        int old = _dragPreview.IndexOf(_dragEntry);
        int next = Math.Clamp(insertion - (old < insertion ? 1 : 0), 0, _dragPreview.Count - 1);
        if (old != next)
        {
            var positions = new Dictionary<AppEntry, Point>();
            foreach (var entry in _dragPreview)
                if (IconGrid.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement c)
                    positions[entry] = c.TranslatePoint(new Point(), IconGrid);
            _dragPreview.Move(old, next);
            foreach (var entry in _dragPreview)
                if (IconGrid.ItemContainerGenerator.ContainerFromItem(entry) is FrameworkElement c)
                    c.RenderTransform = Transform.Identity;
            IconGrid.UpdateLayout();
            foreach (var entry in _dragPreview)
            {
                if (entry == _dragEntry || !positions.TryGetValue(entry, out var previous) ||
                    IconGrid.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement c) continue;
                var current = c.TranslatePoint(new Point(), IconGrid);
                var move = new TranslateTransform(previous.X - current.X, previous.Y - current.Y);
                c.RenderTransform = move;
                var duration = TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config, 190));
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(move.X, 0, duration) { EasingFunction = MotionService.Easing(_app.Config) });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(move.Y, 0, duration) { EasingFunction = MotionService.Easing(_app.Config) });
            }
        }
        if (IconGrid.ItemContainerGenerator.ContainerFromItem(_dragEntry) is FrameworkElement placeholder)
            placeholder.Opacity = .16;
        // Convert the preview slot back to the pre-removal index expected by the move service.
        int original = _activeList.IndexOf(_dragEntry);
        _dropIndex = next + (original >= 0 && original <= next ? 1 : 0);
    }

    /// <summary>
    /// 拖入已展开文件夹时的实时让位：用“原内容 + 被拖项”的临时集合绑定 FolderItems，
    /// 被拖项以半透明占位，其他图标从旧位置平滑滑动到新位置（与主网格 PreviewGridPlacement 同构）。
    /// </summary>
    private void PreviewFolderPlacement(int insertion)
    {
        if (_openFolder == null || _dragEntry is not AppItem dragged) return;
        if (_folderDragPreview == null)
        {
            _folderDragPreview = new ObservableCollection<AppItem>(_openFolder.Items);
            if (!_folderDragPreview.Contains(dragged)) _folderDragPreview.Add(dragged);
            FolderItems.ItemsSource = _folderDragPreview;
            FolderItems.UpdateLayout();
        }
        int old = _folderDragPreview.IndexOf(dragged);
        int next = Math.Clamp(insertion - (old < insertion ? 1 : 0), 0, _folderDragPreview.Count - 1);
        if (old == next)
        {
            if (FolderItems.ItemContainerGenerator.ContainerFromItem(dragged) is FrameworkElement idle)
                idle.Opacity = .16;
            int original = _openFolder.Items.IndexOf(dragged);
            _dropIndex = next + (original >= 0 && original <= next ? 1 : 0);
            return;
        }
        var positions = new Dictionary<AppItem, Point>();
        foreach (var item in _folderDragPreview)
            if (FolderItems.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement c)
                positions[item] = c.TranslatePoint(new Point(), FolderItems);
        _folderDragPreview.Move(old, next);
        foreach (var item in _folderDragPreview)
            if (FolderItems.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement c)
                c.RenderTransform = Transform.Identity;
        FolderItems.UpdateLayout();
        foreach (var item in _folderDragPreview)
        {
            if (item == dragged || !positions.TryGetValue(item, out var previous) ||
                FolderItems.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement c) continue;
            var current = c.TranslatePoint(new Point(), FolderItems);
            var move = new TranslateTransform(previous.X - current.X, previous.Y - current.Y);
            c.RenderTransform = move;
            var duration = TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config, 190));
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(move.X, 0, duration) { EasingFunction = MotionService.Easing(_app.Config) });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(move.Y, 0, duration) { EasingFunction = MotionService.Easing(_app.Config) });
        }
        if (FolderItems.ItemContainerGenerator.ContainerFromItem(dragged) is FrameworkElement placeholder)
            placeholder.Opacity = .16;
        // 被拖项原本不在文件夹中（original=-1），预览槽位即落位索引
        int originalInFolder = _openFolder.Items.IndexOf(dragged);
        _dropIndex = next + (originalInFolder >= 0 && originalInFolder <= next ? 1 : 0);
    }

    /// <summary>取消文件夹内让位预览，恢复真实 Items 绑定、变换与透明度。</summary>
    private void ResetFolderPreview()
    {
        if (_folderDragPreview == null) return;
        var dragged = _dragEntry as AppItem;
        _folderDragPreview = null;
        if (_openFolder != null)
        {
            FolderItems.ItemsSource = _openFolder.Items;
            FolderItems.UpdateLayout();
            foreach (var item in _openFolder.Items)
            {
                if (item == dragged) continue;
                if (FolderItems.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement c)
                {
                    c.RenderTransform = Transform.Identity;
                    c.Opacity = 1;
                }
            }
        }
    }
}
