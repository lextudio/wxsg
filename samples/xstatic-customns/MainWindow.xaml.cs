using System.Collections.ObjectModel;
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
        }
    }
}
