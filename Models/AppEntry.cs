using System.Text.Json.Serialization;

namespace LaunchPad.Models;

/// <summary>网格中的条目基类：应用 或 文件夹。</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(AppItem), "app")]
[JsonDerivedType(typeof(AppFolder), "folder")]
public abstract class AppEntry : ObservableObject
{
    private string _name;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }
}

/// <summary>单个应用/文件条目。</summary>
public class AppItem : AppEntry
{
    private string _customIconPath;
    public string CustomIconPath
    {
        get => _customIconPath;
        set => Set(ref _customIconPath, value);
    }
    /// <summary>文件/快捷方式/可执行文件的完整路径。</summary>
    public string Path { get; set; }

    /// <summary>启动参数（可选）。</summary>
    public string Args { get; set; }

    /// <summary>是否为快捷方式（.lnk）。</summary>
    [JsonIgnore]
    public bool IsLnk => Path != null && Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase);
}

/// <summary>文件夹：容纳多个应用条目的分组（类似 iOS 文件夹）。</summary>
public class AppFolder : AppEntry
{
    public List<AppItem> Items { get; set; } = new();

    /// <summary>格子缩略图只展示前 4 个（2×2），避免文件多时溢出图标。</summary>
    [JsonIgnore]
    public List<AppItem> ThumbItems => Items.Take(4).ToList();

    /// <summary>Items 增删/重排后调用，刷新缩略图。</summary>
    public void RefreshThumb()
    {
        OnPropertyChanged(nameof(ThumbItems));
        OnPropertyChanged(nameof(Items));
    }
}
