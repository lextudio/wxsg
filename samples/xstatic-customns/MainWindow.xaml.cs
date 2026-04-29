using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace XStaticCustomNsSample
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<Item> Items { get; } = new ObservableCollection<Item>();

        public MainWindow()
        {
            InitializeComponent();

            Items.Add(new Item { Name = "Alpha", IsActive = true });
            Items.Add(new Item { Name = "Beta", IsActive = false });
            Items.Add(new Item { Name = "Gamma", IsActive = true });

            DataContext = this;

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
