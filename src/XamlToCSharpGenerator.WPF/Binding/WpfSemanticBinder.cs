using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.WPF.Binding;

/// <summary>
/// Phase 2 semantic binder for WPF XAML.
///
/// Resolves the full object graph for WXSG by mapping XML namespaces to CLR symbols via
/// <c>System.Windows.Markup.XmlnsDefinitionAttribute</c>, then binding:
/// <list type="bullet">
///   <item>Object node types (root + descendants)</item>
///   <item>Property assignments (including attached-property syntax)</item>
///   <item>Property elements</item>
///   <item>Event subscriptions</item>
/// </list>
///
/// Diagnostics are produced for unknown element types and unresolved properties.
/// </summary>
public sealed class WpfSemanticBinder : IXamlSemanticBinder
{
    private const string WpfPresentationXmlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private const string WpfXmlnsDefinitionAttributeMetadataName =
        "System.Windows.Markup.XmlnsDefinitionAttribute";

    private const string ContentPropertyAttributeMetadataName =
        "System.Windows.Markup.ContentPropertyAttribute";

    private const string WxsgUnknownTypeDiagnosticId = "WXSG0101";
    private const string WxsgUnknownPropertyDiagnosticId = "WXSG0102";
    private const string WxsgInvalidEventHandlerDiagnosticId = "WXSG0103";
    private static readonly MarkupExpressionParser MarkupParser = new();
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

