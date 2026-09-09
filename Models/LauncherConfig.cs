using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace LaunchPad.Models;

/// <summary>分类（Tab 栏中的一个分类）。</summary>
public class AppCategory : ObservableObject
{
    private string _name;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>该分类下的条目（应用与文件夹混排）。</summary>
    public ObservableCollection<AppEntry> Entries { get; set; } = new();
}

/// <summary>全局配置。</summary>
public class LauncherConfig
{
    /// <summary>呼出快捷键修饰键：Ctrl=2, Alt=1, Shift=4, Win=8（可组合）。</summary>
    public int HotkeyModifiers { get; set; } = 0x0002; // MOD_CONTROL

    /// <summary>呼出快捷键主键（虚拟键码）。</summary>
    public int HotkeyKey { get; set; } = 0x20; // VK_SPACE

    /// <summary>开机自启。</summary>
    public bool AutoStart { get; set; }

    /// <summary>启动时最小化到托盘。</summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>主题：Light / Dark / System。</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>Off / Fast / Balanced / Optimized：统一控制窗口、页面、文件夹和排序动画。</summary>
    public string AnimationMode { get; set; } = "Balanced";
    private double _folderHoverSeconds = 2, _categoryHoverSeconds = 2;
    public double FolderHoverSeconds
    {
        get => _folderHoverSeconds;
        set => _folderHoverSeconds = double.IsFinite(value) ? Math.Clamp(value, .3, 5) : 2;
    }
    public double CategoryHoverSeconds
    {
        get => _categoryHoverSeconds;
        set => _categoryHoverSeconds = double.IsFinite(value) ? Math.Clamp(value, .3, 5) : 2;
    }

    /// <summary>图标尺寸：Small / Medium / Large。</summary>
    public string IconSize { get; set; } = "Medium";

    /// <summary>弹窗位置：Center / Cursor。</summary>
    public string Position { get; set; } = "Center";

    /// <summary>启动方式：Single（单击启动）/ Double（双击启动）。</summary>
    public string LaunchMode { get; set; } = "Single";

    /// <summary>失焦时自动隐藏。</summary>
    public bool HideOnFocusLost { get; set; }

    /// <summary>启动应用后自动隐藏主窗口。</summary>
    public bool HideAfterLaunch { get; set; }

    public ObservableCollection<AppCategory> Categories { get; set; } = new();

    /// <summary>全部视图的全局条目顺序（AppEntry.Id 列表）：跨分类拖动排序时保持条目归属分类不变，
    /// 只调整全局显示顺序；分类内相对顺序由各分类 Entries 自身顺序决定。</summary>
    public List<string> GlobalOrder { get; set; } = new();

    /// <summary>查找分类（按名称，不存在则创建）。</summary>
    public AppCategory GetOrCreateCategory(string name)
    {
        var c = Categories.FirstOrDefault(x => x.Name == name);
        if (c != null) return c;
        c = new AppCategory { Name = name };
        Categories.Add(c);
        return c;
    }
}
