using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.WPF.Emission;

internal sealed class GraphEmitter
{
    private const string WpfFrameworkId = "WPF";
    private readonly Dictionary<string, string> _namedFieldTypes;
    private int _localCounter;

    public GraphEmitter(ResolvedViewModel viewModel, StringBuilder builder, string memberIndent)
    {
        ViewModel = viewModel;
        Builder = builder;
        MemberIndent = memberIndent;
        _namedFieldTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var namedElement in viewModel.NamedElements)
        {
            if (!_namedFieldTypes.ContainsKey(namedElement.Name))
            {
                _namedFieldTypes[namedElement.Name] = namedElement.TypeName;
            }
        }
    }

    public ResolvedViewModel ViewModel { get; }

    public StringBuilder Builder { get; }

    public string MemberIndent { get; }

    public void EmitNodeInitialization(
        ResolvedObjectNode node,
        string instanceVariable,
        bool isRootNode,
        string? ambientStyleTargetTypeExpression,
        bool suppressNamedFieldRegistration = false)
    {
        var nextAmbientStyleTargetTypeExpression = ambientStyleTargetTypeExpression;
        var nodeTypeNoGlobal = node.TypeName.Replace("global::", string.Empty).Trim();
        var nodeSimpleName = nodeTypeNoGlobal.Contains('.') ? nodeTypeNoGlobal.Substring(nodeTypeNoGlobal.LastIndexOf('.') + 1) : nodeTypeNoGlobal;
        if (string.Equals(nodeSimpleName, "Style", StringComparison.Ordinal) ||
            string.Equals(nodeTypeNoGlobal, "System.Windows.Style", StringComparison.Ordinal))
        {
            try { System.Console.WriteLine("[WXSG] EmitNodeInitialization detected style: node.TypeName='" + node.TypeName + "' simple='" + nodeSimpleName + "' instanceVar='" + instanceVariable + "'"); } catch { }
            nextAmbientStyleTargetTypeExpression = instanceVariable + ".TargetType";
        }

        var nextSuppressNamedFieldRegistration = suppressNamedFieldRegistration || CreatesNestedNameScope(node);

        EmitNamedFieldAssignment(node, instanceVariable, suppressNamedFieldRegistration);
        EmitPropertyAssignments(node, instanceVariable, nextAmbientStyleTargetTypeExpression);
        EmitPropertyElementAssignments(node, nextAmbientStyleTargetTypeExpression, instanceVariable, nextSuppressNamedFieldRegistration);
        EmitEventSubscriptions(node, instanceVariable);
        EmitChildNodes(node, instanceVariable, nextAmbientStyleTargetTypeExpression, nextSuppressNamedFieldRegistration);

        if (isRootNode && !string.IsNullOrWhiteSpace(node.Name))
        {
            EmitNamedFieldAssignment(node, instanceVariable, suppressNamedFieldRegistration);
        }
    }

    private void EmitNamedFieldAssignment(ResolvedObjectNode node, string instanceVariable, bool suppressNamedFieldRegistration)
    {
        if (suppressNamedFieldRegistration ||
            string.IsNullOrWhiteSpace(node.Name) ||
            !_namedFieldTypes.TryGetValue(node.Name, out var fieldType))
        {
            return;
        }

        Builder.AppendLine(MemberIndent + "    this." + CodeGenUtilities.EscapeIdentifier(node.Name) + " = (" + CodeGenUtilities.QualifyType(fieldType) + ")" + instanceVariable + ";");
        Builder.AppendLine(MemberIndent + "    this.RegisterName(" + CodeGenUtilities.EscapeStringLiteral(node.Name) + ", this." + CodeGenUtilities.EscapeIdentifier(node.Name) + ");");
    }

    private void EmitPropertyAssignments(
        ResolvedObjectNode node,
        string instanceVariable,
        string? ambientStyleTargetTypeExpression)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            // StartupUri is handled by an OnStartup override (see ResolveStartupWindowType /
            // EmitOnStartup).  Setting it here would cause Application.DoStartup() to try to
            // load a BAML resource that doesn't exist in Phase 3.
            if (string.Equals(assignment.PropertyName, "StartupUri", StringComparison.Ordinal))
                continue;

            // {Binding ...} — emit a SetBinding call instead of a direct property assignment.
            if (assignment.ValueKind == ResolvedValueKind.Binding)
            {
                EmitSetBinding(node.TypeName, assignment, instanceVariable);
                continue;
            }

            // {TemplateBinding ...} — emit a SetBinding call with RelativeSource.TemplatedParent.
            if (assignment.ValueKind == ResolvedValueKind.TemplateBinding)
            {
                EmitSetTemplateBinding(node.TypeName, assignment, instanceVariable);
                continue;
            }

            // {DynamicResource key} — emit SetResourceReference for the root object (always a
            // FrameworkElement/FrameworkContentElement), or a static TryFindResource lookup for
            // non-root objects like GradientStop that may not have SetResourceReference.
            if (CodeGenUtilities.TryUnquote(assignment.ValueExpression, out var dynResLiteral) &&
                dynResLiteral.StartsWith("{DynamicResource ", StringComparison.Ordinal) &&
                dynResLiteral.EndsWith("}", StringComparison.Ordinal))
            {
                const string dynResOpen = "{DynamicResource ";
                var dynResKey = dynResLiteral.Substring(dynResOpen.Length, dynResLiteral.Length - dynResOpen.Length - 1).Trim();
                string dynResKeyExpression;
                if (dynResKey.StartsWith("{x:Static ", StringComparison.Ordinal) && dynResKey.EndsWith("}", StringComparison.Ordinal))
                {
                    dynResKeyExpression = "__WXSG_ResolveXStatic(\"" + dynResKey.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")";
                }
                else
                {
                    dynResKeyExpression = "\"" + dynResKey.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                }

                if (string.Equals(instanceVariable, "this", StringComparison.Ordinal))
                {
                    // Root object is always a FrameworkElement/FrameworkContentElement — use SetResourceReference.
                    Builder.AppendLine(
                        MemberIndent + "    " +
                        "this.SetResourceReference(" +
                        assignment.PropertyName + "Property, " + dynResKeyExpression + ");");
                }
                else if (!string.IsNullOrWhiteSpace(assignment.ClrPropertyTypeName))
                {
                    // Non-root object (e.g. GradientStop, Freezable) — static TryFindResource at init time.
                    var dynResTypeName = CodeGenUtilities.QualifyType(assignment.ClrPropertyTypeName);
                    Builder.AppendLine(
                        MemberIndent + "    " +
                        "{ var __dynResVal = global::System.Windows.Application.Current?.TryFindResource(" + dynResKeyExpression + "); " +
                        "if (__dynResVal is " + dynResTypeName + " __dynResCast) " +
                        instanceVariable + "." + assignment.PropertyName + " = __dynResCast; }");
                }
                continue;
            }

            // EventSetter.Event: resolve the routed event by name from the style's target type hierarchy
            var isEventSetter = node.TypeName.Replace("global::", string.Empty).Equals("System.Windows.EventSetter", StringComparison.Ordinal)
                || node.TypeName.Equals("EventSetter", StringComparison.Ordinal);
            if (isEventSetter &&
                string.Equals(assignment.PropertyName, "Event", StringComparison.Ordinal) &&
                CodeGenUtilities.TryUnquote(assignment.ValueExpression, out var eventNameLiteral))
            {
                var targetTypeExpr = ambientStyleTargetTypeExpression ?? "typeof(global::System.Windows.FrameworkElement)";
                var eventName = eventNameLiteral.Trim();

                // If the target type expression is a compile-time typeof(T) expression, prefer
                // emitting a direct static field access (e.g. T.LoadedEvent) to avoid runtime
                // assembly scanning via __WXSG_ResolveRoutedEvent.
                if (targetTypeExpr.StartsWith("typeof(", StringComparison.Ordinal))
                {
                    var innerType = targetTypeExpr.Substring("typeof(".Length, targetTypeExpr.Length - "typeof(".Length - 1);
                    Builder.AppendLine(
                        MemberIndent + "    " +
                        instanceVariable + ".Event = " + innerType + "." + eventName + "Event;");
                }
                else
                {
                    Builder.AppendLine(
                        MemberIndent + "    " +
                        instanceVariable + ".Event = __WXSG_ResolveRoutedEvent(" + targetTypeExpr + ", " +
                        CodeGenUtilities.EscapeStringLiteral(eventName) + ");");
                }
                continue;
            }

            // EventSetter.Handler: create a delegate bound to this instance using the event's HandlerType
            if (isEventSetter &&
                string.Equals(assignment.PropertyName, "Handler", StringComparison.Ordinal) &&
                CodeGenUtilities.TryUnquote(assignment.ValueExpression, out var handlerNameLiteral))
            {
                var handlerName = handlerNameLiteral.Trim();
                var handlerFlags = "global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.DeclaredOnly";
                var miVar = "__mi_" + _localCounter++.ToString(CultureInfo.InvariantCulture);

                // Resolve the method on the current type but restrict to declared-only members to
                // avoid accidentally binding inherited methods with the same name.  Only create
                // the delegate if the method is found.
                Builder.AppendLine(MemberIndent + "    var " + miVar + " = this.GetType().GetMethod(" + CodeGenUtilities.EscapeStringLiteral(handlerName) + ", " + handlerFlags + ");");
                Builder.AppendLine(MemberIndent + "    if (" + miVar + " is not null)");
                Builder.AppendLine(MemberIndent + "    {");
                Builder.AppendLine(MemberIndent + "        " + instanceVariable + ".Handler = global::System.Delegate.CreateDelegate(" + instanceVariable + ".Event.HandlerType, this, " + miVar + ");");
                Builder.AppendLine(MemberIndent + "    }");
                continue;
            }

            if (string.Equals(assignment.PropertyName, "Property", StringComparison.Ordinal) &&
                (string.Equals(assignment.ClrPropertyTypeName?.Replace("global::", string.Empty), "System.Windows.DependencyProperty", StringComparison.Ordinal) ||
                 string.Equals(assignment.ClrPropertyTypeName, "DependencyProperty", StringComparison.Ordinal)))
            {
                // Try to resolve owner type at generator time for tokens like "OwnerType.Property" and
                // emit direct static field access: typeof(OwnerType).PropertyProperty
                if (CodeGenUtilities.TryUnquote(assignment.ValueExpression, out var __ownerPropLiteral))
                {
                    var __lastDot = __ownerPropLiteral.LastIndexOf('.');
                    if (__lastDot > 0 && __lastDot < __ownerPropLiteral.Length - 1)
                    {
                        var __ownerToken = __ownerPropLiteral.Substring(0, __lastDot);
                        var __propName = __ownerPropLiteral.Substring(__lastDot + 1);
                        var __ownerSimple = __ownerToken;
                        var __colonIdx = __ownerSimple.IndexOf(':');
                        if (__colonIdx >= 0 && __colonIdx < __ownerSimple.Length - 1)
                            __ownerSimple = __ownerSimple.Substring(__colonIdx + 1);
                        var __ownerType = CodeGenUtilities.ResolveRuntimeType(__ownerSimple);
                        if (__ownerType is not null)
                        {
                            Builder.AppendLine(MemberIndent + "    " + instanceVariable + "." + assignment.PropertyName + " = typeof(" + CodeGenUtilities.QualifyType(__ownerType.FullName) + ")." + __propName + "Property;");
                            continue;
                        }
                    }
                }

                var targetTypeExpression = ambientStyleTargetTypeExpression ?? "typeof(global::System.Windows.FrameworkElement)";
                Builder.AppendLine(
                    MemberIndent + "    " +
                    instanceVariable + "." + assignment.PropertyName + " = " +
                    "__WXSG_ResolveSetterDependencyProperty(" + assignment.ValueExpression + ", " + targetTypeExpression + ");");
                continue;
            }

            if (ambientStyleTargetTypeExpression is not null &&
                string.Equals(node.TypeName.Replace("global::", string.Empty), "System.Windows.Setter", StringComparison.Ordinal) &&
                string.Equals(assignment.PropertyName, "Property", StringComparison.Ordinal) &&
                (string.Equals(assignment.ClrPropertyTypeName?.Replace("global::", string.Empty), "System.Windows.DependencyProperty", StringComparison.Ordinal) ||
                 string.Equals(assignment.ClrPropertyTypeName, "DependencyProperty", StringComparison.Ordinal)))
            {
                // Try to resolve owner type at generator time for tokens like "OwnerType.Property"
                if (CodeGenUtilities.TryUnquote(assignment.ValueExpression, out var __ownerPropLit2))
                {
                    var __lastDot2 = __ownerPropLit2.LastIndexOf('.');
                    if (__lastDot2 > 0 && __lastDot2 < __ownerPropLit2.Length - 1)
                    {
                        var __ownerToken2 = __ownerPropLit2.Substring(0, __lastDot2);
                        var __propName2 = __ownerPropLit2.Substring(__lastDot2 + 1);
                        var __ownerSimple2 = __ownerToken2;
                        var __colonIdx2 = __ownerSimple2.IndexOf(':');
                        if (__colonIdx2 >= 0 && __colonIdx2 < __ownerSimple2.Length - 1)
                            __ownerSimple2 = __ownerSimple2.Substring(__colonIdx2 + 1);
                        var __ownerType2 = CodeGenUtilities.ResolveRuntimeType(__ownerSimple2);
                        if (__ownerType2 is not null)
                        {
                            Builder.AppendLine(MemberIndent + "    " + instanceVariable + "." + assignment.PropertyName + " = typeof(" + CodeGenUtilities.QualifyType(__ownerType2.FullName) + ")." + __propName2 + "Property;");
                            continue;
                        }
                    }
                }

                Builder.AppendLine(
                    MemberIndent + "    " +
                    instanceVariable + "." + assignment.PropertyName + " = " +
                    "__WXSG_ResolveSetterDependencyProperty(" + assignment.ValueExpression + ", " + ambientStyleTargetTypeExpression + ");");
                continue;
            }

            if (string.Equals(node.TypeName.Replace("global::", string.Empty), "System.Windows.Setter", StringComparison.Ordinal) &&
                string.Equals(assignment.PropertyName, "Value", StringComparison.Ordinal))
            {
                var convertedSetterValue = CodeGenUtilities.ConvertLiteralExpression(
                    assignment.ValueExpression,
                    assignment.ClrPropertyTypeName,
                    instanceVariable);
                var convTarget = string.IsNullOrWhiteSpace(ambientStyleTargetTypeExpression) ? "null" : ambientStyleTargetTypeExpression;
                Builder.AppendLine(
                    MemberIndent + "    " +
                    instanceVariable + "." + assignment.PropertyName + " = " +
                    "__WXSG_ConvertSetterValue(" + instanceVariable + ".Property, " + convTarget + ", " + convertedSetterValue + ");");
                continue;
            }

            // Trigger.Value should be converted using the resolved DependencyProperty's type
            // so enum/string values like "SubmenuHeader" are converted to the proper enum.
            if (string.Equals(node.TypeName.Replace("global::", string.Empty), "System.Windows.Trigger", StringComparison.Ordinal) &&
                string.Equals(assignment.PropertyName, "Value", StringComparison.Ordinal))
            {
                var convertedTriggerValue = CodeGenUtilities.ConvertLiteralExpression(
                    assignment.ValueExpression,
                    assignment.ClrPropertyTypeName,
                    instanceVariable);
                var convTarget2 = string.IsNullOrWhiteSpace(ambientStyleTargetTypeExpression) ? "null" : ambientStyleTargetTypeExpression;
                Builder.AppendLine(
                    MemberIndent + "    " +
                    instanceVariable + "." + assignment.PropertyName + " = " +
                    "__WXSG_ConvertSetterValue(" + instanceVariable + ".Property, " + convTarget2 + ", " + convertedTriggerValue + ");");
                continue;
            }

            // Unknown (custom) markup extension: the binder encoded the extension data
            // using \x1e/\x1f sentinels.  Emit a block that calls
            // __WXSG_EvaluateUnknownMarkupExtension and then either installs a Binding
            // (if ProvideValue returned one) or assigns the scalar result directly.
            if (CodeGenUtilities.TryUnquote(assignment.ValueExpression, out var umeLiteral) &&
                umeLiteral.Length > 0 && umeLiteral[0] == '\x1e' &&
                CodeGenUtilities.TryParseUnknownMarkupExtensionEncoding(umeLiteral, out var ume))
            {
                var umeIdx = _localCounter++;
                var umeValVar = "__meVal_" + umeIdx.ToString(CultureInfo.InvariantCulture);
                var umeBindVar = "__meBinding_" + umeIdx.ToString(CultureInfo.InvariantCulture);
                var umeCall =
                    "__WXSG_EvaluateUnknownMarkupExtension(" +
                    CodeGenUtilities.EscapeStringLiteral(ume.NsUri) + ", " +
                    CodeGenUtilities.EscapeStringLiteral(ume.LocalName) + ", " +
                    CodeGenUtilities.BuildStringArrayExpression(ume.PositionalArgs) + ", " +
                    CodeGenUtilities.BuildStringArrayExpression(ume.NamedArgKeys) + ", " +
                    CodeGenUtilities.BuildStringArrayExpression(ume.NamedArgValues) + ")";
                var umeTargetType = string.IsNullOrWhiteSpace(assignment.ClrPropertyTypeName)
                    ? "object"
                    : assignment.ClrPropertyTypeName;
                var umeFrameworkOwnerForMe = assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId);
                var umePropNameEscaped = assignment.PropertyName
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");

                Builder.AppendLine(MemberIndent + "    {");
                Builder.AppendLine(MemberIndent + "        var " + umeValVar + " = " + umeCall + ";");
                Builder.AppendLine(MemberIndent + "        if (" + umeValVar +
                    " is global::System.Windows.Data.BindingBase " + umeBindVar + ")");
                Builder.AppendLine(MemberIndent + "            __WXSG_TrySetBinding(" +
                    instanceVariable + ", \"" + umePropNameEscaped + "\", " + umeBindVar + ");");
                Builder.AppendLine(MemberIndent + "        else if (" + umeValVar + " is not null)");
                if (!string.IsNullOrWhiteSpace(umeFrameworkOwnerForMe))
                {
                    Builder.AppendLine(MemberIndent + "            " +
                        CodeGenUtilities.QualifyType(umeFrameworkOwnerForMe) + ".Set" + assignment.PropertyName +
                        "(" + instanceVariable + ", (" + CodeGenUtilities.QualifyType(umeTargetType) + ")" + umeValVar + ");");
                }
                else
                {
                    Builder.AppendLine(MemberIndent + "            " +
                        instanceVariable + "." + assignment.PropertyName +
                        " = (" + CodeGenUtilities.QualifyType(umeTargetType) + ")" + umeValVar + ";");
                }

                Builder.AppendLine(MemberIndent + "    }");
                continue;
            }

            var convertedValue = CodeGenUtilities.ConvertLiteralExpression(
                assignment.ValueExpression,
                assignment.ClrPropertyTypeName,
                instanceVariable);
            var frameworkOwner = assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId);
            if (!string.IsNullOrWhiteSpace(frameworkOwner))
            {
                Builder.AppendLine(
                    MemberIndent + "    " +
                    CodeGenUtilities.QualifyType(frameworkOwner) +
                    ".Set" + assignment.PropertyName + "(" + instanceVariable + ", " + convertedValue + ");");
                continue;
            }

            Builder.AppendLine(
                MemberIndent + "    " +
                instanceVariable + "." + assignment.PropertyName + " = " + convertedValue + ";");
        }
    }

    private void EmitSetBinding(string? instanceTypeName, ResolvedPropertyAssignment assignment, string instanceVariable)
    {
        // Re-parse the raw XAML binding expression stored in ValueExpression.
        if (!CodeGenUtilities.MarkupParser.TryParseMarkupExtension(assignment.ValueExpression, out var info))
            return;

        // Path: first positional arg OR named "Path" arg.
        string? path = null;
        if (info.PositionalArguments.Length > 0)
            path = info.PositionalArguments[0];
        else
            info.NamedArguments.TryGetValue("Path", out path);

        var pathLiteral = string.IsNullOrEmpty(path)
            ? string.Empty
            : "\"" + path!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        // Build the Binding object-initializer for optional named arguments.
        var initParts = new List<string>();
        if (info.NamedArguments.TryGetValue("Mode", out var mode))
            initParts.Add("Mode = global::System.Windows.Data.BindingMode." + mode);
        if (info.NamedArguments.TryGetValue("UpdateSourceTrigger", out var ust))
            initParts.Add("UpdateSourceTrigger = global::System.Windows.Data.UpdateSourceTrigger." + ust);
        if (info.NamedArguments.TryGetValue("ElementName", out var elementName))
            initParts.Add("ElementName = " + ToBindingStringLiteral(elementName));
        if (info.NamedArguments.TryGetValue("Source", out var source))
            initParts.Add("Source = " + BuildBindingMarkupArgumentExpression(source, "object", instanceVariable));
        if (info.NamedArguments.TryGetValue("Converter", out var converter))
            initParts.Add("Converter = (global::System.Windows.Data.IValueConverter)" + BuildBindingMarkupArgumentExpression(converter, "object", instanceVariable));
        if (info.NamedArguments.TryGetValue("ConverterParameter", out var converterParameter))
            initParts.Add("ConverterParameter = " + BuildBindingMarkupArgumentExpression(converterParameter, "object", instanceVariable));
        if (info.NamedArguments.TryGetValue("StringFormat", out var stringFormat))
            initParts.Add("StringFormat = " + ToBindingStringLiteral(stringFormat));
        if (info.NamedArguments.TryGetValue("FallbackValue", out var fallbackValue))
            initParts.Add("FallbackValue = " + BuildBindingMarkupArgumentExpression(fallbackValue, "object", instanceVariable));
        if (info.NamedArguments.TryGetValue("TargetNullValue", out var targetNullValue))
            initParts.Add("TargetNullValue = " + BuildBindingMarkupArgumentExpression(targetNullValue, "object", instanceVariable));

        if (info.NamedArguments.TryGetValue("RelativeSource", out var relativeSource) &&
            TryBuildRelativeSourceExpression(relativeSource, out var relativeSourceExpression))
        {
            initParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        var bindingExpr = "new global::System.Windows.Data.Binding(" + pathLiteral + ")";
        if (initParts.Count > 0)
            bindingExpr += " { " + string.Join(", ", initParts) + " }";

        if (ShouldAssignBindingDirectly(instanceTypeName, assignment))
        {
            Builder.AppendLine(MemberIndent + "    " + instanceVariable +
                               "." + assignment.PropertyName + " = " + bindingExpr + ";");
            return;
        }

        // DependencyProperty field: WPF convention is OwnerType.PropertyNameProperty.
        // For attached properties the framework owner type is stored in FrameworkPayload.
        var ownerTypeName = assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId)
                            ?? assignment.ClrPropertyOwnerTypeName;
        var dpField = CodeGenUtilities.QualifyType(ownerTypeName) + "." + assignment.PropertyName + "Property";

        Builder.AppendLine(MemberIndent + "    global::System.Windows.Data.BindingOperations.SetBinding(" +
                           instanceVariable + ", " + dpField + ", " + bindingExpr + ");");
    }

    private void EmitSetTemplateBinding(string? instanceTypeName, ResolvedPropertyAssignment assignment, string instanceVariable)
    {
        if (!CodeGenUtilities.MarkupParser.TryParseMarkupExtension(assignment.ValueExpression, out var info))
            return;

        string? sourcePropName = null;
        if (info.PositionalArguments.Length > 0)
            sourcePropName = info.PositionalArguments[0];
        else
            info.NamedArguments.TryGetValue("Property", out sourcePropName);

        var pathLiteral = string.IsNullOrEmpty(sourcePropName)
            ? string.Empty
            : "\"" + sourcePropName!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        var bindingExpr = "new global::System.Windows.Data.Binding(" + pathLiteral + ")" +
                          " { RelativeSource = global::System.Windows.Data.RelativeSource.TemplatedParent }";

        var ownerTypeName = assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId)
                            ?? assignment.ClrPropertyOwnerTypeName;
        var dpField = CodeGenUtilities.QualifyType(ownerTypeName) + "." + assignment.PropertyName + "Property";

        Builder.AppendLine(MemberIndent + "    global::System.Windows.Data.BindingOperations.SetBinding(" +
                           instanceVariable + ", " + dpField + ", " + bindingExpr + ");");
    }

    private static bool ShouldAssignBindingDirectly(string? instanceTypeName, ResolvedPropertyAssignment assignment)
    {
        return ShouldAssignBindingDirectly(instanceTypeName, assignment.ClrPropertyTypeName, assignment.PropertyName);
    }

    private static bool ShouldAssignBindingDirectly(string? instanceTypeName, string? propertyTypeName, string? propertyName)
    {
        var effectivePropertyTypeName = propertyTypeName ?? string.Empty;
        var effectivePropertyName = propertyName ?? string.Empty;
        var instanceType = instanceTypeName ?? string.Empty;

        // Non-DependencyObject-style binding sinks: assign the Binding instance directly.
        if (effectivePropertyTypeName.Contains("System.Windows.Data.BindingBase", StringComparison.Ordinal) ||
            effectivePropertyTypeName.Contains("System.Windows.Data.Binding", StringComparison.Ordinal))
        {
            return true;
        }

        if (effectivePropertyName.Equals("Binding", StringComparison.Ordinal) &&
            instanceType.EndsWith(".DataTrigger", StringComparison.Ordinal))
        {
            return true;
        }

        if (effectivePropertyName.Equals("DisplayMemberBinding", StringComparison.Ordinal))
        {
            return true;
        }

        if (effectivePropertyName.Equals("Value", StringComparison.Ordinal) &&
            instanceType.EndsWith(".Setter", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string BuildBindingMarkupArgumentExpression(string rawValue, string? targetTypeName, string scopeExpression)
    {
        var trimmedValue = XamlQuotedValueSemantics.TrimAndUnquote(rawValue);
        var quotedValue = CodeGenUtilities.EscapeStringLiteral(trimmedValue);
        if (CodeGenUtilities.MarkupParser.TryParseMarkupExtension(trimmedValue, out var markupInfo))
        {
            switch (XamlMarkupExtensionNameSemantics.Classify(markupInfo.Name))
            {
                case XamlMarkupExtensionKind.Null:
                    return "null";
                case XamlMarkupExtensionKind.StaticResource:
                    return "__WXSG_ResolveStaticResource(" + scopeExpression + ", " + quotedValue + ")";
                case XamlMarkupExtensionKind.Static:
                    return "__WXSG_ResolveXStatic(" + quotedValue + ")";
                case XamlMarkupExtensionKind.Type:
                    return CodeGenUtilities.ConvertLiteralExpression(quotedValue, "System.Type", scopeExpression);
            }
        }

        if (string.IsNullOrWhiteSpace(targetTypeName) ||
            targetTypeName.Equals("object", StringComparison.Ordinal) ||
            targetTypeName.Equals("System.Object", StringComparison.Ordinal))
        {
            return quotedValue;
        }

        return CodeGenUtilities.ConvertLiteralExpression(quotedValue, targetTypeName, scopeExpression);
    }

    private static string ToBindingStringLiteral(string rawValue)
    {
        return CodeGenUtilities.EscapeStringLiteral(XamlQuotedValueSemantics.TrimAndUnquote(rawValue));
    }

    private static bool TryBuildRelativeSourceExpression(string rawValue, out string expression)
    {
        expression = string.Empty;
        var candidate = XamlQuotedValueSemantics.TrimAndUnquote(rawValue);
        if (!CodeGenUtilities.MarkupParser.TryParseMarkupExtension(candidate, out var markupInfo) ||
            XamlMarkupExtensionNameSemantics.Classify(markupInfo.Name) != XamlMarkupExtensionKind.RelativeSource)
        {
            return false;
        }

        var modeToken = markupInfo.PositionalArguments.Length > 0
            ? XamlQuotedValueSemantics.TrimAndUnquote(markupInfo.PositionalArguments[0]).Trim()
            : string.Empty;
        if (modeToken.Length == 0 &&
            markupInfo.NamedArguments.TryGetValue("Mode", out var namedModeToken))
        {
            modeToken = XamlQuotedValueSemantics.TrimAndUnquote(namedModeToken).Trim();
        }

        if (modeToken.Length == 0)
        {
            return false;
        }

        var qualifiedMode = "global::System.Windows.Data.RelativeSourceMode." + modeToken;
        if (!modeToken.Equals("FindAncestor", StringComparison.Ordinal))
        {
            expression = "new global::System.Windows.Data.RelativeSource(" + qualifiedMode + ")";
            return true;
        }

        var ancestorTypeExpression = "null";
        if (markupInfo.NamedArguments.TryGetValue("AncestorType", out var ancestorTypeToken))
        {
            ancestorTypeExpression = BuildBindingMarkupArgumentExpression(
                ancestorTypeToken,
                "System.Type",
                "global::System.Windows.Application.Current");
        }

        var ancestorLevelExpression = "1";
        if (markupInfo.NamedArguments.TryGetValue("AncestorLevel", out var ancestorLevelToken))
        {
            ancestorLevelExpression = BuildBindingMarkupArgumentExpression(
                ancestorLevelToken,
                "System.Int32",
                "global::System.Windows.Application.Current");
        }

        expression =
            "new global::System.Windows.Data.RelativeSource(" +
            qualifiedMode + ", " +
            ancestorTypeExpression + ", " +
            ancestorLevelExpression + ")";
        return true;
    }

    public void EmitHotReloadCollectionCleanup(ResolvedObjectNode rootNode)
    {
        var emittedMemberAccesses = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(rootNode.ContentPropertyName))
        {
            var contentPropertyName = rootNode.ContentPropertyName;
            switch (rootNode.ChildAttachmentMode)
            {
                case ResolvedChildAttachmentMode.Content:
                    Builder.AppendLine(MemberIndent + "    this." + contentPropertyName + " = null;");
                    emittedMemberAccesses.Add("this." + contentPropertyName);
                    break;
                case ResolvedChildAttachmentMode.ChildrenCollection:
                case ResolvedChildAttachmentMode.ItemsCollection:
                case ResolvedChildAttachmentMode.DirectAdd:
                case ResolvedChildAttachmentMode.DictionaryAdd:
                    Builder.AppendLine(MemberIndent + "    __WXSG_ClearCollectionLike(this." + contentPropertyName + ");");
                    emittedMemberAccesses.Add("this." + contentPropertyName);
                    break;
            }
        }

        foreach (var propertyElement in rootNode.PropertyElementAssignments)
        {
            if (propertyElement.ObjectValues.IsDefaultOrEmpty)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(propertyElement.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId)))
            {
                continue;
            }

            var forceResourcesDictionaryAdd =
                propertyElement.PropertyName.Equals("Resources", StringComparison.Ordinal) &&
                propertyElement.ObjectValues.Length > 0 &&
                !AllObjectValuesAreResourceDictionaries(propertyElement.ObjectValues);

            if (!(propertyElement.IsCollectionAdd ||
                  IsCollectionLikeTypeName(propertyElement.ClrPropertyTypeName) ||
                  forceResourcesDictionaryAdd))
            {
                continue;
            }

            var memberAccess = "this." + propertyElement.PropertyName;
            if (!emittedMemberAccesses.Add(memberAccess))
            {
                continue;
            }

            Builder.AppendLine(MemberIndent + "    __WXSG_ClearCollectionLike(" + memberAccess + ");");
        }
    }

    private void EmitPropertyElementAssignments(
        ResolvedObjectNode node,
        string? ambientStyleTargetTypeExpression,
        string instanceVariable,
        bool suppressNamedFieldRegistration)
    {
        foreach (var propertyElement in node.PropertyElementAssignments)
        {
            if (propertyElement.ObjectValues.IsDefaultOrEmpty)
            {
                continue;
            }

            var createdValues = new List<string>(propertyElement.ObjectValues.Length);
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                createdValues.Add(EmitChildObjectCreation(
                    objectValue,
                    ambientStyleTargetTypeExpression,
                    suppressNamedFieldRegistration));
            }

            var frameworkOwner = propertyElement.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId);
            if (!string.IsNullOrWhiteSpace(frameworkOwner))
            {
                var normalizedOwner = frameworkOwner.Replace("global::", string.Empty);
                if (normalizedOwner == "Microsoft.Xaml.Behaviors.Interaction" &&
                    (propertyElement.PropertyName.Equals("Behaviors", StringComparison.Ordinal) ||
                     propertyElement.PropertyName.Equals("Triggers", StringComparison.Ordinal)))
                {
                    var getterName = propertyElement.PropertyName.Equals("Behaviors", StringComparison.Ordinal)
                        ? "GetBehaviors"
                        : "GetTriggers";
                    foreach (var childVariable in createdValues)
                    {
                        Builder.AppendLine(
                            MemberIndent + "    " +
                            CodeGenUtilities.QualifyType(frameworkOwner) + "." + getterName + "(" + instanceVariable + ").Add(" + childVariable + ");");
                    }

                    continue;
                }

                foreach (var childVariable in createdValues)
                {
                    Builder.AppendLine(
                        MemberIndent + "    " +
                        CodeGenUtilities.QualifyType(frameworkOwner) +
                        ".Set" + propertyElement.PropertyName + "(" + instanceVariable + ", " + childVariable + ");");
                }

                continue;
            }

            if (propertyElement.PropertyName.Equals("Resources", StringComparison.Ordinal) &&
                AllObjectValuesAreResourceDictionaries(propertyElement.ObjectValues))
            {
                Builder.AppendLine(
                    MemberIndent + "    " +
                    instanceVariable + "." + propertyElement.PropertyName + " = " + createdValues[0] + ";");
                continue;
            }

            if (propertyElement.PropertyName.Equals("VisualTree", StringComparison.Ordinal))
            {
                var visualTreeFactory = EmitFrameworkElementFactoryTree(propertyElement.ObjectValues[0]);
                Builder.AppendLine(
                    MemberIndent + "    " +
                    instanceVariable + "." + propertyElement.PropertyName + " = " + visualTreeFactory + ";");
                continue;
            }

            var forceResourcesDictionaryAdd =
                propertyElement.PropertyName.Equals("Resources", StringComparison.Ordinal) &&
                propertyElement.ObjectValues.Length > 0;

            // Check if property value is a BindingBase BEFORE checking if property type is collection-like.
            // This ensures MultiBinding on IEnumerable properties (like ItemsSource) uses SetBinding,
            // not .Add() which doesn't exist on IEnumerable.
            if (propertyElement.ObjectValues.Length > 0 &&
                CodeGenUtilities.IsBindingBaseTypeName(propertyElement.ObjectValues[0].TypeName))
            {
                var bindingExpression = BuildChildValueExpression(
                    propertyElement.ObjectValues[0],
                    createdValues[0],
                    "global::System.Windows.Data.BindingBase",
                    instanceVariable);

                if (ShouldAssignBindingDirectly(node.TypeName, propertyElement.ClrPropertyTypeName, propertyElement.PropertyName))
                {
                    Builder.AppendLine(
                        MemberIndent + "    " +
                        instanceVariable + "." + propertyElement.PropertyName + " = " + bindingExpression + ";");
                }
                else
                {
                    var ownerTypeName = propertyElement.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId);
                    if (string.IsNullOrWhiteSpace(ownerTypeName))
                    {
                        ownerTypeName = propertyElement.ClrPropertyOwnerTypeName ?? node.TypeName;
                    }

                    Builder.AppendLine(
                        MemberIndent + "    global::System.Windows.Data.BindingOperations.SetBinding(" +
                        instanceVariable + ", " +
                        CodeGenUtilities.QualifyType(ownerTypeName) + "." + propertyElement.PropertyName + "Property, " +
                        bindingExpression + ");");
                }

                continue;
            }

            // Handle collection properties
            if (propertyElement.IsCollectionAdd ||
                IsCollectionLikeTypeName(propertyElement.ClrPropertyTypeName) ||
                forceResourcesDictionaryAdd)
            {
                var isDictionaryAdd = forceResourcesDictionaryAdd ||
                                      IsDictionaryLikeTypeName(propertyElement.ClrPropertyTypeName);
                var collectionElementTypeName = CodeGenUtilities.GetCollectionElementTypeName(propertyElement.ClrPropertyTypeName);
                if (string.IsNullOrWhiteSpace(collectionElementTypeName) &&
                    propertyElement.PropertyName.Equals("Bindings", StringComparison.Ordinal))
                {
                    collectionElementTypeName = "global::System.Windows.Data.BindingBase";
                }
                for (var index = 0; index < createdValues.Count; index++)
                {
                    var childVariable = createdValues[index];
                    var childValueExpression = BuildChildValueExpression(
                        propertyElement.ObjectValues[index],
                        childVariable,
                        collectionElementTypeName,
                        instanceVariable);
                    if (isDictionaryAdd)
                    {
                        var keyExpression = BuildDictionaryKeyExpression(
                            propertyElement.PropertyName,
                            propertyElement.ObjectValues[index],
                            childVariable);
                        Builder.AppendLine(
                            MemberIndent + "    " +
                            instanceVariable + "." + propertyElement.PropertyName + ".Add(" +
                            keyExpression + ", " + childValueExpression + ");");
                    }
                    else
                    {
                        Builder.AppendLine(
                            MemberIndent + "    " +
                            instanceVariable + "." + propertyElement.PropertyName + ".Add(" + childValueExpression + ");");
                    }
                }

                continue;
            }

            Builder.AppendLine(
                MemberIndent + "    " +
                instanceVariable + "." + propertyElement.PropertyName + " = " +
                BuildChildValueExpression(
                    propertyElement.ObjectValues[0],
                    createdValues[0],
                    propertyElement.ClrPropertyTypeName,
                    instanceVariable) + ";");
        }
    }

    private void EmitEventSubscriptions(ResolvedObjectNode node, string instanceVariable)
    {
        foreach (var subscription in node.EventSubscriptions)
        {
            if (string.IsNullOrWhiteSpace(subscription.HandlerMethodName))
            {
                continue;
            }

            if (subscription.Kind == ResolvedEventSubscriptionKind.RoutedEvent &&
                !string.IsNullOrWhiteSpace(subscription.RoutedEventOwnerTypeName) &&
                !string.IsNullOrWhiteSpace(subscription.RoutedEventFieldName))
            {
                var handlerTypeName = string.IsNullOrWhiteSpace(subscription.RoutedEventHandlerTypeName)
                    ? "global::System.Windows.RoutedEventHandler"
                    : CodeGenUtilities.QualifyType(subscription.RoutedEventHandlerTypeName);
                Builder.AppendLine(
                    MemberIndent + "    " +
                    instanceVariable +
                    ".AddHandler(" +
                    CodeGenUtilities.QualifyType(subscription.RoutedEventOwnerTypeName) +
                    "." + subscription.RoutedEventFieldName +
                    ", new " + handlerTypeName + "(this." +
                    subscription.HandlerMethodName + "));" );
                continue;
            }

            Builder.AppendLine(
                MemberIndent + "    " +
                instanceVariable +
                "." + subscription.EventName +
                " += this." + subscription.HandlerMethodName + ";");
        }
    }

    private void EmitChildNodes(
        ResolvedObjectNode node,
        string instanceVariable,
        string? ambientStyleTargetTypeExpression,
        bool suppressNamedFieldRegistration)
    {
        if (node.Children.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            var childVariable = EmitChildObjectCreation(
                child,
                ambientStyleTargetTypeExpression,
                suppressNamedFieldRegistration);
            AttachChild(node, instanceVariable, child, childVariable);
        }
    }

    private string EmitChildObjectCreation(
        ResolvedObjectNode child,
        string? ambientStyleTargetTypeExpression,
        bool suppressNamedFieldRegistration)
    {
        var localVariable = "__node" + _localCounter.ToString(CultureInfo.InvariantCulture);
        _localCounter++;

        if (child.HasSemantic(ResolvedObjectNodeSemanticFlags.IsXamlArray))
        {
            var arrayItems = new List<string>(child.Children.Length);
            foreach (var arrayChild in child.Children)
            {
                arrayItems.Add(EmitChildObjectCreation(
                    arrayChild,
                    ambientStyleTargetTypeExpression,
                    suppressNamedFieldRegistration));
            }

            var elementTypeName = string.IsNullOrWhiteSpace(child.ContentPropertyTypeName)
                ? "object"
                : CodeGenUtilities.QualifyType(child.ContentPropertyTypeName);
            var arrayExpression = arrayItems.Count == 0
                ? "new " + elementTypeName + "[0]"
                : "new " + elementTypeName + "[] { " + string.Join(", ", arrayItems) + " }";
            Builder.AppendLine(MemberIndent + "    var " + localVariable + " = " + arrayExpression + ";");
            return localVariable;
        }

        var creationExpression = !string.IsNullOrWhiteSpace(child.FactoryExpression)
            ? child.FactoryExpression
            : "new " + CodeGenUtilities.QualifyType(child.TypeName) + "()";

        Builder.AppendLine(MemberIndent + "    var " + localVariable + " = " + creationExpression + ";");
        EmitNodeInitialization(
            child,
            localVariable,
            isRootNode: false,
            ambientStyleTargetTypeExpression,
            suppressNamedFieldRegistration);
        return localVariable;
    }

    private static bool CreatesNestedNameScope(ResolvedObjectNode node)
    {
        var typeName = node.TypeName.Replace("global::", string.Empty);
        return typeName switch
        {
            "System.Windows.DataTemplate" => true,
            "System.Windows.HierarchicalDataTemplate" => true,
            "System.Windows.Controls.ControlTemplate" => true,
            "System.Windows.Controls.ItemsPanelTemplate" => true,
            _ => false
        };
    }

    private void AttachChild(
        ResolvedObjectNode parent,
        string parentVariable,
        ResolvedObjectNode child,
        string childVariable)
    {
        var contentProperty = parent.ContentPropertyName;
        if (string.IsNullOrWhiteSpace(contentProperty))
        {
            if (IsDictionaryLikeTypeName(parent.TypeName))
            {
                var keyExpression = BuildDictionaryKeyExpression("Resources", child, childVariable);
                Builder.AppendLine(
                    MemberIndent + "    " +
                    parentVariable + ".Add(" + keyExpression + ", " + childVariable + ");");
            }

            return;
        }

        if (contentProperty.Equals("VisualTree", StringComparison.Ordinal))
        {
            var visualTreeFactory = EmitFrameworkElementFactoryTree(child);
            Builder.AppendLine(
                MemberIndent + "    " +
                parentVariable + "." + contentProperty + " = " + visualTreeFactory + ";");
            return;
        }

        switch (parent.ChildAttachmentMode)
        {
            case ResolvedChildAttachmentMode.Content:
                Builder.AppendLine(
                    MemberIndent + "    " +
                    parentVariable + "." + contentProperty + " = " + childVariable + ";");
                return;
            case ResolvedChildAttachmentMode.ChildrenCollection:
            case ResolvedChildAttachmentMode.ItemsCollection:
            case ResolvedChildAttachmentMode.DirectAdd:
                // When attaching a BindingBase (Binding, MultiBinding, PriorityBinding) to a
                // property that is not a BindingCollection (MultiBinding.Bindings), use SetBinding.
                if (CodeGenUtilities.IsBindingBaseTypeName(child.TypeName) &&
                    !contentProperty.Equals("Bindings", StringComparison.Ordinal))
                {
                    Builder.AppendLine(
                        MemberIndent + "    global::System.Windows.Data.BindingOperations.SetBinding(" +
                        parentVariable + ", " +
                        CodeGenUtilities.QualifyType(parent.TypeName) + "." + contentProperty + "Property, " +
                        childVariable + ");");
                    return;
                }

                // When a MarkupExtension that is not a BindingBase is added to a
                // BindingCollection (i.e. MultiBinding.Bindings), call ProvideValue so
                // the extension can return the Binding it wraps.
                var addExpression = childVariable;
                if (contentProperty.Equals("Bindings", StringComparison.Ordinal) &&
                    CodeGenUtilities.IsMarkupExtensionTypeName(child.TypeName) &&
                    !CodeGenUtilities.IsBindingBaseTypeName(child.TypeName))
                {
                    addExpression = "(global::System.Windows.Data.BindingBase)" +
                                    "__WXSG_EvaluateMarkupExtension(" + childVariable + ")";
                }
                Builder.AppendLine(
                    MemberIndent + "    " +
                    parentVariable + "." + contentProperty + ".Add(" + addExpression + ");");
                return;
            case ResolvedChildAttachmentMode.DictionaryAdd:
                var keyExpression = BuildDictionaryKeyExpression(contentProperty, child, childVariable);
                var dictionaryTarget = contentProperty == "__self"
                    ? parentVariable
                    : parentVariable + "." + contentProperty;
                Builder.AppendLine(
                    MemberIndent + "    " +
                    dictionaryTarget + ".Add(" + keyExpression + ", " + childVariable + ");");
                return;
            default:
                return;
        }
    }

    private string EmitFrameworkElementFactoryTree(ResolvedObjectNode node)
    {
        var localVariable = "__fef" + _localCounter.ToString(CultureInfo.InvariantCulture);
        _localCounter++;

        Builder.AppendLine(
            MemberIndent + "    var " + localVariable + " = new global::System.Windows.FrameworkElementFactory(typeof(" +
            CodeGenUtilities.QualifyType(node.TypeName) + "));");

        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            Builder.AppendLine(
                MemberIndent + "    " + localVariable + ".SetValue(global::System.Windows.FrameworkElement.NameProperty, " +
                CodeGenUtilities.EscapeStringLiteral(node.Name) + ");");
        }

        EmitFrameworkElementFactoryPropertyAssignments(node, localVariable);

        foreach (var child in node.Children)
        {
            var childFactory = EmitFrameworkElementFactoryTree(child);
            Builder.AppendLine(
                MemberIndent + "    " + localVariable + ".AppendChild(" + childFactory + ");");
        }

        return localVariable;
    }

    private void EmitFrameworkElementFactoryPropertyAssignments(ResolvedObjectNode node, string factoryVariable)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (assignment.ValueKind == ResolvedValueKind.Binding)
            {
                EmitFrameworkElementFactoryBindingAssignment(assignment, factoryVariable);
                continue;
            }

            if (assignment.ValueKind == ResolvedValueKind.TemplateBinding)
            {
                EmitFrameworkElementFactoryTemplateBindingAssignment(assignment, factoryVariable);
                continue;
            }

            var dependencyPropertyExpression = TryBuildDependencyPropertyExpression(
                assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId) ??
                assignment.ClrPropertyOwnerTypeName ??
                node.TypeName,
                assignment.PropertyName);

            if (dependencyPropertyExpression is null)
            {
                continue;
            }

            var assignmentTargetTypeName = CodeGenUtilities.ResolveFrameworkElementFactoryPropertyTypeName(
                assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId) ??
                assignment.ClrPropertyOwnerTypeName ??
                node.TypeName,
                assignment.PropertyName,
                assignment.ClrPropertyTypeName);
            var convertedValue = CodeGenUtilities.ConvertLiteralExpression(
                assignment.ValueExpression,
                assignmentTargetTypeName,
                "null");

            Builder.AppendLine(
                MemberIndent + "    " + factoryVariable + ".SetValue(" + dependencyPropertyExpression + ", " + convertedValue + ");");
        }
    }

    private void EmitFrameworkElementFactoryBindingAssignment(
        ResolvedPropertyAssignment assignment,
        string factoryVariable)
    {
        if (!CodeGenUtilities.MarkupParser.TryParseMarkupExtension(assignment.ValueExpression, out var info))
        {
            return;
        }

        var dependencyPropertyExpression = TryBuildDependencyPropertyExpression(
            assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId) ??
            assignment.ClrPropertyOwnerTypeName,
            assignment.PropertyName);
        if (dependencyPropertyExpression is null)
        {
            return;
        }

        string? path = null;
        if (info.PositionalArguments.Length > 0)
        {
            path = info.PositionalArguments[0];
        }
        else
        {
            info.NamedArguments.TryGetValue("Path", out path);
        }

        var pathLiteral = string.IsNullOrEmpty(path)
            ? string.Empty
            : "\"" + path!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var initParts = new List<string>();
        if (info.NamedArguments.TryGetValue("Mode", out var mode))
            initParts.Add("Mode = global::System.Windows.Data.BindingMode." + mode);
        if (info.NamedArguments.TryGetValue("UpdateSourceTrigger", out var ust))
            initParts.Add("UpdateSourceTrigger = global::System.Windows.Data.UpdateSourceTrigger." + ust);
        if (info.NamedArguments.TryGetValue("ElementName", out var elementName))
            initParts.Add("ElementName = " + ToBindingStringLiteral(elementName));
        if (info.NamedArguments.TryGetValue("Source", out var source))
            initParts.Add("Source = " + BuildBindingMarkupArgumentExpression(source, "object", "this"));
        if (info.NamedArguments.TryGetValue("Converter", out var converter))
            initParts.Add("Converter = (global::System.Windows.Data.IValueConverter)" + BuildBindingMarkupArgumentExpression(converter, "object", "this"));
        if (info.NamedArguments.TryGetValue("ConverterParameter", out var converterParameter))
            initParts.Add("ConverterParameter = " + BuildBindingMarkupArgumentExpression(converterParameter, "object", "this"));
        if (info.NamedArguments.TryGetValue("StringFormat", out var stringFormat))
            initParts.Add("StringFormat = " + ToBindingStringLiteral(stringFormat));
        if (info.NamedArguments.TryGetValue("FallbackValue", out var fallbackValue))
            initParts.Add("FallbackValue = " + BuildBindingMarkupArgumentExpression(fallbackValue, "object", "this"));
        if (info.NamedArguments.TryGetValue("TargetNullValue", out var targetNullValue))
            initParts.Add("TargetNullValue = " + BuildBindingMarkupArgumentExpression(targetNullValue, "object", "this"));
        if (info.NamedArguments.TryGetValue("RelativeSource", out var relativeSource) &&
            TryBuildRelativeSourceExpression(relativeSource, out var relativeSourceExpression))
            initParts.Add("RelativeSource = " + relativeSourceExpression);

        var bindingCtor = string.IsNullOrEmpty(pathLiteral)
            ? "new global::System.Windows.Data.Binding()"
            : "new global::System.Windows.Data.Binding(" + pathLiteral + ")";
        if (initParts.Count > 0)
        {
            bindingCtor += " { " + string.Join(", ", initParts) + " }";
        }

        Builder.AppendLine(
            MemberIndent + "    " + factoryVariable + ".SetBinding(" + dependencyPropertyExpression + ", " + bindingCtor + ");");
    }

    private void EmitFrameworkElementFactoryTemplateBindingAssignment(
        ResolvedPropertyAssignment assignment,
        string factoryVariable)
    {
        if (!CodeGenUtilities.MarkupParser.TryParseMarkupExtension(assignment.ValueExpression, out var info))
        {
            return;
        }

        var dependencyPropertyExpression = TryBuildDependencyPropertyExpression(
            assignment.GetFrameworkPropertyOwnerTypeName(WpfFrameworkId) ??
            assignment.ClrPropertyOwnerTypeName,
            assignment.PropertyName);
        if (dependencyPropertyExpression is null)
        {
            return;
        }

        // {TemplateBinding PropertyName} — positional arg is the source property name
        string? sourcePropName = null;
        if (info.PositionalArguments.Length > 0)
        {
            sourcePropName = info.PositionalArguments[0];
        }
        else
        {
            info.NamedArguments.TryGetValue("Property", out sourcePropName);
        }

        var pathLiteral = string.IsNullOrEmpty(sourcePropName)
            ? string.Empty
            : "\"" + sourcePropName!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        var bindingCtor = string.IsNullOrEmpty(pathLiteral)
            ? "new global::System.Windows.Data.Binding()"
            : "new global::System.Windows.Data.Binding(" + pathLiteral + ")";
        bindingCtor += " { RelativeSource = global::System.Windows.Data.RelativeSource.TemplatedParent }";

        Builder.AppendLine(
            MemberIndent + "    " + factoryVariable + ".SetBinding(" + dependencyPropertyExpression + ", " + bindingCtor + ");");
    }

    private static string? TryBuildDependencyPropertyExpression(string? ownerTypeName, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(ownerTypeName) || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        return CodeGenUtilities.QualifyType(ownerTypeName) + "." + propertyName + "Property";
    }

    private static string BuildDictionaryKeyExpression(
        string? parentPropertyName,
        ResolvedObjectNode child,
        string childVariable)
    {
        if (!string.IsNullOrWhiteSpace(child.KeyExpression))
        {
            return child.KeyExpression;
        }

        if (string.Equals(parentPropertyName, "Resources", StringComparison.Ordinal) &&
            string.Equals(child.TypeName.Replace("global::", string.Empty), "System.Windows.Style", StringComparison.Ordinal))
        {
            return childVariable + ".TargetType";
        }

        return AsFallbackDictionaryKey(child);
    }

    private static string BuildChildValueExpression(
        ResolvedObjectNode child,
        string childVariable,
        string? targetTypeName,
        string? scopeExpression)
    {
        if (string.IsNullOrWhiteSpace(targetTypeName))
        {
            return childVariable;
        }

        if (!CodeGenUtilities.IsMarkupExtensionTypeName(child.TypeName))
        {
            return childVariable;
        }

        if (scopeExpression is null)
        {
            scopeExpression = "global::System.Windows.Application.Current";
        }

        return "(" + CodeGenUtilities.QualifyType(targetTypeName) + ")__WXSG_EvaluateMarkupExtension(" + childVariable + ")";
    }

    private static bool IsCollectionLikeTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var normalized = typeName.Replace("global::", string.Empty);
        return normalized.Contains("ICollection", StringComparison.Ordinal) ||
               normalized.Contains("IList", StringComparison.Ordinal) ||
               normalized.Contains("IEnumerable", StringComparison.Ordinal) ||
               normalized.Contains("Collection", StringComparison.Ordinal) ||
               normalized.Contains("List", StringComparison.Ordinal);
    }

    private static bool IsDictionaryLikeTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var normalized = typeName.Replace("global::", string.Empty);
        return normalized.Contains("IDictionary", StringComparison.Ordinal) ||
               normalized.EndsWith("Dictionary", StringComparison.Ordinal) ||
               normalized.EndsWith("Dictionary`1", StringComparison.Ordinal) ||
               normalized.EndsWith("Dictionary`2", StringComparison.Ordinal);
    }

    private static bool AllObjectValuesAreResourceDictionaries(ImmutableArray<ResolvedObjectNode> objectValues)
    {
        foreach (var objectValue in objectValues)
        {
            if (!string.Equals(
                    objectValue.TypeName.Replace("global::", string.Empty),
                    "System.Windows.ResourceDictionary",
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return objectValues.Length > 0;
    }

    private static string AsFallbackDictionaryKey(ResolvedObjectNode child)
    {
        if (!string.IsNullOrWhiteSpace(child.Name))
        {
            return "\"" + child.Name + "\"";
        }

        return "\"" + child.TypeName.Replace("\"", "\\\"") + "\"";
    }
}
