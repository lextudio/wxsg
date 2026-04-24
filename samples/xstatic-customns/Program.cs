using System;
using System.Windows;

namespace XStaticCustomNsSample
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            // Provide an "inspect" mode to reflectively call the generated __WXSG_ResolveXStatic method
            // Usage: dotnet run --project <csproj> inspect
            var args = System.Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1] == "inspect")
            {
                var logFile = @"C:\temp\wxsg-inspect.log";
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logFile));
                try
                {
                    var asm = System.Reflection.Assembly.LoadFrom(typeof(Program).Assembly.Location);
                    foreach (var t in asm.GetTypes())
                    {
                        var m = t.GetMethod("__WXSG_ResolveXStatic", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        if (m != null)
                        {
                            var msg = $"Found __WXSG_ResolveXStatic in {t.FullName}";
                            Console.WriteLine(msg);
                            System.IO.File.AppendAllText(logFile, msg + Environment.NewLine);
                            try
                            {
                                var token = "{x:Static p:Converters.CollectionsToComposite}";
                                var res = m.Invoke(null, new object[] { token });
                                var result = "Invoke result: " + (res?.ToString() ?? "<null>");
                                Console.WriteLine(result);
                                System.IO.File.AppendAllText(logFile, result + Environment.NewLine);
                            }
                            catch (System.Reflection.TargetInvocationException tie)
                            {
                                var error = "Invocation threw: " + tie.InnerException?.ToString();
                                Console.WriteLine(error);
                                System.IO.File.AppendAllText(logFile, error + Environment.NewLine);
                            }
                            catch (Exception ex)
                            {
                                var error = "Invoke exception: " + ex.ToString();
                                Console.WriteLine(error);
                                System.IO.File.AppendAllText(logFile, error + Environment.NewLine);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    var error = "Inspect failure: " + ex.ToString();
                    Console.WriteLine(error);
                    System.IO.File.AppendAllText(logFile, error + Environment.NewLine);
                }
                finally
                {
                    var msg = $"Log written to {logFile}";
                    Console.WriteLine(msg);
                }

                return;
            }

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
