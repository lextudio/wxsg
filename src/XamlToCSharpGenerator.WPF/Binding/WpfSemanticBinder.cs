using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

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
        var xmlnsMap = XmlnsDefinitionCache.GetOrBuildXmlnsDefinitionMap(compilation);
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

        var nodeType = TypeResolver.ResolveTypeSymbol(node.XmlNamespace, node.XmlTypeName, node.TypeArguments, context);
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

        var contentPropertyName = TypeMemberFinder.FindContentPropertyName(nodeType);
        var contentPropertyType = TypeMemberFinder.FindProperty(nodeType, contentPropertyName ?? string.Empty)?.Type;
        if (string.IsNullOrWhiteSpace(contentPropertyName) &&
            TypeMemberFinder.IsDictionaryLikeType(nodeType))
        {
            contentPropertyName = "__self";
            contentPropertyType = nodeType;
        }

        var childAttachmentMode = TypeMemberFinder.ResolveChildAttachmentMode(children.Count, contentPropertyName, contentPropertyType);

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
        var elementType = TypeResolver.ResolveTypeToken(node.ArrayItemType ?? string.Empty, context);
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
                context.AddUnknownPropertyDiagnostic(assignmentName, ToDisplayName(objectType), assignment.Line, assignment.Column);
                return;
            }

            var ownerType = TypeResolver.ResolveOwnerQualifiedTypeSymbol(
                ownerToken,
                assignment.XmlNamespace,
                ownerObjectXmlNamespace,
                context);
            if (ownerType is null)
            {
                context.AddUnknownTypeDiagnostic(ownerToken, assignment.Line, assignment.Column);
                return;
            }

            var ownerQualifiedInstanceProperty = TypeMemberFinder.FindProperty(objectType, attachedPropertyName);
            if (ownerQualifiedInstanceProperty is not null &&
                TypeMemberFinder.IsSameOrDerivedFrom(objectType, ownerType))
            {
                var valueConversion = MarkupExtensionResolver.ConvertAssignmentValue(assignment.Value, context);
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

            var routedEventField = TypeMemberFinder.FindRoutedEventField(ownerType, attachedPropertyName);
            if (routedEventField is not null)
            {
                if (!XamlEventHandlerNameSemantics.TryParseHandlerName(assignment.Value, out var attachedHandlerName))
                {
                    context.AddInvalidEventHandlerDiagnostic(attachedPropertyName, assignment.Line, assignment.Column);
                    return;
                }

                var routedEventHandlerTypeName = TypeMemberFinder.FindEvent(ownerType, attachedPropertyName)?.Type
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

            var attachedPropertyType = TypeResolver.ResolveAttachedPropertyType(ownerType, attachedPropertyName, context);
            if (attachedPropertyType is null)
            {
                context.AddUnknownPropertyDiagnostic(assignmentName, ToDisplayName(ownerType), assignment.Line, assignment.Column);
                return;
            }

            var attachedValueConversion = MarkupExtensionResolver.ConvertAssignmentValue(assignment.Value, context);
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

        var eventSymbol = TypeMemberFinder.FindEvent(objectType, assignment.PropertyName);
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

        var property = TypeMemberFinder.FindProperty(objectType, assignmentName);
        if (property is null)
        {
            context.AddUnknownPropertyDiagnostic(assignmentName, ToDisplayName(objectType), assignment.Line, assignment.Column);
            return;
        }

        var directValueConversion = MarkupExtensionResolver.ConvertAssignmentValue(assignment.Value, context);
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
            var ownerType = TypeResolver.ResolveOwnerQualifiedTypeSymbol(
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
                    (objectType is not null && TypeMemberFinder.IsSameOrDerivedFrom(objectType, ownerType))
                        ? TypeMemberFinder.FindProperty(objectType, attachedPropertyName)
                        : TypeMemberFinder.FindProperty(ownerType, attachedPropertyName);
                if (ownerProperty is not null)
                {
                    ownerTypeName = ownerProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    propertyTypeName = ownerProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
                else
                {
                    ownerTypeName = ToDisplayName(ownerType);
                    propertyTypeName = TypeResolver.ResolveAttachedPropertyType(ownerType, attachedPropertyName, context)?
                        .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (propertyTypeName is null)
                    {
                        context.AddUnknownPropertyDiagnostic(
                            propertyElement.PropertyName,
                            ToDisplayName(ownerType),
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
            var property = TypeMemberFinder.FindProperty(objectType, propertyElement.PropertyName);
            if (property is null)
            {
                context.AddUnknownPropertyDiagnostic(
                    propertyElement.PropertyName,
                    ToDisplayName(objectType),
                    propertyElement.Line,
                    propertyElement.Column);
            }
            else
            {
                propertyName = property.Name;
                ownerTypeName = property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                propertyTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                isCollectionAdd = TypeMemberFinder.IsCollectionLikeType(property.Type) || TypeMemberFinder.IsDictionaryLikeType(property.Type);
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

            var type = TypeResolver.ResolveTypeSymbol(element.XmlNamespace, element.XmlTypeName, ImmutableArray<string>.Empty, context);
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

        return MarkupExtensionResolver.AsStringLiteral(key.Trim());
    }

    private static string ToDisplayName(INamedTypeSymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
}
