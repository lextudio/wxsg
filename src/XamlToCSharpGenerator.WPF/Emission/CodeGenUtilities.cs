using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.WPF.Emission;

internal static class CodeGenUtilities
{
    internal static readonly MarkupExpressionParser MarkupParser = new();
    internal static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    };

    internal static string QualifyType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return "object";
        }

        var trimmed = typeName.Trim();

        // Handle nullable type suffix (e.g. "string?", "object?", "System.Uri?").
        // Must be done before the switch so that "string?" is not treated as an unknown
        // type name and incorrectly qualified as "global::string?" (invalid C#).
        if (trimmed.EndsWith("?", StringComparison.Ordinal))
        {
            var inner = trimmed.Substring(0, trimmed.Length - 1);
            return QualifyType(inner) + "?";
        }

        switch (trimmed)
        {
            case "bool":
            case "byte":
            case "sbyte":
            case "short":
            case "ushort":
            case "int":
            case "uint":
            case "long":
            case "ulong":
            case "float":
            case "double":
            case "decimal":
            case "char":
            case "string":
            case "object":
            case "void":
                return trimmed;
        }

        return trimmed.StartsWith("global::", StringComparison.Ordinal)
            ? trimmed
            : "global::" + trimmed;
    }

    internal static string EscapeIdentifier(string identifier)
    {
        return CSharpKeywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    internal static string EscapeStringLiteral(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t") + "\"";
    }

    internal static string BuildStringArrayExpression(string[] items)
    {
        if (items.Length == 0)
        {
            return "new string[0]";
        }

        var parts = new string[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            parts[i] = EscapeStringLiteral(items[i]);
        }

        return "new string[] { " + string.Join(", ", parts) + " }";
    }

    internal static bool TryUnquote(string expression, out string literal)
    {
        literal = expression;
        var trimmed = expression.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[trimmed.Length - 1] != '"')
        {
            return false;
        }

        var inner = trimmed.Substring(1, trimmed.Length - 2);
        literal = inner.Replace("\\\"", "\"").Replace("\\\\", "\\");
        return true;
    }

    internal static bool TryBuildXStaticDirectMemberAccessExpression(string xStaticToken, out string expression)
    {
        expression = string.Empty;

        if (!MarkupParser.TryParseMarkupExtension(xStaticToken, out var markupInfo) ||
            XamlMarkupExtensionNameSemantics.Classify(markupInfo.Name) != XamlMarkupExtensionKind.Static)
        {
            return false;
        }

        string? memberToken = null;
        if (markupInfo.NamedArguments.TryGetValue("Member", out var namedMember) ||
            markupInfo.NamedArguments.TryGetValue("MemberName", out namedMember))
        {
            memberToken = namedMember;
        }
        else if (markupInfo.PositionalArguments.Length > 0)
        {
            memberToken = markupInfo.PositionalArguments[0];
        }

        if (string.IsNullOrWhiteSpace(memberToken))
        {
            return false;
        }

        memberToken = XamlQuotedValueSemantics.TrimAndUnquote(memberToken).Trim();
        if (!TrySplitQualifiedXStaticMemberToken(memberToken, out var clrNamespace, out var typeName, out var memberName))
        {
            return false;
        }

        if (!TryBuildQualifiedTypeReference(clrNamespace, typeName, out var qualifiedTypeName) ||
            !TryEscapeSimpleIdentifier(memberName, out var escapedMemberName))
        {
            return false;
        }

        expression = qualifiedTypeName + "." + escapedMemberName;
        return true;
    }

    private static bool TrySplitQualifiedXStaticMemberToken(
        string memberToken,
        out string clrNamespace,
        out string typeName,
        out string memberName)
    {
        clrNamespace = string.Empty;
        typeName = string.Empty;
        memberName = string.Empty;

        const string clrNamespacePrefix = "clr-namespace:";
        if (!memberToken.StartsWith(clrNamespacePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var payload = memberToken.Substring(clrNamespacePrefix.Length).Trim();
        var memberSeparator = payload.LastIndexOf(':');
        if (memberSeparator <= 0 || memberSeparator >= payload.Length - 1)
        {
            return false;
        }

        var namespaceSegment = payload.Substring(0, memberSeparator).Trim();
        if (namespaceSegment.StartsWith("xmlns:", StringComparison.Ordinal))
        {
            return false;
        }

        var assemblySeparator = namespaceSegment.IndexOf(";assembly=", StringComparison.Ordinal);
        if (assemblySeparator >= 0)
        {
            // Assembly-qualified tokens often come from XML namespace mappings that
            // resolve via XmlnsDefinitionAttribute and can map to a different CLR
            // namespace than the literal token suggests. Keep runtime resolution for
            // these to avoid emitting invalid direct references.
            return false;
        }

        if (string.IsNullOrWhiteSpace(namespaceSegment))
        {
            return false;
        }

        var typeAndMember = payload.Substring(memberSeparator + 1).Trim();
        var typeMemberSeparator = typeAndMember.LastIndexOf('.');
        if (typeMemberSeparator <= 0 || typeMemberSeparator >= typeAndMember.Length - 1)
        {
            return false;
        }

        var rawTypeName = typeAndMember.Substring(0, typeMemberSeparator).Trim();
        var rawMemberName = typeAndMember.Substring(typeMemberSeparator + 1).Trim();
        if (string.IsNullOrWhiteSpace(rawTypeName) || string.IsNullOrWhiteSpace(rawMemberName))
        {
            return false;
        }

        // Keep runtime resolver behavior for short owner type names because XML
        // namespace mappings can fan out to multiple CLR namespaces and runtime
        // logic probes child namespaces by short type name.
        if (rawTypeName.IndexOf('.') < 0)
        {
            return false;
        }

        clrNamespace = namespaceSegment;
        typeName = rawTypeName.Replace('+', '.');
        memberName = rawMemberName;
        return true;
    }

    private static bool TryBuildQualifiedTypeReference(string clrNamespace, string typeName, out string qualifiedTypeName)
    {
        qualifiedTypeName = string.Empty;

        if (!TryEscapeIdentifierPath(clrNamespace, out var escapedNamespace) ||
            !TryEscapeIdentifierPath(typeName, out var escapedTypeName))
        {
            return false;
        }

        qualifiedTypeName = "global::" + escapedNamespace + "." + escapedTypeName;
        return true;
    }

    private static bool TryEscapeIdentifierPath(string path, out string escapedPath)
    {
        escapedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var segments = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var escapedSegments = new string[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            if (!TryEscapeSimpleIdentifier(segments[i], out escapedSegments[i]))
            {
                return false;
            }
        }

        escapedPath = string.Join(".", escapedSegments);
        return true;
    }

    private static bool TryEscapeSimpleIdentifier(string identifier, out string escapedIdentifier)
    {
        escapedIdentifier = string.Empty;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        var trimmed = identifier.Trim();
        if (!IsValidCSharpIdentifier(trimmed))
        {
            return false;
        }

        escapedIdentifier = EscapeIdentifier(trimmed);
        return true;
    }

    private static bool IsValidCSharpIdentifier(string identifier)
    {
        if (identifier.Length == 0)
        {
            return false;
        }

        var first = identifier[0];
        if (!(first == '_' || char.IsLetter(first)))
        {
            return false;
        }

        for (var i = 1; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (!(c == '_' || char.IsLetterOrDigit(c)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decodes the unknown-markup-extension encoding written by
    /// <c>WpfSemanticBinder.TryBuildUnknownMarkupExtensionEncoding</c>.
    /// </summary>
    internal static bool TryParseUnknownMarkupExtensionEncoding(
        string literalValue,
        out UnknownMarkupExtensionData result)
    {
        result = default;
        const string marker = "\x1ewxsg-ume\x1f";
        if (!literalValue.StartsWith(marker, StringComparison.Ordinal))
        {
            return false;
        }

        var fields = literalValue.Split('\x1f');
        // fields[0] = "\x1ewxsg-ume", fields[1] = nsUri, fields[2] = localName, fields[3..] = args
        if (fields.Length < 3)
        {
            return false;
        }

        var nsUri = fields[1];
        var localName = fields[2];
        var positionalArgs = new List<string>();
        var namedArgKeys = new List<string>();
        var namedArgValues = new List<string>();

        for (var i = 3; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.StartsWith("p:", StringComparison.Ordinal))
            {
                positionalArgs.Add(field.Substring(2));
            }
            else if (field.StartsWith("t:", StringComparison.Ordinal))
            {
                // Type-referenced positional arg: keep the "t:" prefix so the runtime
                // helper can resolve it to a System.Type instead of passing as string.
                positionalArgs.Add(field);
            }
            else if (field.StartsWith("n:", StringComparison.Ordinal))
            {
                var eqIdx = field.IndexOf('=', 2);
                if (eqIdx > 2)
                {
                    namedArgKeys.Add(field.Substring(2, eqIdx - 2));
                    namedArgValues.Add(field.Substring(eqIdx + 1));
                }
            }
        }

        result = new UnknownMarkupExtensionData(
            nsUri,
            localName,
            positionalArgs.ToArray(),
            namedArgKeys.ToArray(),
            namedArgValues.ToArray());
        return true;
    }

    internal static string ConvertLiteralExpression(string valueExpression, string? clrPropertyTypeName, string? scopeExpression = null)
    {
        if (string.IsNullOrWhiteSpace(clrPropertyTypeName))
        {
            return valueExpression;
        }

        var normalizedType = clrPropertyTypeName.Replace("global::", string.Empty).Trim();
        if (normalizedType.Length == 0)
        {
            return valueExpression;
        }

        if (!TryUnquote(valueExpression, out var literalValue))
        {
            return valueExpression;
        }

        if (MarkupParser.TryParseMarkupExtension(literalValue, out var markupInfo) &&
            XamlMarkupExtensionNameSemantics.Classify(markupInfo.Name) == XamlMarkupExtensionKind.Null)
        {
            return "null";
        }

        if (MarkupParser.TryParseMarkupExtension(literalValue, out var typeMarkupInfo) &&
            XamlMarkupExtensionNameSemantics.Classify(typeMarkupInfo.Name) == XamlMarkupExtensionKind.Type)
        {
            string? typeToken = null;
            if (typeMarkupInfo.NamedArguments.TryGetValue("Type", out var namedTypeToken) ||
                typeMarkupInfo.NamedArguments.TryGetValue("TypeName", out namedTypeToken))
            {
                typeToken = namedTypeToken;
            }
            else if (typeMarkupInfo.PositionalArguments.Length > 0)
            {
                typeToken = typeMarkupInfo.PositionalArguments[0];
            }

            if (!string.IsNullOrWhiteSpace(typeToken))
            {
                var __plainTypeToken = XamlQuotedValueSemantics.TrimAndUnquote(typeToken).Trim();
                var __directTypeToken = __plainTypeToken.Contains(':')
                    ? __plainTypeToken.Substring(__plainTypeToken.IndexOf(':') + 1)
                    : __plainTypeToken;
                if (__directTypeToken.Contains('.'))
                {
                    var __resolvedRt = ResolveRuntimeType(__directTypeToken);
                    if (__resolvedRt is not null)
                    {
                        return "typeof(" + QualifyType(__resolvedRt.FullName) + ")";
                    }
                }

                return "__WXSG_ResolveTypeToken(" + EscapeStringLiteral(__plainTypeToken) + ")";
            }
        }

        // Check for a WXSG-encoded unknown markup extension written by the binder.
        // The encoding starts with '\x1e' (Record Separator) followed by "wxsg-ume".
        if (literalValue.Length > 0 && literalValue[0] == '\x1e' &&
            TryParseUnknownMarkupExtensionEncoding(literalValue, out var ume))
        {
            var callExpr =
                "__WXSG_EvaluateUnknownMarkupExtension(" +
                EscapeStringLiteral(ume.NsUri) + ", " +
                EscapeStringLiteral(ume.LocalName) + ", " +
                BuildStringArrayExpression(ume.PositionalArgs) + ", " +
                BuildStringArrayExpression(ume.NamedArgKeys) + ", " +
                BuildStringArrayExpression(ume.NamedArgValues) + ")";

            if (normalizedType == "string" || normalizedType == "System.String")
                return "(string)" + callExpr;
            if (normalizedType == "object" || normalizedType == "System.Object")
                return callExpr;
            return "(" + QualifyType(normalizedType) + ")" + callExpr;
        }

        // {DynamicResource key} — emit a DynamicResourceExtension instance so template
        // and factory contexts preserve the markup extension semantics instead of
        // attempting to convert the literal string (which would call e.g. Brush.Parse
        // and throw when the token contains markup).  Handle nested x:Static keys.
        if (literalValue.StartsWith("{DynamicResource ", StringComparison.Ordinal) &&
            literalValue.EndsWith("}", StringComparison.Ordinal))
        {
            const string dynOpen = "{DynamicResource ";
            var dynKey = literalValue.Substring(dynOpen.Length, literalValue.Length - dynOpen.Length - 1).Trim();
            if (dynKey.StartsWith("{x:Static ", StringComparison.Ordinal) && dynKey.EndsWith("}", StringComparison.Ordinal))
            {
                if (TryBuildXStaticDirectMemberAccessExpression(dynKey, out var directMemberAccess))
                {
                    return "new global::System.Windows.DynamicResourceExtension(" + directMemberAccess + ")";
                }

                return "new global::System.Windows.DynamicResourceExtension(__WXSG_ResolveXStatic(" + EscapeStringLiteral(dynKey) + "))";
            }

            return "new global::System.Windows.DynamicResourceExtension(" + EscapeStringLiteral(dynKey) + ")";
        }

        if (literalValue.StartsWith("{StaticResource ", StringComparison.Ordinal) &&
            literalValue.EndsWith("}", StringComparison.Ordinal))
        {
            var resourceScopeExpression = string.IsNullOrWhiteSpace(scopeExpression)
                ? "global::System.Windows.Application.Current"
                : scopeExpression;
            var callExpr = "__WXSG_ResolveStaticResource(" + resourceScopeExpression + ", " + valueExpression + ")";
            return normalizedType == "object" || normalizedType == "System.Object"
                ? callExpr
                : "(" + QualifyType(normalizedType) + ")" + callExpr;
        }

        if (literalValue.StartsWith("{x:Static ", StringComparison.Ordinal) &&
            literalValue.EndsWith("}", StringComparison.Ordinal))
        {
            if (TryBuildXStaticDirectMemberAccessExpression(literalValue, out var directMemberAccess))
            {
                return normalizedType == "object" || normalizedType == "System.Object"
                    ? directMemberAccess
                    : "(" + QualifyType(normalizedType) + ")" + directMemberAccess;
            }

            var callExpr = "__WXSG_ResolveXStatic(" + valueExpression + ")";
            return normalizedType == "object" || normalizedType == "System.Object"
                ? callExpr
                : "(" + QualifyType(normalizedType) + ")" + callExpr;
        }

        if ((normalizedType == "RelativeSource" || normalizedType == "System.Windows.Data.RelativeSource") &&
            MarkupParser.TryParseMarkupExtension(literalValue, out var relativeSourceInfo) &&
            XamlMarkupExtensionNameSemantics.Classify(relativeSourceInfo.Name) == XamlMarkupExtensionKind.RelativeSource)
        {
            string? modeToken = null;
            if (relativeSourceInfo.NamedArguments.TryGetValue("Mode", out var namedMode) ||
                relativeSourceInfo.NamedArguments.TryGetValue("RelativeSourceMode", out namedMode))
            {
                modeToken = XamlQuotedValueSemantics.TrimAndUnquote(namedMode).Trim();
            }
            else if (relativeSourceInfo.PositionalArguments.Length > 0)
            {
                modeToken = XamlQuotedValueSemantics.TrimAndUnquote(relativeSourceInfo.PositionalArguments[0]).Trim();
            }

            if (!string.IsNullOrWhiteSpace(modeToken))
            {
                var qualifiedMode = "global::System.Windows.Data.RelativeSourceMode." + modeToken;

                if (string.Equals(modeToken, "FindAncestor", StringComparison.OrdinalIgnoreCase))
                {
                    var ancestorTypeToken = string.Empty;
                    if (relativeSourceInfo.NamedArguments.TryGetValue("AncestorType", out var ancestorTypeRaw))
                    {
                        ancestorTypeToken = XamlQuotedValueSemantics.TrimAndUnquote(ancestorTypeRaw).Trim();
                    }

                    var ancestorLevelLiteral = "1";
                    if (relativeSourceInfo.NamedArguments.TryGetValue("AncestorLevel", out var ancestorLevelRaw))
                    {
                        var parsedLevel = XamlQuotedValueSemantics.TrimAndUnquote(ancestorLevelRaw).Trim();
                        if (int.TryParse(parsedLevel, NumberStyles.Integer, CultureInfo.InvariantCulture, out var levelValue) && levelValue > 0)
                        {
                            ancestorLevelLiteral = levelValue.ToString(CultureInfo.InvariantCulture);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(ancestorTypeToken))
                    {
                        var resolvedAncestorType = ResolveRuntimeType(
                            ancestorTypeToken.Contains(':')
                                ? ancestorTypeToken.Substring(ancestorTypeToken.IndexOf(':') + 1)
                                : ancestorTypeToken);
                        var ancestorTypeExpr = resolvedAncestorType is not null
                            ? "typeof(" + QualifyType(resolvedAncestorType.FullName) + ")"
                            : "__WXSG_ResolveTypeToken(" + EscapeStringLiteral(ancestorTypeToken) + ")";

                        return "new global::System.Windows.Data.RelativeSource(" +
                               qualifiedMode + ", " + ancestorTypeExpr + ", " + ancestorLevelLiteral + ")";
                    }
                }

                return "new global::System.Windows.Data.RelativeSource(" + qualifiedMode + ")";
            }
        }

        if (normalizedType == "string" || normalizedType == "System.String" || normalizedType == "object" || normalizedType == "System.Object")
        {
            return valueExpression;
        }

        if (normalizedType == "Type" || normalizedType == "System.Type")
        {
            if (TryUnquote(valueExpression, out var __valueLiteral))
            {
                var __literal = __valueLiteral.Trim();

                // Only use the compile-time fast path for fully qualified names. Short
                // unqualified XAML names like "Path" must go through the runtime resolver
                // (__WXSG_ResolveTypeToken) which is XAML-namespace-aware. The compile-time
                // ResolveRuntimeType cannot see WPF assemblies inside a Roslyn host and
                // can match BCL types by simple name (e.g. "Path" → System.IO.Path),
                // breaking Style.set_TargetType at runtime (lextudio/wxsg#12).
                if (__literal.IndexOf('.') >= 0)
                {
                    var __rt = ResolveRuntimeType(__literal);
                    if (__rt is not null)
                    {
                        return "typeof(" + QualifyType(__rt.FullName) + ")";
                    }
                }
            }

            return "__WXSG_ResolveTypeToken(" + valueExpression + ")";
        }

        var runtimeType = ResolveRuntimeType(normalizedType);
           if ((runtimeType is not null && IsWpfDependencyObjectLike(runtimeType) ||
               IsWpfDependencyObjectTypeName(normalizedType)) &&
            !string.IsNullOrWhiteSpace(scopeExpression) &&
            literalValue.Length > 0 &&
            literalValue.IndexOfAny(new[] { '{', '}', '.', '/', '\\', ':', ',', ' ' }) < 0)
        {
            var qualifiedTypeName = QualifyType(normalizedType);
            return "(" + qualifiedTypeName + ")__WXSG_ResolveElementReference(" +
                   scopeExpression + ", " + valueExpression + ", typeof(" + qualifiedTypeName + "))";
        }

        if (normalizedType == "bool" || normalizedType == "System.Boolean")
        {
            return bool.TryParse(literalValue, out var boolValue)
                ? (boolValue ? "true" : "false")
                : ConvertViaTypeConverter(normalizedType, valueExpression);
        }

        if (normalizedType == "int" || normalizedType == "System.Int32")
        {
            return int.TryParse(literalValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                ? intValue.ToString(CultureInfo.InvariantCulture)
                : ConvertViaTypeConverter(normalizedType, valueExpression);
        }

        if (normalizedType == "long" || normalizedType == "System.Int64")
        {
            return long.TryParse(literalValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue)
                ? longValue.ToString(CultureInfo.InvariantCulture) + "L"
                : ConvertViaTypeConverter(normalizedType, valueExpression);
        }

        if (normalizedType == "double" || normalizedType == "System.Double")
        {
            return double.TryParse(literalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
                ? BuildDoubleLiteralExpression(doubleValue)
                : "__WXSG_ParseWpfDouble(" + valueExpression + ")";
        }

        if (normalizedType == "float" || normalizedType == "System.Single")
        {
            return float.TryParse(literalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue)
                ? floatValue.ToString("R", CultureInfo.InvariantCulture) + "f"
                : ConvertViaTypeConverter(normalizedType, valueExpression);
        }

        if (normalizedType == "decimal" || normalizedType == "System.Decimal")
        {
            return decimal.TryParse(literalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue)
                ? decimalValue.ToString(CultureInfo.InvariantCulture) + "m"
                : ConvertViaTypeConverter(normalizedType, valueExpression);
        }

        if (normalizedType.EndsWith("?", StringComparison.Ordinal))
        {
            var innerType = normalizedType.Substring(0, normalizedType.Length - 1);
            if (literalValue.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                return "null";
            }

            return ConvertLiteralExpression(valueExpression, innerType);
        }

        // ImageSource: handle common resource URI forms that ConvertFromInvariantString
        // cannot resolve without a base URI context.  Prefer emitting an absolute
        // pack URI (pack://application:,,,/Assembly;component/Path) constructed at
        // runtime using the owning object's assembly when possible.
        if (normalizedType is "System.Windows.Media.ImageSource" or "Windows.Media.ImageSource" or "ImageSource")
        {
            // Unquote the original token to inspect the raw path.
            var __unq = XamlQuotedValueSemantics.TrimAndUnquote(valueExpression);

            // If the token looks like a relative project/resource path (contains
            // slashes but no scheme or component token), emit a BitmapImage using
            // a pack:// URI built from the runtime assembly name of the scope.
            if (!string.IsNullOrWhiteSpace(__unq) &&
                (__unq.Contains('/') || __unq.Contains('\\')) &&
                __unq.IndexOf(':') < 0 &&
                __unq.IndexOf(";component/", StringComparison.OrdinalIgnoreCase) < 0 &&
                !__unq.StartsWith("pack://", StringComparison.OrdinalIgnoreCase))
            {
                var normalizedPath = __unq.Replace('\\', '/').TrimStart('/');
                // XAML image paths may be expressed relative to the .xaml file (for example
                // "../Resources/foo.png"). WPF pack URIs address project resources from the
                // assembly root; collapse leading relative navigation segments.
                while (normalizedPath.StartsWith("../", StringComparison.Ordinal))
                {
                    normalizedPath = normalizedPath.Substring(3);
                }

                while (normalizedPath.StartsWith("./", StringComparison.Ordinal))
                {
                    normalizedPath = normalizedPath.Substring(2);
                }

                // WPF resource keys in .g.resources are normalized to lower-case paths
                // (for example "resources/foo.png"). Keep generated pack URIs aligned.
                normalizedPath = normalizedPath.ToLowerInvariant();

                if (!string.IsNullOrWhiteSpace(scopeExpression))
                {
                    return "__WXSG_LoadImageSource(" + scopeExpression + ", " + EscapeStringLiteral(normalizedPath) + ")";
                }
                else
                {
                    return "__WXSG_LoadImageSource(null, " + EscapeStringLiteral(normalizedPath) + ")";
                }
            }

            // Existing behavior: if the literal already contains an Assembly;component
            // token, convert to an absolute pack URI directly.
            var uriCandidate = literalValue.TrimStart('/');
            if (uriCandidate.IndexOf(";component/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var absUri = "pack://application:,,," + (literalValue.StartsWith("/", StringComparison.Ordinal) ? literalValue : "/" + literalValue);
                return "new global::System.Windows.Media.Imaging.BitmapImage(new global::System.Uri(" + EscapeStringLiteral(absUri) + ", global::System.UriKind.Absolute))";
            }
        }

        // ICommand: CommandConverter.ConvertFrom requires ITypeDescriptorContext to resolve
        // short names (e.g. "New" -> ApplicationCommands.New) or prefix-qualified static refs
        // (e.g. "Default:MainWindow.CloseAllCommand"). Use the runtime helper instead.
        if (normalizedType is "System.Windows.Input.ICommand" or "Windows.Input.ICommand" or "ICommand")
        {
            return "__WXSG_ResolveWpfCommand(" + EscapeStringLiteral(literalValue) + ")";
        }

        return ConvertViaTypeConverter(normalizedType, valueExpression);
    }

    internal static string ConvertViaTypeConverter(string normalizedType, string valueExpression)
    {
        var qualifiedType = QualifyType(normalizedType);
        return "(" + qualifiedType + ")global::System.ComponentModel.TypeDescriptor.GetConverter(typeof(" +
               qualifiedType + ")).ConvertFromInvariantString(" + valueExpression + ")";
    }

    internal static string BuildDoubleLiteralExpression(double value)
    {
        if (double.IsNaN(value))
        {
            return "global::System.Double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "D";
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

        return Type.GetType(normalizedName, throwOnError: false);
    }

    private static bool IsWpfDependencyObjectLike(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var fullName = current.FullName;
            if (string.Equals(fullName, "System.Windows.DependencyObject", StringComparison.Ordinal) ||
                string.Equals(fullName, "System.Windows.UIElement", StringComparison.Ordinal) ||
                string.Equals(fullName, "System.Windows.FrameworkElement", StringComparison.Ordinal) ||
                string.Equals(fullName, "System.Windows.FrameworkContentElement", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWpfDependencyObjectTypeName(string normalizedType)
    {
        return normalizedType == "System.Windows.DependencyObject" ||
               normalizedType == "System.Windows.UIElement" ||
               normalizedType == "System.Windows.FrameworkElement" ||
               normalizedType == "System.Windows.FrameworkContentElement" ||
               normalizedType == "DependencyObject" ||
               normalizedType == "UIElement" ||
               normalizedType == "FrameworkElement" ||
               normalizedType == "FrameworkContentElement";
    }

    internal static string? ResolveRuntimePropertyTypeName(string? ownerTypeName, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(ownerTypeName) || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var runtimeType = ResolveRuntimeType(ownerTypeName);
        if (runtimeType is null)
        {
            return null;
        }

        var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (var current in EnumerateRuntimeMemberLookupTypes(runtimeType))
        {
            var property = current.GetProperty(propertyName, flags);
            if (property is not null)
            {
                return property.PropertyType.FullName;
            }
        }

        return null;
    }

    internal static IEnumerable<Type> EnumerateRuntimeMemberLookupTypes(Type type)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    internal static string? ResolveFrameworkElementFactoryPropertyTypeName(
        string? ownerTypeName,
        string propertyName,
        string? fallbackTypeName)
    {
        var runtimeTypeName = ResolveRuntimePropertyTypeName(ownerTypeName, propertyName);
        if (!string.IsNullOrWhiteSpace(runtimeTypeName))
        {
            return runtimeTypeName;
        }

        return fallbackTypeName;
    }

    internal static bool IsBindingBaseTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var normalizedTypeName = typeName.Replace("global::", string.Empty);
        return normalizedTypeName.Equals("System.Windows.Data.BindingBase", StringComparison.Ordinal) ||
               normalizedTypeName.Equals("System.Windows.Data.Binding", StringComparison.Ordinal) ||
               normalizedTypeName.Equals("System.Windows.Data.MultiBinding", StringComparison.Ordinal) ||
               normalizedTypeName.Equals("System.Windows.Data.PriorityBinding", StringComparison.Ordinal);
    }

    internal static bool IsMarkupExtensionTypeName(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        return typeName.EndsWith("Extension", StringComparison.Ordinal);
    }

    internal static string? GetCollectionElementTypeName(string? collectionTypeName)
    {
        if (string.IsNullOrWhiteSpace(collectionTypeName))
        {
            return null;
        }

        var start = collectionTypeName.IndexOf('<');
        var end = collectionTypeName.LastIndexOf('>');

        if (start < 0 || end <= start)
        {
            return null;
        }

        return collectionTypeName.Substring(start + 1, end - start - 1).Trim();
    }

    internal readonly struct UnknownMarkupExtensionData
    {
        public string NsUri { get; }
        public string LocalName { get; }
        public string[] PositionalArgs { get; }
        public string[] NamedArgKeys { get; }
        public string[] NamedArgValues { get; }

        public UnknownMarkupExtensionData(
            string nsUri,
            string localName,
            string[] positionalArgs,
            string[] namedArgKeys,
            string[] namedArgValues)
        {
            NsUri = nsUri;
            LocalName = localName;
            PositionalArgs = positionalArgs;
            NamedArgKeys = namedArgKeys;
            NamedArgValues = namedArgValues;
        }
    }
}
