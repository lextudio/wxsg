using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace CompositeCollectionItemsSourceSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        StaticItems.Collection = new[] { "One", "Two" };
        DynamicItems.Collection = new ObservableCollection<string> { "Three", "Four" };

        ContentRendered += async (_, __) =>
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