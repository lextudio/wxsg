using System;
using System.Threading.Tasks;
using System.Windows;

namespace MultiBindingSample
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Console.Error.WriteLine(e.ExceptionObject?.ToString());
                Environment.Exit(1);
            };

            app.DispatcherUnhandledException += (s, e) =>
            {
                Console.Error.WriteLine(e.Exception?.ToString());
                e.Handled = true;
                Environment.Exit(1);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Console.Error.WriteLine(e.Exception?.ToString());
                e.SetObserved();
                Environment.Exit(1);
            };

            app.Resources["DataConverter"] = new DataConverter();
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
}
