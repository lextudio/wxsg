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
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
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
                var __resolvedRt = ResolveRuntimeType(__plainTypeToken.Contains(':') ? __plainTypeToken.Substring(__plainTypeToken.IndexOf(':') + 1) : __plainTypeToken);
                if (__resolvedRt is not null)
                {
                    return "typeof(" + QualifyType(__resolvedRt.FullName) + ")";
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

        if (normalizedType == "string" || normalizedType == "System.String" || normalizedType == "object" || normalizedType == "System.Object")
        {
            return valueExpression;
        }

        if (literalValue.StartsWith("{StaticResource ", StringComparison.Ordinal) &&
            literalValue.EndsWith("}", StringComparison.Ordinal))
        {
            var resourceScopeExpression = string.IsNullOrWhiteSpace(scopeExpression)
                ? "global::System.Windows.Application.Current"
                : scopeExpression;
            return "(" + QualifyType(normalizedType) + ")__WXSG_ResolveStaticResource(" +
                   resourceScopeExpression + ", " + valueExpression + ")";
        }

        if (literalValue.StartsWith("{x:Static ", StringComparison.Ordinal) &&
            literalValue.EndsWith("}", StringComparison.Ordinal))
        {
            return "(" + QualifyType(normalizedType) + ")__WXSG_ResolveXStatic(" + valueExpression + ")";
        }

        if (normalizedType == "Type" || normalizedType == "System.Type")
        {
            if (TryUnquote(valueExpression, out var __valueLiteral))
            {
                var __rt = ResolveRuntimeType(__valueLiteral.Trim());
                if (__rt is not null)
                {
                    return "typeof(" + QualifyType(__rt.FullName) + ")";
                }
            }

            return "__WXSG_ResolveTypeToken(" + valueExpression + ")";
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
                : ConvertViaTypeConverter(normalizedType, valueExpression);
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

        // ImageSource with a relative pack URI like "/Assembly;component/Path" or
        // "Assembly;component/Path": ConvertFromInvariantString cannot resolve these
        // without a base URI context.  Emit a BitmapImage with an absolute pack URI.
        if (normalizedType is "System.Windows.Media.ImageSource" or "Windows.Media.ImageSource" or "ImageSource")
        {
            var uriCandidate = literalValue.TrimStart('/');
            if (uriCandidate.IndexOf(";component/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var absUri = "pack://application:,,," + (literalValue.StartsWith("/", StringComparison.Ordinal) ? literalValue : "/" + literalValue);
                return "new global::System.Windows.Media.Imaging.BitmapImage(new global::System.Uri(" + EscapeStringLiteral(absUri) + ", global::System.UriKind.Absolute))";
            }
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
