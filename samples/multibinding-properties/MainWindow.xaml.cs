using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
