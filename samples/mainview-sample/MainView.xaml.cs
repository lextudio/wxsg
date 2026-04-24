using System.Windows;

namespace MainViewSample;

public partial class MainView : Window
{
    public MainView()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Hello from MainView sample.");
    }
}
