using System.Collections;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

internal static class Program
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
    private static readonly string XamlDesignerProject = Path.Combine(RepoRoot, "WpfDesigner", "XamlDesigner");
    private static readonly string XamlDesignerOutput = Path.Combine(RepoRoot, "WpfDesigner", "XamlDesigner", "bin", "Debug", "net10.0-windows");

    [STAThread]
    private static int Main()
    {
        Directory.SetCurrentDirectory(XamlDesignerProject);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromXamlDesignerOutput;

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

        Pump();
        Pump();
        EnsureNewDocument(assembly);
        Pump();

        ProbeWindowClone();
        DumpSharedInstancesGenericCctorIl();
        DumpWindowCloneCctorIl();
        DumpWindow(window);
        DumpAssemblyResources("ICSharpCode.WpfDesign.Designer");

        window.Close();
        app.Shutdown();
        return 0;
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
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
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

    private static object? Invoke(object? instance, string methodName)
    {
        if (instance == null)
        {
            return null;
        }

        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
        return method?.Invoke(instance, null);
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
