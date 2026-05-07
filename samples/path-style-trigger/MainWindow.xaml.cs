using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace PathStyleTriggerSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DataContext = new ProbeViewModel { IsInput = false };

        ContentRendered += async (_, __) =>
        {
            try
            {
                await Task.Yield();

                // Issue 1: confirm the style's TargetType resolved to Shapes.Path,
                // not System.IO.Path. If wrong, Style.set_TargetType would have
                // already thrown during InitializeComponent.
                if (probePath.Style is null)
                {
                    throw new InvalidOperationException(
                        "Style was not applied to probePath; TargetType resolution likely wrong.");
                }

                if (probePath.Width != 20)
                {
                    throw new InvalidOperationException(
                        $"Expected Path.Width=20 from style, got {probePath.Width}.");
                }

                // Issue 2: the DataTrigger fires when IsInput=false. Verify the
                // override Setter for Stroke=Blue applied (which proves Value="False"
                // matched the bound bool false).
                if (probePath.Stroke is not SolidColorBrush brush || brush.Color != Colors.Blue)
                {
                    throw new InvalidOperationException(
                        $"Expected Stroke=Blue from DataTrigger (IsInput=false), got {probePath.Stroke}.");
                }

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

public sealed class ProbeViewModel
{
    public bool IsInput { get; set; }
}
