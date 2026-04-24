using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.WPF.Binding;

internal static class TypeMemberFinder
{
    private const string ContentPropertyAttributeMetadataName =
        "System.Windows.Markup.ContentPropertyAttribute";

    internal static IPropertySymbol? FindProperty(INamedTypeSymbol? type, string propertyName)
    {
        if (type is null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var property = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    internal static IEventSymbol? FindEvent(INamedTypeSymbol type, string eventName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    internal static IFieldSymbol? FindRoutedEventField(INamedTypeSymbol ownerType, string eventName)
    {
        foreach (var lookupType in EnumerateInstanceMemberLookupTypes(ownerType))
        {
            var field = lookupType.GetMembers(eventName + "Event")
                .OfType<IFieldSymbol>()
                .FirstOrDefault(candidate => candidate.IsStatic);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    internal static string? FindContentPropertyName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != ContentPropertyAttributeMetadataName ||
                    attribute.ConstructorArguments.Length == 0)
                {
                    continue;
                }

                if (attribute.ConstructorArguments[0].Value is string contentPropertyName &&
                    !string.IsNullOrWhiteSpace(contentPropertyName))
                {
                    return contentPropertyName;
                }
            }
        }

        return null;
    }

    internal static IEnumerable<INamedTypeSymbol> EnumerateInstanceMemberLookupTypes(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            var pending = new Stack<INamedTypeSymbol>();
            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            pending.Push(type);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                yield return current;

                for (var index = current.Interfaces.Length - 1; index >= 0; index--)
                {
                    pending.Push(current.Interfaces[index]);
                }
            }

            yield break;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    internal static bool IsSameOrDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool IsCollectionLikeType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type is INamedTypeSymbol namedType)
        {
            if (ImplementsInterface(namedType, "System.Collections.IEnumerable") ||
                ImplementsInterface(namedType, "System.Collections.Generic.IEnumerable`1") ||
                ImplementsInterface(namedType, "System.Collections.IList") ||
                ImplementsInterface(namedType, "System.Collections.Generic.ICollection`1"))
            {
                return true;
            }

            foreach (var member in namedType.GetMembers("Add").OfType<IMethodSymbol>())
            {
                if (!member.IsStatic && member.Parameters.Length >= 1)
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool IsDictionaryLikeType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return ImplementsInterface(namedType, "System.Collections.IDictionary") ||
               ImplementsInterface(namedType, "System.Collections.Generic.IDictionary`2");
    }

    internal static bool ImplementsInterface(INamedTypeSymbol type, string interfaceMetadataName)
    {
        foreach (var candidate in type.AllInterfaces)
        {
            var candidateMetadata = candidate.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (candidateMetadata == interfaceMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    internal static ResolvedChildAttachmentMode ResolveChildAttachmentMode(
        int childCount,
        string? contentPropertyName,
        ITypeSymbol? contentPropertyType)
    {
        if (childCount == 0 || string.IsNullOrWhiteSpace(contentPropertyName))
        {
            return ResolvedChildAttachmentMode.None;
        }

        if (contentPropertyType is not null)
        {
            if (IsDictionaryLikeType(contentPropertyType))
            {
                return ResolvedChildAttachmentMode.DictionaryAdd;
            }

            if (IsCollectionLikeType(contentPropertyType))
            {
                if (contentPropertyName.Equals("Children", StringComparison.Ordinal))
                {
                    return ResolvedChildAttachmentMode.ChildrenCollection;
                }

                if (contentPropertyName.Equals("Items", StringComparison.Ordinal))
                {
                    return ResolvedChildAttachmentMode.ItemsCollection;
                }

                return ResolvedChildAttachmentMode.DirectAdd;
            }
        }

        return ResolvedChildAttachmentMode.Content;
    }
}
