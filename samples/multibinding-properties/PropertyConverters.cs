using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MultiBindingPropertySample;

public sealed class SummaryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var first = values.Length > 0 ? values[0] as string : string.Empty;
        var last = values.Length > 1 ? values[1] as string : string.Empty;
        return string.Concat(first, " ", last).Trim();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public sealed class DetailsVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var showDetails = values.Length > 0 && values[0] is bool visible && visible;
        var hasDetails = values.Length > 1 && values[1] is bool hasValue && hasValue;
        return showDetails && hasDetails ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
