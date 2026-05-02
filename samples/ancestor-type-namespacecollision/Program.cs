using System;
using System.Windows;

namespace AncestorTypeNamespaceCollisionSample;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var application = new Application();
        application.Run(new Mapping.Mapping());
    }
}