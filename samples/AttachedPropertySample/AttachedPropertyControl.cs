using System.Windows;
using System.Windows.Controls;

namespace XamlToCSharpGenerator.Samples.AttachedPropertySample
{
    public class AttachedPropertyControl : Control
    {
        public static readonly DependencyProperty ShowAlternationProperty =
            DependencyProperty.RegisterAttached(
                "ShowAlternation",
                typeof(bool),
                typeof(AttachedPropertyControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetShowAlternation(DependencyObject obj)
            => (bool)obj.GetValue(ShowAlternationProperty);

        public static void SetShowAlternation(DependencyObject obj, bool value)
            => obj.SetValue(ShowAlternationProperty, value);
    }
}
