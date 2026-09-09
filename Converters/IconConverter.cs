using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LaunchPad.Models;
using LaunchPad.Services;

namespace LaunchPad.Converters;

/// <summary>应用条目 → 图标。文件夹返回 null（使用矢量图形）。</summary>
public class IconConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is AppItem item)
            return IconService.GetIcon(item);
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public object Convert(object[] values, Type type, object parameter, CultureInfo culture)
        => Convert(values.FirstOrDefault(), type, parameter, culture);
    public object[] ConvertBack(object value, Type[] types, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
