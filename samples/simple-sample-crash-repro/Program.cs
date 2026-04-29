using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace SimpleSampleCrashRepro
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            ValidateExtensionForAttributes();
            Console.WriteLine("OK: ExtensionForAttribute Type and Type[] metadata is reflectable.");

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

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Console.Error.WriteLine("UnobservedTaskException: " + e.Exception.ToString());
                e.SetObserved();
                Environment.Exit(1);
            };

            try
            {
                app.Run(new MainWindow());
                Console.WriteLine("OK: SimpleSample-style designer load and toolbox selection completed without crashing.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Run exception: " + ex.ToString());
                Environment.Exit(1);
            }
        }

        private static void ValidateExtensionForAttributes()
        {
            foreach (var type in typeof(Program).Assembly.GetTypes())
            {
                var attributes = type.GetCustomAttributes(typeof(ExtensionForAttribute), inherit: false)
                    .Cast<ExtensionForAttribute>()
                    .ToArray();

                foreach (var attribute in attributes)
                {
                    if (attribute.DesignedItemType == typeof(ProbeControl))
                    {
                        var overrides = attribute.OverrideExtensions;
                        if (overrides.Length != 2 ||
                            overrides[0] != typeof(ResizeProbeExtension) ||
                            overrides[1] != typeof(SelectionProbeExtension))
                        {
                            throw new InvalidOperationException("Unexpected OverrideExtensions on ExtensionForAttribute.");
                        }

                        continue;
                    }

                    if (attribute.DesignedItemType == typeof(System.Windows.Controls.TextBox))
                    {
                        if (attribute.OverrideExtension != typeof(SelectionProbeExtension))
                        {
                            throw new InvalidOperationException("Unexpected OverrideExtension on ExtensionForAttribute.");
                        }

                        continue;
                    }

                    throw new InvalidOperationException("Unexpected DesignedItemType on ExtensionForAttribute.");
                }
            }
        }
    }
}
