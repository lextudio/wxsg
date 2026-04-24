using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.WPF.Binding;

internal static class XmlnsDefinitionCache
{
    private const string WpfXmlnsDefinitionAttributeMetadataName =
        "System.Windows.Markup.XmlnsDefinitionAttribute";

    private const string WpfPresentationXmlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string[] WpfPresentationFallbackClrNamespaces =
    {
        "System.Windows",
        "System.Windows.Automation",
        "System.Windows.Controls",
        "System.Windows.Controls.Primitives",
        "System.Windows.Documents",
        "System.Windows.Input",
        "System.Windows.Media",
        "System.Windows.Media.Animation",
        "System.Windows.Navigation",
        "System.Windows.Shapes"
    };

    // Cache the XmlnsDefinition map per compilation to avoid repeated assembly scans.
    private static readonly ConditionalWeakTable<Compilation, XmlnsDefinitionCacheEntry> XmlnsCache = new();

    internal static XmlnsDefinitionCacheEntry GetOrBuildXmlnsDefinitionMap(Compilation compilation) =>
        XmlnsCache.GetValue(compilation, static c => BuildXmlnsDefinitionMap(c));

    internal static XmlnsDefinitionCacheEntry BuildXmlnsDefinitionMap(Compilation compilation)
    {
        var map = new Dictionary<string, List<XmlnsDefinitionMapping>>(StringComparer.Ordinal);

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attr in assembly.GetAttributes())
            {
                if (!IsXmlnsDefinitionAttribute(attr) ||
                    attr.ConstructorArguments.Length < 2 ||
                    attr.ConstructorArguments[0].Value is not string xmlNamespace ||
                    attr.ConstructorArguments[1].Value is not string clrNamespace)
                {
                    continue;
                }

                string? mappedAssemblyName = null;
                foreach (var namedArgument in attr.NamedArguments)
                {
                    if (!namedArgument.Key.Equals("AssemblyName", StringComparison.Ordinal) ||
                        namedArgument.Value.Value is not string assemblyName ||
                        string.IsNullOrWhiteSpace(assemblyName))
                    {
                        continue;
                    }

                    mappedAssemblyName = assemblyName;
                    break;
                }

                if (!map.TryGetValue(xmlNamespace, out var list))
                {
                    list = new List<XmlnsDefinitionMapping>();
                    map[xmlNamespace] = list;
                }

                list.Add(new XmlnsDefinitionMapping(clrNamespace, mappedAssemblyName));
            }
        }

        if (!map.ContainsKey(WpfPresentationXmlNamespace))
        {
            var fallbackMappings = new List<XmlnsDefinitionMapping>(WpfPresentationFallbackClrNamespaces.Length);
            foreach (var clrNamespace in WpfPresentationFallbackClrNamespaces)
            {
                fallbackMappings.Add(new XmlnsDefinitionMapping(clrNamespace, assemblyName: null));
            }

            map[WpfPresentationXmlNamespace] = fallbackMappings;
        }

        return new XmlnsDefinitionCacheEntry(map);
    }

    internal static bool IsXmlnsDefinitionAttribute(AttributeData attribute)
    {
        return string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            WpfXmlnsDefinitionAttributeMetadataName,
            StringComparison.Ordinal);
    }

    internal static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        if (visited.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }

        foreach (var referenced in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referenced is not null && visited.Add(referenced))
            {
                yield return referenced;
            }
        }
    }
}

internal sealed class XmlnsDefinitionCacheEntry
{
    public static XmlnsDefinitionCacheEntry Empty { get; } = new(
        new Dictionary<string, List<XmlnsDefinitionMapping>>(StringComparer.Ordinal));

    private readonly Dictionary<string, List<XmlnsDefinitionMapping>> _map;

    public XmlnsDefinitionCacheEntry(Dictionary<string, List<XmlnsDefinitionMapping>> map)
    {
        _map = map;
    }

    public bool TryGetNamespaces(string xmlNamespace, out IReadOnlyList<XmlnsDefinitionMapping> namespaces)
    {
        if (_map.TryGetValue(xmlNamespace, out var list))
        {
            namespaces = list;
            return true;
        }

        namespaces = Array.Empty<XmlnsDefinitionMapping>();
        return false;
    }
}

internal sealed class XmlnsDefinitionMapping
{
    public XmlnsDefinitionMapping(string clrNamespace, string? assemblyName)
    {
        ClrNamespace = clrNamespace;
        AssemblyName = assemblyName;
    }

    public string ClrNamespace { get; }

    public string? AssemblyName { get; }
}
