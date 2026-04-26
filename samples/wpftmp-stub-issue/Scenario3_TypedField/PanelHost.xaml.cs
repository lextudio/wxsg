using System.Windows;
using System.Windows.Controls;

namespace WpfTmpStubIssue.Scenario3_TypedField
{
    public partial class PanelHost : Window
    {
        public PanelHost()
        {
            InitializeComponent();

            // CS1061 (without Scenario 2 fix): 'ItemsPanel' does not contain 'Children'.
            // ItemsPanel's stub had base=UserControl; UserControl.Children doesn't exist.
            // After Scenario 2 fix: base=Grid → Panel.Children is available.
            var toolbar = new Button { Content = "Toolbar" };
            panel.Children.Add(toolbar);

            // CS0019 (without Scenario 3 fix): operator '+=' cannot be applied to
            // operands of type 'dynamic' and 'anonymous method'.
            // 'panel.listView' was 'dynamic' in the stub → C# can't infer delegate type.
            // After fix: field is 'public ListView listView' → MouseDoubleClick is properly typed.
            panel.listView.MouseDoubleClick += delegate
            {
                MessageBox.Show("Double-clicked: " + panel.listView.SelectedItem);
            };

            // CS1977 (without Scenario 3 fix): cannot use a lambda expression as an
            // argument to a dynamically dispatched operation.
            panel.listView.SelectionChanged += (sender, e) =>
            {
                // handle selection change
            };
        }
    }
}
