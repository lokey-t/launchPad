// ============================================================
// LaunchPad - 主网格拖拽排序与外部拖入 (partial class)
// 从 MainWindow.xaml.cs 按职责拆分，编译结果与原合并版完全等价。
// ============================================================
using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using LaunchPad.Models;
using LaunchPad.Services;
namespace LaunchPad
{
	public partial class MainWindow
	{
		private void StartDrag()
		{
			_liveMoved = false;
			_hoverTarget = null;
			_mergePending = null;
			_hoverTimer.Start();
			_dragging = true;
			_dragEntry = _pressedEntry;
			if (IconGrid.ItemContainerGenerator.ContainerFromItem(_dragEntry) is FrameworkElement frameworkElement)
			{
				frameworkElement.Opacity = 0.35;
			}
			_dragGhost = new Border
			{
				Width = TileWidth + 12.0,
				Height = TileHeight + 8.0,
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
				CornerRadius = new CornerRadius(14.0),
				BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(64, 46, 109, 153)),
				BorderThickness = new Thickness(1.0),
				Opacity = 0.92
			};
			StackPanel stackPanel = new StackPanel
			{
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			Border border = new Border
			{
				Width = IconBox,
				Height = IconBox,
				CornerRadius = new CornerRadius(10.0),
				Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(byte.MaxValue, 245, 244, 241))
			};
			System.Windows.Controls.Image image = new System.Windows.Controls.Image
			{
				Width = IconBox,
				Height = IconBox,
				Stretch = Stretch.Uniform,
				Margin = new Thickness(4.0)
			};
			ImageSource icon = IconService.GetIcon(_dragEntry);
			if (icon != null)
			{
				image.Source = icon;
			}
			border.Child = image;
			TextBlock element = new TextBlock
			{
				Text = _dragEntry.Name,
				FontSize = 11.0,
				Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(byte.MaxValue, 107, 114, 128)),
				Margin = new Thickness(0.0, 5.0, 0.0, 0.0),
				MaxWidth = 92.0,
				TextTrimming = TextTrimming.CharacterEllipsis,
				HorizontalAlignment = System.Windows.HorizontalAlignment.Center
			};
			stackPanel.Children.Add(border);
			stackPanel.Children.Add(element);
			_dragGhost.Child = stackPanel;
			System.Windows.Controls.Panel.SetZIndex(_dragGhost, 200);
			DragLayer.Children.Add(_dragGhost);
			_highlight = new System.Windows.Shapes.Rectangle
			{
				Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(byte.MaxValue, 46, 109, 153)),
				StrokeThickness = 2.0,
				RadiusX = 12.0,
				RadiusY = 12.0,
				Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(31, 163, 213, 232)),
				Visibility = Visibility.Hidden
			};
			System.Windows.Controls.Panel.SetZIndex(_highlight, 150);
			DragLayer.Children.Add(_highlight);
			_insertHint = new System.Windows.Shapes.Rectangle
			{
				Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(byte.MaxValue, 46, 109, 153)),
				StrokeThickness = 2.0,
				RadiusX = 12.0,
				RadiusY = 12.0,
				Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 163, 213, 232)),
				Visibility = Visibility.Hidden
			};
			System.Windows.Controls.Panel.SetZIndex(_insertHint, 150);
			DragLayer.Children.Add(_insertHint);
            BeginDragSession();
		}

        private void CheckHover()
        {
            if (_dragging || _externalDragging)
                TrackDragAt(_dragging ? Mouse.GetPosition(Root) : _dragPoint, Environment.TickCount64);
        }
		private void UpdateDrag(System.Windows.Input.MouseEventArgs e)
		{
			System.Windows.Point position = e.GetPosition(DragLayer);
			Canvas.SetLeft(_dragGhost, position.X - _dragGhost.Width / 2.0);
			Canvas.SetTop(_dragGhost, position.Y - _dragGhost.Height / 2.0);
			System.Windows.Point position2 = e.GetPosition(IconGrid);
			AppEntry appEntry = FindEntry(IconGrid.InputHitTest(position2) as DependencyObject);
			if (appEntry == null || appEntry == _dragEntry)
			{
				_hoverTarget = null;
				_mergePending = null;
				_highlight.Visibility = Visibility.Hidden;
				ShowInsertHint(position2);
				LiveReorder(position2, null);
				return;
			}
			if (_hoverTarget != appEntry)
			{
				_hoverTarget = appEntry;
				_hoverSince = DateTime.Now;
				_mergePending = null;
			}
			if (_mergePending == appEntry)
			{
				_insertHint.Visibility = Visibility.Hidden;
			}
			else if (IsMergeZone(position2, appEntry))
			{
				_insertHint.Visibility = Visibility.Hidden;
				if ((DateTime.Now - _hoverSince).TotalMilliseconds >= 400.0)
				{
					_mergePending = appEntry;
					if (IconGrid.ItemContainerGenerator.ContainerFromItem(appEntry) is FrameworkElement frameworkElement)
					{
						System.Windows.Point point = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), DragLayer);
						Canvas.SetLeft(_highlight, point.X);
						Canvas.SetTop(_highlight, point.Y);
						_highlight.Width = frameworkElement.ActualWidth;
						_highlight.Height = frameworkElement.ActualHeight;
						_highlight.Visibility = Visibility.Visible;
					}
				}
			}
			else
			{
				_highlight.Visibility = Visibility.Hidden;
				if (IconGrid.ItemContainerGenerator.ContainerFromItem(appEntry) is FrameworkElement anchor)
				{
					ShowInsertHintAt(anchor, IsAfterHalf(position2, appEntry));
				}
				else
				{
					ShowInsertHint(position2);
				}
				LiveReorder(position2, appEntry);
			}
		}

		private void LiveReorder(System.Windows.Point pos, AppEntry target)
		{
			ObservableCollection<AppEntry> dragSourceList = _dragSourceList;
			if (dragSourceList == null || _dragEntry == null)
			{
				return;
			}
			int num = dragSourceList.IndexOf(_dragEntry);
			if (num < 0)
			{
				return;
			}
			int num2 = ((target == null || target == _dragEntry) ? ComputeInsertIndex(pos) : (dragSourceList.IndexOf(target) + (IsAfterHalf(pos, target) ? 1 : 0)));
			if (num2 > num)
			{
				num2--;
			}
			if (num2 != num && num2 >= 0 && num2 < dragSourceList.Count)
			{
				CapturePositions();
				dragSourceList.Move(num, num2);
				AnimateToPositions();
				_liveMoved = true;
				if (IconGrid.ItemContainerGenerator.ContainerFromItem(_dragEntry) is FrameworkElement frameworkElement)
				{
					frameworkElement.Opacity = 0.35;
				}
			}
		}

		private void CapturePositions()
		{
			_oldPositions.Clear();
			foreach (object item in (IEnumerable)IconGrid.Items)
			{
				if (item is AppEntry appEntry && IconGrid.ItemContainerGenerator.ContainerFromItem(appEntry) is FrameworkElement frameworkElement)
				{
					_oldPositions[appEntry] = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), IconGrid);
				}
			}
		}

		private void AnimateToPositions()
		{
			base.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
			{
				if (_oldPositions.Count != 0)
				{
					foreach (KeyValuePair<AppEntry, System.Windows.Point> oldPosition in _oldPositions)
					{
						if (IconGrid.ItemContainerGenerator.ContainerFromItem(oldPosition.Key) is FrameworkElement frameworkElement)
						{
							System.Windows.Point point = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), IconGrid);
							double num = oldPosition.Value.X - point.X;
							double num2 = oldPosition.Value.Y - point.Y;
							if (!(Math.Abs(num) < 0.5) || !(Math.Abs(num2) < 0.5))
							{
								TranslateTransform translateTransform = new TranslateTransform(num, num2);
								frameworkElement.RenderTransformOrigin = new System.Windows.Point(0.0, 0.0);
								frameworkElement.RenderTransform = translateTransform;
								QuadraticEase easingFunction = new QuadraticEase();
								translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(num, 0.0, TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config, 210)))
								{
									EasingFunction = easingFunction
								});
								translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(num2, 0.0, TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config, 210)))
								{
									EasingFunction = easingFunction
								});
							}
						}
					}
					_oldPositions.Clear();
				}
			});
		}

		private double RelX(System.Windows.Point pos, FrameworkElement container)
		{
			System.Windows.Point point = container.TranslatePoint(new System.Windows.Point(0.0, 0.0), IconGrid);
			if (container.ActualWidth <= 0.0)
			{
				return 0.5;
			}
			return (pos.X - point.X) / container.ActualWidth;
		}

		private bool IsMergeZone(System.Windows.Point pos, AppEntry target)
		{
			if (!(IconGrid.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement container))
			{
				return false;
			}
			double num = RelX(pos, container);
			if (num >= 0.33)
			{
				return num <= 0.67;
			}
			return false;
		}

		private bool IsAfterHalf(System.Windows.Point pos, AppEntry target)
		{
			if (!(IconGrid.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement container))
			{
				return false;
			}
			return RelX(pos, container) > 0.5;
		}

		private int ComputeInsertIndex(System.Windows.Point pos)
		{
			ObservableCollection<AppEntry> dragSourceList = _dragSourceList;
			if (dragSourceList == null || dragSourceList.Count == 0)
			{
				return 0;
			}
			int result = dragSourceList.Count;
			for (int i = 0; i < dragSourceList.Count; i++)
			{
				if (!(IconGrid.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement frameworkElement))
				{
					continue;
				}
				System.Windows.Point point = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), IconGrid);
				double num = point.Y + frameworkElement.ActualHeight / 2.0;
				double num2 = point.X + frameworkElement.ActualWidth / 2.0;
				if (Math.Abs(pos.Y - num) <= frameworkElement.ActualHeight)
				{
					if (!(pos.X > num2))
					{
						result = i;
						break;
					}
					result = i + 1;
				}
				else if (pos.Y < num)
				{
					result = i;
					break;
				}
			}
			return result;
		}

		private void ShowInsertHint(System.Windows.Point pos)
		{
			ObservableCollection<AppEntry> dragSourceList = _dragSourceList;
			if (dragSourceList == null || dragSourceList.Count == 0 || _insertHint == null)
			{
				if (_insertHint != null)
				{
					_insertHint.Visibility = Visibility.Hidden;
				}
				return;
			}
			int num = ComputeInsertIndex(pos);
			FrameworkElement frameworkElement = null;
			frameworkElement = ((num >= dragSourceList.Count) ? (IconGrid.ItemContainerGenerator.ContainerFromIndex(dragSourceList.Count - 1) as FrameworkElement) : (IconGrid.ItemContainerGenerator.ContainerFromIndex(num) as FrameworkElement));
			if (frameworkElement == null)
			{
				_insertHint.Visibility = Visibility.Hidden;
				return;
			}
			System.Windows.Point point = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), DragLayer);
			double length = point.X;
			double length2 = point.Y;
			if (num >= dragSourceList.Count)
			{
				double num2 = 6.0;
				if (colsOfFirstRow(out var tileW) > 0)
				{
					System.Windows.Point point2 = (IconGrid.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement)?.TranslatePoint(new System.Windows.Point(0.0, 0.0), DragLayer) ?? new System.Windows.Point(0.0, 0.0);
					double actualWidth = IconGrid.ActualWidth;
					if (point.X + frameworkElement.ActualWidth + num2 + tileW <= actualWidth)
					{
						length = point.X + frameworkElement.ActualWidth + num2;
						length2 = point.Y;
					}
					else
					{
						length = point2.X;
						length2 = point.Y + frameworkElement.ActualHeight + num2;
					}
				}
			}
			Canvas.SetLeft(_insertHint, length);
			Canvas.SetTop(_insertHint, length2);
			_insertHint.Width = frameworkElement.ActualWidth;
			_insertHint.Height = frameworkElement.ActualHeight;
			_insertHint.Visibility = Visibility.Visible;
		}

		private int colsOfFirstRow(out double tileW)
		{
			tileW = 0.0;
			if (_dragSourceList == null || _dragSourceList.Count == 0)
			{
				return 0;
			}
			if (!(IconGrid.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement frameworkElement))
			{
				return 0;
			}
			tileW = frameworkElement.ActualWidth;
			double y = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), IconGrid).Y;
			int num = 1;
			for (int i = 1; i < _dragSourceList.Count && IconGrid.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement frameworkElement2; i++)
			{
				if (!(Math.Abs(frameworkElement2.TranslatePoint(new System.Windows.Point(0.0, 0.0), IconGrid).Y - y) < 1.0))
				{
					break;
				}
				num++;
			}
			return num;
		}

		private void ShowInsertHintAt(FrameworkElement anchor, bool after)
		{
			if (anchor == null || _insertHint == null)
			{
				return;
			}
			System.Windows.Point point = anchor.TranslatePoint(new System.Windows.Point(0.0, 0.0), DragLayer);
			double num = point.X;
			double length = point.Y;
			if (after)
			{
				num += anchor.ActualWidth + 6.0;
				FrameworkElement frameworkElement = ((_dragSourceList != null && _dragSourceList.Count > 0) ? (IconGrid.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement) : null);
				if (frameworkElement != null)
				{
					System.Windows.Point point2 = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), DragLayer);
					if (num + anchor.ActualWidth > IconGrid.ActualWidth)
					{
						num = point2.X;
						length = point.Y + anchor.ActualHeight + 6.0;
					}
				}
			}
			Canvas.SetLeft(_insertHint, num);
			Canvas.SetTop(_insertHint, length);
			_insertHint.Width = anchor.ActualWidth;
			_insertHint.Height = anchor.ActualHeight;
			_insertHint.Visibility = Visibility.Visible;
		}

		private void FinishDrag(System.Windows.Point pos)
		{
			_dragging = false;
			_hoverTimer.Stop();
			if (_dragGhost != null)
			{
				DragLayer.Children.Remove(_dragGhost);
				_dragGhost = null;
			}
			if (_highlight != null)
			{
				DragLayer.Children.Remove(_highlight);
				_highlight = null;
			}
			if (_insertHint != null)
			{
				DragLayer.Children.Remove(_insertHint);
				_insertHint = null;
			}
			if (_dragEntry != null && IconGrid.ItemContainerGenerator.ContainerFromItem(_dragEntry) is FrameworkElement frameworkElement)
			{
				frameworkElement.Opacity = 1.0;
			}
			if (_dragEntry == null || _activeList == null)
			{
				return;
			}
			AppEntry appEntry = FindEntry(IconGrid.InputHitTest(pos) as DependencyObject);
			bool isAll = _activeTab.IsAll;
			if (_mergePending != null && appEntry == _mergePending && appEntry != _dragEntry)
			{
				if (isAll)
				{
					MergeIntoFolderAll(_dragEntry, appEntry);
				}
				else if (!(appEntry is AppItem target))
				{
					if (appEntry is AppFolder folder)
					{
						MoveIntoFolder(_dragEntry, folder);
					}
				}
				else
				{
					MergeIntoFolder(_dragEntry, target);
				}
			}
			else if (isAll && _liveMoved)
			{
				SyncAllOrder(appEntry, pos);
			}
			else if (!_liveMoved)
			{
				if (isAll)
				{
					SortInAllView(appEntry, pos);
				}
				else
				{
					SortInCategoryView(appEntry, pos);
				}
			}
			if (isAll)
			{
				RebuildAll();
			}
			_dragEntry = null;
			_dragSourceList = null;
			_liveMoved = false;
			_hoverTarget = null;
			_mergePending = null;
			_app.SaveConfig();
			UpdateStatus();
		}

		private void SyncGlobalOrderFromCategories()
		{
			List<string> list = new List<string>();
			foreach (AppCategory category in _app.Config.Categories)
			{
				foreach (AppEntry entry in category.Entries)
				{
					list.Add(entry.Id);
				}
			}
			_app.Config.GlobalOrder = list;
		}

		private void SortInCategoryView(AppEntry target, System.Windows.Point pos)
		{
			int num = _activeList.IndexOf(_dragEntry);
			if (num < 0)
			{
				return;
			}
			int num2 = ((target == null || target == _dragEntry) ? ComputeInsertIndex(pos) : (_activeList.IndexOf(target) + (IsAfterHalf(pos, target) ? 1 : 0)));
			if (num2 != num)
			{
				if (num2 > num)
				{
					num2--;
				}
				_activeList.Move(num, num2);
				SyncGlobalOrderFromCategories();
			}
		}

		private AppCategory CategoryOf(AppEntry entry)
		{
			if (entry == null)
			{
				return null;
			}
			foreach (AppCategory category in _app.Config.Categories)
			{
				if (category.Entries.Contains(entry))
				{
					return category;
				}
			}
			return null;
		}

		private void MergeIntoFolderAll(AppEntry dragged, AppEntry target)
		{
			AppCategory appCategory = CategoryOf(dragged);
			AppCategory appCategory2 = CategoryOf(target);
			if (appCategory == null || appCategory2 == null)
			{
				return;
			}
			if (appCategory != appCategory2)
			{
				appCategory2.Entries.Insert(appCategory2.Entries.IndexOf(target), dragged);
				appCategory.Entries.Remove(dragged);
				SyncGlobalOrderFromCategories();
				return;
			}
			ObservableCollection<AppEntry> activeList = _activeList;
			_activeList = appCategory.Entries;
			_dragSourceList = appCategory.Entries;
			if (!(target is AppItem target2))
			{
				if (target is AppFolder folder)
				{
					MoveIntoFolder(dragged, folder);
				}
			}
			else
			{
				MergeIntoFolder(dragged, target2);
			}
			_activeList = activeList;
		}

		private void SortInAllView(AppEntry target, System.Windows.Point pos)
		{
			AppEntry dragEntry = _dragEntry;
			if (dragEntry == null || _activeList == null)
			{
				return;
			}
			int num = _activeList.IndexOf(dragEntry);
			if (num < 0)
			{
				return;
			}
			int num2 = ((target == null || target == dragEntry) ? ComputeInsertIndex(pos) : (_activeList.IndexOf(target) + (IsAfterHalf(pos, target) ? 1 : 0)));
			if (num2 == num)
			{
				return;
			}
			if (num2 > num)
			{
				num2--;
			}
			if (num2 < 0 || num2 >= _activeList.Count)
			{
				return;
			}
			_activeList.Move(num, num2);
			_app.Config.GlobalOrder = _activeList.Select((AppEntry x) => x.Id).ToList();
			AppCategory cur = CategoryOf(dragEntry);
			if (cur == null)
			{
				return;
			}
			List<AppEntry> list = AllEntries.Where((AppEntry x) => CategoryOf(x) == cur).ToList();
			cur.Entries.Clear();
			foreach (AppEntry item in list)
			{
				cur.Entries.Add(item);
			}
		}

		private void SyncAllOrder(AppEntry target, System.Windows.Point pos)
		{
			AppEntry dragEntry = _dragEntry;
			if (dragEntry == null)
			{
				return;
			}
			AppCategory cur = CategoryOf(dragEntry);
			if (cur == null)
			{
				return;
			}
			_app.Config.GlobalOrder = AllEntries.Select((AppEntry x) => x.Id).ToList();
			List<AppEntry> list = AllEntries.Where((AppEntry x) => CategoryOf(x) == cur).ToList();
			cur.Entries.Clear();
			foreach (AppEntry item in list)
			{
				cur.Entries.Add(item);
			}
		}

		private void MergeIntoFolder(AppEntry dragged, AppItem target)
		{
			if (dragged is AppFolder)
			{
				MoveIntoFolder(target, (AppFolder)dragged);
				return;
			}
			ObservableCollection<AppEntry> dragSourceList = _dragSourceList;
			int num = dragSourceList.IndexOf(dragged);
			int num2 = dragSourceList.IndexOf(target);
			if (num >= 0 && num2 >= 0)
			{
				AppFolder item = new AppFolder
				{
					Name = dragged.Name + " 等",
					Items = 
					{
						(AppItem)dragged,
						target
					}
				};
				dragSourceList.RemoveAt(Math.Max(num, num2));
				dragSourceList.RemoveAt(Math.Min(num, num2));
				dragSourceList.Insert(Math.Min(num, num2), item);
				SyncGlobalOrderFromCategories();
			}
		}

		private void MoveIntoFolder(AppEntry dragged, AppFolder folder)
		{
			if (dragged is AppItem item)
			{
				if (folder.Items.Any((AppEntry x) => x is AppItem ai && string.Equals(ai.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
				{
					FlashStatus($"\"{item.Name}\" 已在文件夹中，未重复添加");
					return;
				}
				folder.Items.Add(item);
				folder.RefreshThumb();
				_dragSourceList.Remove(item);
				SyncGlobalOrderFromCategories();
				if (IsFolderOpen && _openFolder == folder)
				{
					RefreshFolderItems();
				}
				_app.SaveConfig();
			}
		}

		private static AppEntry FindEntry(DependencyObject obj)
		{
			while (obj != null)
			{
				if (obj is FrameworkElement frameworkElement && frameworkElement.DataContext is AppEntry result)
				{
					return result;
				}
				obj = VisualTreeHelper.GetParent(obj);
			}
			return null;
		}
	}
}