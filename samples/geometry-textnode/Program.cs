using System;
using System.Threading.Tasks;
using System.Windows;

namespace GeometryTextNodeSample;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.ExceptionObject?.ToString());
            Environment.Exit(1);
        };

        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.Error.WriteLine(e.Exception?.ToString());
            e.Handled = true;
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine(e.Exception?.ToString());
            e.SetObserved();
            Environment.Exit(1);
        };

        try
        {
            app.Run(new MainWindow());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Run exception: " + ex);
            Environment.Exit(1);
        }
    }
}