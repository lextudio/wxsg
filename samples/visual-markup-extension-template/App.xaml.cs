using System;
using System.Threading.Tasks;
using System.Windows;

namespace VisualMarkupExtensionTemplateSample;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            Console.Error.WriteLine("WXSG-SAMPLE-ERROR: " + ev.ExceptionObject);
            Environment.Exit(1);
        };

        DispatcherUnhandledException += (_, ev) =>
        {
            Console.Error.WriteLine("WXSG-SAMPLE-ERROR: " + ev.Exception);
            ev.Handled = true;
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Console.Error.WriteLine("WXSG-SAMPLE-ERROR: " + ev.Exception);
            ev.SetObserved();
            Environment.Exit(1);
        };

        base.OnStartup(e);
    }
}
