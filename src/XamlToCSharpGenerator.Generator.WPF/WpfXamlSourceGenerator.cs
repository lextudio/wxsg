using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.WPF.Framework;

namespace XamlToCSharpGenerator.Generator.WPF;

/// <summary>
/// Roslyn incremental source generator for WPF XAML (WXSG).
///
/// Plugs <see cref="WpfFrameworkProfile"/> into the shared
/// <see cref="XamlSourceGeneratorCompilerHost"/> pipeline from the XSG engine.
/// This mirrors what <c>AvaloniaXamlSourceGenerator</c> does via
/// <c>FrameworkXamlSourceGenerator</c>, but calls the public compiler host API
/// directly so the generator can live outside the XSG submodule.
///
/// Phase 1 output for each <c>&lt;Page /&gt;</c> / <c>&lt;ApplicationDefinition /&gt;</c> file:
/// <list type="bullet">
///   <item>Typed field declarations for all <c>x:Name</c> elements</item>
///   <item><c>InitializeComponent()</c> backed by <c>Application.LoadComponent</c></item>
/// </list>
///
/// Additionally, when classless XAML files (ResourceDictionaries, theme dictionaries such as
/// generic.xaml) are passed as AdditionalFiles with SourceItemGroup=ClasslessPage, this generator
/// emits a <c>__WxsgThemeLoader</c> class with a <c>[ModuleInitializer]</c> that merges those
/// dictionaries into <c>Application.Resources</c> at assembly load time. This replaces WPF's
/// BAML-based theme/template lookup for assemblies using WXSG.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class WpfXamlSourceGenerator : IIncrementalGenerator
{
    private const string ClasslessPageGroup = "ClasslessPage";
    private const string SourceItemGroupMetadataKey = "build_metadata.AdditionalFiles.SourceItemGroup";
    private const string TargetPathMetadataKey = "build_metadata.AdditionalFiles.TargetPath";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        XamlSourceGeneratorCompilerHost.Initialize(context, WpfFrameworkProfile.Instance);
        InitializeClasslessThemeLoader(context);
    }

    private static void InitializeClasslessThemeLoader(IncrementalGeneratorInitializationContext context)
    {
        // Collect TargetPath metadata for all AdditionalFiles with SourceItemGroup=ClasslessPage.
        // These are classless XAML ResourceDictionaries (no x:Class) that WXSG cannot generate
        // InitializeComponent for, but which need to be loaded at runtime for WPF to find
        // control templates (e.g. DockingManager's template in generic.xaml).
        var classlessPaths = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, _) =>
            {
                var text = pair.Left;
                var optionsProvider = pair.Right;

                if (!text.Path.EndsWith(".xaml", System.StringComparison.OrdinalIgnoreCase))
                    return null;

                var options = optionsProvider.GetOptions(text);
                options.TryGetValue(SourceItemGroupMetadataKey, out var sourceItemGroup);
                if (!string.Equals(sourceItemGroup, ClasslessPageGroup, System.StringComparison.OrdinalIgnoreCase))
                    return null;

                options.TryGetValue(TargetPathMetadataKey, out var targetPath);
                if (string.IsNullOrWhiteSpace(targetPath))
                    targetPath = System.IO.Path.GetFileName(text.Path);

                // Normalize path separators to forward slashes for pack:// URIs.
                return targetPath!.Replace('\\', '/');
            })
            .Where(static p => p is not null)
            .Select(static (p, _) => p!);

        var classlessSnapshot = classlessPaths.Collect();

        var assemblyName = context.CompilationProvider
            .Select(static (c, _) => c.AssemblyName ?? string.Empty);

        var combined = classlessSnapshot.Combine(assemblyName);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var paths = pair.Left;
            var asmName = pair.Right;

            if (paths.IsDefaultOrEmpty || string.IsNullOrEmpty(asmName))
                return;

            // Emit ModuleInitializerAttribute polyfill so [ModuleInitializer] compiles on
            // .NET Framework 4.x targets where System.Runtime.CompilerServices does not yet
            // include the attribute. On .NET 5+ the real attribute is already in the BCL and
            // the polyfill is skipped by the #if guard.
            spc.AddSource("__WxsgModuleInitializerPolyfill.wpf.g.cs", BuildPolyfillSource());

            // Emit a helper that loads classless XAML with same-assembly clr-namespace
            // mappings restored. This lets loose ResourceDictionaries behave more like the
            // original BAML path, without forcing explicit ;assembly=... xmlns edits.
            spc.AddSource("__WxsgClasslessXamlLoader.wpf.g.cs", BuildClasslessXamlLoaderSource(asmName));

            // Emit the actual theme loader.
            spc.AddSource("__WxsgThemeLoader.wpf.g.cs", BuildThemeLoaderSource(asmName, paths));
        });
    }

    private static string BuildPolyfillSource()
    {
        return
            """
            // <auto-generated/>
            // ReSharper disable All
            #nullable enable
            #if !NET5_0_OR_GREATER
            namespace System.Runtime.CompilerServices
            {
                [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false)]
                internal sealed class ModuleInitializerAttribute : global::System.Attribute { }
            }
            #endif

            """;
    }

    private static string BuildThemeLoaderSource(string assemblyName, ImmutableArray<string> targetPaths)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// ReSharper disable All");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace __WxsgGenerated");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>");
        sb.AppendLine("    /// WXSG-generated module initializer that merges classless ResourceDictionaries");
        sb.AppendLine("    /// (theme dictionaries, generic.xaml, etc.) into Application.Resources at");
        sb.AppendLine("    /// assembly load time. This replaces WPF's BAML-based theme/template lookup.");
        sb.AppendLine("    /// </summary>");
        sb.AppendLine("    internal static class __WxsgThemeLoader");
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Initialize()");
        sb.AppendLine("        {");
        sb.AppendLine("            var app = global::System.Windows.Application.Current;");
        sb.AppendLine("            if (app is null) return;");
        sb.AppendLine("            MergeResources(app);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static void MergeResources(global::System.Windows.Application app)");
        sb.AppendLine("        {");
        foreach (var path in targetPaths)
        {
            var packUri = $"pack://application:,,,/{assemblyName};component/{path}";
            sb.AppendLine($"            MergeDict(app, \"{packUri}\");");
        }
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        private static void MergeDict(global::System.Windows.Application app, string uri)");
        sb.AppendLine("        {");
        sb.AppendLine("            app.Resources.MergedDictionaries.Add(");
        sb.AppendLine("                __WxsgClasslessXamlLoader.LoadResourceDictionary(");
        sb.AppendLine("                    new global::System.Uri(uri, global::System.UriKind.Absolute)));");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildClasslessXamlLoaderSource(string assemblyName)
    {
        var escapedAssemblyName = assemblyName.Replace("\"", "\"\"");
        return
            $$"""
            // <auto-generated/>
            // ReSharper disable All
            #nullable enable

            namespace __WxsgGenerated
            {
                internal static class __WxsgClasslessXamlLoader
                {
                    private const string CurrentAssemblyName = "{{escapedAssemblyName}}";

                    internal static global::System.Windows.ResourceDictionary LoadResourceDictionary(global::System.Uri uri)
                    {
                        var effectiveUri = NormalizeResourceUri(uri);
                        var resourceInfo = global::System.Windows.Application.GetResourceStream(effectiveUri);
                        if (resourceInfo?.Stream is null)
                        {
                            return new global::System.Windows.ResourceDictionary
                            {
                                Source = effectiveUri
                            };
                        }

                        using var stream = resourceInfo.Stream;
                        using var streamReader = new global::System.IO.StreamReader(
                            stream,
                            global::System.Text.Encoding.UTF8,
                            detectEncodingFromByteOrderMarks: true);
                        var xaml = streamReader.ReadToEnd();
                        var parserContext = CreateParserContext(effectiveUri, xaml);

                        using var xamlStream = new global::System.IO.MemoryStream(
                            global::System.Text.Encoding.UTF8.GetBytes(xaml));
                        return (global::System.Windows.ResourceDictionary)global::System.Windows.Markup.XamlReader.Load(
                            xamlStream,
                            parserContext);
                    }

                    private static global::System.Uri NormalizeResourceUri(global::System.Uri uri)
                    {
                        if (uri.IsAbsoluteUri)
                        {
                            return uri;
                        }

                        var original = uri.OriginalString;
                        if (!original.StartsWith("/", global::System.StringComparison.Ordinal))
                        {
                            original = "/" + original;
                        }

                        return new global::System.Uri(
                            "pack://application:,,," + original,
                            global::System.UriKind.Absolute);
                    }

                    private static global::System.Windows.Markup.ParserContext CreateParserContext(
                        global::System.Uri baseUri,
                        string xaml)
                    {
                        var context = new global::System.Windows.Markup.ParserContext
                        {
                            BaseUri = baseUri,
                            XamlTypeMapper = new global::System.Windows.Markup.XamlTypeMapper(global::System.Array.Empty<string>())
                        };

                        ApplyXmlnsMappings(context, xaml);
                        return context;
                    }

                    private static void ApplyXmlnsMappings(
                        global::System.Windows.Markup.ParserContext context,
                        string xaml)
                    {
                        using var stringReader = new global::System.IO.StringReader(xaml);
                        using var xmlReader = global::System.Xml.XmlReader.Create(
                            stringReader,
                            new global::System.Xml.XmlReaderSettings
                            {
                                DtdProcessing = global::System.Xml.DtdProcessing.Prohibit
                            });

                        if (xmlReader.MoveToContent() != global::System.Xml.XmlNodeType.Element ||
                            !xmlReader.HasAttributes)
                        {
                            return;
                        }

                        while (xmlReader.MoveToNextAttribute())
                        {
                            string prefix;
                            if (xmlReader.Prefix == "xmlns")
                            {
                                prefix = xmlReader.LocalName;
                            }
                            else if (xmlReader.Name == "xmlns")
                            {
                                prefix = string.Empty;
                            }
                            else
                            {
                                continue;
                            }

                            var xmlnsValue = xmlReader.Value;
                            context.XmlnsDictionary[prefix] = xmlnsValue;

                            if (!xmlnsValue.StartsWith("clr-namespace:", global::System.StringComparison.OrdinalIgnoreCase) ||
                                xmlnsValue.IndexOf(";assembly=", global::System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                continue;
                            }

                            var clrNamespace = ExtractClrNamespace(xmlnsValue);
                            if (string.IsNullOrWhiteSpace(clrNamespace))
                            {
                                continue;
                            }

                            context.XamlTypeMapper.AddMappingProcessingInstruction(
                                xmlnsValue,
                                clrNamespace,
                                CurrentAssemblyName);
                        }

                        xmlReader.MoveToElement();
                    }

                    private static string ExtractClrNamespace(string xmlnsValue)
                    {
                        const string prefix = "clr-namespace:";
                        if (!xmlnsValue.StartsWith(prefix, global::System.StringComparison.OrdinalIgnoreCase))
                        {
                            return string.Empty;
                        }

                        var namespaceValue = xmlnsValue.Substring(prefix.Length);
                        var separatorIndex = namespaceValue.IndexOf(';');
                        return separatorIndex >= 0
                            ? namespaceValue.Substring(0, separatorIndex)
                            : namespaceValue;
                    }
                }
            }

            """;
    }
}

[Generator(LanguageNames.VisualBasic)]
public sealed class WpfXamlVisualBasicSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        XamlSourceGeneratorCompilerHost.Initialize(context, WpfFrameworkProfile.VisualBasicInstance);
    }
}
