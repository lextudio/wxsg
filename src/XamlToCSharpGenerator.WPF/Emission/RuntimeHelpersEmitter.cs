using System.Text;

namespace XamlToCSharpGenerator.WPF.Emission;

internal static class RuntimeHelpersEmitter
{
    public static void Emit(GraphEmitter emitter, string? docClassFullName)
    {
        EmitTypeTokenHelper(emitter);
        EmitDependencyPropertyHelper(emitter);
        EmitRoutedEventHelper(emitter);
        EmitStaticResourceHelper(emitter);
        EmitXStaticHelper(emitter, docClassFullName);
        EmitSetterValueHelper(emitter);
        EmitUnknownMarkupExtensionHelper(emitter);
        EmitTrySetBindingHelper(emitter);
    }

    private static void EmitTypeTokenHelper(GraphEmitter emitter)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        sb.AppendLine();
        sb.AppendLine(i + "private static global::System.Type __WXSG_ResolveTypeToken(string __token)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    if (string.IsNullOrWhiteSpace(__token))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Empty type token.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __trimmed = __token.Trim();");
        sb.AppendLine(i + "    var __colonIndex = __trimmed.IndexOf(':');");
        sb.AppendLine(i + "    if (__colonIndex >= 0 && __colonIndex < __trimmed.Length - 1)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __trimmed = __trimmed.Substring(__colonIndex + 1);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __direct = global::System.Type.GetType(__trimmed, throwOnError: false);");
        sb.AppendLine(i + "    if (__direct is not null)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        return __direct;");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __knownNamespaces = new[]");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        \"System.Windows\",");
        sb.AppendLine(i + "        \"System.Windows.Automation\",");
        sb.AppendLine(i + "        \"System.Windows.Controls\",");
        sb.AppendLine(i + "        \"System.Windows.Controls.Primitives\",");
        sb.AppendLine(i + "        \"System.Windows.Documents\",");
        sb.AppendLine(i + "        \"System.Windows.Input\",");
        sb.AppendLine(i + "        \"System.Windows.Media\",");
        sb.AppendLine(i + "        \"System.Windows.Media.Animation\",");
        sb.AppendLine(i + "        \"System.Windows.Navigation\",");
        sb.AppendLine(i + "        \"System.Windows.Shapes\"");
        sb.AppendLine(i + "    };");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __assemblies = global::System.AppDomain.CurrentDomain.GetAssemblies();");
        sb.AppendLine(i + "    foreach (var __assembly in __assemblies)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __byName = __assembly.GetType(__trimmed, throwOnError: false);");
        sb.AppendLine(i + "        if (__byName is not null)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            return __byName;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i);
        sb.AppendLine(i + "        foreach (var __ns in __knownNamespaces)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            var __candidate = __assembly.GetType(__ns + \".\" + __trimmed, throwOnError: false);");
        sb.AppendLine(i + "            if (__candidate is not null)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                return __candidate;");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    foreach (var __assembly in __assemblies)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        global::System.Type[] __types;");
        sb.AppendLine(i + "        try");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            __types = __assembly.GetTypes();");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "        catch (global::System.Reflection.ReflectionTypeLoadException __rtl)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            __types = __rtl.Types;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i);
        sb.AppendLine(i + "        foreach (var __candidate in __types)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            if (__candidate is null)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                continue;");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i);
        sb.AppendLine(i + "            if (string.Equals(__candidate.Name, __trimmed, global::System.StringComparison.Ordinal))");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                return __candidate;");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    throw new global::System.InvalidOperationException(\"Unable to resolve type token '\" + __token + \"'.\");");
        sb.AppendLine(i + "}");
        sb.AppendLine();
    }

    private static void EmitDependencyPropertyHelper(GraphEmitter emitter)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        sb.AppendLine(i + "private static global::System.Windows.DependencyProperty __WXSG_ResolveSetterDependencyProperty(string __token, global::System.Type __styleTargetType)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    if (string.IsNullOrWhiteSpace(__token))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Empty dependency property token.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __trimmed = __token.Trim();");
        sb.AppendLine(i + "    var __lastDot = __trimmed.LastIndexOf('.');");
        sb.AppendLine(i + "    string __ownerToken = null;");
        sb.AppendLine(i + "    string __propertyToken;");
        sb.AppendLine(i + "    if (__lastDot > 0)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __ownerToken = __trimmed.Substring(0, __lastDot);");
        sb.AppendLine(i + "        __propertyToken = __trimmed.Substring(__lastDot + 1);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    else");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __propertyToken = __trimmed;");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __ownerPrefixSeparator = __ownerToken is null ? -1 : __ownerToken.IndexOf(':');");
        sb.AppendLine(i + "    if (__ownerPrefixSeparator >= 0 && __ownerPrefixSeparator < __ownerToken.Length - 1)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __ownerToken = __ownerToken.Substring(__ownerPrefixSeparator + 1);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __propertyPrefixSeparator = __propertyToken.IndexOf(':');");
        sb.AppendLine(i + "    if (__propertyPrefixSeparator >= 0 && __propertyPrefixSeparator < __propertyToken.Length - 1)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __propertyToken = __propertyToken.Substring(__propertyPrefixSeparator + 1);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __fieldName = __propertyToken + \"Property\";");
        sb.AppendLine(i);
        sb.AppendLine(i + "    if (!string.IsNullOrWhiteSpace(__ownerToken))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __ownerType = __WXSG_ResolveTypeToken(__ownerToken);");
        sb.AppendLine(i + "        var __ownerField = __ownerType.GetField(__fieldName, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy);");
        sb.AppendLine(i + "        if (__ownerField?.GetValue(null) is global::System.Windows.DependencyProperty __ownerDp)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            return __ownerDp;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    for (var __type = __styleTargetType; __type is not null; __type = __type.BaseType)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __field = __type.GetField(__fieldName, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy);");
        sb.AppendLine(i + "        if (__field?.GetValue(null) is global::System.Windows.DependencyProperty __dp)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            return __dp;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    foreach (var __assembly in global::System.AppDomain.CurrentDomain.GetAssemblies())");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        global::System.Type[] __types;");
        sb.AppendLine(i + "        try");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            __types = __assembly.GetTypes();");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "        catch (global::System.Reflection.ReflectionTypeLoadException __rtl)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            __types = __rtl.Types;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i);
        sb.AppendLine(i + "        foreach (var __candidateType in __types)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            if (__candidateType is null)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                continue;");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i);
        sb.AppendLine(i + "            var __candidateField = __candidateType.GetField(__fieldName, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy);");
        sb.AppendLine(i + "            if (__candidateField?.GetValue(null) is global::System.Windows.DependencyProperty __candidateDp)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                return __candidateDp;");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    throw new global::System.InvalidOperationException(\"Unable to resolve dependency property token '\" + __token + \"'.\");");
        sb.AppendLine(i + "}");
        sb.AppendLine();
    }

    private static void EmitRoutedEventHelper(GraphEmitter emitter)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        sb.AppendLine(i + "private static global::System.Windows.RoutedEvent __WXSG_ResolveRoutedEvent(global::System.Type __styleTargetType, string __eventName)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    var __fieldName = __eventName + \"Event\";");
        sb.AppendLine(i + "    for (var __type = __styleTargetType; __type is not null; __type = __type.BaseType)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __field = __type.GetField(__fieldName, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy);");
        sb.AppendLine(i + "        if (__field?.GetValue(null) is global::System.Windows.RoutedEvent __re)");
        sb.AppendLine(i + "            return __re;");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    foreach (var __assembly in global::System.AppDomain.CurrentDomain.GetAssemblies())");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        global::System.Type[] __types;");
        sb.AppendLine(i + "        try { __types = __assembly.GetTypes(); }");
        sb.AppendLine(i + "        catch (global::System.Reflection.ReflectionTypeLoadException __rtl) { __types = __rtl.Types; }");
        sb.AppendLine(i + "        foreach (var __candidateType in __types)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            if (__candidateType is null) continue;");
        sb.AppendLine(i + "            var __candidateField = __candidateType.GetField(__fieldName, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy);");
        sb.AppendLine(i + "            if (__candidateField?.GetValue(null) is global::System.Windows.RoutedEvent __re)");
        sb.AppendLine(i + "                return __re;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    throw new global::System.InvalidOperationException(\"Unable to resolve routed event '\" + __eventName + \"'.\");");
        sb.AppendLine(i + "}");
        sb.AppendLine();
    }

    private static void EmitStaticResourceHelper(GraphEmitter emitter)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        sb.AppendLine(i + "[global::System.ThreadStatic]");
        sb.AppendLine(i + "private static object __WXSG_CurrentRootResourceScope;");
        sb.AppendLine();
        sb.AppendLine(i + "private static object __WXSG_ResolveStaticResource(object __scope, string __token)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    if (string.IsNullOrWhiteSpace(__token))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Empty StaticResource token.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __trimmed = __token.Trim();");
        sb.AppendLine(i + "    const string __open = \"{StaticResource \";");
        sb.AppendLine(i + "    if (!__trimmed.StartsWith(__open, global::System.StringComparison.Ordinal) || !__trimmed.EndsWith(\"}\", global::System.StringComparison.Ordinal))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Unsupported StaticResource token '\" + __token + \"'.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __inner = __trimmed.Substring(__open.Length, __trimmed.Length - __open.Length - 1).Trim();");
        sb.AppendLine(i + "    object __key = __inner;");
        sb.AppendLine(i + "    const string __xStaticOpen = \"{x:Static \";");
        sb.AppendLine(i + "    if (__inner.StartsWith(__xStaticOpen, global::System.StringComparison.Ordinal) && __inner.EndsWith(\"}\", global::System.StringComparison.Ordinal))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __memberToken = __inner.Substring(__xStaticOpen.Length, __inner.Length - __xStaticOpen.Length - 1).Trim();");
        sb.AppendLine(i + "        var __memberDot = __memberToken.LastIndexOf('.');");
        sb.AppendLine(i + "        if (__memberDot > 0 && __memberDot < __memberToken.Length - 1)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            var __ownerToken = __memberToken.Substring(0, __memberDot);");
        sb.AppendLine(i + "            var __memberName = __memberToken.Substring(__memberDot + 1);");
        sb.AppendLine(i + "            var __ownerType = __WXSG_ResolveTypeToken(__ownerToken);");
        sb.AppendLine(i + "            var __flags = global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy;");
        sb.AppendLine(i + "            var __property = __ownerType.GetProperty(__memberName, __flags);");
        sb.AppendLine(i + "            if (__property is not null)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                __key = __property.GetValue(null);");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "            else");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                var __field = __ownerType.GetField(__memberName, __flags);");
        sb.AppendLine(i + "                if (__field is not null)");
        sb.AppendLine(i + "                {");
        sb.AppendLine(i + "                    __key = __field.GetValue(null);");
        sb.AppendLine(i + "                }");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    object __resource = null;");
        sb.AppendLine(i + "    if (__scope is global::System.Windows.FrameworkElement __frameworkElement)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __resource = __frameworkElement.TryFindResource(__key);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    else if (__scope is global::System.Windows.FrameworkContentElement __contentElement)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __resource = __contentElement.TryFindResource(__key);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    if (__resource is null)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __rootScope = __WXSG_CurrentRootResourceScope;");
        sb.AppendLine(i + "        var __sameAsScope = global::System.Object.ReferenceEquals(__rootScope, __scope);");
        sb.AppendLine(i + "        if (!__sameAsScope && __rootScope is global::System.Windows.FrameworkElement)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            var __rootFrameworkElement = (global::System.Windows.FrameworkElement)__rootScope;");
        sb.AppendLine(i + "            __resource = __rootFrameworkElement.TryFindResource(__key);");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "        else if (!__sameAsScope && __rootScope is global::System.Windows.FrameworkContentElement)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            var __rootContentElement = (global::System.Windows.FrameworkContentElement)__rootScope;");
        sb.AppendLine(i + "            __resource = __rootContentElement.TryFindResource(__key);");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    if (__resource is null)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __app = global::System.Windows.Application.Current;");
        sb.AppendLine(i + "        __resource = __app?.TryFindResource(__key);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    return __resource;");
        sb.AppendLine(i + "}");
        sb.AppendLine();
    }

    private static void EmitXStaticHelper(GraphEmitter emitter, string? docClassFullName)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        sb.AppendLine(i + "private static object __WXSG_ResolveXStatic(string __token)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    if (string.IsNullOrWhiteSpace(__token))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Empty x:Static token.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __trimmed = __token.Trim();");
        sb.AppendLine(i + "    const string __open = \"{x:Static \";");
        sb.AppendLine(i + "    if (!__trimmed.StartsWith(__open, global::System.StringComparison.Ordinal) || !__trimmed.EndsWith(\"}\", global::System.StringComparison.Ordinal))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Unsupported x:Static token '\" + __token + \"'.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __memberToken = __trimmed.Substring(__open.Length, __trimmed.Length - __open.Length - 1).Trim();");
        sb.AppendLine(i + "    // Handle fully-qualified format: \"clr-namespace:XStaticCustomNsSample;assembly=XStaticCustomNsSample:Converters.CollectionsToComposite\"");
        sb.AppendLine(i + "    var __clrNsIdx = __memberToken.IndexOf(\"clr-namespace:\", global::System.StringComparison.Ordinal);");
        sb.AppendLine(i + "    if (__clrNsIdx >= 0)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        // Extract namespace and assembly info");
        sb.AppendLine(i + "        var __afterPrefix = __memberToken.Substring(__clrNsIdx + 14); // len(\"clr-namespace:\") = 14");
        sb.AppendLine(i + "        var __assemblyIdx = __afterPrefix.IndexOf(';');");
        sb.AppendLine(i + "        var __typeIdx = __afterPrefix.LastIndexOf(':');");
        sb.AppendLine(i + "        if (__typeIdx > __assemblyIdx && __assemblyIdx >= 0)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            // Extract CLR namespace from: \"XStaticCustomNsSample;assembly=...\"");
        sb.AppendLine(i + "            var __clrNamespace = __afterPrefix.Substring(0, __assemblyIdx).Trim();");
        sb.AppendLine(i + "            var __qualifiedTypeName = __afterPrefix.Substring(__typeIdx + 1).Trim();");
        sb.AppendLine(i + "            var __qualifiedMemberDot = __qualifiedTypeName.LastIndexOf('.');");
        sb.AppendLine(i + "            if (__qualifiedMemberDot > 0 && __qualifiedMemberDot < __qualifiedTypeName.Length - 1)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                var __qualifiedTypeNameShort = __qualifiedTypeName.Substring(0, __qualifiedMemberDot);");
        sb.AppendLine(i + "                var __qualifiedMemberName = __qualifiedTypeName.Substring(__qualifiedMemberDot + 1);");
        sb.AppendLine(i + "                // Try fully-qualified name first: namespace.typename");
        sb.AppendLine(i + "                var __fullyQualifiedTypeName = __clrNamespace + \".\" + __qualifiedTypeNameShort;");
        sb.AppendLine(i + "                var __qualifiedOwnerType = global::System.Type.GetType(__fullyQualifiedTypeName, throwOnError: false);");
        sb.AppendLine(i + "                if (__qualifiedOwnerType is null)");
        sb.AppendLine(i + "                {");
        sb.AppendLine(i + "                    // Extract assembly name from format: \"XStaticCustomNsSample;assembly=XStaticCustomNsSample:Converters.CollectionsToComposite\"");
        sb.AppendLine(i + "                    var __asmNameStart = __afterPrefix.IndexOf(\"assembly=\");");
        sb.AppendLine(i + "                    string __targetAsmName = null;");
        sb.AppendLine(i + "                    if (__asmNameStart >= 0)");
        sb.AppendLine(i + "                    {");
        sb.AppendLine(i + "                        __asmNameStart += 9; // len(\"assembly=\")");
        sb.AppendLine(i + "                        var __asmNameEnd = __afterPrefix.IndexOf(':', __asmNameStart);");
        sb.AppendLine(i + "                        if (__asmNameEnd > __asmNameStart)");
        sb.AppendLine(i + "                        {");
        sb.AppendLine(i + "                            __targetAsmName = __afterPrefix.Substring(__asmNameStart, __asmNameEnd - __asmNameStart).Trim();");
        sb.AppendLine(i + "                        }");
        sb.AppendLine(i + "                    }");
        sb.AppendLine(i + "                    // Search loaded assemblies, prioritizing the target assembly");
        sb.AppendLine(i + "                    var __allAssemblies = global::System.AppDomain.CurrentDomain.GetAssemblies();");
        sb.AppendLine(i + "                    global::System.Console.WriteLine($\"[WXSG-DEBUG] Searching for '{__fullyQualifiedTypeName}' in {__allAssemblies.Length} assemblies (target: {__targetAsmName})\");");
        sb.AppendLine(i + "                    if (!string.IsNullOrEmpty(__targetAsmName))");
        sb.AppendLine(i + "                    {");
        sb.AppendLine(i + "                        foreach (var __asm in __allAssemblies)");
        sb.AppendLine(i + "                        {");
        sb.AppendLine(i + "                            var __asmName = __asm.GetName().Name;");
        sb.AppendLine(i + "                            if (string.Equals(__asmName, __targetAsmName, global::System.StringComparison.Ordinal))");
        sb.AppendLine(i + "                            {");
        sb.AppendLine(i + "                                global::System.Console.WriteLine($\"[WXSG-DEBUG] Found target assembly '{__asmName}'\");");
        sb.AppendLine(i + "                                __qualifiedOwnerType = __asm.GetType(__fullyQualifiedTypeName, throwOnError: false);");
        sb.AppendLine(i + "                                var __lookupStatus = __qualifiedOwnerType != null ? \"SUCCESS\" : \"FAILED\";");
        sb.AppendLine(i + "                                global::System.Console.WriteLine($\"[WXSG-DEBUG] Type lookup: {__lookupStatus}\");");
        sb.AppendLine(i + "                                if (__qualifiedOwnerType is not null) break;");
        sb.AppendLine(i + "                            }");
        sb.AppendLine(i + "                        }");
        sb.AppendLine(i + "                    }");
        sb.AppendLine(i + "                    if (__qualifiedOwnerType is null)");
        sb.AppendLine(i + "                    {");
        sb.AppendLine(i + "                        foreach (var __asm in __allAssemblies)");
        sb.AppendLine(i + "                        {");
        sb.AppendLine(i + "                            __qualifiedOwnerType = __asm.GetType(__fullyQualifiedTypeName, throwOnError: false);");
        sb.AppendLine(i + "                            if (__qualifiedOwnerType is not null) break;");
        sb.AppendLine(i + "                        }");
        sb.AppendLine(i + "                    }");
        sb.AppendLine(i + "                }");
        sb.AppendLine(i + "                if (__qualifiedOwnerType is not null)");
        sb.AppendLine(i + "                {");
        sb.AppendLine(i + "                    var __qualifiedFlags = global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy;");
        sb.AppendLine(i + "                    var __qualifiedProp = __qualifiedOwnerType.GetProperty(__qualifiedMemberName, __qualifiedFlags);");
        sb.AppendLine(i + "                    if (__qualifiedProp is not null) return __qualifiedProp.GetValue(null);");
        sb.AppendLine(i + "                    var __qualifiedField = __qualifiedOwnerType.GetField(__qualifiedMemberName, __qualifiedFlags);");
        sb.AppendLine(i + "                    if (__qualifiedField is not null) return __qualifiedField.GetValue(null);");
        sb.AppendLine(i + "                }");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "        // If we processed clr-namespace format but didn't return, error out");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Unable to resolve x:Static member '\" + __memberToken + \"'.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    // Fallback: handle XML namespace prefix format (e.g. \"p:Converters.CollectionsToComposite\")");
        sb.AppendLine(i + "    var __colonIdx = __memberToken.IndexOf(':');");
        sb.AppendLine(i + "    var __withoutPrefix = __memberToken;");
        sb.AppendLine(i + "    if (__colonIdx >= 0 && __colonIdx < __memberToken.Length - 1)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        __withoutPrefix = __memberToken.Substring(__colonIdx + 1);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    var __memberDot = __withoutPrefix.LastIndexOf('.');");
        sb.AppendLine(i + "    if (__memberDot <= 0 || __memberDot >= __withoutPrefix.Length - 1)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Invalid x:Static member token '\" + __memberToken + \"'.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    var __typeName = __withoutPrefix.Substring(0, __memberDot);");
        sb.AppendLine(i + "    var __memberName = __withoutPrefix.Substring(__memberDot + 1);");
        sb.AppendLine(i + "    ");
        sb.AppendLine(i + "    // If we had a namespace prefix, skip standard resolution which finds wrong types");
        sb.AppendLine(i + "    // Otherwise use standard means");
        sb.AppendLine(i + "    var __ownerType = __colonIdx >= 0 ? null : __WXSG_ResolveTypeToken(__typeName);");
        sb.AppendLine(i + "    if (__ownerType is null && __colonIdx >= 0)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        // Search assemblies with calling assembly first");
        if (!string.IsNullOrEmpty(docClassFullName))
        {
            sb.AppendLine(i + "        var __callingAsm = typeof(" + CodeGenUtilities.QualifyType(docClassFullName) + ").Assembly;");
        }
        else
        {
            sb.AppendLine(i + "        var __callingAsm = global::System.Reflection.Assembly.GetEntryAssembly() ?? typeof(global::System.Windows.Application).Assembly;");
        }
        sb.AppendLine(i + "        var __allAssemblies = global::System.AppDomain.CurrentDomain.GetAssemblies();");
        sb.AppendLine(i + "        global::System.Collections.Generic.List<global::System.Reflection.Assembly> __asmSearchOrder = new();");
        sb.AppendLine(i + "        __asmSearchOrder.Add(__callingAsm);");
        sb.AppendLine(i + "        foreach (var __asm in __allAssemblies)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            if (__asm != __callingAsm) __asmSearchOrder.Add(__asm);");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "        foreach (var __asm in __asmSearchOrder)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            __ownerType = __asm.GetType(__typeName, throwOnError: false);");
        sb.AppendLine(i + "            if (__ownerType is not null) break;");
        sb.AppendLine(i + "            ");
        sb.AppendLine(i + "            global::System.Type[] __types;");
        sb.AppendLine(i + "            try { __types = __asm.GetTypes(); }");
        sb.AppendLine(i + "            catch (global::System.Reflection.ReflectionTypeLoadException __rtle) { __types = __rtle.Types; }");
        sb.AppendLine(i + "            ");
        sb.AppendLine(i + "            foreach (var __t in __types)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                if (__t is not null && __t.Name == __typeName)");
        sb.AppendLine(i + "                {");
        sb.AppendLine(i + "                    __ownerType = __t;");
        sb.AppendLine(i + "                    break;");
        sb.AppendLine(i + "                }");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "            if (__ownerType is not null) break;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    ");
        sb.AppendLine(i + "    if (__ownerType is null)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        throw new global::System.InvalidOperationException(\"Unable to resolve x:Static type '\" + __typeName + \"'.\");");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    ");
        sb.AppendLine(i + "    var __flags = global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.FlattenHierarchy;");
        sb.AppendLine(i + "    var __prop = __ownerType.GetProperty(__memberName, __flags);");
        sb.AppendLine(i + "    if (__prop is not null)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        return __prop.GetValue(null);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    var __field = __ownerType.GetField(__memberName, __flags);");
        sb.AppendLine(i + "    if (__field is not null)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        return __field.GetValue(null);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    throw new global::System.InvalidOperationException(\"Unable to resolve x:Static member '\" + __memberName + \"' on type '\" + __typeName + \"'.\");");
        sb.AppendLine(i + "}");
        sb.AppendLine();
    }

    private static void EmitSetterValueHelper(GraphEmitter emitter)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        sb.AppendLine(i + "private static object __WXSG_ConvertSetterValue(global::System.Windows.DependencyProperty __property, object __rawValue)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    if (__property is null || __rawValue is null)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        return __rawValue;");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    if (__rawValue is not string __text)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        return __rawValue;");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    var __targetType = __property.PropertyType;");
        sb.AppendLine(i + "    if (__targetType == typeof(string))");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        return __text;");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    try");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __converter = global::System.ComponentModel.TypeDescriptor.GetConverter(__targetType);");
        sb.AppendLine(i + "        if (__converter is not null && __converter.CanConvertFrom(typeof(string)))");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            var __converted = __converter.ConvertFromInvariantString(__text);");
        sb.AppendLine(i + "            if (__converted is not null)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                return __converted;");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    catch");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i);
        sb.AppendLine(i + "    try");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __nonNullable = global::System.Nullable.GetUnderlyingType(__targetType) ?? __targetType;");
        sb.AppendLine(i + "        if (__nonNullable.IsEnum)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            return global::System.Enum.Parse(__nonNullable, __text, ignoreCase: true);");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i);
        sb.AppendLine(i + "        return global::System.Convert.ChangeType(__text, __nonNullable, global::System.Globalization.CultureInfo.InvariantCulture);");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    catch");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        return __rawValue;");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "}");
        sb.AppendLine();
    }

    private static void EmitUnknownMarkupExtensionHelper(GraphEmitter emitter)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        // Unknown / custom markup extension runtime resolver.
        // Scans loaded assemblies for XmlnsDefinitionAttribute mapping the given XML namespace
        // URI to CLR namespaces, finds the extension type (<localName>Extension or <localName>),
        // creates an instance with the provided positional constructor argument (if any), applies
        // named property assignments, then calls ProvideValue(null) if the type derives from
        // MarkupExtension.  This mirrors the resolution WPF itself does for custom xmlns prefixes.
        sb.AppendLine(i + "private static object __WXSG_EvaluateUnknownMarkupExtension(");
        sb.AppendLine(i + "    string __xmlNsUri,");
        sb.AppendLine(i + "    string __localName,");
        sb.AppendLine(i + "    string[] __positionalArgs,");
        sb.AppendLine(i + "    string[] __namedArgKeys,");
        sb.AppendLine(i + "    string[] __namedArgValues)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    var __typeName = __localName.EndsWith(\"Extension\", global::System.StringComparison.OrdinalIgnoreCase)");
        sb.AppendLine(i + "        ? __localName : __localName + \"Extension\";");
        sb.AppendLine(i + "    var __assemblies = global::System.AppDomain.CurrentDomain.GetAssemblies();");
        sb.AppendLine(i + "    foreach (var __assembly in __assemblies)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __attrs = __assembly.GetCustomAttributes(");
        sb.AppendLine(i + "            typeof(global::System.Windows.Markup.XmlnsDefinitionAttribute), inherit: false);");
        sb.AppendLine(i + "        foreach (global::System.Windows.Markup.XmlnsDefinitionAttribute __def in __attrs)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            if (!string.Equals(__def.XmlNamespace, __xmlNsUri, global::System.StringComparison.Ordinal))");
        sb.AppendLine(i + "                continue;");
        sb.AppendLine(i + "            var __clrNs = __def.ClrNamespace ?? string.Empty;");
        sb.AppendLine(i + "            var __fullTypeName = string.IsNullOrEmpty(__clrNs)");
        sb.AppendLine(i + "                ? __typeName");
        sb.AppendLine(i + "                : __clrNs + \".\" + __typeName;");
        sb.AppendLine(i + "            var __type = __assembly.GetType(__fullTypeName, throwOnError: false);");
        sb.AppendLine(i + "            if (__type is null)");
        sb.AppendLine(i + "                continue;");
        sb.AppendLine(i + "            object __instance;");
        sb.AppendLine(i + "            try");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                __instance = __positionalArgs.Length == 0");
        sb.AppendLine(i + "                    ? global::System.Activator.CreateInstance(__type)");
        sb.AppendLine(i + "                    : global::System.Activator.CreateInstance(__type, (object)__positionalArgs[0]);");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "            catch { continue; }");
        sb.AppendLine(i + "            for (int __i = 0; __i < __namedArgKeys.Length; __i++)");
        sb.AppendLine(i + "            {");
        sb.AppendLine(i + "                var __propInfo = __type.GetProperty(__namedArgKeys[__i]);");
        sb.AppendLine(i + "                __propInfo?.SetValue(__instance, __namedArgValues[__i]);");
        sb.AppendLine(i + "            }");
        sb.AppendLine(i + "            if (__instance is global::System.Windows.Markup.MarkupExtension __ext)");
        sb.AppendLine(i + "                return __ext.ProvideValue(null);");
        sb.AppendLine(i + "            return __instance;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "    return null;");
        sb.AppendLine(i + "}");
        sb.AppendLine();
    }

    private static void EmitTrySetBindingHelper(GraphEmitter emitter)
    {
        var sb = emitter.Builder;
        var i = emitter.MemberIndent;

        // Helper used by the unknown-markup-extension block emitted in EmitPropertyAssignments.
        // Locates the DependencyProperty field for the given property name on the target object's
        // type hierarchy and calls BindingOperations.SetBinding.  A no-op when the property is
        // not a DependencyProperty (e.g. plain CLR property on a non-DO type).
        sb.AppendLine(i + "private static void __WXSG_TrySetBinding(");
        sb.AppendLine(i + "    object __target,");
        sb.AppendLine(i + "    string __propertyName,");
        sb.AppendLine(i + "    global::System.Windows.Data.BindingBase __binding)");
        sb.AppendLine(i + "{");
        sb.AppendLine(i + "    if (!(__target is global::System.Windows.DependencyObject __depObj))");
        sb.AppendLine(i + "        return;");
        sb.AppendLine(i + "    var __dpFieldName = __propertyName + \"Property\";");
        sb.AppendLine(i + "    var __flags = global::System.Reflection.BindingFlags.Public |");
        sb.AppendLine(i + "                  global::System.Reflection.BindingFlags.Static |");
        sb.AppendLine(i + "                  global::System.Reflection.BindingFlags.FlattenHierarchy;");
        sb.AppendLine(i + "    for (var __t = __depObj.GetType(); __t is not null; __t = __t.BaseType)");
        sb.AppendLine(i + "    {");
        sb.AppendLine(i + "        var __field = __t.GetField(__dpFieldName, __flags);");
        sb.AppendLine(i + "        if (__field?.GetValue(null) is global::System.Windows.DependencyProperty __dp)");
        sb.AppendLine(i + "        {");
        sb.AppendLine(i + "            global::System.Windows.Data.BindingOperations.SetBinding(__depObj, __dp, __binding);");
        sb.AppendLine(i + "            return;");
        sb.AppendLine(i + "        }");
        sb.AppendLine(i + "    }");
        sb.AppendLine(i + "}");
    }
}
