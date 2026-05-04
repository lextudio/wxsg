using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

internal static class Program
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static readonly string XamlDesignerProject = Path.Combine(RepoRoot, "WpfDesigner", "XamlDesigner");
    private static readonly string XamlDesignerOutput = Path.Combine(RepoRoot, "WpfDesigner", "XamlDesigner", "bin", "Debug", "net10.0-windows");
    private static readonly string SharpDevelopRoot = Path.Combine(RepoRoot, "SharpDevelop");
    private static readonly string SharpDevelopBin = Path.Combine(SharpDevelopRoot, "bin");
    private static readonly string SharpDevelopExe = Path.Combine(SharpDevelopBin, "SharpDevelop.exe");
    private static readonly string SharpDevelopStartPage = Path.Combine(SharpDevelopRoot, "AddIns", "Misc", "StartPage", "StartPage.dll");
    private static readonly string[] DefaultExcludedAddInPatterns = { "TypeScriptBinding" };
    private static readonly string DefaultTestConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "SharpDevelop Projects", "test-console", "app.config");
    private static WeakReference<FrameworkElement>? LastProbedCodeEditor;

    [STAThread]
    private static int Main(string[] args)
    {
        var mode = args.FirstOrDefault()?.Trim().ToLowerInvariant();
        var modeArgs = args.Skip(1).ToArray();
        if (mode == "--mode=xamldesigner" || mode == "xamldesigner")
        {
            return RunXamlDesignerInspector();
        }

        if (mode == "--mode=sharpdevelop-workbench" || mode == "sharpdevelop-workbench")
        {
            return RunSharpDevelopWorkbenchInspector(modeArgs);
        }

        return RunSharpDevelopStartPageInspector();
    }

    private static int RunSharpDevelopWorkbenchInspector(string[] args)
    {
        Directory.SetCurrentDirectory(SharpDevelopRoot);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromSharpDevelopOutputs;
        EnableBindingDiagnostics();
        InstallUnhandledExceptionConsoleLogging();

        var excludedAddIns = GetExcludedAddInPatterns(args);

        Console.WriteLine("SharpDevelop root: " + SharpDevelopRoot);
        Console.WriteLine("SharpDevelop exe: " + SharpDevelopExe + " (exists=" + File.Exists(SharpDevelopExe) + ")");
        Console.WriteLine("Excluded add-in patterns: " + string.Join(", ", excludedAddIns));

        if (!File.Exists(SharpDevelopExe))
        {
            Console.WriteLine("SharpDevelop executable not found. Build SharpDevelop first.");
            return 2;
        }

        var sharpDevelopAssembly = Assembly.LoadFrom(SharpDevelopExe);
        DisableSharpDevelopUnhandledExceptionUi(sharpDevelopAssembly);
        DumpAssemblyResources(sharpDevelopAssembly, "SharpDevelop");

        var startupAppType = sharpDevelopAssembly.GetType("ICSharpCode.SharpDevelop.Startup.App", throwOnError: false);
        Console.WriteLine("Startup App type: " + Describe(startupAppType));
        if (Application.Current != null)
        {
            Application.Current.DispatcherUnhandledException += (_, e) =>
            {
                Console.WriteLine("DISPATCHER EXCEPTION: " + e.Exception);
                e.Handled = true;
            };
        }

        var workbenchType = sharpDevelopAssembly.GetType("ICSharpCode.SharpDevelop.Workbench.WpfWorkbench", throwOnError: false);
        Console.WriteLine("WpfWorkbench type: " + Describe(workbenchType));
        object? workbenchInstance = null;
        if (workbenchType != null)
        {
            try
            {
                workbenchInstance = Activator.CreateInstance(workbenchType, nonPublic: true);
                Console.WriteLine("WpfWorkbench instance: " + Describe(workbenchInstance));
            }
            catch (Exception ex)
            {
                Console.WriteLine("WpfWorkbench creation failed:");
                DumpException(ex, "  ");
            }
        }

        Window? workbenchWindow = workbenchInstance as Window;
        if (workbenchWindow == null)
        {
            Console.WriteLine("Falling back to SharpDevelopHost + WorkbenchStartup initialization...");
            if (!TryBootstrapWorkbenchFromHost(sharpDevelopAssembly, excludedAddIns, out workbenchWindow))
            {
                Application.Current?.Shutdown();
                return 4;
            }

            // SharpDevelop host bootstrap may register ExceptionBox handlers; force-disable again.
            DisableSharpDevelopUnhandledExceptionUi(sharpDevelopAssembly);
        }

        if (Application.Current == null)
        {
            var app = new Application();
            app.DispatcherUnhandledException += (_, e) =>
            {
                Console.WriteLine("DISPATCHER EXCEPTION: " + e.Exception);
                e.Handled = true;
            };
        }

        if (workbenchWindow != null)
        {
            try
            {
                workbenchWindow.Show();
                Pump();
                Pump();
                workbenchWindow.UpdateLayout();
                Pump();
            }
            catch (Exception ex)
            {
                Console.WriteLine("WpfWorkbench.Show failed:");
                DumpException(ex, "  ");
            }

            DumpResourceDictionaries("Application.Current.Resources", Application.Current?.Resources, depth: 0, maxDepth: 2, maxKeysPerDictionary: 40);
            DumpElementRuntimeState("WpfWorkbench", workbenchWindow);

            var dockPanel = GetFieldValue(workbenchWindow, "dockPanel") as FrameworkElement;
            Console.WriteLine("WpfWorkbench.dockPanel: " + Describe(dockPanel));
            if (dockPanel != null)
            {
                DumpElementRuntimeState("WpfWorkbench.dockPanel", dockPanel);
            }

            var mainContent = GetFieldValue(workbenchWindow, "mainContent") as FrameworkElement;
            Console.WriteLine("WpfWorkbench.mainContent: " + Describe(mainContent));
            if (mainContent != null)
            {
                DumpElementRuntimeState("WpfWorkbench.mainContent", mainContent);

                if (mainContent is ContentPresenter presenter)
                {
                    var contentRoot = presenter.Content;
                    Console.WriteLine("WpfWorkbench.mainContent.Content: " + Describe(contentRoot));
                    if (contentRoot is FrameworkElement contentElement)
                    {
                        DumpElementRuntimeState("WpfWorkbench.mainContent.Content", contentElement);
                    }

                    if (contentRoot is DependencyObject contentObject)
                    {
                        DumpVisualTreeSnapshot(contentObject, maxDepth: 3, maxChildrenPerNode: 30);
                    }
                }
            }

            DumpVisualTreeSnapshot(workbenchWindow, maxDepth: 4, maxChildrenPerNode: 30);

            // Do not close the workbench window in diagnostics mode.
            // Closing can trigger AvalonDock layout serialization while unloaded,
            // which raises the exact unhandled exception we are diagnosing.
            try
            {
                workbenchWindow.Hide();
            }
            catch
            {
            }
        }

        // Debug WXSG: Check for generated theme loaders and classless pages
        Console.WriteLine("\n=== WXSG DIAGNOSTICS ===");
        DumpWxsgDiagnostics(sharpDevelopAssembly);

        // Probe AvalonEdit TextEditor rendering (the "text shown but not rendered" issue)
        var fileToOpen = ResolveTargetFileToOpen(args);
        if (!string.IsNullOrWhiteSpace(fileToOpen))
        {
            Console.WriteLine("\n=== OPEN TARGET FILE ===");
            OpenTargetFileAndProbeEditor(fileToOpen!, sharpDevelopAssembly, workbenchWindow);
        }

        Console.WriteLine("\n=== AVALONEDIT.ADDIN PROBE ===");
        ProbeAvalonEditAddInThemeLoader();

        Console.WriteLine("\n=== RE-PROBE ACTIVE EDITOR (POST THEME LOADER) ===");
        ReprobeActiveEditorAfterThemeRegistration(sharpDevelopAssembly);

        Console.WriteLine("\n=== AVALONEDI TEXEDITOR PROBE ===");
        ProbeAvalonEditTextEditor();

        // Avoid Application shutdown in this probe mode for the same reason as above.
        // Let process teardown end the probe without invoking full SharpDevelop shutdown paths.
        return 0;
    }

    private static string? ResolveTargetFileToOpen(string[] args)
    {
        const string key = "--open-file=";
        var configured = args
            .FirstOrDefault(a => a.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Substring(key.Length).Trim('"');

        return DefaultTestConfigPath;
    }

    private static void OpenTargetFileAndProbeEditor(string filePath, Assembly sharpDevelopAssembly, Window? workbenchWindow)
    {
        Console.WriteLine("  Target file: " + filePath + " (exists=" + File.Exists(filePath) + ")");
        if (!File.Exists(filePath))
            return;

        var fileServiceType = FindType("ICSharpCode.SharpDevelop.FileService", sharpDevelopAssembly);
        var openFileMethod = fileServiceType?.GetMethod(
            "OpenFile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);

        if (openFileMethod == null)
        {
            Console.WriteLine("  Could not find SharpDevelop.FileService.OpenFile(string)");
            return;
        }

        try
        {
            openFileMethod.Invoke(null, new object[] { filePath });
            Pump();
            Pump();
            workbenchWindow?.UpdateLayout();
            Pump();
            Console.WriteLine("  OpenFile invoked successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  OpenFile invocation failed:");
            DumpException(ex, "    ");
            return;
        }

        DumpActiveViewContentState(sharpDevelopAssembly);

        if (workbenchWindow != null)
        {
            var codeEditorElement = FindVisualDescendantByTypeName(workbenchWindow, "CodeEditor");
            Console.WriteLine("  Visual tree CodeEditor: " + Describe(codeEditorElement));
            if (codeEditorElement is FrameworkElement codeEditorFrameworkElement)
            {
                LastProbedCodeEditor = new WeakReference<FrameworkElement>(codeEditorFrameworkElement);
                DumpElementRuntimeState("CodeEditor", codeEditorFrameworkElement);
                DumpEditorTemplateState(codeEditorFrameworkElement, "CodeEditor subtree");
                DumpCodeEditorDeepState(codeEditorFrameworkElement);
            }
        }
    }

    private static void DumpEditorTemplateState(DependencyObject root, string label)
    {
        Console.WriteLine("--- " + label + " template state ---");
        var sharpDevelopTextEditor = FindVisualDescendantByTypeName(root, "SharpDevelopTextEditor");
        Console.WriteLine("  SharpDevelopTextEditor: " + Describe(sharpDevelopTextEditor));
        if (sharpDevelopTextEditor is FrameworkElement sdEditorElement)
        {
            Console.WriteLine("    IsLoaded=" + sdEditorElement.IsLoaded + ", ActualWidth=" + sdEditorElement.ActualWidth + ", ActualHeight=" + sdEditorElement.ActualHeight);
            if (sdEditorElement is Control sdEditorControl)
            {
                Console.WriteLine("    Template=" + Describe(sdEditorControl.Template));
            }
        }

        var avalonTextEditor = FindVisualDescendantByTypeName(root, "ICSharpCode.AvalonEdit.TextEditor");
        Console.WriteLine("  AvalonEdit.TextEditor: " + Describe(avalonTextEditor));
        if (avalonTextEditor is FrameworkElement avEditorElement)
        {
            Console.WriteLine("    IsLoaded=" + avEditorElement.IsLoaded + ", ActualWidth=" + avEditorElement.ActualWidth + ", ActualHeight=" + avEditorElement.ActualHeight);
            if (avEditorElement is Control avEditorControl)
            {
                Console.WriteLine("    Template=" + Describe(avEditorControl.Template));
            }
        }
    }

    private static void DumpCodeEditorDeepState(FrameworkElement codeEditor)
    {
        Console.WriteLine("--- CodeEditor deep composition ---");
        Console.WriteLine("  Type: " + codeEditor.GetType().FullName);
        Console.WriteLine("  VisualChildrenCount=" + VisualTreeHelper.GetChildrenCount(codeEditor));

        if (codeEditor is Panel panel)
        {
            Console.WriteLine("  Panel.Children.Count=" + panel.Children.Count);
            for (var i = 0; i < panel.Children.Count; i++)
            {
                var child = panel.Children[i];
                Console.WriteLine("    Child[" + i + "]: " + Describe(child));
                if (child is FrameworkElement childElement)
                {
                    Console.WriteLine("      IsLoaded=" + childElement.IsLoaded + ", ActualWidth=" + childElement.ActualWidth + ", ActualHeight=" + childElement.ActualHeight);
                    Console.WriteLine("      Style=" + Describe(childElement.Style));
                    if (childElement is Control childControl)
                    {
                        var childDefaultStyleKey = GetDefaultStyleKey(childControl);
                        Console.WriteLine("      DefaultStyleKey=" + Describe(childDefaultStyleKey));
                        var childImplicitStyle = childDefaultStyleKey != null ? childControl.TryFindResource(childDefaultStyleKey) as Style : null;
                        Console.WriteLine("      TryFindResource(DefaultStyleKey)=" + Describe(childImplicitStyle));
                        DumpStyleDetails("      Implicit style", childImplicitStyle);
                        DumpStyleDetails("      Control.Style", childControl.Style);
                        Console.WriteLine("      Template=" + Describe(childControl.Template));
                        var styleSource = DependencyPropertyHelper.GetValueSource(childControl, FrameworkElement.StyleProperty);
                        var templateSource = DependencyPropertyHelper.GetValueSource(childControl, Control.TemplateProperty);
                        Console.WriteLine("      Style ValueSource: Base=" + styleSource.BaseValueSource + ", IsExpression=" + styleSource.IsExpression + ", IsAnimated=" + styleSource.IsAnimated + ", IsCoerced=" + styleSource.IsCoerced + ", IsCurrent=" + styleSource.IsCurrent);
                        Console.WriteLine("      Template ValueSource: Base=" + templateSource.BaseValueSource + ", IsExpression=" + templateSource.IsExpression + ", IsAnimated=" + templateSource.IsAnimated + ", IsCoerced=" + templateSource.IsCoerced + ", IsCurrent=" + templateSource.IsCurrent);
                        DumpControlColorState("      ", childControl);
                    }
                }
            }
        }

        Console.WriteLine("  Logical children:");
        try
        {
            var i = 0;
            foreach (var child in LogicalTreeHelper.GetChildren(codeEditor))
            {
                Console.WriteLine("    Logical[" + i + "]: " + Describe(child));
                i++;
            }
            if (i == 0)
                Console.WriteLine("    <none>");
        }
        catch (Exception ex)
        {
            Console.WriteLine("    Logical children probe failed: " + ex.GetType().Name + ": " + ex.Message);
        }

        // CodeEditor owns its main editor via private readonly field `primaryTextEditor`.
        // Read it directly to prove whether construction happened even if tree attachment failed.
        try
        {
            var field = codeEditor.GetType().GetField("primaryTextEditor", BindingFlags.Instance | BindingFlags.NonPublic);
            var primaryEditor = field?.GetValue(codeEditor);
            Console.WriteLine("  private field primaryTextEditor: " + Describe(primaryEditor));
            if (primaryEditor is FrameworkElement primaryEditorElement)
            {
                Console.WriteLine("    IsLoaded=" + primaryEditorElement.IsLoaded + ", ActualWidth=" + primaryEditorElement.ActualWidth + ", ActualHeight=" + primaryEditorElement.ActualHeight);
                Console.WriteLine("    Style=" + Describe(primaryEditorElement.Style));
                if (primaryEditorElement is Control primaryEditorControl)
                {
                    var primaryDefaultStyleKey = GetDefaultStyleKey(primaryEditorControl);
                    Console.WriteLine("    DefaultStyleKey=" + Describe(primaryDefaultStyleKey));
                    var primaryImplicitStyle = primaryDefaultStyleKey != null ? primaryEditorControl.TryFindResource(primaryDefaultStyleKey) as Style : null;
                    Console.WriteLine("    TryFindResource(DefaultStyleKey)=" + Describe(primaryImplicitStyle));
                    DumpStyleDetails("    Implicit style", primaryImplicitStyle);
                    DumpStyleDetails("    Control.Style", primaryEditorControl.Style);
                    Console.WriteLine("    Template(before)=" + Describe(primaryEditorControl.Template));
                    var styleSource = DependencyPropertyHelper.GetValueSource(primaryEditorControl, FrameworkElement.StyleProperty);
                    var templateSourceBefore = DependencyPropertyHelper.GetValueSource(primaryEditorControl, Control.TemplateProperty);
                    Console.WriteLine("    Style ValueSource: Base=" + styleSource.BaseValueSource + ", IsExpression=" + styleSource.IsExpression + ", IsAnimated=" + styleSource.IsAnimated + ", IsCoerced=" + styleSource.IsCoerced + ", IsCurrent=" + styleSource.IsCurrent);
                    Console.WriteLine("    Template(before) ValueSource: Base=" + templateSourceBefore.BaseValueSource + ", IsExpression=" + templateSourceBefore.IsExpression + ", IsAnimated=" + templateSourceBefore.IsAnimated + ", IsCoerced=" + templateSourceBefore.IsCoerced + ", IsCurrent=" + templateSourceBefore.IsCurrent);
                    try
                    {
                        primaryEditorControl.ApplyTemplate();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("    ApplyTemplate failed: " + ex.GetType().Name + ": " + ex.Message);
                    }
                    Console.WriteLine("    Template(after)=" + Describe(primaryEditorControl.Template));
                    var templateSourceAfter = DependencyPropertyHelper.GetValueSource(primaryEditorControl, Control.TemplateProperty);
                    Console.WriteLine("    Template(after) ValueSource: Base=" + templateSourceAfter.BaseValueSource + ", IsExpression=" + templateSourceAfter.IsExpression + ", IsAnimated=" + templateSourceAfter.IsAnimated + ", IsCoerced=" + templateSourceAfter.IsCoerced + ", IsCurrent=" + templateSourceAfter.IsCurrent);
                    DumpControlColorState("    ", primaryEditorControl);
                }

                if (primaryEditorElement is DependencyObject dep)
                {
                    Console.WriteLine("    VisualChildrenCount=" + VisualTreeHelper.GetChildrenCount(dep));
                }

                DumpAvalonEditTextState(primaryEditorElement, "    ");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  primaryTextEditor reflection probe failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void DumpActiveViewContentState(Assembly sharpDevelopAssembly)
    {
        try
        {
            var sdType = FindType("ICSharpCode.SharpDevelop.SD", sharpDevelopAssembly);
            var workbench = sdType?.GetProperty("Workbench", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var activeView = workbench?.GetType().GetProperty("ActiveViewContent", BindingFlags.Public | BindingFlags.Instance)?.GetValue(workbench);
            Console.WriteLine("  ActiveViewContent: " + Describe(activeView));

            var primaryFile = activeView?.GetType().GetProperty("PrimaryFile", BindingFlags.Public | BindingFlags.Instance)?.GetValue(activeView);
            var fileName = primaryFile?.GetType().GetProperty("FileName", BindingFlags.Public | BindingFlags.Instance)?.GetValue(primaryFile);
            Console.WriteLine("  ActiveViewContent.PrimaryFile: " + Describe(primaryFile));
            Console.WriteLine("  ActiveViewContent.PrimaryFile.FileName: " + Describe(fileName));

            var control = activeView?.GetType().GetProperty("Control", BindingFlags.Public | BindingFlags.Instance)?.GetValue(activeView);
            Console.WriteLine("  ActiveViewContent.Control: " + Describe(control));
            if (control is FrameworkElement controlElement)
            {
                DumpElementRuntimeState("ActiveViewContent.Control", controlElement);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Failed to dump active view content state: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static DependencyObject? FindVisualDescendantByTypeName(DependencyObject root, string typeNameFragment)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentType = current.GetType();
            if (currentType.Name.IndexOf(typeNameFragment, StringComparison.OrdinalIgnoreCase) >= 0
                || currentType.FullName?.IndexOf(typeNameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return current;
            }

            var children = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < children; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }

        return null;
    }

    private static void ProbeAvalonEditAddInThemeLoader()
    {
        var addInAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "ICSharpCode.AvalonEdit.AddIn", StringComparison.Ordinal));

        Console.WriteLine("  AvalonEdit.AddIn assembly loaded: " + (addInAssembly != null));
        if (addInAssembly == null)
        {
            var addInPath = Path.Combine(SharpDevelopBin, "ICSharpCode.AvalonEdit.AddIn.dll");
            Console.WriteLine("  Expected add-in path: " + addInPath + " (exists=" + File.Exists(addInPath) + ")");
            return;
        }

        Console.WriteLine("  AvalonEdit.AddIn assembly path: " + addInAssembly.Location);

        // Probe the BAML theme resources in the AddIn assembly - these are loaded automatically by WPF via ThemeInfo
        Console.WriteLine("\n--- BAML theme resource probe ---");
        var addInBamlUris = new[]
        {
            "pack://application:,,,/ICSharpCode.AvalonEdit.AddIn;component/themes/generic.xaml",
            "pack://application:,,,/ICSharpCode.AvalonEdit.AddIn;component/themes/generic.baml",
        };
        foreach (var uriStr in addInBamlUris)
        {
            try
            {
                var uri = new Uri(uriStr, UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                if (info?.Stream != null)
                {
                    var firstByte = info.Stream.ReadByte();
                    Console.WriteLine("  OK (firstByte=0x" + firstByte.ToString("X2") + "): " + uriStr);
                    info.Stream.Position = 0;
                    try
                    {
                        var rd = (ResourceDictionary)XamlReader.Load(info.Stream);
                        Console.WriteLine("    Loaded ResourceDictionary, Count=" + rd.Count);
                        foreach (var key in rd.Keys)
                        {
                            var val = rd[key];
                            Console.WriteLine("    key=" + Describe(key) + ", type=" + val?.GetType().Name);
                            if (val is Style style2)
                            {
                                Console.WriteLine("    Style.Setters.Count=" + style2.Setters.Count);
                                foreach (var setter in style2.Setters)
                                {
                                    if (setter is Setter s2)
                                        Console.WriteLine("      Setter: Property=" + s2.Property?.Name + ", Value=" + Describe(s2.Value));
                                }
                            }
                        }
                    }
                    catch (Exception loadEx)
                    {
                        Console.WriteLine("    XamlReader.Load failed: " + loadEx.GetType().Name + ": " + loadEx.Message);
                    }
                }
                else
                {
                    Console.WriteLine("  NULL stream: " + uriStr);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  EXCEPTION (" + ex.GetType().Name + "): " + uriStr + " → " + ex.Message);
            }
        }

        // Dump all resources in the AddIn assembly
        DumpAssemblyResources(addInAssembly, "ICSharpCode.AvalonEdit.AddIn");
        var themeLoaderType = addInAssembly.GetType("__WxsgGenerated.__WxsgThemeLoader", throwOnError: false)
            ?? addInAssembly.GetType("__WxsgThemeLoader", throwOnError: false);
        Console.WriteLine("  AvalonEdit.AddIn ThemeLoader type: " + Describe(themeLoaderType));

        if (themeLoaderType == null)
            return;

        var registerMethod = themeLoaderType.GetMethod("RegisterForAppResources", BindingFlags.Public | BindingFlags.Static);
        Console.WriteLine("  RegisterForAppResources method: " + Describe(registerMethod));

        var sdTextEditorType = addInAssembly.GetType("ICSharpCode.AvalonEdit.AddIn.SharpDevelopTextEditor", throwOnError: false);
        Console.WriteLine("  SharpDevelopTextEditor type: " + Describe(sdTextEditorType));
        if (sdTextEditorType != null)
        {
            try
            {
                var resourceBefore = Application.Current?.TryFindResource(sdTextEditorType);
                Console.WriteLine("  TryFindResource(typeof(SharpDevelopTextEditor)) before register: " + Describe(resourceBefore));
            }
            catch (Exception ex)
            {
                Console.WriteLine("  TryFindResource(before) failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        try
        {
            registerMethod?.Invoke(null, null);
            Console.WriteLine("  Called AvalonEdit.AddIn ThemeLoader.RegisterForAppResources().");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  RegisterForAppResources failed:");
            DumpException(ex, "    ");
        }

        if (sdTextEditorType != null)
        {
            try
            {
                var resourceAfter = Application.Current?.TryFindResource(sdTextEditorType);
                Console.WriteLine("  TryFindResource(typeof(SharpDevelopTextEditor)) after register: " + Describe(resourceAfter));
                // Inspect the Style's setters to verify a Template setter exists
                if (resourceAfter is Style mergedStyle)
                {
                    Console.WriteLine("  Merged Style.Setters.Count=" + mergedStyle.Setters.Count);
                    foreach (var setter in mergedStyle.Setters)
                    {
                        if (setter is Setter s)
                            Console.WriteLine("    Setter: Property=" + s.Property?.Name + ", Value=" + Describe(s.Value));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  TryFindResource failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                var editor = Activator.CreateInstance(sdTextEditorType) as FrameworkElement;
                Console.WriteLine("  SharpDevelopTextEditor instance: " + Describe(editor));
                if (editor != null)
                {
                    if (editor is Control editorControl)
                    {
                        Console.WriteLine("    Template(before)=" + Describe(editorControl.Template));
                    }

                    var host = new Window
                    {
                        Width = 500,
                        Height = 300,
                        Content = editor,
                        WindowStyle = WindowStyle.None,
                        ShowInTaskbar = false,
                        Visibility = Visibility.Hidden,
                    };
                    host.Show();
                    host.UpdateLayout();
                    editor.UpdateLayout();

                    Console.WriteLine("    IsLoaded=" + editor.IsLoaded + ", ActualWidth=" + editor.ActualWidth + ", ActualHeight=" + editor.ActualHeight);
                    if (editor is Control loadedControl)
                    {
                        Console.WriteLine("    Template(after)=" + Describe(loadedControl.Template));
                    }
                    Console.WriteLine("    VisualChildrenCount=" + VisualTreeHelper.GetChildrenCount(editor));

                    host.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  SharpDevelopTextEditor probe failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    private static void ReprobeActiveEditorAfterThemeRegistration(Assembly sharpDevelopAssembly)
    {
        try
        {
            var workbenchSingletonType = FindType("ICSharpCode.SharpDevelop.Gui.WorkbenchSingleton", sharpDevelopAssembly);
            var activeWorkbench = workbenchSingletonType?.GetProperty("ActiveWorkbench", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var activeViewContent = activeWorkbench?.GetType().GetProperty("ActiveViewContent", BindingFlags.Public | BindingFlags.Instance)?.GetValue(activeWorkbench);
            var control = activeViewContent?.GetType().GetProperty("Control", BindingFlags.Public | BindingFlags.Instance)?.GetValue(activeViewContent);
            Console.WriteLine("  ActiveViewContent.Control: " + Describe(control));
            if (control is FrameworkElement fe)
            {
                DumpEditorTemplateState(fe, "Post-registration CodeEditor subtree");
                DumpCodeEditorDeepState(fe);
                return;
            }

            if (LastProbedCodeEditor != null && LastProbedCodeEditor.TryGetTarget(out var cachedCodeEditor))
            {
                Console.WriteLine("  Falling back to cached CodeEditor instance.");
                DumpEditorTemplateState(cachedCodeEditor, "Post-registration cached CodeEditor subtree");
                DumpCodeEditorDeepState(cachedCodeEditor);

                // Try invalidating StyleProperty on the inner CodeEditorView to force re-application
                Console.WriteLine("\n--- Attempting InvalidateProperty(StyleProperty) on CodeEditorView ---");
                try
                {
                    if (cachedCodeEditor is Panel codeEditorPanel && codeEditorPanel.Children.Count > 0)
                    {
                        var innerEditor = codeEditorPanel.Children[0] as FrameworkElement;
                        if (innerEditor != null)
                        {
                            Console.WriteLine("  Before: Template=" + Describe((innerEditor as Control)?.Template));
                            innerEditor.InvalidateProperty(FrameworkElement.StyleProperty);
                            Pump();
                            innerEditor.UpdateLayout();
                            Pump();
                            Console.WriteLine("  After InvalidateProperty(StyleProperty): Template=" + Describe((innerEditor as Control)?.Template));

                            if ((innerEditor as Control)?.Template == null)
                            {
                                // Try setting the style explicitly
                                var styleKey = GetDefaultStyleKey(innerEditor);
                                var foundStyle = styleKey != null ? innerEditor.TryFindResource(styleKey) as Style : null;
                                Console.WriteLine("  Explicit style from TryFindResource: " + Describe(foundStyle));
                                if (foundStyle != null)
                                {
                                    innerEditor.Style = foundStyle;
                                    Pump();
                                    innerEditor.UpdateLayout();
                                    Pump();
                                    Console.WriteLine("  After explicit Style set: Template=" + Describe((innerEditor as Control)?.Template));
                                    Console.WriteLine("  VisualChildrenCount=" + VisualTreeHelper.GetChildrenCount(innerEditor));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex2)
                {
                    Console.WriteLine("  InvalidateProperty probe failed: " + ex2.GetType().Name + ": " + ex2.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Reprobe failed:");
            DumpException(ex, "    ");
        }
    }

    private static void ProbeAvalonEditTextEditor()
    {
        // Locate the AvalonEdit assembly (may already be loaded by SharpDevelop).
        var avalonEditAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "ICSharpCode.AvalonEdit", StringComparison.Ordinal));
        if (avalonEditAssembly == null)
        {
            var avalonEditPath = Path.Combine(SharpDevelopBin, "ICSharpCode.AvalonEdit.dll");
            if (File.Exists(avalonEditPath))
            {
                Console.WriteLine("  AvalonEdit not loaded yet; loading from: " + avalonEditPath);
                try { avalonEditAssembly = Assembly.LoadFrom(avalonEditPath); } catch (Exception ex) { Console.WriteLine("  Load failed: " + ex.Message); }
            }
        }

        if (avalonEditAssembly == null)
        {
            Console.WriteLine("  ICSharpCode.AvalonEdit assembly not found. Skipping TextEditor probe.");
            return;
        }

        Console.WriteLine("  AvalonEdit assembly: " + avalonEditAssembly.Location);
        DumpAssemblyResources(avalonEditAssembly, "ICSharpCode.AvalonEdit");

        // Show which AvalonEdit URIs are present in Application.Resources.MergedDictionaries
        Console.WriteLine("\n  Application.Resources.MergedDictionaries (AvalonEdit-related):");
        var mergedDicts = Application.Current?.Resources.MergedDictionaries;
        if (mergedDicts != null)
        {
            Console.WriteLine("    Total merged dicts: " + mergedDicts.Count);
            foreach (var dict in mergedDicts)
            {
                var src = dict?.Source?.OriginalString ?? "<no-source>";
                if (src.IndexOf("AvalonEdit", StringComparison.OrdinalIgnoreCase) >= 0
                    || src.IndexOf("Core.Presentation", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine("    [MATCH] " + src);
                }
                else
                {
                    Console.WriteLine("    " + src);
                }
            }
        }

        // Probe key pack URIs via Application.GetResourceStream
        var packUris = new[]
        {
            "pack://application:,,,/ICSharpCode.AvalonEdit;component/themes/generic.xaml",
            "pack://application:,,,/ICSharpCode.AvalonEdit;component/TextEditor.xaml",
            "pack://application:,,,/ICSharpCode.AvalonEdit;component/Search/SearchPanel.xaml",
            "pack://application:,,,/ICSharpCode.Core.Presentation;component/themes/generic.xaml",
        };
        Console.WriteLine("\n  Pack URI resource probe:");
        foreach (var uriStr in packUris)
        {
            try
            {
                var uri = new Uri(uriStr, UriKind.Absolute);
                var info = System.Windows.Application.GetResourceStream(uri);
                if (info?.Stream != null)
                {
                    var firstByte = info.Stream.ReadByte();
                    var isXaml = firstByte == '<';
                    info.Stream.Dispose();
                    Console.WriteLine("    OK (firstByte=0x" + firstByte.ToString("X2") + ", isRawXaml=" + isXaml + "): " + uriStr);
                }
                else
                {
                    Console.WriteLine("    NULL stream: " + uriStr);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("    EXCEPTION (" + ex.GetType().Name + "): " + uriStr + " → " + ex.Message);
            }
        }

        // Try to manually trigger the ThemeLoader (in case ModuleInitializer didn't fire)
        var themeLoaderType = avalonEditAssembly.GetType("__WxsgGenerated.__WxsgThemeLoader", throwOnError: false)
            ?? avalonEditAssembly.GetType("__WxsgThemeLoader", throwOnError: false);
        Console.WriteLine("\n  AvalonEdit ThemeLoader type: " + Describe(themeLoaderType));
        if (themeLoaderType != null)
        {
            try
            {
                themeLoaderType.GetMethod("RegisterForAppResources", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, null);
                Console.WriteLine("  Called ThemeLoader.RegisterForAppResources()");
            }
            catch (Exception ex)
            {
                Console.WriteLine("  ThemeLoader.RegisterForAppResources() threw: " + ex.Message);
            }
        }

        // Instantiate a TextEditor in its own window and check its template state
        var textEditorType = avalonEditAssembly.GetType("ICSharpCode.AvalonEdit.TextEditor", throwOnError: false);
        Console.WriteLine("\n  TextEditor type: " + Describe(textEditorType));
        if (textEditorType == null) return;

        try
        {
            var editor = (FrameworkElement)Activator.CreateInstance(textEditorType)!;
            // Set Text if the property exists
            try { textEditorType.GetProperty("Text")?.SetValue(editor, "Hello from OutlineInspector probe!\nLine 2\nLine 3"); } catch { }

            var probeWindow = new Window
            {
                Title = "AvalonEdit TextEditor Probe",
                Width = 600,
                Height = 400,
                Content = editor,
                ShowInTaskbar = false,
            };
            probeWindow.Show();
            Pump();
            probeWindow.UpdateLayout();
            Pump();

            Console.WriteLine("  TextEditor in probe window:");
            Console.WriteLine("    IsLoaded=" + editor.IsLoaded);
            Console.WriteLine("    ActualWidth=" + editor.ActualWidth + ", ActualHeight=" + editor.ActualHeight);
            var control = editor as System.Windows.Controls.Control;
            Console.WriteLine("    Template=" + (control?.Template != null ? control.Template.GetType().FullName : "<null>"));
            Console.WriteLine("    VisualChildrenCount=" + VisualTreeHelper.GetChildrenCount(editor));
            DumpVisualTreeSnapshot(editor, maxDepth: 4, maxChildrenPerNode: 10);

            try { probeWindow.Hide(); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  TextEditor probe failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void DumpWxsgDiagnostics(Assembly sharpDevelopAssembly)
    {
        Console.WriteLine("Loaded assemblies with WXSG-generated code:");
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var hasThemeLoader = asm.GetType("__WxsgThemeLoader", throwOnError: false) != null;
                var hasClasslessLoader = asm.GetType("__WxsgClasslessXamlLoader", throwOnError: false) != null;

                if (hasThemeLoader || hasClasslessLoader)
                {
                    Console.WriteLine($"  {asm.GetName().Name}: ThemeLoader={hasThemeLoader}, ClasslessLoader={hasClasslessLoader}");
                }
            }
            catch { }
        }

        Console.WriteLine("\nChecking build outputs for WXSG generated files:");
        var addInsPath = Path.Combine(SharpDevelopRoot, "src", "AddIns", "DisplayBindings", "AvalonEdit.AddIn");
        DumpWxsgGeneratedFiles(addInsPath, "AvalonEdit.AddIn");

        var corePath = Path.Combine(SharpDevelopRoot, "src", "Main", "Core", "Project");
        DumpWxsgGeneratedFiles(corePath, "ICSharpCode.Core");

        var avalonEditPath = Path.Combine(SharpDevelopRoot, "src", "Libraries", "AvalonEdit", "ICSharpCode.AvalonEdit");
        DumpWxsgGeneratedFiles(avalonEditPath, "ICSharpCode.AvalonEdit");

        var corePresentationPath = Path.Combine(SharpDevelopRoot, "src", "Main", "ICSharpCode.Core.Presentation");
        DumpWxsgGeneratedFiles(corePresentationPath, "ICSharpCode.Core.Presentation");
    }

    private static void DumpWxsgGeneratedFiles(string projectPath, string projectName)
    {
        var objPath = Path.Combine(projectPath, "obj", "Debug");
        if (!Directory.Exists(objPath))
        {
            Console.WriteLine($"  {projectName}: obj\\Debug not found");
            return;
        }

        var generatedPath = Path.Combine(objPath, "XamlToCSharpGenerator.Generator.WPF");
        if (Directory.Exists(generatedPath))
        {
            var themeLoaderFiles = Directory.GetFiles(generatedPath, "__WxsgThemeLoader*", SearchOption.AllDirectories);
            var classlessLoaderFiles = Directory.GetFiles(generatedPath, "__WxsgClasslessXamlLoader*", SearchOption.AllDirectories);
            Console.WriteLine($"  {projectName}:");
            Console.WriteLine($"    ThemeLoader files: {themeLoaderFiles.Length}");
            Console.WriteLine($"    ClasslessLoader files: {classlessLoaderFiles.Length}");
            if (themeLoaderFiles.Length > 0)
            {
                foreach (var file in themeLoaderFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var size = new FileInfo(file).Length;
                    Console.WriteLine($"      - {fileName} ({size} bytes)");
                }
            }
        }
        else
        {
            Console.WriteLine($"  {projectName}: Generated files directory not found");
        }

        // Check for any .cs files in the intermediate output
        var allCsFiles = Directory.GetFiles(objPath, "*.g.cs", SearchOption.AllDirectories);
        if (allCsFiles.Length > 0)
        {
            Console.WriteLine($"  {projectName} all generated .g.cs files ({allCsFiles.Length}):");
            foreach (var file in allCsFiles.Take(10))
            {
                var fileName = Path.GetFileName(file);
                Console.WriteLine($"    - {fileName}");
            }
        }

        // Also check for wxsg preprocessing directory
        var wxsgPath = Path.Combine(objPath, "wxsg");
        if (Directory.Exists(wxsgPath))
        {
            var deferredPath = Path.Combine(wxsgPath, "raw-deferred");
            if (Directory.Exists(deferredPath))
            {
                var xamlFiles = Directory.GetFiles(deferredPath, "*.xaml", SearchOption.AllDirectories);
                Console.WriteLine($"  {projectName} deferred classless pages: {xamlFiles.Length}");
                foreach (var file in xamlFiles.Take(5))
                {
                    var relPath = file.Substring(wxsgPath.Length).TrimStart(Path.DirectorySeparatorChar);
                    Console.WriteLine($"    - {relPath}");
                }
            }

            var strippedPath = Path.Combine(wxsgPath, "stripped");
            if (Directory.Exists(strippedPath))
            {
                var xamlFiles = Directory.GetFiles(strippedPath, "*.xaml", SearchOption.AllDirectories);
                Console.WriteLine($"  {projectName} stripped classless pages: {xamlFiles.Length}");
            }
        }
    }

    private static void InstallUnhandledExceptionConsoleLogging()
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Console.WriteLine("UNHANDLED AppDomain exception (captured by OutlineInspector), terminating=" + e.IsTerminating + ":");
                DumpException(e.ExceptionObject as Exception, "  ");
            };

            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
            {
                Console.WriteLine("UNHANDLED WPF dispatcher exception (captured by OutlineInspector):");
                DumpException(e.Exception, "  ");
                e.Handled = true;
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to install unhandled exception console logging: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void DisableSharpDevelopUnhandledExceptionUi(Assembly sharpDevelopAssembly)
    {
        try
        {
            var sharpDevelopCoreAssemblyPath = Path.Combine(SharpDevelopBin, "ICSharpCode.SharpDevelop.dll");
            var sharpDevelopCoreAssembly = LoadAssemblyIfExists(sharpDevelopCoreAssemblyPath);
            var exceptionBoxType = FindType("ICSharpCode.SharpDevelop.Logging.ExceptionBox", sharpDevelopAssembly, sharpDevelopCoreAssembly);
            var winFormsAppType = Type.GetType("System.Windows.Forms.Application, System.Windows.Forms");
            var winFormsThreadExceptionEvent = winFormsAppType?.GetEvent("ThreadException", BindingFlags.Public | BindingFlags.Static);
            if (exceptionBoxType == null)
            {
                Console.WriteLine("ExceptionBox type not found; skipping handler removal.");
                return;
            }

            var threadExceptionMethod = exceptionBoxType
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(method =>
                    method.Name == "ShowErrorBox"
                    && method.GetParameters().Length == 2
                    && string.Equals(method.GetParameters()[1].ParameterType.Name, "ThreadExceptionEventArgs", StringComparison.Ordinal));

            var appDomainUnhandledMethod = exceptionBoxType.GetMethod(
                "ShowErrorBox",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(object), typeof(UnhandledExceptionEventArgs) },
                modifiers: null);

            var dispatcherUnhandledMethod = exceptionBoxType.GetMethod(
                "Dispatcher_UnhandledException",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(object), typeof(DispatcherUnhandledExceptionEventArgs) },
                modifiers: null);

            if (threadExceptionMethod != null)
            {
                if (winFormsThreadExceptionEvent?.EventHandlerType != null)
                {
                    var threadHandler = Delegate.CreateDelegate(winFormsThreadExceptionEvent.EventHandlerType, threadExceptionMethod);
                    winFormsThreadExceptionEvent.RemoveEventHandler(null, threadHandler);
                }
            }

            if (appDomainUnhandledMethod != null)
            {
                var appDomainHandler = (UnhandledExceptionEventHandler)Delegate.CreateDelegate(typeof(UnhandledExceptionEventHandler), appDomainUnhandledMethod);
                AppDomain.CurrentDomain.UnhandledException -= appDomainHandler;
            }

            if (dispatcherUnhandledMethod != null)
            {
                var dispatcherHandler = (DispatcherUnhandledExceptionEventHandler)Delegate.CreateDelegate(typeof(DispatcherUnhandledExceptionEventHandler), dispatcherUnhandledMethod);
                Dispatcher.CurrentDispatcher.UnhandledException -= dispatcherHandler;
            }

            Console.WriteLine("Disabled SharpDevelop ExceptionBox unhandled-exception handlers.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to disable SharpDevelop ExceptionBox handlers: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool TryBootstrapWorkbenchFromHost(Assembly sharpDevelopAssembly, string[] excludedAddInPatterns, out Window? workbenchWindow)
    {
        var dynamicExclusions = new HashSet<string>(excludedAddInPatterns, StringComparer.OrdinalIgnoreCase);
        Exception? firstFailure = null;

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var currentPatterns = dynamicExclusions.ToArray();
            if (TryBootstrapWorkbenchFromHostCore(sharpDevelopAssembly, includeAddIns: true, excludedAddInPatterns: currentPatterns, out workbenchWindow, out firstFailure))
            {
                return true;
            }

            if (!TryExtractBadImageAddInPattern(firstFailure, out var learnedPattern))
            {
                break;
            }

            if (!dynamicExclusions.Add(learnedPattern))
            {
                break;
            }

            Console.WriteLine("Auto-excluding add-in pattern from BadImageFormatException: " + learnedPattern + " (attempt " + attempt + ")");
        }

        if (firstFailure != null)
        {
            Console.WriteLine("Bootstrap through SharpDevelopHost failed (with add-ins):");
            DumpException(firstFailure, "  ");
            if (firstFailure.ToString().IndexOf("TypeScriptBinding", StringComparison.OrdinalIgnoreCase) >= 0
                || firstFailure.ToString().IndexOf("BadImageFormatException", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("Retrying bootstrap without AddIns directory to bypass incompatible add-ins...");
                if (TryBootstrapWorkbenchFromHostCore(sharpDevelopAssembly, includeAddIns: false, excludedAddInPatterns: dynamicExclusions.ToArray(), out workbenchWindow, out var secondFailure))
                {
                    return true;
                }

                Console.WriteLine("Bootstrap through SharpDevelopHost failed (without add-ins):");
                if (secondFailure != null)
                {
                    DumpException(secondFailure, "  ");
                }
            }
        }

        workbenchWindow = null;
        return false;
    }

    private static bool TryExtractBadImageAddInPattern(Exception? ex, out string pattern)
    {
        pattern = string.Empty;
        if (ex == null)
        {
            return false;
        }

        var text = ex.ToString();
        if (text.IndexOf("BadImageFormatException", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var marker = "file:///";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        start += marker.Length;
        var end = text.IndexOf("'", start, StringComparison.Ordinal);
        if (end <= start)
        {
            return false;
        }

        var pathText = text.Substring(start, end - start).Replace('/', '\\');
        string? parentDir = null;
        string? fileStem = null;
        try
        {
            parentDir = Path.GetFileName(Path.GetDirectoryName(pathText));
            fileStem = Path.GetFileNameWithoutExtension(pathText);
        }
        catch
        {
        }

        var candidate = !string.IsNullOrWhiteSpace(parentDir) ? parentDir : fileStem;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        pattern = candidate;
        return true;
    }

    private static bool TryBootstrapWorkbenchFromHostCore(Assembly sharpDevelopAssembly, bool includeAddIns, string[] excludedAddInPatterns, out Window? workbenchWindow, out Exception? failure)
    {
        workbenchWindow = null;
        failure = null;
        try
        {
            var sharpDevelopCoreAssemblyPath = Path.Combine(SharpDevelopBin, "ICSharpCode.SharpDevelop.dll");
            var sharpDevelopCoreAssembly = LoadAssemblyIfExists(sharpDevelopCoreAssemblyPath);

            var startupSettingsType = FindType("ICSharpCode.SharpDevelop.Sda.StartupSettings", sharpDevelopAssembly, sharpDevelopCoreAssembly);
            var hostType = FindType("ICSharpCode.SharpDevelop.Sda.SharpDevelopHost", sharpDevelopAssembly, sharpDevelopCoreAssembly);
            var workbenchStartupType = FindType("ICSharpCode.SharpDevelop.Workbench.WorkbenchStartup", sharpDevelopAssembly, sharpDevelopCoreAssembly);
            var sdType = FindType("ICSharpCode.SharpDevelop.SD", sharpDevelopAssembly, sharpDevelopCoreAssembly);
            if (startupSettingsType == null || hostType == null || workbenchStartupType == null || sdType == null)
            {
                Console.WriteLine("Bootstrap types missing; cannot initialize workbench through SharpDevelopHost.");
                return false;
            }

            var startupSettings = Activator.CreateInstance(startupSettingsType);
            if (startupSettings == null)
            {
                Console.WriteLine("Failed to create StartupSettings instance.");
                return false;
            }

            var applicationRootPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SharpDevelopExe) ?? SharpDevelopBin, ".."));
            var tempConfigDirectory = Path.Combine(Path.GetTempPath(), "OutlineInspector", includeAddIns ? "SharpDevelopConfig-WithAddIns" : "SharpDevelopConfig-NoAddIns");
            var tempDomDirectory = Path.Combine(Path.GetTempPath(), "OutlineInspector", includeAddIns ? "SharpDevelopDom-WithAddIns" : "SharpDevelopDom-NoAddIns");
            Directory.CreateDirectory(tempConfigDirectory);
            Directory.CreateDirectory(tempDomDirectory);

            SetPropertyValue(startupSettings, "ApplicationRootPath", applicationRootPath);
            SetPropertyValue(startupSettings, "ConfigDirectory", tempConfigDirectory);
            SetPropertyValue(startupSettings, "DomPersistencePath", tempDomDirectory);
            SetPropertyValue(startupSettings, "AllowUserAddIns", false);
            SetPropertyValue(startupSettings, "AllowAddInConfigurationAndExternalAddIns", false);
            SetPropertyValue(startupSettings, "UseSharpDevelopErrorHandler", false);
            if (includeAddIns)
            {
                var addInRoot = Path.Combine(applicationRootPath, "AddIns");
                var addInFiles = GetFilteredAddInFiles(addInRoot, excludedAddInPatterns);
                Console.WriteLine("Filtered add-ins: keeping " + addInFiles.Count + " file(s), excluded pattern(s): " + string.Join(", ", excludedAddInPatterns));
                foreach (var addInFile in addInFiles)
                {
                    Invoke(startupSettings, "AddAddInFile", addInFile);
                }
            }

            var host = Activator.CreateInstance(hostType, AppDomain.CurrentDomain, startupSettings);
            Console.WriteLine("SharpDevelopHost instance (includeAddIns=" + includeAddIns + "): " + Describe(host));
            ForceDisableSharpDevelopErrorHandler(host);

            var workbenchStartup = Activator.CreateInstance(workbenchStartupType, nonPublic: true);
            Console.WriteLine("WorkbenchStartup instance: " + Describe(workbenchStartup));
            Invoke(workbenchStartup, "InitializeWorkbench");

            var workbench = sdType.GetProperty("Workbench", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            Console.WriteLine("SD.Workbench: " + Describe(workbench));
            workbenchWindow = GetPropertyValue(workbench, "MainWindow") as Window;
            Console.WriteLine("SD.Workbench.MainWindow: " + Describe(workbenchWindow));
            return workbenchWindow != null;
        }
        catch (Exception ex)
        {
            failure = ex;
            return false;
        }
    }

    private static void ForceDisableSharpDevelopErrorHandler(object? sharpDevelopHost)
    {
        if (sharpDevelopHost == null)
        {
            return;
        }

        try
        {
            var hostType = sharpDevelopHost.GetType();
            var helperField = hostType.GetField("helper", BindingFlags.Instance | BindingFlags.NonPublic);
            var helper = helperField?.GetValue(sharpDevelopHost);
            if (helper == null)
            {
                Console.WriteLine("SharpDevelopHost helper not found; cannot force-disable error handler flag.");
                return;
            }

            var flagField = helper.GetType().GetField("useSharpDevelopErrorHandler", BindingFlags.Instance | BindingFlags.NonPublic);
            if (flagField == null)
            {
                Console.WriteLine("CallHelper.useSharpDevelopErrorHandler field not found.");
                return;
            }

            flagField.SetValue(helper, false);
            Console.WriteLine("Forced CallHelper.useSharpDevelopErrorHandler = false.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to force-disable SharpDevelop error handler flag: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static Assembly? LoadAssemblyIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Assembly.LoadFrom(path);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to load assembly '" + path + "': " + ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    private static string[] GetExcludedAddInPatterns(string[] args)
    {
        var values = args
            .Select(ParseExcludeAddInArg)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToList();

        if (values.Count == 0)
        {
            return DefaultExcludedAddInPatterns;
        }

        return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? ParseExcludeAddInArg(string arg)
    {
        const string key = "--exclude-addin=";
        if (!arg.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return arg.Substring(key.Length);
    }

    private static List<string> GetFilteredAddInFiles(string addInRoot, string[] excludedAddInPatterns)
    {
        if (!Directory.Exists(addInRoot))
        {
            return new List<string>();
        }

        var allAddIns = Directory.GetFiles(addInRoot, "*.addin", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (excludedAddInPatterns.Length == 0)
        {
            return allAddIns;
        }

        var filtered = new List<string>(allAddIns.Count);
        foreach (var addInPath in allAddIns)
        {
            if (excludedAddInPatterns.Any(pattern => addInPath.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                continue;
            }

            filtered.Add(addInPath);
        }

        return filtered;
    }

    private static Type? FindType(string fullName, params Assembly?[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var candidate = assembly?.GetType(fullName, throwOnError: false);
            if (candidate != null)
            {
                return candidate;
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(fullName, throwOnError: false))
            .FirstOrDefault(type => type != null);
    }

    private static int RunXamlDesignerInspector()
    {
        Directory.SetCurrentDirectory(XamlDesignerProject);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromXamlDesignerOutput;
        EnableBindingDiagnostics();

        var app = Application.Current ?? new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.WriteLine("DISPATCHER EXCEPTION: " + e.Exception);
            e.Handled = true;
        };

        var assembly = Assembly.LoadFrom(Path.Combine(XamlDesignerOutput, "Demo.XamlDesigner.dll"));
        assembly.GetType("ICSharpCode.XamlDesigner.App")?
            .GetField("Args", BindingFlags.Public | BindingFlags.Static)?
            .SetValue(null, Array.Empty<string>());

        var windowType = assembly.GetType("ICSharpCode.XamlDesigner.MainWindow", throwOnError: true)!;
        var window = (Window)Activator.CreateInstance(windowType)!;
        window.Show();
        DumpResourceDictionaries("Application.Current.Resources", Application.Current?.Resources, depth: 0, maxDepth: 2, maxKeysPerDictionary: 20);

        Pump();
        Pump();
        EnsureNewDocument(assembly);
        Pump();

        ProbeWindowClone();
        DumpSharedInstancesGenericCctorIl();
        DumpWindowCloneCctorIl();
        DumpWindow(window);
        DumpRuntimeInternals(window);
        DumpAssemblyResources("ICSharpCode.WpfDesign.Designer");

        window.Close();
        app.Shutdown();
        return 0;
    }

    private static int RunSharpDevelopStartPageInspector()
    {
        Directory.SetCurrentDirectory(SharpDevelopRoot);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromSharpDevelopOutputs;
        EnableBindingDiagnostics();

        Console.WriteLine("SharpDevelop root: " + SharpDevelopRoot);
        Console.WriteLine("SharpDevelop bin: " + SharpDevelopBin + " (exists=" + Directory.Exists(SharpDevelopBin) + ")");
        Console.WriteLine("StartPage assembly: " + SharpDevelopStartPage + " (exists=" + File.Exists(SharpDevelopStartPage) + ")");

        if (!File.Exists(SharpDevelopStartPage))
        {
            Console.WriteLine("StartPage assembly not found. Build SharpDevelop StartPage first.");
            return 2;
        }

        var app = Application.Current ?? new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Console.WriteLine("DISPATCHER EXCEPTION: " + e.Exception);
            e.Handled = true;
        };

        var startPageAssembly = Assembly.LoadFrom(SharpDevelopStartPage);
        DumpAssemblyResources(startPageAssembly, "StartPage");
        ProbePackResourceUris(startPageAssembly, "resources/balken_links.gif");
        ProbePackResourceUris(startPageAssembly, "Resources/balken_links.gif");

        var controlType = startPageAssembly.GetType("ICSharpCode.StartPage.StartPageControl", throwOnError: false);
        Console.WriteLine("StartPageControl type: " + Describe(controlType));
        if (controlType == null)
        {
            return 3;
        }

        try
        {
            var instance = Activator.CreateInstance(controlType);
            Console.WriteLine("StartPageControl instance: " + Describe(instance));
            if (instance is FrameworkElement element)
            {
                DumpResourceDictionaries("Application.Current.Resources", Application.Current?.Resources, depth: 0, maxDepth: 2, maxKeysPerDictionary: 20);
                DumpElementRuntimeState("StartPageControl", element);
                if (instance is DependencyObject dependencyObject)
                {
                    DumpVisualTreeSnapshot(dependencyObject, maxDepth: 3, maxChildrenPerNode: 30);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("StartPageControl creation failed:");
            DumpException(ex, "  ");
        }

        app.Shutdown();
        return 0;
    }

    private static void EnableBindingDiagnostics()
    {
        var source = PresentationTraceSources.DataBindingSource;
        source.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
        if (!source.Listeners.OfType<OutlineTraceListener>().Any())
        {
            source.Listeners.Add(new OutlineTraceListener());
        }

        PresentationTraceSources.Refresh();
        Console.WriteLine("Binding diagnostics enabled (Warning+Error).");
    }

    private sealed class OutlineTraceListener : TraceListener
    {
        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Console.Write("[binding] " + message);
            }
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Console.WriteLine("[binding] " + message);
            }
        }
    }

    private static void EnsureNewDocument(Assembly xamlDesignerAssembly)
    {
        var shellType = xamlDesignerAssembly.GetType("ICSharpCode.XamlDesigner.Shell");
        var shell = shellType?.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var currentDocument = GetPropertyValue(shell, "CurrentDocument");
        if (GetPropertyValue(currentDocument, "OutlineRoot") != null)
        {
            return;
        }

        Console.WriteLine("Inspector: creating a new document because startup did not produce an OutlineRoot.");
        shellType?.GetMethod("New", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(shell, null);

        currentDocument = GetPropertyValue(shell, "CurrentDocument");
        var modeProperty = currentDocument?.GetType().GetProperty("Mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var designMode = modeProperty?.PropertyType.GetField("Design")?.GetValue(null);
        if (modeProperty != null && designMode != null)
        {
            modeProperty.SetValue(currentDocument, designMode);
        }
    }

    private static void DumpAssemblyResources(string assemblySimpleName)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, assemblySimpleName, StringComparison.Ordinal));
        Console.WriteLine("Assembly resources for " + assemblySimpleName + ": " + Describe(assembly));
        if (assembly == null)
        {
            return;
        }

        foreach (var attribute in assembly.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == "System.Windows.ThemeInfoAttribute"))
        {
            Console.WriteLine("  attribute: " + attribute);
        }

        foreach (var name in assembly.GetManifestResourceNames())
        {
            Console.WriteLine("  manifest: " + name);
            if (!name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
            {
                continue;
            }

            using var reader = new System.Resources.ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                Console.WriteLine("    " + entry.Key + " => " + Describe(entry.Value));
            }
        }
    }

    private static void ProbeWindowClone()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "ICSharpCode.WpfDesign.Designer", StringComparison.Ordinal));
        var type = assembly?.GetType("ICSharpCode.WpfDesign.Designer.Controls.WindowClone");
        Console.WriteLine("WindowClone type: " + Describe(type));
        if (type == null)
        {
            return;
        }

        try
        {
            var instance = Activator.CreateInstance(type);
            Console.WriteLine("WindowClone instance: " + Describe(instance));
        }
        catch (Exception ex)
        {
            Console.WriteLine("WindowClone probe exception:");
            DumpException(ex, "  ");
        }
    }

    private static void DumpSharedInstancesGenericCctorIl()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "ICSharpCode.WpfDesign.Designer", StringComparison.Ordinal));
        var type = assembly?.GetTypes().FirstOrDefault(candidate => candidate.FullName == "ICSharpCode.WpfDesign.Designer.SharedInstances`1");
        var cctor = type?.GetConstructor(BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        var bytes = cctor?.GetMethodBody()?.GetILAsByteArray();
        Console.WriteLine("SharedInstances<T>.cctor IL bytes: " + (bytes == null ? "<null>" : BitConverter.ToString(bytes)));
        if (type == null || cctor == null || bytes == null)
        {
            return;
        }

        for (var i = 0; i < bytes.Length - 4; i++)
        {
            if (bytes[i] != 0xD0)
            {
                continue;
            }

            var token = BitConverter.ToInt32(bytes, i + 1);
            Console.WriteLine("  ldtoken raw 0x" + token.ToString("X8"));
            TryResolveMember(type.Module, token, null, null, "    resolve(no context)");
            TryResolveMember(type.Module, token, type.GetGenericArguments(), null, "    resolve(generic def context)");

            var keyboardNavigationMode = typeof(System.Windows.Input.KeyboardNavigationMode);
            var closedType = type.MakeGenericType(keyboardNavigationMode);
            TryResolveMember(type.Module, token, closedType.GetGenericArguments(), null, "    resolve(closed context)");
        }
    }

    private static void DumpWindowCloneCctorIl()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "ICSharpCode.WpfDesign.Designer", StringComparison.Ordinal));
        var type = assembly?.GetType("ICSharpCode.WpfDesign.Designer.Controls.WindowClone");
        var cctor = type?.GetConstructor(BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        var bytes = cctor?.GetMethodBody()?.GetILAsByteArray();
        Console.WriteLine("WindowClone.cctor IL bytes: " + (bytes == null ? "<null>" : BitConverter.ToString(bytes)));
        if (type == null || cctor == null || bytes == null)
        {
            return;
        }

        for (var i = 0; i < bytes.Length - 4; i++)
        {
            var isCall = bytes[i] == 0x28 || bytes[i] == 0x6F;
            var isLdToken = bytes[i] == 0xD0;
            var isField = bytes[i] == 0x7E || bytes[i] == 0x7F || bytes[i] == 0x80;
            if (!isCall && !isLdToken && !isField)
            {
                continue;
            }

            var token = BitConverter.ToInt32(bytes, i + 1);
            Console.WriteLine("  " + OpcodeName(bytes[i]) + " raw 0x" + token.ToString("X8"));
            TryResolveMember(type.Module, token, null, null, "    resolve");
        }
    }

    private static string OpcodeName(byte value)
    {
        return value switch
        {
            0x28 => "call",
            0x6F => "callvirt",
            0x7E => "ldsfld",
            0x7F => "ldsflda",
            0x80 => "stsfld",
            0xD0 => "ldtoken",
            _ => "opcode 0x" + value.ToString("X2")
        };
    }

    private static void TryResolveMember(Module module, int token, Type[]? genericTypeArguments, Type[]? genericMethodArguments, string label)
    {
        try
        {
            Console.WriteLine(label + ": " + Describe(module.ResolveMember(token, genericTypeArguments, genericMethodArguments)));
        }
        catch (Exception ex)
        {
            Console.WriteLine(label + ": <throws " + ex.GetType().Name + ": " + ex.Message + ">");
        }
    }

    private static void DumpException(Exception exception, string indent)
    {
        Console.WriteLine(indent + exception.GetType().FullName + ": " + exception.Message);
        Console.WriteLine(indent + exception.StackTrace);

        if (exception is TargetInvocationException { InnerException: not null } targetInvocationException)
        {
            Console.WriteLine(indent + "TargetInvocationException.InnerException:");
            DumpException(targetInvocationException.InnerException!, indent + "  ");
        }
        else if (exception is TypeInitializationException { InnerException: not null } typeInitializationException)
        {
            Console.WriteLine(indent + "TypeInitializationException.InnerException:");
            DumpException(typeInitializationException.InnerException!, indent + "  ");
        }
        else if (exception.InnerException != null)
        {
            Console.WriteLine(indent + "InnerException:");
            DumpException(exception.InnerException, indent + "  ");
        }
    }

    private static Assembly? ResolveFromXamlDesignerOutput(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name + ".dll";
        var path = Path.Combine(XamlDesignerOutput, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }

    private static Assembly? ResolveFromSharpDevelopOutputs(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name + ".dll";
        var candidates = new[]
        {
            Path.Combine(SharpDevelopBin, name),
            Path.Combine(SharpDevelopRoot, "AddIns", "Misc", "StartPage", name),
            Path.Combine(SharpDevelopRoot, "AddIns", "Main", name),
            Path.Combine(SharpDevelopRoot, "AddIns", "BackendBindings", name),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Assembly.LoadFrom(candidate);
            }
        }

        return null;
    }

    private static void ProbePackResourceUris(Assembly assembly, string resourcePath)
    {
        var assemblyName = assembly.GetName().Name ?? "<null>";
        Console.WriteLine("Probe resource path: " + resourcePath);

        var absolutePack = "pack://application:,,,/" + assemblyName + ";component/" + resourcePath;
        TryLoadBitmap("  BitmapImage(" + absolutePack + ")", absolutePack, UriKind.Absolute);

        var relativePack = "/" + assemblyName + ";component/" + resourcePath;
        TryGetResourceStream("  Application.GetResourceStream(" + relativePack + ")", relativePack);

        var appRelative = "/" + resourcePath;
        TryGetResourceStream("  Application.GetResourceStream(" + appRelative + ")", appRelative);
    }

    private static void TryLoadBitmap(string label, string uriText, UriKind kind)
    {
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri(uriText, kind));
            Console.WriteLine(label + " => success " + bitmap.PixelWidth + "x" + bitmap.PixelHeight);
        }
        catch (Exception ex)
        {
            Console.WriteLine(label + " => fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void TryGetResourceStream(string label, string uriText)
    {
        try
        {
            var streamInfo = Application.GetResourceStream(new Uri(uriText, UriKind.Relative));
            Console.WriteLine(label + " => " + Describe(streamInfo?.Stream));
        }
        catch (Exception ex)
        {
            Console.WriteLine(label + " => fail " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void DumpAssemblyResources(Assembly assembly, string label)
    {
        Console.WriteLine("Assembly resources for " + label + ": " + Describe(assembly));
        foreach (var name in assembly.GetManifestResourceNames())
        {
            Console.WriteLine("  manifest: " + name);
            if (!name.EndsWith(".g.resources", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
            {
                continue;
            }

            using var reader = new System.Resources.ResourceReader(stream);
            foreach (DictionaryEntry entry in reader)
            {
                Console.WriteLine("    " + entry.Key + " => " + Describe(entry.Value));
            }
        }
    }

    private static void DumpWindow(Window window)
    {
        Console.WriteLine("Window type: " + window.GetType().FullName);
        Console.WriteLine("DataContext: " + Describe(window.DataContext));

        var dockingManager = GetFieldValue(window, "uxDockingManager");
        Console.WriteLine("DockingManager: " + Describe(dockingManager));

        var currentDocument = GetPropertyValue(window.DataContext, "CurrentDocument");
        Console.WriteLine("CurrentDocument: " + Describe(currentDocument));
        Console.WriteLine("CurrentDocument.OutlineRoot: " + Describe(GetPropertyValue(currentDocument, "OutlineRoot")));
        Console.WriteLine("CurrentDocument.DesignContext.RootItem: " + Describe(GetPropertyValue(GetPropertyValue(currentDocument, "DesignContext"), "RootItem")));
        DumpXamlErrors(currentDocument);

        DumpAvalonDockOutlineContent(dockingManager);

        var outlines = FindObjects(window, o => o.GetType().FullName == "ICSharpCode.WpfDesign.Designer.OutlineView.Outline").ToList();
        Console.WriteLine("Outline instances found: " + outlines.Count);

        for (var i = 0; i < outlines.Count; i++)
        {
            DumpOutline(outlines[i], i);
        }
    }

    private static void DumpRuntimeInternals(Window window)
    {
        Console.WriteLine("=== Runtime XAML Internals ===");
        DumpResourceDictionaries("Window.Resources", window.Resources, depth: 0, maxDepth: 2, maxKeysPerDictionary: 20);
        DumpElementRuntimeState("Window", window);
        DumpVisualTreeSnapshot(window, maxDepth: 4, maxChildrenPerNode: 25);
    }

    private static void DumpXamlErrors(object? currentDocument)
    {
        var errorService = GetPropertyValue(currentDocument, "XamlErrorService");
        var errors = GetPropertyValue(errorService, "Errors") as IEnumerable;
        if (errors == null)
        {
            Console.WriteLine("CurrentDocument.XamlErrors: <none>");
            return;
        }

        var count = 0;
        foreach (var error in errors)
        {
            count++;
            Console.WriteLine("CurrentDocument.XamlError[" + count + "]: " + Describe(GetPropertyValue(error, "Message")));
        }

        Console.WriteLine("CurrentDocument.XamlErrors.Count: " + count);
    }

    private static void DumpAvalonDockOutlineContent(object? dockingManager)
    {
        var layout = GetPropertyValue(dockingManager, "Layout");
        Console.WriteLine("DockingManager.Layout: " + Describe(layout));
        var descendants = Invoke(layout, "Descendents") as IEnumerable;
        if (descendants == null)
        {
            Console.WriteLine("Layout descendants: <not available>");
            return;
        }

        foreach (var item in descendants)
        {
            var contentId = GetPropertyValue(item, "ContentId") as string;
            if (!string.Equals(contentId, "Outline", StringComparison.Ordinal))
            {
                continue;
            }

            Console.WriteLine("Outline anchorable: " + Describe(item));
            Console.WriteLine("Outline anchorable.Content: " + Describe(GetPropertyValue(item, "Content")));
            Console.WriteLine("Outline anchorable.IsVisible: " + Describe(GetPropertyValue(item, "IsVisible")));
            Console.WriteLine("Outline anchorable.IsHidden: " + Describe(GetPropertyValue(item, "IsHidden")));
        }
    }

    private static void DumpOutline(object outline, int index)
    {
        Console.WriteLine($"Outline[{index}]: " + Describe(outline));
        Console.WriteLine($"Outline[{index}].DataContext: " + Describe(GetPropertyValue(outline, "DataContext")));
        Console.WriteLine($"Outline[{index}].Root: " + Describe(GetPropertyValue(outline, "Root")));

        if (outline is DependencyObject outlineDo)
        {
            DumpBinding("Outline.Root", outlineDo, GetDependencyProperty(outline.GetType(), "RootProperty"));
        }

        var outlineTreeView = GetFieldValue(outline, "OutlineTreeView") ?? FindObjects((DependencyObject)outline, o => o.GetType().FullName?.EndsWith(".OutlineTreeView", StringComparison.Ordinal) == true).FirstOrDefault();
        Console.WriteLine($"Outline[{index}].OutlineTreeView: " + Describe(outlineTreeView));
        Console.WriteLine($"Outline[{index}].OutlineTreeView.Root: " + Describe(GetPropertyValue(outlineTreeView, "Root")));
        Console.WriteLine($"Outline[{index}].OutlineTreeView.ItemsSource: " + Describe(GetPropertyValue(outlineTreeView, "ItemsSource")));
        Console.WriteLine($"Outline[{index}].OutlineTreeView.Items.Count: " + Describe(GetPropertyValue(GetPropertyValue(outlineTreeView, "Items"), "Count")));

        if (outlineTreeView is FrameworkElement treeElement)
        {
            treeElement.ApplyTemplate();
            var dragTreeViewType = outlineTreeView.GetType().BaseType;
            var defaultStyleKeyProperty = GetDependencyProperty(typeof(FrameworkElement), "DefaultStyleKeyProperty");
            Console.WriteLine($"Outline[{index}].OutlineTreeView.DefaultStyleKey: " + Describe(defaultStyleKeyProperty == null ? null : treeElement.GetValue(defaultStyleKeyProperty)));
            Console.WriteLine($"Outline[{index}].OutlineTreeView.TryFindResource(OutlineTreeView): " + Describe(TryFindResource(treeElement, outlineTreeView.GetType())));
            Console.WriteLine($"Outline[{index}].OutlineTreeView.TryFindResource(DragTreeView): " + Describe(TryFindResource(treeElement, dragTreeViewType)));
            Console.WriteLine($"Outline[{index}].OutlineTreeView.Template: " + Describe((treeElement as Control)?.Template));
            Console.WriteLine($"Outline[{index}].OutlineTreeView.Style: " + Describe(treeElement.Style));
            DumpBinding("OutlineTreeView.Root", treeElement, GetDependencyProperty(outlineTreeView.GetType().BaseType ?? outlineTreeView.GetType(), "RootProperty"));
            DumpElementRuntimeState($"Outline[{index}].OutlineTreeView", treeElement);
        }

        if (outline is FrameworkElement outlineElement)
        {
            DumpElementRuntimeState($"Outline[{index}]", outlineElement);
        }
    }

    private static void DumpElementRuntimeState(string label, FrameworkElement element)
    {
        Console.WriteLine("--- " + label + " internals ---");
        Console.WriteLine(label + ".Name: " + Describe(element.Name));
        Console.WriteLine(label + ".IsLoaded: " + Describe(element.IsLoaded));
        Console.WriteLine(label + ".DataContext: " + Describe(element.DataContext));
        Console.WriteLine(label + ".TemplatedParent: " + Describe(element.TemplatedParent));
        Console.WriteLine(label + ".Style: " + Describe(element.Style));
        Console.WriteLine(label + ".Resources.Count: " + Describe(element.Resources?.Count));

        if (element is Control control)
        {
            Console.WriteLine(label + ".Template: " + Describe(control.Template));
        }

        DumpResourceProbe(label, element);
        DumpLocalValueSources(label, element, maxEntries: 50);
        DumpNameScope(label, element);
    }

    private static void DumpResourceProbe(string label, FrameworkElement element)
    {
        var keys = new object[]
        {
            SystemColors.ControlBrushKey,
            SystemColors.ControlTextBrushKey,
            SystemColors.WindowBrushKey,
            SystemColors.WindowTextBrushKey,
            SystemColors.MenuBrushKey,
            SystemColors.MenuTextBrushKey
        };

        Console.WriteLine(label + ".ResourceProbe:");
        foreach (var key in keys)
        {
            Console.WriteLine("  " + Describe(key) + " => " + Describe(TryFindResource(element, key)));
        }
    }

    private static void DumpResourceDictionaries(string label, ResourceDictionary? dictionary, int depth, int maxDepth, int maxKeysPerDictionary)
    {
        var seen = new HashSet<ResourceDictionary>(RefEqComparer<ResourceDictionary>.Instance);
        Dump(label, dictionary, depth);
        return;

        void Dump(string currentLabel, ResourceDictionary? currentDictionary, int currentDepth)
        {
            var indent = new string(' ', currentDepth * 2);
            if (currentDictionary == null)
            {
                Console.WriteLine(indent + currentLabel + ": <null>");
                return;
            }

            if (!seen.Add(currentDictionary))
            {
                Console.WriteLine(indent + currentLabel + ": <already visited>");
                return;
            }

            Console.WriteLine(indent + currentLabel + ": " + Describe(currentDictionary));
            Console.WriteLine(indent + "  Source: " + Describe(currentDictionary.Source));
            Console.WriteLine(indent + "  Keys.Count: " + Describe(currentDictionary.Keys.Count));

            var keyCount = 0;
            foreach (var key in currentDictionary.Keys)
            {
                keyCount++;
                if (keyCount > maxKeysPerDictionary)
                {
                    Console.WriteLine(indent + "  ... keys truncated after " + maxKeysPerDictionary);
                    break;
                }

                Console.WriteLine(indent + "  key[" + keyCount + "]: " + Describe(key));
            }

            if (currentDepth >= maxDepth)
            {
                Console.WriteLine(indent + "  merged dictionaries truncated at depth " + maxDepth);
                return;
            }

            var merged = currentDictionary.MergedDictionaries;
            for (var i = 0; i < merged.Count; i++)
            {
                Dump(currentLabel + ".Merged[" + i + "]", merged[i], currentDepth + 1);
            }
        }
    }

    private static void DumpLocalValueSources(string label, DependencyObject element, int maxEntries)
    {
        var localValues = element.GetLocalValueEnumerator();
        var count = 0;
        while (localValues.MoveNext())
        {
            count++;
            if (count > maxEntries)
            {
                Console.WriteLine(label + ".LocalValues: truncated after " + maxEntries + " entries.");
                break;
            }

            var entry = localValues.Current;
            var property = entry.Property;
            var source = DependencyPropertyHelper.GetValueSource(element, property);
            Console.WriteLine(label + ".LocalValue[" + count + "] " + property.OwnerType.Name + "." + property.Name + ": " + Describe(entry.Value));
            Console.WriteLine("  ValueSource: Base=" + source.BaseValueSource + ", IsExpression=" + source.IsExpression + ", IsAnimated=" + source.IsAnimated + ", IsCoerced=" + source.IsCoerced + ", IsCurrent=" + source.IsCurrent);

            var bindingExpression = BindingOperations.GetBindingExpressionBase(element, property);
            if (bindingExpression != null)
            {
                Console.WriteLine("  BindingExpression: " + Describe(bindingExpression));
                Console.WriteLine("  BindingStatus: " + Describe(GetPropertyValue(bindingExpression, "Status")));
            }
        }

        if (count == 0)
        {
            Console.WriteLine(label + ".LocalValues: <none>");
        }
    }

    private static void DumpNameScope(string label, FrameworkElement element)
    {
        var nameScope = NameScope.GetNameScope(element);
        Console.WriteLine(label + ".NameScope: " + Describe(nameScope));
        if (nameScope == null)
        {
            return;
        }

        try
        {
            var mapField = nameScope.GetType().GetField("_nameMap", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? nameScope.GetType().GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic);
            var map = mapField?.GetValue(nameScope) as IDictionary;
            if (map == null)
            {
                Console.WriteLine(label + ".NameScope entries: <not enumerable>");
                return;
            }

            var index = 0;
            foreach (DictionaryEntry entry in map)
            {
                index++;
                Console.WriteLine(label + ".NameScope[" + index + "]: " + Describe(entry.Key) + " => " + Describe(entry.Value));
            }

            if (index == 0)
            {
                Console.WriteLine(label + ".NameScope entries: <empty>");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(label + ".NameScope inspection failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void DumpVisualTreeSnapshot(DependencyObject root, int maxDepth, int maxChildrenPerNode)
    {
        Console.WriteLine("Visual tree snapshot (depth <= " + maxDepth + "):");
        DumpNode(root, depth: 0);

        void DumpNode(DependencyObject node, int depth)
        {
            var indent = new string(' ', depth * 2);
            var name = (node as FrameworkElement)?.Name;
            Console.WriteLine(indent + "- " + node.GetType().FullName + (string.IsNullOrWhiteSpace(name) ? string.Empty : " #" + name));

            if (depth >= maxDepth)
            {
                return;
            }

            if (node is Visual or Visual3D)
            {
                var childCount = VisualTreeHelper.GetChildrenCount(node);
                var max = Math.Min(childCount, maxChildrenPerNode);
                for (var i = 0; i < max; i++)
                {
                    DumpNode(VisualTreeHelper.GetChild(node, i), depth + 1);
                }

                if (childCount > maxChildrenPerNode)
                {
                    Console.WriteLine(indent + "  ... children truncated after " + maxChildrenPerNode + " nodes");
                }
            }
        }
    }

    private static void DumpBinding(string label, DependencyObject target, DependencyProperty? property)
    {
        if (property == null)
        {
            Console.WriteLine(label + " binding: <missing dependency property>");
            return;
        }

        var expression = BindingOperations.GetBindingExpressionBase(target, property);
        Console.WriteLine(label + " binding: " + Describe(expression));
        if (expression != null)
        {
            Console.WriteLine(label + " binding.Status: " + Describe(GetPropertyValue(expression, "Status")));
            Console.WriteLine(label + " binding.ParentBinding: " + Describe(GetPropertyValue(expression, "ParentBindingBase")));
        }
    }

    private static List<object> FindObjects(DependencyObject root, Func<object, bool> predicate)
    {
        var results = new List<object>();
        var seen = new HashSet<object>(RefEqComparer<object>.Instance);
        Walk(root);
        return results;

        void Walk(object? value)
        {
            if (value == null || !seen.Add(value))
            {
                return;
            }

            if (predicate(value))
            {
                results.Add(value);
            }

            if (value is DependencyObject dependencyObject)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject).OfType<object>())
                {
                    Walk(child);
                }

                if (dependencyObject is Visual or Visual3D)
                {
                    var count = VisualTreeHelper.GetChildrenCount(dependencyObject);
                    for (var i = 0; i < count; i++)
                    {
                        Walk(VisualTreeHelper.GetChild(dependencyObject, i));
                    }
                }
            }
        }
    }

    private static DependencyProperty? GetDependencyProperty(Type type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field?.GetValue(null) is DependencyProperty property)
            {
                return property;
            }

            type = type.BaseType!;
        }

        return null;
    }

    private static object? GetFieldValue(object? instance, string fieldName)
    {
        if (instance == null)
        {
            return null;
        }

        var type = instance.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            type = type.BaseType;
        }

        return null;
    }

    private static object? GetPropertyValue(object? instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        try
        {
            return property?.GetValue(instance);
        }
        catch (Exception ex)
        {
            return "<throws " + ex.GetType().Name + ": " + ex.Message + ">";
        }
    }

    private static bool SetPropertyValue(object? instance, string propertyName, object? value)
    {
        if (instance == null)
        {
            return false;
        }

        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property == null || !property.CanWrite)
        {
            return false;
        }

        property.SetValue(instance, value);
        return true;
    }

    private static object? Invoke(object? instance, string methodName)
    {
        if (instance == null)
        {
            return null;
        }

        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, types: Type.EmptyTypes, modifiers: null);
        return method?.Invoke(instance, null);
    }

    private static object? Invoke(object? instance, string methodName, params object[] args)
    {
        if (instance == null)
        {
            return null;
        }

        var argumentTypes = args.Select(a => a?.GetType() ?? typeof(object)).ToArray();
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, types: argumentTypes, modifiers: null);
        return method?.Invoke(instance, args);
    }

    private sealed class RefEqComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly RefEqComparer<T> Instance = new();

        public bool Equals(T? x, T? y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }

    private static object? TryFindResource(FrameworkElement element, object? key)
    {
        if (key == null)
        {
            return null;
        }

        try
        {
            return element.TryFindResource(key);
        }
        catch (Exception ex)
        {
            return "<throws " + ex.GetType().Name + ": " + ex.Message + ">";
        }
    }

    private static object? GetDefaultStyleKey(FrameworkElement element)
    {
        // DefaultStyleKey is a protected member; access via the dependency property through reflection.
        try
        {
            var dp = typeof(FrameworkElement).GetField("DefaultStyleKeyProperty",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) as DependencyProperty;
            return dp != null ? element.GetValue(dp) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DumpStyleDetails(string label, Style? style)
    {
        if (style == null)
        {
            Console.WriteLine(label + ": <null>");
            return;
        }

        Console.WriteLine(label + ": TargetType=" + Describe(style.TargetType) + ", BasedOn=" + Describe(style.BasedOn) + ", Setters=" + style.Setters.Count + ", Triggers=" + style.Triggers.Count);
        var i = 0;
        foreach (var setterBase in style.Setters)
        {
            i++;
            if (setterBase is Setter setter)
            {
                Console.WriteLine("      Setter[" + i + "]: Property=" + setter.Property?.Name + ", Value=" + Describe(setter.Value));
            }
            else
            {
                Console.WriteLine("      Setter[" + i + "]: " + Describe(setterBase));
            }
        }
    }

    private static void DumpControlColorState(string indent, Control control)
    {
        try
        {
            var fg = control.GetValue(Control.ForegroundProperty);
            var bg = control.GetValue(Control.BackgroundProperty);
            Console.WriteLine(indent + "Foreground=" + Describe(fg));
            Console.WriteLine(indent + "Background=" + Describe(bg));
            var fgSource = DependencyPropertyHelper.GetValueSource(control, Control.ForegroundProperty);
            var bgSource = DependencyPropertyHelper.GetValueSource(control, Control.BackgroundProperty);
            Console.WriteLine(indent + "Foreground ValueSource: Base=" + fgSource.BaseValueSource + ", IsExpression=" + fgSource.IsExpression + ", IsAnimated=" + fgSource.IsAnimated + ", IsCoerced=" + fgSource.IsCoerced + ", IsCurrent=" + fgSource.IsCurrent);
            Console.WriteLine(indent + "Background ValueSource: Base=" + bgSource.BaseValueSource + ", IsExpression=" + bgSource.IsExpression + ", IsAnimated=" + bgSource.IsAnimated + ", IsCoerced=" + bgSource.IsCoerced + ", IsCurrent=" + bgSource.IsCurrent);
        }
        catch (Exception ex)
        {
            Console.WriteLine(indent + "Color state probe failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void DumpAvalonEditTextState(FrameworkElement element, string indent)
    {
        try
        {
            var t = element.GetType();
            if (!string.Equals(t.FullName, "ICSharpCode.AvalonEdit.AddIn.CodeEditorView", StringComparison.Ordinal)
                && !string.Equals(t.FullName, "ICSharpCode.AvalonEdit.TextEditor", StringComparison.Ordinal)
                && t.FullName!.IndexOf("AvalonEdit", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            var textProp = t.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
            var text = textProp?.GetValue(element) as string;
            Console.WriteLine(indent + "Text length=" + (text?.Length ?? -1));

            var docProp = t.GetProperty("Document", BindingFlags.Public | BindingFlags.Instance);
            var doc = docProp?.GetValue(element);
            Console.WriteLine(indent + "Document=" + Describe(doc));
            if (doc != null)
            {
                var lineCountProp = doc.GetType().GetProperty("LineCount", BindingFlags.Public | BindingFlags.Instance);
                var textLengthProp = doc.GetType().GetProperty("TextLength", BindingFlags.Public | BindingFlags.Instance);
                Console.WriteLine(indent + "Document.LineCount=" + Describe(lineCountProp?.GetValue(doc)) + ", TextLength=" + Describe(textLengthProp?.GetValue(doc)));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(indent + "AvalonEdit text-state probe failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static string Describe(object? value)
    {
        if (value == null)
        {
            return "<null>";
        }

        if (value is string text)
        {
            return "\"" + text + "\"";
        }

        if (value is IEnumerable enumerable && value is not DependencyObject)
        {
            var count = 0;
            foreach (var _ in enumerable)
            {
                count++;
                if (count > 100)
                {
                    return value.GetType().FullName + " (count > 100)";
                }
            }

            return value.GetType().FullName + " (count " + count + ")";
        }

        return value.GetType().FullName + " = " + value;
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
