using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.WPF.Binding;

internal static class TypeResolver
{
    internal static INamedTypeSymbol? ResolveTypeSymbol(
        string xmlNamespace,
        string xmlTypeName,
        ImmutableArray<string> typeArguments,
        BindingContext context)
    {
        var genericArity = typeArguments.IsDefaultOrEmpty ? (int?)null : typeArguments.Length;
        var metadataTypeName = AppendGenericArity(xmlTypeName, genericArity);

        INamedTypeSymbol? symbol = null;

        if (XamlXmlNamespaceSemantics.TryExtractClrNamespaceReference(
                xmlNamespace,
                out var clrNamespace,
                out var assemblySimpleName))
        {
            symbol = ResolveClrNamespaceType(clrNamespace, metadataTypeName, assemblySimpleName, context.Compilation);
        }
        else if (context.XmlnsMap.TryGetNamespaces(xmlNamespace, out var clrNamespaces))
        {
            foreach (var mapping in clrNamespaces)
            {
                symbol = ResolveClrNamespaceType(
                    mapping.ClrNamespace,
                    metadataTypeName,
                    mapping.AssemblyName,
                    context.Compilation);
                if (symbol is not null)
                {
                    break;
                }
            }
        }

        if (symbol is null)
        {
            return null;
        }

        if (typeArguments.IsDefaultOrEmpty)
        {
            return symbol;
        }

        var resolvedTypeArguments = new List<ITypeSymbol>(typeArguments.Length);
        foreach (var typeArgument in typeArguments)
        {
            var resolvedTypeArgument = ResolveTypeToken(typeArgument, context);
            if (resolvedTypeArgument is null)
            {
                return symbol;
            }

            resolvedTypeArguments.Add(resolvedTypeArgument);
        }

        if (symbol.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return symbol.Construct(resolvedTypeArguments.ToArray());
        }

        if (symbol.OriginalDefinition.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return symbol.OriginalDefinition.Construct(resolvedTypeArguments.ToArray());
        }

        return symbol;
    }

    internal static ITypeSymbol? ResolveTypeToken(string typeToken, BindingContext context)
    {
        var trimmedToken = XamlTypeTokenSemantics.TrimGlobalQualifier(typeToken.Trim());
        if (trimmedToken.Length == 0)
        {
            return null;
        }

        // Prefix-qualified XML type token (for example "local:MyType").
        if (XamlTokenSplitSemantics.TrySplitAtFirstSeparator(trimmedToken, ':', out var prefix, out var xmlTypeName) &&
            context.Document.XmlNamespaces.TryGetValue(prefix, out var prefixXmlNamespace))
        {
            return ResolveTypeSymbol(prefixXmlNamespace, xmlTypeName, ImmutableArray<string>.Empty, context);
        }

        // CLR metadata token (for example "System.String").
        var metadataSymbol = context.Compilation.GetTypeByMetadataName(trimmedToken);
        if (metadataSymbol is not null)
        {
            return metadataSymbol;
        }

        // Default XML namespace fallback.
        if (context.Document.XmlNamespaces.TryGetValue(string.Empty, out var defaultXmlNamespace))
        {
            var defaultResolved = ResolveTypeSymbol(defaultXmlNamespace, trimmedToken, ImmutableArray<string>.Empty, context);
            if (defaultResolved is not null)
            {
                return defaultResolved;
            }
        }

        return null;
    }

