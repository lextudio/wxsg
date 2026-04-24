using System;
using System.Windows;

namespace EventSetterLoadedSample
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("OnLoaded called");
        }
    }
}
