using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace XStaticCustomNsSample
{
    public static class Converters
    {
        public static IMultiValueConverter CollectionsToComposite { get; } = new CollectionsToCompositeConverter();
    }

    public class CollectionsToCompositeConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3)
                return new ObservableCollection<Item>();

            var items = values[0] as ObservableCollection<Item>;
            var filter = values[1] as string ?? string.Empty;
            var activeOnly = values[2] is bool b && b;

            if (items == null)
                return new ObservableCollection<Item>();

            var filtered = items
                .Where(i => string.IsNullOrEmpty(filter) || (i.Name ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Where(i => !activeOnly || i.IsActive)
                .ToList();

            return new ObservableCollection<Item>(filtered);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class Item
    {
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }
}
