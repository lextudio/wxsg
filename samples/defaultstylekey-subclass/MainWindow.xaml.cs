using System;
using System.Windows;

namespace DefaultStyleKeySubclassSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        bool staticHasTemplate = staticBox.Template != null;
        SampleLog.Write(
            $"[Window.Loaded] staticBox.HasTemplate={staticHasTemplate}");
        statusText.Text = $"staticBox template: {(staticHasTemplate ? "OK" : "MISSING")}";
    }

    private void AddSubTextBox_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sub = new SubTextBox();
            try
            {
                dynamicHost.Content = sub;
            }
            catch (Exception ex)
            {
                SampleLog.Write($"[AddSubTextBox_Click] Exception while setting Content: {ex}");
                throw;
            }

            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() => {
                try
                {
                    // Assign text after the control is in the visual tree to avoid TextBox internal view NRE
                    try
                    {
                        sub.Text = "I am a dynamic SubTextBox";
                    }
                    catch (Exception tex)
                    {
                        SampleLog.Write($"[AddSubTextBox_Click] Exception while setting sub.Text: {tex}");
                    }

                    bool hasTemplate = sub.Template != null;
                    SampleLog.Write(
                        $"[AfterLayout] dynamicSubTextBox.HasTemplate={hasTemplate}");
                    statusText.Text = $"staticBox: {(staticBox.Template != null ? "OK" : "MISSING")} | dynamic SubTextBox: {(hasTemplate ? "OK" : "MISSING")}";
                }
                catch (Exception ex)
                {
                    SampleLog.Write($"[AddSubTextBox_Click][BeginInvoke] Exception in callback: {ex}");
                    throw;
                }
            }));
        }
        catch (Exception ex)
        {
            SampleLog.Write($"[AddSubTextBox_Click] Unhandled Exception: {ex}");
        }
    }
}
