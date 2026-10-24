using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SL.Pres.Infrastructure;

/// <summary>bool → Visibility(可反转)。</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var on = value is bool b && b;
        if (Invert) on = !on;
        return on ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>value 与 parameter 相等 → Visibility(适合 "字符串 == 键" 的场景;parameter 缺省视为空串)。</summary>
public sealed class EqualToVisibility : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var eq = string.Equals(value?.ToString() ?? "", parameter?.ToString() ?? "", StringComparison.Ordinal);
        if (Invert) eq = !eq;
        return eq ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>int / enum 等值(true/false,用于 Tab 选中态绑定)。</summary>
public sealed class IntEquals : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int actual;
        try
        {
            actual = System.Convert.ToInt32(value);
        }
        catch
        {
            return false;
        }
        return int.TryParse(parameter?.ToString(), out var expected) && actual == expected;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>字符串等值(bool 化,用于「模式 chip」的双向绑定)。</summary>
public sealed class StringEquals : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var eq = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
        return Invert ? !eq : eq;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter?.ToString() ?? Binding.DoNothing : Binding.DoNothing;
}

/// <summary>多绑定等值：用于列表项「自身 Key == 当前 Key」的选中态。</summary>
public sealed class KeyIsCurrent : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length >= 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => (object)Binding.DoNothing).ToArray();
}

/// <summary>字节数 → 人类可读文本(1.2 MB / 612.5 KB);负值显示 "—"。</summary>
public sealed class BytesText : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not long l || l < 0 ? "—" : FormatBytes(l);

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return u == 0 ? $"{v:0} {units[u]}" : $"{v:0.0} {units[u]}";
    }

    public static string FormatSpeed(double bytesPerSecond)
        => bytesPerSecond <= 0 ? "—" : FormatBytes((long)bytesPerSecond) + "/s";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
