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
	public class CategoryTab : ObservableObject
	{
		private bool _isSelected;

		public AppCategory Category { get; set/*init*/; }

		public bool IsAll => Category == null;

		public string Name
		{
			get
			{
				if (!IsAll)
				{
					return Category.Name;
				}
				return "全部";
			}
		}

		public bool IsSelected
		{
			get
			{
				return _isSelected;
			}
			set
			{
				Set(ref _isSelected, value, "IsSelected");
			}
		}
	}
	public partial class MainWindow : Window, INotifyPropertyChanged
	{
		private readonly App _app;

		private double _iconBox = 64.0;

		private double _tileWidth = 82.0;

		private double _tileHeight = 112.0;

		private double _tileIconW = 40.0;

		private double _tileIconH = 40.0;

		private double _tileIconM = 1.0;

		private double _tileIconInner = 30.0;

		private double _tileNameH;

		private double _tileNameSize;

		// 文件夹格子 2×2 缩略图的单元格/内框尺寸，随图标大小档位变化（= IconBox/2），
		// 保证文件夹名与应用名在同一水平线，且关闭动画末帧与格子缩略图完全重合
		private double _thumbCell = 32.0;

		private double _thumbInner = 28.0;

		private ObservableCollection<AppEntry> _activeList;

		private CategoryTab _activeTab;

		private AppEntry _pressedEntry;

		private System.Windows.Point _pressPoint;

		private bool _pressed;

		private bool _dragging;

		private Border _dragGhost;

		private System.Windows.Shapes.Rectangle _highlight;

		private System.Windows.Shapes.Rectangle _insertHint;

		private AppEntry _dragEntry;

		private int _dragFromIndex = -1;

		private ObservableCollection<AppEntry> _dragSourceList;

		private bool _liveMoved;

		private AppEntry _hoverTarget;

		private DateTime _hoverSince;

		private AppEntry _mergePending;

		private DispatcherTimer _hoverTimer;

		private DispatcherTimer _statusTimer;

		private readonly Dictionary<AppEntry, System.Windows.Point> _oldPositions = new Dictionary<AppEntry, System.Windows.Point>();

		private AppItem _folderDragItem;

		private System.Windows.Point _folderDragStart;

		private bool _folderDragging;

		/// <summary>文件夹内拖动是否已由 MouseMove 实时重排过（松手时跳过重复排序）。</summary>
		private int _lastSwapDir = 0;
		private Dictionary<AppItem, int> _folderLayoutIdx;
		private long _lastSwapTime = 0;
		private bool _folderLiveReordered;

		private Border _folderGhost;

		/// <summary>拖动期间缓存的文件夹内容器（避免拖动中查询生成器触发同步瞬移）。</summary>
		private Dictionary<AppItem, FrameworkElement> _folderContainers;

		/// <summary>拖动期间缓存的网格起点（第一个容器相对 FolderItems 的位置）。</summary>
		private System.Windows.Point _folderGridOrigin;

		private double _folderCellW = 86.0;

		private double _folderCellH = 110.0;

		private AppFolder _openFolder;

		private FrameworkElement _openFolderContainer;

		private int _currentCols;

		private Action _closeDone;


		public double IconBox
		{
			get
			{
				return _iconBox;
			}
			private set
			{
				_iconBox = value;
				Raise("IconBox");
			}
		}

		public double TileWidth
		{
			get
			{
				return _tileWidth;
			}
			private set
			{
				_tileWidth = value;
				Raise("TileWidth");
			}
		}

		public double TileHeight
		{
			get
			{
				return _tileHeight;
			}
			private set
			{
				_tileHeight = value;
				Raise("TileHeight");
			}
		}

		public double TileIconW
		{
			get
			{
				return _tileIconW;
			}
			private set
			{
				_tileIconW = value;
				Raise("TileIconW");
			}
		}

		public double TileIconH
		{
			get
			{
				return _tileIconH;
			}
			private set
			{
				_tileIconH = value;
				Raise("TileIconH");
			}
		}

		public double TileIconM
		{
			get
			{
				return _tileIconM;
			}
			private set
			{
				_tileIconM = value;
				Raise("TileIconM");
			}
		}

		public double TileIconInner
		{
			get
			{
				return _tileIconInner;
			}
			private set
			{
				_tileIconInner = value;
				Raise("TileIconInner");
			}
		}

		public double TileNameH
		{
			get
			{
				return _tileNameH;
			}
			private set
			{
				_tileNameH = value;
				Raise("TileNameH");
			}
		}

		public double TileNameSize
		{
			get
			{
				return _tileNameSize;
			}
			private set
			{
				_tileNameSize = value;
				Raise("TileNameSize");
			}
		}

		/// <summary>文件夹格子 2×2 缩略图单个单元格边长（随图标档位 = IconBox/2）。</summary>
		public double ThumbCell
		{
			get => _thumbCell;
			private set { _thumbCell = value; Raise("ThumbCell"); }
		}

		/// <summary>文件夹格子缩略图内框边长（= ThumbCell - 4）。</summary>
		public double ThumbInner
		{
			get => _thumbInner;
			private set { _thumbInner = value; Raise("ThumbInner"); }
		}

		public ObservableCollection<CategoryTab> Tabs { get; } = new ObservableCollection<CategoryTab>();


		public ObservableCollection<AppEntry> AllEntries { get; } = new ObservableCollection<AppEntry>();


		private bool IsFolderOpen
		{
			get
			{
				if (_openFolder != null)
				{
					return FolderOverlay.Visibility == Visibility.Visible;
				}
				return false;
			}
		}

		public event PropertyChangedEventHandler PropertyChanged;

		private void SetIconSize(double w, double h, double m, double inner, double nameH, double nameSize)
		{
			TileIconW = w;
			TileIconH = h;
			TileIconM = m;
			TileIconInner = inner;
			TileNameH = nameH;
			TileNameSize = nameSize;
		}

		private (double w, double h, double m, double inner, double nameH, double nameSize) GetTileIconSizes()
		{
			// 与 XAML FolderThumbTemplate（34×34 格 + 0.5 边距 + 30 内框）保持同构，
			// 保证打开动画起始帧 = 格子缩略画面（图标大小/间距完全一致）
			// 与 XAML FolderThumbTemplate（ThumbCell 格 + ThumbInner 内框）保持同构，
			// 保证关闭动画末帧 = 格子缩略画面（图标大小/间距完全一致）
			return (w: _thumbCell, h: _thumbCell, m: 0.0, inner: _thumbInner, nameH: 0.0, nameSize: 0.0);
		}

		private void ApplyTileIconSizes()
		{
			(double, double, double, double, double, double) tileIconSizes = GetTileIconSizes();
			SetIconSize(tileIconSizes.Item1, tileIconSizes.Item2, tileIconSizes.Item3, tileIconSizes.Item4, tileIconSizes.Item5, tileIconSizes.Item6);
		}

		private void Raise(string name)
		{
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}

		public MainWindow(App app)
		{
			_app = app;
			InitializeComponent();
			InitializeThemeBackground();
            CategoryStrip.LostMouseCapture+=(_,e)=> { if(e.OriginalSource==CategoryStrip && _panPressed) EndCategoryPan(false); };
            // 内容可滚动时在底部信息条上方显示轻微阴影，滚动条消失时淡出
            GridScroll.ScrollChanged += (_, _) => UpdateBottomBarShadow();
            UpdateBottomBarShadow();
			base.DataContext = this;
            InitializeDragRouting();
            InputBehavior.Apply(this);
            Closing += (_, e) => { if (!_app.IsQuitting) { e.Cancel = true; HideAnimated(); } };
			IconGrid.ItemsSource = AllEntries;
			_hoverTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(100.0)
			};
			_hoverTimer.Tick += delegate
			{
				CheckHover();
			};
			_statusTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromSeconds(3.0)
			};
			_statusTimer.Tick += delegate
			{
				_statusTimer.Stop();
				UpdateStatus();
			};
			base.Loaded += delegate
			{
				ReloadFromConfig();
				base.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)delegate
				{
					ApplyIconSize();
				});
			};
			base.Deactivated += delegate
			{
				if (_dragging || _externalDragging || _featureDialogOpen) return;
				if (IsFolderOpen)
				{
					CloseFolderWithAnim(delegate
					{
						if (_app.Config.HideOnFocusLost && !_app.IsSettingsVisible && base.OwnedWindows.Count == 0)
						{
							HideAnimated();
						}
					});
				}
				else if (_app.Config.HideOnFocusLost && !_app.IsSettingsVisible && base.OwnedWindows.Count == 0)
				{
					HideAnimated();
				}
			};
		}

		public void ReloadFromConfig()
		{
			Tabs.Clear();
			Tabs.Add(new CategoryTab());
			foreach (AppCategory category in _app.Config.Categories)
			{
				Tabs.Add(new CategoryTab
				{
					Category = category
				});
			}
			string keepId = _activeTab?.Category?.Id;
			CategoryTab categoryTab = Tabs.FirstOrDefault((CategoryTab t) => t.Category?.Id == keepId);
			SelectTab(categoryTab ?? Tabs[0]);
			ApplyIconSize();
			UpdateHotkeyLabel();
			UpdateStatus();
		}

		private void SelectTab(CategoryTab tab)
		{
			if (tab == null)
			{
				return;
			}
			if (IsFolderOpen)
			{
				_pendingCategory = tab;
				CloseFolderWithAnim(() =>
				{
					var pending = _pendingCategory;
					_pendingCategory = null;
					if (pending != null) SelectTab(pending);
				});
				return;
			}
            int direction = _activeTab == null ? 1 : Math.Sign(Tabs.IndexOf(tab) - Tabs.IndexOf(_activeTab));
			_activeTab = tab;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,(Action)RevealSelectedCategory);
			RefreshTheme();
			foreach (CategoryTab tab2 in Tabs)
			{
				tab2.IsSelected = tab2 == tab;
			}
			if (tab.IsAll)
			{
				RebuildAll();
				_activeList = AllEntries;
			}
			else
			{
				_activeList = tab.Category.Entries;
				IconGrid.ItemsSource = _activeList;
			}
			ClearSearch();
			UpdateStatus();
			var slide = new TranslateTransform();
            GridScroll.RenderTransform = slide;
            double duration = MotionService.Duration(_app.Config, 230);
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(direction * 28, 0, TimeSpan.FromMilliseconds(duration)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            DoubleAnimation animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(duration))
			{
				EasingFunction = new QuadraticEase()
			};
			IconGrid.BeginAnimation(UIElement.OpacityProperty, animation);
		}

		/// <summary>公开方法：切换到指定分类（供全局分类快捷键回调使用）。</summary>
		public void SelectCategory(AppCategory category)
		{
			if (category == null) return;
			var tab = Tabs.FirstOrDefault(t => t.Category?.Id == category.Id);
			if (tab != null) SelectTab(tab);
		}

		/// <summary>公开方法：按名称切换到指定分类。</summary>
		public void SelectCategoryByName(string name)
		{
			var tab = Tabs.FirstOrDefault(t => !t.IsAll && t.Category?.Name == name);
			if (tab != null) SelectTab(tab);
		}

		/// <summary>公开方法：获取当前活动分类（"全部"视图返回 null）。</summary>
		public AppCategory GetActiveCategory() => _activeTab?.Category;

		private void RebuildAll()
		{
			AllEntries.Clear();
			Dictionary<string, AppEntry> dictionary = new Dictionary<string, AppEntry>();
			foreach (AppCategory category in _app.Config.Categories)
			{
				foreach (AppEntry entry in category.Entries)
				{
					dictionary[entry.Id] = entry;
				}
			}
			List<string> globalOrder = _app.Config.GlobalOrder;
			HashSet<string> hashSet = new HashSet<string>();
			foreach (string item in globalOrder)
			{
				if (dictionary.TryGetValue(item, out var value) && hashSet.Add(item))
				{
					AllEntries.Add(value);
				}
			}
			foreach (AppCategory category2 in _app.Config.Categories)
			{
				foreach (AppEntry entry2 in category2.Entries)
				{
					if (hashSet.Add(entry2.Id))
					{
						AllEntries.Add(entry2);
						globalOrder.Add(entry2.Id);
					}
				}
			}
			if (globalOrder.Count > hashSet.Count)
			{
				HashSet<string> hashSet2 = new HashSet<string>(AllEntries.Select((AppEntry x) => x.Id));
				_app.Config.GlobalOrder = globalOrder.Where(hashSet2.Contains).ToList();
			}
			IconGrid.ItemsSource = AllEntries;
		}

		private void ClearSearch()
		{
			if (SearchBox.Text.Length > 0)
			{
				SearchBox.Clear();
			}
		}

		private void ApplyIconSize()
		{
			string iconSize = _app.Config.IconSize;
			if (!(iconSize == "Small"))
			{
				if (iconSize == "Large")
				{
					IconBox = 80.0;
					TileWidth = 102.0;
					TileHeight = 132.0;
				}
				else
				{
					IconBox = 64.0;
					TileWidth = 82.0;
					TileHeight = 112.0;
				}
			}
			else
			{
				IconBox = 52.0;
				TileWidth = 68.0;
				TileHeight = 96.0;
			}
			// 文件夹 2×2 缩略格 = 应用图标框的一半，保证两行标题水平对齐
			ThumbCell = IconBox / 2.0;
			ThumbInner = ThumbCell - 4.0;
			ApplyTileIconSizes();
		}

		private void UpdateStatus()
		{
			int value = _app.Config.Categories.Sum((AppCategory c) => c.Entries.Count);
			StatusText.Text = $"{_app.Config.Categories.Count} 个分类 · {value} 个应用";
		}

		private void FlashStatus(string msg)
		{
			StatusText.Text = msg;
			_statusTimer.Stop();
			_statusTimer.Start();
		}

		private void UpdateHotkeyLabel()
		{
			HotkeyLabel.Text = FormatHotkey(_app.Config.HotkeyModifiers, _app.Config.HotkeyKey);
		}

		public static string FormatHotkey(int modifiers, int key)
		{
			List<string> list = new List<string>();
			if (((uint)modifiers & 2u) != 0)
			{
				list.Add("Ctrl");
			}
			if (((uint)modifiers & (true ? 1u : 0u)) != 0)
			{
				list.Add("Alt");
			}
			if (((uint)modifiers & 4u) != 0)
			{
				list.Add("Shift");
			}
			if (((uint)modifiers & 8u) != 0)
			{
				list.Add("Win");
			}
			Key key2 = KeyInterop.KeyFromVirtualKey(key);
			string text;
			switch (key2)
			{
			case Key.Space:
				text = "Space";
				break;
			case Key.None:
			{
				int num = key;
				text = num.ToString();
				break;
			}
			default:
				text = key2.ToString();
				break;
			}
			string item = text;
			list.Add(item);
			return string.Join(" + ", list);
		}

		private void Header_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (IsFolderOpen)
			{
				CloseFolderWithAnim();
			}
			else if (e.LeftButton == MouseButtonState.Pressed)
			{
				DragMove();
			}
		}


		private void UpdateBottomBarShadow()
		{
			if (BottomShadowBar == null || GridScroll == null) return;
			bool show = GridScroll.ScrollableHeight > 0;
			BottomShadowBar.BeginAnimation(System.Windows.UIElement.OpacityProperty,
				new System.Windows.Media.Animation.DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(180)));
		}
		private void Tab_Click(object sender, RoutedEventArgs e)
		{
			if ((sender as FrameworkElement)?.DataContext is CategoryTab tab)
			{
				SelectTab(tab);
			}
		}

		private void AddCategory_Click(object sender, RoutedEventArgs e)
		{
			string name = WithFeatureDialog(() => PromptDialog.Show(this, "新建分类", "请输入分类名称", ""));
			if (!string.IsNullOrWhiteSpace(name))
			{
				name = name.Trim();
				if (_app.Config.Categories.Any((AppCategory c) => c.Name == name))
				{
					PromptDialog.Notify(this, "分类名称重复", "已存在同名分类，请换一个名称。");
					return;
				}
				AppCategory appCategory = new AppCategory
				{
					Name = name
				};
				_app.Config.Categories.Add(appCategory);
				CategoryTab categoryTab = new CategoryTab
				{
					Category = appCategory
				};
				Tabs.Add(categoryTab);
				SelectTab(categoryTab);
				_app.SaveConfig();
			}
		}

		private void Settings_Click(object sender, RoutedEventArgs e)
		{
			_app.OpenSettings();
		}

        public void HideAnimated()
        {
            if (_dragging) EndDragSession();
            // “上次位置”模式：隐藏前记录当前窗口位置并持久化，下次在同一位置弹出
            if (_app.Config.Position == "Last")
            {
                _app.Config.LastLeft = Left;
                _app.Config.LastTop = Top;
                _app.SaveConfig();
            }
            MotionService.Hide(this, _app.Config, () => { if (IsFolderOpen) HideFolderNow(); Hide(); });
        }

        private AppCategory GetImportCategory()
        {
            if (_activeTab?.Category is AppCategory selected) return selected;
            var category = _app.Config.GetOrCreateCategory("未分类");
            if (!Tabs.Any(t => t.Category == category))
                Tabs.Add(new CategoryTab { Category = category });
            return category;
        }
		private void AddApp_Click(object sender, RoutedEventArgs e)
		{
			Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
			{
				Multiselect = true,
				Title = "选择要添加的应用",
				Filter = "应用程序和快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.url|所有文件|*.*"
			};
			if (!openFileDialog.ShowDialog(this).GetValueOrDefault())
			{
				return;
			}
			AppCategory category = GetImportCategory();
			int num = 0;
			int num2 = 0;
			string[] fileNames = openFileDialog.FileNames;
			foreach (string path in fileNames)
			{
				try
				{
					if (IsPathExistsAnywhere(System.IO.Path.GetFullPath(path)))
					{
						ShowDuplicateToast(path);
						num2++;
					}
					else if (AddFile(category, path))
					{
						num++;
					}
				}
				catch
				{
				}
			}
			if (num > 0)
			{
				_app.SaveConfig();
				if (_activeTab.IsAll)
				{
					RebuildAll();
				}
				UpdateStatus();
				if (num2 > 0)
				{
					FlashStatus($"已添加 {num} 个 · {num2} 个已在启动器中跳过");
				}
			}
			else if (num2 > 0)
			{
				FlashStatus($"{num2} 个已在启动器中（不重复添加）");
			}
		}

		private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Escape)
			{
                if (_dragging) { EndDragSession(); e.Handled = true; return; }
				if (IsFolderOpen)
				{
					CloseFolderWithAnim();
					e.Handled = true;
				}
				else
				{
					HideAnimated();
					e.Handled = true;
				}
			}
			else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			{
				SearchBox.Focus();
				SearchBox.SelectAll();
				e.Handled = true;
			}
		}

		private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
		{
			if (IsFolderOpen)
			{
				CloseFolderWithAnim();
			}
			string q = SearchBox.Text.Trim();
			if (q.Length == 0)
			{
				if (_activeTab != null)
				{
					IconGrid.ItemsSource = _activeList;
				}
			}
			else
			{
				IconGrid.ItemsSource = new ObservableCollection<AppEntry>(_activeList.Where((AppEntry x) => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)));
			}
		}


		private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (e.LeftButton != MouseButtonState.Pressed)
			{
				return;
			}
			if (_app.Config.LaunchMode == "Double" && e.ClickCount >= 2 && FindEntry(e.OriginalSource as DependencyObject) is AppItem item)
			{
				LaunchApp(item);
				return;
			}
			_pressedEntry = FindEntry(e.OriginalSource as DependencyObject);
			if (_pressedEntry == null)
			{
				if (IsFolderOpen)
				{
					CloseFolderWithAnim();
				}
				else
				{
					DragMove();
				}
			}
			else
			{
				_pressed = true;
				_pressPoint = e.GetPosition(GridScroll);
				_dragFromIndex = _activeList.IndexOf(_pressedEntry);
				_dragSourceList = _activeList;
				Mouse.Capture(IconGrid);
			}
		}

		private void Grid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
		{
			if (_pressed && _pressedEntry != null)
			{
				System.Windows.Point position = e.GetPosition(GridScroll);
				if (!_dragging && _activeTab != null && (Math.Abs(position.X - _pressPoint.X) > 6.0 || Math.Abs(position.Y - _pressPoint.Y) > 6.0))
				{
					if (SearchBox.Text.Trim().Length > 0)
					{
						_pressed = false;
						_pressedEntry = null;
						Mouse.Capture(null);
						FlashStatus("清空搜索后可拖动排序");
						return;
					}
					StartDrag();
				}
				if (_dragging)
				{
					TrackDragAt(e.GetPosition(Root), Environment.TickCount64);
				}
			}
		}

		private void Grid_MouseUp(object sender, MouseButtonEventArgs e)
		{
			_pressed = false;
			Mouse.Capture(null);
			if (_dragging)
			{
				FinishDrag(e.GetPosition(IconGrid));
			}
			else if (_pressedEntry != null && e.ClickCount == 1)
			{
				HandleClick(_pressedEntry);
			}
			_pressedEntry = null;
		}

		private void HandleClick(AppEntry entry)
		{
			if (!(entry is AppItem item))
			{
				if (entry is AppFolder appFolder && (!IsFolderOpen || _openFolder != appFolder))
				{
					OpenFolder(appFolder);
				}
			}
			else if (_app.Config.LaunchMode == "Single")
			{
				LaunchApp(item);
			}
		}

		private static double CubicEaseInOut(double t)
		{
			if (!(t < 0.5))
			{
				return 1.0 - Math.Pow(-2.0 * t + 2.0, 3.0) / 2.0;
			}
			return 4.0 * t * t * t;
		}

		private static double Lerp(double a, double b, double k)
		{
			return a + (b - a) * k;
		}

		private static FrameworkElement FindInContainer(FrameworkElement container, string name)
		{
			if (container == null)
			{
				return null;
			}
			try
			{
				return FindVisualChildByName(container, name);
			}
			catch
			{
				return null;
			}
		}

		private static FrameworkElement FindVisualChildByName(DependencyObject root, string name)
		{
			int childrenCount = VisualTreeHelper.GetChildrenCount(root);
			for (int i = 0; i < childrenCount; i++)
			{
				if (VisualTreeHelper.GetChild(root, i) is FrameworkElement frameworkElement)
				{
					if (frameworkElement.Name == name)
					{
						return frameworkElement;
					}
					FrameworkElement frameworkElement2 = FindVisualChildByName(frameworkElement, name);
					if (frameworkElement2 != null)
					{
						return frameworkElement2;
					}
				}
			}
			return null;
		}

		private void RemoveItem_Click(object sender, RoutedEventArgs e)
		{
			if (sender is MenuItem menuItem && menuItem.CommandParameter is AppEntry entry)
			{
				RemoveEntry(entry);
			}
		}

		private void FolderTitle_MouseDown(object sender, MouseButtonEventArgs e)
		{
			if (sender == FolderTitle && _openFolder != null && e.ClickCount == 1)
			{
				FolderTitle.Visibility = Visibility.Collapsed;
				FolderRenameBox.Visibility = Visibility.Visible;
				FolderRenameBox.Text = _openFolder.Name;
				FolderRenameBox.Focus();
				FolderRenameBox.SelectAll();
				e.Handled = true;
			}
		}

		private void FolderRenameBox_LostFocus(object sender, RoutedEventArgs e)
		{
			CommitRename();
		}

		private void FolderRenameBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
		{
			if (e.Key == Key.Return)
			{
				CommitRename();
			}
			else if (e.Key == Key.Escape)
			{
                if (_dragging) { EndDragSession(); e.Handled = true; return; }
				CancelRename();
			}
		}

		private void CommitRename()
		{
			if (_openFolder != null)
			{
				string text = FolderRenameBox.Text?.Trim();
				if (!string.IsNullOrWhiteSpace(text) && text != _openFolder.Name)
				{
					_openFolder.Name = text;
					_app.SaveConfig();
				}
			}
			ShowFolderTitle();
		}

		private void CancelRename()
		{
			ShowFolderTitle();
		}

		private void ShowFolderTitle()
		{
			FolderRenameBox.Visibility = Visibility.Collapsed;
			if (_openFolder != null)
			{
				FolderTitle.Text = _openFolder.Name;
				FolderNameBottom.Text = _openFolder.Name;
			}
			FolderTitle.Visibility = Visibility.Visible;
		}

		private void LaunchApp(AppItem item)
		{
			AppLaunchService.Launch(item);
			if (_app.Config.HideAfterLaunch)
			{
				HideAnimated();
			}
		}

        protected override void OnDragOver(System.Windows.DragEventArgs e)
        {
            base.OnDragOver(e);
            ExternalDragOver(e);
        }

        protected override void OnDragLeave(System.Windows.DragEventArgs e)
        {
            base.OnDragLeave(e);
            if (!new Rect(Root.RenderSize).Contains(e.GetPosition(Root)))
            {
                EndDragSession();
                if (IsFolderOpen) CloseFolderWithAnim();
            }
        }

        protected override void OnDrop(System.Windows.DragEventArgs e)
        {
            base.OnDrop(e);
            ExternalDrop(e);
        }
		public bool AddFile(AppCategory category, string path)
		{
			try
			{
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
				{
					return false;
				}
				string fullPath = System.IO.Path.GetFullPath(path);
				if (IsPathExistsAnywhere(fullPath))
				{
					ShowDuplicateToast(fullPath);
					return false;
				}
				string text = System.IO.Path.GetFileNameWithoutExtension(fullPath);
				if (string.IsNullOrWhiteSpace(text))
				{
					text = System.IO.Path.GetFileName(fullPath);
				}
				category.Entries.Add(new AppItem
				{
					Name = text,
					Path = fullPath
				});
				return true;
			}
			catch
			{
				return false;
			}
		}

		private bool IsPathExistsAnywhere(string full)
		{
			foreach (AppCategory category in _app.Config.Categories)
			{
				if (category.Entries.OfType<AppItem>().Any((AppItem i) => string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase)))
				{
					return true;
				}
				foreach (AppFolder item in category.Entries.OfType<AppFolder>())
				{
					if (item.Items.Any((AppItem i) => string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase)))
					{
						return true;
					}
				}
			}
			return false;
		}

		public void RemoveEntry(AppEntry entry)
		{
			if (entry == null)
			{
				return;
			}
			foreach (AppCategory category in _app.Config.Categories)
			{
				int num = category.Entries.IndexOf(entry);
				if (num >= 0)
				{
					category.Entries.RemoveAt(num);
					if (entry is AppFolder appFolder)
					{
						for (int i = 0; i < appFolder.Items.Count; i++)
						{
							category.Entries.Insert(num + i, appFolder.Items[i]);
						}
					}
					AfterRemove();
					break;
				}
				foreach (AppFolder item in category.Entries.OfType<AppFolder>())
				{
					if (item.Items.Remove(entry as AppItem))
					{
						item.RefreshThumb();
						AfterRemove();
						return;
					}
				}
			}
		}

		private void AfterRemove()
		{
			_app.SaveConfig();
			if (_activeTab != null && _activeTab.IsAll)
			{
				RebuildAll();
			}
			else if (_activeTab != null)
			{
				_activeList = _activeTab.Category.Entries;
				IconGrid.ItemsSource = _activeList;
			}
			if (IsFolderOpen && _openFolder != null)
			{
				FolderTitle.Text = _openFolder.Name;
				if (_openFolder.Items.Count == 0)
				{
					if (IconGrid.ItemContainerGenerator.ContainerFromItem(_openFolder) is FrameworkElement frameworkElement)
					{
					HideFolderNow();
						frameworkElement.Opacity = 1.0;
					}
			}
		}
	}
}
}
