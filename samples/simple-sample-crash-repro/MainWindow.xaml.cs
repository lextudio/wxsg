using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AvalonDock.Layout.Serialization;

namespace SimpleSampleCrashRepro
{
    public partial class MainWindow : Window
    {
        private readonly FakeShell _shell;
        private readonly object _outlineRoot = new();

        public MainWindow()
        {
            _shell = new FakeShell();
            DataContext = _shell;

            InitializeComponent();

            outlineDock.Loaded += OutlineDock_Loaded;
            _shell.CurrentDocument = new FakeDocument { OutlineRoot = _outlineRoot };

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

        private void OutlineDock_Loaded(object sender, RoutedEventArgs e)
        {
            const string layout =
                """
                <?xml version="1.0" encoding="utf-16"?>
                <LayoutRoot>
                  <RootPanel Orientation="Horizontal">
                    <LayoutPanel Orientation="Horizontal">
                      <LayoutAnchorablePaneGroup Orientation="Vertical" DockWidth="280">
                        <LayoutAnchorablePane DockHeight="3*">
                          <LayoutAnchorable AutoHideMinWidth="100" AutoHideMinHeight="100" Title="Outline" IsSelected="True" ContentId="Outline" CanClose="False" />
                        </LayoutAnchorablePane>
                      </LayoutAnchorablePaneGroup>
                    </LayoutPanel>
                  </RootPanel>
                  <TopSide />
                  <RightSide />
                  <LeftSide />
                  <BottomSide />
                  <FloatingWindows />
                  <Hidden />
                </LayoutRoot>
                """;

            var serializer = new XmlLayoutSerializer(outlineDock);
            using var reader = new StringReader(layout);
            serializer.Deserialize(reader);
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
            AssertStyle(typeof(FakeOutlineTree), "outline tree");
            ValidateAvalonDockOutlineBinding();

            if (cmbFontFamily.ItemsSource is null)
            {
                throw new InvalidOperationException("Unable to resolve {x:Static Member=Fonts.SystemFontFamilies}.");
            }

            if (!string.Equals(rootNameBindingProbe.Text, Title, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unable to resolve ElementName=root binding through the generated namescope.");
            }

            var focusVisualSetter = negativeCheckbox.Style.Setters
                .OfType<Setter>()
                .FirstOrDefault(setter => setter.Property == FrameworkElement.FocusVisualStyleProperty);
            if (focusVisualSetter?.Value is string)
            {
                throw new InvalidOperationException("Unable to resolve Setter.Value={StaticResource FocusVisual} for FocusVisualStyle.");
            }

            Console.WriteLine("OK: SimpleSample-style classless designer resources loaded as BAML and merged into Application.Resources.");
            Console.WriteLine("OK: x:Static Member=Fonts.SystemFontFamilies resolved for toolbar-style binding.");
            Console.WriteLine("OK: Setter.Value StaticResource resolved for FocusVisualStyle.");
            Console.WriteLine("OK: ElementName=root binding resolved through generated namescope.");
            Console.WriteLine("OK: XamlDesigner-style AvalonDock outline pane resolved CurrentDocument.OutlineRoot.");
            Console.WriteLine("OK: XamlDesigner-style outline tree theme template resolved.");
        }

        private void ValidateAvalonDockOutlineBinding()
        {
            if (!ReferenceEquals(avalonOutline.Root, _outlineRoot))
            {
                throw new InvalidOperationException(
                    "Unable to resolve XamlDesigner-style AvalonDock outline binding: Root={Binding CurrentDocument.OutlineRoot}.");
            }

            if (!ReferenceEquals(avalonOutline.InnerTreeRoot, _outlineRoot))
            {
                throw new InvalidOperationException(
                    "Unable to resolve Outline.xaml internal tree binding: Root={Binding Root, ElementName=root}.");
            }

            if (avalonOutline.InnerTreeItemCount != 1)
            {
                throw new InvalidOperationException(
                    "Outline tree did not populate its ItemsSource from the resolved Root property.");
            }

            avalonOutline.ApplyTemplate();
            if (!avalonOutline.ApplyInnerTreeTemplate())
            {
                throw new InvalidOperationException(
                    "Unable to apply XamlDesigner-style outline tree template from merged Generic.xaml resources.");
            }

            var restoredOutline = FindLayoutAnchorable(outlineDock.Layout, "Outline");

            if (!ReferenceEquals(restoredOutline?.Content, avalonOutline))
            {
                throw new InvalidOperationException(
                    "AvalonDock layout restore did not keep the generated outline control attached to the Outline pane.");
            }
        }

        private static AvalonDock.Layout.LayoutAnchorable? FindLayoutAnchorable(object? node, string contentId)
        {
            if (node is null)
            {
                return null;
            }

            if (node is AvalonDock.Layout.LayoutAnchorable anchorable &&
                string.Equals(anchorable.ContentId, contentId, StringComparison.Ordinal))
            {
                return anchorable;
            }

            foreach (var propertyName in new[] { "RootPanel", "TopSide", "RightSide", "BottomSide", "LeftSide", "Content" })
            {
                var child = node.GetType().GetProperty(propertyName)?.GetValue(node);
                var result = FindLayoutAnchorable(child, contentId);
                if (result is not null)
                {
                    return result;
                }
            }

            if (node.GetType().GetProperty("Children")?.GetValue(node) is System.Collections.IEnumerable children)
            {
                foreach (var child in children)
                {
                    var result = FindLayoutAnchorable(child, contentId);
                    if (result is not null)
                    {
                        return result;
                    }
                }
            }

            return null;
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

    public sealed class FakeShell : INotifyPropertyChanged
    {
        private FakeDocument? _currentDocument;

        public event PropertyChangedEventHandler? PropertyChanged;

        public FakeDocument? CurrentDocument
        {
            get => _currentDocument;
            set
            {
                if (ReferenceEquals(_currentDocument, value))
                {
                    return;
                }

                _currentDocument = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDocument)));
            }
        }
    }

    public sealed class FakeDocument
    {
        public object? OutlineRoot { get; init; }
    }

    public sealed class FakeOutlineTree : TreeView
    {
        static FakeOutlineTree()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(FakeOutlineTree),
                new FrameworkPropertyMetadata(typeof(FakeOutlineTree)));
        }

        public static readonly DependencyProperty RootProperty =
            DependencyProperty.Register(
                nameof(Root),
                typeof(object),
                typeof(FakeOutlineTree));

        public object? Root
        {
            get => GetValue(RootProperty);
            set => SetValue(RootProperty, value);
        }

        public string? Filter { get; set; }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == RootProperty)
            {
                ItemsSource = Root is null ? null : new[] { Root };
            }
        }
    }
}
