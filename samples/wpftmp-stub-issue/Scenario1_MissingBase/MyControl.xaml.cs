using System.Windows;
using System.Windows.Controls;

namespace WpfTmpStubIssue.Scenario1_MissingBase
{
    // Real class — UserControl base comes from XAML x:Class + WPF code generation.
    // At runtime WXSG generates the full partial class; no issue there.
    //
    // In _wpftmp (MarkupCompilePass2 temporary assembly), WXSG has removed this
    // XAML from @(Page), so WPF never generates MyControl.g.cs.
    // Result: this partial class has NO base type declaration in _wpftmp.
    // The 'override' keyword then triggers CS0115.
    public partial class MyControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(int), typeof(MyControl));

        public int Value
        {
            get => (int)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        // CS0115 in _wpftmp: 'MyControl.OnPropertyChanged(...)' — no suitable method to override.
        // The base method (DependencyObject.OnPropertyChanged) is only reachable
        // once the base class (UserControl) is declared in the partial class.
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
        }
    }
}
