using System;
using System.Windows;

namespace EventSetterLoadedSample
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Console.Error.WriteLine("UnhandledException: " + e.ExceptionObject?.ToString());
                Environment.Exit(1);
            };

            app.DispatcherUnhandledException += (s, e) =>
            {
                Console.Error.WriteLine("DispatcherUnhandledException: " + e.Exception.ToString());
                e.Handled = true;
                Environment.Exit(1);
            };

            try
            {
                app.Run(new MainWindow());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Run exception: " + ex.ToString());
                Environment.Exit(1);
            }
        }
    }
}
