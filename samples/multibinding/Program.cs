using System;
using System.Windows;

namespace MultiBindingSample
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application();
            app.Resources["DataConverter"] = new DataConverter();
            app.Run(new MainWindow());
        }
    }
}
