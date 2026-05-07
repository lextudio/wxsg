using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
                list.Add(new XmlnsDefinitionMapping(clrNamespace, mappedAssemblyName, assembly));
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
    public XmlnsDefinitionMapping(string clrNamespace, string? assemblyName, IAssemblySymbol? assemblySymbol = null)
    {
        ClrNamespace = clrNamespace;
        AssemblyName = assemblyName;
        _assemblySymbol = assemblySymbol;
    }
    private readonly IAssemblySymbol? _assemblySymbol;

    public string ClrNamespace { get; }

    public string? AssemblyName { get; }

    public IEnumerable<ExportedMemberInfo> ExportedMemberInfos
    {
        get
        {
            if (_assemblySymbol == null)
            {
                return [];
            }
            return field ??= GetExportedMembers(_assemblySymbol);
        }
    }


    private static IEnumerable<ExportedMemberInfo> GetExportedMembers(IAssemblySymbol assembly)
    {
        foreach (var type in GetAllTypes(assembly.GlobalNamespace))
        {
            if (!IsExported(type))
                continue;

            var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            foreach (var member in type.GetMembers())
            {
                if (!IsExported(member))
                    continue;

                switch (member)
                {
                    case IPropertySymbol prop:
                        yield return new ExportedMemberInfo(
                            ContainingType: typeName,
                            MemberName: prop.Name,
                            MemberKind: MemberKinds.Property,
                            TypeName: prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        break;

                    case IFieldSymbol field:
                        yield return new ExportedMemberInfo(
                            ContainingType: typeName,
                            MemberName: field.Name,
                            MemberKind: field.IsConst ? MemberKinds.ConstField : MemberKinds.Field,
                            TypeName: field.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        break;

                    case IEventSymbol evt:
                        yield return new ExportedMemberInfo(
                            ContainingType: typeName,
                            MemberName: evt.Name,
                            MemberKind: MemberKinds.Event,
                            TypeName: evt.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                        break;
                }
            }
        }

        static bool IsExported(ISymbol symbol)
        {
            return symbol.DeclaredAccessibility == Accessibility.Public || symbol.DeclaredAccessibility == Accessibility.Internal;
        }
        static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceSymbol ns)
        {
            foreach (var t in ns.GetTypeMembers())
            {
                foreach (var nested in GetAllNestedTypes(t))
                    yield return nested;
            }

            foreach (var childNs in ns.GetNamespaceMembers())
            {
                foreach (var t in GetAllTypes(childNs))
                    yield return t;
            }
        }
        static IEnumerable<INamedTypeSymbol> GetAllNestedTypes(INamedTypeSymbol type)
        {
            yield return type;

            foreach (var nested in type.GetTypeMembers())
            {
                foreach (var child in GetAllNestedTypes(nested))
                    yield return child;
            }
        }
    }
}

[DebuggerDisplay("{MemberKind},ContainingType={ContainingType},MemberName={MemberName}")]
public sealed class ExportedMemberInfo
{
    internal ExportedMemberInfo(string ContainingType, string MemberName, MemberKinds MemberKind, string TypeName)
    {
        this.ContainingType = ContainingType;
        this.MemberName = MemberName;
        this.MemberKind = MemberKind;
        this.TypeName = TypeName;
    }

    public string ContainingType { get; }
    public string MemberName { get; }
    public MemberKinds MemberKind { get; }
    public string TypeName { get; }
}

public enum MemberKinds
{
    Property,
    ConstField,
    Field,
    Event,
}
