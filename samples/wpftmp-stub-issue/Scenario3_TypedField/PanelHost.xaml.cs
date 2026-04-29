using System;
using System.Threading.Tasks;
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
                Console.WriteLine("Double-clicked: " + listView.SelectedItem);
            };

            // CS1977 (without Scenario 3 fix): cannot use a lambda expression as an
            // argument to a dynamically dispatched operation.
            listView.SelectionChanged += (sender, e) =>
            {
                // handle selection change
            };

            this.ContentRendered += async (_, __) =>
            {
                try
                {
                    await Task.Yield();
                    Console.WriteLine("WXSG-SAMPLE-OK");
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("WXSG-SAMPLE-ERROR: " + ex);
                    Environment.Exit(1);
                }
            };
        }
    }
}
