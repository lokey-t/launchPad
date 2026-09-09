using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Linq;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad
{
    public partial class MainWindow
    {
        private readonly ScaleTransform _folderScale = new();
        private readonly TranslateTransform _folderTranslation = new();
        private int _folderTransitionVersion;
        private bool _folderClosing;
        private Rect _folderOrigin;
        private sealed class MovingIcon
        {
            public Border Glyph;
            public Panel Parent;
            public Border Placeholder;
            public Rect Small, Large;
            public ScaleTransform Scale;
            public TranslateTransform Position;
        }
        private readonly List<MovingIcon> _movingIcons = new();
        private readonly List<ScaleTransform> _labelScales = new();
        private RectangleGeometry _morphClip;

        // Move the actual glyph elements to a temporary render layer. No screenshots or duplicate icons.
        private void PrepareMovingIcons(bool opening)
        {
            if (_movingIcons.Count != 0) return;
            FolderOverlay.UpdateLayout();
            var small = GetFolderOrigin();
            var expanded = new Rect(FolderCard.Margin.Left, FolderCard.Margin.Top, FolderCard.Width, FolderCard.Height);
            _morphClip = new RectangleGeometry(opening ? small : expanded, 12, 12);
            FolderMorphLayer.Clip = _morphClip;
            var thumbs = FindInContainer(_openFolderContainer, "FItems") as ItemsControl;
            // 缩略格尺寸随图标档位变化，第 5 项及以后从 2×2 预览块下方逐格浮现
            double cell = ThumbCell, innerSize = ThumbInner, cellPad = (cell - innerSize) / 2.0;
            Point blockOrigin = thumbs != null
                ? thumbs.TranslatePoint(new Point(), Root)
                : new Point(small.X + (small.Width - 2.0 * cell) / 2.0, small.Y + (small.Height - 2.0 * cell) / 2.0);
            for (int i = 0; i < FolderItems.Items.Count; i++)
            {
                var container = FolderItems.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                var glyph = FindInContainer(container, "FolderGlyph") as Border;
                if (glyph?.Parent is not Panel parent) continue;
                var point = glyph.TranslatePoint(new Point(), FolderCard);
                var large = new Rect(point.X + expanded.X, point.Y + expanded.Y, glyph.ActualWidth, glyph.ActualHeight);
                // Entries beyond the 2x2 preview emerge from below the card's clipping boundary.
                // i<4 用实际缩略图位置；i>=4 从格子底边下方逐格浮现，确保被裁剪不可见
                double originY = i < 4
                    ? blockOrigin.Y + (i / 2) * cell + cellPad
                    : small.Bottom + (i / 2 - 2) * cell;
                var origin = new Rect(blockOrigin.X + (i % 2) * cell + cellPad,
                    originY, innerSize, innerSize);
                if (i < 4 && thumbs?.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement thumbContainer &&
                    FindInContainer(thumbContainer, "ThumbGlyph") is FrameworkElement thumb)
                    origin = new Rect(thumb.TranslatePoint(new Point(), Root), thumb.RenderSize);
                var placeholder = new Border { Width = glyph.ActualWidth, Height = glyph.ActualHeight };
                int index = parent.Children.IndexOf(glyph);
                glyph.DataContext = glyph.DataContext;
                glyph.Opacity = glyph.DataContext == _dragEntry ? 0 : 1;
                parent.Children.RemoveAt(index);
                parent.Children.Insert(index, placeholder);
                var initial = opening ? origin : large;
                var scale = new ScaleTransform(initial.Width / large.Width, initial.Height / large.Height);
                var position = new TranslateTransform(initial.X, initial.Y);
                var transform = new TransformGroup();
                transform.Children.Add(scale); transform.Children.Add(position);
                glyph.RenderTransform = transform;
                FolderMorphLayer.Children.Add(glyph);
                _movingIcons.Add(new MovingIcon { Glyph = glyph, Parent = parent, Placeholder = placeholder,
                    Small = origin, Large = large, Scale = scale, Position = position });
                foreach (var label in parent.Children.OfType<TextBlock>())
                {
                    var labelScale = new ScaleTransform(opening ? 0 : 1, opening ? 0 : 1);
                    label.RenderTransformOrigin = new Point(.5, 0);
                    label.RenderTransform = labelScale;
                    _labelScales.Add(labelScale);
                }
            }
            var titleScale = new ScaleTransform(opening ? 0 : 1, opening ? 0 : 1);
            FolderTitle.RenderTransformOrigin = new Point(.5, .5);
            FolderTitle.RenderTransform = titleScale;
            _labelScales.Add(titleScale);
        }

        private void AnimateMovingIcons(bool opening, double ms, IEasingFunction easing)
        {
            foreach (var icon in _movingIcons)
            {
                var rect = opening ? icon.Large : icon.Small;
                AnimateValue(icon.Scale, ScaleTransform.ScaleXProperty, rect.Width / icon.Large.Width, ms, easing);
                AnimateValue(icon.Scale, ScaleTransform.ScaleYProperty, rect.Height / icon.Large.Height, ms, easing);
                AnimateValue(icon.Position, TranslateTransform.XProperty, rect.X, ms, easing);
                AnimateValue(icon.Position, TranslateTransform.YProperty, rect.Y, ms, easing);
            }
            foreach (var scale in _labelScales)
            {
                AnimateValue(scale, ScaleTransform.ScaleXProperty, opening ? 1 : 0, ms, easing);
                AnimateValue(scale, ScaleTransform.ScaleYProperty, opening ? 1 : 0, ms, easing);
            }
            if (_morphClip != null)
            {
                var rect = opening ? new Rect(FolderCard.Margin.Left, FolderCard.Margin.Top, FolderCard.Width, FolderCard.Height) : GetFolderOrigin();
                var current = _morphClip.Rect;
                _morphClip.BeginAnimation(RectangleGeometry.RectProperty, null);
                _morphClip.Rect = current;
                _morphClip.BeginAnimation(RectangleGeometry.RectProperty,
                    new RectAnimation(current, rect, TimeSpan.FromMilliseconds(ms)) { EasingFunction = easing });
            }
        }

        private void RestoreMovingIcons()
        {
            foreach (var icon in _movingIcons)
            {
                FolderMorphLayer.Children.Remove(icon.Glyph);
                icon.Glyph.RenderTransform = Transform.Identity;
                icon.Glyph.Opacity = 1;
                int index = icon.Parent.Children.IndexOf(icon.Placeholder);
                if (index >= 0)
                {
                    icon.Parent.Children.RemoveAt(index);
                    icon.Parent.Children.Insert(index, icon.Glyph);
                    icon.Glyph.ClearValue(FrameworkElement.DataContextProperty);
                }
            }
            _movingIcons.Clear();
            _labelScales.Clear();
            FolderMorphLayer.Clip = null;
            _morphClip = null;
        }

        private void OpenFolder(AppFolder folder)
        {
            if (IsFolderOpen) HideFolderNow();
            _openFolder = folder;
            _openFolderContainer = IconGrid.ItemContainerGenerator.ContainerFromItem(folder) as FrameworkElement;
            FolderTitle.Text = folder.Name;
            FolderTitle.FontSize = 17;
            FolderTitle.Opacity = 1;
            FolderNameBottom.Opacity = FolderBadge.Opacity = 0;
            FolderRenameBox.Visibility = Visibility.Collapsed;
            FolderItems.ItemsSource = folder.Items;
            _currentCols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(folder.Items.Count)), 2, 6);
            SetFolderGridPanel(_currentCols);
            SetIconSize(78, 96, 4, 50, 15, 11);
            int rows = Math.Max(1, (int)Math.Ceiling(folder.Items.Count / (double)_currentCols));
            FolderCard.Width = Math.Max(220, _currentCols * 86 + 38);
            FolderCard.Height = Math.Min(428, 70 + rows * 104);
            FolderCard.Padding = new Thickness(18, 16, 18, 18);
            FolderCard.CornerRadius = new CornerRadius(22);
            FolderCard.BorderThickness = new Thickness(1);
            FolderCard.Margin = new Thickness((Root.ActualWidth - FolderCard.Width) / 2,
                (Root.ActualHeight - FolderCard.Height) / 2, 0, 0);
            FolderScroll.Margin = new Thickness(0, 34, 0, 0);
            FolderScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            FolderScroll.ScrollToTop();
            var transforms = new TransformGroup();
            transforms.Children.Add(_folderScale);
            transforms.Children.Add(_folderTranslation);
            FolderCard.RenderTransform = transforms;
            _folderOrigin = GetFolderOrigin();
            SetMotion(_folderOrigin);
            FolderCard.Opacity = 1;
            FolderBackdrop.Opacity = 0;
            FolderOverlay.Visibility = Visibility.Visible;
            FolderOverlay.IsHitTestVisible = true;
            FolderCard.IsHitTestVisible = false;
            Root.UpdateLayout(); // Generate the previously collapsed content before moving its glyphs.
            FolderItems.ApplyTemplate();
            FolderOverlay.Measure(new Size(Root.ActualWidth, Root.ActualHeight));
            FolderOverlay.Arrange(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
            FolderItems.UpdateLayout();
            if (_openFolderContainer != null)
            {
                _openFolderContainer.Opacity = 0;
                _openFolderContainer.IsHitTestVisible = false;
            }
            PrepareMovingIcons(true);
            AnimateFolder(true);
        }

        private Rect GetFolderOrigin()
        {
            var body = FindInContainer(_openFolderContainer, "FBody");
            if (body != null && body.IsLoaded && body.ActualWidth > 0 && body.ActualHeight > 0)
            {
                try { return new Rect(body.TranslatePoint(new Point(), Root), body.RenderSize); }
                catch (InvalidOperationException) { }
            }
            return _folderOrigin.Width > 0 ? _folderOrigin :
                new Rect(FolderCard.Margin.Left, FolderCard.Margin.Top, 92, 112);
        }

        private void SetMotion(Rect rect)
        {
            _folderScale.ScaleX = rect.Width / FolderCard.Width;
            _folderScale.ScaleY = rect.Height / FolderCard.Height;
            _folderTranslation.X = rect.X - FolderCard.Margin.Left;
            _folderTranslation.Y = rect.Y - FolderCard.Margin.Top;
        }

        private void AnimateFolder(bool opening)
        {
            int version = ++_folderTransitionVersion;
            _folderClosing = !opening;
            double ms = MotionService.Duration(_app.Config, opening ? 380 : 290);
            var target = opening
                ? new Rect(FolderCard.Margin.Left, FolderCard.Margin.Top, FolderCard.Width, FolderCard.Height)
                : GetFolderOrigin();
            if (ms == 0)
            {
                SettleFolderForDrop();
                FolderBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
                FolderBackdrop.Opacity = opening ? 1 : 0;
                if (opening) CacheFolderGridGeometry(); else FinishCloseFolder();
                return;
            }
            var easing = MotionService.Easing(_app.Config);
            AnimateValue(_folderScale, ScaleTransform.ScaleXProperty, target.Width / FolderCard.Width, ms, easing);
            AnimateValue(_folderScale, ScaleTransform.ScaleYProperty, target.Height / FolderCard.Height, ms, easing);
            AnimateValue(_folderTranslation, TranslateTransform.XProperty, target.X - FolderCard.Margin.Left, ms, easing);

            AnimateMovingIcons(opening, ms, easing);
            AnimateValue(FolderBackdrop, UIElement.OpacityProperty, opening ? 1 : 0, ms, easing);
            AnimateValue(_folderTranslation, TranslateTransform.YProperty, target.Y - FolderCard.Margin.Top, ms, easing, () =>
            {
                if (version != _folderTransitionVersion) return;
                if (opening)
                {
                    RestoreMovingIcons();
                    FolderCard.IsHitTestVisible = true;
                    CacheFolderGridGeometry();
                }
                else FinishCloseFolder();
            });
        }

        private static void AnimateValue(DependencyObject target, DependencyProperty property, double value,
            double ms, IEasingFunction easing, Action completed = null)
        {
            double current = (double)target.GetValue(property);
            if (target is UIElement element) element.BeginAnimation(property, null);
            else ((Animatable)target).BeginAnimation(property, null);
            target.SetValue(property, current);
            var animation = new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = easing, FillBehavior = FillBehavior.HoldEnd };
            if (completed != null) animation.Completed += (_, _) => completed();
            if (target is UIElement visual) visual.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
            else ((Animatable)target).BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        }

        private void CloseFolderWithAnim(Action done = null)
        {
            if (!IsFolderOpen || _openFolder == null) { done?.Invoke(); return; }
            _closeDone += done;
            if (_folderClosing) return;
            if (FolderCard.IsHitTestVisible)
            {
                _openFolder.RefreshThumb();
                _openFolderContainer?.UpdateLayout();
                PrepareMovingIcons(false);
            }
            FolderCard.IsHitTestVisible = false;
            AnimateFolder(false);
        }

        private void FinishCloseFolder() => HideFolderNow();

        private void SettleFolderForDrop()
        {
            ++_folderTransitionVersion;
            _folderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _folderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _folderTranslation.BeginAnimation(TranslateTransform.XProperty, null);
            _folderTranslation.BeginAnimation(TranslateTransform.YProperty, null);
            _folderScale.ScaleX = _folderScale.ScaleY = 1;
            _folderTranslation.X = _folderTranslation.Y = 0;
            foreach (var scale in _labelScales)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = 1;
            }
            RestoreMovingIcons();
            FolderCard.IsHitTestVisible = true;
        }

        private void HideFolderNow()
        {
            ++_folderTransitionVersion;
            _folderClosing = false;
            _folderScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _folderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _folderTranslation.BeginAnimation(TranslateTransform.XProperty, null);
            _folderTranslation.BeginAnimation(TranslateTransform.YProperty, null);
            FolderCard.BeginAnimation(UIElement.OpacityProperty, null);
            RestoreMovingIcons();
            FolderBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
            FolderOverlay.Visibility = Visibility.Collapsed;
            FolderOverlay.IsHitTestVisible = false;

            if (_openFolderContainer != null)
            {
                _openFolderContainer.Opacity = 1;
                _openFolderContainer.IsHitTestVisible = true;
            }
            _openFolder = null;
            _openFolderContainer = null;
            ApplyTileIconSizes();
            Action done = _closeDone;
            _closeDone = null;
            done?.Invoke();
        }

        private void FolderOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsFolderOpen) return;
            var point = e.GetPosition(FolderCard);
            if (!new Rect(FolderCard.RenderSize).Contains(point))
            {
                CloseFolderWithAnim();
                e.Handled = true;
            }
        }
private void CacheFolderGridGeometry()
		{
			if (_openFolder == null)
			{
				return;
			}
			_folderContainers = new Dictionary<AppItem, FrameworkElement>();
			_folderLayoutIdx = new Dictionary<AppItem, int>();
			for (int ci = 0; ci < _openFolder.Items.Count; ci++)
			{
				if (_openFolder.Items[ci] is AppItem ai &&
					FolderItems.ItemContainerGenerator.ContainerFromItem(ai) is FrameworkElement cf)
				{
					_folderContainers[ai] = cf;
					_folderLayoutIdx[ai] = FolderItems.ItemContainerGenerator.IndexFromContainer(cf);
				}
			}
			_folderCellW = 86.0;
			_folderCellH = 110.0;
			if (FolderItems.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement f0c && f0c.ActualWidth > 0.0)
			{
				_folderCellW = f0c.ActualWidth;
				_folderCellH = f0c.ActualHeight;
				_folderGridOrigin = f0c.TranslatePoint(new System.Windows.Point(0.0, 0.0), FolderItems);
			}
		}

    }
}
