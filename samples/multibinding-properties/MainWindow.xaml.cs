using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace MultiBindingPropertySample;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private bool _showDetails = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FirstName => "WXSG";

    public string LastName => "Property MultiBinding";

    public bool HasDetails => true;

    public string Details => "This sample exercises MultiBinding on Text and Visibility.";

    public bool ShowDetails
    {
        get => _showDetails;
        set
        {
            if (_showDetails == value)
            {
                return;
            }

            _showDetails = value;
            OnPropertyChanged();
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
