using System.Windows;
using System.Windows.Controls;

namespace ClasslessOnlyLibrary;

/// <summary>
/// A custom TextBox that declares a default style key so WPF looks up its
/// template from the assembly's Themes/Generic.xaml resource dictionary.
/// This is the same pattern used in AvalonEdit.AddIn controls that were
/// showing blank/unstyled due to __WxsgThemeLoader not being generated.
/// </summary>
public class CustomTextBox : TextBox
{
    static CustomTextBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(CustomTextBox),
            new FrameworkPropertyMetadata(typeof(CustomTextBox)));
    }
}
