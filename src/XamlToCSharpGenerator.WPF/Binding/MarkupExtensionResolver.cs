using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.WPF.Binding;

internal static class MarkupExtensionResolver
{
    private static readonly MarkupExpressionParser MarkupParser = new();

    internal static (string ValueExpression, ResolvedValueKind ValueKind) ConvertAssignmentValue(
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
                // Detect {TemplateBinding ...} — keep the raw XAML string so the emitter can emit
                // a SetBinding call with RelativeSource.TemplatedParent.
                case XamlMarkupExtensionKind.TemplateBinding:
                    return (rawValue, ResolvedValueKind.TemplateBinding);
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
                case XamlMarkupExtensionKind.Static:
                    // Try to qualify {x:Static p:ClassName.Member} with full namespace info
                    // Try the full resolution method first
                    if (TryBuildStaticMarkupExtensionQualified(markupInfo, context, out var qualifiedStaticExpr))
                    {
                        return (qualifiedStaticExpr, ResolvedValueKind.Literal);
                    }
                    // Fall through to keep as literal string for emitter's x:Static resolver.
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

    internal static bool TryConvertTypeMarkupExtension(
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

        var resolvedType = TypeResolver.ResolveTypeToken(typeToken, context);
        if (resolvedType is null)
        {
            return false;
        }

        var displayName = resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty);
        typeExpression = "typeof(" + displayName + ")";
        return true;
    }

