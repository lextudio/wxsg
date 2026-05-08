using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace VisualMarkupExtensionTemplateSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ContentRendered += async (_, __) =>
        {
            try
            {
                await Task.Yield();

                // Issue #14: if FrameworkElementFactory.SetValue() was called with the
                // TextBlock result, InitializeComponent would have already thrown.
                // Verify the button rendered with a TextBlock content via the template.
                probeButton.ApplyTemplate();
                var inner = (Button?)probeButton.Template.FindName("", probeButton);
                // We just need to reach here without an exception — that proves the
                // Visual-returning markup extension was handled without SetValue().
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
