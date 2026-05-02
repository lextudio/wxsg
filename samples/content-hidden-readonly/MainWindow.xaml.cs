using System.Windows;
using System.Windows.Controls;

namespace ContentHiddenReadonlySample;

public partial class MainWindow : ReadonlyContentControl
{
    public MainWindow()
    {
        InitializeComponent();
    }
}

public class ReadonlyContentControl : UserControl
{
    public new Grid? Content => base.Content as Grid;
}