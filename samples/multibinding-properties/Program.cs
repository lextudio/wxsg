using System.Windows;

namespace MultiBindingPropertySample;

public static class Program
{
    [System.STAThread]
    public static void Main()
    {
        var app = new Application();
        app.Resources["SummaryConverter"] = new SummaryConverter();
        app.Resources["DetailsVisibilityConverter"] = new DetailsVisibilityConverter();
        app.Run(new MainWindow());
    }
}
