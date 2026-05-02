using System;
using System.Windows;

namespace InlineTextContentSample;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var application = new Application();
        application.Run(new MainWindow());
    }
}