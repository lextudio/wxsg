using System.Windows;

namespace DynamicResourceSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Hello from DynamicResource sample.");
    }
}