    internal static INamedTypeSymbol? ResolveClrNamespaceType(
        string clrNamespace,
        string metadataTypeName,
        string? assemblySimpleName,
        Compilation compilation)
    {
        var metadataName = clrNamespace + "." + metadataTypeName;

        if (!string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            foreach (var assembly in XmlnsDefinitionCache.EnumerateAssemblies(compilation))
            {
                if (!string.Equals(assembly.Identity.Name, assemblySimpleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var assemblyType = assembly.GetTypeByMetadataName(metadataName);
                if (assemblyType is not null)
                {
                    return assemblyType;
                }
            }

            return null;
        }

        return compilation.GetTypeByMetadataName(metadataName);
    }

    internal static INamedTypeSymbol? ResolveOwnerQualifiedTypeSymbol(
        string ownerToken,
        string? explicitXmlNamespace,
        string? ownerObjectXmlNamespace,
        BindingContext context)
    {
        var token = ownerToken;
        if (XamlTokenSplitSemantics.TrySplitAtFirstSeparator(ownerToken, ':', out var prefix, out var xmlTypeName) &&
            context.Document.XmlNamespaces.TryGetValue(prefix, out var prefixXmlNamespace))
        {
            token = xmlTypeName;
            return ResolveTypeSymbol(prefixXmlNamespace, token, ImmutableArray<string>.Empty, context);
        }

        if (!string.IsNullOrWhiteSpace(explicitXmlNamespace))
        {
            var resolved = ResolveTypeSymbol(explicitXmlNamespace!, token, ImmutableArray<string>.Empty, context);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (context.Document.XmlNamespaces.TryGetValue(string.Empty, out var defaultXmlNamespace))
        {
            var resolved = ResolveTypeSymbol(defaultXmlNamespace, token, ImmutableArray<string>.Empty, context);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (!string.IsNullOrWhiteSpace(ownerObjectXmlNamespace))
        {
            return ResolveTypeSymbol(ownerObjectXmlNamespace!, token, ImmutableArray<string>.Empty, context);
        }

        return null;
    }

    internal static ITypeSymbol? ResolveAttachedPropertyType(INamedTypeSymbol ownerType, string propertyName, BindingContext context)
    {
        foreach (var lookupType in TypeMemberFinder.EnumerateInstanceMemberLookupTypes(ownerType))
        {
            foreach (var method in lookupType.GetMembers("Set" + propertyName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic && method.Parameters.Length >= 2)
                {
                    return method.Parameters[1].Type;
                }
            }

            foreach (var method in lookupType.GetMembers("Get" + propertyName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic)
                {
                    return method.ReturnType;
                }
            }

            var staticProperty = lookupType.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property => property.IsStatic);
            if (staticProperty is not null)
            {
                return staticProperty.Type;
            }

            var propertyField = lookupType.GetMembers(propertyName + "Property")
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field => field.IsStatic);
            if (propertyField is not null)
            {
                if (propertyField.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Windows.DependencyProperty")
                {
                    return propertyField.Type;
                }

                var reflectedType = ResolveAttachedPropertyTypeFromRuntime(ownerType, propertyName, context);
                if (reflectedType is not null)
                {
                    return reflectedType;
                }
            }
        }

        return null;
    }

    internal static ITypeSymbol? ResolveAttachedPropertyTypeFromRuntime(
        INamedTypeSymbol ownerType,
        string propertyName,
        BindingContext context)
    {
        var runtimeType = ResolveRuntimeType(ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        if (runtimeType is null)
        {
            return null;
        }

        var flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (var current in EnumerateRuntimeMemberLookupTypes(runtimeType))
        {
            var setter = current.GetMethod("Set" + propertyName, flags);
            var setterParameters = setter?.GetParameters();
            if (setterParameters is not null && setterParameters.Length >= 2)
            {
                return ResolveRuntimeTypeSymbol(setterParameters[1].ParameterType, context);
            }

            var getter = current.GetMethod("Get" + propertyName, flags);
            if (getter is not null)
            {
                return ResolveRuntimeTypeSymbol(getter.ReturnType, context);
            }

            var staticProperty = current.GetProperty(propertyName, flags);
            if (staticProperty is not null)
            {
                return ResolveRuntimeTypeSymbol(staticProperty.PropertyType, context);
            }
        }

        return null;
    }

    internal static Type? ResolveRuntimeType(string metadataName)
    {
        if (string.IsNullOrWhiteSpace(metadataName))
        {
            return null;
        }

        var normalizedName = metadataName.Replace("global::", string.Empty);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            var direct = assembly.GetType(normalizedName, throwOnError: false);
            if (direct is not null)
            {
                return direct;
            }
        }

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException rtl)
            {
                types = rtl.Types;
            }

            foreach (var candidate in types)
            {
                if (candidate is null)
                {
                    continue;
                }

                if (string.Equals(candidate.FullName, normalizedName, StringComparison.Ordinal) ||
                    string.Equals(candidate.Name, normalizedName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    internal static ITypeSymbol? ResolveRuntimeTypeSymbol(Type runtimeType, BindingContext context)
    {
        if (runtimeType == typeof(bool))
        {
            return context.Compilation.GetSpecialType(SpecialType.System_Boolean);
        }

        if (runtimeType == typeof(int))
        {
            return context.Compilation.GetSpecialType(SpecialType.System_Int32);
        }

        if (runtimeType == typeof(double))
        {
            return context.Compilation.GetSpecialType(SpecialType.System_Double);
        }

        if (runtimeType == typeof(string))
        {
            return context.Compilation.GetSpecialType(SpecialType.System_String);
        }

        var metadataName = runtimeType.FullName?.Replace('+', '.');
        if (string.IsNullOrWhiteSpace(metadataName))
        {
            return null;
        }

        return context.Compilation.GetTypeByMetadataName(metadataName);
    }

    internal static IEnumerable<Type> EnumerateRuntimeMemberLookupTypes(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    internal static string AppendGenericArity(string xmlTypeName, int? genericArity)
    {
        if (genericArity is null || genericArity.Value <= 0)
        {
            return xmlTypeName;
        }

        return xmlTypeName + "`" + genericArity.Value.ToString(CultureInfo.InvariantCulture);
    }
}
