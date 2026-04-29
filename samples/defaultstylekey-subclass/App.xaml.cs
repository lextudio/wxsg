using System;
using System.Threading.Tasks;
using System.Windows;

namespace DefaultStyleKeySubclassSample;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            Console.Error.WriteLine(ev.ExceptionObject?.ToString());
            Environment.Exit(1);
        };

        this.DispatcherUnhandledException += (_, ev) =>
        {
            try { Console.Error.WriteLine(ev.Exception?.ToString()); } catch { }
            ev.Handled = true;
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            try { Console.Error.WriteLine(ev.Exception?.ToString()); } catch { }
            ev.SetObserved();
            Environment.Exit(1);
        };

        base.OnStartup(e);
    }
}
