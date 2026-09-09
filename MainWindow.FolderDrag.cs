// ============================================================
// LaunchPad - 文件夹内拖拽排序 (partial class)
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
		private void FolderItems_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (sender == FolderItems && e.LeftButton == MouseButtonState.Pressed)
			{
				_folderDragItem = FindEntry(e.OriginalSource as DependencyObject) as AppItem;
				if (_folderDragItem != null)
				{
					e.Handled = true;
					_folderDragStart = e.GetPosition(FolderCard);
					_folderDragging = false;
					Mouse.Capture(FolderItems);
				}
			}
		}

		private void FolderItems_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
		{
			if (sender != FolderItems || _folderDragItem == null || e.LeftButton != MouseButtonState.Pressed)
			{
				return;
			}
			System.Windows.Point position = e.GetPosition(FolderCard);
			bool flag = position.X >= 0.0 && position.Y >= 0.0 && position.X <= FolderCard.ActualWidth && position.Y <= FolderCard.ActualHeight;
			if (!_folderDragging && (Math.Abs(position.X - _folderDragStart.X) > 6.0 || Math.Abs(position.Y - _folderDragStart.Y) > 6.0))
			{
				_folderDragging = true;
				ImageSource icon = IconService.GetIcon(_folderDragItem);
				Border element = new Border
				{
					Width = 54.0,
					Height = 54.0,
					CornerRadius = new CornerRadius(10.0),
					Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(byte.MaxValue, 245, 244, 241)),
					Child = ((icon == null) ? null : new System.Windows.Controls.Image
					{
						Source = icon,
						Margin = new Thickness(4.0),
						Stretch = Stretch.Uniform
					})
				};
				_folderGhost = new Border
				{
					Width = 78.0,
					Height = 96.0,
					Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
					CornerRadius = new CornerRadius(12.0),
					Opacity = 0.92,
					Child = new StackPanel
					{
						HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
						VerticalAlignment = VerticalAlignment.Center,
						Children = 
						{
							(UIElement)element,
							(UIElement)new TextBlock
							{
								Text = _folderDragItem.Name,
								FontSize = 11.0,
								Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(byte.MaxValue, 107, 114, 128)),
								HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
								Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
								MaxWidth = 70.0,
								TextTrimming = TextTrimming.CharacterEllipsis
							}
						}
					}
				};
				System.Windows.Controls.Panel.SetZIndex(_folderGhost, 100);
				FolderDragLayer.Children.Add(_folderGhost);
				HideFolderDragVisual();
				// 拖动开始时一次性缓存全部容器、网格起点与单元格尺寸，
				// 拖动全程不再查询 ItemContainerGenerator，避免每次 MouseMove 触发同步重排导致图标瞬移
				if (_openFolder != null)
				{
					FolderItems.UpdateLayout();
					CacheFolderGridGeometry();
				}
			}
			if (!_folderDragging || _folderGhost == null)
			{
				return;
			}
			if (flag)
			{
				if (!FolderDragLayer.Children.Contains(_folderGhost))
				{
					DragLayer.Children.Remove(_folderGhost);
					FolderDragLayer.Children.Add(_folderGhost);
				}
				Canvas.SetLeft(_folderGhost, position.X - _folderGhost.Width / 2.0);
				Canvas.SetTop(_folderGhost, position.Y - _folderGhost.Height / 2.0);
				// 拖动实时让位：目标位置变化时平滑重排，其他图标以动画腾出位置（iOS 效果）
				if (_openFolder != null && _openFolder.Items.Contains(_folderDragItem))
				{
					System.Windows.Point posItems = e.GetPosition(FolderItems);
					int hover = GetHoverIndexInFolder(posItems);
					int cur = _openFolder.Items.IndexOf(_folderDragItem);

					
					if (hover >= 0 && hover != cur)
					{
						// iOS 式逐格让位：重排前先缓存容器与单元格尺寸（避免改列表后再查容器触发生成器同步瞬移）
						double cellW = _folderCellW;
						double cellH = _folderCellH;
						if (cellW <= 0.0 || cellH <= 0.0)
						{
							cellW = 86.0;
							cellH = 110.0;
						}
						var containers = _folderContainers;
						FrameworkElement dragCont = null;
						if (containers != null && _folderDragItem != null && containers.TryGetValue(_folderDragItem, out dragCont))
						{
						}
						// 逐格推挤：拖动项从 cur 一步步移动到 hover，每步只与被挤开的相邻图标交换
						int cols = Math.Max(2, _currentCols);
						int step = hover > cur ? 1 : -1;

					long nowMs = Environment.TickCount64;
					if (step != _lastSwapDir && nowMs - _lastSwapTime < 200)
					{
					}
					else
					{
						int idx = cur;
						while (idx != hover)
						{
							int next = idx + step;
							AppItem neighbor = _openFolder.Items[next];
							_openFolder.Items[next] = _folderDragItem;
							_openFolder.Items[idx] = neighbor;
							// 交换后 neighbor 落在 idx（fromIdx），从原位置 next（toIdx）平滑移到 idx，方向为 from→to
							AnimateOneStep(neighbor, cellW, cellH, cols, _folderLayoutIdx, containers);
							idx = next;
						}
						_folderLiveReordered = true;
						if (dragCont != null)
						{
							dragCont.Opacity = 0.0;
						}

					}
					}
				}
				return;
			}
            PromoteFolderDrag(e.GetPosition(Root));
            e.Handled = true;
        }

		private void FolderItems_MouseUp(object sender, MouseButtonEventArgs e)
		{
			if (sender != FolderItems)
			{
				return;
			}
			AppItem folderDragItem = _folderDragItem;
			_folderDragItem = null;
			if (_folderGhost != null)
			{
				FolderDragLayer.Children.Remove(_folderGhost);
				DragLayer.Children.Remove(_folderGhost);
				_folderGhost = null;
			}
			ShowFolderDragVisual(folderDragItem);
			_folderContainers = null;
			Mouse.Capture(null);
			if (folderDragItem != null)
			{
				e.Handled = true;
			}
			if (!_folderDragging)
			{
				if (folderDragItem != null)
				{
					if (_openFolder != null && IconGrid.ItemContainerGenerator.ContainerFromItem(_openFolder) is FrameworkElement frameworkElement)
					{
						frameworkElement.Opacity = 1.0;
					}
					HideFolderNow();
					LaunchApp(folderDragItem);
				}
				return;
			}
			_folderDragging = false;
			if (folderDragItem == null || _openFolder == null)
			{
				return;
			}
			System.Windows.Point position = e.GetPosition(FolderCard);
			if (position.X >= 0.0 && position.Y >= 0.0 && position.X <= FolderCard.ActualWidth && position.Y <= FolderCard.ActualHeight)
			{
				if (_folderLiveReordered)
				{
					_folderLiveReordered = false;
					RefreshFolderItems();
					_app.SaveConfig();
					return;
				}
				System.Windows.Point position2 = e.GetPosition(FolderItems);
				AppItem appItem = FindEntry(FolderItems.InputHitTest(position2) as DependencyObject) as AppItem;
				int num = _openFolder.Items.IndexOf(folderDragItem);
				if (num >= 0)
				{
					int num2 = ((appItem == null || appItem == folderDragItem) ? _openFolder.Items.Count : (_openFolder.Items.IndexOf(appItem) + (IsAfterHalfInFolder(position2, appItem) ? 1 : 0)));
					if (num2 > num)
					{
						num2--;
					}
					if (num2 != num && num2 >= 0 && num2 < _openFolder.Items.Count)
					{
						_openFolder.Items.Remove(folderDragItem);
						_openFolder.Items.Insert(num2, folderDragItem);
						RefreshFolderItems();
					}
					_app.SaveConfig();
				}
				return;
			}
			System.Windows.Point position3 = e.GetPosition(IconGrid);
			if (position3.X < -20.0 || position3.Y < -20.0 || position3.X > IconGrid.ActualWidth + 20.0 || position3.Y > IconGrid.ActualHeight + 20.0)
			{
				return;
			}
			AppEntry appEntry = FindEntry(IconGrid.InputHitTest(position3) as DependencyObject);
			AppCategory appCategory = ((appEntry != null) ? CategoryOf(appEntry) : CategoryOf(_openFolder));
			if (appCategory == null)
			{
				return;
			}
			int value;
			if (appEntry != null && appEntry != folderDragItem)
			{
				value = appCategory.Entries.IndexOf(appEntry) + (IsAfterHalf(position3, appEntry) ? 1 : 0);
			}
			else
			{
				_dragSourceList = _activeList;
				value = ComputeInsertIndex(position3);
			}
			var sourceCategory = CategoryOf(_openFolder);
			_openFolder.Items.Remove(folderDragItem);
			_openFolder.RefreshThumb();
			appCategory.Entries.Insert(Math.Clamp(value, 0, appCategory.Entries.Count), folderDragItem);
			if (_openFolder.Items.Count == 0)
			{
				// 拖空：先播放关闭动画，动画结束（格子回落原位）后再移除文件夹并重建网格，
				// 避免关闭过程中新格子过早显示
				AppFolder emptyFolder = _openFolder;
				CloseFolderWithAnim(delegate
				{
					sourceCategory?.Entries.Remove(emptyFolder);
					_app.SaveConfig();
					if (_activeTab.IsAll)
					{
						RebuildAll();
					}
					UpdateStatus();
				});
				return;
			}
			RefreshFolderItems();
			_app.SaveConfig();
			if (_activeTab.IsAll)
			{
				RebuildAll();
			}
			if (IsFolderOpen && _openFolder != null)
			{
				FolderTitle.Text = _openFolder.Name;
			}
			UpdateStatus();
		}

		private void HideFolderDragVisual()
		{
			if (_folderDragItem == null || _openFolder == null || !_openFolder.Items.Contains(_folderDragItem))
			{
				return;
			}
			if (FolderItems.ItemContainerGenerator.ContainerFromItem(_folderDragItem) is FrameworkElement frameworkElement2)
			{
				frameworkElement2.Opacity = 0.0;
			}
		}

		private void ShowFolderDragVisual(AppItem item)
		{
			if (item == null || _openFolder == null || !_openFolder.Items.Contains(item))
			{
				return;
			}
			FrameworkElement frameworkElement3 = null;
			if (_folderContainers != null && _folderContainers.TryGetValue(item, out frameworkElement3))
			{
			}
			else if (FolderItems.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement cf)
			{
				frameworkElement3 = cf;
			}
			if (frameworkElement3 != null)
			{
				frameworkElement3.Opacity = 1.0;
			}
		}


		private void RefreshFolderItems()
		{
			_folderContainers = null;
			_folderLayoutIdx = null;
			if (_openFolder != null)
			{
				int count = _openFolder.Items.Count;
				SetFolderGridPanel(Math.Max(2, Math.Min(6, (int)Math.Ceiling(Math.Sqrt(count)))));
				FolderCard.Width = Math.Max(220, _currentCols * 86 + 38);
				FolderCard.Height = Math.Min(428, 70 + Math.Max(1, (int)Math.Ceiling(count / (double)_currentCols)) * 104);
				FolderCard.Margin = new Thickness((Root.ActualWidth - FolderCard.Width) / 2,
					(Root.ActualHeight - FolderCard.Height) / 2, 0, 0);
				FolderItems.ItemsSource = null;
				FolderItems.ItemsSource = _openFolder.Items;
			}
		}

		private void SetFolderGridPanel(int cols, int rows = 0)
		{
			_currentCols = cols;
			FrameworkElementFactory frameworkElementFactory = new FrameworkElementFactory(typeof(UniformGrid));
			frameworkElementFactory.SetValue(UniformGrid.ColumnsProperty, cols);
			if (rows > 0)
			{
				frameworkElementFactory.SetValue(UniformGrid.RowsProperty, rows);
			}
			FolderItems.ItemsPanel = new ItemsPanelTemplate(frameworkElementFactory);
		}

		private bool IsAfterHalfInFolder(System.Windows.Point pos, AppItem target)
		{
			if (!(FolderItems.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement frameworkElement))
			{
				return false;
			}
			System.Windows.Point point = frameworkElement.TranslatePoint(new System.Windows.Point(0.0, 0.0), FolderItems);
			if (frameworkElement.ActualWidth <= 0.0)
			{
				return false;
			}
			return (pos.X - point.X) / frameworkElement.ActualWidth > 0.5;
		}

		/// <summary>计算鼠标在文件夹网格中应插入的位置（纯网格几何，不依赖容器命中/渲染偏移）。</summary>
		private int GetHoverIndexInFolder(System.Windows.Point posInItems)
		{
			int n = _openFolder.Items.Count;
			if (n == 0 || _folderDragItem == null)
			{
				return 0;
			}
			int idx = _openFolder.Items.IndexOf(_folderDragItem);
			// 以第一个格子的布局位置与格子尺寸推算整张网格，鼠标所在行列 → 目标索引
			if (_folderContainers == null)
			{
				return idx;
			}
			double cellW = _folderCellW;
			double cellH = _folderCellH;
			if (cellW <= 0.0 || cellH <= 0.0)
			{
				cellW = 86.0;
				cellH = 110.0;
			}
			System.Windows.Point p0 = _folderGridOrigin;
				int cols = Math.Max(2, _currentCols);
				int rows = Math.Max(1, (n + cols - 1) / cols);
				int row = (int)Math.Floor((posInItems.Y - p0.Y) / cellH);
				row = Math.Clamp(row, 0, rows - 1);
				double relX = (posInItems.X - p0.X) / cellW;
				if (relX < 0.0)
				{
					relX = 0.0;
				}
				int colBase = (int)Math.Floor(relX);
				int col = colBase + ((relX - (double)colBase > 0.5) ? 1 : 0);
				col = Math.Clamp(col, 0, cols);
				int target = row * cols + col;
				return Math.Clamp(target, 0, n - 1);
		}

		/// <summary>文件夹内逐格让位：被挤开的相邻图标从 fromIdx 平滑移动到 toIdx（1 格位移），使用重排前缓存的容器。</summary>
		private void AnimateOneStep(AppItem item, double cellW, double cellH, int cols, Dictionary<AppItem, int> layoutIdx, Dictionary<AppItem, FrameworkElement> containers)
		{
			if (item == null || item == _folderDragItem || containers == null ||
				!containers.TryGetValue(item, out FrameworkElement fe) ||
				layoutIdx == null || !layoutIdx.TryGetValue(item, out int li))
			{
				return;

			}
			int curIdx = _openFolder.Items.IndexOf(item);
			if (curIdx < 0)
			{
				return;
			}
			// 目标偏移 = 当前 Items 索引格 - 布局格（布局格为打开文件夹时固定的容器排列顺序，Items 交换不会改变它）
			double dx = ((double)(curIdx % cols) - (double)(li % cols)) * cellW;
			double dy = ((double)(curIdx / cols) - (double)(li / cols)) * cellH;
			double curDx = 0.0;
			double curDy = 0.0;
			if (fe.RenderTransform is TranslateTransform oldTt)
			{
				curDx = oldTt.X;
				curDy = oldTt.Y;
			}
			var tt = new TranslateTransform(curDx, curDy);
			fe.RenderTransform = tt;
			var animX = new DoubleAnimation(dx, TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config, 220)))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
				FillBehavior = FillBehavior.HoldEnd
			};
			var animY = new DoubleAnimation(dy, TimeSpan.FromMilliseconds(MotionService.Duration(_app.Config, 220)))
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
				FillBehavior = FillBehavior.HoldEnd
			};
			tt.BeginAnimation(TranslateTransform.XProperty, animX);
			tt.BeginAnimation(TranslateTransform.YProperty, animY);
		}
	}
}