    internal static bool TryParseInlineCSharpExpression(
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

    internal static bool TryParseCsPrefixExpression(string value, out string csharpExpression)
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
    internal static bool TryBuildUnknownMarkupExtensionEncoding(
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

    internal static bool TryBuildStaticMarkupExtensionQualified(
        MarkupExtensionInfo markupInfo,
        BindingContext context,
        out string qualifiedExpr)
    {
        qualifiedExpr = string.Empty;

        try
        {
            // Extract the positional argument: {x:Static p:Converters.CollectionsToComposite} -> "p:Converters.CollectionsToComposite"
            if (markupInfo.PositionalArguments.Length == 0)
            {
                return false;
            }

            var memberToken = markupInfo.PositionalArguments[0];
            if (string.IsNullOrWhiteSpace(memberToken))
                return false;

            // Parse the member token to extract prefix and type+member: "p:Converters.CollectionsToComposite" -> ("p", "Converters.CollectionsToComposite")
            if (!XamlTokenSplitSemantics.TrySplitAtFirstSeparator(memberToken, ':', out var prefix, out var typeAndMember))
            {
                return false;
            }

            // Look up the XML namespace for this prefix
            if (!context.Document.XmlNamespaces.TryGetValue(prefix, out var xmlNamespace))
            {
                return false;
            }

            // Try to resolve the CLR namespace and assembly from the XML namespace
            string? clrNamespace = null;
            string? assemblyName = null;

            // Try direct clr-namespace: format first
            if (XamlXmlNamespaceSemantics.TryExtractClrNamespaceReference(xmlNamespace, out var directClrNs, out var directAsmName))
            {
                clrNamespace = directClrNs;
                assemblyName = directAsmName;
            }
            else
            {
                // Try XmlnsMap to resolve the namespace (this includes assembly XmlnsDefinition attributes)
                var mappings = context.XmlnsMap.TryGetNamespaces(xmlNamespace, out var namespaceMappings) ? namespaceMappings : null;
                if (mappings?.Any() == true)
                {
                    var firstMapping = mappings.First();
                    clrNamespace = firstMapping.ClrNamespace;
                    assemblyName = firstMapping.AssemblyName;
                }
            }

            // If we couldn't resolve the namespace, fall back to using the XML namespace URI
            // The emitter can later search assemblies with XmlnsDefinition attributes for this URI at runtime
            if (string.IsNullOrWhiteSpace(clrNamespace))
            {
                // Encode the XML namespace URI so emitter can look it up at runtime
                // Use "xmlns:" prefix to indicate this is an XML namespace lookup
                clrNamespace = $"xmlns:{Uri.EscapeDataString(xmlNamespace)}";
            }

            // Parse type and member: "Converters.CollectionsToComposite" -> ("Converters", "CollectionsToComposite")
            var lastDot = typeAndMember.LastIndexOf('.');
            if (lastDot <= 0 || lastDot >= typeAndMember.Length - 1)
                return false;

            var typeName = typeAndMember.Substring(0, lastDot);
            var memberName = typeAndMember.Substring(lastDot + 1);

            // Build the fully-qualified XAML format: "{x:Static clr-namespace:namespace;assembly=assemblyName:Type.Member}"
            // If clrNamespace starts with "xmlns:", the emitter knows to look up the XML namespace at runtime
            var asmPart = !string.IsNullOrWhiteSpace(assemblyName) ? $";assembly={assemblyName}" : string.Empty;
            qualifiedExpr = AsStringLiteral($"{{x:Static clr-namespace:{clrNamespace}{asmPart}:{typeName}.{memberName}}}");
            return true;
        }
        catch
        {
            // If anything fails, just return false and let the fallback handle it
            return false;
        }
    }

    internal static bool TryResolveStaticMarkupExtension(
        MarkupExtensionInfo markupInfo,
        BindingContext context,
        out string resolvedExpr)
    {
        resolvedExpr = string.Empty;

        // Extract the positional argument: {x:Static p:Converters.CollectionsToComposite} -> "p:Converters.CollectionsToComposite"
        if (markupInfo.PositionalArguments.Length == 0)
            return false;

        var memberToken = markupInfo.PositionalArguments[0];
        if (string.IsNullOrWhiteSpace(memberToken))
            return false;

        // Parse the member token to extract prefix and type+member: "p:Converters.CollectionsToComposite" -> ("p", "Converters.CollectionsToComposite")
        if (!XamlTokenSplitSemantics.TrySplitAtFirstSeparator(memberToken, ':', out var prefix, out var typeAndMember))
        {
            // No prefix, use the member token as-is
            typeAndMember = memberToken;
            prefix = string.Empty;
        }

        // Resolve the XML namespace prefix to a list of CLR namespaces
        string? xmlNamespace = null;
        if (!string.IsNullOrWhiteSpace(prefix) && context.Document.XmlNamespaces.TryGetValue(prefix, out var prefixXmlNamespace))
        {
            xmlNamespace = prefixXmlNamespace;
        }

        // Parse type and member: "Converters.CollectionsToComposite" -> ("Converters", "CollectionsToComposite")
        var lastDot = typeAndMember.LastIndexOf('.');
        if (lastDot <= 0 || lastDot >= typeAndMember.Length - 1)
            return false;

        var typeName = typeAndMember.Substring(0, lastDot);
        var memberName = typeAndMember.Substring(lastDot + 1);

        // Resolve the type name using the namespace(s)
        INamedTypeSymbol? resolvedType = null;

        if (!string.IsNullOrWhiteSpace(xmlNamespace))
        {
            // Use the resolved XML namespace to find the type
            resolvedType = TypeResolver.ResolveTypeSymbol(xmlNamespace!, typeName, ImmutableArray<string>.Empty, context);
        }
        else if (!string.IsNullOrWhiteSpace(prefix))
        {
            // Prefix was present but not in XmlNamespaces (unexpected), fall back to unqualified lookup
            // Try to resolve by searching all loaded types (fallback path)
        }
        else
        {
            // No prefix, try to resolve in known WPF namespaces
            var knownNamespaces = new[] {
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

            foreach (var ns in knownNamespaces)
            {
                resolvedType = TypeResolver.ResolveTypeSymbol(ns, typeName, ImmutableArray<string>.Empty, context);
                if (resolvedType is not null)
                    break;
            }
        }

        if (resolvedType is null)
            return false;

        // Build the fully-qualified XAML namespace reference: "x:Static clr-namespace:XStaticCustomNsSample;assembly=XStaticCustomNsSample:Converters.CollectionsToComposite"
        // This format can be resolved later by the emitter's __WXSG_ResolveXStatic without needing runtime XmlnsDefinition lookup
        var assemblyName = resolvedType.ContainingAssembly?.Name ?? string.Empty;
        var namespaceName = resolvedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        // Format as {x:Static clr-namespace:namespace;assembly=assemblyName:TypeName.MemberName}
        // The emitter will parse this as: extract namespace from before ';', extract member from after final ':'
        resolvedExpr = AsStringLiteral($"{{x:Static clr-namespace:{namespaceName};assembly={assemblyName}:{resolvedType.Name}.{memberName}}}");
        return true;
    }

    internal static string AsStringLiteral(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
