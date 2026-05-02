using System;
using System.Windows;

namespace ContentHiddenReadonlySample;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var application = new Application();
        _ = new MainWindow();
        application.Shutdown();
    }
}