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
                // Pre-process named args that are unknown markup extensions (e.g. Converter={ns:Foo})
                // so the emitter can resolve them at runtime.
                case XamlMarkupExtensionKind.Binding:
                    return (PreprocessBindingNamedArgs(rawValue, markupInfo, context), ResolvedValueKind.Binding);
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
            return (Emission.CodeGenUtilities.EscapeStringLiteral(rawValue), ResolvedValueKind.Literal);
        }

        if (TryParseInlineCSharpExpression(rawValue, context, out var fallbackCsharpExpression))
        {
            return (fallbackCsharpExpression, ResolvedValueKind.MarkupExtension);
        }

        return (Emission.CodeGenUtilities.EscapeStringLiteral(rawValue), ResolvedValueKind.Literal);
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

        ITypeSymbol? resolvedType = null;
        if (XamlTokenSplitSemantics.TrySplitAtFirstSeparator(typeToken, ':', out var prefix, out var xmlTypeName) &&
            context.Document.XmlNamespaces.TryGetValue(prefix, out var prefixXmlNamespace))
        {
            resolvedType = TypeResolver.ResolveTypeSymbol(prefixXmlNamespace, xmlTypeName, ImmutableArray<string>.Empty, context);
        }

        resolvedType ??= TypeResolver.ResolveTypeToken(typeToken, context);
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
        // Avoid emitting generator-only encodings into classless (raw) XAML documents.
        // Classless documents are embedded as raw XAML and parsed by WPF at runtime;
        // emitting a special generator encoding here would leak internal syntax into
        // the raw XAML. Let the build-time preprocessor handle any assembly
        // qualification for classless outputs.
        if (!context.Document.IsClassBacked)
        {
            return false;
        }
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
            // Try to resolve the arg as a XAML type token (e.g. "controls:MainMenu").
            // If resolved, encode it with "t:" so the runtime helper can pass a Type
            // to the markup extension constructor instead of a raw string.
            var trimmedArg = XamlQuotedValueSemantics.TrimAndUnquote(arg).Trim();
            var resolvedArgType = TypeResolver.ResolveTypeToken(trimmedArg, context);
            if (resolvedArgType is not null)
            {
                var fqn = resolvedArgType.ToDisplayString(
                    Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat)
                    .Replace("global::", string.Empty);
                sb.Append("t:");
                sb.Append(fqn);
            }
            else
            {
                sb.Append("p:");
                sb.Append(arg);
            }
        }

        foreach (var kvp in markupInfo.NamedArguments)
        {
            sb.Append('\x1f');
            sb.Append("n:");
            sb.Append(kvp.Key);
            sb.Append('=');
            sb.Append(kvp.Value);
        }

        encoding = Emission.CodeGenUtilities.EscapeStringLiteral(sb.ToString());
        return true;
    }

    internal static bool TryBuildStaticMarkupExtensionQualified(
        MarkupExtensionInfo markupInfo,
        BindingContext context,
        out string qualifiedExpr)
    {
        qualifiedExpr = string.Empty;

        // Do not produce inline clr-namespace/assembly encodings for classless/raw XAML
        // documents. Those files are intended to be loaded by WPF's native loader;
        // injecting generator-specific encodings would create invalid XAML.
        if (!context.Document.IsClassBacked)
        {
            return false;
        }

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
            qualifiedExpr = Emission.CodeGenUtilities.EscapeStringLiteral($"{{x:Static clr-namespace:{clrNamespace}{asmPart}:{typeName}.{memberName}}}");
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
        resolvedExpr = Emission.CodeGenUtilities.EscapeStringLiteral($"{{x:Static clr-namespace:{namespaceName};assembly={assemblyName}:{resolvedType.Name}.{memberName}}}");
        return true;
    }

    private static string PreprocessBindingNamedArgs(string rawValue, MarkupExtensionInfo bindingInfo, BindingContext context)
    {
        // For classless (raw) XAML documents, avoid encoding nested unknown markup
        // extensions. Preserve the original XAML so the runtime/native loader can
        // parse it without generator encodings.
        if (!context.Document.IsClassBacked)
        {
            return rawValue;
        }

        var result = rawValue;
        foreach (var kvp in bindingInfo.NamedArguments)
        {
            var argRaw = TryExtractRawBindingNamedArgumentMarkup(rawValue, kvp.Key) ?? kvp.Value.Trim();
            if (!MarkupParser.TryParseMarkupExtension(argRaw, out var nestedInfo))
                continue;

            switch (XamlMarkupExtensionNameSemantics.Classify(nestedInfo.Name))
            {
                case XamlMarkupExtensionKind.Type:
                    if (TryNormalizeBindingTypeMarkupExtension(argRaw, nestedInfo, context, out var normalizedTypeMarkup))
                    {
                        result = result.Replace(argRaw, normalizedTypeMarkup);
                    }

                    break;
                case XamlMarkupExtensionKind.Unknown:
                    if (TryBuildUnknownMarkupExtensionEncoding(nestedInfo, context, out var encoding))
                    {
                        // Replace the raw nested markup extension with the UME-encoded quoted string.
                        // The encoding is already a quoted string literal from AsStringLiteral.
                        result = result.Replace(argRaw, encoding);
                    }

                    break;
            }
        }
        return result;
    }

    private static string? TryExtractRawBindingNamedArgumentMarkup(string rawBindingValue, string argumentName)
    {
        var searchToken = argumentName + "=";
        var searchIndex = 0;

        while (searchIndex < rawBindingValue.Length)
        {
            var argumentIndex = rawBindingValue.IndexOf(searchToken, searchIndex, StringComparison.Ordinal);
            if (argumentIndex < 0)
            {
                return null;
            }

            var valueStart = argumentIndex + searchToken.Length;
            while (valueStart < rawBindingValue.Length && char.IsWhiteSpace(rawBindingValue[valueStart]))
            {
                valueStart++;
            }

            if (valueStart >= rawBindingValue.Length || rawBindingValue[valueStart] != '{')
            {
                searchIndex = valueStart;
                continue;
            }

            var depth = 0;
            for (var i = valueStart; i < rawBindingValue.Length; i++)
            {
                if (rawBindingValue[i] == '{')
                {
                    depth++;
                }
                else if (rawBindingValue[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return rawBindingValue.Substring(valueStart, i - valueStart + 1);
                    }
                }
            }

            return null;
        }

        return null;
    }

    private static bool TryNormalizeBindingTypeMarkupExtension(
        string rawMarkup,
        MarkupExtensionInfo markupInfo,
        BindingContext context,
        out string normalizedMarkup)
    {
        normalizedMarkup = string.Empty;

        string? rawTypeToken = TryExtractRawTypeMarkupToken(rawMarkup);
        if (markupInfo.NamedArguments.TryGetValue("Type", out var namedTypeToken) ||
            markupInfo.NamedArguments.TryGetValue("TypeName", out namedTypeToken))
        {
            rawTypeToken ??= namedTypeToken;
        }
        else if (markupInfo.PositionalArguments.Length > 0)
        {
            rawTypeToken ??= markupInfo.PositionalArguments[0];
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
        normalizedMarkup = "{x:Type " + displayName + "}";
        return true;
    }

    private static string? TryExtractRawTypeMarkupToken(string rawMarkup)
    {
        var trimmed = rawMarkup.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
        {
            return null;
        }

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (!inner.StartsWith("x:Type", StringComparison.Ordinal) &&
            !inner.StartsWith("Type", StringComparison.Ordinal))
        {
            return null;
        }

        var separatorIndex = inner.IndexOf(' ');
        if (separatorIndex < 0 || separatorIndex == inner.Length - 1)
        {
            return null;
        }

        var token = inner.Substring(separatorIndex + 1).Trim();
        if (token.StartsWith("Type=", StringComparison.Ordinal) ||
            token.StartsWith("TypeName=", StringComparison.Ordinal))
        {
            var equalsIndex = token.IndexOf('=');
            if (equalsIndex >= 0 && equalsIndex < token.Length - 1)
            {
                token = token.Substring(equalsIndex + 1).Trim();
            }
        }

        return token.Length == 0 ? null : token;
    }
}
