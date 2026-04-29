using System;
using System.Windows;

namespace DefaultStyleKeySubclassSample;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
        this.ContentRendered += async (_, __) =>
        {
            try
            {
                // Programmatic self-check: create a dynamic SubTextBox, attach it,
                // force template/layout application, and verify templates are present.
                var sub = new SubTextBox();
                dynamicHost.Content = sub;

                // Try to apply template and update layout synchronously.
                try { sub.ApplyTemplate(); } catch { }
                try { dynamicHost.UpdateLayout(); } catch { }

                // Give the dispatcher a turn to complete any remaining work.
                await System.Threading.Tasks.Task.Yield();

                bool staticHasTemplate = staticBox.Template != null;
                bool dynamicHasTemplate = sub.Template != null;

                SampleLog.Write($"[SelfCheck] staticBox.HasTemplate={staticHasTemplate} dynamicSub.HasTemplate={dynamicHasTemplate}");
                try { statusText.Text = $"staticBox: {(staticHasTemplate ? "OK" : "MISSING")} | dynamic SubTextBox: {(dynamicHasTemplate ? "OK" : "MISSING")}"; } catch { }

                if (staticHasTemplate && dynamicHasTemplate)
                {
                    Console.WriteLine("WXSG-SAMPLE-OK");
                    Environment.Exit(0);
                }
                else
                {
                    Console.Error.WriteLine($"WXSG-SAMPLE-ERROR: staticHas={staticHasTemplate} dynamicHas={dynamicHasTemplate}");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("WXSG-SAMPLE-ERROR: " + ex);
                Environment.Exit(1);
            }
        };
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
