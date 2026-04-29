using System;
using System.Threading.Tasks;
using System.Windows;

namespace XamlToCSharpGenerator.Samples.OnStartup
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.ContentRendered += async (_, __) =>
            {
                try
                {
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
    }
}
