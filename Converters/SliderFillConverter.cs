using System;
using System.Globalization;
using System.Windows.Data;

namespace LaunchPad.Converters;

/// <summary>滑块填充宽度：按 (Value-Min)/(Max-Min) 比例映射到轨道宽度，
/// 并扣除半个滑块直径，使填充始终恰好延伸到滑块中心（类似 iOS 风格滑块）。</summary>
public class SliderFillConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 4 &&
            values[0] is double value && values[1] is double min && values[2] is double max &&
            values[3] is double track && track > 0 && max > min)
        {
            double knob = values.Length >= 5 && values[4] is double k && k > 0 && !double.IsNaN(k) ? k : 16;
            var t = Math.Clamp((value - min) / (max - min), 0, 1);
            return knob / 2 + t * Math.Max(0, track - knob);
        }
        return 0d;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
