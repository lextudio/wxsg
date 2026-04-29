using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SimpleSampleCrashRepro
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            TryLoadDesigner();
            this.ContentRendered += MainWindow_ContentRendered;
        }

        private void TryLoadDesigner()
        {
            try
            {
                // Mirrors WpfDesign's extension scan: corrupt custom-attribute blobs throw here.
                foreach (var type in typeof(MainWindow).Assembly.GetTypes())
                {
                    _ = type.GetCustomAttributes(typeof(ExtensionForAttribute), inherit: false)
                        .Cast<ExtensionForAttribute>()
                        .ToArray();
                }

                designSurface.LoadDesigner();
                Console.WriteLine("OK: fake designer load created DesignPanel.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("LoadDesigner FAILED: " + ex.GetType().FullName + ": " + ex.Message);
            }
        }

        private void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            // Trigger the same handler path as clicking a toolbox item in SimpleSample.
            lstControls.SelectedIndex = 2;
        }

        private void lstControls_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = lstControls.SelectedItem;
            if (item != null)
            {
                var tool = new CreateComponentTool(item.GetType());
                var designPanel = designSurface.DesignPanel;
                if (designPanel == null)
                {
                    throw new InvalidOperationException("DesignPanel is null after designer load; toolbox selection would crash like WpfDesigner SimpleSample.");
                }

                designPanel.Context.Services.Tool.CurrentTool = tool;
                Console.WriteLine("OK: toolbox selection set CurrentTool on DesignPanel.");
                Close();
            }
        }
    }
}
