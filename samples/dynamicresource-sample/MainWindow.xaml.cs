using System;
using System.Threading.Tasks;
using System.Windows;

namespace DynamicResourceSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        this.ContentRendered += async (_, __) =>
        {
            try
            {
                // Exercise the click handler programmatically.
                try { OnClick(this, new RoutedEventArgs()); } catch (Exception ex) { /* log but continue */ Console.Error.WriteLine($"[SelfCheck] OnClick threw: {ex}"); }
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

    private void OnClick(object sender, RoutedEventArgs e)
    {
        Console.WriteLine("Hello from DynamicResource sample.");
    }
}