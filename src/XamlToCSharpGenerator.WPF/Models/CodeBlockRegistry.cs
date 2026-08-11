using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.WPF.Models;

/// <summary>
/// A raw <c>x:Code</c> block extracted from a WPF document by
/// <see cref="XamlToCSharpGenerator.WPF.Parsing.WpfDocumentFeatureEnricher"/>.
/// </summary>
/// <remarks>
/// The AXSG (XamlToCSharpGenerator) engine that wxsg is rebased onto no longer models
/// x:Code blocks on <c>XamlDocumentModel</c>, so the WPF wrapper carries them itself and
/// hands them to the emitter through <see cref="CodeBlockRegistry"/> - keyed by the exact
/// <c>XamlDocumentModel</c> instance that flows through the pipeline (parser -> binders ->
/// emitters), so no engine change is needed.
/// </remarks>
public sealed record WxsgCodeBlock(
    string RawCode,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition)
{
}

/// <summary>
/// Side channel for x:Code blocks between the document enricher (which extracts them)
/// and the code emitters (which splice them into the generated source).
/// </summary>
public static class CodeBlockRegistry
{
    private static readonly ConditionalWeakTable<XamlDocumentModel, WxsgCodeBlock[]> Blocks =
        new();

    public static void Set(XamlDocumentModel document, ImmutableArray<WxsgCodeBlock> codeBlocks)
    {
        Blocks.Remove(document);
        Blocks.Add(document, codeBlocks.ToArray());
    }

    public static ImmutableArray<WxsgCodeBlock> Get(XamlDocumentModel document)
    {
        return Blocks.TryGetValue(document, out var blocks) && blocks.Length > 0
            ? blocks.ToImmutableArray()
            : ImmutableArray<WxsgCodeBlock>.Empty;
    }
}

/// <summary>
/// Side channel for the "user authored an OnStartup override on the App partial class" flag
/// that the semantic binder computes - AXSG's <c>ResolvedViewModel</c> no longer carries it,
/// so it travels here between binder and WPF code emitter.
/// </summary>
public static class StartupOverrideRegistry
{
    private sealed class Flag
    {
        public Flag(bool value) => Value = value;

        public bool Value { get; }
    }

    private static readonly ConditionalWeakTable<XamlDocumentModel, Flag> Flags =
        new();

    public static void Set(XamlDocumentModel document, bool hasUserOnStartupOverride)
    {
        Flags.Remove(document);
        Flags.Add(document, new Flag(hasUserOnStartupOverride));
    }

    public static bool Get(XamlDocumentModel document)
    {
        return Flags.TryGetValue(document, out var flag) && flag.Value;
    }
}

/// <summary>
/// Side channel for the "this object node is an x:Array" semantic flag - AXSG's
/// <c>ResolvedObjectNodeSemanticFlags</c> no longer carries an IsXamlArray bit (it was
/// always set alone, never combined), so the WPF binder and emitter coordinate through
/// this registry keyed by the exact <c>ResolvedObjectNode</c> instance.
/// </summary>
public static class XamlArrayRegistry
{
    private sealed class Flag
    {
        public Flag(bool value) => Value = value;

        public bool Value { get; }
    }

    private static readonly ConditionalWeakTable<ResolvedObjectNode, Flag> Flags =
        new();

    public static void Set(ResolvedObjectNode node)
    {
        Flags.Remove(node);
        Flags.Add(node, new Flag(true));
    }

    public static bool IsXamlArray(ResolvedObjectNode node)
    {
        return Flags.TryGetValue(node, out var flag) && flag.Value;
    }
}