using System;
using System.Threading.Tasks;
using System.Windows;

namespace MainViewSample.CustomStartup;

public partial class MainView : Window
{
    public MainView()
    {
        InitializeComponent();

        this.ContentRendered += async (_, __) =>
        {
            try
            {
                // Invoke the click handler programmatically to exercise UI logic.
                try { OnClick(this, new RoutedEventArgs()); } catch (Exception ex) { Console.Error.WriteLine($"[SelfCheck] OnClick threw: {ex}"); }
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
        Console.WriteLine("Hello from MainView sample.");
    }
}
