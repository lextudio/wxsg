using System;
using System.Windows;

namespace XamlToCSharpGenerator.Samples.OnStartup
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Console.WriteLine("OnStartup called");
        }
    }
}
