using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.WPF.Binding;
using XamlToCSharpGenerator.WPF.Emission;

namespace XamlToCSharpGenerator.WPF.Framework;

/// <summary>
/// XSG framework profile for WPF.
///
/// Follows the same pattern as <c>AvaloniaFrameworkProfile</c> in the XSG engine —
/// a singleton that wires together the WPF-specific semantic binder, code emitter,
/// build contract, and transform provider.
///
/// WPF namespace conventions:
///   xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"   (default)
///   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
///   xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
///   xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
///   xmlns:local="clr-namespace:MyApp"
///
/// Unlike Avalonia, WPF does not use global xmlns prefix attributes injected at the
/// assembly level. Type-to-namespace mappings are discovered at bind time via
/// <c>System.Windows.Markup.XmlnsDefinitionAttribute</c>.
/// </summary>
public sealed class WpfFrameworkProfile : IXamlFrameworkProfile
{
    private const string AvaloniaImplicitDefaultXmlns =
        "https://github.com/avaloniaui";

    private const string WpfXmlnsPrefixAttributeMetadataName =
        "System.Windows.Markup.XmlnsPrefixAttribute";

    private static readonly IXamlFrameworkBuildContract BuildContractInstance =
        WpfFrameworkBuildContract.Instance;

    private static readonly IXamlFrameworkTransformProvider TransformProviderInstance =
        WpfFrameworkTransformProvider.Instance;

    private static readonly IXamlFrameworkSemanticBinder SemanticBinderInstance =
        new WpfFrameworkSemanticBinder(new WpfSemanticBinder());

    private static readonly IXamlFrameworkEmitter EmitterInstance =
        new WpfFrameworkEmitter(new WpfCodeEmitter());

    public static WpfFrameworkProfile Instance { get; } = new();
    private WpfFrameworkProfile() { }

    public string Id => "WPF";

    public IXamlFrameworkBuildContract BuildContract => BuildContractInstance;

    public IXamlFrameworkTransformProvider TransformProvider => TransformProviderInstance;

    public IXamlFrameworkSemanticBinder CreateSemanticBinder() => SemanticBinderInstance;

    public IXamlFrameworkEmitter CreateEmitter() => EmitterInstance;

    /// <summary>
    /// WPF files carry no document enrichers (Phase 1).
    /// Avalonia uses enrichers to inject x:Name members; WPF relies on the name scope
    /// populated during BAML loading (Phase 1) and will switch to direct object construction
    /// in Phase 3.
    /// </summary>
    public ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers() =>
        ImmutableArray<IXamlDocumentEnricher>.Empty;

    /// <summary>
    /// MAUI-style "simpler XAML" support for WXSG:
    /// 1. Implicit default namespace points to WPF presentation.
    /// 2. Standard prefixes x:/d:/mc: can be globalized.
    /// 3. Global prefixes can come from assembly-level XmlnsPrefix attributes and
    ///    from GlobalXmlnsPrefixes options.
    /// </summary>
    public XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options) =>
        BuildSimplerXamlParserSettings(compilation, options);

    private static XamlFrameworkParserSettings BuildSimplerXamlParserSettings(
        Compilation compilation,
        GeneratorOptions options)
    {
        var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var allowImplicitXmlns = true;

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsXmlnsPrefixAttribute(attribute))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                    attribute.ConstructorArguments[1].Value is not string prefix ||
                    string.IsNullOrWhiteSpace(prefix) ||
                    string.IsNullOrWhiteSpace(xmlNamespace))
                {
                    continue;
                }

                globalPrefixes[prefix.Trim()] = xmlNamespace.Trim();
            }
        }

        foreach (var entry in ParseGlobalXmlnsPrefixesProperty(options.GlobalXmlnsPrefixes))
        {
            globalPrefixes[entry.Key] = entry.Value;
        }

        if (allowImplicitXmlns &&
            options.ImplicitStandardXmlnsPrefixesEnabled)
        {
            AddImplicitPrefix(globalPrefixes, "x", WpfXmlNamespaces.Xaml);
            AddImplicitPrefix(globalPrefixes, "d", WpfXmlNamespaces.BlendDesign);
            AddImplicitPrefix(globalPrefixes, "mc", WpfXmlNamespaces.MarkupCompatibility);
        }

        var implicitDefaultXmlns = string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns) ||
                                   string.Equals(
                                       options.ImplicitDefaultXmlns,
                                       AvaloniaImplicitDefaultXmlns,
                                       StringComparison.Ordinal)
            ? WpfXmlNamespaces.Presentation
            : options.ImplicitDefaultXmlns;

        if (allowImplicitXmlns &&
            !globalPrefixes.ContainsKey(string.Empty))
        {
            globalPrefixes[string.Empty] = implicitDefaultXmlns;
        }

        return new XamlFrameworkParserSettings(
            globalPrefixes.ToImmutable(),
            allowImplicitXmlns,
            implicitDefaultXmlns);
    }

    private static void AddImplicitPrefix(
        ImmutableDictionary<string, string>.Builder globalPrefixes,
        string prefix,
        string xmlNamespace)
    {
        if (!globalPrefixes.ContainsKey(prefix))
        {
            globalPrefixes[prefix] = xmlNamespace;
        }
    }

    private static bool IsXmlnsPrefixAttribute(AttributeData attribute)
    {
        return string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            WpfXmlnsPrefixAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referencedAssembly is not null && visited.Add(referencedAssembly))
            {
                yield return referencedAssembly;
            }
        }

        if (visited.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }
    }

    private static ImmutableDictionary<string, string> ParseGlobalXmlnsPrefixesProperty(string? rawValue)
    {
        if (rawValue is null)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var trimmedRawValue = rawValue.Trim();
        if (trimmedRawValue.Length == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var span = trimmedRawValue.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            while (index < span.Length && IsGlobalPrefixDelimiter(span[index]))
            {
                index++;
            }

            if (index >= span.Length)
            {
                break;
            }

            var entryStart = index;
            while (index < span.Length && !IsGlobalPrefixDelimiter(span[index]))
            {
                index++;
            }

            var entry = span.Slice(entryStart, index - entryStart).Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex < 0 || separatorIndex >= entry.Length - 1)
            {
                continue;
            }

            var prefix = entry.Slice(0, separatorIndex).Trim();
            var xmlNamespace = entry.Slice(separatorIndex + 1).Trim();
            if (xmlNamespace.Length == 0)
            {
                continue;
            }

            map[prefix.ToString()] = xmlNamespace.ToString();
        }

        return map.ToImmutable();
    }

    private static bool IsGlobalPrefixDelimiter(char character)
    {
        return character == ';' || character == ',' || character == '\r' || character == '\n';
    }

    // -------------------------------------------------------------------------
    // Private adapter classes — mirrors AvaloniaFrameworkProfile's nested classes
    // -------------------------------------------------------------------------

    private sealed class WpfFrameworkSemanticBinder : IXamlFrameworkSemanticBinder
    {
        private readonly IXamlSemanticBinder _inner;
        public WpfFrameworkSemanticBinder(IXamlSemanticBinder inner) => _inner = inner;

        public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            XamlTransformConfiguration transformConfiguration)
            => _inner.Bind(document, compilation, options, transformConfiguration);
    }

    private sealed class WpfFrameworkEmitter : IXamlFrameworkEmitter
    {
        private readonly IXamlCodeEmitter _inner;
        public WpfFrameworkEmitter(IXamlCodeEmitter inner) => _inner = inner;

        public (string HintName, string Source) Emit(ResolvedViewModel viewModel)
            => _inner.Emit(viewModel);
    }
}
