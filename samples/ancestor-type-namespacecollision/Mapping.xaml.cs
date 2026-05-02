using System.Windows;

namespace AncestorTypeNamespaceCollisionSample.Mapping;

public partial class Mapping : Window
{
    public Mapping()
    {
        InitializeComponent();
    }

    public string HeaderText => "Namespace collision resolved";

    public static string StaticText => "Static text resolved";
}