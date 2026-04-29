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
            ValidateDesignerResources();

            // Trigger the same handler path as clicking a toolbox item in SimpleSample.
            lstControls.SelectedIndex = 2;
        }

        private void ValidateDesignerResources()
        {
            AssertBamlResource("pack://application:,,,/SimpleSampleCrashRepro;component/Themes/Generic.xaml");
            AssertBamlResource("pack://application:,,,/SimpleSampleCrashRepro;component/DesignSurface.xaml");
            AssertBamlResource("pack://application:,,,/SimpleSampleCrashRepro;component/PropertyGrid/PropertyGridView.xaml");

            AssertStyle(typeof(FakeDesignSurface), "designer surface");
            AssertStyle(typeof(FakePropertyGridView), "property grid");
            AssertStyle(typeof(ProbeControl), "theme generic probe control");
            if (cmbFontFamily.ItemsSource is null)
            {
                throw new InvalidOperationException("Unable to resolve {x:Static Member=Fonts.SystemFontFamilies}.");
            }

            Console.WriteLine("OK: SimpleSample-style classless designer resources loaded as BAML and merged into Application.Resources.");
            Console.WriteLine("OK: x:Static Member=Fonts.SystemFontFamilies resolved for toolbar-style binding.");
        }

        private static void AssertBamlResource(string uriText)
        {
            var info = Application.GetResourceStream(new Uri(uriText, UriKind.Absolute));
            if (info?.Stream == null)
            {
                throw new InvalidOperationException("Missing pack resource: " + uriText);
            }

            if (!string.Equals(info.ContentType, "application/baml+xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected BAML resource for {uriText}, but got '{info.ContentType}'.");
            }
        }

        private static void AssertStyle(Type targetType, string description)
        {
            var style = Application.Current.TryFindResource(targetType);
            if (style is not System.Windows.Style)
            {
                throw new InvalidOperationException($"Missing implicit style for {description} ({targetType.FullName}).");
            }
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
