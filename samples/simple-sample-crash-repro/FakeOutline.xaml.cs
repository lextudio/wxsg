using System;
using System.Windows;
using System.Windows.Controls;

namespace SimpleSampleCrashRepro
{
    public partial class FakeOutline : UserControl
    {
        public FakeOutline()
        {
            SpecialInitializeComponent();
        }

        public void SpecialInitializeComponent()
        {
            if (!this._contentLoaded)
            {
                this._contentLoaded = true;
                var resourceLocator = new Uri("FakeOutline.xaml", UriKind.Relative);
                Application.LoadComponent(this, resourceLocator);
            }

            this.InitializeComponent();
        }

        public static readonly DependencyProperty RootProperty =
            DependencyProperty.Register(
                nameof(Root),
                typeof(object),
                typeof(FakeOutline));

        public object? Root
        {
            get => GetValue(RootProperty);
            set => SetValue(RootProperty, value);
        }

        public object? InnerTreeRoot => OutlineTreeView.Root;

        public int InnerTreeItemCount => OutlineTreeView.Items.Count;

        public bool ApplyInnerTreeTemplate()
        {
            OutlineTreeView.ApplyTemplate();
            return OutlineTreeView.Template is not null;
        }
    }
}
