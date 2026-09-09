using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LaunchPad.Converters;

/// <summary>文件夹数量 → 角标可见性：≤4 不显示角标（缩略图 2×2 已展示），超过 4 才显示。</summary>
public class CountToBadgeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count && count > 4)
            return Visibility.Visible;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
