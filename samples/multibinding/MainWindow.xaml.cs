using System.Windows;
using System.Collections.ObjectModel;

namespace MultiBindingSample
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<Item> _items;

        public ObservableCollection<Item> Items
        {
            get => _items;
            set => _items = value;
        }

        public MainWindow()
        {
            InitializeComponent();

            _items = new ObservableCollection<Item>
            {
                new Item { Name = "Item 1", IsActive = true },
                new Item { Name = "Item 2", IsActive = false },
                new Item { Name = "Item 3", IsActive = true },
                new Item { Name = "Apple", IsActive = true },
                new Item { Name = "Banana", IsActive = false },
            };

            DataContext = this;
        }
    }
}

public class Item
{
    public string? Name { get; set; }
    public bool IsActive { get; set; }

    public override string ToString() => Name ?? "";
}
