using System.Windows;

namespace WpfTmpStubIssue.Scenario3_TypedField
{
    public partial class PanelHost : Window
    {
        public PanelHost()
        {
            InitializeComponent();

            // CS0019 (without Scenario 3 fix): operator '+=' cannot be applied to
            // operands of type 'dynamic' and 'anonymous method'.
            // 'listView' was 'dynamic' in the stub → C# can't infer delegate type.
            // After fix: field is 'internal ListView listView' → MouseDoubleClick is properly typed.
            listView.MouseDoubleClick += delegate
            {
                MessageBox.Show("Double-clicked: " + listView.SelectedItem);
            };

            // CS1977 (without Scenario 3 fix): cannot use a lambda expression as an
            // argument to a dynamically dispatched operation.
            listView.SelectionChanged += (sender, e) =>
            {
                // handle selection change
            };
        }
    }
}
