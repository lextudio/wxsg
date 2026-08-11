using System;
using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.WPF.Models;

namespace XamlToCSharpGenerator.WPF.Parsing;

public sealed class WpfDocumentFeatureEnricher : IXamlDocumentEnricher
{
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static WpfDocumentFeatureEnricher Instance { get; } = new();

    private WpfDocumentFeatureEnricher()
    {
    }

    public (XamlDocumentModel Document, ImmutableArray<DiagnosticInfo> Diagnostics) Enrich(
        XamlDocumentModel document,
        XamlDocumentParseContext parseContext)
    {
        var codeBlocks = ImmutableArray.CreateBuilder<WxsgCodeBlock>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        foreach (var element in parseContext.RootElement.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, parseContext.IgnoredNamespaces))
            {
                continue;
            }

            AddElementFeatures(element, parseContext.ConditionalNamespacesByRawUri, codeBlocks, diagnostics, document);
        }

        // The upstream engine's XamlDocumentModel carries no x:Code state, so the blocks
        // travel through CodeBlockRegistry keyed by this exact model instance - which is
        // the same instance the binders and emitters receive downstream (no clones of the
        // model are made between enrichment and emission).
        CodeBlockRegistry.Set(document, codeBlocks.ToImmutable());

        return (document, diagnostics.ToImmutable());
    }

    private static void AddElementFeatures(
        XElement element,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri,
        ImmutableArray<WxsgCodeBlock>.Builder codeBlocks,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document)
    {
        if (!IsCodeElement(element))
        {
            return;
        }

        var lineInfo = (IXmlLineInfo)element;
        var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
        var column = lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1;

        if (!document.IsClassBacked)
        {
            diagnostics.Add(new DiagnosticInfo(
                "WXSG0001",
                "x:Code requires x:Class-backed root element.",
                document.FilePath,
                line,
                column,
                false));
            return;
        }

        var rawCode = element.Value;
        var condition = XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
            element.Name.NamespaceName,
            conditionalNamespacesByRawUri);

        codeBlocks.Add(new WxsgCodeBlock(
            RawCode: rawCode,
            Line: line,
            Column: column,
            Condition: condition));
    }

    private static bool IsCodeElement(XElement element)
    {
        return element.Name.LocalName == "Code" &&
               element.Name.NamespaceName == Xaml2006.NamespaceName;
    }

    private static bool ShouldIgnoreElement(XElement element, ImmutableHashSet<string> ignoredNamespaces)
    {
        return ignoredNamespaces.Contains(element.Name.NamespaceName);
    }
}
