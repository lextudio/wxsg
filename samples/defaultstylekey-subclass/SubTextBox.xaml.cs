using System;
using System.Windows;

namespace DefaultStyleKeySubclassSample;

public partial class SubTextBox
{
    public SubTextBox()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SampleLog.Write(
            $"[SubTextBox.Loaded] HasTemplate={Template != null}, IsInitialized={IsInitialized}, Style={Style?.GetType().Name ?? "null"}, Background={Background?.ToString() ?? "null"}, Parent={Parent?.GetType().Name ?? "null"}, VisualParent={System.Windows.Media.VisualTreeHelper.GetParent(this)?.GetType().Name ?? "null"}");

        // Can TryFindResource find the style at all from this element?
        var foundViaThis = TryFindResource(typeof(CustomTextBox));
        var foundViaApp  = System.Windows.Application.Current?.TryFindResource(typeof(CustomTextBox));
        SampleLog.Write(
            $"[SubTextBox.ResourceLookup] TryFindResource(this)={foundViaThis != null}, TryFindResource(App)={foundViaApp != null}");

        // Try forcing the style explicitly and see if the template applies
        // if (foundViaApp is System.Windows.Style style)
        // {
        //     this.Style = style;
        //     bool applied = ApplyTemplate();
        //     SampleLog.Write(
        //         $"[SubTextBox.ForceStyle] AfterSetStyle HasTemplate={Template != null}, ApplyTemplateResult={applied}");
        // }
        // else
        // {
        //     bool applied = ApplyTemplate();
        //     SampleLog.Write(
        //         $"[SubTextBox.AfterApplyTemplate] ApplyTemplateResult={applied}, HasTemplate={Template != null}");
        // }
    }
}