    public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
        XamlDocumentModel document,
        Compilation compilation,
        GeneratorOptions options,
        XamlTransformConfiguration transformConfiguration)
    {
        if (!document.IsValid || document.ClassFullName is null)
        {
            return (null, ImmutableArray<DiagnosticInfo>.Empty);
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var xmlnsMap = GetOrBuildXmlnsDefinitionMap(compilation);
        var context = new BindingContext(
            document,
            compilation,
            xmlnsMap,
            diagnostics,
            options.StrictMode,
            options.CSharpExpressionsEnabled,
            options.ImplicitCSharpExpressionsEnabled);

        var rootNode = BindObjectNode(document.RootObject, context);
        var namedElements = ResolveNamedElements(document.NamedElements, context);

        // WPF relative pack URI: /AssemblyName;component/SubFolder/File.xaml
        var buildUri = BuildPackUri(document, compilation);

        var viewModel = new ResolvedViewModel(
            Document: document,
            BuildUri: buildUri,
            ClassModifier: document.ClassModifier ?? "public",
            CreateSourceInfo: false,
            EnableHotReload: false,
            EnableHotDesign: false,
            PassExecutionTrace: ImmutableArray<string>.Empty,
            EmitNameScopeRegistration: false,
            EmitStaticResourceResolver: false,
            HasXBind: false,
            RootObject: rootNode,
            NamedElements: namedElements,
            Resources: ImmutableArray<ResolvedResourceDefinition>.Empty,
            Templates: ImmutableArray<ResolvedTemplateDefinition>.Empty,
            CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
            UnsafeAccessors: ImmutableArray<ResolvedUnsafeAccessorDefinition>.Empty,
            Styles: ImmutableArray<ResolvedStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<ResolvedControlThemeDefinition>.Empty,
            Includes: ImmutableArray<ResolvedIncludeDefinition>.Empty,
            HotDesignArtifactKind: ResolvedHotDesignArtifactKind.View,
            HotDesignScopeHints: ImmutableArray<string>.Empty);

        return (viewModel, diagnostics.ToImmutable());
    }

    private static ResolvedObjectNode BindObjectNode(XamlObjectNode node, BindingContext context)
    {
        if (string.Equals(node.XmlTypeName, "Array", StringComparison.Ordinal))
        {
            return BindXamlArrayNode(node, context);
        }

        var nodeType = ResolveTypeSymbol(node.XmlNamespace, node.XmlTypeName, node.TypeArguments, context);
        if (nodeType is null)
        {
            context.AddUnknownTypeDiagnostic(node.XmlTypeName, node.Line, node.Column);
        }

        var typeName = nodeType is not null ? ToDisplayName(nodeType) : "object";
        var assignments = ImmutableArray.CreateBuilder<ResolvedPropertyAssignment>();
        var propertyElementAssignments = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>();
        var eventSubscriptions = ImmutableArray.CreateBuilder<ResolvedEventSubscription>();

        foreach (var assignment in node.PropertyAssignments)
        {
            BindPropertyAssignment(
                assignment,
                node.XmlNamespace,
                nodeType,
                context,
                assignments,
                eventSubscriptions);
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            propertyElementAssignments.Add(BindPropertyElement(propertyElement, node.XmlNamespace, nodeType, context));
        }

        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>();
        foreach (var constructorArgument in node.ConstructorArguments)
        {
            children.Add(BindObjectNode(constructorArgument, context));
        }

        foreach (var child in node.ChildObjects)
        {
            children.Add(BindObjectNode(child, context));
        }

        var contentPropertyName = FindContentPropertyName(nodeType);
        var contentPropertyType = FindProperty(nodeType, contentPropertyName ?? string.Empty)?.Type;
        if (string.IsNullOrWhiteSpace(contentPropertyName) &&
            IsDictionaryLikeType(nodeType))
        {
            contentPropertyName = "__self";
            contentPropertyType = nodeType;
        }

        var childAttachmentMode = ResolveChildAttachmentMode(children.Count, contentPropertyName, contentPropertyType);

        return new ResolvedObjectNode(
            KeyExpression: BuildObjectNodeKeyExpression(node.Key),
            Name: node.Name,
            TypeName: typeName,
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: assignments.ToImmutable(),
            PropertyElementAssignments: propertyElementAssignments.ToImmutable(),
            EventSubscriptions: eventSubscriptions.ToImmutable(),
            Children: children.ToImmutable(),
            ChildAttachmentMode: childAttachmentMode,
            ContentPropertyName: contentPropertyName,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition,
            ContentPropertyTypeName: contentPropertyType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static ResolvedObjectNode BindXamlArrayNode(XamlObjectNode node, BindingContext context)
    {
        var elementType = ResolveTypeToken(node.ArrayItemType ?? string.Empty, context);
        if (elementType is null)
        {
            context.AddUnknownTypeDiagnostic(node.ArrayItemType ?? "Array", node.Line, node.Column);
        }

        var elementTypeName = elementType is not null
            ? elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : "object";

        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>(node.ChildObjects.Length + node.ConstructorArguments.Length);
        foreach (var constructorArgument in node.ConstructorArguments)
        {
            children.Add(BindObjectNode(constructorArgument, context));
        }

        foreach (var child in node.ChildObjects)
        {
            children.Add(BindObjectNode(child, context));
        }

        return new ResolvedObjectNode(
            KeyExpression: BuildObjectNodeKeyExpression(node.Key),
            Name: node.Name,
            TypeName: "global::System.Object",
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: children.ToImmutable(),
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition,
            ContentPropertyTypeName: elementTypeName,
            SemanticFlags: ResolvedObjectNodeSemanticFlags.IsXamlArray);
    }

    private static void BindPropertyAssignment(
        XamlPropertyAssignment assignment,
        string ownerObjectXmlNamespace,
        INamedTypeSymbol? objectType,
        BindingContext context,
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        ImmutableArray<ResolvedEventSubscription>.Builder eventSubscriptions)
    {
        if (objectType is null)
        {
            return;
        }

        var assignmentName = assignment.PropertyName;
        if (assignment.IsAttached)
        {
            if (!XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                    assignmentName,
                    out var ownerToken,
                    out var attachedPropertyName))
            {
                context.AddUnknownPropertyDiagnostic(assignmentName, objectType, assignment.Line, assignment.Column);
                return;
            }

            var ownerType = ResolveOwnerQualifiedTypeSymbol(
                ownerToken,
                assignment.XmlNamespace,
                ownerObjectXmlNamespace,
                context);
            if (ownerType is null)
            {
                context.AddUnknownTypeDiagnostic(ownerToken, assignment.Line, assignment.Column);
                return;
            }

            var ownerQualifiedInstanceProperty = FindProperty(objectType, attachedPropertyName);
            if (ownerQualifiedInstanceProperty is not null &&
                IsSameOrDerivedFrom(objectType, ownerType))
            {
                var valueConversion = ConvertAssignmentValue(assignment.Value, context);
                assignments.Add(new ResolvedPropertyAssignment(
                    PropertyName: ownerQualifiedInstanceProperty.Name,
                    ValueExpression: valueConversion.ValueExpression,
                    ClrPropertyOwnerTypeName: ownerQualifiedInstanceProperty.ContainingType
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: ownerQualifiedInstanceProperty.Type
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: valueConversion.ValueKind));
                return;
            }

            var routedEventField = FindRoutedEventField(ownerType, attachedPropertyName);
            if (routedEventField is not null)
            {
                if (!XamlEventHandlerNameSemantics.TryParseHandlerName(assignment.Value, out var attachedHandlerName))
                {
                    context.AddInvalidEventHandlerDiagnostic(attachedPropertyName, assignment.Line, assignment.Column);
                    return;
                }

                var routedEventHandlerTypeName = FindEvent(ownerType, attachedPropertyName)?.Type
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                eventSubscriptions.Add(new ResolvedEventSubscription(
                    EventName: attachedPropertyName,
                    HandlerMethodName: attachedHandlerName,
                    Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                    RoutedEventOwnerTypeName: ToDisplayName(ownerType),
                    RoutedEventFieldName: routedEventField.Name,
                    RoutedEventHandlerTypeName: routedEventHandlerTypeName,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition));

                return;
            }

            var attachedPropertyType = ResolveAttachedPropertyType(ownerType, attachedPropertyName, context);
            if (attachedPropertyType is null)
            {
                context.AddUnknownPropertyDiagnostic(assignmentName, ownerType, assignment.Line, assignment.Column);
                return;
            }

            var attachedValueConversion = ConvertAssignmentValue(assignment.Value, context);
            assignments.Add(new ResolvedPropertyAssignment(
                PropertyName: attachedPropertyName,
                ValueExpression: attachedValueConversion.ValueExpression,
                ClrPropertyOwnerTypeName: ToDisplayName(ownerType),
                ClrPropertyTypeName: attachedPropertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: attachedValueConversion.ValueKind,
                FrameworkPayload: new ResolvedFrameworkPropertyPayload(
                    FrameworkId: "WPF",
                    PropertyOwnerTypeName: ToDisplayName(ownerType),
                    PropertyFieldName: null)));

            return;
        }

        var eventSymbol = FindEvent(objectType, assignment.PropertyName);
        if (eventSymbol is not null)
        {
            if (!XamlEventHandlerNameSemantics.TryParseHandlerName(assignment.Value, out var handlerName))
            {
                context.AddInvalidEventHandlerDiagnostic(eventSymbol.Name, assignment.Line, assignment.Column);
                return;
            }

            eventSubscriptions.Add(new ResolvedEventSubscription(
                EventName: eventSymbol.Name,
                HandlerMethodName: handlerName,
                Kind: ResolvedEventSubscriptionKind.ClrEvent,
                RoutedEventOwnerTypeName: null,
                RoutedEventFieldName: null,
                RoutedEventHandlerTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition));
            return;
        }

        var property = FindProperty(objectType, assignmentName);
        if (property is null)
        {
            context.AddUnknownPropertyDiagnostic(assignmentName, objectType, assignment.Line, assignment.Column);
            return;
        }

        var directValueConversion = ConvertAssignmentValue(assignment.Value, context);
        assignments.Add(new ResolvedPropertyAssignment(
            PropertyName: property.Name,
            ValueExpression: directValueConversion.ValueExpression,
            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: directValueConversion.ValueKind));
    }

    private static ResolvedPropertyElementAssignment BindPropertyElement(
        XamlPropertyElement propertyElement,
        string ownerObjectXmlNamespace,
        INamedTypeSymbol? objectType,
        BindingContext context)
    {
        var objectValues = ImmutableArray.CreateBuilder<ResolvedObjectNode>();
        foreach (var objectValue in propertyElement.ObjectValues)
        {
            objectValues.Add(BindObjectNode(objectValue, context));
        }

        string propertyName = propertyElement.PropertyName;
        string? ownerTypeName = null;
        string? propertyTypeName = null;
        var isCollectionAdd = false;
        ResolvedFrameworkPropertyPayload? frameworkPayload = null;

        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                propertyElement.PropertyName,
                out var ownerToken,
                out var attachedPropertyName))
        {
            propertyName = attachedPropertyName;
            var ownerType = ResolveOwnerQualifiedTypeSymbol(
                ownerToken,
                propertyElement.XmlNamespace,
                ownerObjectXmlNamespace,
                context);
            if (ownerType is null)
            {
                context.AddUnknownTypeDiagnostic(ownerToken, propertyElement.Line, propertyElement.Column);
            }
            else
            {
                var ownerProperty =
                    (objectType is not null && IsSameOrDerivedFrom(objectType, ownerType))
                        ? FindProperty(objectType, attachedPropertyName)
                        : FindProperty(ownerType, attachedPropertyName);
                if (ownerProperty is not null)
                {
                    ownerTypeName = ownerProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    propertyTypeName = ownerProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                else
                {
                    ownerTypeName = ToDisplayName(ownerType);
                    propertyTypeName = ResolveAttachedPropertyType(ownerType, attachedPropertyName, context)?
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (propertyTypeName is null)
                    {
                        context.AddUnknownPropertyDiagnostic(
                            propertyElement.PropertyName,
                            ownerType,
                            propertyElement.Line,
                            propertyElement.Column);
                    }
                    else
                    {
                        frameworkPayload = new ResolvedFrameworkPropertyPayload(
                            FrameworkId: "WPF",
                            PropertyOwnerTypeName: ownerTypeName,
                            PropertyFieldName: null);
                    }
                }
            }
        }
        else if (objectType is not null)
        {
            var property = FindProperty(objectType, propertyElement.PropertyName);
            if (property is null)
            {
                context.AddUnknownPropertyDiagnostic(
                    propertyElement.PropertyName,
                    objectType,
                    propertyElement.Line,
                    propertyElement.Column);
            }
            else
            {
                propertyName = property.Name;
                ownerTypeName = property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                propertyTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                isCollectionAdd = IsCollectionLikeType(property.Type) || IsDictionaryLikeType(property.Type);
            }
        }

        return new ResolvedPropertyElementAssignment(
            PropertyName: propertyName,
            ClrPropertyOwnerTypeName: ownerTypeName,
            ClrPropertyTypeName: propertyTypeName,
            IsCollectionAdd: isCollectionAdd,
            IsDictionaryMerge: false,
            ObjectValues: objectValues.ToImmutable(),
            Line: propertyElement.Line,
            Column: propertyElement.Column,
            Condition: propertyElement.Condition,
            FrameworkPayload: frameworkPayload);
    }

    private static ImmutableArray<ResolvedNamedElement> ResolveNamedElements(
        ImmutableArray<XamlNamedElement> namedElements,
        BindingContext context)
    {
        if (namedElements.IsEmpty)
        {
            return ImmutableArray<ResolvedNamedElement>.Empty;
        }

        INamedTypeSymbol? classSymbol = null;
        if (!string.IsNullOrWhiteSpace(context.Document.ClassFullName))
        {
            classSymbol = context.Compilation.GetTypeByMetadataName(context.Document.ClassFullName);
        }

        var builder = ImmutableArray.CreateBuilder<ResolvedNamedElement>(namedElements.Length);
        foreach (var element in namedElements)
        {
            if (classSymbol is not null && classSymbol.GetMembers(element.Name).Length > 0)
            {
                // Keep user-authored partial members authoritative and avoid duplicate declarations.
                continue;
            }

            var type = ResolveTypeSymbol(element.XmlNamespace, element.XmlTypeName, ImmutableArray<string>.Empty, context);
            if (type is null)
            {
                context.AddUnknownTypeDiagnostic(element.XmlTypeName, element.Line, element.Column);
            }

            var typeName = type is not null ? ToDisplayName(type) : element.XmlTypeName;
            builder.Add(new ResolvedNamedElement(
                Name: element.Name,
                TypeName: typeName,
                FieldModifier: element.FieldModifier ?? "internal",
                Line: element.Line,
                Column: element.Column));
        }

        return builder.ToImmutable();
    }

    private static INamedTypeSymbol? ResolveTypeSymbol(
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

    private static ITypeSymbol? ResolveTypeToken(string typeToken, BindingContext context)
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

    private static INamedTypeSymbol? ResolveClrNamespaceType(
        string clrNamespace,
        string metadataTypeName,
        string? assemblySimpleName,
        Compilation compilation)
    {
        var metadataName = clrNamespace + "." + metadataTypeName;

        if (!string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            foreach (var assembly in EnumerateAssemblies(compilation))
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

    private static ITypeSymbol? ResolveAttachedPropertyType(INamedTypeSymbol ownerType, string propertyName, BindingContext context)
    {
        foreach (var lookupType in EnumerateInstanceMemberLookupTypes(ownerType))
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

    private static ITypeSymbol? ResolveAttachedPropertyTypeFromRuntime(
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

    private static IEnumerable<Type> EnumerateRuntimeMemberLookupTypes(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static Type? ResolveRuntimeType(string metadataName)
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

    private static ITypeSymbol? ResolveRuntimeTypeSymbol(Type runtimeType, BindingContext context)
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

    private static bool IsSameOrDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
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

    private static IFieldSymbol? FindRoutedEventField(INamedTypeSymbol ownerType, string eventName)
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

    private static IPropertySymbol? FindProperty(INamedTypeSymbol? type, string propertyName)
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

    private static IEventSymbol? FindEvent(INamedTypeSymbol type, string eventName)
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

    private static IEnumerable<INamedTypeSymbol> EnumerateInstanceMemberLookupTypes(INamedTypeSymbol type)
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

    private static string? FindContentPropertyName(INamedTypeSymbol? type)
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

    private static ResolvedChildAttachmentMode ResolveChildAttachmentMode(
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

    private static bool IsCollectionLikeType(ITypeSymbol type)
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

    private static bool IsDictionaryLikeType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        return ImplementsInterface(namedType, "System.Collections.IDictionary") ||
               ImplementsInterface(namedType, "System.Collections.Generic.IDictionary`2");
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string interfaceMetadataName)
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

    private static string BuildPackUri(XamlDocumentModel document, Compilation compilation)
    {
        var assemblyName = compilation.AssemblyName ?? "Application";
        var targetPath = document.TargetPath.Replace('\\', '/').TrimStart('/');
        return "/" + assemblyName + ";component/" + targetPath;
    }

    private static string? BuildObjectNodeKeyExpression(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return AsStringLiteral(key.Trim());
    }

    private static (string ValueExpression, ResolvedValueKind ValueKind) ConvertAssignmentValue(
        string rawValue,
        BindingContext context)
    {
        if (TryParseCsPrefixExpression(rawValue, out var csharpPrefixedExpression))
        {
            return (csharpPrefixedExpression, ResolvedValueKind.MarkupExtension);
        }

        if (MarkupParser.TryParseMarkupExtension(rawValue, out var markupInfo))
        {
            var markupKind = XamlMarkupExtensionNameSemantics.Classify(markupInfo.Name);
            switch (markupKind)
            {
                case XamlMarkupExtensionKind.Null:
                    return ("null", ResolvedValueKind.Literal);
                // Detect {Binding ...} — keep the raw XAML string as ValueExpression so the emitter
                // can parse it and emit a SetBinding call.
                case XamlMarkupExtensionKind.Binding:
                    return (rawValue, ResolvedValueKind.Binding);
                case XamlMarkupExtensionKind.Type:
                    if (TryConvertTypeMarkupExtension(markupInfo, context, out var typeExpression))
                    {
                        return (typeExpression, ResolvedValueKind.MarkupExtension);
                    }

                    break;
                case XamlMarkupExtensionKind.CSharp:
                    if (TryParseInlineCSharpExpression(rawValue, context, out var csharpExpression))
                    {
                        return (csharpExpression, ResolvedValueKind.MarkupExtension);
                    }

                    break;
                case XamlMarkupExtensionKind.Unknown:
                    // For unknown markup extensions that carry a namespace prefix (e.g.
                    // {core:Localize Key}), resolve the prefix to its XML namespace URI and
                    // encode all the information into the ValueExpression so the emitter can
                    // generate a runtime call to __WXSG_EvaluateUnknownMarkupExtension.
                    if (TryBuildUnknownMarkupExtensionEncoding(markupInfo, context, out var unknownMeEncoding))
                    {
                        return (unknownMeEncoding, ResolvedValueKind.Literal);
                    }

                    break;
            }

            // For non-CSharp markup extensions, keep the source text as a literal.
            // The emitter can later lower known forms (x:Type/x:Static/DynamicResource/etc.).
            return (AsStringLiteral(rawValue), ResolvedValueKind.Literal);
        }

        if (TryParseInlineCSharpExpression(rawValue, context, out var fallbackCsharpExpression))
        {
            return (fallbackCsharpExpression, ResolvedValueKind.MarkupExtension);
        }

        return (AsStringLiteral(rawValue), ResolvedValueKind.Literal);
    }

    private static bool TryConvertTypeMarkupExtension(
        MarkupExtensionInfo markupInfo,
        BindingContext context,
        out string typeExpression)
    {
        typeExpression = string.Empty;
        string? rawTypeToken = null;

        if (markupInfo.NamedArguments.TryGetValue("Type", out var namedTypeToken) ||
            markupInfo.NamedArguments.TryGetValue("TypeName", out namedTypeToken))
        {
            rawTypeToken = namedTypeToken;
        }
        else if (markupInfo.PositionalArguments.Length > 0)
        {
            rawTypeToken = markupInfo.PositionalArguments[0];
        }

        if (string.IsNullOrWhiteSpace(rawTypeToken))
        {
            return false;
        }

        var typeToken = XamlQuotedValueSemantics.TrimAndUnquote(rawTypeToken).Trim();
        if (typeToken.Length == 0)
        {
            return false;
        }

        var resolvedType = ResolveTypeToken(typeToken, context);
        if (resolvedType is null)
        {
            return false;
        }

        var displayName = resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
        typeExpression = "typeof(" + displayName + ")";
        return true;
    }

    private static bool TryParseInlineCSharpExpression(
        string value,
        BindingContext context,
        out string csharpExpression)
    {
        csharpExpression = string.Empty;

        if (TryParseCsPrefixExpression(value, out csharpExpression))
        {
            return true;
        }

        if (context.CSharpExpressionsEnabled &&
            CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                value,
                context.ImplicitCSharpExpressionsEnabled,
                static candidate =>
                {
                    if (!MarkupParser.TryParseMarkupExtension(candidate, out var info))
                    {
                        return false;
                    }

                    return XamlMarkupExtensionNameSemantics.Classify(info.Name) != XamlMarkupExtensionKind.CSharp;
                },
                out var rawParsedExpression,
                out _,
                out _))
        {
            csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(rawParsedExpression);
            if (csharpExpression.Length > 0)
            {
                return true;
            }
        }

        if (!MarkupParser.TryParseMarkupExtension(value, out var markupExtension))
        {
            return false;
        }

        if (XamlMarkupExtensionNameSemantics.Classify(markupExtension.Name) != XamlMarkupExtensionKind.CSharp)
        {
            return false;
        }

        string? rawMarkupExpression = null;
        if (markupExtension.NamedArguments.TryGetValue("Code", out var namedCode) ||
            markupExtension.NamedArguments.TryGetValue("Expression", out namedCode))
        {
            rawMarkupExpression = namedCode;
        }
        else if (markupExtension.PositionalArguments.Length > 0)
        {
            rawMarkupExpression = markupExtension.PositionalArguments[0];
        }

        if (string.IsNullOrWhiteSpace(rawMarkupExpression))
        {
            return false;
        }

        csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(
            XamlQuotedValueSemantics.TrimAndUnquote(rawMarkupExpression));
        return csharpExpression.Length > 0;
    }

    private static bool TryParseCsPrefixExpression(string value, out string csharpExpression)
    {
        csharpExpression = string.Empty;
        var trimmed = value.Trim();
        const string prefixedOpen = "{cs:";
        const string csharpOpen = "{csharp:";

        if (trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            string? expression = null;
            if (trimmed.StartsWith(prefixedOpen, StringComparison.OrdinalIgnoreCase))
            {
                expression = trimmed.Substring(prefixedOpen.Length, trimmed.Length - prefixedOpen.Length - 1);
            }
            else if (trimmed.StartsWith(csharpOpen, StringComparison.OrdinalIgnoreCase))
            {
                expression = trimmed.Substring(csharpOpen.Length, trimmed.Length - csharpOpen.Length - 1);
            }

            if (expression is not null)
            {
                csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(
                    XamlQuotedValueSemantics.TrimAndUnquote(expression.Trim()));
                if (csharpExpression.Length > 0)
                {
                    return true;
                }
            }
        }

        if (!MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(value, out var inner))
        {
            return false;
        }

        var trimmedInner = inner.TrimStart();
        const string csPrefix = "cs:";
        const string csharpPrefix = "csharp:";

        string expressionBody;
        if (trimmedInner.StartsWith(csPrefix, StringComparison.OrdinalIgnoreCase))
        {
            expressionBody = trimmedInner.Substring(csPrefix.Length);
        }
        else if (trimmedInner.StartsWith(csharpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            expressionBody = trimmedInner.Substring(csharpPrefix.Length);
        }
        else
        {
            return false;
        }

        csharpExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(
            XamlQuotedValueSemantics.TrimAndUnquote(expressionBody.Trim()));
        return csharpExpression.Length > 0;
    }

    private static string AsStringLiteral(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static INamedTypeSymbol? ResolveOwnerQualifiedTypeSymbol(
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

    private static string AppendGenericArity(string xmlTypeName, int? genericArity)
    {
        if (genericArity is null || genericArity.Value <= 0)
        {
            return xmlTypeName;
        }

        return xmlTypeName + "`" + genericArity.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string ToDisplayName(INamedTypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);

    private static XmlnsDefinitionCacheEntry GetOrBuildXmlnsDefinitionMap(Compilation compilation) =>
        XmlnsCache.GetValue(compilation, static c => BuildXmlnsDefinitionMap(c));

    private static XmlnsDefinitionCacheEntry BuildXmlnsDefinitionMap(Compilation compilation)
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

    private static bool IsXmlnsDefinitionAttribute(AttributeData attribute)
    {
        return string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            WpfXmlnsDefinitionAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
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

    private sealed class BindingContext
    {
        public BindingContext(
            XamlDocumentModel document,
            Compilation compilation,
            XmlnsDefinitionCacheEntry xmlnsMap,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics,
            bool strictMode,
            bool csharpExpressionsEnabled,
            bool implicitCSharpExpressionsEnabled)
        {
            Document = document;
            Compilation = compilation;
            XmlnsMap = xmlnsMap;
            Diagnostics = diagnostics;
            StrictMode = strictMode;
            CSharpExpressionsEnabled = csharpExpressionsEnabled;
            ImplicitCSharpExpressionsEnabled = implicitCSharpExpressionsEnabled;
        }

        public XamlDocumentModel Document { get; }

        public Compilation Compilation { get; }

        public XmlnsDefinitionCacheEntry XmlnsMap { get; }

        public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

        public bool StrictMode { get; }

        public bool CSharpExpressionsEnabled { get; }

        public bool ImplicitCSharpExpressionsEnabled { get; }

        public void AddUnknownTypeDiagnostic(string xmlTypeName, int line, int column)
        {
            Diagnostics.Add(new DiagnosticInfo(
                WxsgUnknownTypeDiagnosticId,
                "Unknown XAML type '" + xmlTypeName + "'.",
                Document.FilePath,
                line,
                column,
                StrictMode));
        }

        public void AddUnknownPropertyDiagnostic(string propertyName, INamedTypeSymbol ownerType, int line, int column)
        {
            Diagnostics.Add(new DiagnosticInfo(
                WxsgUnknownPropertyDiagnosticId,
                "Unknown property or event '" + propertyName + "' on '" + ToDisplayName(ownerType) + "'.",
                Document.FilePath,
                line,
                column,
                StrictMode));
        }

        public void AddInvalidEventHandlerDiagnostic(string eventName, int line, int column)
        {
            Diagnostics.Add(new DiagnosticInfo(
                WxsgInvalidEventHandlerDiagnosticId,
                "Event '" + eventName + "' requires a valid handler method name.",
                Document.FilePath,
                line,
                column,
                StrictMode));
        }
    }

    private sealed class XmlnsDefinitionCacheEntry
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

    private sealed class XmlnsDefinitionMapping
    {
        public XmlnsDefinitionMapping(string clrNamespace, string? assemblyName)
        {
            ClrNamespace = clrNamespace;
            AssemblyName = assemblyName;
        }

        public string ClrNamespace { get; }

        public string? AssemblyName { get; }
    }

    /// <summary>
    /// Encodes an unknown (custom) markup extension into a special literal string that the
    /// emitter can later decode and lower to a <c>__WXSG_EvaluateUnknownMarkupExtension</c>
    /// runtime call.
    ///
    /// Encoding format (fields separated by <c>'\x1f'</c> Unit Separator):
    /// <list type="bullet">
    ///   <item><c>'\x1e' + "wxsg-ume"</c> — marker (Record Separator + magic tag)</item>
    ///   <item>Resolved XML namespace URI</item>
    ///   <item>Local name of the extension (without prefix, without "Extension" suffix)</item>
    ///   <item>Zero or more positional args, each prefixed with <c>"p:"</c></item>
    ///   <item>Zero or more named args, each prefixed with <c>"n:"</c> in <c>Key=Value</c> form</item>
    /// </list>
    /// </summary>
    private static bool TryBuildUnknownMarkupExtensionEncoding(
        MarkupExtensionInfo markupInfo,
        BindingContext context,
        out string encoding)
    {
        encoding = string.Empty;

        var name = markupInfo.Name;
        var colonIndex = name.IndexOf(':');
        if (colonIndex <= 0 || colonIndex >= name.Length - 1)
        {
            return false;
        }

        var prefix = name.Substring(0, colonIndex);
        var localName = name.Substring(colonIndex + 1).Trim();
        if (localName.Length == 0)
        {
            return false;
        }

        if (!context.Document.XmlNamespaces.TryGetValue(prefix, out var nsUri) ||
            string.IsNullOrEmpty(nsUri))
        {
            return false;
        }

        var sb = new StringBuilder();
        sb.Append('\x1e');       // RS: marks the start of an unknown-ME encoding
        sb.Append("wxsg-ume");
        sb.Append('\x1f');       // US: field separator
        sb.Append(nsUri);
        sb.Append('\x1f');
        sb.Append(localName);

        foreach (var arg in markupInfo.PositionalArguments)
        {
            sb.Append('\x1f');
            sb.Append("p:");
            sb.Append(arg);
        }

        foreach (var kvp in markupInfo.NamedArguments)
        {
            sb.Append('\x1f');
            sb.Append("n:");
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(kvp.Value);
        }

        encoding = AsStringLiteral(sb.ToString());
        return true;
    }
}
